namespace Hookwright.Core.Storage;

/// <summary>
/// What one pruning pass removed.
/// </summary>
/// <param name="Attempts">Attempt records removed.</param>
/// <param name="Deliveries">Deliveries removed.</param>
/// <param name="Events">Events removed.</param>
public readonly record struct PruneResult(int Attempts, int Deliveries, int Events)
{
    /// <summary>
    /// Whether the pass removed nothing.
    /// </summary>
    public bool IsEmpty => Attempts == 0 && Deliveries == 0 && Events == 0;
}