using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using God2.ClassicServer.Runtime;

namespace God2.AdvancedHeadlessVerification;

public enum VerificationFailureClassification
{
    InvariantViolation,
    ModelMismatch,
    DifferentialMismatch,
    ReplayDivergence,
    NonDeterministicFailure,
    TransactionAtomicityFailure,
    ExactlyOnceFailure,
    DuplicateReward,
    DuplicateQuestCompletion,
    InventoryDrift,
    EquipmentDrift,
    PartyStateDrift,
    MountStateDrift,
    PetStateDrift,
    BattleActorLeak,
    SessionLeak,
    OutboxLeak,
    JournalLeak,
    DBConnectionLeak,
    CoverageGap,
    UnreachableModelState,
    ShrinkFailure
}

public sealed record VerificationCharacter(long CharacterId, GameplayClass Class, int Level, long Hp, long Mp, bool Online);
public sealed record VerificationProgression(long Experience, int MaximumLevel, ImmutableArray<int> UnlockedSkills);
public sealed record VerificationItem(long InstanceId, int TemplateId, int Quantity, string Kind);
public sealed record VerificationStatus(int StatusId, int Stacks, long ExpiresAtTicks);
public sealed record VerificationQuest(int QuestId, string State, int Progress, int Required);
public sealed record VerificationPartyMember(long CharacterId, bool Online, int MapId, int X, int Y);
public sealed record VerificationWorld(int MapId, int X, int Y, int? PortalId, string State);
public sealed record VerificationBattle(string State, int Round, long PlayerHp, long EnemyHp, bool RewardPrepared);
public sealed record VerificationReward(string Key, long Experience, int ItemTemplateId, bool Committed);
public sealed record VerificationRng(ulong State, long DrawCount);
public sealed record VerificationClockSnapshot(long UtcTicks, long Sequence, ImmutableArray<VirtualTimerSnapshot> Timers);

