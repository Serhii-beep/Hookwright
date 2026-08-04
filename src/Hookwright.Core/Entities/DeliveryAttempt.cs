using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Entities;

/// <summary>
/// One HTTP attempt made for a delivery. Immutable, like <see cref="WebhookEvent"/>.
/// </summary>
public sealed class DeliveryAttempt
{
    /// <summary>
    /// Maximum stored length of <see cref="ResponseBodySnippet"/>.
    /// </summary>
    public const int MaxResponseBodySnippetLength = 4096;

    /// <summary>
    /// Maximum stored length of <see cref="ErrorDetail"/>.
    /// </summary>
    public const int MaxErrorDetailLength = 1000;

    /// <summary>
    /// Maximum length of <see cref="WorkerId"/>.
    /// </summary>
    public const int MaxWorkerIdLength = 200;

    private readonly Dictionary<string, string> _responseHeaders = new(StringComparer.OrdinalIgnoreCase);

    private DeliveryAttempt(
        DeliveryAttemptId id,
        DeliveryId deliveryId,
        AttemptOutcome outcome,
        DateTimeOffset attemptedAt,
        TimeSpan duration,
        int? responseStatusCode,
        string? responseBodySnippet,
        string? errorDetail,
        string workerId)
    {
        Id = id;
        DeliveryId = deliveryId;
        Outcome = outcome;
        AttemptedAt = attemptedAt;
        Duration = duration;
        ResponseStatusCode = responseStatusCode;
        ResponseBodySnippet = responseBodySnippet;
        ErrorDetail = errorDetail;
        WorkerId = workerId;
    }

    /// <summary>
    /// Stable identifier for this attempt.
    /// </summary>
    public DeliveryAttemptId Id { get; }

    /// <summary>
    /// The delivery this attempt belongs to.
    /// </summary>
    public DeliveryId DeliveryId { get; }

    /// <summary>
    /// What happened.
    /// </summary>
    public AttemptOutcome Outcome { get; }

    /// <summary>
    /// When the request was sent.
    /// </summary>
    public DateTimeOffset AttemptedAt { get; }

    /// <summary>
    /// How long the request took, including reading the response.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// The HTTP status code, or <see langword="null"/> when no response
    /// arrived - a timeout, a connection failure, or a request SSRF guard
    /// refused to make.
    /// </summary>
    public int? ResponseStatusCode { get; }

    /// <summary>
    /// The beginning of the response body, truncated.
    /// </summary>
    public string? ResponseBodySnippet { get; }

    /// <summary>
    /// Response headers retained for diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResponseHeaders => _responseHeaders;

    /// <summary>
    /// Why the attempt failed, when there was no response to explain it.
    /// </summary>
    public string? ErrorDetail { get; }

    /// <summary>
    /// Which worker made the attempt, as <c>{machine}:{pid}:{id}</c>.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Records an attempt that received an HTTP response.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The outcome describes a failure with no response, which contradicts having one.
    /// </exception>
    public static DeliveryAttempt FromResponse(
        DeliveryId deliveryId,
        AttemptOutcome outcome,
        int responseStatusCode,
        string? responseBodySnippet,
        IReadOnlyDictionary<string, string>? responseHeaders,
        DateTimeOffset attemptedAt,
        TimeSpan duration,
        string workerId)
    {
        if (!DescribesAResponse(outcome))
        {
            throw new ArgumentException(
                $"Outcome '{outcome}' means no response was received, so it cannot carry a status code.", nameof(outcome));
        }

        DeliveryAttempt attempt = new(
            DeliveryAttemptId.New(),
            deliveryId,
            outcome,
            attemptedAt,
            Validate(duration),
            responseStatusCode,
            Truncate(responseBodySnippet, MaxResponseBodySnippetLength),
            errorDetail: null,
            ValidateWorkerId(workerId));

        if (responseHeaders is not null)
        {
            foreach ((string name, string value) in responseHeaders)
            {
                attempt._responseHeaders[name] = value;
            }
        }

        return attempt;
    }

    /// <summary>
    /// Records an attempt that never received a response.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The outcome describes an HTTP response, which contradicts not having one.
    /// </exception>
    public static DeliveryAttempt FromFailure(
        DeliveryId deliveryId,
        AttemptOutcome outcome,
        string? errorDetail,
        DateTimeOffset attemptedAt,
        TimeSpan duration,
        string workerId)
    {
        if (DescribesAResponse(outcome))
        {
            throw new ArgumentException(
                $"Outcome '{outcome}' means a response was received, so it needs a status code.", nameof(outcome));
        }

        return new DeliveryAttempt(
            DeliveryAttemptId.New(),
            deliveryId,
            outcome,
            attemptedAt,
            Validate(duration),
            responseStatusCode: null,
            responseBodySnippet: null,
            Truncate(errorDetail, MaxErrorDetailLength),
            ValidateWorkerId(workerId));
    }

    /// <summary>
    /// Whether an outcome implies the endpoint answered at all.
    /// </summary>
    private static bool DescribesAResponse(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.Succeeded or
        AttemptOutcome.Rejected or
        AttemptOutcome.ServerError or
        AttemptOutcome.Throttled or
        AttemptOutcome.Gone => true,

        AttemptOutcome.TimedOut or
        AttemptOutcome.ConnectionFailed or
        AttemptOutcome.Blocked => false,

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unhandled outcome.")
    };

    /// <summary>
    /// Shortens over-long diagnostic text rather than rejecting it.
    /// </summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static TimeSpan Validate(TimeSpan duration)
    {
        return duration >= TimeSpan.Zero
            ? duration
            : throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative.");
    }

    private static string ValidateWorkerId(string workerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        string trimmed = workerId.Trim();

        return trimmed.Length <= MaxWorkerIdLength
            ? trimmed
            : throw new ArgumentException(
                $"Worker id must be at most {MaxWorkerIdLength} characters.",
                nameof(workerId));
    }
}