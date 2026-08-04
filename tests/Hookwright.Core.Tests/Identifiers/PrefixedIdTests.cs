using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public abstract class PrefixedIdTests<TId>
    where TId : struct, IPrefixedId<TId>, IParsable<TId>
{
    private const string Body = "014D2PF2DBSQQZXQ5TK1V58CGG";

    private static string Prefix => TId.Prefix;

    protected abstract TId CreateId();

    [Fact]
    public void CreateId_Always_ProducesDistinctValues()
    {
        HashSet<TId> ids = [.. Enumerable.Range(0, 1_000).Select(_ => CreateId())];

        ids.Count.ShouldBe(1_000);
    }

    [Fact]
    public void Prefix_Always_IsShortAndLowerCase()
    {
        Prefix.ShouldNotBeNullOrWhiteSpace();
        Prefix.Length.ShouldBeInRange(2, 5);
        Prefix.ShouldAllBe(character => char.IsAsciiLetterLower(character));
    }

    [Fact]
    public void ToString_Always_ProducesPrefixedCanonicalForm()
    {
        string text = CreateId().ToString()!;

        text.ShouldStartWith(Prefix + "_");
        text.Length.ShouldBe(Prefix.Length + 1 + Crockford32.EncodedLength);
    }

    [Fact]
    public void TryParse_GivenCanonicalForm_RoundTrips()
    {
        TId original = CreateId();

        TId.TryParse(original.ToString(), null, out TId parsed).ShouldBeTrue();

        parsed.ShouldBe(original);
        parsed.Value.ShouldBe(original.Value);
    }

    [Fact]
    public void TryParse_GivenUpperCaseInput_RoundTrips()
    {
        TId original = CreateId();

        TId.TryParse(original.ToString()!.ToUpperInvariant(), null, out TId parsed).ShouldBeTrue();

        parsed.ShouldBe(original);
    }

    [Fact]
    public void TryParse_GivenMalformedInput_Fails()
    {
        string wrongPrefix = new('z', Prefix.Length);

        string?[] malformed =
        [
            null,
            "",
            Prefix + "_",
            Body,                               // no prefix
            wrongPrefix + "_" + Body,           // wrong prefix, correct length
            Prefix + "-" + Body,                // wrong separator
            Prefix + "_" + Body[..^1],          // one character short
            Prefix + "_" + Body + "0",          // one character long
            Prefix + "_8" + Body[1..],          // overflows 128 bits
            Prefix + "_" + Body[..^1] + "I",    // I is not in the alphabet
        ];

        foreach (string? source in malformed)
        {
            TId.TryParse(source, null, out TId result)
                .ShouldBeFalse($"'{source}' must not parse");

            result.ShouldBe(default);
        }
    }

    [Fact]
    public void Equality_GivenTheSameValue_IsStructural()
    {
        Guid value = Guid.CreateVersion7();

        TId.FromGuid(value).ShouldBe(TId.FromGuid(value));
        TId.FromGuid(value).GetHashCode().ShouldBe(TId.FromGuid(value).GetHashCode());
        TId.FromGuid(value).ShouldNotBe(CreateId());
    }

    [Fact]
    public void ToString_GivenIncreasingTimestamps_SortsChronologically()
    {
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        List<string> texts =
        [
            .. Enumerable.Range(0, 200)
                .Select(offset => TId
                    .FromGuid(Guid.CreateVersion7(start.AddMilliseconds(offset)))
                    .ToString()!)
        ];

        texts.ShouldBe([.. texts.Order(StringComparer.Ordinal)]);
    }
}