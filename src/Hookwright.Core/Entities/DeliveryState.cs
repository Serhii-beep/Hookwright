namespace Hookwright.Core.Entities;

/// <summary>
/// Where a delivery sits in its lifecycle.
/// </summary>
/// <remarks>
/// These numbers are persisted. Renumbering or reordering a member silently
/// reinterprets every existing row. Add new members at the end and never reuse
/// a retired number.
/// </remarks>
public enum DeliveryState : short
{
    /// <summary>
    /// Awaiting delivery. Eligible once <c>NextAttemptAt</c> has passed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Claimed by a worker under a lease and currently being attempted.
    /// </summary>
    InFlight = 1,

    /// <summary>
    /// The endpoint returned a 2xx response.
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// The endpoint rejected the event and retrying cannot help.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Every retry was exhausted without success.
    /// </summary>
    Dead = 4,

    /// <summary>
    /// Abandoned because the endpoint was deleted or disabled.
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// Held behind an older incomplete delivery in the same ordering partition.
    /// </summary>
    Blocked = 6
}