using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class GameplayCoreInventoryRuntimeTests
{
    [Fact]
    public void Item_mapper_preserves_verified_usage_rules()
    {
        var mapped = new ItemContentMapper().Map(ItemRecord(
            90,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: false,
            useOnOther: true,
            requiredLevel: 12));

        Assert.True(mapped.Usable);
        Assert.True(mapped.NormalUse);
        Assert.False(mapped.BattleUse);
        Assert.True(mapped.UseOnOther);
        Assert.Equal(12, mapped.RequiredLevel);
    }

    [Fact]
    public void Item_use_engine_applies_combined_recovery_and_clamps_to_maximums()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            90,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item,
        [
            Effect(90, 1, ItemEffectType.RestoreHp, 500),
            Effect(90, 2, ItemEffectType.RestoreMp, 150)
        ]);

        var result = engine.Evaluate(
            new ItemUseRequest(90, ItemUsageContext.Battle),
            Actor(currentHp: 800, maximumHp: 1_000, currentMp: 100, maximumMp: 200));

        Assert.True(result.Succeeded);
        Assert.Equal(200, result.Value!.RestoredHp);
        Assert.Equal(100, result.Value.RestoredMp);
        Assert.Equal(1_000, result.Value.UpdatedActor.CurrentHp);
        Assert.Equal(200, result.Value.UpdatedActor.CurrentMp);
        Assert.Equal(1, result.Value.ConsumedQuantity);
    }

    [Fact]
    public void Item_use_engine_applies_percent_recovery_from_maximums()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            93,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item,
        [
            Effect(93, 1, ItemEffectType.RestoreHpPercent, 15),
            Effect(93, 2, ItemEffectType.RestoreMpPercent, 50)
        ]);

        var result = engine.Evaluate(
            new ItemUseRequest(93, ItemUsageContext.Battle),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201));

        Assert.True(result.Succeeded);
        Assert.Equal(150, result.Value!.RestoredHp);
        Assert.Equal(100, result.Value.RestoredMp);
        Assert.Equal(850, result.Value.UpdatedActor.CurrentHp);
        Assert.Equal(120, result.Value.UpdatedActor.CurrentMp);
        Assert.Equal(1, result.Value.ConsumedQuantity);
    }

    [Fact]
    public void Item_use_engine_models_verified_cleanse_effects_without_changing_vitals()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            94,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item,
        [
            Effect(94, 1, ItemEffectType.CleansePoison, 1),
            Effect(94, 2, ItemEffectType.CleansePetrify, 1)
        ]);

        var result = engine.Evaluate(
            new ItemUseRequest(94, ItemUsageContext.Battle),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201)
                with { ActiveStatusCodes = ["poison", "confusion"] });

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.RestoredHp);
        Assert.Equal(0, result.Value.RestoredMp);
        Assert.Equal(["poison"], result.Value.RemovedStatusCodes);
        Assert.Equal(1, result.Value.ConsumedQuantity);
    }

    [Fact]
    public void Item_use_engine_rejects_cleanse_items_when_target_has_no_matching_status()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            95,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item, [Effect(95, 1, ItemEffectType.CleanseSleep, 1)]);

        var result = engine.Evaluate(
            new ItemUseRequest(95, ItemUsageContext.Battle),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201)
                with { ActiveStatusCodes = ["poison"] });

        Assert.False(result.Succeeded);
        Assert.Equal("item.use.no_effect", result.Error.Code);
    }

    [Fact]
    public void Item_use_engine_models_timed_stat_buff_effects_without_changing_vitals()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            96,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item, [Effect(96, 1, ItemEffectType.ApplyStrengthBuff, 120, durationSeconds: 7_200)]);

        var result = engine.Evaluate(
            new ItemUseRequest(96, ItemUsageContext.World),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.RestoredHp);
        Assert.Equal(0, result.Value.RestoredMp);
        var buff = Assert.Single(result.Value.AppliedBuffs!);
        Assert.Equal("strength", buff.StatCode);
        Assert.Equal(120, buff.Amount);
        Assert.Equal(TimeSpan.FromMinutes(120), buff.Duration);
        Assert.Equal(1, result.Value.ConsumedQuantity);
    }

    [Fact]
    public void Item_use_engine_models_constitution_timed_stat_buff_effects()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            97,
            category: "Consumable",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item, [Effect(97, 1, ItemEffectType.ApplyConstitutionBuff, 80, durationSeconds: 3_600)]);

        var result = engine.Evaluate(
            new ItemUseRequest(97, ItemUsageContext.Battle),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201));

        Assert.True(result.Succeeded);
        var buff = Assert.Single(result.Value!.AppliedBuffs!);
        Assert.Equal("constitution", buff.StatCode);
        Assert.Equal(80, buff.Amount);
        Assert.Equal(TimeSpan.FromMinutes(60), buff.Duration);
    }

    [Fact]
    public void Item_use_engine_models_battle_experience_bonus_effects()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            98,
            category: "Generic",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item, [Effect(98, 1, ItemEffectType.ApplyBattleExperienceBuff, 50, durationSeconds: 14_400)]);

        var result = engine.Evaluate(
            new ItemUseRequest(98, ItemUsageContext.World),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.RestoredHp);
        Assert.Equal(0, result.Value.RestoredMp);
        var buff = Assert.Single(result.Value.AppliedExperienceBuffs!);
        Assert.Equal(50, buff.Percent);
        Assert.Equal(TimeSpan.FromHours(4), buff.Duration);
    }

    [Fact]
    public void Item_use_engine_models_element_attribute_bonus_effects()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            99,
            category: "Generic",
            maxStack: 20,
            usable: true,
            normalUse: true,
            battleUse: true));
        var engine = ItemUseEngine(item, [Effect(99, 1, ItemEffectType.ApplyFireElementBuff, 600, durationSeconds: 10_800)]);

        var result = engine.Evaluate(
            new ItemUseRequest(99, ItemUsageContext.Battle),
            Actor(currentHp: 700, maximumHp: 1_000, currentMp: 20, maximumMp: 201));

        Assert.True(result.Succeeded);
        var buff = Assert.Single(result.Value!.AppliedElementBuffs!);
        Assert.Equal("fire", buff.ElementCode);
        Assert.Equal(600, buff.Amount);
        Assert.Equal(TimeSpan.FromHours(3), buff.Duration);
    }

    [Fact]
    public void Item_use_engine_enforces_context_target_and_level_rules()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            91,
            category: "Consumable",
            usable: true,
            normalUse: true,
            battleUse: false,
            useOnOther: false,
            requiredLevel: 10));
        var engine = ItemUseEngine(item, [Effect(91, 1, ItemEffectType.RestoreHp, 100)]);

        var battle = engine.Evaluate(new ItemUseRequest(91, ItemUsageContext.Battle), Actor(level: 20));
        var other = engine.Evaluate(new ItemUseRequest(91, ItemUsageContext.World, TargetsSelf: false), Actor(level: 20));
        var level = engine.Evaluate(new ItemUseRequest(91, ItemUsageContext.World), Actor(level: 9));

        Assert.Equal("item.use.context_blocked", battle.Error.Code);
        Assert.Equal("item.use.target_blocked", other.Error.Code);
        Assert.Equal("item.use.level_blocked", level.Error.Code);
    }

    [Fact]
    public void Item_use_engine_does_not_consume_for_dead_or_full_actor()
    {
        var item = new ItemContentMapper().Map(ItemRecord(
            92,
            category: "Consumable",
            usable: true,
            normalUse: true));
        var engine = ItemUseEngine(item, [Effect(92, 1, ItemEffectType.RestoreHp, 100)]);

        var dead = engine.Evaluate(
            new ItemUseRequest(92, ItemUsageContext.World),
            Actor(currentHp: 0));
        var full = engine.Evaluate(
            new ItemUseRequest(92, ItemUsageContext.World),
            Actor(currentHp: 1_000, maximumHp: 1_000));

        Assert.Equal("item.use.dead_actor", dead.Error.Code);
        Assert.Equal("item.use.no_effect", full.Error.Code);
    }

    [Fact]
    public void Item_mapper_preserves_unknown_raw_and_runtime_boundaries()
    {
        var mapped = new ItemContentMapper().Map(new ItemDatabaseRecord(
            100,
            "raw_item",
            "Raw Item",
            null,
            "NotRecoveredCategory",
            null,
            7,
            null,
            null,
            null,
            10,
            5,
            null,
            null,
            null,
            QuestItemFlag: false,
            Enabled: true,
            RawMetadata: "{\"unknown\":true}",
            ContentVersion: "items-v1",
            Source: "database:items"));

        Assert.Equal(ItemCategory.Unknown, mapped.ItemCategory);
        Assert.Equal(StackPolicy.Stackable, mapped.StackPolicy);
        Assert.Equal(7, mapped.MaximumStack);
        Assert.Equal("Unknown", mapped.BindPolicy.ToString());
        Assert.Equal("{\"unknown\":true}", mapped.RawMetadata);
        Assert.Equal("items-v1", mapped.ContentVersion);
    }

    [Fact]
    public void Item_validator_quarantines_duplicate_disabled_invalid_price_stack_and_merchant_missing()
    {
        var mapper = new ItemContentMapper();
        var definitions = new[]
        {
            mapper.Map(ItemRecord(100, maxStack: 20)),
            mapper.Map(ItemRecord(100, maxStack: 20)),
            mapper.Map(ItemRecord(101, maxStack: 0)),
            mapper.Map(ItemRecord(102, buyPrice: -1)),
            mapper.Map(ItemRecord(103, enabled: false))
        };

        var result = new ItemContentValidator().Validate(definitions, [new MerchantItemDefinition(999, 10)]);

        Assert.Empty(result.Catalog.Definitions);
        Assert.Equal(5, result.Quarantined.Count);
        Assert.Contains(result.Issues, issue => issue.Code == "item.duplicate_template_id");
        Assert.Contains(result.Issues, issue => issue.Code == "item.invalid_maximum_stack");
        Assert.Contains(result.Issues, issue => issue.Code == "item.negative_price");
        Assert.Contains(result.Issues, issue => issue.Code == "item.disabled");
        Assert.Contains(result.Issues, issue => issue.Code == "merchant_item.missing_item_definition");
    }

    [Fact]
    public void Inventory_runtime_adds_stackable_and_non_stackable_items()
    {
        var catalog = Catalog();
        var inventory = EmptyInventory(capacity: 5, catalog);

        var stack = inventory.AddItem(catalog.Resolve(100).Value!, 12, NextIds(), Now);
        var swords = inventory.AddItem(catalog.Resolve(200).Value!, 2, NextIds(2000), Now);

        Assert.True(stack.Succeeded);
        Assert.True(swords.Succeeded);
        Assert.Equal(3, inventory.UsedSlots);
        Assert.Contains(inventory.Slots.Values, slot => slot.ItemTemplateId == 100 && slot.Quantity == 12);
        Assert.Equal(2, inventory.Slots.Values.Count(slot => slot.ItemTemplateId == 200 && slot.Quantity == 1));
    }

    [Fact]
    public void Inventory_runtime_rejects_full_inventory_without_partial_mutation()
    {
        var catalog = Catalog();
        var inventory = EmptyInventory(capacity: 1, catalog);

        var first = inventory.AddItem(catalog.Resolve(200).Value!, 1, NextIds(), Now);
        var second = inventory.AddItem(catalog.Resolve(200).Value!, 1, NextIds(2000), Now);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal("inventory.capacity_insufficient", second.Error.Code);
        Assert.Single(inventory.Slots);
    }

    [Fact]
    public void Inventory_runtime_removes_partial_and_full_stacks()
    {
        var catalog = Catalog();
        var inventory = EmptyInventory(capacity: 4, catalog);
        Assert.True(inventory.AddItem(catalog.Resolve(100).Value!, 10, NextIds(), Now).Succeeded);

        var partial = inventory.RemoveItem(100, 4, catalog, Now);
        var full = inventory.RemoveItem(100, 6, catalog, Now);

        Assert.True(partial.Succeeded);
        Assert.True(full.Succeeded);
        Assert.Empty(inventory.Slots);
    }

    [Fact]
    public void Inventory_runtime_moves_and_swaps_without_overwriting_items()
    {
        var catalog = Catalog();
        var inventory = EmptyInventory(capacity: 4, catalog);
        Assert.True(inventory.AddItem(catalog.Resolve(100).Value!, 5, NextIds(), Now).Succeeded);
        Assert.True(inventory.AddItem(catalog.Resolve(200).Value!, 1, NextIds(2000), Now).Succeeded);

        var move = inventory.Move(1, 3, Now);
        var swap = inventory.Swap(0, 3, Now);

        Assert.True(move.Succeeded);
        Assert.True(swap.Succeeded);
        Assert.Equal(200, inventory.Slots[0].ItemTemplateId);
        Assert.Equal(100, inventory.Slots[3].ItemTemplateId);
    }

    [Fact]
    public void Inventory_runtime_splits_and_merges_stackable_items()
    {
        var catalog = Catalog();
        var inventory = EmptyInventory(capacity: 4, catalog);
        Assert.True(inventory.AddItem(catalog.Resolve(100).Value!, 10, NextIds(), Now).Succeeded);

        var split = inventory.Split(0, 1, 3, catalog, NextIds(3000), Now);
        var merge = inventory.Merge(1, 0, catalog, Now);

        Assert.True(split.Succeeded);
        Assert.True(merge.Succeeded);
        Assert.Single(inventory.Slots);
        Assert.Equal(10, inventory.Slots[0].Quantity);
    }

    [Fact]
    public void Inventory_runtime_rejects_stack_overflow_merge()
    {
        var catalog = Catalog();
        var inventory = EmptyInventory(capacity: 4, catalog);
        Assert.True(inventory.AddItem(catalog.Resolve(100).Value!, 20, NextIds(), Now).Succeeded);
        Assert.True(inventory.AddItem(catalog.Resolve(100).Value!, 10, NextIds(2000), Now).Succeeded);

        var merge = inventory.Merge(1, 0, catalog, Now);

        Assert.False(merge.Succeeded);
        Assert.Equal("inventory.stack_overflow", merge.Error.Code);
        Assert.Equal(2, inventory.UsedSlots);
    }

    [Fact]
    public void Inventory_invariant_validator_rejects_duplicate_persistent_item_id()
    {
        var catalog = Catalog();
        var exception = Assert.Throws<InvalidOperationException>(() => new PlayerInventoryRuntime(
            Guid.NewGuid(),
            1,
            4,
            [
                Slot(0, 7000, 100, 1),
                Slot(1, 7000, 100, 1)
            ],
            dirtyState: "Clean",
            itemCatalog: catalog));

        Assert.Contains("inventory.invariant.duplicate_persistent_item", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transaction_coordinator_replays_duplicate_add_without_duplicating_item()
    {
        var fixture = Fixture(gold: 100);
        var request = Request(InventoryOperationType.AddItem, "dup-add", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 5));

        var first = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var second = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Success, first.Code);
        Assert.Equal(InventoryTransactionResultCode.DuplicateCompleted, second.Code);
        Assert.NotNull(second.InventorySnapshot);
        Assert.NotNull(second.CurrencySnapshot);
        Assert.Equal(second.InventorySnapshot!.Version, second.InventoryVersionAfter);
        Assert.Equal(5, second.InventorySnapshot.Slots.Sum(slot => slot.Quantity));
        Assert.Equal(5, persisted.Inventory.Slots.Sum(slot => slot.Quantity));
    }

    [Fact]
    public async Task Transaction_authority_and_payload_bind_player_session_and_trusted_server_context()
    {
        var fixture = Fixture(gold: 100);
        var playerRequest = Request(
            InventoryOperationType.AddItem,
            "authority-bound",
            0,
            new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1));

        var first = await fixture.Coordinator.ExecuteAsync(playerRequest, CancellationToken.None);
        var changedSession = await fixture.Coordinator.ExecuteAsync(
            playerRequest with { SessionId = "session-2" },
            CancellationToken.None);
        var ambiguousTrusted = await fixture.Coordinator.ExecuteAsync(
            Request(
                InventoryOperationType.SystemGrant,
                "trusted-ambiguous",
                1,
                new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1)) with
            {
                AuthorityKind = InventoryMutationAuthorityKind.TrustedServer
            },
            CancellationToken.None);
        var validTrusted = await fixture.Coordinator.ExecuteAsync(
            Request(
                InventoryOperationType.SystemGrant,
                "trusted-valid",
                1,
                new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1)) with
            {
                AccountId = null,
                AuthorityKind = InventoryMutationAuthorityKind.TrustedServer
            },
            CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Success, first.Code);
        Assert.Equal(InventoryTransactionResultCode.Conflict, changedSession.Code);
        Assert.Equal("inventory.replay_conflict", changedSession.FailureCode);
        Assert.Equal(InventoryTransactionResultCode.Rejected, ambiguousTrusted.Code);
        Assert.Equal("inventory.trusted_authority_account_ambiguous", ambiguousTrusted.FailureCode);
        Assert.Equal(InventoryTransactionResultCode.Success, validTrusted.Code);
    }

    [Fact]
    public async Task Persistence_exceptions_fail_closed_for_version_and_transaction_calls()
    {
        var coordinator = new InventoryTransactionCoordinator(
            Catalog(),
            MerchantCatalog(),
            new ThrowingInventoryPersistenceStore(),
            new InMemoryInventoryAuditLedger());

        var version = await coordinator.GetCurrentVersionAsync(1, CancellationToken.None);
        var transaction = await coordinator.ExecuteAsync(
            Request(
                InventoryOperationType.AddItem,
                "persistence-throws",
                0,
                new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1)),
            CancellationToken.None);

        Assert.False(version.Succeeded);
        Assert.Equal("inventory.persistence_unavailable", version.Error.Code);
        Assert.Equal(InventoryTransactionResultCode.PersistenceFailure, transaction.Code);
        Assert.Equal("inventory.persistence_unavailable", transaction.FailureCode);
    }

    [Fact]
    public void Inventory_catalog_is_instance_scoped_and_split_uses_allocated_identity()
    {
        var catalog = Catalog();
        var first = EmptyInventory(4, catalog);
        Assert.True(first.AddItem(catalog.Resolve(100).Value!, 5, () => 1001, Now).Succeeded);

        var secondCatalog = new ItemDefinitionCatalog([catalog.Resolve(200).Value!]);
        var second = new PlayerInventoryRuntime(Guid.NewGuid(), 2, 4, itemCatalog: secondCatalog);
        Assert.True(second.AddItem(secondCatalog.Resolve(200).Value!, 1, () => 2001, Now).Succeeded);

        var split = first.Split(0, 1, 2, catalog, () => 9001, Now);

        Assert.True(split.Succeeded);
        Assert.Equal(9001, first.Slots[1].PersistentInventoryItemId);
        Assert.Equal(100, first.Clone().Slots[0].ItemTemplateId);
        Assert.Equal(200, second.Clone().Slots[0].ItemTemplateId);
        Assert.Null(typeof(PlayerInventoryRuntime).GetProperty(
            "ItemDefinitionCatalog",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
    }

    [Fact]
    public void Transaction_coordinator_is_unified_mutation_entrypoint()
    {
        var fixture = Fixture(gold: 100);

        Assert.IsAssignableFrom<IInventoryTransactionCoordinator>(fixture.Coordinator);
    }

    [Fact]
    public async Task Transaction_coordinator_rejects_same_idempotency_key_with_changed_payload()
    {
        var fixture = Fixture(gold: 100);
        var first = Request(InventoryOperationType.AddItem, "same-key", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 5));
        var changed = Request(InventoryOperationType.AddItem, "same-key", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 6));

        _ = await fixture.Coordinator.ExecuteAsync(first, CancellationToken.None);
        var replayConflict = await fixture.Coordinator.ExecuteAsync(changed, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Conflict, replayConflict.Code);
        Assert.Equal("inventory.replay_conflict", replayConflict.FailureCode);
    }

    [Fact]
    public async Task Concurrent_duplicate_add_commits_once()
    {
        var fixture = Fixture(gold: 100);
        var request = Request(InventoryOperationType.AddItem, "concurrent-add", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 5));

        var results = await Task.WhenAll(
            fixture.Coordinator.ExecuteAsync(request, CancellationToken.None),
            fixture.Coordinator.ExecuteAsync(request, CancellationToken.None));
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Contains(results, result => result.Code == InventoryTransactionResultCode.Success);
        Assert.Contains(results, result => result.Code == InventoryTransactionResultCode.DuplicateCompleted);
        Assert.Equal(5, persisted.Inventory.Slots.Sum(slot => slot.Quantity));
    }

    [Fact]
    public async Task Separate_coordinators_share_exactly_once_inventory_authority()
    {
        var fixture = Fixture(gold: 100);
        var second = new InventoryTransactionCoordinator(
            Catalog(),
            MerchantCatalog(),
            fixture.Store,
            fixture.Audit,
            NextIds(30_000),
            fixture.Store.FailureInjection);
        var request = Request(InventoryOperationType.AddItem, "cross-coordinator-duplicate", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 5));

        var results = await Task.WhenAll(
            fixture.Coordinator.ExecuteAsync(request, CancellationToken.None),
            second.ExecuteAsync(request, CancellationToken.None));
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Single(results, result => result.Code == InventoryTransactionResultCode.Success);
        Assert.Single(results, result => result.Code == InventoryTransactionResultCode.DuplicateCompleted);
        Assert.Equal(5, persisted.Inventory.Slots.Sum(slot => slot.Quantity));
        Assert.Equal(1, persisted.Inventory.Version);
    }

    [Fact]
    public async Task Separate_coordinators_reject_lost_update_and_restart_from_persisted_snapshot()
    {
        var fixture = Fixture(gold: 100);
        var second = new InventoryTransactionCoordinator(
            Catalog(),
            MerchantCatalog(),
            fixture.Store,
            fixture.Audit,
            NextIds(40_000),
            fixture.Store.FailureInjection);

        var raced = await Task.WhenAll(
            fixture.Coordinator.ExecuteAsync(
                Request(InventoryOperationType.AddItem, "cross-coordinator-a", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 3)),
                CancellationToken.None),
            second.ExecuteAsync(
                Request(InventoryOperationType.AddItem, "cross-coordinator-b", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 4)),
                CancellationToken.None));

        Assert.Single(raced, result => result.Code == InventoryTransactionResultCode.Success);
        Assert.Single(raced, result => result.Code == InventoryTransactionResultCode.VersionConflict);

        var restarted = new InventoryTransactionCoordinator(
            Catalog(),
            MerchantCatalog(),
            fixture.Store,
            fixture.Audit,
            NextIds(50_000),
            fixture.Store.FailureInjection);
        var version = await restarted.GetCurrentVersionAsync(1, CancellationToken.None);
        Assert.True(version.Succeeded);
        var afterRestart = await restarted.ExecuteAsync(
            Request(InventoryOperationType.AddItem, "after-restart", version.Value, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 2)),
            CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Success, afterRestart.Code);
        Assert.Equal(2, persisted.Inventory.Version);
        Assert.Equal(raced.Single(result => result.Code == InventoryTransactionResultCode.Success).InventorySnapshot!.Slots.Sum(slot => slot.Quantity) + 2,
            persisted.Inventory.Slots.Sum(slot => slot.Quantity));
    }

    [Fact]
    public async Task Transaction_coordinator_rejects_version_conflict()
    {
        var fixture = Fixture(gold: 100);
        var request = Request(InventoryOperationType.AddItem, "bad-version", 99, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 5));

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.VersionConflict, result.Code);
    }

    [Fact]
    public async Task Merchant_buy_debits_server_price_and_grants_item_atomically()
    {
        var fixture = Fixture(gold: 100);
        var request = Request(InventoryOperationType.MerchantBuy, "buy-ok", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 3), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Success, result.Code);
        Assert.Equal(70, persisted.Currency.Balances.Single(balance => balance.CurrencyType == "Gold").Balance);
        Assert.Equal(3, persisted.Inventory.Slots.Sum(slot => slot.Quantity));
        Assert.Single(fixture.Audit.Records);
    }

    [Fact]
    public async Task Merchant_buy_rejects_insufficient_currency_and_keeps_inventory_empty()
    {
        var fixture = Fixture(gold: 5);
        var request = Request(InventoryOperationType.MerchantBuy, "buy-poor", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.InsufficientCurrency, result.Code);
        Assert.Empty(persisted.Inventory.Slots);
        Assert.Equal(5, persisted.Currency.Balances.Single().Balance);
    }

    [Fact]
    public async Task Merchant_buy_rejects_full_inventory_without_currency_loss()
    {
        var fixture = Fixture(gold: 100, capacity: 1, seedSlots: [Slot(0, 9000, 200, 1)]);
        var request = Request(InventoryOperationType.MerchantBuy, "buy-full", 0, new InventoryMutationRequest(ItemTemplateId: 200, Quantity: 1), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.InsufficientCapacity, result.Code);
        Assert.Equal(100, persisted.Currency.Balances.Single().Balance);
        Assert.Single(persisted.Inventory.Slots);
    }

    [Fact]
    public async Task Merchant_buy_rejects_invalid_merchant_and_item()
    {
        var fixture = Fixture(gold: 100);
        var badMerchant = await fixture.Coordinator.ExecuteAsync(
            Request(InventoryOperationType.MerchantBuy, "bad-merchant", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 9999),
            CancellationToken.None);
        var badItem = await fixture.Coordinator.ExecuteAsync(
            Request(InventoryOperationType.MerchantBuy, "bad-item", 0, new InventoryMutationRequest(ItemTemplateId: 9999, Quantity: 1), merchantTemplateId: 4001),
            CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.InvalidMerchant, badMerchant.Code);
        Assert.Equal(InventoryTransactionResultCode.InvalidItem, badItem.Code);
    }

    [Fact]
    public async Task Concurrent_buy_serializes_and_prevents_lost_update()
    {
        var fixture = Fixture(gold: 100);

        var results = await Task.WhenAll(
            fixture.Coordinator.ExecuteAsync(
                Request(InventoryOperationType.MerchantBuy, "buy-race-1", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001),
                CancellationToken.None),
            fixture.Coordinator.ExecuteAsync(
                Request(InventoryOperationType.MerchantBuy, "buy-race-2", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001),
                CancellationToken.None));
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Contains(results, result => result.Code == InventoryTransactionResultCode.Success);
        Assert.Contains(results, result => result.Code == InventoryTransactionResultCode.VersionConflict);
        Assert.Equal(90, persisted.Currency.Balances.Single(balance => balance.CurrencyType == "Gold").Balance);
        Assert.Equal(1, persisted.Inventory.Slots.Sum(slot => slot.Quantity));
    }

    [Fact]
    public async Task Merchant_sell_removes_item_and_credits_server_price()
    {
        var fixture = Fixture(gold: 0, seedSlots: [Slot(0, 9000, 100, 5)]);
        var request = Request(InventoryOperationType.MerchantSell, "sell-ok", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 2), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Success, result.Code);
        Assert.Equal(8, persisted.Currency.Balances.Single().Balance);
        Assert.Equal(3, persisted.Inventory.Slots.Single().Quantity);
    }

    [Fact]
    public async Task Merchant_sell_rejects_insufficient_quantity_and_forbidden_item()
    {
        var fixture = Fixture(gold: 0, seedSlots: [Slot(0, 9000, 300, 1)]);
        var insufficient = await fixture.Coordinator.ExecuteAsync(
            Request(InventoryOperationType.MerchantSell, "sell-too-many", 0, new InventoryMutationRequest(ItemTemplateId: 300, Quantity: 2), merchantTemplateId: 4001),
            CancellationToken.None);
        var forbidden = await fixture.Coordinator.ExecuteAsync(
            Request(InventoryOperationType.MerchantSell, "sell-forbidden", 0, new InventoryMutationRequest(ItemTemplateId: 300, Quantity: 1), merchantTemplateId: 4001),
            CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.Rejected, insufficient.Code);
        Assert.Equal(InventoryTransactionResultCode.Rejected, forbidden.Code);
        Assert.Equal("merchant.sell_forbidden", forbidden.FailureCode);
    }

    [Fact]
    public async Task Failure_injection_rolls_back_currency_and_inventory()
    {
        var fixture = Fixture(gold: 100);
        fixture.Store.FailureInjection.Point = InventoryFailureInjectionPoint.DbFailureAfterCurrencyWrite;
        var request = Request(InventoryOperationType.MerchantBuy, "failure-buy", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.PersistenceFailure, result.Code);
        Assert.Empty(persisted.Inventory.Slots);
        Assert.Equal(100, persisted.Currency.Balances.Single().Balance);
        Assert.Contains(fixture.Audit.Records, record => record.Result == InventoryTransactionResultCode.PersistenceFailure);
    }

    [Fact]
    public async Task Failure_injection_disconnect_during_commit_rolls_back_currency_and_inventory()
    {
        var fixture = Fixture(gold: 100);
        fixture.Store.FailureInjection.Point = InventoryFailureInjectionPoint.DisconnectDuringCommit;
        var request = Request(InventoryOperationType.MerchantBuy, "disconnect-buy", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);

        Assert.Equal(InventoryTransactionResultCode.PersistenceFailure, result.Code);
        Assert.Empty(persisted.Inventory.Slots);
        Assert.Equal(100, persisted.Currency.Balances.Single().Balance);
    }

    [Fact]
    public async Task Failure_injection_runtime_commit_failure_reloads_from_persistence()
    {
        var fixture = Fixture(gold: 100);
        fixture.Store.FailureInjection.Point = InventoryFailureInjectionPoint.RuntimeCommitFailure;
        var request = Request(InventoryOperationType.MerchantBuy, "runtime-commit-failure", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001);

        var result = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);
        var persisted = await fixture.Store.LoadAsync(1, CancellationToken.None);
        var snapshot = fixture.Coordinator.CaptureInspector(new InventoryInspectorQuery(CharacterId: 1, ItemTemplateId: 100));

        Assert.Equal(InventoryTransactionResultCode.InternalFailure, result.Code);
        Assert.Equal("inventory.runtime_commit_failed_reloaded", result.FailureCode);
        Assert.Equal(90, persisted.Currency.Balances.Single().Balance);
        Assert.Single(persisted.Inventory.Slots);
        Assert.Single(snapshot.Items);
        Assert.Contains(fixture.Audit.Records, record => record.Result == InventoryTransactionResultCode.Success);
        Assert.Contains(fixture.Audit.Records, record => record.Result == InventoryTransactionResultCode.InternalFailure);
    }

    [Fact]
    public async Task Inspector_snapshot_is_filtered_and_read_only()
    {
        var fixture = Fixture(gold: 100);
        var request = Request(InventoryOperationType.MerchantBuy, "inspect-buy", 0, new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 1), merchantTemplateId: 4001);
        _ = await fixture.Coordinator.ExecuteAsync(request, CancellationToken.None);

        var snapshot = fixture.Coordinator.CaptureInspector(new InventoryInspectorQuery(CharacterId: 1, ItemTemplateId: 100, Limit: 10));

        Assert.Single(snapshot.Inventories);
        Assert.Single(snapshot.Items);
        Assert.Single(snapshot.Merchants);
        Assert.Single(snapshot.Transactions);
        Assert.Equal("Completed", snapshot.Transactions[0].State);
        Assert.Equal(100, snapshot.Items[0].ItemTemplateId);
        Assert.Equal("Merchant Client Protocol: SerializerBlockedByEvidence", snapshot.Merchants[0].EvidenceProtocolStatus);
    }

    [Fact]
    public void Currency_runtime_rejects_negative_overflow_and_insufficient_balance()
    {
        var wallet = new CurrencyWalletRuntime(1, [new CurrencyBalance("Gold", 10, 0, "Clean")]);

        var insufficient = wallet.Debit("Gold", 11);
        var invalid = wallet.Credit("Gold", -1);
        var overflow = new CurrencyWalletRuntime(1, [new CurrencyBalance("Gold", long.MaxValue, 0, "Clean")]).Credit("Gold", 1);

        Assert.Equal("currency.insufficient", insufficient.Error.Code);
        Assert.Equal("currency.amount_invalid", invalid.Error.Code);
        Assert.Equal("currency.overflow", overflow.Error.Code);
    }

    [Fact]
    public void Merchant_protocol_remains_evidence_gated_and_emits_no_fake_bytes()
    {
        var result = new MerchantSerializer().Serialize(new MerchantState(
            4001,
            1001,
            5001,
            "Merchant",
            "Default",
            "Gold",
            [new MerchantItemDefinition(100, 10)],
            "AttachedToNpcPlacement",
            Enabled: true,
            "{}",
            "test"));

        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, result.Status);
        Assert.Empty(result.Frame);
        Assert.Contains("merchant open/list/update", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    private static ItemDefinitionCatalog Catalog()
    {
        var mapper = new ItemContentMapper();
        var validation = new ItemContentValidator().Validate(
        [
            mapper.Map(ItemRecord(100, maxStack: 20, category: "Material", stackPolicy: "Stackable", buyPrice: 10, sellPrice: 5)),
            mapper.Map(ItemRecord(200, maxStack: 1, category: "Equipment", stackPolicy: "Single", buyPrice: 50, sellPrice: 20)),
            mapper.Map(ItemRecord(300, maxStack: 1, category: "Quest", stackPolicy: "Single", sellPolicy: "NotSellable", buyPrice: 0, sellPrice: 0, quest: true))
        ]);
        Assert.Empty(validation.Quarantined);
        return validation.Catalog;
    }

    private static ItemDatabaseRecord ItemRecord(
        int id,
        int maxStack = 1,
        string category = "Generic",
        string stackPolicy = "Single",
        long buyPrice = 10,
        long sellPrice = 5,
        bool enabled = true,
        bool quest = false,
        string sellPolicy = "Sellable",
        bool? usable = null,
        bool? normalUse = null,
        bool? battleUse = null,
        bool? useOnOther = null,
        int? requiredLevel = null) =>
        new(
            id,
            $"item_{id}",
            $"Item {id}",
            null,
            category,
            stackPolicy,
            maxStack,
            "None",
            "Tradable",
            sellPolicy,
            buyPrice,
            sellPrice,
            "Gold",
            null,
            null,
            quest,
            enabled,
            "{}",
            "test-items-v1",
            "tests",
            usable,
            normalUse,
            battleUse,
            Equippable: category == "Equipment",
            useOnOther,
            requiredLevel);

    private static ItemUseEffectEngine ItemUseEngine(
        ItemDefinition item,
        IReadOnlyList<ItemEffectDefinition> effects) =>
        new(new ItemDefinitionCatalog([item]), new ItemEffectDefinitionCatalog(effects));

    private static ItemEffectDefinition Effect(
        int itemId,
        int index,
        ItemEffectType effectType,
        long value,
        int? durationSeconds = null) =>
        new(itemId, index, effectType, value, WorldAllowed: true, BattleAllowed: true,
            ItemTargetPolicy.Self, "Recovered", Enabled: true, "tests", durationSeconds);

    private static ItemUseActorState Actor(
        int level = 20,
        long currentHp = 500,
        long maximumHp = 1_000,
        long currentMp = 100,
        long maximumMp = 200) =>
        new(1, level, 0, "劍士", "男性", currentHp, maximumHp, currentMp, maximumMp);

    private static PlayerInventoryRuntime EmptyInventory(int capacity, ItemDefinitionCatalog catalog)
    {
        return new PlayerInventoryRuntime(Guid.NewGuid(), 1, capacity, itemCatalog: catalog);
    }

    private static Func<long> NextIds(long start = 1000)
    {
        var next = start;
        return () => Interlocked.Increment(ref next);
    }

    private static InventorySlot Slot(int slotIndex, long persistentId, int itemTemplateId, int quantity) =>
        new(
            slotIndex,
            persistentId,
            RuntimeObjectIds.Static(RuntimeObjectKind.Item, checked((int)(persistentId & 0x7FFFFFFF)), "tests").RuntimeObjectId,
            0,
            itemTemplateId,
            quantity,
            "None",
            "{}",
            Now,
            Now,
            1);

    private static (InventoryTransactionCoordinator Coordinator, InMemoryInventoryPersistenceStore Store, InMemoryInventoryAuditLedger Audit) Fixture(
        long gold,
        int capacity = 32,
        IReadOnlyList<InventorySlot>? seedSlots = null)
    {
        var catalog = Catalog();
        var audit = new InMemoryInventoryAuditLedger();
        var store = new InMemoryInventoryPersistenceStore(audit);
        store.Seed(new InventoryPersistenceBundle(
            new PlayerInventorySnapshot(Guid.NewGuid(), 1, capacity, 0, 0, "Clean", seedSlots ?? []),
            new CurrencyWalletSnapshot(1, [new CurrencyBalance("Gold", gold, 0, "Clean")])));
        var merchant = MerchantCatalog();
        var next = 20_000L;
        var coordinator = new InventoryTransactionCoordinator(catalog, merchant, store, audit, () => Interlocked.Increment(ref next), store.FailureInjection);
        return (coordinator, store, audit);
    }

    private static MerchantDefinitionCatalog MerchantCatalog() =>
        new(
        [
            new MerchantMapping(
                4001,
                1001,
                5001,
                "tests",
                "Test Merchant",
                "Default",
                "Gold",
                [
                    new MerchantItemDefinition(100, 10, PurchasingPrice: 4),
                    new MerchantItemDefinition(200, 50),
                    new MerchantItemDefinition(300, 1)
                ])
        ]);

    private static InventoryTransactionRequest Request(
        InventoryOperationType operationType,
        string idempotencyKey,
        long expectedVersion,
        InventoryMutationRequest mutation,
        int? merchantTemplateId = null) =>
        new(
            Guid.NewGuid(),
            idempotencyKey,
            1,
            "session-1",
            1,
            operationType,
            expectedVersion,
            "HeadlessInventoryScenario",
            [mutation],
            0,
            merchantTemplateId,
            Now);

    private sealed class ThrowingInventoryPersistenceStore : IInventoryPersistenceStore
    {
        public Task<OperationResult> ValidateAuthorityAsync(
            InventoryTransactionRequest request,
            CancellationToken cancellationToken) =>
            throw new TimeoutException("fixture timeout");

        public Task<InventoryPersistenceBundle> LoadAsync(long characterId, CancellationToken cancellationToken) =>
            throw new TimeoutException("fixture timeout");

        public Task<InventoryReplayLookup> FindCompletedAsync(
            string idempotencyKey,
            string payloadHash,
            CancellationToken cancellationToken) =>
            throw new TimeoutException("fixture timeout");

        public Task<OperationResult<IReadOnlyList<long>>> ReservePersistentItemIdsAsync(
            int count,
            CancellationToken cancellationToken) =>
            throw new TimeoutException("fixture timeout");

        public Task<OperationResult> CommitAsync(
            InventoryPersistenceCommit commit,
            CancellationToken cancellationToken) =>
            throw new TimeoutException("fixture timeout");
    }
}
