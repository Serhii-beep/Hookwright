namespace Hookwright.Core.Entities;

/// <summary>
/// The ordering guarantee an endpoint has opted into.
/// </summary>
public enum PartitionMode : short
{
    /// <summary>
    /// Best effort. Deliveries proceed in parallel and a retry may reorder them.
    /// Unbounded throughput.
    /// </summary>
    None = 0,

    /// <summary>
    /// Strict ordering within each partition key, parallel across keys. Gives per-order
    /// or per-customer ordering without a global throughput ceiling.
    /// </summary>
    ByKey = 1,

    /// <summary>
    /// Strict ordering across the whole endpoint. One stuck delivery blocks every later one,
    /// capping throughput at roughly one divided by the consumer's response time.
    /// </summary>
    ByEndpoint = 2
}