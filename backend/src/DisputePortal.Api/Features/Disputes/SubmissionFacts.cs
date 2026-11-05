namespace DisputePortal.Api.Features.Disputes;

/// <summary>What a submission checks against the database, read just before the insert.</summary>
/// <param name="BlockingDisputeId">An active, Accepted or Rejected dispute on the transaction (409 dispute_exists).</param>
/// <param name="ActiveDisputeCount">The customer's disputes in Submitted or UnderReview.</param>
/// <param name="TransactionDisputeCount">Every dispute ever raised on the transaction.</param>
public sealed record SubmissionFacts(Guid? BlockingDisputeId, int ActiveDisputeCount, int TransactionDisputeCount);
