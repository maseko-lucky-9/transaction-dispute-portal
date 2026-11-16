namespace DisputePortal.Api.Shared.Persistence;

/// <summary>Commits the request's changes in one transaction; database refusals arrive as <see cref="Errors.ApiException"/>.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> and saves what it queued in one READ COMMITTED transaction (ADR-014). On a transient
    /// failure the whole attempt runs again, reads included, from an empty change tracker, so every read the work decides
    /// on belongs inside it. Refusals are translated as by <see cref="SaveChangesAsync"/>, after the rollback.
    /// </summary>
    Task<T> SaveInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="SaveInTransactionAsync{T}"/>, then runs <paramref name="afterSave"/> on the work's result inside the
    /// same transaction, after the save and before the commit: it sees the rows just written, still holding their locks.
    /// A retried attempt runs both again.
    /// </summary>
    Task<TResult> SaveInTransactionAsync<TWork, TResult>(
        Func<CancellationToken, Task<TWork>> work, Func<TWork, CancellationToken, Task<TResult>> afterSave, CancellationToken cancellationToken);
}
