using System.Collections.Concurrent;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleProtocolState
{
    WorldActive,
    EncounterPending,
    BattleEntering,
    BattleInitializing,
    FormationLoading,
    ParticipantsLoading,
    WaitingForRoundStart,
    CommandWindowOpen,
    CommandSubmitted,
    AwaitingActionResult,
    ActionPlayback,
    RoundClosing,
    BattleEnding,
    RewardPresentation,
    WorldResuming,
    Completed,
    RecoveryRequired,
    EvidenceBlocked,
    Faulted
}

public enum BattleProtocolStateResultCode
{
    Success,
    Duplicate,
    ConnectionNotFound,
    VersionConflict,
    InvalidTransition,
    InvalidSession,
    StaleSequence,
    UnexpectedPacket,
    EvidenceBlocked,
    Completed,
    InternalFailure
}

public sealed record BattleProtocolStateSnapshot(
    string ConnectionSafeReference,
    long SessionEpoch,
    long CharacterId,
    Guid? BattleId,
    OfficialBattleProtocolState ProtocolState,
    long ProtocolStateVersion,
    long LastInboundSequence,
    long LastOutboundSequence,
    IReadOnlyList<BattleProtocolPacketFamily> ExpectedInboundFamilies,
    IReadOnlyList<BattleProtocolPacketFamily> AllowedOutboundFamilies,
    string ClientBuildId,
    DateTimeOffset UpdatedAtUtc);

public sealed record BattleProtocolStateResult(
    BattleProtocolStateResultCode Code,
    BattleProtocolStateSnapshot? Snapshot,
    string FailureCode)
{
    public bool Succeeded => Code is BattleProtocolStateResultCode.Success or BattleProtocolStateResultCode.Duplicate;
}

public sealed record BattleProtocolStateAuditEntry(
    string ConnectionSafeReference,
    long SessionEpoch,
    OfficialBattleProtocolState State,
    BattleProtocolPacketFamily? Family,
    long Sequence,
    BattleProtocolStateResultCode Result,
    string FailureCode,
    DateTimeOffset CreatedAtUtc);

public interface IBattleProtocolStateAudit
{
    void Write(BattleProtocolStateAuditEntry entry);

    IReadOnlyList<BattleProtocolStateAuditEntry> Snapshot();
}

public sealed class InMemoryBattleProtocolStateAudit : IBattleProtocolStateAudit
{
    private readonly BoundedBattleProtocolStateAudit _entries;

    public InMemoryBattleProtocolStateAudit(int capacity = 4096)
    {
        _entries = new BoundedBattleProtocolStateAudit(capacity);
    }

    public void Write(BattleProtocolStateAuditEntry entry) => _entries.Write(entry);

    public IReadOnlyList<BattleProtocolStateAuditEntry> Snapshot() => _entries.Snapshot();

    private sealed class BoundedBattleProtocolStateAudit
    {
        private readonly ConcurrentQueue<BattleProtocolStateAuditEntry> _entries = [];
        private readonly int _capacity;
        private readonly object _sync = new();

        public BoundedBattleProtocolStateAudit(int capacity)
        {
            _capacity = Math.Clamp(capacity, 1, 1_000_000);
        }

        public void Write(BattleProtocolStateAuditEntry entry)
        {
            lock (_sync)
            {
                _entries.Enqueue(entry);
                while (_entries.Count > _capacity)
                {
                    _entries.TryDequeue(out _);
                }
            }
        }

        public IReadOnlyList<BattleProtocolStateAuditEntry> Snapshot() => _entries.ToArray();
    }
}

public interface IBattleProtocolStateTracker
{
    BattleProtocolStateResult Register(BattleProtocolStateSnapshot initial);

    BattleProtocolStateResult Get(string connectionSafeReference);

    BattleProtocolStateResult Transition(
        string connectionSafeReference,
        long sessionEpoch,
        long expectedVersion,
        OfficialBattleProtocolState nextState,
        Guid? battleId,
        DateTimeOffset updatedAtUtc);

    BattleProtocolStateResult ObserveInbound(
        string connectionSafeReference,
        long sessionEpoch,
        long sequence,
        BattleProtocolPacketFamily family,
        DateTimeOffset observedAtUtc);

