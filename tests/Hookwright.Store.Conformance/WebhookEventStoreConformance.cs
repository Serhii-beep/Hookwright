using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;

namespace Hookwright.Store.Conformance;

public abstract class WebhookEventStoreConformance : StoreConformance
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    protected WebhookEventStoreConformance(IStoreHarness harness)
        : base(harness)
    {

    }

    [Fact]
    public async Task Append_WithoutCommit_IsNotClaimable()
    {
        WebhookEndpoint endpoint = await SeedEndpointAsync();

        await using IStoreSession publisher = OpenSession();
        publisher.Events.Append(Publication(endpoint));

        await using IStoreSession worker = OpenSession();
        (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Append_ThenCommit_IsClaimable()
    {
        WebhookEndpoint endpoint = await SeedEndpointAsync();

        await using IStoreSession publisher = OpenSession();
        publisher.Events.Append(Publication(endpoint));
        await publisher.Events.CommitAsync(TestContext.Current.CancellationToken);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);

        ClaimedDelivery only = claimed.ShouldHaveSingleItem();
        only.EndpointId.ShouldBe(endpoint.Id);
        only.EventType.ShouldBe("order.created");
        only.Payload.ShouldBe("""{"id":1}""");
    }

    private static EventPublication Publication(WebhookEndpoint endpoint)
    {
        WebhookEvent published = WebhookEvent.Create(
            endpoint.SubscriberId, "order.created", """{"id":1}""", Now);

        return EventPublication.Create(published, [Delivery.Create(published.Id, endpoint.Id, null, Now)]);
    }

    private async Task<WebhookEndpoint> SeedEndpointAsync()
    {
        await using IStoreSession session = OpenSession();

        return await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
    }
}