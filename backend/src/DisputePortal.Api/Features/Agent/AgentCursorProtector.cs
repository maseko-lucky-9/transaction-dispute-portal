using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.Api.Features.Agent;

/// <summary>Cursors for the agent queue: a <see cref="KeysetCursor"/> over (created_at, id), bound to the agent's subject.</summary>
public sealed class AgentCursorProtector(IDataProtectionProvider dataProtection)
{
    private readonly KeysetCursor cursor = new(dataProtection.CreateProtector("DisputePortal.Agent.Queue.Cursor.v1"));

    public string Protect(DisputeKey last, string agentSubject, AgentQueueFilter filter) =>
        cursor.Protect(last.CreatedAt, last.Id, KeysetCursor.ScopeOf(agentSubject), filter.Fingerprint());

    /// <exception cref="InvalidCursorException">Tampered, another agent's, or issued for another filter.</exception>
    public DisputeKey Unprotect(string value, string agentSubject, AgentQueueFilter filter)
    {
        (DateTimeOffset at, Guid id) = cursor.Unprotect(value, KeysetCursor.ScopeOf(agentSubject), filter.Fingerprint());
        return new DisputeKey(at, id);
    }
}
