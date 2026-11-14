using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;

namespace Hookwright.EntityFrameworkCore.Stores;

/// <summary>
/// What a batch of completions says about one endpoint's health, reduced to what
/// a store writes: whether the endpoint answered during the batch, and the failures
/// since its last answer.
/// </summary>
internal sealed class EndpointHealthDelta
{
    private EndpointHealthDelta(bool answered, int failures, DateTimeOffset? failingSince)
    {
        Answered = answered;
        Failures = failures;
        FailingSince = failingSince;
    }

    /// <summary>
    /// Whether the endpoint answered at least once in the batch, which ends any run
    /// of failures that was under way before it.
    /// </summary>
    public bool Answered { get; }

    /// <summary>
    /// Retryable failures after the endpoint's last answer in the batch, or in the
    /// whole batch when it did not answer.
    /// </summary>
    public int Failures { get; }

    /// <summary>
    /// When the first of <see cref="Failures"/> was attempted, or <see langword="null"/>
    /// when there were none.
    /// </summary>
    public DateTimeOffset? FailingSince { get; }

    /// <summary>
    /// Whether the batch leaves the endpoint with no failures to its name.
    /// </summary>
    public bool ClearsFailures => Answered && Failures == 0;

    /// <summary>
    /// Reduces a batch of completions to one delta per endpoint whose attempts said anything
    /// about its health. Attempts are read in the order they were made, whatever order the batch
    /// has them in.
    /// </summary>
    public static IReadOnlyDictionary<WebhookEndpointId, EndpointHealthDelta> From(IReadOnlyList<DeliveryCompletion> completions)
    {
        ArgumentNullException.ThrowIfNull(completions);

        Dictionary<WebhookEndpointId, EndpointHealthDelta> deltas = [];

        foreach (IGrouping<WebhookEndpointId, DeliveryCompletion> endpoint in completions
            .Where(completion => completion.Attempt is not null)
            .GroupBy(completion => completion.EndpointId))
        {
            bool answered = false;
            int failures = 0;
            DateTimeOffset? failingSince = null;

            foreach (DeliveryAttempt attempt in endpoint
                .Select(completion => completion.Attempt)
                .OfType<DeliveryAttempt>()
                .OrderBy(attempt => attempt.AttemptedAt))
            {
                switch (AttemptOutcomes.HealthSignalOf(attempt.Outcome))
                {
                    case HealthSignal.Answered:
                        answered = true;
                        failures = 0;
                        failingSince = null;
                        break;
                    case HealthSignal.Failed:
                        failures++;
                        failingSince ??= attempt.AttemptedAt;
                        break;
                    default:
                        break;
                }
            }

            if (answered || failures > 0)
            {
                deltas[endpoint.Key] = new EndpointHealthDelta(answered, failures, failingSince);
            }
        }

        return deltas;
    }
}