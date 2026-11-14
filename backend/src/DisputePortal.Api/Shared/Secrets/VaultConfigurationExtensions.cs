namespace DisputePortal.Api.Shared.Secrets;

public static class VaultConfigurationExtensions
{
    /// <summary>
    /// Adds Vault as the last (highest-precedence) configuration source. In compose the app's credentials come only
    /// from here. Skipped when <c>Vault:Address</c> is empty, so tests supply configuration directly.
    /// </summary>
    /// <exception cref="VaultUnavailableException">Vault stayed unreachable for the whole bounded retry.</exception>
    /// <exception cref="VaultMisconfiguredException">A <c>Vault:</c> setting cannot be bound or can never work.</exception>
    public static void AddVaultSecrets(this IConfigurationManager configuration, bool migrate)
    {
        VaultSettings settings = Bind(configuration);
        if (string.IsNullOrWhiteSpace(settings.Address))
        {
            return;
        }

        // ConfigurationManager loads a source as soon as it is added, so a failure surfaces on this line.
        configuration.Add(new VaultConfigurationSource(settings, VaultSecretPath.For(migrate)));
    }

    // A typo such as Vault__AuthMethod=Kubernets fails in the binder; report it as one line, like any other bad setting.
    // VaultSettings holds addresses and file paths only, so the binder's message (key and value) carries no secret.
    private static VaultSettings Bind(IConfiguration configuration)
    {
        try
        {
            return configuration.GetSection(VaultSettings.Section).Get<VaultSettings>() ?? new VaultSettings();
        }
        catch (InvalidOperationException exception)
        {
            throw new VaultMisconfiguredException(exception.Message);
        }
    }
}
