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
    void Append(EventPublication publication);

    /// <summary>
    /// Makes everything staged durable.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task CommitAsync(CancellationToken cancellationToken);
}