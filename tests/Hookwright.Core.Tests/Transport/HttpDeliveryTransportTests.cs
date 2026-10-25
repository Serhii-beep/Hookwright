using System.Net;
using System.Net.Sockets;
using System.Text;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Tests.Transport.Helpers;
using Hookwright.Core.Transport;
using Hookwright.Signing;

using Microsoft.AspNetCore.Http;

namespace Hookwright.Core.Tests.Transport;

public sealed class HttpDeliveryTransportTests : IDisposable
{
    private const string Worker = "web-01:4242:a1b2c3";

    private const string Payload = """{"orderId":"ord_42", "total":1999}""";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly HmacSha256Signer Signer = new();

    private static readonly PlaintextSecretProtector Protector = new();

    private readonly SocketsHttpHandler _handler = DeliveryHttpHandler.Create(new DeliveryOptions(), Guards.Development());

    [Fact]
    public async Task SendAsync_Always_SignsTheExactBytesItSends()
    {
        WebhookSecret secret = WebhookSecret.Generate();
        ReceivedRequest? received = null;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            received = await ReceivedRequest.ReadAsync(context.Request);
        });

        using HttpDeliveryTransport transport = Transport();
        ClaimedDelivery delivery = Delivery(endpoint.Url, secret);

        DeliveryResult result = await transport.SendAsync(delivery, Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        received.ShouldNotBeNull();
        received.Body.ToArray().ShouldBe(Encoding.UTF8.GetBytes(Payload));
        received.Headers["webhook-id"].ShouldBe(delivery.EventId.ToString());
        received.Headers["content-type"].ShouldBe("application/json");
        received.Headers["user-agent"].ShouldStartWith("Hookwright/");

        new WebhookVerifier(Signer).Verify(received.Headers, received.Body.Span, [secret], Now)
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task SendAsync_GivenTwoActiveKeys_SignsWithBoth()
    {
        WebhookSecret current = WebhookSecret.Generate();
        WebhookSecret next = WebhookSecret.Generate();
        ReceivedRequest? received = null;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            received = await ReceivedRequest.ReadAsync(context.Request);
        });

        using HttpDeliveryTransport transport = Transport();

        await transport.SendAsync(Delivery(endpoint.Url, current, next), Worker, Now, TestContext.Current.CancellationToken);

        received.ShouldNotBeNull();
        received.Headers["webhook-signature"].Split(' ').Length.ShouldBe(2);

        WebhookVerifier verifier = new(Signer);
        verifier.Verify(received.Headers, received.Body.Span, [current], Now).IsValid.ShouldBeTrue();
        verifier.Verify(received.Headers, received.Body.Span, [next], Now).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task SendAsync_GivenCustomHeaders_ForwardsThemButNeverLetsThemReplaceItsOwn()
    {
        WebhookSecret secret = WebhookSecret.Generate();
        ReceivedRequest? received = null;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            received = await ReceivedRequest.ReadAsync(context.Request);
        });

        using HttpDeliveryTransport transport = Transport();

        Dictionary<string, string> headers = new()
        {
            ["x-tenant"] = "test",
            ["content-language"] = "de",
            ["webhook-signature"] = "v1,forged"
        };

        await transport.SendAsync(Delivery(endpoint.Url, headers, secret), Worker, Now, TestContext.Current.CancellationToken);

        received.ShouldNotBeNull();
        received.Headers["x-tenant"].ShouldBe("test");
        received.Headers["content-language"].ShouldBe("de");
        received.Headers["webhook-signature"].ShouldNotContain("forged");

        new WebhookVerifier(Signer).Verify(received.Headers, received.Body.Span, [secret], Now).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(200, AttemptOutcome.Succeeded)]
    [InlineData(202, AttemptOutcome.Succeeded)]
    [InlineData(400, AttemptOutcome.Rejected)]
    [InlineData(410, AttemptOutcome.Gone)]
    [InlineData(429, AttemptOutcome.Throttled)]
    [InlineData(500, AttemptOutcome.ServerError)]
    public async Task SendAsync_GivenAResponse_ClassifiesItByStatus(int status, AttemptOutcome outcome)
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            context.Response.StatusCode = status;
            await context.Response.WriteAsync("test reason");
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(outcome);
        result.Attempt.ResponseStatusCode.ShouldBe(status);
        result.Attempt.ResponseBodySnippet.ShouldBe("test reason");
        result.Attempt.ErrorDetail.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenRetryAfter_ReportsTheRequestedDelay()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(context =>
        {
            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = "120";

            return Task.CompletedTask;
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Throttled);
        result.RequestedDelay.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task SendAsync_Always_RecordsOnlyTheAllowListedHeaders()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(context =>
        {
            context.Response.Headers.SetCookie = "session=a-consumer-secret";
            context.Response.Headers["X-Request-Id"] = "req_1";
            context.Response.Headers["X-Internal-Trace"] = "pod-7";

            return Task.CompletedTask;
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.ResponseHeaders["x-request-id"].ShouldBe("req_1");
        result.Attempt.ResponseHeaders.ShouldNotContainKey("set-cookie");
        result.Attempt.ResponseHeaders.ShouldNotContainKey("x-internal-trace");
    }

    [Fact]
    public async Task SendAsync_GivenASnippetLengthOfZero_RecordsNoBody()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            await context.Response.WriteAsync("test data");
        });

        using HttpDeliveryTransport transport = Transport(retentionOptions: new RetentionOptions { ResponseBodySnippetLength = 0 });

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        result.Attempt.ResponseBodySnippet.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenASnippetLength_RecordsNoMoreThanThat()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            await context.Response.WriteAsync("test data");
        });

        using HttpDeliveryTransport transport = Transport(retentionOptions: new RetentionOptions { ResponseBodySnippetLength = 5 });

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.ResponseBodySnippet.ShouldBe("test ");
    }

    [Fact]
    public async Task SendAsync_GivenNoResponseInTime_IsTimedOut()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        });

        using HttpDeliveryTransport transport = Transport(deliveryOptions: new DeliveryOptions { RequestTimeout = TimeSpan.FromMilliseconds(300) });

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.TimedOut);
        result.Attempt.Duration.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendAsync_GivenABodyStillArrivingAtTheDeadline_KeepsTheStatusAndWhatArrived()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            if (context.Request.Path == "/warm")
            {
                return;
            }

            await context.Response.WriteAsync("first part", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        });

        using HttpDeliveryTransport transport = Transport(deliveryOptions: new DeliveryOptions { RequestTimeout = TimeSpan.FromSeconds(1) });

        (await transport.SendAsync(Delivery(new Uri(endpoint.Url, "/warm"), WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken))
            .Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        result.Attempt.ResponseBodySnippet.ShouldBe("first part");
        result.Attempt.Duration.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendAsync_GivenABodyCutOff_KeepsTheStatusAndWhatArrived()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null);
            context.Response.ContentLength = 100;
            await context.Response.WriteAsync("first part");
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        result.Attempt.ResponseBodySnippet.ShouldBe("first part");
    }

    [Fact]
    public async Task SendAsync_GivenTheConnectionDroppedBeforeAResponse_IsConnectionFailed()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(context =>
        {
            context.Abort();

            return Task.CompletedTask;
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        result.Attempt.ResponseStatusCode.ShouldBeNull();
        result.Attempt.ErrorDetail.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenAHostThatDoesNotResolve_IsConnectionFailed()
    {
        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(new Uri("https://invalid"), WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        result.Attempt.ErrorDetail.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenNothingListening_IsConnectionFailed()
    {
        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(UnusedPort(), WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        result.Attempt.ErrorDetail.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenAServerThatDoesNotSpeakTls_IsConnectionFailed()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(_ => Task.CompletedTask);

        using HttpDeliveryTransport transport = Transport();
        Uri https = new UriBuilder(endpoint.Url) { Scheme = Uri.UriSchemeHttps }.Uri;

        DeliveryResult result = await transport.SendAsync(Delivery(https, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        result.Attempt.ErrorDetail.ShouldNotBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenAConnectRefusedByTheNetworkGuard_IsBlocked()
    {
        using SocketsHttpHandler guarded = DeliveryHttpHandler.Create(new DeliveryOptions(), Guards.Strict());

        using HttpDeliveryTransport transport = new(guarded, Signer, Protector, new DeliveryOptions(), new RetentionOptions());

        DeliveryResult result = await transport.SendAsync(Delivery(new Uri("https://169.254.169.254/"), WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Blocked);
        result.Attempt.ErrorDetail.ShouldBe("169.254.169.254 resolves to a cloud metadata address, which deliveries may not reach.");
    }

    [Fact]
    public async Task SendAsync_GivenAnErrorDetailLengthOfZero_RecordsNoDetail()
    {
        using HttpDeliveryTransport transport = Transport(retentionOptions: new RetentionOptions { ErrorDetailLength = 0 });

        DeliveryResult result = await transport.SendAsync(Delivery(UnusedPort(), WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        result.Attempt.ErrorDetail.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_GivenTheCallersCancellation_Throws()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        });

        using HttpDeliveryTransport transport = Transport();
        using CancellationTokenSource shutdown = new(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<TaskCanceledException>(() => transport.SendAsync(
            Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, shutdown.Token));
    }

    [Fact]
    public async Task SendAsync_GivenNoSigningKey_SendsNothing()
    {
        bool reached = false;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(_ =>
        {
            reached = true;

            return Task.CompletedTask;
        });

        using HttpDeliveryTransport transport = Transport();

        await Should.ThrowAsync<InvalidOperationException>(() => transport.SendAsync(
            Delivery(endpoint.Url), Worker, Now, TestContext.Current.CancellationToken));

        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_GivenAHundredMegabytesResponse_ReadsTheSnippetAndHangsUp()
    {
        const long Streamed = 100L * 1024 * 1024;
        long written = 0;
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            byte[] chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'x');

            try
            {
                while (Interlocked.Read(ref written) < Streamed)
                {
                    await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
                    Interlocked.Add(ref written, chunk.Length);
                }
            }
            catch (OperationCanceledException)
            {
                // The transport hung up, which is the point
            }
            catch (IOException)
            {
                // The same, noticed by the socket first
            }
            finally
            {
                finished.TrySetResult();
            }
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        result.Attempt.ResponseBodySnippet.ShouldBe(new string('x', DeliveryAttempt.MaxResponseBodySnippetLength));

        Interlocked.Read(ref written).ShouldBeLessThan(Streamed / 4);
    }

    [Fact]
    public async Task SendAsync_GivenMoreHeadersThanARecordKeeps_RecordsTheCeilingWithoutThrowing()
    {
        const int Returned = DeliveryAttempt.MaxResponseHeaders + 1;
        string[] names = [.. Enumerable.Range(0, Returned).Select(i => $"x-header-{i}")];

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(context =>
        {
            foreach (string name in names)
            {
                context.Response.Headers[name] = "value";
            }

            return Task.CompletedTask;
        });

        using HttpDeliveryTransport transport = Transport(retentionOptions: new RetentionOptions { ResponseHeaderAllowList = names });

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        result.Attempt.ResponseHeaders.Count.ShouldBe(DeliveryAttempt.MaxResponseHeaders);
    }

    [Fact]
    public async Task SendAsync_GivenAHeaderLongerThanARecordKeeps_RecordsItTruncated()
    {
        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(context =>
        {
            context.Response.Headers["X-Request-Id"] = new string('r', DeliveryAttempt.MaxResponseHeaderValueLength + 1);

            return Task.CompletedTask;
        });

        using HttpDeliveryTransport transport = Transport();

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url, WebhookSecret.Generate()), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.ResponseHeaders["x-request-id"]
            .ShouldBe(new string('r', DeliveryAttempt.MaxResponseHeaderValueLength));
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private static ClaimedDelivery Delivery(Uri url, params WebhookSecret[] secrets)
    {
        return Delivery(url, new Dictionary<string, string>(), secrets);
    }

    private static ClaimedDelivery Delivery(Uri url, IReadOnlyDictionary<string, string> headers, params WebhookSecret[] secrets)
    {
        WebhookEndpointId endpointId = WebhookEndpointId.New();

        return new ClaimedDelivery
        {
            DeliveryId = DeliveryId.New(),
            AttemptCount = 1,
            LeaseExpiresAt = Now.AddMinutes(1),
            EventId = WebhookEventId.New(),
            EventType = "order.created",
            Payload = Payload,
            Headers = headers,
            EndpointId = endpointId,
            Url = url,
            Secrets =
            [
                .. secrets.Select(secret => EndpointSecret.Create(
                    endpointId, Protector.Protect(secret), SignatureAlgorithm.HmacSha256, Now.AddDays(-1)))
            ]
        };
    }

    private static Uri UnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return new Uri($"http://127.0.0.1:{port}/");
    }

    private HttpDeliveryTransport Transport(DeliveryOptions? deliveryOptions = null, RetentionOptions? retentionOptions = null)
    {
        return new HttpDeliveryTransport(
            _handler, Signer, Protector, deliveryOptions ?? new DeliveryOptions(), retentionOptions ?? new RetentionOptions());
    }
}