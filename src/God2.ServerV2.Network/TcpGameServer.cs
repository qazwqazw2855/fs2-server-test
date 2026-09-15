using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using God2.ServerV2.Core;
using God2.ServerV2.Protocol;
using God2.ServerV2.Session;

namespace God2.ServerV2.Network;

public sealed class TcpGameServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private long _nextConnectionId;
    private bool _started;

    public TcpGameServer(
        TcpServerOptions options,
        SessionRegistry sessionRegistry)
    {
        Options = options;
        _sessionRegistry = sessionRegistry ??
            throw new ArgumentNullException(nameof(sessionRegistry));
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
                var task = HandleConnectionAsync(
                    connectionId,
                    client,
                    _sessionRegistry,
                    cancellationToken);
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
        SessionRegistry sessionRegistry,
        CancellationToken serverCancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var state = new ConnectionStateMachine();
        using var sessionContext = new ConnectionSessionContext(
            connectionId,
            sessionRegistry);
        Log(connectionId, $"Connected remote={remoteEndPoint} stage={state.Stage}");
        state.Transition(ConnectionStage.LoginHandshake);

        try
        {
            using (client)
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                Log(
                    connectionId,
                    $"TX LoginServerHandshake bytes={OfficialLoginHandshakeProtocol.ServerHandshakeFrame.Length} " +
                    $"hex={Convert.ToHexString(OfficialLoginHandshakeProtocol.ServerHandshakeFrame.Span)}");

                var handshake = await OfficialLoginHandshakeProtocol.PerformAsync(
                    stream,
                    serverCancellationToken);

                Log(
                    connectionId,
                    $"RX LoginClientHandshake bytes={handshake.ClientHandshake.Length} " +
                    $"hex={Convert.ToHexString(handshake.ClientHandshake)}");

                if (!handshake.Succeeded)
                {
                    Log(connectionId, $"Handshake rejected: {handshake.Detail}");
                    return;
                }

                Log(
                    connectionId,
                    $"TX LoginVersionFollowUp bytes={OfficialLoginHandshakeProtocol.VersionFollowUpFrame.Length} " +
                    $"hex={Convert.ToHexString(OfficialLoginHandshakeProtocol.VersionFollowUpFrame.Span)}");
                state.Transition(ConnectionStage.Login);
                Log(connectionId, $"{handshake.Detail} stage={state.Stage}");

                while (!serverCancellationToken.IsCancellationRequested)
                {
                    var frame = await LengthPrefixedFrameReader.ReadAsync(
                        stream,
                        serverCancellationToken);

                    if (frame is null)
                    {
                        Log(connectionId, "Remote closed connection");
                        break;
                    }

                    if (frame.Length == 208)
                    {
                        Log(
                            connectionId,
                            $"RX LoginRequestCandidate bytes=208 stage={state.Stage} " +
                            "hex=[REDACTED_SENSITIVE_LOGIN_FRAME]");
                        continue;
                    }

                    Log(
                        connectionId,
                        $"RX frame bytes={frame.Length} hex={Convert.ToHexString(frame)}");
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
            sessionContext.Dispose();
            state.TryTransition(ConnectionStage.Closing);
            state.TryTransition(ConnectionStage.Closed);
            Log(connectionId, $"Closed stage={state.Stage}");
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
