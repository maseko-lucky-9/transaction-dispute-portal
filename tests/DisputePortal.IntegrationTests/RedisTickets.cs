using DisputePortal.Api.Shared.Security;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DisputePortal.IntegrationTests;

/// <summary>The session tickets in the fixture's Redis, read as the app's own ACL user.</summary>
public static class RedisTickets
{
    public static HashSet<string> Ids(ApiFactory api)
    {
        IConnectionMultiplexer redis = api.Services.GetRequiredService<IConnectionMultiplexer>();
        return [.. redis.GetServer(redis.GetEndPoints().Single())
            .Keys(pattern: RedisTicketStore.KeyPrefix + "*")
            .Select(key => key.ToString()[RedisTicketStore.KeyPrefix.Length..])];
    }

    /// <summary>Signs in and returns the session together with the id of the ticket that sign-in created.</summary>
    public static async Task<(BrowserSession Session, string TicketId)> SignInAsync(ApiFactory api, IdpUser user)
    {
        HashSet<string> before = Ids(api);
        BrowserSession session = await BrowserSession.SignInAsync(api, user);
        return (session, Assert.Single(Ids(api).Except(before)));
    }
}
