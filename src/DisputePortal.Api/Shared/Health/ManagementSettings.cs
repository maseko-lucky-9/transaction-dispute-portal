namespace DisputePortal.Api.Shared.Health;

/// <summary>The public port serves the API; the management port serves only the health probes and is never published.</summary>
public sealed class ManagementSettings
{
    public const string Section = "Ports";
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    public const string ReadyTag = "ready";

    public int Public { get; init; } = 8080;

    public int Management { get; init; } = 8081;
}
