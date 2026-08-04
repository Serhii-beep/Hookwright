using Hookwright.Core.Entities;
using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Entities;

public sealed class EndpointSecretTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static EndpointSecret CreateSecret(DateTimeOffset? validFrom = null) =>
        EndpointSecret.Create(
            WebhookEndpointId.New(),
            "CfDJ8-protected-blob",
            SignatureAlgorithm.HmacSha256,
            validFrom ?? Now);

    [Fact]
    public void Create_GivenValidInput_InitialisesAnActiveSecret()
    {
        EndpointSecret secret = CreateSecret();

        secret.Id.Value.ShouldNotBe(Guid.Empty);
        secret.Algorithm.ShouldBe(SignatureAlgorithm.HmacSha256);
        secret.ValidFrom.ShouldBe(Now);
        secret.ValidUntil.ShouldBeNull();
        secret.IsRetired.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_GivenMissingProtectedKey_Throws(string? protectedKey)
    {
        Should.Throw<ArgumentException>(() => EndpointSecret.Create(WebhookEndpointId.New(), protectedKey!, SignatureAlgorithm.HmacSha256, Now));
    }

    [Fact]
    public void Create_GivenOverlongProtectedKey_Throws()
    {
        string protectedKey = new('a', EndpointSecret.MaxProtectedKeyLength + 1);

        Should.Throw<ArgumentException>(() => EndpointSecret.Create(WebhookEndpointId.New(), protectedKey, SignatureAlgorithm.HmacSha256, Now));
    }

    [Fact]
    public void IsActiveAt_GivenAnInstantBeforeValidFrom_ReturnsFalse()
    {
        CreateSecret().IsActiveAt(Now.AddSeconds(-1)).ShouldBeFalse();
    }

    [Fact]
    public void IsActiveAt_GivenValidFromItself_ReturnsTrue()
    {
        CreateSecret().IsActiveAt(Now).ShouldBeTrue();
    }

    [Fact]
    public void IsActiveAt_GivenAnUnretiredSecret_ReturnsTrueIndefinitely()
    {
        CreateSecret().IsActiveAt(Now.AddYears(5)).ShouldBeTrue();
    }

    [Fact]
    public void IsActiveAt_DuringARotationWindow_ReturnsTrueForBothKeys()
    {
        EndpointSecret retiring = CreateSecret();
        retiring.Retire(Now.AddHours(24));

        EndpointSecret replacement = CreateSecret();

        DateTimeOffset midWindow = Now.AddHours(12);

        retiring.IsActiveAt(midWindow).ShouldBeTrue();
        replacement.IsActiveAt(midWindow).ShouldBeTrue();
    }

    [Fact]
    public void IsActiveAt_AtValidUntilExactly_ReturnsFalse()
    {
        EndpointSecret secret = CreateSecret();
        secret.Retire(Now.AddHours(24));

        secret.IsActiveAt(Now.AddHours(24)).ShouldBeFalse();
        secret.IsActiveAt(Now.AddHours(24).AddTicks(-1)).ShouldBeTrue();
    }

    [Fact]
    public void Retire_GivenALaterEnd_KeepsTheEarlierOne()
    {
        EndpointSecret secret = CreateSecret();
        secret.Retire(Now.AddHours(1));

        secret.Retire(Now.AddHours(48));

        secret.ValidUntil.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Retire_GivenAnEarlierEnd_BringsRetirementForward()
    {
        EndpointSecret secret = CreateSecret();
        secret.Retire(Now.AddHours(24));

        secret.Retire(Now.AddMinutes(5));

        secret.ValidUntil.ShouldBe(Now.AddMinutes(5));
        secret.IsActiveAt(Now.AddMinutes(10)).ShouldBeFalse();
    }

    [Fact]
    public void Retire_GivenAnEndBeforeValidFrom_Throws()
    {
        EndpointSecret secret = CreateSecret();

        Should.Throw<ArgumentOutOfRangeException>(() => secret.Retire(Now.AddSeconds(-1)));
    }

    [Fact]
    public void Retire_GivenValidFromItself_ProducesAnImmediatelyInactiveSecret()
    {
        EndpointSecret secret = CreateSecret();

        secret.Retire(Now);

        secret.IsActiveAt(Now).ShouldBeFalse();
    }
}