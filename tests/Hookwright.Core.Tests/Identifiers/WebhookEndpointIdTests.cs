using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class WebhookEndpointIdTests : PrefixedIdTests<WebhookEndpointId>
{
    protected override WebhookEndpointId CreateId() => WebhookEndpointId.New();
}