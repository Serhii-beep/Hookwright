using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Subscribers;

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

    [LoggerMessage(EventId = 23, Level = LogLevel.Information, Message = "{WorkerId} retired endpoint {EndpointId}. {Reason}")]
    public static partial void Retired(ILogger logger, string workerId, WebhookEndpointId endpointId, string reason);

    [LoggerMessage(EventId = 24, Level = LogLevel.Warning, Message = "{WorkerId} stopped before the result of {DeliveryId} could be queued. Its lease will lapse")]
    public static partial void ResultAbandoned(ILogger logger, string workerId, DeliveryId deliveryId);

    [LoggerMessage(EventId = 25, Level = LogLevel.Debug, Message = "{WorkerId} deferred {DeliveryId} until {DueAt}. Endpoint {EndpointId} is {Health}")]
    public static partial void Deferred(ILogger logger, string workerId, DeliveryId deliveryId, DateTimeOffset dueAt, WebhookEndpointId endpointId, EndpointHealth health);

    [LoggerMessage(EventId = 26, Level = LogLevel.Information, Message = "{WorkerId} cancelled {DeliveryId}. Endpoint {EndpointId} is disabled")]
    public static partial void Cancelled(ILogger logger, string workerId, DeliveryId deliveryId, WebhookEndpointId endpointId);

    [LoggerMessage(EventId = 30, Level = LogLevel.Warning, Message = "{WorkerId} recorded {Applied} of {Count} results. The rest were for leases it no longer held")]
    public static partial void LateResults(ILogger logger, string workerId, int applied, int count);

    [LoggerMessage(EventId = 31, Level = LogLevel.Error, Message = "{WorkerId} could not record {Count} results. Their leases will lapse and the deliveries be retried")]
    public static partial void ResultsLost(ILogger logger, Exception exception, string workerId, int count);

    [LoggerMessage(EventId = 32, Level = LogLevel.Warning, Message = "Endpoint {EndpointId} of {SubscriberId} is disabled. {Reason}")]
    public static partial void EndpointDisabled(ILogger logger, WebhookEndpointId endpointId, SubscriberId subscriberId, string? reason);

    [LoggerMessage(EventId = 33, Level = LogLevel.Error, Message = "The endpoint notifier failed for {EndpointId}. The endpoint is disabled all the same")]
    public static partial void NotifierFailed(ILogger logger, Exception exception, WebhookEndpointId endpointId);

    [LoggerMessage(EventId = 40, Level = LogLevel.Warning, Message = "Endpoint {EndpointId} has failed {Failures} times in a row. It takes {Limit} deliveries at once from now on")]
    public static partial void CircuitDegraded(ILogger logger, WebhookEndpointId endpointId, int failures, int limit);

    [LoggerMessage(EventId = 41, Level = LogLevel.Warning, Message = "Endpoint {EndpointId} has failed {Failures} times in a row. Nothing more goes to it before {ReopensAt}")]
    public static partial void CircuitOpened(ILogger logger, WebhookEndpointId endpointId, int failures, DateTimeOffset? reopensAt);

    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Endpoint {EndpointId} answered its probe. Deliveries resume one at a time")]
    public static partial void CircuitClosed(ILogger logger, WebhookEndpointId endpointId);

    [LoggerMessage(EventId = 43, Level = LogLevel.Information, Message = "Endpoint {EndpointId} is back at full concurrency")]
    public static partial void CircuitRecovered(ILogger logger, WebhookEndpointId endpointId);
}