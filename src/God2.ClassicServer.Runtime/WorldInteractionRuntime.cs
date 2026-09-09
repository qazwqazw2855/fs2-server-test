using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum RequestedInteractionType
{
    NpcPrimary,
    Merchant,
    Portal,
    Dialog,
    Quest,
    Service,
    Unknown
}

public enum WorldInteractionResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    TargetNotFound,
    TargetInactive,
    InvalidSession,
    OwnershipMismatch,
    DifferentMap,
    DifferentInstance,
    OutOfRange,
    RangeEvidenceBlocked,
    UnsupportedInteraction,
    CooldownActive,
    ReplayConflict,
    VersionConflict,
    InvalidPortalDestination,
    InventoryTransactionRejected,
    PersistenceFailure,
    RuntimeTransitionFailure,
    InternalFailure
}

public enum InteractionRangePolicyStatus
{
    Verified,
    Baseline,
    TestOnly,
    EvidenceBlocked
}

public enum PortalTransitionState
{
    Created,
    Validating,
    SourceReserved,
    PersistencePreparing,
    RuntimeLeavingSource,
    SessionRebinding,
    RuntimeEnteringTarget,
    ReplicationRebuilding,
    Persisted,
    Committed,
    Rejected,
    RollbackPending,
    RolledBack,
    RecoveryRequired,
    Faulted
}

public enum WorldInteractionEventKind
{
    InteractionRequested,
    InteractionRejected,
    InteractionCompleted,
    NpcInteractionResolved,
    MerchantServiceResolved,
    PortalActivationRequested,
    PortalTransitionStarted,
    PlayerLeavingMap,
    PlayerEnteredMap,
    SessionMapRebound,
    ReplicationScopeCleared,
    ReplicationScopeRebuilt,
    PortalTransitionCommitted,
    PortalTransitionRolledBack,
    PortalTransitionRecoveryRequired
}

public enum WorldInteractionFailurePoint
{
    None,
    TargetDestroyedDuringValidation,
    TargetMapCreationFailure,
    PersistenceFailureBeforeRuntimeMutation,
    PersistenceFailureAfterSourceRemoval,
    RuntimeFailureAfterDatabaseCommit,
    SessionRebindFailure,
    TargetRegistryAddFailure,
    ReplicationClearFailure,
    ReplicationRebuildFailure,
    DisconnectDuringTransition,
    ReconnectDuringTransition,
    VersionConflict,
    AuditFailure,
    RollbackFailure
}

public sealed class WorldInteractionFailureInjection
{
    public WorldInteractionFailurePoint Point { get; set; }

    public bool RollbackFailureEnabled { get; set; }
}

public sealed record WorldInteractionRequest(
    Guid InteractionId,
    string IdempotencyKey,
    string SessionId,
    long CharacterId,
    long PlayerRuntimeEntityId,
    long TargetRuntimeEntityId,
    int? TargetTemplateId,
    RequestedInteractionType RequestedInteractionType,
    long ExpectedPlayerRuntimeVersion,
    long? ClientSequenceCandidate,
    string Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId,
    InventoryTransactionRequest? MerchantTransaction = null);

public sealed record WorldInteractionResult(
    WorldInteractionResultCode Code,
    Guid InteractionId,
    string IdempotencySafeId,
    string Handler,
    string FailureCode,
    bool IsDuplicate,
    Guid? TransitionId,
    int SourceMapId,
    int? TargetMapId,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    InventoryTransactionResult? InventoryResult,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is WorldInteractionResultCode.Success or WorldInteractionResultCode.DuplicateCompleted;

    public static WorldInteractionResult Rejected(
        WorldInteractionRequest request,
        WorldInteractionResultCode code,
        string failureCode,
        string handler = "",
        int sourceMapId = 0,
        long runtimeVersion = 0) =>
        new(
            code,
            request.InteractionId,
            WorldInteractionHash.SafeId(request.IdempotencyKey),
            handler,
            failureCode,
            false,
            null,
            sourceMapId,
            null,
            runtimeVersion,
            runtimeVersion,
            null,
            []);
}

public sealed record InteractionSessionBinding(
    RuntimeSession Session,
    MapSession MapSession,
    MapRuntime MapRuntime,
    string WorldInstanceId,
    long PlayerRuntimeVersion,
    bool IsTransitioning,
    DateTimeOffset UpdatedAtUtc);

public interface IWorldInteractionSessionRegistry
{
    OperationResult<InteractionSessionBinding> Get(string sessionId);

    void Set(InteractionSessionBinding binding);

    bool Remove(string sessionId);
}

public sealed class InMemoryWorldInteractionSessionRegistry : IWorldInteractionSessionRegistry
{
    private readonly ConcurrentDictionary<string, InteractionSessionBinding> _bindings = [];

    public IReadOnlyList<InteractionSessionBinding> Snapshot => _bindings.Values
        .OrderBy(binding => binding.Session.SessionId, StringComparer.Ordinal)
        .ToArray();

    public OperationResult<InteractionSessionBinding> Get(string sessionId) =>
        _bindings.TryGetValue(sessionId, out var binding)
            ? OperationResult<InteractionSessionBinding>.Success(binding)
            : OperationResult<InteractionSessionBinding>.Failure(
                "interaction.session_not_bound",
                "Session is not bound to a world runtime.",
                sessionId);

    public void Set(InteractionSessionBinding binding) =>
        _bindings[binding.Session.SessionId] = binding;

    public void Seed(WorldSessionBinding binding, long playerRuntimeVersion = 0) =>
        Set(new InteractionSessionBinding(
            binding.Session,
            binding.MapSession,
            binding.MapRuntime,
            binding.MapRuntime.WorldInstanceId,
            playerRuntimeVersion,
            false,
            DateTimeOffset.UtcNow));

    public bool Remove(string sessionId) =>
        _bindings.TryRemove(sessionId, out _);
}

public sealed record ResolvedInteractionTarget(
    InteractionSessionBinding Binding,
    PlayerObject Player,
    MapRuntime TargetMapRuntime,
    IRuntimeObject Target,
    NpcPlacementRecord? NpcDefinition,
    PortalDefinition? PortalDefinition,
    MerchantMapping? MerchantDefinition);

public interface IInteractionTargetResolver
{
    OperationResult<ResolvedInteractionTarget> Resolve(WorldInteractionRequest request);
}

public sealed class RuntimeInteractionTargetResolver : IInteractionTargetResolver
{
    private readonly WorldRuntime _world;
    private readonly IWorldInteractionSessionRegistry _sessions;

    public RuntimeInteractionTargetResolver(WorldRuntime world, IWorldInteractionSessionRegistry sessions)
    {
        _world = world;
        _sessions = sessions;
    }

    public OperationResult<ResolvedInteractionTarget> Resolve(WorldInteractionRequest request)
    {
        var bindingResult = _sessions.Get(request.SessionId);
        if (!bindingResult.Succeeded || bindingResult.Value is null)
        {
            return Failure("interaction.invalid_session", "Session is not bound to the world runtime.");
        }

        var binding = bindingResult.Value;
        if (!binding.Session.IsAuthenticated ||
            binding.Session.IsClosing ||
            binding.Session.CharacterId != request.CharacterId)
        {
            return Failure("interaction.ownership_mismatch", "Session does not own the requested character.");
        }

        if (binding.MapSession.CharacterId != request.CharacterId ||
            binding.MapSession.PlayerRuntimeEntityId != request.PlayerRuntimeEntityId)
        {
            return Failure("interaction.player_binding_mismatch", "Player runtime identity does not match the session binding.");
        }

        var playerResult = binding.MapRuntime.Objects.Get(request.PlayerRuntimeEntityId);
        if (!playerResult.Succeeded || playerResult.Value is not PlayerObject player)
        {
            return Failure("interaction.player_not_found", "Player runtime object is not active in the bound map.");
        }

        MapRuntime? targetMap = null;
        IRuntimeObject? target = null;
        foreach (var map in _world.ActiveMaps.Values)
        {
            var candidate = map.Objects.Get(request.TargetRuntimeEntityId);
            if (candidate.Succeeded && candidate.Value is not null)
            {
                targetMap = map;
                target = candidate.Value;
                break;
            }
        }

        if (target is null || targetMap is null)
        {
            return Failure("interaction.target_not_found", "Target runtime object is not active.");
        }

        if (request.TargetTemplateId is not null &&
            request.TargetTemplateId.Value != target.Identity.TemplateId)
        {
            return Failure("interaction.target_template_mismatch", "Client target template does not match the resolved runtime target.");
        }

        var npc = target is NpcObject npcObject
            ? targetMap.Definition.NpcPlacements.FirstOrDefault(definition =>
                definition.PlacementId == npcObject.State.PlacementId &&
                definition.NpcTemplateId == npcObject.State.NpcTemplateId)
            : null;
        var portal = target is PortalObject portalObject
            ? targetMap.Definition.Portals.FirstOrDefault(definition => definition.PortalId == portalObject.State.PortalId)
            : null;
        var merchant = npc?.MerchantBindingId is int merchantId
            ? targetMap.Definition.MerchantMappings.FirstOrDefault(definition =>
                definition.MerchantTemplateId == merchantId &&
                definition.NpcTemplateId == npc.NpcTemplateId)
            : null;

        return OperationResult<ResolvedInteractionTarget>.Success(
            new ResolvedInteractionTarget(binding, player, targetMap, target, npc, portal, merchant));
    }

    private static OperationResult<ResolvedInteractionTarget> Failure(string code, string message) =>
        OperationResult<ResolvedInteractionTarget>.Failure(code, message);
}

public sealed record OfficialNpcRuntimeContext(
    WorldSessionBinding Binding,
    PlayerObject Player,
    NpcObject Npc,
    ushort ClientEntityHandle);

/// <summary>
/// Production resolver over the authoritative world-session binding. Unlike the headless
/// WorldRuntime resolver, it never scans another map or accepts an object outside the
/// session's MariaDB-backed MapRuntime.
/// </summary>
public sealed class AuthoritativeWorldSessionInteractionTargetResolver : IInteractionTargetResolver
{
    private readonly IWorldSessionCoordinator _sessions;

