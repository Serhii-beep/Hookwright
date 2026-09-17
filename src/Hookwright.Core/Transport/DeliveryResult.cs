using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Transport;

/// <summary>
/// What one attempt at a destination produced.
/// </summary>
public sealed class DeliveryResult
{
    /// <summary>
    /// Creates the result.
    /// </summary>
    public DeliveryResult(DeliveryAttempt attempt, TimeSpan? requestedDelay)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        Attempt = attempt;
        RequestedDelay = requestedDelay;
    }

    /// <summary>
    /// The attempt to record.
    /// </summary>
    public DeliveryAttempt Attempt { get; }

    /// <summary>
    /// How long the destination asked to wait before the next attempt, when it said.
    /// </summary>
    public TimeSpan? RequestedDelay { get; }
}