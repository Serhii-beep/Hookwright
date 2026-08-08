namespace Hookwright.Core.Deliveries;

/// <summary>
/// The default policy: a jittered schedule, honouring a destination's requested
/// delay as a lower bound, with a cap on how long any single wait may be.
/// </summary>
public sealed class DefaultRetryPolicy : IRetryPolicy
{
    /// <summary>
    /// Longest any single wait might be, however long the destination asks for.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromHours(24);

    private readonly RetrySchedule _schedule;
    private readonly Random _random;
    private readonly TimeSpan _maxDelay;

    /// <summary>
    /// Creates the policy.
    /// </summary>
    /// <param name="schedule">The backoff ladder.</param>
    /// <param name="random">
    /// Source of jitter, for example <see cref="System.Random.Shared"/>.
    /// </param>
    /// <param name="maxDelay">Cap on a single wait. Defaults to <see cref="DefaultMaxDelay"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The cap is not positive.</exception>
    public DefaultRetryPolicy(RetrySchedule schedule, Random random, TimeSpan? maxDelay = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(random);

        TimeSpan cap = maxDelay ?? DefaultMaxDelay;

        if (cap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), cap, "The maximum delay must be positive.");
        }

        _schedule = schedule;
        _random = random;
        _maxDelay = cap;
    }

    /// <inheritdoc/>
    public RetryDecision Decide(AttemptOutcome outcome, int completedAttempts, TimeSpan? requestedDelay, DateTimeOffset now)
    {
        if (completedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAttempts),
                completedAttempts,
                "A decision follows an attempt, so at least one must have been made.");
        }

        if (outcome is AttemptOutcome.Succeeded)
        {
            return new RetryDecision.Succeeded();
        }

        if (AttemptOutcomes.RetiresEdnpoint(outcome))
        {
            return new RetryDecision.RetireEndpoint();
        }

        if (!AttemptOutcomes.IsRetryable(outcome))
        {
            return new RetryDecision.Fail();
        }

        if (completedAttempts >= _schedule.MaxAttempts)
        {
            return new RetryDecision.Exhausted();
        }

        return new RetryDecision.Retry(now + DelayFor(completedAttempts, requestedDelay));
    }

    private TimeSpan DelayFor(int completedAttempts, TimeSpan? requestedDelay)
    {
        TimeSpan delay = _schedule.DelayAfter(completedAttempts, _random);

        if (requestedDelay is { } requested && requested > delay)
        {
            delay = requested;
        }

        return delay > _maxDelay ? _maxDelay : delay;
    }
}