namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// The outcome of verifying a logout token: the Keycloak session id to end, or the rule it broke. A class, not a record, so
/// no generated ToString can put the sid into a log line.
/// </summary>
public sealed class LogoutTokenResult
{
    private LogoutTokenResult(string? sessionId, LogoutTokenFailure? failure)
    {
        SessionId = sessionId;
        Failure = failure;
    }

    /// <summary>The verified <c>sid</c>; set only when the token was accepted.</summary>
    public string? SessionId { get; }

    public LogoutTokenFailure? Failure { get; }

    public bool IsAccepted => Failure is null;

    public static LogoutTokenResult Accepted(string sessionId) => new(sessionId, null);

    public static LogoutTokenResult Refused(LogoutTokenFailure failure) => new(null, failure);
}
