using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal static class TestDeliveries
{
    public static ClaimedDelivery Claimed(DateTimeOffset leaseExpiresAt)
    {
        return new ClaimedDelivery
        {
            DeliveryId = DeliveryId.New(),
            AttemptCount = 1,
            LeaseExpiresAt = leaseExpiresAt,
            EventId = WebhookEventId.New(),
            EventType = "order.created",
            Payload = "{}",
            Headers = new Dictionary<string, string>(),
            EndpointId = WebhookEndpointId.New(),
            Url = new Uri("https://example.test/hooks"),
            Secrets = []
        };
    }
}