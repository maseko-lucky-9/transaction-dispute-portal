using System.Diagnostics.Metrics;
using DisputePortal.Api.Shared.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.UnitTests;

/// <summary>
/// The real <see cref="SecurityMetrics"/> and <see cref="DisputeMetrics"/> on a meter factory of their own, with every
/// measurement they make kept for assertions. The factory is the meters' scope, so no other test's metrics can arrive here.
/// </summary>
public sealed class MeterSandbox : IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly MeterListener listener = new();
    private readonly List<Measured> measured = [];

    public MeterSandbox()
    {
        IMeterFactory factory = services.GetRequiredService<IMeterFactory>();
        Security = new SecurityMetrics(factory);
        Disputes = new DisputeMetrics(factory);
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, factory))
            {
                subscription.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Keep(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Keep(instrument, value, tags));
        listener.Start();
    }

    public SecurityMetrics Security { get; }

    public DisputeMetrics Disputes { get; }

    /// <summary>What <c>security.events</c> counted, as the value of each measurement's <c>type</c> tag.</summary>
    public IReadOnlyList<string?> SecurityEventTypes => [.. Of("security.events").Select(sample => sample.Tags["type"]?.ToString())];

    /// <summary>Every measurement of one instrument, in the order it was made.</summary>
    public IReadOnlyList<Measured> Of(string instrument) => [.. measured.Where(sample => sample.Instrument == instrument)];

    public void Dispose()
    {
        listener.Dispose();
        services.Dispose();
    }

    private void Keep(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> copy = [];
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        measured.Add(new Measured(instrument.Name, value, copy));
    }

    public sealed record Measured(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
}
