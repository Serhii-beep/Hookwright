using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class WorkerIdentityTests
{
    [Fact]
    public void Create_Always_NamesTheMachineTheProcessAndARandomTail()
    {
        string[] parts = WorkerIdentity.Create("web-01", 4242).Split(':');

        parts.Length.ShouldBe(3);
        parts[0].ShouldBe("web-01");
        parts[1].ShouldBe("4242");
        parts[2].Length.ShouldBe(6);
        parts[2].ShouldAllBe(character => char.IsAsciiHexDigitLower(character));
    }

    [Fact]
    public void Create_Always_TellsTwoProcessesWithTheSamePidApart()
    {
        WorkerIdentity.Create("web-01", 4242).ShouldNotBe(WorkerIdentity.Create("web-01", 4242));
    }

    [Fact]
    public void Create_GivenTheCurrentProcess_UsesItsMachineAndPid()
    {
        WorkerIdentity.Create().ShouldStartWith($"{Environment.MachineName}:{Environment.ProcessId}:");
    }

    [Fact]
    public void Create_GivenAnOverlongMachineName_FitsTheColumnAndKeepsTheTail()
    {
        string identity = WorkerIdentity.Create(new string('m', 300), 4242);

        identity.Length.ShouldBe(WorkerIdentity.MaxLength);
        identity.Split(':')[1].ShouldBe("4242");
    }

    [Fact]
    public void Create_GivenAColonInTheMachineName_KeepsTheSeparatorsUnambiguous()
    {
        WorkerIdentity.Create("fe80::1", 7).Split(':').Length.ShouldBe(3);
    }
}