    public AuthoritativeWorldSessionInteractionTargetResolver(IWorldSessionCoordinator sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public OperationResult<OfficialNpcRuntimeContext> ResolveClientNpc(string sessionId, ushort clientEntityHandle)
    {
        var bindingResult = _sessions.GetBinding(sessionId);
        if (!bindingResult.Succeeded || bindingResult.Value is null)
        {
            return OperationResult<OfficialNpcRuntimeContext>.Failure(
                "interaction.invalid_session",
                "Session is not bound to the authoritative world runtime.",
                sessionId);
        }

        var binding = bindingResult.Value;
        var playerResult = binding.MapRuntime.Objects.Get(binding.MapSession.PlayerRuntimeEntityId);
        if (!playerResult.Succeeded || playerResult.Value is not PlayerObject player)
        {
            return OperationResult<OfficialNpcRuntimeContext>.Failure(
                "interaction.player_not_found",
                "Player runtime object is not active in the authoritative map.",
                sessionId);
        }

        var candidates = binding.MapRuntime.Objects
            .OfType<NpcObject>()
            .Where(npc => npc.State.WireIdentity is { } wire &&
                wire.EntityHandle == clientEntityHandle &&
                string.Equals(wire.ClientBuildId, OfficialNpcInteractionWireCodec.ClientBuildId, StringComparison.Ordinal) &&
                string.Equals(wire.EvidenceStatus, "Verified", StringComparison.Ordinal) &&
                OfficialNpcReplicationWireCodec.Validate(npc.State) is null)
            .ToArray();
        if (candidates.Length != 1)
        {
            return OperationResult<OfficialNpcRuntimeContext>.Failure(
                candidates.Length == 0 ? "interaction.target_not_found" : "interaction.target_ambiguous",
                candidates.Length == 0
                    ? "No active authoritative NPC has the selected official Client handle."
                    : "More than one authoritative NPC has the selected official Client handle.",
                clientEntityHandle.ToString(CultureInfo.InvariantCulture));
        }

        return OperationResult<OfficialNpcRuntimeContext>.Success(
            new OfficialNpcRuntimeContext(binding, player, candidates[0], clientEntityHandle));
    }

    public OperationResult<ResolvedInteractionTarget> Resolve(WorldInteractionRequest request)
    {
        var bindingResult = _sessions.GetBinding(request.SessionId);
        if (!bindingResult.Succeeded || bindingResult.Value is null)
        {
            return Failure("interaction.invalid_session", "Session is not bound to the authoritative world runtime.");
        }

        var source = bindingResult.Value;
        if (!source.Session.IsAuthenticated ||
            source.Session.IsClosing ||
            source.Session.ProtocolStage != ProtocolStage.InWorld ||
            source.Session.CharacterId != request.CharacterId ||
            source.Character.CharacterId != request.CharacterId ||
            source.MapSession.CharacterId != request.CharacterId ||
            source.MapSession.PlayerRuntimeEntityId != request.PlayerRuntimeEntityId)
        {
            return Failure("interaction.ownership_mismatch", "Session does not own the requested authoritative player.");
        }

        var playerResult = source.MapRuntime.Objects.Get(request.PlayerRuntimeEntityId);
        if (!playerResult.Succeeded || playerResult.Value is not PlayerObject player)
        {
            return Failure("interaction.player_not_found", "Player runtime object is not active in the authoritative map.");
        }

        var targetResult = source.MapRuntime.Objects.Get(request.TargetRuntimeEntityId);
        if (!targetResult.Succeeded || targetResult.Value is null)
        {
            return Failure("interaction.target_not_found", "Target runtime object is not active in the authoritative map.");
        }

        var target = targetResult.Value;
        if (request.TargetTemplateId is not null && request.TargetTemplateId.Value != target.Identity.TemplateId)
        {
            return Failure("interaction.target_template_mismatch", "Client target template does not match the authoritative runtime target.");
        }

        var npc = target is NpcObject npcObject
            ? source.MapRuntime.Definition.NpcPlacements.FirstOrDefault(definition =>
                definition.PlacementId == npcObject.State.PlacementId &&
                definition.NpcTemplateId == npcObject.State.NpcTemplateId)
            : null;
        var portal = target is PortalObject portalObject
            ? source.MapRuntime.Definition.Portals.FirstOrDefault(definition => definition.PortalId == portalObject.State.PortalId)
            : null;
        var merchant = npc?.MerchantBindingId is int merchantId
            ? source.MapRuntime.Definition.MerchantMappings.FirstOrDefault(definition =>
                definition.MerchantTemplateId == merchantId &&
                definition.NpcTemplateId == npc.NpcTemplateId)
            : null;
        var binding = new InteractionSessionBinding(
            source.Session,
            source.MapSession,
            source.MapRuntime,
            source.MapRuntime.WorldInstanceId,
            request.ExpectedPlayerRuntimeVersion,
            false,
            DateTimeOffset.UtcNow);
        return OperationResult<ResolvedInteractionTarget>.Success(
            new ResolvedInteractionTarget(binding, player, source.MapRuntime, target, npc, portal, merchant));
    }

    private static OperationResult<ResolvedInteractionTarget> Failure(string code, string message) =>
        OperationResult<ResolvedInteractionTarget>.Failure(code, message);
}

public sealed record InteractionRangeEvaluation(
    bool Allowed,
    InteractionRangePolicyStatus Status,
    string FailureCode);

public interface IInteractionRangePolicy
{
    InteractionRangePolicyStatus Status { get; }

    InteractionRangeEvaluation Evaluate(PlayerState player, IRuntimeObject target);
}

public sealed class EvidenceBlockedInteractionRangePolicy : IInteractionRangePolicy
{
    public InteractionRangePolicyStatus Status => InteractionRangePolicyStatus.EvidenceBlocked;

    public InteractionRangeEvaluation Evaluate(PlayerState player, IRuntimeObject target) =>
        new(false, Status, "interaction.range_evidence_blocked");
}

/// <summary>
/// Exact-build NPC interaction range recovered from the official Client world-selection
/// branch. RVA 0x00089511-0x0008953F takes the absolute X and Y deltas independently and
/// suppresses C2S 0x37 when either axis exceeds three Client grid units.
/// </summary>
public sealed class OfficialClientNpcInteractionRangePolicy : IInteractionRangePolicy
{
    public const int MaximumAxisDelta = 3;
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string EvidenceRva = "0x00089511-0x0008953F";

    public InteractionRangePolicyStatus Status => InteractionRangePolicyStatus.Verified;

    public InteractionRangeEvaluation Evaluate(PlayerState player, IRuntimeObject target)
    {
        if (target.State is not IMapPositionedRuntimeState positioned)
        {
            return new(false, Status, "interaction.target_not_positioned");
        }

        var deltaX = Math.Abs((long)positioned.Position.X - player.Position.X);
        var deltaY = Math.Abs((long)positioned.Position.Y - player.Position.Y);
        return deltaX <= MaximumAxisDelta && deltaY <= MaximumAxisDelta
            ? new InteractionRangeEvaluation(true, Status, "")
            : new InteractionRangeEvaluation(false, Status, "interaction.out_of_range");
    }
}

public sealed class DeterministicTestInteractionRangePolicy : IInteractionRangePolicy
{
    private readonly int _maximumRawDistance;

    public DeterministicTestInteractionRangePolicy(int maximumRawDistance)
    {
        if (maximumRawDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRawDistance));
        }

        _maximumRawDistance = maximumRawDistance;
    }

    public InteractionRangePolicyStatus Status => InteractionRangePolicyStatus.TestOnly;

    public InteractionRangeEvaluation Evaluate(PlayerState player, IRuntimeObject target)
    {
        if (target.State is not IMapPositionedRuntimeState positioned)
        {
            return new(false, Status, "interaction.target_not_positioned");
        }

        var dx = (long)positioned.Position.X - player.Position.X;
        var dy = (long)positioned.Position.Y - player.Position.Y;
        var maximum = (long)_maximumRawDistance * _maximumRawDistance;
        return ((dx * dx) + (dy * dy)) <= maximum
            ? new InteractionRangeEvaluation(true, Status, "")
            : new InteractionRangeEvaluation(false, Status, "interaction.out_of_range");
    }
}

public sealed record InteractionEligibilityResult(
    bool Allowed,
    WorldInteractionResultCode ResultCode,
    string FailureCode,
    InteractionRangePolicyStatus RangePolicyStatus);

public interface IInteractionEligibilityPolicy
{
    InteractionEligibilityResult Evaluate(WorldInteractionRequest request, ResolvedInteractionTarget target);
}

public sealed class RuntimeInteractionEligibilityPolicy : IInteractionEligibilityPolicy
{
    private readonly IInteractionRangePolicy _rangePolicy;

    public RuntimeInteractionEligibilityPolicy(IInteractionRangePolicy rangePolicy)
    {
        _rangePolicy = rangePolicy;
    }

