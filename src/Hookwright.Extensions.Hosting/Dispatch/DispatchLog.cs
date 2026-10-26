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
}