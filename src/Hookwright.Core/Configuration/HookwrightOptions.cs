namespace Hookwright.Core.Configuration;

/// <summary>
/// Everything Hookwright can be configured with.
/// </summary>
public sealed class HookwrightOptions
{
    /// <summary>
    /// How the dispatcher claims work and makes each delivery.
    /// </summary>
    public DeliveryOptions Delivery { get; } = new();

    /// <summary>
    /// How failed deliveries are rescheduled.
    /// </summary>
    public RetryOptions Retry { get; } = new();

    /// <summary>
    /// What is accepted at publish time, and where deliveries may go.
    /// </summary>
    public SecurityOptions Security { get; } = new();

    /// <summary>
    /// How long history is kept and how much of each response is recorded.
    /// </summary>
    public RetentionOptions Retention { get; } = new();

    /// <summary>
    /// Collects every problem across every section, each prefixed with its section name.
    /// </summary>
    /// <returns>The problems found, empty when the configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        Collect(problems, nameof(Delivery), Delivery.Validate());
        Collect(problems, nameof(Retry), Retry.Validate());
        Collect(problems, nameof(Security), Security.Validate());
        Collect(problems, nameof(Retention), Retention.Validate());

        return problems;
    }

    private static void Collect(List<string> problems, string section, IReadOnlyList<string> found)
    {
        foreach (string problem in found)
        {
            problems.Add($"{section}: {problem}");
        }
    }
}