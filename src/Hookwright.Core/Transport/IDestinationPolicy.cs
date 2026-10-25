using System.Net;

namespace Hookwright.Core.Transport;

/// <summary>
/// Defines where a delivery may be sent: first by the URL alone, then by every address the URL's
/// host turns out to have.
/// </summary>
public interface IDestinationPolicy
{
    /// <summary>
    /// Judges the URL before anything is resolved or connected.
    /// </summary>
    /// <returns>Why the destination is refused, or <see langword="null"/> when it is not.</returns>
    string? RefuseDestination(Uri destination);

    /// <summary>
    /// Judges one address the destination resolved to.
    /// </summary>
    /// <returns>What kind of address was refused, or <see langword="null"/> when it is allowed.</returns>
    string? RefuseAddress(IPAddress address);
}