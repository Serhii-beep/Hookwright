using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Storage;

/// <summary>
/// What a claim took, and where the next one takes up the round.
/// </summary>
/// <param name="Deliveries">
/// The claimed deliveries, endpoint by endpoint in the order the endpoints were served.
/// </param>
/// <param name="ResumeAfter">
/// The endpoint served last, when the batch filled before every endpoint had its turn:
/// the next claim resumes after it. <see langword="null"/> when the claim went all the
/// way round, so that the next one starts a new round.
/// </param>
public sealed record ClaimedBatch(IReadOnlyList<ClaimedDelivery> Deliveries, WebhookEndpointId? ResumeAfter);