using DisputePortal.Api.Shared.Health;
using Microsoft.AspNetCore.HttpOverrides;

namespace DisputePortal.Api.Shared.Hosting;

public static class HostingRegistration
{
    private const long MaxRequestBodyBytes = 64 * 1024;
    private const string KnownNetworkSetting = "Forwarded:KnownNetwork";

    /// <summary>Kestrel listens on the public and the management port; request bodies are capped.</summary>
    public static WebApplicationBuilder ConfigurePorts(this WebApplicationBuilder builder)
    {
        ManagementSettings ports = builder.Configuration.GetSection(ManagementSettings.Section).Get<ManagementSettings>() ?? new ManagementSettings();
        builder.Services.AddOptions<ManagementSettings>().BindConfiguration(ManagementSettings.Section);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(ports.Public);
            kestrel.ListenAnyIP(ports.Management);
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });
        return builder;
    }

    /// <summary>
    /// Trusts X-Forwarded-For/Proto from one hop inside the configured network (the compose subnet) only, so a client
    /// cannot spoof its address past nginx. Without the setting only loopback is trusted (the framework default).
    /// </summary>
    public static IServiceCollection AddTrustedProxy(this IServiceCollection services, IConfiguration configuration)
    {
        string? knownNetwork = configuration[KnownNetworkSetting];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            if (!string.IsNullOrWhiteSpace(knownNetwork))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(knownNetwork));
            }
        });
        return services;
    }
}