    public InteractionEligibilityResult Evaluate(WorldInteractionRequest request, ResolvedInteractionTarget target)
    {
        if (target.Binding.IsTransitioning ||
            !string.Equals(target.Player.State.Lifecycle, "Attached", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(WorldInteractionResultCode.Rejected, "interaction.player_transitioning");
        }

        if (!IsTargetActive(target.Target))
        {
            return Reject(WorldInteractionResultCode.TargetInactive, "interaction.target_inactive");
        }

        if (target.Player.State.MapId != target.Target.Identity.MapId ||
            target.Binding.MapSession.MapId != target.Target.Identity.MapId)
        {
            return Reject(WorldInteractionResultCode.DifferentMap, "interaction.different_map");
        }

        if (!string.Equals(target.Binding.WorldInstanceId, target.TargetMapRuntime.WorldInstanceId, StringComparison.Ordinal))
        {
            return Reject(WorldInteractionResultCode.DifferentInstance, "interaction.different_instance");
        }

        if (!Supports(request.RequestedInteractionType, target.Target))
        {
            return Reject(WorldInteractionResultCode.UnsupportedInteraction, "interaction.unsupported");
        }

        var range = _rangePolicy.Evaluate(target.Player.State, target.Target);
        if (!range.Allowed)
        {
            return new InteractionEligibilityResult(
                false,
                range.Status == InteractionRangePolicyStatus.EvidenceBlocked
                    ? WorldInteractionResultCode.RangeEvidenceBlocked
                    : WorldInteractionResultCode.OutOfRange,
                range.FailureCode,
                range.Status);
        }

        return new InteractionEligibilityResult(true, WorldInteractionResultCode.Success, "", range.Status);
    }

    private InteractionEligibilityResult Reject(WorldInteractionResultCode code, string failureCode) =>
        new(false, code, failureCode, _rangePolicy.Status);

    private static bool IsTargetActive(IRuntimeObject target) =>
        target switch
        {
            NpcObject npc => npc.State.Enabled &&
                             !string.Equals(npc.State.Lifecycle, "Destroyed", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(npc.State.Lifecycle, "DespawnPending", StringComparison.OrdinalIgnoreCase),
            PortalObject portal => portal.State.IsActive,
            MerchantObject merchant => merchant.State.Enabled &&
                                       !string.Equals(merchant.State.Lifecycle, "Destroyed", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool Supports(RequestedInteractionType requested, IRuntimeObject target) =>
        (target, requested) switch
        {
            (NpcObject, RequestedInteractionType.NpcPrimary or RequestedInteractionType.Merchant or
                RequestedInteractionType.Dialog or RequestedInteractionType.Quest or RequestedInteractionType.Service) => true,
            (PortalObject, RequestedInteractionType.Portal or RequestedInteractionType.NpcPrimary) => true,
            _ => false
        };
}

public sealed record InteractionReplayLookup(
    bool Found,
    bool PayloadMatches,
    WorldInteractionResult? Result);

public interface IInteractionIdempotencyStore
{
    InteractionReplayLookup Find(string idempotencyKey, string payloadHash);

    void Complete(string idempotencyKey, string payloadHash, WorldInteractionResult result);
}

public sealed class InMemoryInteractionIdempotencyStore : IInteractionIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (string PayloadHash, WorldInteractionResult Result)> _completed = [];
    private readonly ConcurrentQueue<(string Key, string PayloadHash)> _completionOrder = [];
    private readonly int _maximumEntries;

    public InMemoryInteractionIdempotencyStore(int maximumEntries = 100_000)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        _maximumEntries = maximumEntries;
    }

    public InteractionReplayLookup Find(string idempotencyKey, string payloadHash)
    {
        if (!_completed.TryGetValue(idempotencyKey, out var existing))
        {
            return new InteractionReplayLookup(false, true, null);
        }

        return new InteractionReplayLookup(
            true,
            string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal),
            existing.Result);
    }

    public void Complete(string idempotencyKey, string payloadHash, WorldInteractionResult result)
    {
        if (!_completed.TryAdd(idempotencyKey, (payloadHash, result)))
        {
            return;
        }

        _completionOrder.Enqueue((idempotencyKey, payloadHash));
        while (_completed.Count > _maximumEntries && _completionOrder.TryDequeue(out var oldest))
        {
            if (_completed.TryGetValue(oldest.Key, out var current) &&
                string.Equals(current.PayloadHash, oldest.PayloadHash, StringComparison.Ordinal))
            {
                _completed.TryRemove(new KeyValuePair<string, (string PayloadHash, WorldInteractionResult Result)>(
                    oldest.Key,
                    current));
            }
        }
    }
}

public interface IInteractionCooldownStore
{
    bool TryEnter(long characterId);

    void Exit(long characterId);
}

public sealed class InFlightInteractionCooldownStore : IInteractionCooldownStore
{
    private readonly ConcurrentDictionary<long, byte> _active = [];

    public bool TryEnter(long characterId) => _active.TryAdd(characterId, 0);

    public void Exit(long characterId) => _active.TryRemove(characterId, out _);
}

public sealed record WorldInteractionEvent(
    Guid EventId,
    WorldInteractionEventKind Kind,
    Guid InteractionId,
    Guid? TransitionId,
    long CharacterId,
    long TargetRuntimeEntityId,
    int SourceMapId,
    int? TargetMapId,
    string State,
    string FailureCode,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public interface IWorldInteractionEventSink
{
    IReadOnlyList<WorldInteractionEvent> Events { get; }

    void Publish(WorldInteractionEvent runtimeEvent);
}

public sealed class InMemoryWorldInteractionEventSink : IWorldInteractionEventSink
{
    private readonly ConcurrentQueue<WorldInteractionEvent> _events = [];
    private readonly int _maximumEntries;

    public InMemoryWorldInteractionEventSink(int maximumEntries = 10_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
    }

    public IReadOnlyList<WorldInteractionEvent> Events => _events.ToArray();

    public void Publish(WorldInteractionEvent runtimeEvent)
    {
        _events.Enqueue(runtimeEvent);
        while (_events.Count > _maximumEntries)
        {
            _events.TryDequeue(out _);
        }
    }
}

public sealed record WorldInteractionAuditRecord(
    Guid AuditId,
    Guid InteractionId,
    Guid? TransitionId,
    string IdempotencySafeId,
    string CorrelationId,
    string SessionId,
    long CharacterId,
    long PlayerRuntimeEntityId,
    long TargetRuntimeEntityId,
    int TargetTemplateId,
    RequestedInteractionType InteractionType,
    string Handler,
    int SourceMapId,
    int? TargetMapId,
    WorldPosition3 SourcePosition,
    WorldPosition3? TargetPosition,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    WorldInteractionResultCode Result,
    string FailureCode,
    string RollbackStatus,
    bool SessionRebound,
    bool ReplicationCleared,
    bool ReplicationRebuilt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public interface IWorldInteractionAuditLedger
{
    IReadOnlyList<WorldInteractionAuditRecord> Records { get; }

    void Append(WorldInteractionAuditRecord record);
}

public sealed class InMemoryWorldInteractionAuditLedger : IWorldInteractionAuditLedger
{
    private readonly ConcurrentQueue<WorldInteractionAuditRecord> _records = [];
    private readonly int _maximumEntries;

    public InMemoryWorldInteractionAuditLedger(int maximumEntries = 10_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
    }

    public IReadOnlyList<WorldInteractionAuditRecord> Records => _records.ToArray();

    public void Append(WorldInteractionAuditRecord record)
    {
        _records.Enqueue(record);
        while (_records.Count > _maximumEntries)
        {
            _records.TryDequeue(out _);
        }
    }
}

public sealed record PortalLocationState(
    long CharacterId,
    int CurrentMapId,
    WorldPosition3 RawPosition,
    WorldDirection Direction,
    long RuntimeVersion,
    int? LastPortalTemplateId,
    Guid? LastTransitionId,
    DateTimeOffset UpdatedAtUtc);

public sealed record PortalTransitionPlan(
    Guid TransitionId,
    string IdempotencyKey,
    long CharacterId,
    string SessionId,
    long PlayerRuntimeEntityId,
    string SourceWorldInstanceId,
    int SourceMapId,
    WorldPosition3 SourcePosition,
    WorldDirection SourceDirection,
    string TargetWorldInstanceId,
    int TargetMapId,
    WorldPosition3 TargetPosition,
    WorldDirection TargetDirection,
    long ExpectedPlayerVersion,
    int PortalTemplateId,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record PortalTransitionResult(
    WorldInteractionResultCode Code,
    PortalTransitionPlan Plan,
    PortalTransitionState State,
    string FailureCode,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    bool SessionRebound,
    bool ReplicationCleared,
    bool ReplicationRebuilt,
    bool PersistenceCommitted,
    string RollbackStatus);

public sealed record PortalTransitionReplayLookup(
    bool Found,
    bool PayloadMatches,
    PortalTransitionResult? Result);

public sealed record PortalTransitionPersistenceCommit(
    PortalTransitionPlan Plan,
    PortalLocationState Before,
    PortalLocationState After,
    string PayloadHash,
    WorldInteractionAuditRecord Audit);

public interface IPortalTransitionStore
{
    Task<PortalLocationState> LoadAsync(long characterId, CancellationToken cancellationToken);

    Task<PortalTransitionReplayLookup> FindCompletedAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitAsync(
        PortalTransitionPersistenceCommit commit,
        CancellationToken cancellationToken);
}

public sealed record WorldMovementPersistenceRequest(
    long CharacterId,
    int MapId,
    WorldPosition3 ExpectedPosition,
    WorldPosition3 Position,
    WorldDirection Direction,
    DateTimeOffset UpdatedAtUtc);

public interface IWorldMovementStore
{
    Task<OperationResult> CommitMovementAsync(
        WorldMovementPersistenceRequest request,
        CancellationToken cancellationToken);
}

public sealed class InMemoryPortalTransitionStore : IPortalTransitionStore, IWorldMovementStore
{
    private readonly ConcurrentDictionary<long, PortalLocationState> _locations = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, PortalTransitionResult Result)> _completed = [];

    public WorldInteractionFailureInjection FailureInjection { get; } = new();

    public void Seed(PortalLocationState state) => _locations[state.CharacterId] = state;

    public bool TrySeed(PortalLocationState state) => _locations.TryAdd(state.CharacterId, state);

    public Task<PortalLocationState> LoadAsync(long characterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_locations.GetOrAdd(
            characterId,
            id => new PortalLocationState(
                id,
                0,
                new WorldPosition3(0, 0),
                WorldDirection.Unknown,
                0,
                null,
                null,
                DateTimeOffset.UtcNow)));
    }

    public Task<PortalTransitionReplayLookup> FindCompletedAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_completed.TryGetValue(idempotencyKey, out var value))
        {
            return Task.FromResult(new PortalTransitionReplayLookup(false, true, null));
        }

        return Task.FromResult(new PortalTransitionReplayLookup(
            true,
            string.Equals(value.PayloadHash, payloadHash, StringComparison.Ordinal),
            value.Result));
    }

    public Task<OperationResult> CommitAsync(
        PortalTransitionPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailureInjection.Point is WorldInteractionFailurePoint.PersistenceFailureBeforeRuntimeMutation or
            WorldInteractionFailurePoint.PersistenceFailureAfterSourceRemoval)
        {
            return Task.FromResult(OperationResult.Failure(
                "portal.persistence_failure",
                $"Injected failure: {FailureInjection.Point}."));
        }

        if (!_locations.TryGetValue(commit.Before.CharacterId, out var current) ||
            current.RuntimeVersion != commit.Before.RuntimeVersion)
        {
            return Task.FromResult(OperationResult.Failure(
                "portal.version_conflict",
                "Persisted player runtime version changed before portal commit."));
        }

        if (_completed.TryGetValue(commit.Plan.IdempotencyKey, out var existing))
        {
            return Task.FromResult(string.Equals(existing.PayloadHash, commit.PayloadHash, StringComparison.Ordinal)
                ? OperationResult.Success
                : OperationResult.Failure("portal.replay_conflict", "Portal idempotency key was reused with a different payload."));
        }

