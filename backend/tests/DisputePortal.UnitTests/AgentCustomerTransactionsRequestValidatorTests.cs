using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Paging;

namespace DisputePortal.UnitTests;

public sealed class AgentCustomerTransactionsRequestValidatorTests
{
    private readonly AgentCustomerTransactionsRequestValidator validator = new();

    [Fact]
    public void AnEmptyQueryIsValid() => Assert.True(validator.Validate(new AgentCustomerTransactionsRequest(null, null)).IsValid);

    [Theory]
    [InlineData("1", true)]
    [InlineData("25", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void ALimitIs1To100(string limit, bool valid) =>
        Assert.Equal(valid, validator.Validate(new AgentCustomerTransactionsRequest(limit, null)).IsValid);

    [Fact]
    public void ABadLimitIsAFieldErrorNamedLimit() =>
        Assert.Equal(["Limit"], validator.Validate(new AgentCustomerTransactionsRequest("0", null)).Errors.Select(error => error.PropertyName));

    [Fact]
    public void ACursorIsBounded()
    {
        Assert.True(validator.Validate(new AgentCustomerTransactionsRequest(null, new string('c', PageLimit.MaxCursorLength))).IsValid);
        Assert.Equal(
            ["Cursor"],
            validator.Validate(new AgentCustomerTransactionsRequest(null, new string('c', PageLimit.MaxCursorLength + 1))).Errors.Select(error => error.PropertyName));
    }
}
