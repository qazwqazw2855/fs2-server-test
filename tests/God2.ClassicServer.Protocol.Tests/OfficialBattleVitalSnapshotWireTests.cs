using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialBattleVitalSnapshotWireTests
{
    private const string ObservedRecord =
        "880000000000000000000000002C0200008E000000000000000000000000000000" +
        "0000000000000000000000000000000000000000070100003F0000000000000000" +
        "000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000020004B19";

    [Fact]
    public void DecodeLayout_RecoversFourteenAttestedCurrentVitalPairs()
    {
        var result = OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
            OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString(ObservedRecord));

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal(0, result.Value.RoundIndex);
        Assert.Equal(14, result.Value.Positions.Count);
        Assert.Equal(new OfficialBattleCurrentVitals(1, 556, 142), result.Value.Positions[1]);
        Assert.Equal(new OfficialBattleCurrentVitals(6, 263, 63), result.Value.Positions[6]);
        Assert.Equal(0x194B0020u, result.Value.OpaqueTrailer);
        Assert.Equal((byte)0, result.Value.ConsumedTrailerByte118);
        Assert.Contains("Offset118ClientConsumerVerified", result.Value.TrailerEvidenceStatus,
            StringComparison.Ordinal);
        Assert.Contains("0x0014BD37", result.Value.FieldLayoutEvidence, StringComparison.Ordinal);
        Assert.Contains("skill-mp5-sequences", result.Value.HitPointMagicPointConsumerEvidence,
            StringComparison.Ordinal);
        Assert.False(result.Value.RuntimeMutationAllowed);
    }

    [Fact]
    public void EncodeLayout_RoundTripsAttestedRecordButRuntimeEncodingRemainsBlocked()
    {
        var original = Convert.FromHexString(ObservedRecord);
        var decoded = OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
            OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            original);
        var layout = new OfficialBattleVitalSnapshotLayout(
            decoded.Value!.RoundIndex,
            decoded.Value.Positions,
            decoded.Value.OpaqueTrailer);

        var encoded = OfficialBattleVitalSnapshotWireCodec.EncodeLayout(
            OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            layout);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal(original, encoded.Value);

        var runtime = OfficialBattleVitalSnapshotWireCodec.EncodeForRuntime(
            OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            layout);
        Assert.Equal(OfficialBattleVitalSnapshotWireResultCode.SemanticEvidenceBlocked, runtime.Code);
        Assert.Null(runtime.Value);
        Assert.Equal("wire.battle.vital_snapshot_server_serializer_evidence_blocked", runtime.FailureCode);
    }

    [Fact]
    public void EncodeLayout_RejectsIncompleteOrMisorderedPositionSets()
    {
        var incomplete = Enumerable.Range(0, 13)
            .Select(position => new OfficialBattleCurrentVitals((byte)position, 0, 0))
            .ToArray();
        var misordered = Enumerable.Range(0, 14)
            .Select(position => new OfficialBattleCurrentVitals((byte)position, 0, 0))
            .ToArray();
        misordered[6] = misordered[6] with { BattlePosition = 7 };

        Assert.Equal(
            OfficialBattleVitalSnapshotWireResultCode.InvalidPositionCount,
            OfficialBattleVitalSnapshotWireCodec.EncodeLayout(
                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                new OfficialBattleVitalSnapshotLayout(0, incomplete, 0)).Code);
        Assert.Equal(
            OfficialBattleVitalSnapshotWireResultCode.InvalidBattlePosition,
            OfficialBattleVitalSnapshotWireCodec.EncodeLayout(
                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                new OfficialBattleVitalSnapshotLayout(0, misordered, 0)).Code);
    }

    [Fact]
    public void DecodeForRuntime_FailsClosedUntilHpMpIdentityIsBound()
    {
        var result = OfficialBattleVitalSnapshotWireCodec.DecodeForRuntime(
            OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString(ObservedRecord));

        Assert.Equal(OfficialBattleVitalSnapshotWireResultCode.SemanticEvidenceBlocked, result.Code);
        Assert.Equal("wire.battle.vital_snapshot_server_serializer_evidence_blocked", result.FailureCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void DecodeLayout_RejectsWrongBuildStateLengthAndOpcode()
    {
        var valid = Convert.FromHexString(ObservedRecord);
        var wrongOpcode = valid.ToArray();
        wrongOpcode[0] = 0x87;

        Assert.Equal(OfficialBattleVitalSnapshotWireResultCode.BuildMismatch,
            OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
                "other", GameplayProtocolState.Battle, valid).Code);
        Assert.Equal(OfficialBattleVitalSnapshotWireResultCode.InvalidState,
            OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                GameplayProtocolState.World, valid).Code);
        Assert.Equal(OfficialBattleVitalSnapshotWireResultCode.InvalidLength,
            OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                GameplayProtocolState.Battle, valid[..^1]).Code);
        Assert.Equal(OfficialBattleVitalSnapshotWireResultCode.InvalidOpcode,
            OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
                OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
                GameplayProtocolState.Battle, wrongOpcode).Code);
    }
}
