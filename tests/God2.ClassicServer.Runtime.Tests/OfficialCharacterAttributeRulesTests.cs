using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialCharacterAttributeRulesTests
{
    [Theory]
    [InlineData(GameplayClass.Swordsman, 161, 45, 28, 32, 20, 20, 11, 6, 8, 4)]
    [InlineData(GameplayClass.Taoist, 142, 54, 24, 20, 32, 24, 6, 17, 19, 5)]
    [InlineData(GameplayClass.Pharmacist, 172, 47, 32, 24, 24, 20, 9, 5, 9, 5)]
    [InlineData(GameplayClass.Warlock, 150, 50, 25, 25, 25, 25, 10, 5, 10, 5)]
    public void Character_birth_profiles_match_verified_level_one_panels(
        GameplayClass @class,
        long hp,
        long mp,
        int constitution,
        int strength,
        int intelligence,
        int speed,
        int physicalAttack,
        int physicalDefense,
        int magicAttack,
        int magicDefense)
    {
        var profile = OfficialCharacterBirthProfiles.Get(@class);

        Assert.Equal((hp, mp), (profile.MaximumHp, profile.MaximumMp));
        Assert.Equal(
            new PrimaryAttributePoints(constitution, strength, intelligence, speed),
            profile.PrimaryAttributes);
        Assert.Equal(
            (physicalAttack, physicalDefense, magicAttack, magicDefense),
            (profile.PhysicalAttack, profile.PhysicalDefense, profile.MagicAttack, profile.MagicDefense));
        Assert.Equal(OfficialCharacterBirthProfiles.EvidenceConfidence, profile.EvidenceConfidence);
    }

    [Theory]
    [InlineData("Swordsman", GameplayClass.Swordsman)]
    [InlineData("Class1", GameplayClass.Swordsman)]
    [InlineData("Taoist", GameplayClass.Taoist)]
    [InlineData("Class2", GameplayClass.Taoist)]
    [InlineData("Pharmacist", GameplayClass.Pharmacist)]
    [InlineData("Class3", GameplayClass.Pharmacist)]
    [InlineData("Warlock", GameplayClass.Warlock)]
    [InlineData("Class4", GameplayClass.Warlock)]
    public void Client_class_aliases_resolve_to_the_same_verified_profile(string clientClass, GameplayClass expected) =>
        Assert.Equal(expected, OfficialCharacterBirthProfiles.ResolveClientClass(clientClass).Class);

    [Theory]
    [InlineData("Class1", GameplayClass.Swordsman, 28, 32, 20, 20, 2, 3, 0, 1)]
    [InlineData("Class2", GameplayClass.Taoist, 24, 20, 32, 24, 1, 0, 3, 2)]
    [InlineData("Class3", GameplayClass.Pharmacist, 32, 24, 24, 20, 2, 0, 2, 2)]
    [InlineData("Class4", GameplayClass.Warlock, 25, 25, 25, 25, 1, 1, 1, 3)]
    public void Client_class_code_binds_the_correct_birth_and_automatic_growth_rules(
        string clientClass,
        GameplayClass expectedClass,
        int birthConstitution,
        int birthStrength,
        int birthIntelligence,
        int birthSpeed,
        int growthConstitution,
        int growthStrength,
        int growthIntelligence,
        int growthSpeed)
    {
        var profile = OfficialCharacterBirthProfiles.ResolveClientClass(clientClass);
        var growth = OfficialAttributeGrowthCatalog.CreateDefault().CharacterRule(profile.Class);

        Assert.Equal(expectedClass, profile.Class);
        Assert.Equal(
            new PrimaryAttributePoints(birthConstitution, birthStrength, birthIntelligence, birthSpeed),
            profile.PrimaryAttributes);
        Assert.Equal(
            new PrimaryAttributePoints(growthConstitution, growthStrength, growthIntelligence, growthSpeed),
            growth.AutomaticPerLevel);
        Assert.Equal(6, growth.AutomaticPerLevel.Total);
        Assert.Equal(4, growth.ManualPointsPerLevel);
    }

    [Fact]
    public void Active_immortal_contributes_one_fifth_with_integer_truncation()
    {
        var inherited = OfficialImmortalAttributeInheritance.Calculate(
            new OfficialImmortalInheritableAttributes(303, 303, 303, 303, 288, 288, 288, 288, 288));

        Assert.Equal(
            new OfficialImmortalInheritableAttributes(60, 60, 60, 60, 57, 57, 57, 57, 57),
            inherited);
        Assert.Equal(5, OfficialImmortalAttributeInheritance.Divisor);
    }

    [Fact]
    public void Wuji_level_one_baseline_contributes_the_verified_one_fifth_values()
    {
        var inherited = OfficialImmortalAttributeInheritance.Calculate(
            new OfficialImmortalInheritableAttributes(
                Constitution: 20,
                Strength: 30,
                Intelligence: 10,
                Speed: 5,
                Metal: 0,
                Wood: 10,
                Water: 0,
                Fire: 0,
                Earth: 0));

        Assert.Equal(
            new OfficialImmortalInheritableAttributes(4, 6, 2, 1, 0, 2, 0, 0, 0),
            inherited);
    }

    [Theory]
    [InlineData(GameplayClass.Swordsman, true)]
    [InlineData(GameplayClass.Taoist, false)]
    [InlineData(GameplayClass.Pharmacist, false)]
    [InlineData(GameplayClass.Warlock, false)]
    public void Normal_immortal_is_limited_to_its_profession(GameplayClass characterClass, bool expected)
    {
        var access = new OfficialImmortalProfessionAccess(GameplayClass.Swordsman, isSpecial: false);

        Assert.Equal(expected, access.CanBeUsedBy(characterClass));
    }

    [Theory]
    [InlineData(GameplayClass.Swordsman)]
    [InlineData(GameplayClass.Taoist)]
    [InlineData(GameplayClass.Pharmacist)]
    [InlineData(GameplayClass.Warlock)]
    public void Special_immortal_is_available_to_every_profession(GameplayClass characterClass)
    {
        var access = new OfficialImmortalProfessionAccess(requiredClass: null, isSpecial: true);

        Assert.True(access.CanBeUsedBy(characterClass));
    }

    [Fact]
    public void Invalid_immortal_profession_access_shape_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new OfficialImmortalProfessionAccess(requiredClass: null, isSpecial: false));
        Assert.Throws<ArgumentException>(() =>
            new OfficialImmortalProfessionAccess(GameplayClass.Swordsman, isSpecial: true));
    }

    [Fact]
    public void Immortal_inheritance_is_added_to_character_bonus_without_mutating_birth_base()
    {
        var source = EmptyCharacter() with
        {
            ConstitutionBase = 28,
            ConstitutionBonus = 7,
            StrengthBase = 32,
            MetalBase = 0
        };
        var immortal = new OfficialImmortalInheritableAttributes(303, 303, 303, 303, 288, 288, 288, 288, 288);

        var applied = OfficialImmortalAttributeInheritance.ApplyToCharacter(source, immortal);
        var projected = BattleParticipantStatsFactory.FromCharacter(applied);

        Assert.Equal(28, applied.ConstitutionBase);
        Assert.Equal(67, applied.ConstitutionBonus);
        Assert.Equal(95, projected.Constitution);
        Assert.Equal(92, projected.Strength);
        Assert.Equal(57, projected.Metal);
    }

    [Fact]
    public void Negative_immortal_attributes_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OfficialImmortalAttributeInheritance.Calculate(
            new OfficialImmortalInheritableAttributes(-1, 0, 0, 0, 0, 0, 0, 0, 0)));

    [Fact]
    public void Only_the_single_active_immortal_contributes()
    {
        var attributes = new OfficialImmortalInheritableAttributes(303, 303, 303, 303, 288, 288, 288, 288, 288);
        var inherited = OfficialImmortalAttributeInheritance.CalculateActiveSelection(
        [
            new OfficialOwnedImmortalAttributeSource(1, false, attributes with { Constitution = 999 }),
            new OfficialOwnedImmortalAttributeSource(2, true, attributes)
        ]);

        Assert.Equal(60, inherited.Constitution);
        Assert.Equal(57, inherited.Metal);
    }

    [Fact]
    public void No_active_immortal_means_no_inherited_attributes()
    {
        var inherited = OfficialImmortalAttributeInheritance.CalculateActiveSelection(
        [
            new OfficialOwnedImmortalAttributeSource(
                1,
                false,
                new OfficialImmortalInheritableAttributes(303, 303, 303, 303, 288, 288, 288, 288, 288))
        ]);

        Assert.Equal(OfficialImmortalInheritableAttributes.Zero, inherited);
    }

    [Fact]
    public void Multiple_active_immortals_are_rejected_instead_of_stacking() =>
        Assert.Throws<InvalidOperationException>(() =>
            OfficialImmortalAttributeInheritance.CalculateActiveSelection(
            [
                new OfficialOwnedImmortalAttributeSource(1, true, OfficialImmortalInheritableAttributes.Zero),
                new OfficialOwnedImmortalAttributeSource(2, true, OfficialImmortalInheritableAttributes.Zero)
            ]));

    [Fact]
    public void Final_primary_projection_combines_birth_growth_manual_and_active_immortal_once()
    {
        var projection = OfficialCharacterPrimaryAttributeProjectionFactory.Calculate(
            GameplayClass.Swordsman,
            level: 3,
            new PrimaryAttributePoints(1, 2, 3, 2),
            [
                new OfficialOwnedImmortalAttributeSource(
                    1,
                    true,
                    new OfficialImmortalInheritableAttributes(303, 303, 303, 303, 288, 288, 288, 288, 288))
            ]);

        Assert.Equal(new PrimaryAttributePoints(28, 32, 20, 20), projection.Birth);
        Assert.Equal(new PrimaryAttributePoints(4, 6, 0, 2), projection.AutomaticGrowth);
        Assert.Equal(new PrimaryAttributePoints(60, 60, 60, 60), projection.ImmortalInheritance);
        Assert.Equal(new PrimaryAttributePoints(93, 100, 83, 84), projection.Total);
        Assert.Equal(
            new RoundedDerivedCombatStatContribution(391, 128, 26, 25, 13, 12),
            projection.DerivedGrowth);
        Assert.Equal(
            new OfficialCharacterDerivedStats(552, 173, 37, 19, 33, 16),
            projection.FinalDerivedStats);
    }

    [Fact]
    public void Manual_allocation_cannot_exceed_four_points_per_completed_level() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OfficialCharacterPrimaryAttributeProjectionFactory.Calculate(
                GameplayClass.Swordsman,
                level: 2,
                new PrimaryAttributePoints(0, 5, 0, 0),
                []));

    private static CharacterBattleStatSource EmptyCharacter()
    {
        var constructor = typeof(CharacterBattleStatSource).GetConstructors().Single();
        var arguments = new object?[constructor.GetParameters().Length];
        arguments[0] = 1L;
        return (CharacterBattleStatSource)constructor.Invoke(arguments);
    }
}
