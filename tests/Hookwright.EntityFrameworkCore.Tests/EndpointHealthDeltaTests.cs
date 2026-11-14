using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore.Stores;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class EndpointHealthDeltaTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_GivenOnlyFailures_CountsThemFromTheFirst()
    {
        WebhookEndpointId endpoint = WebhookEndpointId.New();

        IReadOnlyDictionary<WebhookEndpointId, EndpointHealthDelta> deltas = EndpointHealthDelta.From(
        [
            Failure(endpoint, Now.AddSeconds(2), AttemptOutcome.TimedOut),
            Failure(endpoint, Now.AddSeconds(1), AttemptOutcome.ServerError),
            Failure(endpoint, Now.AddSeconds(3), AttemptOutcome.Blocked)
        ]);

        EndpointHealthDelta delta = deltas.ShouldHaveSingleItem().Value;
        delta.Answered.ShouldBeFalse();
        delta.Failures.ShouldBe(3);
        delta.FailingSince.ShouldBe(Now.AddSeconds(1));
        delta.ClearsFailures.ShouldBeFalse();
    }

    [Fact]
    public void From_GivenAnAnswerAmongFailures_CountsOnlyTheFailuresAfterItWhateverTheBatchOrder()
    {
        WebhookEndpointId endpoint = WebhookEndpointId.New();

        IReadOnlyDictionary<WebhookEndpointId, EndpointHealthDelta> deltas = EndpointHealthDelta.From(
        [
            Failure(endpoint, Now.AddSeconds(3), AttemptOutcome.ServerError),
            Answer(endpoint, Now.AddSeconds(2), AttemptOutcome.Rejected),
            Failure(endpoint, Now.AddSeconds(1), AttemptOutcome.ServerError)
        ]);

        EndpointHealthDelta delta = deltas.ShouldHaveSingleItem().Value;
        delta.Answered.ShouldBeTrue();
        delta.Failures.ShouldBe(1);
        delta.FailingSince.ShouldBe(Now.AddSeconds(3));
        delta.ClearsFailures.ShouldBeFalse();
    }

    [Fact]
    public void From_GivenAnAnswerLast_ClearsTheFailures()
    {
        WebhookEndpointId endpoint = WebhookEndpointId.New();

        IReadOnlyDictionary<WebhookEndpointId, EndpointHealthDelta> deltas = EndpointHealthDelta.From(
        [
            Failure(endpoint, Now.AddSeconds(1), AttemptOutcome.ServerError),
            Answer(endpoint, Now.AddSeconds(2), AttemptOutcome.Succeeded)
        ]);

        EndpointHealthDelta delta = deltas.ShouldHaveSingleItem().Value;
        delta.Answered.ShouldBeTrue();
        delta.Failures.ShouldBe(0);
        delta.FailingSince.ShouldBeNull();
        delta.ClearsFailures.ShouldBeTrue();
    }

    [Fact]
    public void From_GivenCompletionsThatSayNothingAboutTheEndpoint_LeavesItOut()
    {
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        ClaimedDelivery claimed = Claimed(endpoint);

        IReadOnlyDictionary<WebhookEndpointId, EndpointHealthDelta> deltas = EndpointHealthDelta.From(
        [
            DeliveryCompletion.Deferred(claimed, Now.AddSeconds(30)),
            DeliveryCompletion.Cancelled(Claimed(endpoint), Now),
            Completion(Claimed(endpoint), AttemptOutcome.Gone, Now, new RetryDecision.RetireEndpoint()),
            Completion(Claimed(endpoint), AttemptOutcome.Unsendable, Now, new RetryDecision.Fail())
        ]);

        deltas.ShouldBeEmpty();
    }

    [Fact]
    public void From_GivenSeveralEndpoints_KeepsThemApart()
    {
        WebhookEndpointId failing = WebhookEndpointId.New();
        WebhookEndpointId answering = WebhookEndpointId.New();

        IReadOnlyDictionary<WebhookEndpointId, EndpointHealthDelta> deltas = EndpointHealthDelta.From(
        [
            Failure(failing, Now.AddSeconds(1), AttemptOutcome.ServerError),
            Answer(answering, Now.AddSeconds(1), AttemptOutcome.Succeeded),
            Failure(failing, Now.AddSeconds(2), AttemptOutcome.ServerError)
        ]);

        deltas.Count.ShouldBe(2);
        deltas[failing].Failures.ShouldBe(2);
        deltas[failing].Answered.ShouldBeFalse();
        deltas[answering].ClearsFailures.ShouldBeTrue();
    }

    private static DeliveryCompletion Failure(WebhookEndpointId endpoint, DateTimeOffset at, AttemptOutcome outcome)
    {
        return Completion(Claimed(endpoint), outcome, at, new RetryDecision.Retry(at.AddMinutes(5)));
    }

    private static DeliveryCompletion Answer(WebhookEndpointId endpoint, DateTimeOffset at, AttemptOutcome outcome)
    {
        return Completion(Claimed(endpoint), outcome, at, outcome == AttemptOutcome.Succeeded
            ? new RetryDecision.Succeeded()
            : new RetryDecision.Fail());
    }

    private static DeliveryCompletion Completion(
        ClaimedDelivery claimed,
        AttemptOutcome outcome,
        DateTimeOffset at,
        RetryDecision decision)
    {
        DeliveryAttempt attempt = outcome is AttemptOutcome.TimedOut or AttemptOutcome.ConnectionFailed or AttemptOutcome.Blocked or AttemptOutcome.Unsendable
            ? DeliveryAttempt.FromFailure(claimed.DeliveryId, outcome, "no answer", at, TimeSpan.FromMilliseconds(1), "web-01:4242:a1b2c3")
            : DeliveryAttempt.FromResponse(claimed.DeliveryId, outcome, 200, null, null, at, TimeSpan.FromMilliseconds(1), "web-01:4242:a1b2c3");

        return DeliveryCompletion.From(claimed, attempt, decision, at);
    }

    private static ClaimedDelivery Claimed(WebhookEndpointId endpoint)
    {
        return new ClaimedDelivery
        {
            DeliveryId = DeliveryId.New(),
            AttemptCount = 1,
            LeaseExpiresAt = Now.AddMinutes(1),
            EventId = WebhookEventId.New(),
            EventType = "order.created",
            Payload = "{}",
            Headers = new Dictionary<string, string>(),
            EndpointId = endpoint,
            Url = new Uri("https://example.com/"),
            Secrets = [],
            SubscriberId = SubscriberId.New(),
            EndpointHealth = EndpointHealth.Healthy,
            ConsecutiveFailures = 0,
            FailingSince = null
        };
    }
}