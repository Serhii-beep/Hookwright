using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Storage;

/// <summary>
/// A new endpoint together with the signing keys it starts life with.
/// </summary>
public sealed class EndpointRegistration
{
    private EndpointRegistration(WebhookEndpoint endpoint, IReadOnlyList<EndpointSecret> secrets)
    {
        Endpoint = endpoint;
        Secrets = secrets;
    }

    /// <summary>
    /// The endpoint being registered.
    /// </summary>
    public WebhookEndpoint Endpoint { get; }

    /// <summary>
    /// The keys endpoint starts with. Always at least one.
    /// </summary>
    public IReadOnlyList<EndpointSecret> Secrets { get; }

    /// <summary>
    /// Bundles an endpoint with its initial signing keys.
    /// </summary>
    /// <param name="endpoint">The endpoint being registered.</param>
    /// <param name="secrets">
    /// The keys endpoint starts with.
    /// </param>
    /// <exception cref="ArgumentException">
    /// No key was supplied, or a key belongs to a different endpoint.
    /// </exception>
    public static EndpointRegistration Create(WebhookEndpoint endpoint, IEnumerable<EndpointSecret> secrets)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(secrets);

        EndpointSecret[] keys = [.. secrets];

        if (keys.Length == 0)
        {
            throw new ArgumentException(
                "An endpoint must be registered with at least one signing key.",
                nameof(secrets));
        }

        foreach (EndpointSecret secret in keys)
        {
            if (secret.EndpointId != endpoint.Id)
            {
                throw new ArgumentException(
                    $"Secret {secret.Id} belongs to endpoint {secret.EndpointId}, not {endpoint.Id}.",
                    nameof(secrets));
            }
        }

        return new EndpointRegistration(endpoint, keys);
    }
}