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
/// Every write also files the ticket id under its Keycloak session (<see cref="SessionIndexPrefix"/>), so a back-channel
/// logout can find and delete all of that session's tickets (ADR-011).
/// </summary>
public sealed class RedisTicketStore(IConnectionMultiplexer redis, IDataProtectionProvider dataProtection, TimeProvider time) : ITicketStore
{
    public const string KeyPrefix = "bff:ticket:";

    /// <summary>
    /// <c>bff:sid:&lt;SHA-256 of the Keycloak sid&gt;</c>, a set of ticket ids. Ids only, never token material, and the sid is
    /// hashed. Each write resets its TTL to the full absolute lifetime, so it outlives every ticket in it; a member whose
    /// ticket expired on its own lingers harmlessly until then.
    /// </summary>
    public const string SessionIndexPrefix = "bff:sid:";

    private const int CompareAndDeleteAttempts = 3;
    private static readonly TimeSpan MinimumTtl = TimeSpan.FromSeconds(1);

    // One script, so no ticket can join the set between reading it and deleting it. Ticket keys are built from ARGV[1],
    // which a cluster would refuse; this Redis is standalone (and the ACL's ~bff:* still covers every key it touches).
    private const string RemoveSessionScript = """
        local ids = redis.call('SMEMBERS', KEYS[1])
        local removed = 0
        for i = 1, #ids do
          removed = removed + redis.call('DEL', ARGV[1] .. ids[i])
        end
        redis.call('DEL', KEYS[1])
        return removed
        """;

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

    /// <summary>Deletes the ticket and its index entry. The ticket is read first: its sid is inside the encryption.</summary>
    public async Task RemoveAsync(string key)
    {
        using Activity? activity = StartActivity("session.remove");
        IDatabase database = redis.GetDatabase();
        AuthenticationTicket? ticket = Unprotect(await RunAsync(() => database.StringGetAsync(KeyPrefix + key)));
        await RunAsync(() => DeleteAsync(database, key, ticket, condition: null));
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

            AuthenticationTicket? ticket = Unprotect(stored);
            if (ticket is not null && !shouldRemove(ticket))
            {
                return ticket;
            }

            if (await RunAsync(() => DeleteAsync(database, key, ticket, Condition.StringEqual(KeyPrefix + key, stored))))
            {
                return null;
            }
        }

        // Rewritten on every attempt: leave it, and report what is there now.
        return await RetrieveAsync(key);
    }

    /// <summary>Back-channel logout: deletes every ticket signed in under this Keycloak session, and the session's index.</summary>
    /// <returns>How many live tickets were deleted: 0 for an unknown or already-ended session.</returns>
    public async Task<int> RemoveBySessionIdAsync(string sessionId)
    {
        using Activity? activity = StartActivity("session.remove_by_sid");
        RedisResult removed = await RunAsync(() => redis.GetDatabase().ScriptEvaluateAsync(
            RemoveSessionScript, [SessionIndexKey(sessionId)], [KeyPrefix]));
        return (int)removed;
    }

    /// <summary>Sign-in refuses to start (503) when the store is down, rather than failing after the identity provider round trip.</summary>
    public async Task EnsureAvailableAsync()
    {
        using Activity? activity = StartActivity("session.ping");
        await RunAsync(() => redis.GetDatabase().PingAsync());
    }

    public static RedisKey SessionIndexKey(string sessionId) => SessionIndexPrefix + SessionTokens.Fingerprint(sessionId);

    private async Task WriteAsync(string key, AuthenticationTicket ticket, When when)
    {
        using Activity? activity = StartActivity("session.store");
        TimeSpan remaining = SessionTokens.AbsoluteExpiresAt(ticket.Properties) - time.GetUtcNow();
        TimeSpan ttl = remaining > MinimumTtl ? remaining : MinimumTtl;
        byte[] payload = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        // One transaction: the ticket and its index entry are written together, or (a renewal of a ticket that is gone)
        // neither is, so a logged-out session never re-enters the index.
        ITransaction transaction = redis.GetDatabase().CreateTransaction();
        if (when == When.Exists)
        {
            transaction.AddCondition(Condition.KeyExists(KeyPrefix + key));
        }

        _ = transaction.StringSetAsync(KeyPrefix + key, payload, ttl, When.Always);
        if (SessionIdOf(ticket) is { } sessionId)
        {
            RedisKey index = SessionIndexKey(sessionId);
            _ = transaction.SetAddAsync(index, key);
            _ = transaction.KeyExpireAsync(index, SessionTokens.AbsoluteLifetime);
        }

        await RunAsync(() => transaction.ExecuteAsync());
    }

    private static Task<bool> DeleteAsync(IDatabase database, string key, AuthenticationTicket? ticket, Condition? condition)
    {
        ITransaction transaction = database.CreateTransaction();
        if (condition is not null)
        {
            transaction.AddCondition(condition);
        }

        _ = transaction.KeyDeleteAsync(KeyPrefix + key);
        // An unreadable ticket (old key ring) has no readable sid: its entry stays until the index expires.
        if (ticket is not null && SessionIdOf(ticket) is { } sessionId)
        {
            _ = transaction.SetRemoveAsync(SessionIndexKey(sessionId), key);
        }

        return transaction.ExecuteAsync();
    }

    private static string? SessionIdOf(AuthenticationTicket ticket) =>
        ticket.Principal.FindFirst(ClaimNames.SessionId)?.Value is { Length: > 0 } sessionId ? sessionId : null;

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
