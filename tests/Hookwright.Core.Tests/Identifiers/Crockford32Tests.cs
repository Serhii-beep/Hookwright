using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class Crockford32Tests
{
    [Fact]
    public void Encode_Always_ProducesFixedLength()
    {
        Crockford32.Encode(UInt128.Zero).Length.ShouldBe(Crockford32.EncodedLength);
        Crockford32.Encode(UInt128.MaxValue).Length.ShouldBe(Crockford32.EncodedLength);
    }

    [Fact]
    public void Encode_Always_UsesOnlyTheCrockfordAlphabet()
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        string encoded = Crockford32.Encode(UInt128.MaxValue);

        encoded.ShouldAllBe(character => alphabet.Contains(character));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(31UL)]
    [InlineData(32UL)]
    [InlineData(ulong.MaxValue)]
    public void TryDecode_GivenEncodedValue_RoundTrips(ulong seed)
    {
        UInt128 original = new(seed, seed);

        bool decoded = Crockford32.TryDecode(Crockford32.Encode(original), out UInt128 result);

        decoded.ShouldBeTrue();
        result.ShouldBe(original);
    }

    [Fact]
    public void TryDecode_GivenMaximumValue_RoundTrips()
    {
        bool decoded = Crockford32.TryDecode(Crockford32.Encode(UInt128.MaxValue), out UInt128 result);

        decoded.ShouldBeTrue();
        result.ShouldBe(UInt128.MaxValue);
    }

    [Theory]
    [InlineData(0UL, 0UL, "00000000000000000000000000")]
    [InlineData(0UL, 1UL, "00000000000000000000000001")]
    [InlineData(0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL, "014D2PF2DBSQQZXQ5TK1V58CGG")]
    [InlineData(ulong.MaxValue, ulong.MaxValue, "7ZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void Encode_GivenKnownValue_ProducesExpectedText(ulong upper, ulong lower, string expected)
    {
        Crockford32.Encode(new UInt128(upper, lower)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("014D2PF2DBSQQZXQ5TK1V58CGG")]
    [InlineData("014d2pf2dbsqqzxq5tk1v58cgg")]
    public void TryDecode_GivenEitherCase_ProducesTheSameValue(string source)
    {
        bool decoded = Crockford32.TryDecode(source, out UInt128 result);

        decoded.ShouldBeTrue();
        result.ShouldBe(new UInt128(0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL));
    }

    [Theory]
    [InlineData("IIIIIIIIIIIIIIIIIIIIIIIIII")]
    [InlineData("LLLLLLLLLLLLLLLLLLLLLLLLLL")]
    [InlineData("OOOOOOOOOOOOOOOOOOOOOOOOOO")]
    [InlineData("UUUUUUUUUUUUUUUUUUUUUUUUUU")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void TryDecode_GivenCharacterOutsideAlphabet_Fails(string source)
    {
        bool decoded = Crockford32.TryDecode(source, out _);

        decoded.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0000000000000000000000000")] // 25
    [InlineData("000000000000000000000000000")] // 27
    public void TryDecode_GivenWrongLength_Fails(string source)
    {
        bool decoded = Crockford32.TryDecode(source, out _);

        decoded.ShouldBeFalse();
    }

    [Fact]
    public void TryDecode_GivenLeadingCharacterThatOverflows_Fails()
    {
        Crockford32.TryDecode("8ZZZZZZZZZZZZZZZZZZZZZZZZZ", out _).ShouldBeFalse();
        Crockford32.TryDecode("7ZZZZZZZZZZZZZZZZZZZZZZZZZ", out _).ShouldBeTrue();
    }

    [Fact]
    public void Encode_GivenIncreasingValues_PreservesOrdinalOrdering()
    {
        string previous = Crockford32.Encode(UInt128.Zero);

        for (int shift = 0; shift < 128; shift++)
        {
            string current = Crockford32.Encode(UInt128.One << shift);

            string.CompareOrdinal(previous, current).ShouldBeLessThan(0);
            previous = current;
        }
    }
}