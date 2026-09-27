using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Transport;

/// <summary>
/// Classifies a request that failed before any response arrived.
/// </summary>
internal static class TransportFailure
{
    /// <summary>
    /// Maps a failed request to the outcome to record and a line saying why.
    /// </summary>
    /// <param name="exception">What the handler threw.</param>
    /// <returns>
    /// The outcome and its detail.
    /// </returns>
    internal static (AttemptOutcome Outcome, string Detail) Classify(HttpRequestException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string cause = exception.GetBaseException().Message;

        return exception switch
        {
            { InnerException: DeliveryBlockedException blocked } =>
                (AttemptOutcome.Blocked, blocked.Message),

            { HttpRequestError: HttpRequestError.NameResolutionError } =>
                (AttemptOutcome.ConnectionFailed, $"Name resolution failed: {cause}"),

            { HttpRequestError: HttpRequestError.SecureConnectionError } =>
                (AttemptOutcome.ConnectionFailed, $"TLS handshake failed: {cause}"),

            { HttpRequestError: HttpRequestError.ConnectionError } =>
                (AttemptOutcome.ConnectionFailed, $"Connection failed: {cause}"),

            { HttpRequestError: HttpRequestError.ResponseEnded } or { InnerException: IOException } =>
                (AttemptOutcome.ConnectionFailed, $"Connection lost before a response: {cause}"),

            _ => (AttemptOutcome.ConnectionFailed, $"{exception.HttpRequestError}: {cause}")
        };
    }
}