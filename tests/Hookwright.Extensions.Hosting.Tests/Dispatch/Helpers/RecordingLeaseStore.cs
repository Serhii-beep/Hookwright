using System.Threading.Channels;

using Hookwright.Core.Storage;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class RecordingLeaseStore : IDeliveryLeaseStore
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly Queue<ClaimedDelivery> _due = new();
    private readonly Channel<ClaimCall> _claimed = Channel.CreateUnbounded<ClaimCall>();
    private readonly Channel<CompleteCall> _completed = Channel.CreateUnbounded<CompleteCall>();
    private readonly Channel<DateTimeOffset> _reaped = Channel.CreateUnbounded<DateTimeOffset>();

    public List<ClaimCall> Claims { get; } = [];

    public List<CompleteCall> Completions { get; } = [];

    public List<DateTimeOffset> Reaps { get; } = [];

    public List<ReleaseCall> Releases { get; } = [];

    public List<string> Calls { get; } = [];

    public Queue<IReadOnlyList<ReclaimedLeases>> ReapAnswers { get; } = new();

    public Exception? NextClaimFailure { get; set; }

    public Exception? NextCompleteFailure { get; set; }

    public Exception? NextReapFailure { get; set; }

    public Task CompleteGate { get; set; } = Task.CompletedTask;

    public int CompleteShortfall { get; set; }

    public int ReleaseAnswer { get; set; }

    public int Due => _due.Count;

    public int Recorded
    {
        get
        {
            lock (_gate)
            {
                return Completions.Sum(call => call.Completions.Count);
            }
        }
    }

    public void MakeDue(int count, DateTimeOffset leaseExpiresAt)
    {
        for (int i = 0; i < count; i++)
        {
            _due.Enqueue(TestDeliveries.Claimed(leaseExpiresAt));
        }
    }

    public Task<ClaimCall> NextClaimAsync(CancellationToken cancellationToken)
    {
        return _claimed.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(Patience, cancellationToken);
    }

    public Task<CompleteCall> NextCompleteAsync(CancellationToken cancellationToken)
    {
        return _completed.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(Patience, cancellationToken);
    }

    public Task<DateTimeOffset> NextReapAsync(CancellationToken cancellationToken)
    {
        return _reaped.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(Patience, cancellationToken);
    }

    public Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        string leaseOwner,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ClaimCall call = new(leaseOwner, batchSize, leaseDuration, now);

        lock (_gate)
        {
            Claims.Add(call);
            Calls.Add("claim");
        }

        List<ClaimedDelivery> taken = [];

        if (NextClaimFailure is { } failure)
        {
            NextClaimFailure = null;
            _claimed.Writer.TryWrite(call);

            return Task.FromException<IReadOnlyList<ClaimedDelivery>>(failure);
        }

        while (taken.Count < batchSize && _due.TryDequeue(out ClaimedDelivery? delivery))
        {
            taken.Add(delivery);
        }

        _claimed.Writer.TryWrite(call);

        return Task.FromResult<IReadOnlyList<ClaimedDelivery>>(taken);
    }

    public async Task<CompletionReport> CompleteAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        CompleteCall call = new(leaseOwner, [.. completions]);

        lock (_gate)
        {
            Completions.Add(call);
            Calls.Add("complete");
        }

        _completed.Writer.TryWrite(call);
        await CompleteGate.WaitAsync(cancellationToken);

        if (NextCompleteFailure is { } failure)
        {
            NextCompleteFailure = null;

            throw failure;
        }

        return new CompletionReport(Math.Max(0, completions.Count - CompleteShortfall), []);
    }

    public Task<IReadOnlyList<ReclaimedLeases>> ReclaimExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        Reaps.Add(now);
        _reaped.Writer.TryWrite(now);

        if (NextReapFailure is { } failure)
        {
            NextReapFailure = null;

            return Task.FromException<IReadOnlyList<ReclaimedLeases>>(failure);
        }

        return Task.FromResult(ReapAnswers.TryDequeue(out IReadOnlyList<ReclaimedLeases>? answer) ? answer : []);
    }

    public Task<int> ReleaseAsync(string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Releases.Add(new ReleaseCall(leaseOwner, now));
            Calls.Add("release");
        }

        return Task.FromResult(ReleaseAnswer);
    }

    internal sealed record ClaimCall(string LeaseOwner, int BatchSize, TimeSpan LeaseDuration, DateTimeOffset Now);

    internal sealed record CompleteCall(string LeaseOwner, IReadOnlyList<DeliveryCompletion> Completions);

    internal sealed record ReleaseCall(string LeaseOwner, DateTimeOffset Now);
}