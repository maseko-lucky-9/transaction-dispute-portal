using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// ADR-019 take next: the oldest unclaimed dispute, locked FOR UPDATE SKIP LOCKED, so concurrent agents get different
/// ones. The database is shared by every test, so each test seeds disputes older than every Submitted one already there
/// (they are then the ones taken) and asserts on those only. An empty pool (204) cannot be arranged here; it is
/// covered by AgentCommandsTests.
/// </summary>
[Collection("api")]
public sealed class AgentTakeNextTests(ApiFactory factory)
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task TakeNextAssignsTheOldestUnclaimedAndReturnsItsETag()
    {
        List<Guid> seeded = await SeedOldestAsync(2);
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage first = await api.SendAsync(AgentRequests.TakeNext(agent));
        JsonElement body = await first.Content.ReadFromJsonAsync<JsonElement>();
        HttpResponseMessage second = await api.SendAsync(AgentRequests.TakeNext(agent));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(seeded[0], body.GetProperty("id").GetGuid());
        Assert.Equal(("UnderReview", "You", user.Subject), (
            body.GetProperty("status").GetString(), body.GetProperty("assignment").GetString(), body.GetProperty("assigneeId").GetString()));
        Assert.Equal(await api.ETagAsync(agent, seeded[0]), DisputeRequests.ETagOf(first));
        JsonElement last = body.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal(("Submitted", "UnderReview", "Agent", user.Subject), (
            last.GetProperty("from").GetString(), last.GetProperty("to").GetString(), last.GetProperty("actorType").GetString(), last.GetProperty("actorId").GetString()));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(seeded[1], (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(user.Subject, await AssigneeAsync(seeded[0]));
        Assert.Equal(user.Subject, await AssigneeAsync(seeded[1]));
    }

    // Both requests hold their row lock at the gate at the same time. Without SKIP LOCKED the second would wait on the
    // first's row and never reach the gate; picking the oldest without a lock, both would take the same one.
    [Fact]
    public async Task TwoAgentsTakingNextAtOnceGetDifferentDisputes()
    {
        List<Guid> seeded = await SeedOldestAsync(2);
        (IdpUser firstUser, BrowserSession first) = await api.NewAgentAsync();
        (IdpUser secondUser, BrowserSession second) = await api.NewAgentAsync();
        await using WebApplicationFactory<Program> gated = GatedAfterLock(2);

        HttpResponseMessage[] responses = await Task.WhenAll(
            AgentApi.SendAsync(gated, AgentRequests.TakeNext(first)),
            AgentApi.SendAsync(gated, AgentRequests.TakeNext(second)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Guid firstId = (await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Guid secondId = (await responses[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(seeded.ToHashSet(), new HashSet<Guid> { firstId, secondId });
        Assert.Equal(firstUser.Subject, await AssigneeAsync(firstId));
        Assert.Equal(secondUser.Subject, await AssigneeAsync(secondId));
    }

    [Fact]
    public async Task EightParallelTakeNextsNeverHandOutADisputeTwice()
    {
        const int Agents = 8;
        List<Guid> seeded = await SeedOldestAsync(Agents);
        List<(IdpUser User, BrowserSession Session)> agents = [];
        for (int i = 0; i < Agents; i++)
        {
            agents.Add(await api.NewAgentAsync());
        }

        await using WebApplicationFactory<Program> gated = GatedAfterLock(Agents);

        HttpResponseMessage[] responses = await Task.WhenAll(agents.Select(agent => AgentApi.SendAsync(gated, AgentRequests.TakeNext(agent.Session))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        List<Guid> taken = [];
        for (int i = 0; i < Agents; i++)
        {
            JsonElement body = await responses[i].Content.ReadFromJsonAsync<JsonElement>();
            taken.Add(body.GetProperty("id").GetGuid());
            Assert.Equal(agents[i].User.Subject, body.GetProperty("assigneeId").GetString());
            Assert.Equal(agents[i].User.Subject, await AssigneeAsync(taken[^1]));
        }

        Assert.Equal(Agents, taken.Distinct().Count());
        Assert.Equal(seeded.ToHashSet(), taken.ToHashSet());
        Assert.Equal(Agents * 2, await AdminSql.CountAsync(
            factory.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = ANY(@ids)", ("ids", seeded.ToArray())));
    }

    // Take next holds the row lock (past the gate, for a second more) while a plain start review, which read the same
    // version, tries to write: its UPDATE waits for the lock, then finds the version gone (xmin) and is a 412 with the
    // claimed version's ETag. Without the lock the start review would write first and take next would lose.
    [Fact]
    public async Task AStartReviewRacingAHeldTakeNextIs412()
    {
        Guid id = (await SeedOldestAsync(1))[0];
        (IdpUser taker, BrowserSession taking) = await api.NewAgentAsync();
        (_, BrowserSession starting) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(starting, id);
        await using WebApplicationFactory<Program> gated = GatedAfterLock(2, holdLock: TimeSpan.FromSeconds(1));

        Task<HttpResponseMessage> takeNext = AgentApi.SendAsync(gated, AgentRequests.TakeNext(taking));
        Task<HttpResponseMessage> startReview = AgentApi.SendAsync(gated, AgentRequests.Transition(starting, id, etag, "UnderReview"));
        HttpResponseMessage took = await takeNext;
        HttpResponseMessage started = await startReview;

        Assert.Equal(HttpStatusCode.OK, took.StatusCode);
        Assert.Equal(id, (await took.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.PreconditionFailed, started.StatusCode);
        Assert.Equal(DisputeRequests.ETagOf(took), DisputeRequests.ETagOf(started));
        Assert.Equal(taker.Subject, await AssigneeAsync(id));
        Assert.Equal(2, await AdminSql.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    [Fact]
    public async Task TakeNextIsAuditedAndLoggedWithIdsOnly()
    {
        const string DisplayName = "Nomvula Take-Next-Unmistakable";
        DateTimeOffset oldest = await OldestSubmittedAsync();
        TestCustomer customer = await TestCustomer.CreateAsync(factory, DisplayName);
        Guid id = await customer.AddDisputeAsync(factory, await customer.AddEligibleTransactionAsync(factory), DisputeStatus.Submitted, oldest.AddDays(-1));
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        using MeasurementProbe<long> audits = MeasurementProbe<long>.On(host, SecurityMetrics.MeterName, "audit.events");
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage response = await AgentApi.SendAsync(host, AgentRequests.TakeNext(agent));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (string, EventId, LogLevel, string) entry = Assert.Single(logs.Entries, entry => entry.EventId.Id == AgentCommands.DisputeTransitioned.Id);
        Assert.Contains(id.ToString(), entry.Item4, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(user.Subject, entry.Item4, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, logged => logged.Message.Contains(DisplayName, StringComparison.Ordinal)
            || logged.Message.Contains("Inserted by a test.", StringComparison.Ordinal)
            || logged.Message.Contains(customer.User.Subject, StringComparison.Ordinal));
        MeasurementProbe<long>.Sample audit = Assert.Single(audits.Samples);
        Assert.Equal(SecurityMetrics.AgentDisputeTransitioned, audit.Tags["action"]);
    }

    // Refused before the handler runs: nothing is locked or claimed, and no history row is written anywhere.
    [Fact]
    public async Task ACustomerADualRoleSessionAndAnAnonymousCallerCannotTakeNextAndNothingChanges()
    {
        Guid id = (await SeedOldestAsync(1))[0];
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        BrowserSession plain = await BrowserSession.SignInAsync(factory, customer.User);
        BrowserSession both = await BrowserSession.SignInAsync(factory, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });
        long history = await AdminSql.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes");

        Problem forbidden = await api.ProblemAsync(AgentRequests.TakeNext(plain), HttpStatusCode.Forbidden);
        Problem conflict = await api.ProblemAsync(AgentRequests.TakeNext(both), HttpStatusCode.Forbidden);
        using HttpClient browser = factory.CreateBrowserClient();
        using HttpRequestMessage anonymous = new(HttpMethod.Post, $"{AgentRequests.Queue}/take-next");
        anonymous.Headers.Add(CsrfMiddleware.HeaderName, "1");
        HttpResponseMessage unauthenticated = await browser.SendAsync(anonymous);

        Assert.Equal("forbidden", forbidden.Code);
        Assert.Equal(SingleRoleRequirement.Code, conflict.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Null(await AssigneeAsync(id));
        Assert.Equal("Submitted", await StatusAsync(id));
        Assert.Equal(history, await AdminSql.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes"));
    }

    // Take next is held after its save, inside its transaction and holding the row lock, while the customer reads the
    // dispute (still the committed Submitted version) and withdraws it: the withdrawal waits on the lock (the gate opens
    // only once Postgres shows it waiting, or it has finished) and then fails the xmin check. The answer to take next is
    // the claim; read after the commit instead, it would be the withdrawn dispute. The customer can still withdraw with
    // the current ETag, and that clears the assignee.
    [Fact]
    public async Task ACustomerWithdrawalRacingTakeNextIs412AndTheAnswerIsTheClaim()
    {
        DateTimeOffset oldest = (await OldestSubmittedAsync()).AddDays(-1);
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync(DisputeStatus.Submitted, oldest);
        BrowserSession owner = await BrowserSession.SignInAsync(factory, customer.User);
        (IdpUser taker, BrowserSession taking) = await api.NewAgentAsync();
        TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebApplicationFactory<Program> gated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<AgentDisputeRepository>();
            services.AddScoped<IAgentDisputeRepository>(provider => new GatedAgentDisputeRepository(
                provider.GetRequiredService<AgentDisputeRepository>(), new Rendezvous(1), beforeDetail: async token =>
                {
                    arrived.TrySetResult();
                    await open.Task.WaitAsync(GateTimeout, token);
                }));
        }));

        Task<HttpResponseMessage> takeNext = AgentApi.SendAsync(gated, AgentRequests.TakeNext(taking));
        await arrived.Task.WaitAsync(GateTimeout);
        string? customerETag = DisputeRequests.ETagOf(await api.SendAsync(owner.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{id}")));
        Task<HttpResponseMessage> withdraw = api.SendAsync(DisputeRequests.Withdraw(owner, id, customerETag));
        await WaitUntilFinishedOrWaitingOnALockAsync(withdraw);
        open.TrySetResult();
        HttpResponseMessage took = await takeNext;
        HttpResponseMessage withdrawn = await withdraw;

        Assert.Equal(HttpStatusCode.OK, took.StatusCode);
        JsonElement claim = await took.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((id, "UnderReview", "You"), (claim.GetProperty("id").GetGuid(), claim.GetProperty("status").GetString(), claim.GetProperty("assignment").GetString()));
        Assert.Equal(HttpStatusCode.PreconditionFailed, withdrawn.StatusCode);
        Assert.Equal(DisputeRequests.ETagOf(took), DisputeRequests.ETagOf(withdrawn));
        Assert.Equal(("UnderReview", taker.Subject), (await StatusAsync(id), await AssigneeAsync(id)));

        HttpResponseMessage again = await api.SendAsync(DisputeRequests.Withdraw(owner, id, DisputeRequests.ETagOf(withdrawn)));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(("Withdrawn", (string?)null), (await StatusAsync(id), await AssigneeAsync(id)));
    }

    private WebApplicationFactory<Program> GatedAfterLock(int parties, TimeSpan? holdLock = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            Rendezvous afterLock = new(parties);
            services.AddScoped<AgentDisputeRepository>();
            services.AddScoped<IAgentDisputeRepository>(provider =>
                new GatedAgentDisputeRepository(provider.GetRequiredService<AgentDisputeRepository>(), afterLock, holdLock));
        }));

    /// <summary><paramref name="count"/> Submitted disputes, oldest first, all older than any Submitted dispute already there.</summary>
    private async Task<List<Guid>> SeedOldestAsync(int count)
    {
        DateTimeOffset start = (await OldestSubmittedAsync()).AddDays(-1);
        List<Guid> ids = [];
        for (int i = 0; i < count; i++)
        {
            ids.Add((await api.SeedDisputeAsync(DisputeStatus.Submitted, start.AddSeconds(i))).DisputeId);
        }

        return ids;
    }

    private async Task<DateTimeOffset> OldestSubmittedAsync() =>
        (await AdminSql.ListAsync(
            factory.AdminConnectionString,
            "SELECT coalesce(min(created_at), now()) FROM disputes WHERE status = 'Submitted'",
            reader => reader.GetFieldValue<DateTimeOffset>(0))).Single();

    // Postgres signals nothing when a backend starts to wait, so this polls for it, as AdvisoryLockRendezvous does. Tests
    // run one at a time (AssemblyInfo.cs), so a backend of this database waiting on a lock is the request under test.
    private async Task WaitUntilFinishedOrWaitingOnALockAsync(Task request)
    {
        using CancellationTokenSource deadline = new(GateTimeout);
        while (!request.IsCompleted && await AdminSql.CountAsync(
            factory.AdminConnectionString, "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 0)
        {
            await Task.Delay(PollInterval, deadline.Token);
        }
    }

    private async Task<string> StatusAsync(Guid id) =>
        (await AdminSql.ListAsync(factory.AdminConnectionString, "SELECT status FROM disputes WHERE id = @id", reader => reader.GetString(0), ("id", id))).Single();

    private async Task<string?> AssigneeAsync(Guid id) =>
        (await AdminSql.ListAsync(
            factory.AdminConnectionString, "SELECT assignee_id FROM disputes WHERE id = @id", reader => reader.IsDBNull(0) ? null : reader.GetString(0), ("id", id))).Single();
}
