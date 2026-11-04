namespace DisputePortal.Api.Shared.Errors;

/// <summary>Redis, the session store, cannot be reached. Sign-in and session validation fail closed.</summary>
public sealed class SessionStoreUnavailableException(Exception? cause = null)
    : ApiException("The session store is unavailable; retry shortly.", StatusCodes.Status503ServiceUnavailable, "session_store_unavailable", cause);
