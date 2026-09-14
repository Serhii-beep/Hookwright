using Hookwright.Core.Configuration;

namespace Hookwright.Core.Storage;

/// <summary>
/// The instants before which history has aged out, derived from <see cref="RetentionOptions"/>
/// at a given moment.
/// </summary>
public sealed class RetentionCutoffs
{
    private RetentionCutoffs(
        DateTimeOffset attemptsBefore,
        DateTimeOffset deliveriesBefore,
        DateTimeOffset deadDeliveriesBefore)
    {
        AttemptsBefore = attemptsBefore;
        DeliveriesBefore = deliveriesBefore;
        DeadDeliveriesBefore = deadDeliveriesBefore;
    }

    /// <summary>
    /// Attempt records made before this instant have aged out.
    /// </summary>
    public DateTimeOffset AttemptsBefore { get; }

    /// <summary>
    /// Deliveries that finished before this instant have aged out, and with them any event
    /// that has no deliveries left.
    /// </summary>
    public DateTimeOffset DeliveriesBefore { get; }

    /// <summary>
    /// Dead deliveries are kept longer. Only those that died before this instant have aged out.
    /// </summary>
    public DateTimeOffset DeadDeliveriesBefore { get; }

    /// <summary>
    /// Derives the cutoffs from the configured windows.
    /// </summary>
    /// <param name="retention">The configured windows.</param>
    /// <param name="now">The current instant.</param>
    public static RetentionCutoffs From(RetentionOptions retention, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(retention);

        return new RetentionCutoffs(
            now - retention.Attempts,
            now - retention.Events,
            now - retention.DeadDeliveries);
    }
}