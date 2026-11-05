using System.Diagnostics.Metrics;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Shared.Observability;

/// <summary>
/// Domain metrics: submissions by reason, transitions by target status, eligibility refusals by rule, and how long
/// disputes take to resolve.
/// </summary>
public sealed class DisputeMetrics
{
    public const string MeterName = "DisputePortal.Disputes";

    private const string ResolutionDurationName = "disputes.resolution.duration";

    // Seconds: from a minute to two weeks. The SDK's default boundaries (0 to 10 000) suit milliseconds and would put
    // every resolution in two or three buckets.
    private static readonly double[] ResolutionBucketsInSeconds =
        [60, 300, 900, 3_600, 14_400, 43_200, 86_400, 172_800, 345_600, 604_800, 1_209_600];

    private readonly Counter<long> submitted;
    private readonly Counter<long> transitions;
    private readonly Counter<long> eligibilityRejections;
    private readonly Histogram<double> resolutionDuration;

    public DisputeMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        submitted = meter.CreateCounter<long>("disputes.submitted", unit: "{dispute}", description: "Disputes created, by reason code.");
        transitions = meter.CreateCounter<long>("disputes.transitions", unit: "{transition}", description: "Status changes after submission, by target status.");
        eligibilityRejections = meter.CreateCounter<long>(
            "disputes.eligibility_rejections", unit: "{violation}", description: "Eligibility rule violations on submission, by rule code.");
        resolutionDuration = meter.CreateHistogram<double>(
            ResolutionDurationName,
            unit: "s",
            description: "Time from submission to an agent's decision, by outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = ResolutionBucketsInSeconds });
    }

    public void Submitted(string reasonCode) => submitted.Add(1, new KeyValuePair<string, object?>("reason", reasonCode));

    public void Transitioned(DisputeStatus to) => transitions.Add(1, new KeyValuePair<string, object?>("to", to.ToString()));

    public void EligibilityRejected(string ruleCode) => eligibilityRejections.Add(1, new KeyValuePair<string, object?>("rule", ruleCode));

    /// <summary>An agent accepted or rejected a dispute <paramref name="elapsed"/> after it was submitted.</summary>
    public void Resolved(DisputeStatus to, TimeSpan elapsed) =>
        resolutionDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("to", to.ToString()));
}
