namespace DisputePortal.Api.Shared.Secrets;

/// <summary>How the app logs in to Vault (ADR-016).</summary>
public enum VaultAuthMethod
{
    /// <summary>role_id and secret_id read from files that compose's bootstrap container writes (ADR-005).</summary>
    AppRole,

    /// <summary>The pod's projected service-account token, verified by Vault against the cluster.</summary>
    Kubernetes,
}
