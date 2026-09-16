using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialNpcInteractionCodecTests
{
    [Theory]
    [InlineData(
        "0800776188D83FFD",
        OfficialNpcInteractionKind.Open,
        5042)]
    [InlineData(
        "080077ABBED83F73",
        OfficialNpcInteractionKind.Open,
        5096)]
    [InlineData(
        "0800716388D83FFF",
        OfficialNpcInteractionKind.MerchantClose,
        5042)]
    public void Decodes_evidence_pinned_interaction(
        string encodedHex,
        OfficialNpcInteractionKind expectedKind,
        ushort expectedHandle)
    {
        var succeeded =
            OfficialNpcInteractionCodec.TryDecode(
                Convert.FromHexString(encodedHex),
                out var request,
                out var failureCode);

        Assert.True(succeeded, failureCode);
        Assert.NotNull(request);
        Assert.Equal(expectedKind, request.Kind);
        Assert.Equal(
            expectedHandle,
            request.ClientEntityHandle);
    }

    [Fact]
    public void Altered_checksum_is_rejected()
    {
        var frame =
            Convert.FromHexString("0800776188D83FFD");
        frame[^1] ^= 0x01;

        Assert.False(
            OfficialNpcInteractionCodec.TryDecode(
                frame,
                out var request,
                out var failureCode));

        Assert.Null(request);
        Assert.Equal("ChecksumMismatch", failureCode);
    }

    [Fact]
    public void Unknown_opcode_is_rejected()
    {
        var encoded =
            Convert.FromHexString("080028B088D83F5C");

        Assert.False(
            OfficialNpcInteractionCodec.TryDecode(
                encoded,
                out var request,
                out var failureCode));

        Assert.Null(request);
        Assert.Equal(
            "UnsupportedOpcode",
            failureCode);
    }
}
