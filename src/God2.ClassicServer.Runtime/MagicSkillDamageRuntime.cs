using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed record XianDaoMeditationPassive(BattleElement Element, int Level);

public sealed record BattleElementStats(
    decimal Metal,
    decimal Wood,
    decimal Water,
    decimal Fire,
    decimal Earth)
{
    public decimal Get(BattleElement element) => element switch
    {
        BattleElement.Metal => Metal,
        BattleElement.Wood => Wood,
        BattleElement.Water => Water,
        BattleElement.Fire => Fire,
        BattleElement.Earth => Earth,
        _ => 0m
    };
}

public sealed record MagicSkillDamageCoefficient(
    BattleElement Element,
    int Tier,
    decimal MinimumMultiplier,
    decimal MidpointMultiplier,
    decimal MaximumMultiplier,
    decimal AttackerSameElementDivisor,
    decimal TargetWeakElementDivisor,
    decimal TargetCounterElementDivisor,
    decimal TargetSameElementDivisor,
    decimal PetMeditationMultiplier,
    decimal? XianDaoMeditationLevel1Bonus,
    decimal? XianDaoMeditationLevel2Bonus,
    string EvidenceStatus,
    string RegionCompatibilityStatus,
    string Source,
    bool Enabled);

public sealed record MagicSkillDamageRequest(
    decimal MagicAttack,
    decimal MagicDefense,
    BattleElement AttackElement,
    int SkillTier,
    BattleElementStats AttackerElements,
    BattleElementStats TargetElements,
    XianDaoMeditationPassive? MeditationPassive = null);

public sealed record MagicSkillDamageResult(
    decimal MagicAttack,
    decimal MagicDefense,
    decimal AttackerSameElementBonus,
    decimal TargetWeakElementBonus,
    decimal TargetCounterElementDefense,
    decimal TargetSameElementDefense,
    decimal BaseDamageBeforeSkillAndMeditation,
    decimal MinimumSkillMultiplier,
    decimal MidpointSkillMultiplier,
    decimal MaximumSkillMultiplier,
    decimal MeditationCoefficientBonus,
    decimal MinimumDamage,
    decimal MidpointDamage,
    decimal MaximumDamage,
    string EvidenceStatus,
    string RegionCompatibilityStatus)
{
    public decimal SelectDamage(SkillCoefficientSelection selection) => selection switch
    {
        SkillCoefficientSelection.Minimum => MinimumDamage,
        SkillCoefficientSelection.Midpoint => MidpointDamage,
        SkillCoefficientSelection.Maximum => MaximumDamage,
        _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, null)
    };

    public long SelectFinalDamage(SkillCoefficientSelection selection) =>
        Math.Max(1, checked((long)decimal.Truncate(SelectDamage(selection))));
}

public interface IMagicSkillDamageRuntime
{
    OperationResult<MagicSkillDamageCoefficient> ResolveMagicSkillCoefficient(
        BattleElement element,
        int tier);

    OperationResult<MagicSkillDamageResult> CalculateMagicSkillDamage(
        MagicSkillDamageRequest request);
}

public sealed class MagicSkillDamageCatalog
{
    private readonly IReadOnlyDictionary<(BattleElement Element, int Tier), MagicSkillDamageCoefficient> _rules;

    public MagicSkillDamageCatalog(IEnumerable<MagicSkillDamageCoefficient> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var enabled = rules.Where(rule => rule.Enabled).ToArray();
        if (enabled.Any(rule =>
                rule.Element == BattleElement.None ||
                rule.Tier <= 0 ||
                rule.MinimumMultiplier <= 0 ||
                rule.MinimumMultiplier > rule.MidpointMultiplier ||
                rule.MidpointMultiplier > rule.MaximumMultiplier ||
                rule.AttackerSameElementDivisor <= 0 ||
                rule.TargetWeakElementDivisor <= 0 ||
                rule.TargetCounterElementDivisor <= 0 ||
                rule.TargetSameElementDivisor <= 0 ||
                rule.PetMeditationMultiplier <= 0 ||
                rule.XianDaoMeditationLevel1Bonus is <= 0 ||
                rule.XianDaoMeditationLevel2Bonus is <= 0 ||
                rule.XianDaoMeditationLevel2Bonus < rule.XianDaoMeditationLevel1Bonus ||
                string.IsNullOrWhiteSpace(rule.Source)))
        {
            throw new InvalidOperationException("Magic skill damage coefficient data is invalid.");
        }

        _rules = new ReadOnlyDictionary<(BattleElement, int), MagicSkillDamageCoefficient>(
            enabled.ToDictionary(rule => (rule.Element, rule.Tier)));
    }

    public int RuleCount => _rules.Count;

    public OperationResult<MagicSkillDamageCoefficient> Resolve(BattleElement element, int tier) =>
        _rules.TryGetValue((element, tier), out var rule)
            ? OperationResult<MagicSkillDamageCoefficient>.Success(rule)
            : OperationResult<MagicSkillDamageCoefficient>.Failure(
                "battle.skill.magic_coefficient_missing",
                "No community-measured magic skill coefficient exists for this element and tier.",
                $"{element}:{tier}");
}

