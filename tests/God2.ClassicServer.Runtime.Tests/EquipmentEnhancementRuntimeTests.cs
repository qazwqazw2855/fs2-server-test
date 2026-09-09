using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class EquipmentEnhancementRuntimeTests
{
    [Fact]
    public void GeneralEnhancementIsSafeThroughPlusThreeAndConsumesDurabilityOnSuccess()
    {
        var engine = Engine(Material(EquipmentEnhancementGrade.General, EquipmentEnhancementTargetType.Weapon, tier: 3, 1, 1, 3, EquipmentEnhancementFailurePolicy.DestroyTarget));
        var target = Target(EquipmentEnhancementTargetType.Weapon, tier: 3, level: 2, currentDurability: 40, maximumDurability: 50);

        var result = engine.Evaluate(100, target, successRollBasisPoints: 9_999, incrementRoll: 0);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.EnhancementSucceeded);
        Assert.Equal(3, result.Value.UpdatedTarget.EnhancementLevel);
        Assert.Equal(47, result.Value.UpdatedTarget.MaximumDurability);
        Assert.Equal(40, result.Value.UpdatedTarget.CurrentDurability);
        Assert.Equal(3, result.Value.UpdatedTarget.MaximumDurabilityPenalty);
    }

    [Fact]
    public void GeneralFailureAfterSafeLevelDestroysTarget()
    {
        var engine = Engine(Material(EquipmentEnhancementGrade.General, EquipmentEnhancementTargetType.Weapon, tier: 4, 1, 1, 3, EquipmentEnhancementFailurePolicy.DestroyTarget));

        var result = engine.Evaluate(100, Target(EquipmentEnhancementTargetType.Weapon, tier: 4, level: 3), 8_000, 0);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.EnhancementSucceeded);
        Assert.True(result.Value.TargetDestroyed);
        Assert.Equal(8_000, result.Value.SuccessRateBasisPoints);
    }

    [Fact]
    public void AdvancedFailurePreservesTarget()
    {
        var engine = Engine(Material(EquipmentEnhancementGrade.Advanced, EquipmentEnhancementTargetType.Equipment, tier: 5, 1, 2, 2, EquipmentEnhancementFailurePolicy.PreserveTarget));
        var target = Target(EquipmentEnhancementTargetType.Equipment, tier: 5, level: 4);

        var result = engine.Evaluate(100, target, 8_500, 1);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.EnhancementSucceeded);
        Assert.False(result.Value.TargetDestroyed);
        Assert.Equal(target, result.Value.UpdatedTarget);
    }

    [Fact]
    public void SpecialEnhancementAlwaysSucceedsAndCanAddTwoWithoutDurabilityLoss()
    {
        var engine = Engine(Material(EquipmentEnhancementGrade.Special, EquipmentEnhancementTargetType.Equipment, tier: 8, 1, 2, 0, EquipmentEnhancementFailurePolicy.PreserveTarget));

        var result = engine.Evaluate(100, Target(EquipmentEnhancementTargetType.Equipment, tier: 8, level: 8), 9_999, 1);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.EnhancementSucceeded);
        Assert.Equal(2, result.Value.AppliedIncrement);
        Assert.Equal(10, result.Value.UpdatedTarget.EnhancementLevel);
        Assert.Equal(50, result.Value.UpdatedTarget.MaximumDurability);
    }

    [Fact]
    public void EnhancementRejectsWrongEquipmentTierAndType()
    {
        var engine = Engine(Material(EquipmentEnhancementGrade.General, EquipmentEnhancementTargetType.Weapon, tier: 3, 1, 1, 3, EquipmentEnhancementFailurePolicy.DestroyTarget));

        var wrongTier = engine.Evaluate(100, Target(EquipmentEnhancementTargetType.Weapon, tier: 4, level: 0), 0, 0);
        var wrongType = engine.Evaluate(100, Target(EquipmentEnhancementTargetType.Equipment, tier: 3, level: 0), 0, 0);

        Assert.Equal("equipment.enhancement.tier_mismatch", wrongTier.Error.Code);
        Assert.Equal("equipment.enhancement.target_type_mismatch", wrongType.Error.Code);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(20, 3)]
    [InlineData(90, 10)]
    [InlineData(120, 10)]
    public void EquipmentTierUsesTenLevelBands(int? requiredLevel, int expectedTier) =>
        Assert.Equal(expectedTier, EquipmentEnhancementEngine.ResolveEquipmentTier(requiredLevel));

    private static EquipmentEnhancementEngine Engine(EquipmentEnhancementMaterialDefinition material)
    {
        var generalRates = Enumerable.Range(1, 10).Select(level => Rate(EquipmentEnhancementGrade.General, level, level <= 3 ? 10_000 : Math.Max(2_000, 12_000 - level * 1_000)));
        var advancedRates = Enumerable.Range(1, 10).Select(level => Rate(EquipmentEnhancementGrade.Advanced, level, level <= 3 ? 10_000 : 11_000 - level * 500));
        var specialRates = Enumerable.Range(1, 10).Select(level => Rate(EquipmentEnhancementGrade.Special, level, 10_000));
        return new EquipmentEnhancementEngine(new EquipmentEnhancementCatalog([material], generalRates.Concat(advancedRates).Concat(specialRates)));
    }

    private static EquipmentEnhancementMaterialDefinition Material(
        EquipmentEnhancementGrade grade,
        EquipmentEnhancementTargetType target,
        int tier,
        int minimumIncrement,
        int maximumIncrement,
        int durabilityLoss,
        EquipmentEnhancementFailurePolicy failurePolicy) =>
        new(100, 9_101, "強化道具", target, tier, grade, minimumIncrement, maximumIncrement, durabilityLoss, failurePolicy, "OfficialClient", "test", true);

    private static EquipmentEnhancementRateDefinition Rate(EquipmentEnhancementGrade grade, int level, int rate) =>
        new(grade, level, rate, level <= 3 ? "BahamutVerified" : "CompatibilityEstimate", "test", true);

    private static EquipmentEnhancementState Target(
        EquipmentEnhancementTargetType type,
        int tier,
        int level,
        int? currentDurability = 50,
        int? maximumDurability = 50) =>
        new(900, 800, type, tier, level, 10, currentDurability, maximumDurability, 0);
}
