using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Signing;

namespace Hookwright.Core.Transport;

/// <summary>
/// Delivers a webhook as one signed HTTP POST and records what came back.
/// </summary>
/// <remarks>
/// Sends through an <see cref="HttpMessageInvoker"/> over the handler it is given.
/// </remarks>
public sealed class HttpDeliveryTransport : IDeliveryTransport, IDisposable
{
    private static readonly ProductInfoHeaderValue UserAgent = new(
        "Hookwright", typeof(HttpDeliveryTransport).Assembly.GetName().Version?.ToString(3));

    private static readonly FrozenSet<string> OwnHeaders = FrozenSet.ToFrozenSet(
        [
            SignedWebhookHeaders.IdHeaderName,
            SignedWebhookHeaders.TimestampHeaderName,
            SignedWebhookHeaders.SignatureHeaderName,
            "content-type",
            "content-length",
            "user-agent"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly HttpMessageInvoker _invoker;
    private readonly IWebhookSigner _signer;
    private readonly ISecretProtector _protector;
    private readonly TimeSpan _timeout;
    private readonly int _snippetLength;
    private readonly int _errorDetailLength;
    private readonly FrozenSet<string> _retainedHeaders;

    /// <summary>
    /// Creates the transport.
    /// </summary>
    /// <param name="handler">
    /// The handler requests go through, normally from <see cref="DeliveryHttpHandler.Create"/>.
    /// </param>
    /// <param name="signer">Signs each request.</param>
    /// <param name="protector">Turns each stored key back into one that can sign.</param>
    /// <param name="deliveryOptions">Delivery options.</param>
    /// <param name="retentionOptions">Retention options.</param>
    public HttpDeliveryTransport(
        HttpMessageHandler handler,
        IWebhookSigner signer,
        ISecretProtector protector,
        DeliveryOptions deliveryOptions,
        RetentionOptions retentionOptions)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(deliveryOptions);
        ArgumentNullException.ThrowIfNull(retentionOptions);

        _invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        _signer = signer;
        _protector = protector;
        _timeout = deliveryOptions.RequestTimeout;
        _snippetLength = retentionOptions.ResponseBodySnippetLength;
        _errorDetailLength = retentionOptions.ErrorDetailLength;
        _retainedHeaders = retentionOptions.ResponseHeaderAllowList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The endpoint has no active keys this transport can sign with.
    /// Nothing was sent.
    /// </exception>
    public async Task<DeliveryResult> SendAsync(
        ClaimedDelivery delivery,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        using HttpRequestMessage request = CreateRequest(delivery, now);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        long started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;

        try
        {
            response = await _invoker.SendAsync(request, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(delivery.DeliveryId, AttemptOutcome.TimedOut, $"No response within {_timeout}.", now, started, workerId);
        }
        catch (HttpRequestException exception)
        {
            (AttemptOutcome outcome, string detail) = TransportFailure.Classify(exception);

            return Failed(delivery.DeliveryId, outcome, detail, now, started, workerId);
        }

        using (response)
        {
            string? snippet = await ReadSnippetAsync(response.Content, deadline.Token, cancellationToken).ConfigureAwait(false);
            int status = (int)response.StatusCode;

            DeliveryAttempt attempt = DeliveryAttempt.FromResponse(
                delivery.DeliveryId,
                AttemptOutcomes.FromStatusCode(status),
                status,
                snippet,
                RetainedHeaders(response),
                now,
                Stopwatch.GetElapsedTime(started),
                workerId);

            return new DeliveryResult(attempt, RetryAfterHeader.ToDelay(RawHeader(response, RetryAfterHeader.Name), now));
        }
    }

    /// <summary>
    /// Releases the invoker. The handler, which the caller owns, is left alone.
    /// </summary>
    public void Dispose()
    {
        _invoker.Dispose();
    }

    private static string? RawHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.NonValidated.TryGetValues(name, out HeaderStringValues values) ? values.ToString() : null;
    }

    private static string? Limit(string text, int length)
    {
        if (length == 0)
        {
            return null;
        }

        return text.Length <= length ? text : text[..length];
    }

    private static async Task<int> ReadAtMostAsync(
        HttpContent content,
        Memory<byte> buffer,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        int read = 0;

        try
        {
            Stream body = await content.ReadAsStreamAsync(deadline).ConfigureAwait(false);

            while (read < buffer.Length)
            {
                int count = await body.ReadAsync(buffer[read..], deadline).ConfigureAwait(false);

                if (count == 0)
                {
                    break;
                }

                read += count;
            }
        }
        catch (IOException)
        {
            // Cut off mid-body
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Still arriving at the deadline
        }

        return read;
    }

    private HttpRequestMessage CreateRequest(ClaimedDelivery delivery, DateTimeOffset now)
    {
        byte[] body = Encoding.UTF8.GetBytes(delivery.Payload);

        SignedWebhookHeaders signed = SignedWebhookHeaders.Create(
            _signer, delivery.EventId.ToString(), now, body, SigningKeys(delivery));

        HttpRequestMessage request = new(HttpMethod.Post, delivery.Url)
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        foreach ((string name, string value) in delivery.Headers)
        {
            if (!OwnHeaders.Contains(name) && !request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        request.Headers.Add(SignedWebhookHeaders.IdHeaderName, signed.Id);
        request.Headers.Add(SignedWebhookHeaders.TimestampHeaderName, signed.Timestamp);
        request.Headers.Add(SignedWebhookHeaders.SignatureHeaderName, signed.Signature);
        request.Headers.UserAgent.Add(UserAgent);

        return request;
    }

    private WebhookSecret[] SigningKeys(ClaimedDelivery delivery)
    {
        WebhookSecret[] keys =
        [
            .. delivery.Secrets
               .Where(secret => secret.Algorithm == _signer.Algorithm)
               .Select(secret => _protector.Unprotect(secret.ProtectedKey))
        ];

        return keys.Length > 0
            ? keys
            : throw new InvalidOperationException(
                $"Endpoint {delivery.EndpointId} has no active {_signer.Algorithm} signing key, so delivery {delivery.DeliveryId} cannot be sent.");
    }

    private async Task<string?> ReadSnippetAsync(
        HttpContent content,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        if (_snippetLength == 0)
        {
            return null;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(_snippetLength);

        try
        {
            int read = await ReadAtMostAsync(content, buffer.AsMemory(0, _snippetLength), deadline, cancellationToken)
                .ConfigureAwait(false);

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private Dictionary<string, string> RetainedHeaders(HttpResponseMessage response)
    {
        Dictionary<string, string> retained = new(StringComparer.OrdinalIgnoreCase);

        Retain(response.Headers.NonValidated, retained);
        Retain(response.Content.Headers.NonValidated, retained);

        return retained;
    }

    private void Retain(HttpHeadersNonValidated headers, Dictionary<string, string> retained)
    {
        foreach ((string name, HeaderStringValues values) in headers)
        {
            if (_retainedHeaders.Contains(name))
            {
                retained[name] = values.ToString();
            }
        }
    }

    private DeliveryResult Failed(
        DeliveryId deliveryId,
        AttemptOutcome outcome,
        string detail,
        DateTimeOffset now,
        long started,
        string workerId)
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromFailure(
            deliveryId, outcome, Limit(detail, _errorDetailLength), now, Stopwatch.GetElapsedTime(started), workerId);

        return new DeliveryResult(attempt, requestedDelay: null);
    }
}