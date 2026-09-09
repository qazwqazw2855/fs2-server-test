using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace God2.ClassicServer.Runtime;

public enum MonsterAiActionType
{
    BasicAttack,
    UseSkill,
    HealAlly,
    BuffAlly,
    DebuffEnemy,
    Flee,
    Defend
}

public enum MonsterTargetPolicy
{
    LowestHp,
    HighestThreat
}

public sealed record MonsterAiSkill(
    int SkillId,
    MonsterAiActionType ActionType,
    long MpCost,
    int CooldownRounds,
    int Priority);

public sealed record MonsterAiDefinition(
    int MonsterTemplateId,
    MonsterTargetPolicy TargetPolicy,
    IReadOnlyList<MonsterAiSkill> Skills,
    int HealThresholdBasisPoints,
    int FleeThresholdBasisPoints,
    int BossPhaseThresholdBasisPoints,
    int? BossPhaseSkillId,
    string EvidenceConfidence);

public sealed record MonsterAiParticipant(
    Guid ParticipantId,
    bool Ally,
    long CurrentHp,
    long MaximumHp,
    long CurrentMp,
    long Threat,
    bool Alive,
    IReadOnlySet<string> Statuses);

public sealed record MonsterAiContext(
    int Round,
    MonsterAiDefinition Definition,
    MonsterAiParticipant Actor,
    IReadOnlyList<MonsterAiParticipant> Participants,
    IReadOnlyDictionary<int, int> CooldownReadyRounds);

public sealed record MonsterAiDecision(
    MonsterAiActionType Action,
    int? SkillId,
    Guid? TargetId,
    string Reason,
    string EvidenceConfidence);

public sealed class DataDrivenMonsterAiPolicy
{
    public MonsterAiDecision Decide(MonsterAiContext context)
    {
        if (context.Round <= 0 || !context.Actor.Alive)
        {
            return Decision(MonsterAiActionType.Defend, null, null, "actor_unavailable", context);
        }

        var allies = context.Participants.Where(participant => participant.Ally && participant.Alive).ToArray();
        var enemies = context.Participants.Where(participant => !participant.Ally && participant.Alive).ToArray();
        if (enemies.Length == 0)
        {
            return Decision(MonsterAiActionType.Defend, null, null, "no_living_enemy", context);
        }

        if (context.Actor.Statuses.Contains("Stunned") || context.Actor.Statuses.Contains("SilencedAndRooted"))
        {
            return Decision(MonsterAiActionType.Defend, null, context.Actor.ParticipantId, "status_restricted", context);
        }

        var actorHp = Ratio(context.Actor.CurrentHp, context.Actor.MaximumHp);
        if (context.Definition.BossPhaseSkillId is int phaseSkill &&
            actorHp <= context.Definition.BossPhaseThresholdBasisPoints &&
            TrySkill(context, phaseSkill, out var phase))
        {
            return Decision(MonsterAiActionType.UseSkill, phase.SkillId, SelectEnemy(context.Definition.TargetPolicy, enemies).ParticipantId, "boss_phase", context);
        }

        var lowestAlly = allies.OrderBy(participant => Ratio(participant.CurrentHp, participant.MaximumHp)).FirstOrDefault();
        var heal = context.Definition.Skills
            .Where(skill => skill.ActionType == MonsterAiActionType.HealAlly && Available(context, skill))
            .OrderByDescending(skill => skill.Priority)
            .FirstOrDefault();
        if (lowestAlly is not null && heal is not null && Ratio(lowestAlly.CurrentHp, lowestAlly.MaximumHp) <= context.Definition.HealThresholdBasisPoints)
        {
            return Decision(MonsterAiActionType.HealAlly, heal.SkillId, lowestAlly.ParticipantId, "lowest_hp_ally", context);
        }

        var buff = context.Definition.Skills
            .Where(skill => skill.ActionType == MonsterAiActionType.BuffAlly && Available(context, skill))
            .OrderByDescending(skill => skill.Priority)
            .FirstOrDefault();
        var unbuffed = allies.FirstOrDefault(participant => !participant.Statuses.Contains("Buffed"));
        if (buff is not null && unbuffed is not null)
        {
            return Decision(MonsterAiActionType.BuffAlly, buff.SkillId, unbuffed.ParticipantId, "buff_missing", context);
        }

        var debuff = context.Definition.Skills
            .Where(skill => skill.ActionType == MonsterAiActionType.DebuffEnemy && Available(context, skill))
            .OrderByDescending(skill => skill.Priority)
            .FirstOrDefault();
        var debuffTarget = enemies.OrderByDescending(participant => participant.Threat).FirstOrDefault(participant => !participant.Statuses.Contains("Debuffed"));
        if (debuff is not null && debuffTarget is not null)
        {
            return Decision(MonsterAiActionType.DebuffEnemy, debuff.SkillId, debuffTarget.ParticipantId, "highest_threat_debuff", context);
        }

        if (actorHp <= context.Definition.FleeThresholdBasisPoints)
        {
            return Decision(MonsterAiActionType.Flee, null, null, "low_hp_flee_candidate", context);
        }

        var attackSkill = context.Definition.Skills
            .Where(skill => skill.ActionType == MonsterAiActionType.UseSkill && Available(context, skill))
            .OrderByDescending(skill => skill.Priority)
            .FirstOrDefault();
        var target = SelectEnemy(context.Definition.TargetPolicy, enemies);
        return attackSkill is not null
            ? Decision(MonsterAiActionType.UseSkill, attackSkill.SkillId, target.ParticipantId, "skill_available", context)
            : Decision(MonsterAiActionType.BasicAttack, null, target.ParticipantId, "basic_attack_fallback", context);
    }

