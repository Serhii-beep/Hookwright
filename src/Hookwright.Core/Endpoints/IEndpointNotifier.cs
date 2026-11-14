namespace Hookwright.Core.Endpoints;

/// <summary>
/// Tells the host that the delivery engine has disabled an endpoint, so that the host can tell
/// the endpoint's owner.
/// </summary>
/// <remarks>
/// Called once per endpoint, after the store has recorded the disabling, from the engine's
/// result writer. A slow notifier delays the results queued behind it, so one that does real
/// work should hand the work off. An exception is logged and changes nothing: the endpoint
/// stays disabled.
/// </remarks>
public interface IEndpointNotifier
{
    /// <summary>
    /// Says that <paramref name="endpoint"/> has been disabled, for the reason its
    /// <see cref="WebhookEndpoint.DisabledReason"/> gives.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">Cancels the notification.</param>
    /// <returns></returns>
    Task EndpointDisabledAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken);
}