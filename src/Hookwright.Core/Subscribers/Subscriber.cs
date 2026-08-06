namespace Hookwright.Core.Subscribers;

/// <summary>
/// One of your customers: the tenant that owns endpoits and receives your events.
/// </summary>
public sealed class Subscriber
{
    /// <summary>
    /// Maximum length of <see cref="ExternalId" />.
    /// </summary>
    public const int MaxExternalIdLength = 200;

    /// <summary>
    /// Maximum length of <see cref="Name" />.
    /// </summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// Used when materialising a persisted row. EF binds these parameters
    /// to mapped properties by name, which is what allows the immutable properties
    /// below to be get-only rather than needing null-forgiving initialisers.
    /// </summary>
    private Subscriber(SubscriberId id, string externalId, string? name, DateTimeOffset createdAt)
    {
        Id = id;
        ExternalId = externalId;
        Name = name;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Stable identifier for this subscriber.
    /// </summary>
    public SubscriberId Id { get; }

    /// <summary>
    /// Your own identifier for this customer, such as <c>org_111</c>. You
    /// supply it, so there is no mapping table to maintain on your side. Unique
    /// across subscribers.
    /// </summary>
    public string ExternalId { get; }

    /// <summary>
    /// Human-readable label shown in the portal. Optional.
    /// </summary>
    public string? Name { get; private set; }

    /// <summary>
    /// When this subscriber was registered.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }
    /// <summary>
    /// When this subscriber was disabled, or <see langword="null"/> while active.
    /// </summary>

    public DateTimeOffset? DisabledAt { get; private set; }

    /// <summary>
    /// Whether events should currently fan out to this subscriber's endpoints.
    /// </summary>
    public bool IsEnabled => DisabledAt is null;

    /// <summary>
    /// Registers a new subscriber.
    /// </summary>
    /// <param name="externalId">Your identifier for this customer.</param>
    /// <param name="name">Optional display label.</param>
    /// <param name="createdAt">Registration time, supplied by the caller's <see cref="TimeProvider"/>.</param>
    /// <exception cref="ArgumentException">The external id is missing or too long.</exception>
    public static Subscriber Create(string externalId, string? name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        string trimmedExternalId = externalId.Trim();

        if (trimmedExternalId.Length > MaxExternalIdLength)
        {
            throw new ArgumentException($"External id must be at most {MaxExternalIdLength} characters.", nameof(externalId));
        }

        string? trimmedName = Normalise(name, MaxNameLength, nameof(name));

        return new Subscriber(SubscriberId.New(), trimmedExternalId, trimmedName, createdAt);
    }

    /// <summary>
    /// Updates the display label.
    /// </summary>
    /// <exception cref="ArgumentException">The name is too long.</exception>
    public void Rename(string? name)
    {
        Name = Normalise(name, MaxNameLength, nameof(name));
    }

    /// <summary>
    /// Stops events fanning out to this subscriber. Existing deliveries are unaffected.
    /// Cancelling those is the dispatcher's decision, not the subscriber's.
    /// </summary>
    public void Disable(DateTimeOffset disabledAt)
    {
        DisabledAt ??= disabledAt;
    }

    /// <summary>
    /// Resumes fan-out to this subscriber.
    /// </summary>
    public void Enable()
    {
        DisabledAt = null;
    }

    private static string? Normalise(string? value, int maxLength, string parameterName)
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