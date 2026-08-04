using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class DeliveryAttemptIdTests : PrefixedIdTests<DeliveryAttemptId>
{
    protected override DeliveryAttemptId CreateId() => DeliveryAttemptId.New();
}