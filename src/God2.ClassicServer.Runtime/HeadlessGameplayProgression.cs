using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum GameplayClass
{
    Swordsman,
    Taoist,
    Pharmacist,
    Warlock
}

public sealed record ProgressionStats(
    long MaximumHp,
    long MaximumMp,
    long Attack,
    long Defense,
    long Spirit,
    long Speed);

public sealed record PrimaryAttributePoints(
    int Constitution,
    int Strength,
    int Intelligence,
    int Speed)
{
    public static PrimaryAttributePoints Zero { get; } = new(0, 0, 0, 0);

    public int Total => checked(Constitution + Strength + Intelligence + Speed);

    public PrimaryAttributePoints AddScaled(PrimaryAttributePoints value, int multiplier) =>
        new(
            checked(Constitution + (value.Constitution * multiplier)),
            checked(Strength + (value.Strength * multiplier)),
            checked(Intelligence + (value.Intelligence * multiplier)),
            checked(Speed + (value.Speed * multiplier)));

    public bool IsNonNegative =>
        Constitution >= 0 && Strength >= 0 && Intelligence >= 0 && Speed >= 0;
}

public sealed record CharacterAttributeGrowthRule(
    GameplayClass Class,
    PrimaryAttributePoints AutomaticPerLevel,
    int ManualPointsPerLevel,
    string EvidenceConfidence);

public sealed record PetAttributePointBudget(
    int AutomaticPointsPerLevel,
    int ManualPointsPerLevel,
    string EvidenceConfidence);

public sealed class OfficialAttributeGrowthCatalog
{
    private readonly IReadOnlyDictionary<GameplayClass, CharacterAttributeGrowthRule> _characterRules;

    public OfficialAttributeGrowthCatalog(
        IEnumerable<CharacterAttributeGrowthRule> characterRules,
        PetAttributePointBudget petBudget)
    {
        ArgumentNullException.ThrowIfNull(characterRules);
        PetBudget = petBudget ?? throw new ArgumentNullException(nameof(petBudget));
        _characterRules = new ReadOnlyDictionary<GameplayClass, CharacterAttributeGrowthRule>(
            characterRules.ToDictionary(value => value.Class));

        if (Enum.GetValues<GameplayClass>().Any(value => !_characterRules.ContainsKey(value)) ||
            _characterRules.Values.Any(value =>
                !value.AutomaticPerLevel.IsNonNegative ||
                value.AutomaticPerLevel.Total != 6 ||
                value.ManualPointsPerLevel != 4))
        {
            throw new ArgumentException(
                "Every character class must provide six automatic and four manual points per level.",
                nameof(characterRules));
        }

        if (PetBudget.AutomaticPointsPerLevel != 4 || PetBudget.ManualPointsPerLevel != 1)
        {
            throw new ArgumentException(
                "Pet growth must provide four automatic and one manual point per level.",
                nameof(petBudget));
        }
    }

    public PetAttributePointBudget PetBudget { get; }

    public CharacterAttributeGrowthRule CharacterRule(GameplayClass @class) => _characterRules[@class];

    public PrimaryAttributePoints AutomaticGrowthAt(GameplayClass @class, int level)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        return PrimaryAttributePoints.Zero.AddScaled(CharacterRule(@class).AutomaticPerLevel, level - 1);
    }

    public static OfficialAttributeGrowthCatalog CreateDefault() => new(
        [
            new(GameplayClass.Swordsman, new(2, 3, 0, 1), 4, "OfficialCharacterLevelAllocation"),
            new(GameplayClass.Taoist, new(1, 0, 3, 2), 4, "OfficialCharacterLevelAllocation"),
            new(GameplayClass.Pharmacist, new(2, 0, 2, 2), 4, "OfficialCharacterLevelAllocation"),
            new(GameplayClass.Warlock, new(1, 1, 1, 3), 4, "OfficialCharacterLevelAllocation")
        ],
        new PetAttributePointBudget(4, 1, "OfficialPetLevelPointBudget"));
}

