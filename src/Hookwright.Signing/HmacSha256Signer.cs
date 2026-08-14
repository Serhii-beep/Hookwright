using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hookwright.Signing;

/// <summary>
/// The HMAC-SHA256 over <c>{id}.{timestamp}.{payload}</c>,
/// transmitted with the <c>v1</c> identifier.
/// </summary>
public sealed class HmacSha256Signer : IWebhookSigner
{
    /// <summary>
    /// Length of an HMAC-SHA256 result.
    /// </summary>
    private const int MacLengthBytes = 32;

    /// <inheritdoc />
    public SignatureAlgorithm Algorithm => SignatureAlgorithm.HmacSha256;

    /// <inheritdoc />
    public string Version => "v1";

    /// <inheritdoc />
    public string Sign(string messageId, long timestampSeconds, ReadOnlySpan<byte> payload, WebhookSecret secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] prefix = Encoding.UTF8.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{messageId}.{timestampSeconds}."));

        using IncrementalHash hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, secret.Key);

        hash.AppendData(prefix);
        hash.AppendData(payload);

        Span<byte> mac = stackalloc byte[MacLengthBytes];
        hash.GetHashAndReset(mac);

        return string.Concat(Version, ",", Convert.ToBase64String(mac));
    }

    /// <inheritdoc />
    public bool Verify(string signature, string messageId, long timestampSeconds, ReadOnlySpan<byte> payload, WebhookSecret secret)
    {
        ArgumentNullException.ThrowIfNull(signature);

        return ConstantTimeEquals(signature, Sign(messageId, timestampSeconds, payload, secret));
    }

    private static bool ConstantTimeEquals(string candidate, string reference)
    {
        return candidate.Length == reference.Length
            && CryptographicOperations.FixedTimeEquals(
                MemoryMarshal.AsBytes(candidate.AsSpan()),
                MemoryMarshal.AsBytes(reference.AsSpan()));
    }
}