        var result = new PortalTransitionResult(
            WorldInteractionResultCode.Success,
            commit.Plan,
            PortalTransitionState.Committed,
            "",
            commit.Before.RuntimeVersion,
            commit.After.RuntimeVersion,
            true,
            true,
            true,
            true,
            "NotRequired");
        _locations[commit.After.CharacterId] = commit.After;
        _completed[commit.Plan.IdempotencyKey] = (commit.PayloadHash, result);
        return Task.FromResult(OperationResult.Success);
    }

    public Task<OperationResult> CommitMovementAsync(
        WorldMovementPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailureInjection.Point is WorldInteractionFailurePoint.PersistenceFailureBeforeRuntimeMutation or
            WorldInteractionFailurePoint.PersistenceFailureAfterSourceRemoval)
        {
            return Task.FromResult(OperationResult.Failure(
                "movement.persistence_failure",
                $"Injected failure: {FailureInjection.Point}."));
        }

        while (_locations.TryGetValue(request.CharacterId, out var current))
        {
            if (current.CurrentMapId != request.MapId || current.RawPosition != request.ExpectedPosition)
            {
                return Task.FromResult(OperationResult.Failure(
                    "movement.position_conflict",
                    "Persisted player position changed before movement commit."));
            }

            var updated = current with
            {
                RawPosition = request.Position,
                Direction = request.Direction,
                RuntimeVersion = checked(current.RuntimeVersion + 1),
                UpdatedAtUtc = request.UpdatedAtUtc
            };
            if (_locations.TryUpdate(request.CharacterId, updated, current))
            {
                return Task.FromResult(OperationResult.Success);
            }
        }

        return Task.FromResult(OperationResult.Failure(
            "movement.character_not_found",
            "Persisted player position was not found."));
    }
}

public interface IPortalTransitionCoordinator
{
    IReadOnlyList<PortalTransitionResult> Transitions { get; }

    Task<PortalTransitionResult> ExecuteAsync(
        WorldInteractionRequest request,
        ResolvedInteractionTarget target,
        CancellationToken cancellationToken);
}

public sealed class PortalTransitionCoordinator : IPortalTransitionCoordinator
{
    private readonly WorldRuntime _world;
    private readonly WorldContentSnapshot _content;
    private readonly MapRuntimeFactory _mapFactory;
    private readonly IWorldInteractionSessionRegistry _sessions;
    private readonly IPortalTransitionStore _persistence;
    private readonly IWorldInteractionEventSink _events;
    private readonly IWorldInteractionAuditLedger _audit;
    private readonly WorldInteractionFailureInjection _failureInjection;
    private readonly AsyncKeyedLock<long> _locks = new();
    private readonly ConcurrentQueue<PortalTransitionResult> _transitions = [];
    private readonly int _maximumRetainedTransitions;

    public PortalTransitionCoordinator(
        WorldRuntime world,
        WorldContentSnapshot content,
        MapRuntimeFactory mapFactory,
        IWorldInteractionSessionRegistry sessions,
        IPortalTransitionStore persistence,
        IWorldInteractionEventSink events,
        IWorldInteractionAuditLedger audit,
        WorldInteractionFailureInjection? failureInjection = null,
        int maximumRetainedTransitions = 4_096)
    {
        _world = world;
        _content = content;
        _mapFactory = mapFactory;
        _sessions = sessions;
        _persistence = persistence;
        _events = events;
        _audit = audit;
        _failureInjection = failureInjection ?? new WorldInteractionFailureInjection();
        _maximumRetainedTransitions = Math.Max(1, maximumRetainedTransitions);
    }

    public IReadOnlyList<PortalTransitionResult> Transitions => _transitions.ToArray();

    public async Task<PortalTransitionResult> ExecuteAsync(
        WorldInteractionRequest request,
        ResolvedInteractionTarget target,
        CancellationToken cancellationToken)
    {
        if (target.Target is not PortalObject portal || target.PortalDefinition is null)
        {
            return Track(Rejected(request, target, WorldInteractionResultCode.InvalidPortalDestination, "portal.definition_missing"));
        }

        var definition = target.PortalDefinition;
        var plan = new PortalTransitionPlan(
            Guid.NewGuid(),
            request.IdempotencyKey,
            request.CharacterId,
            request.SessionId,
            request.PlayerRuntimeEntityId,
            target.Binding.WorldInstanceId,
            target.Binding.MapSession.MapId,
            target.Player.State.Position,
            target.Player.State.Direction,
            target.Binding.WorldInstanceId,
            definition.Target.MapId,
            definition.Target.Position,
            WorldDirection.Unknown,
            request.ExpectedPlayerRuntimeVersion,
            definition.TemplateId,
            request.CreatedAtUtc,
            request.CorrelationId);
        var payloadHash = WorldInteractionHash.PortalPlan(plan);
        var replay = await _persistence.FindCompletedAsync(request.IdempotencyKey, payloadHash, cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return Track(Failed(plan, WorldInteractionResultCode.ReplayConflict, PortalTransitionState.Rejected, "portal.replay_conflict"));
        }

        if (replay is { Found: true, Result: not null })
        {
            return Track(replay.Result with
            {
                Code = WorldInteractionResultCode.DuplicateCompleted,
                State = PortalTransitionState.Committed
            });
        }

        using var lease = await _locks.TryAcquireAsync(request.CharacterId, cancellationToken);
        if (lease is null)
        {
            return Track(Failed(plan, WorldInteractionResultCode.CooldownActive, PortalTransitionState.Rejected, "portal.transition_in_flight"));
        }

        replay = await _persistence.FindCompletedAsync(request.IdempotencyKey, payloadHash, cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return Track(Failed(plan, WorldInteractionResultCode.ReplayConflict, PortalTransitionState.Rejected, "portal.replay_conflict"));
        }

        if (replay is { Found: true, Result: not null })
        {
            return Track(replay.Result with
            {
                Code = WorldInteractionResultCode.DuplicateCompleted,
                State = PortalTransitionState.Committed
            });
        }

        return await ExecuteLockedAsync(request, target, portal, definition, plan, payloadHash, cancellationToken);
    }

