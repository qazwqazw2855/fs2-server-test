using System.Collections.ObjectModel;

namespace God2.ClassicServer.Runtime;

public sealed record OfficialCharacterBirthProfile(
    GameplayClass Class,
    string ClientClassCode,
    string NameZhTw,
    long MaximumHp,
    long MaximumMp,
    PrimaryAttributePoints PrimaryAttributes,
    int PhysicalAttack,
    int PhysicalDefense,
    int MagicAttack,
    int MagicDefense,
    string EvidenceConfidence);

public static class OfficialCharacterBirthProfiles
{
    public const string EvidenceConfidence = "OfficialUserVerifiedCharacterCreation";

    private static readonly IReadOnlyDictionary<GameplayClass, OfficialCharacterBirthProfile> Profiles =
        new ReadOnlyDictionary<GameplayClass, OfficialCharacterBirthProfile>(
            new Dictionary<GameplayClass, OfficialCharacterBirthProfile>
            {
                [GameplayClass.Swordsman] = new(
                    GameplayClass.Swordsman, "Swordsman", "劍客", 161, 45,
                    new PrimaryAttributePoints(28, 32, 20, 20), 11, 6, 8, 4, EvidenceConfidence),
                [GameplayClass.Taoist] = new(
                    GameplayClass.Taoist, "Taoist", "仙道", 142, 54,
                    new PrimaryAttributePoints(24, 20, 32, 24), 6, 17, 19, 5, EvidenceConfidence),
                [GameplayClass.Pharmacist] = new(
                    GameplayClass.Pharmacist, "Pharmacist", "藥師", 172, 47,
                    new PrimaryAttributePoints(32, 24, 24, 20), 9, 5, 9, 5, EvidenceConfidence),
                [GameplayClass.Warlock] = new(
                    GameplayClass.Warlock, "Warlock", "謀士", 150, 50,
                    new PrimaryAttributePoints(25, 25, 25, 25), 10, 5, 10, 5, EvidenceConfidence)
            });

    private static readonly IReadOnlyDictionary<string, GameplayClass> ClientClassMappings =
        new ReadOnlyDictionary<string, GameplayClass>(
            new Dictionary<string, GameplayClass>(StringComparer.OrdinalIgnoreCase)
            {
                ["Swordsman"] = GameplayClass.Swordsman,
                ["Class1"] = GameplayClass.Swordsman,
                ["Taoist"] = GameplayClass.Taoist,
                ["Class2"] = GameplayClass.Taoist,
                ["Pharmacist"] = GameplayClass.Pharmacist,
                ["Class3"] = GameplayClass.Pharmacist,
                ["Warlock"] = GameplayClass.Warlock,
                ["Class4"] = GameplayClass.Warlock
            });

    public static OfficialCharacterBirthProfile Get(GameplayClass @class) => Profiles[@class];

    public static OfficialCharacterBirthProfile ResolveClientClass(string clientClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientClass);
        var normalized = clientClass.Trim();
        if (ClientClassMappings.TryGetValue(normalized, out var @class))
        {
            return Get(@class);
        }

        throw new ArgumentOutOfRangeException(nameof(clientClass), clientClass, "Unsupported official character class.");
    }

    public static IReadOnlyList<OfficialCharacterBirthProfile> Snapshot() =>
        Array.AsReadOnly(Profiles.Values.OrderBy(profile => profile.Class).ToArray());
}

public sealed record OfficialCharacterPrimaryAttributeProjection(
    PrimaryAttributePoints Birth,
    PrimaryAttributePoints AutomaticGrowth,
    PrimaryAttributePoints AllocatedManualPoints,
    PrimaryAttributePoints ImmortalInheritance,
    PrimaryAttributePoints Total,
    RoundedDerivedCombatStatContribution DerivedGrowth,
    OfficialCharacterDerivedStats FinalDerivedStats);

public sealed record OfficialCharacterDerivedStats(
    long MaximumHp,
    long MaximumMp,
    long PhysicalAttack,
    long PhysicalDefense,
    long MagicAttack,
    long MagicDefense);

public static class OfficialCharacterPrimaryAttributeProjectionFactory
{
    public static OfficialCharacterPrimaryAttributeProjection Calculate(
        GameplayClass @class,
        int level,
        PrimaryAttributePoints allocatedManualPoints,
        IEnumerable<OfficialOwnedImmortalAttributeSource> ownedImmortals)
    {
        ArgumentNullException.ThrowIfNull(allocatedManualPoints);
        ArgumentNullException.ThrowIfNull(ownedImmortals);
        if (level < 1 || !allocatedManualPoints.IsNonNegative)
        {
            throw new ArgumentOutOfRangeException(level < 1 ? nameof(level) : nameof(allocatedManualPoints));
        }

        var growthCatalog = OfficialAttributeGrowthCatalog.CreateDefault();
        var rule = growthCatalog.CharacterRule(@class);
        var availableManualPoints = checked((level - 1) * rule.ManualPointsPerLevel);
        if (allocatedManualPoints.Total > availableManualPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allocatedManualPoints),
                "Allocated manual points exceed the official points earned at this level.");
        }

        var birth = OfficialCharacterBirthProfiles.Get(@class).PrimaryAttributes;
        var automatic = growthCatalog.AutomaticGrowthAt(@class, level);
        var immortal = OfficialImmortalAttributeInheritance.CalculateActiveSelection(ownedImmortals);
        var inheritedPrimary = new PrimaryAttributePoints(
            immortal.Constitution,
            immortal.Strength,
            immortal.Intelligence,
            immortal.Speed);
        var total = birth
            .AddScaled(automatic, 1)
            .AddScaled(allocatedManualPoints, 1)
            .AddScaled(inheritedPrimary, 1);
        var growthAttributes = automatic
            .AddScaled(allocatedManualPoints, 1)
            .AddScaled(inheritedPrimary, 1);
        var derivedGrowth = OfficialAttributeDerivation.CalculateCharacter(growthAttributes).Truncate();
        var birthProfile = OfficialCharacterBirthProfiles.Get(@class);
        var finalDerivedStats = new OfficialCharacterDerivedStats(
            checked(birthProfile.MaximumHp + derivedGrowth.MaximumHp),
            checked(birthProfile.MaximumMp + derivedGrowth.MaximumMp),
            checked(birthProfile.PhysicalAttack + derivedGrowth.PhysicalAttack),
            checked(birthProfile.PhysicalDefense + derivedGrowth.PhysicalDefense),
            checked(birthProfile.MagicAttack + derivedGrowth.MagicAttack),
            checked(birthProfile.MagicDefense + derivedGrowth.MagicDefense));

        return new OfficialCharacterPrimaryAttributeProjection(
            birth,
            automatic,
            allocatedManualPoints,
            inheritedPrimary,
            total,
            derivedGrowth,
            finalDerivedStats);
    }
}

