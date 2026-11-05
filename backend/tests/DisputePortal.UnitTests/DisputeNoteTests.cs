using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.UnitTests;

/// <summary>T040a: the Domain backstop for the agent's note, on the stored (trimmed) text, with the shared free-text cases.</summary>
public sealed class DisputeNoteTests
{
    public static TheoryData<string, string, bool, string?> SharedCases() => SensitiveNumberDetectorTests.SharedCases();

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void ANoteAgreesWithTheSharedCases(string name, string input, bool valid, string? code)
    {
        // A blank note is the "note required" rule on a resolution, not a free-text rule.
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);
        string note = input;
        if (valid)
        {
            dispute.Transition(DisputeStatus.Accepted, Actor.Agent("a"), note, DisputeTestData.Now);
            Assert.Equal(note.Trim(), dispute.ResolutionNote);
        }
        else
        {
            Assert.Equal(code, Assert.Throws<DomainException>(() => dispute.Transition(DisputeStatus.Accepted, Actor.Agent("a"), note, DisputeTestData.Now)).Code);
            Assert.Equal(DisputeStatus.UnderReview, dispute.Status);
        }

        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void ACardNumberHiddenByPaddingIsRefusedOnTheTrimmedNote()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);
        string padded = new string(' ', SensitiveNumberDetector.MaxScanLength + 1) + "4111111111111111";

        Assert.Equal("sensitive_data", Assert.Throws<DomainException>(() =>
            dispute.Transition(DisputeStatus.Rejected, Actor.Agent("a"), padded, DisputeTestData.Now)).Code);
    }

    [Fact]
    public void ANoteWithAControlCharacterIsRefusedBeforeTheNumberRule()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);

        Assert.Equal("invalid_characters", Assert.Throws<DomainException>(() =>
            dispute.Transition(DisputeStatus.Accepted, Actor.Agent("a"), "4111111111111111\0", DisputeTestData.Now)).Code);
    }

    [Fact]
    public void ANoteOnStartingAReviewIsAlsoChecked()
    {
        Dispute dispute = DisputeTestData.Submitted();

        Assert.Equal("sensitive_data", Assert.Throws<DomainException>(() =>
            dispute.Transition(DisputeStatus.UnderReview, Actor.Agent("a"), "card 4111 1111 1111 1111", DisputeTestData.Now)).Code);
    }

    [Fact]
    public void AnOverlongNoteIsTooLongNotAScanError()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);

        Assert.Equal("note_too_long", Assert.Throws<DomainException>(() =>
            dispute.Transition(DisputeStatus.Rejected, Actor.Agent("a"), new string('n', SensitiveNumberDetector.MaxScanLength + 5), DisputeTestData.Now)).Code);
    }
}
