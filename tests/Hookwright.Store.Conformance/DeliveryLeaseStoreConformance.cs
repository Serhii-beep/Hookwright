using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;

namespace Hookwright.Store.Conformance;

public abstract class DeliveryLeaseStoreConformance : StoreConformance
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    protected DeliveryLeaseStoreConformance(IStoreHarness harness)
        : base(harness)
    {

    }

    [Fact]
    public async Task ClaimAsync_TakesDueDeliveriesUnderALease()
    {
        WebhookEndpoint endpoint = await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);

        ClaimedDelivery only = claimed.ShouldHaveSingleItem();
        only.AttemptCount.ShouldBe(1);
        only.LeaseExpiresAt.ShouldBe(Now + Lease);
        only.EndpointId.ShouldBe(endpoint.Id);
        only.Url.ShouldBe(endpoint.Url);
        only.EventType.ShouldBe("order.created");
        only.Payload.ShouldBe("""{"index":0}""");

        only.Secrets.ShouldHaveSingleItem().ProtectedKey.ShouldBe("protected-key");
    }

    [Fact]
    public async Task ClaimAsync_GivenNothingDueYet_ReturnsNothing()
    {
        await SeedAsync(3);

        await using IStoreSession worker = OpenSession();

        (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now.AddHours(-1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ClaimAsync_TakesNoMoreThanTheBatchSize()
    {
        await SeedAsync(5);

        await using IStoreSession worker = OpenSession();

        (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken))
            .Count.ShouldBe(2);
    }

    [Fact]
    public async Task ClaimAsync_GivenManyWorkersAtOnce_NeverHandsOutTheSameDeliveryTwice()
    {
        const int Workers = 16;
        const int Available = 20;
        const int Batch = 5;

        await SeedAsync(Available);

        CancellationToken token = TestContext.Current.CancellationToken;

        IReadOnlyList<ClaimedDelivery>[] results = await Task.WhenAll(
            Enumerable.Range(0, Workers).Select(worker => Task.Run(
                async () =>
                {
                    await using IStoreSession session = OpenSession();

                    return await session.Deliveries.ClaimAsync(
                        $"worker-{worker}", Batch, Lease, Now, token);
                },
                token)));

        DeliveryId[] claimed =
        [
            .. results.SelectMany(result => result.Select(delivery => delivery.DeliveryId))
        ];

        claimed.Distinct().Count().ShouldBe(Available);
        claimed.Length.ShouldBe(Available);
    }

    [Fact]
    public async Task ClaimAsync_CountsAnAttemptEvenWhenTheWorkerNeverReports()
    {
        await SeedAsync(1);

        await using IStoreSession dying = OpenSession();
        (await dying.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .AttemptCount.ShouldBe(1);

        DateTimeOffset afterTheLease = Now + Lease + TimeSpan.FromMinutes(1);

        await using IStoreSession session = OpenSession();
        (await session.Deliveries.ReclaimExpiredLeasesAsync(afterTheLease, TestContext.Current.CancellationToken))
            .ShouldBe(1);

        await using IStoreSession next = OpenSession();
        (await next.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, afterTheLease, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task CompleteAsync_GivenAHeldLease_CompletesTheDelivery()
    {
        await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        ClaimedDelivery claimed = (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        (await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Completion(claimed, new RetryDecision.Succeeded(), Now.AddSeconds(1))],
            TestContext.Current.CancellationToken))
            .ShouldBe(1);

        await using IStoreSession anotherWorker = OpenSession();
        (await anotherWorker.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, Now.AddHours(1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_GivenARetry_ReturnsTheDeliveryToTheQueueWhenItIsDue()
    {
        await SeedAsync(1);
        DateTimeOffset retryAt = Now.AddMinutes(5);

        await using IStoreSession worker = OpenSession();

        ClaimedDelivery claimed = (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Completion(claimed, new RetryDecision.Retry(retryAt), Now.AddSeconds(1))],
            TestContext.Current.CancellationToken);

        await using IStoreSession early = OpenSession();
        (await early.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, retryAt.AddSeconds(-1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();

        await using IStoreSession due = OpenSession();
        (await due.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, retryAt, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task CompleteAsync_GivenALapsedLease_ChangesNothing()
    {
        await SeedAsync(1);

        await using IStoreSession stalled = OpenSession();
        ClaimedDelivery claimed = (await stalled.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        (await stalled.Deliveries.CompleteAsync(
            "web-02:4242:a1b2c3", [Completion(claimed, new RetryDecision.Succeeded(), Now.AddSeconds(1))],
            TestContext.Current.CancellationToken))
            .ShouldBe(0);

        DateTimeOffset afterLease = Now + Lease + TimeSpan.FromMinutes(1);

        await using IStoreSession alive = OpenSession();
        (await alive.Deliveries.ReclaimExpiredLeasesAsync(
            afterLease, TestContext.Current.CancellationToken))
            .ShouldBe(1);
    }

    [Fact]
    public async Task CompleteAsync_GivenGone_DisablesTheEndpoint()
    {
        WebhookEndpoint endpoint = await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        ClaimedDelivery claimed = (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [Completion(claimed, new RetryDecision.RetireEndpoint(), Now.AddSeconds(1), AttemptOutcome.Gone, 410)],
            TestContext.Current.CancellationToken);

        await using IStoreSession reader = OpenSession();

        WebhookEndpoint retired = (await reader.Endpoints.FindAsync(
            endpoint.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        retired.IsEnabled.ShouldBeFalse();
        retired.DisabledReason.ShouldBe(DeliveryCompletion.RetirementReason);

        (await reader.Endpoints.ListForSubscriberAsync(
            endpoint.SubscriberId, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .Id.ShouldBe(endpoint.Id);
    }

    [Fact]
    public async Task ReclaimExpiredLeasesAsync_GivenALiveLease_LeavesItAlone()
    {
        await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        await worker.Deliveries.ClaimAsync("web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);

        (await worker.Deliveries.ReclaimExpiredLeasesAsync(
            Now.AddSeconds(30), TestContext.Current.CancellationToken))
            .ShouldBe(0);

        (await worker.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, Now.AddSeconds(30), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ReclaimExpiredLeasesAsync_GivenAnInstantWithAnOffset_ReclaimsByTheInstant()
    {
        await SeedAsync(1);

        await using IStoreSession dying = OpenSession();
        await dying.Deliveries.ClaimAsync("web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);

        DateTimeOffset afterTheLease = (Now + Lease + TimeSpan.FromMinutes(1)).ToOffset(TimeSpan.FromHours(-5));

        await using IStoreSession worker = OpenSession();
        (await worker.Deliveries.ReclaimExpiredLeasesAsync(afterTheLease, TestContext.Current.CancellationToken))
            .ShouldBe(1);

        await using IStoreSession nextWorker = OpenSession();
        (await nextWorker.Deliveries.ClaimAsync("web-02:4242:a1b2c3", 10, Lease, afterTheLease, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .LeaseExpiresAt.ShouldBe(afterTheLease + Lease);
    }

    private async Task<WebhookEndpoint> SeedAsync(int count)
    {
        await using IStoreSession session = OpenSession();

        return await SeedDueDeliveriesAsync(session, count, TestContext.Current.CancellationToken);
    }

    private static DeliveryCompletion Completion(
        ClaimedDelivery claimed,
        RetryDecision decision,
        DateTimeOffset at,
        AttemptOutcome outcome = AttemptOutcome.Succeeded,
        int responseStatusCode = 200)
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            claimed.DeliveryId, outcome, responseStatusCode, "body",
            null, at, TimeSpan.FromMilliseconds(20), "web-01:4242:a1b2c3");

        return DeliveryCompletion.From(claimed, attempt, decision, at);
    }
}