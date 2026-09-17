using System.Net;

using Hookwright.Core.Configuration;
using Hookwright.Core.Transport;

namespace Hookwright.Core.Tests.Transport;

public sealed class DeliveryHttpHandlerTests
{
    [Fact]
    public void Create_Always_ConfiguresTheHandlerForStarngersServers()
    {
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(new DeliveryOptions { MaxConnectionsPerEndpoint = 3 });

        handler.MaxConnectionsPerServer.ShouldBe(3);
        handler.AllowAutoRedirect.ShouldBeFalse();
        handler.UseCookies.ShouldBeFalse();
        handler.AutomaticDecompression.ShouldBe(DecompressionMethods.All);
        handler.PooledConnectionLifetime.ShouldBe(DeliveryHttpHandler.ConnectionLifetime);
        handler.PooledConnectionIdleTimeout.ShouldBe(DeliveryHttpHandler.ConnectionIdleTimeout);
    }

    [Fact]
    public void Create_GivenNoOptions_Throws()
    {
        Should.Throw<ArgumentNullException>(() => DeliveryHttpHandler.Create(null!));
    }
}