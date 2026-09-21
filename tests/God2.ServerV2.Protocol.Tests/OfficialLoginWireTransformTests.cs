using System.Buffers.Binary;
using System.Text;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialLoginWireTransformTests
{
    [Fact]
    public void Decodes_verified_official_version_follow_up()
    {
        var encoded =
            Convert.FromHexString("0600ED7CEE11");

        var decoded =
            OfficialLoginWireTransform.Decode(encoded);

        Assert.Equal(
            "060001210054",
            Convert.ToHexString(decoded));
    }

    [Fact]
    public void Encodes_verified_official_version_follow_up()
    {
        var decoded =
            Convert.FromHexString("060001210054");

        var encoded =
            OfficialLoginWireTransform.Encode(decoded);

        Assert.Equal(
            "0600ED7CEE11",
            Convert.ToHexString(encoded));
    }

    [Fact]
    public void Decodes_synthetic_208_byte_login_request()
    {
        var encoded = BuildLoginRequest(
            "kero",
            "secret");

        var succeeded =
            OfficialLoginRequestCodec.TryDecode(
                encoded,
                out var request);

        Assert.True(succeeded);
        Assert.NotNull(request);

        using (request)
        {
            Assert.Equal("kero", request.AccountName);
            Assert.Equal(
                "secret",
                new string(request.Password.Span));
        }
    }

    [Fact]
    public void Accepts_login_request_when_generic_checksum_does_not_match()
    {
        var encoded = BuildLoginRequest(
            "kero",
            "secret");

        var decoded =
            OfficialLoginWireTransform.Decode(encoded);

        decoded[^1] ^= 0x01;

        Assert.NotEqual(
            OfficialLoginWireTransform.ComputeChecksum(decoded),
            decoded[^1]);

        encoded =
            OfficialLoginWireTransform.Encode(decoded);

        var succeeded =
            OfficialLoginRequestCodec.TryDecode(
                encoded,
                out var request);

        Assert.True(succeeded);
        Assert.NotNull(request);

        using (request)
        {
            Assert.Equal("kero", request.AccountName);
            Assert.Equal(
                "secret",
                new string(request.Password.Span));
        }
    }

    [Fact]
    public void Rejects_wrong_login_frame_length()
    {
        Assert.False(
            OfficialLoginRequestCodec.TryDecode(
                new byte[207],
                out var request));
        Assert.Null(request);
    }

    [Fact]
    public void Dispose_clears_password_memory()
    {
        var encoded = BuildLoginRequest(
            "kero",
            "secret");

        OfficialLoginRequestCodec.TryDecode(
            encoded,
            out var request);

        Assert.NotNull(request);
        var passwordMemory = request.Password;

        request.Dispose();

        Assert.True(
            passwordMemory.Span.IndexOfAnyExcept('\0') < 0);
        Assert.Throws<ObjectDisposedException>(
            () => _ = request.Password);
    }

    private static byte[] BuildLoginRequest(
        string account,
        string password)
    {
        var decoded = new byte[
            OfficialLoginRequestCodec.FrameLength];

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
            OfficialLoginWireTransform.ComputeChecksum(
                decoded);

        var encoded =
            OfficialLoginWireTransform.Encode(decoded);

        Array.Clear(decoded);
        return encoded;
    }
}
