using System.Net;

using Hookwright.Core.Configuration;
using Hookwright.Core.Transport;

namespace Hookwright.Core.Tests.Transport;

public sealed class DefaultDestinationPolicyTests
{
    private static readonly DefaultDestinationPolicy Strict = new(new SecurityOptions());

    private static readonly DefaultDestinationPolicy Development = new(
        new SecurityOptions { RequireHttps = false, AllowPrivateNetwork = true });

    [Theory]
    [InlineData("127.0.0.1", "a loopback address")]
    [InlineData("127.255.255.254", "a loopback address")]
    [InlineData("::1", "a loopback address")]
    [InlineData("0.0.0.0", "an unspecified address")]
    [InlineData("::", "an unspecified address")]
    [InlineData("10.0.0.1", "a private network address")]
    [InlineData("172.16.0.1", "a private network address")]
    [InlineData("172.31.255.255", "a private network address")]
    [InlineData("192.168.1.1", "a private network address")]
    [InlineData("fc00::1", "a private network address")]
    [InlineData("fd12:3456::1", "a private network address")]
    [InlineData("64:ff9b:1::1", "a private network address")]
    [InlineData("100.64.0.1", "a carrier-grade NAT address")]
    [InlineData("100.100.100.200", "a carrier-grade NAT address")]
    [InlineData("169.254.1.1", "a link-local address")]
    [InlineData("fe80::1", "a link-local address")]
    [InlineData("169.254.169.254", "a cloud metadata address")]
    [InlineData("fd00:ec2::254", "a cloud metadata address")]
    [InlineData("192.0.0.192", "a reserved address")]
    [InlineData("192.88.99.1", "a reserved address")]
    [InlineData("240.0.0.1", "a reserved address")]
    [InlineData("255.255.255.255", "a reserved address")]
    [InlineData("192.0.2.1", "a documentation address")]
    [InlineData("198.51.100.1", "a documentation address")]
    [InlineData("203.0.113.1", "a documentation address")]
    [InlineData("2001:db8::1", "a documentation address")]
    [InlineData("198.18.0.1", "a benchmarking address")]
    [InlineData("224.0.0.1", "a multicast address")]
    [InlineData("239.255.255.250", "a multicast address")]
    [InlineData("ff02::1", "a multicast address")]
    [InlineData("100::1", "a discard address")]
    [InlineData("::ffff:10.0.0.1", "a private network address")]
    [InlineData("::ffff:169.254.169.254", "a cloud metadata address")]
    [InlineData("64:ff9b::7f00:1", "a loopback address")]
    [InlineData("64:ff9b::a9fe:a9fe", "a cloud metadata address")]
    public void RefuseAddress_GivenAnAddressThatIsNotPublicInternet_NamesItsKind(string address, string reason)
    {
        Strict.RefuseAddress(IPAddress.Parse(address)).ShouldBe(reason);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    public void RefuseAddress_GivenAPublicAddress_Allows(string address)
    {
        Strict.RefuseAddress(IPAddress.Parse(address)).ShouldBeNull();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.0.10")]
    [InlineData("fd00::1")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.1.1")]
    public void RefuseAddress_GivenADevelopmentAllowance_AdmitsTheLocalKinds(string address)
    {
        Development.RefuseAddress(IPAddress.Parse(address)).ShouldBeNull();
    }

    [Theory]
    [InlineData("169.254.169.254", "a cloud metadata address")]
    [InlineData("fd00:ec2::254", "a cloud metadata address")]
    [InlineData("0.0.0.0", "an unspecified address")]
    [InlineData("224.0.0.1", "a multicast address")]
    [InlineData("255.255.255.255", "a reserved address")]
    [InlineData("192.0.2.1", "a documentation address")]
    public void RefuseAddress_GivenADevelopmentAllowance_StillRefusesWhatIsNeverLocal(string address, string reason)
    {
        Development.RefuseAddress(IPAddress.Parse(address)).ShouldBe(reason);
    }

    [Fact]
    public void RefuseAddress_GivenAnAllowedNetwork_AdmitsItAndNothingBeside()
    {
        DefaultDestinationPolicy policy = new(new SecurityOptions { AllowedNetworks = ["10.20.0.0/16"] });

        policy.RefuseAddress(IPAddress.Parse("10.20.5.6")).ShouldBeNull();
        policy.RefuseAddress(IPAddress.Parse("10.21.5.6")).ShouldBe("a private network address");
    }

    [Fact]
    public void RefuseDestination_GivenHttpsOnTheDefaultPort_Allows()
    {
        Strict.RefuseDestination(new Uri("https://example.com/hooks")).ShouldBeNull();
        Strict.RefuseDestination(new Uri("https://example.com:443/hooks")).ShouldBeNull();
    }

    [Fact]
    public void RefuseDestination_GivenPlainHttp_RefusesUnlessPermitted()
    {
        Uri destination = new("http://example.com/hooks");

        Strict.RefuseDestination(destination).ShouldNotBeNull().ShouldContain(nameof(SecurityOptions.RequireHttps));
        Development.RefuseDestination(destination).ShouldBeNull();
    }

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    public void RefuseDestination_GivenAnotherScheme_Refuses(string destination)
    {
        Development.RefuseDestination(new Uri(destination)).ShouldNotBeNull().ShouldContain("scheme");
    }

    [Fact]
    public void RefuseDestination_GivenAnotherPort_RefusesUnlessListed()
    {
        Uri destination = new("https://example.com:8443/hooks");

        Strict.RefuseDestination(destination).ShouldNotBeNull().ShouldContain("port 8443");
        new DefaultDestinationPolicy(new SecurityOptions
        {
            AllowedPorts = [8443]
        }).RefuseDestination(destination).ShouldBeNull();
        Development.RefuseDestination(destination).ShouldBeNull();
    }
}