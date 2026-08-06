using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Identifiers;
using Hookwright.Core.Tests.Identifiers;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class DeliveryAttemptIdTests : PrefixedIdTests<DeliveryAttemptId>
{
    protected override DeliveryAttemptId CreateId() => DeliveryAttemptId.New();
}