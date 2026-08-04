using System.Buffers.Binary;

namespace Hookwright.Core.Identifiers;

/// <summary>
/// Formatting and parsing shared by every <see cref="IPrefixedId{TSelf}"/> implementation
/// </summary>
internal static class PrefixedId
{
    private const char Separator = '_';

    /// <summary>
    /// Renders <paramref name="value"/> in the canonical text form for <typeparamref name="TId"/>.
    /// </summary>
    internal static string Format<TId>(Guid value) where TId : struct, IPrefixedId<TId>
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteBigEndian(value, bytes);

        return string.Concat(TId.Prefix, Separator.ToString(), Crockford32.Encode(BinaryPrimitives.ReadUInt128BigEndian(bytes)));
    }

    internal static bool TryParse<TId>(ReadOnlySpan<char> source, out TId id) where TId : struct, IPrefixedId<TId>
    {
        id = default;

        ReadOnlySpan<char> prefix = TId.Prefix;

        // Length is fixed, so checking it first rejects almost all malformed
        // input before any character comparison.
        if (source.Length != prefix.Length + 1 + Crockford32.EncodedLength)
        {
            return false;
        }

        if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || source[prefix.Length] != Separator)
        {
            return false;
        }

        if (!Crockford32.TryDecode(source[(prefix.Length + 1)..], out UInt128 value))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(bytes, value);

        id = TId.FromGuid(new Guid(bytes, bigEndian: true));
        return true;
    }

    private static void WriteBigEndian(Guid value, Span<byte> destination)
    {
        value.TryWriteBytes(destination, bigEndian: true, out _);
    }
}