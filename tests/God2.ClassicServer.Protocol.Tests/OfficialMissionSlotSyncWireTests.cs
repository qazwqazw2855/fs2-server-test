using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialMissionSlotSyncWireTests
{
    [Fact]
    public void Current_minimal_layout_round_trips_state_and_slot()
    {
        var encoded = OfficialMissionSlotSyncWireCodec.EncodeCanonicalLayout(
            OfficialMissionSlotSyncWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            rawState: 4,
            slotIndex: 14);
        var decoded = OfficialMissionSlotSyncWireCodec.DecodeLayout(
            OfficialMissionSlotSyncWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            encoded.Value!);

        Assert.True(encoded.LayoutDecoded);
        Assert.Equal(new byte[] { 0xDF, 4, 14 }, encoded.Value);
        Assert.True(decoded.LayoutDecoded);
        Assert.Equal(4, decoded.Value!.RawState);
        Assert.Equal(14, decoded.Value.SlotIndex);
        Assert.False(decoded.Value.RuntimeMutationAllowed);
        Assert.Contains("ThreeByte", decoded.Value.EvidenceStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_beta_thirteen_byte_shape_is_rejected_for_current_build()
    {
        var beta = new byte[13];
        beta[0] = 0xDF;

        var result = OfficialMissionSlotSyncWireCodec.DecodeLayout(
            OfficialMissionSlotSyncWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            beta);

        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidLength, result.Code);
        Assert.Contains("length13", OfficialMissionSlotSyncWireCodec.RejectedPublicBetaLayout);
    }

    [Fact]
    public void State_slot_and_runtime_gates_fail_closed()
    {
        var battle = OfficialMissionSlotSyncWireCodec.EncodeCanonicalLayout(
            OfficialMissionSlotSyncWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            rawState: 1,
            slotIndex: 0);
        var overflow = OfficialMissionSlotSyncWireCodec.EncodeCanonicalLayout(
            OfficialMissionSlotSyncWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            rawState: 1,
            slotIndex: 15);

        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidState, battle.Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, overflow.Code);
        Assert.False(OfficialMissionSlotSyncWireCodec.RuntimeMutationEnabled);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
            OfficialMissionSlotSyncWireCodec.RejectRuntimeMutation<object>().Code);
    }
}
