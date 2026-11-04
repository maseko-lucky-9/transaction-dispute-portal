using System.Text.RegularExpressions;

namespace DisputePortal.Domain.Disputes;

/// <summary>
/// Finds card and South African ID numbers in free text: 13 to 19 digits that pass the Luhn check, possibly written in
/// groups joined by single spaces, hyphens or line breaks. Any run of whole groups inside a longer chain counts, so a
/// leading reference number cannot hide a card behind it. The web form runs the same rules
/// (web/src/lib/validation-cases.json is the shared contract).
/// </summary>
public static partial class SensitiveNumberDetector
{
    public const string Code = "sensitive_data";

    private const int MinDigits = 13;
    private const int MaxDigits = 19;

    public static bool ContainsSensitiveNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (Match chain in DigitChain().Matches(text))
        {
            string[] groups = [.. DigitGroup().Matches(chain.Value).Select(group => group.Value)];
            if (AnyGroupRunIsSensitive(groups))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The Luhn (mod 10) check every payment card and SA ID number carries.</summary>
    public static bool PassesLuhn(string digits)
    {
        int sum = 0;
        bool doubleIt = false;
        for (int index = digits.Length - 1; index >= 0; index--)
        {
            int digit = digits[index] - '0';
            if (doubleIt)
            {
                digit = digit * 2 > 9 ? (digit * 2) - 9 : digit * 2;
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }

    private static bool AnyGroupRunIsSensitive(string[] groups)
    {
        for (int start = 0; start < groups.Length; start++)
        {
            string digits = string.Empty;
            for (int end = start; end < groups.Length && digits.Length + groups[end].Length <= MaxDigits; end++)
            {
                digits += groups[end];
                if (digits.Length >= MinDigits && PassesLuhn(digits))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ASCII digits only, joined by exactly one separator; the input is at most a few thousand characters.
    [GeneratedRegex(@"[0-9]+(?:(?:\r\n|[ \t\r\n-])[0-9]+)*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitChain();

    [GeneratedRegex("[0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitGroup();
}
