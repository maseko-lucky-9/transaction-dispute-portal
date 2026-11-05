using System.Security.Claims;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DisputePortal.IntegrationTests;

/// <summary>The ticket store against the real Redis, as the compose ACL user.</summary>
[Collection("api")]
public sealed class SessionStoreTests(ApiFactory api)
{
    private RedisTicketStore Store => api.Services.GetRequiredService<RedisTicketStore>();

    private IDatabase Redis => api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    // L1: a sliding renewal that lands after a logout must not bring the session back.
    [Fact]
    public async Task ARenewalAfterARemovalLeavesTheTicketRemoved()
    {
        string key = await Store.StoreAsync(Ticket("rt-1"));
        await Store.RemoveAsync(key);

        await Store.RenewAsync(key, Ticket("rt-1"));

        Assert.Null(await Store.RetrieveAsync(key));
        Assert.False(await Redis.KeyExistsAsync(RedisTicketStore.KeyPrefix + key));
    }

    [Fact]
    public async Task ARenewalOfALiveTicketReplacesIt()
    {
        string key = await Store.StoreAsync(Ticket("rt-1"));

        await Store.RenewAsync(key, Ticket("rt-2"));

        Assert.Equal("rt-2", (await Store.RetrieveAsync(key))?.Properties.GetTokenValue(SessionTokens.RefreshToken));
    }

    // M1: a replica whose refresh was refused must not delete a ticket another replica has already rotated.
    [Fact]
    public async Task CompareAndDeleteKeepsATicketThatNoLongerHoldsTheRefusedToken()
    {
        string key = await Store.StoreAsync(Ticket("rt-2"));

        AuthenticationTicket? kept = await Store.RemoveIfAsync(key, HoldsToken("rt-1"));

        Assert.Equal("rt-2", kept?.Properties.GetTokenValue(SessionTokens.RefreshToken));
        Assert.True(await Redis.KeyExistsAsync(RedisTicketStore.KeyPrefix + key));
    }

    [Fact]
    public async Task CompareAndDeleteRemovesATicketThatStillHoldsTheRefusedToken()
    {
        string key = await Store.StoreAsync(Ticket("rt-1"));

        AuthenticationTicket? kept = await Store.RemoveIfAsync(key, HoldsToken("rt-1"));

        Assert.Null(kept);
        Assert.False(await Redis.KeyExistsAsync(RedisTicketStore.KeyPrefix + key));
    }

    // The session index (ADR-011): every ticket of a Keycloak session, findable by its sid.
    [Fact]
    public async Task RemovingBySessionIdDeletesEveryTicketOfThatSessionAndItsIndexButNoOtherSession()
    {
        string sid = NewSid();
        string first = await Store.StoreAsync(Ticket("rt-1", sid));
        string second = await Store.StoreAsync(Ticket("rt-2", sid));
        string other = await Store.StoreAsync(Ticket("rt-3", NewSid()));

        int removed = await Store.RemoveBySessionIdAsync(sid);

        Assert.Equal(2, removed);
        Assert.Null(await Store.RetrieveAsync(first));
        Assert.Null(await Store.RetrieveAsync(second));
        Assert.NotNull(await Store.RetrieveAsync(other));
        Assert.False(await Redis.KeyExistsAsync(RedisTicketStore.SessionIndexKey(sid)));
    }

    [Fact]
    public async Task RemovingAnUnknownSessionIdRemovesNothing() =>
        Assert.Equal(0, await Store.RemoveBySessionIdAsync(NewSid()));

    // A ticket that expired on its own leaves a stale member behind; it is skipped, not counted.
    [Fact]
    public async Task RemovingBySessionIdCountsOnlyTicketsThatStillExisted()
    {
        string sid = NewSid();
        string live = await Store.StoreAsync(Ticket("rt-1", sid));
        await Redis.SetAddAsync(RedisTicketStore.SessionIndexKey(sid), "expired-ticket-id");

        Assert.Equal(1, await Store.RemoveBySessionIdAsync(sid));
        Assert.Null(await Store.RetrieveAsync(live));
    }

    // L1 again, for the index: a renewal racing a back-channel logout must not bring the ticket or its entry back.
    [Fact]
    public async Task ARenewalAfterABackchannelRemovalRestoresNeitherTheTicketNorItsIndexEntry()
    {
        string sid = NewSid();
        string key = await Store.StoreAsync(Ticket("rt-1", sid));
        await Store.RemoveBySessionIdAsync(sid);

        await Store.RenewAsync(key, Ticket("rt-2", sid));

        Assert.False(await Redis.KeyExistsAsync(RedisTicketStore.KeyPrefix + key));
        Assert.False(await Redis.KeyExistsAsync(RedisTicketStore.SessionIndexKey(sid)));
    }

    [Fact]
    public async Task RemoveAndCompareAndDeleteBothTakeTheTicketOutOfTheIndex()
    {
        string sid = NewSid();
        string removed = await Store.StoreAsync(Ticket("rt-1", sid));
        string refused = await Store.StoreAsync(Ticket("rt-2", sid));
        string kept = await Store.StoreAsync(Ticket("rt-3", sid));

        await Store.RemoveAsync(removed);
        await Store.RemoveIfAsync(refused, HoldsToken("rt-2"));

        Assert.Equal([kept], (await Redis.SetMembersAsync(RedisTicketStore.SessionIndexKey(sid))).Select(member => member.ToString()));
    }

    private static Func<AuthenticationTicket, bool> HoldsToken(string refreshToken) =>
        ticket => ticket.Properties.GetTokenValue(SessionTokens.RefreshToken) == refreshToken;

    private static string NewSid() => Guid.NewGuid().ToString();

    private static AuthenticationTicket Ticket(string refreshToken, string? sessionId = null)
    {
        AuthenticationProperties properties = new();
        SessionTokens.StartAbsoluteLifetime(properties, DateTimeOffset.UtcNow);
        SessionTokens.Store(properties, "id", refreshToken, DateTimeOffset.UtcNow.AddMinutes(5));
        List<Claim> claims = [new Claim("sub", "store-test")];
        if (sessionId is not null)
        {
            claims.Add(new Claim(ClaimNames.SessionId, sessionId));
        }

        return new AuthenticationTicket(SessionPrincipal.Create(claims), properties, "Cookies");
    }
}
