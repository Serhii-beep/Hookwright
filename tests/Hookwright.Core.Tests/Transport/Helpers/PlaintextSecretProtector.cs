using Hookwright.Core.Endpoints;
using Hookwright.Signing;

namespace Hookwright.Core.Tests.Transport.Helpers;

internal sealed class PlaintextSecretProtector : ISecretProtector
{
    public string Protect(WebhookSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.Reveal();
    }

    public WebhookSecret Unprotect(string protectedKey)
    {
        return WebhookSecret.TryParse(protectedKey, out WebhookSecret? secret)
            ? secret
            : throw new FormatException("The stored form is not a revealed signing key.");
    }
}