namespace Hookwright.Core.Endpoints;

/// <summary>
/// How an endpoint is currently behaving, as shown to its owner in the protal.
/// </summary>
public enum EndpointHealth : short
{
    /// <summary>
    /// Responding normally. Full Delivery concurrency.
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Failing intermittently. Concurrency is being reduced.
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// Failing consistently. No deliveries are being attempted.
    /// </summary>
    Failing = 2,

    /// <summary>
    /// A single probe delivery is in flight to test whether it has recovered.
    /// </summary>
    Recovering = 3,

    /// <summary>
    /// Switched off - it returned 410 Gone, failed past the threshold, or its owner disabled it.
    /// </summary>
    Disabled = 4
}