using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class ClientSkillMetadataRuntimeTests
{
    [Fact]
    public void ResolvesOfficialMpRangeAndTargetScopeWithoutInterpretingEffectText()
    {
        var engine = new ClientSkillMetadataEngine(new ClientSkillMetadataCatalog([
            Metadata(6501, "劍一 蒼穹訣。劍氣", 5, 3, "一體")
        ], [(9001L, 6501)]));

        var result = engine.ResolveClientSkillMetadata(6501);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Value!.MpCost);
        Assert.Equal(3, result.Value.AttackRange);
        Assert.Equal("一體", result.Value.TargetScopeZhTw);
        Assert.Equal(ClientSkillTargetShape.Unknown, result.Value.TargetShape);
    }

    [Fact]
    public void ResolvesAllExactMappingsForFormalSkillInClientItemOrder()
    {
        var engine = new ClientSkillMetadataEngine(new ClientSkillMetadataCatalog([
            Metadata(6502, "劍一 蒼穹訣。劍氣", 10, 3, "一體"),
            Metadata(6501, "劍一 蒼穹訣。劍氣", 5, 3, "一體")
        ], [(9001L, 6502), (9001L, 6501)]));

        var metadata = engine.ResolveClientSkillMetadataForFormalSkill(9001L);

        Assert.Equal([6501, 6502], metadata.Select(value => value.OfficialClientItemId));
    }

    [Fact]
    public void MissingClientItemFailsClosed()
    {
        var engine = new ClientSkillMetadataEngine(new ClientSkillMetadataCatalog([], []));

        var result = engine.ResolveClientSkillMetadata(9999);

        Assert.False(result.Succeeded);
        Assert.Equal("skill.client_metadata_missing", result.Error.Code);
    }

    [Fact]
    public void NegativeMpOrRangeIsRejectedDuringCatalogBuild()
    {
        Assert.Throws<InvalidOperationException>(() => new ClientSkillMetadataCatalog([
            Metadata(6501, "劍一 蒼穹訣。劍氣", -1, 3, "一體")
        ], []));
        Assert.Throws<InvalidOperationException>(() => new ClientSkillMetadataCatalog([
            Metadata(6501, "劍一 蒼穹訣。劍氣", 5, -1, "一體")
        ], []));
    }

    [Fact]
    public void LiveVerifiedSingleTargetShapeIsExposedWithoutInventingTargetSideOrDamage()
    {
        var metadata = Metadata(6501, "劍一 蒼穹訣。劍氣", 5, 3, "一體") with
        {
            TargetShape = ClientSkillTargetShape.SingleTarget,
            TargetShapeEvidenceStatus = "OfficialClientLiveVerified",
            LiveVerified = true
        };
        var result = new ClientSkillMetadataEngine(new ClientSkillMetadataCatalog([metadata], []))
            .ResolveClientSkillMetadata(6501);

        Assert.True(result.Succeeded);
        Assert.Equal(ClientSkillTargetShape.SingleTarget, result.Value!.TargetShape);
        Assert.Equal("OfficialClientLiveVerified", result.Value.TargetShapeEvidenceStatus);
    }

    [Fact]
    public void SingleTargetShapeWithoutLiveVerificationIsRejected()
    {
        var metadata = Metadata(6501, "劍一 蒼穹訣。劍氣", 5, 3, "一體") with
        {
            TargetShape = ClientSkillTargetShape.SingleTarget,
            TargetShapeEvidenceStatus = "OfficialClientLiveVerified"
        };

        Assert.Throws<InvalidOperationException>(() => new ClientSkillMetadataCatalog([metadata], []));
    }

    private static ClientSkillMetadata Metadata(
        int itemId,
        string name,
        int? mp,
        int? range,
        string? scope) =>
        new(itemId, itemId, name, "主動技能", "劍技", 1, mp, range, scope,
            ClientSkillTargetShape.Unknown, "EvidenceBlocked",
            "官方效果文字", "OfficialClientStatic", false);
}
