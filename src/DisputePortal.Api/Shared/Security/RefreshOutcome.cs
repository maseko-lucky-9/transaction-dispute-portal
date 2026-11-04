using Microsoft.AspNetCore.Authentication;

namespace DisputePortal.Api.Shared.Security;

public enum RefreshOutcomeKind
{
    /// <summary>The ticket now holds fresh tokens (refreshed here, or by another replica): adopt it.</summary>
    Renewed,

    /// <summary>The identity provider refused the refresh token; the ticket was deleted.</summary>
    Refused,

    /// <summary>The ticket is gone (logout, or ended elsewhere).</summary>
    Gone,

    /// <summary>The identity provider, or the replica holding the refresh lock, did not answer in time.</summary>
    Unavailable,
}

public sealed record RefreshOutcome(RefreshOutcomeKind Kind, AuthenticationTicket? Ticket = null)
{
    public static readonly RefreshOutcome Refused = new(RefreshOutcomeKind.Refused);
    public static readonly RefreshOutcome Gone = new(RefreshOutcomeKind.Gone);
    public static readonly RefreshOutcome Unavailable = new(RefreshOutcomeKind.Unavailable);

    public static RefreshOutcome Renewed(AuthenticationTicket ticket) => new(RefreshOutcomeKind.Renewed, ticket);
}
