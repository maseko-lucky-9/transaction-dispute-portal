using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Validation;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// The transition request names no dispute status. <see cref="TransitionRequestValidator"/> refuses this first, so over
/// HTTP it never gets here; this is the command's own guard when it is called without the validator, and it answers
/// with the validator's body (400 validation_failed, the message under <c>toStatus</c>).
/// </summary>
public sealed class UnknownTargetStatusException()
    : ApiException(FieldMessage, StatusCodes.Status400BadRequest, ValidationFilter<TransitionRequest>.DefaultCode)
{
    public const string FieldMessage = "toStatus must be UnderReview, Accepted or Rejected.";

    public const string Field = "toStatus";

    public override ProblemDetails ToProblem() =>
        new HttpValidationProblemDetails(new Dictionary<string, string[]> { [Field] = [FieldMessage] })
        {
            Status = Status,
            Detail = ValidationFilter<TransitionRequest>.InvalidFieldsDetail,
            Extensions = { ["code"] = Code },
        };
}
