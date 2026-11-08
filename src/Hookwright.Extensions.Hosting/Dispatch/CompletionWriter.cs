using System.Threading.Channels;

using Hookwright.Core.Configuration;
using Hookwright.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Hookwright.Dispatch;

/// <summary>
/// Writes the workers' results to the store in batches.
/// </summary>
/// <remarks>
/// The channel the workers write to is bounded, two batches deep. A store that cannot
/// keep up fills it, the workers stall on it, the claimer's buffer fills behind them,
/// and the claimer stops claiming: backpressure reaches the database from the database,
/// and nothing in between grows.
/// </remarks>
public sealed class CompletionWriter
{
    /// <summary>
    /// How long a batch that is not full waits for company before it is written.
    /// </summary>
    public static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(100);

    private readonly IDeliveryLeaseStore _store;
    private readonly int _batchSize;
    private readonly TimeProvider _time;
    private readonly ILogger<CompletionWriter> _logger;
    private readonly Channel<DeliveryCompletion> _completions;

    /// <summary>
    /// Creates the writer.
    /// </summary>
    /// <param name="store">Where results are written.</param>
    /// <param name="options">The batch size, which is also the claim batch size.</param>
    /// <param name="workerId">The worker whose leases the results are written under.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Used for logging.</param>
    public CompletionWriter(
        IDeliveryLeaseStore store,
        DeliveryOptions options,
        string workerId,
        TimeProvider timeProvider,
        ILogger<CompletionWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _batchSize = options.BatchSize;
        WorkerId = workerId;
        _time = timeProvider;
        _logger = logger;
        _completions = Channel.CreateBounded<DeliveryCompletion>(new BoundedChannelOptions(2 * options.BatchSize)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>
    /// The worker whose results these are.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Where the workers queue their results.
    /// </summary>
    public ChannelWriter<DeliveryCompletion> Completions => _completions.Writer;

    /// <summary>
    /// Says that nothing more will be queued, so that <see cref="RunAsync"/> returns once it has
    /// written what is there.
    /// </summary>
    public void Complete()
    {
        _completions.Writer.TryComplete();
    }

    /// <summary>
    /// Writes results as they come, a full batch at once and a partial one after <see cref="Linger"/>,
    /// until <see cref="Complete"/> has been called and the last result is written.
    /// </summary>
    public async Task RunAsync()
    {
        ChannelReader<DeliveryCompletion> reader = _completions.Reader;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            List<DeliveryCompletion> batch = new(_batchSize);

            await GatherAsync(reader, batch).ConfigureAwait(false);
            await WriteAsync(batch).ConfigureAwait(false);
        }
    }

    private static void Take(ChannelReader<DeliveryCompletion> reader, List<DeliveryCompletion> batch, int batchSize)
    {
        while (batch.Count < batchSize && reader.TryRead(out DeliveryCompletion? completion))
        {
            batch.Add(completion);
        }
    }

    private async Task GatherAsync(ChannelReader<DeliveryCompletion> reader, List<DeliveryCompletion> batch)
    {
        Take(reader, batch, _batchSize);

        if (batch.Count >= _batchSize)
        {
            return;
        }

        using CancellationTokenSource linger = new(Linger, _time);

        try
        {
            while (batch.Count < _batchSize && await reader.WaitToReadAsync(linger.Token).ConfigureAwait(false))
            {
                Take(reader, batch, _batchSize);
            }
        }
        catch (OperationCanceledException) when (linger.IsCancellationRequested)
        {
            // The linger is over. What is in hand goes now.
        }
    }

    private async Task WriteAsync(List<DeliveryCompletion> batch)
    {
        int applied;

        try
        {
            applied = await _store.CompleteAsync(WorkerId, batch, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Holding these would hold every worker behind them. Their deliveries stay in flight until
            // the reaper returns them, and are attempted again.
            DispatchMetrics.Lost.Add(batch.Count);
            DispatchLog.ResultsLost(_logger, exception, WorkerId, batch.Count);

            return;
        }

        if (applied < batch.Count)
        {
            // The store dropped results for leases this worker no longer held: another worker has
            // those deliveries now, and the attempt record says so.
            DispatchMetrics.Late.Add(batch.Count - applied);
            DispatchLog.LateResults(_logger, WorkerId, applied, batch.Count);
        }
    }
}