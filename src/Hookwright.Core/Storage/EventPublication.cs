using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;

namespace Hookwright.Core.Storage;

/// <summary>
/// One event together with the deliveries it fanned out to.
/// </summary>
public sealed class EventPublication
{
    private EventPublication(WebhookEvent webhookEvent, IReadOnlyList<Delivery> deliveries)
    {
        Event = webhookEvent;
        Deliveries = deliveries;
    }

    /// <summary>
    /// The event being published.
    /// </summary>
    public WebhookEvent Event { get; }

    /// <summary>
    /// One delivery per subscribed endpoint. Empty when no endpoint matched.
    /// </summary>
    public IReadOnlyList<Delivery> Deliveries { get; }

    /// <summary>
    /// Bundles an event with its deliveries.
    /// </summary>
    /// <param name="webhookEvent">The vent being published.</param>
    /// <param name="deliveries">
    /// One delivery per endpoint whose filter matched. May be empty.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A delivery belongs to a different event, or two deliveries target the same
    /// endpoint.
    /// </exception>
    public static EventPublication Create(WebhookEvent webhookEvent, IEnumerable<Delivery> deliveries)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        ArgumentNullException.ThrowIfNull(deliveries);

        Delivery[] fanOut = [.. deliveries];
        HashSet<WebhookEndpointId> targeted = new(fanOut.Length);

        foreach (Delivery delivery in fanOut)
        {
            if (delivery.EventId != webhookEvent.Id)
            {
                throw new ArgumentException(
                    $"Delivery {delivery.Id} belongs to event {delivery.EventId}, not {webhookEvent.Id}.",
                    nameof(deliveries));
            }

            if (!targeted.Add(delivery.EndpointId))
            {
                throw new ArgumentException(
                    $"Endpoint {delivery.EndpointId} appears twice in the fan-out.",
                    nameof(deliveries));
            }
        }

        return new EventPublication(webhookEvent, fanOut);
    }
}