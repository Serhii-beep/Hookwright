using Hookwright.Core.Endpoints;
using Hookwright.Core.Tests.Identifiers;

namespace Hookwright.Core.Tests.Endpoints;

public sealed class EndpointSecretIdTests : PrefixedIdTests<EndpointSecretId>
{
    protected override EndpointSecretId CreateId() => EndpointSecretId.New();
}