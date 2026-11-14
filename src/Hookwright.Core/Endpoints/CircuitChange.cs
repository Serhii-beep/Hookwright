namespace Hookwright.Core.Endpoints;

/// <summary>
/// What recording an attempt did to an endpoint's circuit.
/// </summary>
public enum CircuitChange
{
    /// <summary>
    /// Nothing worth telling anyone.
    /// </summary>
    None = 0,

    /// <summary>
    /// The endpoint has failed <see cref="EndpointCircuit.DegradeAfter"/> times in a row.
    /// Its concurrency is being cut.
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// The endpoint has failed <see cref="EndpointCircuit.OpenAfter"/> times in a row, or
    /// has failed its probe. Nothing gies to it until the cooldown is over.
    /// </summary>
    Opened = 2,

    /// <summary>
    /// The endpoint answered its probe. Deliveries flow again.
    /// </summary>
    Closed = 3,

    /// <summary>
    /// The endpoint is back at full concurrency.
    /// </summary>
    Recovered = 4,

    /// <summary>
    /// The endpoint has been failing for longer than it is allowed to. Is should be disabled.
    /// </summary>
    Exhausted = 5
}