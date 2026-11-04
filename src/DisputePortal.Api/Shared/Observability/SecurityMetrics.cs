using System.Diagnostics.Metrics;

namespace DisputePortal.Api.Shared.Observability;

/// <summary>
/// <c>security.events{type}</c>: the counter the runbook alerts on when refusals spike. <c>audit.events{action}</c>:
/// privileged access (an agent viewing or moving a customer's dispute), with a small fixed set of action values.
/// </summary>
public sealed class SecurityMetrics
{
    public const string MeterName = "DisputePortal.Security";

    public const string AuthzDenied = "authz_denied";

    /// <summary>
    /// A customer asked for a transaction or dispute and got the owner-filter 404. The filter hides other customers' rows,
    /// so the code cannot tell "someone else's id" from "no such id" (ADR-007, and by design: an extra lookup that
    /// ignores the filter would defeat it). The counter therefore counts every not-found answered to a customer: a probe
    /// for foreign ids shows as a spike, a stale bookmark as noise. Agents see every dispute, so their 404s are not counted.
    /// The tag is the type alone: an id would make the series unbounded and put a customer's data in the metrics.
    /// </summary>
    public const string Bola404 = "bola_404";

    public const string CsrfRejected = "csrf_rejected";
    public const string RateLimited = "rate_limited";
    public const string SessionRefreshFailed = "session_refresh_failed";

    public const string AgentDisputeViewed = "agent_dispute_viewed";
    public const string AgentDisputeTransitioned = "agent_dispute_transitioned";

    private static readonly HashSet<string> AuditActions = new(StringComparer.Ordinal) { AgentDisputeViewed, AgentDisputeTransitioned };

    private readonly Counter<long> events;
    private readonly Counter<long> audits;

    public SecurityMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        events = meter.CreateCounter<long>("security.events", unit: "{event}", description: "Security-relevant refusals by type.");
        audits = meter.CreateCounter<long>("audit.events", unit: "{event}", description: "Privileged access to customer data, by action.");
    }

    /// <exception cref="ArgumentOutOfRangeException">Not one of the known actions: the tag stays bounded.</exception>
    public void Audit(string action)
    {
        if (!AuditActions.Contains(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Not a known audit action.");
        }

        audits.Add(1, new KeyValuePair<string, object?>("action", action));
    }

    public void Record(string type) => events.Add(1, new KeyValuePair<string, object?>("type", type));
}
