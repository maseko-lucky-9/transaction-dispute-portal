namespace DisputePortal.Api.Shared.Errors;

/// <summary>Missing or not owned: both look identical, so a response never reveals that another customer's resource exists.</summary>
public sealed class NotFoundException(string code, string message)
    : ApiException(message, StatusCodes.Status404NotFound, code);
