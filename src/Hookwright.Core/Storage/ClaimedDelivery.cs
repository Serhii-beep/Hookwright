using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Storage;

/// <summary>
/// Everything a worker needs to attempt one delivery, gathered by the claim
/// itself.
/// </summary>
public sealed class ClaimedDelivery
{
    /// <summary>
    /// The delivery being attempted.
    /// </summary>
    public required DeliveryId DeliveryId { get; init; }

    /// <summary>
    /// Attempts made including this one.
    /// </summary>
    public required int AttemptCount { get; init; }

    /// <summary>
    /// When this worker's claim lapses and the delivery may be reclaimed.
    /// </summary>
    public required DateTimeOffset LeaseExpiresAt { get; init; }

    /// <summary>
    /// Sent as the <c>webhook-id</c> header and used as the consumer's
    /// idempotency key.
    /// </summary>
    public required WebhookEventId EventId { get; init; }

    /// <summary>
    /// The event type name, such as <c>order.created</c>.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// The serialised body.
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// Custom headers the publisher attached to the event.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// The destination endpoint, used to key circuit breaking and rate limiting.
    /// </summary>
    public required WebhookEndpointId EndpointId { get; init; }

    /// <summary>
    /// Where to send the event.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Every key valid for this endpoint right now. More than one during a
    /// rotation window.
    /// </summary>
    public required IReadOnlyList<EndpointSecret> Secrets { get; init; }

    /// <summary>
    /// The subscriber the endpoint belongs to.
    /// </summary>
    public required SubscriberId SubscriberId { get; init; }

    /// <summary>
    /// The endpoint's health when the delivery was claimed. A disabled endpoint's
    /// delivery is cancelled rather than sent.
    /// </summary>
    public required EndpointHealth EndpointHealth { get; init; }

    /// <summary>
    /// The endpoint's failures in a row when the delivery was claimed. Seeds the
    /// endpoint's circuit on an instance that has not seen it yet.
    /// </summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>
    /// When the endpoint's current run of failures began, or <see langword="null"/>
    /// while it is answering.
    /// </summary>
    public required DateTimeOffset? FailingSince { get; init; }
}