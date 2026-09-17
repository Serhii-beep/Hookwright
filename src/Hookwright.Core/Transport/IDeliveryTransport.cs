using Hookwright.Core.Storage;

namespace Hookwright.Core.Transport;

/// <summary>
/// Turns one claimed delivery into one attempt at its destination.
/// </summary>
public interface IDeliveryTransport
{
    /// <summary>
    /// Sends one delivery and reports what happened.
    /// </summary>
    /// <param name="delivery">The claimed delivery: destination, payload, headers, and signing keys.</param>
    /// <param name="workerId">Identifies this worker on the attempt record, as <c>{machine}:{pid}:{id}</c>.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancels the attempt. A canceled attempt is not recorded.</param>
    /// <returns>The attempt to record, and any delay the destination asked for before the next one.</returns>
    Task<DeliveryResult> SendAsync(
        ClaimedDelivery delivery,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}