public sealed class MagicSkillDamageEngine : IMagicSkillDamageRuntime
{
    private readonly MagicSkillDamageCatalog _catalog;

    public MagicSkillDamageEngine(MagicSkillDamageCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public OperationResult<MagicSkillDamageCoefficient> ResolveMagicSkillCoefficient(
        BattleElement element,
        int tier) =>
        _catalog.Resolve(element, tier);

    public OperationResult<MagicSkillDamageResult> CalculateMagicSkillDamage(
        MagicSkillDamageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AttackerElements);
        ArgumentNullException.ThrowIfNull(request.TargetElements);
        if (request.MagicAttack < 0 || request.MagicDefense < 0 ||
            ElementValues(request.AttackerElements).Any(value => value < 0) ||
            ElementValues(request.TargetElements).Any(value => value < 0))
        {
            return Failure("battle.skill.magic_stat_invalid", "Magic damage inputs cannot be negative.");
        }

        var coefficient = _catalog.Resolve(request.AttackElement, request.SkillTier);
        if (!coefficient.Succeeded)
        {
            return Failure(coefficient.Error.Code, coefficient.Error.Message, coefficient.Error.Source);
        }

        var rule = coefficient.Value!;
        var weakElement = Overcomes(request.AttackElement);
        var counterElement = OvercomesAttack(request.AttackElement);
        var attackerSameBonus = request.AttackerElements.Get(request.AttackElement) / rule.AttackerSameElementDivisor;
        var targetWeakBonus = request.TargetElements.Get(weakElement) / rule.TargetWeakElementDivisor;
        var targetCounterDefense = request.TargetElements.Get(counterElement) / rule.TargetCounterElementDivisor;
        var targetSameDefense = request.TargetElements.Get(request.AttackElement) / rule.TargetSameElementDivisor;
        var baseDamage = request.MagicAttack + attackerSameBonus + targetWeakBonus
            - request.MagicDefense - targetCounterDefense - targetSameDefense;
        if (baseDamage <= 0)
        {
            return Failure(
                "battle.skill.magic_base_damage_non_positive",
                "The measured magic formula produced non-positive base damage; no minimum-damage rule is evidenced.");
        }

        if (request.MeditationPassive is not null &&
            request.MeditationPassive.Element != request.AttackElement)
        {
            return Failure(
                "battle.skill.magic_meditation_element_mismatch",
                "XianDao meditation only strengthens magic of the same element.");
        }

        var meditationBonus = request.MeditationPassive?.Level switch
        {
            null => 0m,
            1 when rule.XianDaoMeditationLevel1Bonus is not null => rule.XianDaoMeditationLevel1Bonus.Value,
            2 when rule.XianDaoMeditationLevel2Bonus is not null => rule.XianDaoMeditationLevel2Bonus.Value,
            _ => -1m
        };
        if (meditationBonus < 0)
        {
            return Failure(
                "battle.skill.magic_meditation_level_missing",
                "No accepted XianDao meditation coefficient exists for this level.");
        }

        var minimumCoefficient = rule.MinimumMultiplier + meditationBonus;
        var midpointCoefficient = rule.MidpointMultiplier + meditationBonus;
        var maximumCoefficient = rule.MaximumMultiplier + meditationBonus;
        return OperationResult<MagicSkillDamageResult>.Success(new(
            request.MagicAttack,
            request.MagicDefense,
            attackerSameBonus,
            targetWeakBonus,
            targetCounterDefense,
            targetSameDefense,
            baseDamage,
            rule.MinimumMultiplier,
            rule.MidpointMultiplier,
            rule.MaximumMultiplier,
            meditationBonus,
            baseDamage * minimumCoefficient * rule.PetMeditationMultiplier,
            baseDamage * midpointCoefficient * rule.PetMeditationMultiplier,
            baseDamage * maximumCoefficient * rule.PetMeditationMultiplier,
            rule.EvidenceStatus,
            rule.RegionCompatibilityStatus));
    }

    private static IEnumerable<decimal> ElementValues(BattleElementStats stats) =>
        [stats.Metal, stats.Wood, stats.Water, stats.Fire, stats.Earth];

    private static BattleElement Overcomes(BattleElement element) => element switch
    {
        BattleElement.Metal => BattleElement.Wood,
        BattleElement.Wood => BattleElement.Earth,
        BattleElement.Earth => BattleElement.Water,
        BattleElement.Water => BattleElement.Fire,
        BattleElement.Fire => BattleElement.Metal,
        _ => BattleElement.None
    };

    private static BattleElement OvercomesAttack(BattleElement element) => element switch
    {
        BattleElement.Metal => BattleElement.Fire,
        BattleElement.Wood => BattleElement.Metal,
        BattleElement.Earth => BattleElement.Wood,
        BattleElement.Water => BattleElement.Earth,
        BattleElement.Fire => BattleElement.Water,
        _ => BattleElement.None
    };

    private static OperationResult<MagicSkillDamageResult> Failure(
        string code,
        string message,
        string source = "") =>
        OperationResult<MagicSkillDamageResult>.Failure(code, message, source);
}
