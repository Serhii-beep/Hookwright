using System.Diagnostics.Metrics;

namespace Hookwright.Dispatch;

/// <summary>
/// The engine's counters, on the one meter an OpenTelemetry pipeline subscribes to by name.
/// </summary>
public static class DispatchMetrics
{
    /// <summary>
    /// The meter every Hookwright instrument hangs off.
    /// </summary>
    public const string MeterName = "Hookwright";

    private static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> Claimed = Meter.CreateCounter<long>(
        "hookwright.deliveries.claimed", "{delivery}", "Deliveries taken under a lease.");

    internal static readonly Counter<long> Reclaimed = Meter.CreateCounter<long>(
        "hookwright.leases.reclaimed", "{delivery}", "Deliveries returned to the queue because the worker holding them never reported back.");

    internal static readonly Counter<long> Released = Meter.CreateCounter<long>(
        "hookwright.leases.released", "{delivery}", "Deliveries handed back by a worker that stopped before starting them.");
}