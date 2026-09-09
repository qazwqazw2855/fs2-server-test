using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class MagicSkillDamageRuntimeTests
{
    [Fact]
    public void ReproducesCraneAgainstMesmerMetalOneExampleWithoutHidingRange()
    {
        var engine = Engine();
        var result = engine.CalculateMagicSkillDamage(new MagicSkillDamageRequest(
            MagicAttack: 171,
            MagicDefense: 102,
            AttackElement: BattleElement.Metal,
            SkillTier: 1,
            AttackerElements: Elements(metal: 300),
            TargetElements: Elements(water: 415)));

        Assert.True(result.Succeeded);
        Assert.Equal(60m, result.Value!.AttackerSameElementBonus);
        Assert.Equal(129m, result.Value.BaseDamageBeforeSkillAndMeditation);
        Assert.Equal((129m, 141.9m, 154.8m),
            (result.Value.MinimumDamage, result.Value.MidpointDamage, result.Value.MaximumDamage));
        Assert.Equal((129L, 141L, 154L),
            (result.Value.SelectFinalDamage(SkillCoefficientSelection.Minimum),
             result.Value.SelectFinalDamage(SkillCoefficientSelection.Midpoint),
             result.Value.SelectFinalDamage(SkillCoefficientSelection.Maximum)));
        Assert.Equal("ProductionAccepted", result.Value.RegionCompatibilityStatus);
    }

    [Fact]
    public void AppliesTargetSameElementDefenseFromArticleExample()
    {
        var result = Engine().CalculateMagicSkillDamage(new MagicSkillDamageRequest(
            MagicAttack: 236,
            MagicDefense: 80,
            AttackElement: BattleElement.Metal,
            SkillTier: 1,
            AttackerElements: Elements(water: 415),
            TargetElements: Elements(metal: 300)));

        Assert.True(result.Succeeded);
        Assert.Equal(30m, result.Value!.TargetSameElementDefense);
        Assert.Equal(126m, result.Value.BaseDamageBeforeSkillAndMeditation);
        Assert.Equal(151.2m, result.Value.MaximumDamage);
    }

    [Fact]
    public void MetalTwoUsesMeasuredPointSevenToPointNineRange()
    {
        var result = Engine().CalculateMagicSkillDamage(new MagicSkillDamageRequest(
            MagicAttack: 236,
            MagicDefense: 102,
            AttackElement: BattleElement.Metal,
            SkillTier: 2,
            AttackerElements: Elements(),
            TargetElements: Elements()));

        Assert.True(result.Succeeded);
        Assert.Equal((93.8m, 107.2m, 120.6m),
            (result.Value!.MinimumDamage, result.Value.MidpointDamage, result.Value.MaximumDamage));
    }

    [Fact]
    public void XianDaoMeditationPassiveAddsToMatchingElementSkillCoefficient()
    {
        var withoutPassive = Engine().CalculateMagicSkillDamage(Request());
        var levelOne = Engine().CalculateMagicSkillDamage(Request(
            new XianDaoMeditationPassive(BattleElement.Metal, 1)));
        var levelTwo = Engine().CalculateMagicSkillDamage(Request(
            new XianDaoMeditationPassive(BattleElement.Metal, 2)));

        Assert.Equal(0m, withoutPassive.Value!.MeditationCoefficientBonus);
        Assert.Equal(0.05m, levelOne.Value!.MeditationCoefficientBonus);
        Assert.Equal(0.10m, levelTwo.Value!.MeditationCoefficientBonus);
        Assert.Equal(92m, levelOne.Value.MidpointDamage);
        Assert.Equal(96m, levelTwo.Value.MidpointDamage);
    }

    [Fact]
    public void XianDaoMeditationRejectsDifferentElementAndUnknownThirdLevel()
    {
        var wrongElement = Engine().CalculateMagicSkillDamage(Request(
            new XianDaoMeditationPassive(BattleElement.Fire, 1)));
        var unknownLevel = Engine().CalculateMagicSkillDamage(Request(
            new XianDaoMeditationPassive(BattleElement.Metal, 3)));

        Assert.Equal("battle.skill.magic_meditation_element_mismatch", wrongElement.Error.Code);
        Assert.Equal("battle.skill.magic_meditation_level_missing", unknownLevel.Error.Code);
    }

    [Fact]
    public void UnmeasuredElementOrTierFailsClosed()
    {
        var result = Engine().ResolveMagicSkillCoefficient(BattleElement.Fire, 1);

        Assert.False(result.Succeeded);
        Assert.Equal("battle.skill.magic_coefficient_missing", result.Error.Code);
    }

    private static MagicSkillDamageRequest Request(XianDaoMeditationPassive? passive = null) =>
        new(100, 20, BattleElement.Metal, 1, Elements(), Elements(), passive);

    private static MagicSkillDamageEngine Engine() =>
        new(new MagicSkillDamageCatalog([
            Rule(1, 1.0m, 1.1m, 1.2m),
            Rule(2, 0.7m, 0.8m, 0.9m)
        ]));

    private static MagicSkillDamageCoefficient Rule(
        int tier,
        decimal minimum,
        decimal midpoint,
        decimal maximum) =>
        new(
            BattleElement.Metal,
            tier,
            minimum,
            midpoint,
            maximum,
            5m,
            5m,
            5m,
            10m,
            1m,
            0.05m,
            0.10m,
            "ProductionCompatibilityAccepted",
            "ProductionAccepted",
            "https://forum.gamer.com.tw/G2.php?bsn=8395&parent=41&sn=1484",
            true);

    private static BattleElementStats Elements(
        decimal metal = 0,
        decimal wood = 0,
        decimal water = 0,
        decimal fire = 0,
        decimal earth = 0) =>
        new(metal, wood, water, fire, earth);
}
