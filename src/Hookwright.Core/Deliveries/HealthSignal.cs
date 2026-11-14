namespace Hookwright.Core.Deliveries;

/// <summary>
/// What an attempt's outcome says about the endpoint it went to.
/// </summary>
public enum HealthSignal
{
    /// <summary>
    /// Nothing. No request reached the endpoint for a reason of the producer's, or the endpoint
    /// asked to be retired.
    /// </summary>
    None = 0,

    /// <summary>
    /// The endpoint is up: it answered, whether or not it accepted the event.
    /// </summary>
    Answered = 1,

    /// <summary>
    /// The endpoint could not take the event just now: it failed, timed out, could not be reached,
    /// or asked for less.
    /// </summary>
    Failed = 2
}