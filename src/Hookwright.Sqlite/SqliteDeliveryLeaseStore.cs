using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.Sqlite;

/// <summary>
/// The SQLite claim.
/// </summary>
public sealed class SqliteDeliveryLeaseStore : EfDeliveryLeaseStore
{
    /// <summary>
    /// Creates the store.
    /// </summary>
    public SqliteDeliveryLeaseStore(DbContext context)
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

        long dueBy = now.UtcDateTime.Ticks;
        long leaseExpiresAt = (now + leaseDuration).UtcDateTime.Ticks;
        List<Guid> taken = await Context.Database
            .SqlQuery<Guid>($"""
                UPDATE hookwright_deliveries
                SET state = {(short)DeliveryState.InFlight},
                    lease_owner = {leaseOwner},
                    lease_expires_at = {leaseExpiresAt},
                    attempt_count = attempt_count + 1
                WHERE id IN (
                    SELECT id
                    FROM hookwright_deliveries
                    WHERE state = {(short)DeliveryState.Pending}
                        AND next_attempt_at <= {dueBy}
                    ORDER BY next_attempt_at
                    LIMIT {batchSize})
                RETURNING id AS Value
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