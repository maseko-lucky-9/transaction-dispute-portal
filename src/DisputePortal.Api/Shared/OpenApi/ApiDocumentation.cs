namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>
/// The built-in OpenAPI 3.1 document, generated at runtime from the endpoints (never at build). Served only outside
/// Production; docs/openapi.json is its committed snapshot, kept current by an integration test.
/// </summary>
public static class ApiDocumentation
{
    public const string DocumentPath = "/openapi/v1.json";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer<SessionSecurityTransformer>());

    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }
}
