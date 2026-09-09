using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialBattleAssistanceJoinWireTests
{
    private const string CapturedFrame = "0C006AFE000000F2000000FA";

    [Fact]
    public void ControlledThreeClientRequest_BindsJoiningAndAnchorEntities()
    {
        var result = OfficialBattleAssistanceJoinWireCodec.DecodeLayout(
            OfficialBattleAssistanceJoinWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Convert.FromHexString(CapturedFrame));

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal((uint)254, result.Value.JoiningEntityId);
        Assert.Equal((uint)242, result.Value.BattleAnchorEntityId);
        Assert.Equal(OfficialBattleAssistanceJoinWireCodec.EvidenceFrameSha256, result.Value.DecodedFrameSha256);
        Assert.False(result.Value.RuntimeMutationAllowed);
    }

    [Fact]
    public void Encoder_RoundTripsCapturedRequest()
    {
        var encoded = OfficialBattleAssistanceJoinWireCodec.EncodeLayout(
            OfficialBattleAssistanceJoinWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            254,
            242);

        Assert.True(encoded.LayoutDecoded);
        Assert.Equal(CapturedFrame, Convert.ToHexString(encoded.Value!.Span));
    }

    [Fact]
    public void RuntimeMutation_RemainsBlockedUntilBattleAuthorityAndSerializersAreConnected()
    {
        var result = OfficialBattleAssistanceJoinWireCodec.DecodeForRuntime(
            OfficialBattleAssistanceJoinWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Convert.FromHexString(CapturedFrame));

        Assert.Equal(OfficialBattleAssistanceJoinWireResultCode.SemanticEvidenceBlocked, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Decoder_RejectsWrongStateChecksumAndEntityPair()
    {
        var frame = Convert.FromHexString(CapturedFrame);
        var badChecksum = frame.ToArray();
        badChecksum[^1] ^= 1;
        var sameEntity = frame.ToArray();
        sameEntity.AsSpan(7, 4).Clear();
        sameEntity[7] = 0xFE;
        sameEntity[^1] = OfficialLoginWireTransform.ComputeChecksum(sameEntity);

        Assert.Equal(
            OfficialBattleAssistanceJoinWireResultCode.InvalidState,
            OfficialBattleAssistanceJoinWireCodec.DecodeLayout(
                OfficialBattleAssistanceJoinWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                frame).Code);
        Assert.Equal(
            OfficialBattleAssistanceJoinWireResultCode.InvalidChecksum,
            OfficialBattleAssistanceJoinWireCodec.DecodeLayout(
                OfficialBattleAssistanceJoinWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                badChecksum).Code);
        Assert.Equal(
            OfficialBattleAssistanceJoinWireResultCode.SameEntity,
            OfficialBattleAssistanceJoinWireCodec.DecodeLayout(
                OfficialBattleAssistanceJoinWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                sameEntity).Code);
    }
}
