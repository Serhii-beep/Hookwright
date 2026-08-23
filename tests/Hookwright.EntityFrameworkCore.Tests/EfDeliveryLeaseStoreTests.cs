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
    }

    [Fact]
    public async Task CompleteAsync_GivenAHeldLease_AppliesTHeStateChangeAndRecordsTheAttempt()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery claimed) = await SeedClaimedAsync(context);

        DeliveryCompletion completion = DeliveryCompletion.From(
            claimed, Attempt(delivery.Id, AttemptOutcome.Succeeded), new RetryDecision.Succeeded(), Now.AddSeconds(2));

        int applied = await new TestLeaseStore(context)
            .CompleteAsync("web-01:4242:a1b2c3", [completion], TestContext.Current.CancellationToken);

        applied.ShouldBe(1);

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        Delivery loaded = await reader.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken);
        loaded.State.ShouldBe(DeliveryState.Succeeded);
        loaded.CompletedAt.ShouldBe(Now.AddSeconds(2));
        loaded.LeaseOwner.ShouldBeNull();
        (await reader.Set<DeliveryAttempt>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task CompleteAsync_GivenALapsedLease_RecordsTheAttemptButNotTheStateChange()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery claimed) = await SeedClaimedAsync(context);

        DeliveryCompletion completion = DeliveryCompletion.From(
            claimed, Attempt(delivery.Id, AttemptOutcome.Succeeded), new RetryDecision.Succeeded(), Now.AddSeconds(2));

        int applied = await new TestLeaseStore(context)
            .CompleteAsync("some-other-worker", [completion], TestContext.Current.CancellationToken);

        applied.ShouldBe(0);

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        (await reader.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken))
            .State.ShouldBe(DeliveryState.InFlight);
        (await reader.Set<DeliveryAttempt>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task CompleteAsync_GivenARetry_ReturnsTheDeliveryToTheQueue()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery claimed) = await SeedClaimedAsync(context);

        DeliveryCompletion completion = DeliveryCompletion.From(
            claimed,
            Attempt(delivery.Id, AttemptOutcome.ServerError, 500),
            new RetryDecision.Retry(Now.AddMinutes(5)),
            Now.AddSeconds(2));

        await new TestLeaseStore(context).CompleteAsync("web-01:4242:a1b2c3", [completion], TestContext.Current.CancellationToken);

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        Delivery loaded = await reader.Set<Delivery>().SingleAsync(TestContext.Current.CancellationToken);
        loaded.State.ShouldBe(DeliveryState.Pending);
        loaded.NextAttemptAt.ShouldBe(Now.AddMinutes(5));
        loaded.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task CompleteAsync_GivenGone_DisablesTheEndpointInTheSameTransaction()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        (Delivery delivery, ClaimedDelivery claimed) = await SeedClaimedAsync(context);

        DeliveryCompletion completion = DeliveryCompletion.From(
            claimed,
            Attempt(delivery.Id, AttemptOutcome.Gone, 410),
            new RetryDecision.RetireEndpoint(),
            Now.AddSeconds(2));

        await new TestLeaseStore(context).CompleteAsync("web-01:4242:a1b2c3", [completion], TestContext.Current.CancellationToken);

        await using HookwrightDbContext reader = TestDatabase.CreateContext(connection);
        WebhookEndpoint endpoint = await reader.Set<WebhookEndpoint>()
            .SingleAsync(TestContext.Current.CancellationToken);

        endpoint.IsEnabled.ShouldBeFalse();
        endpoint.DisabledReason.ShouldBe(DeliveryCompletion.RetirementReason);
    }

    private static DeliveryAttempt Attempt(DeliveryId deliveryId, AttemptOutcome outcome, int status = 200)
    {
        return DeliveryAttempt.FromResponse(
            deliveryId, outcome, status, "body", null, Now.AddSeconds(1),
            TimeSpan.FromMilliseconds(120), "web-01:4242:a1b2c3");
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
        context.Add(EndpointSecret.Create(endpoint.Id, "protected-keyu", SignatureAlgorithm.HmacSha256, Now));
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