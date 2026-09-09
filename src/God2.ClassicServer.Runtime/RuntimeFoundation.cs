using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public static class RuntimeEvidencePathResolver
{
    public const string EnvironmentVariableName = "GOD2_RUNTIME_EVIDENCE_DIRECTORY";

    public static string Resolve(string? configuredDirectory = null)
    {
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Environment.GetEnvironmentVariable(EnvironmentVariableName)
            : configuredDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "God2ClassicServer",
                "runtime",
                "protocol-evidence");
        }

        return Path.GetFullPath(directory);
    }
}

public sealed class ImmutableRuntimeCacheBuilder : IRuntimeCacheBuilder
{
    private IReadOnlyList<StaticDataLoadCount> _publishedCounts =
        Array.AsReadOnly(Array.Empty<StaticDataLoadCount>());

    public IReadOnlyList<StaticDataLoadCount> PublishedCounts => Volatile.Read(ref _publishedCounts);

    public Task<OperationResult> BuildAsync(IReadOnlyList<StaticDataLoadCount> staticData, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = staticData
            .Select(entry => new StaticDataLoadCount(entry.DataType, entry.Count))
            .ToArray();
        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.DataType) || entry.Count < 0) ||
            entries.Select(entry => entry.DataType).Distinct(StringComparer.Ordinal).Count() != entries.Length)
        {
            return Task.FromResult(OperationResult.Failure(
                "runtime_cache.invalid",
                "Static runtime cache input contains duplicate, unnamed, or negative entries.",
                "static-data"));
        }

        IReadOnlyList<StaticDataLoadCount> snapshot = Array.AsReadOnly(entries);
        Interlocked.Exchange(ref _publishedCounts, snapshot);
        return Task.FromResult(OperationResult.Success);
    }
}

public sealed class InMemorySessionAuthority : ISessionAuthority, IShutdownParticipant
{
    private readonly SessionStore _sessionStore;
    private int _acceptingLogins;

    public InMemorySessionAuthority(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    public string Name => "Session Authority";

    public bool IsAcceptingLogins => Volatile.Read(ref _acceptingLogins) == 1;

    public IReadOnlyList<RuntimeSession> ActiveSessions => _sessionStore.ActiveSessions;

    public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _acceptingLogins, 1);
        return Task.FromResult(OperationResult.Success);
    }

    public Task StopAcceptingLoginsAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _acceptingLogins, 0);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => StopAcceptingLoginsAsync(cancellationToken);

    public RuntimeSession CreateSession(string connectionId, string remoteEndpoint) => _sessionStore.Create(connectionId, remoteEndpoint);

    public OperationResult<RuntimeSession> GetSession(string sessionId) => _sessionStore.Get(sessionId);

    public OperationResult<RuntimeSession> Authenticate(RuntimeSession session, long accountId) => _sessionStore.Authenticate(session, accountId);

    public OperationResult<RuntimeSession> BindCharacter(RuntimeSession session, long characterId) => _sessionStore.BindCharacter(session, characterId);

    public OperationResult<RuntimeSession> Transition(RuntimeSession session, ProtocolStage stage) => _sessionStore.Transition(session, stage);

    public Task CloseSessionAsync(RuntimeSession session, string reason, CancellationToken cancellationToken) =>
        _sessionStore.CloseAsync(session, reason, cancellationToken);
}

public enum ServerLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
    None
}

public interface IServerLogger
{
    void Log(
        ServerLogLevel level,
        string message,
        string? connectionId = null,
        string? sessionId = null,
        ProtocolStage? stage = null,
        string? errorCode = null);
}

public sealed class NullServerLogger : IServerLogger
{
    public static NullServerLogger Instance { get; } = new();

    public void Log(
        ServerLogLevel level,
        string message,
        string? connectionId = null,
        string? sessionId = null,
        ProtocolStage? stage = null,
        string? errorCode = null)
    {
    }
}

public sealed class TextWriterServerLogger : IServerLogger
{
    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private readonly ServerLogLevel _minimumLevel;

    public TextWriterServerLogger(TextWriter writer, string minimumLevel)
    {
        _writer = writer;
        _minimumLevel = ParseLevel(minimumLevel);
    }

    public void Log(
        ServerLogLevel level,
        string message,
        string? connectionId = null,
        string? sessionId = null,
        ProtocolStage? stage = null,
        string? errorCode = null)
    {
        if (level < _minimumLevel || _minimumLevel == ServerLogLevel.None)
        {
            return;
        }

        var context = string.Join(
            " ",
            new[]
            {
                connectionId is null ? null : $"connection={connectionId}",
                sessionId is null ? null : $"session={sessionId}",
                stage is null ? null : $"stage={stage}",
                errorCode is null ? null : $"code={errorCode}"
            }.Where(value => value is not null));

        lock (_gate)
        {
            try
            {
                _writer.WriteLine(
                    $"{DateTimeOffset.UtcNow:O} [{level}] {message}{(context.Length == 0 ? string.Empty : $" {context}")}");
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }
    }

    private static ServerLogLevel ParseLevel(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "TRACE" => ServerLogLevel.Trace,
            "DEBUG" => ServerLogLevel.Debug,
            "WARNING" or "WARN" => ServerLogLevel.Warning,
            "ERROR" => ServerLogLevel.Error,
            "CRITICAL" => ServerLogLevel.Critical,
            "NONE" => ServerLogLevel.None,
            _ => ServerLogLevel.Information
        };
}

public enum OfficialWorldWireProfile
{
    LegacyMap19,
    LegacyMap19GoldenIdentityEvidence,
    CurrentCreateEvidence
}

public sealed class TcpNetworkHost : INetworkHost, IShutdownParticipant
{
    private static readonly byte[] OfficialLoginServerHandshake =
        Convert.FromHexString("1300405FD0401BB55367D34D90DF1D929883DD");

    private static readonly byte[] OfficialWorldServerHandshake =
        OfficialClientWorldProtocolFrames.BuildWorldServerHandshake();

    private static readonly byte[] OfficialLoginServerFollowUp =
        OfficialClientLoginProtocolFrames.BuildLoginVersionFollowUp();

    private static readonly byte[] OfficialCharacterCreationLoginServerFollowUp =
        OfficialClientLoginProtocolFrames.BuildCharacterCreationLoginVersionFollowUp();

    private static readonly byte[] OfficialWorldServerFirstFollowUp =
        OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();

    private const int OfficialWorldEntryRequestLength = 208;

    private static readonly byte[] OfficialLoginClientHandshake =
        Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC");

    private static readonly byte[] OfficialWorldClientHandshake =
        Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962");

    private enum OfficialHandlerDisposition
    {
        NotHandled,
        Handled,
        CloseConnection
    }

    private readonly record struct OfficialHandlerResult(
        OfficialHandlerDisposition Disposition,
        string CloseReason = "")
    {
        public static OfficialHandlerResult NotHandled => new(OfficialHandlerDisposition.NotHandled);

        public static OfficialHandlerResult Handled => new(OfficialHandlerDisposition.Handled);

        public static OfficialHandlerResult Close(string reason) =>
            new(OfficialHandlerDisposition.CloseConnection, reason);
    }

    private enum ConnectionRole
    {
        Unknown,
        LoginHandshake,
        LoginAuthenticated,
        CharacterSelect,
        PendingWorld,
        WorldHandshake,
        InWorld,
        Closed
    }

    private sealed record PendingWorldConnection(
        string RemoteAddress,
        string LoginConnectionId,
        string LoginSessionId,
        long AccountId,
        byte SelectedServerId,
        CharacterSummary Character,
        DateTimeOffset ExpiresAtUtc);

    private sealed class ActiveConnection
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public ActiveConnection(string connectionId, TcpClient client)
        {
            ConnectionId = connectionId;
            Client = client;
        }

        public string ConnectionId { get; }

        public TcpClient Client { get; }

        public ConnectionRole Role { get; set; } = ConnectionRole.Unknown;

        public ushort WorldXCandidate { get; set; }

        public ushort WorldYCandidate { get; set; }

        public byte LastWorldMovementSequence { get; set; }

        public int WorldMovementCount { get; set; }

        public long PortalActionSequence { get; set; } = 1;

        public long NpcInteractionIngressOrdinal { get; set; } = 1;

        public long MerchantTransactionIngressOrdinal { get; set; } = 1;

        public long WorldChatIngressOrdinal { get; set; } = 1;

        public string SessionId { get; set; } = string.Empty;

        public bool PortalAwaitingMovementFollowUp { get; set; }

        public bool PortalEligibilityInvalidatedByUnverifiedMovement { get; set; }

        public bool RuntimeReplicationEnabled { get; set; }

        public bool CharacterCreationLoginReconnect { get; set; }

        public bool CloseAfterCharacterCreationList { get; set; }

        public string WorldHandshakeRemoteAddress { get; set; } = string.Empty;

        public IReadOnlyList<CharacterSummary> AuthenticatedCharacters { get; set; } = [];

        public Task Runner { get; set; } = Task.CompletedTask;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await Client.GetStream().WriteAsync(frame, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }

    private readonly object _lifecycleGate = new();
    private readonly object _worldClaimGate = new();
    private readonly List<TcpListener> _listeners = [];
    private readonly List<Task> _acceptLoops = [];
    private readonly ConcurrentDictionary<string, ActiveConnection> _connections = [];
    private readonly ConcurrentDictionary<string, PendingWorldConnection> _pendingWorldConnectionsByRemoteAddress = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingCharacterCreationLogins = [];
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _worldHandshakeConnectionsByRemoteAddress = [];
    private readonly HashSet<string> _ambiguousWorldHandshakeRemoteAddresses = new(StringComparer.Ordinal);
    private readonly PacketFactory _packetFactory;
    private readonly ProtocolConnectionRuntime _protocolRuntime;
    private readonly InMemorySessionAuthority? _sessionAuthority;
    private readonly OfficialServerSelectionClosedLoop _serverSelectionClosedLoop;
    private readonly OfficialPortalClosedLoop? _portalClosedLoop;
    private readonly OfficialNpcInteractionClosedLoop? _npcInteractionClosedLoop;
    private readonly OfficialMerchantTransactionClosedLoop? _merchantTransactionClosedLoop;
    private readonly AuthenticationService? _authenticationService;
    private readonly CharacterListQuery? _characterListQuery;
    private readonly CharacterRuntimeService? _characterRuntimeService;
    private readonly IWorldSessionCoordinator? _worldSessionCoordinator;
    private readonly IWorldBootstrapProjector? _worldBootstrapProjector;
    private readonly IWorldMovementStore? _worldMovementStore;
    private readonly IPortalTransitionStore? _portalTransitionStore;
    private readonly IInventoryTransactionCoordinator? _inventoryTransactionCoordinator;
    private readonly IPetLifecycleCoordinator? _petLifecycleCoordinator;
    private readonly IProductionMonsterCombatServiceFactory? _productionMonsterCombatFactory;
    private readonly ServerOwnedMultiplayerRuntime? _serverOwnedMultiplayerRuntime;
    private readonly PublicBetaCompatibilityProfile? _publicBetaCompatibilityProfile;
    private readonly OfficialWorldChatClosedLoop? _officialWorldChatClosedLoop;
    private readonly RuntimeReplicationEmitter _runtimeReplicationEmitter;
    private readonly IServerLogger _logger;
    private readonly int _maximumConnections;
    private readonly OfficialWorldWireProfile _officialWorldWireProfile;
    private readonly byte[] _officialWorldServerHandshake;
    private readonly byte[] _officialWorldClientHandshake;
    private readonly byte[] _officialWorldServerFirstFollowUp;
    private readonly string _rawEvidenceDirectory;
    private CancellationTokenSource? _stop;
    private Task? _stopTask;
    private int _maxPacketSize = 4096;
    private long _nextConnectionId;
    private IPAddress? _advertisedIpAddress;