public sealed record ClassGrowthProfile(
    GameplayClass Class,
    ProgressionStats BaseStats,
    ProgressionStats PerLevelGrowth,
    string EvidenceConfidence);

public sealed record SkillUnlockRule(GameplayClass Class, int Level, int SkillId, string SkillCode);

public sealed record CharacterProgressionSnapshot(
    long CharacterId,
    GameplayClass Class,
    int Level,
    long TotalExperience,
    ProgressionStats Stats,
    IReadOnlyList<int> UnlockedSkillIds,
    long Version,
    PrimaryAttributePoints? AutomaticAttributeGrowth = null,
    int UnspentAttributePoints = 0);

public enum ExperienceAwardResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    InvalidAmount,
    MaximumLevelReached
}

public sealed record ExperienceAwardResult(
    ExperienceAwardResultCode ResultCode,
    long AppliedExperience,
    int PreviousLevel,
    int CurrentLevel,
    int LevelsGained,
    IReadOnlyList<int> NewlyUnlockedSkillIds,
    CharacterProgressionSnapshot Snapshot,
    string EvidenceConfidence);

public sealed class ConservativeProgressionCatalog
{
    private readonly IReadOnlyDictionary<GameplayClass, ClassGrowthProfile> _profiles;
    private readonly IReadOnlyList<SkillUnlockRule> _unlocks;

