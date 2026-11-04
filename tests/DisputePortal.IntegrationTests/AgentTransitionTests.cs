using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>ADR-007 for agents: existence (404), If-Match (428, 412), the body (400), the lifecycle (409), then the write.</summary>
[Collection("api")]
public sealed class AgentTransitionTests(ApiFactory factory)
{
    private const string Card = "4111111111111111";

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task ACustomerIs403AndADualRoleSessionIs403RoleConflict()
    {
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync();
        BrowserSession plain = await BrowserSession.SignInAsync(factory, customer.User);
        BrowserSession both = await BrowserSession.SignInAsync(factory, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });

        Problem forbidden = await api.ProblemAsync(AgentRequests.Transition(plain, id, "\"1\"", "UnderReview"), HttpStatusCode.Forbidden);
        Problem conflict = await api.ProblemAsync(AgentRequests.Transition(both, id, "\"1\"", "UnderReview"), HttpStatusCode.Forbidden);

        Assert.Equal("forbidden", forbidden.Code);
        Assert.Equal(SingleRoleRequirement.Code, conflict.Code);
        Assert.Equal("Submitted", await StatusAsync(id));
    }

    [Fact]
    public async Task AnUnauthenticatedCallerIs401()
    {
        using HttpClient browser = factory.CreateBrowserClient();
        using HttpRequestMessage request = new(HttpMethod.Post, $"{AgentRequests.Queue}/{Guid.NewGuid()}/transitions");
        request.Headers.Add(CsrfMiddleware.HeaderName, "1");
        request.Content = JsonContent.Create(new { toStatus = "UnderReview" });

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Existence before preconditions, so a missing id is 404 even without If-Match.
    [Fact]
    public async Task AMissingDisputeIs404EvenWithoutIfMatch()
    {
        (_, BrowserSession agent) = await api.NewAgentAsync();

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(agent, Guid.NewGuid(), ifMatch: null, "UnderReview"), HttpStatusCode.NotFound);

        Assert.Equal("dispute_not_found", problem.Code);
    }

    [Fact]
    public async Task WithoutIfMatchTheMoveIs428AndNothingChanges()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession agent) = await api.NewAgentAsync();

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(agent, id, ifMatch: null, "UnderReview"), HttpStatusCode.PreconditionRequired);

        Assert.Equal("precondition_required", problem.Code);
        Assert.Equal("Submitted", await StatusAsync(id));
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("W/\"{0}\"")]
    [InlineData("*")]
    [InlineData("not-an-etag")]
    public async Task AStaleWeakOrLooseIfMatchIs412WithTheCurrentETag(string ifMatchTemplate)
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession agent) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(agent, id);
        string ifMatch = ifMatchTemplate.Replace("{0}", etag.Trim('"'), StringComparison.Ordinal);

        HttpResponseMessage response = await api.SendAsync(AgentRequests.Transition(agent, id, ifMatch, "UnderReview"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(etag, DisputeRequests.ETagOf(response));
        Assert.Equal("precondition_failed", (await Problem.ReadAsync(response)).Code);
        Assert.Equal("Submitted", await StatusAsync(id));
    }

    // A stale tag wins over an illegal move: the agent is told to reload before being told the move is not allowed.
    [Fact]
    public async Task AStaleETagIs412EvenForAnIllegalMove()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage response = await api.SendAsync(AgentRequests.Transition(agent, id, "\"1\"", "Withdrawn"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Theory]
    [InlineData(DisputeStatus.Submitted, "Accepted")]
    [InlineData(DisputeStatus.Submitted, "Rejected")]
    [InlineData(DisputeStatus.Submitted, "Submitted")]
    [InlineData(DisputeStatus.Submitted, "Withdrawn")]
    [InlineData(DisputeStatus.UnderReview, "UnderReview")]
    [InlineData(DisputeStatus.UnderReview, "Withdrawn")]
    [InlineData(DisputeStatus.Accepted, "Rejected")]
    [InlineData(DisputeStatus.Withdrawn, "UnderReview")]
    public async Task AMoveTheLifecycleDoesNotHoldIs409InvalidTransition(DisputeStatus from, string to)
    {
        (_, Guid id) = await api.SeedDisputeAsync(from);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(agent, id);

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(agent, id, etag, to, "Checked with the merchant."), HttpStatusCode.Conflict);

        Assert.Equal("invalid_transition", problem.Code);
        Assert.Equal(from.ToString(), await StatusAsync(id));
    }

    [Theory]
    [InlineData("Accepted", null, "note", "validation_failed")]
    [InlineData("Rejected", "   ", "note", "validation_failed")]
    [InlineData("Accepted", "Refunded. Card 4111 1111 1111 1111 on file.", "note", "sensitive_data")]
    [InlineData("Accepted", "ID 800101 5009 087", "note", "sensitive_data")]
    [InlineData("Accepted", "Checked\u0000 with the merchant.", "note", "invalid_characters")]
    [InlineData("nonsense", "Checked.", "toStatus", "validation_failed")]
    [InlineData("", "Checked.", "toStatus", "validation_failed")]
    public async Task ABadMoveBodyIs400WithTheFieldNamed(string toStatus, string? note, string field, string code)
    {
        (_, BrowserSession agent) = await api.NewAgentAsync();
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview);
        string etag = await api.ETagAsync(agent, id);

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(agent, id, etag, toStatus, note), HttpStatusCode.BadRequest);

        Assert.Equal(code, problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty(field, out _), "errors." + field);
        Assert.Equal("UnderReview", await StatusAsync(id));
    }

    // The padding bypass: a note over the 10 000-character scan cap on paper, a card number once trimmed.
    [Fact]
    public async Task APaddedCardNumberIsStillSensitiveData()
    {
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(agent, id);
        string padded = new string(' ', 12_000) + Card;

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(agent, id, etag, "Accepted", padded), HttpStatusCode.BadRequest);

        Assert.Equal("sensitive_data", problem.Code);
        Assert.Equal("UnderReview", await StatusAsync(id));
    }

    [Fact]
    public async Task ANoteOver2000CharactersIs400()
    {
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(agent, id);

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(agent, id, etag, "Rejected", new string('n', 2001)), HttpStatusCode.BadRequest);

        Assert.True(problem.Body.GetProperty("errors").TryGetProperty("note", out _));
    }

    [Fact]
    public async Task AnUnknownBodyFieldIs400()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession agent) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(agent, id);

        HttpResponseMessage response = await api.SendAsync(AgentRequests.TransitionJson(agent, id, etag, """{"toStatus":"UnderReview","actor":"customer"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Submitted", await StatusAsync(id));
    }

    [Fact]
    public async Task AValidMoveIs200WithTheNewETagAndTheAgentAsActor()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();
        string before = await api.ETagAsync(agent, id);

        HttpResponseMessage review = await api.SendAsync(AgentRequests.Transition(agent, id, before, "underreview"));
        string afterReview = DisputeRequests.ETagOf(review)!;
        HttpResponseMessage accept = await api.SendAsync(AgentRequests.Transition(agent, id, afterReview, "Accepted", "  Merchant confirmed the duplicate.  "));
        JsonElement accepted = await accept.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.NotEqual(before, afterReview);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.NotEqual(afterReview, DisputeRequests.ETagOf(accept));
        Assert.Equal(await api.ETagAsync(agent, id), DisputeRequests.ETagOf(accept));
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.Equal("Merchant confirmed the duplicate.", accepted.GetProperty("resolutionNote").GetString());
        Assert.NotEqual(JsonValueKind.Null, accepted.GetProperty("resolvedAt").ValueKind);
        JsonElement last = accepted.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("Agent", last.GetProperty("actorType").GetString());
        Assert.Equal(user.Subject, last.GetProperty("actorId").GetString());
        Assert.Equal(2, await AdminSql.CountAsync(factory.AdminConnectionString,
            "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id AND actor_type = 'Agent' AND actor_id = @subject", ("id", id), ("subject", user.Subject)));
    }

    // Both agents load version N and pass If-Match; the second UPDATE ... WHERE xmin = N finds nothing.
    [Fact]
    public async Task TwoAgentsWithTheSameETagGiveOne200AndOne412WithTheNewETag()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession first) = await api.NewAgentAsync();
        (_, BrowserSession second) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(first, id);
        await using WebApplicationFactory<Program> gated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            Rendezvous afterFind = new(2);
            services.AddScoped<AgentDisputeRepository>();
            services.AddScoped<IAgentDisputeRepository>(provider =>
                new GatedAgentDisputeRepository(provider.GetRequiredService<AgentDisputeRepository>(), afterFind));
        }));

        HttpResponseMessage[] responses = await Task.WhenAll(
            AgentApi.SendAsync(gated, AgentRequests.Transition(first, id, etag, "UnderReview")),
            AgentApi.SendAsync(gated, AgentRequests.Transition(second, id, etag, "UnderReview")));

        HttpResponseMessage won = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage lost = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(DisputeRequests.ETagOf(won), DisputeRequests.ETagOf(lost));
        Assert.Equal("precondition_failed", (await Problem.ReadAsync(lost)).Code);
        Assert.Equal(2, await AdminSql.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    [Fact]
    public async Task TheNoteTextIsNeverLoggedOnlyIdsAndCodes()
    {
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview);
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(agent, id);
        const string Secret = "Refund reference ZX-SECRET-NOTE-9931 approved";
        const string Rejected = "Contains card 4111 1111 1111 1111 in a refused note";

        HttpResponseMessage refused = await AgentApi.SendAsync(host, AgentRequests.Transition(agent, id, etag, "Accepted", Rejected));
        HttpResponseMessage accepted = await AgentApi.SendAsync(host, AgentRequests.Transition(agent, id, etag, "Accepted", Secret));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        List<string> messages = [.. logs.Entries.Select(entry => entry.Message)];
        Assert.DoesNotContain(messages, message => message.Contains("ZX-SECRET-NOTE-9931", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains(Rejected, StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.EventId.Id == AgentCommands.DisputeTransitioned.Id
            && entry.Message.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)
            && entry.Message.Contains(user.Subject, StringComparison.Ordinal));
    }

    private async Task<string> StatusAsync(Guid id) =>
        (await AdminSql.ListAsync(factory.AdminConnectionString, "SELECT status FROM disputes WHERE id = @id", reader => reader.GetString(0), ("id", id))).Single();
}
