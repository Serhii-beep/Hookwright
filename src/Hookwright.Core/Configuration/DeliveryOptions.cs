namespace Hookwright.Core.Configuration;

/// <summary>
/// Hot the dispatcher claims work and how it makes each delivery.
/// </summary>
public sealed class DeliveryOptions
{
    /// <summary>
    /// Maximum deliveries in flight at once, per instance. Default 64.
    /// </summary>
    public int MaxConcurrency { get; set; } = 64;

    /// <summary>
    /// How many deliveries a worker claims per round trip. Default 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How often to look for due work when idle. Default 5 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a claimed delivery stays claimed before another worker may reclaim it.
    /// Default 60 seconds.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Timeout for a single delivery request, body included. Default 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Connection pool limit per destination host. Default 8.
    /// </summary>
    public int MaxConnectionsPerEndpoint { get; set; } = 8;

    /// <summary>
    /// Collects every configuration problem.
    /// </summary>
    /// <returns>The problems found, empty when the configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        OptionsValidator validator = new();

        validator.AtLeast(MaxConcurrency, 1, nameof(MaxConcurrency));
        validator.AtLeast(BatchSize, 1, nameof(BatchSize));
        validator.AtLeast(MaxConnectionsPerEndpoint, 1, nameof(MaxConnectionsPerEndpoint));
        validator.Positive(PollInterval, nameof(PollInterval));
        validator.Positive(LeaseDuration, nameof(LeaseDuration));
        validator.Positive(RequestTimeout, nameof(RequestTimeout));

        validator.Require(
            LeaseDuration > RequestTimeout,
            $"{nameof(LeaseDuration)} ({LeaseDuration}) must be longer than {nameof(RequestTimeout)} " +
            $"({RequestTimeout}).");

        return validator.Errors;
    }
}