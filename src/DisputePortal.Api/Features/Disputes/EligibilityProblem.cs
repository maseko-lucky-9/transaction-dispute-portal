using DisputePortal.Domain.Eligibility;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The 422 body: every rule the submission breaks, at once.</summary>
public sealed class EligibilityProblem : ProblemDetails
{
    public IReadOnlyList<EligibilityViolation> Violations { get; init; } = [];
}
