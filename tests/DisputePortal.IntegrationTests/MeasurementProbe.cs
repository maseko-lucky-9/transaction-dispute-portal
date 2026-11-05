using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Listens to one instrument of one host. The host's <see cref="IMeterFactory"/> is the meter's scope, so a measurement
/// made by another host or an earlier test (each derived host has its own factory) can never reach <see cref="Samples"/>.
/// A measurement is delivered on the thread that records it, before the recording call returns: once a response is
/// back, everything the request recorded is already here.
/// </summary>
public sealed class MeasurementProbe<T> : IDisposable
    where T : struct
{
    private readonly MeterListener listener = new();
    private readonly List<Sample> samples = [];
    private readonly object gate = new();

    private MeasurementProbe(object meterScope, string meterName, string instrumentName)
    {
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (instrument.Name == instrumentName && instrument.Meter.Name == meterName && ReferenceEquals(instrument.Meter.Scope, meterScope))
            {
                Unit = instrument.Unit;
                subscription.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<T>((_, value, tags, _) =>
        {
            Dictionary<string, object?> copy = [];
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (gate)
            {
                samples.Add(new Sample(value, copy));
            }
        });
        listener.Start();
    }

    /// <summary>The instrument's unit once it has been created (null before the first request that creates it).</summary>
    public string? Unit { get; private set; }

    public IReadOnlyList<Sample> Samples
    {
        get
        {
            lock (gate)
            {
                return [.. samples];
            }
        }
    }

    public static MeasurementProbe<T> On(WebApplicationFactory<Program> host, string meterName, string instrumentName) =>
        new(host.Services.GetRequiredService<IMeterFactory>(), meterName, instrumentName);

    public void Dispose() => listener.Dispose();

    public sealed record Sample(T Value, IReadOnlyDictionary<string, object?> Tags);
}
