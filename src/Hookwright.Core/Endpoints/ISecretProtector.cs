using Hookwright.Signing;

namespace Hookwright.Core.Endpoints;

/// <summary>
/// Protects signing keys at rest. What <see cref="EndpointSecret.ProtectedKey"/> holds is
/// whatever <see cref="Protect"/> returned, and only <see cref="Unprotect"/> turns it back into a key.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Turns a key into the opaque form that is stored.
    /// </summary>
    string Protect(WebhookSecret secret);

    /// <summary>
    /// Turns a stored form back into the key it protects.
    /// </summary>
    WebhookSecret Unprotect(string protectedKey);
}