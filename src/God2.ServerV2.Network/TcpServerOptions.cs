using System.Net;

namespace God2.ServerV2.Network;

public sealed record TcpServerOptions(
    IPAddress BindAddress,
    int Port,
    IPAddress AdvertisedAddress)
{
    public TcpServerOptions(IPAddress bindAddress, int port)
        : this(bindAddress, port, bindAddress)
    {
    }

    public static TcpServerOptions Loopback(int port = 2592) =>
        new(IPAddress.Loopback, port, IPAddress.Loopback);
}