    private async Task<PortalTransitionResult> ExecuteLockedAsync(
        WorldInteractionRequest request,
        ResolvedInteractionTarget target,
        PortalObject portal,
        PortalDefinition definition,
        PortalTransitionPlan plan,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        Publish(WorldInteractionEventKind.PortalTransitionStarted, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.Validating.ToString(), "");
        if (!definition.Enabled ||
            !portal.State.IsActive ||
            definition.Source.MapId != plan.SourceMapId ||
            !_content.Maps.ContainsKey(definition.Target.MapId))
        {
            return Track(Failed(plan, WorldInteractionResultCode.InvalidPortalDestination, PortalTransitionState.Rejected, "portal.destination_invalid"));
        }

        if (_failureInjection.Point == WorldInteractionFailurePoint.TargetMapCreationFailure)
        {
            return Track(Failed(plan, WorldInteractionResultCode.InvalidPortalDestination, PortalTransitionState.Rejected, "portal.target_map_creation_failed"));
        }

        var targetMapResult = _world.GetMap(definition.Target.MapId);
        MapRuntime targetMap;
        if (targetMapResult.Succeeded && targetMapResult.Value is not null)
        {
            targetMap = targetMapResult.Value;
        }
        else
        {
            var create = _mapFactory.Create(_content, definition.Target.MapId);
            if (!create.Succeeded || create.Value is null)
            {
                return Track(Failed(plan, WorldInteractionResultCode.InvalidPortalDestination, PortalTransitionState.Rejected, "portal.target_map_unavailable"));
            }

            targetMap = create.Value.MapRuntime;
            _world.Add(targetMap);
        }

        var persistedBefore = await _persistence.LoadAsync(request.CharacterId, cancellationToken);
        if (persistedBefore.RuntimeVersion != request.ExpectedPlayerRuntimeVersion ||
            _failureInjection.Point == WorldInteractionFailurePoint.VersionConflict)
        {
            return Track(Failed(plan, WorldInteractionResultCode.VersionConflict, PortalTransitionState.Rejected, "portal.version_conflict"));
        }

        if (_failureInjection.Point == WorldInteractionFailurePoint.PersistenceFailureBeforeRuntimeMutation)
        {
            return Track(Failed(plan, WorldInteractionResultCode.PersistenceFailure, PortalTransitionState.Rejected, "portal.persistence_failure_before_runtime"));
        }

        var sourceMap = target.Binding.MapRuntime;
        var sourcePlayer = target.Player;
        var sourceEntityResult = sourceMap.EntityRegistry.Get(request.PlayerRuntimeEntityId);
        if (!sourceEntityResult.Succeeded || sourceEntityResult.Value is null)
        {
            return Track(Failed(plan, WorldInteractionResultCode.RuntimeTransitionFailure, PortalTransitionState.Faulted, "portal.source_entity_missing"));
        }

        var sourceEntity = sourceEntityResult.Value;
        var sourceSession = target.Binding.MapSession;
        var transitionBinding = target.Binding with
        {
            IsTransitioning = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _sessions.Set(transitionBinding);
        sourceMap.Objects.Add(sourcePlayer with { State = sourcePlayer.State with { Lifecycle = "Transitioning" } });

        var replicationCleared = false;
        var sessionRebound = false;
        var replicationRebuilt = false;
        var targetAdded = false;
        try
        {
            Publish(WorldInteractionEventKind.PlayerLeavingMap, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.RuntimeLeavingSource.ToString(), "");
            sourceMap.Replication.ClearSession(request.SessionId);
            replicationCleared = true;
            Publish(WorldInteractionEventKind.ReplicationScopeCleared, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.RuntimeLeavingSource.ToString(), "");
            if (_failureInjection.Point == WorldInteractionFailurePoint.ReplicationClearFailure)
            {
                throw new PortalTransitionException("portal.replication_clear_failed");
            }

            sourceMap.Objects.Remove(request.PlayerRuntimeEntityId);
            sourceMap.EntityRegistry.Remove(request.PlayerRuntimeEntityId);
            sourceMap.ActiveSessions.RemoveAll(session => string.Equals(session.SessionId, request.SessionId, StringComparison.Ordinal));
            if (_failureInjection.Point == WorldInteractionFailurePoint.PersistenceFailureAfterSourceRemoval)
            {
                throw new PortalTransitionException("portal.persistence_failure_after_source_removal", WorldInteractionResultCode.PersistenceFailure);
            }

            if (_failureInjection.Point is WorldInteractionFailurePoint.DisconnectDuringTransition or WorldInteractionFailurePoint.ReconnectDuringTransition)
            {
                throw new PortalTransitionException("portal.session_changed_during_transition");
            }

            var targetPlayer = sourcePlayer with
            {
                Identity = sourcePlayer.Identity with { MapId = definition.Target.MapId },
                State = sourcePlayer.State with
                {
                    MapId = definition.Target.MapId,
                    Position = definition.Target.Position,
                    Direction = WorldDirection.Unknown,
                    Lifecycle = "Attached"
                }
            };
            var targetEntity = sourceEntity with
            {
                MapId = definition.Target.MapId,
                Position = definition.Target.Position,
                Direction = WorldDirection.Unknown,
                State = "Attached",
                DirtyFlags = "PortalTransition"
            };
            if (_failureInjection.Point == WorldInteractionFailurePoint.TargetRegistryAddFailure)
            {
                throw new PortalTransitionException("portal.target_registry_add_failed");
            }

            targetMap.Objects.Add(targetPlayer);
            targetMap.EntityRegistry.Add(targetEntity);
            targetAdded = true;

            if (_failureInjection.Point == WorldInteractionFailurePoint.SessionRebindFailure)
            {
                throw new PortalTransitionException("portal.session_rebind_failed");
            }

            var targetSession = sourceSession with { MapId = definition.Target.MapId };
            targetMap.ActiveSessions.Add(targetSession);
            _sessions.Set(new InteractionSessionBinding(
                target.Binding.Session,
                targetSession,
                targetMap,
                targetMap.WorldInstanceId,
                persistedBefore.RuntimeVersion + 1,
                false,
                DateTimeOffset.UtcNow));
            sessionRebound = true;
            Publish(WorldInteractionEventKind.SessionMapRebound, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.SessionRebinding.ToString(), "");

            targetMap.Replication.RecalculateVisibility(
                targetSession,
                definition.Target.Position,
                targetMap.Objects.ActiveObjects,
                targetEntity.VisibilityRadius,
                DateTimeOffset.UtcNow);
            replicationRebuilt = true;
            Publish(WorldInteractionEventKind.ReplicationScopeRebuilt, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.ReplicationRebuilding.ToString(), "");
            if (_failureInjection.Point == WorldInteractionFailurePoint.ReplicationRebuildFailure)
            {
                throw new PortalTransitionException("portal.replication_rebuild_failed");
            }

            var persistedAfter = new PortalLocationState(
                request.CharacterId,
                definition.Target.MapId,
                definition.Target.Position,
                WorldDirection.Unknown,
                persistedBefore.RuntimeVersion + 1,
                definition.TemplateId,
                plan.TransitionId,
                DateTimeOffset.UtcNow);
            var provisional = Success(plan, persistedBefore.RuntimeVersion, persistedAfter.RuntimeVersion, sessionRebound, replicationCleared, replicationRebuilt);
            var audit = BuildAudit(request, target, provisional);
            var commit = await _persistence.CommitAsync(
                new PortalTransitionPersistenceCommit(plan, persistedBefore, persistedAfter, payloadHash, audit),
                cancellationToken);
            if (!commit.Succeeded)
            {
                throw new PortalTransitionException(
                    commit.Error.Code,
                    commit.Error.Code == "portal.version_conflict"
                        ? WorldInteractionResultCode.VersionConflict
                        : WorldInteractionResultCode.PersistenceFailure);
            }

            if (_failureInjection.Point == WorldInteractionFailurePoint.RuntimeFailureAfterDatabaseCommit)
            {
                var recovered = await _persistence.LoadAsync(request.CharacterId, cancellationToken);
                ReconcileRuntimeToPersisted(request, target.Binding.Session, targetMap, sourcePlayer, sourceEntity, recovered);
                var recovery = provisional with
                {
                    Code = WorldInteractionResultCode.RuntimeTransitionFailure,
                    State = PortalTransitionState.RecoveryRequired,
                    FailureCode = "portal.runtime_failure_after_commit_reloaded",
                    PersistenceCommitted = true,
                    RollbackStatus = "RecoveredFromPersistence"
                };
                Publish(WorldInteractionEventKind.PortalTransitionRecoveryRequired, request, plan, plan.SourceMapId, plan.TargetMapId, recovery.State.ToString(), recovery.FailureCode);
                AppendAuditSafe(BuildAudit(request, target, recovery));
                return Track(recovery);
            }

            Publish(WorldInteractionEventKind.PlayerEnteredMap, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.RuntimeEnteringTarget.ToString(), "");
            Publish(WorldInteractionEventKind.PortalTransitionCommitted, request, plan, plan.SourceMapId, plan.TargetMapId, PortalTransitionState.Committed.ToString(), "");
            AppendAuditSafe(audit);
            return Track(provisional);
        }
        catch (PortalTransitionException exception)
        {
            var rollback = Rollback(
                request,
                target.Binding,
                sourceMap,
                targetMap,
                sourcePlayer,
                sourceEntity,
                sourceSession,
                targetAdded);
            var result = new PortalTransitionResult(
                exception.Code,
                plan,
                rollback ? PortalTransitionState.RolledBack : PortalTransitionState.RecoveryRequired,
                exception.FailureCode,
                persistedBefore.RuntimeVersion,
                persistedBefore.RuntimeVersion,
                false,
                replicationCleared,
                false,
                false,
                rollback ? "RolledBack" : "RecoveryRequired");
            Publish(
                rollback ? WorldInteractionEventKind.PortalTransitionRolledBack : WorldInteractionEventKind.PortalTransitionRecoveryRequired,
                request,
                plan,
                plan.SourceMapId,
                plan.TargetMapId,
                result.State.ToString(),
                result.FailureCode);
            AppendAuditSafe(BuildAudit(request, target, result));
            return Track(result);
        }
    }

    private bool Rollback(
        WorldInteractionRequest request,
        InteractionSessionBinding originalBinding,
        MapRuntime sourceMap,
        MapRuntime targetMap,
        PlayerObject sourcePlayer,
        RuntimeEntity sourceEntity,
        MapSession sourceSession,
        bool targetAdded)
    {
        if (_failureInjection.Point == WorldInteractionFailurePoint.RollbackFailure ||
            _failureInjection.RollbackFailureEnabled)
        {
            return false;
        }

        if (targetAdded)
        {
            targetMap.Replication.ClearSession(request.SessionId);
            targetMap.ActiveSessions.RemoveAll(session => string.Equals(session.SessionId, request.SessionId, StringComparison.Ordinal));
            targetMap.Objects.Remove(request.PlayerRuntimeEntityId);
            targetMap.EntityRegistry.Remove(request.PlayerRuntimeEntityId);
        }

        sourceMap.Objects.Add(sourcePlayer);
        sourceMap.EntityRegistry.Add(sourceEntity);
        if (!sourceMap.ActiveSessions.Any(session => string.Equals(session.SessionId, request.SessionId, StringComparison.Ordinal)))
        {
            sourceMap.ActiveSessions.Add(sourceSession);
        }

        sourceMap.Replication.RecalculateVisibility(
            sourceSession,
            sourcePlayer.State.Position,
            sourceMap.Objects.ActiveObjects,
            sourcePlayer.State.VisibilityRadius,
            DateTimeOffset.UtcNow);
        _sessions.Set(originalBinding with { IsTransitioning = false, UpdatedAtUtc = DateTimeOffset.UtcNow });
        return true;
    }

    private void ReconcileRuntimeToPersisted(
        WorldInteractionRequest request,
        RuntimeSession session,
        MapRuntime targetMap,
        PlayerObject sourcePlayer,
        RuntimeEntity sourceEntity,
        PortalLocationState persisted)
    {
        var targetPlayer = sourcePlayer with
        {
            Identity = sourcePlayer.Identity with { MapId = persisted.CurrentMapId },
            State = sourcePlayer.State with
            {
                MapId = persisted.CurrentMapId,
                Position = persisted.RawPosition,
                Direction = persisted.Direction,
                Lifecycle = "Attached"
            }
        };
        var targetEntity = sourceEntity with
        {
            MapId = persisted.CurrentMapId,
            Position = persisted.RawPosition,
            Direction = persisted.Direction,
            State = "Attached",
            DirtyFlags = "PortalRecovery"
        };
        targetMap.Objects.Add(targetPlayer);
        targetMap.EntityRegistry.Add(targetEntity);
        var mapSession = new MapSession(
            request.SessionId,
            persisted.CurrentMapId,
            request.CharacterId,
            request.PlayerRuntimeEntityId,
            WorldContentMode.MariaDbAuthoritative);
        if (!targetMap.ActiveSessions.Any(value => string.Equals(value.SessionId, request.SessionId, StringComparison.Ordinal)))
        {
            targetMap.ActiveSessions.Add(mapSession);
        }

        _sessions.Set(new InteractionSessionBinding(
            session,
            mapSession,
            targetMap,
            targetMap.WorldInstanceId,
            persisted.RuntimeVersion,
            false,
            DateTimeOffset.UtcNow));
    }

    private void AppendAuditSafe(WorldInteractionAuditRecord record)
    {
        if (_failureInjection.Point == WorldInteractionFailurePoint.AuditFailure)
        {
            return;
        }

        _audit.Append(record);
    }

    private WorldInteractionAuditRecord BuildAudit(
        WorldInteractionRequest request,
        ResolvedInteractionTarget target,
        PortalTransitionResult result) =>
        new(
            Guid.NewGuid(),
            request.InteractionId,
            result.Plan.TransitionId,
            WorldInteractionHash.SafeId(request.IdempotencyKey),
            request.CorrelationId,
            request.SessionId,
            request.CharacterId,
            request.PlayerRuntimeEntityId,
            request.TargetRuntimeEntityId,
            target.Target.Identity.TemplateId,
            request.RequestedInteractionType,
            "Portal",
            result.Plan.SourceMapId,
            result.Plan.TargetMapId,
            result.Plan.SourcePosition,
            result.Plan.TargetPosition,
            result.RuntimeVersionBefore,
            result.RuntimeVersionAfter,
            result.Code,
            result.FailureCode,
            result.RollbackStatus,
            result.SessionRebound,
            result.ReplicationCleared,
            result.ReplicationRebuilt,
            request.CreatedAtUtc,
            DateTimeOffset.UtcNow);

    private PortalTransitionResult Rejected(
        WorldInteractionRequest request,
        ResolvedInteractionTarget target,
        WorldInteractionResultCode code,
        string failureCode)
    {
        var plan = new PortalTransitionPlan(
            Guid.NewGuid(),
            request.IdempotencyKey,
            request.CharacterId,
            request.SessionId,
            request.PlayerRuntimeEntityId,
            target.Binding.WorldInstanceId,
            target.Binding.MapSession.MapId,
            target.Player.State.Position,
            target.Player.State.Direction,
            target.Binding.WorldInstanceId,
            target.Binding.MapSession.MapId,
            target.Player.State.Position,
            target.Player.State.Direction,
            request.ExpectedPlayerRuntimeVersion,
            target.Target.Identity.TemplateId,
            request.CreatedAtUtc,
            request.CorrelationId);
        return Failed(plan, code, PortalTransitionState.Rejected, failureCode);
    }

    private static PortalTransitionResult Success(
        PortalTransitionPlan plan,
        long before,
        long after,
        bool sessionRebound,
        bool replicationCleared,
        bool replicationRebuilt) =>
        new(
            WorldInteractionResultCode.Success,
            plan,
            PortalTransitionState.Committed,
            "",
            before,
            after,
            sessionRebound,
            replicationCleared,
            replicationRebuilt,
            true,
            "NotRequired");

    private static PortalTransitionResult Failed(
        PortalTransitionPlan plan,
        WorldInteractionResultCode code,
        PortalTransitionState state,
        string failureCode) =>
        new(code, plan, state, failureCode, plan.ExpectedPlayerVersion, plan.ExpectedPlayerVersion, false, false, false, false, "NotStarted");

    private PortalTransitionResult Track(PortalTransitionResult result)
    {
        _transitions.Enqueue(result);
        while (_transitions.Count > _maximumRetainedTransitions)
        {
            _transitions.TryDequeue(out _);
        }

        return result;
    }

    private void Publish(
        WorldInteractionEventKind kind,
        WorldInteractionRequest request,
        PortalTransitionPlan plan,
        int sourceMapId,
        int? targetMapId,
        string state,
        string failureCode) =>
        _events.Publish(new WorldInteractionEvent(
            Guid.NewGuid(),
            kind,
            request.InteractionId,
            plan.TransitionId,
            request.CharacterId,
            request.TargetRuntimeEntityId,
            sourceMapId,
            targetMapId,
            state,
            failureCode,
            DateTimeOffset.UtcNow,
            request.CorrelationId));

    private sealed class PortalTransitionException : Exception
    {
        public PortalTransitionException(
            string failureCode,
            WorldInteractionResultCode code = WorldInteractionResultCode.RuntimeTransitionFailure)
        {
            FailureCode = failureCode;
            Code = code;
        }

        public string FailureCode { get; }

        public WorldInteractionResultCode Code { get; }
    }
}

public sealed record WorldInteractionHandlerContext(
    WorldInteractionRequest Request,
    ResolvedInteractionTarget Target,
    InteractionEligibilityResult Eligibility);

public interface IInteractionHandler
{
    RequestedInteractionType InteractionType { get; }

    Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken);
}

