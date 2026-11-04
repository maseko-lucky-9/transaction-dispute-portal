using System.Diagnostics.Metrics;

namespace DisputePortal.Api.Shared.Observability;

/// <summary><c>security.events{type}</c>: the counter the runbook alerts on when refusals spike.</summary>
public sealed class SecurityMetrics
{
    public const string MeterName = "DisputePortal.Security";

    public const string AuthzDenied = "authz_denied";
    public const string CsrfRejected = "csrf_rejected";
    public const string RateLimited = "rate_limited";
    public const string SessionRefreshFailed = "session_refresh_failed";

    private readonly Counter<long> events;

    public SecurityMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        events = meter.CreateCounter<long>("security.events", unit: "{event}", description: "Security-relevant refusals by type.");
    }

    public void Record(string type) => events.Add(1, new KeyValuePair<string, object?>("type", type));
}
