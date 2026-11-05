using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Api.Shared.Errors;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.Api.Shared.Paging;

/// <summary>
/// The opaque cursor every keyset list uses (ADR-006): the last row's (timestamp, id), the scope it was issued to (the
/// customer, or the agent's subject) and a fingerprint of the filters, encrypted and authenticated by a purpose-specific
/// DataProtection protector. Every replica shares the key ring.
/// </summary>
public sealed class KeysetCursor(IDataProtector protector)
{
    private const char Separator = '|';
    private const string GuidFormat = "N";

    /// <summary>A short, stable hash of a filter set's canonical form.</summary>
    public static string Fingerprint(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)), 0, 16);

    /// <summary>The scope of a customer's cursor: their customer id.</summary>
    public static string ScopeOf(Guid customerId) => customerId.ToString(GuidFormat);

    /// <summary>The scope of a cursor bound to a signed-in subject (an agent): a hash, so no subject text sits in the cursor.</summary>
    public static string ScopeOf(string subject) => Fingerprint("sub:" + subject);

    public string Protect(DateTimeOffset at, Guid id, string scope, string fingerprint) =>
        protector.Protect(string.Join(
            Separator,
            at.UtcTicks.ToString(CultureInfo.InvariantCulture),
            id.ToString(GuidFormat),
            scope,
            fingerprint));

    /// <exception cref="InvalidCursorException">Tampered, foreign, or issued for other filters.</exception>
    public (DateTimeOffset At, Guid Id) Unprotect(string cursor, string scope, string fingerprint)
    {
        string[] parts = Open(cursor).Split(Separator);
        if (parts.Length != 4
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || !Guid.TryParseExact(parts[1], GuidFormat, out Guid id)
            || parts[2] != scope
            || parts[3] != fingerprint)
        {
            throw new InvalidCursorException();
        }

        return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
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
