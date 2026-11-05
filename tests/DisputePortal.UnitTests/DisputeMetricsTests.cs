using System.Diagnostics.Metrics;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Domain.Disputes;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DisputePortal.UnitTests;

/// <summary>
/// What the OpenTelemetry SDK (the one Telemetry registers the meter with) would export for the resolution histogram,
/// not just what the instrument records: its name, unit, value and buckets.
/// </summary>
public sealed class DisputeMetricsTests
{
    [Fact]
    public void AResolutionIsExportedInSecondsInBucketsThatSuitHoursAndDays()
    {
        using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        DisputeMetrics metrics = new(services.GetRequiredService<IMeterFactory>());
        ExportedHistogram exported = new();
        using MeterProvider provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(DisputeMetrics.MeterName)
            .AddReader(new BaseExportingMetricReader(exported))
            .Build();

        metrics.Resolved(DisputeStatus.Accepted, TimeSpan.FromMinutes(90));
        provider.ForceFlush();

        Assert.Equal("disputes.resolution.duration", exported.Name);
        Assert.Equal("s", exported.Unit);
        Assert.Equal(1, exported.Count);
        Assert.Equal(5_400, exported.Sum);
        // The SDK's default boundaries stop at 10 000 and start at 0, 5, 10: useless for a decision that takes days.
        Assert.Equal(60, exported.Bounds[0]);
        Assert.Contains(86_400d, exported.Bounds);
        Assert.Contains(1_209_600d, exported.Bounds);
        // 90 minutes is past the one-hour bound and within the four-hour one (a bucket holds values up to and including its bound).
        (double upper, long inBucket) = Assert.Single(exported.Buckets, bucket => bucket.Count > 0);
        Assert.Equal((14_400d, 1L), (upper, inBucket));
        Assert.Equal(3_600d, exported.Bounds[exported.Bounds.IndexOf(upper) - 1]);
        Assert.Equal(("to", "Accepted"), Assert.Single(exported.Tags));
    }

    // The SDK reuses its Metric objects between collections, so everything is read while the export call is running.
    private sealed class ExportedHistogram : BaseExporter<Metric>
    {
        public string? Name { get; private set; }

        public string? Unit { get; private set; }

        public long Count { get; private set; }

        public double Sum { get; private set; }

        public List<double> Bounds { get; } = [];

        public List<(double Bound, long Count)> Buckets { get; } = [];

        public List<(string Key, string? Value)> Tags { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (Metric metric in batch)
            {
                foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
                {
                    Name = metric.Name;
                    Unit = metric.Unit;
                    Count = point.GetHistogramCount();
                    Sum = point.GetHistogramSum();
                    foreach (HistogramBucket bucket in point.GetHistogramBuckets())
                    {
                        Bounds.Add(bucket.ExplicitBound);
                        Buckets.Add((bucket.ExplicitBound, bucket.BucketCount));
                    }

                    foreach (KeyValuePair<string, object?> tag in point.Tags)
                    {
                        Tags.Add((tag.Key, tag.Value?.ToString()));
                    }
                }
            }

            return ExportResult.Success;
        }
    }
}
