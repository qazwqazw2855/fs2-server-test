using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldLogoutCodecTests
{
    [Fact]
    public void Recognizes_evidence_verified_logout_request()
    {
        Assert.True(
            OfficialWorldLogoutCodec.IsVerifiedRequest(
                Convert.FromHexString("0500AC9D30")));
    }

    [Theory]
    [InlineData("0500AC9D31")]
    [InlineData("05003D09A5")]
    [InlineData("0400AC9D")]
    public void Rejects_unverified_or_malformed_frames(string frameHex)
    {
        Assert.False(
            OfficialWorldLogoutCodec.IsVerifiedRequest(
                Convert.FromHexString(frameHex)));
    }
}
