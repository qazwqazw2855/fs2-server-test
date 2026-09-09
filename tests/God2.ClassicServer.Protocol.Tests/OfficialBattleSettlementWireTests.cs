using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialBattleSettlementWireTests
{
    private const string ControlledSettlement =
        "890000000049000000490000000300000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void CurrentSettlement_DecodesVerifiedExperienceAndRoundTripsExactly()
    {
        var decoded = OfficialBattleSettlementWireCodec.DecodeLayout(
            OfficialBattleSettlementWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            Convert.FromHexString(ControlledSettlement));

        Assert.True(decoded.LayoutDecoded);
        Assert.Equal((uint)73, decoded.Value!.ExperienceBase);
        Assert.Equal((uint)73, decoded.Value.ExperienceCredited);
        Assert.Equal((uint)3, decoded.Value.RawSummaryValue2);
        Assert.Equal((uint)0, decoded.Value.RawSummaryValue3);
        Assert.Equal(4, decoded.Value.Entries.Count);
        Assert.Equal((uint)0, decoded.Value.PresentationKind);
        Assert.False(decoded.Value.RuntimeMutationAllowed);

        var encoded = OfficialBattleSettlementWireCodec.EncodeLayout(
            OfficialBattleSettlementWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            new OfficialBattleSettlementLayout(
                decoded.Value.RawCaseValue,
                decoded.Value.ExperienceBase,
                decoded.Value.ExperienceCredited,
                decoded.Value.RawSummaryValue2,
                decoded.Value.RawSummaryValue3,
                decoded.Value.Entries,
                decoded.Value.PresentationKind));

        Assert.True(encoded.LayoutEncoded);
        Assert.Equal(ControlledSettlement, Convert.ToHexString(encoded.Value!));
    }

    [Fact]
    public void CurrentSettlement_RuntimeSerializerRemainsAuthorityGated()
    {
        var layout = new OfficialBattleSettlementLayout(
            0,
            73,
            73,
            3,
            0,
            Enumerable.Repeat(new OfficialBattleSettlementEntry(0, 0), 4).ToArray(),
            0);

        var result = OfficialBattleSettlementWireCodec.EncodeForRuntime(
            OfficialBattleSettlementWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            layout);

        Assert.Equal(OfficialBattleSettlementWireResultCode.SemanticEvidenceBlocked, result.Code);
        Assert.Null(result.Value);
    }
}
