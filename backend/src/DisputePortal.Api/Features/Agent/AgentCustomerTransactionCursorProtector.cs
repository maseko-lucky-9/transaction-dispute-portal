using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// Cursors for a dispute's customer-transactions list: a <see cref="KeysetCursor"/> over (transaction_date, id), bound to
/// the agent's subject and to the dispute it was issued for, with a purpose of its own.
/// </summary>
public sealed class AgentCustomerTransactionCursorProtector(IDataProtectionProvider dataProtection)
{
    private readonly KeysetCursor cursor = new(dataProtection.CreateProtector("DisputePortal.Agent.CustomerTransactions.Cursor.v1"));

    /// <summary>Binds a cursor to the dispute it was issued for: the page of another dispute (of the same customer too) is refused.</summary>
    public static string Fingerprint(Guid disputeId) => KeysetCursor.Fingerprint($"agent-customer-transactions;dispute={disputeId:D}");

    public string Protect(TransactionKey last, string agentSubject, Guid disputeId) =>
        cursor.Protect(last.TransactionDate, last.Id, KeysetCursor.ScopeOf(agentSubject), Fingerprint(disputeId));

    /// <exception cref="InvalidCursorException">Tampered, another agent's, or issued for another dispute.</exception>
    public TransactionKey Unprotect(string value, string agentSubject, Guid disputeId)
    {
        (DateTimeOffset at, Guid id) = cursor.Unprotect(value, KeysetCursor.ScopeOf(agentSubject), Fingerprint(disputeId));
        return new TransactionKey(at, id);
    }
}
