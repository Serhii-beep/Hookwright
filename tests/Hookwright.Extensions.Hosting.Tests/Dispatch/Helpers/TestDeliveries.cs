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

    public static DeliveryCompletion Completed(ClaimedDelivery claimed, DateTimeOffset now)
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            claimed.DeliveryId, AttemptOutcome.Succeeded, 200, null, null, now, ScriptedTransport.Duration, "web-01:4242:a1b2c3");

        return DeliveryCompletion.From(claimed, attempt, new RetryDecision.Succeeded(), now);
    }
}