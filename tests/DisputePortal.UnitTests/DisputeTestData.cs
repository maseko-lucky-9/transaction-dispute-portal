using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.UnitTests;

/// <summary>Disputes in any status, reached through the legal moves only.</summary>
public static class DisputeTestData
{
    public const string CustomerSubject = "customer-sub";
    public const string AgentSubject = "agent-sub";

    public static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    public static Dispute Submitted() => Dispute.Submit(Submission(), CustomerSubject, Now);

    public static DisputeSubmission Submission(decimal amount = 125.50m, string description = "Charged twice for one order.") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "duplicate_charge", description, Money.Of(amount, "ZAR"));

    public static Dispute In(DisputeStatus status)
    {
        Dispute dispute = Submitted();
        DateTimeOffset later = Now.AddHours(1);
        switch (status)
        {
            case DisputeStatus.Submitted:
                break;
            case DisputeStatus.UnderReview:
                dispute.Transition(DisputeStatus.UnderReview, Actor.Agent(AgentSubject), null, later);
                break;
            case DisputeStatus.Accepted or DisputeStatus.Rejected:
                dispute.Transition(DisputeStatus.UnderReview, Actor.Agent(AgentSubject), null, later);
                dispute.Transition(status, Actor.Agent(AgentSubject), "Checked with the merchant.", later);
                break;
            case DisputeStatus.Withdrawn:
                dispute.Withdraw(Actor.Customer(CustomerSubject), later);
                break;
        }

        return dispute;
    }

    public static Actor ActorOf(ActorType type) => type == ActorType.Customer ? Actor.Customer(CustomerSubject) : Actor.Agent(AgentSubject);
}
