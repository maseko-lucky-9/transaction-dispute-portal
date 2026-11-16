using DisputePortal.Domain.Common;

namespace DisputePortal.Domain.Disputes;

/// <summary>
/// An agent other than the one reviewing the dispute tried to accept, reject or release it (ADR-019). The API answers
/// 409, as it conflicts with the current state. The message never names the reviewing agent.
/// </summary>
public sealed class NotAssigneeException() : DomainException(ErrorCode, "Another agent is reviewing this dispute.")
{
    public const string ErrorCode = "not_assignee";
}
