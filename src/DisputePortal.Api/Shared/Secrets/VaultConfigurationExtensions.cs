namespace DisputePortal.Api.Shared.Secrets;

public static class VaultConfigurationExtensions
{
    /// <summary>
    /// Adds Vault as the last (highest-precedence) configuration source. In compose the app's credentials come only
    /// from here. Skipped when <c>Vault:Address</c> is empty, so tests supply configuration directly.
    /// </summary>
    /// <exception cref="VaultUnavailableException">Vault stayed unreachable for the whole bounded retry.</exception>
    public static void AddVaultSecrets(this IConfigurationManager configuration, bool migrate)
    {
        VaultSettings settings = configuration.GetSection(VaultSettings.Section).Get<VaultSettings>() ?? new VaultSettings();
        if (string.IsNullOrWhiteSpace(settings.Address))
        {
            return;
        }

        // ConfigurationManager loads a source as soon as it is added, so a failure surfaces on this line.
        configuration.Add(new VaultConfigurationSource(settings, VaultSecretPath.For(migrate)));
    }
}
