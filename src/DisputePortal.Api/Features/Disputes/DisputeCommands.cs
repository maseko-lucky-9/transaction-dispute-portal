using System.Globalization;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Eligibility;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// The customer's state changes. Order of checks (ADR-007): ownership (404), idempotent replay, existing dispute (409),
/// eligibility (422), then the write, whose unique indexes back up the checks when requests race.
/// Logs carry ids and codes only, never the description.
/// </summary>
public sealed class DisputeCommands(
    IDisputeRepository disputes,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    ICurrentCustomer currentCustomer,
    DisputeEligibilityPolicy eligibility,
    TimeProvider time,
    DisputeMetrics metrics,
    SecurityMetrics securityMetrics,
    ILogger<DisputeCommands> logger)
{
    /// <exception cref="NotFoundException">The transaction is missing or someone else's (counted as <see cref="SecurityMetrics.Bola404"/>).</exception>
    /// <exception cref="DisputeConflictException">The transaction is already disputed, or the id was used otherwise.</exception>
    /// <exception cref="EligibilityFailedException">One or more eligibility rules refuse it.</exception>
    public async Task<SubmitOutcome> SubmitAsync(CreateDisputeRequest request, string subject, CancellationToken cancellationToken)
    {
        Guid id = Guid.ParseExact(request.Id!, "D");
        Guid transactionId = Guid.ParseExact(request.TransactionId!, "D");
        TransactionSnapshot? transaction = await transactions.GetSnapshotAsync(transactionId, cancellationToken);
        if (transaction is null)
        {
            securityMetrics.Record(SecurityMetrics.Bola404);
            throw new NotFoundException("transaction_not_found", "No transaction with this id.");
        }

        if (await disputes.FindTransactionIdAsync(id, cancellationToken) is { } usedFor)
        {
            return await ReplayAsync(id, transactionId, usedFor, cancellationToken);
        }

        SubmissionFacts facts = await disputes.GetSubmissionFactsAsync(transactionId, cancellationToken);
        if (facts.BlockingDisputeId is { } existing)
        {
            throw DisputeConflictException.Exists(existing);
        }

        Money amount = request.Amount is null
            ? transaction.Amount
            : Money.Of(decimal.Parse(request.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), transaction.Amount.Currency);
        EnsureEligible(transaction, amount, facts);
        Dispute dispute = Dispute.Submit(
            new DisputeSubmission(id, transactionId, CustomerId(), request.ReasonCode!, request.Description!.Trim(), amount), subject, time.GetUtcNow());
        disputes.Add(dispute);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException duplicate)
        {
            return await ResolveDuplicateAsync(id, transactionId, duplicate, cancellationToken);
        }

        metrics.Submitted(dispute.ReasonCode);
        logger.LogInformation("Dispute {DisputeId} submitted on transaction {TransactionId} with reason {ReasonCode}", id, transactionId, dispute.ReasonCode);
        return new SubmitOutcome(await DetailAsync(id, cancellationToken), Created: true);
    }

    /// <summary>
    /// Ownership (404), then If-Match (428 missing, 412 stale: a stale tag wins over a terminal state, since every change
    /// also changes the tag), then the lifecycle (409). A change that lands between this read and the write fails the
    /// xmin check and is answered like a stale tag: 412 with the current ETag.
    /// </summary>
    public async Task<VersionedDispute> WithdrawAsync(Guid id, string? ifMatch, string subject, CancellationToken cancellationToken)
    {
        Dispute? dispute = await disputes.FindAsync(id, cancellationToken);
        if (dispute is null)
        {
            securityMetrics.Record(SecurityMetrics.Bola404);
            throw DisputeNotFound();
        }

        if (ifMatch is null)
        {
            throw new PreconditionRequiredException();
        }

        if (!DisputeETag.Matches(ifMatch, dispute.Version))
        {
            throw new PreconditionFailedException(DisputeETag.For(dispute.Version));
        }

        dispute.Withdraw(Actor.Customer(subject), time.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException conflict)
        {
            uint? current = await disputes.GetVersionAsync(id, cancellationToken);
            throw new PreconditionFailedException(current is { } version ? DisputeETag.For(version) : null, conflict);
        }

        metrics.Transitioned(DisputeStatus.Withdrawn);
        logger.LogInformation("Dispute {DisputeId} withdrawn by the customer", id);
        return await DetailAsync(id, cancellationToken);
    }

    public static NotFoundException DisputeNotFound() => new("dispute_not_found", "No dispute with this id.");

    private void EnsureEligible(TransactionSnapshot transaction, Money amount, SubmissionFacts facts)
    {
        DisputeEligibilityContext context = eligibility.ContextFor(transaction) with
        {
            DisputedAmount = amount,
            ActiveDisputeCount = facts.ActiveDisputeCount,
            TransactionDisputeCount = facts.TransactionDisputeCount,
        };
        IReadOnlyList<EligibilityViolation> violations = eligibility.Evaluate(context);
        if (violations.Count == 0)
        {
            return;
        }

        foreach (EligibilityViolation violation in violations)
        {
            metrics.EligibilityRejected(violation.Code);
        }

        throw new EligibilityFailedException(violations);
    }

    // The same id again: the original answer if it names the same transaction (a retry), else the key was reused.
    private async Task<SubmitOutcome> ReplayAsync(Guid id, Guid requestedTransaction, Guid usedFor, CancellationToken cancellationToken) =>
        usedFor == requestedTransaction
            ? new SubmitOutcome(await DetailAsync(id, cancellationToken), Created: false)
            : throw DisputeConflictException.KeyReused();

    /// <summary>
    /// A unique index refused the insert: a concurrent request won. If this customer's dispute now has this id, it was the
    /// same submission racing (or EF retrying after a lost commit acknowledgement), so replay. Otherwise the partial
    /// index means another dispute became active on the transaction first; the primary key means the id is someone else's.
    /// </summary>
    private async Task<SubmitOutcome> ResolveDuplicateAsync(Guid id, Guid transactionId, DuplicateKeyException duplicate, CancellationToken cancellationToken)
    {
        if (await disputes.FindTransactionIdAsync(id, cancellationToken) is { } usedFor)
        {
            return await ReplayAsync(id, transactionId, usedFor, cancellationToken);
        }

        if (duplicate.ConstraintName == DisputeConfiguration.OneActivePerTransactionIndex
            && (await disputes.GetSubmissionFactsAsync(transactionId, cancellationToken)).BlockingDisputeId is { } winner)
        {
            throw DisputeConflictException.Exists(winner);
        }

        throw DisputeConflictException.IdTaken();
    }

    private async Task<VersionedDispute> DetailAsync(Guid id, CancellationToken cancellationToken) =>
        await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeNotFound();

    private Guid CustomerId() =>
        currentCustomer.CustomerId ?? throw new InvalidOperationException("The customer scope filter did not run.");
}
