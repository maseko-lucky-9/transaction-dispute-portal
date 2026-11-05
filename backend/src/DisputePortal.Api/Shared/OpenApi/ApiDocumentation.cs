namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>
/// The built-in OpenAPI 3.1 document, generated at runtime from the endpoints (never at build), and a Swagger UI over
/// it. Both are mapped only when OpenApi:Enabled is true: off by default in every environment, so Production exposes
/// them only by an explicit flag (ADR-013). They live under /api so the web tier's existing /api/ proxy reaches them;
/// docs/openapi.json is the committed snapshot of the document, kept current by an integration test.
/// </summary>
public static class ApiDocumentation
{
    public const string EnabledSetting = "OpenApi:Enabled";
    public const string DocumentPath = "/api/openapi/v1.json";
    public const string UiPath = "/api/swagger";

    // MapOpenApi needs the {documentName} placeholder in its pattern; DocumentPath is that pattern for "v1".
    private const string DocumentPattern = "/api/openapi/{documentName}.json";

    // Swashbuckle's route prefix takes no leading slash; its routes are "api/swagger/{**path}".
    private const string UiRoutePrefix = "api/swagger";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi(options => options
            .AddDocumentTransformer<SessionSecurityTransformer>()
            .AddSchemaTransformer<ProblemSchemaTransformer>());

    // Anonymous, so the API can be read without a session. The routes it describes still need one.
    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        // Fails closed: only the text "true" turns it on; unset, empty or garbled stays off (and does not stop the host).
        if (bool.TryParse(app.Configuration[EnabledSetting], out bool enabled) && enabled)
        {
            app.MapOpenApi(DocumentPattern).AsApiDocumentation();
            // The UI only reads the document above; its own endpoint is not in it (OpenApiTests pins the snapshot).
            app.MapSwaggerUI(UiRoutePrefix, options => options.SwaggerEndpoint(DocumentPath, "Dispute Portal API"))
                .AsApiDocumentation();
        }

        return app;
    }
}
