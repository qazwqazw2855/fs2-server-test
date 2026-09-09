using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class PetGrowthGradeRuntimeTests
{
    private readonly PetGrowthGradeCatalog _catalog = PetGrowthGradeCatalog.CreateVerifiedDefault();

    [Fact]
    public void CatalogContainsFourGradesAndTwelveVerifiedLevelBands()
    {
        Assert.Equal(12, _catalog.RuleCount);
        Assert.Equal(4, _catalog.Resolve(PetGrowthGrade.Normal, 19).Value!.AutomaticPointsPerLevel);
        Assert.Equal(5, _catalog.Resolve(PetGrowthGrade.Top, 19).Value!.AutomaticPointsPerLevel);
        Assert.Equal(10, _catalog.Resolve(PetGrowthGrade.LateBreakthrough, 50).Value!.AutomaticPointsPerLevel);
        Assert.Equal(10, _catalog.Resolve(PetGrowthGrade.Breakthrough, 99).Value!.AutomaticPointsPerLevel);
        Assert.Equal(1, _catalog.Resolve(PetGrowthGrade.Breakthrough, 50).Value!.ManualPointsPerLevel);
    }

    [Fact]
    public void InitialFourIsProvisionalNormalAndInitialFiveIsConfirmedTop()
    {
        var engine = new PetGrowthGradeEngine(_catalog);

        var normal = engine.ClassifyInitial(4);
        var top = engine.ClassifyInitial(5);

        Assert.Equal(PetGrowthGrade.Normal, normal.Value!.Grade);
        Assert.Equal(PetGrowthGradeStatus.Provisional, normal.Value.Status);
        Assert.Equal(PetGrowthGrade.Top, top.Value!.Grade);
        Assert.Equal(PetGrowthGradeStatus.Confirmed, top.Value.Status);
    }

    [Fact]
    public void NormalPetBreakingAtTwentyBecomesBreakthrough()
    {
        var engine = new PetGrowthGradeEngine(_catalog);
        var initial = engine.ClassifyInitial(4).Value!;

        var result = engine.ObserveLevelUp(initial, 20, 8);

        Assert.True(result.Succeeded);
        Assert.Equal(PetGrowthGrade.Breakthrough, result.Value!.Grade);
        Assert.Equal(20, result.Value.ConfirmedAtLevel);
    }

    [Fact]
    public void NormalPetBreakingAtFiftyBecomesLateBreakthrough()
    {
        var engine = new PetGrowthGradeEngine(_catalog);
        var initial = engine.ClassifyInitial(4).Value!;
        var levelTwenty = engine.ObserveLevelUp(initial, 20, 6).Value!;

        var result = engine.ObserveLevelUp(levelTwenty, 50, 10);

        Assert.True(result.Succeeded);
        Assert.Equal(PetGrowthGrade.LateBreakthrough, result.Value!.Grade);
        Assert.Equal(50, result.Value.ConfirmedAtLevel);
    }

    [Fact]
    public void NormalPetWithoutBreakthroughIsConfirmedAtFifty()
    {
        var engine = new PetGrowthGradeEngine(_catalog);
        var initial = engine.ClassifyInitial(4).Value!;
        var levelTwenty = engine.ObserveLevelUp(initial, 20, 6).Value!;

        var result = engine.ObserveLevelUp(levelTwenty, 50, 8);

        Assert.True(result.Succeeded);
        Assert.Equal(PetGrowthGrade.Normal, result.Value!.Grade);
        Assert.Equal(PetGrowthGradeStatus.Confirmed, result.Value.Status);
    }

    [Fact]
    public void TopPetCannotUseBreakthroughGrowthAtTwenty()
    {
        var engine = new PetGrowthGradeEngine(_catalog);
        var top = engine.ClassifyInitial(5).Value!;

        var result = engine.ObserveLevelUp(top, 20, 8);

        Assert.False(result.Succeeded);
        Assert.Equal("pet.growth.delta_mismatch", result.Error.Code);
    }

    [Fact]
    public void UnverifiedGrowthAboveLevelNinetyNineFailsClosed()
    {
        var result = _catalog.Resolve(PetGrowthGrade.Breakthrough, 100);

        Assert.False(result.Succeeded);
        Assert.Equal("pet.growth.rule_missing", result.Error.Code);
    }

    [Fact]
    public void LevelOneNormalFoxStartsFromFixedBaseAndAppliesFirstGrowthAllocation()
    {
        var allocation = new PetAutomaticGrowthAllocation(
            1,
            PetGrowthGrade.Normal,
            1,
            19,
            0,
            2,
            0,
            2,
            "BahamutVerified",
            "Bahamut SN 961",
            true);
        var catalog = new PetGrowthGradeCatalog(
            PetGrowthGradeCatalog.CreateVerifiedDefault().Rules,
            [allocation]);
        var result = new PetGrowthGradeEngine(catalog).CalculateCoreStats(1, PetGrowthGrade.Normal, 1);

        Assert.True(result.Succeeded);
        Assert.Equal(new PetCoreStats(100, 52, 50, 102), result.Value);
    }
}
