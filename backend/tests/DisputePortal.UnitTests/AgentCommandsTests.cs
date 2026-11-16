using System.Security.Claims;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DisputePortal.UnitTests;

/// <summary>
/// The command called directly, without the validator or the endpoint in front of it: a status that names nothing is a
/// typed 400 and not a parse exception, and an agent's 404 is never counted as bola_404.
/// </summary>
public sealed class AgentCommandsTests : IDisposable
{
    private readonly MeterSandbox meters = new();
    private readonly IAgentDisputeRepository disputes = Substitute.For<IAgentDisputeRepository>();
    private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly AgentCommands commands;
    private bool insideAfterSave;

    public AgentCommandsTests()
    {
        commands = new AgentCommands(
            disputes, unitOfWork, new FixedClock(DisputeTestData.Now.AddHours(2)), meters.Disputes, meters.Security, NullLogger<AgentCommands>.Instance);
    }

    public void Dispose() => meters.Dispose();

    [Theory]
    [InlineData("Nonsense")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData(" Accepted")]
    [InlineData("Accepted ")]
    [InlineData("Accepted,Rejected")]
    [InlineData("Submitted,UnderReview")]
    public async Task AStatusThatNamesNoDisputeStatusIsATypedBadRequestBeforeAnythingIsRead(string? toStatus)
    {
        UnknownTargetStatusException refusal = await Assert.ThrowsAsync<UnknownTargetStatusException>(
            () => commands.TransitionAsync(Guid.NewGuid(), new TransitionRequest(toStatus, "Checked."), "\"1\"", DisputeTestData.AgentSubject, CancellationToken.None));

        Assert.Equal((400, "validation_failed"), (refusal.Status, refusal.Code));
        await disputes.DidNotReceiveWithAnyArgs().FindAsync(default, default);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // The body is the validator's: the message under the field, so a caller sees the same answer from either source.
    [Fact]
    public void TheTypedFailureIsTheValidatorsBadRequest()
    {
        HttpValidationProblemDetails problem = Assert.IsType<HttpValidationProblemDetails>(new UnknownTargetStatusException().ToProblem());

        Assert.Equal(400, problem.Status);
        Assert.Equal("validation_failed", problem.Extensions["code"]);
        Assert.Equal([UnknownTargetStatusException.FieldMessage], problem.Errors["toStatus"]);
    }

    // Any case of a real status name gets past the parse (to the 404 here), including the ones the lifecycle then refuses.
    [Theory]
    [InlineData("UnderReview")]
    [InlineData("underreview")]
    [InlineData("ACCEPTED")]
    [InlineData("rejected")]
    [InlineData("Withdrawn")]
    [InlineData("Submitted")]
    public async Task AnyCaseOfAStatusNameIsReadAndAMissingDisputeIsANotFoundThatIsNotCounted(string toStatus)
    {
        disputes.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(null));

        NotFoundException refusal = await Assert.ThrowsAsync<NotFoundException>(
            () => commands.TransitionAsync(Guid.NewGuid(), new TransitionRequest(toStatus, "Checked."), "\"1\"", DisputeTestData.AgentSubject, CancellationToken.None));

        Assert.Equal("dispute_not_found", refusal.Code);
        Assert.Empty(meters.Of("security.events"));
    }

    // Take next with nothing to take: the lock finds no row, nothing moves, nothing is counted, and the endpoint answers 204.
    [Fact]
    public async Task TakeNextWithNothingUnclaimedReturnsNullAndRecordsNothing()
    {
        RunTheTransactionsWork();
        disputes.LockOldestUnclaimedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(null));

        VersionedAgentDispute? claimed = await commands.TakeNextAsync(DisputeTestData.AgentSubject, CancellationToken.None);

