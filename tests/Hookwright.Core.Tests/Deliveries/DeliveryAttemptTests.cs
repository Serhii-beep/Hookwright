using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class DeliveryAttemptTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);
    private const string Worker = "web-01:4242:a1b2c3";

    [Fact]
    public void FromResponse_GivenSuccess_RecordsTheResponse()
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            DeliveryId.New(), AttemptOutcome.Succeeded, 200, "ok",
            new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
            Now, Duration, Worker);

        attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        attempt.ResponseStatusCode.ShouldBe(200);
        attempt.ResponseBodySnippet.ShouldBe("ok");
        attempt.ResponseHeaders["content-type"].ShouldBe("text/plain");
        attempt.ErrorDetail.ShouldBeNull();
        attempt.WorkerId.ShouldBe(Worker);
    }

    [Fact]
    public void FromFailure_GivenATimeout_RecordsNoStatusCode()
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromFailure(
            DeliveryId.New(), AttemptOutcome.TimedOut, "No response within 30s",
            Now, Duration, Worker);

        attempt.ResponseStatusCode.ShouldBeNull();
        attempt.ResponseBodySnippet.ShouldBeNull();
        attempt.ErrorDetail.ShouldBe("No response within 30s");
        attempt.ResponseHeaders.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AttemptOutcome.TimedOut)]
    [InlineData(AttemptOutcome.ConnectionFailed)]
    [InlineData(AttemptOutcome.Blocked)]
    public void FromResponse_GivenAnOutcomeWithNoResponse_Throws(AttemptOutcome outcome)
    {
        Should.Throw<ArgumentException>(() => DeliveryAttempt.FromResponse(
            DeliveryId.New(), outcome, 200, null, null, Now, Duration, Worker));
    }

    [Fact]
    public void FromResponse_GivenATooLongHeaderValue_TruncatesIt()
    {
        string longValue = new('x', DeliveryAttempt.MaxResponseHeaderValueLength + 1);

        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            DeliveryId.New(), AttemptOutcome.Succeeded, 200, "ok",
            new Dictionary<string, string> { ["x-header"] = longValue },
            Now, Duration, Worker);

        attempt.ResponseHeaders["x-header"].Length.ShouldBe(DeliveryAttempt.MaxResponseHeaderValueLength);
    }

    [Fact]
    public void FromResponse_GivenAnEmptyHeaderValue_KeepsTheHeader()
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            DeliveryId.New(), AttemptOutcome.Succeeded, 200, "ok",
            new Dictionary<string, string> { ["x-header"] = string.Empty },
            Now, Duration, Worker);

        attempt.ResponseHeaders["x-header"].ShouldBe(string.Empty);
    }

    [Fact]
    public void FromResponse_GivenATooLongHeaderName_DropsThatHeader()
    {
        string longName = new('x', DeliveryAttempt.MaxResponseHeaderNameLength + 1);

        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            DeliveryId.New(), AttemptOutcome.Succeeded, 200, "ok",
            new Dictionary<string, string> { [longName] = "value", ["x-header"] = "another_value" },
            Now, Duration, Worker);

        attempt.ResponseHeaders.ShouldHaveSingleItem().Key.ShouldBe("x-header");
    }

    [Theory]
    [InlineData(AttemptOutcome.Succeeded)]
    [InlineData(AttemptOutcome.Rejected)]
    [InlineData(AttemptOutcome.ServerError)]
    [InlineData(AttemptOutcome.Throttled)]
    [InlineData(AttemptOutcome.Gone)]
    public void FromFailure_GivenAnOutcomeThatImpliesAResponse_Throws(AttemptOutcome outcome)
    {
        Should.Throw<ArgumentException>(() => DeliveryAttempt.FromFailure(
            DeliveryId.New(), outcome, "detail", Now, Duration, Worker));
    }

    [Fact]
    public void FromResponse_GivenOverlongBody_TruncatesRatherThanThrowing()
    {
        string body = new('x', DeliveryAttempt.MaxResponseBodySnippetLength * 3);

        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            DeliveryId.New(), AttemptOutcome.ServerError, 500, body, null, Now, Duration, Worker);

        attempt.ResponseBodySnippet!.Length.ShouldBe(DeliveryAttempt.MaxResponseBodySnippetLength);
    }

    [Fact]
    public void FromResponse_GivenMoreHeadersThanTheLimit_KeepsTheLimitWithoutThrowing()
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < DeliveryAttempt.MaxResponseHeaders + 1; i++)
        {
            headers[$"x-header-{i}"] = "value";
        }

        DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
            DeliveryId.New(), AttemptOutcome.Succeeded, 200, "ok", headers, Now, Duration, Worker);

        attempt.ResponseHeaders.Count.ShouldBe(DeliveryAttempt.MaxResponseHeaders);
    }

    [Fact]
    public void FromFailure_GivenANegativeDuration_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DeliveryAttempt.FromFailure(
            DeliveryId.New(), AttemptOutcome.TimedOut, null, Now, TimeSpan.FromTicks(-1), Worker));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromFailure_GivenNoWorkerId_Throws(string? workerId)
    {
        Should.Throw<ArgumentException>(() => DeliveryAttempt.FromFailure(
            DeliveryId.New(), AttemptOutcome.TimedOut, null, Now, Duration, workerId!));
    }
}