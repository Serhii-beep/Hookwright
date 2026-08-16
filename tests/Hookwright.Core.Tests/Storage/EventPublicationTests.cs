using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Tests.Storage;

public sealed class EventPublicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static WebhookEvent CreateEvent()
    {
        return WebhookEvent.Create(SubscriberId.New(), "order.created", """{"id":1}""", Now);
    }

    [Fact]
    public void Create_GivenAFanOut_KeepsEveryDelivery()
    {
        WebhookEvent published = CreateEvent();
        Delivery first = Delivery.Create(published.Id, WebhookEndpointId.New(), null, Now);
        Delivery second = Delivery.Create(published.Id, WebhookEndpointId.New(), null, Now);

        EventPublication publication = EventPublication.Create(published, [first, second]);

        publication.Event.ShouldBeSameAs(published);
        publication.Deliveries.Count.ShouldBe(2);
        publication.Deliveries.ShouldContain(first);
        publication.Deliveries.ShouldContain(second);
    }

    [Fact]
    public void Create_GivenNoSubscribers_StillRecordsTheEvent()
    {
        WebhookEvent published = CreateEvent();

        EventPublication publication = EventPublication.Create(published, []);

        publication.Event.ShouldBeSameAs(published);
        publication.Deliveries.ShouldBeEmpty();
    }

    [Fact]
    public void Create_GivenADeliveryForAnotherEvent_Throws()
    {
        WebhookEvent published = CreateEvent();
        Delivery stray = Delivery.Create(WebhookEventId.New(), WebhookEndpointId.New(), null, Now);

        Should.Throw<ArgumentException>(() => EventPublication.Create(published, [stray]));
    }

    [Fact]
    public void Create_GivenTwoDeliveriesToOneEndpoint_Throws()
    {
        WebhookEvent published = CreateEvent();
        WebhookEndpointId endpointId = WebhookEndpointId.New();

        Should.Throw<ArgumentException>(() => EventPublication.Create(
            published,
            [
                Delivery.Create(published.Id, endpointId, null, Now),
                Delivery.Create(published.Id, endpointId, null, Now)
            ]));
    }
}