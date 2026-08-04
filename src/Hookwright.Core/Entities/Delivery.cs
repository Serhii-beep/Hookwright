using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Entities;

/// <summary>
/// One event bound for one endpoint: the unit of work the dispatcher claims, retries,
/// and eventually completes. Fanning out at publish time gives every endpoint its own retry
/// state, so one dead consumer cannot hold up another.
/// </summary>
public sealed class Delivery
{
    /// <summary>
    /// Maximum length of <see cref="LeaseOwner"/>.
    /// </summary>
    public const int MaxLeaseOwnerLength = 200;

    private Delivery(
        DeliveryId id,
        WebhookEventId eventId,
        WebhookEndpointId endpointId,
        string? partitionKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        EventId = eventId;
        EndpointId = endpointId;
        PartitionKey = partitionKey;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
        State = DeliveryState.Pending;
    }

    /// <summary>
    /// Stable identifier for this delivery.
    /// </summary>
    public DeliveryId Id { get; }

    /// <summary>
    /// The event being delivered.
    /// </summary>
    public WebhookEventId EventId { get; }

    /// <summary>
    /// The endpoint it is going to.
    /// </summary>
    public WebhookEndpointId EndpointId { get; }

    /// <summary>
    /// Where this delivery sits in its lifecycle.
    /// </summary>
    public DeliveryState State { get; private set; }

    /// <summary>
    /// How many attempts have been made so far.
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// The earliest instant this delivery may be attempted. Backoff
    /// is expressed by moving this forward, which is why there is no
    /// separate scheduled state.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>
    /// Copied from the event rather than joined because the claim query filters
    /// on it and a join in the hottest query in the system would be expensive.
    /// </summary>
    public string? PartitionKey { get; }

    /// <summary>
    /// Which worker currently holds this delivery, if any.
    /// </summary>
    public string? LeaseOwner { get; private set; }

    /// <summary>
    /// When the current lease expires. A worker that dies mid-flight has its work reclaimed
    /// after this passes, rather than losing it.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>
    /// When the delivery reached a terminal state.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// When the delivery was created, at publish time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Whether this delivery has finished and will not be attempted again.
    /// </summary>
    public bool IsTerminal => State
        is DeliveryState.Succeeded
        or DeliveryState.Failed
        or DeliveryState.Dead
        or DeliveryState.Cancelled;

    /// <summary>
    /// Creates a delivery for one event and one endpoint,
    /// due immediately.
    /// </summary>
    public static Delivery Create(
        WebhookEventId eventId,
        WebhookEndpointId endpointId,
        string? partitionKey,
        DateTimeOffset createdAt)
    {
        return new Delivery(DeliveryId.New(), eventId, endpointId, partitionKey, createdAt);
    }

    /// <summary>
    /// Whether a worker may claim this delivery at <paramref name="instant"/>.
    /// </summary>
    public bool IsDue(DateTimeOffset instant)
    {
        return State is DeliveryState.Pending && NextAttemptAt <= instant;
    }

    /// <summary>
    /// Whether this delivery is held by a lease that has lapsed, meaning the worker
    /// holding it has almost certainly died and the work should be returned to the queue.
    /// </summary>
    /// <param name="instant"></param>
    /// <returns></returns>
    public bool IsLeaseExpired(DateTimeOffset instant)
    {
        return State is DeliveryState.InFlight && LeaseExpiresAt is { } expiry && expiry <= instant;
    }
}