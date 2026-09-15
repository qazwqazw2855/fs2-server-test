using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldHeartbeatCodecTests
{
    [Theory]
    [InlineData("05003D09A5")]
    [InlineData("05007ACCEC")]
    public void Classify_accepts_evidence_verified_keepalive_samples(
        string frameHex)
    {
        var classification = OfficialWorldHeartbeatCodec.Classify(
            Convert.FromHexString(frameHex));

        Assert.Equal(
            OfficialWorldFrameClassification.KeepAlive,
            classification);
    }

    [Fact]
    public void Classify_rejects_unobserved_five_byte_frame()
    {
        var classification = OfficialWorldHeartbeatCodec.Classify(
            Convert.FromHexString("0500000000"));

        Assert.Equal(
            OfficialWorldFrameClassification.Unknown,
            classification);
    }

    [Theory]
    [InlineData("04003D09")]
    [InlineData("05003D09")]
    public void Classify_rejects_invalid_length_or_envelope(string frameHex)
    {
        var classification = OfficialWorldHeartbeatCodec.Classify(
            Convert.FromHexString(frameHex));

        Assert.Equal(
            OfficialWorldFrameClassification.Unknown,
            classification);
    }
}
