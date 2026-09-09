using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum PetInitialGrowthQuality
{
    Normal,
    Top
}

public enum PetGrowthGrade
{
    Normal,
    Top,
    LateBreakthrough,
    Breakthrough
}

public enum PetGrowthGradeStatus
{
    Unknown,
    Provisional,
    Confirmed
}

public sealed record PetGrowthGradeRule(
    PetGrowthGrade Grade,
    int MinimumLevel,
    int MaximumLevel,
    int AutomaticPointsPerLevel,
    int ManualPointsPerLevel,
    PetInitialGrowthQuality InitialQuality,
    int? BreakthroughLevel,
    string EvidenceStatus,
    string Source,
    bool Enabled);

public sealed record PetAutomaticGrowthAllocation(
    int CategoryId,
    PetGrowthGrade Grade,
    int MinimumLevel,
    int MaximumLevel,
    int ConstitutionDelta,
    int StrengthDelta,
    int IntelligenceDelta,
    int SpeedDelta,
    string EvidenceStatus,
    string Source,
    bool Enabled)
{
    public int Total => ConstitutionDelta + StrengthDelta + IntelligenceDelta + SpeedDelta;
}

public sealed record PetCoreStats(int Constitution, int Strength, int Intelligence, int Speed);

public static class PetBaseStatRules
{
    public const int Constitution = 100;
    public const int Strength = 50;
    public const int Intelligence = 50;
    public const int Speed = 100;

    public static PetCoreStats Create() => new(Constitution, Strength, Intelligence, Speed);
}

public sealed record PetGrowthClassification(
    PetInitialGrowthQuality InitialQuality,
    PetGrowthGrade Grade,
    PetGrowthGradeStatus Status,
    int? ConfirmedAtLevel,
    int LastAutomaticGrowthTotal);

public interface IPetGrowthGradeRuntime
{
    OperationResult<PetGrowthClassification> ClassifyPetInitialGrowth(int automaticGrowthTotal);

    OperationResult<PetGrowthClassification> ObservePetLevelUp(
        PetGrowthClassification current,
        int reachedLevel,
        int automaticGrowthDeltaTotal);

    OperationResult<PetAutomaticGrowthAllocation> ResolvePetAutomaticGrowth(
        int categoryId,
        PetGrowthGrade grade,
        int reachedLevel);

    OperationResult<PetCoreStats> CalculatePetCoreStats(
        int categoryId,
        PetGrowthGrade grade,
        int level);
}

public sealed class PetGrowthGradeCatalog
{
    private readonly IReadOnlyDictionary<(PetGrowthGrade Grade, int MinimumLevel), PetGrowthGradeRule> _rules;
    private readonly IReadOnlyDictionary<(int CategoryId, PetGrowthGrade Grade, int MinimumLevel), PetAutomaticGrowthAllocation> _allocations;

    public PetGrowthGradeCatalog(
        IEnumerable<PetGrowthGradeRule> rules,
        IEnumerable<PetAutomaticGrowthAllocation>? allocations = null)
    {
        _rules = new ReadOnlyDictionary<(PetGrowthGrade, int), PetGrowthGradeRule>(
            rules.Where(rule => rule.Enabled).ToDictionary(rule => (rule.Grade, rule.MinimumLevel)));
        _allocations = new ReadOnlyDictionary<(int, PetGrowthGrade, int), PetAutomaticGrowthAllocation>(
            (allocations ?? []).Where(rule => rule.Enabled)
                .ToDictionary(rule => (rule.CategoryId, rule.Grade, rule.MinimumLevel)));
    }

    public int RuleCount => _rules.Count;

    public int AllocationRuleCount => _allocations.Count;

    public IReadOnlyList<PetGrowthGradeRule> Rules => _rules.Values.ToArray();

    public OperationResult<PetGrowthGradeRule> Resolve(PetGrowthGrade grade, int level)
    {
        var matches = _rules.Values
            .Where(rule => rule.Grade == grade && level >= rule.MinimumLevel && level <= rule.MaximumLevel)
            .ToArray();
        return matches.Length == 1
            ? OperationResult<PetGrowthGradeRule>.Success(matches[0])
            : OperationResult<PetGrowthGradeRule>.Failure(
                "pet.growth.rule_missing",
                "No unique verified pet growth rule exists for this grade and level.",
                $"{grade}:{level}");
    }

