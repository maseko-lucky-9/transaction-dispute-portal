using System.Text.Json;
using DisputePortal.Api.Shared.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DisputePortal.UnitTests;

/// <summary>Class-22 data errors from PostgreSQL become 400 invalid_value, logged at Warning without the value.</summary>
public sealed class ApiExceptionHandlerTests
{
    // The message quotes the offending value, as PostgreSQL's own messages can: it must never reach the log or the body.
    private const string SecretValue = "my card 4111 1111 1111 1111";

    // A query the database refused (the validator's reason lookup was one): not translated by the unit of work.
    [Fact]
    public async Task ARawDataExceptionFromAQueryIsA400InvalidValue()
    {
        (int status, JsonElement body, RecordingLogger<ApiExceptionHandler> logger) = await HandleAsync(Postgres("22021"));

        Assert.Equal(400, status);
        Assert.Equal(InvalidValueException.ErrorCode, body.GetProperty("code").GetString());
        AssertLoggedSafely(logger, "22021", nameof(PostgresException));
    }

    // The unit of work's translation of a save keeps the SQLSTATE reachable through the cause chain.
    [Fact]
    public async Task ATranslatedSaveIsLoggedWithItsSqlState()
    {
        ApiException refusal = DbErrorTranslator.Translate(new DbUpdateException("save", Postgres("22001")))!;

        (int status, _, RecordingLogger<ApiExceptionHandler> logger) = await HandleAsync(refusal);

        Assert.Equal(400, status);
        AssertLoggedSafely(logger, "22001", nameof(InvalidValueException));
    }

    [Fact]
    public async Task AQueryErrorOutsideClass22IsStillA500()
    {
        (int status, JsonElement body, _) = await HandleAsync(Postgres("42P01"));

        Assert.Equal(500, status);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
    }

    private static void AssertLoggedSafely(RecordingLogger<ApiExceptionHandler> logger, string sqlState, string exceptionType)
    {
        (LogLevel level, string message, Exception? exception) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(sqlState, message, StringComparison.Ordinal);
        Assert.Contains(exceptionType, message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretValue, message, StringComparison.Ordinal);
        Assert.Null(exception);
    }

    private static async Task<(int Status, JsonElement Body, RecordingLogger<ApiExceptionHandler> Logger)> HandleAsync(Exception exception)
    {
        RecordingLogger<ApiExceptionHandler> logger = new();
        DefaultHttpContext context = new();
        context.Request.Path = "/api/v1/disputes";
        using MemoryStream body = new();
        context.Response.Body = body;

        Assert.True(await new ApiExceptionHandler(logger).TryHandleAsync(context, exception, CancellationToken.None));

        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        Assert.DoesNotContain(SecretValue, document.RootElement.GetRawText(), StringComparison.Ordinal);
        return (context.Response.StatusCode, document.RootElement.Clone(), logger);
    }

    private static PostgresException Postgres(string sqlState) => new(SecretValue, "ERROR", "ERROR", sqlState);
}
