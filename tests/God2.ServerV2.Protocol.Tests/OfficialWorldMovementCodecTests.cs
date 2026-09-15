using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldMovementCodecTests
{
    [Fact]
    public void Recognizes_recovered_movement_sample()
    {
        Assert.True(
            OfficialWorldMovementCodec.IsVerifiedRequest(
                Convert.FromHexString("0A0080BAD7C34DA69488")));
    }

    [Theory]
    [InlineData("0A0080BAD7C34DA69489")]
    [InlineData("0A002E10000E0001FF72")]
    [InlineData("090080BAD7C34DA694")]
    public void Rejects_unverified_plaintext_or_malformed_frames(
        string frameHex)
    {
        Assert.False(
            OfficialWorldMovementCodec.IsVerifiedRequest(
                Convert.FromHexString(frameHex)));
    }
}
