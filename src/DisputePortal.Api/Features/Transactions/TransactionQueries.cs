using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Eligibility;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The Transactions slice's read side: repository rows to read models, eligibility flags and cursors.</summary>
public sealed class TransactionQueries(
    ITransactionRepository repository,
    ICurrentCustomer currentCustomer,
    TransactionCursorProtector cursors,
    DisputeEligibilityPolicy eligibility)
{
    public const int DefaultLimit = 25;

    public async Task<TransactionListResponse> ListAsync(TransactionListRequest request, CancellationToken cancellationToken)
    {
        Guid customerId = CustomerId();
        TransactionFilter filter = TransactionFilter.Parse(request);
        TransactionKey? after = string.IsNullOrEmpty(request.Cursor) ? null : cursors.Unprotect(request.Cursor, customerId, filter);
        int limit = TransactionListRequestValidator.TryParseLimit(request.Limit, out int requested) ? requested : DefaultLimit;
        // One extra row says whether a next page exists, without a count query.
        IReadOnlyList<TransactionRow> rows = await repository.ListAsync(filter, after, limit + 1, cancellationToken);
        string? nextCursor = rows.Count > limit
            ? cursors.Protect(new TransactionKey(rows[limit - 1].TransactionDate, rows[limit - 1].Id), customerId, filter)
            : null;
        return new TransactionListResponse([.. rows.Take(limit).Select(ToItem)], nextCursor);
    }

    /// <exception cref="NotFoundException">Missing or another customer's.</exception>
    public async Task<TransactionItem> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, cancellationToken) is { } row
            ? ToItem(row)
            : throw new NotFoundException("transaction_not_found", "No transaction with this id.");

    // The customer scope filter always sets it before these endpoints run; reaching here without one is a wiring defect.
    private Guid CustomerId() =>
        currentCustomer.CustomerId ?? throw new InvalidOperationException("The customer scope filter did not run.");

    private TransactionItem ToItem(TransactionRow row)
    {
        IReadOnlyList<EligibilityViolation> violations = eligibility.Evaluate(new TransactionSnapshot(row.Status, row.Direction, row.TransactionDate));
        return new TransactionItem(
            row.Id,
            row.Reference,
            row.MerchantName,
            row.Category,
            Money.FormatAmount(row.Amount),
            row.Currency,
            row.Direction,
            row.Status,
            row.TransactionDate,
            row.PostedAt,
            new TransactionAccount(row.MaskedNumber
                ?? throw new InvalidOperationException($"Transaction {row.Id} has no account owned by its customer.")),
            violations.Count == 0,
            [.. violations.Select(violation => violation.Code)],
            Dispute: null);
    }
}
