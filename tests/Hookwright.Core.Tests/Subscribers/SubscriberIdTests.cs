using Hookwright.Core.Subscribers;
using Hookwright.Core.Tests.Identifiers;

namespace Hookwright.Core.Tests.Subscribers;

public sealed class SubscriberIdTests : PrefixedIdTests<SubscriberId>
{
    protected override SubscriberId CreateId() => SubscriberId.New();
}