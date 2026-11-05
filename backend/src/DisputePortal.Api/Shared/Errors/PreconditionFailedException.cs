namespace DisputePortal.Api.Shared.Errors;

/// <summary>
/// If-Match names a version that is no longer current (412). The endpoint answers it itself with the current ETag, so
/// the client can reload: the exception handler middleware strips ETag from every response it writes.
/// </summary>
public sealed class PreconditionFailedException(string? currentETag, Exception? cause = null)
    : ApiException("This changed since you loaded it; reload and try again.", StatusCodes.Status412PreconditionFailed, "precondition_failed", cause)
{
    public string? CurrentETag { get; } = currentETag;
}
