using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class DeliveryIdTests : PrefixedIdTests<DeliveryId>
{
    protected override DeliveryId CreateId() => DeliveryId.New();
}