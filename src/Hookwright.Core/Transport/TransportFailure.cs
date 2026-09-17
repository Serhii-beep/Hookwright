using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Transport;

/// <summary>
/// Classifies what went wrong when no response arrived.
/// </summary>
internal static class TransportFailure
{
    /// <summary>
    /// Maps an exception the transport caught to the outcome.
    /// </summary>
    /// <param name="exception">What the transport caught.</param>
    /// <param name="timeout">The request timeout in force.</param>
    /// <returns>
    /// The outcome and its detail, or <see langword="null"/> when the exception is not a delivery
    /// failure at all.
    /// </returns>
    internal static (AttemptOutcome Outcome, string Detail)? Classify(Exception exception, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            DeliveryBlockedException blocked => (AttemptOutcome.Blocked, blocked.Message),

            TaskCanceledException { InnerException: TimeoutException } =>
                (AttemptOutcome.TimedOut, $"No response within {timeout}."),

            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } failure =>
                (AttemptOutcome.ConnectionFailed, $"Name resolution failed: {failure.Message}"),

            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } failure =>
                (AttemptOutcome.ConnectionFailed, $"TLS handshake failed: {failure.Message}"),

            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } failure =>
                (AttemptOutcome.ConnectionFailed, $"Connection failed: {failure.Message}"),

            HttpIOException failure => (AttemptOutcome.ConnectionFailed, $"Connection lost mid-response ({failure.HttpRequestError}): {failure.Message}"),

            HttpRequestException failure => (AttemptOutcome.ConnectionFailed, $"{failure.HttpRequestError}: {failure.Message}"),

            _ => null
        };
    }
}