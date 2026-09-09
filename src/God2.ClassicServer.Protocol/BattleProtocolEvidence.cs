using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum BattleEvidenceConfidence
{
    Missing,
    Candidate,
    ObservedOnce,
    ObservedRepeated,
    CrossValidated,
    DecoderVerified,
    SerializerCandidate,
    SerializerVerified,
    ProductionReady,
    EvidenceBlocked,
    SerializerBlockedByEvidence
}

public enum BattleProtocolGateStatus
{
    BlockedByEvidence,
    SerializerBlockedByEvidence,
    DecoderVerified,
    SerializerVerified,
    ProductionReady,
    RevalidationRequired
}

public enum BattleProtocolPacketFamily
{
    EncounterTrigger,
    BattleEnter,
    Formation,
    PlayerSpawn,
    PetSpawn,
    EnemySpawn,
    RoundStart,
    CommandWindow,
    BasicAttackClientCommand,
    SkillClientCommand,
    ItemClientCommand,
    DefendClientCommand,
    FleeClientCommand,
    ActionConfirmation,
    ActionStart,
    Damage,
    Healing,
    StatusApplyRemove,
    DeathRevive,
    RoundEnd,
    BattleEnd,
    Reward,
    WorldResume
}

public enum BattleProtocolAdapterResultCode
{
    Success,
    CandidateOnly,
    EvidenceBlocked,
    SerializerBlockedByEvidence,
    InvalidFrame,
    InvalidLength,
    InvalidOpcode,
    InvalidState,
    InvalidSession,
    BattleNotFound,
    ParticipantNotFound,
    OwnershipMismatch,
    StaleRound,
    UnsupportedPacket,
    UnsupportedClientBuild,
    DecoderFailure,
    SerializerFailure,
    InternalFailure
}

public enum BattleEvidenceType
{
    ExistingGolden,
    ServerAudit,
    ClientSocketCapture,
    ClientStaticReference,
    RuntimeTrace,
    DecoderFixture,
    SerializerAcceptance,
    DifferentialCorrelation,
    Unknown
}

public enum BattleUnknownFieldPolicy
{
    VerifiedConstant,
    VerifiedReservedZero,
    DerivedFromVerifiedSemanticField,
    EvidenceBlocked
}

public sealed record ClientBuildIdentity(
    string ClientBuildId,
    string God2OptSha256,
    string LauncherSha256,
    IReadOnlyDictionary<string, string> ModuleHashes,
    string FileVersionCandidate,
    string BuildTimestampCandidate,
    string Architecture,
    string Locale,
    IReadOnlyDictionary<string, string> RelevantConfigHashes,
    string ProtocolVariant,
    DateTimeOffset RecordedAtUtc)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientBuildId))
        {
            errors.Add("battle.client_build.id_missing");
        }

        if (!IsSha256(God2OptSha256) || !IsSha256(LauncherSha256))
        {
            errors.Add("battle.client_build.executable_hash_invalid");
        }

        if (ModuleHashes.Any(pair => !IsSafeRelativePath(pair.Key) || !IsSha256(pair.Value)) ||
            RelevantConfigHashes.Any(pair => !IsSafeRelativePath(pair.Key) || !IsSha256(pair.Value)))
        {
            errors.Add("battle.client_build.relative_hash_entry_invalid");
        }

        if (RecordedAtUtc == default)
        {
            errors.Add("battle.client_build.recorded_at_missing");
        }

        return errors.AsReadOnly();
    }

    public static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    public static bool IsSafeRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !Path.IsPathRooted(value) &&
        !value.Split('/', '\\').Any(segment => segment == "..");
}

