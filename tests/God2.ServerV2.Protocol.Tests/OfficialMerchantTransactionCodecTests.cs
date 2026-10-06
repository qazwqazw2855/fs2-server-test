using System.Buffers.Binary;
using God2.ServerV2.Protocol;
using Xunit;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialMerchantTransactionCodecTests
{
    [Theory]
    [InlineData(3954, 6901, 1, 1, 7)]
    [InlineData(1504, 6906, 1, 1, 13)]
    [InlineData(1504, 6906, 2, 2, 12)]
    [InlineData(6000, 7000, 3, 1, 0)]
    public void Parses_fields_without_binding_a_specific_merchant(
        int handle, int item, int quantity, int operation, int index)
    {
        var frame = Frame(handle, item, quantity, operation, index);
        Assert.True(OfficialMerchantTransactionCodec.TryDecodeDecoded(
            frame, out var value, out var failure));
        Assert.Equal(string.Empty, failure);
        Assert.NotNull(value);
        Assert.Equal((ushort)handle, value!.ClientEntityHandle);
        Assert.Equal((ushort)item, value.ClientItemId);
        Assert.Equal((byte)quantity, value.Quantity);
        Assert.Equal((OfficialMerchantTransactionOperation)operation,
            value.Operation);
        Assert.Equal((ushort)index, value.CatalogIndexOrClientInventorySlot);
    }

    [Theory]
    [InlineData(3954, 6901, 1, 1, 7)]
    [InlineData(1504, 6906, 1, 1, 13)]
    [InlineData(1504, 6906, 2, 2, 12)]
    public void Parses_encoded_world_frame(
        int handle, int item, int quantity, int operation, int index)
    {
        var encoded = OfficialMerchantTransactionCodec.EncodeRequest(
            (ushort)handle,
            (ushort)item,
            (byte)quantity,
            (OfficialMerchantTransactionOperation)operation,
            (ushort)index);

        Assert.True(
            OfficialMerchantTransactionCodec.IsCandidate(encoded));

        Assert.True(
            OfficialMerchantTransactionCodec.TryDecode(
                encoded,
                out var value,
                out var failure));

        Assert.Equal(string.Empty, failure);
        Assert.NotNull(value);
        Assert.Equal((ushort)handle, value!.ClientEntityHandle);
        Assert.Equal((ushort)item, value.ClientItemId);
        Assert.Equal((byte)quantity, value.Quantity);
        Assert.Equal(
            (OfficialMerchantTransactionOperation)operation,
            value.Operation);
        Assert.Equal(
            (ushort)index,
            value.CatalogIndexOrClientInventorySlot);
    }

    [Fact]
    public void Recognizes_corrupt_merchant_candidate_but_rejects_decode()
    {
        var decoded = Frame(1504, 6906, 1, 1, 13);
        decoded[7] = 2;

        var encoded =
            OfficialWorldBootstrapCodec.EncodeFrame(decoded);

        Assert.True(
            OfficialMerchantTransactionCodec.IsCandidate(encoded));

        Assert.False(
            OfficialMerchantTransactionCodec.TryDecode(
                encoded,
                out var value,
                out var failure));

        Assert.Null(value);
        Assert.Equal("ChecksumMismatch", failure);
    }

    [Theory]
    [InlineData(0, 6906, 1, 1, "InvalidFields")]
    [InlineData(1504, 0, 1, 1, "InvalidFields")]
    [InlineData(1504, 6906, 0, 1, "InvalidFields")]
    [InlineData(1504, 6906, 1, 0, "UnsupportedOperation")]
    [InlineData(1504, 6906, 1, 3, "UnsupportedOperation")]
    public void Rejects_invalid_fields(
        int handle, int item, int quantity, int operation, string expected)
    {
        Reject(Frame(handle, item, quantity, operation, 13), expected);
    }

    [Fact]
    public void Rejects_truncation_header_opcode_and_corruption()
    {
        var valid = Frame(1504, 6906, 1, 1, 13);
        Reject(valid[..11], "InvalidLength");

        var wrongHeader = (byte[])valid.Clone();
        wrongHeader[0] = 11;
        Reject(wrongHeader, "InvalidLength");

        var wrongOpcode = (byte[])valid.Clone();
        wrongOpcode[2] = 0x39;
        wrongOpcode[^1] =
            OfficialLoginWireTransform.ComputeChecksum(wrongOpcode);
        Reject(wrongOpcode, "UnsupportedOpcode");

        var corrupted = (byte[])valid.Clone();
        corrupted[7] = 2;
        Reject(corrupted, "ChecksumMismatch");
    }

    private static void Reject(byte[] frame, string expected)
    {
        Assert.False(OfficialMerchantTransactionCodec.TryDecodeDecoded(
            frame, out var value, out var failure));
        Assert.Null(value);
        Assert.Equal(expected, failure);
    }

    private static byte[] Frame(
        int handle, int item, int quantity, int operation, int index)
    {
        var frame = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, 12);
        frame[2] = 0x38;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), (ushort)handle);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), (ushort)item);
        frame[7] = (byte)quantity;
        frame[8] = (byte)operation;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(9), (ushort)index);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }
}
