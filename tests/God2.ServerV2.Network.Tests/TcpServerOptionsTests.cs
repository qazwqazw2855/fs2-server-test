using System.Net;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class TcpServerOptionsTests
{
    [Fact]
    public void ExplicitAdvertisedAddress_IsIndependentFromBindAddress()
    {
        var advertisedAddress = IPAddress.Parse("203.0.113.10");

        var options = new TcpServerOptions(
            IPAddress.Any,
            2592,
            advertisedAddress);

        Assert.Equal(IPAddress.Any, options.BindAddress);
        Assert.Equal(advertisedAddress, options.AdvertisedAddress);
        Assert.Equal(2592, options.Port);
    }

    [Fact]
    public void LegacyConstructor_AdvertisesBindAddress()
    {
        var options = new TcpServerOptions(
            IPAddress.Loopback,
            2592);

        Assert.Equal(options.BindAddress, options.AdvertisedAddress);
    }

    [Fact]
    public void LoopbackFactory_UsesLoopbackForBothAddresses()
    {
        var options = TcpServerOptions.Loopback();

        Assert.Equal(IPAddress.Loopback, options.BindAddress);
        Assert.Equal(IPAddress.Loopback, options.AdvertisedAddress);
        Assert.Equal(2592, options.Port);
    }
}