    private static bool TrySkill(MonsterAiContext context, int skillId, out MonsterAiSkill skill)
    {
        skill = context.Definition.Skills.FirstOrDefault(value => value.SkillId == skillId)!;
        return skill is not null && Available(context, skill);
    }

    private static bool Available(MonsterAiContext context, MonsterAiSkill skill) =>
        context.Actor.CurrentMp >= skill.MpCost &&
        context.CooldownReadyRounds.GetValueOrDefault(skill.SkillId) <= context.Round;

    private static MonsterAiParticipant SelectEnemy(MonsterTargetPolicy policy, IReadOnlyList<MonsterAiParticipant> enemies) =>
        policy == MonsterTargetPolicy.HighestThreat
            ? enemies.OrderByDescending(participant => participant.Threat).ThenBy(participant => participant.ParticipantId).First()
            : enemies.OrderBy(participant => Ratio(participant.CurrentHp, participant.MaximumHp)).ThenBy(participant => participant.ParticipantId).First();

    private static int Ratio(long current, long maximum) => maximum <= 0 ? 0 : (int)Math.Clamp(checked(current * 10_000 / maximum), 0, 10_000);

    private static MonsterAiDecision Decision(
        MonsterAiActionType action,
        int? skillId,
        Guid? targetId,
        string reason,
        MonsterAiContext context) =>
        new(action, skillId, targetId, reason, context.Definition.EvidenceConfidence);
}

public readonly record struct NavigationPoint(int X, int Y);

public enum NavigationResultCode
{
    Success,
    StartBlocked,
    GoalBlocked,
    Unreachable,
    InvalidPoint
}

public sealed record NavigationResult(
    NavigationResultCode ResultCode,
    IReadOnlyList<NavigationPoint> Path,
    int ExpandedNodes,
    bool NoCornerCutting,
    bool Replanned);

public sealed class NavigationGrid
{
    private static readonly (int X, int Y)[] Directions =
    [
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (-1, 1), (1, -1), (1, 1)
    ];

    private readonly bool[,] _walkable;

    public NavigationGrid(bool[,] walkable)
    {
        ArgumentNullException.ThrowIfNull(walkable);
        if (walkable.GetLength(0) == 0 || walkable.GetLength(1) == 0)
        {
            throw new ArgumentException("Navigation grid cannot be empty.", nameof(walkable));
        }
        _walkable = (bool[,])walkable.Clone();
    }

    public int Width => _walkable.GetLength(0);

    public int Height => _walkable.GetLength(1);

    public bool IsWalkable(NavigationPoint point) =>
        point.X >= 0 && point.Y >= 0 && point.X < Width && point.Y < Height && _walkable[point.X, point.Y];

    public NavigationResult FindPath(
        NavigationPoint start,
        NavigationPoint goal,
        IReadOnlySet<NavigationPoint>? temporaryBlocked = null)
    {
        if (!Inside(start) || !Inside(goal))
        {
            return new NavigationResult(NavigationResultCode.InvalidPoint, [], 0, true, temporaryBlocked is not null);
        }
        if (!Available(start, temporaryBlocked))
        {
            return new NavigationResult(NavigationResultCode.StartBlocked, [], 0, true, temporaryBlocked is not null);
        }
        if (!Available(goal, temporaryBlocked))
        {
            return new NavigationResult(NavigationResultCode.GoalBlocked, [], 0, true, temporaryBlocked is not null);
        }

        var frontier = new PriorityQueue<NavigationPoint, (int Cost, int Tie)>();
        var cameFrom = new Dictionary<NavigationPoint, NavigationPoint>();
        var costs = new Dictionary<NavigationPoint, int> { [start] = 0 };
        var tie = 0;
        frontier.Enqueue(start, (0, tie++));
        var expanded = 0;
        while (frontier.TryDequeue(out var current, out _))
        {
            expanded++;
            if (current == goal)
            {
                return new NavigationResult(
                    NavigationResultCode.Success,
                    Reconstruct(cameFrom, start, goal),
                    expanded,
                    true,
                    temporaryBlocked is not null);
            }

            foreach (var direction in Directions)
            {
                var next = new NavigationPoint(current.X + direction.X, current.Y + direction.Y);
                if (!Available(next, temporaryBlocked) ||
                    direction.X != 0 && direction.Y != 0 &&
                    (!Available(new NavigationPoint(current.X + direction.X, current.Y), temporaryBlocked) ||
                     !Available(new NavigationPoint(current.X, current.Y + direction.Y), temporaryBlocked)))
                {
                    continue;
                }

                var nextCost = costs[current] + (direction.X == 0 || direction.Y == 0 ? 10 : 14);
                if (costs.TryGetValue(next, out var known) && known <= nextCost)
                {
                    continue;
                }
                costs[next] = nextCost;
                cameFrom[next] = current;
                frontier.Enqueue(next, (nextCost + Heuristic(next, goal), tie++));
            }
        }

        return new NavigationResult(NavigationResultCode.Unreachable, [], expanded, true, temporaryBlocked is not null);
    }

