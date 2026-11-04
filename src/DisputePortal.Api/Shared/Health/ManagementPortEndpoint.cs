namespace DisputePortal.Api.Shared.Health;

/// <summary>
/// Endpoint metadata: the endpoint is served only on the management port. <see cref="ManagementPortMiddleware"/> matches
/// the routed endpoint, not the raw path, so a variant of the path (case, trailing slash, encoding) that still routes to it
/// is refused on the public port all the same.
/// </summary>
public sealed class ManagementPortEndpoint
{
    public static readonly ManagementPortEndpoint Instance = new();

    private ManagementPortEndpoint()
    {
    }

    public static bool IsMarked(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<ManagementPortEndpoint>() is not null;
}
