namespace Hookwright.Core.Identifiers;

/// <summary>
/// Identifies a subscriber.
/// </summary>
public readonly record struct SubscriberId : IPrefixedId<SubscriberId>, IParsable<SubscriberId>
{
    /// <summary>
    /// Creates an identifier around an existing value.
    /// </summary>
    public SubscriberId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public static string Prefix => "sub";

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>
    /// Mints a new identifier from the current time.
    /// </summary>
    public static SubscriberId New()
    {
        return new SubscriberId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static SubscriberId FromGuid(Guid value)
    {
        return new SubscriberId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return PrefixedId.Format<SubscriberId>(Value);
    }

    /// <inheritdoc />
    public static SubscriberId Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out SubscriberId result)
            ? result
            : throw new FormatException($"'{s}' is not a valid subscriber id.");
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out SubscriberId result)
    {
        return PrefixedId.TryParse(s, out result);
    }
}