    public ConservativeProgressionCatalog(
        int maximumLevel,
        IEnumerable<ClassGrowthProfile> profiles,
        IEnumerable<SkillUnlockRule> unlocks,
        OfficialAttributeGrowthCatalog? attributeGrowth = null)
    {
        if (maximumLevel < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLevel));
        }

        MaximumLevel = maximumLevel;
        _profiles = new ReadOnlyDictionary<GameplayClass, ClassGrowthProfile>(
            profiles.ToDictionary(profile => profile.Class));
        _unlocks = unlocks.OrderBy(unlock => unlock.Level).ThenBy(unlock => unlock.SkillId).ToArray();
        AttributeGrowth = attributeGrowth ?? OfficialAttributeGrowthCatalog.CreateDefault();
        if (Enum.GetValues<GameplayClass>().Any(value => !_profiles.ContainsKey(value)))
        {
            throw new ArgumentException("Every gameplay class requires a growth profile.", nameof(profiles));
        }
    }

    public int MaximumLevel { get; }

    public OfficialAttributeGrowthCatalog AttributeGrowth { get; }

    public string EvidenceConfidence => "ConservativeDataDriven";

    public ClassGrowthProfile Profile(GameplayClass @class) => _profiles[@class];

    public long RequiredTotalExperience(int level)
    {
        if (level < 1 || level > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var completedLevels = level - 1L;
        return checked(100L * completedLevels * completedLevels);
    }

    public ProgressionStats StatsAt(GameplayClass @class, int level)
    {
        if (level < 1 || level > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var profile = Profile(@class);
        var steps = level - 1L;
        return new ProgressionStats(
            checked(profile.BaseStats.MaximumHp + (profile.PerLevelGrowth.MaximumHp * steps)),
            checked(profile.BaseStats.MaximumMp + (profile.PerLevelGrowth.MaximumMp * steps)),
            checked(profile.BaseStats.Attack + (profile.PerLevelGrowth.Attack * steps)),
            checked(profile.BaseStats.Defense + (profile.PerLevelGrowth.Defense * steps)),
            checked(profile.BaseStats.Spirit + (profile.PerLevelGrowth.Spirit * steps)),
            checked(profile.BaseStats.Speed + (profile.PerLevelGrowth.Speed * steps)));
    }

    public IReadOnlyList<int> UnlockedSkills(GameplayClass @class, int level) =>
        _unlocks
            .Where(unlock => unlock.Class == @class && unlock.Level <= level)
            .Select(unlock => unlock.SkillId)
            .Distinct()
            .Order()
            .ToArray();

    public static ConservativeProgressionCatalog CreateDefault()
    {
        var births = OfficialCharacterBirthProfiles.Snapshot().ToDictionary(profile => profile.Class);
        var profiles = new[]
        {
            Profile(GameplayClass.Swordsman, new(18, 5, 4, 3, 1, 1)),
            Profile(GameplayClass.Taoist, new(11, 16, 2, 2, 5, 1)),
            Profile(GameplayClass.Pharmacist, new(13, 14, 2, 2, 4, 1)),
            Profile(GameplayClass.Warlock, new(14, 12, 2, 3, 4, 1))
        };
        var unlocks = Enum.GetValues<GameplayClass>()
            .SelectMany(@class => Enumerable.Range(0, 5).Select(index =>
                new SkillUnlockRule(@class, 1 + (index * 5), 1000 + ((int)@class * 100) + index, $"{@class}.skill.{index}")))
            .ToArray();
        return new ConservativeProgressionCatalog(100, profiles, unlocks);

        ClassGrowthProfile Profile(GameplayClass @class, ProgressionStats perLevel)
        {
            var birth = births[@class];
            return new ClassGrowthProfile(
                @class,
                new ProgressionStats(
                    birth.MaximumHp,
                    birth.MaximumMp,
                    birth.PhysicalAttack,
                    birth.PhysicalDefense,
                    birth.MagicAttack,
                    birth.PrimaryAttributes.Speed),
                perLevel,
                "OfficialUserVerifiedBirthProfile+ConservativePerLevelGrowth");
        }
    }
}

public sealed class CharacterProgressionRuntime
{
    private readonly object _sync = new();
    private readonly ConservativeProgressionCatalog _catalog;
    private readonly Dictionary<string, (long Amount, ExperienceAwardResult Result)> _completed = new(StringComparer.Ordinal);
    private CharacterProgressionSnapshot _snapshot;

    public CharacterProgressionRuntime(
        long characterId,
        GameplayClass @class,
        ConservativeProgressionCatalog catalog,
        CharacterProgressionSnapshot? restored = null)
    {
        _catalog = catalog;
        _snapshot = restored ?? new CharacterProgressionSnapshot(
            characterId,
            @class,
            1,
            0,
            catalog.StatsAt(@class, 1),
            catalog.UnlockedSkills(@class, 1),
            0,
            PrimaryAttributePoints.Zero,
            0);
        if (_snapshot.AutomaticAttributeGrowth is null)
        {
            _snapshot = _snapshot with
            {
                AutomaticAttributeGrowth = catalog.AttributeGrowth.AutomaticGrowthAt(@class, _snapshot.Level)
            };
        }
        if (_snapshot.CharacterId != characterId || _snapshot.Class != @class ||
            _snapshot.Level < 1 || _snapshot.Level > catalog.MaximumLevel ||
            _snapshot.TotalExperience < 0 ||
            _snapshot.UnspentAttributePoints < 0 ||
            !_snapshot.AutomaticAttributeGrowth.IsNonNegative)
        {
            throw new ArgumentException("Restored progression snapshot is invalid.", nameof(restored));
        }
    }

    public CharacterProgressionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot with { UnlockedSkillIds = _snapshot.UnlockedSkillIds.ToArray() };
            }
        }
    }

    public ExperienceAwardResult AwardExperience(string idempotencyKey, long amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_sync)
        {
            if (_completed.TryGetValue(idempotencyKey, out var replay))
            {
                return replay.Amount == amount
                    ? replay.Result with { ResultCode = ExperienceAwardResultCode.DuplicateCompleted }
                    : Failure(ExperienceAwardResultCode.ReplayConflict);
            }

            if (amount <= 0)
            {
                return Failure(ExperienceAwardResultCode.InvalidAmount);
            }

            if (_snapshot.Level >= _catalog.MaximumLevel)
            {
                var maximum = Failure(ExperienceAwardResultCode.MaximumLevelReached);
                _completed[idempotencyKey] = (amount, maximum);
                return maximum;
            }

            var previous = _snapshot;
            var maximumExperience = _catalog.RequiredTotalExperience(_catalog.MaximumLevel);
            var total = Math.Min(maximumExperience, checked(previous.TotalExperience + amount));
            var level = previous.Level;
            while (level < _catalog.MaximumLevel && total >= _catalog.RequiredTotalExperience(level + 1))
            {
                level++;
            }

            var skills = _catalog.UnlockedSkills(previous.Class, level);
            var newSkills = skills.Except(previous.UnlockedSkillIds).Order().ToArray();
            var levelsGained = level - previous.Level;
            var attributeRule = _catalog.AttributeGrowth.CharacterRule(previous.Class);
            var automaticGrowth = previous.AutomaticAttributeGrowth!.AddScaled(
                attributeRule.AutomaticPerLevel,
                levelsGained);
            var unspentAttributePoints = checked(
                previous.UnspentAttributePoints + (attributeRule.ManualPointsPerLevel * levelsGained));
            _snapshot = new CharacterProgressionSnapshot(
                previous.CharacterId,
                previous.Class,
                level,
                total,
                _catalog.StatsAt(previous.Class, level),
                skills,
                previous.Version + 1,
                automaticGrowth,
                unspentAttributePoints);
            var result = new ExperienceAwardResult(
                ExperienceAwardResultCode.Success,
                total - previous.TotalExperience,
                previous.Level,
                level,
                levelsGained,
                newSkills,
                _snapshot,
                _catalog.EvidenceConfidence);
            _completed[idempotencyKey] = (amount, result);
            return result;
        }
    }

    private ExperienceAwardResult Failure(ExperienceAwardResultCode code) =>
        new(code, 0, _snapshot.Level, _snapshot.Level, 0, [], _snapshot, _catalog.EvidenceConfidence);
}

