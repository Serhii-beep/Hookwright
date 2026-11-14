namespace Hookwright.Core.Deliveries;

/// <summary>
/// Interprets the result of a delivery attempt: what a response means, and
/// what can be done about it.
/// </summary>
public static class AttemptOutcomes
{
    /// <summary>
    /// Classifies the HTTP status code and endpoint answered with.
    /// </summary>
    /// <remarks>
    /// Transport failures never reach here. No status code maps to
    /// <see cref="AttemptOutcome.TimedOut"/>, <see cref="AttemptOutcome.ConnectionFailed"/>,
    /// <see cref="AttemptOutcome.Blocked"/> or <see cref="AttemptOutcome.Unsendable"/>.
    /// </remarks>
    public static AttemptOutcome FromStatusCode(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and <= 299 => AttemptOutcome.Succeeded,
            410 => AttemptOutcome.Gone,
            408 or 429 or 502 or 504 => AttemptOutcome.Throttled,
            >= 500 and <= 599 => AttemptOutcome.ServerError,
            _ => AttemptOutcome.Rejected
        };
    }

    /// <summary>
    /// Whether another attempt could succeed without anyone changing anything.
    /// </summary>
    public static bool IsRetryable(AttemptOutcome outcome)
    {
        return outcome switch
        {
            AttemptOutcome.ServerError or
            AttemptOutcome.Throttled or
            AttemptOutcome.TimedOut or
            AttemptOutcome.ConnectionFailed => true,

            AttemptOutcome.Blocked => true,

            AttemptOutcome.Succeeded or
            AttemptOutcome.Rejected or
            AttemptOutcome.Gone or
            AttemptOutcome.Unsendable => false,

            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "No retry rule is defined for this outcome.")
        };
    }

    /// <summary>
    /// What the outcome says about the endpoint's health, for its circuit.
    /// </summary>
    /// <remarks>
    /// Any response that is not retryable failure means the endpoint is up, a refusal included.
    /// Only retryable failures count against it. A 410 retires the endpoint and so says nothing
    /// about its health.
    /// </remarks>
    public static HealthSignal HealthSignalOf(AttemptOutcome outcome)
    {
        return outcome switch
        {
            AttemptOutcome.Succeeded or
            AttemptOutcome.Rejected => HealthSignal.Answered,

            AttemptOutcome.ServerError or
            AttemptOutcome.Throttled or
            AttemptOutcome.TimedOut or
            AttemptOutcome.ConnectionFailed or
            AttemptOutcome.Blocked => HealthSignal.Failed,

            AttemptOutcome.Gone or
            AttemptOutcome.Unsendable => HealthSignal.None,

            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "No health signal is defined for this outcome.")
        };
    }

    /// <summary>
    /// Whether this outcome means the endpoint itself should be retired.
    /// </summary>
    /// <param name="outcome">The outcome to classify.</param>
    /// <returns><see langword="true"/> when the endpoint asked to stop receiving events.</returns>
    public static bool RetiresEndpoint(AttemptOutcome outcome)
    {
        return outcome is AttemptOutcome.Gone;
    }
}