using System.Net;
using System.Text;
using System.Text.Json;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// A body field the API does not know is never echoed back, whatever the route does with it. The two routes that bind a
/// body refuse it (400 malformed_request, so a typo such as "ammount" surfaces instead of disputing the whole amount); the
/// withdraw route binds no body at all, so it ignores one and answers 200.
/// </summary>
[Collection("api")]
public sealed class UnknownBodyFieldTests(ApiFactory api)
{
    private const string Field = "colour";

    [Theory]
    [InlineData("submit")]
    [InlineData("transition")]
    [InlineData("withdraw")]
    public async Task AnUnknownBodyFieldIsNeverEchoed(string route)
    {
        (HttpRequestMessage request, HttpStatusCode expected) = route switch
        {
            "submit" => await SubmitAsync(),
            "transition" => await TransitionAsync(),
            "withdraw" => await WithdrawAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "No request is defined for this route."),
        };
        using HttpRequestMessage sent = request;

        using HttpResponseMessage response = await AgentApi.SendAsync(api, sent);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal("malformed_request", (await Problem.ReadAsync(response)).Code);
        }

        // The name appears nowhere (a property, a detail, an errors key); the quoted value would be a property's value or a
        // string in a message ("red" alone is inside "required").
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Field, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"red\"", body, StringComparison.Ordinal);
    }

    private async Task<(HttpRequestMessage Request, HttpStatusCode Expected)> SubmitAsync()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        string json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = Guid.NewGuid(),
            ["transactionId"] = transactionId,
            ["reasonCode"] = DisputeRequests.Reason,
            ["description"] = DisputeRequests.Description,
            [Field] = "red",
        });
        return (DisputeRequests.SubmitJson(session, json), HttpStatusCode.BadRequest);
    }

    private async Task<(HttpRequestMessage Request, HttpStatusCode Expected)> TransitionAsync()
    {
        AgentApi agents = new(api);
        (_, Guid disputeId) = await agents.SeedDisputeAsync();
        (_, BrowserSession agent) = await agents.NewAgentAsync();
        string etag = await agents.ETagAsync(agent, disputeId);
        return (AgentRequests.TransitionJson(agent, disputeId, etag, $$"""{"toStatus":"UnderReview","{{Field}}":"red"}"""), HttpStatusCode.BadRequest);
    }

    private async Task<(HttpRequestMessage Request, HttpStatusCode Expected)> WithdrawAsync()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        Guid disputeId = Guid.NewGuid();
        using HttpResponseMessage created = await AgentApi.SendAsync(api, DisputeRequests.Submit(session, disputeId, transactionId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        HttpRequestMessage withdraw = DisputeRequests.Withdraw(session, disputeId, DisputeRequests.ETagOf(created));
        withdraw.Content = new StringContent($$"""{"{{Field}}":"red"}""", Encoding.UTF8, "application/json");
        return (withdraw, HttpStatusCode.OK);
    }
}
