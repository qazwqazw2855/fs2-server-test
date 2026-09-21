using System.Buffers.Binary;
using System.Text;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialLoginSuccessCodecTests
{
    [Fact]
    public void Builds_verified_417_byte_server_group_bootstrap()
    {
        var loginRequest = BuildLoginRequest("kero", "secret");

        var encoded =
            OfficialLoginSuccessCodec.EncodeServerGroupBootstrap(
                loginRequest,
                [127, 0, 0, 1],
                2592);

        Assert.Equal(OfficialLoginSuccessCodec.FrameLength, encoded.Length);

        var decoded = CurrentClientServerWireTransform.Decode(encoded);

        Assert.Equal((byte)0xA1, decoded[0]);
        Assert.Equal((byte)0x01, decoded[1]);
        Assert.Equal(OfficialLoginSuccessCodec.Opcode, decoded[2]);
        Assert.Equal(
            CurrentClientServerWireTransform.ComputeChecksum(decoded),
            decoded[^1]);
    }

    [Fact]
    public void Echoes_login_fields_and_patches_all_server_endpoints()
    {
        var loginRequest = BuildLoginRequest("test-account", "test-password");

        var encoded =
            OfficialLoginSuccessCodec.EncodeServerGroupBootstrap(
                loginRequest,
                [10, 20, 30, 40],
                2592);

        var decoded = CurrentClientServerWireTransform.Decode(encoded);

        Assert.Equal(
            "test-account",
            ReadAscii(decoded.AsSpan(
                OfficialLoginRequestCodec.AccountOffset,
                OfficialLoginRequestCodec.AccountLength)));
        Assert.Equal(
            "test-password",
            ReadAscii(decoded.AsSpan(
                OfficialLoginRequestCodec.PasswordOffset,
                OfficialLoginRequestCodec.PasswordLength)));

        foreach (var offset in new[] { 215, 274, 333 })
        {
            Assert.Equal(
                new byte[] { 10, 20, 30, 40 },
                decoded.AsSpan(offset, 4).ToArray());
            Assert.Equal((byte)0x20, decoded[offset + 4]);
            Assert.Equal((byte)0x0A, decoded[offset + 5]);
        }
    }

    [Fact]
    public void Rejects_wrong_login_request_length()
    {
        Assert.Throws<ArgumentException>(
            () => OfficialLoginSuccessCodec.EncodeServerGroupBootstrap(
                new byte[207],
                [127, 0, 0, 1],
                2592));
    }

    [Fact]
    public void Rejects_non_ipv4_endpoint()
    {
        var loginRequest = BuildLoginRequest("kero", "secret");

        Assert.Throws<ArgumentException>(
            () => OfficialLoginSuccessCodec.EncodeServerGroupBootstrap(
                loginRequest,
                new byte[16],
                2592));
    }

    private static byte[] BuildLoginRequest(
        string account,
        string password)
    {
        var decoded =
            new byte[OfficialLoginRequestCodec.FrameLength];

        BinaryPrimitives.WriteUInt16LittleEndian(
            decoded,
            OfficialLoginRequestCodec.FrameLength);

        Encoding.ASCII.GetBytes(account).CopyTo(
            decoded,
            OfficialLoginRequestCodec.AccountOffset);
        Encoding.ASCII.GetBytes(password).CopyTo(
            decoded,
            OfficialLoginRequestCodec.PasswordOffset);

        decoded[^1] =
            OfficialLoginWireTransform.ComputeChecksum(decoded);

        var encoded = OfficialLoginWireTransform.Encode(decoded);
        Array.Clear(decoded);
        return encoded;
    }

    private static string ReadAscii(ReadOnlySpan<byte> field)
    {
        var terminator = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(
            terminator >= 0 ? field[..terminator] : field);
    }
}
