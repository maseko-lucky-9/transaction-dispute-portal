using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DisputePortal.Api.Shared.Security;

public static class SessionStoreRegistration
{
    public static IServiceCollection AddSessionStore(this IServiceCollection services)
    {
        services.AddOptions<RedisSettings>().BindConfiguration(RedisSettings.Section).ValidateDataAnnotations().ValidateOnStart();
        // Resolved on first use, so test settings apply; one multiplexer per process, as StackExchange.Redis requires.
        services.AddSingleton<IConnectionMultiplexer>(provider =>
            ConnectionMultiplexer.Connect(provider.GetRequiredService<IOptions<RedisSettings>>().Value.ToConfigurationOptions()));
        services.AddSingleton<RedisTicketStore>();
        services.AddSingleton<ITicketStore>(provider => provider.GetRequiredService<RedisTicketStore>());
        return services;
    }
}
