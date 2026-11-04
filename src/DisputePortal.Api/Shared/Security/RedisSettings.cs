using System.ComponentModel.DataAnnotations;
using StackExchange.Redis;

namespace DisputePortal.Api.Shared.Security;

/// <summary>The session store's connection, as Vault delivers it (kv/dispute-portal/redis). The ACL user may touch only bff:*.</summary>
public sealed class RedisSettings
{
    public const string Section = "Redis";

    private const int TimeoutMilliseconds = 3000;

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 6379;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public ConfigurationOptions ToConfigurationOptions()
    {
        ConfigurationOptions options = new()
        {
            // Keep reconnecting in the background instead of throwing at startup; readiness reports the outage.
            AbortOnConnectFail = false,
            ConnectTimeout = TimeoutMilliseconds,
            AsyncTimeout = TimeoutMilliseconds,
            SyncTimeout = TimeoutMilliseconds,
            ClientName = "dispute-portal",
            // The ACL user may touch only bff:* keys: no tie-breaker key, no configuration pub/sub channel.
            TieBreaker = string.Empty,
            ConfigurationChannel = string.Empty,
            User = string.IsNullOrEmpty(Username) ? null : Username,
            Password = string.IsNullOrEmpty(Password) ? null : Password,
        };
        options.EndPoints.Add(Host, Port);
        return options;
    }
}
