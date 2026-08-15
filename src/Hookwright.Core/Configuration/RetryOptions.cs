using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Configuration;

/// <summary>
/// How failed deliveries are rescheduled.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// The backoff ladder and its jitter.
    /// </summary>
    public RetrySchedule Schedule { get; set; } = RetrySchedule.Default;

    /// <summary>
    /// Cap on any single wait, however long the ladder or a destination's
    /// <c>Retry-After</c> asks for. Default 24 hours.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = DefaultRetryPolicy.DefaultMaxDelay;

    /// <summary>
    /// Collects every configuration problem.
    /// </summary>
    /// <returns>The problems found, empty when the configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        OptionsValidator validator = new();

        validator.NotNull(Schedule, nameof(Schedule));
        validator.Positive(MaxDelay, nameof(MaxDelay));

        return validator.Errors;
    }
}