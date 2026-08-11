using Hookwright.Core.Events;

namespace Hookwright.Core.Tests.Events;

public sealed class EventTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("order.created")]
    [InlineData("order")]
    [InlineData("a.b.c.d.e")]
    [InlineData("user_account.password_reset")]
    [InlineData("v2.order.Created")]
    [InlineData("order.created2")]
    public void IsValidName_GivenWellFormedName_ReturnsTrue(string name)
    {
        EventType.IsValidName(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".order")]
    [InlineData("order.")]
    [InlineData("order..created")]
    [InlineData("order created")]
    [InlineData("order-created")]
    [InlineData("order/created")]
    [InlineData("order.créated")]
    [InlineData("order.created!")]
    public void IsValidName_GivenMalformedName_ReturnsFalse(string? name)
    {
        EventType.IsValidName(name).ShouldBeFalse();
    }

    [Fact]
    public void IsValidName_GivenOverlongName_ReturnsFalse()
    {
        EventType.IsValidName(new string('a', EventType.MaxNameLength + 1)).ShouldBeFalse();
    }

    [Fact]
    public void Create_GivenValidInput_InitialisesAnActiveType()
    {
        EventType type = EventType.Create("order.created", "An order was placed", Now);

        type.Name.ShouldBe("order.created");
        type.Description.ShouldBe("An order was placed");
        type.SchemaJson.ShouldBeNull();
        type.CreatedAt.ShouldBe(Now);
        type.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void Create_GivenMalformedName_Throws()
    {
        Should.Throw<ArgumentException>(() => EventType.Create("order..created", null, Now));
    }

    [Fact]
    public void Create_GivenOverlongDescription_Throws()
    {
        string description = new('a', EventType.MaxDescriptionLength + 1);

        Should.Throw<ArgumentException>(() => EventType.Create("order.created", description, Now));
    }

    [Fact]
    public void Archive_ThenRestore_RoundTrips()
    {
        EventType type = EventType.Create("order.created", null, Now);

        type.Archive();
        type.IsArchived.ShouldBeTrue();

        type.Restore();
        type.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void SetSchema_GivenBlankInput_ClearsTheSchema()
    {
        EventType type = EventType.Create("order.created", null, Now);
        type.SetSchema("""{"type": "object"}""");

        type.SetSchema("   ");

        type.SchemaJson.ShouldBeNull();
    }
}