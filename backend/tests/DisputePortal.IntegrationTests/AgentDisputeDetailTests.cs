using System.Net;
using System.Text.Json;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>An agent opening a dispute: roles, 404, the ETag, the customer's details, and the audit trail.</summary>
[Collection("api")]
public sealed class AgentDisputeDetailTests(ApiFactory factory)
{
    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task ACustomerIs403AndADualRoleSessionIs403RoleConflictOnTheDetail()
    {
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync();
        BrowserSession customerSession = await BrowserSession.SignInAsync(factory, customer.User);
        BrowserSession both = await BrowserSession.SignInAsync(factory, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });

        Problem plain = await api.ProblemAsync(customerSession.Request(HttpMethod.Get, AgentRequests.Detail(id)), HttpStatusCode.Forbidden);
        Problem dual = await api.ProblemAsync(both.Request(HttpMethod.Get, AgentRequests.Detail(id)), HttpStatusCode.Forbidden);

        Assert.Equal("forbidden", plain.Code);
        Assert.Equal(SingleRoleRequirement.Code, dual.Code);
    }

    [Fact]
    public async Task AnUnauthenticatedCallerIs401OnTheDetail()
    {
        using HttpClient browser = factory.CreateBrowserClient();
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRequests.Detail(Guid.NewGuid()));
        request.Headers.Add(CsrfMiddleware.HeaderName, "1");

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AMissingDisputeIs404()
    {
        (_, BrowserSession agent) = await api.NewAgentAsync();

        Problem problem = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.Detail(Guid.NewGuid())), HttpStatusCode.NotFound);

        Assert.Equal("dispute_not_found", problem.Code);
    }

    [Fact]
    public async Task TheDetailCarriesTheETagTheCustomerAndTheTimelineWithActorIds()
    {
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage response = await api.SendAsync(agent.Request(HttpMethod.Get, AgentRequests.Detail(id)));
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^\"[0-9]+\"$", DisputeRequests.ETagOf(response));
        Assert.Equal("Test Customer", body.GetProperty("customerDisplayName").GetString());
        Assert.Equal("**** 0001", body.GetProperty("maskedAccountNumber").GetString());
        JsonElement first = body.GetProperty("timeline")[0];
        Assert.Equal("Customer", first.GetProperty("actorType").GetString());
        Assert.Equal(customer.User.Subject, first.GetProperty("actorId").GetString());
    }

    [Fact]
    public async Task EverySuccessfulViewIsAuditedWithIdsOnlyAndAMissingOneIsNot()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();
        Guid missing = Guid.NewGuid();

        await AgentApi.SendAsync(host, agent.Request(HttpMethod.Get, AgentRequests.Detail(id)));
        await AgentApi.SendAsync(host, agent.Request(HttpMethod.Get, AgentRequests.Detail(id)));
        await AgentApi.SendAsync(host, agent.Request(HttpMethod.Get, AgentRequests.Detail(missing)));

        List<string> viewed = [.. logs.Entries.Where(entry => entry.EventId.Id == AgentQueries.DisputeViewed.Id).Select(entry => entry.Message)];
        Assert.Equal(2, viewed.Count(message => message.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase) && message.Contains(user.Subject, StringComparison.Ordinal)));
        Assert.DoesNotContain(viewed, message => message.Contains(missing.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("Inserted by a test.", StringComparison.Ordinal));
    }
}
