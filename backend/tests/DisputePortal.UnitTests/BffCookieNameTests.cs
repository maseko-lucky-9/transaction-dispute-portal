using DisputePortal.Api.Features.Bff;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DisputePortal.UnitTests;

/// <summary>The __Host- prefix binds the cookie to the exact origin, but browsers drop such a cookie over plain http.</summary>
public sealed class BffCookieNameTests
{
    [Theory]
    [InlineData("https://portal.example", "__Host-dp_session")]
    [InlineData("HTTPS://portal.example", "__Host-dp_session")]
    [InlineData("http://localhost:8080", "dp_session")]
    public void TheCookieNameFollowsThePublicScheme(string baseUrl, string expected)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Public:BaseUrl"] = baseUrl,
            ["Keycloak:Authority"] = "https://idp.example/realms/dispute-portal",
            ["Keycloak:ClientSecret"] = "unused",
        }).Build();
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<ITicketStore>());
        services.AddBffAuthentication();
        using ServiceProvider provider = services.BuildServiceProvider();

        CookieAuthenticationOptions options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(expected, options.Cookie.Name);
    }
}
