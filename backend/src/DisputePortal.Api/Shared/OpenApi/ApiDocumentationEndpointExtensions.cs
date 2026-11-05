namespace DisputePortal.Api.Shared.OpenApi;

public static class ApiDocumentationEndpointExtensions
{
    /// <summary>Anonymous, and a documentation resource for the CSRF rule (see <see cref="ApiDocumentationEndpoint"/>).</summary>
    public static TBuilder AsApiDocumentation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AllowAnonymous().WithMetadata(ApiDocumentationEndpoint.Instance);
}
