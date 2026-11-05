namespace DisputePortal.Api.Shared.Health;

public static class ManagementPortEndpointExtensions
{
    /// <summary>Serves the endpoint on the management port only (see <see cref="ManagementPortEndpoint"/>).</summary>
    public static TBuilder ServeOnManagementPortOnly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(ManagementPortEndpoint.Instance);
}
