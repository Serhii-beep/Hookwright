using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Entities;

/// <summary>
/// A URL a subscriber registered, together with the
/// event types it wants, its ordering and rate-limit
/// settings, and its current operational health.
/// </summary>
public sealed class WebhookEndpoint
{
    /// <summary>
    /// Maximum length of the endpoint URL.
    /// </summary>
    public const int MaxUrlLength = 2000;

    /// <summary>
    /// Maximum length of <see cref="Description"/>.
    /// </summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>
    /// Maximum number of patterns in <see cref="EventTypeFilter"/>.
    /// </summary>
    public const int MaxEventTypeFilters = 100;

    private readonly List<string> _eventTypeFilter = [];

    private WebhookEndpoint(
        WebhookEndpointId id,
        SubscriberId subscriberId,
        Uri url,
        string? description,
        DateTimeOffset createdAt)
    {
        Id = id;
        SubscriberId = subscriberId;
        Url = url;
        Description = description;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Stable identifier for this endpoint.
    /// </summary>
    public WebhookEndpointId Id { get; }

    /// <summary>
    /// The subscriber that owns this endpoint
    /// </summary>
    public SubscriberId SubscriberId { get; }

    /// <summary>
    /// Absolute HTTP or HTTPS URL that events are delivered to.
    /// </summary>
    public Uri Url { get; private set; }

    /// <summary>
    /// Label shown to the subscriber. Optional.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Event type patterns this endpoint subscribes to. An empty filter
    /// means every event type, which is the default for a newly registered
    /// endpoint.
    /// </summary>
    public IReadOnlyList<string> EventTypeFilter => _eventTypeFilter;

    /// <summary>
    /// The ordering guarantee this endpoint has opted into.
    /// </summary>
    public PartitionMode PartitionMode { get; private set; }

    /// <summary>
    /// Maximum requests per second, or <see langword="null"/> for no explicit limit.
    /// </summary>
    public int? RateLimitPerSecond { get; private set; }

    /// <summary>
    /// Current operational state, as shown to the subscriber.
    /// </summary>
    public EndpointHealth Health { get; private set; }

    /// <summary>
    /// Failures since the last success. Drives circuit breaking.
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// Why the endpoint was disabled, or <see langword="null"/> if it is not.
    /// </summary>
    public string? DisabledReason { get; private set; }

    /// <summary>
    /// When the endpoint was disabled, or <see langword="null"/> if it is not.
    /// </summary>
    public DateTimeOffset? DisabledAt { get; private set; }

    /// <summary>
    /// When the subscriber proved ownership by echoing a challenge, or
    /// <see langword="null"/> if that has not happened.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>
    /// When this endpoint was registered.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When this endpoint was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Whether the dispatcher should currently deliver to this endpoint.
    /// </summary>
    public bool IsEnabled => Health is not EndpointHealth.Disabled;

    /// <summary>
    /// Whether ownership of this URL has been verified.
    /// </summary>
    public bool IsVerified => VerifiedAt is not null;

    /// <summary>
    /// Registers a new endpoint, subscribed to every event type.
    /// </summary>
    /// <exception cref="ArgumentException">The URL or description is not acceptable.</exception>
    public static WebhookEndpoint Create(
        SubscriberId subscriberId,
        Uri url,
        string? description,
        DateTimeOffset createdAt)
    {
        ValidateUrl(url, nameof(url));

        return new WebhookEndpoint(
            WebhookEndpointId.New(),
            subscriberId,
            url,
            ValidateDescription(description),
            createdAt);
    }

    /// <summary>
    /// Points this endpoint at a different URL. Ownership verification is reset, because
    /// it was proved for the previous URL.
    /// </summary>
    /// <exception cref="ArgumentException">The URL is not acceptable.</exception>
    public void ChangeUrl(Uri url, DateTimeOffset now)
    {
        ValidateUrl(url, nameof(url));

        if (url == Url)
        {
            return;
        }

        Url = url;
        VerifiedAt = null;
        Touch(now);
    }

    /// <summary>
    /// Updates the description.
    /// </summary>
    /// <exception cref="ArgumentException">The description is too long.</exception>
    public void Describe(string? description, DateTimeOffset now)
    {
        Description = ValidateDescription(description);
        Touch(now);
    }

    /// <summary>
    /// Replaces the subscription filter. Pass <see langword="null"/> or an empty
    /// sequence to receive every event type.
    /// </summary>
    /// <exception cref="ArgumentException">A pattern is malformed, or there are too many.</exception>
    public void SetEventTypeFilter(IEnumerable<string>? patterns, DateTimeOffset now)
    {
        List<string> normalised = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string pattern in patterns ?? [])
        {
            string trimmed = pattern?.Trim() ?? string.Empty;

            if (!EventType.IsValidFilterPattern(trimmed))
            {
                throw new ArgumentException(
                    $"'{pattern}' is not a valid event type filter. Use an exact name such as" +
                    "'order.created', a prefix wildcard such as 'order.*', or '*' for everything.",
                    nameof(patterns));
            }

            if (seen.Add(trimmed))
            {
                normalised.Add(trimmed);
            }
        }

        if (normalised.Count > MaxEventTypeFilters)
        {
            throw new ArgumentException(
                $"An endpoint may subscribe to at most {MaxEventTypeFilters} patterns.",
                nameof(patterns));
        }

        _eventTypeFilter.Clear();
        _eventTypeFilter.AddRange(normalised);
        Touch(now);
    }

