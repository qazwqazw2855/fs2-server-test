using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialNpcDialogSelectionCodecTests
{
    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    public void Standalone_selection_round_trips(
        byte opaqueClientValue)
    {
        var encoded =
            OfficialNpcDialogSelectionCodec
                .EncodeStandaloneForProbe(
                    opaqueClientValue);

        try
        {
            Assert.True(
                OfficialNpcDialogSelectionCodec.TryDecode(
                    encoded,
                    out var request,
                    out var failureCode),
                failureCode);
            Assert.NotNull(request);
            Assert.Equal(
                OfficialNpcDialogSelectionCodec
                    .LiveDialogHandle,
                request.ClientEntityHandle);
            Assert.Equal(
                OfficialNpcDialogSelectionCodec
                    .LiveDialogSelector,
                request.Selector);
            Assert.Equal(
                opaqueClientValue,
                request.OpaqueClientValue);
            Assert.False(request.IsCompoundTransport);
        }
        finally
        {
            Array.Clear(encoded);
        }
    }

    [Fact]
    public void Captured_compound_transport_is_accepted()
    {
        var encoded =
            Convert.FromHexString(
                "0F0028C9A047D23B3ABEB1C2C528F4");

        Assert.True(
            OfficialNpcDialogSelectionCodec.TryDecode(
                encoded,
                out var request,
                out var failureCode),
            failureCode);
        Assert.NotNull(request);
        Assert.True(request.IsCompoundTransport);
        Assert.Equal(3793, request.ClientEntityHandle);
        Assert.Equal(7, request.Selector);
        Assert.Equal(0x11, request.OpaqueClientValue);
    }

    [Fact]
    public void Altered_checksum_is_rejected()
    {
        var encoded =
            OfficialNpcDialogSelectionCodec
                .EncodeStandaloneForProbe();
        encoded[^1] ^= 1;

        Assert.False(
            OfficialNpcDialogSelectionCodec.TryDecode(
                encoded,
                out var request,
                out var failureCode));
        Assert.Null(request);
        Assert.Equal(
            "ChecksumMismatch",
            failureCode);
    }

    [Fact]
    public void Unobserved_opaque_value_is_rejected_by_encoder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                OfficialNpcDialogSelectionCodec
                    .EncodeStandaloneForProbe(0x13));
    }

    [Fact]
    public void Valid_length_unknown_opcode_is_not_a_candidate()
    {
        var encoded =
            OfficialNpcDialogSelectionCodec
                .EncodeStandaloneForProbe();

        var decoded =
            OfficialLoginWireTransform.Decode(encoded);

        try
        {
            decoded[2] = 0x84;
            decoded[^1] =
                OfficialLoginWireTransform.ComputeChecksum(
                    decoded);

            var altered =
                OfficialLoginWireTransform.Encode(decoded);

            try
            {
                Assert.False(
                    OfficialNpcDialogSelectionCodec
                        .IsCandidate(altered));
            }
            finally
            {
                Array.Clear(altered);
            }
        }
        finally
        {
            Array.Clear(decoded);
            Array.Clear(encoded);
        }
    }
}