    public bool CanReach(NavigationPoint start, NavigationPoint target) =>
        FindPath(start, target).ResultCode == NavigationResultCode.Success;

    public IReadOnlySet<NavigationPoint> ReachableRegion(NavigationPoint origin, int maximumSteps)
    {
        if (!IsWalkable(origin) || maximumSteps < 0)
        {
            return new HashSet<NavigationPoint>();
        }

        var visited = new HashSet<NavigationPoint> { origin };
        var queue = new Queue<(NavigationPoint Point, int Steps)>();
        queue.Enqueue((origin, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Steps >= maximumSteps)
            {
                continue;
            }
            foreach (var direction in Directions.Take(4))
            {
                var next = new NavigationPoint(current.Point.X + direction.X, current.Point.Y + direction.Y);
                if (IsWalkable(next) && visited.Add(next))
                {
                    queue.Enqueue((next, current.Steps + 1));
                }
            }
        }
        return visited;
    }

    private bool Inside(NavigationPoint point) => point.X >= 0 && point.Y >= 0 && point.X < Width && point.Y < Height;

    private bool Available(NavigationPoint point, IReadOnlySet<NavigationPoint>? temporaryBlocked) =>
        IsWalkable(point) && (temporaryBlocked is null || !temporaryBlocked.Contains(point));

    private static int Heuristic(NavigationPoint point, NavigationPoint goal)
    {
        var x = Math.Abs(point.X - goal.X);
        var y = Math.Abs(point.Y - goal.Y);
        return (14 * Math.Min(x, y)) + (10 * Math.Abs(x - y));
    }

    private static IReadOnlyList<NavigationPoint> Reconstruct(
        IReadOnlyDictionary<NavigationPoint, NavigationPoint> cameFrom,
        NavigationPoint start,
        NavigationPoint goal)
    {
        var path = new List<NavigationPoint> { goal };
        var current = goal;
        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}

public sealed class NavigationStuckDetector
{
    private readonly int _repeatThreshold;
    private NavigationPoint? _last;
    private int _repeats;

    public NavigationStuckDetector(int repeatThreshold = 3)
    {
        if (repeatThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatThreshold));
        }
        _repeatThreshold = repeatThreshold;
    }

    public bool Observe(NavigationPoint point)
    {
        if (_last == point)
        {
            _repeats++;
        }
        else
        {
            _last = point;
            _repeats = 1;
        }
        return _repeats >= _repeatThreshold;
    }
}

public sealed record HeadlessGameplaySnapshot(
    string SessionId,
    CharacterProgressionSnapshot Progression,
    MountSnapshot Mount,
    PetSnapshot Pet,
    int MapId,
    NavigationPoint Position,
    long Version);

public interface IHeadlessGameplaySnapshotStore
{
    void Save(HeadlessGameplaySnapshot snapshot);
    HeadlessGameplaySnapshot? Load(string sessionId);
}

public sealed class InMemoryHeadlessGameplaySnapshotStore : IHeadlessGameplaySnapshotStore
{
    private readonly ConcurrentDictionary<string, HeadlessGameplaySnapshot> _snapshots = new(StringComparer.Ordinal);

    public void Save(HeadlessGameplaySnapshot snapshot) => _snapshots[snapshot.SessionId] = snapshot;

    public HeadlessGameplaySnapshot? Load(string sessionId) => _snapshots.GetValueOrDefault(sessionId);
}

public sealed record HeadlessGameplayLoopPorts(
    Func<CancellationToken, Task<bool>> Quest,
    Func<CancellationToken, Task<bool>> Npc,
    Func<CancellationToken, Task<bool>> Portal,
    Func<CancellationToken, Task<bool>> Encounter,
    Func<CancellationToken, Task<bool>> Battle,
    Func<CancellationToken, Task<bool>> Reward,
    Func<CancellationToken, Task<bool>> Equipment);

