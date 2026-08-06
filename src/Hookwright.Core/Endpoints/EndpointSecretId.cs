using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Endpoints;

/// <summary>
/// Identifies a signing secret belonging to an endpoint, including secrets retained
/// during a rotation window so that consumers see no signing downtime.
/// </summary>
/// <remarks>
/// This is the identifier of the secret <em>record</em>. It is not the secret value,
/// which carries its own <c>whsec_</c> prefix and is never exposed through this type.
/// </remarks>
public readonly record struct EndpointSecretId
    : IPrefixedId<EndpointSecretId>, IParsable<EndpointSecretId>
{
    /// <summary>Creates an identifier around an existing value.</summary>
    /// <param name="value">The underlying UUIDv7.</param>
    public EndpointSecretId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public static string Prefix => "sec";

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Mints a new identifier from the current time.</summary>
    /// <returns>A new <see cref="EndpointSecretId"/>.</returns>
    public static EndpointSecretId New()
    {
        return new EndpointSecretId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static EndpointSecretId FromGuid(Guid value)
    {
        return new EndpointSecretId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return PrefixedId.Format<EndpointSecretId>(Value);
    }

    /// <inheritdoc />
    public static EndpointSecretId Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out EndpointSecretId result)
            ? result
            : throw new FormatException($"'{s}' is not a valid endpoint secret id.");
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out EndpointSecretId result)
    {
        return PrefixedId.TryParse(s, out result);
    }
}