public sealed record VerificationSnapshot(
    string Name,
    VerificationCharacter Character,
    VerificationProgression Progression,
    ImmutableArray<VerificationItem> Inventory,
    IReadOnlyDictionary<string, long> Equipment,
    ImmutableArray<int> Skills,
    ImmutableArray<VerificationStatus> Statuses,
    ImmutableArray<VerificationQuest> Quests,
    ImmutableArray<VerificationPartyMember> Party,
    IReadOnlyDictionary<int, int> MountLoyalty,
    IReadOnlyDictionary<int, string> PetStates,
    VerificationWorld World,
    VerificationBattle Battle,
    IReadOnlyDictionary<string, long> Cooldowns,
    ImmutableArray<VerificationReward> PendingRewards,
    ImmutableArray<string> Outbox,
    ImmutableArray<string> Journal,
    ImmutableArray<string> IdempotencyKeys,
    long PersistenceVersion,
    string ContentVersion,
    string FormulaVersion,
    VerificationRng Rng,
    VerificationClockSnapshot Clock)
{
    public VerificationSnapshot Fork(string name) => new(
        name,
        Character with { },
        Progression with { UnlockedSkills = Progression.UnlockedSkills.Select(value => value).ToImmutableArray() },
        Inventory.Select(value => value with { }).ToImmutableArray(),
        new ReadOnlyDictionary<string, long>(Equipment.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
        Skills.Select(value => value).ToImmutableArray(),
        Statuses.Select(value => value with { }).ToImmutableArray(),
        Quests.Select(value => value with { }).ToImmutableArray(),
        Party.Select(value => value with { }).ToImmutableArray(),
        new ReadOnlyDictionary<int, int>(MountLoyalty.ToDictionary(pair => pair.Key, pair => pair.Value)),
        new ReadOnlyDictionary<int, string>(PetStates.ToDictionary(pair => pair.Key, pair => pair.Value)),
        World with { },
        Battle with { },
        new ReadOnlyDictionary<string, long>(Cooldowns.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
        PendingRewards.Select(value => value with { }).ToImmutableArray(),
        Outbox.Select(value => string.Concat(value)).ToImmutableArray(),
        Journal.Select(value => string.Concat(value)).ToImmutableArray(),
        IdempotencyKeys.Select(value => string.Concat(value)).ToImmutableArray(),
        PersistenceVersion,
        string.Concat(ContentVersion),
        string.Concat(FormulaVersion),
        Rng with { },
        Clock with { Timers = Clock.Timers.Select(value => value with { }).ToImmutableArray() });

    public string StableHash() => DeterministicHash.Of(this);
}

public static class SnapshotCatalog
{
    public static IReadOnlyList<VerificationSnapshot> Create()
    {
        var epoch = DateTimeOffset.UnixEpoch.UtcTicks;
        var fresh = new VerificationSnapshot(
            "FreshCharacter",
            new VerificationCharacter(10_001, GameplayClass.Swordsman, 1, 130, 45, true),
            new VerificationProgression(0, 100, [1000]),
            [],
            ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal),
            [1000],
            [],
            [],
            [new VerificationPartyMember(10_001, true, 1, 0, 0)],
            ImmutableDictionary<int, int>.Empty,
            ImmutableDictionary<int, string>.Empty,
            new VerificationWorld(1, 0, 0, null, "Character"),
            new VerificationBattle("None", 0, 130, 0, false),
            ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal),
            [],
            [],
            [],
            [],
            0,
            "client-content-v1",
            "conservative-formula-v1",
            new VerificationRng(0x5A17UL, 0),
            new VerificationClockSnapshot(epoch, 0, []));

        var world = fresh with { Name = "WorldReady", World = fresh.World with { State = "World", X = 10, Y = 10 }, PersistenceVersion = 1 };
        var quest = world with { Name = "QuestAccepted", Quests = [new VerificationQuest(2001, "Accepted", 0, 3)], PersistenceVersion = 2 };
        var encounter = quest with { Name = "BeforeEncounter", World = quest.World with { State = "Encounter", X = 12 } };
        var command = encounter with { Name = "BattleCommandWindow", Battle = new VerificationBattle("CommandWindow", 1, 130, 100, false) };
        var settlement = command with { Name = "BeforeSettlement", Battle = command.Battle with { State = "Settlement", EnemyHp = 0 } };
        var reward = settlement with
        {
            Name = "BeforeRewardCommit",
            Battle = settlement.Battle with { RewardPrepared = true },
            PendingRewards = [new VerificationReward("battle:1", 100, 3001, false)],
            Outbox = ["reward.prepared"]
        };
        var submit = quest with { Name = "BeforeQuestSubmit", Quests = [new VerificationQuest(2001, "Complete", 3, 3)] };
        var nearlyFull = world with
        {
            Name = "InventoryNearlyFull",
            Inventory = Enumerable.Range(0, 15).Select(index => new VerificationItem(50_000 + index, 3000 + index, 1, "Item")).ToImmutableArray()
        };
        var party = world with
        {
            Name = "PartyReady",
            Party = Enumerable.Range(0, 4).Select(index => new VerificationPartyMember(10_001 + index, true, 1, 10 + index, 10)).ToImmutableArray()
        };
        var mount = world with { Name = "MountActive", MountLoyalty = ImmutableDictionary<int, int>.Empty.Add(4001, 80) };
        var pet = world with { Name = "PetDeployed", PetStates = ImmutableDictionary<int, string>.Empty.Add(5001, "Deployed") };
        var reconnect = command with { Name = "ReconnectCheckpoint", Character = command.Character with { Online = false }, PersistenceVersion = 8 };
        var recovery = reward with
        {
            Name = "FailureRecoveryCheckpoint",
            Battle = reward.Battle with { State = "RecoveryRequired" },
            Journal = ["reward.prepare", "checkpoint.saved"],
            IdempotencyKeys = ["battle:1:reward"],
            PersistenceVersion = 9
        };

        return [fresh, world, quest, encounter, command, settlement, reward, submit, nearlyFull, party, mount, pet, reconnect, recovery];
    }
}

public sealed record ModelTransition(
    string OperationId,
    string FromState,
    string ToState,
    string ExpectedResultClass,
    string Branch,
    string Invariant,
    string ShrinkStrategy);

