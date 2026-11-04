using System.Text.RegularExpressions;
using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Disputes;
using FluentValidation;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>Shape, length and format (400). Eligibility, including the amount's bounds, is the Domain's (422).</summary>
public sealed partial class CreateDisputeRequestValidator : AbstractValidator<CreateDisputeRequest>
{
    public const int DescriptionMinLength = 10;

    public CreateDisputeRequestValidator(IDisputeReasonRepository reasons)
    {
        RuleFor(request => request.Id).Must(BeANonEmptyUuid).WithMessage("id must be a non-empty UUID.");
        RuleFor(request => request.TransactionId).Must(BeANonEmptyUuid).WithMessage("transactionId must be a non-empty UUID.");
        RuleFor(request => request.ReasonCode).NotEmpty().WithMessage("reasonCode is required.");
        RuleFor(request => request.ReasonCode).Must(code => !SensitiveNumberDetector.ContainsInvalidCharacters(code))
            .WithMessage(InvalidCharactersMessage("reasonCode"))
            .WithState(_ => new ProblemCode(SensitiveNumberDetector.InvalidCharactersCode));
        // Only a clean, bounded code is looked up: PostgreSQL refuses a NUL in text.
        RuleFor(request => request.ReasonCode)
            .MustAsync((code, cancellationToken) => reasons.ExistsAsync(code!, cancellationToken))
            .When(request => request.ReasonCode is { Length: > 0 and <= DisputeReasonEntry.CodeMaxLength }
                && !SensitiveNumberDetector.ContainsInvalidCharacters(request.ReasonCode))
            .WithMessage("reasonCode is not a known reason; GET /api/v1/dispute-reasons lists them.");
        RuleFor(request => request.Description)
            .Must(description => description?.Trim().Length is >= DescriptionMinLength and <= Dispute.DescriptionMaxLength)
            .WithMessage($"description must be {DescriptionMinLength} to {Dispute.DescriptionMaxLength} characters.");
        // Rule A before rule B: a description with control characters is not scanned for numbers.
        RuleFor(request => request.Description).Must(description => !SensitiveNumberDetector.ContainsInvalidCharacters(description))
            .WithMessage(InvalidCharactersMessage("description"))
            .WithState(_ => new ProblemCode(SensitiveNumberDetector.InvalidCharactersCode));
        RuleFor(request => request.Description)
            .Must(description => !SensitiveNumberDetector.ContainsSensitiveNumber(description))
            .When(request => request.Description is { Length: <= SensitiveNumberDetector.MaxScanLength }
                && !SensitiveNumberDetector.ContainsInvalidCharacters(request.Description))
            .WithMessage("description must not contain a card or ID number; describe the problem without it.")
            .WithState(_ => new ProblemCode(SensitiveNumberDetector.Code));
        RuleFor(request => request.Amount).Matches(AmountPattern()).When(request => request.Amount is not null)
            .WithMessage("amount must be a decimal string with at most two decimals, e.g. \"125.50\".");
    }

    private static string InvalidCharactersMessage(string field) =>
        $"{field} must not contain control characters (tab and line breaks are fine).";

    // The canonical form only ("D"), as the OpenAPI document's format: uuid says.
    private static bool BeANonEmptyUuid(string? value) => Guid.TryParseExact(value, "D", out Guid id) && id != Guid.Empty;

    // Up to 13 integer digits (numeric(19,4) holds 15) and at most two decimals; zero passes here and fails eligibility.
    [GeneratedRegex(@"^(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,2})?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AmountPattern();
}
