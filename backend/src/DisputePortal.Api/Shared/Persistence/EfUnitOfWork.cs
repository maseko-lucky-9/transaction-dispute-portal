using System.Data;
using DisputePortal.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisputePortal.Api.Shared.Persistence;

public sealed class EfUnitOfWork(DisputePortalDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => TranslatedAsync(() => db.SaveChangesAsync(cancellationToken));

    // A user transaction under EnableRetryOnFailure must run inside the execution strategy. Refusals are translated only
    // once it has given up, so a transient failure inside still reaches it as itself and is retried.
    public Task<T> SaveInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        TranslatedAsync(() => db.Database.CreateExecutionStrategy().ExecuteAsync(work, AttemptAsync, cancellationToken));

    // Each attempt starts clean: a retry must not find the entities a failed attempt queued. READ COMMITTED gives each
    // statement a fresh snapshot, so a read after a lock wait sees what the previous holder committed.
    private async Task<T> AttemptAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        T result = await work(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<T> TranslatedAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateException exception) when (DbErrorTranslator.Translate(exception) is { } refusal)
        {
            throw refusal;
        }
    }
}
