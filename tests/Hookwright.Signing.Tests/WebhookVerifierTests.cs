using System.Text;

namespace Hookwright.Signing.Tests;

public sealed class WebhookVerifierTests
{
    private const string MessageId = "msg_p5jXN8AQM9LWM0D4loKWxJek";

    private static readonly DateTimeOffset SignedAt = DateTimeOffset.FromUnixTimeSeconds(1614265330);
    private static readonly HmacSha256Signer Signer = new();
    private static readonly WebhookVerifier Verifier = new(Signer);

    private static byte[] Payload => Encoding.UTF8.GetBytes("""{"test": 2432232314}""");

    private static WebhookSecret Secret()
    {
        WebhookSecret.TryParse("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw", out WebhookSecret? secret).ShouldBeTrue();
        return secret!;
    }

    private static SignedWebhookHeaders Sign(params WebhookSecret[] secrets) =>
        SignedWebhookHeaders.Create(Signer, MessageId, SignedAt, Payload, secrets);

    [Fact]
    public void Verify_GivenAGenuineWebhook_Succeeds()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        WebhookVerificationResult result = Verifier.Verify(
            headers.Id, headers.Timestamp, headers.Signature, Payload, [Secret()], SignedAt);

        result.IsValid.ShouldBeTrue();
        result.Failure.ShouldBe(WebhookVerificationFailure.None);
    }

    [Fact]
    public void Verify_GivenATamperedPayload_Fails()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature,
            Encoding.UTF8.GetBytes("""{"test": 1}"""), [Secret()], SignedAt).Failure
            .ShouldBe(WebhookVerificationFailure.SignatureMismatch);
    }

    [Fact]
    public void Verify_GivenAReSerialisedPayload_Fails()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature,
            Encoding.UTF8.GetBytes("""{"test":2432232314}"""), [Secret()], SignedAt).Failure
            .ShouldBe(WebhookVerificationFailure.SignatureMismatch);
    }

    [Fact]
    public void Verify_GivenAnotherKey_Fails()
    {
        SignedWebhookHeaders headers = Sign(WebhookSecret.Generate());

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [Secret()], SignedAt)
            .Failure.ShouldBe(WebhookVerificationFailure.SignatureMismatch);
    }

    [Fact]
    public void Verify_DuringARotationWindow_AcceptsEitherKey()
    {
        WebhookSecret retiring = Secret();
        WebhookSecret replacement = WebhookSecret.Generate();
        SignedWebhookHeaders headers = Sign(retiring, replacement);

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [retiring], SignedAt)
            .IsValid.ShouldBeTrue();

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [replacement], SignedAt)
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Verify_GivenAnUnknownSignatureVersionAlongsideAValidOne_Succeeds()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Verifier.Verify(headers.Id, headers.Timestamp, "v2,c29tZXRoaW5nZWxzZQ== " + headers.Signature, Payload, [Secret()], SignedAt)
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Verify_GivenAMessageOlderThanTheTolerance_Fails()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [Secret()],
            SignedAt + WebhookVerifier.DefaultTolerance + TimeSpan.FromSeconds(1))
            .Failure.ShouldBe(WebhookVerificationFailure.TimestampTooOld);
    }

    [Fact]
    public void Verify_GivenAMessageDatedAhead_ReportsClockSkewDistinctly()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [Secret()],
            SignedAt - WebhookVerifier.DefaultTolerance - TimeSpan.FromSeconds(1))
            .Failure.ShouldBe(WebhookVerificationFailure.TimestampInFuture);
    }

    [Fact]
    public void Verify_AtTheToleranceBoundary_Succeeds()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [Secret()],
            SignedAt + WebhookVerifier.DefaultTolerance)
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "1614265330", "v1,x", WebhookVerificationFailure.MissingId)]
    [InlineData("", "1614265330", "v1,x", WebhookVerificationFailure.MissingId)]
    [InlineData("   ", "1614265330", "v1,x", WebhookVerificationFailure.MissingId)]
    [InlineData(MessageId, null, "v1,x", WebhookVerificationFailure.MissingTimestamp)]
    [InlineData(MessageId, "", "v1,x", WebhookVerificationFailure.MissingTimestamp)]
    [InlineData(MessageId, "   ", "v1,x", WebhookVerificationFailure.MissingTimestamp)]
    [InlineData(MessageId, "1614265330", null, WebhookVerificationFailure.MissingSignature)]
    [InlineData(MessageId, "1614265330", "", WebhookVerificationFailure.MissingSignature)]
    [InlineData(MessageId, "1614265330", "   ", WebhookVerificationFailure.MissingSignature)]
    [InlineData(MessageId, "text", "v1,x", WebhookVerificationFailure.MalformedTimestamp)]
    [InlineData(MessageId, "99999999999999999999", "v1,x", WebhookVerificationFailure.MalformedTimestamp)]
    public void Verify_GivenMalformedHeaders_ReportsTheSpecificProblem(
        string? id, string? timestamp, string? signature, WebhookVerificationFailure expected)
    {
        Verifier.Verify(id, timestamp, signature, Payload, [Secret()], SignedAt)
            .Failure.ShouldBe(expected);
    }

    [Fact]
    public void Verify_GivenHeadersInAnyCase_FindsThem()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Dictionary<string, string> mixedCase = new(StringComparer.Ordinal)
        {
            ["Webhook-Id"] = headers.Id,
            ["WEBHOOK-TIMESTAMP"] = headers.Timestamp,
            ["webhOOk-Signature"] = headers.Signature
        };

        Verifier.Verify(mixedCase, Payload, [Secret()], SignedAt).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Verify_GivenNoKeys_ThrowsRatherThanReportingAMismatch()
    {
        SignedWebhookHeaders headers = Sign(Secret());

        Should.Throw<ArgumentException>(
            () => Verifier.Verify(headers.Id, headers.Timestamp, headers.Signature, Payload, [], SignedAt));
    }

    [Fact]
    public void Verify_GivenMoreSignaturesThanTheLimit_FailsWithoutHashing()
    {
        SignedWebhookHeaders headers = Sign(Secret());
        string overflowed = string.Join(' ', Enumerable.Repeat(headers.Signature, WebhookVerifier.MaxSignatures + 1));

        Verifier.Verify(headers.Id, headers.Timestamp, overflowed, Payload, [Secret()], SignedAt)
            .Failure.ShouldBe(WebhookVerificationFailure.TooManySignatures);
    }

    [Fact]
    public void Constructor_GivenANegativeTolerance_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new WebhookVerifier(Signer, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Failed_GivenNone_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => WebhookVerificationResult.Failed(WebhookVerificationFailure.None));
    }
}