using DisputePortal.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Customers;

public sealed class CustomerRepository(DisputePortalDbContext db) : ICustomerRepository
{
    // A read: no tracking, so a query never leaves state for a later SaveChanges.
    public Task<Customer?> GetBySubjectAsync(string keycloakSubject, CancellationToken cancellationToken) =>
        db.Customers.AsNoTracking().SingleOrDefaultAsync(customer => customer.KeycloakSubject == keycloakSubject, cancellationToken);
}
