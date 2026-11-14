using Hookwright.Core.Configuration;
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
        only.SubscriberId.ShouldBe(endpoint.SubscriberId);
        only.EndpointHealth.ShouldBe(EndpointHealth.Healthy);
        only.ConsecutiveFailures.ShouldBe(0);
        only.FailingSince.ShouldBeNull();

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
    public async Task ClaimAsync_GivenABacklogAndManyWorkers_HandsOutEveryDeliveryExactlyOnce()
    {
        const int Backlog = 10000;
        const int Workers = 8;
        const int Batch = 100;

        await SeedAsync(Backlog);

        CancellationToken token = TestContext.Current.CancellationToken;

        List<DeliveryId>[] results = await Task.WhenAll(
            Enumerable.Range(0, Workers).Select(worker => Task.Run(
                async () =>
                {
                    List<DeliveryId> taken = [];
                    IReadOnlyList<ClaimedDelivery> claimed;

                    await using IStoreSession session = OpenSession();

                    do
                    {
                        claimed = await session.Deliveries.ClaimAsync($"worker-{worker}", Batch, Lease, Now, token);
                        taken.AddRange(claimed.Select(delivery => delivery.DeliveryId));
                    }
                    while (claimed.Count > 0);

                    return taken;
                },
                token)));

        DeliveryId[] claimed = [.. results.SelectMany(result => result)];

        claimed.Length.ShouldBe(Backlog);
        claimed.Distinct().Count().ShouldBe(Backlog);
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
            .ShouldHaveSingleItem()
            .ShouldBe(new ReclaimedLeases("web-01:4242:a1b2c3", 1));

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
            .Applied.ShouldBe(1);

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
            .Applied.ShouldBe(0);

        DateTimeOffset afterLease = Now + Lease + TimeSpan.FromMinutes(1);

        await using IStoreSession alive = OpenSession();
        (await alive.Deliveries.ReclaimExpiredLeasesAsync(
            afterLease, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .ShouldBe(new ReclaimedLeases("web-01:4242:a1b2c3", 1));
    }

    [Fact]
    public async Task CompleteAsync_GivenGone_DisablesTheEndpoint()
    {
        WebhookEndpoint endpoint = await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        ClaimedDelivery claimed = (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        CompletionReport report = await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [Completion(claimed, new RetryDecision.RetireEndpoint(), Now.AddSeconds(1), AttemptOutcome.Gone, 410)],
            TestContext.Current.CancellationToken);

        report.Retired.ShouldHaveSingleItem().Id.ShouldBe(endpoint.Id);

        await using IStoreSession reader = OpenSession();

        WebhookEndpoint retired = (await reader.Endpoints.FindAsync(
            endpoint.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        retired.IsEnabled.ShouldBeFalse();
        retired.DisabledReason.ShouldBe(DeliveryCompletion.GoneReason);
        retired.DisabledAt.ShouldBe(Now.AddSeconds(1));

        (await reader.Endpoints.ListForSubscriberAsync(
            endpoint.SubscriberId, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .Id.ShouldBe(endpoint.Id);
    }

    [Fact]
    public async Task CompleteAsync_GivenABatchOfMixedOutcomes_AppliesEachToItsOwnDelivery()
    {
        await SeedAsync(3);
        DateTimeOffset retryAt = Now.AddMinutes(5);


        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);
        claimed.Count.ShouldBe(3);

        (await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [
                Completion(claimed[0], new RetryDecision.Succeeded(), Now.AddSeconds(1)),
                Completion(claimed[1], new RetryDecision.Retry(retryAt), Now.AddSeconds(1), AttemptOutcome.ServerError, 503),
                Completion(claimed[2], new RetryDecision.Fail(), Now.AddSeconds(1), AttemptOutcome.Rejected, 400)
            ],
            TestContext.Current.CancellationToken))
            .Applied.ShouldBe(3);

        await using IStoreSession early = OpenSession();
        (await early.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, retryAt.AddSeconds(-1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();

        await using IStoreSession due = OpenSession();
        ClaimedDelivery retried = (await due.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, Now.AddHours(1), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        retried.DeliveryId.ShouldBe(claimed[1].DeliveryId);
        retried.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task CompleteAsync_GivenADeferral_ReturnsTheDeliveryToTheQueueWithItsAttemptGivenBack()
    {
        await SeedAsync(1);
        DateTimeOffset dueAt = Now.AddSeconds(30);

        await using IStoreSession worker = OpenSession();
        ClaimedDelivery claimed = (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        (await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [DeliveryCompletion.Deferred(claimed, dueAt)], TestContext.Current.CancellationToken))
            .Applied.ShouldBe(1);

        await using IStoreSession early = OpenSession();
        (await early.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, dueAt.AddSeconds(-1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();

        await using IStoreSession due = OpenSession();
        (await due.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, dueAt, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .AttemptCount.ShouldBe(1);

        (await CountAttemptsAsync(due)).ShouldBe(0);
    }

    [Fact]
    public async Task CompleteAsync_GivenACancellation_AbandonsTheDeliveryWithoutAnAttempt()
    {
        await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        ClaimedDelivery claimed = (await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        (await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [DeliveryCompletion.Cancelled(claimed, Now.AddSeconds(1))], TestContext.Current.CancellationToken))
            .Applied.ShouldBe(1);

        await using IStoreSession later = OpenSession();
        (await later.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, Now.AddHours(1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
        (await later.Deliveries.ReclaimExpiredLeasesAsync(
            Now + Lease + TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
        (await CountAttemptsAsync(later)).ShouldBe(0);
    }

    [Fact]
    public async Task CompleteAsync_GivenARetirement_DisablesTheEndpointForTheReasonAndReportsItOnce()
    {
        WebhookEndpoint endpoint = await SeedAsync(2);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);
        claimed.Count.ShouldBe(2);

        CompletionReport first = await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [Retirement(claimed[0], "The endpoint has failed continuously since 2025-12-27.", Now.AddSeconds(1))],
            TestContext.Current.CancellationToken);

        first.Applied.ShouldBe(1);
        first.Retired.ShouldHaveSingleItem().Id.ShouldBe(endpoint.Id);

        CompletionReport second = await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [Retirement(claimed[1], "The endpoint has failed continuously since 2025-12-28.", Now.AddSeconds(2))],
            TestContext.Current.CancellationToken);

        second.Applied.ShouldBe(1);
        second.Retired.ShouldBeEmpty();

        await using IStoreSession reader = OpenSession();
        WebhookEndpoint retired = (await reader.Endpoints.FindAsync(
            endpoint.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        retired.IsEnabled.ShouldBeFalse();
        retired.DisabledReason.ShouldBe("The endpoint has failed continuously since 2025-12-27.");
        retired.DisabledAt.ShouldBe(Now.AddSeconds(1));
        (await CountAttemptsAsync(reader)).ShouldBe(2);
    }

    [Theory]
    [InlineData(2, EndpointHealth.Healthy)]
    [InlineData(3, EndpointHealth.Degraded)]
    [InlineData(10, EndpointHealth.Failing)]
    public async Task CompleteAsync_GivenFailures_CountsThemAgainstTheEndpointFromTheFirst(int failures, EndpointHealth expected)
    {
        WebhookEndpoint endpoint = await SeedAsync(failures);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", failures, Lease, Now, TestContext.Current.CancellationToken);

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [.. claimed.Select((delivery, i) => Failure(delivery, Now.AddSeconds(i + 1)))],
            TestContext.Current.CancellationToken);

        WebhookEndpoint found = await FindAsync(endpoint.Id);
        found.ConsecutiveFailures.ShouldBe(failures);
        found.FailingSince.ShouldBe(Now.AddSeconds(1));
        found.Health.ShouldBe(expected);
    }

    [Fact]
    public async Task CompleteAsync_GivenFailuresInSeparateBatches_AddsThemToWhatTheEndpointHad()
    {
        WebhookEndpoint endpoint = await SeedAsync(2);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken);

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Failure(claimed[0], Now.AddSeconds(1))], TestContext.Current.CancellationToken);
        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Failure(claimed[1], Now.AddSeconds(2))], TestContext.Current.CancellationToken);

        WebhookEndpoint found = await FindAsync(endpoint.Id);
        found.ConsecutiveFailures.ShouldBe(2);
        found.FailingSince.ShouldBe(Now.AddSeconds(1));
        found.Health.ShouldBe(EndpointHealth.Healthy);
    }

    [Fact]
    public async Task CompleteAsync_GivenAnAnswer_EndsTheEndpointsRunOfFailures()
    {
        WebhookEndpoint endpoint = await SeedAsync(2);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken);

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Failure(claimed[0], Now.AddSeconds(1))], TestContext.Current.CancellationToken);
        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [Completion(claimed[1], new RetryDecision.Fail(), Now.AddSeconds(2), AttemptOutcome.Rejected, 403)],
            TestContext.Current.CancellationToken);

        WebhookEndpoint found = await FindAsync(endpoint.Id);
        found.ConsecutiveFailures.ShouldBe(0);
        found.FailingSince.ShouldBeNull();
        found.Health.ShouldBe(EndpointHealth.Healthy);
    }

    [Fact]
    public async Task CompleteAsync_GivenABatchInWhichTheEndpointAnswersThenFails_KeepsOnlyWhatFollowedTheAnswer()
    {
        WebhookEndpoint endpoint = await SeedAsync(4);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 4, Lease, Now, TestContext.Current.CancellationToken);

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Failure(claimed[0], Now.AddSeconds(1))], TestContext.Current.CancellationToken);
        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [
                Failure(claimed[3], Now.AddSeconds(4)),
                Completion(claimed[2], new RetryDecision.Succeeded(), Now.AddSeconds(3)),
                Failure(claimed[1], Now.AddSeconds(2))
            ],
            TestContext.Current.CancellationToken);

        WebhookEndpoint found = await FindAsync(endpoint.Id);
        found.ConsecutiveFailures.ShouldBe(1);
        found.FailingSince.ShouldBe(Now.AddSeconds(4));
        found.Health.ShouldBe(EndpointHealth.Healthy);
    }

    [Fact]
    public async Task CompleteAsync_GivenAnAttemptThatSaysNothingAboutTheEndpoint_LeavesItsHealthAlone()
    {
        WebhookEndpoint endpoint = await SeedAsync(2);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken);

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Failure(claimed[0], Now.AddSeconds(1))], TestContext.Current.CancellationToken);
        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Unsendable(claimed[1], Now.AddSeconds(2))], TestContext.Current.CancellationToken);

        WebhookEndpoint found = await FindAsync(endpoint.Id);
        found.ConsecutiveFailures.ShouldBe(1);
        found.FailingSince.ShouldBe(Now.AddSeconds(1));
    }

    [Fact]
    public async Task CompleteAsync_GivenADisabledEndpoint_LeavesItsHealthAlone()
    {
        WebhookEndpoint endpoint = await SeedAsync(2);

        await using IStoreSession worker = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await worker.Deliveries.ClaimAsync(
            "web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken);

        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3",
            [Retirement(claimed[0], "The endpoint has failed continuously since 2025-12-27.", Now.AddSeconds(1))],
            TestContext.Current.CancellationToken);
        await worker.Deliveries.CompleteAsync(
            "web-01:4242:a1b2c3", [Failure(claimed[1], Now.AddSeconds(2))], TestContext.Current.CancellationToken);

        WebhookEndpoint found = await FindAsync(endpoint.Id);
        found.Health.ShouldBe(EndpointHealth.Disabled);
        found.ConsecutiveFailures.ShouldBe(1);
        found.FailingSince.ShouldBe(Now.AddSeconds(1));
    }

    [Fact]
    public async Task ReclaimExpiredLeasesAsync_GivenALiveLease_LeavesItAlone()
    {
        await SeedAsync(1);

        await using IStoreSession worker = OpenSession();
        await worker.Deliveries.ClaimAsync("web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken);

        (await worker.Deliveries.ReclaimExpiredLeasesAsync(
            Now.AddSeconds(30), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();

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
            .ShouldHaveSingleItem()
            .ShouldBe(new ReclaimedLeases("web-01:4242:a1b2c3", 1));

        await using IStoreSession nextWorker = OpenSession();
        (await nextWorker.Deliveries.ClaimAsync("web-02:4242:a1b2c3", 10, Lease, afterTheLease, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .LeaseExpiresAt.ShouldBe(afterTheLease + Lease);
    }

    [Fact]
    public async Task ReclaimExpiredLeasesAsync_GivenTwoDeadWorkers_NamesEachWithWhatItDropped()
    {
        await SeedAsync(3);

        await using IStoreSession first = OpenSession();
        (await first.Deliveries.ClaimAsync("web-01:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken))
            .Count.ShouldBe(2);

        await using IStoreSession second = OpenSession();
        (await second.Deliveries.ClaimAsync("web-02:4242:a1b2c3", 2, Lease, Now, TestContext.Current.CancellationToken))
            .Count.ShouldBe(1);

        DateTimeOffset afterTheLease = Now + Lease + TimeSpan.FromMinutes(1);

        await using IStoreSession reaper = OpenSession();
        IReadOnlyList<ReclaimedLeases> reclaimed = await reaper.Deliveries.ReclaimExpiredLeasesAsync(
            afterTheLease, TestContext.Current.CancellationToken);

        reclaimed.ShouldBe(
            [new ReclaimedLeases("web-01:4242:a1b2c3", 2), new ReclaimedLeases("web-02:4242:a1b2c3", 1)],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ReleaseAsync_GivenAStoppingWorker_HandsItsLeasesBackWithTheirAttempt()
    {
        await SeedAsync(2);

        await using IStoreSession stopping = OpenSession();
        (await stopping.Deliveries.ClaimAsync("web-01:4242:a1b2c3", 10, Lease, Now, TestContext.Current.CancellationToken))
            .Count.ShouldBe(2);

        (await stopping.Deliveries.ReleaseAsync("web-01:4242:a1b2c3", Now.AddSeconds(5), TestContext.Current.CancellationToken))
            .ShouldBe(2);

        await using IStoreSession next = OpenSession();
        IReadOnlyList<ClaimedDelivery> claimed = await next.Deliveries.ClaimAsync(
            "web-02:4242:a1b2c3", 10, Lease, Now.AddSeconds(5), TestContext.Current.CancellationToken);

        claimed.Count.ShouldBe(2);
        claimed.ShouldAllBe(delivery => delivery.AttemptCount == 1);
    }

    [Fact]
    public async Task ReleaseAsync_GivenAnotherWorkersLeases_LeavesThemAlone()
    {
        await SeedAsync(2);

        await using IStoreSession first = OpenSession();
        await first.Deliveries.ClaimAsync("web-01:4242:a1b2c3", 1, Lease, Now, TestContext.Current.CancellationToken);

        await using IStoreSession second = OpenSession();
        await second.Deliveries.ClaimAsync("web-02:4242:a1b2c3", 1, Lease, Now, TestContext.Current.CancellationToken);

        (await first.Deliveries.ReleaseAsync("web-01:4242:a1b2c3", Now, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await first.Deliveries.ReleaseAsync("web-01:4242:a1b2c3", Now, TestContext.Current.CancellationToken)).ShouldBe(0);

        await using IStoreSession reaper = OpenSession();
        (await reaper.Deliveries.ReclaimExpiredLeasesAsync(
            Now + Lease + TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .ShouldBe(new ReclaimedLeases("web-02:4242:a1b2c3", 1));
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

    private static DeliveryCompletion Retirement(ClaimedDelivery claimed, string reason, DateTimeOffset at)
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            claimed.DeliveryId, AttemptOutcome.ServerError, 503, "body",
            null, at, TimeSpan.FromMilliseconds(20), "web-01:4242:a1b2c3");

        return DeliveryCompletion.Retiring(claimed, attempt, reason, at);
    }

    private static DeliveryCompletion Failure(ClaimedDelivery claimed, DateTimeOffset at)
    {
        return Completion(claimed, new RetryDecision.Retry(at.AddMinutes(5)), at, AttemptOutcome.ServerError, 503);
    }

    private static DeliveryCompletion Unsendable(ClaimedDelivery claimed, DateTimeOffset at)
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromFailure(
            claimed.DeliveryId, AttemptOutcome.Unsendable, "no key", at, TimeSpan.FromMilliseconds(1), "web-01:4242:a1b2c3");

        return DeliveryCompletion.From(claimed, attempt, new RetryDecision.Fail(), at);
    }

    private async Task<WebhookEndpoint> FindAsync(WebhookEndpointId endpointId)
    {
        await using IStoreSession reader = OpenSession();

        return (await reader.Endpoints.FindAsync(endpointId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    private static async Task<int> CountAttemptsAsync(IStoreSession session)
    {
        RetentionOptions options = new()
        {
            Attempts = TimeSpan.FromTicks(1),
            Events = TimeSpan.FromTicks(1),
            DeadDeliveries = TimeSpan.FromTicks(1)
        };

        return (await session.Retention.PruneAsync(
            RetentionCutoffs.From(options, Now.AddYears(1)), 1000, TestContext.Current.CancellationToken))
            .Attempts;
    }
}