    public TcpNetworkHost()
        : this(new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official)))
    {
    }

    public TcpNetworkHost(InMemorySessionAuthority sessionAuthority)
        : this(new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official)), null, sessionAuthority)
    {
    }

    public TcpNetworkHost(
        PacketFactory packetFactory,
        ProtocolConnectionRuntime? protocolRuntime = null,
        InMemorySessionAuthority? sessionAuthority = null,
        AuthenticationService? authenticationService = null,
        CharacterListQuery? characterListQuery = null,
        IPortalTransitionStore? portalTransitionStore = null,
        IWorldSessionCoordinator? worldSessionCoordinator = null,
        IWorldBootstrapProjector? worldBootstrapProjector = null,
        IOfficialPortalMapIdentitySource? portalMapIdentitySource = null,
        RuntimeReplicationEmitter? runtimeReplicationEmitter = null,
        int maximumConnections = 1_000,
        IServerLogger? logger = null,
        OfficialServerSelectionClosedLoop? serverSelectionClosedLoop = null,
        OfficialPortalClosedLoop? portalClosedLoop = null,
        OfficialNpcInteractionClosedLoop? npcInteractionClosedLoop = null,
        OfficialWorldWireProfile officialWorldWireProfile = OfficialWorldWireProfile.LegacyMap19,
        IWorldMovementStore? worldMovementStore = null,
        IInventoryTransactionCoordinator? inventoryTransactionCoordinator = null,
        IPetLifecycleCoordinator? petLifecycleCoordinator = null,
        IProductionMonsterCombatServiceFactory? productionMonsterCombatFactory = null,
        OfficialMerchantTransactionClosedLoop? merchantTransactionClosedLoop = null,
        CharacterRuntimeService? characterRuntimeService = null,
        string? rawEvidenceDirectory = null,
        ServerOwnedMultiplayerRuntime? serverOwnedMultiplayerRuntime = null,
        PublicBetaCompatibilityProfile? publicBetaCompatibilityProfile = null)
    {
        _packetFactory = packetFactory;
        _protocolRuntime = protocolRuntime ?? RuntimeProtocolConnectionRuntimeFactory.Create(packetFactory);
        _sessionAuthority = sessionAuthority;
        _authenticationService = authenticationService;
        _characterListQuery = characterListQuery;
        _characterRuntimeService = characterRuntimeService;
        _worldSessionCoordinator = worldSessionCoordinator;
        _worldBootstrapProjector = worldBootstrapProjector;
        _worldMovementStore = worldMovementStore ?? portalTransitionStore as IWorldMovementStore;
        _portalTransitionStore = portalTransitionStore;
        _inventoryTransactionCoordinator = inventoryTransactionCoordinator;
        _petLifecycleCoordinator = petLifecycleCoordinator;
        _productionMonsterCombatFactory = productionMonsterCombatFactory;
        _serverOwnedMultiplayerRuntime = serverOwnedMultiplayerRuntime;
        _publicBetaCompatibilityProfile = publicBetaCompatibilityProfile;
        _officialWorldChatClosedLoop = serverOwnedMultiplayerRuntime is null
            ? null
            : new OfficialWorldChatClosedLoop(serverOwnedMultiplayerRuntime.Chat);
        _npcInteractionClosedLoop = npcInteractionClosedLoop;
        _merchantTransactionClosedLoop = merchantTransactionClosedLoop ??
            (npcInteractionClosedLoop is not null && inventoryTransactionCoordinator is not null
                ? new OfficialMerchantTransactionClosedLoop(npcInteractionClosedLoop, inventoryTransactionCoordinator)
                : null);
        _runtimeReplicationEmitter = runtimeReplicationEmitter ?? new RuntimeReplicationEmitter();
        _rawEvidenceDirectory = RuntimeEvidencePathResolver.Resolve(rawEvidenceDirectory);
        _officialWorldWireProfile = officialWorldWireProfile;
        _officialWorldServerHandshake = officialWorldWireProfile == OfficialWorldWireProfile.CurrentCreateEvidence
            ? OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldServerHandshake()
            : OfficialWorldServerHandshake.ToArray();
        _officialWorldClientHandshake = officialWorldWireProfile == OfficialWorldWireProfile.CurrentCreateEvidence
            ? OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldClientHandshake()
            : OfficialWorldClientHandshake.ToArray();
        _officialWorldServerFirstFollowUp = officialWorldWireProfile == OfficialWorldWireProfile.CurrentCreateEvidence
            ? OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldFirstFollowUp()
            : OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        if (authenticationService is not null &&
            (worldSessionCoordinator is null || worldBootstrapProjector is null || _worldMovementStore is null))
        {
            throw new ArgumentException(
                "An authenticated production network host requires an authoritative world-session coordinator, bootstrap projector, and movement store.",
                nameof(worldSessionCoordinator));
        }
        _serverSelectionClosedLoop = serverSelectionClosedLoop ?? new OfficialServerSelectionClosedLoop();
        if (portalClosedLoop is not null)
        {
            _portalClosedLoop = portalClosedLoop;
        }
        else if (sessionAuthority is not null)
        {
            if (authenticationService is not null && portalTransitionStore is null)
            {
                throw new ArgumentException(
                    "An authenticated production network host requires an explicit portal persistence store.",
                    nameof(portalTransitionStore));
            }

            var portalRuntime = new OfficialPortalPhase2RuntimeService(
                sessionAuthority,
                portalTransitionStore,
                portalMapIdentitySource);
            _portalClosedLoop = new OfficialPortalClosedLoop(portalRuntime, portalRuntime);
        }
        _maximumConnections = Math.Max(1, maximumConnections);
        _logger = logger ?? NullServerLogger.Instance;
    }

    public IInventoryTransactionCoordinator? InventoryTransactionCoordinator => _inventoryTransactionCoordinator;

    public IPetLifecycleCoordinator? PetLifecycleCoordinator => _petLifecycleCoordinator;

    public IProductionMonsterCombatServiceFactory? ProductionMonsterCombatFactory => _productionMonsterCombatFactory;

    public ServerOwnedMultiplayerRuntime? ServerOwnedMultiplayerRuntime => _serverOwnedMultiplayerRuntime;

    public OfficialWorldChatClosedLoop? OfficialWorldChatClosedLoop => _officialWorldChatClosedLoop;

    public PublicBetaCompatibilityProfile? PublicBetaCompatibilityProfile => _publicBetaCompatibilityProfile;

    public string Name => "Network Host";

    public bool IsStarted
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _stop is not null && !_stop.IsCancellationRequested && _listeners.Count > 0;
            }
        }
    }

    public int ListenerCount
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _listeners.Count;
            }
        }
    }

    public int AcceptLoopCount
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _acceptLoops.Count;
            }
        }
    }

    public int ActiveConnectionCount => _connections.Count;

    public int ConnectionTaskCount => _connections.Count;

    public int ProtocolConnectionStateCount => _protocolRuntime.ConnectionStateCount;

    public OfficialPortalDiagnostics? OfficialPortalDiagnostics => _portalClosedLoop?.Diagnostics();

    public IReadOnlyList<int> BoundPorts
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _listeners
                    .Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port)
                    .ToArray();
            }
        }
    }

    public OperationResult<PacketEnvelope> DecodeIngressFrame(ReadOnlyMemory<byte> frame) => _packetFactory.CreateFromFrame(frame);

    public Task<OperationResult> StartAsync(NetworkOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TcpListener? listener = null;
        try
        {
            var address = IPAddress.Parse(options.BindIp);
            IPAddress? advertisedAddress = null;
            if (!string.IsNullOrWhiteSpace(options.AdvertisedIp) &&
                (!IPAddress.TryParse(options.AdvertisedIp, out advertisedAddress) ||
                 advertisedAddress.AddressFamily != AddressFamily.InterNetwork ||
                 IPAddress.Any.Equals(advertisedAddress)))
            {
                return Task.FromResult(OperationResult.Failure(
                    "network.advertised_ip_invalid",
                    "The advertised endpoint must be a concrete IPv4 address.",
                    options.AdvertisedIp));
            }

            lock (_lifecycleGate)
            {
                if (_stop is not null || _stopTask is not null)
                {
                    return Task.FromResult(OperationResult.Failure(
                        "network.lifecycle_invalid",
                        "Network host is already started or stopping.",
                        options.BindIp));
                }

                listener = new TcpListener(address, options.LoginPort);
                listener.Start();
                _maxPacketSize = options.MaxFrameSize;
                _advertisedIpAddress = advertisedAddress;
                _stop = new CancellationTokenSource();
                _listeners.Add(listener);
                _acceptLoops.Add(AcceptLoopAsync(listener, _stop.Token));
            }

            _logger.Log(ServerLogLevel.Information, $"Game endpoint listening on {options.BindIp}:{options.LoginPort}.");
            return Task.FromResult(OperationResult.Success);
        }
        catch (Exception ex) when (ex is SocketException or FormatException)
        {
            listener?.Stop();
            lock (_lifecycleGate)
            {
                _listeners.Clear();
                _acceptLoops.Clear();
                _stop?.Dispose();
                _stop = null;
                _advertisedIpAddress = null;
            }

            return Task.FromResult(OperationResult.Failure("network.bind_failed", ex.Message, options.BindIp));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopTask;
        TaskCompletionSource? completion = null;
        CancellationTokenSource? stop;
        TcpListener[] listeners;
        Task[] acceptLoops;

        lock (_lifecycleGate)
        {
            if (_stopTask is not null)
            {
                stopTask = _stopTask;
                stop = null;
                listeners = [];
                acceptLoops = [];
            }
            else if (_stop is null)
            {
                return;
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = completion.Task;
                stopTask = completion.Task;
                stop = _stop;
                listeners = _listeners.ToArray();
                acceptLoops = _acceptLoops.ToArray();
            }
        }

        if (completion is null)
        {
            await stopTask.WaitAsync(cancellationToken);
            return;
        }

        try
        {
            stop!.Cancel();
            foreach (var listener in listeners)
            {
                listener.Stop();
            }

            await ObserveAllAsync(acceptLoops);

            var activeConnections = _connections.Values.ToArray();
            foreach (var connection in activeConnections)
            {
                connection.Client.Dispose();
            }

            await ObserveAllAsync(activeConnections.Select(connection => connection.Runner).ToArray());
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            throw;
        }
        finally
        {
            lock (_lifecycleGate)
            {
                _listeners.Clear();
                _acceptLoops.Clear();
                _stop?.Dispose();
                _stop = null;
                _stopTask = null;
                _advertisedIpAddress = null;
            }

            _logger.Log(ServerLogLevel.Information, "Network host stopped after all connection tasks completed.");
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                if (_connections.Count >= _maximumConnections)
                {
                    client.Dispose();
                    _logger.Log(ServerLogLevel.Warning, "Connection rejected because the configured connection limit was reached.");
                    continue;
                }

                var connectionId = Interlocked.Increment(ref _nextConnectionId)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                var connection = new ActiveConnection(connectionId, client);
                if (!_connections.TryAdd(connectionId, connection))
                {
                    client.Dispose();
                    continue;
                }

                connection.Runner = RunConnectionAsync(connection, cancellationToken);
                client = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                return;
            }
            catch (SocketException exception) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                _logger.Log(ServerLogLevel.Debug, "Accept loop stopped.", errorCode: exception.SocketErrorCode.ToString());
                return;
            }
            catch (SocketException exception)
            {
                client?.Dispose();
                _logger.Log(ServerLogLevel.Error, "TCP accept failed; listener remains active.", errorCode: exception.SocketErrorCode.ToString());
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
    }

    private async Task RunConnectionAsync(ActiveConnection connection, CancellationToken cancellationToken)
    {
        RuntimeSession? initialSession = null;
        var reason = "server_shutdown";
        try
        {
            var remoteEndpoint = connection.Client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            var remoteAddress = GetRemoteAddress(connection.Client.Client.RemoteEndPoint);
            PendingWorldConnection? pendingWorld;
            lock (_worldClaimGate)
            {
                pendingWorld = TryGetPendingWorldConnection(remoteAddress);
                connection.Role = pendingWorld is null ? ConnectionRole.LoginHandshake : ConnectionRole.WorldHandshake;
                connection.CharacterCreationLoginReconnect = pendingWorld is null &&
                    HasPendingCharacterCreationLogin(remoteAddress);
                if (connection.Role == ConnectionRole.WorldHandshake)
                {
                    connection.WorldHandshakeRemoteAddress = remoteAddress;
                    var handshakes = _worldHandshakeConnectionsByRemoteAddress
                        .GetOrAdd(remoteAddress, _ => []);
                    handshakes.TryAdd(connection.ConnectionId, 0);
                    if (handshakes.Count > 1)
                    {
                        // Once concurrent world claims from one address have been
                        // observed, the entire cohort stays ambiguous. Otherwise the
                        // first rejection can remove itself and let a racing peer see
                        // a count of one and consume the one-time claim.
                        _ambiguousWorldHandshakeRemoteAddresses.Add(remoteAddress);
                    }
                }
            }
            initialSession = _sessionAuthority?.CreateSession(connection.ConnectionId, remoteEndpoint) ??
                RuntimeSession.Connected(connection.ConnectionId, remoteEndpoint, DateTimeOffset.UtcNow) with
                {
                    ProtocolStage = connection.Role == ConnectionRole.WorldHandshake ? ProtocolStage.WorldEntering : ProtocolStage.Login
                };
            connection.SessionId = initialSession.SessionId;
            _logger.Log(
                ServerLogLevel.Information,
                $"Connection accepted from {remoteEndpoint}; role={connection.Role}.",
                connection.ConnectionId,
                initialSession.SessionId,
                initialSession.ProtocolStage);
            await using var stream = connection.Client.GetStream();
            reason = _sessionAuthority is null
                ? await ReceiveLoopAsync(connection, initialSession, stream, cancellationToken)
                : await CompleteOfficialHandshakeAsync(connection, initialSession, pendingWorld, stream, cancellationToken) ??
                await ReceiveLoopAsync(connection, initialSession, stream, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reason = "server_shutdown";
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            if (connection.Role == ConnectionRole.Closed)
            {
                reason = "server_initiated_close";
                _logger.Log(
                    ServerLogLevel.Debug,
                    "Server-initiated connection close completed.",
                    connection.ConnectionId,
                    initialSession?.SessionId,
                    initialSession?.ProtocolStage,
                    reason);
            }
            else
            {
                reason = "transport_error";
                _logger.Log(
                    ServerLogLevel.Warning,
                    "Connection transport ended.",
                    connection.ConnectionId,
                    initialSession?.SessionId,
                    initialSession?.ProtocolStage,
                    exception is SocketException socket ? socket.SocketErrorCode.ToString() : exception.GetType().Name);
            }
        }
        catch (Exception exception)
        {
            reason = "connection_unhandled_error";
            _logger.Log(
                ServerLogLevel.Error,
                "Connection task failed.",
                connection.ConnectionId,
                initialSession?.SessionId,
                initialSession?.ProtocolStage,
                exception.GetType().Name);
        }
        finally
        {
            try
            {
                if (initialSession is not null)
                {
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await CloseLatestSessionAsync(initialSession.SessionId, reason, cleanupTimeout.Token);
                }
            }
            catch (Exception exception)
            {
                _logger.Log(
                    ServerLogLevel.Error,
                    "Session cleanup failed after transport close.",
                    connection.ConnectionId,
                    initialSession?.SessionId,
                    initialSession?.ProtocolStage,
                    exception.GetType().Name);
            }

            _protocolRuntime.RemoveConnection(connection.ConnectionId);
            if (initialSession is not null)
            {
                _officialWorldChatClosedLoop?.RemoveSession(initialSession.SessionId);
                _worldSessionCoordinator?.Unbind(initialSession.SessionId);
                _serverSelectionClosedLoop.RemoveSession(initialSession.SessionId);
                _portalClosedLoop?.RemoveSession(initialSession.SessionId);
                _npcInteractionClosedLoop?.RemoveSession(initialSession.SessionId);
            }
            RemovePendingWorldConnection(connection.ConnectionId);
            RemoveWorldHandshakeConnection(connection);
            connection.Client.Dispose();
            _connections.TryRemove(connection.ConnectionId, out _);
            _logger.Log(
                ServerLogLevel.Information,
                $"Connection closed ({reason}).",
                connection.ConnectionId,
                initialSession?.SessionId,
                ProtocolStage.Closed,
                reason);
        }
    }

    private async Task<string> ReceiveLoopAsync(
        ActiveConnection connection,
        RuntimeSession initialSession,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var accumulator = new FrameAccumulator(_maxPacketSize);
        var receiveBufferSize = Math.Min(_maxPacketSize, 8192);
        var buffer = ArrayPool<byte>.Shared.Rent(receiveBufferSize);
        Task<int>? pendingRead = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                pendingRead ??= stream.ReadAsync(buffer.AsMemory(0, receiveBufferSize), cancellationToken).AsTask();
                using var replicationTickCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var replicationTick = Task.Delay(TimeSpan.FromMilliseconds(50), replicationTickCancellation.Token);
                if (await Task.WhenAny(pendingRead, replicationTick) != pendingRead)
                {
                    var latestSession = GetLatestSession(initialSession);
                    if (!latestSession.Succeeded || latestSession.Value is null)
                    {
                        return "session_not_found";
                    }

                    var replicationFailure = await FlushWorldReplicationAsync(
                        connection,
                        latestSession.Value,
                        stream,
                        cancellationToken);
                    if (replicationFailure is not null)
                    {
                        return replicationFailure;
                    }

                    continue;
                }

                await replicationTickCancellation.CancelAsync();
                var read = await pendingRead;
                pendingRead = null;
                if (read == 0)
                {
                    return "client_close";
                }

                var accumulated = accumulator.Append(buffer.AsSpan(0, read));
                if (accumulated.Issue is { Status: not FrameReadStatus.NeedMoreData } issue)
                {
                    foreach (var parsedFrame in accumulated.Frames)
                    {
                        parsedFrame.Clear();
                    }

                    return issue.Code;
                }

                foreach (var frame in accumulated.Frames)
                {
                    try
                    {
                        var sessionResult = GetLatestSession(initialSession);
                        if (!sessionResult.Succeeded || sessionResult.Value is null)
                        {
                            return "session_not_found";
                        }

                        var session = sessionResult.Value;
                        if (await TryHandleOfficialServerSelectionAsync(connection, session, stream, frame.Bytes, cancellationToken))
                        {
                            if (connection.CloseAfterCharacterCreationList)
                            {
                                return "character_creation_reconnect_required";
                            }

                            var replicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                            if (replicationFailure is not null)
                            {
                                return replicationFailure;
                            }

                            continue;
                        }

                        if (connection.Role == ConnectionRole.CharacterSelect &&
                            session.ProtocolStage == ProtocolStage.CharacterList)
                        {
                            Directory.CreateDirectory(_rawEvidenceDirectory);
                            var evidencePath = Path.Combine(
                                _rawEvidenceDirectory,
                                $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-character-list-ingress-{Guid.NewGuid():N}.sensitive.bin");
                            await File.WriteAllBytesAsync(evidencePath, frame.Bytes.ToArray(), cancellationToken);
                        }

                        var characterLifecycle = await TryHandleOfficialCharacterLifecycleAsync(
                            connection,
                            session,
                            frame.Bytes,
                            cancellationToken);
                        if (characterLifecycle.Disposition == OfficialHandlerDisposition.CloseConnection)
                        {
                            return characterLifecycle.CloseReason;
                        }

                        if (characterLifecycle.Disposition == OfficialHandlerDisposition.Handled)
                        {
                            continue;
                        }

                        var login = await TryHandleOfficialLoginRequestAsync(
                            connection,
                            session,
                            stream,
                            frame.Bytes,
                            cancellationToken);
                        if (login.Disposition == OfficialHandlerDisposition.CloseConnection)
                        {
                            return login.CloseReason;
                        }

                        if (login.Disposition == OfficialHandlerDisposition.Handled)
                        {
                            var replicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                            if (replicationFailure is not null)
                            {
                                return replicationFailure;
                            }

                            continue;
                        }

                        if (await TryHandlePublicBetaCompatibilityGameplayRequest(
                                connection,
                                session,
                                frame.Bytes,
                                cancellationToken))
                        {
                            continue;
                        }

                        if (await TryHandleOfficialWorldChatAsync(connection, session, frame.Bytes, cancellationToken))
                        {
                            continue;
                        }

                        if (await TryHandleOfficialNpcInteractionAsync(connection, session, stream, frame.Bytes, cancellationToken))
                        {
                            var replicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                            if (replicationFailure is not null)
                            {
                                return replicationFailure;
                            }

                            continue;
                        }

                        if (await TryHandleOfficialMerchantTransactionAsync(connection, session, stream, frame.Bytes, cancellationToken))
                        {
                            var replicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                            if (replicationFailure is not null)
                            {
                                return replicationFailure;
                            }

                            continue;
                        }

                        if (await TryHandleOfficialPortalAsync(connection, session, stream, frame.Bytes, cancellationToken))
                        {
                            var replicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                            if (replicationFailure is not null)
                            {
                                return replicationFailure;
                            }

                            continue;
                        }

                        if (await TryHandleOfficialWorldMovementAsync(connection, session, stream, frame.Bytes, cancellationToken))
                        {
                            var replicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                            if (replicationFailure is not null)
                            {
                                return replicationFailure;
                            }

                            continue;
                        }

                        var result = await _protocolRuntime.DecodeAndRouteAsync(
                            new PacketRuntimeContext(
                                connection.ConnectionId,
                                session.SessionId,
                                session.ProtocolStage,
                                _rawEvidenceDirectory),
                            frame.Bytes,
                            cancellationToken);

                        switch (result.Status)
                        {
                            case PacketRouteStatus.Handled:
                                break;
                            case PacketRouteStatus.CapturedEvidence:
                                _logger.Log(
                                    ServerLogLevel.Information,
                                    $"Capture-backed packet recognized; signature={result.EvidenceSignatureId}; gameplay mutation remains evidence-blocked.",
                                    connection.ConnectionId,
                                    session.SessionId,
                                    session.ProtocolStage,
                                    "packet.current_build_evidence");
                                break;
                            case PacketRouteStatus.CapturedUnknown:
                                _logger.Log(
                                    ServerLogLevel.Warning,
                                    $"Unknown protocol packet recorded as metadata; payload length={result.Packet?.Payload.Length ?? 0}.",
                                    connection.ConnectionId,
                                    session.SessionId,
                                    session.ProtocolStage,
                                    "packet.unknown");
                                break;
                            case PacketRouteStatus.StageRejected:
                            case PacketRouteStatus.ProtocolError:
                                return result.Error.Code;
                        }

                        var pendingReplicationFailure = await FlushWorldReplicationAsync(connection, session, stream, cancellationToken);
                        if (pendingReplicationFailure is not null)
                        {
                            return pendingReplicationFailure;
                        }
                    }
                    finally
                    {
                        frame.Clear();
                    }
                }
            }

            return "server_shutdown";
        }
        finally
        {
            accumulator.Clear();
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private async Task<bool> TryHandlePublicBetaCompatibilityGameplayRequest(
        ActiveConnection connection,
        RuntimeSession session,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld ||
            session.ProtocolStage != ProtocolStage.InWorld ||
            frame.Length is not (
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectDecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementDecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FDecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95DecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestDecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetDecodedFrameLength or
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishDecodedFrameLength))
        {
            return false;
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldTransportFrame(frame.Span);
        try
        {
            if (decoded[2] is not (
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode))
            {
                return false;
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode)
            {
                var attributeIncrement = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!attributeIncrement.Adapted || attributeIncrement.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x21 compatibility request. failure={attributeIncrement.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        attributeIncrement.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0x21 to canonical attribute_increment. rawCode={attributeIncrement.Command.RawAttributeCode}; candidate={attributeIncrement.Command.PublicBetaCandidate}; mutationAllowed={attributeIncrement.Command.RuntimeMutationAllowed}; request={attributeIncrement.Command.RecordSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.attribute_increment_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.attribute_increment_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode)
            {
                var interaction27 = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction27(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!interaction27.Adapted || interaction27.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x27 compatibility request. failure={interaction27.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        interaction27.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0x27 to canonical interaction_27. raw={interaction27.Command.RawValue}; mutationAllowed={interaction27.Command.ProductionMutationAllowed}; request={interaction27.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.interaction_27_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.interaction_27_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28Opcode)
            {
                var interaction28 = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInventoryActivation(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!interaction28.Adapted || interaction28.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x28 compatibility request. failure={interaction28.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        interaction28.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0x28 to canonical interaction_28. payloadBytes={interaction28.Command.RawPayload.Length}; mutationAllowed={interaction28.Command.ProductionMutationAllowed}; request={interaction28.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.interaction_28_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.interaction_28_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode)
            {
                var gameplayDisconnect = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptGameplayDisconnect(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!gameplayDisconnect.Adapted || gameplayDisconnect.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x0A compatibility request. failure={gameplayDisconnect.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        gameplayDisconnect.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0x0A raw request to canonical disconnect. payloadBytes={gameplayDisconnect.Command.RawPayload.Length}; mutationAllowed={gameplayDisconnect.Command.ProductionMutationAllowed}; request={gameplayDisconnect.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.gameplay_disconnect_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.gameplay_disconnect_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode)
            {
                var interaction26 = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction26(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!interaction26.Adapted || interaction26.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x26 compatibility request. failure={interaction26.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        interaction26.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0x26 to canonical world_interaction_26. raw0={interaction26.Command.RawValue0}; raw2={interaction26.Command.RawValue2}; raw4={interaction26.Command.RawValue4}; tail={interaction26.Command.RawTail}; mutationAllowed={interaction26.Command.ProductionMutationAllowed}; request={interaction26.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.interaction_26_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.interaction_26_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode)
            {
                var combinationStep = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCombinationStep(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!combinationStep.Adapted || combinationStep.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0xA4 compatibility request. failure={combinationStep.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        combinationStep.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0xA4 to canonical combination_step. selector={combinationStep.Command.RawSelector}; step={combinationStep.Command.Step}; mutationAllowed={combinationStep.Command.ProductionMutationAllowed}; request={combinationStep.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.combination_step_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.combination_step_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode)
            {
                var vendorCartPublish = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartPublish(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!vendorCartPublish.Adapted || vendorCartPublish.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0xB0 compatibility request. failure={vendorCartPublish.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        vendorCartPublish.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0xB0 raw request to canonical command. payloadBytes={vendorCartPublish.Command.RawPayload.Length}; mutationAllowed={vendorCartPublish.Command.ProductionMutationAllowed}; request={vendorCartPublish.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.vendor_cart_publish_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.vendor_cart_publish_adapted");
            }

            if (decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode)
            {
                var petPkTarget = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPetPkTarget(
                    OfficialNpcReplicationWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    decoded);
                if (!petPkTarget.Adapted || petPkTarget.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0xB8 compatibility request. failure={petPkTarget.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        petPkTarget.FailureCode);
                    return true;
                }

                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0xB8 raw request to canonical pk target. payloadBytes={petPkTarget.Command.RawPayload.Length}; mutationAllowed={petPkTarget.Command.ProductionMutationAllowed}; request={petPkTarget.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "public_beta_compat.pet_pk_target_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    "public_beta_compat.pet_pk_target_adapted");
            }

            if (decoded[2] is PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode)
            {
                var rawRequest = decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode
                    ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest8F(
                        OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded)
                    : decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode
                        ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest90(
                            OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded)
                        : decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode
                            ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest91(
                                OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded)
                            : decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode
                                ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest92(
                                    OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded)
                                : PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest94(
                                    OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded);

                if (!rawRequest.Adapted || rawRequest.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x{decoded[2]:X2} compatibility request. failure={rawRequest.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId, session.SessionId, session.ProtocolStage, rawRequest.FailureCode);
                    return true;
                }

                var operationCode = rawRequest.Command.Opcode switch
                {
                    PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode => "public_beta_compat.party_request_8f_adapted",
                    PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode => "public_beta_compat.party_request_90_adapted",
                    PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode => "public_beta_compat.party_request_91_adapted",
                    PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode => "public_beta_compat.party_request_92_adapted",
                    _ => "public_beta_compat.party_request_94_adapted"
                };
                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build 0x{rawRequest.Command.Opcode:X2} raw request to canonical command. payloadBytes={rawRequest.Command.RawPayload.Length}; mutationAllowed={rawRequest.Command.ProductionMutationAllowed}; request={rawRequest.Command.DecodedFrameSha256}.",
                    connection.ConnectionId, session.SessionId, session.ProtocolStage, operationCode);
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    operationCode);
            }

            if (decoded[2] is PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode)
            {
                var rawRequest = decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode
                    ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartySelection(
                        OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded)
                    : decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode
                        ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptTeamRequest(
                            OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded)
                        : PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest95(
                            OfficialNpcReplicationWireCodec.ClientBuildId, GameplayProtocolState.World, decoded);
                if (!rawRequest.Adapted || rawRequest.Command is null)
                {
                    _logger.Log(ServerLogLevel.Warning,
                        $"Rejected malformed current-build 0x{decoded[2]:X2} compatibility request. failure={rawRequest.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId, session.SessionId, session.ProtocolStage, rawRequest.FailureCode);
                    return true;
                }

                _logger.Log(ServerLogLevel.Information,
                    $"Adapted current-build 0x{rawRequest.Command.Opcode:X2} raw request to canonical command. payloadBytes={rawRequest.Command.RawPayload.Length}; mutationAllowed={rawRequest.Command.ProductionMutationAllowed}; request={rawRequest.Command.DecodedFrameSha256}.",
                    connection.ConnectionId, session.SessionId, session.ProtocolStage,
                    rawRequest.Command.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode
                        ? "public_beta_compat.party_selection_adapted"
                        : rawRequest.Command.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode
                            ? "public_beta_compat.team_request_adapted"
                            : "public_beta_compat.party_request_95_adapted");
                return await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    rawRequest.Command.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode
                        ? "public_beta_compat.party_selection_adapted"
                        : rawRequest.Command.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode
                            ? "public_beta_compat.team_request_adapted"
                            : "public_beta_compat.party_request_95_adapted");
            }

            var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptClientCommand(
                OfficialNpcReplicationWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                decoded);
            if (!result.Adapted || result.Command is null)
            {
                _logger.Log(
                    ServerLogLevel.Warning,
                    $"Rejected malformed current-build 0x24 compatibility request. failure={result.FailureCode}; bytes={frame.Length}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    result.FailureCode);
                return true;
            }

            _logger.Log(
                ServerLogLevel.Information,
                $"Adapted current-build 0x24 to canonical interaction_24. raw0={result.Command.RawValue0}; raw1={result.Command.RawValue1}; mutationAllowed={result.Command.ProductionMutationAllowed}; request={result.Command.DecodedFrameSha256}.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "public_beta_compat.interaction_24_adapted");
            return await RouteCompatibilityGameplayFrameAsync(
                connection,
                session,
                frame,
                cancellationToken,
                "public_beta_compat.interaction_24_adapted");
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    private async Task<bool> RouteCompatibilityGameplayFrameAsync(
        ActiveConnection connection,
        RuntimeSession session,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken,
        string operationCode)
    {
        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldTransportFrame(frame.Span);
        try
        {
            var result = await _protocolRuntime.DecodeAndRouteAsync(
            new PacketRuntimeContext(
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                _rawEvidenceDirectory),
            decodedFrame,
            cancellationToken);

            switch (result.Status)
            {
                case PacketRouteStatus.Handled:
                    _logger.Log(
                        ServerLogLevel.Information,
                        $"Compatibility request routed through protocol runtime; status={result.Status}; signature={result.EvidenceSignatureId}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        operationCode);
                    break;
                case PacketRouteStatus.CapturedEvidence:
                    _logger.Log(
                        ServerLogLevel.Information,
                        $"Compatibility request matched evidence-backed packet; status={result.Status}; signature={result.EvidenceSignatureId}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        operationCode);
                    break;
                case PacketRouteStatus.CapturedUnknown:
                    _logger.Log(
                        ServerLogLevel.Warning,
                        "Compatibility request recognized as unknown packet during protocol routing.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        operationCode);
                    break;
                case PacketRouteStatus.HandlerMissing:
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Compatibility request has no protocol handler mapped yet; bytes={decodedFrame.Length}; status={result.Status}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        operationCode);
                    break;
                case PacketRouteStatus.StageRejected:
                case PacketRouteStatus.ProtocolError:
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"{operationCode} failed protocol routing. status={result.Status}; bytes={decodedFrame.Length}; code={result.Error.Code}; reason={result.Error.Message}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        result.Error.Code);
                    return false;
                default:
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Compatibility request returned protocol status {result.Status}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        operationCode);
                    break;
            }
        }
        finally
        {
            Array.Clear(decodedFrame);
        }

        return true;
    }

    private async Task<bool> TryHandleOfficialWorldChatAsync(
        ActiveConnection connection,
        RuntimeSession session,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld ||
            _officialWorldChatClosedLoop is null ||
            !OfficialWorldChatWireCodec.RecognizesEncodedFrame(frame.Span))
        {
            return false;
        }

        var ordinal = connection.WorldChatIngressOrdinal;
        connection.WorldChatIngressOrdinal = checked(ordinal + 1);
        var receipt = _officialWorldChatClosedLoop.ExecuteFrame(session.SessionId, ordinal, frame);
        if (receipt.Code == OfficialWorldChatTransactionCode.Rejected)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Rejected official general chat. ordinal={ordinal}; failure={receipt.FailureCode}; bytes={frame.Length}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                receipt.FailureCode);
            return true;
        }

        var sent = 0;
        foreach (var delivery in receipt.Deliveries)
        {
            var recipient = _connections.Values.FirstOrDefault(value =>
                value.Role == ConnectionRole.InWorld &&
                string.Equals(value.SessionId, delivery.RecipientSessionId, StringComparison.Ordinal));
            if (recipient is null)
            {
                _officialWorldChatClosedLoop.RemoveSession(delivery.RecipientSessionId);
                continue;
            }

            try
            {
                await recipient.SendAsync(delivery.EncodedResponse, cancellationToken);
                sent++;
            }
            catch (Exception exception) when (
                exception is IOException or SocketException or ObjectDisposedException)
            {
                _officialWorldChatClosedLoop.RemoveSession(delivery.RecipientSessionId);
                _logger.Log(
                    ServerLogLevel.Warning,
                    "Official general-chat delivery ended on a closed recipient transport.",
                    recipient.ConnectionId,
                    delivery.RecipientSessionId,
                    ProtocolStage.InWorld,
                    exception.GetType().Name);
            }
        }

        _logger.Log(
            ServerLogLevel.Information,
            $"Handled official general chat. ordinal={ordinal}; recipients={sent}; duplicate={receipt.Code == OfficialWorldChatTransactionCode.DuplicateIgnored}; request={receipt.RequestSha256}.",
            connection.ConnectionId,
            session.SessionId,
            ProtocolStage.InWorld,
            receipt.Code == OfficialWorldChatTransactionCode.DuplicateIgnored
                ? "official_world_chat_duplicate_ignored"
                : "official_world_chat_delivered");
        return true;
    }

    private async Task<bool> TryHandleOfficialNpcInteractionAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld ||
            _npcInteractionClosedLoop is null ||
            (!OfficialNpcInteractionClosedLoop.RecognizesEncodedInteraction(frame.Span) &&
             !_npcInteractionClosedLoop.RecognizesEncodedDialogSelection(session.SessionId, frame.Span)))
        {
            return false;
        }

        var ordinal = connection.NpcInteractionIngressOrdinal;
        connection.NpcInteractionIngressOrdinal = checked(ordinal + 1);
        var receipt = await _npcInteractionClosedLoop.ExecuteFrameAsync(
            session.SessionId,
            ordinal,
            $"npc-dialog:{connection.ConnectionId}:{ordinal}",
            frame,
            cancellationToken);
        if (receipt.Code == OfficialNpcInteractionTransactionCode.Rejected)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Rejected official NPC interaction. ordinal={ordinal}; handle={receipt.ClientEntityHandle}; failure={receipt.FailureCode}; bytes={frame.Length}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                receipt.FailureCode);
            return true;
        }

        if (!receipt.EncodedResponse.IsEmpty)
        {
            await connection.SendAsync(receipt.EncodedResponse, cancellationToken);
        }
        _logger.Log(
            ServerLogLevel.Information,
            $"Handled official NPC interaction. ordinal={ordinal}; action={receipt.Code}; handler={receipt.Handler}; handle={receipt.ClientEntityHandle}; runtimeEntity={receipt.TargetRuntimeEntityId}; map={receipt.RuntimeMapId}; bytes={receipt.EncodedResponse.Length}.",
            connection.ConnectionId,
            session.SessionId,
            ProtocolStage.InWorld,
            receipt.Code switch
            {
                OfficialNpcInteractionTransactionCode.InteractionClosed => "official_npc_interaction_closed",
                OfficialNpcInteractionTransactionCode.DialogOptionSelected => "official_npc_dialog_option_selected",
                _ => "official_npc_interaction_opened"
            });
        return true;
    }

    private async Task<bool> TryHandleOfficialMerchantTransactionAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld ||
            _merchantTransactionClosedLoop is null ||
            !OfficialMerchantTransactionClosedLoop.RecognizesEncodedFrame(frame.Span))
        {
            return false;
        }

        var ordinal = connection.MerchantTransactionIngressOrdinal;
        connection.MerchantTransactionIngressOrdinal = checked(ordinal + 1);
        var receipt = await _merchantTransactionClosedLoop.ExecuteFrameAsync(
            session.SessionId,
            ordinal,
            frame,
            cancellationToken);
        if (receipt.Code == OfficialMerchantTransactionCode.Rejected)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Rejected official Merchant transaction. ordinal={ordinal}; handle={receipt.ClientEntityHandle}; item={receipt.ItemTemplateId}; quantity={receipt.Quantity}; failure={receipt.FailureCode}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                receipt.FailureCode);
            return true;
        }

        if (!receipt.EncodedResponse.IsEmpty)
        {
            await connection.SendAsync(receipt.EncodedResponse, cancellationToken);
        }

        _logger.Log(
            ServerLogLevel.Information,
            $"Handled official Merchant transaction. ordinal={ordinal}; action={receipt.Code}; handle={receipt.ClientEntityHandle}; merchant={receipt.MerchantTemplateId}; item={receipt.ItemTemplateId}; quantity={receipt.Quantity}; currencyAfter={receipt.CurrencyAfter}; bytes={receipt.EncodedResponse.Length}.",
            connection.ConnectionId,
            session.SessionId,
            ProtocolStage.InWorld,
            receipt.Code == OfficialMerchantTransactionCode.ShopOpened
                ? "official_merchant_shop_opened"
                : "official_merchant_transaction_committed");
        return true;
    }

    private async Task<bool> TryHandleOfficialPortalAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld ||
            _portalClosedLoop is null ||
            frame.Length != 8 ||
            frame.Span[0] != 8 ||
            frame.Span[1] != 0 ||
            frame.Span[2] != OfficialPortalWireCodec.ActivateOpcode)
        {
            return false;
        }

        if (connection.PortalEligibilityInvalidatedByUnverifiedMovement)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected official portal command because authoritative walking coordinates are not synchronized for this connection.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "wire.portal.position_unsynchronized");
            return true;
        }

        var sequence = connection.PortalActionSequence;
        var correlationId = $"portal:{connection.ConnectionId}:{sequence}";
        var ingress = await _portalClosedLoop.ExecuteFrameAsync(
            session.SessionId,
            sequence,
            correlationId,
            frame,
            cancellationToken);
        if (!ingress.Succeeded)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Rejected official portal command. sequence={sequence}; failure={ingress.Receipt.FailureCode}; bytes={frame.Length}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                ingress.Receipt.FailureCode);
            return true;
        }

        if (ingress.Receipt.Code == OfficialPortalTransactionCode.Committed)
        {
            var rebound = await RebindAuthoritativeWorldAfterPortalAsync(session, cancellationToken);
            if (!rebound.Succeeded)
            {
                _logger.Log(
                    ServerLogLevel.Error,
                    $"Official portal committed but authoritative MapRuntime rebinding failed closed. failure={rebound.Error.Code}.",
                    connection.ConnectionId,
                    session.SessionId,
                    ProtocolStage.InWorld,
                    rebound.Error.Code);
                throw new InvalidOperationException("Authoritative post-portal world rebinding failed.");
            }
        }

        var dispatched = _portalClosedLoop.MarkOutboxDispatched(ingress.Receipt);
        if (!dispatched.OutboxDispatched)
        {
            _logger.Log(
                ServerLogLevel.Error,
                $"Official portal outbox dispatch failed. sequence={sequence}; failure={dispatched.FailureCode}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                dispatched.FailureCode);
            return true;
        }

        foreach (var responseFrame in dispatched.OrderedEncodedFrames)
        {
            await connection.SendAsync(responseFrame, cancellationToken);
        }

        var sent = _portalClosedLoop.MarkNetworkSent(dispatched);
        if (sent.Code == OfficialPortalTransactionCode.Committed)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = false;
            connection.PortalAwaitingMovementFollowUp = true;
            _npcInteractionClosedLoop?.MarkPositionSynchronized(session.SessionId);
        }
        _logger.Log(
            ServerLogLevel.Information,
            $"Handled official portal closed loop. sequence={sequence}; sourceMap={sent.SourceRuntimeMapId}; targetMap={sent.TargetRuntimeMapId}; frames={sent.OrderedEncodedFrames.Count}; bytes={sent.OrderedEncodedFrames.Sum(value => value.Length)}; duplicate={sent.Code == OfficialPortalTransactionCode.DuplicateCommitted}; runtimeMutations={sent.RuntimeMutationCount}; networkSends={sent.NetworkSendCount}.",
            connection.ConnectionId,
            session.SessionId,
            ProtocolStage.InWorld,
            "official_portal_committed");
        return true;
    }

    private async Task<OperationResult<WorldSessionBinding>> RebindAuthoritativeWorldAfterPortalAsync(
        RuntimeSession session,
        CancellationToken cancellationToken)
    {
        if (_portalTransitionStore is null ||
            _worldSessionCoordinator is not IWorldSessionTransitionCoordinator transitions ||
            session.CharacterId is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.portal_rebind_unavailable",
                "Production portal rebinding composition is unavailable.");
        }

        var current = _worldSessionCoordinator.GetBinding(session.SessionId);
        if (!current.Succeeded || current.Value is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                current.Error.Code,
                current.Error.Message,
                current.Error.Source);
        }

        PortalLocationState persisted;
        try
        {
            persisted = await _portalTransitionStore.LoadAsync(session.CharacterId.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.portal_location_reload_failed",
                "The committed portal destination could not be reloaded.",
                exception.GetType().Name);
        }

        var character = current.Value.Character with
        {
            MapId = persisted.CurrentMapId,
            PositionX = persisted.RawPosition.X,
            PositionY = persisted.RawPosition.Y
        };
        return await transitions.RebindAsync(session, character, cancellationToken);
    }

    private async Task<bool> TryHandleOfficialWorldMovementAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld ||
            !OfficialClientWorldProtocolFrames.TryDecodeWorldMovement(frame.Span, out var movement))
        {
            return false;
        }

        if (connection.PortalAwaitingMovementFollowUp)
        {
            connection.LastWorldMovementSequence = movement.Sequence;
            connection.WorldMovementCount++;
            connection.PortalAwaitingMovementFollowUp = false;
            connection.PortalActionSequence = checked(connection.PortalActionSequence + 1);
            var rebound = _worldSessionCoordinator?.GetBinding(session.SessionId);
            var reboundPlayer = rebound is { Succeeded: true, Value: not null }
                ? rebound.Value.MapRuntime.Objects.Get(rebound.Value.MapSession.PlayerRuntimeEntityId)
                : null;
            if (reboundPlayer is { Succeeded: true, Value: PlayerObject reboundPlayerObject } &&
                reboundPlayerObject.State.Position.X is >= 0 and <= ushort.MaxValue &&
                reboundPlayerObject.State.Position.Y is >= 0 and <= ushort.MaxValue)
            {
                connection.WorldXCandidate = checked((ushort)reboundPlayerObject.State.Position.X);
                connection.WorldYCandidate = checked((ushort)reboundPlayerObject.State.Position.Y);
                connection.PortalEligibilityInvalidatedByUnverifiedMovement = false;
                _npcInteractionClosedLoop?.MarkPositionSynchronized(session.SessionId);
            }
            else
            {
                connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
                _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            }
            _logger.Log(
                ServerLogLevel.Information,
                $"Observed official post-portal movement follow-up. sequence={movement.Sequence}; authoritativeX={connection.WorldXCandidate}; authoritativeY={connection.WorldYCandidate}; runtimeRebound={connection.PortalEligibilityInvalidatedByUnverifiedMovement == false}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_portal_followup_observed");
            return true;
        }

        if (_worldSessionCoordinator is null || _worldMovementStore is null || session.CharacterId is null)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Error,
                "Rejected official world movement because authoritative movement composition is unavailable.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_authority_unavailable");
            return true;
        }

        var bound = _worldSessionCoordinator.GetBinding(session.SessionId);
        if (!bound.Succeeded || bound.Value is null)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected official world movement because the session has no authoritative MapRuntime binding.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_binding_missing");
            return true;
        }

        var binding = bound.Value;
        var playerLookup = binding.MapRuntime.Objects.Get(binding.MapSession.PlayerRuntimeEntityId);
        var entityLookup = binding.MapRuntime.EntityRegistry.Get(binding.MapSession.PlayerRuntimeEntityId);
        if (!playerLookup.Succeeded || playerLookup.Value is not PlayerObject player ||
            !entityLookup.Succeeded || entityLookup.Value is not { EntityType: WorldRuntimeEntityType.Player } playerEntity ||
            player.State.CharacterId != session.CharacterId.Value ||
            player.State.MapId != binding.MapSession.MapId ||
            playerEntity.MapId != binding.MapSession.MapId)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected official world movement because the bound player runtime identity is inconsistent.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_runtime_identity_invalid");
            return true;
        }

        var expectedSequence = connection.WorldMovementCount == 0
            ? movement.Sequence
            : unchecked((byte)(connection.LastWorldMovementSequence + 1));
        var sequenceAdvance = connection.WorldMovementCount == 0
            ? 1
            : unchecked((byte)(movement.Sequence - connection.LastWorldMovementSequence));
        if (connection.WorldMovementCount > 0 &&
            (sequenceAdvance == 0 || sequenceAdvance > 127))
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Warning,
                $"Rejected official world movement sequence. expected={expectedSequence}; actual={movement.Sequence}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_sequence_rejected");
            return true;
        }

        if (movement.Sequence != expectedSequence)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Resynchronized a forward official world movement sequence gap. expected={expectedSequence}; actual={movement.Sequence}; advance={sequenceAdvance}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_sequence_resynchronized");
        }

        var reportedPosition = new WorldPosition3(movement.XCandidate, movement.YCandidate, player.State.Position.Z);
        WorldPosition3? portalTriggerPosition = null;
        var portalMovementTriggered = _portalClosedLoop is not null &&
            _portalClosedLoop.TryResolveMovementTrigger(
                session.SessionId,
                player.State.Position,
                reportedPosition,
                out portalTriggerPosition);
        var nextPosition = portalMovementTriggered ? portalTriggerPosition! : reportedPosition;
        var bounds = binding.MapRuntime.Definition.Bounds;
        if (nextPosition.X < bounds.MinX || nextPosition.X > bounds.MaxX ||
            nextPosition.Y < bounds.MinY || nextPosition.Y > bounds.MaxY)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Warning,
                $"Rejected official world movement outside authoritative map bounds. x={nextPosition.X}; y={nextPosition.Y}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_bounds_rejected");
            return true;
        }

        // The official client reports the clicked path destination, not one grid cell per frame.
        var deltaX = nextPosition.X - player.State.Position.X;
        var deltaY = nextPosition.Y - player.State.Position.Y;
        var direction = ResolveWorldDirection(deltaX, deltaY, player.State.Direction);
        OperationResult persisted;
        try
        {
            persisted = await _worldMovementStore.CommitMovementAsync(
                new WorldMovementPersistenceRequest(
                    session.CharacterId.Value,
                    binding.MapSession.MapId,
                    player.State.Position,
                    nextPosition,
                    direction,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Error,
                $"Official world movement persistence failed closed. exception={exception.GetType().Name}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_movement_persistence_exception");
            return true;
        }

        if (!persisted.Succeeded)
        {
            connection.PortalEligibilityInvalidatedByUnverifiedMovement = true;
            _npcInteractionClosedLoop?.MarkPositionUnsynchronized(session.SessionId);
            _logger.Log(
                ServerLogLevel.Warning,
                $"Official world movement persistence rejected the commit. failure={persisted.Error.Code}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                persisted.Error.Code);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var updatedPlayer = player with
        {
            State = player.State with
            {
                Position = nextPosition,
                Direction = direction
            }
        };
        var updatedEntity = playerEntity with
        {
            Position = nextPosition,
            Direction = direction,
            DirtyFlags = "AuthoritativeMovement"
        };
        binding.MapRuntime.Objects.Add(updatedPlayer);
        binding.MapRuntime.EntityRegistry.Add(updatedEntity);
        binding.MapRuntime.Replication.RecalculateVisibility(
            binding.MapSession,
            nextPosition,
            binding.MapRuntime.Objects.ActiveObjects,
            updatedPlayer.State.VisibilityRadius,
            now);
        var portalMovementSync = _portalClosedLoop?.SynchronizeMovement(
            session.SessionId,
            player.State.Position,
            nextPosition,
            direction);

        connection.WorldXCandidate = movement.XCandidate;
        connection.WorldYCandidate = movement.YCandidate;
        connection.LastWorldMovementSequence = movement.Sequence;
        connection.WorldMovementCount++;
        connection.PortalEligibilityInvalidatedByUnverifiedMovement = portalMovementSync?.Succeeded != true;
        _npcInteractionClosedLoop?.MarkPositionSynchronized(session.SessionId);

        var acknowledgement = OfficialClientWorldProtocolFrames.BuildWorldMovementAcknowledgement(movement.Sequence);
        await connection.SendAsync(acknowledgement, cancellationToken);
        if (portalMovementSync is { Succeeded: false })
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Portal runtime movement synchronization failed closed. failure={portalMovementSync.Error.Code}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                portalMovementSync.Error.Code);
        }

        if (portalMovementSync?.Succeeded == true && portalMovementTriggered)
        {
            // The official flow emits portal activation only after the client has
            // consumed its movement acknowledgement. Keep the transition out of the
            // same immediate receive batch so the legacy client can advance state.
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            var syntheticActivation = OfficialClientWorldProtocolFrames.EncodeWorldClientFrame(
                Convert.FromHexString("08000CA82734FC9C"));
            await TryHandleOfficialPortalAsync(
                connection,
                session,
                stream,
                syntheticActivation,
                cancellationToken);
        }
        _logger.Log(
            ServerLogLevel.Information,
            $"Committed official world movement and sent acknowledgement. bytes={frame.Length}; acknowledgementBytes={acknowledgement.Length}; sequence={movement.Sequence}; x={movement.XCandidate}; y={movement.YCandidate}; direction={direction}; portalRuntimeSynchronized={portalMovementSync?.Succeeded == true}; movementCount={connection.WorldMovementCount}; observedSessionStage={session.ProtocolStage}.",
            connection.ConnectionId,
            session.SessionId,
            ProtocolStage.InWorld,
            "world_movement_committed");
        return true;
    }

    internal static WorldDirection ResolveWorldDirection(int deltaX, int deltaY, WorldDirection current) =>
        (Math.Sign(deltaX), Math.Sign(deltaY)) switch
        {
            (0, -1) => WorldDirection.North,
            (0, 1) => WorldDirection.South,
            (1, 0) => WorldDirection.East,
            (-1, 0) => WorldDirection.West,
            (1, -1) => WorldDirection.NorthEast,
            (-1, -1) => WorldDirection.NorthWest,
            (1, 1) => WorldDirection.SouthEast,
            (-1, 1) => WorldDirection.SouthWest,
            _ => current
        };

    private async Task<OfficialHandlerResult> TryHandleOfficialLoginRequestAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (session.ProtocolStage != ProtocolStage.Login || frame.Length != 208)
        {
            return OfficialHandlerResult.NotHandled;
        }

        if (_authenticationService is null || _characterListQuery is null)
        {
            _logger.Log(
                ServerLogLevel.Error,
                "Official login rejected because the production authentication pipeline is unavailable.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "login.authentication_pipeline_unavailable");
            return OfficialHandlerResult.Close("login_authentication_pipeline_unavailable");
        }

        if (!OfficialClientLoginProtocolFrames.TryDecodeSensitiveLoginRequest(frame.Span, out var username, out var password))
        {
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected malformed official login request.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "login.request_invalid");
            return OfficialHandlerResult.Close("login_request_invalid");
        }

        var opcodeCandidate = frame.Length >= 4
            ? Convert.ToHexString(frame.Span.Slice(2, 2))
            : string.Empty;
        var frameHash = PacketEvidenceHash.Sha256Hex(frame.Span);
        _logger.Log(
            ServerLogLevel.Information,
            $"Received official login request candidate. bytes={frame.Length}; opcodeCandidate={opcodeCandidate}; sha256={frameHash}; decodedUsernameLength={username.Length}; decodedUsernameSha256={ComputeNormalizedUsernameHash(username)}.",
            connection.ConnectionId,
            session.SessionId,
            session.ProtocolStage,
            "login_request_208_received");

        LoginResult loginResult;
        try
        {
            loginResult = await _authenticationService.LoginSensitiveAsync(
                username,
                password,
                frameHash,
                session,
                _characterListQuery,
                cancellationToken);
        }
        finally
        {
            Array.Clear(password);
        }

        if (!loginResult.Succeeded)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                "Official login authentication rejected.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                loginResult.Code.ToString());

            if (TryMapOfficialLoginFailure(loginResult.Code, out var officialFailureCode))
            {
                var loginFailureResponse = OfficialClientLoginProtocolFrames.BuildLoginFailureResponse(officialFailureCode);
                try
                {
                    await connection.SendAsync(loginFailureResponse, cancellationToken);
                    _logger.Log(
                        ServerLogLevel.Information,
                        $"Sent official login rejection response. bytes={loginFailureResponse.Length}; result={officialFailureCode}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        "login.rejection_sent");
                    return OfficialHandlerResult.Handled;
                }
                finally
                {
                    Array.Clear(loginFailureResponse);
                }
            }

            return OfficialHandlerResult.Close($"login_{loginResult.Code.ToString().ToLowerInvariant()}");
        }

        if (loginResult.Characters.Count > 1)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Official login rejected because the recovered current-build character-list serializer supports zero or one active character; count={loginResult.Characters.Count}.",
                connection.ConnectionId,
                loginResult.Session.SessionId,
                loginResult.Session.ProtocolStage,
                "login.character_count_unsupported");
            return OfficialHandlerResult.Close("login_character_count_unsupported");
        }

        if (connection.Client.Client.LocalEndPoint is not IPEndPoint localEndpoint)
        {
            return OfficialHandlerResult.Close("login_advertised_endpoint_unavailable");
        }

        var advertisedAddress = _advertisedIpAddress ?? localEndpoint.Address;
        if (advertisedAddress.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.Any.Equals(advertisedAddress))
        {
            return OfficialHandlerResult.Close("login_advertised_endpoint_unavailable");
        }

        var characterCreationReconnect = loginResult.Characters.Count == 0 &&
            TryConsumePendingCharacterCreationLogin(connection, loginResult.Session.AccountId!.Value);
        var loginSuccessBootstrap = characterCreationReconnect
            ? OfficialClientLoginProtocolFrames.BuildCharacterCreationBootstrap()
            : OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap(
                frame,
                advertisedAddress.GetAddressBytes(),
                checked((ushort)localEndpoint.Port));
        try
        {
            await connection.SendAsync(loginSuccessBootstrap, cancellationToken);
            connection.Role = ConnectionRole.CharacterSelect;
            connection.AuthenticatedCharacters = loginResult.Characters.ToArray();
            _logger.Log(
                ServerLogLevel.Information,
                $"Sent verified official login success/character bootstrap. bytes={loginSuccessBootstrap.Length}; characterCount={loginResult.Characters.Count}; advertisedEndpoint={advertisedAddress}:{localEndpoint.Port}; characterCreationReconnect={characterCreationReconnect}; echoSource=LoginRequest208.",
                connection.ConnectionId,
                loginResult.Session.SessionId,
                loginResult.Session.ProtocolStage,
                "login_success_bootstrap_sent");
            return OfficialHandlerResult.Handled;
        }
        finally
        {
            Array.Clear(loginSuccessBootstrap);
        }
    }

    private static bool TryMapOfficialLoginFailure(
        LoginResultCode resultCode,
        out OfficialLoginFailureCode officialFailureCode)
    {
        officialFailureCode = resultCode switch
        {
            // Message 189 deliberately covers both a bad password and an unknown account,
            // preserving the authentication boundary without exposing account existence.
            LoginResultCode.InvalidCredentials or LoginResultCode.AccountNotFound or LoginResultCode.AccountLocked =>
                OfficialLoginFailureCode.CredentialsRejected,
            LoginResultCode.AccountDisabled => OfficialLoginFailureCode.AccountStopped,
            LoginResultCode.AlreadyOnline => OfficialLoginFailureCode.DuplicateLogin,
            LoginResultCode.ServerFull => OfficialLoginFailureCode.ServerFull,
            _ => default
        };

        return resultCode is
            LoginResultCode.InvalidCredentials or
            LoginResultCode.AccountNotFound or
            LoginResultCode.AccountLocked or
            LoginResultCode.AccountDisabled or
            LoginResultCode.AlreadyOnline or
            LoginResultCode.ServerFull;
    }

    private async Task<OfficialHandlerResult> TryHandleOfficialCharacterLifecycleAsync(
        ActiveConnection connection,
        RuntimeSession session,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.CharacterSelect ||
            session.ProtocolStage != ProtocolStage.CharacterList ||
            !session.IsAuthenticated ||
            session.AccountId is null)
        {
            return OfficialHandlerResult.NotHandled;
        }

        if (frame.Length == 48 &&
            OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(frame.Span, out var create))
        {
            if (_characterRuntimeService is null)
            {
                _logger.Log(
                    ServerLogLevel.Error,
                    "Rejected official character-create request because the lifecycle service is unavailable.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "character.create_service_unavailable");
                return OfficialHandlerResult.Close("character_create_service_unavailable");
            }

            var requestHash = Convert.ToHexString(SHA256.HashData(frame.Span));
            var result = await _characterRuntimeService.CreateAsync(
                session,
                new CharacterCreateRequest(
                    create.Name,
                    create.Class,
                    create.Gender,
                    create.LifeSkill,
                    create.Appearance,
                    $"official-create:{requestHash}"),
                cancellationToken);
            if (!result.Succeeded || result.Character is null)
            {
                _logger.Log(
                    ServerLogLevel.Warning,
                    $"Rejected official character-create request. result={result.Code}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    $"character.create_{result.Code.ToString().ToLowerInvariant()}");
                return OfficialHandlerResult.Close($"character_create_{result.Code.ToString().ToLowerInvariant()}");
            }

            connection.AuthenticatedCharacters = [result.Character];
            _logger.Log(
                ServerLogLevel.Information,
                $"Created official-client character from the verified current-build profile. characterId={result.Character.CharacterId}; reconnectRequired=true.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "character.create_committed_reconnect_required");
            return OfficialHandlerResult.Close("character_created_reconnect_required");
        }

        if (frame.Length == 5 &&
            OfficialClientWorldProtocolFrames.TryDecodeCharacterSlotAction(frame.Span, out var slot))
        {
            _logger.Log(
                ServerLogLevel.Information,
                $"Observed official character-slot action. slot={slot}; no lifecycle mutation was performed.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "character.slot_action_observed");
            return OfficialHandlerResult.Handled;
        }

        if (frame.Length is 5 or 41 or 48 or 57)
        {
            var decoded = OfficialClientWorldProtocolFrames.DecodeWorldTransportFrame(frame.Span);
            try
            {
                var rawRequest = decoded.Length == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteDecodedFrameLength &&
                    decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode
                        ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterDeleteRequest(
                            OfficialNpcReplicationWireCodec.ClientBuildId,
                            GameplayProtocolState.World,
                            decoded)
                        : decoded.Length == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectDecodedFrameLength &&
                            decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode
                                ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterSelectRequest(
                                    OfficialNpcReplicationWireCodec.ClientBuildId,
                                    GameplayProtocolState.World,
                                    decoded)
                                : decoded.Length == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bDecodedFrameLength &&
                                    decoded[2] == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bRequestOpcode
                                        ? PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterCreate1bRequest(
                                            OfficialNpcReplicationWireCodec.ClientBuildId,
                                            GameplayProtocolState.World,
                                            decoded)
                                        : null;

                if (rawRequest is null)
                {
                    if (frame.Length is 5 or 48)
                    {
                        _logger.Log(
                            ServerLogLevel.Warning,
                            $"Rejected malformed official character lifecycle frame. bytes={frame.Length}.",
                            connection.ConnectionId,
                            session.SessionId,
                            session.ProtocolStage,
                            "character.lifecycle_frame_invalid");
                        return OfficialHandlerResult.Close("character_lifecycle_frame_invalid");
                    }

                    return OfficialHandlerResult.NotHandled;
                }

                if (!rawRequest.Adapted || rawRequest.Command is null)
                {
                    _logger.Log(
                        ServerLogLevel.Warning,
                        $"Rejected malformed current-build character lifecycle compatibility request. failure={rawRequest.FailureCode}; bytes={frame.Length}.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        rawRequest.FailureCode);
                    return OfficialHandlerResult.Close("character_lifecycle_frame_invalid");
                }

                var operationCode = rawRequest.Command.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode
                    ? "public_beta_compat.character_delete_request_adapted"
                    : rawRequest.Command.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode
                        ? "public_beta_compat.character_select_request_adapted"
                        : "public_beta_compat.character_create_1b_request_adapted";
                _logger.Log(
                    ServerLogLevel.Information,
                    $"Adapted current-build character lifecycle request through compatibility adapter. opcode=0x{rawRequest.Command.Opcode:X2}; bytes={frame.Length}; role={connection.Role}; request={rawRequest.Command.DecodedFrameSha256}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    operationCode);
                await RouteCompatibilityGameplayFrameAsync(
                    connection,
                    session,
                    frame,
                    cancellationToken,
                    operationCode);
                return OfficialHandlerResult.Handled;
            }
            finally
            {
                Array.Clear(decoded);
            }
        }

        return OfficialHandlerResult.NotHandled;
    }

    private async Task<bool> TryHandleOfficialServerSelectionAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.CharacterSelect ||
            session.ProtocolStage != ProtocolStage.CharacterList ||
            !session.IsAuthenticated ||
            session.AccountId is null ||
            connection.AuthenticatedCharacters.Count > 1 ||
            frame.Length != OfficialServerSelectionWireCodec.RequestFrameLength)
        {
            return false;
        }

        var selectedCharacter = connection.AuthenticatedCharacters.SingleOrDefault();
        if (selectedCharacter is not null && selectedCharacter.AccountId != session.AccountId.Value)
        {
            _logger.Log(
                ServerLogLevel.Error,
                "Rejected character bootstrap because the selected character does not belong to the authenticated account.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "character.ownership_mismatch");
            return true;
        }

        var decoded = OfficialClientLoginProtocolFrames.DecodeLoginFrame(frame.Span);
        var isServerSelection = decoded.Length >= 3 && decoded[2] == OfficialServerSelectionWireCodec.RequestOpcode;
        var decodedOpcode = decoded.Length >= 3
            ? $"0x{decoded[2]:X2}"
            : "n/a";
        Array.Clear(decoded);
        if (!isServerSelection)
        {
            return false;
        }
        var request = OfficialServerSelectionWireCodec.DecodeRequest(
            frame.Span,
            session.ProtocolStage,
            OfficialServerSelectionWireCodec.ClientBuildId);
        if (!request.Succeeded || request.Value is null)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected malformed official server-selection request.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                request.Error.Code);
            return true;
        }

        if (selectedCharacter is not null &&
            !TryReservePendingWorldConnection(
                connection,
                session,
                request.Value.SelectedServerId,
                selectedCharacter))
        {
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected server selection because another world transition is already pending for this remote address.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                "world.pending_ambiguous");
            return true;
        }

        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Span));
        var correlationId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{session.SessionId}|server-selection|1")));
        var responseModel = selectedCharacter is null
            ? OperationResult<OfficialCharacterListBootstrapWireModel>.Success(
                OfficialServerSelectionWireCodec.EmptyCharacterResponseModel())
            : _officialWorldWireProfile switch
            {
                OfficialWorldWireProfile.CurrentCreateEvidence =>
                    OperationResult<OfficialCharacterListBootstrapWireModel>.Success(
                        OfficialServerSelectionWireCodec.SingleCharacterResponseModel()),
                OfficialWorldWireProfile.LegacyMap19GoldenIdentityEvidence =>
                    OperationResult<OfficialCharacterListBootstrapWireModel>.Success(
                        OfficialServerSelectionWireCodec.GoldenResponseModel()),
                _ => OfficialServerSelectionWireCodec.SingleCharacterResponseModel(selectedCharacter.Class)
            };
        if (!responseModel.Succeeded || responseModel.Value is null)
        {
            RemovePendingWorldConnection(connection.ConnectionId);
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected character bootstrap because the official client class profile is not proven.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                responseModel.Error.Code);
            connection.Role = ConnectionRole.Closed;
            connection.Client.Dispose();
            return true;
        }

        var receipt = _serverSelectionClosedLoop.Commit(
            new OfficialServerSelectionCanonicalCommand(
                session.SessionId,
                ClientSequence: 1,
                correlationId,
                request.Value.SelectedServerId,
                OfficialServerSelectionWireCodec.ClientBuildId,
                requestHash),
            responseModel.Value with
            {
                CharacterName = selectedCharacter is null
                    ? string.Empty
                    : _officialWorldWireProfile == OfficialWorldWireProfile.LegacyMap19GoldenIdentityEvidence
                    ? OfficialServerSelectionWireCodec.GoldenResponseModel().CharacterName
                    : selectedCharacter.Name
            });
        if (!receipt.TransactionCommitted || receipt.Code == OfficialServerSelectionTransactionCode.ReplayConflict)
        {
            RemovePendingWorldConnection(connection.ConnectionId);
            _logger.Log(
                ServerLogLevel.Warning,
                "Rejected official server-selection transaction.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                receipt.FailureCode);
            return true;
        }

        receipt = _serverSelectionClosedLoop.DispatchOutbox(
            receipt,
            _ => { });
        if (!receipt.OutboxDispatched)
        {
            RemovePendingWorldConnection(connection.ConnectionId);
            _logger.Log(
                ServerLogLevel.Error,
                "Official server-selection outbox dispatch failed.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage,
                receipt.FailureCode);
            return true;
        }

        if (selectedCharacter is null)
        {
            MarkPendingCharacterCreationLogin(connection, session.AccountId.Value);
            await CloseLatestSessionAsync(
                session.SessionId,
                "character_creation_reconnect_required",
                cancellationToken);
            connection.CloseAfterCharacterCreationList = true;
        }

        var characterListBootstrap = Convert.FromHexString(receipt.ResponseHex);
        try
        {
            await connection.SendAsync(characterListBootstrap, cancellationToken);
            receipt = _serverSelectionClosedLoop.MarkNetworkSent(receipt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(characterListBootstrap);
        }
        _logger.Log(
            ServerLogLevel.Information,
            $"Handled official server-selection request. bytes={frame.Length}; decodedOpcode={decodedOpcode}; selectedServerId={request.Value.SelectedServerId}; characterCount={connection.AuthenticatedCharacters.Count}; responseBytes={characterListBootstrap.Length}; transactionCommitted={receipt.TransactionCommitted}; runtimeMutations={receipt.RuntimeMutationCount}; pendingWorld={selectedCharacter is not null}.",
            connection.ConnectionId,
            session.SessionId,
            session.ProtocolStage,
            "server_selection_character_list_sent");
        return true;
    }

    private void MarkPendingCharacterCreationLogin(ActiveConnection connection, long accountId)
    {
        var remoteAddress = GetRemoteAddress(connection.Client.Client.RemoteEndPoint);
        if (remoteAddress != "unknown")
        {
            _pendingCharacterCreationLogins[$"{remoteAddress}|{accountId}"] = DateTimeOffset.UtcNow.AddMinutes(2);
        }
    }

    private bool HasPendingCharacterCreationLogin(string remoteAddress)
    {
        if (remoteAddress == "unknown")
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var prefix = $"{remoteAddress}|";
        var found = false;
        foreach (var entry in _pendingCharacterCreationLogins)
        {
            if (entry.Value <= now)
            {
                _pendingCharacterCreationLogins.TryRemove(entry.Key, out _);
                continue;
            }

            found |= entry.Key.StartsWith(prefix, StringComparison.Ordinal);
        }

        return found;
    }

    private bool TryConsumePendingCharacterCreationLogin(ActiveConnection connection, long accountId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _pendingCharacterCreationLogins)
        {
            if (entry.Value <= now)
            {
                _pendingCharacterCreationLogins.TryRemove(entry.Key, out _);
            }
        }

        var remoteAddress = GetRemoteAddress(connection.Client.Client.RemoteEndPoint);
        return remoteAddress != "unknown" &&
            _pendingCharacterCreationLogins.TryRemove($"{remoteAddress}|{accountId}", out var expiresAt) &&
            expiresAt > now;
    }

    private bool TryReservePendingWorldConnection(
        ActiveConnection connection,
        RuntimeSession session,
        byte selectedServerId,
        CharacterSummary character)
    {
        lock (_worldClaimGate)
        {
            foreach (var entry in _pendingWorldConnectionsByRemoteAddress.ToArray())
            {
                if (entry.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    RemovePendingWorldEntryAndRestoreRoleLocked(entry.Key, entry.Value);
                }
            }

            var remoteAddress = GetRemoteAddress(connection.Client.Client.RemoteEndPoint);
            if (remoteAddress == "unknown")
            {
                return false;
            }

            // Character selection is currently client-driven after the verified character-list bootstrap.
            // The only recovered world-link material is the selected server id plus the authenticated login session.
            var pending = new PendingWorldConnection(
                remoteAddress,
                connection.ConnectionId,
                session.SessionId,
                session.AccountId!.Value,
                selectedServerId,
                character,
                DateTimeOffset.UtcNow.AddMinutes(2));
            if (!_pendingWorldConnectionsByRemoteAddress.TryAdd(remoteAddress, pending))
            {
                return false;
            }

            // Publish the login-side role under the same gate as the pending claim so a
            // world socket can never observe a reservation whose owner is still marked
            // as CharacterSelect. Connection cleanup removes the claim if the response
            // write fails after this point.
            connection.Role = ConnectionRole.PendingWorld;
            return true;
        }
    }

    private void RemovePendingWorldConnection(string loginConnectionId)
    {
        lock (_worldClaimGate)
        {
            foreach (var entry in _pendingWorldConnectionsByRemoteAddress.ToArray())
            {
                if (string.Equals(entry.Value.LoginConnectionId, loginConnectionId, StringComparison.Ordinal))
                {
                    RemovePendingWorldEntryAndRestoreRoleLocked(entry.Key, entry.Value);
                }
            }
        }
    }

    private PendingWorldConnection? TryGetPendingWorldConnection(string remoteAddress)
    {
        lock (_worldClaimGate)
        {
            if (remoteAddress == "unknown")
            {
                return null;
            }

            foreach (var entry in _pendingWorldConnectionsByRemoteAddress.ToArray())
            {
                if (entry.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    RemovePendingWorldEntryAndRestoreRoleLocked(entry.Key, entry.Value);
                }
            }

            if (!_pendingWorldConnectionsByRemoteAddress.TryGetValue(remoteAddress, out var pending) ||
                pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            if (!IsPendingWorldConnectionValid(pending))
            {
                RemovePendingWorldEntryAndRestoreRoleLocked(remoteAddress, pending);
                return null;
            }

            return pending;
        }
    }

    private bool RemovePendingWorldEntryAndRestoreRoleLocked(
        string remoteAddress,
        PendingWorldConnection pending)
    {
        var removed =
            ((ICollection<KeyValuePair<string, PendingWorldConnection>>)_pendingWorldConnectionsByRemoteAddress)
            .Remove(new KeyValuePair<string, PendingWorldConnection>(remoteAddress, pending));
        if (removed &&
            _connections.TryGetValue(pending.LoginConnectionId, out var connection) &&
            connection.Role == ConnectionRole.PendingWorld)
        {
            connection.Role = ConnectionRole.CharacterSelect;
        }

        return removed;
    }

    private PendingWorldConnection? TryClaimPendingWorldConnection(
        string remoteAddress,
        PendingWorldConnection expected,
        string worldConnectionId)
    {
        lock (_worldClaimGate)
        {
            if (_ambiguousWorldHandshakeRemoteAddresses.Contains(remoteAddress) ||
                !_worldHandshakeConnectionsByRemoteAddress.TryGetValue(remoteAddress, out var handshakes) ||
                handshakes.Count != 1 ||
                !handshakes.ContainsKey(worldConnectionId))
            {
                return null;
            }

            if (!_pendingWorldConnectionsByRemoteAddress.TryGetValue(remoteAddress, out var current) ||
                current != expected ||
                !IsPendingWorldConnectionValid(current))
            {
                return null;
            }

            // Do not consume the one-time transition merely because a socket connected.
            // The claim is made atomically only after the exact client handshake passed.
            return ((ICollection<KeyValuePair<string, PendingWorldConnection>>)_pendingWorldConnectionsByRemoteAddress)
                .Remove(new KeyValuePair<string, PendingWorldConnection>(remoteAddress, current))
                ? current
                : null;
        }
    }

    private void RemoveWorldHandshakeConnection(ActiveConnection connection)
    {
        lock (_worldClaimGate)
        {
            var remoteAddress = connection.WorldHandshakeRemoteAddress;
            if (string.IsNullOrEmpty(remoteAddress))
            {
                return;
            }

            connection.WorldHandshakeRemoteAddress = string.Empty;
            if (!_worldHandshakeConnectionsByRemoteAddress.TryGetValue(remoteAddress, out var handshakes))
            {
                return;
            }

            handshakes.TryRemove(connection.ConnectionId, out _);
            if (handshakes.IsEmpty)
            {
                ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_worldHandshakeConnectionsByRemoteAddress)
                    .Remove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(remoteAddress, handshakes));
                _ambiguousWorldHandshakeRemoteAddresses.Remove(remoteAddress);
            }
        }
    }

    private bool IsPendingWorldConnectionValid(PendingWorldConnection pending)
    {
        if (!_connections.TryGetValue(pending.LoginConnectionId, out var loginConnection) ||
            loginConnection.Role != ConnectionRole.PendingWorld ||
            _sessionAuthority is null)
        {
            return false;
        }

        var loginSession = _sessionAuthority.GetSession(pending.LoginSessionId);
        if (!loginSession.Succeeded || loginSession.Value is null ||
            !loginSession.Value.IsAuthenticated ||
            loginSession.Value.ProtocolStage != ProtocolStage.CharacterList ||
            loginSession.Value.AccountId != pending.AccountId ||
            pending.Character.AccountId != pending.AccountId)
        {
            return false;
        }

        return true;
    }

    private async Task<OperationResult<PendingWorldConnection>> RefreshPendingWorldCharacterAsync(
        PendingWorldConnection pendingWorld,
        CancellationToken cancellationToken)
    {
        if (_sessionAuthority is null || _characterListQuery is null)
        {
            return OperationResult<PendingWorldConnection>.Failure(
                "world.character_repository_unavailable",
                "The selected character cannot be revalidated without the MariaDB-backed character query.");
        }

        var loginSession = _sessionAuthority.GetSession(pendingWorld.LoginSessionId);
        if (!loginSession.Succeeded || loginSession.Value is null)
        {
            return OperationResult<PendingWorldConnection>.Failure(
                "world.login_session_missing",
                "The authenticated login session is no longer available for character revalidation.");
        }

        var characters = await _characterListQuery.ListAsync(loginSession.Value, cancellationToken);
        var matches = characters.Where(character =>
            character.CharacterId == pendingWorld.Character.CharacterId &&
            character.AccountId == pendingWorld.AccountId &&
            string.Equals(character.Status, "Active", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
        {
            return OperationResult<PendingWorldConnection>.Failure(
                "world.character_revalidation_failed",
                "World entry requires exactly one active MariaDB character matching the one-time PendingWorld claim.",
                pendingWorld.Character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return OperationResult<PendingWorldConnection>.Success(pendingWorld with { Character = matches[0] });
    }

    private async Task<OperationResult<RuntimeSession>> PromoteWorldSessionAsync(
        ActiveConnection connection,
        RuntimeSession session,
        PendingWorldConnection? pendingWorld,
        CancellationToken cancellationToken)
    {
        if (_sessionAuthority is null || _authenticationService is null || pendingWorld is null)
        {
            return OperationResult<RuntimeSession>.Failure(
                "world.session_transfer_unavailable",
                "The authenticated login-to-world transfer context is unavailable.");
        }

        var loginSession = _sessionAuthority.GetSession(pendingWorld.LoginSessionId);
        if (!loginSession.Succeeded || loginSession.Value is null)
        {
            return OperationResult<RuntimeSession>.Failure(
                "world.login_session_missing",
                "The authenticated login session is no longer available.");
        }

        var transferred = await _authenticationService.TransferToWorldSessionAsync(
            loginSession.Value,
            session,
            cancellationToken);
        if (!transferred.Succeeded || transferred.Value is null)
        {
            await AbortWorldPromotionAsync(session.SessionId, pendingWorld, transferred.Error.Code);
            return OperationResult<RuntimeSession>.Failure(
                transferred.Error.Code,
                transferred.Error.Message);
        }

        var current = transferred.Value;
        var bound = _sessionAuthority.BindCharacter(current, pendingWorld.Character.CharacterId);
        if (!bound.Succeeded || bound.Value is null)
        {
            await AbortWorldPromotionAsync(session.SessionId, pendingWorld, bound.Error.Code);
            return OperationResult<RuntimeSession>.Failure(bound.Error.Code, bound.Error.Message);
        }

        current = bound.Value;
        var transition = _sessionAuthority.Transition(current, ProtocolStage.WorldEntering);
        if (!transition.Succeeded || transition.Value is null)
        {
            await AbortWorldPromotionAsync(session.SessionId, pendingWorld, transition.Error.Code);
            return OperationResult<RuntimeSession>.Failure(transition.Error.Code, transition.Error.Message);
        }

        return OperationResult<RuntimeSession>.Success(transition.Value);
    }

    private async Task<OperationResult<RuntimeSession>> FinalizeWorldPromotionAsync(
        ActiveConnection connection,
        RuntimeSession worldEnteringSession,
        PendingWorldConnection pendingWorld,
        CancellationToken cancellationToken)
    {
        if (_sessionAuthority is null)
        {
            return OperationResult<RuntimeSession>.Failure(
                "world.session_authority_unavailable",
                "The authoritative world session cannot be finalized without session authority.");
        }

        var transitioned = _sessionAuthority.Transition(worldEnteringSession, ProtocolStage.InWorld);
        if (!transitioned.Succeeded || transitioned.Value is null)
        {
            await AbortWorldPromotionAsync(worldEnteringSession.SessionId, pendingWorld, transitioned.Error.Code);
            return OperationResult<RuntimeSession>.Failure(transitioned.Error.Code, transitioned.Error.Message);
        }

        var current = transitioned.Value;
        var updatedBinding = _worldSessionCoordinator?.UpdateSession(current);
        if (updatedBinding is null || !updatedBinding.Succeeded)
        {
            var error = updatedBinding?.Error ?? new OperationError(
                "world_authority.coordinator_unavailable",
                "The authoritative world-session coordinator is unavailable.");
            await AbortWorldPromotionAsync(current.SessionId, pendingWorld, error.Code);
            return OperationResult<RuntimeSession>.Failure(error.Code, error.Message, error.Source);
        }

        if (_portalClosedLoop is not null)
        {
            var seeded = await _portalClosedLoop.SeedSessionAsync(
                current,
                pendingWorld.Character,
                cancellationToken);
            if (!seeded.Succeeded)
            {
                await AbortWorldPromotionAsync(current.SessionId, pendingWorld, seeded.Error.Code);
                return OperationResult<RuntimeSession>.Failure(seeded.Error.Code, seeded.Error.Message);
            }
        }

        if (_connections.TryGetValue(pendingWorld.LoginConnectionId, out var loginConnection))
        {
            loginConnection.Role = ConnectionRole.Closed;
            loginConnection.Client.Dispose();
        }

        return OperationResult<RuntimeSession>.Success(current);
    }

    private async Task AbortWorldPromotionAsync(
        string worldSessionId,
        PendingWorldConnection pendingWorld,
        string reason)
    {
        _worldSessionCoordinator?.Unbind(worldSessionId);
        foreach (var sessionId in new[] { worldSessionId, pendingWorld.LoginSessionId })
        {
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await CloseLatestSessionAsync(sessionId, reason, cleanupTimeout.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException)
            {
                _logger.Log(
                    ServerLogLevel.Error,
                    "Session cleanup failed while aborting world promotion.",
                    sessionId: sessionId,
                    errorCode: exception.GetType().Name);
            }
        }

        if (_connections.TryGetValue(pendingWorld.LoginConnectionId, out var loginConnection))
        {
            loginConnection.Role = ConnectionRole.Closed;
            loginConnection.Client.Dispose();
        }
    }

    private async Task<string?> CompleteOfficialHandshakeAsync(
        ActiveConnection connection,
        RuntimeSession session,
        PendingWorldConnection? pendingWorld,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var handshake = connection.Role == ConnectionRole.WorldHandshake
            ? _officialWorldServerHandshake
            : OfficialLoginServerHandshake;
        await connection.SendAsync(handshake, cancellationToken);
        _logger.Log(
            ServerLogLevel.Information,
            $"Sent official {connection.Role} server handshake. bytes={handshake.Length}.",
            connection.ConnectionId,
            session.SessionId,
            session.ProtocolStage);

        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        byte[] clientHandshake;
        try
        {
            clientHandshake = await ReadSingleFramedPacketAsync(stream, handshakeTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (connection.Role == ConnectionRole.WorldHandshake)
            {
                RemoveWorldHandshakeConnection(connection);
            }

            return connection.Role == ConnectionRole.WorldHandshake ? "world_handshake_timeout" : "login_handshake_timeout";
        }

        try
        {
            if (clientHandshake.Length == 0)
            {
                if (connection.Role == ConnectionRole.WorldHandshake)
                {
                    RemoveWorldHandshakeConnection(connection);
                }

                return connection.Role == ConnectionRole.WorldHandshake ? "world_handshake_client_close" : "login_handshake_client_close";
            }

            var expectedClientHandshake = connection.Role == ConnectionRole.WorldHandshake
                ? _officialWorldClientHandshake
                : OfficialLoginClientHandshake;
            if (clientHandshake.Length != expectedClientHandshake.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    clientHandshake,
                    expectedClientHandshake))
            {
                var clientHandshakeHash = Convert.ToHexString(SHA256.HashData(clientHandshake)).ToLowerInvariant();
                var failureCode = connection.Role == ConnectionRole.WorldHandshake
                    ? "world_handshake_unexpected_content"
                    : "login_handshake_unexpected_content";
                _logger.Log(
                    ServerLogLevel.Warning,
                    $"Unexpected official {connection.Role} client handshake; length={clientHandshake.Length}; sha256={clientHandshakeHash}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    failureCode);
                if (connection.Role == ConnectionRole.WorldHandshake)
                {
                    RemoveWorldHandshakeConnection(connection);
                }

                return failureCode;
            }

            _logger.Log(
                ServerLogLevel.Information,
                $"Received official {connection.Role} client handshake. bytes={clientHandshake.Length}.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage);

            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
            if (connection.Role == ConnectionRole.WorldHandshake)
            {
                // The recovered current-build state machine sends this follow-up first. The
                // official client then emits one 208-byte framed world-entry request before it
                // will accept PlayerSpawn. This request is only a protocol state-transition
                // signal: the authenticated PendingWorld claim and atomic session transfer
                // remain the authority for account, character, map, and coordinates.
                await connection.SendAsync(_officialWorldServerFirstFollowUp, cancellationToken);
                _logger.Log(
                    ServerLogLevel.Information,
                    $"Sent official world-entry follow-up. bytes={_officialWorldServerFirstFollowUp.Length}; profile={_officialWorldWireProfile}.",
                    connection.ConnectionId,
                    session.SessionId,
                    session.ProtocolStage,
                    "world_entry_followup_sent");

                byte[] worldEntryRequest;
                using (var worldEntryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    worldEntryTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        worldEntryRequest = await ReadSingleFramedPacketAsync(stream, worldEntryTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        RemoveWorldHandshakeConnection(connection);
                        return "world_entry_request_timeout";
                    }
                }

                try
                {
                    if (worldEntryRequest.Length != OfficialWorldEntryRequestLength)
                    {
                        var worldEntryRequestHash = Convert.ToHexString(SHA256.HashData(worldEntryRequest)).ToLowerInvariant();
                        _logger.Log(
                            ServerLogLevel.Warning,
                            $"Rejected official world-entry request; length={worldEntryRequest.Length}; sha256={worldEntryRequestHash}.",
                            connection.ConnectionId,
                            session.SessionId,
                            session.ProtocolStage,
                            "world_entry_request_invalid");
                        RemoveWorldHandshakeConnection(connection);
                        return "world_entry_request_invalid";
                    }

                    _logger.Log(
                        ServerLogLevel.Information,
                        $"Received official world-entry request. bytes={worldEntryRequest.Length}; payload=opaque_non_authoritative.",
                        connection.ConnectionId,
                        session.SessionId,
                        session.ProtocolStage,
                        "world_entry_request_received");

                    pendingWorld = pendingWorld is null
                        ? null
                        : TryClaimPendingWorldConnection(
                            GetRemoteAddress(connection.Client.Client.RemoteEndPoint),
                            pendingWorld,
                            connection.ConnectionId);
                    if (pendingWorld is null)
                    {
                        return "world_pending_claim_failed";
                    }

                    RemoveWorldHandshakeConnection(connection);

                    var refreshed = await RefreshPendingWorldCharacterAsync(pendingWorld, cancellationToken);
                    if (!refreshed.Succeeded || refreshed.Value is null)
                    {
                        _logger.Log(
                            ServerLogLevel.Warning,
                            "Rejected world handshake because the selected MariaDB character could not be revalidated.",
                            connection.ConnectionId,
                            session.SessionId,
                            session.ProtocolStage,
                            refreshed.Error.Code);
                        await AbortWorldPromotionAsync(session.SessionId, pendingWorld, refreshed.Error.Code);
                        return refreshed.Error.Code;
                    }

                    pendingWorld = refreshed.Value;
                    var promoted = await PromoteWorldSessionAsync(
                        connection,
                        session,
                        pendingWorld,
                        cancellationToken);
                    if (!promoted.Succeeded || promoted.Value is null)
                    {
                        _logger.Log(
                            ServerLogLevel.Warning,
                            "Rejected world handshake because the authenticated login session could not be transferred.",
                            connection.ConnectionId,
                            session.SessionId,
                            session.ProtocolStage,
                            promoted.Error.Code);
                        return promoted.Error.Code;
                    }

                    if (_worldSessionCoordinator is null || _worldBootstrapProjector is null)
                    {
                        await AbortWorldPromotionAsync(session.SessionId, pendingWorld, "world_authority.coordinator_unavailable");
                        return "world_authority.coordinator_unavailable";
                    }

                    var bound = await _worldSessionCoordinator.BindAsync(
                        promoted.Value,
                        pendingWorld.Character,
                        cancellationToken);
                    if (!bound.Succeeded || bound.Value is null)
                    {
                        _logger.Log(
                            ServerLogLevel.Warning,
                            "Rejected world promotion because the MariaDB character could not be attached to an authoritative MapRuntime.",
                            connection.ConnectionId,
                            promoted.Value.SessionId,
                            promoted.Value.ProtocolStage,
                            bound.Error.Code);
                        await AbortWorldPromotionAsync(promoted.Value.SessionId, pendingWorld, bound.Error.Code);
                        return bound.Error.Code;
                    }

                    var projection = _worldBootstrapProjector.Project(bound.Value);
                    if (!projection.Succeeded || projection.Value is null)
                    {
                        _logger.Log(
                            ServerLogLevel.Warning,
                            "Rejected world bootstrap because the authoritative runtime state cannot be represented by the recovered projector.",
                            connection.ConnectionId,
                            promoted.Value.SessionId,
                            promoted.Value.ProtocolStage,
                            projection.Error.Code);
                        await AbortWorldPromotionAsync(promoted.Value.SessionId, pendingWorld, projection.Error.Code);
                        return projection.Error.Code;
                    }

                    var worldBootstrap = projection.Value.Bytes;
                    try
                    {
                        if (worldBootstrap.Length <= _officialWorldServerFirstFollowUp.Length ||
                            !CryptographicOperations.FixedTimeEquals(
                                worldBootstrap.AsSpan(0, _officialWorldServerFirstFollowUp.Length),
                                _officialWorldServerFirstFollowUp))
                        {
                            await AbortWorldPromotionAsync(
                                promoted.Value.SessionId,
                                pendingWorld,
                                "world_bootstrap.followup_contract_mismatch");
                            return "world_bootstrap.followup_contract_mismatch";
                        }

                        if (projection.Value.LegacyWorldEntitiesExcluded)
                        {
                            var preflight = _runtimeReplicationEmitter.PreflightSpawnQueue(
                                bound.Value.MapRuntime,
                                promoted.Value.SessionId);
                            if (preflight.BlockedFrames != 0)
                            {
                                _logger.Log(
                                    ServerLogLevel.Warning,
                                    $"Initial runtime replication contains evidence-blocked families. blocked={preflight.BlockedFrames}; evidence-ready families will continue independently.",
                                    connection.ConnectionId,
                                    promoted.Value.SessionId,
                                    promoted.Value.ProtocolStage,
                                    "world_replication.family_evidence_blocked");
                            }
                        }

                        var finalized = await FinalizeWorldPromotionAsync(
                            connection,
                            promoted.Value,
                            pendingWorld,
                            cancellationToken);
                        if (!finalized.Succeeded || finalized.Value is null)
                        {
                            _logger.Log(
                                ServerLogLevel.Warning,
                                "Rejected world handshake because the authoritative MapRuntime session could not enter InWorld.",
                                connection.ConnectionId,
                                promoted.Value.SessionId,
                                promoted.Value.ProtocolStage,
                                finalized.Error.Code);
                            return finalized.Error.Code;
                        }

                        if (_officialWorldChatClosedLoop is not null)
                        {
                            var chatRegistration = _officialWorldChatClosedLoop.RegisterSession(
                                finalized.Value.SessionId,
                                bound.Value.Character.CharacterId,
                                bound.Value.Character.Name);
                            if (!chatRegistration.Succeeded)
                            {
                                await AbortWorldPromotionAsync(
                                    finalized.Value.SessionId,
                                    pendingWorld,
                                    chatRegistration.Error.Code);
                                return chatRegistration.Error.Code;
                            }
                        }

                        // Connection-local protocol state is expressed in official-client
                        // coordinates. Database/runtime coordinates may use a proven scale
                        // and offset, so initializing these fields from the raw character row
                        // would silently mix coordinate systems.
                        connection.WorldXCandidate = projection.Value.ClientX;
                        connection.WorldYCandidate = projection.Value.ClientY;
                        await connection.SendAsync(
                            worldBootstrap.AsMemory(_officialWorldServerFirstFollowUp.Length),
                            cancellationToken);
                        RuntimeReplicationEmissionResult? replication = null;
                        if (projection.Value.LegacyWorldEntitiesExcluded)
                        {
                            _runtimeReplicationEmitter.AcknowledgeEmbeddedInitialNpcSpawns(
                                bound.Value.MapRuntime,
                                finalized.Value.SessionId,
                                projection.Value.EmbeddedInitialNpcRuntimeObjectIds ?? []);
                            replication = await _runtimeReplicationEmitter.FlushSpawnQueueAsync(
                                bound.Value.MapRuntime,
                                finalized.Value.SessionId,
                                new NetworkStreamRuntimeFrameSender(connection),
                                cancellationToken);
                        }
                        if (replication is { BlockedFrames: not 0 })
                        {
                            _logger.Log(
                                ServerLogLevel.Warning,
                                $"Quarantined evidence-blocked initial replication without emitting bytes. sent={replication.SentFrames}; blocked={replication.BlockedFrames}.",
                                connection.ConnectionId,
                                finalized.Value.SessionId,
                                ProtocolStage.InWorld,
                                "world_replication.family_evidence_blocked");
                        }

                        connection.RuntimeReplicationEnabled = projection.Value.LegacyWorldEntitiesExcluded;
                        connection.Role = ConnectionRole.InWorld;
                        _logger.Log(
                            ServerLogLevel.Information,
                            $"Sent authoritative world projection through isolated legacy compatibility framing. bytes={worldBootstrap.Length - _officialWorldServerFirstFollowUp.Length}; runtimeMap={projection.Value.RuntimeMapId}; clientMap={projection.Value.ClientMapId}; clientArea={projection.Value.ClientAreaId}; clientX={projection.Value.ClientX}; clientY={projection.Value.ClientY}; playerEntity={projection.Value.PlayerRuntimeEntityId}; projector={projection.Value.Projector}; wireProfile={_officialWorldWireProfile}; movementMode=DeferredUntilOfficialWalkingEvidence; evidence={projection.Value.EvidenceId}.",
                            connection.ConnectionId,
                            finalized.Value.SessionId,
                            ProtocolStage.InWorld,
                            "world_bootstrap_sent");
                        if (replication is not null)
                        {
                            _logger.Log(
                                replication.BlockedFrames == 0 ? ServerLogLevel.Information : ServerLogLevel.Warning,
                                $"Flushed authoritative runtime entity replication. sent={replication.SentFrames}; blocked={replication.BlockedFrames}; legacyWorldEntitiesExcluded=true.",
                                connection.ConnectionId,
                                finalized.Value.SessionId,
                                ProtocolStage.InWorld,
                                replication.BlockedFrames == 0 ? null : "world_replication.non_npc_evidence_blocked");
                        }
                        return null;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(worldBootstrap);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(worldEntryRequest);
                }
            }

            var loginFollowUp = connection.CharacterCreationLoginReconnect
                ? OfficialCharacterCreationLoginServerFollowUp
                : OfficialLoginServerFollowUp;
            await connection.SendAsync(loginFollowUp, cancellationToken);
            connection.Role = ConnectionRole.LoginAuthenticated;
            _logger.Log(
                ServerLogLevel.Information,
                $"Sent official login server follow-up. bytes={loginFollowUp.Length}; characterCreationReconnect={connection.CharacterCreationLoginReconnect}.",
                connection.ConnectionId,
                session.SessionId,
                session.ProtocolStage);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clientHandshake);
        }
    }

    private async Task<byte[]> ReadSingleFramedPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactOrEmptyAsync(stream, 2, cancellationToken);
        if (header.Length != 2)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var length = header[0] | (header[1] << 8);
            if (length < 2 || length > _maxPacketSize)
            {
                return header.ToArray();
            }

            var packet = new byte[length];
            packet[0] = header[0];
            packet[1] = header[1];
            var rest = await ReadExactOrEmptyAsync(stream, length - 2, cancellationToken);
            try
            {
                if (rest.Length != length - 2)
                {
                    CryptographicOperations.ZeroMemory(packet);
                    return Array.Empty<byte>();
                }

                rest.CopyTo(packet.AsSpan(2));
                return packet;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rest);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header);
        }
    }

    private static async Task<byte[]> ReadExactOrEmptyAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                CryptographicOperations.ZeroMemory(buffer);
                return Array.Empty<byte>();
            }

            offset += read;
        }

        return buffer;
    }

    private static string GetRemoteAddress(EndPoint? endpoint) =>
        endpoint switch
        {
            IPEndPoint ipEndPoint => ipEndPoint.Address.ToString(),
            null => "unknown",
            _ => endpoint.ToString() ?? "unknown"
        };

    private static string ComputeNormalizedUsernameHash(string username)
    {
        var normalizedLength = Encoding.UTF8.GetByteCount(username.Trim().ToUpperInvariant());
        Span<byte> normalized = normalizedLength <= 256
            ? stackalloc byte[normalizedLength]
            : new byte[normalizedLength];
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        try
        {
            Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant(), normalized);
            SHA256.HashData(normalized, digest);
            return Convert.ToHexString(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(normalized);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private OperationResult<RuntimeSession> GetLatestSession(RuntimeSession fallback)
    {
        if (_sessionAuthority is null)
        {
            return OperationResult<RuntimeSession>.Success(fallback);
        }

        return _sessionAuthority.GetSession(fallback.SessionId);
    }

    private async Task<string?> FlushWorldReplicationAsync(
        ActiveConnection connection,
        RuntimeSession session,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        if (connection.Role != ConnectionRole.InWorld || !connection.RuntimeReplicationEnabled)
        {
            return null;
        }

        if (_worldSessionCoordinator is null)
        {
            return "world_replication.coordinator_unavailable";
        }

        var binding = _worldSessionCoordinator.GetBinding(session.SessionId);
        if (!binding.Succeeded || binding.Value is null)
        {
            return binding.Error.Code;
        }

        var preflight = _runtimeReplicationEmitter.PreflightPendingQueues(
            binding.Value.MapRuntime,
            session.SessionId);
        if (preflight.BlockedFrames != 0)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Pending runtime replication contains evidence-blocked families. blocked={preflight.BlockedFrames}; ready frames remain eligible.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_replication.family_evidence_blocked");
        }

        var emitted = await _runtimeReplicationEmitter.FlushAsync(
            binding.Value.MapRuntime,
            session.SessionId,
            new NetworkStreamRuntimeFrameSender(connection),
            cancellationToken);
        if (emitted.BlockedFrames != 0)
        {
            _logger.Log(
                ServerLogLevel.Warning,
                $"Runtime replication quarantined unsupported family entries without bytes. sent={emitted.SentFrames}; blocked={emitted.BlockedFrames}.",
                connection.ConnectionId,
                session.SessionId,
                ProtocolStage.InWorld,
                "world_replication.family_evidence_blocked");
        }

        return null;
    }

    private async Task CloseLatestSessionAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        if (_sessionAuthority is null)
        {
            return;
        }

        var latest = _sessionAuthority.GetSession(sessionId);
        if (latest.Succeeded && latest.Value is not null)
        {
            await _sessionAuthority.CloseSessionAsync(latest.Value, reason, cancellationToken);
        }
    }

    private async Task ObserveAllAsync(IReadOnlyList<Task> tasks)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            foreach (var faulted in tasks.Where(task => task.IsFaulted))
            {
                _logger.Log(
                    ServerLogLevel.Error,
                    "Tracked network task completed with an observed exception.",
                    errorCode: faulted.Exception?.GetBaseException().GetType().Name);
            }
        }
    }

    private sealed class NetworkStreamRuntimeFrameSender : IRuntimeFrameSender
    {
        private readonly ActiveConnection _connection;

        public NetworkStreamRuntimeFrameSender(ActiveConnection connection)
        {
            _connection = connection;
        }

        public ValueTask SendAsync(string sessionId, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            return _connection.SendAsync(frame, cancellationToken);
        }
    }
}
