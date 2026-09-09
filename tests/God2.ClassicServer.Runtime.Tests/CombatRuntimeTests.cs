using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class CombatRuntimeTests
{
    [Fact]
    public async Task Active_battle_reservation_blocks_direct_non_battle_combat_intent()
    {
        var reservations = new InMemoryWorldBattleReservationRegistry();
        var fixture = Fixture.Create(battleReservations: reservations);
        var reserved = reservations.Reserve(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Fixture.CharacterId,
            fixture.PlayerId,
            Fixture.Now);
        Assert.True(reserved.Succeeded);

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.Rejected, result.Code);
        Assert.Equal("combat.player_in_battle", result.FailureCode);
        Assert.Equal(100, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Turn_based_adapter_invokes_existing_combat_runtime_through_reserved_internal_path()
    {
        var reservations = new InMemoryWorldBattleReservationRegistry();
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(20),
            battleReservations: reservations);
        var battleId = Guid.NewGuid();
        var playerParticipantId = Guid.NewGuid();
        reservations.Reserve(
            battleId,
            playerParticipantId,
            Fixture.CharacterId,
            fixture.PlayerId,
            Fixture.Now);
        var player = fixture.Players.Get(fixture.PlayerId).Value!;
        var monster = fixture.Monsters.Get(fixture.MonsterId).Value!;
        var attacker = new BattleParticipant(
            playerParticipantId,
            battleId,
            BattleParticipantType.Player,
            BattleSide.PlayerSide,
            0,
            Fixture.CharacterId,
            null,
            fixture.PlayerId,
            50_000_001,
            fixture.SessionId,
            "Player",
            1,
            player.MaximumHp,
            player.CurrentHp,
            true,
            true,
            true,
            true,
            BattleActionState.Locked,
            BattleParticipantCombatState.ActionLocked,
            player.RuntimeVersion,
            player.AttackPower,
            player.Defense,
            null,
            player.StatPolicyStatus,
            CombatPolicyStatus.TestOnly,
            Fixture.Now,
            null,
            "{}");
        var target = new BattleParticipant(
            Guid.NewGuid(),
            battleId,
            BattleParticipantType.Monster,
            BattleSide.EnemySide,
            100,
            null,
            monster.MonsterTemplateId,
            fixture.MonsterId,
            50_000_002,
            null,
            "Monster",
            monster.Level,
            monster.MaximumHp,
            monster.CurrentHp,
            true,
            true,
            true,
            true,
            BattleActionState.Locked,
            BattleParticipantCombatState.ActionLocked,
            monster.RuntimeVersion,
            monster.AttackPower,
            monster.Defense,
            null,
            monster.StatPolicyStatus,
            CombatPolicyStatus.TestOnly,
            Fixture.Now,
            null,
            "{}");

        var result = await new ExistingCombatRuntimeBattleExecutionPort(fixture.Coordinator).ExecuteAsync(
            new BattleCombatExecutionRequest(
                battleId,
                1,
                Guid.NewGuid(),
                0,
                attacker,
                target,
                $"battle:{battleId:N}:round:1:action:1",
                Fixture.Now,
                "battle-adapter-test"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(20, result.Damage);
        Assert.Equal(80, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public void Monster_content_mapper_preserves_database_stats_and_raw_metadata()
    {
        var definition = new MonsterCombatContentMapper().Map(Fixture.Spawn());

        Assert.Equal(2001, definition.MonsterTemplateId);
        Assert.Equal(5, definition.Level);
        Assert.Equal(100, definition.MaximumHp);
        Assert.Equal(30, definition.AttackPower);
        Assert.Equal(4, definition.Defense);
        Assert.Equal(CombatPolicyStatus.ContentBacked, definition.StatPolicyStatus);
        Assert.Equal("{\"unknown\":true}", definition.RawMetadata);
    }

    [Fact]
    public void Database_monster_record_flows_to_spawn_and_combat_definition()
    {
        var record = new MonsterSpawnDatabaseRecord(
            3001,
            2001,
            "monster_2001",
            "Combat Monster",
            Fixture.MapId,
            12,
            10,
            WorldDirection.South,
            TimeSpan.FromSeconds(5),
            0,
            1,
            true,
            "{\"raw\":1}",
            "MariaDB:spawns+monsters",
            "db-v1",
            5,
            100,
            30,
            4);

        var spawn = new MonsterContentMapper().Map(record);
        var combat = new MonsterCombatContentMapper().Map(spawn);

        Assert.Equal(record.MaximumHp!.Value, combat.MaximumHp);
        Assert.Equal((long)record.AttackPower!.Value, combat.AttackPower);
        Assert.Equal((long)record.Defense!.Value, combat.Defense);
        Assert.Equal(CombatPolicyStatus.ContentBacked, combat.StatPolicyStatus);
    }

    [Fact]
    public void Database_monster_record_missing_stats_is_rejected_without_zero_defaults()
    {
        var record = new MonsterSpawnDatabaseRecord(
            3001,
            2001,
            "monster_2001",
            "Incomplete Monster",
            Fixture.MapId,
            12,
            10,
            WorldDirection.South,
            TimeSpan.FromSeconds(5),
            0,
            1,
            true,
            "{}",
            "MariaDB:incomplete-monster",
            "db-v1");

        var mapped = new MonsterContentMapper().TryMap(record);

        Assert.False(mapped.Succeeded);
        Assert.Equal("content.monster_spawn_evidence_incomplete", mapped.Error.Code);
        Assert.Throws<InvalidOperationException>(() => new MonsterContentMapper().Map(record));
    }

    [Fact]
    public void Combat_intent_does_not_accept_client_authoritative_damage_hp_or_reward()
    {
        var properties = typeof(CombatIntent).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Damage", properties);
        Assert.DoesNotContain("TargetHp", properties);
        Assert.DoesNotContain("Reward", properties);
        Assert.DoesNotContain("DropTableId", properties);
    }

    [Fact]
    public void Monster_content_validator_accepts_valid_content()
    {
        var definition = new MonsterCombatContentMapper().Map(Fixture.Spawn());
        var result = new MonsterCombatContentValidator().Validate([definition], new HashSet<int> { Fixture.MapId });

        Assert.Single(result.Valid);
        Assert.Empty(result.Quarantined);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Monster_content_validator_quarantines_duplicate_spawn()
    {
        var definition = new MonsterCombatContentMapper().Map(Fixture.Spawn());
        var result = new MonsterCombatContentValidator().Validate([definition, definition], new HashSet<int> { Fixture.MapId });

        Assert.Empty(result.Valid);
        Assert.Equal(2, result.Quarantined.Count);
        Assert.Contains(result.Issues, issue => issue.Code == "combat_content.spawn_duplicate");
    }

    [Fact]
    public void Monster_content_validator_quarantines_missing_map()
    {
        var definition = new MonsterCombatContentMapper().Map(Fixture.Spawn());
        var result = new MonsterCombatContentValidator().Validate([definition], new HashSet<int>());

        Assert.Empty(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Code == "combat_content.map_missing");
    }

    [Fact]
    public void Monster_content_validator_quarantines_disabled_content()
    {
        var definition = new MonsterCombatContentMapper().Map(Fixture.Spawn() with { Enabled = false });
        var result = new MonsterCombatContentValidator().Validate([definition], new HashSet<int> { Fixture.MapId });

        Assert.Empty(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Code == "combat_content.disabled");
    }

    [Theory]
    [InlineData(0, 30, 4, "combat_content.maximum_hp_invalid")]
    [InlineData(100, -1, 4, "combat_content.stat_negative")]
    [InlineData(100, 30, -1, "combat_content.stat_negative")]
    public void Monster_content_validator_quarantines_invalid_stats(
        long maximumHp,
        int attack,
        int defense,
        string expectedCode)
    {
        var definition = new MonsterCombatContentMapper().Map(
            Fixture.Spawn() with { MaximumHp = maximumHp, AttackPower = attack, Defense = defense });
        var result = new MonsterCombatContentValidator().Validate([definition], new HashSet<int> { Fixture.MapId });

        Assert.Empty(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void Monster_combat_registry_builds_from_map_runtime()
    {
        var fixture = Fixture.Create();

        var state = fixture.Monsters.Get(fixture.MonsterId);

        Assert.True(state.Succeeded);
        Assert.Equal(100, state.Value!.CurrentHp);
        Assert.Equal(MonsterCombatStateKind.Idle, state.Value.CombatState);
    }

    [Fact]
    public void Monster_combat_registry_rejects_duplicate_runtime_entity()
    {
        var fixture = Fixture.Create();
        var state = fixture.Monsters.Get(fixture.MonsterId).Value!;
        var definition = fixture.Monsters.GetDefinition(state.SpawnDefinitionId).Value!;

        Assert.Throws<InvalidOperationException>(() => fixture.Monsters.Register(state, definition));
    }

    [Fact]
    public void Combat_stat_provider_returns_player_snapshot()
    {
        var fixture = Fixture.Create();

        var result = fixture.Stats.GetPlayer(fixture.PlayerId);

        Assert.True(result.Succeeded);
        Assert.Equal(CombatEntityType.Player, result.Value!.EntityType);
        Assert.Equal(40, result.Value.AttackPower);
    }

    [Fact]
    public void Combat_stat_provider_returns_content_backed_monster_snapshot()
    {
        var fixture = Fixture.Create();

        var result = fixture.Stats.GetMonster(fixture.MonsterId);

        Assert.True(result.Succeeded);
        Assert.Equal(CombatEntityType.Monster, result.Value!.EntityType);
        Assert.Equal(CombatPolicyStatus.ContentBacked, result.Value.PolicyStatus);
    }

    [Fact]
    public void Monster_registry_rejects_invalid_hp_invariant()
    {
        var fixture = Fixture.Create();
        var state = fixture.Monsters.Get(fixture.MonsterId).Value! with { RuntimeEntityId = 9999, CurrentHp = 101 };
        var definition = fixture.Monsters.GetDefinition(state.SpawnDefinitionId).Value!;

        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Monsters.Register(state, definition));
    }

    [Theory]
    [InlineData(10, 10, 1)]
    [InlineData(30, 10, 20)]
    [InlineData(100, 1, 99)]
    [InlineData(44, 14, 30)]
    [InlineData(34, 15, 19)]
    public void Baseline_damage_policy_is_checked_server_baseline(long attack, long defense, long expected)
    {
        var result = new BaselineServerDamagePolicy().Compute(Stats(attack), Stats(0, defense));

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.FinalDamage);
        Assert.Equal(CombatPolicyStatus.Baseline, result.PolicyStatus);
    }

    [Fact]
    public void Deterministic_damage_policy_rejects_non_positive_damage()
    {
        var result = new DeterministicTestDamagePolicy(0).Compute(Stats(10), Stats(0, 1));

        Assert.False(result.Succeeded);
        Assert.Equal("combat.damage_invalid", result.FailureCode);
    }

    [Fact]
    public void Deterministic_range_policy_allows_valid_raw_distance()
    {
        var result = new DeterministicTestCombatRangePolicy(10).Evaluate(
            Position(0, 0),
            Position(6, 8));

        Assert.True(result.Allowed);
        Assert.Equal(CombatPolicyStatus.TestOnly, result.PolicyStatus);
    }

    [Fact]
    public void Deterministic_range_policy_rejects_out_of_range()
    {
        var result = new DeterministicTestCombatRangePolicy(9).Evaluate(
            Position(0, 0),
            Position(6, 8));

        Assert.False(result.Allowed);
        Assert.Equal("combat.out_of_range", result.FailureCode);
    }

    [Fact]
    public void Deterministic_range_policy_rejects_overflow_without_throwing()
    {
        var result = new DeterministicTestCombatRangePolicy(int.MaxValue).Evaluate(
            Position(int.MinValue, int.MinValue),
            Position(int.MaxValue, int.MaxValue));

        Assert.False(result.Allowed);
        Assert.Equal("combat.range_overflow", result.FailureCode);
    }

    [Fact]
    public void Target_resolver_resolves_owned_active_player_and_monster()
    {
        var fixture = Fixture.Create();

        var result = fixture.Resolver.Resolve(fixture.Intent());

        Assert.True(result.Succeeded);
        Assert.IsType<PlayerObject>(result.Value!.AttackerObject);
        Assert.IsType<MonsterObject>(result.Value.TargetObject);
    }

    [Fact]
    public async Task Combat_rejects_invalid_session()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { SessionId = "missing" });

        Assert.Equal(CombatResultCode.InvalidSession, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_cross_character_ownership()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { CharacterId = 999 });

        Assert.Equal(CombatResultCode.OwnershipMismatch, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_forged_attacker_runtime_id()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { AttackerRuntimeEntityId = 999999 });

        Assert.Equal(CombatResultCode.AttackerNotFound, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_missing_target()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { TargetRuntimeEntityId = 999999 });

        Assert.Equal(CombatResultCode.TargetNotFound, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_forged_target_template()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { TargetTemplateId = 999999 });

        Assert.Equal(CombatResultCode.Rejected, result.Code);
        Assert.Equal("combat.target_template_mismatch", result.FailureCode);
    }

    [Fact]
    public async Task Combat_rejects_inactive_attacker()
    {
        var fixture = Fixture.Create();
        fixture.SetPlayer(state => state with { LifecycleState = MonsterLifecycleState.Inactive });

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.AttackerInactive, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_dead_attacker()
    {
        var fixture = Fixture.Create();
        fixture.SetPlayer(state => state with { CurrentHp = 0 });

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.AttackerDead, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_inactive_target()
    {
        var fixture = Fixture.Create();
        fixture.SetMonster(state => state with { LifecycleState = MonsterLifecycleState.Inactive });

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.TargetInactive, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_dead_target()
    {
        var fixture = Fixture.Create();
        fixture.SetMonster(state => state with
        {
            LifecycleState = MonsterLifecycleState.Inactive,
            CombatState = MonsterCombatStateKind.Dead,
            CurrentHp = 0
        });

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.TargetDead, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_different_map()
    {
        var fixture = Fixture.Create();
        fixture.MoveMonsterToTargetMap();

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.DifferentMap, result.Code);
    }

    [Fact]
    public async Task Combat_rejects_different_world_instance()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Sessions.Get(fixture.SessionId).Value!;
        fixture.Sessions.Set(binding with { WorldInstanceId = "world:other" });

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.DifferentInstance, result.Code);
    }

    [Fact]
    public async Task Production_range_policy_remains_evidence_blocked()
    {
        var fixture = Fixture.Create(range: new EvidenceBlockedCombatRangePolicy());

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.RangeEvidenceBlocked, result.Code);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Combat_rejects_out_of_range()
    {
        var fixture = Fixture.Create(range: new DeterministicTestCombatRangePolicy(1));

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.OutOfRange, result.Code);
    }

    [Theory]
    [InlineData(CombatActionType.Skill)]
    [InlineData(CombatActionType.ItemEffect)]
    [InlineData(CombatActionType.Environmental)]
    [InlineData(CombatActionType.Unknown)]
    public async Task Combat_rejects_deferred_action_types(CombatActionType action)
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { CombatActionType = action });

        Assert.Equal(CombatResultCode.UnsupportedAction, result.Code);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Basic_attack_reduces_monster_hp_and_increments_version()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(25));

        var result = await fixture.Execute();
        var state = fixture.Monsters.Get(fixture.MonsterId).Value!;

        Assert.Equal(CombatResultCode.Success, result.Code);
        Assert.Equal(75, state.CurrentHp);
        Assert.Equal(1, state.RuntimeVersion);
        Assert.Contains("HP", state.DirtyFlags, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Damage_clamps_hp_at_zero()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(1000));

        var result = await fixture.Execute();

        Assert.Equal(0, result.HpAfter);
        Assert.Equal(0, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Injected_damage_overflow_is_rejected_without_hp_mutation()
    {
        var fixture = Fixture.Create();
        fixture.Failure.Point = CombatFailurePoint.DamageOverflow;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.DamagePolicyBlocked, result.Code);
        Assert.Equal(100, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Duplicate_intent_does_not_double_damage()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(25));
        var intent = fixture.Intent(idempotencyKey: "same-combat");

        var first = await fixture.Execute(intent);
        var second = await fixture.Execute(intent);

        Assert.Equal(CombatResultCode.Success, first.Code);
        Assert.Equal(CombatResultCode.DuplicateCompleted, second.Code);
        Assert.True(second.IsDuplicate);
        Assert.Equal(75, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Same_key_changed_payload_returns_replay_conflict()
    {
        var fixture = Fixture.Create();
        var first = fixture.Intent(idempotencyKey: "reused-key");
        var changed = first with { TargetTemplateId = first.TargetTemplateId + 1 };

        Assert.Equal(CombatResultCode.Success, (await fixture.Execute(first)).Code);
        Assert.Equal(CombatResultCode.ReplayConflict, (await fixture.Execute(changed)).Code);
    }

    [Fact]
    public async Task Target_version_conflict_is_rejected()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute(fixture.Intent() with { ExpectedTargetVersion = 9 });

        Assert.Equal(CombatResultCode.VersionConflict, result.Code);
        Assert.Equal(100, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Cooldown_boundary_rejects_second_distinct_attack()
    {
        var fixture = Fixture.Create(cooldown: new DeterministicTestCombatCooldownStore(TimeSpan.FromSeconds(1)));

        var first = await fixture.Execute(fixture.Intent(at: Fixture.Now));
        var second = await fixture.Execute(fixture.Intent(at: Fixture.Now.AddMilliseconds(100)));

        Assert.Equal(CombatResultCode.Success, first.Code);
        Assert.Equal(CombatResultCode.CooldownActive, second.Code);
    }

    [Fact]
    public async Task Concurrent_duplicate_commits_once()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(30));
        var intent = fixture.Intent(idempotencyKey: "concurrent-duplicate");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Execute(intent)));

        Assert.Single(results, result => result.Code == CombatResultCode.Success);
        Assert.Equal(70, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Concurrent_distinct_attacks_do_not_lose_updates()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(20));
        var intents = Enumerable.Range(0, 8)
            .Select(index => fixture.Intent(idempotencyKey: $"distinct-{index}"))
            .ToArray();

        var results = await Task.WhenAll(intents.Select(fixture.Execute));

        Assert.Single(results, result => result.Code == CombatResultCode.Success);
        Assert.Equal(7, results.Count(result => result.Code == CombatResultCode.VersionConflict));
        Assert.Equal(80, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Non_lethal_hit_keeps_monster_active()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(10));

        await fixture.Execute();
        var state = fixture.Monsters.Get(fixture.MonsterId).Value!;

        Assert.Equal(MonsterLifecycleState.Active, state.LifecycleState);
        Assert.Equal(MonsterCombatStateKind.Hit, state.CombatState);
    }

    [Fact]
    public async Task Lethal_hit_commits_single_death()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));

        var result = await fixture.Execute();

        Assert.True(result.DeathCommitted);
        Assert.Single(fixture.Store.Deaths);
        Assert.Equal(MonsterCombatStateKind.RespawnPending, fixture.Monsters.Get(fixture.MonsterId).Value!.CombatState);
    }

    [Fact]
    public async Task Concurrent_lethal_hits_commit_one_killer_and_one_death()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        var intents = Enumerable.Range(0, 6)
            .Select(index => fixture.Intent(idempotencyKey: $"lethal-{index}"))
            .ToArray();

        var results = await Task.WhenAll(intents.Select(fixture.Execute));

        Assert.Single(results, result => result.Code == CombatResultCode.Success);
        Assert.Single(fixture.Store.Deaths);
    }

    [Fact]
    public async Task Dead_monster_rejects_follow_up_attack()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();

        var result = await fixture.Execute(fixture.Intent());

        Assert.Equal(CombatResultCode.TargetDead, result.Code);
    }

    [Fact]
    public async Task Death_events_are_emitted_once()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        var intent = fixture.Intent(idempotencyKey: "death-events");

        await fixture.Execute(intent);
        await fixture.Execute(intent);

        Assert.Single(fixture.Events.Events, value => value.Kind == CombatEventKind.MonsterDied);
        Assert.Single(fixture.Events.Events, value => value.Kind == CombatEventKind.MonsterLethalDamage);
    }

    [Fact]
    public async Task No_reward_policy_commits_death_without_items_or_currency()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new NoCombatRewardPolicy());

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.Success, result.Code);
        Assert.True(result.RewardCommitted);
        Assert.Equal(CombatRewardResultCode.NoReward, result.RewardResult!.Code);
    }

    [Fact]
    public async Task Deterministic_item_reward_uses_inventory_transaction()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new DeterministicTestCombatRewardPolicy([new CombatItemGrant(100, 1)]));

        var result = await fixture.Execute();
        var persisted = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.Equal(CombatResultCode.Success, result.Code);
        Assert.Single(persisted.Inventory.Slots);
        Assert.Equal(100, persisted.Inventory.Slots[0].ItemTemplateId);
    }

    [Fact]
    public async Task Catalog_backed_reward_policy_uses_monster_drop_table_for_inventory_transaction()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new CatalogBackedCombatRewardPolicy(
            [
                new CombatDropRewardDefinition(
                    300001,
                    2001,
                    100,
                    1,
                    1,
                    1m,
                    false,
                    CombatPolicyStatus.ContentBacked,
                    "tests")
            ]));

        var result = await fixture.Execute();
        var persisted = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.Equal(CombatResultCode.Success, result.Code);
        Assert.True(result.RewardCommitted);
        Assert.Equal(1, result.RewardResult!.ItemGrantCount);
        Assert.Single(persisted.Inventory.Slots);
        Assert.Equal(100, persisted.Inventory.Slots[0].ItemTemplateId);
    }

    [Fact]
    public async Task Deterministic_currency_reward_uses_inventory_transaction()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new DeterministicTestCombatRewardPolicy(
                currencies: [new CombatCurrencyGrant("Gold", 25)]));

        await fixture.Execute();
        var persisted = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.Equal(125, Assert.Single(persisted.Currency.Balances).Balance);
    }

    [Fact]
    public async Task Item_and_currency_reward_commit_atomically()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new DeterministicTestCombatRewardPolicy(
                [new CombatItemGrant(100, 1)],
                [new CombatCurrencyGrant("Gold", 25)]));

        var result = await fixture.Execute();
        var persisted = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.True(result.RewardCommitted);
        Assert.Single(persisted.Inventory.Slots);
        Assert.Equal(125, Assert.Single(persisted.Currency.Balances).Balance);
    }

    [Fact]
    public async Task Inventory_full_reward_keeps_death_and_records_recovery()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new DeterministicTestCombatRewardPolicy([new CombatItemGrant(100, 1)]),
            inventoryFull: true);

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.RewardFailure, result.Code);
        Assert.True(result.DeathCommitted);
        Assert.False(result.RewardCommitted);
        Assert.Equal(CombatRewardResultCode.InventoryFull, result.RewardResult!.Code);
    }

    [Fact]
    public async Task Reward_transaction_failure_does_not_rollback_death()
    {
        var fixture = Fixture.Create(
            damage: new DeterministicTestDamagePolicy(100),
            reward: new DeterministicTestCombatRewardPolicy([new CombatItemGrant(100, 1)]));
        fixture.Failure.Point = CombatFailurePoint.RewardTransactionFailure;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.RewardFailure, result.Code);
        Assert.True(result.DeathCommitted);
        Assert.Single(fixture.Store.Deaths);
    }

    [Fact]
    public async Task Invalid_reward_currency_is_rejected()
    {
        var fixture = Fixture.Create();
        var plan = fixture.RewardPlan([new CombatCurrencyGrant("Unknown", 5)]);

        var result = await fixture.RewardCoordinator.ExecuteAsync(
            plan,
            fixture.SessionId,
            Fixture.AccountId,
            CancellationToken.None);

        Assert.Equal(CombatRewardResultCode.TransactionFailed, result.Code);
        Assert.Equal("combat.reward_currency_invalid", result.FailureCode);
    }

    [Fact]
    public async Task Reward_currency_overflow_is_rejected()
    {
        var fixture = Fixture.Create();
        var plan = fixture.RewardPlan(
        [
            new CombatCurrencyGrant("Gold", long.MaxValue),
            new CombatCurrencyGrant("Gold", 1)
        ]);

        var result = await fixture.RewardCoordinator.ExecuteAsync(
            plan,
            fixture.SessionId,
            Fixture.AccountId,
            CancellationToken.None);

        Assert.Equal("combat.reward_currency_overflow", result.FailureCode);
    }

    [Fact]
    public async Task Reward_duplicate_commits_once()
    {
        var fixture = Fixture.Create();
        var plan = fixture.RewardPlan([new CombatCurrencyGrant("Gold", 10)]);

        var first = await fixture.RewardCoordinator.ExecuteAsync(plan, fixture.SessionId, Fixture.AccountId, CancellationToken.None);
        var second = await fixture.RewardCoordinator.ExecuteAsync(plan, fixture.SessionId, Fixture.AccountId, CancellationToken.None);
        var persisted = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.Equal(CombatRewardResultCode.Committed, first.Code);
        Assert.Equal(CombatRewardResultCode.DuplicateCompleted, second.Code);
        Assert.Equal(110, Assert.Single(persisted.Currency.Balances).Balance);
    }

    [Fact]
    public void Evidence_blocked_respawn_policy_does_not_create_plan()
    {
        var fixture = Fixture.Create(respawn: new EvidenceBlockedMonsterRespawnPolicy());
        var death = fixture.DeathRecord();
        var state = fixture.Monsters.Get(fixture.MonsterId).Value!;
        var definition = fixture.Monsters.GetDefinition(state.SpawnDefinitionId).Value!;

        var result = fixture.Respawns.CreatePlan(death, definition, state);

        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Respawns.Plans);
    }

    [Fact]
    public async Task Deterministic_respawn_is_scheduled_after_lethal_commit()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));

        var result = await fixture.Execute();

        Assert.True(result.RespawnScheduled);
        Assert.Single(fixture.Respawns.Plans);
    }

    [Fact]
    public async Task Respawn_before_due_remains_scheduled()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();
        var plan = Assert.Single(fixture.Respawns.Plans);

        var result = await fixture.Respawns.ProcessDueAsync(plan, plan.RespawnDueAtUtc.AddTicks(-1), CancellationToken.None);

        Assert.Equal(MonsterRespawnState.Scheduled, result.State);
        Assert.Null(result.NewRuntimeEntityId);
    }

    [Fact]
    public async Task Due_respawn_creates_new_active_runtime_entity()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();
        var plan = Assert.Single(fixture.Respawns.Plans);

        var result = await fixture.Respawns.ProcessDueAsync(plan, plan.RespawnDueAtUtc, CancellationToken.None);

        Assert.Equal(MonsterRespawnState.Active, result.State);
        Assert.NotEqual(fixture.MonsterId, result.NewRuntimeEntityId);
        Assert.True(fixture.Monsters.Get(result.NewRuntimeEntityId!.Value).Succeeded);
    }

    [Fact]
    public async Task Respawn_restores_maximum_hp_and_idle_state()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();
        var plan = Assert.Single(fixture.Respawns.Plans);

        var result = await fixture.Respawns.ProcessDueAsync(plan, plan.RespawnDueAtUtc, CancellationToken.None);
        var state = fixture.Monsters.Get(result.NewRuntimeEntityId!.Value).Value!;

        Assert.Equal(state.MaximumHp, state.CurrentHp);
        Assert.Equal(MonsterCombatStateKind.Idle, state.CombatState);
    }

    [Fact]
    public async Task Respawn_registry_contains_exactly_one_monster_for_spawn()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();
        var plan = Assert.Single(fixture.Respawns.Plans);

        await fixture.Respawns.ProcessDueAsync(plan, plan.RespawnDueAtUtc, CancellationToken.None);

        Assert.Single(fixture.Map.Objects.OfType<MonsterObject>());
        Assert.Single(fixture.Monsters.Snapshot, state => state.SpawnDefinitionId == plan.SpawnDefinitionId);
    }

    [Fact]
    public async Task Respawn_target_map_missing_is_reported()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();
        fixture.Failure.Point = CombatFailurePoint.RespawnTargetMapMissing;
        var plan = Assert.Single(fixture.Respawns.Plans);

        var result = await fixture.Respawns.ProcessDueAsync(plan, plan.RespawnDueAtUtc, CancellationToken.None);

        Assert.Equal(MonsterRespawnState.TargetMapUnavailable, result.State);
    }

    [Fact]
    public async Task Respawn_registry_conflict_is_reported_without_mutation()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();
        fixture.Failure.Point = CombatFailurePoint.RespawnRegistryConflict;
        var plan = Assert.Single(fixture.Respawns.Plans);

        var result = await fixture.Respawns.ProcessDueAsync(plan, plan.RespawnDueAtUtc, CancellationToken.None);

        Assert.Equal(MonsterRespawnState.RegistryConflict, result.State);
        Assert.True(fixture.Map.Objects.Get(fixture.MonsterId).Succeeded);
    }

    [Fact]
    public async Task Persistence_failure_before_hp_mutation_rolls_back_runtime()
    {
        var fixture = Fixture.Create();
        fixture.Failure.Point = CombatFailurePoint.PersistenceFailureBeforeHpMutation;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.PersistenceFailure, result.Code);
        Assert.Equal(100, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
        Assert.Empty(fixture.Respawns.Plans);
    }

    [Fact]
    public async Task Persistence_failure_after_hp_mutation_rolls_back_store_and_runtime()
    {
        var fixture = Fixture.Create();
        fixture.Store.FailureInjection.Point = CombatFailurePoint.PersistenceFailureAfterHpMutation;

        var result = await fixture.Execute();
        var persisted = await fixture.Store.LoadMonsterAsync(fixture.MonsterId, CancellationToken.None);

        Assert.Equal(CombatResultCode.PersistenceFailure, result.Code);
        Assert.Equal(100, persisted.CurrentHp);
        Assert.Equal(100, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Runtime_failure_after_commit_reloads_persisted_state()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(20));
        fixture.Failure.Point = CombatFailurePoint.RuntimeFailureAfterDatabaseCommit;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.RuntimeMutationFailure, result.Code);
        Assert.Equal("combat.runtime_commit_failed_reloaded", result.FailureCode);
        Assert.Equal(80, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Recovery_failure_is_precisely_reported()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(20));
        fixture.Failure.Point = CombatFailurePoint.RuntimeFailureAfterDatabaseCommit;
        fixture.Failure.RecoveryFailureEnabled = true;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.RuntimeMutationFailure, result.Code);
        Assert.Equal("combat.recovery_failed", result.FailureCode);
    }

    [Fact]
    public async Task Disconnect_during_combat_does_not_commit_damage()
    {
        var fixture = Fixture.Create();
        fixture.Store.FailureInjection.Point = CombatFailurePoint.DisconnectDuringCombat;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.PersistenceFailure, result.Code);
        Assert.Equal(100, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Reconnect_recovery_restores_committed_monster_state()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(20));
        await fixture.Execute();
        fixture.SetMonster(state => state with { CurrentHp = 100, RuntimeVersion = 0 }, seedPersistence: false);

        var restored = await fixture.Recovery.RestoreMonsterAsync(fixture.MonsterId, CancellationToken.None);

        Assert.True(restored.Succeeded);
        Assert.Equal(80, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public void Disconnect_and_reconnect_player_combat_component_is_explicit()
    {
        var fixture = Fixture.Create();
        var player = fixture.Players.Get(fixture.PlayerId).Value!;

        fixture.Recovery.DisconnectPlayer(fixture.PlayerId);
        Assert.False(fixture.Players.Get(fixture.PlayerId).Succeeded);
        fixture.Recovery.ReconnectPlayer(player);

        Assert.True(fixture.Players.Get(fixture.PlayerId).Succeeded);
    }

    [Fact]
    public async Task Combat_audit_records_actor_target_damage_and_policy()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(20));

        await fixture.Execute();
        var audit = Assert.Single(fixture.Audit.Records);

        Assert.Equal(fixture.PlayerId, audit.AttackerRuntimeEntityId);
        Assert.Equal(fixture.MonsterId, audit.TargetRuntimeEntityId);
        Assert.Equal(20, audit.Damage);
        Assert.Equal(CombatPolicyStatus.TestOnly, audit.PolicyStatus);
    }

    [Fact]
    public async Task Audit_failure_returns_internal_failure_after_committed_damage()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(20));
        fixture.Failure.Point = CombatFailurePoint.AuditFailure;

        var result = await fixture.Execute();

        Assert.Equal(CombatResultCode.InternalFailure, result.Code);
        Assert.Equal("combat.audit_failed", result.FailureCode);
        Assert.Equal(80, fixture.Monsters.Get(fixture.MonsterId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Inspector_returns_isolated_combat_monster_and_respawn_snapshots()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));
        await fixture.Execute();

        var snapshot = fixture.Inspector.Capture(new CombatInspectorQuery(), Fixture.Now.AddSeconds(1));

        Assert.Single(snapshot.Combat);
        Assert.Single(snapshot.Monsters);
        Assert.Single(snapshot.DeathRewards);
        Assert.Single(snapshot.Respawns);
        Assert.Equal("SerializerBlockedByEvidence", snapshot.Monsters[0].ProtocolStatus);
    }

    [Fact]
    public async Task Inspector_filters_failed_and_evidence_blocked_records()
    {
        var fixture = Fixture.Create(range: new EvidenceBlockedCombatRangePolicy());
        await fixture.Execute();

        var snapshot = fixture.Inspector.Capture(
            new CombatInspectorQuery(FailedOnly: true, EvidenceBlockedOnly: true),
            Fixture.Now);

        Assert.Single(snapshot.Combat);
        Assert.Equal(CombatResultCode.RangeEvidenceBlocked, snapshot.Combat[0].Result);
    }

    [Fact]
    public async Task Combat_runtime_events_cover_semantic_pipeline_without_packets()
    {
        var fixture = Fixture.Create(damage: new DeterministicTestDamagePolicy(100));

        var result = await fixture.Execute();
        var kinds = fixture.Events.Events.Select(value => value.Kind).ToHashSet();

        Assert.Contains(CombatEventKind.CombatIntentReceived, kinds);
        Assert.Contains(CombatEventKind.DamagePlanned, kinds);
        Assert.Contains(CombatEventKind.DamageApplied, kinds);
        Assert.Contains(CombatEventKind.MonsterDied, kinds);
        Assert.Contains(CombatEventKind.MonsterRespawnScheduled, kinds);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Monster_serializer_gate_emits_zero_network_bytes()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Execute();

        Assert.Empty(result.NetworkBytes);
        Assert.Contains(
            fixture.Map.Replication.UpdateQueue.Snapshot,
            entry => entry.SerializerStatus == OfficialSerializerStatus.SerializerBlockedByEvidence);
    }

    [Fact]
    public void Monster_official_serializer_remains_blocked_and_emits_no_frame()
    {
        var fixture = Fixture.Create();

        var serialized = new MonsterSerializer().Serialize(fixture.Monster.State);

        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, serialized.Status);
        Assert.Empty(serialized.Frame);
    }

    [Fact]
    public async Task Idempotency_audit_uses_hash_not_raw_key()
    {
        var fixture = Fixture.Create();
        const string rawKey = "combat-private-correlation-value";

        await fixture.Execute(fixture.Intent(idempotencyKey: rawKey));
        var audit = Assert.Single(fixture.Audit.Records);

        Assert.DoesNotContain(rawKey, audit.IdempotencySafeId, StringComparison.Ordinal);
        Assert.Equal(16, audit.IdempotencySafeId.Length);
    }

    [Fact]
    public async Task In_memory_persistence_replay_lookup_detects_changed_payload()
    {
        var fixture = Fixture.Create();
        var intent = fixture.Intent(idempotencyKey: "lookup-key");
        await fixture.Execute(intent);

        var lookup = await fixture.Store.FindCompletedAsync("lookup-key", "changed", CancellationToken.None);

        Assert.True(lookup.Found);
        Assert.False(lookup.PayloadMatches);
    }

    [Fact]
    public void Content_backed_respawn_policy_uses_database_interval()
    {
        var definition = new MonsterCombatContentMapper().Map(Fixture.Spawn() with { RespawnTime = TimeSpan.FromSeconds(17) });

        var result = new ContentBackedMonsterRespawnPolicy().ResolveDueAt(definition, Fixture.Now);

        Assert.True(result.Succeeded);
        Assert.Equal(Fixture.Now.AddSeconds(17), result.Value);
    }

    private static CombatStatSnapshot Stats(long attackPower, long defense = 0) =>
        new(
            1,
            CombatEntityType.Player,
            1,
            100,
            100,
            0,
            0,
            attackPower,
            defense,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            0,
            CombatPolicyStatus.TestOnly,
            "tests");

    private static PlayerState Position(int x, int y) =>
        new(1, 1, "P", 1, "Class1", "Gender1", "A", Fixture.MapId, new WorldPosition3(x, y), WorldDirection.South, "Active", 24, null);

    private sealed class Fixture
    {
        public const int MapId = 100;
        public const int TargetMapId = 200;
        public const long CharacterId = 10;
        public const long AccountId = 1;
        public static readonly DateTimeOffset Now = new(2026, 7, 31, 8, 0, 0, TimeSpan.Zero);

        private Fixture(
            WorldRuntime world,
            MapRuntime map,
            MapRuntime targetMap,
            RuntimeSession session,
            InMemoryWorldInteractionSessionRegistry sessions,
            PlayerObject player,
            MonsterObject monster,
            PlayerCombatRuntimeRegistry players,
            MonsterCombatRuntimeRegistry monsters,
            RuntimeCombatTargetResolver resolver,
            RuntimeCombatStatProvider stats,
            InMemoryCombatMutationStore store,
            InMemoryCombatEventSink events,
            InMemoryCombatAuditLedger audit,
            CombatFailureInjection failure,
            InventoryTransactionCoordinator inventory,
            InMemoryInventoryPersistenceStore inventoryStore,
            CombatRewardCoordinator rewardCoordinator,
            MonsterRespawnCoordinator respawns,
            CombatCoordinator coordinator,
            CombatRuntimeInspector inspector,
            CombatRuntimeRecoveryService recovery)
        {
            World = world;
            Map = map;
            TargetMap = targetMap;
            Session = session;
            Sessions = sessions;
            Player = player;
            Monster = monster;
            Players = players;
            Monsters = monsters;
            Resolver = resolver;
            Stats = stats;
            Store = store;
            Events = events;
            Audit = audit;
            Failure = failure;
            Inventory = inventory;
            InventoryStore = inventoryStore;
            RewardCoordinator = rewardCoordinator;
            Respawns = respawns;
            Coordinator = coordinator;
            Inspector = inspector;
            Recovery = recovery;
        }

        public WorldRuntime World { get; }
        public MapRuntime Map { get; }
        public MapRuntime TargetMap { get; }
        public RuntimeSession Session { get; }
        public string SessionId => Session.SessionId;
        public InMemoryWorldInteractionSessionRegistry Sessions { get; }
        public PlayerObject Player { get; }
        public MonsterObject Monster { get; private set; }
        public long PlayerId => Player.Identity.RuntimeObjectId;
        public long MonsterId => Monster.Identity.RuntimeObjectId;
        public PlayerCombatRuntimeRegistry Players { get; }
        public MonsterCombatRuntimeRegistry Monsters { get; }
        public RuntimeCombatTargetResolver Resolver { get; }
        public RuntimeCombatStatProvider Stats { get; }
        public InMemoryCombatMutationStore Store { get; }
        public InMemoryCombatEventSink Events { get; }
        public InMemoryCombatAuditLedger Audit { get; }
        public CombatFailureInjection Failure { get; }
        public InventoryTransactionCoordinator Inventory { get; }
        public InMemoryInventoryPersistenceStore InventoryStore { get; }
        public CombatRewardCoordinator RewardCoordinator { get; }
        public MonsterRespawnCoordinator Respawns { get; }
        public CombatCoordinator Coordinator { get; }
        public CombatRuntimeInspector Inspector { get; }
        public CombatRuntimeRecoveryService Recovery { get; }

        public static Fixture Create(
            ICombatRangePolicy? range = null,
            ICombatDamagePolicy? damage = null,
            ICombatRewardPolicy? reward = null,
            IMonsterRespawnPolicy? respawn = null,
            ICombatCooldownStore? cooldown = null,
            bool inventoryFull = false,
            IWorldBattleReservationRegistry? battleReservations = null)
        {
            var spawn = Spawn();
            var sourceDefinition = MapDefinition(MapId, [spawn]);
            var targetDefinition = MapDefinition(TargetMapId, []);
            var content = new WorldContentSnapshot(
                new Dictionary<int, MapDefinition>
                {
                    [MapId] = sourceDefinition,
                    [TargetMapId] = targetDefinition
                },
                []);
            var factory = new MapRuntimeFactory();
            var session = RuntimeSession.Connected("connection-combat", "127.0.0.1:1000", Now) with
            {
                SessionId = "session-combat",
                AccountId = AccountId,
                CharacterId = CharacterId,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.InWorld
            };
            var character = new CharacterSummary(
                CharacterId,
                AccountId,
                "CombatTester",
                "Class1",
                "Gender1",
                "LifeSkill1",
                1,
                "Test",
                MapId,
                10,
                10,
                "Active",
                Now,
                null);
            var bindingResult = new WorldSessionBinder(factory).Bind(
                session,
                character,
                content,
                WorldContentMode.MariaDbAuthoritative);
            Assert.True(bindingResult.Succeeded);
            var binding = bindingResult.Value!;
            var targetResult = factory.Create(content, TargetMapId);
            Assert.True(targetResult.Succeeded);
            var targetMap = targetResult.Value!.MapRuntime;
            var world = new WorldRuntime();
            world.Add(binding.MapRuntime);
            world.Add(targetMap);
            var sessions = new InMemoryWorldInteractionSessionRegistry();
            sessions.Seed(binding);
            var player = Assert.Single(binding.MapRuntime.Objects.OfType<PlayerObject>());
            var monster = Assert.Single(binding.MapRuntime.Objects.OfType<MonsterObject>());

            var definition = new MonsterCombatContentMapper().Map(spawn);
            var validated = new MonsterCombatContentValidator().Validate([definition], new HashSet<int> { MapId, TargetMapId });
            Assert.Single(validated.Valid);
            var monsters = MonsterCombatRuntimeRegistry.Build(binding.MapRuntime, validated.Valid, Now);
            var players = new PlayerCombatRuntimeRegistry();
            players.Register(new PlayerCombatRuntimeState(
                player.Identity.RuntimeObjectId,
                CharacterId,
                binding.MapRuntime.WorldInstanceId,
                MapId,
                player.State.Position,
                MonsterLifecycleState.Active,
                100,
                100,
                40,
                2,
                0,
                null,
                "Spawned",
                CombatPolicyStatus.Baseline,
                Now));
            var store = new InMemoryCombatMutationStore();
            store.Seed(monsters.Get(monster.Identity.RuntimeObjectId).Value!);
            var events = new InMemoryCombatEventSink();
            var audit = new InMemoryCombatAuditLedger();
            var failure = new CombatFailureInjection();
            var inventoryAudit = new InMemoryInventoryAuditLedger();
            var inventoryStore = new InMemoryInventoryPersistenceStore(inventoryAudit);
            var slots = inventoryFull
                ? new[]
                {
                    new InventorySlot(
                        0,
                        9001,
                        9001,
                        9001,
                        200,
                        1,
                        "None",
                        "{}",
                        Now,
                        Now,
                        0)
                }
                : [];
            inventoryStore.Seed(new InventoryPersistenceBundle(
                new PlayerInventorySnapshot(Guid.NewGuid(), CharacterId, inventoryFull ? 1 : 8, 0, 0, "Clean", slots),
                new CurrencyWalletSnapshot(CharacterId, [new CurrencyBalance("Gold", 100, 0, "Clean")])));
            var inventory = new InventoryTransactionCoordinator(
                ItemCatalog(),
                new MerchantDefinitionCatalog([]),
                inventoryStore,
                inventoryAudit,
                new SequentialId().Next,
                inventoryStore.FailureInjection);
            var rewardCoordinator = new CombatRewardCoordinator(inventory, failure);
            var resolver = new RuntimeCombatTargetResolver(world, sessions, players, monsters);
            var stats = new RuntimeCombatStatProvider(players, monsters);
            var effectiveRange = range ?? new DeterministicTestCombatRangePolicy(100);
            var respawns = new MonsterRespawnCoordinator(
                world,
                monsters,
                store,
                respawn ?? new DeterministicTestMonsterRespawnPolicy(TimeSpan.FromSeconds(5)),
                events,
                failure);
            var coordinator = new CombatCoordinator(
                resolver,
                new RuntimeCombatEligibilityPolicy(effectiveRange),
                stats,
                damage ?? new BaselineServerDamagePolicy(),
                monsters,
                players,
                store,
                new MonsterDeathCoordinator(),
                reward ?? new NoCombatRewardPolicy(),
                rewardCoordinator,
                respawns,
                cooldown ?? new BaselineCombatCooldownStore(),
                events,
                audit,
                failure,
                battleReservations);
            var inspector = new CombatRuntimeInspector(audit, monsters, respawns, store, effectiveRange);
            var recovery = new CombatRuntimeRecoveryService(store, monsters, players);
            return new Fixture(
                world,
                binding.MapRuntime,
                targetMap,
                session,
                sessions,
                player,
                monster,
                players,
                monsters,
                resolver,
                stats,
                store,
                events,
                audit,
                failure,
                inventory,
                inventoryStore,
                rewardCoordinator,
                respawns,
                coordinator,
                inspector,
                recovery);
        }

        public CombatIntent Intent(string? idempotencyKey = null, DateTimeOffset? at = null)
        {
            var player = Players.Get(PlayerId).Value;
            var monster = Monsters.Get(MonsterId).Value;
            return new CombatIntent(
                Guid.NewGuid(),
                idempotencyKey ?? Guid.NewGuid().ToString("N"),
                SessionId,
                CharacterId,
                PlayerId,
                MonsterId,
                Monster.Identity.TemplateId,
                CombatActionType.BasicAttack,
                player?.RuntimeVersion ?? 0,
                monster?.RuntimeVersion ?? 0,
                null,
                "TrustedInternalHeadlessTest",
                at ?? Now,
                Guid.NewGuid().ToString("N"));
        }

        public Task<CombatResult> Execute() =>
            Coordinator.ExecuteAsync(Intent(), CancellationToken.None);

        public Task<CombatResult> Execute(CombatIntent intent) =>
            Coordinator.ExecuteAsync(intent, CancellationToken.None);

        public void SetPlayer(Func<PlayerCombatRuntimeState, PlayerCombatRuntimeState> mutate)
        {
            var state = Players.Get(PlayerId).Value!;
            Players.Replace(mutate(state));
        }

        public void SetMonster(
            Func<MonsterCombatRuntimeState, MonsterCombatRuntimeState> mutate,
            bool seedPersistence = true)
        {
            var state = mutate(Monsters.Get(MonsterId).Value!);
            Monsters.Replace(state);
            if (seedPersistence)
            {
                Store.Seed(state);
            }
        }

        public void MoveMonsterToTargetMap()
        {
            Map.Objects.Remove(MonsterId);
            Map.EntityRegistry.Remove(MonsterId);
            Monster = Monster with { State = Monster.State with { MapId = TargetMapId } };
            TargetMap.Objects.Add(Monster);
            TargetMap.EntityRegistry.Add(new RuntimeEntity(
                MonsterId,
                WorldRuntimeEntityType.Monster,
                Monster.Identity.TemplateId,
                TargetMapId,
                Monster.State.Position,
                Monster.State.Direction,
                "Active",
                24,
                "MovedForTest"));
            SetMonster(state => state with { MapId = TargetMapId });
        }

        public CombatRewardPlan RewardPlan(IReadOnlyList<CombatCurrencyGrant> currencies) =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CharacterId,
                2001,
                null,
                [],
                currencies,
                0,
                Guid.NewGuid().ToString("N"),
                CombatPolicyStatus.TestOnly,
                Now,
                "reward-test",
                0);

        public MonsterDeathRecord DeathRecord() =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                MonsterId,
                Monster.Identity.TemplateId,
                PlayerId,
                CharacterId,
                MapId,
                Spawn().SpawnId,
                100,
                100,
                Now,
                CombatPolicyStatus.EvidenceBlocked,
                CombatPolicyStatus.EvidenceBlocked,
                CombatPolicyStatus.TestOnly,
                0,
                1,
                "death-test");

        public static MonsterSpawnDefinition Spawn() =>
            new(
                3001,
                2001,
                MapId,
                new WorldPosition3(12, 10),
                WorldDirection.South,
                TimeSpan.FromSeconds(5),
                0,
                1,
                "Always",
                "MariaDB:spawns+monsters",
                "Combat Monster",
                true,
                "{\"unknown\":true}",
                "combat-content-v1",
                5,
                100,
                30,
                4);

        private static MapDefinition MapDefinition(int mapId, IReadOnlyList<MonsterSpawnDefinition> monsters) =>
            new(
                mapId,
                mapId,
                $"Map {mapId}",
                "tests",
                new MapBounds(0, 0, 1000, 1000),
                [new SpawnPoint("Default", new WorldPosition3(10, 10), WorldDirection.South, "tests")],
                [],
                monsters,
                [],
                [],
                []);

        private static ItemDefinitionCatalog ItemCatalog()
        {
            var mapper = new ItemContentMapper();
            var validation = new ItemContentValidator().Validate(
            [
                mapper.Map(ItemRecord(100, 20, "Material", "Stackable", 10, 5)),
                mapper.Map(ItemRecord(200, 1, "Equipment", "Single", 50, 20)),
                mapper.Map(ItemRecord(300, 1, "Quest", "Single", 0, 0, quest: true, sellPolicy: "NotSellable"))
            ]);
            Assert.Empty(validation.Quarantined);
            return validation.Catalog;
        }

        private static ItemDatabaseRecord ItemRecord(
            int id,
            int maximumStack,
            string category,
            string stackPolicy,
            long buyPrice,
            long sellPrice,
            bool quest = false,
            string sellPolicy = "Sellable") =>
            new(
                id,
                $"item_{id}",
                $"Item {id}",
                null,
                category,
                stackPolicy,
                maximumStack,
                "None",
                "Tradable",
                sellPolicy,
                buyPrice,
                sellPrice,
                "Gold",
                null,
                null,
                quest,
                true,
                "{}",
                "test-items-v1",
                "tests");

        private sealed class SequentialId
        {
            private long _next = 10_000;

            public long Next() => Interlocked.Increment(ref _next);
        }
    }
}
