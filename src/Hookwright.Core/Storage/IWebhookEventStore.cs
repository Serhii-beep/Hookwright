namespace Hookwright.Core.Storage;

/// <summary>
/// The publisher's view of storage.
/// </summary>
public interface IWebhookEventStore
{
    /// <summary>
    /// Stages a publication.
    /// </summary>
    /// <param name="publication">The event and its fan-out.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task AppendAsync(EventPublication publication, CancellationToken cancellationToken);

    /// <summary>
    /// Makes everything staged durable.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task CommitAsync(CancellationToken cancellationToken);
}