using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The validated list filters. Dates are whole UTC days; <see cref="To"/> is inclusive.</summary>
public sealed record TransactionFilter(DateOnly? From, DateOnly? To, TransactionStatus? Status)
{
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The accepted date window. Wide enough for any real statement, narrow enough that no edge arithmetic overflows.</summary>
    public static readonly DateOnly EarliestDate = new(1900, 1, 1);

    public static readonly DateOnly LatestDate = new(2100, 12, 31);

    /// <summary>Parses a request that already passed <see cref="TransactionListRequestValidator"/>.</summary>
    public static TransactionFilter Parse(TransactionListRequest request) => new(
        ParseDate(request.From),
        ParseDate(request.To),
        request.Status is null ? null : Enum.Parse<TransactionStatus>(request.Status, ignoreCase: true));

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public DateTimeOffset? FromInclusive => From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    /// <summary>The instant after the last included day. Safe on its own: the last representable day has no upper bound to apply.</summary>
    public DateTimeOffset? ToExclusive => To is { } to && to < DateOnly.MaxValue
        ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        : null;

    /// <summary>Binds a cursor to the filters it was issued for: a cursor reused with other filters is refused.</summary>
    public string Fingerprint()
    {
        string canonical = string.Create(CultureInfo.InvariantCulture, $"from={From:yyyy-MM-dd};to={To:yyyy-MM-dd};status={Status}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)), 0, 16);
    }

    private static DateOnly? ParseDate(string? value) => TryParseDate(value, out DateOnly date) ? date : null;
}
