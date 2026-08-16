namespace Hookwright.Core.Deliveries;

/// <summary>
/// Thrown when a delivery is asked to move to a state it cannot reach
/// from its current one.
/// </summary>
public sealed class InvalidDeliveryTransitionException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception for a specific move.
    /// </summary>
    /// <param name="from">The state the delivery is in.</param>
    /// <param name="to">The state it was asked to move to.</param>
    public InvalidDeliveryTransitionException(DeliveryState from, DeliveryState to)
        : base($"A delivery cannot move from {from} to {to}")
    {
        From = from;
        To = to;
    }

    /// <summary>
    /// Creates the exception with a default message.
    /// </summary>
    public InvalidDeliveryTransitionException()
        : base("A delivery was asked to make an illegal state transition.")
    { }

    /// <summary>
    /// Creates the exception with a specific message.
    /// </summary>
    /// <param name="message">Describes the transition that was refused.</param>
    public InvalidDeliveryTransitionException(string message)
        : base(message)
    { }

    /// <summary>
    /// Creates the exception with a specific message and cause.
    /// </summary>
    /// <param name="message">Describes the transition that was refused.</param>
    /// <param name="innerException">The underlying cause.</param>
    public InvalidDeliveryTransitionException(string message, Exception innerException)
        : base(message, innerException)
    { }

    /// <summary>
    /// The state the delivery was in.
    /// </summary>
    public DeliveryState From { get; }

    /// <summary>
    /// The state it was asked to move to.
    /// </summary>
    public DeliveryState To { get; }
}