        Assert.Null(claimed);
        await disputes.Received(1).LockOldestUnclaimedAsync(Arg.Any<CancellationToken>());
        await disputes.DidNotReceiveWithAnyArgs().GetDetailAsync(default, default!, default);
        Assert.Empty(meters.Of("audit.events"));
        Assert.Empty(meters.Of("disputes.transitions"));
    }

    // The detail is read inside the transaction, so a claim that cannot be read back rolls back with it: nothing counted.
    [Fact]
    public async Task AClaimThatCannotBeReadBackIsANotFoundAndRecordsNothing()
    {
        RunTheTransactionsWork();
        Dispute dispute = DisputeTestData.Submitted();
        disputes.LockOldestUnclaimedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));

        await Assert.ThrowsAsync<NotFoundException>(() => commands.TakeNextAsync("agent-taking", CancellationToken.None));

        Assert.Equal((DisputeStatus.UnderReview, "agent-taking"), (dispute.Status, dispute.AssigneeId));
        await disputes.Received(1).GetDetailAsync(dispute.Id, "agent-taking", Arg.Any<CancellationToken>());
        Assert.Empty(meters.Of("audit.events"));
        Assert.Empty(meters.Of("disputes.transitions"));
    }

    // The claim is read back before the commit (while the row lock is held), so nothing can land between them.
    [Fact]
    public async Task TakeNextClaimsTheLockedDisputeAndReadsItBackInsideTheTransaction()
    {
        RunTheTransactionsWork();
        Dispute dispute = DisputeTestData.Submitted();
        disputes.LockOldestUnclaimedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));
        VersionedAgentDispute detail = new(DetailOf(dispute), 42);
        bool readInsideTheTransaction = false;
        disputes.GetDetailAsync(dispute.Id, "agent-taking", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            readInsideTheTransaction = insideAfterSave;
            return Task.FromResult<VersionedAgentDispute?>(detail);
        });

        VersionedAgentDispute? claimed = await commands.TakeNextAsync("agent-taking", CancellationToken.None);

        Assert.Same(detail, claimed);
        Assert.True(readInsideTheTransaction, "the detail must be read inside the transaction, before the commit");
        Assert.Equal((DisputeStatus.UnderReview, "agent-taking"), (dispute.Status, dispute.AssigneeId));
        Assert.Equal(SecurityMetrics.AgentDisputeTransitioned, Assert.Single(meters.Of("audit.events")).Tags["action"]);
        Assert.Single(meters.Of("disputes.transitions"));
    }

    [Fact]
    public async Task TakeNextAnswers204WhenThereIsNothingToTake()
    {
        RunTheTransactionsWork();
        disputes.LockOldestUnclaimedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(null));
        DefaultHttpContext context = new() { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimNames.Subject, DisputeTestData.AgentSubject)], "test"));
        context.Response.Body = new MemoryStream();

        IResult result = (await AgentEndpoints.TakeNextAsync(commands, context, CancellationToken.None)).Result;
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
    }

    // The unit of work runs the attempt it is handed, as the real one does: the work, then (after the save, before the
    // commit) the step after it. An unconfigured substitute would skip both.
    private void RunTheTransactionsWork() =>
        unitOfWork.SaveInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Guid?>>>(), Arg.Any<Func<Guid?, CancellationToken, Task<VersionedAgentDispute?>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Guid? claimed = await call.Arg<Func<CancellationToken, Task<Guid?>>>()(CancellationToken.None);
                insideAfterSave = true;
                try
                {
                    return await call.Arg<Func<Guid?, CancellationToken, Task<VersionedAgentDispute?>>>()(claimed, CancellationToken.None);
                }
                finally
                {
                    insideAfterSave = false;
                }
            });

    private static AgentDisputeDetail DetailOf(Dispute dispute) => new(
        dispute.Id, dispute.TransactionId, dispute.Status, dispute.ReasonCode, dispute.Description, "125.50", "ZAR", dispute.CreatedAt, dispute.UpdatedAt,
        null, null, "Customer", "**** 0001", [], new TransactionSummary(dispute.TransactionId, "REF", "Merchant", "125.50", "ZAR", DisputeTestData.Now),
        AgentAssignment.You, dispute.AssigneeId, dispute.UpdatedAt);

    // ADR-019: a refusal because another agent is reviewing is counted once as agent_dispute_denied and still answered 409.
    [Theory]
    [InlineData("Accepted", "Checked with the merchant.")]
    [InlineData("Rejected", "Checked with the merchant.")]
    [InlineData("Submitted", null)]
    public async Task AMoveOnSomeoneElsesReviewIsCountedAsDeniedAndNothingIsSaved(string to, string? note)
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);
        disputes.FindAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));

        await Assert.ThrowsAsync<NotAssigneeException>(() => commands.TransitionAsync(
            dispute.Id, new TransitionRequest(to, note), DisputeETag.For(dispute.Version), "agent-intruder", CancellationToken.None));

        Assert.Equal(SecurityMetrics.AgentDisputeDenied, Assert.Single(meters.Of("audit.events")).Tags["action"]);
        Assert.Empty(meters.Of("disputes.transitions"));
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task TheAssigneesOwnMoveIsNotCountedAsDenied()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);
        disputes.FindAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));
        disputes.GetDetailAsync(dispute.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<VersionedAgentDispute?>(new VersionedAgentDispute(DetailOf(dispute), 7)));

        await commands.TransitionAsync(
            dispute.Id, new TransitionRequest("Submitted", null), DisputeETag.For(dispute.Version), DisputeTestData.AgentSubject, CancellationToken.None);

        Assert.Equal(SecurityMetrics.AgentDisputeTransitioned, Assert.Single(meters.Of("audit.events")).Tags["action"]);
    }

    // The decision is stored, then the dispute cannot be read back: still an agent's 404, still not a probe of someone's data.
    [Fact]
    public async Task ADecisionWhoseDisputeCannotBeReadBackIsANotFoundThatIsNotCounted()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);
        disputes.FindAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));
        disputes.GetDetailAsync(dispute.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<VersionedAgentDispute?>(null));

        await Assert.ThrowsAsync<NotFoundException>(() => commands.TransitionAsync(
            dispute.Id, new TransitionRequest("Accepted", "Checked with the merchant."), DisputeETag.For(dispute.Version), DisputeTestData.AgentSubject, CancellationToken.None));

        Assert.Equal(DisputeStatus.Accepted, dispute.Status);
        Assert.Empty(meters.Of("security.events"));
        Assert.Single(meters.Of("audit.events"));
    }
}
