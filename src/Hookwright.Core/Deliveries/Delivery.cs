using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;

namespace Hookwright.Core.Deliveries;

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
    public bool IsTerminal => DeliveryStateMachine.IsTerminal(State);

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

    /// <summary>
    /// Takes ownership of this delivery for one attempt, under a lease that lapses at <paramref name="leaseExpiresAt"/>
    /// </summary>
    /// <param name="leaseOwner">Identifies the worker, as <c>{machine}:{pid}:{id}</c>.</param>
    /// <param name="leaseExpiresAt">When the claim lapses and the work may be reclaimed.</param>
    /// <param name="now">The current instant, supplied by the caller's <see cref="TimeProvider"/></param>
    /// <exception cref="InvalidDeliveryTransitionException">The delivery is not claimable.</exception>
    public void Claim(string leaseOwner, DateTimeOffset leaseExpiresAt, DateTimeOffset now)
    {
        string owner = ValidateLeaseOwner(leaseOwner);

        if (leaseExpiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                leaseExpiresAt,
                "A lease must expire in the future.");
        }

        TransitionTo(DeliveryState.InFlight);

        LeaseOwner = owner;
        LeaseExpiresAt = leaseExpiresAt;

        AttemptCount++;
    }

    /// <summary>
    /// Records that the endpoint accepted the event.
    /// </summary>
    /// <exception cref="InvalidDeliveryTransitionException">The delivery is not in flight.</exception>
    public void MarkSucceeded(DateTimeOffset completedAt)
    {
        TransitionTo(DeliveryState.Succeeded, completedAt);
        ReleaseLease();
    }

    /// <summary>
    /// Records that the endpoint rejected the event in a way retrying cannot fix.
    /// </summary>
    /// <exception cref="InvalidDeliveryTransitionException">The delivery is not in flight.</exception>
    public void MarkFailed(DateTimeOffset completedAt)
    {
        TransitionTo(DeliveryState.Failed, completedAt);
        ReleaseLease();
    }

    /// <summary>
    /// Records that event retry was used without success.
    /// </summary>
    /// <exception cref="InvalidDeliveryTransitionException">The delivery is not in flight.</exception>
    public void MarkDead(DateTimeOffset completedAt)
    {
        TransitionTo(DeliveryState.Dead, completedAt);
        ReleaseLease();
    }

    /// <summary>
    /// Returns this delivery to the queue after a retryable failure, to be attempted again
    /// no earlier than <paramref name="nextAttemptAt"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The delivery is not in flight.</exception>
    public void ScheduleRetry(DateTimeOffset nextAttemptAt)
    {
        if (State is not DeliveryState.InFlight)
        {
            throw new InvalidOperationException("Only an in-flight delivery can be rescheduled.");
        }

        TransitionTo(DeliveryState.Pending);
        ReleaseLease();
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>
    /// Returns this delivery to the queue after its lease expired, which almost always
    /// means the worker holding it died.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The delivery is not in flight, or its lease has not lapsed yet.
    /// </exception>
    public void Reclaim(DateTimeOffset now)
    {
        if (!IsLeaseExpired(now))
        {
            throw new InvalidOperationException(
                State is DeliveryState.InFlight
                    ? "This delivery's lease has not lapsed. Reclaiming it would put two workers on one delivery."
                    : "Only an in-flight delivery can be reclaimed.");
        }

        TransitionTo(DeliveryState.Pending);
        ReleaseLease();
        NextAttemptAt = now;
    }

    /// <summary>
    /// Holds this delivery behind an older incomplete delivery in the same ordering
    /// partition.
    /// </summary>
    /// <exception cref="InvalidDeliveryTransitionException">The delivery is not pending.</exception>
    public void Block()
    {
        TransitionTo(DeliveryState.Blocked);
    }

    /// <summary>
    /// Releases this delivery once the head of its partition has completed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The delivery is not blocked.</exception>
    public void Unblock()
    {
        if (State is not DeliveryState.Blocked)
        {
            throw new InvalidOperationException("Only a blocked delivery can be unblocked.");
        }

        TransitionTo(DeliveryState.Pending);
    }

    /// <summary>
    /// Abandons this delivery because its endpoint was deleted or disabled.
    /// </summary>
    /// <remarks>
    /// An in-flight delivery cannot be cancelled, so a bulk cancellation for a deleted
    /// endpoint has to leave those rows alone and let them finish.
    /// </remarks>
    /// <exception cref="InvalidDeliveryTransitionException">The delivery cannot be cancelled.</exception>
    public void Cancel(DateTimeOffset completedAt)
    {
        TransitionTo(DeliveryState.Cancelled, completedAt);
        ReleaseLease();
    }

    /// <summary>
    /// Reopens a completed delivery so it is attempted again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The delivery has not completed.</exception>
    /// <exception cref="InvalidDeliveryTransitionException">
    /// The delivery was cancelled, so its endpoint no longer exists to retry to.
    /// </exception>
    public void Reopen(DateTimeOffset nextAttemptAt)
    {
        if (!IsTerminal)
        {
            throw new InvalidOperationException("Only a completed delivery can be reopened.");
        }

        TransitionTo(DeliveryState.Pending);

        AttemptCount = 0;
        NextAttemptAt = nextAttemptAt;
    }

    private void TransitionTo(DeliveryState to, DateTimeOffset? completedAt = null)
    {
        DeliveryStateMachine.EnsureCanTransition(State, to);

        if (DeliveryStateMachine.IsTerminal(to))
        {
            CompletedAt = completedAt ?? throw new ArgumentNullException(
                nameof(completedAt), "A terminal transition must record when the delivery completed.");
        }
        else
        {
            CompletedAt = null;
        }

        State = to;
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }

    private static string ValidateLeaseOwner(string leaseOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);

        string trimmed = leaseOwner.Trim();

        return trimmed.Length <= MaxLeaseOwnerLength
            ? trimmed
            : throw new ArgumentException(
                $"Lease owner must be at most {MaxLeaseOwnerLength} characters.",
                nameof(leaseOwner));
    }
}