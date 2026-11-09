using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;

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
        Secrets = [],
        SubscriberId = SubscriberId.New(),
        EndpointHealth = EndpointHealth.Healthy,
        ConsecutiveFailures = 0,
        FailingSince = null
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
        completion.RetirementReason.ShouldBeNull();
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
        completion.RetirementReason.ShouldBeNull();
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
        completion.RetirementReason.ShouldBe(DeliveryCompletion.GoneReason);
    }

    [Fact]
    public void From_Always_CarriesTheAttemptAndKeepsTheClaimsAttemptCounted()
    {
        DeliveryAttempt attempt = Attempt();

        DeliveryCompletion completion = DeliveryCompletion.From(Claimed(), attempt, new RetryDecision.Succeeded(), Now);

        completion.Attempt.ShouldBeSameAs(attempt);
        completion.GivesBackAttempt.ShouldBeFalse();
    }

    [Fact]
    public void Deferred_Always_ReturnsTheDeliveryToTheQueueUnattemptedWithItsAttemptGivenBack()
    {
        ClaimedDelivery claimed = Claimed();
        DateTimeOffset dueAt = Now.AddSeconds(30);

        DeliveryCompletion completion = DeliveryCompletion.Deferred(claimed, dueAt);

        completion.DeliveryId.ShouldBe(claimed.DeliveryId);
        completion.EndpointId.ShouldBe(claimed.EndpointId);
        completion.State.ShouldBe(DeliveryState.Pending);
        completion.NextAttemptAt.ShouldBe(dueAt);
        completion.CompletedAt.ShouldBeNull();
        completion.Attempt.ShouldBeNull();
        completion.GivesBackAttempt.ShouldBeTrue();
        completion.RetirementReason.ShouldBeNull();
    }

    [Fact]
    public void Cancelled_Always_AbandonsTheDeliveryUnattempted()
    {
        DeliveryCompletion completion = DeliveryCompletion.Cancelled(Claimed(), Now);

        completion.State.ShouldBe(DeliveryState.Cancelled);
        completion.CompletedAt.ShouldBe(Now);
        completion.NextAttemptAt.ShouldBeNull();
        completion.Attempt.ShouldBeNull();
        completion.GivesBackAttempt.ShouldBeFalse();
        completion.RetirementReason.ShouldBeNull();
    }

    [Fact]
    public void Retiring_Always_RecordsTheAttemptAbandonsTheDeliveryAndNamesTheReason()
    {
        DeliveryAttempt attempt = Attempt();

        DeliveryCompletion completion = DeliveryCompletion.Retiring(
            Claimed(), attempt, "The endpoint has failed continuously since 2025-12-27.", Now);

        completion.State.ShouldBe(DeliveryState.Cancelled);
        completion.CompletedAt.ShouldBe(Now);
        completion.NextAttemptAt.ShouldBeNull();
        completion.Attempt.ShouldBeSameAs(attempt);
        completion.GivesBackAttempt.ShouldBeFalse();
        completion.RetirementReason.ShouldBe("The endpoint has failed continuously since 2025-12-27.");
    }

    [Fact]
    public void Retiring_GivenABlankReason_Throws()
    {
        Should.Throw<ArgumentException>(() => DeliveryCompletion.Retiring(Claimed(), Attempt(), "   ", Now));
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