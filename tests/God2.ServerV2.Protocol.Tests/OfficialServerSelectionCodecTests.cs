using System.Buffers.Binary;
using System.Text;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialServerSelectionCodecTests
{
    [Fact]
    public void Decodes_verified_server_selection_request()
    {
        var encoded = Convert.FromHexString("06009202CE97");

        var succeeded =
            OfficialServerSelectionCodec.TryDecodeRequest(
                encoded,
                out var request);

        Assert.True(succeeded);
        Assert.Equal(
            OfficialServerSelectionCodec.SupportedServerId,
            request.SelectedServerId);
    }

    [Fact]
    public void Encodes_single_swordsman_character()
    {
        var encoded =
            OfficialServerSelectionCodec.EncodeCharacterList(
                "test001",
                "Swordsman");

        Assert.Equal(
            OfficialServerSelectionCodec.ResponseFrameLength,
            encoded.Length);

        var decoded = OfficialLoginWireTransform.Decode(encoded);

        try
        {
            Assert.Equal(
                OfficialServerSelectionCodec.ResponseFrameLength,
                BinaryPrimitives.ReadUInt16LittleEndian(decoded));

            Assert.Equal(
                OfficialServerSelectionCodec.ResponseOpcode,
                decoded[2]);

            Assert.Equal(
                1,
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(3, 2)));

            Assert.Equal(
                "test001",
                Encoding.ASCII.GetString(
                    decoded,
                    OfficialServerSelectionCodec.CharacterNameOffset,
                    7));

            Assert.Equal(
                OfficialLoginWireTransform.ComputeChecksum(decoded),
                decoded[^1]);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    [Fact]
    public void Current_client_character_list_decodes_with_current_transform()
    {
        var encoded =
            OfficialServerSelectionCodec.EncodeCurrentClientCharacterList(
                "test001", "Swordsman");
        var decoded = CurrentClientServerWireTransform.Decode(encoded);

        try
        {
            Assert.Equal(OfficialServerSelectionCodec.ResponseFrameLength,
                decoded.Length);
            Assert.Equal(OfficialServerSelectionCodec.ResponseOpcode,
                decoded[2]);
            Assert.Equal(1,
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(3, 2)));
            Assert.Equal("test001",
                Encoding.ASCII.GetString(
                    decoded,
                    OfficialServerSelectionCodec.CharacterNameOffset,
                    7));
            Assert.Equal(
                CurrentClientServerWireTransform.ComputeChecksum(decoded),
                decoded[^1]);
        }
        finally
        {
            Array.Clear(encoded);
            Array.Clear(decoded);
        }
    }

    [Fact]
    public void Encodes_empty_character_list()
    {
        var encoded =
            OfficialServerSelectionCodec.EncodeCharacterList(null, null);

        var decoded = OfficialLoginWireTransform.Decode(encoded);

        try
        {
            Assert.Equal(
                0,
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(3, 2)));

            Assert.All(
                decoded[
                    OfficialServerSelectionCodec.CharacterNameOffset..
                    (OfficialServerSelectionCodec.CharacterNameOffset +
                     OfficialServerSelectionCodec.CharacterNameLength)],
                value => Assert.Equal(0, value));
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    [Fact]
    public void Rejects_unverified_class_profile()
    {
        Assert.Throws<NotSupportedException>(
            () => OfficialServerSelectionCodec.EncodeCharacterList(
                "test001",
                "Mage"));
    }
}
