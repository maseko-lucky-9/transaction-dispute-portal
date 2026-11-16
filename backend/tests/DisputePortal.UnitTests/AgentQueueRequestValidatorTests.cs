using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Paging;

namespace DisputePortal.UnitTests;

public sealed class AgentQueueRequestValidatorTests
{
    private readonly AgentQueueRequestValidator validator = new();

    [Fact]
    public void AnEmptyQueryIsValid() => Assert.True(validator.Validate(new AgentQueueRequest(null, null, null, null)).IsValid);

    [Theory]
    [InlineData("Submitted")]
    [InlineData("underreview")]
    [InlineData("WITHDRAWN")]
    public void AStatusIsCaseInsensitive(string status) => Assert.True(validator.Validate(new AgentQueueRequest(status, null, null, null)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("Open")]
    public void AnUnknownStatusIsAFieldError(string status) =>
        Assert.Contains("Status", validator.Validate(new AgentQueueRequest(status, null, null, null)).Errors.Select(error => error.PropertyName));

    [Theory]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void ALimitIs1To100(string limit, bool valid) =>
        Assert.Equal(valid, validator.Validate(new AgentQueueRequest(null, limit, null, null)).IsValid);

    [Fact]
    public void ACursorIsBounded()
    {
        Assert.True(validator.Validate(new AgentQueueRequest(null, null, new string('c', PageLimit.MaxCursorLength), null)).IsValid);
        Assert.False(validator.Validate(new AgentQueueRequest(null, null, new string('c', PageLimit.MaxCursorLength + 1), null)).IsValid);
    }

    [Theory]
    [InlineData("unclaimed")]
    [InlineData("Mine")]
    [InlineData("OTHERS")]
    public void AnAssignmentIsCaseInsensitive(string assignment) =>
        Assert.True(validator.Validate(new AgentQueueRequest(null, null, null, assignment)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("yours")]
    [InlineData("1")]
    [InlineData("mine,others")]
    public void AnUnknownAssignmentIsAFieldError(string assignment) =>
        Assert.Equal(["Assignment"], FailingFields(new AgentQueueRequest(null, null, null, assignment)));

    // An assignment fixes the status already, so both together are refused, under the assignment field.
    [Theory]
    [InlineData("Submitted", "unclaimed")]
    [InlineData("UnderReview", "mine")]
    [InlineData("Accepted", "others")]
    public void AnAssignmentWithAStatusIsAFieldErrorOnAssignment(string status, string assignment) =>
        Assert.Equal(["Assignment"], FailingFields(new AgentQueueRequest(status, null, null, assignment)));

    private List<string> FailingFields(AgentQueueRequest request) => [.. validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct()];
}
