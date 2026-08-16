using Hookwright.Core.Events;

namespace Hookwright.Core.Configuration;

/// <summary>
/// What is accepted at publish time, and where deliveries are allowed to go.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// Hard ceiling on <see cref="MaxPayloadBytes"/>.
    /// </summary>
    public const int AbsolutePayloadBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Whether endpoints must use HTTPS. Default <see langword="true"/>.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Whether endpoints may resolve to private, loopback, or link-local addresses.
    /// Default <see langword="false"/>.
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }

    /// <summary>
    /// Largest event payload accepted at publish time. Default 256 KB.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Most custom headers an event may carry. Default to 20.
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

        validator.Require(
            MaxPayloadBytes <= AbsolutePayloadBytes,
            $"{nameof(MaxPayloadBytes)} ({MaxPayloadBytes}) must not exceed {AbsolutePayloadBytes}.");

        validator.Require(
            MaxEventHeaders <= WebhookEvent.MaxHeaders,
            $"{nameof(MaxEventHeaders)} ({MaxEventHeaders}) must not exceed {WebhookEvent.MaxHeaders}, " +
            "the structural maximum enforced by the event itself.");

        return validator.Errors;
    }
}