using System.Text.Json;

using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore.Stores;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.Sqlite;

/// <summary>
/// The SQLite claim.
/// </summary>
public sealed class SqliteDeliveryLeaseStore : EfDeliveryLeaseStore
{
    private static readonly IComparer<Guid> StoredOrder = Comparer<Guid>.Create(
        (left, right) => string.CompareOrdinal(Stored(left), Stored(right)));

    /// <summary>
    /// Creates the store.
    /// </summary>
    public SqliteDeliveryLeaseStore(DbContext context)
        : base(context)
    {

    }

    /// <inheritdoc />
    protected override IComparer<Guid> EndpointOrder => StoredOrder;

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
        long dueBy = now.UtcDateTime.Ticks;
        long expiresAt = leaseExpiresAt.UtcDateTime.Ticks;
        string limits = JsonSerializer.Serialize(
            round.Limits.ToDictionary(limit => Stored(limit.Key.Value), limit => limit.Value));

        return await Context.Database
            .SqlQuery<Guid>($"""
                WITH RECURSIVE lane(endpoint_id) AS NOT MATERIALIZED (
                    SELECT (
                        SELECT d.endpoint_id
                        FROM hookwright_deliveries AS d
                        WHERE d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id > {after}
                            AND d.endpoint_id <= {upTo}
                        ORDER BY d.endpoint_id
                        LIMIT 1)
                    UNION ALL
                    SELECT (
                        SELECT d.endpoint_id
                        FROM hookwright_deliveries AS d
                        WHERE d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id > lane.endpoint_id
                            AND d.endpoint_id <= {upTo}
                        ORDER BY d.endpoint_id
                        LIMIT 1)
                    FROM lane
                    WHERE lane.endpoint_id IS NOT NULL),
                turn(endpoint_id, most) AS (
                    SELECT lane.endpoint_id, COALESCE(
                        (SELECT limited.value FROM json_each({limits}) AS limited WHERE limited.key = lane.endpoint_id),
                        {round.PerEndpoint})
                    FROM lane
                    WHERE lane.endpoint_id IS NOT NULL)
                UPDATE hookwright_deliveries
                SET state = {(short)DeliveryState.InFlight},
                    lease_owner = {leaseOwner},
                    lease_expires_at = {expiresAt},
                    attempt_count = attempt_count + 1
                WHERE id IN (
                    SELECT taken.id
                    FROM turn
                    CROSS JOIN hookwright_deliveries AS taken
                    WHERE turn.most > 0
                        AND taken.id IN (
                            SELECT ranked.id
                            FROM (
                                SELECT oldest.id, ROW_NUMBER() OVER (ORDER BY oldest.next_attempt_at) AS position
                                FROM (
                                    SELECT d.id, d.next_attempt_at
                                    FROM hookwright_deliveries AS d
                                    WHERE d.state = {(short)DeliveryState.Pending}
                                        AND d.endpoint_id = turn.endpoint_id
                                        AND d.next_attempt_at <= {dueBy}
                                    ORDER BY d.next_attempt_at
                                    LIMIT {round.PerEndpoint}) AS oldest) AS ranked
                            WHERE ranked.position <= turn.most)
                    LIMIT {count})
                RETURNING id AS Value
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    }

    private static string Stored(Guid id)
    {
        return id.ToString().ToUpperInvariant();
    }
}