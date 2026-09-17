namespace Hookwright.Core.Storage;

/// <summary>
/// The pruner's view of storage: remove history that has aged out, a bounded batch at a time.
/// </summary>
public interface IRetentionStore
{
    /// <summary>
    /// Removes up to <paramref name="batchSize"/> rows of each kind that have aged past their
    /// cutoff. Attempt records are removed first, then finished deliveries with no attempts left, then events with no
    /// deliveries left.
    /// </summary>
    /// <param name="cutoffs">The instants before which each kind of history has aged out.</param>
    /// <param name="batchSize">Maximum rows of each kind to remove in this pass.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many rows of each kind were removed. Empty when nothing had aged out.</returns>
    Task<PruneResult> PruneAsync(RetentionCutoffs cutoffs, int batchSize, CancellationToken cancellationToken);
}