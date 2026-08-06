namespace Hookwright.Core.Deliveries;

/// <summary>
/// What happened on a single HTTP attempt. Records the observation.
/// The retry policy decides what to do about it.
/// </summary>
/// <remarks>
/// Persisted. See the renumbering warning on <see cref="DeliveryState"/>.
/// </remarks>
public enum AttemptOutcome : short
{
    /// <summary>
    /// A 2xx response.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    /// A 4xx response other than 408 or 429. Consumer refused the event.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// A 5xx response. The consumer is broken rather than refusing.
    /// </summary>
    ServerError = 2,

    /// <summary>
    /// 429 or a 502/504 treated as backpressure. Retry later, more slowly.
    /// </summary>
    Throttled = 3,

    /// <summary>
    /// 410 Gone. The endpoint asked to stop receiving events.
    /// </summary>
    Gone = 4,

    /// <summary>
    /// The request exceeded its timeout.
    /// </summary>
    TimedOut = 5,

    /// <summary>
    /// DNS failure, TLS failure, connection refused or reset.
    /// </summary>
    ConnectionFailed = 6,

    /// <summary>
    /// The SSRF guard refused to connect, because the URL resolved to a blocked address.
    /// </summary>
    Blocked = 7
}