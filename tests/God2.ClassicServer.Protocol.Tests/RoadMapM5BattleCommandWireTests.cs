using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class RoadMapM5BattleCommandWireTests
{
    [Fact]
    public void DecodeLayout_UsesThreeFourteenPositionTargetGroups()
    {
        var frame = BuildFrame(
            position: 6,
            action: 3,
            continuation: false,
            side: 1,
            preservedByte3: 25,
            group0: 0x0001,
            group1: 0x0010,
            group2: 0x2000,
            battleContext: 0,
            actionParameter: 257);

        var result = OfficialBattleCommandWireCodec.DecodeLayout(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            frame);

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal([0, 18, 41], result.Value.TargetPositionIndices);
        Assert.Equal(3, result.Value.ActionCode);
        Assert.Equal(OfficialBattleCommandAction.Skill, result.Value.Action);
        Assert.Equal((uint)257, result.Value.ActionParameter);
        Assert.False(result.Value.RuntimeMutationAllowed);
    }

    [Theory]
    [InlineData(1, OfficialBattleCommandAction.BasicAttack)]
    [InlineData(2, OfficialBattleCommandAction.Defend)]
    [InlineData(3, OfficialBattleCommandAction.Skill)]
    [InlineData(11, OfficialBattleCommandAction.Flee)]
    public void DecodeLayout_MapsControlledBattleActionEvidence(
        byte actionCode,
        OfficialBattleCommandAction expected)
    {
        var result = OfficialBattleCommandWireCodec.DecodeLayout(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            BuildFrame(action: actionCode));

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal(expected, result.Value.Action);
        Assert.False(result.Value.RuntimeMutationAllowed);
    }

    [Theory]
    [InlineData("1400350601000000000020000000002E00000012", OfficialBattleCommandAction.BasicAttack)]
    [InlineData("14003506030000000000200000000001000000E7", OfficialBattleCommandAction.Skill)]
    [InlineData("1400350602000040000000000000020000000007", OfficialBattleCommandAction.Defend)]
    [InlineData("140035060B00004000000001000000001000001F", OfficialBattleCommandAction.Flee)]
    public void DecodeLayout_RecognizesControlledLiveCaptureSamples(
        string frameHex,
        OfficialBattleCommandAction expected)
    {
        var result = OfficialBattleCommandWireCodec.DecodeLayout(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString(frameHex));

        Assert.True(result.LayoutDecoded);
        Assert.NotNull(result.Value);
        Assert.Equal(expected, result.Value.Action);
        Assert.Equal("ActionCodeVerified_ActionParameterSemanticsEvidenceBlocked", result.Value.SemanticEvidenceStatus);
    }

    [Fact]
    public void DecodeForRuntime_FailsClosedAfterLayoutDecode()
    {
        var frame = BuildFrame(group1: 0x0008, actionParameter: 36);

        var result = OfficialBattleCommandWireCodec.DecodeForRuntime(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            frame);

        Assert.Equal(OfficialBattleCommandWireResultCode.SemanticEvidenceBlocked, result.Code);
        Assert.False(result.LayoutDecoded);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("another-build", GameplayProtocolState.Battle, OfficialBattleCommandWireResultCode.BuildMismatch)]
    [InlineData(OfficialBattleCommandWireCodec.ClientBuildId, GameplayProtocolState.World, OfficialBattleCommandWireResultCode.InvalidState)]
    public void DecodeLayout_RejectsWrongBuildOrState(
        string build,
        GameplayProtocolState state,
        OfficialBattleCommandWireResultCode expected)
    {
        var result = OfficialBattleCommandWireCodec.DecodeLayout(build, state, BuildFrame());

        Assert.Equal(expected, result.Code);
    }

    [Fact]
    public void DecodeLayout_RejectsTruncatedWrongOpcodeAndBadChecksum()
    {
        var frame = BuildFrame();
        var wrongOpcode = frame.ToArray();
        wrongOpcode[2] = 0x36;
        wrongOpcode[^1] = OfficialLoginWireTransform.ComputeChecksum(wrongOpcode);
        var badChecksum = frame.ToArray();
        badChecksum[^1] ^= 0x5A;

        Assert.Equal(
            OfficialBattleCommandWireResultCode.InvalidLength,
            OfficialBattleCommandWireCodec.DecodeLayout(
                OfficialBattleCommandWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                frame[..^1]).Code);
        Assert.Equal(
            OfficialBattleCommandWireResultCode.InvalidOpcode,
            OfficialBattleCommandWireCodec.DecodeLayout(
                OfficialBattleCommandWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                wrongOpcode).Code);
        Assert.Equal(
            OfficialBattleCommandWireResultCode.InvalidChecksum,
            OfficialBattleCommandWireCodec.DecodeLayout(
                OfficialBattleCommandWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                badChecksum).Code);
    }

    [Fact]
    public void DecodeLayout_RejectsPositionAndTargetBitsOutsideStaticBuilderContract()
    {
        var position = BuildFrame(position: 15);
        var target = BuildFrame(group0: 0x4000);

        Assert.Equal(
            OfficialBattleCommandWireResultCode.InvalidPosition,
            OfficialBattleCommandWireCodec.DecodeLayout(
                OfficialBattleCommandWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                position).Code);
        Assert.Equal(
            OfficialBattleCommandWireResultCode.InvalidTargetMask,
            OfficialBattleCommandWireCodec.DecodeLayout(
                OfficialBattleCommandWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                target).Code);
    }

    private static byte[] BuildFrame(
        byte position = 1,
        byte action = 1,
        bool continuation = false,
        byte side = 0,
        byte preservedByte3 = 0,
        ushort group0 = 0,
        ushort group1 = 0,
        ushort group2 = 0,
        ushort battleContext = 0,
        uint actionParameter = 1)
    {
        var frame = new byte[OfficialBattleCommandWireCodec.DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, OfficialBattleCommandWireCodec.DecodedFrameLength);
        frame[2] = OfficialBattleCommandWireCodec.CommandOpcode;
        var payload = frame.AsSpan(3, OfficialBattleCommandWireCodec.ApplicationPayloadLength);
        payload[0] = position;
        payload[1] = (byte)(action | (continuation ? 0x80 : 0));
        payload[2] = side;
        payload[3] = preservedByte3;
        BinaryPrimitives.WriteUInt16LittleEndian(payload[4..], group0);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[6..], group1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[8..], group2);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[10..], battleContext);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[12..], actionParameter);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }
}
