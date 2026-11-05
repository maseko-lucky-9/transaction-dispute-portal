using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Paging;

namespace DisputePortal.UnitTests;

public sealed class AgentQueueRequestValidatorTests
{
    private readonly AgentQueueRequestValidator validator = new();

    [Fact]
    public void AnEmptyQueryIsValid() => Assert.True(validator.Validate(new AgentQueueRequest(null, null, null)).IsValid);

    [Theory]
    [InlineData("Submitted")]
    [InlineData("underreview")]
    [InlineData("WITHDRAWN")]
    public void AStatusIsCaseInsensitive(string status) => Assert.True(validator.Validate(new AgentQueueRequest(status, null, null)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("Open")]
    public void AnUnknownStatusIsAFieldError(string status) =>
        Assert.Contains("Status", validator.Validate(new AgentQueueRequest(status, null, null)).Errors.Select(error => error.PropertyName));

    [Theory]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void ALimitIs1To100(string limit, bool valid) =>
        Assert.Equal(valid, validator.Validate(new AgentQueueRequest(null, limit, null)).IsValid);

    [Fact]
    public void ACursorIsBounded()
    {
        Assert.True(validator.Validate(new AgentQueueRequest(null, null, new string('c', PageLimit.MaxCursorLength))).IsValid);
        Assert.False(validator.Validate(new AgentQueueRequest(null, null, new string('c', PageLimit.MaxCursorLength + 1))).IsValid);
    }
}
