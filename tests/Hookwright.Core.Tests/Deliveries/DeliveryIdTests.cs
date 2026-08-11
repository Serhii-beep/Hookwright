using Hookwright.Core.Deliveries;
using Hookwright.Core.Tests.Identifiers;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class DeliveryIdTests : PrefixedIdTests<DeliveryId>
{
    protected override DeliveryId CreateId() => DeliveryId.New();
}