    public OperationResult<PetAutomaticGrowthAllocation> ResolveAllocation(
        int categoryId,
        PetGrowthGrade grade,
        int level)
    {
        var matches = _allocations.Values
            .Where(rule => rule.CategoryId == categoryId && rule.Grade == grade
                && level >= rule.MinimumLevel && level <= rule.MaximumLevel)
            .ToArray();
        return matches.Length == 1
            ? OperationResult<PetAutomaticGrowthAllocation>.Success(matches[0])
            : OperationResult<PetAutomaticGrowthAllocation>.Failure(
                "pet.growth.allocation_missing",
                "No verified automatic stat allocation exists for this pet category, grade and level.",
                $"{categoryId}:{grade}:{level}");
    }

    public static PetGrowthGradeCatalog CreateVerifiedDefault()
    {
        const string source = "Bahamut God2 pet growth references";
        return new PetGrowthGradeCatalog(
        [
            Rule(PetGrowthGrade.Normal, 1, 19, 4, PetInitialGrowthQuality.Normal, null, source),
            Rule(PetGrowthGrade.Normal, 20, 49, 6, PetInitialGrowthQuality.Normal, null, source),
            Rule(PetGrowthGrade.Normal, 50, 99, 8, PetInitialGrowthQuality.Normal, null, source),
            Rule(PetGrowthGrade.Top, 1, 19, 5, PetInitialGrowthQuality.Top, null, source),
            Rule(PetGrowthGrade.Top, 20, 49, 6, PetInitialGrowthQuality.Top, null, source),
            Rule(PetGrowthGrade.Top, 50, 99, 8, PetInitialGrowthQuality.Top, null, source),
            Rule(PetGrowthGrade.LateBreakthrough, 1, 19, 4, PetInitialGrowthQuality.Normal, 50, source),
            Rule(PetGrowthGrade.LateBreakthrough, 20, 49, 6, PetInitialGrowthQuality.Normal, 50, source),
            Rule(PetGrowthGrade.LateBreakthrough, 50, 99, 10, PetInitialGrowthQuality.Normal, 50, source),
            Rule(PetGrowthGrade.Breakthrough, 1, 19, 4, PetInitialGrowthQuality.Normal, 20, source),
            Rule(PetGrowthGrade.Breakthrough, 20, 49, 8, PetInitialGrowthQuality.Normal, 20, source),
            Rule(PetGrowthGrade.Breakthrough, 50, 99, 10, PetInitialGrowthQuality.Normal, 20, source)
        ]);
    }

    private static PetGrowthGradeRule Rule(
        PetGrowthGrade grade,
        int minimumLevel,
        int maximumLevel,
        int automaticPoints,
        PetInitialGrowthQuality initialQuality,
        int? breakthroughLevel,
        string source) =>
        new(grade, minimumLevel, maximumLevel, automaticPoints, 1, initialQuality, breakthroughLevel, "BahamutVerified", source, true);
}

public sealed class PetGrowthGradeEngine
{
    private readonly PetGrowthGradeCatalog _catalog;

    public PetGrowthGradeEngine(PetGrowthGradeCatalog catalog) => _catalog = catalog;

    public OperationResult<PetGrowthClassification> ClassifyInitial(int automaticGrowthTotal)
    {
        return automaticGrowthTotal switch
        {
            4 => OperationResult<PetGrowthClassification>.Success(new(
                PetInitialGrowthQuality.Normal,
                PetGrowthGrade.Normal,
                PetGrowthGradeStatus.Provisional,
                null,
                automaticGrowthTotal)),
            5 => OperationResult<PetGrowthClassification>.Success(new(
                PetInitialGrowthQuality.Top,
                PetGrowthGrade.Top,
                PetGrowthGradeStatus.Confirmed,
                1,
                automaticGrowthTotal)),
            _ => Failure("pet.growth.initial_total_invalid", "Initial pet automatic growth must total four or five points.")
        };
    }

