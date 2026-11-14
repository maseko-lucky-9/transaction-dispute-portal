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
        // Read once, before the retry loop: a bad pin file is a settings error, not something that clears up.
        byte[]? pinnedHash = string.IsNullOrWhiteSpace(source.Settings.PinnedServerCertificateFile)
            ? null
            : VaultServerCertificatePin.ReadHash(source.Settings.PinnedServerCertificateFile);
        TimeSpan delay = source.Settings.FirstRetryDelay;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await ReadAllAsync(pinnedHash);
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
        // A pin over plain http would be skipped silently and send the credentials in cleartext: refuse it, any method.
        if (!string.IsNullOrWhiteSpace(settings.PinnedServerCertificateFile) && !IsHttps(settings.Address))
        {
            throw new VaultMisconfiguredException("Vault:PinnedServerCertificateFile is set, so Vault:Address must use https://");
        }

        switch (settings.AuthMethod)
        {
            case VaultAuthMethod.AppRole:
                return;
            case VaultAuthMethod.Kubernetes when string.IsNullOrWhiteSpace(settings.KubernetesRole):
                throw new VaultMisconfiguredException("Vault:AuthMethod is Kubernetes but Vault:KubernetesRole is empty");
            // The service-account token is a replayable credential: never send it over plain http.
            case VaultAuthMethod.Kubernetes when !IsHttps(settings.Address):
                throw new VaultMisconfiguredException("Vault:AuthMethod is Kubernetes, so Vault:Address must use https://");
            case VaultAuthMethod.Kubernetes:
                return;
            default:
                throw new VaultMisconfiguredException(
                    $"Vault:AuthMethod '{settings.AuthMethod}' is not supported; use AppRole or Kubernetes");
        }
    }

    private static bool IsHttps(string address) => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private async Task<Dictionary<string, string?>> ReadAllAsync(byte[]? pinnedHash)
    {
        VaultClientSettings clientSettings = new(source.Settings.Address, await LoginInfoAsync(source.Settings));
        if (source.HttpMessageHandler is { } handler)
        {
            // The test seam: the injected handler is used as-is and the pin is NOT applied (unit tests answer in memory).
            clientSettings.MyHttpClientProviderFunc = _ => new HttpClient(handler, disposeHandler: false);
        }
        else if (pinnedHash is not null)
        {
            clientSettings.PostProcessHttpClientHandlerAction = vaultHandler => VaultServerCertificatePin.Apply(vaultHandler, pinnedHash);
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
            string jwt = (await ReadTokenAsync(settings.JwtFile)).Trim();
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

    // A token file the process cannot read yet (permissions or the mount still settling) is retried like a missing one.
    private static async Task<string> ReadTokenAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException($"The service-account token file {path} cannot be read", exception);
        }
    }

    // A missing file, a refused connection or a Vault error (sealed, 403 before the policy or role exists) may all clear
    // up once the bootstrap container or the cluster finishes; anything else is a defect and fails at once.
    private static bool IsRetryable(Exception exception) =>
        exception is IOException or HttpRequestException or VaultApiException or TaskCanceledException;
}
