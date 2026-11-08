using Hookwright.Core.Configuration;

namespace Hookwright.Dispatch;

/// <summary>
/// The delivery engine of one instance: the claimer feeding the workers feeding the
/// writer, started and stopped as one.
/// </summary>
/// <remarks>
/// Backpressure runs the whole length of it. The writer's channel is bounded, so a
/// slow store stalls the workers. The claimer's channel is bounded, so stalled
/// workers stop the claimer and a stopped claimer takes no more leases. Whatever the
/// backlog, what is in memory is two batches of deliveries, two batches of results,
/// and one attempt per worker. The <see cref="LeaseReaper"/> runs beside the pipeline,
/// not inside it: it recovers other instances' work as much as this one's.
/// </remarks>
public sealed class DispatchPipeline
{
    private readonly DeliveryClaimer _claimer;
    private readonly DeliveryWorker _worker;
    private readonly CompletionWriter _writer;
    private readonly int _concurrency;

    /// <summary>
    /// Creates the pipeline.
    /// </summary>
    /// <param name="claimer">Takes due deliveries under this instance's lease.</param>
    /// <param name="worker">Attempts them; run once per unit of concurrency.</param>
    /// <param name="writer">Records the results.</param>
    /// <param name="options">The concurrency.</param>
    public DispatchPipeline(
        DeliveryClaimer claimer,
        DeliveryWorker worker,
        CompletionWriter writer,
        DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(claimer);
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        _claimer = claimer;
        _worker = worker;
        _writer = writer;
        _concurrency = options.MaxConcurrency;
    }

    /// <summary>
    /// Names this instance on every lease it takes and every attempt it makes.
    /// </summary>
    public string WorkerId => _claimer.WorkerId;

    /// <summary>
    /// Runs until <paramref name="stoppingToken"/> is cancelled, then stops in order:
    /// no more claims, the attempts in flight finish and their results are written,
    /// and what was claimed but never started is handed back. Returns once the last
    /// of that is done.
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        Task writing = _writer.RunAsync();
        Task claiming = _claimer.RunAsync(stoppingToken);
        Task[] working = new Task[_concurrency];

        for (int i = 0; i < working.Length; i++)
        {
            working[i] = _worker.RunAsync(_claimer.Deliveries, _writer.Completions, stoppingToken);
        }

        try
        {
            await Task.WhenAll([claiming, .. working]).ConfigureAwait(false);
        }
        finally
        {
            _writer.Complete();
            await writing.ConfigureAwait(false);
        }

        await ReleaseAsync().ConfigureAwait(false);
    }

    private Task<int> ReleaseAsync()
    {
        return _claimer.ReleaseAsync(CancellationToken.None);
    }
}