    public OperationResult<PetGrowthClassification> ObserveLevelUp(
        PetGrowthClassification current,
        int reachedLevel,
        int automaticGrowthDeltaTotal)
    {
        if (reachedLevel is < 2 or > 99 || automaticGrowthDeltaTotal <= 0)
        {
            return Failure("pet.growth.observation_invalid", "Pet level-up observation is outside the verified range.");
        }

        var next = current;
        if (current.InitialQuality == PetInitialGrowthQuality.Top)
        {
            next = current with
            {
                Grade = PetGrowthGrade.Top,
                Status = PetGrowthGradeStatus.Confirmed,
                ConfirmedAtLevel = current.ConfirmedAtLevel ?? 1,
                LastAutomaticGrowthTotal = automaticGrowthDeltaTotal
            };
        }
        else if (reachedLevel == 20)
        {
            next = automaticGrowthDeltaTotal switch
            {
                8 => current with
                {
                    Grade = PetGrowthGrade.Breakthrough,
                    Status = PetGrowthGradeStatus.Confirmed,
                    ConfirmedAtLevel = 20,
                    LastAutomaticGrowthTotal = automaticGrowthDeltaTotal
                },
                6 => current with
                {
                    Grade = PetGrowthGrade.Normal,
                    Status = PetGrowthGradeStatus.Provisional,
                    ConfirmedAtLevel = null,
                    LastAutomaticGrowthTotal = automaticGrowthDeltaTotal
                },
                _ => current
            };
        }
        else if (reachedLevel == 50 && current.Grade != PetGrowthGrade.Breakthrough)
        {
            next = automaticGrowthDeltaTotal switch
            {
                10 => current with
                {
                    Grade = PetGrowthGrade.LateBreakthrough,
                    Status = PetGrowthGradeStatus.Confirmed,
                    ConfirmedAtLevel = 50,
                    LastAutomaticGrowthTotal = automaticGrowthDeltaTotal
                },
                8 => current with
                {
                    Grade = PetGrowthGrade.Normal,
                    Status = PetGrowthGradeStatus.Confirmed,
                    ConfirmedAtLevel = 50,
                    LastAutomaticGrowthTotal = automaticGrowthDeltaTotal
                },
                _ => current
            };
        }
        else
        {
            next = current with { LastAutomaticGrowthTotal = automaticGrowthDeltaTotal };
        }

        var rule = _catalog.Resolve(next.Grade, reachedLevel);
        if (!rule.Succeeded || rule.Value is null || rule.Value.AutomaticPointsPerLevel != automaticGrowthDeltaTotal)
        {
            return Failure(
                "pet.growth.delta_mismatch",
                "Observed automatic growth does not match the verified pet grade rule.");
        }

        return OperationResult<PetGrowthClassification>.Success(next);
    }

    public OperationResult<PetAutomaticGrowthAllocation> ResolveAutomaticGrowth(
        int categoryId,
        PetGrowthGrade grade,
        int reachedLevel) =>
        _catalog.ResolveAllocation(categoryId, grade, reachedLevel);

    public OperationResult<PetCoreStats> CalculateCoreStats(
        int categoryId,
        PetGrowthGrade grade,
        int level)
    {
        if (level is < 1 or > 99)
        {
            return OperationResult<PetCoreStats>.Failure(
                "pet.growth.level_invalid",
                "Battle-pet stat calculation requires a level from 1 through 99.",
                nameof(PetGrowthGradeEngine));
        }

        var stats = PetBaseStatRules.Create();
        for (var reachedLevel = 1; reachedLevel <= level; reachedLevel++)
        {
            var allocation = _catalog.ResolveAllocation(categoryId, grade, reachedLevel);
            if (!allocation.Succeeded || allocation.Value is null)
            {
                return OperationResult<PetCoreStats>.Failure(
                    allocation.Error.Code,
                    allocation.Error.Message,
                    allocation.Error.Source);
            }

            stats = new PetCoreStats(
                checked(stats.Constitution + allocation.Value.ConstitutionDelta),
                checked(stats.Strength + allocation.Value.StrengthDelta),
                checked(stats.Intelligence + allocation.Value.IntelligenceDelta),
                checked(stats.Speed + allocation.Value.SpeedDelta));
        }

        return OperationResult<PetCoreStats>.Success(stats);
    }

    private static OperationResult<PetGrowthClassification> Failure(string code, string message) =>
        OperationResult<PetGrowthClassification>.Failure(code, message, nameof(PetGrowthGradeEngine));
}
