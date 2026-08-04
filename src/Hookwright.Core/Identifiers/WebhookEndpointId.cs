namespace Hookwright.Core.Identifiers;

/// <summary>
/// Identifies a webhook endpoint — a URL a subscriber registered, together with its
/// event filters, signing secret and health state.
/// </summary>
public readonly record struct WebhookEndpointId
    : IPrefixedId<WebhookEndpointId>, IParsable<WebhookEndpointId>
{
    /// <summary>Creates an identifier around an existing value.</summary>
    /// <param name="value">The underlying UUIDv7.</param>
    public WebhookEndpointId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public static string Prefix => "ep";

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Mints a new identifier from the current time.</summary>
    /// <returns>A new <see cref="WebhookEndpointId"/>.</returns>
    public static WebhookEndpointId New()
    {
        return new WebhookEndpointId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static WebhookEndpointId FromGuid(Guid value)
    {
        return new WebhookEndpointId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return PrefixedId.Format<WebhookEndpointId>(Value);
    }

    /// <inheritdoc />
    public static WebhookEndpointId Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out WebhookEndpointId result)
            ? result
            : throw new FormatException($"'{s}' is not a valid webhook endpoint id.");
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out WebhookEndpointId result)
    {
        return PrefixedId.TryParse(s, out result);
    }
}