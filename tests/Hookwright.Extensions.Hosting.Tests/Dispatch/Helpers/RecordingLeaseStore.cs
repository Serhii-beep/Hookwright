using System.Threading.Channels;

using Hookwright.Core.Storage;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class RecordingLeaseStore : IDeliveryLeaseStore
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Queue<ClaimedDelivery> _due = new();
    private readonly Channel<ClaimCall> _claimed = Channel.CreateUnbounded<ClaimCall>();
    private readonly Channel<DateTimeOffset> _reaped = Channel.CreateUnbounded<DateTimeOffset>();

    public List<ClaimCall> Claims { get; } = [];

    public List<DateTimeOffset> Reaps { get; } = [];

    public List<ReleaseCall> Releases { get; } = [];

    public Queue<IReadOnlyList<ReclaimedLeases>> ReapAnswers { get; } = new();

    public Exception? NextClaimFailure { get; set; }

    public Exception? NextReapFailure { get; set; }

    public int ReleaseAnswer { get; set; }

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
        Claims.Add(call);

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

    public Task<int> CompleteAsync(
        string leaseOwner,
        IReadOnlyList<DeliveryCompletion> completions,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The claimer and the reaper never complete a delivery.");
    }

    public Task<IReadOnlyList<ReclaimedLeases>> ReclaimExpiredLeasesAsync(DateTimeOffset now, CancellationToken
cancellationToken)
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
        Releases.Add(new ReleaseCall(leaseOwner, now));

        return Task.FromResult(ReleaseAnswer);
    }

    internal sealed record ClaimCall(string LeaseOwner, int BatchSize, TimeSpan LeaseDuration, DateTimeOffset Now);

    internal sealed record ReleaseCall(string LeaseOwner, DateTimeOffset Now);
}