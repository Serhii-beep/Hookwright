using Hookwright.Core.Entities;

namespace Hookwright.Core.Tests.Entities;

public sealed class SubscriberTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_GivenValidInput_InitialisesAnEnabledSubscriber()
    {
        Subscriber subscriber = Subscriber.Create("org_4f2a", "Acme", Now);

        subscriber.Id.Value.ShouldNotBe(Guid.Empty);
        subscriber.ExternalId.ShouldBe("org_4f2a");
        subscriber.Name.ShouldBe("Acme");
        subscriber.CreatedAt.ShouldBe(Now);
        subscriber.DisabledAt.ShouldBeNull();
        subscriber.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Create_GivenPaddedExternalId_TrimsIt()
    {
        Subscriber.Create("  org_4f2a  ", null, Now).ExternalId.ShouldBe("org_4f2a");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_GivenMissingExternalId_Throws(string? externalId)
    {
        Should.Throw<ArgumentException>(() => Subscriber.Create(externalId!, null, Now));
    }

    [Fact]
    public void Create_GivenOverlongExternalId_Throws()
    {
        string externalId = new('a', Subscriber.MaxExternalIdLength + 1);

        Should.Throw<ArgumentException>(() => Subscriber.Create(externalId, null, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_GivenBlankName_StoresNull(string? name)
    {
        Subscriber.Create("org_4f2a", name, Now).Name.ShouldBeNull();
    }

    [Fact]
    public void Disable_GivenAnAlreadyDisabledSubscriber_KeepsTheOriginalTimestamp()
    {
        Subscriber subscriber = Subscriber.Create("org_4f2a", null, Now);

        subscriber.Disable(Now.AddHours(1));
        subscriber.Disable(Now.AddHours(5));

        subscriber.DisabledAt.ShouldBe(Now.AddHours(1));
        subscriber.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Enable_GivenADisabledSubscriber_ClearsTheTimestamp()
    {
        Subscriber subscriber = Subscriber.Create("org_4f2a", null, Now);
        subscriber.Disable(Now);

        subscriber.Enable();

        subscriber.DisabledAt.ShouldBeNull();
        subscriber.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Rename_GivenAName_ReplacesIt()
    {
        Subscriber subscriber = Subscriber.Create("org_4f2a", "Acme", Now);

        subscriber.Rename("Acme Inc.");

        subscriber.Name.ShouldBe("Acme Inc.");
    }
}