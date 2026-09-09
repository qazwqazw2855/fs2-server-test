using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum PhysicalSkillFamily
{
    Blade,
    Sword,
    Staff,
    Whip,
    Spear,
    ThrowingKnife
}

public enum SkillCoefficientSelection
{
    Minimum,
    Midpoint,
    Maximum
}

public sealed record PhysicalSkillDamageCoefficient(
    PhysicalSkillFamily Family,
    int Tier,
    decimal MinimumMultiplier,
    decimal MidpointMultiplier,
    decimal MaximumMultiplier,
    string EvidenceStatus,
    int IndependentSourceCount,
    string PrimarySource,
    string? CorroboratingSource,
    bool Enabled)
{
    public decimal Select(SkillCoefficientSelection selection) => selection switch
    {
        SkillCoefficientSelection.Minimum => MinimumMultiplier,
        SkillCoefficientSelection.Midpoint => MidpointMultiplier,
        SkillCoefficientSelection.Maximum => MaximumMultiplier,
        _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, null)
    };
}

public interface IPhysicalSkillDamageRuntime
{
    OperationResult<PhysicalSkillDamageCoefficient> ResolvePhysicalSkillCoefficient(
        PhysicalSkillFamily family,
        int tier);

    OperationResult<decimal> ResolvePhysicalSkillMultiplier(
        PhysicalSkillFamily family,
        int tier,
        SkillCoefficientSelection selection = SkillCoefficientSelection.Midpoint);
}

public sealed class PhysicalSkillDamageCatalog
{
    private readonly IReadOnlyDictionary<(PhysicalSkillFamily Family, int Tier), PhysicalSkillDamageCoefficient> _rules;

    public PhysicalSkillDamageCatalog(IEnumerable<PhysicalSkillDamageCoefficient> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var enabled = rules.Where(rule => rule.Enabled).ToArray();
        if (enabled.Any(rule =>
                rule.Tier is < 1 or > 8 ||
                rule.MinimumMultiplier <= 0 ||
                rule.MinimumMultiplier > rule.MidpointMultiplier ||
                rule.MidpointMultiplier > rule.MaximumMultiplier ||
                rule.IndependentSourceCount <= 0 ||
                string.IsNullOrWhiteSpace(rule.PrimarySource)))
        {
            throw new InvalidOperationException("Physical skill damage coefficient data is invalid.");
        }

        var dictionary = enabled.ToDictionary(rule => (rule.Family, rule.Tier));
        _rules = new ReadOnlyDictionary<(PhysicalSkillFamily, int), PhysicalSkillDamageCoefficient>(dictionary);
    }

    public int RuleCount => _rules.Count;

    public OperationResult<PhysicalSkillDamageCoefficient> Resolve(PhysicalSkillFamily family, int tier) =>
        _rules.TryGetValue((family, tier), out var rule)
            ? OperationResult<PhysicalSkillDamageCoefficient>.Success(rule)
            : OperationResult<PhysicalSkillDamageCoefficient>.Failure(
                "battle.skill.physical_coefficient_missing",
                "No community-measured physical skill coefficient exists for this family and tier.",
                $"{family}:{tier}");
}

public sealed class PhysicalSkillDamageEngine : IPhysicalSkillDamageRuntime
{
    private readonly PhysicalSkillDamageCatalog _catalog;

    public PhysicalSkillDamageEngine(PhysicalSkillDamageCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public OperationResult<PhysicalSkillDamageCoefficient> ResolvePhysicalSkillCoefficient(
        PhysicalSkillFamily family,
        int tier) =>
        _catalog.Resolve(family, tier);

    public OperationResult<decimal> ResolvePhysicalSkillMultiplier(
        PhysicalSkillFamily family,
        int tier,
        SkillCoefficientSelection selection = SkillCoefficientSelection.Midpoint)
    {
        var resolved = _catalog.Resolve(family, tier);
        return !resolved.Succeeded
            ? OperationResult<decimal>.Failure(
                resolved.Error.Code,
                resolved.Error.Message,
                resolved.Error.Source)
            : OperationResult<decimal>.Success(resolved.Value!.Select(selection));
    }
}
