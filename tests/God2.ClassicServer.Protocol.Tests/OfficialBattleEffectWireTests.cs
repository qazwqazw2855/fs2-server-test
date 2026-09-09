using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialBattleEffectWireTests
{
    [Fact]
    public void DecodeLayout_RecoversAttestedLiveRecordFields()
    {
        var result = OfficialBattleEffectWireCodec.DecodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString("830101010000000400DBFF001C0000"));

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal((byte)1, result.Value.EffectKind);
        Assert.Equal((byte)1, result.Value.SourceBattlePosition);
        Assert.Equal((byte)0, result.Value.SourceSide);
        Assert.Equal((byte)1, result.Value.SourceSlot);
        Assert.Equal((byte)1, result.Value.PlaybackGate);
        Assert.Equal((ushort)0, result.Value.FriendlyTargetMask);
        Assert.Equal((ushort)0x0400, result.Value.EnemyTargetMask);
        Assert.Equal((byte)0, result.Value.ReservedByte8);
        Assert.Equal([24], result.Value.TargetBattlePositions);
        Assert.Equal((short)-37, result.Value.SignedResult);
        Assert.Equal((ushort)0x1C00, result.Value.AuxiliaryValue0);
        Assert.Equal(
            "RecordBoundaryVerified_SignedResultProjectionAndTypedSelectorRoutingConsumerVerified_ServerFormulaAndSerializerBlocked",
            result.Value.SemanticEvidenceStatus);
        Assert.Contains("0x00160D20", result.Value.FieldLayoutEvidence, StringComparison.Ordinal);
        Assert.Contains("BattleActorStateRoutingEvidence", result.Value.FieldLayoutEvidence, StringComparison.Ordinal);
        Assert.False(result.Value.RuntimeMutationAllowed);
    }

    [Fact]
    public void DecodeForRuntime_FailsClosedBecauseClientConsumerDoesNotProveServerSerializer()
    {
        var result = OfficialBattleEffectWireCodec.DecodeForRuntime(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString("830101010000000400DBFF001C0000"));

        Assert.Equal(OfficialBattleEffectWireResultCode.SemanticEvidenceBlocked, result.Code);
        Assert.Null(result.Value);
        Assert.Equal("wire.battle.effect_server_serializer_evidence_blocked", result.FailureCode);
    }

    [Fact]
    public void EncodeLayout_RoundTripsTheAttestedRecordButRuntimeEncodingRemainsBlocked()
    {
        var layout = new OfficialBattleEffectLayout(
            EffectKind: 1,
            SourceBattlePosition: 1,
            PlaybackGate: 1,
            FriendlyTargetMask: 0,
            EnemyTargetMask: 0x0400,
            ReservedByte8: 0,
            SignedResult: -37,
            AuxiliaryValue0: 0x1C00,
            AuxiliaryValue1: 0);

        var encoded = OfficialBattleEffectWireCodec.EncodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            layout);

        Assert.True(encoded.LayoutEncoded);
        Assert.True(encoded.Succeeded);
        Assert.Equal("830101010000000400DBFF001C0000", Convert.ToHexString(encoded.Value!));

        var decoded = OfficialBattleEffectWireCodec.DecodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            encoded.Value!);
        Assert.True(decoded.LayoutDecoded);
        Assert.Equal(layout.SignedResult, decoded.Value!.SignedResult);

        var runtime = OfficialBattleEffectWireCodec.EncodeForRuntime(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            layout);
        Assert.Equal(OfficialBattleEffectWireResultCode.SemanticEvidenceBlocked, runtime.Code);
        Assert.Null(runtime.Value);
        Assert.Equal("wire.battle.effect_server_serializer_evidence_blocked", runtime.FailureCode);
    }

    [Fact]
    public void EncodeLayout_RejectsOutOfRangeSourceButPreservesObservedHighMaskBits()
    {
        var invalidSource = new OfficialBattleEffectLayout(1, 28, 0, 0, 0, 0, 0, 0, 0);
        var observedHighBits = new OfficialBattleEffectLayout(7, 6, 0, 0x6000, 0x14FC, 0x14, 17, 0, 0xAD63);

        Assert.Equal(
            OfficialBattleEffectWireResultCode.InvalidSourceBattlePosition,
            OfficialBattleEffectWireCodec.EncodeLayout(
                OfficialBattleEffectWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                invalidSource).Code);
        var encoded = OfficialBattleEffectWireCodec.EncodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            observedHighBits);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal("830706000060FC14141100000063AD", Convert.ToHexString(encoded.Value!));

        var decoded = OfficialBattleEffectWireCodec.DecodeLayout(
            OfficialBattleEffectWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            encoded.Value!);
        Assert.True(decoded.LayoutDecoded);
        Assert.Equal((ushort)0x6000, decoded.Value!.FriendlyTargetMask);
        Assert.Equal((ushort)0x14FC, decoded.Value.EnemyTargetMask);
    }

    [Fact]
    public void DecodeLayout_RejectsWrongBuildStateLengthAndOpcode()
    {
        var valid = Convert.FromHexString("830101010000000400DBFF001C0000");
        var wrongOpcode = valid.ToArray();
        wrongOpcode[0] = 0x84;

        Assert.Equal(
            OfficialBattleEffectWireResultCode.BuildMismatch,
            OfficialBattleEffectWireCodec.DecodeLayout("other", GameplayProtocolState.Battle, valid).Code);
        Assert.Equal(
            OfficialBattleEffectWireResultCode.InvalidState,
            OfficialBattleEffectWireCodec.DecodeLayout(
                OfficialBattleEffectWireCodec.ClientBuildId, GameplayProtocolState.World, valid).Code);
        Assert.Equal(
            OfficialBattleEffectWireResultCode.InvalidLength,
            OfficialBattleEffectWireCodec.DecodeLayout(
                OfficialBattleEffectWireCodec.ClientBuildId, GameplayProtocolState.Battle, valid[..^1]).Code);
        Assert.Equal(
            OfficialBattleEffectWireResultCode.InvalidOpcode,
            OfficialBattleEffectWireCodec.DecodeLayout(
                OfficialBattleEffectWireCodec.ClientBuildId, GameplayProtocolState.Battle, wrongOpcode).Code);

        var invalidSource = valid.ToArray();
        invalidSource[2] = 28;
        Assert.Equal(
            OfficialBattleEffectWireResultCode.InvalidSourceBattlePosition,
            OfficialBattleEffectWireCodec.DecodeLayout(
                OfficialBattleEffectWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                invalidSource).Code);
    }
}
