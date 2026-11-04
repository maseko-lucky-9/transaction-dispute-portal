namespace DisputePortal.Api.Shared.Errors;

/// <summary>An expected refusal with its HTTP status and stable machine-readable code (SCAD ADR-004).</summary>
/// <remarks>
/// SCAD calls this DomainException; here that name belongs to the pure Domain project, whose rule violations map to 422.
/// The cause reaches the log, never the response.
/// </remarks>
public abstract class ApiException(string message, int status, string code, Exception? cause = null)
    : Exception(message, cause)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}
