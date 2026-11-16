using System.Data;
using DisputePortal.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisputePortal.Api.Shared.Persistence;

public sealed class EfUnitOfWork(DisputePortalDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => TranslatedAsync(() => db.SaveChangesAsync(cancellationToken));

    public Task<T> SaveInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        SaveInTransactionAsync(work, (result, _) => Task.FromResult(result), cancellationToken);

    // A user transaction under EnableRetryOnFailure must run inside the execution strategy. Refusals are translated only
    // once it has given up, so a transient failure inside still reaches it as itself and is retried.
    public Task<TResult> SaveInTransactionAsync<TWork, TResult>(
        Func<CancellationToken, Task<TWork>> work, Func<TWork, CancellationToken, Task<TResult>> afterSave, CancellationToken cancellationToken) =>
        TranslatedAsync(() => db.Database.CreateExecutionStrategy().ExecuteAsync(
            (work, afterSave), (steps, token) => AttemptAsync(steps.work, steps.afterSave, token), cancellationToken));

    // Each attempt starts clean: a retry must not find the entities a failed attempt queued. READ COMMITTED gives each
    // statement a fresh snapshot, so a read after a lock wait sees what the previous holder committed.
    private async Task<TResult> AttemptAsync<TWork, TResult>(
        Func<CancellationToken, Task<TWork>> work, Func<TWork, CancellationToken, Task<TResult>> afterSave, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        TWork result = await work(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        TResult answer = await afterSave(result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return answer;
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
