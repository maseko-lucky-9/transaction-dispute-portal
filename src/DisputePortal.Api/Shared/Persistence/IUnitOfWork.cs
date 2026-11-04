namespace DisputePortal.Api.Shared.Persistence;

/// <summary>Commits the request's changes in one transaction; database refusals arrive as <see cref="Errors.ApiException"/>.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
