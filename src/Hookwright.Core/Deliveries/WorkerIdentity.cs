using System.Security.Cryptography;

namespace Hookwright.Core.Deliveries;

/// <summary>
/// Names the process that claims and attempts deliveries, as <c>{machine}:{pid}:{id}</c>, so that
/// a lease or an attempt record says which instance it came from.
/// </summary>
public static class WorkerIdentity
{
    /// <summary>
    /// Longest identity produced, the limit <see cref="Delivery.LeaseOwner"/> and
    /// <see cref="DeliveryAttempt.WorkerId"/> both store.
    /// </summary>
    public const int MaxLength = Delivery.MaxLeaseOwnerLength;

    private const int TailBytes = 3;

    /// <summary>
    /// Creates the identity of the current process.
    /// </summary>
    public static string Create()
    {
        return Create(Environment.MachineName, Environment.ProcessId);
    }

    /// <summary>
    /// Creates an identity for the given machine and process, with a fresh random tail.
    /// </summary>
    /// <exception cref="ArgumentException">The machine name is empty.</exception>
    public static string Create(string machineName, int processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        string tail = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TailBytes));
        string suffix = $":{processId}:{tail}";

        string machine = machineName.Trim().Replace(':', '-');
        int room = MaxLength - suffix.Length;

        return string.Concat(machine.Length <= room ? machine : machine.AsSpan(0, room), suffix);
    }
}