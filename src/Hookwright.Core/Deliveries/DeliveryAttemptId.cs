using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Deliveries;

/// <summary>
/// Identifies a single HTTP attempt made for a delivery. A delivery accumulates one
/// attempt per try, so the sequence of attempts is its complete audit trail.
/// </summary>
public readonly record struct DeliveryAttemptId
    : IPrefixedId<DeliveryAttemptId>, IParsable<DeliveryAttemptId>
{
    /// <summary>Creates an identifier around an existing value.</summary>
    /// <param name="value">The underlying UUIDv7.</param>
    public DeliveryAttemptId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public static string Prefix => "att";

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Mints a new identifier from the current time.</summary>
    /// <returns>A new <see cref="DeliveryAttemptId"/>.</returns>
    public static DeliveryAttemptId New()
    {
        return new DeliveryAttemptId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static DeliveryAttemptId FromGuid(Guid value)
    {
        return new DeliveryAttemptId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return PrefixedId.Format<DeliveryAttemptId>(Value);
    }

    /// <inheritdoc />
    public static DeliveryAttemptId Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out DeliveryAttemptId result)
            ? result
            : throw new FormatException($"'{s}' is not a valid delivery attempt id.");
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out DeliveryAttemptId result)
    {
        return PrefixedId.TryParse(s, out result);
    }
}