using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using God2.ServerV2.Application;
using God2.ServerV2.Core;
using God2.ServerV2.Protocol;
using God2.ServerV2.Session;

namespace God2.ServerV2.Network;

public sealed class TcpGameServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly LoginService _loginService;
    private readonly CharacterListService _characterListService;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private long _nextConnectionId;
    private bool _started;

    public TcpGameServer(
        TcpServerOptions options,
        SessionRegistry sessionRegistry,
        LoginService loginService,
        CharacterListService characterListService)
    {
        Options = options;
        _loginService = loginService ??
            throw new ArgumentNullException(nameof(loginService));
        _characterListService = characterListService ??
            throw new ArgumentNullException(nameof(characterListService));
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
                    _loginService,
                    _characterListService,
                    Options.BindAddress.GetAddressBytes(),
                    checked((ushort)Options.Port),
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
        LoginService loginService,
        CharacterListService characterListService,
        byte[] advertisedAddress,
        ushort advertisedPort,
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

                IReadOnlyList<CharacterListEntry>? pendingCharacters = null;

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

                    if (state.Stage == ConnectionStage.CharacterSelect)
                    {
                        if (!OfficialServerSelectionCodec.TryDecodeRequest(
                                frame,
                                out var selection) ||
                            pendingCharacters is null ||
                            pendingCharacters.Count > 1)
                        {
                            Log(connectionId, "Server selection rejected.");
                            return;
                        }

                        var character = pendingCharacters.SingleOrDefault();
                        var response =
                            OfficialServerSelectionCodec.EncodeCharacterList(
                                character?.Name,
                                character?.ClassCode);

                        await stream.WriteAsync(
                            response,
                            serverCancellationToken);

                        Log(
                            connectionId,
                            $"TX CharacterListBootstrap bytes={response.Length} " +
                            $"server={selection.SelectedServerId} " +
                            $"characters={pendingCharacters.Count} hex=[REDACTED]");

                        return;
                    }

                    if (frame.Length == OfficialLoginRequestCodec.FrameLength)
                    {
                        Log(
                            connectionId,
                            $"RX LoginRequestCandidate bytes={frame.Length} stage={state.Stage} " +
                            "hex=[REDACTED_SENSITIVE_LOGIN_FRAME]");

                        if (!OfficialLoginRequestCodec.TryDecode(
                                frame,
                                out var loginRequest) ||
                            loginRequest is null)
                        {
                            Log(connectionId, "Login request rejected: invalid wire frame.");
                            return;
                        }

                        using (loginRequest)
                        {
                            var loginResult = await loginService.AuthenticateAsync(
                                loginRequest.AccountName,
                                loginRequest.Password,
                                sessionContext,
                                serverCancellationToken);

                            Log(
                                connectionId,
                                $"Login result={loginResult.Code} stage={state.Stage} " +
                                "account=[REDACTED]");

                            if (!loginResult.Succeeded)
                            {
                                var failureCode = loginResult.Code switch
                                {
                                    LoginResultCode.CredentialsRejected =>
                                        OfficialLoginFailureCode.CredentialsRejected,
                                    LoginResultCode.DuplicateLogin =>
                                        OfficialLoginFailureCode.DuplicateLogin,
                                    _ => throw new InvalidOperationException(
                                        $"Unsupported failed login result: {loginResult.Code}.")
                                };

                                var response =
                                    OfficialLoginResponseCodec.EncodeFailure(failureCode);

                                await stream.WriteAsync(
                                    response,
                                    serverCancellationToken);

                                Log(
                                    connectionId,
                                    $"TX LoginFailure code={failureCode} bytes={response.Length} " +
                                    $"hex={Convert.ToHexString(response)}");
                            }
                            else
                            {
                                var characters =
                                    await characterListService.GetAsync(
                                        loginResult.AccountId!.Value,
                                        serverCancellationToken);

                                Log(
                                    connectionId,
                                    $"Character list loaded count={characters.Count} " +
                                    "account=[REDACTED]");

                                var response =
                                    OfficialLoginSuccessCodec.EncodeServerGroupBootstrap(
                                        frame,
                                        advertisedAddress,
                                        advertisedPort);

                                await stream.WriteAsync(
                                    response,
                                    serverCancellationToken);

                                Log(
                                    connectionId,
                                    $"TX LoginSuccessBootstrap bytes={response.Length} " +
                                    $"endpoint={new IPAddress(advertisedAddress)}:{advertisedPort} " +
                                    "hex=[REDACTED_SENSITIVE_LOGIN_ECHO]");

                                pendingCharacters = characters;
                                state.Transition(ConnectionStage.CharacterSelect);

                                Log(
                                    connectionId,
                                    $"Ready for server selection stage={state.Stage}");
                            }
                        }

                        if (state.Stage == ConnectionStage.CharacterSelect)
                        {
                            continue;
                        }

                        return;
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