public enum GeneralizedSkillFamily
{
    BasicAttack,
    SingleTargetDamage,
    MultiTargetDamage,
    SingleTargetHeal,
    MultiTargetHeal,
    Buff,
    Debuff,
    StatusRemove,
    Revive,
    Formation,
    PositionSwap,
    SummonPet,
    Passive,
    ResourceRestore,
    DamageOverTime,
    HealOverTime,
    Shield,
    Taunt,
    Drain,
    Control
}

public enum GeneralizedTargetMode
{
    Self,
    SingleAlly,
    AllAllies,
    SingleEnemy,
    AllEnemies,
    Position,
    None
}

public sealed record GeneralizedSkillDefinition(
    int SkillId,
    GeneralizedSkillFamily Family,
    GeneralizedTargetMode TargetMode,
    long ResourceCost,
    int CooldownRounds,
    int MinimumLevel,
    IReadOnlySet<GameplayClass> AllowedClasses,
    string EvidenceConfidence);

public sealed class GeneralizedSkillCatalog
{
    private readonly IReadOnlyDictionary<int, GeneralizedSkillDefinition> _definitions;

    public GeneralizedSkillCatalog(IEnumerable<GeneralizedSkillDefinition> definitions)
    {
        _definitions = new ReadOnlyDictionary<int, GeneralizedSkillDefinition>(
            definitions.ToDictionary(definition => definition.SkillId));
    }

    public IReadOnlyCollection<GeneralizedSkillDefinition> Definitions => _definitions.Values.ToArray();

    public OperationResult<GeneralizedSkillDefinition> Resolve(int skillId) =>
        _definitions.TryGetValue(skillId, out var value)
            ? OperationResult<GeneralizedSkillDefinition>.Success(value)
            : OperationResult<GeneralizedSkillDefinition>.Failure("skill.definition_missing", "Skill definition is not available.");

