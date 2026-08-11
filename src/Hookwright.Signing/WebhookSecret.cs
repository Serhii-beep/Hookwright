using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Hookwright.Signing;

/// <summary>
/// A signing key in the text format: <c>whsec_</c> followed by the
/// base64 encoded key.
/// </summary>
public sealed class WebhookSecret
{
    /// <summary>
    /// The prefix on the text form.
    /// </summary>
    public const string Prefix = "whsec_";

    /// <summary>
    /// Key length used by <see cref="Generate"/>, matching HMAC-SHA256's output size.
    /// </summary>
    public const int DefaultKeyLengthBytes = 32;

    /// <summary>
    /// Shortest key the specification permits.
    /// </summary>
    public const int MinimumKeyLengthBytes = 24;

    /// <summary>
    /// Longest key the specification permits.
    /// </summary>
    public const int MaximumKeyLengthBytes = 64;

    private readonly byte[] _key;

    private WebhookSecret(byte[] key)
    {
        _key = key;
    }

    /// <summary>
    /// The raw key for a signer to compute a MAC over.
    /// </summary>
    public ReadOnlySpan<byte> Key => _key;

    /// <summary>
    /// Generates a key from a cryptographically secure source.
    /// </summary>
    /// <param name="keyLengthBytes">Key length. Defaults to <see cref="DefaultKeyLengthBytes"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length is outside the permitted range.</exception>
    public static WebhookSecret Generate(int keyLengthBytes = DefaultKeyLengthBytes)
    {
        if (keyLengthBytes is < MinimumKeyLengthBytes or > MaximumKeyLengthBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keyLengthBytes),
                keyLengthBytes,
                $"A signing key must be between {MinimumKeyLengthBytes} and {MaximumKeyLengthBytes} bytes.");
        }

        return new WebhookSecret(RandomNumberGenerator.GetBytes(keyLengthBytes));
    }

    /// <summary>
    /// Parses the text form, with or without the <see cref="Prefix"/>.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="text"/> was a well-formed key.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out WebhookSecret? secret)
    {
        secret = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> encoded = text.AsSpan().Trim();

        if (encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            encoded = encoded[Prefix.Length..];
        }

        Span<byte> buffer = stackalloc byte[MaximumKeyLengthBytes];

        try
        {
            if (!Convert.TryFromBase64Chars(encoded, buffer, out int written) || written < MinimumKeyLengthBytes)
            {
                return false;
            }

            secret = new WebhookSecret(buffer[..written].ToArray());
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>
    /// Returns the full text form, including the key.
    /// </summary>
    public string Reveal()
    {
        return Prefix + Convert.ToBase64String(_key);
    }

    /// <summary>
    /// Returns a redacted placeholder.
    /// </summary>
    public override string ToString()
    {
        return Prefix + "***";
    }
}