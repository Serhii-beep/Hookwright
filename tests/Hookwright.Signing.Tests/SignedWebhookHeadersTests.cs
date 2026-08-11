using System.Globalization;
using System.Text;

namespace Hookwright.Signing.Tests;

public sealed class SignedWebhookHeadersTests
{
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeSeconds(1614265330);
    private static readonly HmacSha256Signer Signer = new();

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("""{"test": 2432232314}""");

    private static WebhookSecret Secret()
    {
        WebhookSecret.TryParse("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw", out WebhookSecret? secret).ShouldBeTrue();
        return secret!;
    }

    [Fact]
    public void Create_GivenOneKey_ProducesNecessaryHeaders()
    {
        SignedWebhookHeaders headers = SignedWebhookHeaders.Create(
            Signer, "msg_p5jXN8AQM9LWM0D4loKWxJek", Timestamp, Payload, [Secret()]);

        headers.Id.ShouldBe("msg_p5jXN8AQM9LWM0D4loKWxJek");
        headers.Timestamp.ShouldBe("1614265330");
        headers.Signature.ShouldBe("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");
    }

    [Fact]
    public void Create_GivenSeveralKeys_EmitsOneSignaturePerKeySpaceDelimited()
    {
        SignedWebhookHeaders headers = SignedWebhookHeaders.Create(
            Signer, "msg_p5jXN8AQM9LWM0D4loKWxJek", Timestamp, Payload, [Secret(), WebhookSecret.Generate()]);

        string[] signatures = headers.Signature.Split(' ');

        signatures.Length.ShouldBe(2);
        signatures.ShouldAllBe(signature => signature.StartsWith("v1,", StringComparison.Ordinal));
        signatures[0].ShouldNotBe(signatures[1]);
    }

    [Fact]
    public void Create_Always_UsesOneTimestampForTheHeaderAndTheSignature()
    {
        SignedWebhookHeaders headers = SignedWebhookHeaders.Create(
            Signer, "msg_p5jXN8AQM9LWM0D4loKWxJek", Timestamp, Payload, [Secret()]);

        string expected = Signer.Sign(
            headers.Id,
            long.Parse(headers.Timestamp, CultureInfo.InvariantCulture),
            Payload,
            Secret());

        headers.Signature.ShouldBe(expected);
    }

    [Fact]
    public void Create_GivenNoKeys_Throws()
    {
        Should.Throw<ArgumentException>(
            () => SignedWebhookHeaders.Create(Signer, "msg_p5jXN8AQM9LWM0D4loKWxJek", Timestamp, Payload, []));
    }

    [Fact]
    public void HeaderNames_Always_MatchTheSpecification()
    {
        SignedWebhookHeaders.IdHeaderName.ShouldBe("webhook-id");
        SignedWebhookHeaders.TimestampHeaderName.ShouldBe("webhook-timestamp");
        SignedWebhookHeaders.SignatureHeaderName.ShouldBe("webhook-signature");
    }
}