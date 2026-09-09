using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialBattleRosterControlWireTests
{
    private const string KeroRoster = "1C060DFE00023800006B65726F0000000000000000000000000000000000000000000000000000000000000101";

    [Fact]
    public void CurrentRoster_BindsControlledJoinerIdentityAndRoundTrips()
    {
        var result = OfficialBattleRosterControlWireCodec.DecodeRoster(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString(KeroRoster));

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal((byte)6, result.Value.BattlePosition);
        Assert.Equal((byte)13, result.Value.DisplayLevel);
        Assert.Equal((ushort)254, result.Value.EntityId);
        Assert.Equal((ushort)0x3802, result.Value.ActorKindFlags);
        Assert.Equal((ushort)0, result.Value.EncounterLocalId);
        Assert.Equal("6B65726F000000000000000000000000", Convert.ToHexString(result.Value.NameFieldRaw.Span));
        Assert.False(result.Value.RuntimeMutationAllowed);

        var encoded = OfficialBattleRosterControlWireCodec.EncodeRosterLayout(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            result.Value);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal(KeroRoster, Convert.ToHexString(encoded.Value!));
    }

    [Fact]
    public void CurrentControl_PreservesChangedSeventeenByteLayout()
    {
        var result = OfficialBattleRosterControlWireCodec.DecodeControl(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString("86001400009C0100005D01000000000101"));

        Assert.True(result.LayoutDecoded);
        Assert.Equal((byte)0, result.Value!.Subtype);
        Assert.Equal((byte)20, result.Value.BattlePosition);
        Assert.Equal((uint)412, result.Value.Value0);
        Assert.Equal((uint)349, result.Value.Value1);
        Assert.Equal((uint)0x01010000, result.Value.Value2);

        var encoded = OfficialBattleRosterControlWireCodec.EncodeControlLayout(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            result.Value);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal("86001400009C0100005D01000000000101", Convert.ToHexString(encoded.Value!));
    }

    [Theory]
    [InlineData("3840", 0x38, 0x40)]
    [InlineData("8500", 0x85, 0x00)]
    public void CurrentBoundaries_PreservePayloadWithoutInventingMeaning(string hex, byte opcode, byte raw)
    {
        var result = OfficialBattleRosterControlWireCodec.DecodeBoundary(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString(hex));

        Assert.True(result.LayoutDecoded);
        Assert.Equal(opcode, result.Value!.Opcode);
        Assert.Equal(raw, result.Value.RawControl);
        Assert.False(result.Value.ConsumerReadsControl);

        var encoded = OfficialBattleRosterControlWireCodec.EncodeBoundaryLayout(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            result.Value);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal(hex, Convert.ToHexString(encoded.Value!));
    }

    [Fact]
    public void CurrentPacked87_UsesCrossVersionVerifiedBitSplit()
    {
        var result = OfficialBattleRosterControlWireCodec.DecodePackedControl87(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString("870180"));

        Assert.True(result.LayoutDecoded);
        Assert.Equal((ushort)0x8001, result.Value!.RawWord);
        Assert.Equal((byte)1, result.Value.LowSelector);
        Assert.Equal((byte)0, result.Value.HighSelector);
        Assert.False(result.Value.ModeBit14);
        Assert.True(result.Value.StateBit15);

        var encoded = OfficialBattleRosterControlWireCodec.EncodePackedControl87Layout(
            OfficialBattleRosterControlWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            result.Value);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal("870180", Convert.ToHexString(encoded.Value!));
    }

    [Fact]
    public void CrossVersionLedger_RejectsChangedLayoutsAndPromotesOnlyCurrentConfirmedFields()
    {
        var action = OfficialBattleCrossVersionEvidence.Find(0x35, PacketDirection.ClientToServer)!;
        var roster = OfficialBattleCrossVersionEvidence.Find(0x1C, PacketDirection.ServerToClient)!;
        var snapshot = OfficialBattleCrossVersionEvidence.Find(0x88, PacketDirection.ServerToClient)!;
        var settlement = OfficialBattleCrossVersionEvidence.Find(0x89, PacketDirection.ServerToClient)!;
        var localDerivedStats = OfficialBattleCrossVersionEvidence.Find(0x2C, PacketDirection.ServerToClient)!;

        Assert.Equal(BattleCrossVersionPromotionStatus.RejectedAsChanged, action.Status);
        Assert.Contains("actionCode", action.PromotedFields);
        Assert.Contains("rawOperand", action.BlockedFields);
        Assert.Equal(29, roster.PublicBetaRecordLength);
        Assert.Equal(45, roster.CurrentBuildRecordLength);
        Assert.Contains("entityId", roster.PromotedFields);
        Assert.Equal(BattleCrossVersionPromotionStatus.CurrentBuildVerified, snapshot.Status);
        Assert.Equal(121, snapshot.CurrentBuildRecordLength);
        Assert.Equal(BattleCrossVersionPromotionStatus.RejectedAsChanged, settlement.Status);
        Assert.Contains("publicBetaSettlementOffsets", settlement.BlockedFields);
        Assert.Equal(BattleCrossVersionPromotionStatus.HypothesisOnly, localDerivedStats.Status);
    }

    [Fact]
    public void CrossVersionLedger_TracksPendingCombatOpcode_0x84_BattleOutputFeedback_AsEvidenceOnly()
    {
        var battleTick = OfficialBattleCrossVersionEvidence.Find(0x84, PacketDirection.ServerToClient)!;

        Assert.Equal(BattleCrossVersionPromotionStatus.HypothesisOnly, battleTick.Status);
        Assert.Equal(15, battleTick.PublicBetaRecordLength);
        Assert.Null(battleTick.CurrentBuildRecordLength);
        Assert.Contains("entireCurrentBuildRecord", battleTick.BlockedFields);
        Assert.Equal("not observed in current controlled traces", battleTick.CurrentBuildEvidence);
    }

    [Fact]
    public void CrossVersionLedger_TracksPendingCombatOpcode_0x2C_Local_Derived_Stats_IsEvidenceOnly()
    {
        var localStats = OfficialBattleCrossVersionEvidence.Find(0x2C, PacketDirection.ServerToClient)!;

        Assert.Equal(BattleCrossVersionPromotionStatus.HypothesisOnly, localStats.Status);
        Assert.Equal(31, localStats.PublicBetaRecordLength);
        Assert.Null(localStats.CurrentBuildRecordLength);
        Assert.Contains("currentBuildPrivateStatOpcodeAndLayout", localStats.BlockedFields);
        Assert.Equal(
            "no current-build network record carries a confirmed local-player derived-stats panel vector",
            localStats.CurrentBuildEvidence);
    }
}
