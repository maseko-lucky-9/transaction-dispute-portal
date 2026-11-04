using System.Globalization;

namespace DisputePortal.Api.Shared.Paging;

/// <summary>The page-size and cursor rules every keyset list shares (ADR-006).</summary>
public static class PageLimit
{
    public const int Max = 100;
    public const int Default = 25;
    public const int MaxCursorLength = 1024;

    public static readonly string Message = $"limit must be a whole number between 1 and {Max}.";

    /// <summary>Digits only (no sign, spaces, exponent or hex), within 1 to <see cref="Max"/>.</summary>
    public static bool TryParse(string? value, out int limit) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) && limit is >= 1 and <= Max;

    /// <summary>The validated limit, or <see cref="Default"/> when absent.</summary>
    public static int ParseOrDefault(string? value) => TryParse(value, out int limit) ? limit : Default;
}
