namespace Hookwright.Core.Transport;

/// <summary>
/// Thrown when a destination is refused before any connection is made.
/// </summary>
public sealed class DeliveryBlockedException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public DeliveryBlockedException()
    {

    }

    /// <summary>
    /// Creates the exception with a message saying what refused and why.
    /// </summary>
    public DeliveryBlockedException(string message)
        : base(message)
    {

    }

    /// <summary>
    /// Creates the exception with a message and the failure behind it.
    /// </summary>
    public DeliveryBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {

    }
}