    /// <summary>
    /// Sets the ordering guarantee and rate limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rate limit is not positive.</exception>
    public void SetDeliverySettings(PartitionMode partitionMode, int? rateLimitPerSecond, DateTimeOffset now)
    {
        if (rateLimitPerSecond is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rateLimitPerSecond),
                rateLimitPerSecond,
                "A rate limit must be positive. Pass null for no limit.");
        }

        PartitionMode = partitionMode;
        RateLimitPerSecond = rateLimitPerSecond;
        Touch(now);
    }

    /// <summary>
    /// Records that the subscriber proved ownership of this URL.
    /// </summary>
    public void MarkVerified(DateTimeOffset verifiedAt)
    {
        VerifiedAt = verifiedAt;
        Touch(verifiedAt);
    }

    /// <summary>
    /// Stops delivery to this endpoint. Used both when the subscriber switches it off
    /// and when the dispatcher retires it after a 410 or a long failure streak.
    /// </summary>
    /// <exception cref="ArgumentException">No reason was supplied.</exception>
    public void Disable(string reason, DateTimeOffset disabledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Health is EndpointHealth.Disabled)
        {
            return;
        }

        Health = EndpointHealth.Disabled;
        DisabledReason = reason.Trim();
        DisabledAt = disabledAt;
        Touch(disabledAt);
    }

    /// <summary>
    /// Resumes delivery. The failure streak is cleared so a previously failing endpoint
    /// starts from a clean state rather than immediately tripping the breaker again.
    /// </summary>
    /// <param name="now"></param>
    public void Enable(DateTimeOffset now)
    {
        Health = EndpointHealth.Healthy;
        ConsecutiveFailures = 0;
        DisabledReason = null;
        DisabledAt = null;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }

    private static void ValidateUrl(Uri url, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(url, parameterName);

        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint URL must be absolute.", parameterName);
        }

        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"Endpoint URL scheme must be http or https, but was '{url.Scheme}'.",
                parameterName);
        }

        if (!string.IsNullOrEmpty(url.UserInfo))
        {
            throw new ArgumentException("Endpoint URL must not contain credentials.", parameterName);
        }

        if (!string.IsNullOrEmpty(url.Fragment))
        {
            throw new ArgumentException("Endpoint URL must not contain a fragment.", parameterName);
        }

        if (url.AbsoluteUri.Length > MaxUrlLength)
        {
            throw new ArgumentException(
                $"Endpoint URL must be at most {MaxUrlLength} characters.",
                parameterName);
        }
    }

    private static string? ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        string trimmed = description.Trim();

        return trimmed.Length <= MaxDescriptionLength
            ? trimmed
            : throw new ArgumentException(
                $"Description must be at most {MaxDescriptionLength} characters.",
                nameof(description));
    }
}