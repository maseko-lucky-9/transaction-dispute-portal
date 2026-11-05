namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>
/// Endpoint metadata: the endpoint is a public, read-only documentation resource (the OpenAPI document or a file of the
/// Swagger UI). A browser cannot add <c>X-CSRF</c> to a page navigation or a script tag, so a GET or HEAD to such an
/// endpoint is exempt from the header rule (see <c>CsrfMiddleware</c>). Matched on the routed endpoint, not the raw path:
/// when the documentation is switched off nothing carries the marker, and /api/swagger and /api/openapi/v1.json meet the
/// same CSRF check as any other unknown /api path.
/// </summary>
public sealed class ApiDocumentationEndpoint
{
    public static readonly ApiDocumentationEndpoint Instance = new();

    private ApiDocumentationEndpoint()
    {
    }

    public static bool IsMarked(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<ApiDocumentationEndpoint>() is not null;
}
