using System.Security.Claims;
using DisputePortal.Api.Features.Customers;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputePortal.Api.Features.Me;

public static class MeEndpoints
{
    public static WebApplication MapMeEndpoints(this WebApplication app)
    {
        app.MapGroup("/api/v1").WithTags("Me")
            .MapGet("/me", GetMeAsync)
            .WithSummary("Who the session belongs to, and the customer record it maps to.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return app;
    }

    private static async Task<Ok<MeResponse>> GetMeAsync(ClaimsPrincipal user, ICustomerRepository customers, CancellationToken cancellationToken)
    {
        string subject = SessionPrincipal.Subject(user);
        Customer? customer = await customers.GetBySubjectAsync(subject, cancellationToken);
        return TypedResults.Ok(new MeResponse(customer?.Id, subject, SessionPrincipal.RolesOf(user)));
    }
}
