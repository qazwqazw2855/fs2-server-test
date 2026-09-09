using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public sealed record OfficialMissionSlotSyncLayout(
    byte RawState,
    byte SlotIndex,
    string RecordSha256,
    string EvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Exact-current minimal layout consumed by the World-state S2C 0xDF handler.
/// The current consumer stores payload[0] into a 40-byte mission-slot row
/// selected by payload[1]. This is not the public-beta 13-byte subtype/u32/u32
/// record, which is explicitly rejected for the current build.
/// </summary>
public static class OfficialMissionSlotSyncWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte Opcode = 0xDF;
    public const int CanonicalApplicationRecordLength = 3;
    public const int EvidenceBoundSlotCount = 15;
    public const bool RuntimeMutationEnabled = false;
    public const string CurrentConsumerEvidence =
        "God2_opt-6B127086E0C0:WorldDispatchRva-0x0008EB8F+ConsumerRva-0x0012C840";
    public const string RejectedPublicBetaLayout =
        "FS2TW-research-evidence-20260815.zip:legacy_mission_protocol/0xDF-length13";

    public static OfficialPlayerSnapshotWireResult<OfficialMissionSlotSyncLayout> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> applicationRecord)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.BuildMismatch,
                "wire.mission.slot_sync.build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.InvalidState,
                "wire.mission.slot_sync.state_invalid");
        }

        if (applicationRecord.Length != CanonicalApplicationRecordLength)
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.InvalidLength,
                "wire.mission.slot_sync.length_invalid");
        }

        if (applicationRecord[0] != Opcode)
        {
            return Failure(OfficialPlayerSnapshotWireResultCode.InvalidOpcode,
                "wire.mission.slot_sync.opcode_invalid");
        }

        return OfficialPlayerSnapshotWireResult<OfficialMissionSlotSyncLayout>.Success(
            new OfficialMissionSlotSyncLayout(
                applicationRecord[1],
                applicationRecord[2],
                Convert.ToHexString(SHA256.HashData(applicationRecord)),
                "ExactCurrentThreeByteConsumer_PublicBetaLengthRejected_RuntimeRegistrationPendingLiveTest",
                RuntimeMutationAllowed: false));
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeCanonicalLayout(
        string clientBuildId,
        GameplayProtocolState state,
        byte rawState,
        byte slotIndex)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.BuildMismatch,
                "wire.mission.slot_sync.build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.InvalidState,
                "wire.mission.slot_sync.state_invalid");
        }

        if (slotIndex >= EvidenceBoundSlotCount)
        {
            return EncodeFailure(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
                "wire.mission.slot_sync.slot_out_of_range");
        }

        return OfficialPlayerSnapshotWireResult<byte[]>.Success([Opcode, rawState, slotIndex]);
    }

    public static OfficialPlayerSnapshotWireResult<T> RejectRuntimeMutation<T>() =>
        OfficialPlayerSnapshotWireResult<T>.Failure(
            OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
            "wire.mission.slot_sync.runtime_registration_requires_live_test");

    private static OfficialPlayerSnapshotWireResult<OfficialMissionSlotSyncLayout> Failure(
        OfficialPlayerSnapshotWireResultCode code,
        string failureCode) =>
        OfficialPlayerSnapshotWireResult<OfficialMissionSlotSyncLayout>.Failure(code, failureCode);

    private static OfficialPlayerSnapshotWireResult<byte[]> EncodeFailure(
        OfficialPlayerSnapshotWireResultCode code,
        string failureCode) =>
        OfficialPlayerSnapshotWireResult<byte[]>.Failure(code, failureCode);
}
