using Hookwright.Core.Deliveries;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Stores;

/// <summary>
/// <see cref="IRetentionStore"/> over Entity Framework Core.
/// </summary>
public sealed class EfRetentionStore : IRetentionStore
{
    private readonly DbContext _context;

    /// <summary>
    /// Creates the store.
    /// </summary>
    public EfRetentionStore(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<PruneResult> PruneAsync(RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        int attempts = await _context.Set<DeliveryAttempt>()
            .Where(attempt => attempt.AttemptedAt < cutoffs.AttemptsBefore)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        int deliveries = await _context.Set<Delivery>()
            .Where(delivery => delivery.CompletedAt != null
                && !_context.Set<DeliveryAttempt>().Any(attempt => attempt.DeliveryId == delivery.Id)
                && ((delivery.State != DeliveryState.Dead && delivery.CompletedAt < cutoffs.DeliveriesBefore)
                    || (delivery.State == DeliveryState.Dead && delivery.CompletedAt < cutoffs.DeadDeliveriesBefore)))
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        int events = await _context.Set<WebhookEvent>()
            .Where(published => published.CreatedAt < cutoffs.DeliveriesBefore
                && !_context.Set<Delivery>().Any(delivery => delivery.EventId == published.Id))
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PruneResult(attempts, deliveries, events);
    }
}