public sealed record BattleEvidenceSource(
    string EvidenceId,
    BattleEvidenceType EvidenceType,
    string SourcePath,
    string SourceHash,
    string ClientBuildId,
    string? CaptureSessionId,
    string CaptureMethod,
    string? AutomationRunId,
    IReadOnlyList<string> StatePreconditions,
    string OperatorInteraction,
    BattleCaptureRedactionStatus RedactionStatus,
    DateTimeOffset CreatedAtUtc,
    string Notes)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(EvidenceId))
        {
            errors.Add("battle.evidence.id_missing");
        }

        if (!ClientBuildIdentity.IsSafeRelativePath(SourcePath))
        {
            errors.Add("battle.evidence.source_path_not_safe_relative");
        }

        if (!ClientBuildIdentity.IsSha256(SourceHash))
        {
            errors.Add("battle.evidence.source_hash_invalid");
        }

        if (string.IsNullOrWhiteSpace(ClientBuildId))
        {
            errors.Add("battle.evidence.client_build_missing");
        }

        if (CreatedAtUtc == default)
        {
            errors.Add("battle.evidence.created_at_missing");
        }

        return errors.AsReadOnly();
    }
}

public sealed record BattleCaptureManifest(
    string CaptureSessionId,
    string ClientBuildId,
    string CaptureMethod,
    bool CapturedBeforeEncryption,
    bool CapturedAfterEncryption,
    bool CapturedBeforeCompression,
    bool CapturedAfterCompression,
    bool FramingRemoved,
    string ArtifactPath,
    string ArtifactSha256,
    BattleCaptureRedactionStatus RedactionStatus,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long RecordCount,
    long DroppedRecordCount,
    string Notes)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CaptureSessionId) || string.IsNullOrWhiteSpace(ClientBuildId))
        {
            errors.Add("battle.capture.manifest_identity_missing");
        }

        if (!ClientBuildIdentity.IsSafeRelativePath(ArtifactPath))
        {
            errors.Add("battle.capture.manifest_path_not_safe_relative");
        }

        if (!ClientBuildIdentity.IsSha256(ArtifactSha256))
        {
            errors.Add("battle.capture.manifest_hash_invalid");
        }

        if (RecordCount < 0 || DroppedRecordCount < 0)
        {
            errors.Add("battle.capture.manifest_count_invalid");
        }

        return errors.AsReadOnly();
    }
}

public sealed record BattlePacketEvidence(
    BattleProtocolPacketFamily Family,
    PacketDirection Direction,
    string? CandidateOpcode,
    string Framing,
    string DecodeStatus,
    string SemanticMappingStatus,
    string SerializerStatus,
    BattleEvidenceConfidence Confidence,
    BattleProtocolGateStatus Gate,
    string ClientBuildId,
    int SampleCount,
    DateTimeOffset? FirstSeenUtc,
    DateTimeOffset? LastSeenUtc,
    IReadOnlyList<string> StatePreconditions,
    string PreviousPacketFamily,
    string NextPacketFamily,
    IReadOnlyList<string> VariableFields,
    IReadOnlyList<string> ConstantFields,
    IReadOnlyList<string> UnknownFields,
    int DecoderTestCount,
    int DecoderNegativeTestCount,
    int SerializerTestCount,
    int ClientAcceptanceCount,
    IReadOnlyList<string> EvidenceIds,
    bool HasUnknownRequiredDynamicField,
    string ProductionGateReason);

public sealed record BattleProtocolGateDecision(
    bool Allowed,
    BattleProtocolAdapterResultCode ResultCode,
    BattleProtocolPacketFamily Family,
    BattleProtocolGateStatus Gate,
    string FailureCode);

public interface IBattlePacketEvidenceRegistry
{
    BattlePacketEvidence Get(BattleProtocolPacketFamily family);

    bool TryGet(BattleProtocolPacketFamily family, out BattlePacketEvidence? evidence);

    IReadOnlyList<BattlePacketEvidence> Snapshot();
}

public interface IBattleProtocolGate
{
    BattleProtocolGateDecision CanDecode(BattleProtocolPacketFamily family, string clientBuildId);

    BattleProtocolGateDecision CanSerialize(BattleProtocolPacketFamily family, string clientBuildId);
}

public sealed class BattlePacketEvidenceRegistry : IBattlePacketEvidenceRegistry
{
    private readonly IReadOnlyDictionary<BattleProtocolPacketFamily, BattlePacketEvidence> _evidence;