    public static GeneralizedSkillCatalog CreateDefault()
    {
        var classes = Enum.GetValues<GameplayClass>().ToHashSet();
        var definitions = Enum.GetValues<GeneralizedSkillFamily>()
            .Select((family, index) => new GeneralizedSkillDefinition(
                2000 + index,
                family,
                TargetMode(family),
                family == GeneralizedSkillFamily.BasicAttack || family == GeneralizedSkillFamily.Passive ? 0 : 5 + index,
                family == GeneralizedSkillFamily.BasicAttack ? 0 : 1 + (index % 3),
                1 + (index % 10),
                classes,
                "ClientCatalogAndCrossClassEvidence"))
            .ToArray();
        return new GeneralizedSkillCatalog(definitions);
    }

    private static GeneralizedTargetMode TargetMode(GeneralizedSkillFamily family) => family switch
    {
        GeneralizedSkillFamily.Passive => GeneralizedTargetMode.None,
        GeneralizedSkillFamily.MultiTargetDamage or GeneralizedSkillFamily.Debuff => GeneralizedTargetMode.AllEnemies,
        GeneralizedSkillFamily.MultiTargetHeal or GeneralizedSkillFamily.Buff => GeneralizedTargetMode.AllAllies,
        GeneralizedSkillFamily.SingleTargetHeal or GeneralizedSkillFamily.StatusRemove or GeneralizedSkillFamily.Revive or
            GeneralizedSkillFamily.Shield or GeneralizedSkillFamily.HealOverTime or GeneralizedSkillFamily.ResourceRestore => GeneralizedTargetMode.SingleAlly,
        GeneralizedSkillFamily.Formation or GeneralizedSkillFamily.PositionSwap => GeneralizedTargetMode.Position,
        GeneralizedSkillFamily.SummonPet => GeneralizedTargetMode.Self,
        _ => GeneralizedTargetMode.SingleEnemy
    };
}

public sealed record SkillCommandEnvelopeCandidate(
    Guid ActorId,
    int SkillId,
    GeneralizedSkillFamily Family,
    GeneralizedTargetMode TargetMode,
    IReadOnlyList<Guid> TargetIds,
    int Round,
    long CommandWindow,
    int? FormationId,
    int? Position,
    uint Flags);

public sealed record SkillExecutionCommand(
    Guid ActorId,
    GeneralizedSkillDefinition Definition,
    IReadOnlyList<Guid> TargetIds,
    int Round,
    long CommandWindow,
    int? FormationId,
    int? Position,
    uint Flags);

public sealed record SkillResultProjection(
    int SkillId,
    GeneralizedSkillFamily Family,
    Guid ActorId,
    IReadOnlyList<Guid> Targets,
    long ResourceCost,
    string ProjectionStatus);

public sealed class SkillCommandGeneralizer
{
    private readonly GeneralizedSkillCatalog _catalog;

    public SkillCommandGeneralizer(GeneralizedSkillCatalog catalog) => _catalog = catalog;

