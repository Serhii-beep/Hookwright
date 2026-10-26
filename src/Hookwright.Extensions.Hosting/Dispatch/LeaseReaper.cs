using Hookwright.Core.Configuration;
using Hookwright.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Hookwright.Dispatch;

/// <summary>
/// Returns the work of workers that died to the queue, by finding leases that lapsed.
/// </summary>
public sealed class LeaseReaper
{
    private readonly IDeliveryLeaseStore _store;
    private readonly DeliveryOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LeaseReaper> _logger;

    /// <summary>
    /// Creates the reaper.
    /// </summary>
    public LeaseReaper(
        IDeliveryLeaseStore store,
        DeliveryOptions options,
        TimeProvider timeProvider,
        ILogger<LeaseReaper> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        _time = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// How often lapsed leases are looked for.
    /// </summary>
    public TimeSpan Period => _options.LeaseDuration / 4;

    /// <summary>
    /// Reaps at once and then every <see cref="Period"/>, until <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Period, _time);

        try
        {
            do
            {
                await ReapAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {

        }
    }

    /// <summary>
    /// Returns every delivery whose lease has lapsed to the queue, once.
    /// </summary>
    /// <returns>
    /// What was recovered, by the worker that dropped it. Empty when the store
    /// could not be reached.
    /// </returns>
    public async Task<IReadOnlyList<ReclaimedLeases>> ReapAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ReclaimedLeases> reclaimed;

        try
        {
            reclaimed = await _store.ReclaimExpiredLeasesAsync(_time.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            DispatchLog.ReapFailed(_logger, exception, Period);

            return [];
        }

        foreach (ReclaimedLeases dropped in reclaimed)
        {
            DispatchMetrics.Reclaimed.Add(dropped.Count);
            DispatchLog.Reclaimed(_logger, dropped.Count, dropped.LeaseOwner);
        }

        return reclaimed;
    }
}