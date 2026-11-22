using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore.Stores;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.PostgreSql;

/// <summary>
/// The PostgreSQL claim.
/// </summary>
public sealed class PostgresDeliveryLeaseStore : EfDeliveryLeaseStore
{
    /// <summary>
    /// Creates the store.
    /// </summary>
    public PostgresDeliveryLeaseStore(DbContext context)
        : base(context)
    {

    }

    /// <inheritdoc />
    protected override IComparer<Guid> EndpointOrder => Comparer<Guid>.Default;

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<Guid>> TakeAsync(
        string leaseOwner,
        int count,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        ClaimRound round,
        Guid after,
        Guid upTo,
        CancellationToken cancellationToken)
    {
        DateTimeOffset dueBy = now.ToUniversalTime();
        DateTimeOffset expiresAt = leaseExpiresAt.ToUniversalTime();
        Guid[] limited = [.. round.Limits.Select(limit => limit.Key.Value)];
        int[] limits = [.. round.Limits.Select(limit => limit.Value)];

        return await Context.Database
            .SqlQuery<Guid>($"""
                WITH RECURSIVE lane AS (
                    (SELECT d.endpoint_id, d.next_attempt_at
                    FROM hookwright_deliveries AS d
                    WHERE d.state = {(short)DeliveryState.Pending}
                        AND d.endpoint_id > {after}
                        AND d.endpoint_id <= {upTo}
                    ORDER BY d.endpoint_id, d.next_attempt_at
                    LIMIT 1)
                    UNION ALL
                    SELECT following.endpoint_id, following.next_attempt_at
                    FROM lane
                    CROSS JOIN LATERAL (
                        SELECT d.endpoint_id, d.next_attempt_at
                        FROM hookwright_deliveries AS d
                        WHERE d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id > lane.endpoint_id
                            AND d.endpoint_id <= {upTo}
                        ORDER BY d.endpoint_id, d.next_attempt_at
                        LIMIT 1) AS following),
                due AS (
                    SELECT taken.id
                    FROM lane
                    CROSS JOIN LATERAL (
                        SELECT d.id
                        FROM hookwright_deliveries AS d
                        WHERE d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id = lane.endpoint_id
                            AND d.next_attempt_at <= {dueBy}
                        ORDER BY d.next_attempt_at
                        LIMIT COALESCE(({limits})[array_position({limited}, lane.endpoint_id)], {round.PerEndpoint})
                        FOR UPDATE SKIP LOCKED) AS taken
                    WHERE lane.next_attempt_at <= {dueBy}
                    LIMIT {count})
                UPDATE hookwright_deliveries AS d
                SET state = {(short)DeliveryState.InFlight},
                    lease_owner = {leaseOwner},
                    lease_expires_at = {expiresAt},
                    attempt_count = d.attempt_count + 1
                FROM due
                WHERE d.id = due.id
                RETURNING d.id AS "Value"
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<int> UpdateDeliveriesAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. completions.Select(completion => completion.DeliveryId.Value)];
        short[] states = [.. completions.Select(completion => (short)completion.State)];
        DateTimeOffset?[] nextAttemptAt = [.. completions.Select(completion => completion.NextAttemptAt?.ToUniversalTime())];
        DateTimeOffset?[] completedAt = [.. completions.Select(completion => completion.CompletedAt?.ToUniversalTime())];
        int[] attemptsGivenBack = [.. completions.Select(completion => completion.GivesBackAttempt ? 1 : 0)];

        return await Context.Database
            .ExecuteSqlAsync($"""
                UPDATE hookwright_deliveries AS d
                SET state = v.state,
                    next_attempt_at = COALESCE(v.next_attempt_at, d.next_attempt_at),
                    completed_at = v.completed_at,
                    lease_owner = NULL,
                    lease_expires_at = NULL,
                    attempt_count = d.attempt_count - v.attempts_given_back
                FROM unnest({ids}, {states}, {nextAttemptAt}, {completedAt}, {attemptsGivenBack})
                    AS v(id, state, next_attempt_at, completed_at, attempts_given_back)
                WHERE d.id = v.id AND d.lease_owner = {leaseOwner}
            """, cancellationToken).ConfigureAwait(false);
    }
}