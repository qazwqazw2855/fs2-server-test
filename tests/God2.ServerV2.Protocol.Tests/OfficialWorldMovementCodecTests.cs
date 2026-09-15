using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldMovementCodecTests
{
    [Theory]
    [InlineData("0A0080BAD7C34DA69488", 16, 14, 1)]
    [InlineData("0A0080BAD7C44EA79586", 16, 15, 2)]
    public void Decodes_recovered_movement_samples(
        string frameHex,
        ushort expectedX,
        ushort expectedY,
        byte expectedSequence)
    {
        Assert.True(
            OfficialWorldMovementCodec.TryDecode(
                Convert.FromHexString(frameHex),
                out var movement));

        Assert.Equal(expectedX, movement.X);
        Assert.Equal(expectedY, movement.Y);
        Assert.Equal(expectedSequence, movement.Sequence);
        Assert.Equal(
            OfficialWorldMovementCodec.MountedState,
            movement.State);
    }

    [Theory]
    [InlineData("0A0080BAD7C34DA69489")]
    [InlineData("0A002E10000E0001FF72")]
    [InlineData("090080BAD7C34DA694")]
    public void Rejects_invalid_checksum_plaintext_or_malformed_frames(
        string frameHex)
    {
        Assert.False(
            OfficialWorldMovementCodec.TryDecode(
                Convert.FromHexString(frameHex),
                out _));
    }
}
