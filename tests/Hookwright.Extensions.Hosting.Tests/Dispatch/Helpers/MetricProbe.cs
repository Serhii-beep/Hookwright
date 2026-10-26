using System.Diagnostics.Metrics;

using Hookwright.Dispatch;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class MetricProbe : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _total;

    public MetricProbe(string instrument)
    {
        _listener.InstrumentPublished = (candidate, listener) =>
        {
            if (candidate.Meter.Name == DispatchMetrics.MeterName && candidate.Name == instrument)
            {
                listener.EnableMeasurementEvents(candidate);
            }
        };

        _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _total, value));
        _listener.Start();
    }

    public long Total => Interlocked.Read(ref _total);

    public void Dispose()
    {
        _listener.Dispose();
    }
}