public sealed record GameplayModelDefinition(
    string Name,
    string InitialState,
    ImmutableArray<string> States,
    ImmutableArray<ModelTransition> Transitions);

public static class GameplayModelCatalog
{
    public static IReadOnlyList<GameplayModelDefinition> Create() =>
    [
        Model("CharacterModel", "Fresh", ["Fresh", "Ready", "Disconnected"],
            T("Create", "Fresh", "Ready"), T("GainExperience", "Ready", "Ready"), T("Reconnect", "Disconnected", "Ready"), T("Disconnect", "Ready", "Disconnected")),
        Model("InventoryModel", "Empty", ["Empty", "HasItems", "NearlyFull", "Full"],
            T("Add", "Empty", "HasItems"), T("Remove", "HasItems", "Empty"), T("Move", "HasItems", "HasItems"), T("Split", "HasItems", "HasItems"), T("Merge", "HasItems", "HasItems"), T("Swap", "NearlyFull", "NearlyFull"), T("Fill", "HasItems", "NearlyFull"), T("FillLast", "NearlyFull", "Full"), T("RemoveFromFull", "Full", "NearlyFull")),
        Model("EquipmentModel", "Unequipped", ["Unequipped", "Weapon", "Armor", "Both", "InventoryFull"],
            T("EquipWeapon", "Unequipped", "Weapon"), T("EquipArmorOnly", "Unequipped", "Armor"), T("EquipArmor", "Weapon", "Both"), T("EquipWeaponAfterArmor", "Armor", "Both"), T("UnequipArmor", "Both", "Weapon"), T("UnequipWeaponFromBoth", "Both", "Armor"), T("SwapWeapon", "Weapon", "Weapon"), T("UnequipWeapon", "Weapon", "Unequipped"), T("InventoryCapacityLost", "Both", "InventoryFull"), T("Rollback", "InventoryFull", "Both")),
        Model("ProgressionModel", "Level1", ["Level1", "NearThreshold", "Leveled", "MaxLevel"],
            T("GainExperience", "Level1", "NearThreshold"), T("LevelUp", "NearThreshold", "Leveled"), T("MultiLevel", "Leveled", "Leveled"), T("ReachMaximum", "Leveled", "MaxLevel"), T("MaxLevelAward", "MaxLevel", "MaxLevel", "MaximumLevelReached")),
        Model("SkillModel", "Ready", ["Ready", "Cooldown", "NoMp", "InvalidTarget"],
            T("Execute", "Ready", "Cooldown"), T("CooldownElapsed", "Cooldown", "Ready"), T("ConsumeMp", "Ready", "NoMp"), T("RestoreMp", "NoMp", "Ready"), T("SelectInvalidTarget", "Ready", "InvalidTarget", "InvalidTarget"), T("Retarget", "InvalidTarget", "Ready")),
        Model("QuestModel", "NotAccepted", ["NotAccepted", "Accepted", "Complete", "Submitted", "Abandoned"],
            T("Accept", "NotAccepted", "Accepted"), T("Progress", "Accepted", "Accepted"), T("CraftObjective", "Accepted", "Complete"), T("BattleObjective", "Accepted", "Complete"), T("Submit", "Complete", "Submitted"), T("Abandon", "Accepted", "Abandoned"), T("Repeat", "Submitted", "Accepted")),
        Model("PartyModel", "Solo", ["Solo", "Invited", "Member", "Leader", "Disbanded"],
            T("Invite", "Solo", "Invited"), T("Accept", "Invited", "Member"), T("PromoteLeader", "Member", "Leader"), T("TransferLeader", "Leader", "Member"), T("Leave", "Member", "Solo"), T("Kick", "Leader", "Leader"), T("SharedEncounter", "Member", "Member"), T("Disband", "Leader", "Disbanded"), T("Reject", "Invited", "Solo")),
        Model("MountModel", "NotOwned", ["NotOwned", "Owned", "Equipped", "Active", "LowLoyalty"],
            T("Acquire", "NotOwned", "Owned"), T("Equip", "Owned", "Equipped"), T("Activate", "Equipped", "Active"), T("Deactivate", "Active", "Equipped"), T("LoyaltyChange", "Active", "LowLoyalty"), T("RestoreLoyalty", "LowLoyalty", "Equipped"), T("Unequip", "Equipped", "Owned")),
        Model("PetModel", "NotOwned", ["NotOwned", "Owned", "Deployed", "Withdrawn", "Dead"],
            T("Acquire", "NotOwned", "Owned"), T("Deploy", "Owned", "Deployed"), T("Withdraw", "Deployed", "Withdrawn"), T("Redeploy", "Withdrawn", "Deployed"), T("Death", "Deployed", "Dead"), T("Revive", "Dead", "Withdrawn"), T("LoyaltyChange", "Deployed", "Deployed")),
        Model("WorldModel", "Character", ["Character", "World", "Portal", "Encounter", "Disconnected"],
            T("EnterWorld", "Character", "World"), T("UsePortal", "World", "Portal"), T("PortalComplete", "Portal", "World"), T("StartEncounter", "World", "Encounter"), T("ReturnToWorld", "Encounter", "World"), T("Disconnect", "World", "Disconnected"), T("Reconnect", "Disconnected", "World")),
        Model("NavigationModel", "Start", ["Start", "Pathing", "Replanned", "Stuck", "Reached"],
            T("Plan", "Start", "Pathing"), T("DynamicObstacle", "Pathing", "Replanned"), T("Resume", "Replanned", "Pathing"), T("DetectStuck", "Pathing", "Stuck"), T("Recover", "Stuck", "Pathing"), T("Arrive", "Pathing", "Reached")),
        Model("BattleModel", "Created", ["Created", "CommandWindow", "Resolving", "Settlement", "Completed", "Recovery"],
            T("OpenRound", "Created", "CommandWindow"), T("BasicAttack", "CommandWindow", "Resolving"), T("Skill", "CommandWindow", "Resolving"), T("Defend", "CommandWindow", "Resolving"), T("Flee", "CommandWindow", "Settlement"), T("Resolve", "Resolving", "CommandWindow"), T("BattleEnd", "Resolving", "Settlement"), T("Settlement", "Settlement", "Completed"), T("Disconnect", "Resolving", "Recovery"), T("Recover", "Recovery", "CommandWindow"), T("Timeout", "CommandWindow", "Resolving")),
        Model("RewardModel", "None", ["None", "Prepared", "Committed", "Recovered"],
            T("Prepare", "None", "Prepared"), T("Commit", "Prepared", "Committed"), T("DuplicateCommit", "Committed", "Committed", "DuplicateCompleted"), T("FailBeforeCommit", "Prepared", "Prepared", "PersistenceFailure"), T("Recover", "Prepared", "Recovered"), T("CommitRecovered", "Recovered", "Committed")),
        Model("SessionModel", "Login", ["Login", "World", "Disconnected", "Reconnected", "Expired"],
            T("EnterWorld", "Login", "World"), T("Disconnect", "World", "Disconnected"), T("Reconnect", "Disconnected", "Reconnected"), T("Resume", "Reconnected", "World"), T("Timeout", "Disconnected", "Expired")),
        Model("PersistenceModel", "Clean", ["Clean", "Dirty", "Checkpointed", "Recovery", "Conflict"],
            T("Mutate", "Clean", "Dirty"), T("Checkpoint", "Dirty", "Checkpointed"), T("Commit", "Checkpointed", "Clean"), T("Crash", "Dirty", "Recovery"), T("Recover", "Recovery", "Clean"), T("ConcurrentMutation", "Dirty", "Conflict"), T("Retry", "Conflict", "Dirty"))
    ];

    private static GameplayModelDefinition Model(string name, string initial, string[] states, params ModelTransition[] transitions) =>
        new(name, initial, states.ToImmutableArray(), transitions.ToImmutableArray());

    private static ModelTransition T(
        string operation,
        string from,
        string to,
        string result = "Success") =>
        new(operation, from, to, result, $"{from}->{to}:{result}", "NoForbiddenStateChange", "RemoveUnrelatedOperationsThenShrinkValues");
}

public static class DeterministicHash
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Of<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string OfText(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class VerificationGuard
{
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
