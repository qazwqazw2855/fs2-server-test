namespace God2.ClassicServer.Runtime;

public enum BattleElement
{
    None,
    Metal,
    Wood,
    Water,
    Fire,
    Earth
}

public enum FiveElementRelationship
{
    Neutral,
    Overcomes,
    OvercomeBy,
    Generates,
    GeneratedBy
}

public enum FormulaRoundingMode
{
    Truncate,
    Floor,
    Ceiling,
    AwayFromZero
}

public sealed record BaselineBasicPhysicalAttackResult(
    long EffectiveDefense,
    long NonCriticalDamage,
    long FinalDamage,
    bool IsDefending,
    bool IsCritical);

/// <summary>
/// Derived compatibility baseline retained for research and deterministic server behavior.
/// It is not an official formula and must not be treated as production-authoritative evidence.
/// </summary>
public static class BaselineBasicPhysicalAttackFormula
{
    public const string EvidenceConfidence = "DerivedBaseline_NotOfficial";
    public const bool ProductionAuthoritative = false;
    public const decimal DefendingDefenseMultiplier = 1.5m;
    public const int CriticalDamageMultiplier = 2;
    public const long MinimumDamage = 1;

    public static BaselineBasicPhysicalAttackResult Compute(
        long physicalAttack,
        long physicalDefense,
        bool isDefending = false,
        bool isCritical = false)
    {
        if (physicalAttack < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalAttack));
        }

        if (physicalDefense < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalDefense));
        }

        var effectiveDefense = EffectiveDefense(physicalDefense, isDefending);
        var nonCriticalDamage = Math.Max(
            MinimumDamage,
            checked(physicalAttack - effectiveDefense));
        var finalDamage = isCritical
            ? checked(nonCriticalDamage * CriticalDamageMultiplier)
            : nonCriticalDamage;

        return new BaselineBasicPhysicalAttackResult(
            effectiveDefense,
            nonCriticalDamage,
            finalDamage,
            isDefending,
            isCritical);
    }

    public static long EffectiveDefense(long physicalDefense, bool isDefending) =>
        !isDefending
            ? physicalDefense
            : checked((long)decimal.Floor(physicalDefense * DefendingDefenseMultiplier));

    public static decimal EffectiveDefense(decimal physicalDefense, bool isDefending) =>
        !isDefending
            ? physicalDefense
            : checked(decimal.Floor(physicalDefense * DefendingDefenseMultiplier));
}

public sealed record CompatibilityFormulaCalibration(
    decimal? OvercomesAttackMultiplier,
    decimal? OvercomeByAttackMultiplier,
    decimal? GeneratesDefenseMultiplier,
    decimal? GeneratedByDefenseMultiplier,
    FormulaRoundingMode RoundingMode,
    CombatPolicyStatus PolicyStatus)
{
    public static CompatibilityFormulaCalibration ApproximateDefault { get; } = new(
        OvercomesAttackMultiplier: 1.5m,
        OvercomeByAttackMultiplier: 0.5m,
        GeneratesDefenseMultiplier: 1.5m,
        GeneratedByDefenseMultiplier: 0.5m,
        RoundingMode: FormulaRoundingMode.Truncate,
        PolicyStatus: CombatPolicyStatus.Baseline);

    public static CompatibilityFormulaCalibration EvidenceBlocked { get; } = new(
        null,
        null,
        null,
        null,
        FormulaRoundingMode.Truncate,
        CombatPolicyStatus.EvidenceBlocked);
}

public sealed record CompatibilityDamageRequest(
    BattleParticipantStats Attacker,
    BattleParticipantStats Target,
    CombatDamageType DamageType,
    decimal SkillMultiplier,
    BattleElement AttackElement,
    BattleElement DefenseElement,
    decimal RandomMultiplier = 1m,
    bool IsCritical = false,
    decimal CriticalMultiplier = 2m,
    bool IsDefending = false);

