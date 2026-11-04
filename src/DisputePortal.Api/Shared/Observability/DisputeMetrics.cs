using System.Diagnostics.Metrics;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Shared.Observability;

/// <summary>Domain counters: submissions by reason, transitions by target status, eligibility refusals by rule.</summary>
public sealed class DisputeMetrics
{
    public const string MeterName = "DisputePortal.Disputes";

    private readonly Counter<long> submitted;
    private readonly Counter<long> transitions;
    private readonly Counter<long> eligibilityRejections;

    public DisputeMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        submitted = meter.CreateCounter<long>("disputes.submitted", unit: "{dispute}", description: "Disputes created, by reason code.");
        transitions = meter.CreateCounter<long>("disputes.transitions", unit: "{transition}", description: "Status changes after submission, by target status.");
        eligibilityRejections = meter.CreateCounter<long>(
            "disputes.eligibility_rejections", unit: "{violation}", description: "Eligibility rule violations on submission, by rule code.");
    }

    public void Submitted(string reasonCode) => submitted.Add(1, new KeyValuePair<string, object?>("reason", reasonCode));

    public void Transitioned(DisputeStatus to) => transitions.Add(1, new KeyValuePair<string, object?>("to", to.ToString()));

    public void EligibilityRejected(string ruleCode) => eligibilityRejections.Add(1, new KeyValuePair<string, object?>("rule", ruleCode));
}
