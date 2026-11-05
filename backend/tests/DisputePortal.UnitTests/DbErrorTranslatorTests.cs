using DisputePortal.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.UnitTests;

public sealed class DbErrorTranslatorTests
{
    [Fact]
    public void AUniqueViolationIsA409Duplicate()
    {
        ApiException? refusal = DbErrorTranslator.Translate(new DbUpdateException("save", Postgres("23505")));

        Assert.Equal((409, "duplicate"), (refusal?.Status, refusal?.Code));
    }

    [Fact]
    public void AConcurrencyConflictIsA409()
    {
        ApiException? refusal = DbErrorTranslator.Translate(new DbUpdateConcurrencyException("stale"));

        Assert.Equal((409, "concurrency_conflict"), (refusal?.Status, refusal?.Code));
    }

    [Fact]
    public void ATransientServerErrorIsA503()
    {
        ApiException? refusal = DbErrorTranslator.Translate(new DbUpdateException("save", Postgres("57P01")));

        Assert.Equal((503, "database_unavailable"), (refusal?.Status, refusal?.Code));
    }

    // Defence in depth behind the validators: a value PostgreSQL refuses as data is the client's 400, never a 500.
    [Theory]
    [InlineData("22021")]
    [InlineData("22001")]
    [InlineData("22P02")]
    public void ADataExceptionIsA400InvalidValue(string sqlState)
    {
        ApiException? onSave = DbErrorTranslator.Translate(new DbUpdateException("save", Postgres(sqlState)));
        ApiException? onQuery = DbErrorTranslator.Translate(Postgres(sqlState));

        Assert.Equal((400, "invalid_value"), (onSave?.Status, onSave?.Code));
        Assert.Equal((400, "invalid_value"), (onQuery?.Status, onQuery?.Code));
    }

    [Fact]
    public void AQueryErrorOutsideClass22IsADefect() => Assert.Null(DbErrorTranslator.Translate(Postgres("42P01")));

    // A CHECK violation means a guard was missing: a defect, answered with 500.
    [Fact]
    public void AnythingElseIsADefect() =>
        Assert.Null(DbErrorTranslator.Translate(new DbUpdateException("save", Postgres("23514"))));

    [Fact]
    public void TheCauseIsKeptForTheLog()
    {
        DbUpdateException cause = new("save", Postgres("23505"));

        Assert.Same(cause, DbErrorTranslator.Translate(cause)?.InnerException);
    }

    private static PostgresException Postgres(string sqlState) => new("message", "ERROR", "ERROR", sqlState);
}
