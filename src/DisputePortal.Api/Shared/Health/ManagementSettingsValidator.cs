using System.Net;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Health;

/// <summary>
/// Checked at start-up (ValidateOnStart): an internal host name without an internal network would make the management
/// port accept that name from every network the API is on, so the app refuses to start rather than run half-configured.
/// </summary>
public sealed class ManagementSettingsValidator : IValidateOptions<ManagementSettings>
{
    public const string MissingNetwork =
        "Ports:InternalHost is set but Ports:InternalNetwork is not. Set Ports:InternalNetwork to the CIDR the identity "
        + "provider calls from (compose: DATA_SUBNET), or unset Ports:InternalHost.";

    public const string MalformedNetwork = "Ports:InternalNetwork is not a CIDR network (for example 10.231.28.0/24).";

    public ValidateOptionsResult Validate(string? name, ManagementSettings options)
    {
        bool hasNetwork = !string.IsNullOrWhiteSpace(options.InternalNetwork);
        if (!string.IsNullOrWhiteSpace(options.InternalHost) && !hasNetwork)
        {
            return ValidateOptionsResult.Fail(MissingNetwork);
        }

        return hasNetwork && !IPNetwork.TryParse(options.InternalNetwork, out _)
            ? ValidateOptionsResult.Fail(MalformedNetwork)
            : ValidateOptionsResult.Success;
    }
}