    BattleProtocolStateResult ObserveOutbound(
        string connectionSafeReference,
        long sessionEpoch,
        long sequence,
        BattleProtocolPacketFamily family,
        DateTimeOffset observedAtUtc);

    bool Remove(string connectionSafeReference);
}

public sealed class BattleProtocolStateTracker : IBattleProtocolStateTracker
{
    private sealed class TrackedState
    {
        public TrackedState(BattleProtocolStateSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public object Sync { get; } = new();

        public BattleProtocolStateSnapshot Snapshot { get; set; }
    }

    private static readonly IReadOnlyDictionary<OfficialBattleProtocolState, IReadOnlySet<OfficialBattleProtocolState>> AllowedTransitions =
        new Dictionary<OfficialBattleProtocolState, IReadOnlySet<OfficialBattleProtocolState>>
        {
            [OfficialBattleProtocolState.WorldActive] = Set(OfficialBattleProtocolState.EncounterPending),
            [OfficialBattleProtocolState.EncounterPending] = Set(OfficialBattleProtocolState.BattleEntering, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.BattleEntering] = Set(OfficialBattleProtocolState.BattleInitializing, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.BattleInitializing] = Set(OfficialBattleProtocolState.FormationLoading, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.FormationLoading] = Set(OfficialBattleProtocolState.ParticipantsLoading, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.ParticipantsLoading] = Set(OfficialBattleProtocolState.WaitingForRoundStart, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.WaitingForRoundStart] = Set(OfficialBattleProtocolState.CommandWindowOpen, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.CommandWindowOpen] = Set(OfficialBattleProtocolState.CommandSubmitted, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.CommandSubmitted] = Set(OfficialBattleProtocolState.AwaitingActionResult, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.AwaitingActionResult] = Set(OfficialBattleProtocolState.ActionPlayback, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.ActionPlayback] = Set(OfficialBattleProtocolState.RoundClosing, OfficialBattleProtocolState.BattleEnding, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.RoundClosing] = Set(OfficialBattleProtocolState.WaitingForRoundStart, OfficialBattleProtocolState.BattleEnding, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.BattleEnding] = Set(OfficialBattleProtocolState.RewardPresentation, OfficialBattleProtocolState.WorldResuming, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.RewardPresentation] = Set(OfficialBattleProtocolState.WorldResuming, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.WorldResuming] = Set(OfficialBattleProtocolState.Completed, OfficialBattleProtocolState.EvidenceBlocked),
            [OfficialBattleProtocolState.Completed] = Set(),
            [OfficialBattleProtocolState.RecoveryRequired] = Set(OfficialBattleProtocolState.EvidenceBlocked, OfficialBattleProtocolState.Faulted),
            [OfficialBattleProtocolState.EvidenceBlocked] = Set(OfficialBattleProtocolState.RecoveryRequired, OfficialBattleProtocolState.Faulted),
            [OfficialBattleProtocolState.Faulted] = Set()
        };

    private readonly ConcurrentDictionary<string, TrackedState> _states = new(StringComparer.Ordinal);
    private readonly IBattleProtocolStateAudit _audit;

    public BattleProtocolStateTracker(IBattleProtocolStateAudit? audit = null)
    {
        _audit = audit ?? new InMemoryBattleProtocolStateAudit();
    }

    public BattleProtocolStateResult Register(BattleProtocolStateSnapshot initial)
    {
        if (string.IsNullOrWhiteSpace(initial.ConnectionSafeReference) ||
            string.IsNullOrWhiteSpace(initial.ClientBuildId) ||
            initial.SessionEpoch < 0 ||
            initial.ProtocolStateVersion < 0 ||
            initial.LastInboundSequence < 0 ||
            initial.LastOutboundSequence < 0)
        {
            return Result(BattleProtocolStateResultCode.InternalFailure, null, "battle.protocol.state_initial_invalid");
        }

        var normalized = WithFamilies(initial);
        var tracked = new TrackedState(normalized);
        if (_states.TryAdd(initial.ConnectionSafeReference, tracked))
        {
            return Result(BattleProtocolStateResultCode.Success, normalized, string.Empty);
        }

        var existing = _states[initial.ConnectionSafeReference].Snapshot;
        return existing == normalized
            ? Result(BattleProtocolStateResultCode.Duplicate, existing, string.Empty)
            : Result(BattleProtocolStateResultCode.VersionConflict, existing, "battle.protocol.state_registration_conflict");
    }

