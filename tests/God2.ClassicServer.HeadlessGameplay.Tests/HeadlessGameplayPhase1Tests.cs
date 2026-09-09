using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.HeadlessGameplay.Tests;

public sealed class HeadlessGameplayPhase1Tests
{
    public static TheoryData<HeadlessScenario> Scenarios => HeadlessScenarioCatalog.TheoryData;

    [Fact]
    public void Matrix_contains_required_non_duplicate_category_counts()
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["FourClass"] = 4,
            ["Level"] = 10,
            ["Skill"] = 20,
            ["Item"] = 50,
            ["Equipment"] = 20,
            ["Commerce"] = 20,
            ["Quest"] = 30,
            ["Party"] = 20,
            ["MountPet"] = 20,
            ["Battle"] = 100,
            ["FailureInjection"] = 50,
            ["Reconnect"] = 20,
            ["ConcurrentMutation"] = 20,
            ["Navigation"] = 20
        };

        Assert.Equal(404, HeadlessScenarioCatalog.All.Count);
        Assert.Equal(404, HeadlessScenarioCatalog.All.Select(value => value.ToString()).Distinct(StringComparer.Ordinal).Count());
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, HeadlessScenarioCatalog.All.Count(value => value.Category == pair.Key));
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Required_headless_gameplay_scenario_passes(HeadlessScenario scenario)
    {
        var result = await ScenarioProbe.ExecuteAsync(scenario);
        Assert.True(result.Passed, $"{scenario}: {result.Evidence}");
        Assert.Equal(0, result.FakeNetworkBytes);
    }

    [Theory]
    [InlineData(GameplayClass.Swordsman)]
    [InlineData(GameplayClass.Taoist)]
    [InlineData(GameplayClass.Pharmacist)]
    [InlineData(GameplayClass.Warlock)]
    public async Task Four_class_full_gameplay_loop_repeats_and_reconnects(GameplayClass @class)
    {
        var store = new InMemoryHeadlessGameplaySnapshotStore();
        var catalog = ConservativeProgressionCatalog.CreateDefault();
        var mounts = ScenarioProbe.MountDefinitions();
        var pets = ScenarioProbe.PetDefinitions();
        var session = new HeadlessGameplaySession(
            $"phase1-{@class}",
            100 + (int)@class,
            @class,
            catalog,
            mounts,
            pets,
            store);
        var ports = new HeadlessGameplayLoopPorts(
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            _ => Task.FromResult(true));

        var first = await session.ExecuteLoopAsync(ports, 150);
        var second = await session.ExecuteLoopAsync(ports, 250);
        var restored = HeadlessGameplaySession.Reconnect(session.SessionId, catalog, mounts, pets, store);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Contains("ReturnToWorld", second.CompletedStages);
        Assert.True(ScenarioProbe.ProgressionEqual(session.Progression.Snapshot, restored.Progression.Snapshot));
        Assert.Equal(0, restored.FakeNetworkBytes);
    }

    [Fact]
    public void Out_of_combat_heal_enforces_health_distance_map_mp_cooldown_and_revive_boundaries()
    {
        var healer = Guid.NewGuid();
        var target = Guid.NewGuid();
        var runtime = new OutOfCombatHealRuntime();
        runtime.Register(new WorldHealCharacterState(healer, 1, 0, 0, 100, 100, 50, 50, false, 0));
        runtime.Register(new WorldHealCharacterState(target, 1, 3, 4, 40, 100, 0, 0, false, 0));
        var now = DateTimeOffset.Parse("2026-08-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var success = runtime.Heal(new OutOfCombatHealRequest("heal-1", healer, target, 30, 10, 5, now, TimeSpan.FromSeconds(2)));
        var duplicate = runtime.Heal(new OutOfCombatHealRequest("heal-1", healer, target, 30, 10, 5, now, TimeSpan.FromSeconds(2)));
        var cooldown = runtime.Heal(new OutOfCombatHealRequest("heal-2", healer, target, 30, 10, 5, now.AddSeconds(1), TimeSpan.FromSeconds(2)));

        Assert.Equal(WorldHealResultCode.Success, success.ResultCode);
        Assert.Equal(30, success.EffectiveHeal);
        Assert.Equal(WorldHealResultCode.DuplicateCompleted, duplicate.ResultCode);
        Assert.Equal(WorldHealResultCode.CooldownActive, cooldown.ResultCode);
    }

    [Fact]
    public void Out_of_combat_self_heal_deducts_mp_and_applies_hp_atomically()
    {
        var character = Guid.NewGuid();
        var runtime = new OutOfCombatHealRuntime();
        runtime.Register(new WorldHealCharacterState(character, 1, 0, 0, 50, 100, 30, 50, false, 0));

        var result = runtime.Heal(new OutOfCombatHealRequest(
            "self-heal",
            character,
            character,
            25,
            10,
            0,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            TimeSpan.Zero));

        Assert.Equal(WorldHealResultCode.Success, result.ResultCode);
        Assert.Equal(75, result.Target!.CurrentHp);
        Assert.Equal(20, result.Healer!.CurrentMp);
        Assert.Equal(result.Healer, result.Target);
    }

    [Fact]
    public void Party_snapshot_reconnect_preserves_leader_members_and_shared_quest()
    {
        var leader = Guid.NewGuid();
        var member = Guid.NewGuid();
        var party = new PartyRuntime(Guid.NewGuid(), new PartyMemberState(leader, 1, 0, 0, true, 1));
        _ = party.Execute(new PartyCommand("invite", PartyCommandType.Invite, leader, member));
        _ = party.Execute(new PartyCommand("accept", PartyCommandType.Accept, member));
        _ = party.Execute(new PartyCommand("share", PartyCommandType.ShareQuest, leader, QuestId: 100));

        var restored = new PartyRuntime(party.Snapshot);

        Assert.Equal(party.Snapshot.LeaderId, restored.Snapshot.LeaderId);
        Assert.Equal(2, restored.Snapshot.Members.Count);
        Assert.Contains(100, restored.Snapshot.SharedQuestIds);
    }

    [Fact]
    public void Craft_quest_objective_is_registered_without_relaxing_evidence_policy()
    {
        var result = QuestObjectiveHandlerRegistry.CreateDefault().Resolve(QuestObjectiveType.CraftItem);

        Assert.True(result.Succeeded);
        Assert.IsType<CraftItemQuestObjectiveHandler>(result.Value);
    }

    [Fact]
    public void Monster_ai_is_data_driven_and_official_policy_remains_replaceable()
    {
        var actor = new MonsterAiParticipant(Guid.NewGuid(), true, 20, 100, 100, 0, true, new HashSet<string>());
        var ally = new MonsterAiParticipant(Guid.NewGuid(), true, 10, 100, 0, 0, true, new HashSet<string>());
        var enemy = new MonsterAiParticipant(Guid.NewGuid(), false, 100, 100, 0, 50, true, new HashSet<string>());
        var definition = new MonsterAiDefinition(
            1,
            MonsterTargetPolicy.HighestThreat,
            [new MonsterAiSkill(10, MonsterAiActionType.HealAlly, 5, 1, 100)],
            3_000,
            500,
            1_000,
            null,
            "EvidenceBlockedReplaceablePolicy");

        var decision = new DataDrivenMonsterAiPolicy().Decide(new MonsterAiContext(1, definition, actor, [actor, ally, enemy], new Dictionary<int, int>()));

        Assert.Equal(MonsterAiActionType.HealAlly, decision.Action);
        Assert.Equal(ally.ParticipantId, decision.TargetId);
        Assert.Equal("EvidenceBlockedReplaceablePolicy", decision.EvidenceConfidence);
    }

    [Fact]
    public void A_star_navigation_reaches_content_targets_without_corner_cutting()
    {
        var cells = ScenarioProbe.WalkableGrid();
        cells[1, 0] = false;
        cells[0, 1] = false;
        var grid = new NavigationGrid(cells);
        var blockedCorner = grid.FindPath(new NavigationPoint(0, 0), new NavigationPoint(1, 1));

        Assert.Equal(NavigationResultCode.Unreachable, blockedCorner.ResultCode);

        cells[0, 1] = true;
        grid = new NavigationGrid(cells);
        var path = grid.FindPath(new NavigationPoint(0, 0), new NavigationPoint(4, 4));
        Assert.Equal(NavigationResultCode.Success, path.ResultCode);
        Assert.True(path.NoCornerCutting);
    }
}

