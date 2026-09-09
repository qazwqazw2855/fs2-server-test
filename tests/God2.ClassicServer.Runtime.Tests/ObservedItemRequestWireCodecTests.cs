using System.Buffers.Binary;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class ObservedItemRequestWireCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void Exact_build_shape_decodes_item_and_slot_while_preserving_opaque_tail(byte rawTail)
    {
        var result = Decode(Frame(0x0C1D, 7, rawTail));

        Assert.True(result.Succeeded);
        Assert.Equal(0x0C1D, result.Value!.ClientItemId);
        Assert.Equal(7, result.Value.SlotIndex);
        Assert.Equal(rawTail, result.Value.RawTail);
        Assert.Equal(
            "ExactBuildStaticAndObserved_CrossVersionSemanticCorroboration",
            result.Value.EvidenceClassification);
        Assert.Equal(64, result.Value.DecodedFrameSha256.Length);
    }

    [Fact]
    public void Build_state_length_opcode_and_integrity_fail_closed()
    {
        var valid = Frame(0x0C1D, 7, 0);
        var badOpcode = valid.ToArray();
        badOpcode[2] = 0x26;
        badOpcode[^1] = OfficialLoginWireTransform.ComputeChecksum(badOpcode);
        var badIntegrity = valid.ToArray();
        badIntegrity[^1] ^= 1;

        Assert.Equal(ObservedItemRequestWireResultCode.BuildMismatch, Decode(valid, "other").Code);
        Assert.Equal(ObservedItemRequestWireResultCode.InvalidState, Decode(valid, state: GameplayProtocolState.Login).Code);
        Assert.Equal(ObservedItemRequestWireResultCode.InvalidLength, Decode(valid[..^1]).Code);
        Assert.Equal(ObservedItemRequestWireResultCode.InvalidOpcode, Decode(badOpcode).Code);
        Assert.Equal(ObservedItemRequestWireResultCode.Malformed, Decode(badIntegrity).Code);
    }

    [Theory]
    [InlineData(0, 7, ObservedItemRequestWireResultCode.Malformed)]
    [InlineData(3101, 50, ObservedItemRequestWireResultCode.SlotOutOfRange)]
    public void Invalid_identity_or_slot_fails_closed(
        ushort itemId,
        byte slot,
        ObservedItemRequestWireResultCode expected)
    {
        var result = Decode(Frame(itemId, slot, 0));

        Assert.Equal(expected, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Codec_exposes_no_serializer_or_runtime_mutation_bridge()
    {
        var publicMethods = typeof(ObservedItemRequestWireCodec)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(["Decode"], publicMethods);
    }

    private static ObservedItemRequestWireResult<ObservedItemRequest> Decode(
        ReadOnlySpan<byte> frame,
        string? build = null,
        GameplayProtocolState state = GameplayProtocolState.World) =>
        ObservedItemRequestWireCodec.Decode(
            build ?? ObservedItemRequestWireCodec.ClientBuildId,
            state,
            frame);

    private static byte[] Frame(ushort clientItemId, byte slotIndex, byte rawTail)
    {
        var frame = new byte[ObservedItemRequestWireCodec.FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = ObservedItemRequestWireCodec.Opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), clientItemId);
        frame[5] = slotIndex;
        frame[6] = rawTail;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }
}