public sealed record HeadlessGameplayLoopResult(
    bool Succeeded,
    IReadOnlyList<string> CompletedStages,
    string FailureStage,
    CharacterProgressionSnapshot Progression,
    int FakeNetworkBytes);

public enum HeadlessSessionPhase
{
    Login,
    Character,
    World,
    Disconnected
}

public sealed class HeadlessGameplaySession
{
    private readonly IHeadlessGameplaySnapshotStore _store;
    private long _version;

    public HeadlessGameplaySession(
        string sessionId,
        long characterId,
        GameplayClass @class,
        ConservativeProgressionCatalog progressionCatalog,
        IEnumerable<MountDefinition> mounts,
        IEnumerable<PetDefinition> pets,
        IHeadlessGameplaySnapshotStore store,
        HeadlessGameplaySnapshot? restored = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        SessionId = sessionId;
        _store = store;
        Progression = new CharacterProgressionRuntime(characterId, @class, progressionCatalog, restored?.Progression);
        Mount = new MountRuntime(characterId, mounts, restored?.Mount);
        Pet = new PetRuntime(characterId, pets, restored?.Pet);
        MapId = restored?.MapId ?? 1;
        Position = restored?.Position ?? new NavigationPoint(0, 0);
        _version = restored?.Version ?? 0;
        Phase = restored is null ? HeadlessSessionPhase.Login : HeadlessSessionPhase.World;
    }

    public string SessionId { get; }
    public CharacterProgressionRuntime Progression { get; }
    public MountRuntime Mount { get; }
    public PetRuntime Pet { get; }
    public int MapId { get; private set; }
    public NavigationPoint Position { get; private set; }
    public HeadlessSessionPhase Phase { get; private set; }
    public int FakeNetworkBytes => 0;

    public void SetWorldLocation(int mapId, NavigationPoint position)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }
        MapId = mapId;
        Position = position;
        _version++;
    }

    public void Save()
    {
        _store.Save(new HeadlessGameplaySnapshot(SessionId, Progression.Snapshot, Mount.Snapshot, Pet.Snapshot, MapId, Position, ++_version));
    }

    public async Task<HeadlessGameplayLoopResult> ExecuteLoopAsync(
        HeadlessGameplayLoopPorts ports,
        long experienceReward,
        CancellationToken cancellationToken = default)
    {
        var completed = new List<string>();
        if (Phase == HeadlessSessionPhase.Login)
        {
            completed.Add("Login");
            Phase = HeadlessSessionPhase.Character;
        }
        if (Phase == HeadlessSessionPhase.Character)
        {
            completed.Add("Character");
            Phase = HeadlessSessionPhase.World;
        }
        if (Phase != HeadlessSessionPhase.World)
        {
            return new HeadlessGameplayLoopResult(false, completed, "WorldSession", Progression.Snapshot, 0);
        }
        completed.Add("World");
        var stages = new (string Name, Func<CancellationToken, Task<bool>> Action)[]
        {
            ("Quest", ports.Quest),
            ("NPC", ports.Npc),
            ("Portal", ports.Portal),
            ("Encounter", ports.Encounter),
            ("Battle", ports.Battle),
            ("Reward", ports.Reward),
            ("Equipment", ports.Equipment)
        };
        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await stage.Action(cancellationToken))
            {
                return new HeadlessGameplayLoopResult(false, completed, stage.Name, Progression.Snapshot, 0);
            }
            completed.Add(stage.Name);
        }

        var award = Progression.AwardExperience($"{SessionId}:loop:{_version + 1}:experience", experienceReward);
        if (award.ResultCode is not ExperienceAwardResultCode.Success and not ExperienceAwardResultCode.MaximumLevelReached)
        {
            return new HeadlessGameplayLoopResult(false, completed, "Experience", Progression.Snapshot, 0);
        }
        completed.Add("Experience");
        completed.Add("ReturnToWorld");
        Save();
        return new HeadlessGameplayLoopResult(true, completed, string.Empty, Progression.Snapshot, 0);
    }

    public static HeadlessGameplaySession Reconnect(
        string sessionId,
        ConservativeProgressionCatalog progressionCatalog,
        IEnumerable<MountDefinition> mounts,
        IEnumerable<PetDefinition> pets,
        IHeadlessGameplaySnapshotStore store)
    {
        var snapshot = store.Load(sessionId) ?? throw new InvalidOperationException("Headless gameplay snapshot is missing.");
        return new HeadlessGameplaySession(
            sessionId,
            snapshot.Progression.CharacterId,
            snapshot.Progression.Class,
            progressionCatalog,
            mounts,
            pets,
            store,
            snapshot);
    }
}
