using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class RetryAfterHeaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 7, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToDelay_GivenNoValue_ReturnsNull(string? headerValue)
    {
        RetryAfterHeader.ToDelay(headerValue, Now).ShouldBeNull();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("120", 120)]
    [InlineData("   120", 120)]
    [InlineData("86400", 86400)]
    public void ToDelay_GivenDelaySeconds_ReturnsThatDelay(string headerValue, int expectedSeconds)
    {
        RetryAfterHeader.ToDelay(headerValue, Now).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("1 20")]
    [InlineData("1,200")]
    [InlineData("soon")]
    [InlineData("120s")]
    public void ToDelay_GivenAMalformedValue_ReturnsNull(string headerValue)
    {
        RetryAfterHeader.ToDelay(headerValue, Now).ShouldBeNull();
    }

    [Fact]
    public void ToDelay_GivenAnHttpDateInTheFuture_ReturnsTheDifference()
    {
        RetryAfterHeader.ToDelay("Tue, 27 Oct 2026 07:00:00 GMT", Now).ShouldBe(TimeSpan.FromDays(6));
    }

    [Fact]
    public void ToDelay_GivenAnHttpDateInThePast_ReturnsZero()
    {
        RetryAfterHeader.ToDelay("Fri, 07 Aug 2026 07:00:00 GMT", Now).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ToDelay_GivenAnHttpDateEquealToNow_ReturnsZero()
    {
        RetryAfterHeader.ToDelay("Wed, 21 Oct 2026 07:00:00 GMT", Now).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ToDelay_GivenAnHttpDate_IsRelativeToTheSuppliedClock()
    {
        const string header = "Wed, 21 Oct 2026 08:00:00 GMT";

        RetryAfterHeader.ToDelay(header, Now).ShouldBe(TimeSpan.FromHours(1));
        RetryAfterHeader.ToDelay(header, Now.AddMinutes(30)).ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData("Wed, 21 Oct 2026 07:28:00 gmt")]
    [InlineData("21 Oct 2026 07:28:00 GMT")]
    [InlineData("Wednesday, 21-Oct-26 07:28:00 GMT")]
    [InlineData("Wed Oct 21 07:28:00 2026")]
    public void ToDelay_GivenAnUnsupportedDateForm_ReturnsNull(string headerValue)
    {
        RetryAfterHeader.ToDelay(headerValue, Now).ShouldBeNull();
    }

    [Fact]
    public void ToDelay_GivenALargeDelay_SaturatesInsteadOfThrowing()
    {
        RetryAfterHeader.ToDelay("1000000000000000000", Now).ShouldBe(TimeSpan.MaxValue);
    }

    [Fact]
    public void ToDelay_GivenMoreDigitsThanALongCanHold_ReturnsNull()
    {
        RetryAfterHeader.ToDelay(new string('9', 40), Now).ShouldBeNull();
    }
}