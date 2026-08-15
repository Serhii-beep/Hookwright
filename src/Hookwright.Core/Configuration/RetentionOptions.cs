using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Configuration;

/// <summary>
/// How long delivery history is kept, and how much of each response is recorded.
/// </summary>
public sealed class RetentionOptions
{
    /// <summary>
    /// How long events and their deliveries are kept. Default 30 days.
    /// </summary>
    public TimeSpan Events { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long individual attempt records are kept. Default 7 days.
    /// </summary>
    public TimeSpan Attempts { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How much of a consumer's response body to record. Default 4 KB.
    /// <c>0</c> records none.
    /// </summary>
    public int ResponseBodySnippetLength { get; set; } = DeliveryAttempt.MaxResponseBodySnippetLength;

    /// <summary>
    /// How much transport-failure detail to record. Default 1 KB.
    /// <c>0</c> records none.
    /// </summary>
    public int ErrorDetailLength { get; set; } = DeliveryAttempt.MaxErrorDetailLength;

    /// <summary>
    /// Collects every configuration problem.
    /// </summary>
    /// <returns>The problems found, empty when the configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        OptionsValidator validator = new();

        validator.Positive(Events, nameof(Events));
        validator.Positive(Attempts, nameof(Attempts));
        validator.AtLeast(ResponseBodySnippetLength, 0, nameof(ResponseBodySnippetLength));
        validator.AtLeast(ErrorDetailLength, 0, nameof(ErrorDetailLength));

        validator.Require(
            Attempts <= Events,
            $"{nameof(Attempts)} ({Attempts}) must not exceed {nameof(Events)} ({Events}).");

        validator.Require(
            ResponseBodySnippetLength <= DeliveryAttempt.MaxResponseBodySnippetLength,
            $"{nameof(ResponseBodySnippetLength)} ({ResponseBodySnippetLength}) must not exceed " +
            $"{DeliveryAttempt.MaxResponseBodySnippetLength}, the structural maximum the attempt " +
            "record enforces.");

        validator.Require(
            ErrorDetailLength <= DeliveryAttempt.MaxErrorDetailLength,
            $"{nameof(ErrorDetailLength)} ({ErrorDetailLength}) must not exceed " +
            $"{DeliveryAttempt.MaxErrorDetailLength}, the structural maximum the attemp record enforces.");

        return validator.Errors;
    }
}