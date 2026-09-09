using System.Buffers.Binary;
namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialImmortalReplicationWireCodecTests
{
    [Fact]
    public void Active_wuji_reuses_the_frozen_bootstrap_record_and_projects_the_verified_update()
    {
        var result = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord([Wuji(active: true)]);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.NotNull(result.Value);
        var update = result.Value;
        Assert.Equal(OfficialImmortalReplicationWireCodec.FrozenLoginApplicationRecordLength, update.Length);
        Assert.Equal(OfficialImmortalReplicationWireCodec.UpdateOpcode, update[0]);
        Assert.Equal(OfficialImmortalReplicationWireCodec.VerifiedLoginWujiResourceId, update[1]);
        Assert.Equal(1, update[2]);
        Assert.Equal(0, ReadUInt16(update, 3));
        Assert.Equal(10, ReadUInt16(update, 5));
        Assert.Equal(0, ReadUInt16(update, 7));
        Assert.Equal(0, ReadUInt16(update, 9));
        Assert.Equal(0, ReadUInt16(update, 11));
        Assert.Equal("04010000", Convert.ToHexString(update.AsSpan(13, 4)));
        Assert.Equal(30, ReadUInt16(update, 17));
        Assert.Equal(20, ReadUInt16(update, 19));
        Assert.Equal(10, ReadUInt16(update, 21));
        Assert.Equal(5, ReadUInt16(update, 23));
        Assert.All(update[25..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Active_wuji_projects_current_and_maximum_vitals_through_the_exact_opcode_39_record()
    {
        var result = OfficialImmortalReplicationWireCodec.BuildAuthoritativeVitalApplicationRecord([Wuji(active: true)]);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(
            "39900100009C000000900100009C000000",
            Convert.ToHexString(result.Value!));
    }

    [Fact]
    public void Empty_owned_set_emits_no_login_projection()
    {
        var result = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord([]);
        var vitals = OfficialImmortalReplicationWireCodec.BuildAuthoritativeVitalApplicationRecord([]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!);
        Assert.True(vitals.Succeeded);
        Assert.Empty(vitals.Value!);
    }

    [Fact]
    public void Unsupported_or_ambiguous_owned_sets_fail_closed()
    {
        var multipleRecords = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord([
            Wuji(active: true),
            Wuji(active: true) with { ImmortalInstanceId = 2 }
        ]);
        var wrongResourceDomain = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord([
            Wuji(active: true) with { ResourceId = 95 }
        ]);
        var inactive = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord([Wuji(active: false)]);
        var impossibleVitals = OfficialImmortalReplicationWireCodec.BuildAuthoritativeVitalApplicationRecord([
            Wuji(active: true) with { CurrentHp = 401 }
        ]);

        Assert.False(multipleRecords.Succeeded);
        Assert.False(wrongResourceDomain.Succeeded);
        Assert.False(inactive.Succeeded);
        Assert.False(impossibleVitals.Succeeded);
        Assert.Equal("immortal_projection.invalid_state", multipleRecords.Error?.Code);
        Assert.Equal("immortal_projection.invalid_state", impossibleVitals.Error?.Code);
    }

    private static OfficialOwnedImmortalWireState Wuji(bool active) => new(
        1,
        OfficialImmortalReplicationWireCodec.VerifiedLoginWujiResourceId,
        1,
        400,
        400,
        156,
        156,
        30,
        20,
        10,
        5,
        0,
        10,
        0,
        0,
        0,
        active);

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
}
