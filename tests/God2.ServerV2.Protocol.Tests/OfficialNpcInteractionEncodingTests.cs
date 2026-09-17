using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialNpcInteractionEncodingTests
{
    [Fact]
    public void Encoded_open_round_trips_through_decoder()
    {
        var frame =
            OfficialNpcInteractionCodec.EncodeOpen(
                OfficialNpcDialogCodec.LiveDialogHandle);

        try
        {
            Assert.Equal(
                OfficialNpcInteractionCodec.FrameLength,
                frame.Length);
            Assert.True(
                OfficialNpcInteractionCodec.TryDecode(
                    frame,
                    out var request,
                    out var failureCode),
                failureCode);
            Assert.NotNull(request);
            Assert.Equal(
                OfficialNpcInteractionKind.Open,
                request.Kind);
            Assert.Equal(
                OfficialNpcDialogCodec.LiveDialogHandle,
                request.ClientEntityHandle);
        }
        finally
        {
            Array.Clear(frame);
        }
    }

    [Fact]
    public void Zero_handle_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfficialNpcInteractionCodec.EncodeOpen(0));
}
