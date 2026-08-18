using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Storage;

/// <summary>
/// Registering subscribers and looking them up.
/// </summary>
public interface ISubscriberStore
{
    /// <summary>
    /// Registers a subscriber.
    /// </summary>
    /// <param name="subscriber">The subscriber to register.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="StoreConflictException">
    /// Another subscriber already has this <see cref="Subscriber.ExternalId"/>.
    /// </exception>
    Task AddAsync(Subscriber subscriber, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a subscriber.
    /// </summary>
    /// <param name="subscriberId">The subscriber to find.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The subscriber, or <see langword="null"/> if no such subscriber exist.</returns>
    Task<Subscriber?> FindAsync(SubscriberId subscriberId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a subscriber by external identifier for the customer.
    /// </summary>
    /// <param name="externalId">
    /// The value supplied as <see cref="Subscriber.ExternalId"/>. Matched exactly and case-sensitively.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The subscriber, or <see langword="null"/> if no such subscriber exists.</returns>
    Task<Subscriber?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken);
}