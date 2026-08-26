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

public sealed class EfDeliveryLeaseStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class TestLeaseStore : EfDeliveryLeaseStore
    {
        public TestLeaseStore(DbContext context)
            : base(context)
        {

        }

        public override Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
            string leaseOwner,
            int batchSize,
            TimeSpan leaseDuration,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ClaimedDelivery>> ProjectAsync(
            IReadOnlyList<DeliveryId> deliveryIds,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            return ProjectClaimedAsync(deliveryIds, now, cancellationToken);
        }
    }

    [Fact]
    public async Task ProjectClaimedAsync_Always_GathersEverythingOneRequestNeeds()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery _) = await SeedClaimedAsync(context);

        IReadOnlyList<ClaimedDelivery> claimed = await new TestLeaseStore(context)
            .ProjectAsync([delivery.Id], Now, TestContext.Current.CancellationToken);

        ClaimedDelivery only = claimed.ShouldHaveSingleItem();
        only.DeliveryId.ShouldBe(delivery.Id);
        only.AttemptCount.ShouldBe(1);
        only.LeaseExpiresAt.ShouldBe(Now.AddSeconds(60));
        only.EventType.ShouldBe("order.created");
        only.Payload.ShouldBe("{}");
        only.Url.ShouldBe(new Uri("https://example.com/"));
        only.Secrets.ShouldHaveSingleItem().ProtectedKey.ShouldBe("protected-key");
    }

    [Fact]
    public async Task ProjectClaimedAsync_DuringARotation_CarriesBothLiveKeys()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery claimed) = await SeedClaimedAsync(context);

        EndpointSecret retiring = await context.Set<EndpointSecret>()
            .SingleAsync(TestContext.Current.CancellationToken);
        retiring.Retire(Now.AddHours(24));
        context.Add(EndpointSecret.Create(
            claimed.EndpointId, "protected-new", SignatureAlgorithm.HmacSha256, Now.AddHours(1)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<ClaimedDelivery> projected = await new TestLeaseStore(context)
            .ProjectAsync([delivery.Id], Now.AddHours(2), TestContext.Current.CancellationToken);

        projected.ShouldHaveSingleItem().Secrets.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ProjectClaimedAsync_GivenAKeyRetiredInThePast_LeavesItOut()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery _) = await SeedClaimedAsync(context);

        EndpointSecret expired = await context.Set<EndpointSecret>()
            .SingleAsync(TestContext.Current.CancellationToken);
        expired.Retire(Now.AddMinutes(1));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<ClaimedDelivery> projected = await new TestLeaseStore(context)
            .ProjectAsync([delivery.Id], Now.AddHours(1), TestContext.Current.CancellationToken);

        projected.ShouldHaveSingleItem().Secrets.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProjectClaimedAsync_GivenNothing_ReturnsNothing()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (await new TestLeaseStore(context).ProjectAsync([], Now, TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    private static async Task<(Delivery delivery, ClaimedDelivery Claimed)> SeedClaimedAsync(HookwrightDbContext context)
    {
        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);
        WebhookEvent published = WebhookEvent.Create(subscriber.Id, "order.created", "{}", Now);
        Delivery delivery = Delivery.Create(published.Id, endpoint.Id, null, Now);
        delivery.Claim("web-01:4242:a1b2c3", Now.AddSeconds(60), Now);

        context.AddRange(subscriber, endpoint, published, delivery);
        context.Add(EndpointSecret.Create(endpoint.Id, "protected-key", SignatureAlgorithm.HmacSha256, Now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ClaimedDelivery claimed = new()
        {
            DeliveryId = delivery.Id,
            AttemptCount = 1,
            LeaseExpiresAt = Now.AddSeconds(60),
            EventId = published.Id,
            EventType = published.Type,
            Payload = published.Payload,
            Headers = published.Headers,
            EndpointId = endpoint.Id,
            Url = endpoint.Url,
            Secrets = []
        };

        return (delivery, claimed);
    }
}