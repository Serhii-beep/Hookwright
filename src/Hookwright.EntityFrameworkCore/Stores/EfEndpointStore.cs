using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Stores;

/// <summary>
/// <see cref="IWebhookEndpointStore"/> over Entity Framework Core.
/// </summary>
public sealed class EfEndpointStore : IWebhookEndpointStore
{
    private readonly DbContext _context;

    /// <summary>
    /// Creates the store.
    /// </summary>
    public EfEndpointStore(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(EndpointRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _context.Add(registration.Endpoint);
        _context.AddRange(registration.Secrets);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WebhookEndpoint?> FindAsync(WebhookEndpointId endpointId, CancellationToken cancellationToken)
    {
        return await _context.Set<WebhookEndpoint>()
            .AsNoTracking()
            .SingleOrDefaultAsync(endpoint => endpoint.Id == endpointId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookEndpoint>> ListForSubscriberAsync(SubscriberId subscriberId, CancellationToken cancellationToken)
    {
        return await _context.Set<WebhookEndpoint>()
            .AsNoTracking()
            .Where(endpoint => endpoint.SubscriberId == subscriberId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}