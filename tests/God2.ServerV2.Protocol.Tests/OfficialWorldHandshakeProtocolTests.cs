using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldHandshakeProtocolTests
{
    [Fact]
    public void Exposes_verified_world_handshake_sequence()
    {
        Assert.Equal(
            "1300FD9FCAC842A869FEA1BEC83A23DACF5249",
            Convert.ToHexString(
                OfficialWorldHandshakeProtocol.ServerHandshakeFrame.Span));

        Assert.Equal(
            "1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962",
            Convert.ToHexString(
                OfficialWorldHandshakeProtocol
                    .ExpectedClientHandshakeFrame.Span));

        Assert.Equal(
            "0600A96DB7E8",
            Convert.ToHexString(
                OfficialWorldHandshakeProtocol.FirstFollowUpFrame.Span));
    }

    [Fact]
    public void Accepts_verified_world_client_handshake()
    {
        Assert.True(
            OfficialWorldHandshakeProtocol.IsExpectedClientHandshake(
                OfficialWorldHandshakeProtocol
                    .ExpectedClientHandshakeFrame.Span));
    }

    [Fact]
    public void Rejects_modified_world_client_handshake()
    {
        var modified = OfficialWorldHandshakeProtocol
            .ExpectedClientHandshakeFrame
            .ToArray();

        modified[^1] ^= 0x01;

        Assert.False(
            OfficialWorldHandshakeProtocol.IsExpectedClientHandshake(
                modified));
    }
}
