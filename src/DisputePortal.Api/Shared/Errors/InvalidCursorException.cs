namespace DisputePortal.Api.Shared.Errors;

/// <summary>A cursor that was tampered with, belongs to another user, or was issued for other filters (ADR-006).</summary>
public sealed class InvalidCursorException()
    : ApiException("The cursor is not valid for this list; start again from the first page.", StatusCodes.Status400BadRequest, "invalid_cursor");
