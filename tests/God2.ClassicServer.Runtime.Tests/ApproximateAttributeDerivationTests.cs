using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialAttributeDerivationTests
{
    [Theory]
    [MemberData(nameof(CharacterHundredPointExamples))]
    public void Character_hundred_point_examples_match_official_growth(
        PrimaryAttributePoints attributes,
        RoundedDerivedCombatStatContribution expected)
    {
        var result = OfficialAttributeDerivation.CalculateCharacter(attributes).Truncate();

        Assert.Equal(expected, result);
        Assert.Equal("OfficialUserVerifiedAttributeGrowth", OfficialAttributeDerivation.EvidenceConfidence);
    }

    [Theory]
    [MemberData(nameof(PetHundredPointExamples))]
    public void Pet_hundred_point_examples_match_official_growth(
        PrimaryAttributePoints attributes,
        RoundedDerivedCombatStatContribution expected) =>
        Assert.Equal(expected, OfficialAttributeDerivation.CalculatePet(attributes).Truncate());

    [Fact]
    public void Pet_hp_and_mp_equal_character_while_all_attack_and_defense_values_are_doubled()
    {
        var attributes = new PrimaryAttributePoints(137, 83, 219, 64);
        var character = OfficialAttributeDerivation.CalculateCharacter(attributes);
        var pet = OfficialAttributeDerivation.CalculatePet(attributes);

        Assert.Equal(character.MaximumHp, pet.MaximumHp);
        Assert.Equal(character.MaximumMp, pet.MaximumMp);
        Assert.Equal(character.PhysicalAttack * 2m, pet.PhysicalAttack);
        Assert.Equal(character.PhysicalDefense * 2m, pet.PhysicalDefense);
        Assert.Equal(character.MagicAttack * 2m, pet.MagicAttack);
        Assert.Equal(character.MagicDefense * 2m, pet.MagicDefense);
    }

    [Fact]
    public void Mixed_attributes_are_aggregated_before_integer_truncation()
    {
        var result = OfficialAttributeDerivation.CalculateCharacter(new PrimaryAttributePoints(2, 3, 0, 1));

        Assert.Equal(11.4m, result.MaximumHp);
        Assert.Equal(1.8m, result.MaximumMp);
        Assert.Equal(0.99m, result.PhysicalAttack);
        Assert.Equal(0.18m, result.MagicAttack);
        Assert.Equal(0.4m, result.PhysicalDefense);
        Assert.Equal(0.15m, result.MagicDefense);
    }

    [Fact]
    public void Former_approximation_entry_point_uses_the_official_character_formula()
    {
        var attributes = new PrimaryAttributePoints(100, 100, 100, 100);

        Assert.Equal(
            OfficialAttributeDerivation.CalculateCharacter(attributes),
            ApproximateAttributeDerivation.Calculate(attributes));
        Assert.Equal(OfficialAttributeDerivation.EvidenceConfidence, ApproximateAttributeDerivation.EvidenceConfidence);
    }

    [Fact]
    public void Negative_attribute_values_are_rejected()
    {
        var attributes = new PrimaryAttributePoints(-1, 0, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => OfficialAttributeDerivation.CalculateCharacter(attributes));
        Assert.Throws<ArgumentOutOfRangeException>(() => OfficialAttributeDerivation.CalculatePet(attributes));
    }

    public static TheoryData<PrimaryAttributePoints, RoundedDerivedCombatStatContribution> CharacterHundredPointExamples => new()
    {
        {
            new PrimaryAttributePoints(100, 0, 0, 0),
            new RoundedDerivedCombatStatContribution(400, 20, 2, 2, 10, 5)
        },
        {
            new PrimaryAttributePoints(0, 100, 0, 0),
            new RoundedDerivedCombatStatContribution(80, 30, 30, 3, 5, 0)
        },
        {
            new PrimaryAttributePoints(0, 0, 100, 0),
            new RoundedDerivedCombatStatContribution(20, 100, 3, 30, 0, 10)
        },
        {
            new PrimaryAttributePoints(0, 0, 0, 100),
            new RoundedDerivedCombatStatContribution(100, 50, 5, 5, 5, 5)
        }
    };

    public static TheoryData<PrimaryAttributePoints, RoundedDerivedCombatStatContribution> PetHundredPointExamples => new()
    {
        {
            new PrimaryAttributePoints(100, 0, 0, 0),
            new RoundedDerivedCombatStatContribution(400, 20, 4, 4, 20, 10)
        },
        {
            new PrimaryAttributePoints(0, 100, 0, 0),
            new RoundedDerivedCombatStatContribution(80, 30, 60, 6, 10, 0)
        },
        {
            new PrimaryAttributePoints(0, 0, 100, 0),
            new RoundedDerivedCombatStatContribution(20, 100, 6, 60, 0, 20)
        },
        {
            new PrimaryAttributePoints(0, 0, 0, 100),
            new RoundedDerivedCombatStatContribution(100, 50, 10, 10, 10, 10)
        }
    };
}
