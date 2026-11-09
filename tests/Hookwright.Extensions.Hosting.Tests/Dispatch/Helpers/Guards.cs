using Hookwright.Core.Configuration;
using Hookwright.Core.Transport;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal static class Guards
{
    public static SsrfGuard Development()
    {
        return new SsrfGuard(
            new DefaultDestinationPolicy(new SecurityOptions { RequireHttps = false, AllowPrivateNetwork = true }),
            new DnsHostResolver());
    }
}