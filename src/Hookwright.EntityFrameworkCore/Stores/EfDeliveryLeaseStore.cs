using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Hookwright.EntityFrameworkCore.Stores;

/// <summary>
/// <see cref="IDeliveryLeaseStore"/> over Entity Framework Core.
/// </summary>
public abstract class EfDeliveryLeaseStore : IDeliveryLeaseStore
{
    /// <summary>
    /// Creates the store.
    /// </summary>
    protected EfDeliveryLeaseStore(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    /// <summary>
    /// The context holding Hookwright's tables, for a provider writing its own claim SQL.
    /// </summary>
    protected DbContext Context { get; }

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        string leaseOwner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual async Task<int> CompleteAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentNullException.ThrowIfNull(completions);

        if (completions.Count == 0)
        {
            return 0;
        }

        IDbContextTransaction? transaction = Context.Database.CurrentTransaction is null
            ? await Context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            int applied = 0;

            foreach (DeliveryCompletion completion in completions)
            {
                applied += completion.NextAttemptAt is { } nextAttemptAt
                    ? await RequeueAsync(completion, nextAttemptAt, leaseOwner, cancellationToken).ConfigureAwait(false)
                    : await FinishAsync(completion, leaseOwner, cancellationToken).ConfigureAwait(false);

                if (completion.RetireEndpoint)
                {
                    await RetireAsync(completion, cancellationToken).ConfigureAwait(false);
                }
            }

            Context.AddRange(completions.Select(completion => completion.Attempt));
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public virtual async Task<int> ReclaimExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return await Context.Set<Delivery>()
            .Where(delivery => delivery.State == DeliveryState.InFlight && delivery.LeaseExpiresAt <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.State, DeliveryState.Pending)
                    .SetProperty(delivery => delivery.NextAttemptAt, now)
                    .SetProperty(delivery => delivery.LeaseOwner, (string?)null)
                    .SetProperty(delivery => delivery.LeaseExpiresAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gathers everything a worker needs for deliveries it has just claimed.
    /// </summary>
    protected async Task<IReadOnlyList<ClaimedDelivery>> ProjectClaimedAsync(
        IReadOnlyList<DeliveryId> deliveryIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliveryIds);

        if (deliveryIds.Count == 0)
        {
            return [];
        }

        DeliveryId[] ids = [.. deliveryIds];

        var rows = await Context.Set<Delivery>()
            .AsNoTracking()
            .Where(delivery => ids.Contains(delivery.Id))
            .Join(
                Context.Set<WebhookEvent>().AsNoTracking(),
                delivery => delivery.EventId,
                published => published.Id,
                (delivery, published) => new { Delivery = delivery, Event = published })
            .Join(
                Context.Set<WebhookEndpoint>().AsNoTracking(),
                pair => pair.Delivery.EndpointId,
                endpoint => endpoint.Id,
                (pair, endpoint) => new { pair.Delivery, pair.Event, Endpoint = endpoint })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<WebhookEndpointId, List<EndpointSecret>> keys =
            await LoadSigningKeysAsync([.. rows.Select(row => row.Endpoint.Id).Distinct()], now, cancellationToken)
                .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new ClaimedDelivery
            {
                DeliveryId = row.Delivery.Id,
                AttemptCount = row.Delivery.AttemptCount,
                LeaseExpiresAt = row.Delivery.LeaseExpiresAt
                    ?? throw new InvalidOperationException(
                        $"Delivery {row.Delivery.Id} was claimed wihtout a lease expiry."),
                EventId = row.Event.Id,
                EventType = row.Event.Type,
                Payload = row.Event.Payload,
                Headers = row.Event.Headers,
                EndpointId = row.Endpoint.Id,
                Url = row.Endpoint.Url,
                Secrets = keys.TryGetValue(row.Endpoint.Id, out List<EndpointSecret>? found) ? found : []
            })
        ];
    }

    private async Task<IReadOnlyDictionary<WebhookEndpointId, List<EndpointSecret>>> LoadSigningKeysAsync(
        WebhookEndpointId[] endpointIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<EndpointSecret> candidates = await Context.Set<EndpointSecret>()
            .AsNoTracking()
            .Where(secret => endpointIds.Contains(secret.EndpointId)
                && (secret.ValidUntil == null || secret.ValidUntil > now))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Where(secret => secret.IsActiveAt(now))
            .GroupBy(secret => secret.EndpointId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private async Task<int> RequeueAsync(
        DeliveryCompletion completion,
        DateTimeOffset nextAttemptAt,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        return await GuardedByLease(completion, leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.State, completion.State)
                    .SetProperty(delivery => delivery.NextAttemptAt, nextAttemptAt)
                    .SetProperty(delivery => delivery.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(delivery => delivery.LeaseOwner, (string?)null)
                    .SetProperty(delivery => delivery.LeaseExpiresAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> FinishAsync(
        DeliveryCompletion completion,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        return await GuardedByLease(completion, leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.State, completion.State)
                    .SetProperty(delivery => delivery.CompletedAt, completion.CompletedAt)
                    .SetProperty(delivery => delivery.LeaseOwner, (string?)null)
                    .SetProperty(delivery => delivery.LeaseExpiresAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<Delivery> GuardedByLease(DeliveryCompletion completion, string leaseOwner)
    {
        return Context.Set<Delivery>()
            .Where(delivery => delivery.Id == completion.DeliveryId && delivery.LeaseOwner == leaseOwner);
    }

    private async Task RetireAsync(DeliveryCompletion completion, CancellationToken cancellationToken)
    {
        WebhookEndpoint? endpoint = await Context.Set<WebhookEndpoint>()
            .SingleOrDefaultAsync(candidate => candidate.Id == completion.EndpointId, cancellationToken)
            .ConfigureAwait(false);

        endpoint?.Disable(DeliveryCompletion.RetirementReason, completion.Attempt.AttemptedAt);
    }
}