using DisputePortal.Api.Shared.Errors;
using DisputePortal.Domain.Eligibility;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The eligibility chain refused the submission (422 eligibility_failed, ADR-007), listing every violation.</summary>
public sealed class EligibilityFailedException(IReadOnlyList<EligibilityViolation> violations)
    : ApiException("This transaction cannot be disputed as submitted.", StatusCodes.Status422UnprocessableEntity, ErrorCode)
{
    public const string ErrorCode = "eligibility_failed";

    public IReadOnlyList<EligibilityViolation> Violations { get; } = violations;

    public override ProblemDetails ToProblem() =>
        new EligibilityProblem { Status = Status, Detail = Message, Violations = Violations, Extensions = { ["code"] = Code } };
}
