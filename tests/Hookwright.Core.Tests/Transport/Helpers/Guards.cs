using Hookwright.Core.Configuration;
using Hookwright.Core.Transport;

namespace Hookwright.Core.Tests.Transport.Helpers;

internal static class Guards
{
    public static SsrfGuard Development(IHostResolver? resolver = null)
    {
        return new SsrfGuard(
            new DefaultDestinationPolicy(new SecurityOptions { RequireHttps = false, AllowPrivateNetwork = true }),
            resolver ?? new DnsHostResolver());
    }

    public static SsrfGuard Strict(IHostResolver? resolver = null, SecurityOptions? security = null)
    {
        return new SsrfGuard(
            new DefaultDestinationPolicy(security ?? new SecurityOptions()),
            resolver ?? new DnsHostResolver());
    }
}