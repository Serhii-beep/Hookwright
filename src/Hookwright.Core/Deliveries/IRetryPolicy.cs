namespace Hookwright.Core.Deliveries;

/// <summary>
/// Deicdes the fate of a delivery from the outcome of its latest attempt.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Decides what to do next.
    /// </summary>
    /// <param name="outcome">What the attempt produced.</param>
    /// <param name="completedAttempts">
    /// Attempts made so far, including the one just finished.
    /// </param>
    /// <param name="requestedDelay">
    /// A delay the destination asked for, if any. Over HTTP that
    /// comes from <c>Retry-After</c>.
    /// </param>
    /// <param name="now">The current instant, from the caller's <see cref="TimeProvider"/>.</param>
    /// <returns></returns>
    RetryDecision Decide(AttemptOutcome outcome, int completedAttempts, TimeSpan? requestedDelay, DateTimeOffset now);
}