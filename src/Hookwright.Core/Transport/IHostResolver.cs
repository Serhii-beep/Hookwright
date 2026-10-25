using System.Net;

namespace Hookwright.Core.Transport;

/// <summary>
/// Turns a destination's host name into the addresses it currently has.
/// </summary>
public interface IHostResolver
{
    /// <summary>
    /// Resolves a host name to every address it has, in the order they should be tried.
    /// </summary>
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}