public interface IInteractionHandlerRegistry
{
    OperationResult<IInteractionHandler> Resolve(RequestedInteractionType interactionType);
}

public sealed class InteractionHandlerRegistry : IInteractionHandlerRegistry
{
    private readonly IReadOnlyDictionary<RequestedInteractionType, IInteractionHandler> _handlers;

    public InteractionHandlerRegistry(IEnumerable<IInteractionHandler> handlers)
    {
        _handlers = new ReadOnlyDictionary<RequestedInteractionType, IInteractionHandler>(
            handlers.ToDictionary(handler => handler.InteractionType));
    }

    public OperationResult<IInteractionHandler> Resolve(RequestedInteractionType interactionType) =>
        _handlers.TryGetValue(interactionType, out var handler)
            ? OperationResult<IInteractionHandler>.Success(handler)
            : OperationResult<IInteractionHandler>.Failure(
                "interaction.handler_missing",
                "No explicit interaction handler is registered.",
                interactionType.ToString());
}

public sealed class MerchantInteractionHandler : IInteractionHandler
{
    private readonly MerchantDefinitionCatalog _merchants;
    private readonly IInventoryTransactionCoordinator _inventory;
    private readonly IWorldInteractionEventSink _events;

    public MerchantInteractionHandler(
        MerchantDefinitionCatalog merchants,
        IInventoryTransactionCoordinator inventory,
        IWorldInteractionEventSink events)
    {
        _merchants = merchants;
        _inventory = inventory;
        _events = events;
    }

    public RequestedInteractionType InteractionType => RequestedInteractionType.Merchant;

    public async Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var target = context.Target;
        if (target.Target is not NpcObject npc ||
            npc.State.MerchantId is not int merchantId ||
            target.MerchantDefinition is null ||
            target.MerchantDefinition.MerchantTemplateId != merchantId ||
            target.MerchantDefinition.NpcTemplateId != npc.State.NpcTemplateId)
        {
            return WorldInteractionResult.Rejected(
                request,
                WorldInteractionResultCode.Rejected,
                "merchant.binding_invalid",
                "Merchant",
                target.Binding.MapSession.MapId,
                target.Binding.PlayerRuntimeVersion);
        }

        var merchant = _merchants.Resolve(merchantId);
        if (!merchant.Succeeded ||
            merchant.Value is null ||
            merchant.Value.NpcTemplateId != npc.State.NpcTemplateId)
        {
            return WorldInteractionResult.Rejected(
                request,
                WorldInteractionResultCode.Rejected,
                "merchant.definition_invalid",
                "Merchant",
                target.Binding.MapSession.MapId,
                target.Binding.PlayerRuntimeVersion);
        }

        _events.Publish(new WorldInteractionEvent(
            Guid.NewGuid(),
            WorldInteractionEventKind.MerchantServiceResolved,
            request.InteractionId,
            null,
            request.CharacterId,
            request.TargetRuntimeEntityId,
            target.Binding.MapSession.MapId,
            null,
            "Resolved",
            "",
            DateTimeOffset.UtcNow,
            request.CorrelationId));

        InventoryTransactionResult? inventoryResult = null;
        if (request.MerchantTransaction is not null)
        {
            var transaction = request.MerchantTransaction with
            {
                CharacterId = request.CharacterId,
                SessionId = request.SessionId,
                AccountId = target.Binding.Session.AccountId,
                MerchantTemplateId = merchantId,
                Source = "WorldInteraction:Merchant",
                AuthorityKind = InventoryMutationAuthorityKind.PlayerSession
            };
            inventoryResult = await _inventory.ExecuteAsync(transaction, cancellationToken);
            if (!inventoryResult.Succeeded)
            {
                return new WorldInteractionResult(
                    WorldInteractionResultCode.InventoryTransactionRejected,
                    request.InteractionId,
                    WorldInteractionHash.SafeId(request.IdempotencyKey),
                    "Merchant",
                    inventoryResult.FailureCode,
                    false,
                    null,
                    target.Binding.MapSession.MapId,
                    null,
                    target.Binding.PlayerRuntimeVersion,
                    target.Binding.PlayerRuntimeVersion,
                    inventoryResult,
                    []);
            }
        }

        return new WorldInteractionResult(
            WorldInteractionResultCode.Success,
            request.InteractionId,
            WorldInteractionHash.SafeId(request.IdempotencyKey),
            "Merchant",
            "",
            false,
            null,
            target.Binding.MapSession.MapId,
            null,
            target.Binding.PlayerRuntimeVersion,
            target.Binding.PlayerRuntimeVersion,
            inventoryResult,
            []);
    }
}

/// <summary>
/// Read-only Merchant service-open authority for the official Client interaction slice.
/// Inventory mutation remains in <see cref="MerchantInteractionHandler"/>; opening the
/// service only validates the MariaDB-backed NPC/merchant binding and emits no mutation.
/// </summary>
public sealed class MerchantServiceOpenInteractionHandler : IInteractionHandler
{
    private readonly IWorldInteractionEventSink _events;

    public MerchantServiceOpenInteractionHandler(IWorldInteractionEventSink events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public RequestedInteractionType InteractionType => RequestedInteractionType.Merchant;

    public Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var target = context.Target;
        if (request.MerchantTransaction is not null ||
            target.Target is not NpcObject npc ||
            npc.State.MerchantId is not int merchantId ||
            target.MerchantDefinition is null ||
            !target.MerchantDefinition.Enabled ||
            target.MerchantDefinition.MerchantTemplateId != merchantId ||
            target.MerchantDefinition.NpcTemplateId != npc.State.NpcTemplateId)
        {
            return Task.FromResult(WorldInteractionResult.Rejected(
                request,
                WorldInteractionResultCode.Rejected,
                "merchant.binding_invalid",
                "Merchant",
                target.Binding.MapSession.MapId,
                target.Binding.PlayerRuntimeVersion));
        }

        _events.Publish(new WorldInteractionEvent(
            Guid.NewGuid(),
            WorldInteractionEventKind.MerchantServiceResolved,
            request.InteractionId,
            null,
            request.CharacterId,
            request.TargetRuntimeEntityId,
            target.Binding.MapSession.MapId,
            null,
            "Opened",
            "",
            DateTimeOffset.UtcNow,
            request.CorrelationId));
        return Task.FromResult(new WorldInteractionResult(
            WorldInteractionResultCode.Success,
            request.InteractionId,
            WorldInteractionHash.SafeId(request.IdempotencyKey),
            "Merchant",
            "",
            false,
            null,
            target.Binding.MapSession.MapId,
            null,
            target.Binding.PlayerRuntimeVersion,
            target.Binding.PlayerRuntimeVersion,
            null,
            []));
    }
}

/// <summary>
/// Stateless dialog-open authority. It intentionally performs no persistence mutation;
/// the exact official response is projected only after the coordinator has validated the
/// session, target, map, instance, lifecycle, interaction family and distance.
/// </summary>
public sealed class DialogInteractionHandler : IInteractionHandler
{
    public RequestedInteractionType InteractionType => RequestedInteractionType.Dialog;

    public Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Target.Target is not NpcObject npc ||
            npc.State.WireIdentity is not { } wire ||
            wire.EntityHandle > ushort.MaxValue ||
            !OfficialNpcInteractionWireCodec.HasVerifiedDialogProfile((ushort)wire.EntityHandle))
        {
            return Task.FromResult(WorldInteractionResult.Rejected(
                context.Request,
                WorldInteractionResultCode.UnsupportedInteraction,
                "interaction.dialog_profile_evidence_blocked",
                "Dialog",
                context.Target.Binding.MapSession.MapId,
                context.Target.Binding.PlayerRuntimeVersion));
        }

        return Task.FromResult(new WorldInteractionResult(
            WorldInteractionResultCode.Success,
            context.Request.InteractionId,
            WorldInteractionHash.SafeId(context.Request.IdempotencyKey),
            "Dialog",
            "",
            false,
            null,
            context.Target.Binding.MapSession.MapId,
            null,
            context.Target.Binding.PlayerRuntimeVersion,
            context.Target.Binding.PlayerRuntimeVersion,
            null,
            []));
    }
}

