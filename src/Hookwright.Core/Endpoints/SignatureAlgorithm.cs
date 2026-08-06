namespace Hookwright.Core.Endpoints;

/// <summary>
/// The scheme used to sign outgoing webhooks.
/// </summary>
/// <remarks>
/// Persisted. Renumbering or reordering a member silently reinterprets every existing
/// row, so add new members at the end and never reuse a retired number.
/// </remarks>
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