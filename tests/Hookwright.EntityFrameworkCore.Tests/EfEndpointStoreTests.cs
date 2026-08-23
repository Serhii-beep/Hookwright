using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class EfEndpointStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_GivenARegistration_PersistsTheEndpointAndItsKey()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = await RegisterAsync(context);

        (await new EfEndpointStore(context).FindAsync(endpoint.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Url.ShouldBe(new Uri("https://example.com/"));

        (await context.Set<EndpointSecret>()
            .CountAsync(secret => secret.EndpointId == endpoint.Id, TestContext.Current.CancellationToken))
            .ShouldBe(1);
    }

    [Fact]
    public async Task ListForSubscriberAsync_Always_ReturnsDisabledEndpointsToo()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = await RegisterAsync(context);
        endpoint.Disable("410 Gone", Now.AddHours(1));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<WebhookEndpoint> found = await new EfEndpointStore(context)
            .ListForSubscriberAsync(endpoint.SubscriberId, TestContext.Current.CancellationToken);

        found.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
    }

    private static async Task<WebhookEndpoint> RegisterAsync(HookwrightDbContext context)
    {
        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        await new EfSubscriberStore(context).AddAsync(subscriber, TestContext.Current.CancellationToken);

        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);

        EndpointRegistration registration = EndpointRegistration.Create(
            endpoint,
            [EndpointSecret.Create(endpoint.Id, "protected-key", SignatureAlgorithm.HmacSha256, Now)]);

        await new EfEndpointStore(context).AddAsync(registration, TestContext.Current.CancellationToken);

        return endpoint;
    }
}