public sealed class PortalInteractionHandler : IInteractionHandler
{
    private readonly IPortalTransitionCoordinator _transitions;

    public PortalInteractionHandler(IPortalTransitionCoordinator transitions)
    {
        _transitions = transitions;
    }

    public RequestedInteractionType InteractionType => RequestedInteractionType.Portal;

    public async Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken)
    {
        var transition = await _transitions.ExecuteAsync(context.Request, context.Target, cancellationToken);
        return new WorldInteractionResult(
            transition.Code,
            context.Request.InteractionId,
            WorldInteractionHash.SafeId(context.Request.IdempotencyKey),
            "Portal",
            transition.FailureCode,
            transition.Code == WorldInteractionResultCode.DuplicateCompleted,
            transition.Plan.TransitionId,
            transition.Plan.SourceMapId,
            transition.Plan.TargetMapId,
            transition.RuntimeVersionBefore,
            transition.RuntimeVersionAfter,
            null,
            []);
    }
}

public interface INpcInteractionRouter
{
    Task<WorldInteractionResult> RouteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken);
}

public sealed class NpcInteractionRouter : INpcInteractionRouter
{
    private readonly IInteractionHandlerRegistry _handlers;
    private readonly IWorldInteractionEventSink _events;

    public NpcInteractionRouter(
        IInteractionHandlerRegistry handlers,
        IWorldInteractionEventSink events)
    {
        _handlers = handlers;
        _events = events;
    }

    public async Task<WorldInteractionResult> RouteAsync(
        WorldInteractionHandlerContext context,
        CancellationToken cancellationToken)
    {
        var type = ResolveType(context);
        _events.Publish(new WorldInteractionEvent(
            Guid.NewGuid(),
            WorldInteractionEventKind.NpcInteractionResolved,
            context.Request.InteractionId,
            null,
            context.Request.CharacterId,
            context.Request.TargetRuntimeEntityId,
            context.Target.Binding.MapSession.MapId,
            null,
            type.ToString(),
            "",
            DateTimeOffset.UtcNow,
            context.Request.CorrelationId));
        var handler = _handlers.Resolve(type);
        return !handler.Succeeded || handler.Value is null
            ? WorldInteractionResult.Rejected(
                context.Request,
                WorldInteractionResultCode.UnsupportedInteraction,
                "interaction.handler_missing",
                type.ToString(),
                context.Target.Binding.MapSession.MapId,
                context.Target.Binding.PlayerRuntimeVersion)
            : await handler.Value.ExecuteAsync(context, cancellationToken);
    }

    private static RequestedInteractionType ResolveType(WorldInteractionHandlerContext context)
    {
        if (context.Target.Target is PortalObject)
        {
            return RequestedInteractionType.Portal;
        }

        if (context.Target.Target is NpcObject npc)
        {
            if (context.Request.RequestedInteractionType == RequestedInteractionType.Merchant ||
                context.Request.RequestedInteractionType == RequestedInteractionType.NpcPrimary && npc.State.MerchantId is not null)
            {
                return RequestedInteractionType.Merchant;
            }

            return context.Request.RequestedInteractionType;
        }

        return RequestedInteractionType.Unknown;
    }
}

public interface IWorldInteractionCoordinator
{
    Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionRequest request,
        CancellationToken cancellationToken);
}

public sealed class WorldInteractionCoordinator : IWorldInteractionCoordinator
{
    private readonly IInteractionTargetResolver _targets;
    private readonly IInteractionEligibilityPolicy _eligibility;
    private readonly INpcInteractionRouter _router;
    private readonly IInteractionIdempotencyStore _idempotency;
    private readonly IInteractionCooldownStore _cooldown;
    private readonly IWorldInteractionEventSink _events;
    private readonly IWorldInteractionAuditLedger _audit;
    private readonly WorldInteractionFailureInjection _failureInjection;
    private readonly IWorldBattleReservationRegistry? _battleReservations;

    public WorldInteractionCoordinator(
        IInteractionTargetResolver targets,
        IInteractionEligibilityPolicy eligibility,
        INpcInteractionRouter router,
        IInteractionIdempotencyStore idempotency,
        IInteractionCooldownStore cooldown,
        IWorldInteractionEventSink events,
        IWorldInteractionAuditLedger audit,
        WorldInteractionFailureInjection? failureInjection = null,
        IWorldBattleReservationRegistry? battleReservations = null)
    {
        _targets = targets;
        _eligibility = eligibility;
        _router = router;
        _idempotency = idempotency;
        _cooldown = cooldown;
        _events = events;
        _audit = audit;
        _failureInjection = failureInjection ?? new WorldInteractionFailureInjection();
        _battleReservations = battleReservations;
    }

    public async Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionRequest request,
        CancellationToken cancellationToken)
    {
        var payloadHash = WorldInteractionHash.Request(request);
        var replay = _idempotency.Find(request.IdempotencyKey, payloadHash);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return WorldInteractionResult.Rejected(request, WorldInteractionResultCode.ReplayConflict, "interaction.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with
            {
                Code = WorldInteractionResultCode.DuplicateCompleted,
                IsDuplicate = true
            };
        }

        if (!_cooldown.TryEnter(request.CharacterId))
        {
            return WorldInteractionResult.Rejected(request, WorldInteractionResultCode.CooldownActive, "interaction.in_flight");
        }

        Publish(WorldInteractionEventKind.InteractionRequested, request, 0, null, "Requested", "");
        try
        {
            if (_battleReservations?.IsReserved(request.CharacterId) == true)
            {
                return WorldInteractionResult.Rejected(
                    request,
                    WorldInteractionResultCode.Rejected,
                    "interaction.player_in_battle");
            }

            replay = _idempotency.Find(request.IdempotencyKey, payloadHash);
            if (replay is { Found: true, PayloadMatches: false })
            {
                return WorldInteractionResult.Rejected(request, WorldInteractionResultCode.ReplayConflict, "interaction.replay_conflict");
            }

            if (replay is { Found: true, Result: not null })
            {
                return replay.Result with
                {
                    Code = WorldInteractionResultCode.DuplicateCompleted,
                    IsDuplicate = true
                };
            }

            var resolution = _targets.Resolve(request);
            if (!resolution.Succeeded || resolution.Value is null)
            {
                var rejected = MapResolutionFailure(request, resolution.Error.Code);
                Publish(WorldInteractionEventKind.InteractionRejected, request, 0, null, "Rejected", rejected.FailureCode);
                return rejected;
            }

            if (_failureInjection.Point == WorldInteractionFailurePoint.TargetDestroyedDuringValidation)
            {
                return WorldInteractionResult.Rejected(
                    request,
                    WorldInteractionResultCode.TargetInactive,
                    "interaction.target_destroyed_during_validation",
                    sourceMapId: resolution.Value.Binding.MapSession.MapId,
                    runtimeVersion: resolution.Value.Binding.PlayerRuntimeVersion);
            }

            var eligibility = _eligibility.Evaluate(request, resolution.Value);
            if (!eligibility.Allowed)
            {
                var rejected = WorldInteractionResult.Rejected(
                    request,
                    eligibility.ResultCode,
                    eligibility.FailureCode,
                    sourceMapId: resolution.Value.Binding.MapSession.MapId,
                    runtimeVersion: resolution.Value.Binding.PlayerRuntimeVersion);
                Publish(WorldInteractionEventKind.InteractionRejected, request, rejected.SourceMapId, null, "Rejected", rejected.FailureCode);
                AppendAuditSafe(request, resolution.Value, rejected, eligibility.RangePolicyStatus);
                return rejected;
            }

            var result = await _router.RouteAsync(
                new WorldInteractionHandlerContext(request, resolution.Value, eligibility),
                cancellationToken);
            if (result.Succeeded)
            {
                _idempotency.Complete(request.IdempotencyKey, payloadHash, result);
            }

            Publish(
                result.Succeeded ? WorldInteractionEventKind.InteractionCompleted : WorldInteractionEventKind.InteractionRejected,
                request,
                result.SourceMapId,
                result.TargetMapId,
                result.Succeeded ? "Completed" : "Rejected",
                result.FailureCode);
            if (!string.Equals(result.Handler, "Portal", StringComparison.Ordinal))
            {
                AppendAuditSafe(request, resolution.Value, result, eligibility.RangePolicyStatus);
            }
            return result;
        }
        catch (OverflowException)
        {
            return WorldInteractionResult.Rejected(request, WorldInteractionResultCode.Rejected, "interaction.numeric_overflow");
        }
        finally
        {
            _cooldown.Exit(request.CharacterId);
        }
    }

    private void AppendAuditSafe(
        WorldInteractionRequest request,
        ResolvedInteractionTarget target,
        WorldInteractionResult result,
        InteractionRangePolicyStatus rangeStatus)
    {
        if (_failureInjection.Point == WorldInteractionFailurePoint.AuditFailure)
        {
            return;
        }

        _audit.Append(new WorldInteractionAuditRecord(
            Guid.NewGuid(),
            request.InteractionId,
            result.TransitionId,
            result.IdempotencySafeId,
            request.CorrelationId,
            request.SessionId,
            request.CharacterId,
            request.PlayerRuntimeEntityId,
            request.TargetRuntimeEntityId,
            target.Target.Identity.TemplateId,
            request.RequestedInteractionType,
            string.IsNullOrWhiteSpace(result.Handler) ? "None" : result.Handler,
            result.SourceMapId,
            result.TargetMapId,
            target.Player.State.Position,
            result.TargetMapId is null ? null : target.PortalDefinition?.Target.Position,
            result.RuntimeVersionBefore,
            result.RuntimeVersionAfter,
            result.Code,
            result.FailureCode,
            result.Code == WorldInteractionResultCode.RuntimeTransitionFailure ? "RecoveryRequired" : "NotRequired",
            result.TargetMapId is not null && result.Succeeded,
            result.TargetMapId is not null && result.Succeeded,
            result.TargetMapId is not null && result.Succeeded,
            request.CreatedAtUtc,
            DateTimeOffset.UtcNow));
    }

    private static WorldInteractionResult MapResolutionFailure(WorldInteractionRequest request, string code) =>
        code switch
        {
            "interaction.invalid_session" => WorldInteractionResult.Rejected(request, WorldInteractionResultCode.InvalidSession, code),
            "interaction.ownership_mismatch" or "interaction.player_binding_mismatch" =>
                WorldInteractionResult.Rejected(request, WorldInteractionResultCode.OwnershipMismatch, code),
            "interaction.target_not_found" or "interaction.player_not_found" =>
                WorldInteractionResult.Rejected(request, WorldInteractionResultCode.TargetNotFound, code),
            _ => WorldInteractionResult.Rejected(request, WorldInteractionResultCode.Rejected, code)
        };

    private void Publish(
        WorldInteractionEventKind kind,
        WorldInteractionRequest request,
        int sourceMapId,
        int? targetMapId,
        string state,
        string failureCode) =>
        _events.Publish(new WorldInteractionEvent(
            Guid.NewGuid(),
            kind,
            request.InteractionId,
            null,
            request.CharacterId,
            request.TargetRuntimeEntityId,
            sourceMapId,
            targetMapId,
            state,
            failureCode,
            DateTimeOffset.UtcNow,
            request.CorrelationId));
}

