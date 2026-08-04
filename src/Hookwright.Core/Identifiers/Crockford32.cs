namespace Hookwright.Core.Identifiers;

/// <summary>
/// Crockford Base32 encoding for 128-bit values, producing a fixed 26-character
/// representation that preserves the ordering of the underlying value.
/// </summary>
internal static class Crockford32
{
    /// <summary>
    /// Characters produced for 128-bit value: ceil(128 / 5) = 26.
    /// </summary>
    internal const int EncodedLength = 26;

    /// <summary>
    /// Crockford's alphabet omits I, L, O, and U. The first three are visually
    /// confusable with 1 and 0 when an identifier is read aloud or retyped from
    /// support tickets. U is excluded so encoded values cannot spell obscenities.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Marks a character that has no place in the alphabet
    /// </summary>
    private const byte InvalidDigit = 0xFF;

    private static readonly byte[] DecodeMap = CreateDecodeMap();

    /// <summary>
    /// Encodes <paramref name="value"/> as exactly <see cref="EncodedLength"/> characters.
    /// The result is ordinally comparable: if <c>a &lt; b</c> then
    /// <c>string.CompareOrdinal(Encode(a), Encode(b)) &lt; 0</c>.
    /// </summary>
    internal static string Encode(UInt128 value)
    {
        return string.Create(EncodedLength, value, static (destination, state) =>
        {
            for (int i = EncodedLength - 1; i >= 0; i--)
            {
                destination[i] = Alphabet[(int)(state & 0x1F)];
                state >>= 5;
            }
        });
    }

    /// <summary>
    /// Decodes a 26-character Crockford Base32 value. Accepts either case. The
    /// canonical form emitted by <see cref="Encode"/> is upper case.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="source"/> was well formed.</returns>
    internal static bool TryDecode(ReadOnlySpan<char> source, out UInt128 value)
    {
        value = default;

        if (source.Length != EncodedLength)
        {
            return false;
        }

        UInt128 result = UInt128.Zero;

        for (int i = 0; i < source.Length; i++)
        {
            char character = source[i];

            if (character >= DecodeMap.Length)
            {
                return false;
            }

            byte digit = DecodeMap[character];

            if (digit == InvalidDigit)
            {
                return false;
            }

            // 26 characters carry 130 bits but a UInt128 holds 128, so the
            // leading character may only use its low three bits. Rejecting
            // anything larger keeps decoding injective - without this check
            // two distinct strings would silently decode to the same value.
            if (i == 0 && digit > 0x07)
            {
                return false;
            }

            result = (result << 5) | digit;
        }

        value = result;
        return true;
    }

    private static byte[] CreateDecodeMap()
    {
        // Indexed by ASCII code: a lookup array beats a dictionary for a
        // 32-entry alphabet and keeps decoding allocation-free.
        byte[] map = new byte[128];
        map.AsSpan().Fill(InvalidDigit);

        for (byte digit = 0; digit < Alphabet.Length; digit++)
        {
            map[Alphabet[digit]] = digit;
            map[char.ToLowerInvariant(Alphabet[digit])] = digit;
        }

        return map;
    }
}