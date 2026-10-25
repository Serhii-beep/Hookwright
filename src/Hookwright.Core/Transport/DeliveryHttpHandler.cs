using System.Net;

using Hookwright.Core.Configuration;

namespace Hookwright.Core.Transport;

/// <summary>
/// The one <see cref="SocketsHttpHandler"/> deliveries go through, configured for talking to
/// strangers' servers rather than to the host's own services.
/// </summary>
public static class DeliveryHttpHandler
{
    /// <summary>
    /// How long a pooled connection lives before it is replaced, so that a destination that
    /// moved in DNS is found without a restart.
    /// </summary>
    public static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long an idle pooled connection is kept.
    /// </summary>
    public static readonly TimeSpan ConnectionIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates the handler. The caller owns it and disposes it.
    /// </summary>
    public static SocketsHttpHandler Create(DeliveryOptions options, SsrfGuard guard)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(guard);

        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = ConnectionLifetime,
            PooledConnectionIdleTimeout = ConnectionIdleTimeout,
            MaxConnectionsPerServer = options.MaxConnectionsPerEndpoint,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            ConnectCallback = guard.ConnectAsync
        };
    }
}