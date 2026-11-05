using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Paging;
using DisputePortal.Api.Shared.Persistence;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The Disputes slice's read side: the history, a detail with its ETag, and the reason catalogue.</summary>
public sealed class DisputeQueries(
    IDisputeRepository disputes,
    IDisputeReasonRepository reasons,
    ICurrentCustomer currentCustomer,
    DisputeCursorProtector cursors,
    SecurityMetrics securityMetrics)
{
    public async Task<DisputeListResponse> ListAsync(DisputeListRequest request, CancellationToken cancellationToken)
    {
        Guid customerId = currentCustomer.CustomerId ?? throw new InvalidOperationException("The customer scope filter did not run.");
        DisputeListFilter filter = DisputeListFilter.Parse(request);
        DisputeKey? after = string.IsNullOrEmpty(request.Cursor) ? null : cursors.Unprotect(request.Cursor, customerId, filter);
        int limit = PageLimit.ParseOrDefault(request.Limit);
        // One extra row says whether a next page exists, without a count query.
        IReadOnlyList<DisputeListItem> rows = await disputes.ListAsync(filter, after, limit + 1, cancellationToken);
        string? nextCursor = rows.Count > limit
            ? cursors.Protect(new DisputeKey(rows[limit - 1].CreatedAt, rows[limit - 1].Id), customerId, filter)
            : null;
        return new DisputeListResponse([.. rows.Take(limit)], nextCursor);
    }

    /// <exception cref="NotFoundException">Missing or another customer's (counted as <see cref="SecurityMetrics.Bola404"/>).</exception>
    public async Task<VersionedDispute> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeNotFound.ToCustomer(securityMetrics);

    public Task<IReadOnlyList<DisputeReason>> ReasonsAsync(CancellationToken cancellationToken) => reasons.ListAsync(cancellationToken);
}
