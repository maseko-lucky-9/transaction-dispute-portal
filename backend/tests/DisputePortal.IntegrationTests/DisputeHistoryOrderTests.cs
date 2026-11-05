using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The history order must be total on its own, not borrowed from the index: with index and bitmap scans off, Postgres
/// sorts, and only the query's ORDER BY decides how disputes with the same created_at come out. Through the index,
/// ties already arrive in id order, so a missing id tie-breaker would pass every API test until the plan changed.
/// </summary>
[Collection("api")]
public sealed class DisputeHistoryOrderTests(ApiFactory api)
{
    [Fact]
    public async Task TiesOnCreatedAtComeOutByIdDescendingEvenWithoutTheIndex()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        DateTimeOffset tied = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        // Inserted in ascending id order, so the heap (what a sort sees first) is the opposite of the expected order.
        List<Guid> ids = [.. Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).Order()];
        foreach (Guid id in ids)
        {
            await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), DisputeStatus.Withdrawn, tied, id);
        }

        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentCustomer>().Set(customer.CustomerId);
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();
        await db.Database.OpenConnectionAsync();
        // Session settings on this connection only; Npgsql resets the session when it returns to the pool.
        await db.Database.ExecuteSqlRawAsync("SET enable_indexscan = off; SET enable_indexonlyscan = off; SET enable_bitmapscan = off;");

        IReadOnlyList<DisputeListItem> page = await new DisputeRepository(db).ListAsync(new DisputeListFilter(null), after: null, take: 10, CancellationToken.None);
        await db.Database.CloseConnectionAsync();

        Assert.Equal([.. Enumerable.Reverse(ids)], page.Select(item => item.Id));
    }
}
