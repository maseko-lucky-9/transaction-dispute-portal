namespace DisputePortal.Api.Shared.Errors;

/// <summary>
/// PostgreSQL refused a value as data (SQLSTATE class 22, e.g. 22021 for a NUL in text): the request carried something
/// no validator caught. Answered as 400, not 500, since retrying the same request cannot succeed.
/// </summary>
public sealed class InvalidValueException(Exception? cause = null)
    : ApiException("A value in the request cannot be stored.", StatusCodes.Status400BadRequest, ErrorCode, cause)
{
    public const string ErrorCode = "invalid_value";
}
