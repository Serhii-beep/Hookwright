using System.Collections.Concurrent;

using Hookwright.Core.Configuration;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;

namespace Hookwright.Dispatch;

/// <summary>
/// This instance's circuits, one per endpoint it has sent to.
/// </summary>
/// <remarks>
/// A circuit is made the first time a claim for its endpoint is seen, seeded from
/// the claim's view of the endpoint, and kept for the life of the instance: one per
/// endpoint ever sent to, which is bounded by the endpoints that exist. The ceiling
/// is <see cref="DeliveryOptions.MaxConnectionsPerEndpoint"/>.
/// </remarks>
public sealed class EndpointCircuits
{
    /// <summary>
    /// The most endpoints <see cref="Refusing"/> names.
    /// </summary>
    public const int MaxRefusing = 256;

    private readonly ConcurrentDictionary<WebhookEndpointId, EndpointCircuit> _circuits = new();
    private readonly int _ceiling;
    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _disableAfter;

    /// <summary>
    /// Creates the registry.
    /// </summary>
    /// <param name="delivery">The connections per endpoint, which is each circuit's ceiling.</param>
    /// <param name="circuit">The cooldown and the allowance every circuit is made with.</param>
    public EndpointCircuits(DeliveryOptions delivery, CircuitOptions circuit)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(circuit);

        _ceiling = delivery.MaxConnectionsPerEndpoint;
        _cooldown = circuit.Cooldown;
        _disableAfter = circuit.DisableAfter;
    }

    /// <summary>
    /// How many endpoints this instance has a circuit for.
    /// </summary>
    public int Count => _circuits.Count;

    /// <summary>
    /// The circuit for the endpoint <paramref name="claimed"/> is going to, made from the claim's
    /// view of the endpoint if this instance has not seen it before.
    /// </summary>
    public EndpointCircuit For(ClaimedDelivery claimed)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        EndpointCircuit circuit = _circuits.GetOrAdd(
            claimed.EndpointId,
            static (_, seed) => new EndpointCircuit(
                seed.Circuits._ceiling,
                seed.Circuits._cooldown,
                seed.Circuits._disableAfter,
                seed.Claimed.ConsecutiveFailures,
                seed.Claimed.FailingSince),
            (Circuits: this, Claimed: claimed));

        circuit.Reconcile(claimed.FailingSince);

        return circuit;
    }

    /// <summary>
    /// The circuit for <paramref name="endpointId"/>, or <see langword="null"/> if this instance
    /// has not sent to that endpoint.
    /// </summary>
    public EndpointCircuit? Find(WebhookEndpointId endpointId)
    {
        return _circuits.TryGetValue(endpointId, out EndpointCircuit? circuit) ? circuit : null;
    }

    /// <summary>
    /// The endpoints whose circuit would refuse a delivery now: every slot taken, or open with
    /// no probe due. What the claimer tells the store to leave in the queue, <see cref="MaxRefusing"/>
    /// at most.
    /// </summary>
    /// <param name="now">The current instant.</param>
    public IReadOnlyCollection<WebhookEndpointId> Refusing(DateTimeOffset now)
    {
        List<WebhookEndpointId> refusing = [];
        List<WebhookEndpointId> open = [];

        foreach ((WebhookEndpointId endpointId, EndpointCircuit circuit) in _circuits)
        {
            if (circuit.Room(now) > 0)
            {
                continue;
            }

            (circuit.ReopensAt is null ? refusing : open).Add(endpointId);
        }

        refusing.AddRange(open);

        return refusing.Count <= MaxRefusing ? refusing : refusing.GetRange(0, MaxRefusing);
    }
}