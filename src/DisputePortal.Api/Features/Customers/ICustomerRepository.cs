namespace DisputePortal.Api.Features.Customers;

/// <summary>Aggregate-specific repository (no generic repository); the seam for unit tests.</summary>
public interface ICustomerRepository
{
    Task<Customer?> GetBySubjectAsync(string keycloakSubject, CancellationToken cancellationToken);
}