    public BattlePacketEvidenceRegistry(IEnumerable<BattlePacketEvidence> evidence)
    {
        var rows = evidence.ToArray();
        if (rows.Select(row => row.Family).Distinct().Count() != rows.Length)
        {
            throw new ArgumentException("A packet family may only have one active evidence row.", nameof(evidence));
        }

        _evidence = new ReadOnlyDictionary<BattleProtocolPacketFamily, BattlePacketEvidence>(
            rows.ToDictionary(row => row.Family));
    }

    public BattlePacketEvidence Get(BattleProtocolPacketFamily family) =>
        _evidence.TryGetValue(family, out var evidence)
            ? evidence
            : throw new KeyNotFoundException($"Battle packet evidence is missing for {family}.");

    public bool TryGet(BattleProtocolPacketFamily family, out BattlePacketEvidence? evidence) =>
        _evidence.TryGetValue(family, out evidence);

    public IReadOnlyList<BattlePacketEvidence> Snapshot() =>
        _evidence.Values.OrderBy(row => row.Family).ToArray();

    public static BattlePacketEvidenceRegistry CreateInitial(string clientBuildId)
    {
        var inbound = new HashSet<BattleProtocolPacketFamily>
        {
            BattleProtocolPacketFamily.BasicAttackClientCommand,
            BattleProtocolPacketFamily.SkillClientCommand,
            BattleProtocolPacketFamily.ItemClientCommand,
            BattleProtocolPacketFamily.DefendClientCommand,
            BattleProtocolPacketFamily.FleeClientCommand
        };

        return new BattlePacketEvidenceRegistry(
            Enum.GetValues<BattleProtocolPacketFamily>().Select(family =>
            {
                var isInbound = inbound.Contains(family);
                return new BattlePacketEvidence(
                    family,
                    isInbound ? PacketDirection.ClientToServer : PacketDirection.ServerToClient,
                    CandidateOpcode: null,
                    Framing: "Only the existing uint16-le outer frame boundary is verified; battle framing is unverified.",
                    DecodeStatus: "Missing",
                    SemanticMappingStatus: family == BattleProtocolPacketFamily.BasicAttackClientCommand
                        ? "Adapter boundary present; no official raw command sample."
                        : "Missing",
                    SerializerStatus: isInbound ? "N/A" : "SerializerBlockedByEvidence",
                    Confidence: isInbound
                        ? BattleEvidenceConfidence.EvidenceBlocked
                        : BattleEvidenceConfidence.SerializerBlockedByEvidence,
                    Gate: isInbound
                        ? BattleProtocolGateStatus.BlockedByEvidence
                        : BattleProtocolGateStatus.SerializerBlockedByEvidence,
                    ClientBuildId: clientBuildId,
                    SampleCount: 0,
                    FirstSeenUtc: null,
                    LastSeenUtc: null,
                    StatePreconditions: [],
                    PreviousPacketFamily: "Unknown",
                    NextPacketFamily: "Unknown",
                    VariableFields: [],
                    ConstantFields: [],
                    UnknownFields: ["Opcode", "payload layout", "required dynamic fields", "state token semantics"],
                    DecoderTestCount: 0,
                    DecoderNegativeTestCount: 0,
                    SerializerTestCount: 0,
                    ClientAcceptanceCount: 0,
                    EvidenceIds: [],
                    HasUnknownRequiredDynamicField: true,
                    ProductionGateReason: "No build-bound repeated raw official-client battle frame evidence exists.");
            }));
    }
}

public sealed class EvidenceBackedBattleProtocolGate : IBattleProtocolGate
{
    private readonly IBattlePacketEvidenceRegistry _registry;

    public EvidenceBackedBattleProtocolGate(IBattlePacketEvidenceRegistry registry)
    {
        _registry = registry;
    }

