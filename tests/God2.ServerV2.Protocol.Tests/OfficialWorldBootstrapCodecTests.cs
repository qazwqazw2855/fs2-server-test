using System.Buffers.Binary;
using System.Text;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialWorldBootstrapCodecTests
{
    [Fact]
    public void Projects_verified_character_into_player_spawn()
    {
        var payload = OfficialWorldBootstrapCodec.EncodeAfterFirstFollowUp(
            1,
            "test001",
            "Swordsman",
            "Female",
            "Appearance1");

        Assert.Equal(
            OfficialWorldBootstrapCodec.PayloadLength,
            payload.Length);

        var spawn =
            OfficialWorldBootstrapCodec.DecodeFirstPlayerSpawn(payload);

        try
        {
            Assert.Equal(
                OfficialWorldBootstrapCodec.PlayerSpawnFrameLength,
                BinaryPrimitives.ReadUInt16LittleEndian(spawn));

            Assert.Equal(
                OfficialWorldBootstrapCodec.PlayerSpawnOpcode,
                spawn[2]);

            Assert.Equal(
                "test001",
                Encoding.ASCII.GetString(
                    spawn,
                    OfficialWorldBootstrapCodec.PlayerNameOffset,
                    7));

            Assert.Equal(
                1u,
                BinaryPrimitives.ReadUInt32LittleEndian(
                    spawn.AsSpan(
                        OfficialWorldBootstrapCodec.PlayerIdOffset,
                        sizeof(uint))));

            Assert.Equal(
                OfficialLoginWireTransform.ComputeChecksum(spawn),
                spawn[^1]);
        }
        finally
        {
            Array.Clear(spawn);
            Array.Clear(payload);
        }
    }

    [Fact]
    public void Rejects_unverified_world_profile()
    {
        Assert.Throws<NotSupportedException>(() =>
            OfficialWorldBootstrapCodec.EncodeAfterFirstFollowUp(
                1,
                "test001",
                "Mage",
                "Female",
                "Appearance1"));
    }

    [Fact]
    public void Rejects_character_name_longer_than_verified_field()
    {
        Assert.Throws<ArgumentException>(() =>
            OfficialWorldBootstrapCodec.EncodeAfterFirstFollowUp(
                1,
                "abcdefghijkl",
                "Swordsman",
                "Female",
                "Appearance1"));
    }
}
