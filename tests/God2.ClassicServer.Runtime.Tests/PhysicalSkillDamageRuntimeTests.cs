using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class PhysicalSkillDamageRuntimeTests
{
    [Fact]
    public void ResolvesMinimumMidpointAndMaximumWithoutCollapsingMeasuredRange()
    {
        var engine = new PhysicalSkillDamageEngine(new PhysicalSkillDamageCatalog([
            Rule(PhysicalSkillFamily.Blade, 1, 1.5m, 1.8m, 2.1m, 2)
        ]));

        Assert.Equal(1.5m, engine.ResolvePhysicalSkillMultiplier(
            PhysicalSkillFamily.Blade, 1, SkillCoefficientSelection.Minimum).Value);
        Assert.Equal(1.8m, engine.ResolvePhysicalSkillMultiplier(
            PhysicalSkillFamily.Blade, 1).Value);
        Assert.Equal(2.1m, engine.ResolvePhysicalSkillMultiplier(
            PhysicalSkillFamily.Blade, 1, SkillCoefficientSelection.Maximum).Value);
    }

    [Fact]
    public void MissingFamilyOrTierFailsClosed()
    {
        var engine = new PhysicalSkillDamageEngine(new PhysicalSkillDamageCatalog([
            Rule(PhysicalSkillFamily.Sword, 1, 1.3m, 1.5m, 1.7m, 2)
        ]));

        var result = engine.ResolvePhysicalSkillMultiplier(PhysicalSkillFamily.Sword, 8);

        Assert.False(result.Succeeded);
        Assert.Equal("battle.skill.physical_coefficient_missing", result.Error.Code);
    }

    [Fact]
    public void InvalidMeasuredRangeIsRejectedDuringCatalogBuild()
    {
        Assert.Throws<InvalidOperationException>(() => new PhysicalSkillDamageCatalog([
            Rule(PhysicalSkillFamily.Staff, 1, 1.5m, 1.4m, 1.3m, 1)
        ]));
    }

    [Fact]
    public void ResolvedMidpointFeedsCompatibilityDamageFormula()
    {
        var engine = new PhysicalSkillDamageEngine(new PhysicalSkillDamageCatalog([
            Rule(PhysicalSkillFamily.Blade, 1, 1.5m, 1.8m, 2.1m, 2)
        ]));
        var multiplier = engine.ResolvePhysicalSkillMultiplier(PhysicalSkillFamily.Blade, 1).Value;
        var formula = new CompatibilityBattleFormula(CompatibilityFormulaCalibration.ApproximateDefault);

        var result = formula.Compute(new CompatibilityDamageRequest(
            Stats(1, physicalAttack: 100),
            Stats(2, physicalDefense: 20),
            CombatDamageType.Physical,
            multiplier,
            BattleElement.None,
            BattleElement.None));

        Assert.True(result.Succeeded);
        Assert.Equal(144, result.FinalDamage);
    }

    private static PhysicalSkillDamageCoefficient Rule(
        PhysicalSkillFamily family,
        int tier,
        decimal minimum,
        decimal midpoint,
        decimal maximum,
        int sources) =>
        new(
            family,
            tier,
            minimum,
            midpoint,
            maximum,
            sources == 2 ? "CommunityCorroborated" : "CommunityMeasured",
            sources,
            "https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=2481",
            sources == 2 ? "https://forum.gamer.com.tw/G2.php?bsn=8395&parent=40&sn=1794" : null,
            true);

    private static BattleParticipantStats Stats(
        long id,
        int? physicalAttack = null,
        int? physicalDefense = null) =>
        new(
            BattleParticipantEntityType.Character,
            id,
            Level: 1,
            CurrentHp: 100,
            MaximumHp: 100,
            CurrentMp: 100,
            MaximumMp: 100,
            Strength: null,
            Constitution: null,
            Intelligence: null,
            Speed: null,
            Metal: null,
            Wood: null,
            Water: null,
            Fire: null,
            Earth: null,
            physicalAttack,
            physicalDefense,
            MagicAttack: null,
            MagicDefense: null);
}