public sealed record CompatibilityDamageResult(
    bool Succeeded,
    long FinalDamage,
    decimal AttackBeforeElement,
    decimal DefenseBeforeElement,
    decimal AttackAfterElement,
    decimal DefenseAfterElement,
    FiveElementRelationship ElementRelationship,
    decimal ElementAttackMultiplier,
    decimal ElementDefenseMultiplier,
    decimal SkillMultiplier,
    decimal RandomMultiplier,
    decimal CriticalMultiplier,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

public sealed record CompatibilityInitiativeResult(
    bool Succeeded,
    IReadOnlyList<long> OrderedEntityIds,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

/// <summary>
/// 可校準的相容公式，不宣稱是官方服務端原始公式。
/// 所有未確認係數都必須保持 null，使運算以 EvidenceBlocked 結束。
/// </summary>
public sealed class CompatibilityBattleFormula
{
    private readonly CompatibilityFormulaCalibration _calibration;

    public CompatibilityBattleFormula(CompatibilityFormulaCalibration calibration)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
    }

    public CompatibilityDamageResult Compute(CompatibilityDamageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Attacker);
        ArgumentNullException.ThrowIfNull(request.Target);

        if (request.SkillMultiplier <= 0 ||
            request.RandomMultiplier <= 0 ||
            request.CriticalMultiplier <= 0)
        {
            return Failure(request, "battle.formula.multiplier_invalid");
        }

        var attack = request.DamageType switch
        {
            CombatDamageType.Physical => request.Attacker.PhysicalAttack,
            CombatDamageType.Magic => request.Attacker.MagicAttack,
            _ => null
        };
        var defense = request.DamageType switch
        {
            CombatDamageType.Physical => request.Target.PhysicalDefense,
            CombatDamageType.Magic => request.Target.MagicDefense,
            _ => null
        };

        if (attack is null || defense is null)
        {
            return Failure(request, "battle.formula.required_stat_unknown");
        }

        if (attack < 0 || defense < 0)
        {
            return Failure(request, "battle.formula.stat_invalid");
        }

        var relationship = ResolveRelationship(request.AttackElement, request.DefenseElement);
        var multipliers = ResolveElementMultipliers(relationship);
        if (multipliers is null)
        {
            return Failure(request, "battle.formula.element_calibration_missing", relationship);
        }

        try
        {
            var attackAfterElement = checked(attack.Value * multipliers.Value.Attack);
            var effectiveDefense = BaselineBasicPhysicalAttackFormula.EffectiveDefense(defense.Value, request.IsDefending);
            var defenseAfterElement = checked(effectiveDefense * multipliers.Value.Defense);
            var reducedDamage = Math.Max(1m, checked(attackAfterElement - defenseAfterElement));
            var unroundedDamage = checked(
                reducedDamage *
                request.SkillMultiplier *
                request.RandomMultiplier *
                (request.IsCritical ? request.CriticalMultiplier : 1m));
            var roundedDamage = Round(unroundedDamage, _calibration.RoundingMode);
            if (roundedDamage > long.MaxValue)
            {
                return Failure(request, "battle.formula.damage_overflow", relationship);
            }

            return new CompatibilityDamageResult(
                true,
                Math.Max(1, (long)roundedDamage),
                attack.Value,
                effectiveDefense,
                attackAfterElement,
                defenseAfterElement,
                relationship,
                multipliers.Value.Attack,
                multipliers.Value.Defense,
                request.SkillMultiplier,
                request.RandomMultiplier,
                request.IsCritical ? request.CriticalMultiplier : 1m,
                _calibration.PolicyStatus,
                "");
        }
        catch (OverflowException)
        {
            return Failure(request, "battle.formula.damage_overflow", relationship);
        }
    }

    public static CompatibilityInitiativeResult ResolveInitiative(
        IEnumerable<BattleParticipantStats> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var values = participants.ToArray();
        if (values.Any(value => value.Speed is null))
        {
            return new CompatibilityInitiativeResult(
                false,
                [],
                CombatPolicyStatus.EvidenceBlocked,
                "battle.formula.speed_unknown");
        }

        return new CompatibilityInitiativeResult(
            true,
            values
                .OrderByDescending(value => value.Speed)
                .ThenBy(value => value.EntityId)
                .Select(value => value.EntityId)
                .ToArray(),
            CombatPolicyStatus.Baseline,
            "");
    }

    public static FiveElementRelationship ResolveRelationship(BattleElement attack, BattleElement defense)
    {
        if (attack == BattleElement.None || defense == BattleElement.None || attack == defense)
        {
            return FiveElementRelationship.Neutral;
        }

        if (Overcomes(attack) == defense)
        {
            return FiveElementRelationship.Overcomes;
        }

        if (Overcomes(defense) == attack)
        {
            return FiveElementRelationship.OvercomeBy;
        }

        if (Generates(attack) == defense)
        {
            return FiveElementRelationship.Generates;
        }

        return FiveElementRelationship.GeneratedBy;
    }

    private (decimal Attack, decimal Defense)? ResolveElementMultipliers(FiveElementRelationship relationship) =>
        relationship switch
        {
            FiveElementRelationship.Neutral => (1m, 1m),
            FiveElementRelationship.Overcomes when Valid(_calibration.OvercomesAttackMultiplier) =>
                (_calibration.OvercomesAttackMultiplier!.Value, 1m),
            FiveElementRelationship.OvercomeBy when Valid(_calibration.OvercomeByAttackMultiplier) =>
                (_calibration.OvercomeByAttackMultiplier!.Value, 1m),
            FiveElementRelationship.Generates when Valid(_calibration.GeneratesDefenseMultiplier) =>
                (1m, _calibration.GeneratesDefenseMultiplier!.Value),
            FiveElementRelationship.GeneratedBy when Valid(_calibration.GeneratedByDefenseMultiplier) =>
                (1m, _calibration.GeneratedByDefenseMultiplier!.Value),
            _ => null
        };

    private static bool Valid(decimal? multiplier) => multiplier is > 0m;

    private static BattleElement Overcomes(BattleElement element) => element switch
    {
        BattleElement.Metal => BattleElement.Wood,
        BattleElement.Wood => BattleElement.Earth,
        BattleElement.Earth => BattleElement.Water,
        BattleElement.Water => BattleElement.Fire,
        BattleElement.Fire => BattleElement.Metal,
        _ => BattleElement.None
    };

    private static BattleElement Generates(BattleElement element) => element switch
    {
        BattleElement.Wood => BattleElement.Fire,
        BattleElement.Fire => BattleElement.Earth,
        BattleElement.Earth => BattleElement.Metal,
        BattleElement.Metal => BattleElement.Water,
        BattleElement.Water => BattleElement.Wood,
        _ => BattleElement.None
    };

    private static decimal Round(decimal value, FormulaRoundingMode mode) => mode switch
    {
        FormulaRoundingMode.Truncate => decimal.Truncate(value),
        FormulaRoundingMode.Floor => decimal.Floor(value),
        FormulaRoundingMode.Ceiling => decimal.Ceiling(value),
        FormulaRoundingMode.AwayFromZero => decimal.Round(value, 0, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private CompatibilityDamageResult Failure(
        CompatibilityDamageRequest request,
        string failureCode,
        FiveElementRelationship? relationship = null) =>
        new(
            false,
            0,
            0,
            0,
            0,
            0,
            relationship ?? ResolveRelationship(request.AttackElement, request.DefenseElement),
            0,
            0,
            request.SkillMultiplier,
            request.RandomMultiplier,
            request.IsCritical ? request.CriticalMultiplier : 1m,
            CombatPolicyStatus.EvidenceBlocked,
            failureCode);
}
