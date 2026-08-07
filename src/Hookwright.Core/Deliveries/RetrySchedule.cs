namespace Hookwright.Core.Deliveries;

/// <summary>
/// How long to wait between delivery attempts. A ladder of nominal
/// delays, plus how much of each is randomised away so that many
/// deliveries failing together do not all return at the same instant.
/// </summary>
public sealed class RetrySchedule
{
    /// <summary>
    /// Fraction of each nominal delay that is randomised. Half keeps delays within
    /// [half, full] of the ladder value, which spreads a recovering endpoint's
    /// backlog without quietly shortening a schedule that was chosen deliverately.
    /// </summary>
    public const double DefaultJitterFactor = 0.5;

    /// <summary>
    /// The schedule default schedule: 9 retries spread over roughly 3 days. 
    /// </summary>
    public static RetrySchedule Default { get; } = new(
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(5),
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(14),
            TimeSpan.FromHours(20),
            TimeSpan.FromHours(24),
        ],
        DefaultJitterFactor);

    private readonly TimeSpan[] _steps;

    /// <summary>
    /// Creates a schedule from an explicit ladder.
    /// </summary>
    /// <param name="steps">Nominal delay before each retry, in order.</param>
    /// <param name="jitterFactor">
    /// Fraction of each delay to randomise, from 0 (exact) to 1 (full jitter).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The jitter factor is outside 0 to 1.</exception>
    /// <exception cref="ArgumentException">The ladder is empty or contains a negative delay.</exception>
    public RetrySchedule(IEnumerable<TimeSpan> steps, double jitterFactor = DefaultJitterFactor)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (jitterFactor is < 0 or > 1 || double.IsNaN(jitterFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitterFactor),
                jitterFactor,
                "The jitter factor must be between 0 and 1 inclusive.");
        }

        _steps = [.. steps];

        if (_steps.Length == 0)
        {
            throw new ArgumentException("A retry schedule needs at least one step.", nameof(steps));
        }

        if (Array.Exists(_steps, step => step < TimeSpan.Zero))
        {
            throw new ArgumentException("A retry delay cannot be negative.", nameof(steps));
        }

        JitterFactor = jitterFactor;
    }

    /// <summary>
    /// The nominal delay before each retry, in order.
    /// </summary>
    public IReadOnlyList<TimeSpan> Steps => _steps;

    /// <summary>
    /// Fraction of each nominal delay that is randomised.
    /// </summary>
    public double JitterFactor { get; }

    /// <summary>
    /// How many attempts a delivery gets in total.
    /// </summary>
    public int MaxAttempts => _steps.Length + 1;

    /// <summary>
    /// The delay before the next attempt, given how many attempts have already
    /// been made.
    /// </summary>
    /// <param name="completedAttempts">
    /// Attempts made so far.
    /// </param>
    /// <param name="random">
    /// Source of jitter.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No attempts have been made, or the schedule is already exhausted.
    /// </exception>
    public TimeSpan DelayAfter(int completedAttempts, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (completedAttempts < 1 || completedAttempts > _steps.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAttempts),
                completedAttempts,
                $"A schedule with {_steps.Length} steps can only delay after 1 to {_steps.Length} attempts.");
        }

        TimeSpan nominal = _steps[completedAttempts - 1];

        if (JitterFactor == 0 || nominal == TimeSpan.Zero)
        {
            return nominal;
        }

        double jittered = nominal.Ticks * (1 - JitterFactor) + (random.NextDouble() * nominal.Ticks * JitterFactor);

        return TimeSpan.FromTicks((long)jittered);
    }
}