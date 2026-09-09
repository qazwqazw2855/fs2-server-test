using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class CompatibilityBattleFormulaTests
{
    [Theory]
    [InlineData(44, 14, false, false, 14, 30)]
    [InlineData(44, 14, true, false, 21, 23)]
    [InlineData(34, 15, false, false, 15, 19)]
    [InlineData(34, 15, true, false, 22, 12)]
    [InlineData(44, 14, false, true, 14, 60)]
    [InlineData(44, 14, true, true, 21, 46)]
    [InlineData(34, 15, false, true, 15, 38)]
    [InlineData(34, 15, true, true, 22, 24)]
    public void Derived_baseline_basic_physical_attack_formula_remains_non_authoritative(
        long physicalAttack,
        long physicalDefense,
        bool isDefending,
        bool isCritical,
        long expectedEffectiveDefense,
        long expectedDamage)
    {
        var result = BaselineBasicPhysicalAttackFormula.Compute(
            physicalAttack,
            physicalDefense,
            isDefending,
            isCritical);

        Assert.Equal(expectedEffectiveDefense, result.EffectiveDefense);
        Assert.Equal(expectedDamage, result.FinalDamage);
        Assert.Equal(
            "DerivedBaseline_NotOfficial",
            BaselineBasicPhysicalAttackFormula.EvidenceConfidence);
        Assert.False(BaselineBasicPhysicalAttackFormula.ProductionAuthoritative);
        Assert.Equal(2, BaselineBasicPhysicalAttackFormula.CriticalDamageMultiplier);
        Assert.Equal(1.5m, BaselineBasicPhysicalAttackFormula.DefendingDefenseMultiplier);
    }

    private static readonly CompatibilityFormulaCalibration Calibration =
        CompatibilityFormulaCalibration.ApproximateDefault;

    [Theory]
    [InlineData(BattleElement.Metal, BattleElement.Wood, FiveElementRelationship.Overcomes)]
    [InlineData(BattleElement.Metal, BattleElement.Fire, FiveElementRelationship.OvercomeBy)]
    [InlineData(BattleElement.Metal, BattleElement.Water, FiveElementRelationship.Generates)]
    [InlineData(BattleElement.Metal, BattleElement.Earth, FiveElementRelationship.GeneratedBy)]
    [InlineData(BattleElement.Metal, BattleElement.Metal, FiveElementRelationship.Neutral)]
    [InlineData(BattleElement.None, BattleElement.Wood, FiveElementRelationship.Neutral)]
    public void Resolves_all_five_element_relationships(
        BattleElement attack,
        BattleElement defense,
        FiveElementRelationship expected)
    {
        Assert.Equal(expected, CompatibilityBattleFormula.ResolveRelationship(attack, defense));
    }

    [Fact]
    public void Computes_physical_damage_with_separate_element_adjustment()
    {
        var formula = new CompatibilityBattleFormula(Calibration);
        var result = formula.Compute(Request(
            physicalAttack: 100,
            physicalDefense: 20,
            attackElement: BattleElement.Metal,
            defenseElement: BattleElement.Wood,
            skillMultiplier: 2m));

        Assert.True(result.Succeeded);
        Assert.Equal(FiveElementRelationship.Overcomes, result.ElementRelationship);
        Assert.Equal(150m, result.AttackAfterElement);
        Assert.Equal(20m, result.DefenseAfterElement);
        Assert.Equal(260, result.FinalDamage);
        Assert.Equal(CombatPolicyStatus.Baseline, result.PolicyStatus);
    }

    [Fact]
    public void Computes_magic_damage_against_magic_defense()
    {
        var formula = new CompatibilityBattleFormula(Calibration);
        var result = formula.Compute(Request(
            magicAttack: 80,
            magicDefense: 30,
            damageType: CombatDamageType.Magic,
            skillMultiplier: 1.25m));

        Assert.True(result.Succeeded);
        Assert.Equal(62, result.FinalDamage);
        Assert.Equal(80m, result.AttackBeforeElement);
        Assert.Equal(30m, result.DefenseBeforeElement);
    }

    [Fact]
    public void Defending_applies_one_point_five_multiplier_to_magic_defense()
    {
        var formula = new CompatibilityBattleFormula(Calibration);
        var result = formula.Compute(Request(
            magicAttack: 80,
            magicDefense: 31,
            damageType: CombatDamageType.Magic,
            isDefending: true));

        Assert.True(result.Succeeded);
        Assert.Equal(46m, result.DefenseBeforeElement);
        Assert.Equal(34, result.FinalDamage);
    }

    [Fact]
    public void Applies_random_and_critical_after_defense_reduction()
    {
        var formula = new CompatibilityBattleFormula(Calibration);
        var result = formula.Compute(Request(
            physicalAttack: 100,
            physicalDefense: 40,
            randomMultiplier: 0.9m,
            isCritical: true,
            criticalMultiplier: 1.5m));

        Assert.True(result.Succeeded);
        Assert.Equal(81, result.FinalDamage);
    }

    [Theory]
    [InlineData(44, 14, 30, 23)]
    [InlineData(34, 15, 19, 12)]
    public void Verified_player_basic_attacks_subtract_defense_and_defending_adds_half_defense_truncated(
        int physicalAttack,
        int physicalDefense,
        long expectedNormalDamage,
        long expectedDefendingDamage)
    {
        var formula = new CompatibilityBattleFormula(Calibration);

        var normal = formula.Compute(Request(
            physicalAttack: physicalAttack,
            physicalDefense: physicalDefense));
        var defending = formula.Compute(Request(
            physicalAttack: physicalAttack,
            physicalDefense: physicalDefense,
            isDefending: true));

        Assert.True(normal.Succeeded);
        Assert.True(defending.Succeeded);
        Assert.Equal(expectedNormalDamage, normal.FinalDamage);
        Assert.Equal(expectedDefendingDamage, defending.FinalDamage);
        Assert.Equal(physicalDefense + (physicalDefense / 2), defending.DefenseBeforeElement);
    }

    [Theory]
    [InlineData(44, 14, false, 60)]
    [InlineData(44, 14, true, 46)]
    [InlineData(34, 15, false, 38)]
    [InlineData(34, 15, true, 24)]
    public void Verified_basic_attack_critical_is_twice_the_final_noncritical_damage(
        int physicalAttack,
        int physicalDefense,
        bool isDefending,
        long expectedDamage)
    {
        var result = new CompatibilityBattleFormula(Calibration).Compute(Request(
            physicalAttack: physicalAttack,
            physicalDefense: physicalDefense,
            isCritical: true,
            criticalMultiplier: 2m,
            isDefending: isDefending));

        Assert.True(result.Succeeded);
        Assert.Equal(expectedDamage, result.FinalDamage);
    }

    [Fact]
    public void Blocks_non_neutral_element_damage_when_coefficient_is_unknown()
    {
        var formula = new CompatibilityBattleFormula(CompatibilityFormulaCalibration.EvidenceBlocked);
        var result = formula.Compute(Request(
            attackElement: BattleElement.Earth,
            defenseElement: BattleElement.Water));

        Assert.False(result.Succeeded);
        Assert.Equal("battle.formula.element_calibration_missing", result.FailureCode);
        Assert.Equal(CombatPolicyStatus.EvidenceBlocked, result.PolicyStatus);
    }

    [Fact]
    public void Blocks_formula_when_required_stat_is_unknown()
    {
        var formula = new CompatibilityBattleFormula(Calibration);
        var result = formula.Compute(Request(physicalAttack: null));

        Assert.False(result.Succeeded);
        Assert.Equal("battle.formula.required_stat_unknown", result.FailureCode);
    }

    [Fact]
    public void Higher_speed_acts_first_when_there_is_no_tie()
    {
        var result = CompatibilityBattleFormula.ResolveInitiative([
            Stats(1, speed: 100),
            Stats(2, speed: 130),
            Stats(3, speed: 80)
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal([2L, 1L, 3L], result.OrderedEntityIds);
        Assert.Equal(CombatPolicyStatus.Baseline, result.PolicyStatus);
    }

    [Fact]
    public void Equal_speed_uses_stable_entity_id_fallback()
    {
        var result = CompatibilityBattleFormula.ResolveInitiative([
            Stats(1, speed: 100),
            Stats(2, speed: 100)
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal([1L, 2L], result.OrderedEntityIds);
        Assert.Equal(CombatPolicyStatus.Baseline, result.PolicyStatus);
    }

    private static CompatibilityDamageRequest Request(
        int? physicalAttack = 100,
        int? physicalDefense = 20,
        int? magicAttack = 70,
        int? magicDefense = 10,
        CombatDamageType damageType = CombatDamageType.Physical,
        decimal skillMultiplier = 1m,
        BattleElement attackElement = BattleElement.None,
        BattleElement defenseElement = BattleElement.None,
        decimal randomMultiplier = 1m,
        bool isCritical = false,
        decimal criticalMultiplier = 1m,
        bool isDefending = false) =>
        new(
            Stats(1, physicalAttack: physicalAttack, magicAttack: magicAttack),
            Stats(2, physicalDefense: physicalDefense, magicDefense: magicDefense),
            damageType,
            skillMultiplier,
            attackElement,
            defenseElement,
            randomMultiplier,
            isCritical,
            criticalMultiplier,
            isDefending);

    private static BattleParticipantStats Stats(
        long id,
        int? speed = null,
        int? physicalAttack = null,
        int? physicalDefense = null,
        int? magicAttack = null,
        int? magicDefense = null) =>
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
            speed,
            Metal: null,
            Wood: null,
            Water: null,
            Fire: null,
            Earth: null,
            physicalAttack,
            physicalDefense,
            magicAttack,
            magicDefense);
}
