using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Disputes;
using FluentValidation;

namespace DisputePortal.Api.Features.Agent;

public sealed class AgentQueueRequestValidator : AbstractValidator<AgentQueueRequest>
{
    public AgentQueueRequestValidator()
    {
        RuleFor(request => request.Status).IsEnumName(typeof(DisputeStatus), caseSensitive: false).When(request => request.Status is not null)
            .WithMessage("status must be Submitted, UnderReview, Accepted, Rejected or Withdrawn.");
        RuleFor(request => request.Limit).Must(limit => PageLimit.TryParse(limit, out _)).When(request => request.Limit is not null)
            .WithMessage(PageLimit.Message);
        RuleFor(request => request.Cursor).MaximumLength(PageLimit.MaxCursorLength);
        RuleFor(request => request.Assignment).IsEnumName(typeof(AgentQueueAssignment), caseSensitive: false).When(request => request.Assignment is not null)
            .WithMessage("assignment must be unclaimed, mine or others.");
        // Each assignment already fixes the status (unclaimed is Submitted; mine and others are UnderReview).
        RuleFor(request => request.Assignment).Null().When(request => request.Status is not null)
            .WithMessage("assignment cannot be combined with status.");
    }
}
