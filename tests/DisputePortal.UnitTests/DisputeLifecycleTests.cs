using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.UnitTests;

/// <summary>ADR-003: every (from, to, actor) is either a row of the table or refused, with nothing changed.</summary>
public sealed class DisputeLifecycleTests
{
    // Written out here, not read from DisputeLifecycle, so the test is an independent statement of the design.
    private static readonly HashSet<(DisputeStatus, DisputeStatus, ActorType)> Legal =
    [
        (DisputeStatus.Submitted, DisputeStatus.UnderReview, ActorType.Agent),
        (DisputeStatus.UnderReview, DisputeStatus.Accepted, ActorType.Agent),
        (DisputeStatus.UnderReview, DisputeStatus.Rejected, ActorType.Agent),
        (DisputeStatus.Submitted, DisputeStatus.Withdrawn, ActorType.Customer),
        (DisputeStatus.UnderReview, DisputeStatus.Withdrawn, ActorType.Customer),
    ];

    public static TheoryData<DisputeStatus, DisputeStatus, ActorType> EveryMove()
    {
        TheoryData<DisputeStatus, DisputeStatus, ActorType> moves = [];
        foreach (DisputeStatus from in Enum.GetValues<DisputeStatus>())
        {
            foreach (DisputeStatus to in Enum.GetValues<DisputeStatus>())
            {
                foreach (ActorType actor in Enum.GetValues<ActorType>())
                {
                    moves.Add(from, to, actor);
                }
            }
        }

        return moves;
    }

    [Theory]
    [MemberData(nameof(EveryMove))]
    public void EveryMoveIsAllowedExactlyWhenItIsInTheTable(DisputeStatus from, DisputeStatus to, ActorType actor)
    {
        Dispute dispute = DisputeTestData.In(from);
        int history = dispute.StatusChanges.Count;
        DateTimeOffset at = DisputeTestData.Now.AddDays(1);

        if (Legal.Contains((from, to, actor)))
        {
            dispute.Transition(to, DisputeTestData.ActorOf(actor), "Checked with the merchant.", at);

            Assert.Equal(to, dispute.Status);
            Assert.Equal(history + 1, dispute.StatusChanges.Count);
            DisputeStatusChange last = dispute.StatusChanges[^1];
            Assert.Equal((from, to, actor), (last.From!.Value, last.To, last.ActorType));
            Assert.Equal(at, dispute.UpdatedAt);
            Assert.Equal(to is DisputeStatus.Accepted or DisputeStatus.Rejected or DisputeStatus.Withdrawn, dispute.ResolvedAt == at);
        }
        else
        {
            InvalidTransitionException refused = Assert.Throws<InvalidTransitionException>(
                () => dispute.Transition(to, DisputeTestData.ActorOf(actor), "Checked with the merchant.", at));

            Assert.Equal(InvalidTransitionException.ErrorCode, refused.Code);
            Assert.Equal(from, dispute.Status);
            Assert.Equal(history, dispute.StatusChanges.Count);
        }
    }

    [Theory]
    [InlineData(DisputeStatus.Accepted)]
    [InlineData(DisputeStatus.Rejected)]
    [InlineData(DisputeStatus.Withdrawn)]
    public void TerminalStatusesHaveNoWayOut(DisputeStatus terminal)
    {
        Assert.True(DisputeLifecycle.IsTerminal(terminal));
        Assert.All(Enum.GetValues<DisputeStatus>(), to => Assert.All(Enum.GetValues<ActorType>(), actor =>
            Assert.False(DisputeLifecycle.Allows(terminal, to, actor))));
    }

    [Fact]
    public void SubmittingRecordsTheFirstEntryWithNoPreviousStatus()
    {
        Dispute dispute = DisputeTestData.Submitted();

        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        DisputeStatusChange first = Assert.Single(dispute.StatusChanges);
        Assert.Null(first.From);
        Assert.Equal((DisputeStatus.Submitted, ActorType.Customer, DisputeTestData.CustomerSubject), (first.To, first.ActorType, first.ActorId));
        Assert.Equal((DisputeTestData.Now, DisputeTestData.Now), (dispute.CreatedAt, dispute.UpdatedAt));
        Assert.Null(dispute.ResolvedAt);
        Assert.Null(dispute.ResolutionNote);
    }

