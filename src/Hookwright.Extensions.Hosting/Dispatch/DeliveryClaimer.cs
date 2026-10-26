using System.Threading.Channels;

using Hookwright.Core.Configuration;
using Hookwright.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Hookwright.Dispatch;

/// <summary>
/// Takes due deliveries out of the store under this worker's lease and hands them downstream,
/// never faster than the downstream takes them.
/// </summary>
/// <remarks>
/// Backpressure is the room in <see cref="Deliveries"/>: a claim is sized to what fits, and when
/// nothing fits nothing is claimed, so a slow pipeline shows up as fewer leases held rather than
/// as memory. The buffer holds two batches, one being handed out and one waiting, so that a claim
/// round trip never leaves workers idle, and no more, so that a lease is never held long before
/// anyone works on it.
/// </remarks>
public sealed class DeliveryClaimer
{
    private readonly IDeliveryLeaseStore _store;
    private readonly DeliveryOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<DeliveryClaimer> _logger;
    private readonly Channel<ClaimedDelivery> _deliveries;
    private readonly int _capacity;
    private readonly ClaimPacing _pacing;
    private TaskCompletionSource _wake = NewWake();

    /// <summary>
    /// Creates the claimer.
    /// </summary>
    /// <param name="store">Where deliveries are claimed from, and handed back to.</param>
    /// <param name="options">Batch size, lease duration, and the poll interval.</param>
    /// <param name="workerId">This worker's name, from <see cref="Core.Deliveries.WorkerIdentity"/>.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Logger.</param>
    public DeliveryClaimer(
        IDeliveryLeaseStore store,
        DeliveryOptions options,
        string workerId,
        TimeProvider timeProvider,
        ILogger<DeliveryClaimer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        WorkerId = workerId;
        _time = timeProvider;
        _logger = logger;
        _capacity = 2 * options.BatchSize;
        _pacing = new ClaimPacing(options.PollInterval);
        _deliveries = Channel.CreateBounded<ClaimedDelivery>(new BoundedChannelOptions(_capacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>
    /// Names this worker on every lease it takes and every attempt it makes.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Claimed deliveries, in the order they were claimed. Completes once <see cref="RunAsync"/> has stopped.
    /// </summary>
    public ChannelReader<ClaimedDelivery> Deliveries => _deliveries.Reader;

    /// <summary>
    /// Claims until <paramref name="stoppingToken"/> is cancelled, then completes <see cref="Deliveries"/>.
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _deliveries.Writer.WaitToWriteAsync(stoppingToken).ConfigureAwait(false))
            {
                int requested = Math.Min(_options.BatchSize, Math.Max(1, _capacity - _deliveries.Reader.Count));
                TimeSpan pause;

                try
                {
                    int claimed = await ClaimAsync(requested, stoppingToken).ConfigureAwait(false);
                    pause = _pacing.Next(claimed, _options.BatchSize);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    pause = _pacing.Next(0, _options.BatchSize);
                    DispatchLog.ClaimFailed(_logger, exception, WorkerId, pause);
                }

                if (pause > TimeSpan.Zero)
                {
                    await PauseAsync(pause, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {

        }
        finally
        {
            _deliveries.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Cuts the current pause short, because work was just committed.
    /// </summary>
    public void Wake()
    {
        Volatile.Read(ref _wake).TrySetResult();
    }

    /// <summary>
    /// Hands back every delivery this worker still holds, due at once.
    /// </summary>
    /// <returns>How many deliveries were handed back.</returns>
    public async Task<int> ReleaseAsync(CancellationToken cancellationToken)
    {
        int released = await _store.ReleaseAsync(WorkerId, _time.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (released > 0)
        {
            DispatchMetrics.Released.Add(released);
            DispatchLog.Released(_logger, WorkerId, released);
        }

        return released;
    }

    private static TaskCompletionSource NewWake()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task<int> ClaimAsync(int requested, CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedDelivery> claimed = await _store
            .ClaimAsync(WorkerId, requested, _options.LeaseDuration, _time.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        foreach (ClaimedDelivery delivery in claimed)
        {
            await _deliveries.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
        }

        if (claimed.Count > 0)
        {
            DispatchMetrics.Claimed.Add(claimed.Count);
            DispatchLog.Claimed(_logger, WorkerId, claimed.Count);
        }

        return claimed.Count;
    }

    private async Task PauseAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        TaskCompletionSource wake = Volatile.Read(ref _wake);
        using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(pause, _time, timer.Token);

        if (await Task.WhenAny(delay, wake.Task).ConfigureAwait(false) == wake.Task)
        {
            Interlocked.Exchange(ref _wake, NewWake());
            _pacing.Reset();
            await timer.CancelAsync().ConfigureAwait(false);

            return;
        }

        await delay.ConfigureAwait(false);
    }
}