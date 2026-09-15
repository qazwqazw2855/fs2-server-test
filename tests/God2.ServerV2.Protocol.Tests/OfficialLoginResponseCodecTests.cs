using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialLoginResponseCodecTests
{
    [Theory]
    [InlineData(OfficialLoginFailureCode.AccountNotRegistered, 0)]
    [InlineData(OfficialLoginFailureCode.DuplicateLogin, 1)]
    [InlineData(OfficialLoginFailureCode.AccountStopped, 2)]
    [InlineData(OfficialLoginFailureCode.CredentialsRejected, 3)]
    [InlineData(OfficialLoginFailureCode.NetworkDisconnected, 4)]
    [InlineData(OfficialLoginFailureCode.IllegalCharacter, 5)]
    [InlineData(OfficialLoginFailureCode.VersionMismatch, 6)]
    [InlineData(OfficialLoginFailureCode.EntitlementUnavailable, 7)]
    [InlineData(OfficialLoginFailureCode.ServerFull, 9)]
    public void Encodes_official_failure_frame(
        OfficialLoginFailureCode failureCode,
        byte expectedWireCode)
    {
        var encoded =
            OfficialLoginResponseCodec.EncodeFailure(failureCode);

        Assert.Equal(
            OfficialLoginResponseCodec.FailureFrameLength,
            encoded.Length);

        var decoded =
            OfficialLoginWireTransform.Decode(encoded);

        Assert.Equal((byte)5, decoded[0]);
        Assert.Equal((byte)0, decoded[1]);
        Assert.Equal((byte)0x1E, decoded[2]);
        Assert.Equal(expectedWireCode, decoded[3]);
        Assert.Equal(
            OfficialLoginWireTransform.ComputeChecksum(decoded),
            decoded[^1]);
    }

    [Fact]
    public void Credentials_rejected_matches_verified_plain_frame_shape()
    {
        var encoded =
            OfficialLoginResponseCodec.EncodeFailure(
                OfficialLoginFailureCode.CredentialsRejected);

        var decoded =
            OfficialLoginWireTransform.Decode(encoded);

        Assert.Equal("05001E0316", Convert.ToHexString(decoded));
    }
}
