using System.Net;

namespace Hookwright.Core.Transport;

/// <summary>
/// Resolves through the system resolver.
/// </summary>
public sealed class DnsHostResolver : IHostResolver
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}