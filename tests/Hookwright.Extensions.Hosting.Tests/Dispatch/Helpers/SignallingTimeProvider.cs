using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class SignallingTimeProvider : FakeTimeProvider
{
    private readonly Channel<bool> _timers = Channel.CreateUnbounded<bool>();

    public int TimersCreated { get; private set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);

        TimersCreated++;
        _timers.Writer.TryWrite(true);

        return timer;
    }

    public Task TimerCreatedAsync(CancellationToken cancellationToken)
    {
        return _timers.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
}