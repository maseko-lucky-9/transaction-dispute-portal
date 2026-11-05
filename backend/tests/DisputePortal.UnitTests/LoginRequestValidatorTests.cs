using DisputePortal.Api.Features.Bff;

namespace DisputePortal.UnitTests;

public sealed class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("/")]
    [InlineData("/disputes?status=Submitted")]
    [InlineData("/transactions/123#top")]
    public void ALocalPathOrNoneIsAccepted(string? returnUrl) =>
        Assert.True(validator.Validate(new LoginRequest(returnUrl)).IsValid);

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("disputes")]
    [InlineData("")]
    [InlineData("/ok\r\nSet-Cookie: x=y")]
    [InlineData("javascript:alert(1)")]
    public void AnythingThatCouldLeaveTheSiteIsRefused(string returnUrl) =>
        Assert.False(validator.Validate(new LoginRequest(returnUrl)).IsValid);

    [Fact]
    public void AnOverlongReturnUrlIsRefused() =>
        Assert.False(validator.Validate(new LoginRequest("/" + new string('a', LoginRequestValidator.MaxReturnUrlLength))).IsValid);
}
