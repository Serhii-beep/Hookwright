namespace Hookwright.Core.Entities;

/// <summary>
/// The scheme used to sign outgoing webhooks.
/// </summary>
/// <remarks>
/// Persisted. See the renumbering warning on <see cref="DeliveryState"/>.</remarks>
public enum SignatureAlgorithm : short
{
    /// <summary>
    /// HMAC-SHA256, transmitted with the <c>v1</c> identifier.
    /// </summary>
    HmacSha256 = 0,

    /// <summary>
    /// Ed25519, transmitted with the <c>v1a</c> identifier.
    /// Asymmetric, so a consumer verifies with a public key
    /// and a leaked consumer-side key cannot forge events.
    /// </summary>
    Ed25519 = 1
}