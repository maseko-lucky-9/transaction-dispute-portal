using System.Diagnostics;
using System.Security.Cryptography;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// Server-side session tickets: the browser's cookie carries only an opaque key. Each ticket is DataProtection-encrypted
/// before it reaches Redis, and expires with the session's absolute lifetime. Any Redis failure fails closed (503).
/// </summary>
public sealed class RedisTicketStore(IConnectionMultiplexer redis, IDataProtectionProvider dataProtection, TimeProvider time) : ITicketStore
{
    public const string KeyPrefix = "bff:ticket:";

    private const int CompareAndDeleteAttempts = 3;
    private static readonly TimeSpan MinimumTtl = TimeSpan.FromSeconds(1);

    private readonly IDataProtector protector = dataProtection.CreateProtector("DisputePortal.Bff.SessionTicket.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        string key = Guid.NewGuid().ToString("N");
        await WriteAsync(key, ticket, When.Always);
        return key;
    }

    /// <summary>Only rewrites a ticket that still exists, so a renewal racing a logout cannot bring the session back.</summary>
    public Task RenewAsync(string key, AuthenticationTicket ticket) => WriteAsync(key, ticket, When.Exists);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        using Activity? activity = StartActivity("session.retrieve");
        return Unprotect(await RunAsync(() => redis.GetDatabase().StringGetAsync(KeyPrefix + key)));
    }

    public async Task RemoveAsync(string key)
    {
        using Activity? activity = StartActivity("session.remove");
        await RunAsync(() => redis.GetDatabase().KeyDeleteAsync(KeyPrefix + key));
    }

    /// <summary>
    /// Compare-and-delete: removes the ticket only while <paramref name="shouldRemove"/> holds for exactly the stored
    /// bytes (WATCH/MULTI through a StackExchange.Redis transaction condition), so a ticket another replica rewrote in
    /// between is never deleted.
    /// </summary>
    /// <returns>The ticket left in place, or null when it was removed or was already gone.</returns>
    public async Task<AuthenticationTicket?> RemoveIfAsync(string key, Func<AuthenticationTicket, bool> shouldRemove)
    {
        using Activity? activity = StartActivity("session.remove_if");
        IDatabase database = redis.GetDatabase();
        for (int attempt = 0; attempt < CompareAndDeleteAttempts; attempt++)
        {
            RedisValue stored = await RunAsync(() => database.StringGetAsync(KeyPrefix + key));
            if (stored.IsNullOrEmpty)
            {
                return null;
            }

            if (Unprotect(stored) is { } ticket && !shouldRemove(ticket))
            {
                return ticket;
            }

            ITransaction transaction = database.CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(KeyPrefix + key, stored));
            _ = transaction.KeyDeleteAsync(KeyPrefix + key);
            if (await RunAsync(() => transaction.ExecuteAsync()))
            {
                return null;
            }
        }

        // Rewritten on every attempt: leave it, and report what is there now.
        return await RetrieveAsync(key);
    }

    /// <summary>Sign-in refuses to start (503) when the store is down, rather than failing after the identity provider round trip.</summary>
    public async Task EnsureAvailableAsync()
    {
        using Activity? activity = StartActivity("session.ping");
        await RunAsync(() => redis.GetDatabase().PingAsync());
    }

    private async Task WriteAsync(string key, AuthenticationTicket ticket, When when)
    {
        using Activity? activity = StartActivity("session.store");
        TimeSpan remaining = SessionTokens.AbsoluteExpiresAt(ticket.Properties) - time.GetUtcNow();
        TimeSpan ttl = remaining > MinimumTtl ? remaining : MinimumTtl;
        byte[] payload = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        await RunAsync(() => redis.GetDatabase().StringSetAsync(KeyPrefix + key, payload, ttl, when));
    }

    private AuthenticationTicket? Unprotect(RedisValue value)
    {
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(protector.Unprotect((byte[])value!));
        }
        catch (CryptographicException)
        {
            // A ticket from a revoked key ring reads as no session, never as an error page.
            return null;
        }
    }

    private static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            throw new SessionStoreUnavailableException(exception);
        }
    }

    // No statement or key on the span: a key would let a trace reader replay a session.
    private static Activity? StartActivity(string name)
    {
        Activity? activity = Telemetry.Source.StartActivity(name, ActivityKind.Client);
        activity?.SetTag("db.system.name", "redis");
        return activity;
    }
}
