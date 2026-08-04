using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Identifiers;

public sealed class EndpointSecretIdTests : PrefixedIdTests<EndpointSecretId>
{
    protected override EndpointSecretId CreateId() => EndpointSecretId.New();
}