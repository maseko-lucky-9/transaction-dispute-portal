namespace DisputePortal.Api.Shared.Errors;

/// <summary>The request conflicts with the resource's current state (409).</summary>
public sealed class ConflictException(string message, string code, Exception? cause = null)
    : ApiException(message, StatusCodes.Status409Conflict, code, cause);
