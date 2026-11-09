using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Storage;

/// <summary>
/// What writing a batch of completions did.
/// </summary>
/// <param name="Applied">How many deliveries were still under the worker's lease and were written.</param>
/// <param name="Retired">
/// The endpoints the batch disabled, so that their owners can be told. An endpoint that was already
/// disabled is not listed.
/// </param>
public sealed record CompletionReport(int Applied, IReadOnlyList<WebhookEndpoint> Retired);