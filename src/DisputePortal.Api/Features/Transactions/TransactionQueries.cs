using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
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
    /// <summary>A block reason outside the eligibility chain: an active, Accepted or Rejected dispute exists (a state, not a rule).</summary>
    public const string AlreadyDisputedCode = "already_disputed";

    public async Task<TransactionListResponse> ListAsync(TransactionListRequest request, CancellationToken cancellationToken)
    {
        Guid customerId = CustomerId();
        TransactionFilter filter = TransactionFilter.Parse(request);
        TransactionKey? after = string.IsNullOrEmpty(request.Cursor) ? null : cursors.Unprotect(request.Cursor, customerId, filter);
        int limit = PageLimit.ParseOrDefault(request.Limit);
        // One extra row says whether a next page exists, without a count query.
        IReadOnlyList<TransactionRow> rows = await repository.ListAsync(filter, after, limit + 1, cancellationToken);
        int activeDisputes = await repository.CountActiveDisputesAsync(cancellationToken);
        string? nextCursor = rows.Count > limit
            ? cursors.Protect(new TransactionKey(rows[limit - 1].TransactionDate, rows[limit - 1].Id), customerId, filter)
            : null;
        return new TransactionListResponse([.. rows.Take(limit).Select(row => ToItem(row, activeDisputes))], nextCursor);
    }

    /// <exception cref="NotFoundException">Missing or another customer's.</exception>
    public async Task<TransactionItem> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        TransactionRow row = await repository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("transaction_not_found", "No transaction with this id.");
        return ToItem(row, await repository.CountActiveDisputesAsync(cancellationToken));
    }

    // The customer scope filter always sets it before these endpoints run; reaching here without one is a wiring defect.
    private Guid CustomerId() =>
        currentCustomer.CustomerId ?? throw new InvalidOperationException("The customer scope filter did not run.");

    // The same chain a submission runs (minus the amount, which only the form knows), plus the state check, so the
    // flag predicts the answer a dispute would get.
    private TransactionItem ToItem(TransactionRow row, int activeDisputes)
    {
        TransactionSnapshot snapshot = new(row.Status, row.Direction, row.TransactionDate, Money.Of(row.Amount, row.Currency));
        DisputeEligibilityContext context = eligibility.ContextFor(snapshot) with
        {
            ActiveDisputeCount = activeDisputes,
            TransactionDisputeCount = row.DisputeCount,
        };
        bool alreadyDisputed = row.DisputeStatus is { } status && DisputeLifecycle.BlockingStatuses.Contains(status);
        string[] reasons = [.. alreadyDisputed ? [AlreadyDisputedCode] : Array.Empty<string>(), .. eligibility.Evaluate(context).Select(violation => violation.Code)];
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
            reasons.Length == 0,
            reasons,
            row.DisputeId is { } disputeId && row.DisputeStatus is { } disputeStatus ? new DisputeSummary(disputeId, disputeStatus) : null);
    }
}
