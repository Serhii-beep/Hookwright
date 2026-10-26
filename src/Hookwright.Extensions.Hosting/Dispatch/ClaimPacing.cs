namespace Hookwright.Dispatch;

/// <summary>
/// How long the claimer waits between claims: nothing while batches come
/// back full, a short pause while work trickles in, and longer and longer pauses,
/// up to the poll interval, while there is none.
/// </summary>
internal sealed class ClaimPacing
{
    /// <summary>
    /// The shortest pause between two claims that did not fill a batch.
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _floor;
    private readonly TimeSpan _ceiling;
    private TimeSpan _idle;

    /// <summary>
    /// Creates the pacing.
    /// </summary>
    /// <param name="ceiling">The longest pause: how often to look when nothing has been due for a while.</param>
    public ClaimPacing(TimeSpan ceiling)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ceiling, TimeSpan.Zero);

        _ceiling = ceiling;
        _floor = Floor < ceiling ? Floor : ceiling;
        _idle = _floor;
    }

    /// <summary>
    /// The pause before the next claim, given how the last one went.
    /// </summary>
    /// <param name="claimed">How many deliveries the last claim returned.</param>
    /// <param name="batchSize">How many a full batch holds.</param>
    public TimeSpan Next(int claimed, int batchSize)
    {
        if (claimed >= batchSize)
        {
            _idle = _floor;

            return TimeSpan.Zero;
        }

        if (claimed > 0)
        {
            _idle = _floor;

            return _floor;
        }

        TimeSpan pause = _idle;
        _idle = _idle + _idle < _ceiling ? _idle + _idle : _ceiling;

        return pause;
    }

    /// <summary>
    /// Forgets the idle backoff, because something just arrived.
    /// </summary>
    public void Reset()
    {
        _idle = _floor;
    }
}