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
    private readonly NpcSnapshotService _npcSnapshotService;
    private readonly ICharacterPositionWriter? _characterPositionWriter;
    private readonly SessionRegistry _sessionRegistry;
    private readonly PendingWorldEntryRegistry _pendingWorldEntries;
    private readonly WorldPresenceRegistry _worldPresences = new();
    private readonly WorldReplicationOutboxRegistry _worldReplicationOutboxes = new();
    private readonly NpcInteractionSessionRegistry _npcInteractions = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private long _nextConnectionId;
    private bool _started;

    public TcpGameServer(
        TcpServerOptions options,
        SessionRegistry sessionRegistry,
        LoginService loginService,
        CharacterListService characterListService,
        ICharacterPositionWriter? characterPositionWriter = null,
        NpcSnapshotService? npcSnapshotService = null)
    {
        Options = options;
        _loginService = loginService ??
            throw new ArgumentNullException(nameof(loginService));
        _characterListService = characterListService ??
            throw new ArgumentNullException(nameof(characterListService));
        _npcSnapshotService =
            npcSnapshotService ??
            new NpcSnapshotService(
                new EmptyNpcSnapshotRepository());
        _characterPositionWriter = characterPositionWriter;
        _sessionRegistry = sessionRegistry ??
            throw new ArgumentNullException(nameof(sessionRegistry));
        _pendingWorldEntries =
            new PendingWorldEntryRegistry(_sessionRegistry);
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
                    _npcSnapshotService,
                    _characterPositionWriter,
                    _pendingWorldEntries,
                    _worldPresences,
                    _worldReplicationOutboxes,
                    _npcInteractions,
                    Options.AdvertisedAddress.GetAddressBytes(),
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
        NpcSnapshotService npcSnapshotService,
        ICharacterPositionWriter? characterPositionWriter,
        PendingWorldEntryRegistry pendingWorldEntries,
        WorldPresenceRegistry worldPresences,
        WorldReplicationOutboxRegistry worldReplicationOutboxes,
        NpcInteractionSessionRegistry npcInteractions,
        byte[] advertisedAddress,
        ushort advertisedPort,
        CancellationToken serverCancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var remoteAddress =
            (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ??
            "unknown";
        var state = new ConnectionStateMachine();
        using var sessionContext = new ConnectionSessionContext(
            connectionId,
            sessionRegistry);
        Log(connectionId, $"Connected remote={remoteEndPoint} stage={state.Stage}");

        try
        {
            using (client)
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                if (pendingWorldEntries.TryClaim(
                        remoteAddress,
                        DateTimeOffset.UtcNow,
                        out var pendingWorld) &&
                    pendingWorld is not null)
                {
                    var worldSessionTransferred =
                        sessionContext.TransferOrAcquireFrom(
                            pendingWorld.AccountName,
                            pendingWorld.ReservationConnectionId);

                    if (!worldSessionTransferred)
                    {
                        sessionRegistry.Release(
                            pendingWorld.AccountName,
                            pendingWorld.ReservationConnectionId);

                        Log(
                            connectionId,
                            "World session ownership transfer rejected: " +
                            $"loginConnection={pendingWorld.LoginConnectionId}; " +
                            $"reservationConnection={pendingWorld.ReservationConnectionId}; " +
                            "closing connection.");
                        return;
                    }

                    state.Transition(ConnectionStage.WorldHandshake);

                    await stream.WriteAsync(
                        OfficialWorldHandshakeProtocol.ServerHandshakeFrame,
                        serverCancellationToken);

                    var worldClientHandshake = new byte[
                        OfficialWorldHandshakeProtocol.HandshakeLength];

                    await stream.ReadExactlyAsync(
                        worldClientHandshake,
                        serverCancellationToken);

                    if (!OfficialWorldHandshakeProtocol
                            .IsExpectedClientHandshake(worldClientHandshake))
                    {
                        Log(connectionId, "World handshake rejected.");
                        return;
                    }

                    if (pendingWorld.Character.MapId is not long mapId)
                    {
                        Log(
                            connectionId,
                            "NPC snapshot load rejected: " +
                            "character has no authoritative map; " +
                            "closing connection.");
                        return;
                    }

                    IReadOnlyList<NpcSnapshotEntry> npcSnapshot;

                    try
                    {
                        npcSnapshot =
                            await npcSnapshotService.GetAsync(
                                mapId,
                                serverCancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (serverCancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Log(
                            connectionId,
                            "NPC snapshot load rejected: " +
                            $"map={mapId}; " +
                            $"error={exception.GetType().Name}; " +
                            "closing connection.");
                        return;
                    }

                    var npcSpawnFrames = new List<byte[]>();
                    var blockedNpcSpawns = 0;

                    foreach (var npc in npcSnapshot)
                    {
                        var encoding =
                            OfficialNpcSpawnCodec.Encode(
                                new OfficialNpcSpawnEvidence(
                                    npc.ClientBuildId,
                                    npc.ClientEntityHandle,
                                    npc.ResourceType,
                                    npc.ResourceOrdinal,
                                    npc.SelectorHighBits,
                                    npc.DirectionCode,
                                    npc.StateCode,
                                    npc.PositionX,
                                    npc.PositionY,
                                    npc.SpawnMessageSha256,
                                    npc.OpaqueTemplateSha256,
                                    npc.WireEvidenceStatus,
                                    npc.WireEvidenceReference));

                        if (encoding.Succeeded)
                        {
                            npcSpawnFrames.Add(encoding.Frame);
                        }
                        else
                        {
                            blockedNpcSpawns++;

                            Log(
                                connectionId,
                                "NPC spawn blocked by evidence: " +
                                $"spawn={npc.SpawnId}; " +
                                $"handle={npc.ClientEntityHandle}; " +
                                $"reason={encoding.Reason}.");
                        }
                    }

                    var presenceResult =
                        worldPresences.TryEnter(
                            new WorldPresence(
                                connectionId,
                                pendingWorld.AccountName,
                                pendingWorld.AccountId,
                                pendingWorld.Character,
                                DateTimeOffset.UtcNow),
                            out var visibleWorldPeers);

                    if (!presenceResult.Succeeded)
                    {
                        Log(
                            connectionId,
                            "World presence rejected: " +
                            $"status={presenceResult.Status}; " +
                            $"existingConnection={presenceResult.ExistingConnectionId}; " +
                            $"character={pendingWorld.Character.CharacterId}; " +
                            "closing connection.");
                        return;
                    }

                    if (!worldReplicationOutboxes.TryRegister(
                            connectionId))
                    {
                        Log(
                            connectionId,
                            "World replication outbox registration rejected.");
                        return;
                    }

                    var currentPresence =
                        worldPresences.TryGetByConnection(
                            connectionId,
                            out var registeredPresence)
                            ? registeredPresence!
                            : throw new InvalidOperationException(
                                "Registered world presence is unavailable.");

                    foreach (var peer in visibleWorldPeers)
                    {
                        worldReplicationOutboxes.TryEnqueue(
                            peer.ConnectionId,
                            WorldReplicationEventKind.PlayerEntered,
                            currentPresence,
                            DateTimeOffset.UtcNow,
                            out _);

                        worldReplicationOutboxes.TryEnqueue(
                            connectionId,
                            WorldReplicationEventKind.PlayerEntered,
                            peer,
                            DateTimeOffset.UtcNow,
                            out _);
                    }

                    Log(
                        connectionId,
                        "World presence entered: " +
                        $"character={pendingWorld.Character.CharacterId}; " +
                        $"map={pendingWorld.Character.MapId}; " +
                        $"visiblePeers={visibleWorldPeers.Count}; " +
                        $"queuedReplicationEvents=" +
                        $"{worldReplicationOutboxes.Snapshot(connectionId).Count}; " +
                        "wireDispatch=BlockedNoVerifiedPlayerReplicationCodec.");

                    await stream.WriteAsync(
                        OfficialWorldHandshakeProtocol.FirstFollowUpFrame,
                        serverCancellationToken);

                    var worldBootstrap =
                        OfficialWorldBootstrapCodec.EncodeAfterFirstFollowUp(
                            pendingWorld.Character.CharacterId,
                            pendingWorld.Character.Name,
                            pendingWorld.Character.ClassCode,
                            pendingWorld.Character.GenderCode,
                            pendingWorld.Character.AppearanceCode);

                    await stream.WriteAsync(
                        worldBootstrap,
                        serverCancellationToken);

                    foreach (var npcSpawnFrame in npcSpawnFrames)
                    {
                        try
                        {
                            await stream.WriteAsync(
                                npcSpawnFrame,
                                serverCancellationToken);
                        }
                        finally
                        {
                            Array.Clear(npcSpawnFrame);
                        }
                    }

                    Log(
                        connectionId,
                        "NPC snapshot loaded: " +
                        $"map={mapId}; " +
                        $"count={npcSnapshot.Count}; " +
                        $"wireEligible={npcSpawnFrames.Count}; " +
                        $"wireBlocked={blockedNpcSpawns}; " +
                        "wireDispatch=" +
                        "EvidenceGatedOfficialNpcSpawnCodec.");

                    state.Transition(ConnectionStage.InWorld);

                    Log(
                        connectionId,
                        $"World bootstrap completed bytes={worldBootstrap.Length}; " +
                        $"character={pendingWorld.Character.CharacterId}; " +
                        $"stage={state.Stage}");

                    var worldActivity =
                        new WorldActivityTracker(DateTimeOffset.UtcNow);
                    var runtimeVersion =
                        pendingWorld.Character.RuntimeVersion;
                    var concurrencyToken =
                        pendingWorld.Character.ConcurrencyToken;
                    var movementSequences =
                        new WorldMovementSequenceTracker();

                    while (!serverCancellationToken.IsCancellationRequested)
                    {
                        var remainingIdleTime =
                            worldActivity.GetRemaining(DateTimeOffset.UtcNow);

                        if (remainingIdleTime == TimeSpan.Zero)
                        {
                            Log(
                                connectionId,
                                "World idle timeout after 30 seconds.");
                            break;
                        }

                        using var readCancellation =
                            CancellationTokenSource.CreateLinkedTokenSource(
                                serverCancellationToken);
                        readCancellation.CancelAfter(remainingIdleTime);

                        byte[]? worldFrame;

                        try
                        {
                            worldFrame =
                                await LengthPrefixedFrameReader.ReadAsync(
                                    stream,
                                    readCancellation.Token);
                        }
                        catch (OperationCanceledException)
                            when (!serverCancellationToken.IsCancellationRequested)
                        {
                            Log(
                                connectionId,
                                "World idle timeout after 30 seconds.");
                            break;
                        }

                        if (worldFrame is null)
                        {
                            Log(connectionId, "World remote closed connection.");
                            break;
                        }

                        worldActivity.RecordActivity(DateTimeOffset.UtcNow);

                        if (OfficialNpcDialogSelectionCodec.TryDecode(
                                worldFrame,
                                out var dialogSelection,
                                out var dialogSelectionFailure) &&
                            dialogSelection is not null)
                        {
                            var selectionCloseStatus =
                                npcInteractions.TryClose(
                                    connectionId,
                                    dialogSelection.ClientEntityHandle,
                                    out var selectedInteraction);

                            if (selectionCloseStatus !=
                                NpcInteractionCloseStatus.Closed)
                            {
                                Log(
                                    connectionId,
                                    "NPC dialog selection rejected: " +
                                    $"handle={dialogSelection.ClientEntityHandle}; " +
                                    $"selector={dialogSelection.Selector}; " +
                                    $"status={selectionCloseStatus}; " +
                                    "closing connection.");
                                break;
                            }

                            Log(
                                connectionId,
                                "RX NpcDialogSelection " +
                                $"bytes={worldFrame.Length}; " +
                                $"character={selectedInteraction!.CharacterId}; " +
                                $"map={selectedInteraction.MapId}; " +
                                $"handle={selectedInteraction.ClientEntityHandle}; " +
                                $"spawn={selectedInteraction.SpawnId}; " +
                                $"selector={dialogSelection.Selector}; " +
                                $"opaque=0x{dialogSelection.OpaqueClientValue:X2}; " +
                                $"compound={dialogSelection.IsCompoundTransport}; " +
                                "sessionOwnership=Released; " +
                                "wireResponse=None; " +
                                $"evidence={OfficialNpcDialogSelectionCodec.EvidenceId}");
                            continue;
                        }

                        if (OfficialNpcDialogSelectionCodec.IsCandidate(
                                worldFrame))
                        {
                            Log(
                                connectionId,
                                "NPC dialog selection candidate rejected: " +
                                $"bytes={worldFrame.Length}; " +
                                $"reason={dialogSelectionFailure}; " +
                                "closing connection.");
                            break;
                        }

                        if (OfficialNpcInteractionCodec.TryDecode(
                                worldFrame,
                                out var npcInteraction,
                                out var npcInteractionFailure) &&
                            npcInteraction is not null)
                        {
                            if (npcInteraction.Kind ==
                                OfficialNpcInteractionKind.Open)
                            {
                                var openResult =
                                    npcInteractions.TryOpen(
                                        connectionId,
                                        pendingWorld.Character.CharacterId,
                                        mapId,
                                        npcInteraction.ClientEntityHandle,
                                        npcSnapshot,
                                        DateTimeOffset.UtcNow);

                                if (!openResult.Succeeded)
                                {
                                    Log(
                                        connectionId,
                                        "NPC interaction open rejected: " +
                                        $"handle={npcInteraction.ClientEntityHandle}; " +
                                        $"status={openResult.Status}; " +
                                        "closing connection.");
                                    break;
                                }

                                var dialogEncoding =
                                    OfficialNpcDialogCodec.EncodeOpenResponse(
                                        OfficialNpcSpawnCodec.ClientBuildId,
                                        npcInteraction.ClientEntityHandle);

                                if (!dialogEncoding.Succeeded)
                                {
                                    Log(
                                        connectionId,
                                        "RX NpcInteractionOpen " +
                                        $"bytes={worldFrame.Length}; " +
                                        $"character={pendingWorld.Character.CharacterId}; " +
                                        $"map={mapId}; " +
                                        $"handle={npcInteraction.ClientEntityHandle}; " +
                                        $"spawn={openResult.Session!.SpawnId}; " +
                                        "sessionOwnership=Accepted; " +
                                        "wireResponse=BlockedByEvidence; " +
                                        $"reason={dialogEncoding.Reason}");
                                    continue;
                                }

                                try
                                {
                                    await stream.WriteAsync(
                                        dialogEncoding.Frame,
                                        serverCancellationToken);
                                }
                                finally
                                {
                                    Array.Clear(dialogEncoding.Frame);
                                }

                                Log(
                                    connectionId,
                                    "RX NpcInteractionOpen " +
                                    $"bytes={worldFrame.Length}; " +
                                    $"character={pendingWorld.Character.CharacterId}; " +
                                    $"map={mapId}; " +
                                    $"handle={npcInteraction.ClientEntityHandle}; " +
                                    $"spawn={openResult.Session!.SpawnId}; " +
                                    "sessionOwnership=Accepted; " +
                                    "wireResponse=OfficialNpcDialogCodec; " +
                                    $"txBytes={OfficialNpcDialogCodec.ExactOpenResponseLength}; " +
                                    $"evidence={OfficialNpcDialogCodec.EvidenceId}");
                                continue;
                            }

                            var closeStatus =
                                npcInteractions.TryClose(
                                    connectionId,
                                    npcInteraction.ClientEntityHandle,
                                    out var closedInteraction);

                            if (closeStatus !=
                                NpcInteractionCloseStatus.Closed)
                            {
                                Log(
                                    connectionId,
                                    "NPC interaction close rejected: " +
                                    $"handle={npcInteraction.ClientEntityHandle}; " +
                                    $"status={closeStatus}; " +
                                    "closing connection.");
                                break;
                            }

                            Log(
                                connectionId,
                                "RX NpcInteractionMerchantClose " +
                                $"bytes={worldFrame.Length}; " +
                                $"character={closedInteraction!.CharacterId}; " +
                                $"map={closedInteraction.MapId}; " +
                                $"handle={closedInteraction.ClientEntityHandle}; " +
                                $"spawn={closedInteraction.SpawnId}; " +
                                "sessionOwnership=Released; " +
                                "wireResponse=None");
                            continue;
                        }

                        if (OfficialNpcInteractionCodec.IsCandidate(
                                worldFrame))
                        {
                            Log(
                                connectionId,
                                "NPC interaction candidate rejected: " +
                                $"bytes={worldFrame.Length}; " +
                                $"reason={npcInteractionFailure}; " +
                                "closing connection.");
                            break;
                        }

                        if (OfficialWorldLogoutCodec.IsVerifiedRequest(worldFrame))
                        {
                            Log(
                                connectionId,
                                $"RX WorldLogout bytes={worldFrame.Length} " +
                                "evidence=VerifiedRawCandidate; closing connection.");
                            break;
                        }

                        if (OfficialWorldMovementCodec.TryDecode(
                                worldFrame,
                                out var movement))
                        {
                            if (!movementSequences.TryAccept(
                                    movement.Sequence))
                            {
                                var expectedSequence = unchecked(
                                    (byte)(
                                        movementSequences.LastAcceptedSequence +
                                        1));

                                Log(
                                    connectionId,
                                    $"World movement sequence rejected " +
                                    $"received={movement.Sequence} " +
                                    $"expected={expectedSequence}; " +
                                    "closing without persistence or acknowledgement.");
                                break;
                            }

                            var persistenceState = "Deferred";
                            var movementPersisted = false;

                            if (characterPositionWriter is not null)
                            {
                                var writeResult =
                                    await characterPositionWriter.TryUpdateAsync(
                                        new CharacterPositionWriteRequest(
                                            pendingWorld.Character.CharacterId,
                                            movement.X,
                                            movement.Y,
                                            runtimeVersion,
                                            concurrencyToken),
                                        serverCancellationToken);

                                if (!writeResult.Updated)
                                {
                                    Log(
                                        connectionId,
                                        "World movement persistence conflict; " +
                                        "closing without acknowledgement.");
                                    break;
                                }

                                runtimeVersion = writeResult.RuntimeVersion;
                                concurrencyToken = writeResult.ConcurrencyToken;
                                persistenceState = "Updated";
                                movementPersisted = true;
                            }

                            var replicationRecipients = 0;

                            if (movementPersisted &&
                                worldPresences.TryGetByConnection(
                                    connectionId,
                                    out var movementSubject) &&
                                movementSubject is not null)
                            {
                                var movementReplication =
                                    new WorldReplicationMovement(
                                        movement.X,
                                        movement.Y,
                                        movement.Sequence,
                                        movement.State);

                                foreach (var peer in
                                         worldPresences.VisiblePeers(
                                             connectionId))
                                {
                                    if (worldReplicationOutboxes
                                        .TryEnqueueMovement(
                                            peer.ConnectionId,
                                            movementSubject,
                                            movementReplication,
                                            DateTimeOffset.UtcNow,
                                            out _))
                                    {
                                        replicationRecipients++;
                                    }
                                }
                            }

                            Log(
                                connectionId,
                                $"RX WorldMovement bytes={worldFrame.Length} " +
                                $"x={movement.X} y={movement.Y} " +
                                $"sequence={movement.Sequence} " +
                                $"state=0x{movement.State:X2}; " +
                                $"persistence={persistenceState} " +
                                $"version={runtimeVersion}; " +
                                $"replicationRecipients={replicationRecipients}; " +
                                "wireDispatch=" +
                                "BlockedNoVerifiedPlayerReplicationCodec");

                            var acknowledgement =
                                OfficialWorldMovementCodec.EncodeAcknowledgement(
                                    movement.Sequence);

                            await stream.WriteAsync(
                                acknowledgement,
                                serverCancellationToken);

                            Log(
                                connectionId,
                                $"TX WorldMovementAck bytes={acknowledgement.Length} " +
                                $"sequence={movement.Sequence}");
                            continue;
                        }

                        var classification =
                            OfficialWorldHeartbeatCodec.Classify(worldFrame);

                        if (classification ==
                            OfficialWorldFrameClassification.KeepAlive)
                        {
                            Log(
                                connectionId,
                                $"RX WorldHeartbeat bytes={worldFrame.Length} " +
                                "classification=KeepAlive");
                        }
                        else
                        {
                            Log(
                                connectionId,
                                $"RX UnknownWorldFrame bytes={worldFrame.Length} " +
                                "payload=[REDACTED_UNVERIFIED]");
                        }
                    }

                    return;
                }

                state.Transition(ConnectionStage.LoginHandshake);

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

                        if (character is not null &&
                            !pendingWorldEntries.TryReserve(
                                remoteAddress,
                                sessionContext.AccountName ??
                                    throw new InvalidOperationException(
                                        "Authenticated account name is unavailable."),
                                connectionId,
                                character.AccountId,
                                selection.SelectedServerId,
                                character,
                                DateTimeOffset.UtcNow))
                        {
                            Log(connectionId, "Pending world entry reservation rejected.");
                            return;
                        }

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
            if (npcInteractions.Remove(
                    connectionId,
                    out var releasedInteraction))
            {
                Log(
                    connectionId,
                    "NPC interaction session released: " +
                    $"character={releasedInteraction!.CharacterId}; " +
                    $"map={releasedInteraction.MapId}; " +
                    $"handle={releasedInteraction.ClientEntityHandle}; " +
                    $"spawn={releasedInteraction.SpawnId}.");
            }

            if (worldPresences.TryLeave(
                    connectionId,
                    out var departedPresence,
                    out var departedVisiblePeers))
            {
                foreach (var peer in departedVisiblePeers)
                {
                    worldReplicationOutboxes.TryEnqueue(
                        peer.ConnectionId,
                        WorldReplicationEventKind.PlayerLeft,
                        departedPresence!,
                        DateTimeOffset.UtcNow,
                        out _);
                }

                Log(
                    connectionId,
                    "World presence released: " +
                    $"character={departedPresence!.Character.CharacterId}; " +
                    $"map={departedPresence.Character.MapId}; " +
                    $"visiblePeers={departedVisiblePeers.Count}; " +
                    "wireDispatch=BlockedNoVerifiedPlayerReplicationCodec.");
            }

            worldReplicationOutboxes.TryRemove(
                connectionId,
                out _);

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
