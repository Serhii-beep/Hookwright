namespace Hookwright.EntityFrameworkCore;

/// <summary>
/// The table names Hookwright creates.
/// </summary>
public static class HookwrightTables
{
    /// <summary>
    /// Prefix carried by every table.
    /// </summary>
    public const string Prefix = "hookwright_";

    /// <summary>
    /// Subscribers.
    /// </summary>
    public const string Subscribers = Prefix + "subscribers";

    /// <summary>
    /// The URLs the subscribers registered.
    /// </summary>
    public const string Endpoints = Prefix + "endpoints";

    /// <summary>
    /// Signing keys, one row per key endpoint.
    /// </summary>
    public const string EndpointSecrets = Prefix + "endpoint_secrets";

    /// <summary>
    /// The event type registry.
    /// </summary>
    public const string EventTypes = Prefix + "event_types";

    /// <summary>
    /// The durable event log.
    /// </summary>
    public const string Events = Prefix + "events";

    /// <summary>
    /// One row per event per endpoint.
    /// </summary>
    public const string Deliveries = Prefix + "deliveries";

    /// <summary>
    /// One row per HTTP attempt.
    /// </summary>
    public const string DeliveryAttempts = Prefix + "delivery_attempts";
}