    public OperationResult<SkillExecutionCommand> Translate(
        SkillCommandEnvelopeCandidate envelope,
        GameplayClass actorClass,
        int actorLevel)
    {
        var resolved = _catalog.Resolve(envelope.SkillId);
        if (!resolved.Succeeded)
        {
            return OperationResult<SkillExecutionCommand>.Failure(resolved.Error.Code, resolved.Error.Message);
        }

        var definition = resolved.Value!;
        if (definition.Family != envelope.Family || definition.TargetMode != envelope.TargetMode)
        {
            return OperationResult<SkillExecutionCommand>.Failure("skill.envelope_mismatch", "Skill family or target mode does not match the catalog.");
        }

        if (envelope.ActorId == Guid.Empty || envelope.Round <= 0 || envelope.CommandWindow < 0)
        {
            return OperationResult<SkillExecutionCommand>.Failure("skill.command_invalid", "Actor, round, and command window are required.");
        }

        if (!definition.AllowedClasses.Contains(actorClass) || actorLevel < definition.MinimumLevel)
        {
            return OperationResult<SkillExecutionCommand>.Failure("skill.requirement_failed", "Class or level requirement failed.");
        }

        var targetCount = envelope.TargetIds.Distinct().Count();
        var targetValid = definition.TargetMode switch
        {
            GeneralizedTargetMode.None or GeneralizedTargetMode.Position => targetCount == 0,
            GeneralizedTargetMode.Self => targetCount == 0 || (targetCount == 1 && envelope.TargetIds[0] == envelope.ActorId),
            GeneralizedTargetMode.SingleAlly or GeneralizedTargetMode.SingleEnemy => targetCount == 1,
            GeneralizedTargetMode.AllAllies or GeneralizedTargetMode.AllEnemies => targetCount > 0,
            _ => false
        };
        if (!targetValid)
        {
            return OperationResult<SkillExecutionCommand>.Failure("skill.target_invalid", "Target shape does not match the generalized target mode.");
        }

        if (definition.TargetMode == GeneralizedTargetMode.Position && envelope.Position is null)
        {
            return OperationResult<SkillExecutionCommand>.Failure("skill.position_required", "A position is required for this skill family.");
        }

        return OperationResult<SkillExecutionCommand>.Success(new SkillExecutionCommand(
            envelope.ActorId,
            definition,
            envelope.TargetIds.Distinct().ToArray(),
            envelope.Round,
            envelope.CommandWindow,
            envelope.FormationId,
            envelope.Position,
            envelope.Flags));
    }

    public static SkillResultProjection Project(SkillExecutionCommand command) =>
        new(
            command.Definition.SkillId,
            command.Definition.Family,
            command.ActorId,
            command.TargetIds,
            command.Definition.ResourceCost,
            "HeadlessSemanticProjectionOnly");
}

public sealed record WorldHealCharacterState(
    Guid CharacterId,
    int MapId,
    int X,
    int Y,
    long CurrentHp,
    long MaximumHp,
    long CurrentMp,
    long MaximumMp,
    bool InBattle,
    long Version);

public sealed record OutOfCombatHealRequest(
    string IdempotencyKey,
    Guid HealerId,
    Guid TargetId,
    long HealAmount,
    long MpCost,
    int MaximumDistance,
    DateTimeOffset Now,
    TimeSpan Cooldown);

public enum WorldHealResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    MissingParticipant,
    InBattle,
    DifferentMap,
    OutOfRange,
    TargetDead,
    AlreadyFullHealth,
    InsufficientMp,
    CooldownActive,
    InvalidRequest
}

public sealed record WorldHealResult(
    WorldHealResultCode ResultCode,
    long EffectiveHeal,
    WorldHealCharacterState? Healer,
    WorldHealCharacterState? Target);

