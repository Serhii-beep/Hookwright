using Hookwright.Core.Endpoints;
using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Storage;

/// <summary>
/// Reading and registering endpoints.
/// </summary>
public interface IWebhookEndpointStore
{
    /// <summary>
    /// Registers an endpoint and its initial signing keys, atomically.
    /// </summary>
    /// <param name="registration">The endpoint and its keys.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Self-committing, unlike <see cref="IWebhookEventStore.AppendAsync"/>. Registering
    /// an endpoint is not a part of a host's business transaction.
    /// </remarks>
    Task AddAsync(EndpointRegistration registration, CancellationToken cancellationToken);

    /// <summary>
    /// Finds one endpoint by identifier.
    /// </summary>
    /// <param name="endpointId">The endpoint to find.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The endpoint, or <see langword="null"/> if no such endpoint exists.</returns>
    /// <remarks>
    /// Signing keys are not returned. A worker does not read them from here. The claim
    /// denormalises them into <see cref="ClaimedDelivery.Secrets"/>.
    /// </remarks>
    Task<WebhookEndpoint?> FindAsync(WebhookEndpointId endpointId, CancellationToken cancellationToken);

    /// <summary>
    /// Every endpoint belonging to one subscriber, which is the candidate set a publish fans out over.
    /// </summary>
    /// <param name="subscriberId">The subscriber whose endpoints to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The subscriber's endpoints, empty when they have none.</returns>
    /// <remarks>
    /// Returns disabled endpoints too and does no event-type filtering.
    /// </remarks>
    Task<IReadOnlyList<WebhookEndpoint>> ListForSubscriberAsync(SubscriberId subscriberId, CancellationToken cancellationToken);
}