using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Signing;

/// <summary>
/// Produces a webhook signature over a message
/// </summary>
public interface IWebhookSigner
{
    /// <summary>
    /// The scheme this signer implements
    /// </summary>
    SignatureAlgorithm Algorithm { get; }

    /// <summary>
    /// The version identifier that prefixes each signature, such as <c>v1</c>
    /// for HMAC-SHA256 or <v>v1a</v> for Ed25519
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Signs one message, returning a <c>{version},{base64}</c> signature.
    /// </summary>
    /// <param name="messageId">The value sent as <c>webhook-id</c>.</param>
    /// <param name="timestampSeconds">The value sent as <c>webhook-timestamp</c>.</param>
    /// <param name="payload">
    /// The exact bytes that will be transmitted.
    /// </param>
    /// <param name="secret">The signing key.</param>
    /// <returns></returns>
    string Sign(string messageId, long timestampSeconds, ReadOnlySpan<byte> payload, WebhookSecret secret);
}