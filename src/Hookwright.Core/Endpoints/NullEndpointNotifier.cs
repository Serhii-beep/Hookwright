namespace Hookwright.Core.Endpoints;

/// <summary>
/// The notifier of a host that has not registered one. Nobody is told.
/// </summary>
public sealed class NullEndpointNotifier : IEndpointNotifier
{
    private NullEndpointNotifier()
    {

    }

    /// <summary>
    /// The one instance.
    /// </summary>
    public static NullEndpointNotifier Instance { get; } = new();

    /// <inheritdoc />
    public Task EndpointDisabledAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}