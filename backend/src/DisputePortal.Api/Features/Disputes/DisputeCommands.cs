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
/// eligibility (422), then the write. A submission decides under the customer's lock (ADR-014), so the counts the caps
/// are judged on cannot move before the insert; the unique indexes still back up the checks if anything bypasses it.
/// Logs carry ids and codes only, never the description.
/// </summary>
public sealed class DisputeCommands(
    IDisputeRepository disputes,
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    ICustomerLock customerLock,
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
        Guid transactionId = Guid.ParseExact(request.TransactionId!, "D");
        // The app cannot write the read model, so the snapshot needs no lock, and a probe of a foreign id never takes one.
        TransactionSnapshot transaction = await transactions.GetSnapshotAsync(transactionId, cancellationToken) ?? throw TransactionNotFound();
        DisputeSubmission submission = new(
            Guid.ParseExact(request.Id!, "D"), transactionId, CustomerId(), request.ReasonCode!, request.Description!.Trim(), AmountOf(request, transaction));
        bool created;
        try
        {
            created = await unitOfWork.SaveInTransactionAsync(token => DecideAsync(submission, transaction, subject, token), cancellationToken);
        }
        catch (DuplicateKeyException duplicate)
        {
            // Resolved after the rollback: the lock is released, and the reads do not depend on the failed transaction.
            return await ResolveDuplicateAsync(submission.Id, transactionId, duplicate, cancellationToken);
        }

        if (created)
        {
            metrics.Submitted(submission.ReasonCode);
            logger.LogInformation("Dispute {DisputeId} submitted on transaction {TransactionId} with reason {ReasonCode}", submission.Id, transactionId, submission.ReasonCode);
        }

        return new SubmitOutcome(await DetailAsync(submission.Id, cancellationToken), created);
    }

    /// <summary>
    /// One attempt of the submit transaction; the lock is its first statement. The replay check sits inside it, so the
    /// second of two identical requests waits for the first and replays it, and so does a retry after a lost commit
    /// acknowledgement. Queues the dispute and returns true, or returns false for a replay; never writes.
    /// </summary>
    private async Task<bool> DecideAsync(DisputeSubmission submission, TransactionSnapshot transaction, string subject, CancellationToken cancellationToken)
    {
        await customerLock.AcquireAsync(submission.CustomerId, cancellationToken);
        if (await disputes.FindTransactionIdAsync(submission.Id, cancellationToken) is { } usedFor)
        {
            return usedFor == submission.TransactionId ? false : throw DisputeConflictException.KeyReused();
        }

        SubmissionFacts facts = await disputes.GetSubmissionFactsAsync(submission.TransactionId, cancellationToken);
        if (facts.BlockingDisputeId is { } existing)
        {
            throw DisputeConflictException.Exists(existing);
        }

        EnsureEligible(transaction, submission.DisputedAmount, facts);
        disputes.Add(Dispute.Submit(submission, subject, time.GetUtcNow()));
        return true;
    }

    /// <summary>
    /// Ownership (404), then If-Match (428 missing, 412 stale: a stale tag wins over a terminal state, since every change
    /// also changes the tag), then the lifecycle (409). A change that lands between this read and the write fails the
    /// xmin check and is answered like a stale tag: 412 with the current ETag.
    /// </summary>
    public async Task<VersionedDispute> WithdrawAsync(Guid id, string? ifMatch, string subject, CancellationToken cancellationToken)
    {
        Dispute dispute = await disputes.FindAsync(id, cancellationToken) ?? throw DisputeNotFound.ToCustomer(securityMetrics);
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

    private NotFoundException TransactionNotFound()
    {
        securityMetrics.Record(SecurityMetrics.Bola404);
        return new NotFoundException("transaction_not_found", "No transaction with this id.");
    }

    private static Money AmountOf(CreateDisputeRequest request, TransactionSnapshot transaction) =>
        request.Amount is null
            ? transaction.Amount
            : Money.Of(decimal.Parse(request.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), transaction.Amount.Currency);

    // A dispute the customer's own command just found, wrote or replayed that is no longer visible to them: still a customer 404, so counted.
    private async Task<VersionedDispute> DetailAsync(Guid id, CancellationToken cancellationToken) =>
        await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeNotFound.ToCustomer(securityMetrics);

    private Guid CustomerId() =>
        currentCustomer.CustomerId ?? throw new InvalidOperationException("The customer scope filter did not run.");
}
