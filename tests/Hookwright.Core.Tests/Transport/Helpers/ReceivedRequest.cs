using Microsoft.AspNetCore.Http;

namespace Hookwright.Core.Tests.Transport.Helpers;

internal sealed class ReceivedRequest
{
    private ReceivedRequest(IReadOnlyDictionary<string, string> headers, ReadOnlyMemory<byte> body)
    {
        Headers = headers;
        Body = body;
    }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public static async Task<ReceivedRequest> ReadAsync(HttpRequest request)
    {
        using MemoryStream body = new();
        await request.Body.CopyToAsync(body);

        return new ReceivedRequest(
            request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase),
            body.ToArray());
    }
}