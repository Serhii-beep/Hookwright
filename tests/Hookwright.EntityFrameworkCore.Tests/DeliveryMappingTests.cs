using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class DeliveryMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Delivery_Always_RoundTripsWhileLeased()
    {
        await using SqliteConnection connection = TestDatabase.Open();

        Delivery delivery = await SeedAsync(connection, claimed: true, completed: false);

        await using HookwrightDbContext read = TestDatabase.CreateContext(connection);
        Delivery loaded = await read.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken);

        loaded.Id.ShouldBe(delivery.Id);
        loaded.EventId.ShouldBe(delivery.EventId);
        loaded.EndpointId.ShouldBe(delivery.EndpointId);
        loaded.State.ShouldBe(DeliveryState.InFlight);
        loaded.AttemptCount.ShouldBe(1);
        loaded.LeaseOwner.ShouldBe("web-01:4242:a1b2c3");
        loaded.LeaseExpiresAt.ShouldBe(Now.AddSeconds(60));
        loaded.PartitionKey.ShouldBe("order-1");
        loaded.CompletedAt.ShouldBeNull();
        loaded.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public async Task Delivery_Always_RoundTripsOnceCompleted()
    {
        await using SqliteConnection connection = TestDatabase.Open();

        await SeedAsync(connection, claimed: true, completed: true);

        await using HookwrightDbContext read = TestDatabase.CreateContext(connection);
        Delivery loaded = await read.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken);

        loaded.State.ShouldBe(DeliveryState.Succeeded);
        loaded.CompletedAt.ShouldBe(Now.AddSeconds(2));
        loaded.IsTerminal.ShouldBeTrue();
        loaded.LeaseOwner.ShouldBeNull();
        loaded.LeaseExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Attempt_Always_RoundTrips()
    {
        await using SqliteConnection connection = TestDatabase.Open();

        Delivery delivery = await SeedAsync(connection, claimed: true, completed: false);

        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            delivery.Id,
            AttemptOutcome.Succeeded,
            200,
            "ok",
            new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
            Now.AddSeconds(1),
            TimeSpan.FromMilliseconds(120),
            "web-01:4242:a1b2c3");

        await using HookwrightDbContext write = TestDatabase.CreateContext(connection);
        write.Add(attempt);
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext read = TestDatabase.CreateContext(connection);
        DeliveryAttempt loaded = await read.Set<DeliveryAttempt>()
            .SingleAsync(TestContext.Current.CancellationToken);

        loaded.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        loaded.ResponseStatusCode.ShouldBe(200);
        loaded.ResponseBodySnippet.ShouldBe("ok");
        loaded.ErrorDetail.ShouldBeNull();
        loaded.WorkerId.ShouldBe("web-01:4242:a1b2c3");

        loaded.Duration.ShouldBe(TimeSpan.FromMilliseconds(120));

        loaded.ResponseHeaders["content-type"].ShouldBe("text/plain");
    }

    private static async Task<Delivery> SeedAsync(SqliteConnection connection, bool claimed, bool completed)
    {
        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);
        WebhookEvent published = WebhookEvent.Create(
            subscriber.Id, "order.created", "{}", Now, partitionKey: "order-1");

        Delivery delivery = Delivery.Create(published.Id, endpoint.Id, "order-1", Now);

        if (claimed)
        {
            delivery.Claim("web-01:4242:a1b2c3", Now.AddSeconds(60), Now);
        }

        if (completed)
        {
            delivery.MarkSucceeded(Now.AddSeconds(2));
        }

        await using HookwrightDbContext write = TestDatabase.CreateContext(connection);
        await write.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        write.Add(subscriber);
        write.Add(endpoint);
        write.Add(published);
        write.Add(delivery);
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        return delivery;
    }
}