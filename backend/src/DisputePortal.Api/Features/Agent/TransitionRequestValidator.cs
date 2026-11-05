using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Disputes;
using FluentValidation;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// Shape and the note's rules (400). The note gets the description's free-text rules (invalid_characters, then
/// sensitive_data on the trimmed text it will be stored as). The Domain re-checks the note when it applies the move.
/// </summary>
public sealed class TransitionRequestValidator : AbstractValidator<TransitionRequest>
{
    public TransitionRequestValidator()
    {
        RuleFor(request => request.ToStatus).NotEmpty().WithMessage("toStatus is required.");
        RuleFor(request => request.ToStatus).IsEnumName(typeof(DisputeStatus), caseSensitive: false)
            .When(request => !string.IsNullOrEmpty(request.ToStatus))
            .WithMessage(UnknownTargetStatusException.FieldMessage);
        RuleFor(request => request.Note).Must(note => !string.IsNullOrWhiteSpace(note))
            .When(request => IsResolution(request.ToStatus))
            .WithMessage("note is required to accept or reject a dispute.");
        RuleFor(request => request.Note).Must(note => note is null || note.Trim().Length <= Dispute.NoteMaxLength)
            .WithMessage($"note has at most {Dispute.NoteMaxLength} characters.");
        RuleFor(request => request.Note).Must(note => !SensitiveNumberDetector.ContainsInvalidCharacters(note))
            .WithMessage("note must not contain control characters (tab and line breaks are fine).")
            .WithState(_ => new ProblemCode(SensitiveNumberDetector.InvalidCharactersCode));
        RuleFor(request => request.Note).Must(note => !SensitiveNumberDetector.ContainsSensitiveNumber(note!.Trim()))
            .When(request => request.Note is not null
                && request.Note.Trim().Length <= SensitiveNumberDetector.MaxScanLength
                && !SensitiveNumberDetector.ContainsInvalidCharacters(request.Note))
            .WithMessage("note must not contain a card or ID number.")
            .WithState(_ => new ProblemCode(SensitiveNumberDetector.Code));
    }

    private static bool IsResolution(string? toStatus) =>
        Enum.TryParse(toStatus, ignoreCase: true, out DisputeStatus status) && Enum.IsDefined(status) && DisputeLifecycle.IsResolution(status);
}
