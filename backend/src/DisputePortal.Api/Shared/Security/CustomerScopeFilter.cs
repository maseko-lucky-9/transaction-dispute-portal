using DisputePortal.Api.Features.Customers;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Persistence;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// Runs after the Customer policy: maps the session's subject to its Customer row and sets the request's
/// <see cref="CurrentCustomer"/>, which the Owner query filter reads. A customer-role user with no row is refused
/// (403 customer_not_provisioned) rather than shown an empty account.
/// </summary>
public sealed class CustomerScopeFilter : IEndpointFilter
{
    public const string NotProvisionedCode = "customer_not_provisioned";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Resolved per request: the filter instance lives as long as the endpoint, the scoped services do not.
        IServiceProvider services = context.HttpContext.RequestServices;
        Customer? customer = await services.GetRequiredService<ICustomerRepository>()
            .GetBySubjectAsync(SessionPrincipal.Subject(context.HttpContext.User), context.HttpContext.RequestAborted);
        if (customer is null)
        {
            return ProblemResponses.Result(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Detail = "This account is not linked to a customer record.",
                Extensions = { ["code"] = NotProvisionedCode },
            });
        }

        services.GetRequiredService<CurrentCustomer>().Set(customer.Id);
        return await next(context);
    }
}
