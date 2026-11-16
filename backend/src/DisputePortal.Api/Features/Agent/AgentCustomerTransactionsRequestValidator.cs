using DisputePortal.Api.Shared.Paging;
using FluentValidation;

namespace DisputePortal.Api.Features.Agent;

public sealed class AgentCustomerTransactionsRequestValidator : AbstractValidator<AgentCustomerTransactionsRequest>
{
    public AgentCustomerTransactionsRequestValidator()
    {
        RuleFor(request => request.Limit).Must(limit => PageLimit.TryParse(limit, out _)).When(request => request.Limit is not null)
            .WithMessage(PageLimit.Message);
        RuleFor(request => request.Cursor).MaximumLength(PageLimit.MaxCursorLength);
    }
}
