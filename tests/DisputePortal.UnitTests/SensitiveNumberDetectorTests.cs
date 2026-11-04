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

    // The same 22 cases the web form runs: both sides refuse exactly the same descriptions.
    [Theory]
    [MemberData(nameof(SharedCases))]
    public void AgreesWithTheWebFormOnEverySharedCase(string name, string input, bool valid, string? code)
    {
        Assert.Equal(!valid, SensitiveNumberDetector.ContainsSensitiveNumber(input));
        Assert.Equal(valid ? null : SensitiveNumberDetector.Code, code);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void TheSharedCasesFileHasAll22Cases() => Assert.Equal(22, SharedCases().Count);

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

    [Theory]
    [InlineData("4111  1111 1111 1111")]
    [InlineData("4111_1111_1111_1111")]
    [InlineData("4111.1111.1111.1111")]
    public void OnlyASingleSpaceHyphenOrLineBreakJoinsGroups(string text) => Assert.False(SensitiveNumberDetector.ContainsSensitiveNumber(text));

    // Arabic-Indic digits are not ASCII digits; the web's \d agrees.
    [Fact]
    public void OnlyAsciiDigitsCount() => Assert.False(SensitiveNumberDetector.ContainsSensitiveNumber("٤١١١١١١١١١١١١١١١"));

    private static string LuhnValid(int length)
    {
        string body = string.Concat(Enumerable.Range(0, length - 1).Select(index => (char)('1' + (index % 9))));
        return Enumerable.Range(0, 10).Select(check => body + check).First(SensitiveNumberDetector.PassesLuhn);
    }

    private static string BreakLuhn(string number) => number[..^1] + (char)('0' + ((number[^1] - '0' + 1) % 10));
}
