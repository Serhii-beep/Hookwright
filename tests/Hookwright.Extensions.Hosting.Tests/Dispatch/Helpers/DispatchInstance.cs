using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.Dispatch;

using Microsoft.Extensions.Logging.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class DispatchInstance
{
    private readonly DeliveryClaimer _claimer;
    private readonly IDeliveryLeaseStore _store;
    private readonly int _hangAfter;
    private int _completed;

    private DispatchInstance(DeliveryClaimer claimer, IDeliveryLeaseStore store, int hangAfter)
    {
        _claimer = claimer;
        _store = store;
        _hangAfter = hangAfter;
    }

    public string WorkerId => _claimer.WorkerId;

    public int Completed => Volatile.Read(ref _completed);

    public Task Stopped { get; private set; } = Task.CompletedTask;

    public static DispatchInstance Start(
        string machine,
        DeliveryOptions options,
        IDeliveryLeaseStore store,
        FakeLogCollector log,
        CancellationToken token,
        int hangAfter = int.MaxValue)
    {
        DeliveryClaimer claimer = new(
            store, options, WorkerIdentity.Create(machine, Environment.ProcessId), TimeProvider.System, new FakeLogger<DeliveryClaimer>(log));
        LeaseReaper reaper = new(store, options, TimeProvider.System, new FakeLogger<LeaseReaper>(log));
        DispatchInstance instance = new(claimer, store, hangAfter);

        instance.Stopped = Task.WhenAll(claimer.RunAsync(token), reaper.RunAsync(token), instance.WorkAsync(token));

        return instance;
    }

    private async Task WorkAsync(CancellationToken token)
    {
        try
        {
            await foreach (ClaimedDelivery claimed in _claimer.Deliveries.ReadAllAsync(token))
            {
                DateTimeOffset now = TimeProvider.System.GetUtcNow();
                DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
                    claimed.DeliveryId, AttemptOutcome.Succeeded, 200, null, null, now, TimeSpan.FromMilliseconds(1), WorkerId);

                await _store.CompleteAsync(
                    WorkerId, [DeliveryCompletion.From(claimed, attempt, new RetryDecision.Succeeded(), now)], token);

                if (Interlocked.Increment(ref _completed) == _hangAfter)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Killed, or stopped.
        }
    }
}