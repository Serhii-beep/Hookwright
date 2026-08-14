namespace Hookwright.Signing;

/// <summary>
/// Why a webhook failed verification.
/// </summary>
public enum WebhookVerificationFailure
{
    /// <summary>
    /// Verification succeeded.
    /// </summary>
    None,

    /// <summary>
    /// The <c>webhook-id</c> header was absent or empty.
    /// </summary>
    MissingId,

    /// <summary>
    /// The <c>webhook-timestamp</c> header was absent or empty.
    /// </summary>
    MissingTimestamp,

    /// <summary>
    /// The <c>webhook-signature</c> header was absent or empty.
    /// </summary>
    MissingSignature,

    /// <summary>
    /// The timestamp was not a Unix seconds value.
    /// </summary>
    MalformedTimestamp,

    /// <summary>
    /// The message is older than the tolerance allows.
    /// </summary>
    TimestampTooOld,

    /// <summary>
    /// The message is dated further ahead than the tolerance allows.
    /// </summary>
    TimestampInFuture,

    /// <summary>
    /// No supplied signature matched any configured key.
    /// </summary>
    SignatureMismatch,

    /// <summary>
    /// The signature header carried more signatures than will ever be legitimate.
    /// </summary>
    TooManySignatures
}