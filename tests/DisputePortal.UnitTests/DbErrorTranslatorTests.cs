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
