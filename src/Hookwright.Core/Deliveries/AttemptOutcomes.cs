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
    /// <see cref="AttemptOutcome.TimedOut"/>, <see cref="AttemptOutcome.ConnectionFailed"/>
    /// or <see cref="AttemptOutcome.Blocked"/>.
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
            AttemptOutcome.Gone => false,

            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "No retry rule is defined for this outcome.")
        };
    }

    /// <summary>
    /// Whether this outcome means the endpoint itself should be retired.
    /// </summary>
    /// <param name="outcome"></param>
    /// <returns></returns>
    public static bool RetiresEdnpoint(AttemptOutcome outcome)
    {
        return outcome is AttemptOutcome.Gone;
    }
}