public sealed record OfficialImmortalInheritableAttributes(
    int Constitution,
    int Strength,
    int Intelligence,
    int Speed,
    int Metal,
    int Wood,
    int Water,
    int Fire,
    int Earth)
{
    public static OfficialImmortalInheritableAttributes Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsNonNegative =>
        Constitution >= 0 && Strength >= 0 && Intelligence >= 0 && Speed >= 0 &&
        Metal >= 0 && Wood >= 0 && Water >= 0 && Fire >= 0 && Earth >= 0;
}

public sealed record OfficialOwnedImmortalAttributeSource(
    long ImmortalInstanceId,
    bool IsActive,
    OfficialImmortalInheritableAttributes Attributes);

public sealed record OfficialImmortalProfessionAccess
{
    public OfficialImmortalProfessionAccess(GameplayClass? requiredClass, bool isSpecial)
    {
        if (isSpecial == requiredClass.HasValue)
        {
            throw new ArgumentException(
                "A normal immortal requires exactly one profession; a special immortal must not require one.");
        }

        RequiredClass = requiredClass;
        IsSpecial = isSpecial;
    }

    public GameplayClass? RequiredClass { get; }
    public bool IsSpecial { get; }

    public bool CanBeUsedBy(GameplayClass characterClass) => IsSpecial || RequiredClass == characterClass;
}

public static class OfficialImmortalAttributeInheritance
{
    public const int Divisor = 5;
    public const string EvidenceConfidence = "OfficialUserVerifiedOneFifthInheritance";

    public static OfficialImmortalInheritableAttributes Calculate(
        OfficialImmortalInheritableAttributes activeImmortal)
    {
        ArgumentNullException.ThrowIfNull(activeImmortal);
        if (!activeImmortal.IsNonNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(activeImmortal));
        }

        return new OfficialImmortalInheritableAttributes(
            activeImmortal.Constitution / Divisor,
            activeImmortal.Strength / Divisor,
            activeImmortal.Intelligence / Divisor,
            activeImmortal.Speed / Divisor,
            activeImmortal.Metal / Divisor,
            activeImmortal.Wood / Divisor,
            activeImmortal.Water / Divisor,
            activeImmortal.Fire / Divisor,
            activeImmortal.Earth / Divisor);
    }

    public static CharacterBattleStatSource ApplyToCharacter(
        CharacterBattleStatSource character,
        OfficialImmortalInheritableAttributes activeImmortal)
    {
        ArgumentNullException.ThrowIfNull(character);
        return ApplyInherited(character, Calculate(activeImmortal));
    }

    public static OfficialImmortalInheritableAttributes CalculateActiveSelection(
        IEnumerable<OfficialOwnedImmortalAttributeSource> ownedImmortals)
    {
        ArgumentNullException.ThrowIfNull(ownedImmortals);
        var active = ownedImmortals.Where(value => value.IsActive).Take(2).ToArray();
        return active.Length switch
        {
            0 => OfficialImmortalInheritableAttributes.Zero,
            1 => Calculate(active[0].Attributes),
            _ => throw new InvalidOperationException("Only one active immortal may contribute attributes.")
        };
    }

    public static CharacterBattleStatSource ApplyActiveSelectionToCharacter(
        CharacterBattleStatSource character,
        IEnumerable<OfficialOwnedImmortalAttributeSource> ownedImmortals)
    {
        ArgumentNullException.ThrowIfNull(character);
        return ApplyInherited(character, CalculateActiveSelection(ownedImmortals));
    }

    private static CharacterBattleStatSource ApplyInherited(
        CharacterBattleStatSource character,
        OfficialImmortalInheritableAttributes inherited) =>
        character with
        {
            ConstitutionBonus = AddBonus(character.ConstitutionBonus, inherited.Constitution),
            StrengthBonus = AddBonus(character.StrengthBonus, inherited.Strength),
            IntelligenceBonus = AddBonus(character.IntelligenceBonus, inherited.Intelligence),
            SpeedBonus = AddBonus(character.SpeedBonus, inherited.Speed),
            MetalBonus = AddBonus(character.MetalBonus, inherited.Metal),
            WoodBonus = AddBonus(character.WoodBonus, inherited.Wood),
            WaterBonus = AddBonus(character.WaterBonus, inherited.Water),
            FireBonus = AddBonus(character.FireBonus, inherited.Fire),
            EarthBonus = AddBonus(character.EarthBonus, inherited.Earth)
        };

    private static int? AddBonus(int? existing, int inherited) =>
        inherited == 0 ? existing : checked((existing ?? 0) + inherited);
}
