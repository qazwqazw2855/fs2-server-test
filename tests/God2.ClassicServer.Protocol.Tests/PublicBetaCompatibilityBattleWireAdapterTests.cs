using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class PublicBetaCompatibilityBattleWireAdapterTests
{
    [Fact]
    public void CurrentBasicAttack_AdaptsToCanonicalTwoSideCommand()
    {
        var result = PublicBetaCompatibilityBattleWireAdapter.AdaptClientCommand(
            OfficialBattleCommandWireCodec.ClientBuildId,
            BuildFrame(action: 1, group1: 0x0400));

        Assert.True(result.Adapted);
        Assert.Equal(OfficialBattleCommandAction.BasicAttack, result.Command!.Action);
        Assert.Equal([(byte)24], result.Command.TargetBattlePositions);
        Assert.False(result.Command.ProductionMutationAllowed);
    }

    [Fact]
    public void ThirdTargetGroup_IsNotMisreadAsPublicBetaTargetMask()
    {
        var result = PublicBetaCompatibilityBattleWireAdapter.AdaptClientCommand(
            OfficialBattleCommandWireCodec.ClientBuildId,
            BuildFrame(action: 1, group2: 1));

        Assert.Equal(PublicBetaBattleAdapterResultCode.UnsupportedThirdTargetGroup, result.Code);
        Assert.Null(result.Command);
    }

    [Fact]
    public void SkillOperand_RequiresCurrentCatalogBinding()
    {
        var blocked = PublicBetaCompatibilityBattleWireAdapter.AdaptClientCommand(
            OfficialBattleCommandWireCodec.ClientBuildId,
            BuildFrame(action: 3, group1: 1, actionParameter: 257));
        var mapped = PublicBetaCompatibilityBattleWireAdapter.AdaptClientCommand(
            OfficialBattleCommandWireCodec.ClientBuildId,
            BuildFrame(action: 3, group1: 1, actionParameter: 257),
            operand => operand == 257);

        Assert.Equal(PublicBetaBattleAdapterResultCode.SkillCatalogMappingRequired, blocked.Code);
        Assert.True(mapped.Adapted);
        Assert.Equal((uint)257, mapped.Command!.RawActionParameter);
    }

    [Fact]
    public void CurrentOutputAdapterSet_CoversEightBattleOutputOpcodesAndLeaves_0x84BattleFeedback_AsEvidenceOnly()
    {
        Assert.Equal(8, PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputOpcodes.Count);
        Assert.DoesNotContain((byte)0x84, PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputOpcodes);
        Assert.Contains((byte)0x89, PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputOpcodes);
    }

    [Fact]
    public void CurrentOutputAdapterSet_LeavesOneCombatOpcodeForEvidenceOnlyPending()
    {
        Assert.Equal(
            PublicBetaCompatibilityBattleWireAdapter.CanonicalServerCombatContractCount,
            PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputOpcodes.Count +
            PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputEvidenceBlockedCount);

        var battle84 = OfficialBattleCrossVersionEvidence.Find(0x84, PacketDirection.ServerToClient);
        Assert.NotNull(battle84);
        Assert.Equal(BattleCrossVersionPromotionStatus.HypothesisOnly, battle84.Status);
        Assert.Contains("entireCurrentBuildRecord", battle84.BlockedFields);
    }

    private static byte[] BuildFrame(
        byte action,
        ushort group0 = 0,
        ushort group1 = 0,
        ushort group2 = 0,
        uint actionParameter = 0)
    {
        var frame = new byte[OfficialBattleCommandWireCodec.DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = OfficialBattleCommandWireCodec.CommandOpcode;
        var payload = frame.AsSpan(3, OfficialBattleCommandWireCodec.ApplicationPayloadLength);
        payload[0] = 6;
        payload[1] = action;
        BinaryPrimitives.WriteUInt16LittleEndian(payload[4..], group0);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[6..], group1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[8..], group2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[12..], actionParameter);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }
}
