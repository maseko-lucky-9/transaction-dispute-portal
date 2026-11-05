using DisputePortal.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Disputes;

public sealed class DisputeReasonRepository(DisputePortalDbContext db) : IDisputeReasonRepository
{
    public async Task<IReadOnlyList<DisputeReason>> ListAsync(CancellationToken cancellationToken) =>
        await db.DisputeReasons.AsNoTracking()
            .OrderBy(reason => reason.SortOrder)
            .Select(reason => new DisputeReason(reason.Code, reason.Label, reason.Description))
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(string code, CancellationToken cancellationToken) =>
        db.DisputeReasons.AnyAsync(reason => reason.Code == code, cancellationToken);
}
