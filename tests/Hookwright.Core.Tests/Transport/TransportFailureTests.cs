using Hookwright.Core.Deliveries;
using Hookwright.Core.Transport;

namespace Hookwright.Core.Tests.Transport;

public sealed class TransportFailureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void Classify_GivenTheClientTimeout_IsTimedOut()
    {
        TaskCanceledException timedOut = new("The request was canceled due to the configured timeout.", new TimeoutException());

        (AttemptOutcome Outcome, string Detail)? failure = TransportFailure.Classify(timedOut, Timeout);

        failure.ShouldNotBeNull();
        failure.Value.Outcome.ShouldBe(AttemptOutcome.TimedOut);
        failure.Value.Detail.ShouldContain("00:00:30");
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "Name resolution failed")]
    [InlineData(HttpRequestError.SecureConnectionError, "TLS handshake failed")]
    [InlineData(HttpRequestError.ConnectionError, "Connection failed")]
    [InlineData(HttpRequestError.InvalidResponse, "InvalidResponse")]
    public void Classify_GivenARequestError_IsConnectionFailedWithADistinctReason(HttpRequestError error, string reason)
    {
        HttpRequestException refused = new(error, "The socket refused");

        (AttemptOutcome Outcome, string Detail)? failure = TransportFailure.Classify(refused, Timeout);

        failure.ShouldNotBeNull();
        failure.Value.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        failure.Value.Detail.ShouldStartWith(reason);
        failure.Value.Detail.ShouldContain("The socket refused");
    }

    [Fact]
    public void Classify_GivenAResponseCutOffMidway_IsConnectionFailed()
    {
        HttpIOException cutOff = new(HttpRequestError.ResponseEnded, "the response ended prematurely");

        (AttemptOutcome Outcome, string Detail)? failure = TransportFailure.Classify(cutOff, Timeout);

        failure.ShouldNotBeNull();
        failure.Value.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        failure.Value.Detail.ShouldContain("ResponseEnded");
    }

    [Fact]
    public void Classify_GivenABlockedDestination_IsBlockedWithTheReason()
    {
        DeliveryBlockedException blocked = new("delivery blocked");

        (AttemptOutcome Outcome, string Detail)? failure = TransportFailure.Classify(blocked, Timeout);

        failure.ShouldNotBeNull();
        failure.Value.Outcome.ShouldBe(AttemptOutcome.Blocked);
        failure.Value.Detail.ShouldBe("delivery blocked");
    }

    [Fact]
    public void Classify_GivenTheCallersOwnCancellation_Declines()
    {
        TransportFailure.Classify(new TaskCanceledException(), Timeout).ShouldBeNull();
        TransportFailure.Classify(new OperationCanceledException(), Timeout).ShouldBeNull();
    }

    [Fact]
    public void Classify_GivenAnythingElse_Declines()
    {
        TransportFailure.Classify(new InvalidOperationException("a bug"), Timeout).ShouldBeNull();
    }
}