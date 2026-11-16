using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Disputes;
using FluentValidation.Results;

namespace DisputePortal.UnitTests;

public sealed class TransitionRequestValidatorTests
{
    private const string Card = "4111111111111111";

    private readonly TransitionRequestValidator validator = new();

    [Theory]
    [InlineData("UnderReview")]
    [InlineData("underreview")]
    [InlineData("UNDERREVIEW")]
    public void AToStatusIsCaseInsensitive(string toStatus) => Assert.True(validator.Validate(new TransitionRequest(toStatus, null)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AToStatusIsRequired(string? toStatus) =>
        Assert.Contains("ToStatus", FailingFields(new TransitionRequest(toStatus, "Checked.")));

    [Theory]
    [InlineData("Approved")]
    [InlineData("42x")]
    public void AnUnknownToStatusIsAFieldError(string toStatus) =>
        Assert.Contains("ToStatus", FailingFields(new TransitionRequest(toStatus, "Checked.")));

    [Theory]
    [InlineData("Accepted", null)]
    [InlineData("rejected", "   ")]
    [InlineData("Accepted", "")]
    public void AResolutionNeedsANote(string toStatus, string? note) =>
        Assert.Contains("Note", FailingFields(new TransitionRequest(toStatus, note)));

    // ADR-019: a release goes back to the queue with nothing the customer would read; blank counts as no note, as in the Domain.
    [Theory]
    [InlineData("Submitted", "Going on leave.")]
    [InlineData("submitted", "x")]
    public void AReleaseWithANoteIsAFieldErrorOnNote(string toStatus, string note) =>
        Assert.Equal(["Note"], FailingFields(new TransitionRequest(toStatus, note)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AReleaseWithoutANoteIsValid(string? note) => Assert.True(validator.Validate(new TransitionRequest("Submitted", note)).IsValid);

    [Fact]
    public void StartingAReviewNeedsNoNote() => Assert.True(validator.Validate(new TransitionRequest("UnderReview", null)).IsValid);

    [Theory]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void ANoteHasAtMost2000CharactersAfterTrimming(int length, bool valid) =>
        Assert.Equal(valid, validator.Validate(new TransitionRequest("Accepted", "  " + new string('n', length) + "  ")).IsValid);

    [Fact]
    public void ANoteWithAControlCharacterIsInvalidCharacters() =>
        Assert.Equal(SensitiveNumberDetector.InvalidCharactersCode, CodeOf(new TransitionRequest("Accepted", "nul\0here")));

    [Theory]
    [InlineData(Card)]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("  4111-1111-1111-1111  ")]
    public void ANoteWithACardNumberIsSensitiveData(string note) =>
        Assert.Equal(SensitiveNumberDetector.Code, CodeOf(new TransitionRequest("Rejected", note)));

    // The padding bypass: a raw note over the scan cap whose trimmed text is short must still be scanned.
    [Fact]
    public void AWhitespacePaddedCardNumberIsStillSensitiveData()
    {
        string padded = new string(' ', SensitiveNumberDetector.MaxScanLength + 500) + Card + new string(' ', 10);

        Assert.Equal(SensitiveNumberDetector.Code, CodeOf(new TransitionRequest("Accepted", padded)));
    }

    [Fact]
    public void AnOverlongNoteIsRefusedWithoutScanning()
    {
        ValidationResult result = validator.Validate(new TransitionRequest("Accepted", new string('n', SensitiveNumberDetector.MaxScanLength + 1)));

        Assert.False(result.IsValid);
        Assert.DoesNotContain(result.Errors, error => error.CustomState is ProblemCode);
    }

    private List<string> FailingFields(TransitionRequest request) => [.. validator.Validate(request).Errors.Select(error => error.PropertyName)];

    private string? CodeOf(TransitionRequest request) =>
        validator.Validate(request).Errors.Select(error => error.CustomState).OfType<ProblemCode>().FirstOrDefault()?.Code;
}