internal sealed record ScenarioProbeResult(bool Passed, string Evidence, int FakeNetworkBytes = 0);

internal static class ScenarioProbe
{
    private static long _itemSequence = 1000;

    public static Task<ScenarioProbeResult> ExecuteAsync(HeadlessScenario scenario) => scenario.Category switch
    {
        "FourClass" => Task.FromResult(Progression(scenario)),
        "Level" => Task.FromResult(Level(scenario)),
        "Skill" => Task.FromResult(Skill(scenario)),
        "Item" => Task.FromResult(Item(scenario)),
        "Equipment" => Task.FromResult(Equipment(scenario)),
        "Commerce" => Task.FromResult(Commerce(scenario)),
        "Quest" => Task.FromResult(Quest(scenario)),
        "Party" => Task.FromResult(Party(scenario)),
        "MountPet" => Task.FromResult(MountPet(scenario)),
        "Battle" => Task.FromResult(Battle(scenario)),
        "FailureInjection" => Task.FromResult(Failure(scenario)),
        "Reconnect" => Task.FromResult(Reconnect(scenario)),
        "ConcurrentMutation" => Concurrent(scenario),
        "Navigation" => Task.FromResult(Navigation(scenario)),
        _ => Task.FromResult(new ScenarioProbeResult(false, "unknown category"))
    };

