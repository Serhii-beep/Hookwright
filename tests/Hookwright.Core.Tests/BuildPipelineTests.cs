namespace Hookwright.Core.Tests;

public sealed class BuildPipelineTests
{
    [Fact]
    public void TestAssembly_RunsOnTargetedRuntime()
    {
        Assert.Equal(10, Environment.Version.Major);
    }
}