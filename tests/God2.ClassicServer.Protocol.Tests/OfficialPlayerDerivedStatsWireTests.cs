using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialPlayerDerivedStatsWireTests
{
    [Fact]
    public void Canonical_current_layout_round_trips_all_promoted_fields()
    {
        var source = Layout(
            positiveMask: 0x01F6,
            negativeMask: 0x0001);

        var encoded = OfficialPlayerDerivedStatsWireCodec.EncodeCanonicalLayout(
            OfficialPlayerDerivedStatsWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            source);

        Assert.True(encoded.LayoutDecoded);
        Assert.Equal(OfficialPlayerDerivedStatsWireCodec.ApplicationRecordLength, encoded.Value!.Length);
        Assert.Equal(OfficialPlayerDerivedStatsWireCodec.Opcode, encoded.Value[0]);
        Assert.All(new[] { 3, 4, 7, 8, 11, 12, 15, 16, 39, 40, 43, 44 },
            offset => Assert.Equal(0, encoded.Value[offset]));

        var decoded = OfficialPlayerDerivedStatsWireCodec.DecodeLayout(
            OfficialPlayerDerivedStatsWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            encoded.Value);

        Assert.True(decoded.LayoutDecoded);
        Assert.Equal(205, decoded.Value!.VitalityModifier);
        Assert.Equal(24, decoded.Value.StrengthModifier);
        Assert.Equal(-813, decoded.Value.IntelligenceModifier);
        Assert.Equal(813, decoded.Value.SpeedModifier);
        Assert.Equal((uint)57, decoded.Value.Gold);
        Assert.Equal((uint)57, decoded.Value.Wood);
        Assert.Equal((uint)57, decoded.Value.Water);
        Assert.Equal((uint)57, decoded.Value.Fire);
        Assert.Equal((uint)57, decoded.Value.Earth);
        Assert.Equal((uint)94, decoded.Value.PhysicalDefense);
        Assert.Equal((uint)102, decoded.Value.PhysicalAttack);
        Assert.Equal((uint)185, decoded.Value.MagicalDefense);
        Assert.Equal((uint)318, decoded.Value.MagicalAttack);
        Assert.Equal(OfficialPlayerDerivedStatPolarity.Positive, decoded.Value.GoldPolarity);
        Assert.False(decoded.Value.RuntimeMutationAllowed);
    }

    [Fact]
    public void Decode_requires_the_exact_current_build_state_opcode_and_canonical_length()
    {
        var record = new byte[OfficialPlayerDerivedStatsWireCodec.ApplicationRecordLength];
        record[0] = OfficialPlayerDerivedStatsWireCodec.Opcode;

        Assert.Equal(OfficialPlayerSnapshotWireResultCode.BuildMismatch,
            OfficialPlayerDerivedStatsWireCodec.DecodeLayout(
                "wrong", GameplayProtocolState.World, record).Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidState,
            OfficialPlayerDerivedStatsWireCodec.DecodeLayout(
                OfficialPlayerDerivedStatsWireCodec.ClientBuildId, GameplayProtocolState.Login, record).Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidLength,
            OfficialPlayerDerivedStatsWireCodec.DecodeLayout(
                OfficialPlayerDerivedStatsWireCodec.ClientBuildId, GameplayProtocolState.World, record[..^1]).Code);
        record[0] = 0x2D;
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidOpcode,
            OfficialPlayerDerivedStatsWireCodec.DecodeLayout(
                OfficialPlayerDerivedStatsWireCodec.ClientBuildId, GameplayProtocolState.World, record).Code);
    }

    [Fact]
    public void Encoder_rejects_ambiguous_masks_and_signed_word_overflow()
    {
        var overlap = OfficialPlayerDerivedStatsWireCodec.EncodeCanonicalLayout(
            OfficialPlayerDerivedStatsWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Layout(positiveMask: 1, negativeMask: 1));
        var unknownBit = OfficialPlayerDerivedStatsWireCodec.EncodeCanonicalLayout(
            OfficialPlayerDerivedStatsWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Layout(positiveMask: 0x0200, negativeMask: 0));
        var overflow = OfficialPlayerDerivedStatsWireCodec.EncodeCanonicalLayout(
            OfficialPlayerDerivedStatsWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Layout(positiveMask: 0, negativeMask: 0) with { VitalityMagnitude = 0x8000 });

        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, overlap.Code);
        Assert.Equal("wire.player.derived_stats.mask_invalid", overlap.FailureCode);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, unknownBit.Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, overflow.Code);
        Assert.False(OfficialPlayerDerivedStatsWireCodec.RuntimeMutationEnabled);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
            OfficialPlayerDerivedStatsWireCodec.RejectRuntimeMutation<object>().Code);
    }

    private static OfficialPlayerDerivedStatsLayout Layout(ushort positiveMask, ushort negativeMask) =>
        new(
            VitalityMagnitude: 205,
            StrengthMagnitude: 24,
            IntelligenceMagnitude: 813,
            SpeedMagnitude: 813,
            Gold: 57,
            Wood: 57,
            Water: 57,
            Fire: 57,
            Earth: 57,
            PositiveMask: positiveMask,
            NegativeMask: negativeMask,
            PhysicalDefense: 94,
            PhysicalAttack: 102,
            MagicalDefense: 185,
            MagicalAttack: 318,
            RecordSha256: string.Empty,
            EvidenceStatus: string.Empty,
            RuntimeMutationAllowed: false);
}
