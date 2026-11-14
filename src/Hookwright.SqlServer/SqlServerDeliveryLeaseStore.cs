using System.Text.Json;

using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
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
        IReadOnlyCollection<WebhookEndpointId> excludedEndpoints,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseDuration.Ticks);
        ArgumentNullException.ThrowIfNull(excludedEndpoints);

        DateTimeOffset leaseExpiresAt = now + leaseDuration;

        List<object> parameters = [(short)DeliveryState.Pending, now, batchSize, (short)DeliveryState.InFlight, leaseOwner, leaseExpiresAt];
        string exclusion = string.Empty;

        if (excludedEndpoints.Count > 0)
        {
            exclusion = " AND endpoint_id NOT IN ("
                + string.Join(", ", excludedEndpoints.Select((_, i) => $"{{{parameters.Count + i}}}"))
                + ")";
            parameters.AddRange(excludedEndpoints.Select(endpoint => (object)endpoint.Value));
        }

        string sql = $$"""
            WITH due AS (
                SELECT TOP ({2}) id
                FROM hookwright_deliveries WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE state = {0} AND next_attempt_at <= {1}{{exclusion}}
                ORDER BY next_attempt_at)
            UPDATE d
            SET state = {3},
                lease_owner = {4},
                lease_expires_at = {5},
                attempt_count = d.attempt_count + 1
            OUTPUT inserted.id AS [Value]
            FROM hookwright_deliveries AS d
            INNER JOIN due ON d.id = due.id
            """;

        List<Guid> taken = await Context.Database
            .SqlQueryRaw<Guid>(sql, [.. parameters])
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

    /// <inheritdoc />
    protected override async Task<int> UpdateDeliveriesAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        string batch = JsonSerializer.Serialize(completions.Select(completion => new
        {
            Id = completion.DeliveryId.Value,
            State = (short)completion.State,
            completion.NextAttemptAt,
            completion.CompletedAt,
            AttemptsGivenBack = completion.GivesBackAttempt ? 1 : 0
        }));

        return await Context.Database
            .ExecuteSqlAsync($"""
                UPDATE d
                SET state = v.state,
                    next_attempt_at = COALESCE(v.next_attempt_at, d.next_attempt_at),
                    completed_at = v.completed_at,
                    lease_owner = NULL,
                    lease_expires_at = NULL,
                    attempt_count = d.attempt_count - v.attempts_given_back
                FROM hookwright_deliveries AS d
                INNER JOIN OPENJSON({batch})
                    WITH (
                        id uniqueidentifier '$.Id',
                        state smallint '$.State',
                        next_attempt_at datetimeoffset '$.NextAttemptAt',
                        completed_at datetimeoffset '$.CompletedAt',
                        attempts_given_back int '$.AttemptsGivenBack') AS v
                    ON d.id = v.id
                WHERE d.lease_owner = {leaseOwner}
            """, cancellationToken)
            .ConfigureAwait(false);
    }
}