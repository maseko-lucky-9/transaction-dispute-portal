using System.Text.Json;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.UnitTests;

/// <summary>Card and SA ID numbers in free text: parity with the web form's shared cases, then the Luhn boundaries.</summary>
public sealed class SensitiveNumberDetectorTests
{
    public static TheoryData<string, string, bool, string?> SharedCases()
    {
        using JsonDocument cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "validation-cases.json")));
        TheoryData<string, string, bool, string?> data = [];
        foreach (JsonElement item in cases.RootElement.EnumerateArray())
        {
            data.Add(
                item.GetProperty("name").GetString()!,
                item.GetProperty("input").GetString()!,
                item.GetProperty("valid").GetBoolean(),
                item.GetProperty("code").GetString());
        }

        return data;
    }

    // Every case the web form runs, both rules: the two sides refuse exactly the same descriptions with the same code.
    [Theory]
    [MemberData(nameof(SharedCases))]
    public void AgreesWithTheWebFormOnEverySharedCase(string name, string input, bool valid, string? code)
    {
        Assert.Equal(code, SensitiveNumberDetector.ProblemCodeFor(input));
        Assert.Equal(valid, code is null);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void TheSharedCasesFileHasAll45CasesCoveringBothRules()
    {
        TheoryData<string, string, bool, string?> cases = SharedCases();

        Assert.Equal(45, cases.Count);
        Assert.Contains(cases, row => (string?)row[3] == SensitiveNumberDetector.InvalidCharactersCode);
        Assert.Contains(cases, row => (string?)row[3] == SensitiveNumberDetector.Code);
    }

    // Rule A: control characters other than tab, line feed and carriage return; checked before rule B.
    [Theory]
    [InlineData("tab\there", null)]
    [InlineData("lines\r\nhere", null)]
    [InlineData("nul\0here", SensitiveNumberDetector.InvalidCharactersCode)]
    [InlineData("delete\u007Fhere", SensitiveNumberDetector.InvalidCharactersCode)]
    [InlineData("c1\u0085here", SensitiveNumberDetector.InvalidCharactersCode)]
    [InlineData("4111 1111 1111 1111\u0001", SensitiveNumberDetector.InvalidCharactersCode)]
    public void ControlCharactersAreInvalidAndCheckedFirst(string text, string? code) =>
        Assert.Equal(code, SensitiveNumberDetector.ProblemCodeFor(text));

    // Only 13 to 19 digits count, whatever the Luhn result.
    [Theory]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(16, true)]
    [InlineData(19, true)]
    [InlineData(20, false)]
    public void OnlyLuhnValidRunsOf13To19DigitsAreSensitive(int length, bool sensitive)
    {
        string number = LuhnValid(length);

        Assert.True(SensitiveNumberDetector.PassesLuhn(number));
        Assert.Equal(sensitive, SensitiveNumberDetector.ContainsSensitiveNumber(number));
        Assert.Equal(sensitive, SensitiveNumberDetector.ContainsSensitiveNumber($"See ref {number}, thanks."));
        Assert.False(SensitiveNumberDetector.ContainsSensitiveNumber(BreakLuhn(number)));
    }

    // A property over many seeded random runs: for one unbroken run of digits, the detector is exactly "13-19 digits and Luhn".
    [Fact]
    public void ForASingleRunTheDetectorIsExactlyLengthAndLuhn()
    {
        Random random = new(20261104);
        for (int sample = 0; sample < 2000; sample++)
        {
            string digits = string.Concat(Enumerable.Range(0, random.Next(1, 24)).Select(_ => (char)('0' + random.Next(10))));
            bool expected = digits.Length is >= 13 and <= 19 && SensitiveNumberDetector.PassesLuhn(digits);

            Assert.Equal(expected, SensitiveNumberDetector.ContainsSensitiveNumber("x " + digits + " y"));
        }
    }

    [Theory]
    [InlineData("79927398713", true)]
    [InlineData("79927398710", false)]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    public void LuhnMatchesKnownVectors(string digits, bool valid) => Assert.Equal(valid, SensitiveNumberDetector.PassesLuhn(digits));

    // Runs of 1 to 4 separators join groups (two spaces used to slip through); five end the chain.
    [Theory]
    [InlineData("4111  1111 1111 1111", true)]
    [InlineData("4111_1111_1111_1111", true)]
    [InlineData("4111.1111.1111.1111", true)]
    [InlineData("4111 -/ 1111(1111)1111", true)]
    [InlineData("4111    1111 1111 1111", true)]
    [InlineData("4111     1111 1111 1111", false)]
    [InlineData("4111 : 1111 1111 1111", false)]
    public void UpToFourSeparatorsJoinGroups(string text, bool sensitive) => Assert.Equal(sensitive, SensitiveNumberDetector.ContainsSensitiveNumber(text));

    // Digits of any script count after normalisation, and invisible characters cannot split a number.
    [Theory]
    [InlineData("٤١١١١١١١١١١١١١١١")]
    [InlineData("४१११११११११११११११")]
    [InlineData("\U0001D7D2\U0001D7CF\U0001D7CF\U0001D7CF 1111 1111 1111")]
    [InlineData("4111\u00AD1111\uFEFF1111\u200D1111")]
    [InlineData("\u2074\u00B9\u00B9\u00B9 \u2460\u2460\u2460\u2460 1111 1111")]
    [InlineData("4111 1111 1111 111\U000E00311")]
    public void LookAlikeDigitsAndInvisibleCharactersCannotHideACard(string text) =>
        Assert.True(SensitiveNumberDetector.ContainsSensitiveNumber(text));

    [Fact]
    public void TheScanIsBoundedToMaxScanLength()
    {
        Assert.False(SensitiveNumberDetector.ContainsSensitiveNumber(new string('9', SensitiveNumberDetector.MaxScanLength)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveNumberDetector.ContainsSensitiveNumber(new string('a', SensitiveNumberDetector.MaxScanLength + 1)));
    }

    private static string LuhnValid(int length)
    {
        string body = string.Concat(Enumerable.Range(0, length - 1).Select(index => (char)('1' + (index % 9))));
        return Enumerable.Range(0, 10).Select(check => body + check).First(SensitiveNumberDetector.PassesLuhn);
    }

    private static string BreakLuhn(string number) => number[..^1] + (char)('0' + ((number[^1] - '0' + 1) % 10));
}
