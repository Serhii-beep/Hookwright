using System.Data.SqlTypes;
using System.Text.Json;

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
    private static readonly IComparer<Guid> ServerOrder = Comparer<Guid>.Create(
        (left, right) => new SqlGuid(left).CompareTo(new SqlGuid(right)));

    /// <summary>
    /// Creates the store.
    /// </summary>
    public SqlServerDeliveryLeaseStore(DbContext context)
        : base(context)
    {

    }

    /// <inheritdoc />
    protected override IComparer<Guid> EndpointOrder => ServerOrder;

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
        string limits = JsonSerializer.Serialize(
            round.Limits.Select(limit => new { Endpoint = limit.Key.Value, Most = limit.Value }));

        return await Context.Database
            .SqlQuery<Guid>($"""
                WITH lane AS (
                    SELECT first.endpoint_id, first.next_attempt_at
                    FROM (
                        SELECT TOP (1) d.endpoint_id, d.next_attempt_at
                        FROM hookwright_deliveries AS d WITH (READPAST)
                        WHERE d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id > {after}
                            AND d.endpoint_id <= {upTo}
                        ORDER BY d.endpoint_id, d.next_attempt_at) AS first
                    UNION ALL
                    SELECT following.endpoint_id, following.next_attempt_at
                    FROM (
                        SELECT d.endpoint_id, d.next_attempt_at,
                            ROW_NUMBER() OVER (ORDER BY d.endpoint_id, d.next_attempt_at) AS position
                        FROM lane
                        INNER JOIN hookwright_deliveries AS d WITH (READPAST)
                            ON d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id > lane.endpoint_id
                            AND d.endpoint_id <= {upTo}) AS following
                    WHERE following.position = 1),
                due AS (
                    SELECT TOP ({count}) taken.id
                    FROM lane
                    OUTER APPLY (
                        SELECT TOP (1) limited.most
                        FROM OPENJSON({limits})
                            WITH (endpoint_id uniqueidentifier '$.Endpoint', most int '$.Most') AS limited
                        WHERE limited.endpoint_id = lane.endpoint_id) AS share
                    CROSS APPLY (
                        SELECT TOP (COALESCE(share.most, {round.PerEndpoint})) d.id
                        FROM hookwright_deliveries AS d WITH (READPAST, UPDLOCK, ROWLOCK)
                        WHERE d.state = {(short)DeliveryState.Pending}
                            AND d.endpoint_id = lane.endpoint_id
                            AND d.next_attempt_at <= {now}
                        ORDER BY d.next_attempt_at) AS taken
                    WHERE lane.next_attempt_at <= {now})
                UPDATE d
                SET state = {(short)DeliveryState.InFlight},
                    lease_owner = {leaseOwner},
                    lease_expires_at = {leaseExpiresAt},
                    attempt_count = d.attempt_count + 1
                OUTPUT inserted.id AS [Value]
                FROM hookwright_deliveries AS d
                INNER JOIN due ON d.id = due.id
                OPTION (MAXRECURSION 0)
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