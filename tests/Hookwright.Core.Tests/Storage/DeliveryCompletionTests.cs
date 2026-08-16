using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;

namespace Hookwright.Core.Tests.Storage;

public sealed class DeliveryCompletionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ClaimedDelivery Claimed() => new()
    {
        DeliveryId = DeliveryId.New(),
        AttemptCount = 1,
        LeaseExpiresAt = Now.AddSeconds(60),
        EventId = WebhookEventId.New(),
        EventType = "order.created",
        Payload = """{"id":1}""",
        Headers = new Dictionary<string, string>(),
        EndpointId = WebhookEndpointId.New(),
        Url = new Uri("https://example.com"),
        Secrets = []
    };

    private static DeliveryAttempt Attempt() => DeliveryAttempt.FromResponse(
        DeliveryId.New(), AttemptOutcome.Succeeded, 200, "ok", null, Now, TimeSpan.FromMilliseconds(20), "w:1:a");

    [Fact]
    public void From_GivenSuccess_CompletesTheDelivery()
    {
        DeliveryCompletion completion = DeliveryCompletion.From(
            Claimed(), Attempt(), new RetryDecision.Succeeded(), Now);

        completion.State.ShouldBe(DeliveryState.Succeeded);
        completion.CompletedAt.ShouldBe(Now);
        completion.NextAttemptAt.ShouldBeNull();
        completion.RetireEndpoint.ShouldBeFalse();
    }

    [Fact]
    public void From_GivenARetry_ReturnsItToTheQueueWithoutCompleting()
    {
        DateTimeOffset next = Now.AddMinutes(5);

        DeliveryCompletion completion = DeliveryCompletion.From(
            Claimed(), Attempt(), new RetryDecision.Retry(next), Now);

        completion.State.ShouldBe(DeliveryState.Pending);
        completion.NextAttemptAt.ShouldBe(next);
        completion.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void From_GivenAFailure_MarksItFailedAndLeavesTheEndpointAlone()
    {
        DeliveryCompletion completion = DeliveryCompletion.From(
            Claimed(), Attempt(), new RetryDecision.Fail(), Now);

        completion.State.ShouldBe(DeliveryState.Failed);
        completion.RetireEndpoint.ShouldBeFalse();
    }

    [Fact]
    public void From_GivenExhaustion_MarksItDead()
    {
        DeliveryCompletion completion = DeliveryCompletion.From(
            Claimed(), Attempt(), new RetryDecision.Exhausted(), Now);

        completion.State.ShouldBe(DeliveryState.Dead);
        completion.CompletedAt.ShouldBe(Now);
    }

    [Fact]
    public void From_GivenGone_FailsTheDeliveryAndRetiresTheEndpoint()
    {
        DeliveryCompletion completion = DeliveryCompletion.From(
            Claimed(), Attempt(), new RetryDecision.RetireEndpoint(), Now);

        completion.State.ShouldBe(DeliveryState.Failed);
        completion.RetireEndpoint.ShouldBeTrue();
    }

    [Fact]
    public void From_Always_KeepsCompletedAtInStepWithTerminality()
    {
        RetryDecision[] decisions =
        [
            new RetryDecision.Succeeded(),
            new RetryDecision.Fail(),
            new RetryDecision.Exhausted(),
            new RetryDecision.RetireEndpoint(),
            new RetryDecision.Retry(Now.AddMinutes(1))
        ];

        foreach (RetryDecision decision in decisions)
        {
            DeliveryCompletion completion = DeliveryCompletion.From(Claimed(), Attempt(), decision, Now);

            (completion.CompletedAt is not null)
                .ShouldBe(DeliveryStateMachine.IsTerminal(completion.State), decision.GetType().Name);
        }
    }

    [Fact]
    public void From_Always_ReachesOnlyStatesLegalFromInFlight()
    {
        RetryDecision[] decisions =
        [
            new RetryDecision.Succeeded(),
            new RetryDecision.Fail(),
            new RetryDecision.Exhausted(),
            new RetryDecision.RetireEndpoint(),
            new RetryDecision.Retry(Now)
        ];

        foreach (RetryDecision decision in decisions)
        {
            DeliveryCompletion completion = DeliveryCompletion.From(Claimed(), Attempt(), decision, Now);

            DeliveryStateMachine.CanTransition(DeliveryState.InFlight, completion.State).ShouldBeTrue(completion.State.ToString());
        }
    }
}