    public BattleProtocolStateResult Get(string connectionSafeReference) =>
        _states.TryGetValue(connectionSafeReference, out var tracked)
            ? Result(BattleProtocolStateResultCode.Success, tracked.Snapshot, string.Empty)
            : Result(BattleProtocolStateResultCode.ConnectionNotFound, null, "battle.protocol.state_connection_not_found");

    public BattleProtocolStateResult Transition(
        string connectionSafeReference,
        long sessionEpoch,
        long expectedVersion,
        OfficialBattleProtocolState nextState,
        Guid? battleId,
        DateTimeOffset updatedAtUtc)
    {
        if (!_states.TryGetValue(connectionSafeReference, out var tracked))
        {
            return Result(BattleProtocolStateResultCode.ConnectionNotFound, null, "battle.protocol.state_connection_not_found");
        }

        lock (tracked.Sync)
        {
            var current = tracked.Snapshot;
            if (current.SessionEpoch != sessionEpoch)
            {
                return AuditAndReturn(current, null, 0, BattleProtocolStateResultCode.InvalidSession, "battle.protocol.state_session_epoch_mismatch");
            }

            if (current.ProtocolStateVersion != expectedVersion)
            {
                return AuditAndReturn(current, null, 0, BattleProtocolStateResultCode.VersionConflict, "battle.protocol.state_version_conflict");
            }

            if (current.ProtocolState == nextState)
            {
                return Result(BattleProtocolStateResultCode.Duplicate, current, string.Empty);
            }

            if (current.ProtocolState == OfficialBattleProtocolState.Completed)
            {
                return AuditAndReturn(current, null, 0, BattleProtocolStateResultCode.Completed, "battle.protocol.state_already_completed");
            }

            if (!AllowedTransitions[current.ProtocolState].Contains(nextState))
            {
                return AuditAndReturn(current, null, 0, BattleProtocolStateResultCode.InvalidTransition, "battle.protocol.state_transition_invalid");
            }

            var updated = WithFamilies(current with
            {
                BattleId = battleId ?? current.BattleId,
                ProtocolState = nextState,
                ProtocolStateVersion = checked(current.ProtocolStateVersion + 1),
                UpdatedAtUtc = updatedAtUtc
            });
            tracked.Snapshot = updated;
            return AuditAndReturn(updated, null, 0, BattleProtocolStateResultCode.Success, string.Empty);
        }
    }

    public BattleProtocolStateResult ObserveInbound(
        string connectionSafeReference,
        long sessionEpoch,
        long sequence,
        BattleProtocolPacketFamily family,
        DateTimeOffset observedAtUtc) =>
        Observe(connectionSafeReference, sessionEpoch, sequence, family, observedAtUtc, inbound: true);

    public BattleProtocolStateResult ObserveOutbound(
        string connectionSafeReference,
        long sessionEpoch,
        long sequence,
        BattleProtocolPacketFamily family,
        DateTimeOffset observedAtUtc) =>
        Observe(connectionSafeReference, sessionEpoch, sequence, family, observedAtUtc, inbound: false);

    public bool Remove(string connectionSafeReference) => _states.TryRemove(connectionSafeReference, out _);

