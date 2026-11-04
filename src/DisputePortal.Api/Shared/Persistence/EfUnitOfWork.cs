using DisputePortal.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Shared.Persistence;

public sealed class EfUnitOfWork(DisputePortalDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbErrorTranslator.Translate(exception) is { } refusal)
        {
            throw refusal;
        }
    }
}
