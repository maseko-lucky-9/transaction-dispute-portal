using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DisputePortal.Domain.Disputes;

/// <summary>
/// The free-text rules a dispute description must pass, identical on the web form
/// (web/src/lib/validation-cases.json is the shared contract):
/// <list type="number">
/// <item><b>invalid_characters</b>: any control character (Unicode Cc) other than tab, line feed and carriage return.
/// Checked first; a NUL would otherwise reach PostgreSQL, which refuses it.</item>
/// <item><b>sensitive_data</b>: a card or South African ID number. The text is normalised first: every format character
/// (Unicode Cf: zero-width space and joiner, BOM, soft hyphen...) is removed, and every decimal digit of any script
/// (Unicode Nd: fullwidth, Arabic-Indic, Devanagari...) is mapped to ASCII. A chain is digit groups joined by runs of 1
/// to 4 separators (whitespace, dashes, <c>. , / _ ( )</c>); any run of whole groups inside a chain with 13 to 19 digits
/// that passes the Luhn check is sensitive, so a leading reference number cannot hide a card behind it.</item>
/// </list>
/// Known limit: this deters ACCIDENTAL disclosure, not deliberate obfuscation by the customer about their own number.
/// Not folded: circled, superscript and subscript digits (Unicode No); fullwidth or other compatibility punctuation used
/// as a separator; five or more separators; words between groups; and invisible code points that are not format
/// characters (variation selectors, U+034F, fillers such as U+115F, U+3164, U+FFA0, the braille blank U+2800). The README
/// lists it. There is deliberately no compatibility normalisation (NFKC): the API ships with invariant globalization,
/// where it does nothing, so only .NET's built-in Unicode tables are used and the rule behaves the same in production,
/// in the tests and on the web form. Treating every non-alphanumeric as a separator would flag ordinary text, and the
/// same person could spell the digits out, so the rule stays a guard against accidents.
/// </summary>
public static partial class SensitiveNumberDetector
{
    public const string Code = "sensitive_data";
    public const string InvalidCharactersCode = "invalid_characters";

    /// <summary>The longest text <see cref="ContainsSensitiveNumber"/> scans; callers bound their input (a description is at most 2000).</summary>
    public const int MaxScanLength = 10_000;

    private const int MinDigits = 13;
    private const int MaxDigits = 19;

    /// <summary>The problem code the text earns, in rule order: invalid_characters, then sensitive_data; null when it is fine.</summary>
    public static string? ProblemCodeFor(string? text) =>
        ContainsInvalidCharacters(text) ? InvalidCharactersCode
        : ContainsSensitiveNumber(text) ? Code
        : null;

    /// <summary>True for any Unicode Cc character other than tab, line feed and carriage return.</summary>
    public static bool ContainsInvalidCharacters(string? text) =>
        text is not null && text.Any(character => char.IsControl(character) && character is not ('\t' or '\n' or '\r'));

    /// <exception cref="ArgumentOutOfRangeException">The text is longer than <see cref="MaxScanLength"/>.</exception>
    public static bool ContainsSensitiveNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(text.Length, MaxScanLength, nameof(text));
        foreach (Match chain in DigitChain().Matches(Normalise(text)))
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

    // Drop format characters, map decimal digits of any script to ASCII. Runes, so a character outside the BMP (a
    // surrogate pair) is judged whole; a lone surrogate enumerates as U+FFFD and is kept as it is.
    private static string Normalise(string text)
    {
        StringBuilder normalised = new(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Format)
            {
                continue;
            }

            if (category == UnicodeCategory.DecimalDigitNumber)
            {
                normalised.Append((char)('0' + CharUnicodeInfo.GetDecimalDigitValue(rune.ToString(), 0)));
                continue;
            }

            normalised.Append(rune.ToString());
        }

        return normalised.ToString();
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

    // Linear: the separator class holds no digit, so every boundary is unambiguous. Runs on normalised text (ASCII digits).
    [GeneratedRegex(@"[0-9]+(?:[\s\p{Pd}.,/_()]{1,4}[0-9]+)*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitChain();

    [GeneratedRegex("[0-9]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitGroup();
}
