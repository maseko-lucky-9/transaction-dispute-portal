namespace DisputePortal.Api.Shared.Secrets;

/// <summary>The Vault settings cannot work (for example Kubernetes auth without a role); thrown before any retry.</summary>
public sealed class VaultMisconfiguredException(string message, Exception? cause = null) : Exception(message, cause);
