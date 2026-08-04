namespace Hookwright.Core.Identifiers;

/// <summary>
/// Identifies a webhook event — an immutable record of something that happened,
/// which exists independently of any attempt to deliver it.
/// </summary>
public readonly record struct WebhookEventId
    : IPrefixedId<WebhookEventId>, IParsable<WebhookEventId>
{
    /// <summary>Creates an identifier around an existing value.</summary>
    /// <param name="value">The underlying UUIDv7.</param>
    public WebhookEventId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public static string Prefix => "evt";

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Mints a new identifier from the current time.</summary>
    /// <returns>A new <see cref="WebhookEventId"/>.</returns>
    public static WebhookEventId New()
    {
        return new WebhookEventId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static WebhookEventId FromGuid(Guid value)
    {
        return new WebhookEventId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return PrefixedId.Format<WebhookEventId>(Value);
    }

    /// <inheritdoc />
    public static WebhookEventId Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out WebhookEventId result)
            ? result
            : throw new FormatException($"'{s}' is not a valid webhook event id.");
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out WebhookEventId result)
    {
        return PrefixedId.TryParse(s, out result);
    }
}