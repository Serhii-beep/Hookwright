using System.Threading.Channels;

using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.Core.Transport;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class ScriptedTransport : IDeliveryTransport
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Func<ClaimedDelivery, int> _status;
    private readonly Channel<ClaimedDelivery> _started = Channel.CreateUnbounded<ClaimedDelivery>();
    private int _inFlight;
    private int _maxInFlight;
    private int _sent;

    public ScriptedTransport(Func<ClaimedDelivery, int>? status = null, TimeSpan? requestedDelay = null)
    {
        _status = status ?? (_ => 200);
        RequestedDelay = requestedDelay;
    }

    public TimeSpan? RequestedDelay { get; }

    public Task Gate { get; set; } = Task.CompletedTask;

    public int SentCount => Volatile.Read(ref _sent);

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public Task<ClaimedDelivery> NextSendAsync(CancellationToken cancellationToken)
    {
        return _started.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(Patience, cancellationToken);
    }

    public async Task<DeliveryResult> SendAsync(
        ClaimedDelivery delivery,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int inFlight = Interlocked.Increment(ref _inFlight);
        RaiseMax(inFlight);
        _started.Writer.TryWrite(delivery);

        try
        {
            await Gate.WaitAsync(cancellationToken);

            int status = _status(delivery);
            Interlocked.Increment(ref _sent);

            DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
                delivery.DeliveryId, AttemptOutcomes.FromStatusCode(status), status, null, null, now, Duration, workerId);

            return new DeliveryResult(attempt, RequestedDelay);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void RaiseMax(int inFlight)
    {
        int seen = Volatile.Read(ref _maxInFlight);

        while (seen < inFlight)
        {
            int previous = Interlocked.CompareExchange(ref _maxInFlight, inFlight, seen);

            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }
}