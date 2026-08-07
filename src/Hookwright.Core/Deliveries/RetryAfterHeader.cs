using System.Globalization;

namespace Hookwright.Core.Deliveries;

/// <summary>
/// Reads the HTTP <c>Retry-After</c> header, by which a consumer
/// asks for a specific delay before the next attempt.
/// </summary>
/// <remarks>
/// Takes a raw header value rather than an HTTP type.
/// </remarks>
public static class RetryAfterHeader
{
    /// <summary>
    /// The largest whole number of seconds a <see cref="TimeSpan"/> can represent.
    /// </summary>
    public static readonly long MaxRepresentableSeconds = TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond;

    /// <summary>
    /// Converts a <c>Retry-After</c> value into a delay measured from <paramref name="now"/>.
    /// </summary>
    /// <param name="headerValue">
    /// The raw value: either delay-seconds (<c>100</c>) or an HTTP-date
    /// (<c>Fri, 27 Aug 2026 07:49:00 GMT)</c>, per RFC 9110.
    /// </param>
    /// <param name="now">The current instant, from the caller's <see cref="TimeProvider"/>.</param>
    /// <returns>
    /// The requested delay, or <see langword="null"/> when the value is absent or malformed, in
    /// which case the caller fallse back to its own schedule.
    /// </returns>
    public static TimeSpan? ToDelay(string? headerValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        string value = headerValue.Trim();

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            return seconds > MaxRepresentableSeconds
                ? TimeSpan.MaxValue
                : TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParseExact(value, "R", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset when))
        {
            TimeSpan delay = when - now;

            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}