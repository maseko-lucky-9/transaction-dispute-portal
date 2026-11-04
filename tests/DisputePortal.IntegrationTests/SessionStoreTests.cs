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

    private static Func<AuthenticationTicket, bool> HoldsToken(string refreshToken) =>
        ticket => ticket.Properties.GetTokenValue(SessionTokens.RefreshToken) == refreshToken;

    private static AuthenticationTicket Ticket(string refreshToken)
    {
        AuthenticationProperties properties = new();
        SessionTokens.StartAbsoluteLifetime(properties, DateTimeOffset.UtcNow);
        SessionTokens.Store(properties, "id", refreshToken, DateTimeOffset.UtcNow.AddMinutes(5));
        return new AuthenticationTicket(SessionPrincipal.Create([new Claim("sub", "store-test")]), properties, "Cookies");
    }
}
