using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum BattleCrossVersionPromotionStatus
{
    CurrentBuildVerified,
    CurrentBuildRecovered,
    HypothesisOnly,
    RejectedAsChanged
}

public sealed record BattleCrossVersionEvidenceRow(
    byte Opcode,
    PacketDirection Direction,
    int PublicBetaRecordLength,
    int? CurrentBuildRecordLength,
    BattleCrossVersionPromotionStatus Status,
    IReadOnlyList<string> PromotedFields,
    IReadOnlyList<string> BlockedFields,
    string CurrentBuildEvidence,
    string PublicBetaEvidence);

/// <summary>
/// Explicit promotion ledger. Public-beta facts enter the current server only
/// where a current-build handler, producer or controlled live trace independently
/// confirms the same boundary. Equal opcode or length alone never promotes a field.
/// </summary>
public static class OfficialBattleCrossVersionEvidence
{
    private static readonly ReadOnlyCollection<BattleCrossVersionEvidenceRow> RowsValue = Array.AsReadOnly(
    [
        Row(0x1C, PacketDirection.ServerToClient, 29, 45, BattleCrossVersionPromotionStatus.CurrentBuildRecovered,
            ["battlePosition", "displayLevel", "entityId", "actorKindFlags", "encounterLocalId"],
            ["remaining36ByteIdentityPayloadSemantics"],
            "three-client HandlerDecoded records plus actor snapshots", "consumer_004E1580"),
        Row(0x35, PacketDirection.ClientToServer, 17, 17, BattleCrossVersionPromotionStatus.RejectedAsChanged,
            ["opcode", "actorSelector", "actionCode", "continuationBit"],
            ["targetMasksBeyondStablePrefix", "reserved", "clientActionToken", "rawOperand"],
            "current producer rva-0x0014E7D0..0x0014EB3C and live actions", "producer_004E3C70"),
        Row(0x38, PacketDirection.ServerToClient, 2, 2, BattleCrossVersionPromotionStatus.CurrentBuildRecovered,
            ["recordBoundary", "payloadNotReadByConsumer"], [],
            "current live HandlerDecoded 0x38/2", "dispatcher_004DFA30"),
        Row(0x83, PacketDirection.ServerToClient, 15, 15, BattleCrossVersionPromotionStatus.RejectedAsChanged,
            ["opcode", "actionCode", "sourcePosition", "currentBuildLayoutIndependentlyVerified"],
            ["publicBetaTargetMaskOffsets", "serverFormula", "productionOrdering"],
            OfficialBattleEffectWireCodec.FieldLayoutEvidence, "dispatcher_004DFCB6 and consumer projections"),
        Row(0x84, PacketDirection.ServerToClient, 15, null, BattleCrossVersionPromotionStatus.HypothesisOnly,
            [], ["entireCurrentBuildRecord"], "not observed in current controlled traces", "dispatcher_004DFA30"),
        Row(0x85, PacketDirection.ServerToClient, 2, 2, BattleCrossVersionPromotionStatus.CurrentBuildRecovered,
            ["recordBoundary", "payloadNotReadByConsumer"], [],
            "repeated current live HandlerDecoded records", "dispatcher_004DFD63_payload_unread"),
        Row(0x86, PacketDirection.ServerToClient, 13, 17, BattleCrossVersionPromotionStatus.RejectedAsChanged,
            ["opcode", "subtype", "battlePosition", "current17ByteBoundary"],
            ["crossVersionValueSemantics"],
            "current live HandlerDecoded 17-byte records", "LegacyCombatSession_accept 13-byte record"),
        Row(0x87, PacketDirection.ServerToClient, 3, 3, BattleCrossVersionPromotionStatus.CurrentBuildRecovered,
            ["rawWord", "lowSelector", "highSixBitSelector", "modeBit14", "stateBit15"],
            ["userFacingMeaning"],
            "current live HandlerDecoded 0x87/3 and current handler identity", "fightgod consumer_004E7260"),
        Row(0x88, PacketDirection.ServerToClient, 121, 121, BattleCrossVersionPromotionStatus.CurrentBuildVerified,
            ["roundIndex", "fourteenHpMpPairs", "rawTrailer"],
            ["trailerMeaning", "productionOrdering"],
            OfficialBattleVitalSnapshotWireCodec.FieldLayoutEvidence, "consumer_004F7A30"),
        Row(0x89, PacketDirection.ServerToClient, 53, 57, BattleCrossVersionPromotionStatus.RejectedAsChanged,
            ["terminalBattleRole", "current57ByteBoundary"],
            ["publicBetaSettlementOffsets", "currentPresentationKind", "currentRewardEntries"],
            "current live HandlerDecoded 0x89/57 after controlled flee", "LegacyCombatSession_accept 53-byte settlement"),
        Row(0x2C, PacketDirection.ServerToClient, 31, null, BattleCrossVersionPromotionStatus.HypothesisOnly,
            [], ["currentBuildPrivateStatOpcodeAndLayout"],
            "no current-build network record carries a confirmed local-player derived-stats panel vector", "consumer_004B4620"),
        Row(0x69, PacketDirection.ClientToServer, 45, null, BattleCrossVersionPromotionStatus.RejectedAsChanged,
            [], ["currentBuildPkRequest"],
            "current controlled assistance uses 0x6A/12", "producer_0047AC50")
    ]);

    public static IReadOnlyList<BattleCrossVersionEvidenceRow> Rows => RowsValue;

    public static BattleCrossVersionEvidenceRow? Find(byte opcode, PacketDirection direction) =>
        RowsValue.SingleOrDefault(row => row.Opcode == opcode && row.Direction == direction);

    private static BattleCrossVersionEvidenceRow Row(
        byte opcode,
        PacketDirection direction,
        int betaLength,
        int? currentLength,
        BattleCrossVersionPromotionStatus status,
        IReadOnlyList<string> promoted,
        IReadOnlyList<string> blocked,
        string currentEvidence,
        string betaEvidence) =>
        new(opcode, direction, betaLength, currentLength, status, promoted, blocked, currentEvidence, betaEvidence);
}
