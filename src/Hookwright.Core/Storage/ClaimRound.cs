using System.Collections.ObjectModel;

using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Storage;

/// <summary>
/// How a claim is shared out between endpoints: where the round of them resumes,
/// and how many deliveries each may give.
/// </summary>
/// <remarks>
/// A claim serves endpoints in turn, in the order the store keeps their ids in.
/// It starts with the endpoint after <see cref="After"/>, carries on to the last
/// one and round from the first, and stops when the batch is full or it is back
/// where it started. Each endpoint with due deliveries gives its oldest,
/// <see cref="PerEndpoint"/> at most.
/// </remarks>
public sealed class ClaimRound
{
    /// <summary>
    /// Creates the round.
    /// </summary>
    /// <param name="after">The endpoint the round resumes after, or <see langword="null"/> to start with the first.</param>
    /// <param name="perEndpoint">The most deliveries one endpoint gives.</param>
    /// <param name="limits">The endpoints that may give fewer, each with the most it may give.</param>
    public ClaimRound(WebhookEndpointId? after, int perEndpoint, IReadOnlyDictionary<WebhookEndpointId, int> limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perEndpoint);
        ArgumentNullException.ThrowIfNull(limits);

        foreach (int limit in limits.Values)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(limit, nameof(limits));
        }

        After = after;
        PerEndpoint = perEndpoint;
        Limits = limits;
    }

    /// <summary>
    /// The endpoint the round resumes after: the one previous claim served last.
    /// <see langword="null"/> starts with the first endpoint.
    /// </summary>
    public WebhookEndpointId? After { get; }

    /// <summary>
    /// The most deliveries one endpoint gives to this claim.
    /// </summary>
    public int PerEndpoint { get; }

    /// <summary>
    /// The endpoints that may give fewer than <see cref="PerEndpoint"/>, each
    /// with the most it may give. An endpoint limited to zero is passed over,
    /// and its deliveries stay in the queue.
    /// </summary>
    public IReadOnlyDictionary<WebhookEndpointId, int> Limits { get; }

    /// <summary>
    /// A round that starts with the first endpoint and limits none of them.
    /// </summary>
    /// <param name="perEndpoint">The most deliveries one endpoint gives.</param>
    public static ClaimRound FromTheStart(int perEndpoint)
    {
        return new ClaimRound(null, perEndpoint, ReadOnlyDictionary<WebhookEndpointId, int>.Empty);
    }
}