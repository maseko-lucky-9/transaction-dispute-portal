namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// Why a back-channel logout token was refused: one value per rule of the spec's section 2.6. Logged (a bounded name,
/// never a token value) so a misconfiguration can be diagnosed; the caller always gets the same generic 400.
/// </summary>
public enum LogoutTokenFailure
{
    Malformed,
    Algorithm,
    Type,
    Signature,
    Issuer,
    Audience,
    IssuedAt,
    Expired,
    NotYetValid,
    Events,
    Nonce,
    TokenId,
    SessionId,
    Replayed,
}