    // The customer withdraws; an agent cannot do it for them, and a customer cannot review their own dispute.
    [Fact]
    public void OnlyTheCustomerWithdrawsAndOnlyAnAgentReviews()
    {
        Assert.Throws<InvalidTransitionException>(() => DisputeTestData.Submitted().Withdraw(Actor.Agent(DisputeTestData.AgentSubject), DisputeTestData.Now));
        Assert.Throws<InvalidTransitionException>(() =>
            DisputeTestData.Submitted().Transition(DisputeStatus.UnderReview, Actor.Customer(DisputeTestData.CustomerSubject), null, DisputeTestData.Now));
    }

    [Fact]
    public void WithdrawingResolvesWithoutANote()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.Withdrawn);

        Assert.NotNull(dispute.ResolvedAt);
        Assert.Null(dispute.ResolutionNote);
        Assert.Null(dispute.StatusChanges[^1].Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AResolutionNeedsANote(string? note)
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);

        DomainException refused = Assert.Throws<DomainException>(() => dispute.Transition(DisputeStatus.Accepted, Actor.Agent("a"), note, DisputeTestData.Now));

        Assert.Equal("resolution_note_required", refused.Code);
        Assert.Equal(DisputeStatus.UnderReview, dispute.Status);
    }

    [Fact]
    public void ANoteIsTrimmedAndAtMost2000Characters()
    {
        Dispute accepted = DisputeTestData.In(DisputeStatus.UnderReview);
        accepted.Transition(DisputeStatus.Accepted, Actor.Agent("a"), "  " + new string('n', Dispute.NoteMaxLength) + "  ", DisputeTestData.Now);
        Dispute tooLong = DisputeTestData.In(DisputeStatus.UnderReview);

        Assert.Equal(new string('n', Dispute.NoteMaxLength), accepted.ResolutionNote);
        Assert.Equal("note_too_long", Assert.Throws<DomainException>(() =>
            tooLong.Transition(DisputeStatus.Rejected, Actor.Agent("a"), new string('n', Dispute.NoteMaxLength + 1), DisputeTestData.Now)).Code);
    }

    [Fact]
    public void StartingAReviewNeedsNoNote()
    {
        Dispute dispute = DisputeTestData.Submitted();

        dispute.Transition(DisputeStatus.UnderReview, Actor.Agent("a"), null, DisputeTestData.Now);

        Assert.Equal(DisputeStatus.UnderReview, dispute.Status);
        Assert.Null(dispute.ResolvedAt);
    }

    [Theory]
    [InlineData("", 10, "description_invalid")]
    [InlineData("   ", 10, "description_invalid")]
    [InlineData("Valid text", 0, "amount_not_positive")]
    public void SubmitGuardsItsInvariants(string description, decimal amount, string code) =>
        Assert.Equal(code, Assert.Throws<DomainException>(() =>
            Dispute.Submit(DisputeTestData.Submission(amount, description), "s", DisputeTestData.Now)).Code);

    [Fact]
    public void SubmitRefusesAnEmptyIdAndAnOverlongDescription()
    {
        DisputeSubmission emptyId = DisputeTestData.Submission() with { Id = Guid.Empty };
        DisputeSubmission overlong = DisputeTestData.Submission(description: new string('d', Dispute.DescriptionMaxLength + 1));

        Assert.Equal("dispute_id_required", Assert.Throws<DomainException>(() => Dispute.Submit(emptyId, "s", DisputeTestData.Now)).Code);
        Assert.Equal("description_invalid", Assert.Throws<DomainException>(() => Dispute.Submit(overlong, "s", DisputeTestData.Now)).Code);
    }
}
