using System.Net;
using System.Net.Sockets;
using God2.ServerV2.Core;
using God2.ServerV2.Network;
using God2.ServerV2.Session;

Console.WriteLine("God2 Server V2");
Console.WriteLine($"Version: {ServerV2Architecture.Version}");
Console.WriteLine($"Default content mode: {ServerContentMode.ClassicCompatibility}");

var bindText = Environment.GetEnvironmentVariable("GOD2_BIND") ?? "127.0.0.1";
var portText = Environment.GetEnvironmentVariable("GOD2_PORT") ?? "2592";

if (!IPAddress.TryParse(bindText, out var bindAddress))
{
    Console.Error.WriteLine($"Invalid GOD2_BIND value: {bindText}");
    return 2;
}

if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
{
    Console.Error.WriteLine($"Invalid GOD2_PORT value: {portText}");
    return 2;
}

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;

    if (!shutdown.IsCancellationRequested)
    {
        Console.WriteLine("Shutdown requested...");
        shutdown.Cancel();
    }
};

var sessionRegistry = new SessionRegistry();

await using var server = new TcpGameServer(
    new TcpServerOptions(bindAddress, port),
    sessionRegistry);

try
{
    await server.RunAsync(shutdown.Token);
    return 0;
}
catch (SocketException exception)
{
    Console.Error.WriteLine(
        $"Unable to listen on {bindAddress}:{port}: {exception.SocketErrorCode} - {exception.Message}");
    return 1;
}