public sealed record WorldInteractionInspectorQuery(
    long? CharacterId = null,
    int? MapId = null,
    long? TargetRuntimeEntityId = null,
    RequestedInteractionType? InteractionType = null,
    string? State = null,
    WorldInteractionResultCode? Result = null,
    bool FailedOnly = false,
    bool PendingOnly = false,
    bool EvidenceBlockedOnly = false,
    int Skip = 0,
    int Take = 100);

public sealed record InteractionInspectorItem(
    Guid InteractionId,
    long CharacterId,
    long TargetRuntimeEntityId,
    RequestedInteractionType InteractionType,
    string Handler,
    string State,
    WorldInteractionResultCode Result,
    string FailureCode,
    InteractionRangePolicyStatus RangePolicyStatus,
    bool IsDuplicate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record PortalTransitionInspectorItem(
    Guid TransitionId,
    long CharacterId,
    int SourceMapId,
    int TargetMapId,
    WorldPosition3 SourcePosition,
    WorldPosition3 TargetPosition,
    PortalTransitionState State,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    bool SessionRebound,
    bool ReplicationCleared,
    bool ReplicationRebuilt,
    bool PersistenceCommitted,
    string RollbackStatus,
    string FailureCode);

public sealed record NpcServiceInspectorItem(
    long NpcRuntimeEntityId,
    int NpcTemplateId,
    string InteractionType,
    int? MerchantBindingId,
    int? PortalBindingId,
    IReadOnlyList<string> SupportedHandlers,
    bool Enabled,
    string ProtocolStatus);

public sealed record PortalInteractionInspectorItem(
    long PortalRuntimeEntityId,
    int PortalTemplateId,
    int SourceMapId,
    int TargetMapId,
    string ActivationType,
    bool Enabled,
    InteractionRangePolicyStatus RangePolicyStatus,
    string ProtocolStatus);

public sealed record WorldInteractionInspectorSnapshot(
    string SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<InteractionInspectorItem> Interactions,
    IReadOnlyList<PortalTransitionInspectorItem> PortalTransitions,
    IReadOnlyList<NpcServiceInspectorItem> NpcServices,
    IReadOnlyList<PortalInteractionInspectorItem> Portals);

public sealed class WorldInteractionInspector
{
    private readonly IWorldInteractionAuditLedger _audit;
    private readonly IPortalTransitionCoordinator _transitions;
    private readonly WorldRuntime _world;
    private readonly IInteractionRangePolicy _rangePolicy;

    public WorldInteractionInspector(
        IWorldInteractionAuditLedger audit,
        IPortalTransitionCoordinator transitions,
        WorldRuntime world,
        IInteractionRangePolicy rangePolicy)
    {
        _audit = audit;
        _transitions = transitions;
        _world = world;
        _rangePolicy = rangePolicy;
    }

    public WorldInteractionInspectorSnapshot Capture(
        WorldInteractionInspectorQuery query,
        DateTimeOffset now)
    {
        var audit = _audit.Records.AsEnumerable();
        if (query.CharacterId is not null)
        {
            audit = audit.Where(record => record.CharacterId == query.CharacterId.Value);
        }

        if (query.MapId is not null)
        {
            audit = audit.Where(record => record.SourceMapId == query.MapId.Value || record.TargetMapId == query.MapId.Value);
        }

        if (query.TargetRuntimeEntityId is not null)
        {
            audit = audit.Where(record => record.TargetRuntimeEntityId == query.TargetRuntimeEntityId.Value);
        }

        if (query.InteractionType is not null)
        {
            audit = audit.Where(record => record.InteractionType == query.InteractionType.Value);
        }

        if (query.Result is not null)
        {
            audit = audit.Where(record => record.Result == query.Result.Value);
        }

        if (query.FailedOnly)
        {
            audit = audit.Where(record => record.Result is not (WorldInteractionResultCode.Success or WorldInteractionResultCode.DuplicateCompleted));
        }

        if (query.PendingOnly)
        {
            audit = [];
        }

        if (query.EvidenceBlockedOnly)
        {
            audit = audit.Where(record => record.Result == WorldInteractionResultCode.RangeEvidenceBlocked);
        }

        var interactionItems = audit
            .OrderByDescending(record => record.CompletedAtUtc)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 500))
            .Select(record => new InteractionInspectorItem(
                record.InteractionId,
                record.CharacterId,
                record.TargetRuntimeEntityId,
                record.InteractionType,
                record.Handler,
                string.IsNullOrEmpty(record.FailureCode) ? "Completed" : "Rejected",
                record.Result,
                record.FailureCode,
                record.Result == WorldInteractionResultCode.RangeEvidenceBlocked
                    ? InteractionRangePolicyStatus.EvidenceBlocked
                    : _rangePolicy.Status,
                record.Result == WorldInteractionResultCode.DuplicateCompleted,
                record.CreatedAtUtc,
                record.CompletedAtUtc))
            .ToArray();

        var transitionItems = _transitions.Transitions
            .Where(result => query.CharacterId is null || result.Plan.CharacterId == query.CharacterId.Value)
            .Where(result => query.MapId is null || result.Plan.SourceMapId == query.MapId.Value || result.Plan.TargetMapId == query.MapId.Value)
            .Where(result => !query.FailedOnly || result.Code is not (WorldInteractionResultCode.Success or WorldInteractionResultCode.DuplicateCompleted))
            .Where(result => !query.PendingOnly || result.State is not (PortalTransitionState.Committed or PortalTransitionState.Rejected or PortalTransitionState.RolledBack or PortalTransitionState.Faulted))
            .Select(result => new PortalTransitionInspectorItem(
                result.Plan.TransitionId,
                result.Plan.CharacterId,
                result.Plan.SourceMapId,
                result.Plan.TargetMapId,
                result.Plan.SourcePosition,
                result.Plan.TargetPosition,
                result.State,
                result.RuntimeVersionBefore,
                result.RuntimeVersionAfter,
                result.SessionRebound,
                result.ReplicationCleared,
                result.ReplicationRebuilt,
                result.PersistenceCommitted,
                result.RollbackStatus,
                result.FailureCode))
            .ToArray();

        var npcs = _world.ActiveMaps.Values
            .SelectMany(map => map.Objects.OfType<NpcObject>())
            .Where(npc => query.MapId is null || npc.State.MapId == query.MapId.Value)
            .Select(npc => new NpcServiceInspectorItem(
                npc.Identity.RuntimeObjectId,
                npc.State.NpcTemplateId,
                npc.State.InteractionType,
                npc.State.MerchantId,
                null,
                npc.State.MerchantId is not null ? ["Merchant"] : ["Unsupported"],
                npc.State.Enabled,
                "BlockedByEvidence"))
            .ToArray();

        var portals = _world.ActiveMaps.Values
            .SelectMany(map => map.Objects.OfType<PortalObject>())
            .Where(portal => query.MapId is null || portal.State.Source.MapId == query.MapId.Value)
            .Select(portal => new PortalInteractionInspectorItem(
                portal.Identity.RuntimeObjectId,
                portal.Identity.TemplateId,
                portal.State.Source.MapId,
                portal.State.Target.MapId,
                portal.State.TriggerType,
                portal.State.IsActive,
                _rangePolicy.Status,
                "SerializerBlockedByEvidence"))
            .ToArray();

        return new WorldInteractionInspectorSnapshot(
            "world-interaction-inspector-v1",
            now,
            Array.AsReadOnly(interactionItems),
            Array.AsReadOnly(transitionItems),
            Array.AsReadOnly(npcs),
            Array.AsReadOnly(portals));
    }
}

internal static class WorldInteractionHash
{
    public static string Request(WorldInteractionRequest request)
    {
        var merchant = request.MerchantTransaction is null
            ? ""
            : string.Join(
                ":",
                request.MerchantTransaction.TransactionId,
                request.MerchantTransaction.OperationType,
                request.MerchantTransaction.ExpectedInventoryVersion,
                request.MerchantTransaction.MerchantTemplateId,
                string.Join(",", request.MerchantTransaction.RequestedMutations.Select(mutation =>
                    $"{mutation.SourceSlotIndex}/{mutation.TargetSlotIndex}/{mutation.InventoryItemId}/{mutation.ItemTemplateId}/{mutation.Quantity}")));
        return Hash(string.Join(
            "|",
            request.SessionId,
            request.CharacterId,
            request.PlayerRuntimeEntityId,
            request.TargetRuntimeEntityId,
            request.TargetTemplateId,
            request.RequestedInteractionType,
            request.ExpectedPlayerRuntimeVersion,
            merchant));
    }

    public static string PortalPlan(PortalTransitionPlan plan) =>
        Hash(string.Join(
            "|",
            plan.CharacterId,
            plan.SessionId,
            plan.PlayerRuntimeEntityId,
            plan.SourceWorldInstanceId,
            plan.SourceMapId,
            plan.TargetWorldInstanceId,
            plan.TargetMapId,
            plan.TargetPosition.X,
            plan.TargetPosition.Y,
            plan.TargetPosition.Z,
            plan.TargetDirection,
            plan.ExpectedPlayerVersion,
            plan.PortalTemplateId));

    public static string SafeId(string idempotencyKey) => Hash(idempotencyKey)[..16];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
