using System.Globalization;
using System.Security.Cryptography;
using DisputePortal.Api.Shared.Errors;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>
/// Opaque, integrity-protected cursors (ADR-006): the last row's (date, id), the customer and the filter fingerprint,
/// encrypted and authenticated with a purpose-specific DataProtection key. Every replica shares the key ring.
/// </summary>
public sealed class TransactionCursorProtector(IDataProtectionProvider dataProtection)
{
    private const char Separator = '|';
    private const string GuidFormat = "N";

    private readonly IDataProtector protector = dataProtection.CreateProtector("DisputePortal.Transactions.Cursor.v1");

    public string Protect(TransactionKey last, Guid customerId, TransactionFilter filter) =>
        protector.Protect(string.Join(
            Separator,
            last.TransactionDate.UtcTicks.ToString(CultureInfo.InvariantCulture),
            last.Id.ToString(GuidFormat),
            customerId.ToString(GuidFormat),
            filter.Fingerprint()));

    /// <exception cref="InvalidCursorException">Tampered, foreign, or issued for other filters.</exception>
    public TransactionKey Unprotect(string cursor, Guid customerId, TransactionFilter filter)
    {
        string[] parts = Open(cursor).Split(Separator);
        if (parts.Length != 4
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || !Guid.TryParseExact(parts[1], GuidFormat, out Guid id)
            || parts[2] != customerId.ToString(GuidFormat)
            || parts[3] != filter.Fingerprint())
        {
            throw new InvalidCursorException();
        }

        return new TransactionKey(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    private string Open(string cursor)
    {
        try
        {
            return protector.Unprotect(cursor);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new InvalidCursorException();
        }
    }
}
