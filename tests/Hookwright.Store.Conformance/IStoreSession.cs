using Hookwright.Core.Storage;

namespace Hookwright.Store.Conformance;

public interface IStoreSession : IAsyncDisposable
{
    ISubscriberStore Subscribers { get; }

    IWebhookEndpointStore Endpoints { get; }

    IWebhookEventStore Events { get; }

    IDeliveryLeaseStore Deliveries { get; }

    IRetentionStore Retention { get; }
}