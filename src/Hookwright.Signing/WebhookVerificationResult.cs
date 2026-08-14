namespace Hookwright.Signing;

/// <summary>
/// The outcome of verifying a webhook.
/// </summary>
public sealed record WebhookVerificationResult
{
    private WebhookVerificationResult(WebhookVerificationFailure failure)
    {
        Failure = failure;
    }

    /// <summary>
    /// A successful verification.
    /// </summary>
    public static WebhookVerificationResult Success { get; } = new(WebhookVerificationFailure.None);

    /// <summary>
    /// Why verification failed, or <see cref="WebhookVerificationFailure.None"/>.
    /// </summary>
    public WebhookVerificationFailure Failure { get; }

    /// <summary>
    /// Whether the webhook is authentic and within the timestamp tolerance.
    /// </summary>
    public bool IsValid => Failure is WebhookVerificationFailure.None;

    /// <summary>
    /// A failed verification.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="WebhookVerificationFailure.None"/> was passed, which would produce a failure
    /// that reports itself as valid.
    /// </exception>
    public static WebhookVerificationResult Failed(WebhookVerificationFailure failure)
    {
        if (failure is WebhookVerificationFailure.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "A failure result requires a reason.");
        }

        return new WebhookVerificationResult(failure);
    }
}