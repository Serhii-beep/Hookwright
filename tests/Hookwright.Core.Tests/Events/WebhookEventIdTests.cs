using Hookwright.Core.Events;
using Hookwright.Core.Tests.Identifiers;

namespace Hookwright.Core.Tests.Events;

public sealed class WebhookEventIdTests : PrefixedIdTests<WebhookEventId>
{
    protected override WebhookEventId CreateId() => WebhookEventId.New();
}