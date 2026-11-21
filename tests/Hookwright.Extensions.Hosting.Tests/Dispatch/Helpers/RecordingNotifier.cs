using Hookwright.Core.Endpoints;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class RecordingNotifier : IEndpointNotifier
{
    public List<WebhookEndpoint> Disabled { get; } = [];

    public Exception? Failure { get; set; }

    public Task EndpointDisabledAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken)
    {
        Disabled.Add(endpoint);

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}