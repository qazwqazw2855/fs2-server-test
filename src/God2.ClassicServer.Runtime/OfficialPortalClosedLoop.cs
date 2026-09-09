using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialPortalTransactionCode
{
    Committed,
    DuplicateCommitted,
    ReplayConflict,
    RolledBack,
    RecoveryRequired
}

public enum OfficialPortalFailurePoint
{
    None,
    BeforeRuntime,
    CommitResponseLost
}

public sealed record OfficialPortalRuntimeBinding(
    string SessionId,
    long CharacterId,
    long PlayerRuntimeEntityId,
    long PortalRuntimeEntityId,
    int PortalTemplateId,
    long ExpectedPlayerRuntimeVersion,
    int SourceRuntimeMapId,
    int TargetRuntimeMapId,
    ushort TargetClientMapId,
    byte TargetClientAreaId,
    WorldPosition3 TargetPosition,
    WorldPosition3? SourcePosition = null,
    int SourceRadius = 0,
    string TriggerType = "ExplicitActivation");

public sealed record OfficialPortalMapRuntimeIdentity(
    int RuntimeMapId,
    ushort ClientMapId,
    byte ClientAreaId,
    string ResourceIdentity,
    MapBounds Bounds,
    string Evidence);

public interface IOfficialPortalMapIdentitySource
{
    OperationResult<OfficialPortalMapRuntimeIdentity> ResolveRuntimeMap(int runtimeMapId);

    OperationResult<OfficialPortalMapRuntimeIdentity> ResolveClientMap(ushort clientMapId, byte clientAreaId);
}

public sealed class LegacyOfficialPortalMapIdentitySource : IOfficialPortalMapIdentitySource
{
    private static readonly IReadOnlyDictionary<int, OfficialPortalMapRuntimeIdentity> ByRuntimeMap =
        new Dictionary<int, OfficialPortalMapRuntimeIdentity>
        {
            [0] = Identity(0, 0, 2, "official-map/hongmeng-realm"),
            [3] = Identity(3, 3, 4, "legacy-phase2-map-3"),
            [19] = Identity(19, 19, 4, "legacy-phase2-map-19"),
            [43] = Identity(43, 43, 2, "official-map/biyou-palace-1f")
        };

    public OperationResult<OfficialPortalMapRuntimeIdentity> ResolveRuntimeMap(int runtimeMapId) =>
        ByRuntimeMap.TryGetValue(runtimeMapId, out var identity)
            ? OperationResult<OfficialPortalMapRuntimeIdentity>.Success(identity)
            : Missing(runtimeMapId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public OperationResult<OfficialPortalMapRuntimeIdentity> ResolveClientMap(ushort clientMapId, byte clientAreaId)
    {
        var identity = ByRuntimeMap.Values.SingleOrDefault(value =>
            value.ClientMapId == clientMapId && value.ClientAreaId == clientAreaId);
        return identity is not null
            ? OperationResult<OfficialPortalMapRuntimeIdentity>.Success(identity)
            : Missing($"{clientMapId}:{clientAreaId}");
    }

    private static OfficialPortalMapRuntimeIdentity Identity(
        int runtimeMapId,
        ushort clientMapId,
        byte clientAreaId,
        string resourceIdentity) => new(
        runtimeMapId,
        clientMapId,
        clientAreaId,
        resourceIdentity,
        new MapBounds(0, 0, OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate, OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate),
        OfficialPortalWireCodec.EvidenceId);

    private static OperationResult<OfficialPortalMapRuntimeIdentity> Missing(string source) =>
        OperationResult<OfficialPortalMapRuntimeIdentity>.Failure(
            "wire.portal.map_identity_missing",
            "The verified portal slice has no map identity for this runtime or official Client map.",
            source);
}

public sealed record OfficialPortalRoute(
    ushort SourceClientMapId,
    byte SourceClientAreaId,
    WorldPosition3 SourceCenter,
    int SourceRadius,
    ushort TargetClientMapId,
    byte TargetClientAreaId,
    WorldPosition3 TargetPosition,
    string TriggerType,
    string Evidence);

public static class OfficialPortalRouteCatalog
{
    private static readonly IReadOnlyDictionary<(ushort MapId, byte AreaId), OfficialPortalRoute> Routes =
        new Dictionary<(ushort, byte), OfficialPortalRoute>
        {
            [(3, 4)] = new(3, 4, new WorldPosition3(252, 397), 0, 19, 4,
                new WorldPosition3(28, 34), "ExplicitActivation", "PortalCapture/portal-verified-20260811-151345"),
            [(19, 4)] = new(19, 4, new WorldPosition3(16, 20), 1, 3, 4,
                new WorldPosition3(196, 139), "MovementRegion",
                "FormalClient/20260812-map19-visual-exit-position-16-20"),
            [(0, 2)] = new(0, 2, new WorldPosition3(284, 452), 8, 43, 2,
                new WorldPosition3(52, 184), "MovementRegion", "PortalCapture/portal-next-elevated-20260811-152341"),
            [(0, 15)] = new(0, 15, new WorldPosition3(249, 246), 0, 7, 15,
                new WorldPosition3(48, 81), "MovementRegion",
                "LiveRecovery/Stage3-CGC-S3-portal-map0-area15-to-map7-area15"),
            [(43, 2)] = new(43, 2, new WorldPosition3(51, 185), 8, 0, 2,
                new WorldPosition3(283, 453), "MovementRegion", "PortalCapture/portal-biyou-return-20260811-153414")
        };

    public static IReadOnlyCollection<OfficialPortalRoute> All => Routes.Values.ToArray();

    public static OperationResult<OfficialPortalRoute> Resolve(ushort sourceClientMapId, byte sourceClientAreaId) =>
        Routes.TryGetValue((sourceClientMapId, sourceClientAreaId), out var route)
            ? OperationResult<OfficialPortalRoute>.Success(route)
            : OperationResult<OfficialPortalRoute>.Failure(
                "wire.portal.route_missing",
                "The official portal catalog has no verified route for this Client map and area.",
                $"{sourceClientMapId}:{sourceClientAreaId}");
}

public interface IOfficialPortalCommandContextResolver
{
    OperationResult<OfficialPortalRuntimeBinding> Resolve(string sessionId);
}

public interface IOfficialPortalSessionLifecycle
{
    bool RemoveSession(string sessionId);
}

public interface IOfficialPortalSessionSeeder
{
    Task<OperationResult> SeedSessionAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken);
}

