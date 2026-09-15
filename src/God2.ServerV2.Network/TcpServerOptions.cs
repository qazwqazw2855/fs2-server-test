using System.Net;

namespace God2.ServerV2.Network;

public sealed record TcpServerOptions(IPAddress BindAddress, int Port)
{
    public static TcpServerOptions Loopback(int port = 2592) =>
        new(IPAddress.Loopback, port);
}