public sealed class OutOfCombatHealRuntime
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, WorldHealCharacterState> _characters = [];
    private readonly Dictionary<Guid, DateTimeOffset> _cooldowns = [];
    private readonly Dictionary<string, (string Fingerprint, WorldHealResult Result)> _completed = new(StringComparer.Ordinal);

    public void Register(WorldHealCharacterState state)
    {
        if (state.CharacterId == Guid.Empty || state.MaximumHp <= 0 || state.MaximumMp < 0 ||
            state.CurrentHp < 0 || state.CurrentHp > state.MaximumHp || state.CurrentMp < 0 || state.CurrentMp > state.MaximumMp)
        {
            throw new ArgumentException("World heal character state is invalid.", nameof(state));
        }

        lock (_sync)
        {
            _characters[state.CharacterId] = state;
        }
    }

    public WorldHealResult Heal(OutOfCombatHealRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"{request.HealerId:N}:{request.TargetId:N}:{request.HealAmount}:{request.MpCost}:{request.MaximumDistance}";
            if (_completed.TryGetValue(request.IdempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = WorldHealResultCode.DuplicateCompleted }
                    : new WorldHealResult(WorldHealResultCode.ReplayConflict, 0, null, null);
            }

            if (request.HealAmount <= 0 || request.MpCost < 0 || request.MaximumDistance < 0 || request.Cooldown < TimeSpan.Zero)
            {
                return new WorldHealResult(WorldHealResultCode.InvalidRequest, 0, null, null);
            }

            var healerFound = _characters.TryGetValue(request.HealerId, out var healer);
            var targetFound = _characters.TryGetValue(request.TargetId, out var target);
            if (!healerFound || !targetFound)
            {
                return new WorldHealResult(WorldHealResultCode.MissingParticipant, 0, healer, target);
            }

            var healerState = healer!;
            var targetState = target!;

            WorldHealResult result;
            if (healerState.InBattle || targetState.InBattle)
            {
                result = new(WorldHealResultCode.InBattle, 0, healerState, targetState);
            }
            else if (healerState.MapId != targetState.MapId)
            {
                result = new(WorldHealResultCode.DifferentMap, 0, healerState, targetState);
            }
            else if (DistanceSquared(healerState, targetState) > checked((long)request.MaximumDistance * request.MaximumDistance))
            {
                result = new(WorldHealResultCode.OutOfRange, 0, healerState, targetState);
            }
            else if (targetState.CurrentHp == 0)
            {
                result = new(WorldHealResultCode.TargetDead, 0, healerState, targetState);
            }
            else if (targetState.CurrentHp >= targetState.MaximumHp)
            {
                result = new(WorldHealResultCode.AlreadyFullHealth, 0, healerState, targetState);
            }
            else if (healerState.CurrentMp < request.MpCost)
            {
                result = new(WorldHealResultCode.InsufficientMp, 0, healerState, targetState);
            }
            else if (_cooldowns.TryGetValue(healerState.CharacterId, out var until) && request.Now < until)
            {
                result = new(WorldHealResultCode.CooldownActive, 0, healerState, targetState);
            }
            else
            {
                var effective = Math.Min(request.HealAmount, targetState.MaximumHp - targetState.CurrentHp);
                if (healerState.CharacterId == targetState.CharacterId)
                {
                    healerState = healerState with
                    {
                        CurrentHp = healerState.CurrentHp + effective,
                        CurrentMp = healerState.CurrentMp - request.MpCost,
                        Version = healerState.Version + 1
                    };
                    targetState = healerState;
                    _characters[healerState.CharacterId] = healerState;
                }
                else
                {
                    healerState = healerState with { CurrentMp = healerState.CurrentMp - request.MpCost, Version = healerState.Version + 1 };
                    targetState = targetState with { CurrentHp = targetState.CurrentHp + effective, Version = targetState.Version + 1 };
                    _characters[healerState.CharacterId] = healerState;
                    _characters[targetState.CharacterId] = targetState;
                }
                _cooldowns[healerState.CharacterId] = request.Now + request.Cooldown;
                result = new(WorldHealResultCode.Success, effective, healerState, targetState);
            }

            _completed[request.IdempotencyKey] = (fingerprint, result);
            return result;
        }
    }

    public IReadOnlyDictionary<Guid, WorldHealCharacterState> Snapshot()
    {
        lock (_sync)
        {
            return new ReadOnlyDictionary<Guid, WorldHealCharacterState>(new Dictionary<Guid, WorldHealCharacterState>(_characters));
        }
    }

    private static long DistanceSquared(WorldHealCharacterState left, WorldHealCharacterState right)
    {
        var x = (long)left.X - right.X;
        var y = (long)left.Y - right.Y;
        return checked((x * x) + (y * y));
    }
}
