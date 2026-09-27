using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Hookwright.Core.Tests.Transport.Helpers;

internal sealed class LoopbackEndpoint : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackEndpoint(WebApplication app, Uri url)
    {
        _app = app;
        Url = url;
    }

    public Uri Url { get; }

    public static async Task<LoopbackEndpoint> StartAsync(RequestDelegate respond)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        WebApplication app = builder.Build();
        app.Run(respond);
        await app.StartAsync();

        return new LoopbackEndpoint(app, new Uri(app.Urls.Single()));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}