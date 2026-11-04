using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>Maps an EF Core save failure to a refusal; null means a defect, answered with 500.</summary>
public static class DbErrorTranslator
{
    public const string UniqueViolation = "23505";

    // The cause becomes the refusal's InnerException, so the log keeps the SQLSTATE and server message.
    public static ApiException? Translate(DbUpdateException exception) => exception switch
    {
        DbUpdateConcurrencyException => new ConcurrencyConflictException(exception),
        { InnerException: PostgresException { SqlState: UniqueViolation } unique } => new DuplicateKeyException(unique.ConstraintName, exception),
        { InnerException: NpgsqlException { IsTransient: true } } => new DatabaseUnavailableException(exception),
        _ => null,
    };
}
