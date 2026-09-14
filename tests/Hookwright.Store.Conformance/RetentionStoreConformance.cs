using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;

namespace Hookwright.Store.Conformance;

public abstract class RetentionStoreConformance : StoreConformance
{
    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    protected RetentionStoreConformance(IStoreHarness harness)
        : base(harness)
    {

    }

    [Fact]
    public async Task PruneAsync_GivenNothingAged_RemovesNothing()
    {
        await using IStoreSession session = OpenSession();
        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        await FinishDeliveryAsync(session, endpoint, Now, new RetryDecision.Succeeded());

        PruneResult result = await session.Retention.PruneAsync(Cutoffs(), 100, TestContext.Current.CancellationToken);

        result.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task PruneAsync_GivenAgedAttempts_RemovesOnlyThoseBeforeCutoff()
    {
        await using IStoreSession session = OpenSession();
        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-10), new RetryDecision.Succeeded());
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-5), new RetryDecision.Succeeded());
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-1), new RetryDecision.Succeeded());

        (await session.Retention.PruneAsync(Cutoffs(attemptDays: 7), 100, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 1, Deliveries: 0, Events: 0));

        (await session.Retention.PruneAsync(Cutoffs(attemptDays: 2), 100, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 1, Deliveries: 0, Events: 0));
    }

    [Fact]
    public async Task PruneAsync_GivenAnAgedEventAndItsFinishedDelivery_RemovesBothInOnePass()
    {
        await using IStoreSession session = OpenSession();
        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-40), new RetryDecision.Succeeded());

        (await session.Retention.PruneAsync(Cutoffs(), 100, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 1, Deliveries: 1, Events: 1));

        (await session.Retention.PruneAsync(Cutoffs(), 100, TestContext.Current.CancellationToken))
            .IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task PruneAsync_GivenADeadDelivery_KeepsItAndItsEventPastTheOrdinaryWindow()
    {
        await using IStoreSession session = OpenSession();
        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-40), new RetryDecision.Exhausted());

        (await session.Retention.PruneAsync(Cutoffs(), 100, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 1, Deliveries: 0, Events: 0));

        (await session.Retention.PruneAsync(Cutoffs(deadDays: 30), 100, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 0, Deliveries: 1, Events: 1));
    }

    [Fact]
    public async Task PruneAsync_GivenAnAgedEventWithADeliveryStillPending_KeepsTheEvent()
    {
        await using IStoreSession session = OpenSession();
        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        await PublishAsync(session, endpoint, Now.AddDays(-40));

        (await session.Retention.PruneAsync(Cutoffs(), 100, TestContext.Current.CancellationToken))
            .IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task PruneAsync_RemovesNoMoreThanTheBatchSizeOfEachKind()
    {
        await using IStoreSession session = OpenSession();
        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-42), new RetryDecision.Succeeded());
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-41), new RetryDecision.Succeeded());
        await FinishDeliveryAsync(session, endpoint, Now.AddDays(-40), new RetryDecision.Succeeded());

        (await session.Retention.PruneAsync(Cutoffs(), 2, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 2, Deliveries: 2, Events: 2));

        (await session.Retention.PruneAsync(Cutoffs(), 2, TestContext.Current.CancellationToken))
            .ShouldBe(new PruneResult(Attempts: 1, Deliveries: 1, Events: 1));

        (await session.Retention.PruneAsync(Cutoffs(), 2, TestContext.Current.CancellationToken))
            .IsEmpty.ShouldBeTrue();
    }

    private static RetentionCutoffs Cutoffs(int attemptDays = 7, int eventDays = 30, int deadDays = 90)
    {
        return RetentionCutoffs.From(
            new RetentionOptions
            {
                Attempts = TimeSpan.FromDays(attemptDays),
                Events = TimeSpan.FromDays(eventDays),
                DeadDeliveries = TimeSpan.FromDays(deadDays)
            }, Now);
    }

    private static async Task PublishAsync(IStoreSession session, WebhookEndpoint endpoint, DateTimeOffset at)
    {
        WebhookEvent published = WebhookEvent.Create(endpoint.SubscriberId, "order.created", """{"index":0}""", at);

        session.Events.Append(EventPublication.Create(
            published,
            [Delivery.Create(published.Id, endpoint.Id, null, at)]));

        await session.Events.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static async Task FinishDeliveryAsync(
        IStoreSession session,
        WebhookEndpoint endpoint,
        DateTimeOffset at,
        RetryDecision decision)
    {
        await PublishAsync(session, endpoint, at);

        ClaimedDelivery claimed = (await session.Deliveries.ClaimAsync(
            Worker, 10, Lease, at, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            claimed.DeliveryId, AttemptOutcome.Succeeded, 200, "body", null, at, TimeSpan.FromMilliseconds(20), Worker);

        await session.Deliveries.CompleteAsync(
            Worker, [DeliveryCompletion.From(claimed, attempt, decision, at)], TestContext.Current.CancellationToken);
    }
}