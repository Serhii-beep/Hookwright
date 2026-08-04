using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class SubscriberIdTests : PrefixedIdTests<SubscriberId>
{
    protected override SubscriberId CreateId() => SubscriberId.New();
}