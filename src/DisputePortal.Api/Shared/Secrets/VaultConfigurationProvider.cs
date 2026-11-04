using System.Globalization;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.Commons;

namespace DisputePortal.Api.Shared.Secrets;

/// <summary>
/// Reads the app's credentials from Vault KV v2 once, at startup, after an AppRole login.
/// ponytail: credentials are read once, so rotating one means a rolling restart; the upgrade path is Vault's
/// database secrets engine (dynamic, short-lived DB credentials) with a lease-renewing provider.
/// </summary>
public sealed class VaultConfigurationProvider(VaultConfigurationSource source) : ConfigurationProvider
{
    // Startup runs before any request and has no synchronization context, so blocking here cannot deadlock.
    public override void Load() => Data = LoadWithRetryAsync().GetAwaiter().GetResult();

    private async Task<Dictionary<string, string?>> LoadWithRetryAsync()
    {
        TimeSpan delay = source.Settings.FirstRetryDelay;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await ReadAllAsync();
            }
            catch (Exception exception) when (IsRetryable(exception) && attempt < source.Settings.Attempts)
            {
                await Task.Delay(delay);
                delay *= 2;
            }
            catch (Exception exception) when (IsRetryable(exception))
            {
                throw new VaultUnavailableException(
                    $"Vault at {source.Settings.Address} is unreachable or refused the AppRole login after {attempt} attempts", exception);
            }
        }
    }

    private async Task<Dictionary<string, string?>> ReadAllAsync()
    {
        // Re-read every attempt: the bootstrap container may still be writing the files.
        string roleId = (await File.ReadAllTextAsync(source.Settings.RoleIdFile)).Trim();
        string secretId = (await File.ReadAllTextAsync(source.Settings.SecretIdFile)).Trim();
        VaultClientSettings clientSettings = new(source.Settings.Address, new AppRoleAuthMethodInfo(roleId, secretId));
        if (source.HttpMessageHandler is { } handler)
        {
            clientSettings.MyHttpClientProviderFunc = _ => new HttpClient(handler, disposeHandler: false);
        }

        VaultClient client = new(clientSettings);
        Dictionary<string, string?> data = new(StringComparer.OrdinalIgnoreCase);
        foreach (VaultSecretPath path in source.Paths)
        {
            Secret<SecretData> secret = await client.V1.Secrets.KeyValue.V2.ReadSecretAsync(path.Path, mountPoint: source.Settings.Mount);
            foreach (KeyValuePair<string, object> entry in secret.Data.Data)
            {
                data[path.ConfigurationPrefix + ConfigurationPath.KeyDelimiter + entry.Key] =
                    Convert.ToString(entry.Value, CultureInfo.InvariantCulture);
            }
        }

        return data;
    }

    // A missing file, a refused connection or a Vault error (sealed, 403 before the policy exists) may all clear up
    // once the bootstrap container finishes; anything else is a defect and fails at once.
    private static bool IsRetryable(Exception exception) =>
        exception is IOException or HttpRequestException or VaultApiException or TaskCanceledException;
}
