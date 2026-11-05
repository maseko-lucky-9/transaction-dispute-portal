using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisputePortal.IntegrationTests;

/// <summary>What the agent endpoint tests share: signing in, seeding disputes in any status, sending, walking the queue.</summary>
public sealed class AgentApi(ApiFactory api)
{
    private const int MaxPages = 500;

    public ApiFactory Factory { get; } = api;

    public static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> host, HttpRequestMessage request)
    {
        using HttpClient browser = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(ApiFactory.PublicBaseUrl),
        });
        return await browser.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => SendAsync(Factory, request);

    /// <summary>A signed-in agent with a subject of their own, so actor and audit assertions cannot see another test's work.</summary>
    public async Task<(IdpUser User, BrowserSession Session)> NewAgentAsync()
    {
        IdpUser user = AgentRequests.Agent("agent-" + Guid.NewGuid().ToString("N"));
        return (user, await BrowserSession.SignInAsync(Factory, user));
    }

    /// <summary>A dispute of a new customer, inserted directly in <paramref name="status"/>.</summary>
    public async Task<(TestCustomer Customer, Guid DisputeId)> SeedDisputeAsync(DisputeStatus status = DisputeStatus.Submitted, DateTimeOffset? createdAt = null)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(Factory);
        Guid transactionId = await customer.AddEligibleTransactionAsync(Factory);
        return (customer, await customer.AddDisputeAsync(Factory, transactionId, status, createdAt));
    }

    /// <summary>Another dispute of an existing customer, on a new transaction of theirs.</summary>
    public async Task<Guid> SeedAnotherAsync(TestCustomer customer, DisputeStatus status, DateTimeOffset? createdAt = null) =>
        await customer.AddDisputeAsync(Factory, await customer.AddEligibleTransactionAsync(Factory), status, createdAt);

    public async Task<string> ETagAsync(BrowserSession agent, Guid id)
    {
        HttpResponseMessage response = await SendAsync(agent.Request(HttpMethod.Get, AgentRequests.Detail(id)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return DisputeRequests.ETagOf(response)!;
    }

    public async Task<JsonElement> OkAsync(BrowserSession session, string path)
    {
        HttpResponseMessage response = await SendAsync(session.Request(HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<Problem> ProblemAsync(HttpRequestMessage request, HttpStatusCode status)
    {
        HttpResponseMessage response = await SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        return await Problem.ReadAsync(response);
    }

    /// <summary>Follows nextCursor until the last page (or until <paramref name="stopWhen"/> holds); returns every item seen, in order.</summary>
    public async Task<List<JsonElement>> WalkQueueAsync(BrowserSession agent, string query, Func<List<JsonElement>, bool>? stopWhen = null)
    {
        List<JsonElement> seen = [];
        string? cursor = null;
        for (int page = 0; page < MaxPages; page++)
        {
            string separator = query.Length == 0 ? "?" : "&";
            string path = AgentRequests.Queue + query + (cursor is null ? string.Empty : separator + "cursor=" + Uri.EscapeDataString(cursor));
            JsonElement body = await OkAsync(agent, path);
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(item => item.Clone()));
            cursor = body.GetProperty("nextCursor").GetString();
            if (cursor is null || (stopWhen is not null && stopWhen(seen)))
            {
                break;
            }
        }

        return seen;
    }

    public static List<Guid> IdsOf(IEnumerable<JsonElement> items) => [.. items.Select(item => item.GetProperty("id").GetGuid())];
}
