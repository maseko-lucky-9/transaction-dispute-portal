using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>Maps an EF Core save failure to a refusal; null means a defect, answered with 500.</summary>
public static class DbErrorTranslator
{
    public const string UniqueViolation = "23505";

    /// <summary>SQLSTATE class 22, data exception: a value the database refuses (22021 is a NUL in text).</summary>
    public const string DataExceptionClass = "22";

    // The cause becomes the refusal's InnerException, so the log keeps the SQLSTATE and server message.
    public static ApiException? Translate(DbUpdateException exception) => exception switch
    {
        DbUpdateConcurrencyException => new ConcurrencyConflictException(exception),
        { InnerException: PostgresException { SqlState: UniqueViolation } unique } => new DuplicateKeyException(unique.ConstraintName, exception),
        { InnerException: PostgresException postgres } when IsDataException(postgres) => new InvalidValueException(exception),
        { InnerException: NpgsqlException { IsTransient: true } } => new DatabaseUnavailableException(exception),
        _ => null,
    };

    /// <summary>A query (not a save) the database refused; null means a defect, answered with 500.</summary>
    public static ApiException? Translate(PostgresException exception) =>
        IsDataException(exception) ? new InvalidValueException(exception) : null;

    private static bool IsDataException(PostgresException exception) =>
        exception.SqlState.StartsWith(DataExceptionClass, StringComparison.Ordinal);
}
