namespace DisputePortal.Domain.Eligibility;

/// <summary>One broken rule: a stable code for client logic and a message for people. A value, never thrown.</summary>
public sealed record EligibilityViolation(string Code, string Message);
