using System.Net;

using Hookwright.Core.Configuration;

namespace Hookwright.Core.Transport;

/// <summary>
/// Deny by default: every address range that is not the public internet is refused, with the
/// exceptions <see cref="SecurityOptions"/> grants, and HTTPS on a default port unless told otherwise.
/// </summary>
public sealed class DefaultDestinationPolicy : IDestinationPolicy
{
    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");

    // Cloud metadata first, so that it is named as such and stays refused under a development allowance.
    private static readonly DeniedNetwork[] Denied =
    [
        new(IPNetwork.Parse("169.254.169.254/32"), "a cloud metadata address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("fd00:ec2::254/128"), "a cloud metadata address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("0.0.0.0/8"), "an unspecified address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("10.0.0.0/8"), "a private network address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("100.64.0.0/10"), "a carrier-grade NAT address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("127.0.0.0/8"), "a loopback address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("169.254.0.0/16"), "a link-local address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("172.16.0.0/12"), "a private network address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("192.0.0.0/24"), "a reserved address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("192.0.2.0/24"), "a documentation address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("192.88.99.0/24"), "a reserved address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("192.168.0.0/16"), "a private network address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("198.18.0.0/15"), "a benchmarking address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("198.51.100.0/24"), "a documentation address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("203.0.113.0/24"), "a documentation address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("224.0.0.0/4"), "a multicast address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("240.0.0.0/4"), "a reserved address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("::/128"), "an unspecified address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("::1/128"), "a loopback address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("64:ff9b:1::/48"), "a private network address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("100::/64"), "a discard address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("2001:db8::/32"), "a documentation address", AllowedInDevelopment: false),
        new(IPNetwork.Parse("fc00::/7"), "a private network address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("fe80::/10"), "a link-local address", AllowedInDevelopment: true),
        new(IPNetwork.Parse("ff00::/8"), "a multicast address", AllowedInDevelopment: false),
    ];

    private readonly bool _requireHttps;
    private readonly bool _allowPrivateNetwork;
    private readonly IPNetwork[] _allowedNetworks;
    private readonly HashSet<int> _allowedPorts;

    /// <summary>
    /// Creates the policy from the configured exceptions.
    /// </summary>
    public DefaultDestinationPolicy(SecurityOptions security)
    {
        ArgumentNullException.ThrowIfNull(security);

        _requireHttps = security.RequireHttps;
        _allowPrivateNetwork = security.AllowPrivateNetwork;
        _allowedNetworks = [.. security.AllowedNetworks.Select(IPNetwork.Parse)];
        _allowedPorts = [.. security.AllowedPorts];
    }

    /// <inheritdoc />
    public string? RefuseDestination(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.IsAbsoluteUri)
        {
            return "the URL is not absolute";
        }

        if (destination.Scheme == Uri.UriSchemeHttp && _requireHttps)
        {
            return $"plain HTTP is not allowed. Set {nameof(SecurityOptions)}.{nameof(SecurityOptions.RequireHttps)} to false to permit it";
        }

        if (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps)
        {
            return $"the {destination.Scheme} scheme is not allowed";
        }

        if (!destination.IsDefaultPort && !_allowPrivateNetwork && !_allowedPorts.Contains(destination.Port))
        {
            return $"port {destination.Port} is not allowed. Add it to {nameof(SecurityOptions)}.{nameof(SecurityOptions.AllowedPorts)} to permit it";
        }

        return null;
    }

    /// <inheritdoc />
    public string? RefuseAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        IPAddress candidate = Unwrap(address);

        if (_allowedNetworks.Any(network => network.Contains(candidate)))
        {
            return null;
        }

        foreach (DeniedNetwork denied in Denied)
        {
            if (!denied.Network.Contains(candidate))
            {
                continue;
            }

            return denied.AllowedInDevelopment && _allowPrivateNetwork ? null : denied.Reason;
        }

        return null;
    }

    private static IPAddress Unwrap(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        if (Nat64.Contains(address))
        {
            return new IPAddress(address.GetAddressBytes()[12..]);
        }

        return address;
    }

    private readonly record struct DeniedNetwork(IPNetwork Network, string Reason, bool AllowedInDevelopment);
}