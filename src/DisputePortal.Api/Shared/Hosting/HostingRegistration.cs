using DisputePortal.Api.Shared.Health;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Hosting;

public static class HostingRegistration
{
    private const long MaxRequestBodyBytes = 64 * 1024;
    private const string KnownNetworkSetting = "Forwarded:KnownNetwork";
    private const string AnyHost = "*";

    /// <summary>Kestrel listens on the public and the management port; request bodies are capped.</summary>
    public static WebApplicationBuilder ConfigurePorts(this WebApplicationBuilder builder)
    {
        ManagementSettings ports = builder.Configuration.GetSection(ManagementSettings.Section).Get<ManagementSettings>() ?? new ManagementSettings();
        builder.Services.AddOptions<ManagementSettings>().BindConfiguration(ManagementSettings.Section).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ManagementSettings>, ManagementSettingsValidator>();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(ports.Public);
            kestrel.ListenAnyIP(ports.Management);
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });
        if (!string.IsNullOrWhiteSpace(ports.InternalHost))
        {
            builder.Services.AllowInternalHost(ports.InternalHost);
        }

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

    /// <summary>
    /// X-Forwarded-* only on the public port, where nginx is the one hop. Nothing proxies the management port, so there
    /// the socket's peer is the caller and stays so: a container on the trusted app network cannot claim a data-network
    /// address (or another rate-limit partition) by sending the header.
    /// </summary>
    public static WebApplication UseForwardedHeadersOnThePublicPort(this WebApplication app)
    {
        int management = app.Services.GetRequiredService<IOptions<ManagementSettings>>().Value.Management;
        app.UseWhen(context => context.Connection.LocalPort != management, branch => branch.UseForwardedHeaders());
        return app;
    }

    // After the framework's own fallback, which fills AllowedHosts from configuration (a fixed-size array, so replaced).
    // ManagementPortMiddleware refuses this name on the public port, so the public listener's hosts are unchanged.
    private static void AllowInternalHost(this IServiceCollection services, string internalHost) =>
        services.PostConfigure<HostFilteringOptions>(options =>
        {
            if (!options.AllowedHosts.Contains(AnyHost))
            {
                options.AllowedHosts = [.. options.AllowedHosts, internalHost];
            }
        });
}
