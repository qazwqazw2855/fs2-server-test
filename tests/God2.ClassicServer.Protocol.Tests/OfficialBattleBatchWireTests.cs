using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialBattleBatchWireTests
{
    private const string JoinerBootstrap =
        "6C0182010000000214000086041901110101201102310000000000001C1957E90008240000CDF5D7DCB2C300000000000000000000000000000000000000000000000000000000000186040601110001601100510000000000001C060DFE00023800006B65726F000000000000000000000000000000000000000000000000000000000000010186040701200100C02000200000000000001C07A8F20001340200CFC4B1A6D8BC00000000000000000000476F2030000000000000000000000000000001011C0F2FE940134CD7004C763A34370000000000000000000000000000000000000000000000000000000000000088050000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005D010000580000002A1000003F0700000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000050";

    [Fact]
    public void DecodeAndEncode_ControlledJoinerBootstrap_RoundTripsExactly()
    {
        var frame = Convert.FromHexString(JoinerBootstrap);
        var decoded = OfficialBattleBatchWireCodec.DecodeLayout(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(decoded.LayoutDecoded);
        Assert.NotNull(decoded.Value);
        Assert.Equal((byte)0x82, decoded.Value.OuterOpcode);
        Assert.Equal("0100000002140000", Convert.ToHexString(decoded.Value.BootstrapHeader.Span));
        Assert.Equal(8, decoded.Value.Records.Count);
        Assert.Equal([0x86, 0x1C, 0x86, 0x1C, 0x86, 0x1C, 0x1C, 0x88], decoded.Value.Records.Select(row => row.Opcode));
        Assert.False(decoded.Value.RuntimeSerializerAllowed);

        var encoded = OfficialBattleBatchWireCodec.EncodeLayout(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decoded.Value.OuterOpcode,
            decoded.Value.BootstrapHeader.Span,
            decoded.Value.Records);

        Assert.True(encoded.LayoutDecoded);
        Assert.Equal(JoinerBootstrap, Convert.ToHexString(encoded.Value!.Span));

        var typed = OfficialBattleBatchWireCodec.DecodeTypedLayout(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame);
        Assert.True(typed.LayoutDecoded);
        Assert.True(typed.Value!.FullyTyped);
        Assert.Equal(4, typed.Value.Rosters.Count);
        Assert.Equal(3, typed.Value.Controls.Count);
        Assert.Single(typed.Value.VitalSnapshots);
        Assert.Empty(typed.Value.Effects);
        Assert.Contains(typed.Value.Rosters, row => row.EntityId == 254 && row.DisplayLevel == 13);
    }

    [Fact]
    public void IncrementalBatch_RoundTripsKnownInnerRecords()
    {
        var control = Convert.FromHexString("8604190081810000000000000000000000");
        var roster = Convert.FromHexString("1C060DFE00023800006B65726F0000000000000000000000000000000000000000000000000000000000000101");
        var records = new[]
        {
            new OfficialBattleInnerRecord(0x86, control),
            new OfficialBattleInnerRecord(0x1C, roster)
        };

        var encoded = OfficialBattleBatchWireCodec.EncodeLayout(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            0x86,
            ReadOnlySpan<byte>.Empty,
            records);

        Assert.True(encoded.LayoutDecoded);
        var decoded = OfficialBattleBatchWireCodec.DecodeLayout(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            encoded.Value!.Span);
        Assert.True(decoded.LayoutDecoded);
        Assert.Equal([0x86, 0x1C], decoded.Value!.Records.Select(row => row.Opcode));
    }

    [Fact]
    public void RuntimeSerializer_IsFailClosed()
    {
        var decoded = OfficialBattleBatchWireCodec.DecodeLayout(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Convert.FromHexString(JoinerBootstrap));
        var result = OfficialBattleBatchWireCodec.EncodeForRuntime(
            OfficialBattleBatchWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            0x82,
            decoded.Value!.BootstrapHeader.Span,
            decoded.Value.Records);

        Assert.Equal(OfficialBattleBatchWireResultCode.SerializerEvidenceBlocked, result.Code);
    }
}
