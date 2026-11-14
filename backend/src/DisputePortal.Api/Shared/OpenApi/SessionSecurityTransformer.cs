using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>
/// Describes the BFF session cookie and the CSRF header, requires the CSRF header on every /api operation so Swagger UI
/// sends it after Authorize, and drops the server list (the origin is nginx's, not ours). The cookie is described but
/// not attached: the browser sends an HttpOnly cookie by itself and Swagger UI does not honour an apiKey-in-cookie.
/// </summary>
public sealed class SessionSecurityTransformer : IOpenApiDocumentTransformer
{
    private const string ApiPathPrefix = "/api/";
    private const string CsrfSchemeName = "csrf";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Transactions Dispute Portal API";
        document.Info.Version = "v1";
        document.Servers?.Clear();
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["session"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = BffAuthentication.CookieName,
            Description = "HttpOnly session cookie set by GET /bff/login (named __Host-dp_session over https). Informational: the browser sends it by itself once you have signed in at /bff/login in the same browser, so Authorize has nothing to enter for it.",
        };
        document.Components.SecuritySchemes[CsrfSchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = CsrfMiddleware.HeaderName,
            Description = "Must be 1 on every /api request and every non-GET /bff request.",
        };
        RequireCsrfOnApiOperations(document);
        return Task.CompletedTask;
    }

    private static void RequireCsrfOnApiOperations(OpenApiDocument document)
    {
        foreach ((string path, IOpenApiPathItem item) in document.Paths)
        {
            if (!path.StartsWith(ApiPathPrefix, StringComparison.Ordinal) || item.Operations is null)
            {
                continue;
            }

            foreach (OpenApiOperation operation in item.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(CsrfSchemeName, document)] = [],
                });
            }
        }
    }
}