public interface IOfficialPortalMovementSynchronizer
{
    OperationResult SynchronizeMovement(
        string sessionId,
        WorldPosition3 expectedPosition,
        WorldPosition3 position,
        WorldDirection direction);
}

/// <summary>
/// Resolves the packet's implicit portal target from authoritative world state. The selected
/// Phase 2 family contains no proven target identifier, so ambiguous maps fail closed.
/// Eligibility and range rules remain in WorldInteractionCoordinator.
/// </summary>
public sealed class SingleActivePortalCommandContextResolver : IOfficialPortalCommandContextResolver
{
    private readonly WorldRuntime _world;
    private readonly IWorldInteractionSessionRegistry _sessions;

    public SingleActivePortalCommandContextResolver(
        WorldRuntime world,
        IWorldInteractionSessionRegistry sessions)
    {
        _world = world;
        _sessions = sessions;
    }

    public OperationResult<OfficialPortalRuntimeBinding> Resolve(string sessionId)
    {
        var bindingResult = _sessions.Get(sessionId);
        if (!bindingResult.Succeeded || bindingResult.Value is null)
        {
            return OperationResult<OfficialPortalRuntimeBinding>.Failure(
                "wire.portal.session_not_bound",
                "The official portal command session is not bound to world state.");
        }

        var binding = bindingResult.Value;
        var playerResult = binding.MapRuntime.Objects.Get(binding.MapSession.PlayerRuntimeEntityId);
        if (!playerResult.Succeeded || playerResult.Value is not PlayerObject player)
        {
            return OperationResult<OfficialPortalRuntimeBinding>.Failure(
                "wire.portal.player_not_bound",
                "The authoritative player runtime object is unavailable.");
        }

        var portals = binding.MapRuntime.Objects.OfType<PortalObject>()
            .Where(value => value.State.IsActive && value.State.Source.MapId == binding.MapSession.MapId)
            .OrderBy(value => value.Identity.RuntimeObjectId)
            .ToArray();
        if (portals.Length != 1)
        {
            return OperationResult<OfficialPortalRuntimeBinding>.Failure(
                portals.Length == 0 ? "wire.portal.active_target_missing" : "wire.portal.target_ambiguous",
                "The implicit portal target could not be resolved without guessing.");
        }

        var portal = portals[0];
        var definition = binding.MapRuntime.Definition.Portals.SingleOrDefault(value =>
            value.PortalId == portal.State.PortalId && value.Enabled);
        if (definition is null || !_world.ActiveMaps.ContainsKey(definition.Target.MapId))
        {
            return OperationResult<OfficialPortalRuntimeBinding>.Failure(
                "wire.portal.destination_unavailable",
                "The authoritative portal destination is unavailable.");
        }

        var targetClientIdentity = _world.ActiveMaps[definition.Target.MapId].Definition.ClientIdentity;
        if (targetClientIdentity is null || !targetClientIdentity.ProductionEnabled)
        {
            return OperationResult<OfficialPortalRuntimeBinding>.Failure(
                "wire.portal.destination_client_identity_missing",
                "The portal destination has no promoted official Client map identity.");
        }

        return OperationResult<OfficialPortalRuntimeBinding>.Success(
            new OfficialPortalRuntimeBinding(
                sessionId,
                binding.Session.CharacterId ?? 0,
                player.Identity.RuntimeObjectId,
                portal.Identity.RuntimeObjectId,
                portal.Identity.TemplateId,
                binding.PlayerRuntimeVersion,
                binding.MapSession.MapId,
                definition.Target.MapId,
                targetClientIdentity.ClientMapId,
                targetClientIdentity.ClientAreaId,
                definition.Target.Position,
                definition.Source.Position,
                definition.TriggerRadius,
                definition.TriggerType));
    }
}

