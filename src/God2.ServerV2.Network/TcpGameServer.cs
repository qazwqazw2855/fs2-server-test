using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace God2.ServerV2.Network;

public sealed class TcpGameServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private long _nextConnectionId;
    private bool _started;

    public TcpGameServer(TcpServerOptions options)
    {
        Options = options;
        _listener = new TcpListener(options.BindAddress, options.Port);
    }

    public TcpServerOptions Options { get; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            throw new InvalidOperationException("The TCP server is already running.");
        }

        _started = true;
        _listener.Start();
        Log("server", $"Listening on {Options.BindAddress}:{Options.Port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var connectionId = Interlocked.Increment(ref _nextConnectionId);
                var task = HandleConnectionAsync(connectionId, client, cancellationToken);
                _connections[connectionId] = task;
                _ = ObserveConnectionAsync(connectionId, task);
            }
        }
        finally
        {
            _listener.Stop();

            var activeConnections = _connections.Values.ToArray();
            if (activeConnections.Length > 0)
            {
                await Task.WhenAll(activeConnections).ConfigureAwait(false);
            }

            Log("server", "Stopped");
        }
    }

    private async Task ObserveConnectionAsync(long connectionId, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // HandleConnectionAsync logs expected per-connection failures.
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
        }
    }

    private static async Task HandleConnectionAsync(
        long connectionId,
        TcpClient client,
        CancellationToken serverCancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Log(connectionId, $"Connected remote={remoteEndPoint}");

        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            using (client)
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                while (!serverCancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        serverCancellationToken);

                    if (bytesRead == 0)
                    {
                        Log(connectionId, "Remote closed connection");
                        break;
                    }

                    var hex = Convert.ToHexString(buffer.AsSpan(0, bytesRead));
                    Log(connectionId, $"RX bytes={bytesRead} hex={hex}");
                }
            }
        }
        catch (OperationCanceledException) when (serverCancellationToken.IsCancellationRequested)
        {
            Log(connectionId, "Closing because server is stopping");
        }
        catch (IOException exception)
        {
            Log(connectionId, $"I/O closed: {exception.Message}");
        }
        catch (SocketException exception)
        {
            Log(connectionId, $"Socket closed: {exception.SocketErrorCode}");
        }
        catch (Exception exception)
        {
            Log(connectionId, $"Unexpected error: {exception}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            Log(connectionId, "Closed");
        }
    }

    private static void Log(object source, string message) =>
        Console.WriteLine(
            $"{DateTimeOffset.UtcNow:O} [{source}] {message}");

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
