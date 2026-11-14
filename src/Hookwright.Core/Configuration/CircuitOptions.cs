namespace Hookwright.Core.Configuration;

/// <summary>
/// How an endpoint's circuit behaves once the endpoint fails.
/// </summary>
public sealed class CircuitOptions
{
    /// <summary>
    /// How long an open circuit stays open before it admits one probe. Default 1 minute.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long an endpoint may fail without a single answer before it is disabled and its owner
    /// told. Default 5 days.
    /// </summary>
    public TimeSpan DisableAfter { get; set; } = TimeSpan.FromDays(5);

    /// <summary>
    /// Collects every configuration problem.
    /// </summary>
    /// <returns>The problems found, empty when the configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        OptionsValidator validator = new();

        validator.Positive(Cooldown, nameof(Cooldown));
        validator.Positive(DisableAfter, nameof(DisableAfter));

        validator.Require(
            DisableAfter > Cooldown,
            $"{nameof(DisableAfter)} ({DisableAfter}) must be longer than {nameof(Cooldown)} ({Cooldown}), " +
            "or an endpoint could be disabled before its circuit admits a single probe.");

        return validator.Errors;
    }
}