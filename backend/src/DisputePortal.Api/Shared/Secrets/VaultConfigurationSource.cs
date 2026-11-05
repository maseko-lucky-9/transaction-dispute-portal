namespace DisputePortal.Api.Shared.Secrets;

public sealed class VaultConfigurationSource(VaultSettings settings, IReadOnlyList<VaultSecretPath> paths) : IConfigurationSource
{
    public VaultSettings Settings { get; } = settings;

    public IReadOnlyList<VaultSecretPath> Paths { get; } = paths;

    /// <summary>Replaces the network handler; unit tests answer Vault's HTTP API in memory. Null in production.</summary>
    public HttpMessageHandler? HttpMessageHandler { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new VaultConfigurationProvider(this);
}
