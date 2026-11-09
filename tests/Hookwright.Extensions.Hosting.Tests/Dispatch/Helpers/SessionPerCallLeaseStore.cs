using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore;
using Hookwright.Sqlite;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class SessionPerCallLeaseStore : IDeliveryLeaseStore
{
    private readonly DbContextOptions<HookwrightDbContext> _options;

    public SessionPerCallLeaseStore(DbContextOptions<HookwrightDbContext> options)
    {
        _options = options;
    }

    public Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        string leaseOwner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return WithStoreAsync(store => store.ClaimAsync(leaseOwner, batchSize, leaseDuration, now, cancellationToken));
    }

    public Task<CompletionReport> CompleteAsync(string leaseOwner, IReadOnlyList<DeliveryCompletion> completions, CancellationToken cancellationToken)
    {
        return WithStoreAsync(store => store.CompleteAsync(leaseOwner, completions, cancellationToken));
    }

    public Task<IReadOnlyList<ReclaimedLeases>> ReclaimExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        return WithStoreAsync(store => store.ReclaimExpiredLeasesAsync(now, cancellationToken));
    }

    public Task<int> ReleaseAsync(string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return WithStoreAsync(store => store.ReleaseAsync(leaseOwner, now, cancellationToken));
    }

    private async Task<T> WithStoreAsync<T>(Func<IDeliveryLeaseStore, Task<T>> call)
    {
        await using HookwrightDbContext context = new(_options);

        return await call(new SqliteDeliveryLeaseStore(context));
    }
}