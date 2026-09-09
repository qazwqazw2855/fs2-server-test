using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class LegacyPublicBetaPkWireTests
{
    [Fact]
    public void CursorTarget_RoundTripsAllLosslessFields()
    {
        var request = new LegacyPublicBetaPkCursorTargetRequest(
            254,
            242,
            Enumerable.Range(1, 16).Select(value => (byte)value).ToArray(),
            Enumerable.Range(17, 16).Select(value => (byte)value).ToArray(),
            new byte[] { 0xAA, 0xBB, 0xCC },
            3);

        var encoded = LegacyPublicBetaPkWireCodec.EncodeCursorTarget(request);
        var decoded = LegacyPublicBetaPkWireCodec.DecodeCursorTarget(encoded.Span);

        Assert.True(decoded.Succeeded);
        Assert.Equal(request.SourceActorId, decoded.Value!.SourceActorId);
        Assert.Equal(request.TargetActorId, decoded.Value.TargetActorId);
        Assert.Equal(request.SourceLabelRaw.ToArray(), decoded.Value.SourceLabelRaw.ToArray());
        Assert.Equal(request.TargetLabelRaw.ToArray(), decoded.Value.TargetLabelRaw.ToArray());
        Assert.Equal(request.ReservedRaw.ToArray(), decoded.Value.ReservedRaw.ToArray());
        Assert.Equal(request.Mode, decoded.Value.Mode);
    }

    [Fact]
    public void PetTarget_UsesSeparateB8Record()
    {
        var encoded = LegacyPublicBetaPkWireCodec.EncodePetTarget(0x12345678);
        var decoded = LegacyPublicBetaPkWireCodec.DecodePetTarget(encoded.Span);

        Assert.True(decoded.Succeeded);
        Assert.Equal((uint)0x12345678, decoded.Value);
        Assert.Equal("B878563412", Convert.ToHexString(encoded.Span));
    }

    [Fact]
    public void DerivedStats_DecodesFourModifiersFiveElementsAndFourCombatStats()
    {
        var record = new byte[LegacyPublicBetaPkWireCodec.PlayerDerivedStatsLength];
        record[0] = LegacyPublicBetaPkWireCodec.PlayerDerivedStatsOpcode;
        for (var index = 0; index < 15; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1 + index * 2), checked((ushort)(101 + index)));
        }

        var decoded = LegacyPublicBetaPkWireCodec.DecodePlayerDerivedStats(record);

        Assert.True(decoded.Succeeded);
        Assert.Equal((short)101, decoded.Value!.VitalityModifier);
        Assert.Equal((short)104, decoded.Value.SpeedModifier);
        Assert.Equal(new ushort[] { 105, 106, 107, 108, 109 }, decoded.Value.ElementsGoldWoodWaterFireEarth);
        Assert.Equal((ushort)112, decoded.Value.PhysicalDefense);
        Assert.Equal((ushort)113, decoded.Value.PhysicalAttack);
        Assert.Equal((ushort)114, decoded.Value.MagicalDefense);
        Assert.Equal((ushort)115, decoded.Value.MagicalAttack);

        var encoded = LegacyPublicBetaPkWireCodec.EncodePlayerDerivedStats(decoded.Value);
        Assert.Equal(record, encoded.ToArray());
    }
}
