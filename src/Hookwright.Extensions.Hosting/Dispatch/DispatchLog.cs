using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;

using Microsoft.Extensions.Logging;

namespace Hookwright.Dispatch;

internal static partial class DispatchLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "{WorkerId} claimed {Count} deliveries")]
    public static partial void Claimed(ILogger logger, string workerId, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "{WorkerId} could not claim deliveries. The next attempt is in {Pause}")]
    public static partial void ClaimFailed(ILogger logger, Exception exception, string workerId, TimeSpan pause);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "{WorkerId} handed back {Count} deliveries it had not started")]
    public static partial void Released(ILogger logger, string workerId, int count);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Reclaimed {Count} deliveries whose lease lapsed under {LeaseOwner}")]
    public static partial void Reclaimed(ILogger logger, int count, string leaseOwner);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Could not reclaim expired leases. The next attempt is in {Period}")]
    public static partial void ReapFailed(ILogger logger, Exception exception, TimeSpan period);

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "{WorkerId} attempted {DeliveryId}: {Outcome} in {Duration}")]
    public static partial void Attempted(ILogger logger, string workerId, DeliveryId deliveryId, AttemptOutcome outcome, TimeSpan duration);

    [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "{WorkerId} left {DeliveryId} to the reaper. Its lease had {Remaining} left, less than the request timeout")]
    public static partial void LeftStale(ILogger logger, string workerId, DeliveryId deliveryId, TimeSpan remaining);

    [LoggerMessage(EventId = 22, Level = LogLevel.Error, Message = "{WorkerId} could not attempt {DeliveryId}, which is failed. {Reason}")]
    public static partial void Unsendable(ILogger logger, Exception exception, string workerId, DeliveryId deliveryId, string reason);

    [LoggerMessage(EventId = 23, Level = LogLevel.Information, Message = "{WorkerId} retired endpoint {EndpointId}. It answered 410 Gone")]
    public static partial void Retired(ILogger logger, string workerId, WebhookEndpointId endpointId);

    [LoggerMessage(EventId = 24, Level = LogLevel.Warning, Message = "{WorkerId} stopped before the result of {DeliveryId} could be queued. Its lease will lapse")]
    public static partial void ResultAbandoned(ILogger logger, string workerId, DeliveryId deliveryId);

    [LoggerMessage(EventId = 30, Level = LogLevel.Warning, Message = "{WorkerId} recorded {Applied} of {Count} results. The rest were for leases it no longer held")]
    public static partial void LateResults(ILogger logger, string workerId, int applied, int count);

    [LoggerMessage(EventId = 31, Level = LogLevel.Error, Message = "{WorkerId} could not record {Count} results. Their leases will lapse and the deliveries be retried")]
    public static partial void ResultsLost(ILogger logger, Exception exception, string workerId, int count);
}