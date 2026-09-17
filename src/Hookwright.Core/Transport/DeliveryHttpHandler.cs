using System.Net;

using Hookwright.Core.Configuration;

namespace Hookwright.Core.Transport;

/// <summary>
/// The one <see cref="SocketsHttpHandler"/> deliveries go through.
/// </summary>
public static class DeliveryHttpHandler
{
    /// <summary>
    /// How long a pooled connection lives before it is replaced.
    /// </summary>
    public static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long an idle pooled connection is kept.
    /// </summary>
    public static readonly TimeSpan ConnectionIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates the handler.
    /// </summary>
    public static SocketsHttpHandler Create(DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = ConnectionLifetime,
            PooledConnectionIdleTimeout = ConnectionIdleTimeout,
            MaxConnectionsPerServer = options.MaxConnectionsPerEndpoint,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        };
    }
}