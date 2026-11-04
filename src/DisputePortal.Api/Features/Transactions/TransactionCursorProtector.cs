using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>Cursors for the transaction list: a <see cref="KeysetCursor"/> over (transaction_date, id) with its own purpose.</summary>
public sealed class TransactionCursorProtector(IDataProtectionProvider dataProtection)
{
    private readonly KeysetCursor cursor = new(dataProtection.CreateProtector("DisputePortal.Transactions.Cursor.v1"));

    public string Protect(TransactionKey last, Guid customerId, TransactionFilter filter) =>
        cursor.Protect(last.TransactionDate, last.Id, customerId, filter.Fingerprint());

    /// <exception cref="InvalidCursorException">Tampered, foreign, or issued for other filters.</exception>
    public TransactionKey Unprotect(string value, Guid customerId, TransactionFilter filter)
    {
        (DateTimeOffset at, Guid id) = cursor.Unprotect(value, customerId, filter.Fingerprint());
        return new TransactionKey(at, id);
    }
}
