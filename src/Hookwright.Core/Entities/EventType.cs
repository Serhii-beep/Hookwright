namespace Hookwright.Core.Entities;

/// <summary>
/// A declared event name, such as <c>order.created</c>.
/// </summary>
/// <remarks>
/// The name is the primary key - event types are few, stable,
/// and referenced by name everywhere.
/// </remarks>
public sealed class EventType
{
    /// <summary>
    /// Maximum length of <see cref="Name" />.
    /// </summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Maximum length of <see cref="Description"/>.
    /// </summary>
    public const int MaxDescriptionLength = 500;

    private EventType(string name, string? description, DateTimeOffset createdAt)
    {
        Name = name;
        Description = description;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Hierarchical name such as <c>order.created</c>. Segments use
    /// only ASCII letters, digits, and underscores.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// What this event means.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Optional JSON Schema used to validate payloads at publish time.
    /// </summary>
    public string? SchemaJson { get; private set; }

    /// <summary>
    /// When this type was registered
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Whether this type is retired. Archived types remain valid on historical events.
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>
    /// Registers a new EventType.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not a valid event type name.</exception>
    public static EventType Create(string name, string? description, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string trimmed = name.Trim();

        if (!IsValidName(trimmed))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid event type name. Names are full-stop-delimited segments of ASCII letters, digits, and underscores.",
                nameof(name));
        }

        if (description is { Length: > MaxDescriptionLength })
        {
            throw new ArgumentException(
                $"Description must be at most {MaxDescriptionLength} characters.",
                nameof(description));
        }

        return new EventType(trimmed, description?.Trim(), createdAt);
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a well-formed event type name
    /// </summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        if (name[0] == '.' || name[^1] == '.')
        {
            return false;
        }

        bool previousWasSeparator = false;

        foreach (char character in name)
        {
            if (character == '.')
            {
                if (previousWasSeparator)
                {
                    return false;
                }

                previousWasSeparator = true;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }

            previousWasSeparator = false;
        }

        return true;
    }

    /// <summary>
    /// Updates the description.
    /// </summary>
    /// <param name="description"></param>
    public void Describe(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>
    /// Attaches or clears the JSON Schema used to validate payloads.
    /// </summary>
    /// <param name="schemaJson"></param>
    public void SetSchema(string? schemaJson)
    {
        SchemaJson = string.IsNullOrWhiteSpace(schemaJson) ? null : schemaJson;
    }

    /// <summary>
    /// Retires this type without affecting historical events.
    /// </summary>
    public void Archive()
    {
        IsArchived = true;
    }

    /// <summary>
    /// Restores a retired type.
    /// </summary>
    public void Restore()
    {
        IsArchived = false;
    }

    /// <summary>
    /// Whether <paramref name="pattern"/> is a valid subscription
    /// filter: an exact name such as <c>order.created</c>, a prefix
    /// wildcard such as <c>order.*</c>, or <c>*</c> for every event type.
    /// </summary>
    public static bool IsValidFilterPattern(string? pattern)
    {
        if (pattern is null)
        {
            return false;
        }

        if (pattern == "*")
        {
            return true;
        }

        return pattern.EndsWith(".*", StringComparison.Ordinal)
            ? IsValidName(pattern[..^2])
            : IsValidName(pattern);
    }
}