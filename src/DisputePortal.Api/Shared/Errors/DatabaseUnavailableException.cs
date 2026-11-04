namespace DisputePortal.Api.Shared.Errors;

public sealed class DatabaseUnavailableException(Exception? cause = null)
    : ApiException("The database is unavailable; retry shortly.", StatusCodes.Status503ServiceUnavailable, "database_unavailable", cause);
