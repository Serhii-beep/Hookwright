using System.Collections.Frozen;
using System.Diagnostics.Metrics;

using Hookwright.Core.Deliveries;

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

    private static readonly FrozenDictionary<AttemptOutcome, KeyValuePair<string, object?>> OutcomeTags =
        Enum.GetValues<AttemptOutcome>().ToFrozenDictionary(
            outcome => outcome,
            outcome => new KeyValuePair<string, object?>("outcome", outcome.ToString()));

    internal static readonly Counter<long> Claimed = Meter.CreateCounter<long>(
        "hookwright.deliveries.claimed", "{delivery}", "Deliveries taken under a lease.");

    internal static readonly Counter<long> Reclaimed = Meter.CreateCounter<long>(
        "hookwright.leases.reclaimed", "{delivery}", "Deliveries returned to the queue because the worker holding them never reported back.");

    internal static readonly Counter<long> Released = Meter.CreateCounter<long>(
        "hookwright.leases.released", "{delivery}", "Deliveries handed back by a worker that stopped before starting them.");

    internal static readonly Counter<long> Attempted = Meter.CreateCounter<long>(
        "hookwright.deliveries.attempted", "{delivery}", "Attempts made, tagged with their outcome.");

    internal static readonly Counter<long> Stale = Meter.CreateCounter<long>(
        "hookwright.deliveries.stale", "{delivery}", "Deliveries left to the reaper because too little of their lease remained to attempt them.");

    internal static readonly Counter<long> Late = Meter.CreateCounter<long>(
        "hookwright.completions.late", "{delivery}", "Results the store dropped because the lease had lapsed and the delivery been reclaimed.");

    internal static readonly Counter<long> Lost = Meter.CreateCounter<long>(
        "hookwright.completions.lost", "{delivery}", "Results the store could not record. Their leases lapse and the deliveries are attempted again.");

    internal static void CountAttempt(AttemptOutcome outcome)
    {
        Attempted.Add(1, OutcomeTags[outcome]);
    }
}