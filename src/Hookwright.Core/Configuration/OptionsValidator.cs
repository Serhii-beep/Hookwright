namespace Hookwright.Core.Configuration;

/// <summary>
/// Accumulates configuration problems.
/// </summary>
internal sealed class OptionsValidator
{
    private readonly List<string> _errors = [];

    /// <summary>
    /// The problems found so far, empty when the configuration is usable.
    /// </summary>
    internal IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Requires a reference to be supplied.
    /// </summary>
    internal void NotNull(object? value, string name)
    {
        if (value is null)
        {
            _errors.Add($"{name} must not be null.");
        }
    }

    /// <summary>
    /// Requires a count to meet a floor.
    /// </summary>
    internal void AtLeast(int value, int minimum, string name)
    {
        if (value < minimum)
        {
            _errors.Add($"{name} must be at least {minimum}, but was {value}.");
        }
    }

    /// <summary>
    /// Requires a duration to be greater than zero.
    /// </summary>
    internal void Positive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            _errors.Add($"{name} must be greater than zero, but was {value}");
        }
    }

    /// <summary>
    /// Records a rule. Used for relashionships between settings, where the
    /// message needs to explain the consequence.
    /// </summary>
    internal void Require(bool condition, string message)
    {
        if (!condition)
        {
            _errors.Add(message);
        }
    }
}