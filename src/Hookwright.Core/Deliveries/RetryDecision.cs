namespace Hookwright.Core.Deliveries;

/// <summary>
/// What should happen to a delivery after an attempt.
/// </summary>
public abstract record RetryDecision
{
    private RetryDecision()
    {

    }

    /// <summary>
    /// The endpoint accepted the event. Nothing further is needed.
    /// </summary>
    public sealed record Succeeded : RetryDecision;

    /// <summary>
    /// Try again, no earlier than <see cref="NextAttemptAt"/>.
    /// </summary>
    public sealed record Retry : RetryDecision
    {
        /// <summary>
        /// Creates the decision.
        /// </summary>
        /// <param name="nextAttemptAt">When the delivery becomes eligible again.</param>
        public Retry(DateTimeOffset nextAttemptAt)
        {
            NextAttemptAt = nextAttemptAt;
        }

        /// <summary>
        /// When the delivery becomes eligible again.
        /// </summary>
        public DateTimeOffset NextAttemptAt { get; }
    }

    /// <summary>
    /// The endpoint rejected the event in a way retrying cannot fix. The delivery is
    /// abandoned.
    /// </summary>
    public sealed record Fail : RetryDecision;

    /// <summary>
    /// Every attempt the schedule allows has been used without success.
    /// </summary>
    public sealed record Exhausted : RetryDecision;

    /// <summary>
    /// The endpoint asked to stop receiving events. This delivery is abandoned
    /// <em>and</em> the endpoint is disabled.
    /// </summary>
    public sealed record RetireEndpoint : RetryDecision;
}