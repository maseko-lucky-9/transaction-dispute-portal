using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>The Owner filter is fail-closed: a context with no current customer sees nothing, never everything.</summary>
[Collection("api")]
public sealed class OwnerFilterTests(ApiFactory api)
{
    [Fact]
    public async Task WithNoCurrentCustomerTheAppSeesNoTransactionsOrAccounts()
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();

        int transactions = await db.Transactions.CountAsync();
        int accounts = await db.Accounts.CountAsync();

        Assert.True(await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM transactions") >= 150);
        Assert.Equal(0, transactions);
        Assert.Equal(0, accounts);
    }

    [Fact]
    public async Task WithNoCurrentCustomerTheAppSeesNoDisputesOrHistory()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();

        int disputes = await db.Disputes.CountAsync();
        int history = await db.Set<DisputeStatusChange>().CountAsync();

        Assert.Equal(0, disputes);
        Assert.Equal(0, history);
    }

    // History is scoped through its dispute: another customer's entries never show, even queried directly.
    [Fact]
    public async Task WithACurrentCustomerTheAppSeesOnlyThatCustomersDisputesAndHistory()
    {
        TestCustomer mine = await TestCustomer.CreateAsync(api);
        TestCustomer theirs = await TestCustomer.CreateAsync(api);
        Guid myDispute = await mine.AddDisputeAsync(api, await mine.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        await theirs.AddDisputeAsync(api, await theirs.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentCustomer>().Set(mine.CustomerId);
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();

        List<Guid> disputes = await db.Disputes.Select(dispute => dispute.Id).ToListAsync();
        List<Guid> history = await db.Set<DisputeStatusChange>().Select(change => change.DisputeId).Distinct().ToListAsync();

        Assert.Equal([myDispute], disputes);
        Assert.Equal([myDispute], history);
    }

    [Fact]
    public async Task WithACurrentCustomerTheAppSeesOnlyThatCustomersRows()
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentCustomer>().Set(Seeder.SiphoCustomerId);
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();

        List<Guid> owners = await db.Transactions.Select(transaction => transaction.CustomerId).Distinct().ToListAsync();
        List<Account> accounts = await db.Accounts.ToListAsync();

        Assert.Equal([Seeder.SiphoCustomerId], owners);
        Assert.All(accounts, account => Assert.Equal(Seeder.SiphoCustomerId, account.CustomerId));
    }
}
