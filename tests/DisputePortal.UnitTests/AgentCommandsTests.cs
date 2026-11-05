using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Http;
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

    // The decision is stored, then the dispute cannot be read back: still an agent's 404, still not a probe of someone's data.
    [Fact]
    public async Task ADecisionWhoseDisputeCannotBeReadBackIsANotFoundThatIsNotCounted()
    {
        Dispute dispute = DisputeTestData.In(DisputeStatus.UnderReview);
        disputes.FindAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));
        disputes.GetDetailAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<VersionedAgentDispute?>(null));

        await Assert.ThrowsAsync<NotFoundException>(() => commands.TransitionAsync(
            dispute.Id, new TransitionRequest("Accepted", "Checked with the merchant."), DisputeETag.For(dispute.Version), DisputeTestData.AgentSubject, CancellationToken.None));

        Assert.Equal(DisputeStatus.Accepted, dispute.Status);
        Assert.Empty(meters.Of("security.events"));
        Assert.Single(meters.Of("audit.events"));
    }
}
