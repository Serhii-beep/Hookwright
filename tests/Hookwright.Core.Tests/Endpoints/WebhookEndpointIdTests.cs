using Hookwright.Core.Endpoints;
using Hookwright.Core.Tests.Identifiers;

namespace Hookwright.Core.Tests.Endpoints;

public sealed class WebhookEndpointIdTests : PrefixedIdTests<WebhookEndpointId>
{
    protected override WebhookEndpointId CreateId() => WebhookEndpointId.New();
}