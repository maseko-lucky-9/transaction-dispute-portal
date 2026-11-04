using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>Cursors for the dispute history: a <see cref="KeysetCursor"/> over (created_at, id) with its own purpose.</summary>
public sealed class DisputeCursorProtector(IDataProtectionProvider dataProtection)
{
    private readonly KeysetCursor cursor = new(dataProtection.CreateProtector("DisputePortal.Disputes.Cursor.v1"));

    public string Protect(DisputeKey last, Guid customerId, DisputeListFilter filter) =>
        cursor.Protect(last.CreatedAt, last.Id, KeysetCursor.ScopeOf(customerId), filter.Fingerprint());

    /// <exception cref="InvalidCursorException">Tampered, foreign, or issued for another filter.</exception>
    public DisputeKey Unprotect(string value, Guid customerId, DisputeListFilter filter)
    {
        (DateTimeOffset at, Guid id) = cursor.Unprotect(value, KeysetCursor.ScopeOf(customerId), filter.Fingerprint());
        return new DisputeKey(at, id);
    }
}
