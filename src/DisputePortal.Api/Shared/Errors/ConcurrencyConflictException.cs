namespace DisputePortal.Api.Shared.Errors;

/// <summary>The row changed between read and write: the optimistic-concurrency check (xmin) found it stale.</summary>
public sealed class ConcurrencyConflictException(Exception? cause = null)
    : ApiException("The resource was changed by another request; reload and retry.", StatusCodes.Status409Conflict, "concurrency_conflict", cause);
