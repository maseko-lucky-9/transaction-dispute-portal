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
    }
}
