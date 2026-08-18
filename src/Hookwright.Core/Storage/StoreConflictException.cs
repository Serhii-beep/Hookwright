namespace Hookwright.Core.Storage;

/// <summary>
/// Thrown when a write would violate a uniqueness rule the store enforces.
/// </summary>
public sealed class StoreConflictException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception with a default message.
    /// </summary>
    public StoreConflictException()
        : base("The write conflicts with a record that already exists.")
    {

    }

    /// <summary>
    /// Create the exception with a specific message.
    /// </summary>
    public StoreConflictException(string message)
        : base(message)
    {

    }

    /// <summary>
    /// Creates the exception with a specific message and cause.
    /// </summary>
    public StoreConflictException(string message, Exception innerException)
        : base(message, innerException)
    {

    }
}