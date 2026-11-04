using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DisputePortal.Api.Shared.Observability;

namespace DisputePortal.UnitTests;

/// <summary>Records the <c>type</c> tag of every security.events measurement from one meter factory.</summary>
public sealed class SecurityEventListener : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<string> types = new();

    public SecurityEventListener(IMeterFactory meterFactory)
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == SecurityMetrics.MeterName && instrument.Meter.Scope == meterFactory)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "type")
                {
                    types.Enqueue(tag.Value?.ToString() ?? "");
                }
            }
        });
        listener.Start();
    }

    public IReadOnlyCollection<string> Types => types;

    public void Dispose() => listener.Dispose();
}
