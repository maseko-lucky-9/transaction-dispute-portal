namespace DisputePortal.Api.Shared.Secrets;

/// <summary>Where Vault is and where the AppRole credentials are mounted. Empty <see cref="Address"/> skips Vault (tests).</summary>
public sealed class VaultSettings
{
    public const string Section = "Vault";

    public string Address { get; init; } = string.Empty;

    public string RoleIdFile { get; init; } = string.Empty;

    public string SecretIdFile { get; init; } = string.Empty;

    public string Mount { get; init; } = "kv";

    /// <summary>Bounded retry: about 30 s in total with the default delay, then the process exits 1.</summary>
    public int Attempts { get; init; } = 6;

    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}
