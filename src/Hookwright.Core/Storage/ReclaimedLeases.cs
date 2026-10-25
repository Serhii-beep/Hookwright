namespace Hookwright.Core.Storage;

/// <summary>
/// Deliveries returned to the queue because on worker's lease on them lapsed.
/// </summary>
/// <param name="LeaseOwner">The worker that held them and never reported back.</param>
/// <param name="Count">How many it dropped.</param>
public readonly record struct ReclaimedLeases(string LeaseOwner, int Count);