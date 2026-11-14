namespace Hookwright.Core.Endpoints;

/// <summary>
/// An endpoint circuit's answer to a delivery that wants to go now.
/// </summary>
public abstract record Admission
{
    private Admission()
    {

    }

    /// <summary>
    /// The delivery may go. The circuit holds a slot for it until <see cref="EndpointCircuit.Record"/>
    /// or <see cref="EndpointCircuit.Release"/> gives it back.
    /// </summary>
    public sealed record Granted : Admission
    {
        /// <summary>
        /// Creates the admission.
        /// </summary>
        /// <param name="isProbe">Whether this is the one delivery an open circuit lets through.</param>
        public Granted(bool isProbe)
        {
            IsProbe = isProbe;
        }

        /// <summary>
        /// Whether this is the one delivery an open circuit lets through to see whether the endpoint
        /// has recovered.
        /// </summary>
        public bool IsProbe { get; }
    }

    /// <summary>
    /// The delivery may not go now.
    /// </summary>
    public sealed record Refused : Admission
    {
        /// <summary>
        /// Creates the refusal.
        /// </summary>
        /// <param name="reopensAt">When an open circuit will admit a probe, if that is the reason.</param>
        public Refused(DateTimeOffset? reopensAt)
        {
            ReopensAt = reopensAt;
        }

        /// <summary>
        /// When the circuit, being open, will admit a probe. <see langword="null"/> when every slot is
        /// taken or a probe is already in flight, which is a matter of moments rather than of the cooldown.
        /// </summary>
        public DateTimeOffset? ReopensAt { get; }
    }
}