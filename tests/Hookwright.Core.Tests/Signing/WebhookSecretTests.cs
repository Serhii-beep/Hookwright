using System.Globalization;

using Hookwright.Core.Signing;

namespace Hookwright.Core.Tests.Signing;

public sealed class WebhookSecretTests
{
    [Fact]
    public void Generate_ByDefault_ProducesA32ByteKey()
    {
        WebhookSecret.Generate().Key.Length.ShouldBe(WebhookSecret.DefaultKeyLengthBytes);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    public void Generate_GivenAPermittedLength_ProducesThatManyBytes(int length)
    {
        WebhookSecret.Generate(length).Key.Length.ShouldBe(length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(65)]
    [InlineData(-1)]
    public void Generate_GivenALengthOutsideTheSpecification_Throws(int length)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => WebhookSecret.Generate(length));
    }

    [Fact]
    public void Generate_Always_ProducesDistinctKeys()
    {
        HashSet<string> revealed = [.. Enumerable.Range(0, 1000).Select(_ => WebhookSecret.Generate().Reveal())];

        revealed.Count.ShouldBe(1000);
    }

    [Fact]
    public void Reveal_Always_RoundTripsThroughTryParse()
    {
        WebhookSecret original = WebhookSecret.Generate();

        WebhookSecret.TryParse(original.Reveal(), out WebhookSecret? parsed).ShouldBeTrue();

        parsed!.Key.ToArray().ShouldBe(original.Key.ToArray());
    }

    [Fact]
    public void Reveal_Always_CarriesThePrefix()
    {
        WebhookSecret.Generate().Reveal().ShouldStartWith(WebhookSecret.Prefix);
    }

    [Fact]
    public void TryParse_GivenAKeyWithoutThePrefix_Succeeds()
    {
        WebhookSecret original = WebhookSecret.Generate();
        string bare = original.Reveal()[WebhookSecret.Prefix.Length..];

        WebhookSecret.TryParse(bare, out WebhookSecret? parsed).ShouldBeTrue();

        parsed!.Key.ToArray().ShouldBe(original.Key.ToArray());
    }

    [Fact]
    public void TryParse_GivenSurroundingWhitespace_Succeeds()
    {
        WebhookSecret original = WebhookSecret.Generate();

        WebhookSecret.TryParse($"   {original.Reveal()}   ", out WebhookSecret? parsed).ShouldBeTrue();

        parsed!.Key.ToArray().ShouldBe(original.Key.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("whsec_")]
    [InlineData("whsec_not!base64")]
    [InlineData("not base 64")]
    public void TryParse_GivenAMalformedValue_Fails(string? text)
    {
        WebhookSecret.TryParse(text, out WebhookSecret? parsed).ShouldBeFalse();

        parsed.ShouldBeNull();
    }

    [Fact]
    public void TryParse_GivenAKeyShorterThanTheSpecificationAllows_Fails()
    {
        string tooShort = WebhookSecret.Prefix + Convert.ToBase64String(new byte[WebhookSecret.MinimumKeyLengthBytes - 1]);

        WebhookSecret.TryParse(tooShort, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryParse_GivenAKeyLongerThanTheSpecificationAllows_Fails()
    {
        string tooLong = WebhookSecret.Prefix + Convert.ToBase64String(new byte[WebhookSecret.MaximumKeyLengthBytes + 1]);

        WebhookSecret.TryParse(tooLong, out _).ShouldBeFalse();
    }

    [Fact]
    public void ToString_Never_DisclosesTheKey()
    {
        WebhookSecret secret = WebhookSecret.Generate();
        string encoded = secret.Reveal()[WebhookSecret.Prefix.Length..];

        string text = secret.ToString();

        text.ShouldBe("whsec_***");
        text.ShouldNotContain(encoded);
        $"{secret}".ShouldNotContain(encoded);
        string.Format(CultureInfo.InvariantCulture, "{0}", secret).ShouldNotContain(encoded);
    }
}