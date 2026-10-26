using Hookwright.Dispatch;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

public sealed class ClaimPacingTests
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(1);

    [Fact]
    public void Next_GivenAFullBatch_IsImmediate()
    {
        new ClaimPacing(Ceiling).Next(claimed: 10, batchSize: 10).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Next_GivenAPartialBatch_IsTheFloor()
    {
        new ClaimPacing(Ceiling).Next(claimed: 3, batchSize: 10).ShouldBe(ClaimPacing.Floor);
    }

    [Fact]
    public void Next_GivenNothing_DoublesFromTheFloorToTheCeilingAndStaysThere()
    {
        ClaimPacing pacing = new(Ceiling);

        TimeSpan[] pauses = [.. Enumerable.Range(0, 6).Select(_ => pacing.Next(claimed: 0, batchSize: 10))];

        pauses.ShouldBe(
        [
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(800),
            TimeSpan.FromMilliseconds(1000),
            TimeSpan.FromMilliseconds(1000)
        ]);
    }

    [Fact]
    public void Next_GivenWorkAfterIdleness_StartsOverFromTheFloor()
    {
        ClaimPacing pacing = new(Ceiling);
        pacing.Next(claimed: 0, batchSize: 10);
        pacing.Next(claimed: 0, batchSize: 10);
        pacing.Next(claimed: 0, batchSize: 10);

        pacing.Next(claimed: 1, batchSize: 10).ShouldBe(ClaimPacing.Floor);
        pacing.Next(claimed: 0, batchSize: 10).ShouldBe(ClaimPacing.Floor);
        pacing.Next(claimed: 0, batchSize: 10).ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Reset_Always_ForgetsTheBackoff()
    {
        ClaimPacing pacing = new(Ceiling);
        pacing.Next(claimed: 0, batchSize: 10);
        pacing.Next(claimed: 0, batchSize: 10);

        pacing.Reset();

        pacing.Next(claimed: 0, batchSize: 10).ShouldBe(ClaimPacing.Floor);
    }

    [Fact]
    public void Constructor_GivenACeilingBelowTheFloor_PausesTheCeiling()
    {
        ClaimPacing pacing = new(TimeSpan.FromMilliseconds(50));

        pacing.Next(claimed: 1, batchSize: 10).ShouldBe(TimeSpan.FromMilliseconds(50));
        pacing.Next(claimed: 0, batchSize: 10).ShouldBe(TimeSpan.FromMilliseconds(50));
        pacing.Next(claimed: 0, batchSize: 10).ShouldBe(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void Constructor_GivenNoCeiling_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ClaimPacing(TimeSpan.Zero));
    }
}