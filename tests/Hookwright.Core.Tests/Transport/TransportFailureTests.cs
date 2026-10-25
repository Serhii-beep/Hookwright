using System.Net.Sockets;
using System.Security.Authentication;

using Hookwright.Core.Deliveries;
using Hookwright.Core.Transport;

namespace Hookwright.Core.Tests.Transport;

public sealed class TransportFailureTests
{
    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "Name resolution failed")]
    [InlineData(HttpRequestError.SecureConnectionError, "TLS handshake failed")]
    [InlineData(HttpRequestError.ConnectionError, "Connection failed")]
    [InlineData(HttpRequestError.ResponseEnded, "Connection lost before a response")]
    [InlineData(HttpRequestError.InvalidResponse, "InvalidResponse")]
    public void Classify_GivenARequestError_IsConnectionFailedWithADistinctReason(HttpRequestError error, string reason)
    {
        (AttemptOutcome outcome, string detail) = TransportFailure.Classify(new HttpRequestException(error, "the socket said no"));

        outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        detail.ShouldBe($"{reason}: the socket said no");
    }

    [Fact]
    public void Classify_GivenAResetBeforeAResponse_NamesItAndItsRootCause()
    {
        SocketException reset = new((int)SocketError.ConnectionReset);
        HttpRequestException exception = new(
            HttpRequestError.Unknown,
            "An error occurred while sending the request.",
            new IOException("Unable to read data from the transport connection.", reset));

        (AttemptOutcome outcome, string detail) = TransportFailure.Classify(exception);

        outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        detail.ShouldBe($"Connection lost before a response: {reset.Message}");
    }

    [Fact]
    public void Classify_GivenAFailedHandshake_ReportsWhyRatherThanSeeInnerException()
    {
        HttpRequestException exception = new(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established, see inner exception.",
            new AuthenticationException("Cannot determine the frame size or a corrupted frame was received."));

        TransportFailure.Classify(exception).Detail
            .ShouldBe("TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.");
    }

    [Fact]
    public void Classify_GivenAConnectRefusedByTheNetworkGuard_IsBlockedWithItsReason()
    {
        HttpRequestException wrapped = new(
            HttpRequestError.ConnectionError,
            "169.254.169.254 is a cloud metadata address. (169.254.169.254:80)",
            new DeliveryBlockedException("169.254.169.254 is a cloud metadata address."));

        (AttemptOutcome outcome, string detail) = TransportFailure.Classify(wrapped);

        outcome.ShouldBe(AttemptOutcome.Blocked);
        detail.ShouldBe("169.254.169.254 is a cloud metadata address.");
    }

    [Fact]
    public void Classify_GivenALookupTheGuardCouldNotComplete_IsNameResolution()
    {
        HttpRequestException wrapped = new(
            HttpRequestError.ConnectionError,
            "No such host is known. (hookwright.invalid:443)",
            new SocketException((int)SocketError.HostNotFound));

        (AttemptOutcome outcome, string detail) = TransportFailure.Classify(wrapped);

        outcome.ShouldBe(AttemptOutcome.ConnectionFailed);
        detail.ShouldStartWith("Name resolution failed: ");
    }
}