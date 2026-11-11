namespace DisputePortal.Api.Shared.Secrets;

/// <summary>Vault could not be reached, or refused the AppRole or Kubernetes login, within the bounded retry.</summary>
public sealed class VaultUnavailableException(string message, Exception? cause)
    : Exception(message, cause);
