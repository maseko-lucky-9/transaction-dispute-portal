namespace DisputePortal.Api.Shared.Errors;

/// <summary>A state change sent without If-Match (428, RFC 6585): the client must say which version it saw.</summary>
public sealed class PreconditionRequiredException()
    : ApiException("Send If-Match with the ETag of the version you are changing.", StatusCodes.Status428PreconditionRequired, "precondition_required");
