using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// The 404 for a dispute, in two forms that stay apart on purpose. A customer's 404 is the owner filter's answer: it
/// cannot tell another customer's id from a missing one, so each is counted as <see cref="SecurityMetrics.Bola404"/>.
/// An agent sees every dispute, so theirs is only a missing id and is never counted; the agent slice must not call
/// <see cref="ToCustomer"/> (an architecture test checks it).
/// </summary>
public static class DisputeNotFound
{
    /// <summary>Counts the refusal, then returns it: every dispute 404 answered to a customer comes through here.</summary>
    public static NotFoundException ToCustomer(SecurityMetrics securityMetrics)
    {
        securityMetrics.Record(SecurityMetrics.Bola404);
        return Create();
    }

    /// <summary>The same answer, uncounted: an agent asked for an id that does not exist.</summary>
    public static NotFoundException ToAgent() => Create();

    private static NotFoundException Create() => new("dispute_not_found", "No dispute with this id.");
}
