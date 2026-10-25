using System.Net;
using System.Net.Sockets;

namespace Hookwright.Core.Transport;

/// <summary>
/// Opens every delivery connection itself, so that the address it checked is the address it connects to.
/// </summary>
public sealed class SsrfGuard
{
    private readonly IDestinationPolicy _policy;
    private readonly IHostResolver _resolver;

    /// <summary>
    /// Creates the guard.
    /// </summary>
    public SsrfGuard(IDestinationPolicy policy, IHostResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(resolver);

        _policy = policy;
        _resolver = resolver;
    }

    /// <summary>
    /// Connects to the destination of a request, or refuses to. Shaped for
    /// <see cref="SocketsHttpHandler.ConnectCallback"/>.
    /// </summary>
    /// <exception cref="DeliveryBlockedException">
    /// The destination, or an address it resolves to, is refused.
    /// </exception>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Uri destination = context.InitialRequestMessage.RequestUri
            ?? throw new InvalidOperationException("The request has no destination.");

        if (_policy.RefuseDestination(destination) is { } refusal)
        {
            throw new DeliveryBlockedException($"{destination} was refused: {refusal}.");
        }

        string host = context.DnsEndPoint.Host;
        IReadOnlyList<IPAddress> addresses = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);

        foreach (IPAddress address in addresses)
        {
            if (_policy.RefuseAddress(address) is { } reason)
            {
                throw new DeliveryBlockedException($"{host} resolves to {reason}, which deliveries may not reach.");
            }
        }

        SocketException? last = null;

        foreach (IPAddress address in addresses)
        {
            try
            {
                return await ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                last = exception;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    private static async ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Socket? socket = null;

        try
        {
            socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

            NetworkStream stream = new(socket, ownsSocket: true);
            socket = null;

            return stream;
        }
        finally
        {
            socket?.Dispose();
        }
    }
}