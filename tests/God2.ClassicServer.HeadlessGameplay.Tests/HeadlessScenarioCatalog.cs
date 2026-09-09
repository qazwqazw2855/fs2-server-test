using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.HeadlessGameplay.Tests;

public sealed record HeadlessScenario(int Id, string Category, string Feature, string Variant)
{
    public override string ToString() => $"{Id:000}-{Category}-{Feature}-{Variant}";
}

public static class HeadlessScenarioCatalog
{
    public static IReadOnlyList<HeadlessScenario> All { get; } = Build();

    public static TheoryData<HeadlessScenario> TheoryData
    {
        get
        {
            var data = new TheoryData<HeadlessScenario>();
            foreach (var scenario in All)
            {
                data.Add(scenario);
            }
            return data;
        }
    }

    private static IReadOnlyList<HeadlessScenario> Build()
    {
        var scenarios = new List<HeadlessScenario>();
        AddEach(scenarios, "FourClass", Enum.GetNames<GameplayClass>(), ["baseline"]);
        AddEach(scenarios, "Level", ["1", "2", "5", "10", "20", "30", "40", "50", "75", "100"], ["threshold"]);
        AddEach(scenarios, "Skill", Enum.GetNames<GeneralizedSkillFamily>(), ["catalog-driven"]);
        AddEach(scenarios, "Item",
            ["Add", "Remove", "Move", "Swap", "Split", "Merge", "Grant", "Consume", "Capacity", "InvalidItem"],
            ["success", "duplicate", "invalid", "rollback", "reconnect"]);
        AddEach(scenarios, "Equipment",
            ["EquipArmor", "EquipWeapon", "UnequipArmor", "UnequipWeapon"],
            ["success", "wrong-slot", "class-restricted", "level-restricted", "rollback"]);
        AddEach(scenarios, "Commerce",
            ["Buy", "Sell", "Craft", "QuestLinkedCraft"],
            ["success", "insufficient", "inventory-full", "duplicate", "rollback"]);
        AddEach(scenarios, "Quest",
            ["Accept", "Abandon", "Kill", "Item", "Npc", "Portal", "Battle", "Craft", "Submit", "Reward"],
            ["success", "guarded", "reconnect"]);
        AddEach(scenarios, "Party",
            [
                "Invite", "Accept", "Reject", "Leader", "LeaderTransfer", "Kick", "Leave", "Disband",
                "Reconnect", "SameMap", "Distance", "SharedEncounter", "PartyBattle", "ExpDistribution",
                "RewardDistribution", "QuestSharing", "HealMember", "BuffMember", "InvalidTarget", "DuplicateInvite"
            ],
            ["semantic"]);
        AddEach(scenarios, "MountPet",
            [
                "MountAcquire", "MountEquip", "MountUnequip", "MountOn", "MountOff", "MountLoyalty",
                "MountRideRejected", "MountMapRestriction", "MountBattleRestriction", "MountReconnect",
                "PetAcquire", "PetDeploy", "PetWithdraw", "PetWalk", "PetRecall", "PetActiveSlot",
                "PetBattle", "PetDeath", "PetExperience", "PetReconnect"
            ],
            ["semantic"]);
        AddEach(scenarios, "Battle",
            [
                "BasicAttack", "Skill", "Defend", "FleeSuccess", "FleeFailure", "Damage", "Healing", "Buff",
                "Debuff", "StatusTick", "Death", "Revive", "RoundEnd", "BattleEnd", "Reward", "Experience",
                "LevelUp", "QuestEvent", "ItemDrop", "MonsterAi"
            ],
            ["success", "invalid-target", "duplicate", "stale-round", "timeout"]);
        AddEach(scenarios, "FailureInjection",
            ["Inventory", "Equipment", "Crafting", "Quest", "Party", "Battle", "Reward", "Persistence", "Reconnect", "Companion"],
            ["before-plan", "before-commit", "after-commit", "retry", "rollback"]);
        AddEach(scenarios, "Reconnect",
            ["Progression", "Inventory", "Equipment", "Quest", "Party", "Battle", "Mount", "Pet", "World", "Idempotency"],
            ["clean", "interrupted"]);
        AddEach(scenarios, "ConcurrentMutation",
            ["Experience", "Inventory", "Equipment", "Crafting", "Quest", "Party", "Battle", "Reward", "Mount", "Pet"],
            ["distinct-keys", "duplicate-key"]);
        AddEach(scenarios, "Navigation",
            ["AStar", "NoCornerCutting", "PortalReachability", "NpcReachability", "MonsterRegion", "Replan", "StuckDetection", "BlockedStart", "BlockedGoal", "Unreachable"],
            ["static", "dynamic"]);

        if (scenarios.Count != 404 || scenarios.Select(value => value.ToString()).Distinct(StringComparer.Ordinal).Count() != 404)
        {
            throw new InvalidOperationException("Headless scenario matrix must contain exactly 404 non-duplicate cases.");
        }
        return scenarios;
    }

    private static void AddEach(
        ICollection<HeadlessScenario> scenarios,
        string category,
        IReadOnlyList<string> features,
        IReadOnlyList<string> variants)
    {
        foreach (var feature in features)
        {
            foreach (var variant in variants)
            {
                scenarios.Add(new HeadlessScenario(scenarios.Count + 1, category, feature, variant));
            }
        }
    }
}
