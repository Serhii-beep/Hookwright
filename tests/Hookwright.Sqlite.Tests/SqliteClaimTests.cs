using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ClaimAsync_GivenDueDeliveries_TakesThemUnderALease()
    {
        await using SqliteConnection connection = Open();
        await using HookwrightDbContext context = await SeedAsync(connection, dueCount: 1);

        IReadOnlyList<ClaimedDelivery> claimed = await new SqliteDeliveryLeaseStore(context)
            .ClaimAsync("web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);

        ClaimedDelivery only = claimed.ShouldHaveSingleItem();
        only.AttemptCount.ShouldBe(1);
        only.LeaseExpiresAt.ShouldBe(Now + Lease);
        only.EventType.ShouldBe("order.created");
        only.Url.ShouldBe(new Uri("https://example.com/"));
        only.Secrets.ShouldHaveSingleItem();

        await using HookwrightDbContext reader = Context(connection);
        (await reader.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken))
            .State.ShouldBe(DeliveryState.InFlight);
    }

    [Fact]
    public async Task ClaimAsync_Always_TakesTheOldestDueFirst()
    {
        await using SqliteConnection connection = Open();
        await using HookwrightDbContext context = await SeedAsync(connection, dueCount: 5);

        IReadOnlyList<ClaimedDelivery> claimed = await new SqliteDeliveryLeaseStore(context)
            .ClaimAsync("web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken);

        claimed.Count.ShouldBe(2);

        await using HookwrightDbContext reader = Context(connection);
        List<Delivery> untouched = await reader.Set<Delivery>()
            .Where(delivery => delivery.State == DeliveryState.Pending)
            .ToListAsync(TestContext.Current.CancellationToken);

        DateTimeOffset newestTaken = await reader.Set<Delivery>()
            .Where(delivery => delivery.State == DeliveryState.InFlight)
            .MaxAsync(delivery => delivery.NextAttemptAt, TestContext.Current.CancellationToken);

        untouched.Count.ShouldBe(3);
        untouched.ShouldAllBe(delivery => delivery.NextAttemptAt >= newestTaken);
    }

    [Fact]
    public async Task ClaimAsync_GivenNothingDueYet_ReturnsNothing()
    {
        await using SqliteConnection connection = Open();
        await using HookwrightDbContext context = await SeedAsync(connection, dueCount: 3);

        (await new SqliteDeliveryLeaseStore(context)
            .ClaimAsync("web-01:4242:a1b2c3", 10, Lease, Now.AddHours(-1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ClaimAsync_GivenASecondWorker_NeverHandsOutTheSameDeliveryTwice()
    {
        await using SqliteConnection connection = Open();
        await using HookwrightDbContext context = await SeedAsync(connection, dueCount: 3);

        SqliteDeliveryLeaseStore store = new(context);

        IReadOnlyList<ClaimedDelivery> first = await store
            .ClaimAsync("web-01:1:a", 2, Lease, Now, TestContext.Current.CancellationToken);
        IReadOnlyList<ClaimedDelivery> second = await store
            .ClaimAsync("web-02:1:b", 2, Lease, Now, TestContext.Current.CancellationToken);

        first.Count.ShouldBe(2);
        second.Count.ShouldBe(1);
        first.Select(claimed => claimed.DeliveryId)
            .Intersect(second.Select(claimed => claimed.DeliveryId))
            .ShouldBeEmpty();
    }

    private static SqliteConnection Open()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        return connection;
    }

    private static HookwrightDbContext Context(SqliteConnection connection)
    {
        return new HookwrightDbContext(
            new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite(connection).Options);
    }

    private static async Task<HookwrightDbContext> SeedAsync(SqliteConnection connection, int dueCount)
    {
        HookwrightDbContext context = Context(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);
        WebhookEvent published = WebhookEvent.Create(subscriber.Id, "order.created", "{}", Now);

        context.AddRange(subscriber, endpoint, published);
        context.Add(EndpointSecret.Create(
            endpoint.Id, "protected-key", SignatureAlgorithm.HmacSha256, Now));

        for (int i = 0; i < dueCount; i++)
        {
            context.Add(Delivery.Create(published.Id, endpoint.Id, null, Now.AddSeconds(-i)));
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return context;
    }
}