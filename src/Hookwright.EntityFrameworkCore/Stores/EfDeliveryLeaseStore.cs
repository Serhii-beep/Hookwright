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
    public async Task<CompletionReport> CompleteAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentNullException.ThrowIfNull(completions);

        if (completions.Count == 0)
        {
            return new CompletionReport(0, []);
        }

        IDbContextTransaction? transaction = Context.Database.CurrentTransaction is null
            ? await Context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            int applied = await UpdateDeliveriesAsync(leaseOwner, completions, cancellationToken).ConfigureAwait(false);
            await UpdateEndpointsAsync(completions, cancellationToken).ConfigureAwait(false);
            List<WebhookEndpoint> retired = await RetireAsync(completions, cancellationToken).ConfigureAwait(false);

            Context.AddRange(completions.Select(completion => completion.Attempt).OfType<DeliveryAttempt>());
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new CompletionReport(applied, retired);
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
    public virtual async Task<IReadOnlyList<ReclaimedLeases>> ReclaimExpiredLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<string?> owners = await Expired(now)
            .Select(delivery => delivery.LeaseOwner)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ReclaimedLeases> reclaimed = [];

        foreach (string owner in owners.OfType<string>())
        {
            int count = await Expired(now)
                .Where(delivery => delivery.LeaseOwner == owner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(delivery => delivery.State, DeliveryState.Pending)
                        .SetProperty(delivery => delivery.NextAttemptAt, now)
                        .SetProperty(delivery => delivery.LeaseOwner, (string?)null)
                        .SetProperty(delivery => delivery.LeaseExpiresAt, (DateTimeOffset?)null),
                    cancellationToken)
                .ConfigureAwait(false);

            if (count > 0)
            {
                reclaimed.Add(new ReclaimedLeases(owner, count));
            }
        }

        return reclaimed;
    }

    /// <inheritdoc />
    public async Task<int> ReleaseAsync(string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);

        return await Context.Set<Delivery>()
            .Where(delivery => delivery.State == DeliveryState.InFlight && delivery.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.State, DeliveryState.Pending)
                    .SetProperty(delivery => delivery.NextAttemptAt, now)
                    .SetProperty(delivery => delivery.LeaseOwner, (string?)null)
                    .SetProperty(delivery => delivery.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(delivery => delivery.AttemptCount, delivery => delivery.AttemptCount - 1),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes each completion to its delivery for every delivery whose lease
    /// <paramref name="leaseOwner"/> still holds.
    /// </summary>
    /// <returns>How many deliveries were still under the lease and were written.</returns>
    protected virtual async Task<int> UpdateDeliveriesAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        int applied = 0;

        foreach (DeliveryCompletion completion in completions)
        {
            applied += completion.NextAttemptAt is { } nextAttemptAt
                ? await RequeueAsync(completion, nextAttemptAt, leaseOwner, cancellationToken).ConfigureAwait(false)
                : await FinishAsync(completion, leaseOwner, cancellationToken).ConfigureAwait(false);
        }

        return applied;
    }

    /// <summary>
    /// Updates endpoints for the specified delivery completions.
    /// </summary>
    /// <param name="completions">The delivery completions to apply.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    protected virtual async Task UpdateEndpointsAsync(
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completions);

        List<WebhookEndpointId> answering = [];

        foreach ((WebhookEndpointId endpointId, EndpointHealthDelta delta) in EndpointHealthDelta.From(completions))
        {
            if (delta.ClearsFailures)
            {
                answering.Add(endpointId);
            }
            else if (delta.Answered)
            {
                await ResetFailuresAsync(endpointId, delta, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await AddFailuresAsync(endpointId, delta, cancellationToken).ConfigureAwait(false);
            }
        }

        if (answering.Count > 0)
        {
            await ClearFailuresAsync([.. answering], cancellationToken).ConfigureAwait(false);
        }
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
                        $"Delivery {row.Delivery.Id} was claimed without a lease expiry."),
                EventId = row.Event.Id,
                EventType = row.Event.Type,
                Payload = row.Event.Payload,
                Headers = row.Event.Headers,
                EndpointId = row.Endpoint.Id,
                Url = row.Endpoint.Url,
                Secrets = keys.TryGetValue(row.Endpoint.Id, out List<EndpointSecret>? found) ? found : [],
                SubscriberId = row.Endpoint.SubscriberId,
                EndpointHealth = row.Endpoint.Health,
                ConsecutiveFailures = row.Endpoint.ConsecutiveFailures,
                FailingSince = row.Endpoint.FailingSince
            })
        ];
    }

    private static EndpointHealth HealthFor(int failures)
    {
        if (failures >= EndpointCircuit.OpenAfter)
        {
            return EndpointHealth.Failing;
        }

        return failures >= EndpointCircuit.DegradeAfter ? EndpointHealth.Degraded : EndpointHealth.Healthy;
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
        int attemptsGivenBack = completion.GivesBackAttempt ? 1 : 0;

        return await GuardedByLease(completion, leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.State, completion.State)
                    .SetProperty(delivery => delivery.NextAttemptAt, nextAttemptAt)
                    .SetProperty(delivery => delivery.CompletedAt, (DateTimeOffset?)null)
                    .SetProperty(delivery => delivery.LeaseOwner, (string?)null)
                    .SetProperty(delivery => delivery.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(delivery => delivery.AttemptCount, delivery => delivery.AttemptCount - attemptsGivenBack),
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

    private IQueryable<Delivery> Expired(DateTimeOffset now)
    {
        return Context.Set<Delivery>()
            .Where(delivery => delivery.State == DeliveryState.InFlight && delivery.LeaseExpiresAt <= now);
    }

    private IQueryable<WebhookEndpoint> Enabled()
    {
        return Context.Set<WebhookEndpoint>().Where(endpoint => endpoint.Health != EndpointHealth.Disabled);
    }

    private async Task ClearFailuresAsync(WebhookEndpointId[] endpointIds, CancellationToken cancellationToken)
    {
        await Enabled()
            .Where(endpoint => endpointIds.Contains(endpoint.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(endpoint => endpoint.ConsecutiveFailures, 0)
                    .SetProperty(endpoint => endpoint.FailingSince, (DateTimeOffset?)null)
                    .SetProperty(endpoint => endpoint.Health, EndpointHealth.Healthy),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ResetFailuresAsync(
        WebhookEndpointId endpointId,
        EndpointHealthDelta delta,
        CancellationToken cancellationToken)
    {
        EndpointHealth health = HealthFor(delta.Failures);

        await Enabled()
            .Where(endpoint => endpoint.Id == endpointId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(endpoint => endpoint.ConsecutiveFailures, delta.Failures)
                    .SetProperty(endpoint => endpoint.FailingSince, delta.FailingSince)
                    .SetProperty(endpoint => endpoint.Health, health),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AddFailuresAsync(
        WebhookEndpointId endpointId,
        EndpointHealthDelta delta,
        CancellationToken cancellationToken)
    {
        int added = delta.Failures;
        DateTimeOffset? since = delta.FailingSince;

        await Enabled()
            .Where(endpoint => endpoint.Id == endpointId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(endpoint => endpoint.ConsecutiveFailures, endpoint => endpoint.ConsecutiveFailures + added)
                    .SetProperty(endpoint => endpoint.FailingSince, endpoint => endpoint.FailingSince ?? since)
                    .SetProperty(
                        endpoint => endpoint.Health,
                        endpoint => endpoint.ConsecutiveFailures + added >= EndpointCircuit.OpenAfter
                            ? EndpointHealth.Failing
                            : endpoint.ConsecutiveFailures + added >= EndpointCircuit.DegradeAfter
                                ? EndpointHealth.Degraded
                                : EndpointHealth.Healthy),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<WebhookEndpoint>> RetireAsync(
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        Dictionary<WebhookEndpointId, (string Reason, DateTimeOffset At)> retiring = [];

        foreach (DeliveryCompletion completion in completions)
        {
            if (completion.RetirementReason is { } reason)
            {
                DateTimeOffset at = completion.CompletedAt
                    ?? throw new InvalidOperationException(
                        $"Completion of {completion.DeliveryId} retires its endpoint without finishing the delivery.");

                retiring.TryAdd(completion.EndpointId, (reason, at));
            }
        }

        if (retiring.Count == 0)
        {
            return [];
        }

        WebhookEndpointId[] ids = [.. retiring.Keys];
        List<WebhookEndpoint> endpoints = await Context.Set<WebhookEndpoint>()
            .Where(endpoint => ids.Contains(endpoint.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<WebhookEndpoint> retired = [];

        foreach (WebhookEndpoint endpoint in endpoints)
        {
            if (!endpoint.IsEnabled)
            {
                continue;
            }

            (string reason, DateTimeOffset at) = retiring[endpoint.Id];
            endpoint.Disable(reason, at);
            retired.Add(endpoint);
        }

        return retired;
    }
}