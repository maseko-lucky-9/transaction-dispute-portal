using System.Security.Claims;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DisputePortal.UnitTests;

public sealed class SessionValidatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private readonly FixedClock clock = new(Now);
    private readonly ITokenRefresher refresher = Substitute.For<ITokenRefresher>();
    private readonly IAuthenticationService authentication = Substitute.For<IAuthenticationService>();
    private readonly ServiceProvider services;
    private readonly SessionValidator validator;
    private readonly SecurityEventListener securityEvents;

    public SessionValidatorTests()
    {
        services = new ServiceCollection().AddMetrics().AddSingleton(authentication).BuildServiceProvider();
        System.Diagnostics.Metrics.IMeterFactory meterFactory = services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        securityEvents = new SecurityEventListener(meterFactory);
        SecurityMetrics metrics = new(meterFactory);
        validator = new SessionValidator(refresher, new RefreshSingleFlight(clock), clock, metrics, NullLogger<SessionValidator>.Instance);
    }

    [Fact]
    public async Task AnAccessTokenFarFromExpiryIsLeftAlone()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddMinutes(4));

        await validator.ValidateAsync(context);

        Assert.NotNull(context.Principal);
        Assert.False(context.ShouldRenew);
        await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task AnAccessTokenWithinTheLeewayIsRefreshedAndTheRolesReplaced()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddSeconds(60));
        refresher.RefreshAsync("rt-1", Arg.Any<CancellationToken>()).Returns(new RefreshedSession(
            new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("name", "Thandi"), new Claim("roles", Roles.DisputeAgent)]),
            "id-2", "rt-2", Now.AddMinutes(5)));

        await validator.ValidateAsync(context);

        Assert.True(context.ShouldRenew);
        Assert.Equal([Roles.DisputeAgent], SessionPrincipal.RolesOf(context.Principal!));
        // The refreshed ID token had no sid: the session keeps its own, so the logout link still matches.
        Assert.Equal("s-1", context.Principal!.FindFirstValue("sid"));
        Assert.Equal("rt-2", context.Properties.GetTokenValue(SessionTokens.RefreshToken));
        Assert.Equal("id-2", context.Properties.GetTokenValue(SessionTokens.IdToken));
        Assert.Equal(Now.AddMinutes(5), SessionTokens.AccessTokenExpiresAt(context.Properties));
    }

    [Fact]
    public async Task AFailedRefreshEndsTheSessionAndIsCounted()
    {
        CookieValidatePrincipalContext context = Context(accessTokenExpiresAt: Now.AddSeconds(10));
        refresher.RefreshAsync("rt-1", Arg.Any<CancellationToken>()).Returns((RefreshedSession?)null);

        await validator.ValidateAsync(context);

        Assert.Null(context.Principal);
        await authentication.Received(1).SignOutAsync(context.HttpContext, CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
        Assert.Equal([SecurityMetrics.SessionRefreshFailed], securityEvents.Types);
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
        return new CookieValidatePrincipalContext(httpContext, scheme, new CookieAuthenticationOptions(), new AuthenticationTicket(principal, properties, scheme.Name));
    }

    public void Dispose()
    {
        securityEvents.Dispose();
        services.Dispose();
    }
}
