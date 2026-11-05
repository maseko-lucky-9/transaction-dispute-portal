using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Domain.Transactions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The isDisputable flag and its reasons, judged by the API at a chosen instant: a host whose clock the test moves to
/// day 119, 120 and 121 after the transactions.
/// </summary>
[Collection("api")]
public sealed class TransactionEligibilityTests(ApiFactory api)
{
    [Fact]
    public async Task FlagsAndReasonsFollowTheRulesIncludingTheDay120Boundary()
    {
        DateTimeOffset day0 = new(DateTime.UtcNow.Date.AddDays(-200), TimeSpan.Zero);
        day0 = day0.AddHours(12);
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid postedDebit = await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, day0);
        Guid pendingDebit = await customer.AddTransactionAsync(api, TransactionStatus.Pending, TransactionDirection.Debit, day0);
        Guid postedCredit = await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Credit, day0);
        Guid reversedDebit = await customer.AddTransactionAsync(api, TransactionStatus.Reversed, TransactionDirection.Debit, day0);
        Guid everythingWrong = await customer.AddTransactionAsync(api, TransactionStatus.Pending, TransactionDirection.Credit, day0.AddDays(-30));
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        SettableClock clock = new(day0.AddDays(119));
        await using WebApplicationFactory<Program> clocked = api.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        using HttpClient browser = clocked.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        Dictionary<Guid, string[]> day119 = await ReasonsAsync(browser, session);
        clock.Now = day0.AddDays(120);
        Dictionary<Guid, string[]> day120 = await ReasonsAsync(browser, session);
        clock.Now = day0.AddDays(121);
        Dictionary<Guid, string[]> day121 = await ReasonsAsync(browser, session);
        JsonElement detail = await (await browser.SendAsync(session.Request(HttpMethod.Get, $"/api/v1/transactions/{postedDebit}"))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(day119[postedDebit]);
        Assert.Empty(day120[postedDebit]);
        Assert.Equal(["window_expired"], day121[postedDebit]);
        Assert.False(detail.GetProperty("isDisputable").GetBoolean());
        Assert.Equal(["not_posted"], day119[pendingDebit]);
        Assert.Equal(["not_debit"], day119[postedCredit]);
        Assert.Equal(["not_posted"], day119[reversedDebit]);
        Assert.Equal(["not_posted", "not_debit", "window_expired"], day119[everythingWrong]);
    }

    private static async Task<Dictionary<Guid, string[]>> ReasonsAsync(HttpClient browser, BrowserSession session)
    {
        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/transactions?limit=100"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return page.GetProperty("items").EnumerateArray().ToDictionary(
            item => item.GetProperty("id").GetGuid(),
            item =>
            {
                string[] reasons = [.. item.GetProperty("disputeBlockReasons").EnumerateArray().Select(reason => reason.GetString()!)];
                Assert.Equal(reasons.Length == 0, item.GetProperty("isDisputable").GetBoolean());
                return reasons;
            });
    }
}
