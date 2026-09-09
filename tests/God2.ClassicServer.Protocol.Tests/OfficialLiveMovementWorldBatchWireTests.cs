using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialLiveMovementWorldBatchWireTests
{
    [Theory]
    [InlineData("08007387000000A6", OfficialLiveMovementWorldBatchWireCodec.EvidenceOnlyServerChild73Opcode)]
    [InlineData("1800724E0500000321000082E875140201BF00D400E600D4", OfficialLiveMovementWorldBatchWireCodec.EvidenceOnlyServerChild72Opcode)]
    [InlineData("2D0072BF09000010800000A2E875140201BF00EC00EC0060BF0900001080B80BA2E875140201BF00F800EC0028", OfficialLiveMovementWorldBatchWireCodec.EvidenceOnlyServerChild72Opcode)]
    [InlineData("05005D196B", OfficialLiveMovementWorldBatchWireCodec.EvidenceOnlyServerChild5DOpcode)]
    [InlineData("0E00360000000011D51D00000053", 0x36)]
    public void DecodeServerWorldApplicationBatchLayout_preserves_observed_top_level_frame_boundary(
        string frameHex,
        byte expectedOpcode)
    {
        var frame = Convert.FromHexString(frameHex);

        var result = OfficialLiveMovementWorldBatchWireCodec.DecodeServerWorldApplicationBatchLayout(frame);

        Assert.True(result.LayoutDecoded, result.FailureCode);
        var decoded = Assert.Single(result.Value!.Children);
        Assert.Equal(expectedOpcode, result.Value.TopLevelOpcodeCandidate);
        Assert.Equal(expectedOpcode, decoded.Opcode);
        Assert.Equal(0, decoded.Offset);
        Assert.Equal(frame.Length, decoded.Length);
        Assert.Equal(frameHex, decoded.RawHex);
        Assert.False(result.Value.RuntimeMutationAllowed);
    }

    [Fact]
    public void DecodeServerWorldApplicationBatchLayout_rejects_corrupted_observed_top_level_frame()
    {
        var frame = Convert.FromHexString("1800724E0500000321000082E875140201BF00D400E600D4");
        frame[^1] ^= 0x01;

        var result = OfficialLiveMovementWorldBatchWireCodec.DecodeServerWorldApplicationBatchLayout(frame);

        Assert.False(result.LayoutDecoded);
        Assert.Equal(OfficialLiveMovementWorldBatchResultCode.InvalidChecksum, result.Code);
    }
}
