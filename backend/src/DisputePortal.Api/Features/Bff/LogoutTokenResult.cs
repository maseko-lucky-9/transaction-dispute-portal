namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// The outcome of verifying a logout token: the Keycloak session id to end, or the rule it broke. A class, not a record, so
/// no generated ToString can put the sid or jti into a log line.
/// </summary>
public sealed class LogoutTokenResult
{
    private LogoutTokenResult(string? sessionId, string? issuer, string? tokenId, LogoutTokenFailure? failure)
    {
        SessionId = sessionId;
        Issuer = issuer;
        TokenId = tokenId;
        Failure = failure;
    }

    /// <summary>The verified <c>sid</c>; set only when the token was accepted.</summary>
    public string? SessionId { get; }

    /// <summary>The verified <c>iss</c> and <c>jti</c> the replay cache claimed, so a failed deletion can release the claim.</summary>
    public string? Issuer { get; }

    public string? TokenId { get; }

    public LogoutTokenFailure? Failure { get; }

    public bool IsAccepted => Failure is null;

    public static LogoutTokenResult Accepted(string sessionId, string issuer, string tokenId) => new(sessionId, issuer, tokenId, null);

    public static LogoutTokenResult Refused(LogoutTokenFailure failure) => new(null, null, null, failure);
}
