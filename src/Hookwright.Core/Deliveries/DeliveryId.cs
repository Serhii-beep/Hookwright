using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Deliveries;

/// <summary>
/// Identifies a delivery — one event bound for one endpoint. This is the unit of work
/// the dispatcher claims, retries and ultimately completes.
/// </summary>
public readonly record struct DeliveryId : IPrefixedId<DeliveryId>, IParsable<DeliveryId>
{
    /// <summary>Creates an identifier around an existing value.</summary>
    /// <param name="value">The underlying UUIDv7.</param>
    public DeliveryId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public static string Prefix => "dlv";

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Mints a new identifier from the current time.</summary>
    /// <returns>A new <see cref="DeliveryId"/>.</returns>
    public static DeliveryId New()
    {
        return new DeliveryId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static DeliveryId FromGuid(Guid value)
    {
        return new DeliveryId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return PrefixedId.Format<DeliveryId>(Value);
    }

    /// <inheritdoc />
    public static DeliveryId Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out DeliveryId result)
            ? result
            : throw new FormatException($"'{s}' is not a valid delivery id.");
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out DeliveryId result)
    {
        return PrefixedId.TryParse(s, out result);
    }
}