    private BattleProtocolStateResult Observe(
        string connectionSafeReference,
        long sessionEpoch,
        long sequence,
        BattleProtocolPacketFamily family,
        DateTimeOffset observedAtUtc,
        bool inbound)
    {
        if (!_states.TryGetValue(connectionSafeReference, out var tracked))
        {
            return Result(BattleProtocolStateResultCode.ConnectionNotFound, null, "battle.protocol.state_connection_not_found");
        }

        lock (tracked.Sync)
        {
            var current = tracked.Snapshot;
            if (current.SessionEpoch != sessionEpoch)
            {
                return AuditAndReturn(current, family, sequence, BattleProtocolStateResultCode.InvalidSession, "battle.protocol.state_session_epoch_mismatch");
            }

            var lastSequence = inbound ? current.LastInboundSequence : current.LastOutboundSequence;
            if (sequence <= lastSequence)
            {
                return AuditAndReturn(current, family, sequence, BattleProtocolStateResultCode.StaleSequence, "battle.protocol.state_sequence_stale");
            }

            var allowed = inbound ? current.ExpectedInboundFamilies : current.AllowedOutboundFamilies;
            if (!allowed.Contains(family))
            {
                return AuditAndReturn(current, family, sequence, BattleProtocolStateResultCode.UnexpectedPacket, "battle.protocol.state_packet_unexpected");
            }

            var updated = current with
            {
                LastInboundSequence = inbound ? sequence : current.LastInboundSequence,
                LastOutboundSequence = inbound ? current.LastOutboundSequence : sequence,
                ProtocolStateVersion = checked(current.ProtocolStateVersion + 1),
                UpdatedAtUtc = observedAtUtc
            };
            tracked.Snapshot = updated;
            return AuditAndReturn(updated, family, sequence, BattleProtocolStateResultCode.Success, string.Empty);
        }
    }

    private BattleProtocolStateResult AuditAndReturn(
        BattleProtocolStateSnapshot snapshot,
        BattleProtocolPacketFamily? family,
        long sequence,
        BattleProtocolStateResultCode code,
        string failureCode)
    {
        _audit.Write(new BattleProtocolStateAuditEntry(
            snapshot.ConnectionSafeReference,
            snapshot.SessionEpoch,
            snapshot.ProtocolState,
            family,
            sequence,
            code,
            failureCode,
            DateTimeOffset.UtcNow));
        return Result(code, snapshot, failureCode);
    }

    private static BattleProtocolStateSnapshot WithFamilies(BattleProtocolStateSnapshot snapshot)
    {
        var inbound = snapshot.ProtocolState == OfficialBattleProtocolState.CommandWindowOpen
            ? new[] { BattleProtocolPacketFamily.BasicAttackClientCommand }
            : Array.Empty<BattleProtocolPacketFamily>();
        var outbound = snapshot.ProtocolState switch
        {
            OfficialBattleProtocolState.EncounterPending => [BattleProtocolPacketFamily.BattleEnter],
            OfficialBattleProtocolState.BattleEntering or OfficialBattleProtocolState.BattleInitializing =>
                [BattleProtocolPacketFamily.Formation],
            OfficialBattleProtocolState.FormationLoading =>
                [BattleProtocolPacketFamily.PlayerSpawn, BattleProtocolPacketFamily.EnemySpawn],
            OfficialBattleProtocolState.ParticipantsLoading or OfficialBattleProtocolState.WaitingForRoundStart =>
                [BattleProtocolPacketFamily.RoundStart, BattleProtocolPacketFamily.CommandWindow],
            OfficialBattleProtocolState.CommandSubmitted or OfficialBattleProtocolState.AwaitingActionResult =>
                [BattleProtocolPacketFamily.ActionConfirmation, BattleProtocolPacketFamily.ActionStart, BattleProtocolPacketFamily.Damage],
            OfficialBattleProtocolState.ActionPlayback or OfficialBattleProtocolState.RoundClosing =>
                [BattleProtocolPacketFamily.RoundEnd],
            OfficialBattleProtocolState.BattleEnding =>
                [BattleProtocolPacketFamily.BattleEnd, BattleProtocolPacketFamily.Reward],
            OfficialBattleProtocolState.RewardPresentation or OfficialBattleProtocolState.WorldResuming =>
                [BattleProtocolPacketFamily.WorldResume],
            _ => Array.Empty<BattleProtocolPacketFamily>()
        };

        return snapshot with
        {
            ExpectedInboundFamilies = inbound,
            AllowedOutboundFamilies = outbound
        };
    }

    private static IReadOnlySet<OfficialBattleProtocolState> Set(params OfficialBattleProtocolState[] states) =>
        new HashSet<OfficialBattleProtocolState>(states);

    private static BattleProtocolStateResult Result(
        BattleProtocolStateResultCode code,
        BattleProtocolStateSnapshot? snapshot,
        string failureCode) =>
        new(code, snapshot, failureCode);
}
