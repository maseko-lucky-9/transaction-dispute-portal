using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Eligibility;

/// <summary>
/// Everything a rule may look at, taken once so every rule judges the same instant. Later rules (disputed amount,
/// active-dispute caps) add their inputs here; a rule whose input is absent does not apply.
/// </summary>
public sealed record DisputeEligibilityContext(TransactionSnapshot Transaction, DateTimeOffset Now);
