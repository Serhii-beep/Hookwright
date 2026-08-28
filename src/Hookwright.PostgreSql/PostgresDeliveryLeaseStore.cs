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
    public override async Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        string leaseOwner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseDuration.Ticks);

        DateTimeOffset dueBy = now.ToUniversalTime();
        DateTimeOffset leaseExpiresAt = (now + leaseDuration).ToUniversalTime();

        List<Guid> taken = await Context.Database
            .SqlQuery<Guid>($"""
                WITH due AS (
                    SELECT id
                    FROM hookwright_deliveries
                    WHERE state = {(short)DeliveryState.Pending}
                        AND next_attempt_at <= {dueBy}
                    ORDER BY next_attempt_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                UPDATE hookwright_deliveries AS d
                SET state = {(short)DeliveryState.InFlight},
                    lease_owner = {leaseOwner},
                    lease_expires_at = {leaseExpiresAt},
                    attempt_count = d.attempt_count + 1
                FROM due
                WHERE d.id = due.id
                RETURNING d.id AS "Value"
            """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (taken.Count == 0)
        {
            return [];
        }

        return await ProjectClaimedAsync(
            [.. taken.Select(DeliveryId.FromGuid)],
            now,
            cancellationToken)
            .ConfigureAwait(false);
    }
}