using System.Text;

using Hookwright.Core.Endpoints;
using Hookwright.Core.Signing;

namespace Hookwright.Core.Tests.Signing;

public sealed class HmacSha256SignerTests
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string MessageId = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const long Timestamp = 1614265330;
    private readonly byte[] _payload = Encoding.UTF8.GetBytes("""{"test": 2432232314}""");
    private const string Signature = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";

    private static readonly HmacSha256Signer Signer = new();

    private static WebhookSecret Parse(string text)
    {
        WebhookSecret.TryParse(text, out WebhookSecret? secret).ShouldBeTrue();
        return secret!;
    }

    [Fact]
    public void Sign_GivenTheSpecification_ProducesThePublishedSignature()
    {
        string signature = Signer.Sign(
            MessageId,
            Timestamp,
            _payload,
            Parse(Secret));

        signature.ShouldBe(Signature);
    }

    [Fact]
    public void Sign_Always_ReportsItsAlgorithmAndVersion()
    {
        Signer.Algorithm.ShouldBe(SignatureAlgorithm.HmacSha256);
        Signer.Version.ShouldBe("v1");
    }

    [Fact]
    public void Sign_GivenTheSameInputs_IsDeterministic()
    {
        WebhookSecret secret = Parse(Secret);

        Signer.Sign(MessageId, Timestamp, _payload, secret).ShouldBe(Signer.Sign(MessageId, Timestamp, _payload, secret));
    }

    [Fact]
    public void Sign_GivenADifferentPayload_ProducesADifferentSignature()
    {
        WebhookSecret secret = Parse(Secret);

        string original = Signer.Sign(MessageId, Timestamp, _payload, secret);
        string altered = Signer.Sign(MessageId, Timestamp, Encoding.UTF8.GetBytes("""{"test": "123"}"""), secret);

        altered.ShouldNotBe(original);
    }

    [Fact]
    public void Sign_GivenWhitespaceThatDoesNotChangeJson_StillProducesADifferentSignature()
    {
        WebhookSecret secret = Parse(Secret);

        string compact = Signer.Sign(MessageId, Timestamp, Encoding.UTF8.GetBytes("""{"test":1}"""), secret);

        string spaced = Signer.Sign(MessageId, Timestamp, Encoding.UTF8.GetBytes("""{"test": 1}"""), secret);

        spaced.ShouldNotBe(compact);
    }

    [Fact]
    public void Sign_GivenADifferentTimestamp_ProducesADifferentSignature()
    {
        WebhookSecret secret = Parse(Secret);

        Signer.Sign(MessageId, Timestamp + 1, _payload, secret).ShouldNotBe(Signer.Sign(MessageId, Timestamp, _payload, secret));
    }

    [Fact]
    public void Sign_GivenADifferentKey_ProducesADifferentSignature()
    {
        Signer.Sign(MessageId, Timestamp, _payload, WebhookSecret.Generate()).ShouldNotBe(Signature);
    }

    [Fact]
    public void Sign_GivenAnEmptyPayload_StillSigns()
    {
        Signer.Sign(MessageId, Timestamp, [], Parse(Secret)).ShouldStartWith("v1,");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sign_GivenNoMessageId_Throws(string? messageId)
    {
        Should.Throw<ArgumentException>(() => Signer.Sign(messageId!, Timestamp, [], Parse(Secret)));
    }
}