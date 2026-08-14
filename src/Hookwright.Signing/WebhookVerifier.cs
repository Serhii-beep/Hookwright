using System.Globalization;

namespace Hookwright.Signing;

/// <summary>
/// Verifies that a webhook was signed by a holder of one of the configured keys, and is
/// recent enough not to be a replay.
/// </summary>
public sealed class WebhookVerifier
{
    /// <summary>
    /// Most signatures a single header may carry.
    /// </summary>
    public const int MaxSignatures = 10;

    /// <summary>
    /// How far the signing timestamp may differ from now. Default is 5 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    private readonly IWebhookSigner _signer;
    private readonly TimeSpan _tolerance;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="signer">The scheme to verify against.</param>
    /// <param name="tolerance">Timestamp tolerance. Defaults to <see cref="DefaultTolerance"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance is negative.</exception>
    public WebhookVerifier(IWebhookSigner signer, TimeSpan? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(signer);

        TimeSpan window = tolerance ?? DefaultTolerance;

        if (window < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), window, "The tolerance cannot be negative.");
        }

        _signer = signer;
        _tolerance = window;
    }

    /// <summary>
    /// Verifies a webhook from its header values and the exact body bytes received.
    /// </summary>
    /// <param name="messageId">The received <c>webhook-id</c>, or <see langword="null"/> if absent.</param>
    /// <param name="timestamp">The received <c>webhook-timestamp</c>, or <see langword="null"/> if absent.</param>
    /// <param name="signature">The received <c>webhook-signature</c>, or <see langword="null"/> if absent.</param>
    /// <param name="payload">
    /// The raw request body, exactly as received. Re-serialising a parsed
    /// body before calling this can lead to a verification failure.
    /// </param>
    /// <param name="secrets">
    /// <param name="now">The current instant.</param>
    /// Every key currently valid for the endpoint. During a rotation window that
    /// is two, and a signature from either one verifies.
    /// </param>
    /// 
    /// <exception cref="ArgumentException">No keys were supplied.</exception>
    public WebhookVerificationResult Verify(
        string? messageId,
        string? timestamp,
        string? signature,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<WebhookSecret> secrets,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        if (secrets.Count == 0)
        {
            throw new ArgumentException("At least one key is required to verify.", nameof(secrets));
        }

        if (string.IsNullOrWhiteSpace(messageId))
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.MissingId);
        }

        if (string.IsNullOrWhiteSpace(timestamp))
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.MissingTimestamp);
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.MissingSignature);
        }

        if (!TryReadTimestamp(timestamp, out long seconds, out DateTimeOffset signedAt))
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.MalformedTimestamp);
        }

        TimeSpan age = now - signedAt;

        if (age > _tolerance)
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.TimestampTooOld);
        }

        if (age < -_tolerance)
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.TimestampInFuture);
        }

        string[] candidates = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (candidates.Length > MaxSignatures)
        {
            return WebhookVerificationResult.Failed(WebhookVerificationFailure.TooManySignatures);
        }

        return Matches(messageId, seconds, candidates, payload, secrets)
            ? WebhookVerificationResult.Success
            : WebhookVerificationResult.Failed(WebhookVerificationFailure.SignatureMismatch);
    }

    /// <summary>
    /// Verifies a webhook from a header collection, looking names up
    /// case-insensitively.
    /// </summary>
    /// <param name="headers">The received headers, in any casing.</param>
    /// <param name="payload">
    /// The raw request body, exactly as received. Re-serialising a parsed
    /// body before calling this can lead to a verification failure.
    /// </param>
    /// <param name="secrets">
    /// Every key currently valid for the endpoint. During a rotation window that
    /// is two, and a signature from either one verifies.
    /// </param>
    /// <param name="now">The current instant.</param>
    public WebhookVerificationResult Verify(
        IReadOnlyDictionary<string, string> headers,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<WebhookSecret> secrets,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return Verify(
            Find(headers, SignedWebhookHeaders.IdHeaderName),
            Find(headers, SignedWebhookHeaders.TimestampHeaderName),
            Find(headers, SignedWebhookHeaders.SignatureHeaderName),
            payload,
            secrets,
            now);
    }

    private static string? Find(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach ((string key, string value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadTimestamp(string timestamp, out long seconds, out DateTimeOffset signedAt)
    {
        signedAt = default;

        if (!long.TryParse(
            timestamp.AsSpan().Trim(),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out seconds))
        {
            return false;
        }

        if (seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() ||
            seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        signedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private bool Matches(
        string messageId,
        long seconds,
        string[] candidates,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<WebhookSecret> secrets)
    {
        bool matched = false;

        foreach (string candidate in candidates)
        {
            foreach (WebhookSecret secret in secrets)
            {
                // Non short-circuiting so exit doesn't leak a timing info
                matched |= _signer.Verify(candidate, messageId, seconds, payload, secret);
            }
        }

        return matched;
    }
}