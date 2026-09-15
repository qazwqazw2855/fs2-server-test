using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldMovementCodecTests
{
    [Theory]
    [InlineData("0A0080BAD7C34DA69488")]
    [InlineData("0A0080BAD7C44EA79586")]
    public void Recognizes_recovered_movement_samples(string frameHex)
    {
        Assert.True(
            OfficialWorldMovementCodec.IsVerifiedRequest(
                Convert.FromHexString(frameHex)));
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
