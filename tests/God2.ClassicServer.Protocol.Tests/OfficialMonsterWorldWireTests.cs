using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialMonsterWorldWireTests
{
    [Theory]
    [InlineData("718B0000820200000379030C03DE00860100002200", 139, 2, 0, 4, 222, 390)]
    [InlineData("71D60000620F000000D103D201F400E90000003B00", 214, 15, 0, 7, 244, 233)]
    [InlineData("71ED0000A20200203D15041C0305018E0100004900", 237, 2, 0, 9, 261, 398)]
    public void CurrentMonsterWorldRecord_DecodesIdentityPositionAndRoundTrips(
        string hex,
        ushort objectId,
        byte island,
        ushort area,
        ushort entry,
        ushort x,
        ushort y)
    {
        var decoded = OfficialMonsterWorldWireCodec.DecodeLayout(
            OfficialMonsterWorldWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Convert.FromHexString(hex));

        Assert.True(decoded.LayoutDecoded);
        Assert.Equal(objectId, decoded.Value!.Layout.ObjectId);
        Assert.Equal(island, decoded.Value.Layout.IslandIndex);
        Assert.Equal(area, decoded.Value.Layout.AreaId);
        Assert.Equal(entry, decoded.Value.Layout.AreaEntryIndex);
        Assert.Equal(x, decoded.Value.Layout.MapX);
        Assert.Equal(y, decoded.Value.Layout.MapY);
        Assert.False(decoded.Value.RuntimeMutationAllowed);

        var encoded = OfficialMonsterWorldWireCodec.EncodeLayout(
            OfficialMonsterWorldWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decoded.Value.Layout);
        Assert.True(encoded.LayoutEncoded);
        Assert.Equal(hex, Convert.ToHexString(encoded.Value!));
    }

    [Fact]
    public void CoordinateDuplicateMismatch_FailsClosed()
    {
        var record = Convert.FromHexString("718B0000820200000379030C03DE00860100002200");
        record[13] ^= 1;

        var result = OfficialMonsterWorldWireCodec.DecodeLayout(
            OfficialMonsterWorldWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            record);

        Assert.Equal(OfficialMonsterWorldWireResultCode.InvalidCoordinate, result.Code);
    }
}
