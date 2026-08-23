using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Stores;

/// <summary>
/// <see cref="ISubscriberStore"/> over Entity Framework Core.
/// </summary>
public sealed class EfSubscriberStore : ISubscriberStore
{
    private readonly DbContext _context;

    /// <summary>
    /// Creates the store
    /// </summary>
    public EfSubscriberStore(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        _context.Add(subscriber);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            _context.Entry(subscriber).State = EntityState.Detached;

            if (await ExistsAsync(subscriber.ExternalId, cancellationToken).ConfigureAwait(false))
            {
                throw new StoreConflictException(
                    $"A subscriber with external id '{subscriber.ExternalId}' already exists.",
                    exception);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Subscriber?> FindAsync(SubscriberId subscriberId, CancellationToken cancellationToken)
    {
        return await _context.Set<Subscriber>()
            .AsNoTracking()
            .SingleOrDefaultAsync(subscriber => subscriber.Id == subscriberId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Subscriber?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        return await _context.Set<Subscriber>()
            .AsNoTracking()
            .SingleOrDefaultAsync(subscriber => subscriber.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> ExistsAsync(string externalId, CancellationToken cancellationToken)
    {
        return await _context.Set<Subscriber>()
            .AsNoTracking()
            .AnyAsync(subscriber => subscriber.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
    }
}