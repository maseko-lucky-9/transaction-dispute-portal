namespace DisputePortal.Api.Shared.Health;

/// <summary>
/// The public port serves the API behind nginx. The management port is never published and nginx never proxies to it: it
/// serves only the health probes and Keycloak's back-channel logout (ADR-011).
/// </summary>
public sealed class ManagementSettings
{
    public const string Section = "Ports";
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    public const string ReadyTag = "ready";

    public int Public { get; init; } = 8080;

    public int Management { get; init; } = 8081;

    /// <summary>
    /// The name Keycloak uses to reach the management port (a compose alias on the data network, e.g. <c>api-mgmt</c>).
    /// Host filtering admits it so that call can arrive, and <see cref="ManagementPortMiddleware"/> refuses it on the public
    /// port, so the public listener still accepts only <c>AllowedHosts</c>. Unset: nothing changes.
    /// </summary>
    public string? InternalHost { get; init; }

    /// <summary>
    /// A CIDR (the compose data network). When set, the management port answers only loopback (the container's own health
    /// probe) and peers in this network, so a container that is only on the app network (nginx) cannot reach it.
    /// </summary>
    public string? InternalNetwork { get; init; }
}
