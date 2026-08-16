namespace Hookwright.Core.Storage;

/// <summary>
/// The dispatcher's view of storage: take work under a lease, report results, and recover
/// work whose lease lapsed.
/// </summary>
/// <remarks>
/// Deliberately narrow. This is the only part of storage on the hot path and the only part
/// every provider hand-writes SQL for, so it is separated from the query surface the
/// management API needs — a dispatcher should not be able to see, or a provider be forced to
/// implement, anything else.
/// </remarks>
public interface IDeliveryLeaseStore
{
    /// <summary>
    /// Atomically takes up to <paramref name="batchSize"/> due deliveries under a lease.
    /// </summary>
    /// <param name="leaseOwner">Identifies this worker, as <c>{machine}:{pid}:{id}</c>.</param>
    /// <param name="batchSize">Maximum deliveries to take.</param>
    /// <param name="leaseDuration">How long the claim holds before it may be reclaimed.</param>
    /// <param name="now">The current instant, from the caller's <see cref="TimeProvider"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The claimed deliveries, empty when nothing is due.</returns>
    /// <remarks>
    /// Must be atomic: two workers running this concurrently must never receive the same
    /// delivery. The attempt counter is incremented here rather than on completion, so a
    /// worker that dies after claiming has still consumed an attempt and a delivery that
    /// reliably kills whatever claims it still converges on
    /// <see cref="Deliveries.DeliveryState.Dead"/>.
    /// </remarks>
    Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        string leaseOwner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the results of a batch of attempts.
    /// </summary>
    /// <param name="leaseOwner">The worker reporting, used to prove the claims are still its own.</param>
    /// <param name="completions">The results to apply.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many state changes were applied.</returns>
    /// <remarks>
    /// A state change applies only while <paramref name="leaseOwner"/> still holds the
    /// lease. If a worker stalls long enough for its lease to lapse and another worker
    /// to reclaim the delivery, the stalled worker's result must not overwrite the new claim,
    /// so it is dropped — and the shortfall in the return value makes that visible rather than
    /// silent. The attempt record is appended either way: the request really was made, and
    /// that row is what later explains a duplicate the consumer received.
    /// </remarks>
    Task<int> CompleteAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns deliveries whose lease has lapsed to the queue.
    /// </summary>
    /// <param name="now">The current instant, from the caller's <see cref="TimeProvider"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// How many deliveries were recovered — worth emitting as a metric, since a rising number
    /// means workers are dying or leases are too short.
    /// </returns>
    Task<int> ReclaimExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken);
}