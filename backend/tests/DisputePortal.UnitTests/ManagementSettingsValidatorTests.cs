using DisputePortal.Api.Shared.Health;
using Microsoft.Extensions.Options;

namespace DisputePortal.UnitTests;

/// <summary>An internal host name is only safe together with the network it may be used from; anything else stops start-up.</summary>
public sealed class ManagementSettingsValidatorTests
{
    private readonly ManagementSettingsValidator validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AnInternalHostWithoutAnInternalNetworkFails(string? network)
    {
        ValidateOptionsResult result = validator.Validate(null, new ManagementSettings { InternalHost = "api-mgmt", InternalNetwork = network });

        Assert.True(result.Failed);
        Assert.Equal(ManagementSettingsValidator.MissingNetwork, result.FailureMessage);
    }

    [Theory]
    [InlineData("10.231.28.0/33")]
    [InlineData("api-mgmt")]
    public void AMalformedInternalNetworkFails(string network)
    {
        ValidateOptionsResult result = validator.Validate(null, new ManagementSettings { InternalHost = "api-mgmt", InternalNetwork = network });

        Assert.True(result.Failed);
        Assert.Equal(ManagementSettingsValidator.MalformedNetwork, result.FailureMessage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "10.231.28.0/24")]
    [InlineData("api-mgmt", "10.231.28.0/24")]
    public void NeitherOrBothOrANetworkAlonePass(string? host, string? network) =>
        Assert.True(validator.Validate(null, new ManagementSettings { InternalHost = host, InternalNetwork = network }).Succeeded);
}
