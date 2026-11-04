namespace DisputePortal.Api.Shared.Validation;

/// <summary>
/// Attach with <c>.WithState(_ =&gt; new ProblemCode("..."))</c> to give a failing rule its own problem code instead of
/// validation_failed (e.g. sensitive_data), so a client can react to it specifically. The first one found wins.
/// </summary>
public sealed record ProblemCode(string Code);
