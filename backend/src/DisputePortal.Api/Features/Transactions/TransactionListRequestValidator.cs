using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Transactions;
using FluentValidation;

namespace DisputePortal.Api.Features.Transactions;

public sealed class TransactionListRequestValidator : AbstractValidator<TransactionListRequest>
{
    public const int MaxRangeDays = 366;

    public TransactionListRequestValidator()
    {
        RuleFor(request => request.Limit).Must(limit => PageLimit.TryParse(limit, out _)).When(request => request.Limit is not null)
            .WithMessage(PageLimit.Message);
        RuleFor(request => request.From).Must(BeADate).When(request => request.From is not null).WithMessage(DateMessage("from"));
        RuleFor(request => request.To).Must(BeADate).When(request => request.To is not null).WithMessage(DateMessage("to"));
        RuleFor(request => request.To).Must((request, to) => Span(request) >= 0).When(BothDatesValid)
            .WithMessage("to must not be before from.");
        RuleFor(request => request.To).Must((request, to) => Span(request) <= MaxRangeDays).When(BothDatesValid)
            .WithMessage($"The date range must not exceed {MaxRangeDays} days.");
        RuleFor(request => request.Status).IsEnumName(typeof(TransactionStatus), caseSensitive: false).When(request => request.Status is not null)
            .WithMessage("status must be Pending, Posted or Reversed.");
        RuleFor(request => request.Cursor).MaximumLength(PageLimit.MaxCursorLength);
    }

    private static string DateMessage(string field) =>
        $"{field} must be a date in yyyy-MM-dd form between {TransactionFilter.EarliestDate:yyyy-MM-dd} and {TransactionFilter.LatestDate:yyyy-MM-dd}.";

    private static bool BeADate(string? value) =>
        TransactionFilter.TryParseDate(value, out DateOnly date) && date >= TransactionFilter.EarliestDate && date <= TransactionFilter.LatestDate;

    private static bool BothDatesValid(TransactionListRequest request) => BeADate(request.From) && BeADate(request.To);

    private static int Span(TransactionListRequest request)
    {
        TransactionFilter.TryParseDate(request.From, out DateOnly from);
        TransactionFilter.TryParseDate(request.To, out DateOnly to);
        return to.DayNumber - from.DayNumber;
    }
}
