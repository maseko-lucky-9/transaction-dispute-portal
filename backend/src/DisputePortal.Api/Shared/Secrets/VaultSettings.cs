namespace DisputePortal.Api.Shared.Secrets;

/// <summary>
/// Where Vault is and how the app logs in to it: AppRole files (compose, the default) or the Kubernetes service-account
/// token (ADR-016). Empty <see cref="Address"/> skips Vault (tests).
/// </summary>
public sealed class VaultSettings
{
    public const string Section = "Vault";

    public string Address { get; init; } = string.Empty;

    public VaultAuthMethod AuthMethod { get; init; } = VaultAuthMethod.AppRole;

    public string RoleIdFile { get; init; } = string.Empty;

    public string SecretIdFile { get; init; } = string.Empty;

    /// <summary>The Vault role bound to the pod's service account and namespace. Required for Kubernetes.</summary>
    public string KubernetesRole { get; init; } = string.Empty;

    /// <summary>The projected service-account token; the kubelet rotates it, so it is re-read on every login.</summary>
    public string JwtFile { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    public string KubernetesMountPoint { get; init; } = "kubernetes";

    public string Mount { get; init; } = "kv";

    /// <summary>Bounded retry: about 30 s in total with the default delay, then the process exits 1.</summary>
    public int Attempts { get; init; } = 6;

    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}
