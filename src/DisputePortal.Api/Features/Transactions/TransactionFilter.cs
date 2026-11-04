using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The validated list filters. Dates are whole UTC days; <see cref="To"/> is inclusive.</summary>
public sealed record TransactionFilter(DateOnly? From, DateOnly? To, TransactionStatus? Status)
{
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Parses a request that already passed <see cref="TransactionListRequestValidator"/>.</summary>
    public static TransactionFilter Parse(TransactionListRequest request) => new(
        ParseDate(request.From),
        ParseDate(request.To),
        request.Status is null ? null : Enum.Parse<TransactionStatus>(request.Status, ignoreCase: true));

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public DateTimeOffset? FromInclusive => From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    public DateTimeOffset? ToExclusive => To is { } to ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    /// <summary>Binds a cursor to the filters it was issued for: a cursor reused with other filters is refused.</summary>
    public string Fingerprint()
    {
        string canonical = string.Create(CultureInfo.InvariantCulture, $"from={From:yyyy-MM-dd};to={To:yyyy-MM-dd};status={Status}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)), 0, 16);
    }

    private static DateOnly? ParseDate(string? value) => TryParseDate(value, out DateOnly date) ? date : null;
}
