using System.Globalization;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Kubernetes;
using VaultSharp.V1.Commons;

namespace DisputePortal.Api.Shared.Secrets;

/// <summary>
/// Reads the app's credentials from Vault KV v2 once, at startup, after an AppRole or Kubernetes login (ADR-016).
/// ponytail: credentials are read once, so rotating one means a rolling restart; the upgrade path is Vault's
/// database secrets engine (dynamic, short-lived DB credentials) with a lease-renewing provider.
/// </summary>
public sealed class VaultConfigurationProvider(VaultConfigurationSource source) : ConfigurationProvider
{
    // Startup runs before any request and has no synchronization context, so blocking here cannot deadlock.
    public override void Load() => Data = LoadWithRetryAsync().GetAwaiter().GetResult();

    private async Task<Dictionary<string, string?>> LoadWithRetryAsync()
    {
        Validate(source.Settings);
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
                    $"Vault at {source.Settings.Address} is unreachable or refused the {source.Settings.AuthMethod} login after {attempt} attempts", exception);
            }
        }
    }

    // Settings that can never work fail at once instead of burning the retry budget. AppRole is not checked here: an
    // empty file path already fails on the first read, as it always has.
    private static void Validate(VaultSettings settings)
    {
        switch (settings.AuthMethod)
        {
            case VaultAuthMethod.AppRole:
                return;
            case VaultAuthMethod.Kubernetes when string.IsNullOrWhiteSpace(settings.KubernetesRole):
                throw new VaultMisconfiguredException("Vault:AuthMethod is Kubernetes but Vault:KubernetesRole is empty");
            case VaultAuthMethod.Kubernetes:
                return;
            default:
                throw new VaultMisconfiguredException(
                    $"Vault:AuthMethod '{settings.AuthMethod}' is not supported; use AppRole or Kubernetes");
        }
    }

    private async Task<Dictionary<string, string?>> ReadAllAsync()
    {
        VaultClientSettings clientSettings = new(source.Settings.Address, await LoginInfoAsync(source.Settings));
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

    // Re-read every attempt: the bootstrap container may still be writing the AppRole files, and the kubelet rotates the
    // projected service-account token (which may not be mounted yet on the first attempt).
    private static async Task<IAuthMethodInfo> LoginInfoAsync(VaultSettings settings)
    {
        if (settings.AuthMethod == VaultAuthMethod.Kubernetes)
        {
            string jwt = (await File.ReadAllTextAsync(settings.JwtFile)).Trim();
            if (jwt.Length == 0)
            {
                // Retryable like a missing file; the token itself is never part of a message.
                throw new IOException($"The service-account token file {settings.JwtFile} is empty");
            }

            return new KubernetesAuthMethodInfo(settings.KubernetesMountPoint, settings.KubernetesRole, jwt);
        }

        string roleId = (await File.ReadAllTextAsync(settings.RoleIdFile)).Trim();
        string secretId = (await File.ReadAllTextAsync(settings.SecretIdFile)).Trim();
        return new AppRoleAuthMethodInfo(roleId, secretId);
    }

    // A missing file, a refused connection or a Vault error (sealed, 403 before the policy or role exists) may all clear
    // up once the bootstrap container or the cluster finishes; anything else is a defect and fails at once.
    private static bool IsRetryable(Exception exception) =>
        exception is IOException or HttpRequestException or VaultApiException or TaskCanceledException;
}
