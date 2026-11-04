using System.Security.Claims;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace DisputePortal.UnitTests;

/// <summary>The decisions taken before any refresh. Refresh itself (Redis lock, identity provider) is covered by the integration suite.</summary>
public sealed class SessionValidatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly FixedClock clock = new(Now);
    private readonly ITokenRefresher refresher = Substitute.For<ITokenRefresher>();
    private readonly IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IAuthenticationService authentication = Substitute.For<IAuthenticationService>();
    private readonly ServiceProvider services;
    private readonly SessionValidator validator;

    public SessionValidatorTests()
    {
        services = new ServiceCollection().AddMetrics().AddSingleton(authentication).BuildServiceProvider();
        SessionRefreshCoordinator coordinator = new(
            refresher, new SessionRefreshLock(redis, clock), new RedisTicketStore(redis, new EphemeralDataProtectionProvider(), clock));
        validator = new SessionValidator(
            coordinator, new RefreshSingleFlight(), clock,
            new SecurityMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()), NullLogger<SessionValidator>.Instance);
    }

    [Fact]
    public async Task AnAccessTokenFarFromExpiryIsLeftAlone()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddMinutes(4));

        await validator.ValidateAsync(context);

        Assert.NotNull(context.Principal);
        Assert.False(context.ShouldRenew);
        await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
        Assert.Empty(redis.ReceivedCalls());
    }

    [Fact]
    public async Task ASessionPastItsAbsoluteLifetimeEndsWithoutARefresh()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddMinutes(4));
        clock.Now = Now + SessionTokens.AbsoluteLifetime;

        await validator.ValidateAsync(context);

        Assert.Null(context.Principal);
        await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
        await authentication.Received(1).SignOutAsync(context.HttpContext, CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task ATicketWithoutAnAbsoluteLifetimeStampIsTreatedAsExpired()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddMinutes(4), stampLifetime: false);

        await validator.ValidateAsync(context);

        Assert.Null(context.Principal);
    }

    // No session cookie to find the ticket by: nothing can be refreshed safely, so the session ends.
    [Fact]
    public async Task ANearlyExpiredSessionWithoutASessionCookieEnds()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddSeconds(30));

        await validator.ValidateAsync(context);

        Assert.Null(context.Principal);
        await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    private CookieValidatePrincipalContext Context(DateTimeOffset accessTokenExpiresAt, bool stampLifetime = true)
    {
        AuthenticationProperties properties = new();
        if (stampLifetime)
        {
            SessionTokens.StartAbsoluteLifetime(properties, Now);
        }

        SessionTokens.Store(properties, "id-1", "rt-1", accessTokenExpiresAt);
        ClaimsPrincipal principal = SessionPrincipal.Create([new Claim("sub", "user-1"), new Claim("sid", "s-1"), new Claim("roles", Roles.Customer)]);
        DefaultHttpContext httpContext = new() { RequestServices = services };
        AuthenticationScheme scheme = new(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        CookieAuthenticationOptions options = new() { Cookie = { Name = "dp_session" }, CookieManager = new ChunkingCookieManager() };
        return new CookieValidatePrincipalContext(httpContext, scheme, options, new AuthenticationTicket(principal, properties, scheme.Name));
    }

    public void Dispose() => services.Dispose();
}
