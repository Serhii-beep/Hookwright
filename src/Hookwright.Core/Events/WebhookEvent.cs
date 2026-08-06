using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Events;

/// <summary>
/// An immutable record of something that happened, adressed to one subscriber.
/// Exists independently of any attempt to deliver it, which is what allows a consumer
/// that was offline past the delivery retry window to recover events by paging the log.
/// </summary>
public sealed class WebhookEvent
{
    /// <summary>
    /// Maximum length of <see cref="PartitionKey"/>.
    /// </summary>
    public const int MaxPartitionKeyLength = 200;

    /// <summary>
    /// Maximum length of <see cref="IdempotencyKey"/>.
    /// </summary>
    public const int MaxIdempotencyKeyLength = 200;

    /// <summary>
    /// Maximum number of custom headers an event may carry.
    /// </summary>
    public const int MaxHeaders = 20;

    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    private WebhookEvent(
        WebhookEventId id,
        SubscriberId subscriberId,
        string type,
        string payload,
        string? partitionKey,
        string? idempotencyKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        SubscriberId = subscriberId;
        Type = type;
        Payload = payload;
        PartitionKey = partitionKey;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Stable identifier, sent to consumers as the <c>webhook-id</c> header.
    /// </summary>
    public WebhookEventId Id { get; }

    /// <summary>
    /// The subscriber this event was published to.
    /// </summary>
    public SubscriberId SubscriberId { get; }

    /// <summary>
    /// The event type name, such as <c>order.created</c>.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// The serialised JSON body, stored exactly as it will be
    /// transmitted.
    /// </summary>
    /// <remarks>
    /// This text is never re-serialised. The signature is computed over these exact bytes,
    /// and re-serialising would change whitespace or key order and invalidate it.
    /// </remarks>
    public string Payload { get; }

    /// <summary>
    /// Extra headers to send alongside the standard ones. Names are matched
    /// case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers => _headers;

    /// <summary>
    /// Groups events that must be delivered in order relative to one
    /// another, when the endpoint has opted into
    /// <see cref="Endpoints.PartitionMode.ByKey"/>.
    /// Typically a resource id.
    /// </summary>
    public string? PartitionKey { get; }

    /// <summary>
    /// Caller-supplied key that makes publishing safe to repeat. A second publish with
    /// the same key returns the original event rather than fanning out again.
    /// </summary>
    public string? IdempotencyKey { get; }

    /// <summary>
    /// When the event was published.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Publishes a new event.
    /// </summary>
    /// <exception cref="ArgumentException">Any argument is missing or malformed.</exception>
    public static WebhookEvent Create(
        SubscriberId subscriberId,
        string type,
        string payload,
        DateTimeOffset createdAt,
        string? partitionKey = null,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        string trimmedType = type.Trim();

        if (!EventType.IsValidName(trimmedType))
        {
            throw new ArgumentException($"'{type}' is not a valid event type name.", nameof(type));
        }

        WebhookEvent webhookEvent = new(
            WebhookEventId.New(),
            subscriberId,
            trimmedType,
            payload,
            Limit(partitionKey, MaxPartitionKeyLength, nameof(partitionKey)),
            Limit(idempotencyKey, MaxIdempotencyKeyLength, nameof(idempotencyKey)),
            createdAt);

        webhookEvent.AddHeaders(headers);

        return webhookEvent;
    }

    private void AddHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        if (headers.Count > MaxHeaders)
        {
            throw new ArgumentException($"An event may carry at most {MaxHeaders} custom headers.", nameof(headers));
        }

        foreach ((string name, string value) in headers)
        {
            ValidateHeader(name, value);
            _headers[name] = value;
        }
    }

    private static void ValidateHeader(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || !IsHttpToken(name))
        {
            throw new ArgumentException($"'{name}' is not a valid HTTP header name.", nameof(name));
        }

        // A carriage return or newline in a header value is request splitting.
        if (value is null || value.Any(char.IsControl))
        {
            throw new ArgumentException($"Header '{name}' must not contain control characters.", nameof(value));
        }
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a valid HTTP field name per RFC 9110's
    /// <c>token</c> production.
    /// </summary>
    private static bool IsHttpToken(string value)
    {
        const string AllowedSymbols = "!#$%&'*+-.^_`|~";

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && !AllowedSymbols.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Limit(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"Value must be at most {maxLength} characters.", parameterName);
    }
}