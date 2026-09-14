using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore.Stores;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.SqlServer;

/// <summary>
/// The SQL Server claim.
/// </summary>
public sealed class SqlServerDeliveryLeaseStore : EfDeliveryLeaseStore
{
    /// <summary>
    /// Creates the store.
    /// </summary>
    public SqlServerDeliveryLeaseStore(DbContext context)
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

        DateTimeOffset leaseExpiresAt = now + leaseDuration;

        List<Guid> taken = await Context.Database
            .SqlQuery<Guid>($"""
                WITH due AS (
                    SELECT TOP ({batchSize}) id
                    FROM hookwright_deliveries WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE state = {(short)DeliveryState.Pending} AND next_attempt_at <= {now}
                    ORDER BY next_attempt_at)
                UPDATE d
                SET state = {(short)DeliveryState.InFlight},
                    lease_owner = {leaseOwner},
                    lease_expires_at = {leaseExpiresAt},
                    attempt_count = d.attempt_count + 1
                OUTPUT inserted.id AS [Value]
                FROM hookwright_deliveries AS d
                INNER JOIN due ON d.id = due.id
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