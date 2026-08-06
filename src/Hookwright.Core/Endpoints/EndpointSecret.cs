namespace Hookwright.Core.Endpoints;

/// <summary>
/// A signing key belonging to an endpoint, valid over the half-open interval
/// <c>[ValidFrom, ValidUntil)</c>. An endpoint may hold several at once: during
/// a rotation the old and new keys overlap, every message is signed with both, and
/// the consumer sees no downtime while they switch over.
/// </summary>
/// <remarks>
/// This type never holds plaintext key material. <see cref="ProtectedKey"/> is an
/// opaque blob produced by whatever protects secrets at rest, so a dump of this table -
/// or of these objects - discloses nothing usable.
/// </remarks>
public sealed class EndpointSecret
{
    /// <summary>
    /// Maximum length of <see cref="ProtectedKey"/>.
    /// </summary>
    public const int MaxProtectedKeyLength = 4096;

    private EndpointSecret(
        EndpointSecretId id,
        WebhookEndpointId endpointId,
        string protectedKey,
        SignatureAlgorithm algorithm,
        DateTimeOffset validFrom)
    {
        Id = id;
        EndpointId = endpointId;
        ProtectedKey = protectedKey;
        Algorithm = algorithm;
        ValidFrom = validFrom;
    }

    /// <summary>
    /// Stable identifier for this secret record.
    /// </summary>
    public EndpointSecretId Id { get; }

    /// <summary>
    /// The endpoint this key signs for.
    /// </summary>
    public WebhookEndpointId EndpointId { get; }

    /// <summary>
    /// The key, already encrypted by the caller. Opaque here -
    /// Core has no way to decrypt it.
    /// </summary>
    public string ProtectedKey { get; }

    /// <summary>
    /// The signing scheme this key is used with.
    /// </summary>
    public SignatureAlgorithm Algorithm { get; }

    /// <summary>
    /// When this key starts signing. Inclusive.
    /// </summary>
    public DateTimeOffset ValidFrom { get; }

    /// <summary>
    /// When this key stops signing, exclusive, or <see langword="null"/>
    /// while it is the endpoint's current key.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; private set; }

    /// <summary>
    /// Whether an end to this key's life has been scheduled.
    /// </summary>
    public bool IsRetired => ValidUntil is not null;

    /// <summary>
    /// Issues a new signing key for an endpoint.
    /// </summary>
    /// <param name="endpointId">The endpoint this key signs for.</param>
    /// <param name="protectedKey">The key, already encrypted by the caller.</param>
    /// <param name="algorithm">The signing scheme.</param>
    /// <param name="validFrom">When the key starts signing.</param>
    /// <exception cref="ArgumentException">The protected key is missing or too long.</exception>
    public static EndpointSecret Create(
        WebhookEndpointId endpointId,
        string protectedKey,
        SignatureAlgorithm algorithm,
        DateTimeOffset validFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedKey);

        if (protectedKey.Length > MaxProtectedKeyLength)
        {
            throw new ArgumentException(
                $"Protected key must be at most {MaxProtectedKeyLength} characters.",
                nameof(protectedKey));
        }

        return new EndpointSecret(
            EndpointSecretId.New(),
            endpointId,
            protectedKey,
            algorithm,
            validFrom);
    }

    /// <summary>
    /// Whether this key should sign a message produced at <paramref name="instant"/>.
    /// The interval is half-open: a key retired at <c>T</c> signs up to but not including
    /// <c>T</c>, so no instant is ever covered by both an old key's last moment and its
    /// replacement's first.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset instant)
    {
        return instant >= ValidFrom && (ValidUntil is null || instant < ValidUntil);
    }

    /// <summary>
    /// Schedules the end of this key's life, normally at the close of a rotation window.
    /// </summary>
    /// <remarks>
    /// Calling this again may only bring the end <em>forward</em>. An emergency revocation
    /// must be able to cut a rotation short when a key leaks, but nothing should be able to
    /// extend the life of a key already being retired.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The end would fall before <see cref="ValidFrom"/>, leaving a key that never signs.
    /// </exception>
    public void Retire(DateTimeOffset validUntil)
    {
        if (validUntil < ValidFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validUntil),
                validUntil,
                "A secret cannot stop being valid before it starts.");
        }

        ValidUntil = ValidUntil is { } scheduled && scheduled < validUntil
            ? scheduled
            : validUntil;
    }
}