using System.Buffers.Binary;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialInventoryActivationWireCodecTests
{
    [Fact]
    public void Exact_build_static_contract_decodes_item_slot_and_quantity()
    {
        var frame = Frame(clientItemId: 0x1906, slotIndex: 0x23, quantity: 1);

        var result = OfficialInventoryActivationWireCodec.Decode(
            OfficialInventoryActivationWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Succeeded);
        Assert.Equal(0x1906, result.Value!.ClientItemId);
        Assert.Equal(0x23, result.Value.SlotIndex);
        Assert.Equal(1, result.Value.Quantity);
        Assert.Equal(64, result.Value.DecodedFrameSha256.Length);
        Assert.Equal(OfficialInventoryActivationWireCodec.EvidenceId, result.Value.EvidenceId);
    }

    [Fact]
    public void Build_state_length_opcode_and_integrity_fail_closed()
    {
        var valid = Frame(0x1906, 0x23, 1);
        var badLength = valid[..^1];
        var badOpcode = valid.ToArray();
        badOpcode[2] = 0x29;
        badOpcode[^1] = OfficialLoginWireTransform.ComputeChecksum(badOpcode);
        var badIntegrity = valid.ToArray();
        badIntegrity[^1] ^= 1;

        Assert.Equal(
            OfficialInventoryActivationWireResultCode.BuildMismatch,
            Decode(valid, build: "unsupported").Code);
        Assert.Equal(
            OfficialInventoryActivationWireResultCode.InvalidState,
            Decode(valid, state: GameplayProtocolState.Login).Code);
        Assert.Equal(
            OfficialInventoryActivationWireResultCode.InvalidLength,
            Decode(badLength).Code);
        Assert.Equal(
            OfficialInventoryActivationWireResultCode.InvalidOpcode,
            Decode(badOpcode).Code);
        Assert.Equal(
            OfficialInventoryActivationWireResultCode.Malformed,
            Decode(badIntegrity).Code);
    }

    [Theory]
    [InlineData(0, 1, OfficialInventoryActivationWireResultCode.Malformed)]
    [InlineData(50, 1, OfficialInventoryActivationWireResultCode.SlotOutOfRange)]
    [InlineData(0, 0, OfficialInventoryActivationWireResultCode.UnsupportedQuantity)]
    [InlineData(0, 2, OfficialInventoryActivationWireResultCode.UnsupportedQuantity)]
    public void Unverified_field_values_fail_closed(
        byte slotIndex,
        byte quantity,
        OfficialInventoryActivationWireResultCode expected)
    {
        var item = expected == OfficialInventoryActivationWireResultCode.Malformed ? (ushort)0 : (ushort)0x1906;
        var result = Decode(Frame(item, slotIndex, quantity));

        Assert.Equal(expected, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Decoder_exposes_no_serializer_or_runtime_mutation_bridge()
    {
        var publicMethods = typeof(OfficialInventoryActivationWireCodec)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(["Decode"], publicMethods);
    }

    private static OfficialInventoryActivationWireResult<OfficialInventoryActivationRequest> Decode(
        ReadOnlySpan<byte> frame,
        string? build = null,
        GameplayProtocolState state = GameplayProtocolState.World) =>
        OfficialInventoryActivationWireCodec.Decode(
            build ?? OfficialInventoryActivationWireCodec.ClientBuildId,
            state,
            frame);

    private static byte[] Frame(ushort clientItemId, byte slotIndex, byte quantity)
    {
        var frame = new byte[OfficialInventoryActivationWireCodec.FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = OfficialInventoryActivationWireCodec.Opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), clientItemId);
        frame[5] = slotIndex;
        frame[6] = quantity;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }
}
