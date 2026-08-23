using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class EfEventStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Append_WithoutCommit_IsInvisibleToAnyOtherReader()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext writer = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = await SeedAsync(writer);
        EfEventStore store = new(writer);

        store.Append(Publication(endpoint));

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        (await reader.Set<WebhookEvent>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await reader.Set<Delivery>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Append_ThenCommit_PersistsTheEventAndItsFanOut()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext writer = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = await SeedAsync(writer);
        EfEventStore store = new(writer);

        store.Append(Publication(endpoint));
        await store.CommitAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        (await reader.Set<WebhookEvent>().SingleAsync(TestContext.Current.CancellationToken))
            .Type.ShouldBe("order.created");

        Delivery delivery = await reader.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken);
        delivery.EndpointId.ShouldBe(endpoint.Id);
        delivery.State.ShouldBe(DeliveryState.Pending);
    }

    [Fact]
    public async Task Append_GivenNoSubscribedEndpoints_StillPersistsTheEvent()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext writer = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = await SeedAsync(writer);
        EfEventStore store = new(writer);

        WebhookEvent published = WebhookEvent.Create(endpoint.SubscriberId, "order.created", "{}", Now);
        store.Append(EventPublication.Create(published, []));
        await store.CommitAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        (await reader.Set<WebhookEvent>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await reader.Set<Delivery>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    private static EventPublication Publication(WebhookEndpoint endpoint)
    {
        WebhookEvent published = WebhookEvent.Create(
            endpoint.SubscriberId, "order.created", """{"id":1}""", Now);

        return EventPublication.Create(
            published,
            [Delivery.Create(published.Id, endpoint.Id, null, Now)]);
    }

    private static async Task<WebhookEndpoint> SeedAsync(HookwrightDbContext context)
    {
        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        await new EfSubscriberStore(context).AddAsync(subscriber, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);

        await new EfEndpointStore(context).AddAsync(
            EndpointRegistration.Create(
                endpoint,
                [EndpointSecret.Create(endpoint.Id, "protected-key", SignatureAlgorithm.HmacSha256, Now)]),
            TestContext.Current.CancellationToken);

        return endpoint;
    }
}