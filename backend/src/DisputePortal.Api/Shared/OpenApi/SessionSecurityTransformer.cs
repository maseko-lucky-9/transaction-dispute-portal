using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>Describes the BFF session cookie and the CSRF header, and drops the server list (the origin is nginx's, not ours).</summary>
public sealed class SessionSecurityTransformer : IOpenApiDocumentTransformer
{
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
            Description = "HttpOnly session cookie set by GET /bff/login (named __Host-dp_session over https).",
        };
        document.Components.SecuritySchemes["csrf"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = CsrfMiddleware.HeaderName,
            Description = "Must be 1 on every /api request and every non-GET /bff request.",
        };
        return Task.CompletedTask;
    }
}