/// <summary>
/// Production composition for evidence-promoted portal routes. Bidirectional captures retain
/// their verified reverse route; one-way captures expose only their proven outbound route.
/// Runtime-only object ids are deterministic server identities and never enter official wire output.
/// </summary>
public sealed class OfficialPortalPhase2RuntimeService :
    IOfficialPortalCommandContextResolver,
    IWorldInteractionCoordinator,
    IOfficialPortalSessionLifecycle,
    IOfficialPortalSessionSeeder,
    IOfficialPortalMovementSynchronizer
{
    private readonly InMemorySessionAuthority _sessionAuthority;
    private readonly IPortalTransitionStore _persistence;
    private readonly IOfficialPortalMapIdentitySource _mapIdentities;
    private readonly ConcurrentDictionary<string, SessionRuntime> _sessions = new(StringComparer.Ordinal);
    private readonly object _creationGate = new();

    public OfficialPortalPhase2RuntimeService(
        InMemorySessionAuthority sessionAuthority,
        IPortalTransitionStore? persistence = null,
        IOfficialPortalMapIdentitySource? mapIdentities = null)
    {
        _sessionAuthority = sessionAuthority;
        _persistence = persistence ?? new InMemoryPortalTransitionStore();
        _mapIdentities = mapIdentities ?? new LegacyOfficialPortalMapIdentitySource();
    }

    public int ActiveRuntimeCount => _sessions.Count;

    public bool RemoveSession(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public async Task<OperationResult> SeedSessionAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        if (session.ProtocolStage != ProtocolStage.InWorld ||
            !session.IsAuthenticated ||
            session.AccountId is null ||
            session.CharacterId != character.CharacterId ||
            session.AccountId != character.AccountId)
        {
            return OperationResult.Failure(
                "wire.portal.seed_identity_invalid",
                "The portal runtime seed must match the authenticated in-world character.");
        }

        if (_persistence is InMemoryPortalTransitionStore inMemory)
        {
            inMemory.TrySeed(new PortalLocationState(
                character.CharacterId,
                character.MapId,
                new WorldPosition3(character.PositionX, character.PositionY),
                WorldDirection.Unknown,
                0,
                null,
                null,
                DateTimeOffset.UtcNow));
        }

        PortalLocationState persisted;
        try
        {
            persisted = await _persistence.LoadAsync(character.CharacterId, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException or TimeoutException)
        {
            return OperationResult.Failure(
                "wire.portal.seed_persistence_unavailable",
                "The authoritative character location could not be loaded for the portal runtime.",
                exception.GetType().Name);
        }

        var sourceIdentity = _mapIdentities.ResolveRuntimeMap(persisted.CurrentMapId);
        if (persisted.CharacterId != character.CharacterId ||
            !sourceIdentity.Succeeded ||
            sourceIdentity.Value is null ||
            persisted.RawPosition.X < sourceIdentity.Value.Bounds.MinX ||
            persisted.RawPosition.X > sourceIdentity.Value.Bounds.MaxX ||
            persisted.RawPosition.Y < sourceIdentity.Value.Bounds.MinY ||
            persisted.RawPosition.Y > sourceIdentity.Value.Bounds.MaxY)
        {
            return OperationResult.Failure(
                "wire.portal.seed_location_unsupported",
                "The character location is outside the promoted portal-slice map identity or its proven bounds.");
        }

        var route = OfficialPortalRouteCatalog.Resolve(
            sourceIdentity.Value.ClientMapId,
            sourceIdentity.Value.ClientAreaId);
        if (!route.Succeeded || route.Value is null)
        {
            // A verified map identity and in-bounds persisted position are sufficient to
            // enter the world. Absence of an evidence-backed outbound route disables only
            // the portal slice for this session; explicit portal use remains fail-closed
            // because no SessionRuntime is registered.
            if (string.Equals(route.Error.Code, "wire.portal.route_missing", StringComparison.Ordinal))
            {
                return OperationResult.Success;
            }

            return OperationResult.Failure(route.Error.Code, route.Error.Message, route.Error.Source);
        }

        var targetIdentity = _mapIdentities.ResolveClientMap(
            route.Value.TargetClientMapId,
            route.Value.TargetClientAreaId);
        if (!targetIdentity.Succeeded || targetIdentity.Value is null)
        {
            return OperationResult.Failure(
                targetIdentity.Error.Code,
                targetIdentity.Error.Message,
                targetIdentity.Error.Source);
        }

        var authoritativeCharacter = character with
        {
            MapId = persisted.CurrentMapId,
            PositionX = persisted.RawPosition.X,
            PositionY = persisted.RawPosition.Y
        };

        lock (_creationGate)
        {
            if (_sessions.ContainsKey(session.SessionId))
            {
                return OperationResult.Success;
            }

            _sessions[session.SessionId] = SessionRuntime.Create(
                session,
                authoritativeCharacter,
                persisted,
                _persistence,
                sourceIdentity.Value,
                targetIdentity.Value,
                route.Value);
        }

        return OperationResult.Success;
    }

    public OperationResult<OfficialPortalRuntimeBinding> Resolve(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            return existing.Contexts.Resolve(sessionId);
        }

        var authoritativeSession = _sessionAuthority.GetSession(sessionId);
        if (!authoritativeSession.Succeeded ||
            authoritativeSession.Value is null ||
            authoritativeSession.Value.ProtocolStage != ProtocolStage.InWorld ||
            !authoritativeSession.Value.IsAuthenticated ||
            authoritativeSession.Value.CharacterId is null)
        {
            return OperationResult<OfficialPortalRuntimeBinding>.Failure(
                "wire.portal.authoritative_session_invalid",
                "The official portal runtime requires an authenticated InWorld session.");
        }

        return OperationResult<OfficialPortalRuntimeBinding>.Failure(
            "wire.portal.runtime_session_not_seeded",
            "The official portal runtime has not been seeded from the authenticated character state.");
    }

    public Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionRequest request,
        CancellationToken cancellationToken) =>
        _sessions.TryGetValue(request.SessionId, out var runtime)
            ? runtime.Coordinator.ExecuteAsync(request, cancellationToken)
            : Task.FromResult(WorldInteractionResult.Rejected(
                request,
                WorldInteractionResultCode.InvalidSession,
                "wire.portal.runtime_session_missing"));

    public OperationResult SynchronizeMovement(
        string sessionId,
        WorldPosition3 expectedPosition,
        WorldPosition3 position,
        WorldDirection direction) =>
        _sessions.TryGetValue(sessionId, out var runtime)
            ? runtime.SynchronizeMovement(sessionId, expectedPosition, position, direction)
            : OperationResult.Failure(
                "wire.portal.runtime_session_missing",
                "The official portal runtime session is unavailable for movement synchronization.");

    private sealed record SessionRuntime(
        IWorldInteractionCoordinator Coordinator,
        SingleActivePortalCommandContextResolver Contexts,
        InMemoryWorldInteractionSessionRegistry InteractionSessions)
    {
        public OperationResult SynchronizeMovement(
            string sessionId,
            WorldPosition3 expectedPosition,
            WorldPosition3 position,
            WorldDirection direction)
        {
            var resolved = InteractionSessions.Get(sessionId);
            if (!resolved.Succeeded || resolved.Value is null || resolved.Value.IsTransitioning)
            {
                return OperationResult.Failure(
                    "wire.portal.movement_binding_invalid",
                    "The portal movement binding is unavailable or transitioning.");
            }

            var binding = resolved.Value;
            var objectLookup = binding.MapRuntime.Objects.Get(binding.MapSession.PlayerRuntimeEntityId);
            var entityLookup = binding.MapRuntime.EntityRegistry.Get(binding.MapSession.PlayerRuntimeEntityId);
            if (!objectLookup.Succeeded || objectLookup.Value is not PlayerObject player ||
                !entityLookup.Succeeded || entityLookup.Value is not { EntityType: WorldRuntimeEntityType.Player } entity ||
                player.State.Position != expectedPosition || entity.Position != expectedPosition ||
                player.State.MapId != binding.MapSession.MapId || entity.MapId != binding.MapSession.MapId)
            {
                return OperationResult.Failure(
                    "wire.portal.movement_position_conflict",
                    "The portal runtime player no longer matches the expected authoritative position.");
            }

            binding.MapRuntime.Objects.Add(player with
            {
                State = player.State with
                {
                    Position = position,
                    Direction = direction
                }
            });
            binding.MapRuntime.EntityRegistry.Add(entity with
            {
                Position = position,
                Direction = direction,
                DirtyFlags = "AuthoritativeMovement"
            });
            InteractionSessions.Set(binding with
            {
                PlayerRuntimeVersion = checked(binding.PlayerRuntimeVersion + 1),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            return OperationResult.Success;
        }

        public static SessionRuntime Create(
            RuntimeSession session,
            CharacterSummary character,
            PortalLocationState persisted,
            IPortalTransitionStore persistence,
            OfficialPortalMapRuntimeIdentity sourceIdentity,
            OfficialPortalMapRuntimeIdentity targetIdentity,
            OfficialPortalRoute route)
        {
            var content = BuildLockedContent(sourceIdentity, targetIdentity, route);
            var factory = new MapRuntimeFactory();
            var sourceBinding = new WorldSessionBinder(factory).Bind(
                session,
                character,
                content,
                WorldContentMode.MariaDbAuthoritative);
            if (!sourceBinding.Succeeded || sourceBinding.Value is null)
            {
                throw new InvalidOperationException("Unable to bind the verified Phase 2 portal source map.");
            }

            var targetCreate = factory.Create(content, targetIdentity.RuntimeMapId);
            if (!targetCreate.Succeeded || targetCreate.Value is null)
            {
                throw new InvalidOperationException("Unable to create the verified Phase 2 portal target map.");
            }

            var world = new WorldRuntime();
            world.Add(sourceBinding.Value.MapRuntime);
            world.Add(targetCreate.Value.MapRuntime);
            var interactionSessions = new InMemoryWorldInteractionSessionRegistry();
            interactionSessions.Seed(sourceBinding.Value, persisted.RuntimeVersion);
            var events = new InMemoryWorldInteractionEventSink();
            var audit = new InMemoryWorldInteractionAuditLedger();
            var transitions = new PortalTransitionCoordinator(
                world,
                content,
                factory,
                interactionSessions,
                persistence,
                events,
                audit);
            var handlers = new InteractionHandlerRegistry([new PortalInteractionHandler(transitions)]);
            var coordinator = new WorldInteractionCoordinator(
                new RuntimeInteractionTargetResolver(world, interactionSessions),
                new RuntimeInteractionEligibilityPolicy(new OfficialPortalPhase2RangePolicy()),
                new NpcInteractionRouter(handlers, events),
                new InMemoryInteractionIdempotencyStore(),
                new InFlightInteractionCooldownStore(),
                events,
                audit);
            return new SessionRuntime(
                coordinator,
                new SingleActivePortalCommandContextResolver(world, interactionSessions),
                interactionSessions);
        }

        private static WorldContentSnapshot BuildLockedContent(
            OfficialPortalMapRuntimeIdentity sourceIdentity,
            OfficialPortalMapRuntimeIdentity targetIdentity,
            OfficialPortalRoute route)
        {
            var reverse = OfficialPortalRouteCatalog.Resolve(
                targetIdentity.ClientMapId,
                targetIdentity.ClientAreaId);
            var verifiedReverse = reverse.Succeeded && reverse.Value is not null &&
                reverse.Value.TargetClientMapId == sourceIdentity.ClientMapId &&
                reverse.Value.TargetClientAreaId == sourceIdentity.ClientAreaId
                    ? reverse.Value
                    : null;

            var toTarget = Portal(sourceIdentity, targetIdentity, route);
            var targetPortals = verifiedReverse is null
                ? Array.Empty<PortalDefinition>()
                : [Portal(targetIdentity, sourceIdentity, verifiedReverse)];
            return new WorldContentSnapshot(
                new Dictionary<int, MapDefinition>
                {
                    [sourceIdentity.RuntimeMapId] = Map(
                        sourceIdentity,
                        verifiedReverse?.TargetPosition ?? route.SourceCenter,
                        [toTarget]),
                    [targetIdentity.RuntimeMapId] = Map(targetIdentity, route.TargetPosition, targetPortals)
                },
                []);
        }

        private static PortalDefinition Portal(
            OfficialPortalMapRuntimeIdentity source,
            OfficialPortalMapRuntimeIdentity target,
            OfficialPortalRoute route) => new(
                PortalId: 0x50480000L | ((long)route.SourceClientMapId << 10) | route.TargetClientMapId,
                new PortalEndpoint(source.RuntimeMapId, route.SourceCenter),
                new PortalEndpoint(target.RuntimeMapId, route.TargetPosition),
                route.TriggerType,
                "None",
                route.Evidence,
                $"Official Map {route.SourceClientMapId} to Map {route.TargetClientMapId}",
                RawMetadata: $"{{\"sourceRadius\":{route.SourceRadius},\"runtimeIdentity\":\"server-deterministic\"}}",
                ContentVersion: OfficialPortalWireCodec.ClientBuildId,
                TriggerRadius: route.SourceRadius);

        private static MapDefinition Map(
            OfficialPortalMapRuntimeIdentity identity,
            WorldPosition3 position,
            IReadOnlyList<PortalDefinition> portals) =>
            new(
                identity.RuntimeMapId,
                identity.ClientMapId,
                $"Official Phase 2 Map {identity.ClientMapId}",
                identity.ResourceIdentity,
                identity.Bounds,
                [new SpawnPoint("VerifiedPortalArrival", position, WorldDirection.Unknown, OfficialPortalWireCodec.EvidenceId)],
                [],
                [],
                portals,
                [],
                [],
                new ClientMapIdentity(
                    identity.RuntimeMapId,
                    identity.ClientMapId,
                    identity.ClientAreaId,
                    identity.ResourceIdentity,
                    OfficialPortalWireCodec.ClientBuildId,
                    1,
                    1,
                    0,
                    0,
                    MapIdentityEvidenceStatus.Verified,
                    MapIdentityEvidenceStatus.Verified,
                    true,
                    identity.Evidence));
    }
}

public sealed class OfficialPortalPhase2RangePolicy : IInteractionRangePolicy
{
    public InteractionRangePolicyStatus Status => InteractionRangePolicyStatus.Verified;

    public InteractionRangeEvaluation Evaluate(PlayerState player, IRuntimeObject target)
    {
        if (target is not PortalObject portal || player.MapId != portal.State.Source.MapId)
        {
            return new InteractionRangeEvaluation(false, Status, "wire.portal.exact_position_required");
        }

        // Explicit activation is already gated by the verified client command and the
        // source map's single active route. Its packet carries no coordinate, so the
        // route's observed landing/source anchor must not be treated as a range proof.
        if (string.Equals(portal.State.TriggerType, "ExplicitActivation", StringComparison.Ordinal))
        {
            return new InteractionRangeEvaluation(true, Status, string.Empty);
        }

        var deltaX = (long)player.Position.X - portal.State.Source.Position.X;
        var deltaY = (long)player.Position.Y - portal.State.Source.Position.Y;
        var radius = Math.Max(0, portal.State.TriggerRadius);
        if ((deltaX * deltaX) + (deltaY * deltaY) > (long)radius * radius)
        {
            return new InteractionRangeEvaluation(false, Status, "wire.portal.outside_trigger_region");
        }

        return new InteractionRangeEvaluation(true, Status, string.Empty);
    }
}

public sealed record OfficialPortalCanonicalCommand(
    string SessionId,
    long ClientSequence,
    string CorrelationId,
    string ClientBuildId,
    string EncodedRequestSha256,
    OfficialPortalActivateCommand WireCommand,
    OfficialPortalRuntimeBinding RuntimeBinding);

public sealed record OfficialPortalReceipt(
    string TransactionId,
    string JournalId,
    string OutboxId,
    string SafeSessionId,
    string IdempotencyKeyHash,
    long ClientSequence,
    string CorrelationId,
    string PayloadSha256,
    string ResponseSha256,
    OfficialPortalTransactionCode Code,
    bool TransactionCommitted,
    bool OutboxDispatched,
    int RuntimeMutationCount,
    int NetworkSendCount,
    int SourceRuntimeMapId,
    int TargetRuntimeMapId,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    string FailureCode,
    IReadOnlyList<ReadOnlyMemory<byte>> OrderedEncodedFrames,
    IReadOnlyList<string> OrderedStages);

public sealed record OfficialPortalIngressResult(
    bool Recognized,
    OfficialPortalReceipt Receipt)
{
    public bool Succeeded => Receipt.Code is OfficialPortalTransactionCode.Committed or
        OfficialPortalTransactionCode.DuplicateCommitted or
        OfficialPortalTransactionCode.RecoveryRequired;
}

public sealed record OfficialPortalDiagnostics(
    int ReceiptCount,
    int CommittedCount,
    int PendingOutboxCount,
    int RuntimeMutationCount,
    int NetworkSendCount);

public sealed class OfficialPortalClosedLoop
{
    private readonly IOfficialPortalCommandContextResolver _contexts;
    private readonly IWorldInteractionCoordinator _runtime;
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private readonly Dictionary<string, OfficialPortalReceipt> _receipts = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, string TransactionId)> _receiptOrder = [];
    private readonly int _maximumRetainedReceipts;

    public OfficialPortalClosedLoop(
        IOfficialPortalCommandContextResolver contexts,
        IWorldInteractionCoordinator runtime,
        int maximumRetainedReceipts = 4_096)
    {
        _contexts = contexts;
        _runtime = runtime;
        _maximumRetainedReceipts = Math.Max(1, maximumRetainedReceipts);
    }

    public OfficialPortalFailurePoint FailurePoint { get; set; }

    public Task<OperationResult> SeedSessionAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken) =>
        _contexts is IOfficialPortalSessionSeeder seeder
            ? seeder.SeedSessionAsync(session, character, cancellationToken)
            : Task.FromResult(OperationResult.Failure(
                "wire.portal.seed_not_supported",
                "The configured portal context resolver does not support authoritative session seeding."));

    public OperationResult SynchronizeMovement(
        string sessionId,
        WorldPosition3 expectedPosition,
        WorldPosition3 position,
        WorldDirection direction) =>
        _contexts is IOfficialPortalMovementSynchronizer synchronizer
            ? synchronizer.SynchronizeMovement(sessionId, expectedPosition, position, direction)
            : OperationResult.Failure(
                "wire.portal.movement_sync_not_supported",
                "The configured portal runtime does not support movement synchronization.");

    public bool IsMovementTrigger(string sessionId, WorldPosition3 position)
        => IsMovementTrigger(sessionId, position, position);

    public bool IsMovementTrigger(
        string sessionId,
        WorldPosition3 previousPosition,
        WorldPosition3 position)
        => TryResolveMovementTrigger(sessionId, previousPosition, position, out _);

    public bool TryResolveMovementTrigger(
        string sessionId,
        WorldPosition3 previousPosition,
        WorldPosition3 position,
        out WorldPosition3? triggerPosition)
    {
        triggerPosition = null;
        var binding = _contexts.Resolve(sessionId);
        if (!binding.Succeeded || binding.Value is null ||
            binding.Value.SourcePosition is null)
        {
            return false;
        }

        if (!string.Equals(binding.Value.TriggerType, "MovementRegion", StringComparison.Ordinal))
        {
            return false;
        }

        var source = binding.Value.SourcePosition;
        triggerPosition = source;
        var radius = Math.Max(0, binding.Value.SourceRadius);
        var pathX = (long)position.X - previousPosition.X;
        var pathY = (long)position.Y - previousPosition.Y;
        var pathLengthSquared = (pathX * pathX) + (pathY * pathY);
        if (pathLengthSquared == 0)
        {
            var deltaX = (long)position.X - source.X;
            var deltaY = (long)position.Y - source.Y;
            return (deltaX * deltaX) + (deltaY * deltaY) <= (long)radius * radius;
        }

        var sourceX = (long)source.X - previousPosition.X;
        var sourceY = (long)source.Y - previousPosition.Y;
        if ((sourceX * sourceX) + (sourceY * sourceY) <= (long)radius * radius)
        {
            return false;
        }

        var projection = Math.Clamp(
            ((double)sourceX * pathX + (double)sourceY * pathY) / pathLengthSquared,
            0d,
            1d);
        var closestX = previousPosition.X + (projection * pathX);
        var closestY = previousPosition.Y + (projection * pathY);
        var distanceX = source.X - closestX;
        var distanceY = source.Y - closestY;
        return (distanceX * distanceX) + (distanceY * distanceY) <= (double)radius * radius;
    }

    public async Task<OfficialPortalIngressResult> ExecuteFrameAsync(
        string sessionId,
        long clientSequence,
        string correlationId,
        ReadOnlyMemory<byte> encodedFrame,
        CancellationToken cancellationToken)
    {
        var recognized = encodedFrame.Length == 8 &&
            encodedFrame.Span[0] == 8 &&
            encodedFrame.Span[1] == 0 &&
            encodedFrame.Span[2] == OfficialPortalWireCodec.ActivateOpcode;
        if (!recognized)
        {
            return new OfficialPortalIngressResult(false, Failure(
                sessionId, clientSequence, correlationId,
                OfficialPortalTransactionCode.RolledBack,
                "wire.portal.not_recognized"));
        }

        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldClientFrame(encodedFrame.Span);
        var decoded = OfficialPortalWireCodec.DecodeActivate(
            OfficialPortalWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedFrame);
        if (!decoded.Succeeded || decoded.Value is null)
        {
            return new OfficialPortalIngressResult(true, Failure(
                sessionId, clientSequence, correlationId,
                OfficialPortalTransactionCode.RolledBack,
                decoded.FailureCode));
        }

        var binding = _contexts.Resolve(sessionId);
        if (!binding.Succeeded || binding.Value is null)
        {
            return new OfficialPortalIngressResult(true, Failure(
                sessionId, clientSequence, correlationId,
                OfficialPortalTransactionCode.RolledBack,
                binding.Error.Code));
        }

        var command = new OfficialPortalCanonicalCommand(
            sessionId,
            clientSequence,
            correlationId,
            OfficialPortalWireCodec.ClientBuildId,
            Sha256Hex(encodedFrame.Span),
            decoded.Value,
            binding.Value);
        return new OfficialPortalIngressResult(
            true,
            await ExecuteAsync(command, cancellationToken));
    }

    public async Task<OfficialPortalReceipt> ExecuteAsync(
        OfficialPortalCanonicalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Validate(command))
        {
            return Failure(
                command.SessionId,
                command.ClientSequence,
                command.CorrelationId,
                OfficialPortalTransactionCode.RolledBack,
                "wire.portal.command_invalid");
        }

        var binding = command.RuntimeBinding;
        var preflight = OfficialPortalWireCodec.SerializeVerifiedClientDestination(
            binding.TargetClientMapId,
            binding.TargetClientAreaId,
            binding.TargetPosition.X,
            binding.TargetPosition.Y);
        if (!preflight.Succeeded)
        {
            return Failure(
                command.SessionId,
                command.ClientSequence,
                command.CorrelationId,
                OfficialPortalTransactionCode.RolledBack,
                preflight.FailureCode);
        }

        var key = Sha256Hex($"{command.SessionId}|{command.ClientSequence}");
        var payloadHash = PayloadHash(command);
        await _transactionGate.WaitAsync(cancellationToken);
        try
        {
            if (_receipts.TryGetValue(key, out var existing))
            {
                return existing.PayloadSha256 == payloadHash
                    ? Freeze(existing with { Code = OfficialPortalTransactionCode.DuplicateCommitted })
                    : Freeze(existing with
                    {
                        Code = OfficialPortalTransactionCode.ReplayConflict,
                        FailureCode = "wire.portal.sequence_payload_conflict",
                        OrderedEncodedFrames = EmptyFrames()
                    });
            }

            if (FailurePoint == OfficialPortalFailurePoint.BeforeRuntime)
            {
                return Failure(
                    command.SessionId,
                    command.ClientSequence,
                    command.CorrelationId,
                    OfficialPortalTransactionCode.RolledBack,
                    "wire.portal.before_runtime_injected");
            }

            var idempotencyKey = $"official-portal:{key}";
            var request = new WorldInteractionRequest(
                DeterministicGuid($"interaction|{key}|{payloadHash}"),
                idempotencyKey,
                command.SessionId,
                binding.CharacterId,
                binding.PlayerRuntimeEntityId,
                binding.PortalRuntimeEntityId,
                binding.PortalTemplateId,
                RequestedInteractionType.Portal,
                binding.ExpectedPlayerRuntimeVersion,
                command.ClientSequence,
                "OfficialPortalWirePhase2",
                DateTimeOffset.UtcNow,
                command.CorrelationId);
            var runtimeResult = await _runtime.ExecuteAsync(request, cancellationToken);
            if (!runtimeResult.Succeeded ||
                runtimeResult.TargetMapId != binding.TargetRuntimeMapId ||
                runtimeResult.RuntimeVersionAfter <= runtimeResult.RuntimeVersionBefore)
            {
                return Failure(
                    command.SessionId,
                    command.ClientSequence,
                    command.CorrelationId,
                    runtimeResult.Code == WorldInteractionResultCode.RuntimeTransitionFailure &&
                    runtimeResult.RuntimeVersionAfter > runtimeResult.RuntimeVersionBefore
                        ? OfficialPortalTransactionCode.RecoveryRequired
                        : OfficialPortalTransactionCode.RolledBack,
                    string.IsNullOrWhiteSpace(runtimeResult.FailureCode)
                        ? "wire.portal.runtime_result_invalid"
                        : runtimeResult.FailureCode);
            }

            var serialized = OfficialPortalWireCodec.SerializeVerifiedClientDestination(
                binding.TargetClientMapId,
                binding.TargetClientAreaId,
                binding.TargetPosition.X,
                binding.TargetPosition.Y);
            if (!serialized.Succeeded || serialized.Value is null)
            {
                throw new InvalidOperationException("Portal wire preflight and committed result serialization diverged.");
            }

            var encodedFrames = FreezeFrames(serialized.Value.OrderedDecodedFrames.Select(value =>
                (ReadOnlyMemory<byte>)OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(value.Span)));
            var responseHash = ResponseHash(encodedFrames);
            var transactionId = Sha256Hex($"transaction|{key}|{payloadHash}");
            var receipt = new OfficialPortalReceipt(
                transactionId,
                Sha256Hex($"journal|{transactionId}|1"),
                Sha256Hex($"outbox|{transactionId}|1"),
                SafeId(command.SessionId),
                key,
                command.ClientSequence,
                command.CorrelationId,
                payloadHash,
                responseHash,
                OfficialPortalTransactionCode.Committed,
                TransactionCommitted: true,
                OutboxDispatched: false,
                RuntimeMutationCount: runtimeResult.IsDuplicate ? 0 : 1,
                NetworkSendCount: 0,
                runtimeResult.SourceMapId,
                runtimeResult.TargetMapId.Value,
                runtimeResult.RuntimeVersionBefore,
                runtimeResult.RuntimeVersionAfter,
                string.Empty,
                encodedFrames,
                FreezeStages(
                [
                    "ClientRequest",
                    "WorldEnvelopeDecoded",
                    "PortalFamilyDecoded",
                    "CanonicalCommand",
                    "AuthoritativeContextResolved",
                    "SerializerPreflight",
                    "RuntimeCommandPrepared",
                    "RuntimeCommitted",
                    "AuthoritativeResult",
                    "ResultSerialized",
                    "JournalCommitted",
                    "OutboxAppended",
                    "TransactionCommitted"
                ]));
            _receipts[key] = receipt;
            _receiptOrder.Enqueue((key, receipt.TransactionId));
            TrimReceipts();

            return FailurePoint == OfficialPortalFailurePoint.CommitResponseLost
                ? Freeze(receipt with
                {
                    Code = OfficialPortalTransactionCode.RecoveryRequired,
                    FailureCode = "wire.portal.commit_response_lost"
                })
                : Freeze(receipt);
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public OfficialPortalReceipt MarkOutboxDispatched(OfficialPortalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _transactionGate.Wait();
        try
        {
            if (!_receipts.TryGetValue(receipt.IdempotencyKeyHash, out var committed) ||
                !committed.TransactionCommitted ||
                committed.TransactionId != receipt.TransactionId ||
                committed.PayloadSha256 != receipt.PayloadSha256 ||
                committed.ResponseSha256 != receipt.ResponseSha256)
            {
                return receipt with
                {
                    Code = OfficialPortalTransactionCode.RolledBack,
                    FailureCode = "wire.portal.outbox_before_commit"
                };
            }

            if (committed.OutboxDispatched)
            {
                return Freeze(committed with { Code = OfficialPortalTransactionCode.DuplicateCommitted });
            }

            var updated = committed with
            {
                OutboxDispatched = true,
                OrderedStages = FreezeStages(committed.OrderedStages.Append("OutboxDispatched"))
            };
            _receipts[receipt.IdempotencyKeyHash] = updated;
            return Freeze(updated with { Code = receipt.Code });
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public OfficialPortalReceipt MarkNetworkSent(OfficialPortalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _transactionGate.Wait();
        try
        {
            if (!_receipts.TryGetValue(receipt.IdempotencyKeyHash, out var committed) ||
                !committed.OutboxDispatched ||
                committed.TransactionId != receipt.TransactionId ||
                committed.PayloadSha256 != receipt.PayloadSha256 ||
                committed.ResponseSha256 != receipt.ResponseSha256)
            {
                return receipt with
                {
                    Code = OfficialPortalTransactionCode.RolledBack,
                    FailureCode = "wire.portal.send_before_commit_or_outbox"
                };
            }

            var updated = committed with
            {
                NetworkSendCount = checked(committed.NetworkSendCount + 1),
                OrderedStages = committed.NetworkSendCount == 0
                    ? FreezeStages(committed.OrderedStages.Append("NetworkSend"))
                    : committed.OrderedStages
            };
            _receipts[receipt.IdempotencyKeyHash] = updated;
            return Freeze(updated with { Code = receipt.Code });
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public IReadOnlyList<OfficialPortalReceipt> Snapshot()
    {
        _transactionGate.Wait();
        try
        {
            return new ReadOnlyCollection<OfficialPortalReceipt>(_receipts.Values
                .OrderBy(value => value.TransactionId, StringComparer.Ordinal)
                .Select(Freeze)
                .ToArray());
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public void Restore(IEnumerable<OfficialPortalReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        _transactionGate.Wait();
        try
        {
            foreach (var receipt in receipts)
            {
                if (!receipt.TransactionCommitted ||
                    !IsSha256(receipt.TransactionId) ||
                    !IsSha256(receipt.JournalId) ||
                    !IsSha256(receipt.OutboxId) ||
                    !IsSha256(receipt.IdempotencyKeyHash) ||
                    !IsSha256(receipt.PayloadSha256) ||
                    !IsSha256(receipt.ResponseSha256) ||
                    receipt.OrderedEncodedFrames.Count != 2 ||
                    !HasExpectedFrameLengths(receipt.OrderedEncodedFrames) ||
                    !receipt.OrderedStages.Contains("TransactionCommitted", StringComparer.Ordinal) ||
                    receipt.ResponseSha256 != ResponseHash(receipt.OrderedEncodedFrames))
                {
                    throw new InvalidDataException("Official portal recovery receipt failed integrity validation.");
                }

                _receipts[receipt.IdempotencyKeyHash] = Freeze(receipt);
                _receiptOrder.Enqueue((receipt.IdempotencyKeyHash, receipt.TransactionId));
                TrimReceipts();
            }
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    private static bool HasExpectedFrameLengths(IReadOnlyList<ReadOnlyMemory<byte>> frames) =>
        (frames[0].Length == 6 && frames[1].Length == 12) ||
        (frames[0].Length == 12 && frames[1].Length == 6);

    public OfficialPortalDiagnostics Diagnostics()
    {
        _transactionGate.Wait();
        try
        {
            return new OfficialPortalDiagnostics(
                _receipts.Count,
                _receipts.Values.Count(value => value.TransactionCommitted),
                _receipts.Values.Count(value => !value.OutboxDispatched),
                _receipts.Values.Sum(value => value.RuntimeMutationCount),
                _receipts.Values.Sum(value => value.NetworkSendCount));
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public bool RemoveSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var safeSessionId = SafeId(sessionId);
        _transactionGate.Wait();
        try
        {
            var keys = _receipts
                .Where(value =>
                    value.Value.SafeSessionId == safeSessionId &&
                    value.Value.NetworkSendCount > 0)
                .Select(value => value.Key)
                .ToArray();
            foreach (var key in keys)
            {
                _receipts.Remove(key);
            }

            var runtimeRemoved = _contexts is IOfficialPortalSessionLifecycle lifecycle &&
                lifecycle.RemoveSession(sessionId);
            return keys.Length > 0 || runtimeRemoved;
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    private static bool Validate(OfficialPortalCanonicalCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.SessionId) ||
            command.ClientSequence <= 0 ||
            string.IsNullOrWhiteSpace(command.CorrelationId) ||
            !string.Equals(command.ClientBuildId, OfficialPortalWireCodec.ClientBuildId, StringComparison.Ordinal) ||
            !string.Equals(command.WireCommand.ClientBuildId, command.ClientBuildId, StringComparison.Ordinal) ||
            !string.Equals(command.WireCommand.EvidenceId, OfficialPortalWireCodec.EvidenceId, StringComparison.Ordinal) ||
            !IsSha256(command.EncodedRequestSha256) ||
            !IsSha256(command.WireCommand.DecodedRequestSha256) ||
            !string.Equals(command.RuntimeBinding.SessionId, command.SessionId, StringComparison.Ordinal) ||
            command.RuntimeBinding.CharacterId <= 0 ||
            command.RuntimeBinding.PlayerRuntimeEntityId <= 0 ||
            command.RuntimeBinding.PortalRuntimeEntityId <= 0 ||
            command.RuntimeBinding.PortalTemplateId <= 0 ||
            command.RuntimeBinding.ExpectedPlayerRuntimeVersion < 0 ||
            command.RuntimeBinding.TargetClientMapId > 1023 ||
            command.RuntimeBinding.TargetClientAreaId > 63)
        {
            return false;
        }

        var serialized = OfficialPortalWireCodec.SerializeActivate(command.WireCommand);
        if (!serialized.Succeeded)
        {
            return false;
        }

        return string.Equals(
                Sha256Hex(serialized.Value.Span),
                command.WireCommand.DecodedRequestSha256,
                StringComparison.Ordinal) &&
            string.Equals(
                Sha256Hex(OfficialClientWorldProtocolFrames.EncodeWorldClientFrame(serialized.Value.Span)),
                command.EncodedRequestSha256,
                StringComparison.Ordinal);
    }

    private void TrimReceipts()
    {
        while (_receipts.Count > _maximumRetainedReceipts && _receiptOrder.TryDequeue(out var oldest))
        {
            if (_receipts.TryGetValue(oldest.Key, out var current) &&
                current.TransactionId == oldest.TransactionId)
            {
                _receipts.Remove(oldest.Key);
            }
        }
    }

    private static string PayloadHash(OfficialPortalCanonicalCommand command)
    {
        // Idempotency identifies the immutable client request. Runtime binding is
        // authoritative server state and necessarily changes after a successful
        // transition; including it would turn a byte-for-byte retransmit into a
        // false replay conflict.
        return Sha256Hex(string.Join('|',
            command.ClientBuildId,
            command.EncodedRequestSha256,
            command.WireCommand.DecodedRequestSha256));
    }

    private static OfficialPortalReceipt Failure(
        string sessionId,
        long clientSequence,
        string correlationId,
        OfficialPortalTransactionCode code,
        string failureCode) =>
        new(
            string.Empty,
            string.Empty,
            string.Empty,
            SafeId(sessionId ?? string.Empty),
            string.Empty,
            clientSequence,
            correlationId ?? string.Empty,
            string.Empty,
            string.Empty,
            code,
            TransactionCommitted: false,
            OutboxDispatched: false,
            RuntimeMutationCount: 0,
            NetworkSendCount: 0,
            SourceRuntimeMapId: 0,
            TargetRuntimeMapId: 0,
            RuntimeVersionBefore: 0,
            RuntimeVersionAfter: 0,
            failureCode,
            EmptyFrames(),
            FreezeStages(["Rejected"]));

    private static OfficialPortalReceipt Freeze(OfficialPortalReceipt receipt) => receipt with
    {
        OrderedEncodedFrames = FreezeFrames(receipt.OrderedEncodedFrames),
        OrderedStages = FreezeStages(receipt.OrderedStages)
    };

    private static IReadOnlyList<ReadOnlyMemory<byte>> EmptyFrames() =>
        new ReadOnlyCollection<ReadOnlyMemory<byte>>([]);

    private static IReadOnlyList<ReadOnlyMemory<byte>> FreezeFrames(IEnumerable<ReadOnlyMemory<byte>> frames) =>
        new ReadOnlyCollection<ReadOnlyMemory<byte>>(frames.Select(value => (ReadOnlyMemory<byte>)value.ToArray()).ToArray());

    private static IReadOnlyList<string> FreezeStages(IEnumerable<string> stages) =>
        new ReadOnlyCollection<string>(stages.ToArray());

    private static string ResponseHash(IEnumerable<ReadOnlyMemory<byte>> frames)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var frame in frames)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, frame.Length);
            hash.AppendData(length);
            hash.AppendData(frame.Span);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Guid DeterministicGuid(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string SafeId(string value) => Sha256Hex(value)[..16];

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Sha256Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value));
}
