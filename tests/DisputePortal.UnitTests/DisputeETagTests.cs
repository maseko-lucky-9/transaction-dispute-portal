using DisputePortal.Api.Features.Disputes;

namespace DisputePortal.UnitTests;

public sealed class DisputeETagTests
{
    [Fact]
    public void ForIsTheQuotedVersion() => Assert.Equal("\"42\"", DisputeETag.For(42));

    [Fact]
    public void ForIsCultureInvariant() => Assert.Equal("\"4294967295\"", DisputeETag.For(uint.MaxValue));

    [Fact]
    public void AStrongTagForTheCurrentVersionMatches() => Assert.True(DisputeETag.Matches("\"7\"", 7));

    [Fact]
    public void ATagListContainingTheCurrentVersionMatches() => Assert.True(DisputeETag.Matches("\"3\", \"7\"", 7));

    [Theory]
    [InlineData("\"8\"")]
    [InlineData("\"07\"")]
    [InlineData("7")]
    [InlineData("")]
    [InlineData("garbage")]
    public void AnotherOrMalformedTagDoesNotMatch(string ifMatch) => Assert.False(DisputeETag.Matches(ifMatch, 7));

    [Fact]
    public void AWeakTagNeverMatches() => Assert.False(DisputeETag.Matches("W/\"7\"", 7));

    [Fact]
    public void StarIsNotAccepted() => Assert.False(DisputeETag.Matches("*", 7));
}