    public BattleProtocolGateDecision CanDecode(BattleProtocolPacketFamily family, string clientBuildId)
    {
        if (!_registry.TryGet(family, out var evidence) || evidence is null)
        {
            return Blocked(family, BattleProtocolGateStatus.BlockedByEvidence, "battle.protocol.evidence_missing");
        }

        if (!string.Equals(evidence.ClientBuildId, clientBuildId, StringComparison.Ordinal))
        {
            return Blocked(family, BattleProtocolGateStatus.RevalidationRequired, "battle.protocol.client_build_mismatch");
        }

        return evidence.Gate is BattleProtocolGateStatus.DecoderVerified or BattleProtocolGateStatus.ProductionReady
            ? new BattleProtocolGateDecision(true, BattleProtocolAdapterResultCode.Success, family, evidence.Gate, string.Empty)
            : Blocked(family, evidence.Gate, "battle.protocol.decoder_blocked_by_evidence");
    }

    public BattleProtocolGateDecision CanSerialize(BattleProtocolPacketFamily family, string clientBuildId)
    {
        if (!_registry.TryGet(family, out var evidence) || evidence is null)
        {
            return Blocked(family, BattleProtocolGateStatus.SerializerBlockedByEvidence, "battle.protocol.evidence_missing");
        }

        if (!string.Equals(evidence.ClientBuildId, clientBuildId, StringComparison.Ordinal))
        {
            return Blocked(family, BattleProtocolGateStatus.RevalidationRequired, "battle.protocol.client_build_mismatch");
        }

        if (evidence.HasUnknownRequiredDynamicField)
        {
            return Blocked(family, BattleProtocolGateStatus.SerializerBlockedByEvidence, "battle.protocol.serializer_unknown_required_field");
        }

        return evidence.Gate is BattleProtocolGateStatus.SerializerVerified or BattleProtocolGateStatus.ProductionReady
            ? new BattleProtocolGateDecision(true, BattleProtocolAdapterResultCode.Success, family, evidence.Gate, string.Empty)
            : Blocked(family, evidence.Gate, "battle.protocol.serializer_blocked_by_evidence");
    }

    private static BattleProtocolGateDecision Blocked(
        BattleProtocolPacketFamily family,
        BattleProtocolGateStatus gate,
        string failureCode) =>
        new(
            false,
            gate == BattleProtocolGateStatus.SerializerBlockedByEvidence
                ? BattleProtocolAdapterResultCode.SerializerBlockedByEvidence
                : BattleProtocolAdapterResultCode.EvidenceBlocked,
            family,
            gate,
            failureCode);
}

public static class BattleEvidencePromotionPolicy
{
    public static IReadOnlyList<string> Validate(BattlePacketEvidence evidence)
    {
        var errors = new List<string>();
        if (evidence.Gate == BattleProtocolGateStatus.ProductionReady)
        {
            if (evidence.SampleCount < 3)
            {
                errors.Add("battle.evidence.production_ready_requires_three_samples");
            }

            if (string.IsNullOrWhiteSpace(evidence.ClientBuildId))
            {
                errors.Add("battle.evidence.production_ready_requires_client_build");
            }

            if (evidence.Direction == PacketDirection.ClientToServer &&
                (evidence.DecoderTestCount < 3 || evidence.DecoderNegativeTestCount < 10))
            {
                errors.Add("battle.evidence.production_ready_decoder_tests_insufficient");
            }

            if (evidence.Direction == PacketDirection.ServerToClient &&
                (evidence.SerializerTestCount < 3 || evidence.ClientAcceptanceCount < 3))
            {
                errors.Add("battle.evidence.production_ready_serializer_acceptance_insufficient");
            }

            if (evidence.HasUnknownRequiredDynamicField)
            {
                errors.Add("battle.evidence.production_ready_unknown_required_field");
            }
        }

        if (evidence.Confidence == BattleEvidenceConfidence.ObservedOnce &&
            evidence.Gate == BattleProtocolGateStatus.ProductionReady)
        {
            errors.Add("battle.evidence.observed_once_cannot_be_production_ready");
        }

        return errors.AsReadOnly();
    }
}
