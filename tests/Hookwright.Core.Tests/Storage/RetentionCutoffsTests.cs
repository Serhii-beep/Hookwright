using Hookwright.Core.Configuration;
using Hookwright.Core.Storage;

namespace Hookwright.Core.Tests.Storage;

public sealed class RetentionCutoffsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_Always_SubtractsEachWindowFromNow()
    {
        RetentionOptions options = new()
        {
            Attempts = TimeSpan.FromDays(7),
            Events = TimeSpan.FromDays(30),
            DeadDeliveries = TimeSpan.FromDays(90)
        };

        RetentionCutoffs cutoffs = RetentionCutoffs.From(options, Now);

        cutoffs.AttemptsBefore.ShouldBe(Now.AddDays(-7));
        cutoffs.DeliveriesBefore.ShouldBe(Now.AddDays(-30));
        cutoffs.DeadDeliveriesBefore.ShouldBe(Now.AddDays(-90));
    }

    [Fact]
    public void From_GivenNoOptions_Throws()
    {
        Should.Throw<ArgumentNullException>(() => RetentionCutoffs.From(null!, Now));
    }
}