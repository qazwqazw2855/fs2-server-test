using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialLiveMovementEvidenceRecordKind
{
    ClientParserOnlyFrame,
    ServerWorldApplicationChild,
    ServerWorldApplicationBatch,
    ParserFailure
}

public enum OfficialLiveMovementSemanticPromotionStatus
{
    ParserOnlyRawPreservation,
    TimingAdjacentPairingOnly,
    EvidenceBlocked
}

public sealed record OfficialLiveMovementEvidenceRecord(
    OfficialLiveMovementEvidenceRecordKind Kind,
    PacketDirection Direction,
    byte Opcode,
    int Length,
    string RawHex,
    string RawSha256,
    string RunId,
    string MarkerName,
    long ObservedAtUnixMs,
    string SourceFrameId,
    OfficialLiveMovementSemanticPromotionStatus PromotionStatus,
    string BlockedSemantics,
    int? ChildOffset = null,
    int? ChildLength = null,
    byte? TopLevelOpcode = null,
    string? PairingKey = null);

public static class OfficialLiveMovementEvidenceRecordFactory
{
    public const string DefaultBlockedSemantics =
        "CoordinateOwnership;PathReconciliation;ServerAcceptance;AckTickOwnership;" +
        "AntiCheatInterpretation;GameplayMutation;SerializerOutput";

    public static OfficialLiveMovementEvidenceRecord CreateClientFrame(
        byte opcode,
        ReadOnlySpan<byte> decodedFrame,
        string runId,
        string markerName,
        long observedAtUnixMs,
        string sourceFrameId,
        string? pairingKey = null) =>
        new(
            OfficialLiveMovementEvidenceRecordKind.ClientParserOnlyFrame,
            PacketDirection.ClientToServer,
            opcode,
            decodedFrame.Length,
            Convert.ToHexString(decodedFrame),
            Convert.ToHexString(SHA256.HashData(decodedFrame)),
            runId,
            markerName,
            observedAtUnixMs,
            sourceFrameId,
            pairingKey is null
                ? OfficialLiveMovementSemanticPromotionStatus.ParserOnlyRawPreservation
                : OfficialLiveMovementSemanticPromotionStatus.TimingAdjacentPairingOnly,
            DefaultBlockedSemantics,
            PairingKey: pairingKey);

    public static OfficialLiveMovementEvidenceRecord CreateServerChild(
        byte topLevelOpcode,
        byte childOpcode,
        int childOffset,
        ReadOnlySpan<byte> child,
        ReadOnlySpan<byte> decodedFrame,
        string runId,
        string markerName,
        long observedAtUnixMs,
        string sourceFrameId) =>
        new(
            OfficialLiveMovementEvidenceRecordKind.ServerWorldApplicationChild,
            PacketDirection.ServerToClient,
            childOpcode,
            child.Length,
            Convert.ToHexString(child),
            Convert.ToHexString(SHA256.HashData(decodedFrame)),
            runId,
            markerName,
            observedAtUnixMs,
            sourceFrameId,
            OfficialLiveMovementSemanticPromotionStatus.ParserOnlyRawPreservation,
            DefaultBlockedSemantics,
            ChildOffset: childOffset,
            ChildLength: child.Length,
            TopLevelOpcode: topLevelOpcode);

    public static OfficialLiveMovementEvidenceRecord CreateParserFailure(
        PacketDirection direction,
        byte opcode,
        ReadOnlySpan<byte> decodedFrame,
        string runId,
        string markerName,
        long observedAtUnixMs,
        string sourceFrameId,
        string blockedSemantics) =>
        new(
            OfficialLiveMovementEvidenceRecordKind.ParserFailure,
            direction,
            opcode,
            decodedFrame.Length,
            Convert.ToHexString(decodedFrame),
            Convert.ToHexString(SHA256.HashData(decodedFrame)),
            runId,
            markerName,
            observedAtUnixMs,
            sourceFrameId,
            OfficialLiveMovementSemanticPromotionStatus.EvidenceBlocked,
            blockedSemantics);
}
