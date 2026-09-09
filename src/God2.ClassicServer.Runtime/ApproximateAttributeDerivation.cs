namespace God2.ClassicServer.Runtime;

public sealed record DerivedCombatStatContribution(
    decimal MaximumHp,
    decimal MaximumMp,
    decimal PhysicalAttack,
    decimal MagicAttack,
    decimal PhysicalDefense,
    decimal MagicDefense)
{
    public RoundedDerivedCombatStatContribution Truncate() => new(
        checked((long)decimal.Truncate(MaximumHp)),
        checked((long)decimal.Truncate(MaximumMp)),
        checked((long)decimal.Truncate(PhysicalAttack)),
        checked((long)decimal.Truncate(MagicAttack)),
        checked((long)decimal.Truncate(PhysicalDefense)),
        checked((long)decimal.Truncate(MagicDefense)));
}

public sealed record RoundedDerivedCombatStatContribution(
    long MaximumHp,
    long MaximumMp,
    long PhysicalAttack,
    long MagicAttack,
    long PhysicalDefense,
    long MagicDefense);

public static class OfficialAttributeDerivation
{
    public const string EvidenceConfidence = "OfficialUserVerifiedAttributeGrowth";

    public static DerivedCombatStatContribution CalculateCharacter(PrimaryAttributePoints attributes)
    {
        Validate(attributes);
        return CalculateCharacterUnchecked(attributes);
    }

    public static DerivedCombatStatContribution CalculatePet(PrimaryAttributePoints attributes)
    {
        Validate(attributes);
        var character = CalculateCharacterUnchecked(attributes);
        return character with
        {
            PhysicalAttack = character.PhysicalAttack * 2m,
            MagicAttack = character.MagicAttack * 2m,
            PhysicalDefense = character.PhysicalDefense * 2m,
            MagicDefense = character.MagicDefense * 2m
        };
    }

    private static DerivedCombatStatContribution CalculateCharacterUnchecked(PrimaryAttributePoints attributes)
    {
        var constitution = attributes.Constitution;
        var strength = attributes.Strength;
        var intelligence = attributes.Intelligence;
        var speed = attributes.Speed;

        return new DerivedCombatStatContribution(
            MaximumHp: PerHundred(constitution, 400, strength, 80, intelligence, 20, speed, 100),
            MaximumMp: PerHundred(constitution, 20, strength, 30, intelligence, 100, speed, 50),
            PhysicalAttack: PerHundred(constitution, 2, strength, 30, intelligence, 3, speed, 5),
            MagicAttack: PerHundred(constitution, 2, strength, 3, intelligence, 30, speed, 5),
            PhysicalDefense: PerHundred(constitution, 10, strength, 5, intelligence, 0, speed, 5),
            MagicDefense: PerHundred(constitution, 5, strength, 0, intelligence, 10, speed, 5));
    }

    private static decimal PerHundred(
        int constitution, int constitutionRate,
        int strength, int strengthRate,
        int intelligence, int intelligenceRate,
        int speed, int speedRate) =>
        ((constitution * (decimal)constitutionRate) +
         (strength * (decimal)strengthRate) +
         (intelligence * (decimal)intelligenceRate) +
         (speed * (decimal)speedRate)) / 100m;

    private static void Validate(PrimaryAttributePoints attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (!attributes.IsNonNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(attributes));
        }
    }
}

// Compatibility entry point for callers compiled against the former approximation API.
public static class ApproximateAttributeDerivation
{
    public const string EvidenceConfidence = OfficialAttributeDerivation.EvidenceConfidence;

    public static DerivedCombatStatContribution Calculate(PrimaryAttributePoints attributes) =>
        OfficialAttributeDerivation.CalculateCharacter(attributes);
}
