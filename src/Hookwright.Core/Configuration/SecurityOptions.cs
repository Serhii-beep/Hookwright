using System.Net;

using Hookwright.Core.Events;

namespace Hookwright.Core.Configuration;

/// <summary>
/// What is accepted at publish time, and where deliveries are allowed to go.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// The largest payload the structure can hold, whatever <see cref="MaxPayloadBytes"/> says.
    /// </summary>
    public const int AbsolutePayloadBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Whether endpoints must use HTTPS. Default <see langword="true"/>.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Whether deliveries may reach loopback, private, link-local, and carrier-grade NAT addresses,
    /// on any port.
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }

    /// <summary>
    /// Networks deliveries may reach although the default rules would refuse them, as CIDR blocks
    /// such as <c>10.20.0.0/16</c>.
    /// </summary>
    public IReadOnlyCollection<string> AllowedNetworks { get; set; } = [];

    /// <summary>
    /// Ports deliveries may use besides the scheme's default. A destination on another port is
    /// refused.
    /// </summary>
    public IReadOnlyCollection<int> AllowedPorts { get; set; } = [];

    /// <summary>
    /// Largest event payload accepted at publish time. Default 256 KB.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Most custom headers an event may carry. Default, and ceiling, <see cref="WebhookEvent.MaxHeaders"/>.
    /// </summary>
    public int MaxEventHeaders { get; set; } = WebhookEvent.MaxHeaders;

    /// <summary>
    /// Collects every configuration problem.
    /// </summary>
    /// <returns>The problems found, empty when configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        OptionsValidator validator = new();

        validator.AtLeast(MaxPayloadBytes, 1, nameof(MaxPayloadBytes));
        validator.AtLeast(MaxEventHeaders, 0, nameof(MaxEventHeaders));
        validator.NotNull(AllowedNetworks, nameof(AllowedNetworks));
        validator.NotNull(AllowedPorts, nameof(AllowedPorts));

        validator.Require(
            MaxPayloadBytes <= AbsolutePayloadBytes,
            $"{nameof(MaxPayloadBytes)} ({MaxPayloadBytes}) must not exceed {AbsolutePayloadBytes}.");

        validator.Require(
            MaxEventHeaders <= WebhookEvent.MaxHeaders,
            $"{nameof(MaxEventHeaders)} ({MaxEventHeaders}) must not exceed {WebhookEvent.MaxHeaders}, " +
            "the structural maximum enforced by the event itself.");

        if (AllowedNetworks is not null)
        {
            foreach (string network in AllowedNetworks.Where(network => !IPNetwork.TryParse(network, out _)))
            {
                validator.Require(false, $"{nameof(AllowedNetworks)} entry '{network}' is not a CIDR block.");
            }
        }

        if (AllowedPorts is not null)
        {
            foreach (int port in AllowedPorts.Where(port => port is < 1 or > 65535))
            {
                validator.Require(false, $"{nameof(AllowedPorts)} entry {port} is not a port.");
            }
        }

        return validator.Errors;
    }
}