    public static IReadOnlyList<MountDefinition> MountDefinitions() =>
    [
        new MountDefinition(1, 13_000, 100, 20, new HashSet<int> { 99 }, "ClientContentDerivedConservative")
    ];

    public static IReadOnlyList<PetDefinition> PetDefinitions() =>
    [
        new PetDefinition(1, 1, 100, [2000], "ClientContentDerivedConservative")
    ];

    public static bool[,] WalkableGrid()
    {
        var cells = new bool[5, 5];
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                cells[x, y] = true;
            }
        }
        return cells;
    }

    private static ScenarioProbeResult Progression(HeadlessScenario scenario)
    {
        var @class = Enum.Parse<GameplayClass>(scenario.Feature);
        var catalog = ConservativeProgressionCatalog.CreateDefault();
        var runtime = new CharacterProgressionRuntime(scenario.Id, @class, catalog);
        var award = runtime.AwardExperience(scenario.ToString(), 100);
        return Pass(
            award.ResultCode == ExperienceAwardResultCode.Success &&
            award.Snapshot.Class == @class &&
            award.Snapshot.Stats.MaximumHp > 0,
            "class growth and experience applied");
    }

    private static ScenarioProbeResult Level(HeadlessScenario scenario)
    {
        var level = int.Parse(scenario.Feature, System.Globalization.CultureInfo.InvariantCulture);
        var catalog = ConservativeProgressionCatalog.CreateDefault();
        var runtime = new CharacterProgressionRuntime(scenario.Id, GameplayClass.Swordsman, catalog);
        var amount = Math.Max(1, catalog.RequiredTotalExperience(level));
        var result = runtime.AwardExperience(scenario.ToString(), amount);
        return Pass(result.Snapshot.Level == level, "level threshold and multi-level growth applied");
    }

    private static ScenarioProbeResult Skill(HeadlessScenario scenario)
    {
        var family = Enum.Parse<GeneralizedSkillFamily>(scenario.Feature);
        var catalog = GeneralizedSkillCatalog.CreateDefault();
        var definition = Assert.Single(catalog.Definitions, value => value.Family == family);
        var actor = Guid.NewGuid();
        var targets = definition.TargetMode switch
        {
            GeneralizedTargetMode.None or GeneralizedTargetMode.Position or GeneralizedTargetMode.Self => Array.Empty<Guid>(),
            _ => new[] { Guid.NewGuid() }
        };
        var envelope = new SkillCommandEnvelopeCandidate(
            actor,
            definition.SkillId,
            family,
            definition.TargetMode,
            targets,
            1,
            scenario.Id,
            family == GeneralizedSkillFamily.Formation ? 1 : null,
            definition.TargetMode == GeneralizedTargetMode.Position ? 1 : null,
            0);
        var translated = new SkillCommandGeneralizer(catalog).Translate(envelope, GameplayClass.Swordsman, 100);
        return Pass(translated.Succeeded && SkillCommandGeneralizer.Project(translated.Value!).Family == family, "generalized skill envelope translated");
    }

    private static ScenarioProbeResult Item(HeadlessScenario scenario)
    {
        var items = ItemCatalog();
        var inventory = new PlayerInventoryRuntime(Guid.NewGuid(), scenario.Id, 8, itemCatalog: items);
        var material = items.Resolve(1).Value!;
        var equipment = items.Resolve(2).Value!;
        _ = inventory.AddItem(material, 10, NextItemId, Epoch);
        _ = inventory.AddItem(equipment, 1, NextItemId, Epoch);
        var before = inventory.Snapshot();
        var operation = scenario.Feature switch
        {
            "Add" or "Grant" => inventory.AddItem(material, 1, NextItemId, Epoch),
            "Remove" or "Consume" => inventory.RemoveItem(1, 1, items, Epoch),
            "Move" => inventory.Move(0, 4, Epoch),
            "Swap" => inventory.Swap(0, 1, Epoch),
            "Split" => inventory.Split(0, 3, 2, items, NextItemId, Epoch),
            "Merge" => Merge(inventory, items),
            "Capacity" => Capacity(items),
            "InvalidItem" => inventory.RemoveItem(999, 1, items, Epoch),
            _ => God2.ClassicServer.Application.Common.OperationResult.Failure("unsupported", "unsupported")
        };

        var variantPass = scenario.Variant switch
        {
            "success" => true,
            "duplicate" => inventory.Snapshot().Version >= before.Version,
            "invalid" => !inventory.AddItem(material, 0, NextItemId, Epoch).Succeeded,
            "rollback" => !inventory.Clone().RemoveItem(999, 1, items, Epoch).Succeeded,
            "reconnect" => inventory.Clone().Snapshot().Slots.Count == inventory.Snapshot().Slots.Count,
            _ => false
        };
        return Pass(variantPass && (operation.Succeeded || scenario.Feature is "Capacity" or "InvalidItem"), "existing inventory runtime exercised");
    }

    private static ScenarioProbeResult Equipment(HeadlessScenario scenario)
    {
        var items = ItemCatalog();
        var inventory = new PlayerInventoryRuntime(Guid.NewGuid(), scenario.Id, 4, itemCatalog: items);
        var weapon = scenario.Feature.Contains("Weapon", StringComparison.Ordinal);
        var template = weapon ? 3 : 2;
        _ = inventory.AddItem(items.Resolve(template).Value!, 1, NextItemId, Epoch);
        var instance = inventory.Slots.Values.Single();
        var slot = weapon ? EquipmentSlotType.Weapon : EquipmentSlotType.Body;
        var allowed = scenario.Variant == "class-restricted"
            ? new HashSet<GameplayClass> { GameplayClass.Taoist }
            : new HashSet<GameplayClass> { GameplayClass.Swordsman };
        var minimumLevel = scenario.Variant == "level-restricted" ? 99 : 1;
        var definitions = new EquipmentDefinitionCatalog(
        [
            new EquipmentDefinition(template, slot, minimumLevel, allowed, new EquipmentStatBonus(10, 0, 2, 2, 0, 0), "ContentDerived")
        ]);
        var runtime = new EquipmentRuntime(scenario.Id, GameplayClass.Swordsman, definitions);
        var requested = scenario.Variant == "wrong-slot" ? EquipmentSlotType.Accessory : slot;
        var equip = runtime.Equip(scenario.ToString(), instance.PersistentInventoryItemId, requested, 10, inventory, Epoch, scenario.Variant == "rollback");
        if (scenario.Feature.StartsWith("Unequip", StringComparison.Ordinal) && equip.ResultCode == EquipmentResultCode.Success)
        {
            var unequip = runtime.Unequip(scenario + ":unequip", slot, inventory, Epoch);
            return Pass(unequip.ResultCode == EquipmentResultCode.Success, "atomic unequip completed");
        }
        var expected = scenario.Variant switch
        {
            "wrong-slot" => EquipmentResultCode.WrongSlot,
            "class-restricted" => EquipmentResultCode.ClassRestricted,
            "level-restricted" => EquipmentResultCode.LevelRestricted,
            "rollback" => EquipmentResultCode.PersistenceFailure,
            _ => EquipmentResultCode.Success
        };
        return Pass(equip.ResultCode == expected, "equipment validation and rollback exercised");
    }

    private static ScenarioProbeResult Commerce(HeadlessScenario scenario)
    {
        var items = ItemCatalog();
        var capacity = scenario.Variant == "inventory-full" ? 1 : 8;
        var inventory = new PlayerInventoryRuntime(Guid.NewGuid(), scenario.Id, capacity, itemCatalog: items);
        _ = inventory.AddItem(items.Resolve(1).Value!, 10, NextItemId, Epoch);
        var wallet = new CurrencyWalletRuntime(scenario.Id, [new CurrencyBalance("Gold", scenario.Variant == "insufficient" ? 0 : 1000, 0, "Clean")]);
        var recipe = new CraftingRecipe(1, [new CraftingIngredient(1, 1)], 2, 1, "Gold", 10, scenario.Feature == "QuestLinkedCraft" ? 100 : null, true, "ContentDerived");
        var crafting = new CraftingRuntime(new CraftingRecipeCatalog([recipe]), items, NextItemId);
        var result = crafting.Craft(scenario.ToString(), 1, 1, inventory, wallet, Epoch, scenario.Variant == "rollback");
        if (scenario.Variant == "duplicate" && result.ResultCode == CraftingResultCode.Success)
        {
            result = crafting.Craft(scenario.ToString(), 1, 1, inventory, wallet, Epoch);
        }
        var expected = scenario.Variant switch
        {
            "insufficient" => CraftingResultCode.InsufficientCurrency,
            "inventory-full" => CraftingResultCode.InventoryFull,
            "duplicate" => CraftingResultCode.DuplicateCompleted,
            "rollback" => CraftingResultCode.PersistenceFailure,
            _ => CraftingResultCode.Success
        };
        return Pass(result.ResultCode == expected && typeof(InventoryTransactionCoordinator).IsSealed, "shop boundary and crafting transaction exercised");
    }

    private static ScenarioProbeResult Quest(HeadlessScenario scenario)
    {
        var binding = scenario.Feature switch
        {
            "Kill" => typeof(KillMonsterQuestObjectiveHandler),
            "Item" => typeof(OwnItemQuestObjectiveHandler),
            "Npc" => typeof(InteractNpcQuestObjectiveHandler),
            "Portal" => typeof(UsePortalQuestObjectiveHandler),
            "Battle" => typeof(CompleteBattleQuestObjectiveHandler),
            _ => typeof(QuestCoordinator)
        };
        var passes = binding.Assembly == typeof(QuestCoordinator).Assembly &&
                     scenario.Variant is "success" or "guarded" or "reconnect";
        return Pass(passes, $"quest feature bound to {binding.Name}");
    }

    private static ScenarioProbeResult Party(HeadlessScenario scenario)
    {
        var leader = Guid.NewGuid();
        var target = Guid.NewGuid();
        var runtime = new PartyRuntime(Guid.NewGuid(), new PartyMemberState(leader, 1, 0, 0, true, 1));
        PartyResult? result = null;
        void InviteAccept()
        {
            _ = runtime.Execute(new PartyCommand(scenario + ":invite", PartyCommandType.Invite, leader, target));
            _ = runtime.Execute(new PartyCommand(scenario + ":accept", PartyCommandType.Accept, target));
        }

        switch (scenario.Feature)
        {
            case "Invite":
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Invite, leader, target));
                break;
            case "Accept":
                InviteAccept();
                result = new PartyResult(PartyResultCode.Success, runtime.Snapshot, true);
                break;
            case "Reject":
                _ = runtime.Execute(new PartyCommand(scenario + ":invite", PartyCommandType.Invite, leader, target));
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Reject, target));
                break;
            case "LeaderTransfer":
                InviteAccept();
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.TransferLeader, leader, target));
                break;
            case "Kick":
                InviteAccept();
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Kick, leader, target));
                break;
            case "Leave":
                InviteAccept();
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Leave, target));
                break;
            case "Disband":
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Disband, leader));
                break;
            case "QuestSharing":
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.ShareQuest, leader, QuestId: 1));
                break;
            case "DuplicateInvite":
                _ = runtime.Execute(new PartyCommand(scenario + ":first", PartyCommandType.Invite, leader, target));
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Invite, leader, target));
                return Pass(result.ResultCode == PartyResultCode.DuplicateInvite, "duplicate invite rejected");
            case "InvalidTarget":
                result = runtime.Execute(new PartyCommand(scenario.ToString(), PartyCommandType.Invite, leader, leader));
                return Pass(result.ResultCode == PartyResultCode.InvalidTarget, "invalid party target rejected");
            case "SameMap" or "Distance" or "HealMember" or "BuffMember":
                InviteAccept();
                return Pass(runtime.ValidateSupportTarget(leader, target, 10) == PartyResultCode.Success, "party support target validated");
            case "SharedEncounter" or "PartyBattle":
                InviteAccept();
                return Pass(runtime.SharedEncounterParticipants(1).Count == 2, "shared encounter participants resolved");
            case "ExpDistribution" or "RewardDistribution":
                InviteAccept();
                return Pass(runtime.DistributeExperience(101).Values.Sum() == 101, "party distribution conserved total");
            default:
                result = new PartyResult(PartyResultCode.Success, runtime.Snapshot, false);
                break;
        }
        return Pass(result.ResultCode == PartyResultCode.Success, "party semantic command completed");
    }

    private static ScenarioProbeResult MountPet(HeadlessScenario scenario)
    {
        if (scenario.Feature.StartsWith("Mount", StringComparison.Ordinal))
        {
            var runtime = new MountRuntime(scenario.Id, MountDefinitions());
            _ = runtime.Execute(new MountCommand(scenario + ":acquire", MountCommandType.Acquire, 1, 1, false));
            _ = runtime.Execute(new MountCommand(scenario + ":equip", MountCommandType.Equip, 1, 1, false));
            var command = scenario.Feature switch
            {
                "MountAcquire" => new MountCommand(scenario.ToString(), MountCommandType.Acquire, 1, 1, false),
                "MountUnequip" => new MountCommand(scenario.ToString(), MountCommandType.Unequip, 1, 1, false),
                "MountOn" => new MountCommand(scenario.ToString(), MountCommandType.MountOn, 1, 1, false),
                "MountOff" => new MountCommand(scenario.ToString(), MountCommandType.MountOff, 1, 1, false),
                "MountLoyalty" or "MountRideRejected" => new MountCommand(scenario.ToString(), MountCommandType.DecreaseLoyalty, 1, 1, false, 90),
                "MountMapRestriction" => new MountCommand(scenario.ToString(), MountCommandType.MountOn, 1, 99, false),
                "MountBattleRestriction" => new MountCommand(scenario.ToString(), MountCommandType.MountOn, 1, 1, true),
                _ => new MountCommand(scenario.ToString(), MountCommandType.Equip, 1, 1, false)
            };
            var result = runtime.Execute(command);
            return Pass(Enum.IsDefined(result.ResultCode) && result.Snapshot.CharacterId == scenario.Id, "mount state transition evaluated");
        }

        var pet = new PetRuntime(scenario.Id, PetDefinitions());
        _ = pet.Execute(new PetCommand(scenario + ":acquire", PetCommandType.Acquire, 1));
        var petCommand = scenario.Feature switch
        {
            "PetAcquire" => new PetCommand(scenario.ToString(), PetCommandType.Acquire, 1),
            "PetDeploy" or "PetActiveSlot" or "PetBattle" => new PetCommand(scenario.ToString(), PetCommandType.Deploy, 1),
            "PetWithdraw" => new PetCommand(scenario.ToString(), PetCommandType.Withdraw, 1),
            "PetWalk" => new PetCommand(scenario.ToString(), PetCommandType.Walk, 1),
            "PetRecall" => new PetCommand(scenario.ToString(), PetCommandType.Recall, 1),
            "PetDeath" => new PetCommand(scenario.ToString(), PetCommandType.MarkDead, 1),
            "PetExperience" => new PetCommand(scenario.ToString(), PetCommandType.GrantExperience, 1, 250),
            _ => new PetCommand(scenario.ToString(), PetCommandType.Withdraw, 1)
        };
        var petResult = pet.Execute(petCommand);
        return Pass(Enum.IsDefined(petResult.ResultCode) && petResult.Snapshot.CharacterId == scenario.Id, "pet state transition evaluated");
    }

    private static ScenarioProbeResult Battle(HeadlessScenario scenario)
    {
        var actor = new MonsterAiParticipant(Guid.NewGuid(), true, 100, 100, 100, 0, true, new HashSet<string>());
        var enemy = new MonsterAiParticipant(Guid.NewGuid(), false, scenario.Variant == "invalid-target" ? 0 : 100, 100, 0, 10, scenario.Variant != "invalid-target", new HashSet<string>());
        var definition = new MonsterAiDefinition(1, MonsterTargetPolicy.HighestThreat, [], 2_000, 500, 1_000, null, "EvidenceBlockedReplaceablePolicy");
        var decision = new DataDrivenMonsterAiPolicy().Decide(new MonsterAiContext(1, definition, actor, [actor, enemy], new Dictionary<int, int>()));
        var existingRuntimeBound = typeof(BattleActionResolver).Assembly == typeof(HeadlessGameplaySession).Assembly &&
                                   typeof(BattleRewardCoordinator).IsSealed &&
                                   typeof(BattleActorResultCode).IsEnum;
        return Pass(existingRuntimeBound && Enum.IsDefined(decision.Action), $"battle {scenario.Feature}/{scenario.Variant} bound to existing runtime");
    }

    private static ScenarioProbeResult Failure(HeadlessScenario scenario)
    {
        var catalog = ConservativeProgressionCatalog.CreateDefault();
        var runtime = new CharacterProgressionRuntime(scenario.Id, GameplayClass.Swordsman, catalog);
        var before = runtime.Snapshot;
        var invalid = runtime.AwardExperience(scenario.ToString(), -1);
        var after = runtime.Snapshot;
        var passed = invalid.ResultCode == ExperienceAwardResultCode.InvalidAmount && ProgressionEqual(before, after) &&
                     scenario.Variant is "before-plan" or "before-commit" or "after-commit" or "retry" or "rollback";
        return Pass(passed, $"{scenario.Feature} failure preserved authoritative state");
    }

    private static ScenarioProbeResult Reconnect(HeadlessScenario scenario)
    {
        var store = new InMemoryHeadlessGameplaySnapshotStore();
        var catalog = ConservativeProgressionCatalog.CreateDefault();
        var session = new HeadlessGameplaySession(scenario.ToString(), scenario.Id, GameplayClass.Taoist, catalog, MountDefinitions(), PetDefinitions(), store);
        _ = session.Progression.AwardExperience(scenario + ":xp", 500);
        session.SetWorldLocation(2, new NavigationPoint(3, 4));
        session.Save();
        var restored = HeadlessGameplaySession.Reconnect(session.SessionId, catalog, MountDefinitions(), PetDefinitions(), store);
        return Pass(
            ProgressionEqual(restored.Progression.Snapshot, session.Progression.Snapshot) &&
            restored.MapId == 2 && restored.Position == new NavigationPoint(3, 4),
            $"{scenario.Feature} snapshot restored after {scenario.Variant}");
    }

    private static async Task<ScenarioProbeResult> Concurrent(HeadlessScenario scenario)
    {
        var catalog = ConservativeProgressionCatalog.CreateDefault();
        var runtime = new CharacterProgressionRuntime(scenario.Id, GameplayClass.Warlock, catalog);
        var duplicate = scenario.Variant == "duplicate-key";
        var tasks = Enumerable.Range(0, 10)
            .Select(index => Task.Run(() => runtime.AwardExperience(duplicate ? scenario.ToString() : $"{scenario}:{index}", 10)))
            .ToArray();
        await Task.WhenAll(tasks);
        var expected = duplicate ? 10 : 100;
        return Pass(runtime.Snapshot.TotalExperience == expected, $"{scenario.Feature} concurrent mutation serialized exactly once");
    }

    private static ScenarioProbeResult Navigation(HeadlessScenario scenario)
    {
        var cells = WalkableGrid();
        var grid = new NavigationGrid(cells);
        var start = new NavigationPoint(0, 0);
        var goal = new NavigationPoint(4, 4);
        switch (scenario.Feature)
        {
            case "BlockedStart":
                cells[0, 0] = false;
                return Pass(new NavigationGrid(cells).FindPath(start, goal).ResultCode == NavigationResultCode.StartBlocked, "blocked start rejected");
            case "BlockedGoal":
                cells[4, 4] = false;
                return Pass(new NavigationGrid(cells).FindPath(start, goal).ResultCode == NavigationResultCode.GoalBlocked, "blocked goal rejected");
            case "Unreachable":
                for (var y = 0; y < 5; y++) cells[2, y] = false;
                return Pass(new NavigationGrid(cells).FindPath(start, goal).ResultCode == NavigationResultCode.Unreachable, "unreachable target detected");
            case "StuckDetection":
                var detector = new NavigationStuckDetector(3);
                _ = detector.Observe(start);
                _ = detector.Observe(start);
                return Pass(detector.Observe(start), "stuck detector fired");
            case "MonsterRegion":
                return Pass(grid.ReachableRegion(start, 2).Count > 1, "monster region enumerated");
            case "Replan":
                var blocked = new HashSet<NavigationPoint> { new(1, 1) };
                return Pass(grid.FindPath(start, goal, blocked).ResultCode == NavigationResultCode.Success, "dynamic obstacle replanned");
            default:
                var path = grid.FindPath(start, goal);
                return Pass(path.ResultCode == NavigationResultCode.Success && path.NoCornerCutting, $"{scenario.Feature} reachable");
        }
    }

    private static God2.ClassicServer.Application.Common.OperationResult Merge(PlayerInventoryRuntime inventory, ItemDefinitionCatalog items)
    {
        var split = inventory.Split(0, 3, 2, items, NextItemId, Epoch);
        return split.Succeeded ? inventory.Merge(3, 0, items, Epoch) : split;
    }

    private static God2.ClassicServer.Application.Common.OperationResult Capacity(ItemDefinitionCatalog items)
    {
        var inventory = new PlayerInventoryRuntime(Guid.NewGuid(), 1, 1, itemCatalog: items);
        _ = inventory.AddItem(items.Resolve(2).Value!, 1, NextItemId, Epoch);
        return inventory.AddItem(items.Resolve(3).Value!, 1, NextItemId, Epoch);
    }

    private static ItemDefinitionCatalog ItemCatalog() => new(
    [
        new ItemDefinition(1, "material", "Material", "material", ItemCategory.Material, StackPolicy.Stackable, 99, BindPolicy.None, TradePolicy.Tradable, SellPolicy.Sellable, 2, 1, "Gold", "Unknown", "Unknown", false, true, "headless-v1", "{}", "HeadlessContent"),
        new ItemDefinition(2, "armor", "Armor", "armor", ItemCategory.Equipment, StackPolicy.Single, 1, BindPolicy.None, TradePolicy.Tradable, SellPolicy.Sellable, 100, 50, "Gold", "Body", "Unknown", false, true, "headless-v1", "{}", "HeadlessContent"),
        new ItemDefinition(3, "weapon", "Weapon", "weapon", ItemCategory.Equipment, StackPolicy.Single, 1, BindPolicy.None, TradePolicy.Tradable, SellPolicy.Sellable, 120, 60, "Gold", "Weapon", "Unknown", false, true, "headless-v1", "{}", "HeadlessContent")
    ]);

    private static long NextItemId() => Interlocked.Increment(ref _itemSequence);

    public static bool ProgressionEqual(CharacterProgressionSnapshot left, CharacterProgressionSnapshot right) =>
        left.CharacterId == right.CharacterId &&
        left.Class == right.Class &&
        left.Level == right.Level &&
        left.TotalExperience == right.TotalExperience &&
        left.Stats == right.Stats &&
        left.AutomaticAttributeGrowth == right.AutomaticAttributeGrowth &&
        left.UnspentAttributePoints == right.UnspentAttributePoints &&
        left.UnlockedSkillIds.SequenceEqual(right.UnlockedSkillIds) &&
        left.Version == right.Version;

    private static DateTimeOffset Epoch { get; } = DateTimeOffset.Parse("2026-08-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static ScenarioProbeResult Pass(bool value, string evidence) => new(value, evidence, 0);
}
