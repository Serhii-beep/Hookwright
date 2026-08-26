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

    private async Task<WebhookEndpoint> SeedAsync(int count)
    {
        await using IStoreSession session = OpenSession();

        return await SeedDueDeliveriesAsync(session, count, TestContext.Current.CancellationToken);
    }
}