using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class WebhookEventIdTests : PrefixedIdTests<WebhookEventId>
{
    protected override WebhookEventId CreateId() => WebhookEventId.New();
}