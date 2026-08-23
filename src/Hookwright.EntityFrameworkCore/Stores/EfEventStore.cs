using Hookwright.Core.Storage;

using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Stores;

/// <summary>
/// <see cref="IWebhookEventStore"/> over Entity Framework Core.
/// </summary>
public sealed class EfEventStore : IWebhookEventStore
{
    private readonly DbContext _context;

    /// <summary>
    /// Creates the store
    /// </summary>
    public EfEventStore(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public void Append(EventPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        _context.Add(publication.Event);
        _context.AddRange(publication.Deliveries);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}