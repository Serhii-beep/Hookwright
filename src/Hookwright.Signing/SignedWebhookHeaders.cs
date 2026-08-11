using System.Globalization;

namespace Hookwright.Signing;

/// <summary>
/// The headers the accompany a signed webhook.
/// </summary>
public sealed record SignedWebhookHeaders
{
    /// <summary>
    /// Name of the header carrying the message id.
    /// </summary>
    public const string IdHeaderName = "webhook-id";

    /// <summary>
    /// Name of the header carrying the signing timestamp.
    /// </summary>
    public const string TimestampHeaderName = "webhook-timestamp";

    /// <summary>
    /// Name of the header carrying the signatures.
    /// </summary>
    public const string SignatureHeaderName = "webhook-signature";

    private SignedWebhookHeaders(string id, string timestamp, string signature)
    {
        Id = id;
        Timestamp = timestamp;
        Signature = signature;
    }

    /// <summary>
    /// Value for <see cref="IdHeaderName"/>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Value for <see cref="TimestampHeaderName"/>: Unix seconds.
    /// </summary>
    public string Timestamp { get; }

    /// <summary>
    /// Value for <see cref="SignatureHeaderName"/>: one or more
    /// space delimited signatures.
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// Signs a message with every supplied key.
    /// </summary>
    /// <exception cref="ArgumentException">No keys were supplied.</exception>
    public static SignedWebhookHeaders Create(
        IWebhookSigner signer,
        string messageId,
        DateTimeOffset timestamp,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<WebhookSecret> secrets)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(secrets);

        if (secrets.Count == 0)
        {
            throw new ArgumentException("At least one signing key is required.", nameof(secrets));
        }

        long seconds = timestamp.ToUnixTimeSeconds();

        string[] signatures = new string[secrets.Count];

        for (int i = 0; i < secrets.Count; i++)
        {
            signatures[i] = signer.Sign(messageId, seconds, payload, secrets[i]);
        }

        return new SignedWebhookHeaders(
            messageId,
            seconds.ToString(CultureInfo.InvariantCulture),
            string.Join(' ', signatures));
    }
}