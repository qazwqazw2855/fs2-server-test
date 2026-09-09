using God2.ClassicServer.Runtime;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class WorldInteractionRuntimeTests
{
    [Fact]
    public async Task Active_battle_reservation_blocks_world_interaction_mutation()
    {
        var reservations = new InMemoryWorldBattleReservationRegistry();
        var fixture = Fixture.Create(battleReservations: reservations);
        var reserved = reservations.Reserve(
            Guid.NewGuid(),
            Guid.NewGuid(),
            fixture.Character.CharacterId,
            fixture.Player.Identity.RuntimeObjectId,
            Now);
        Assert.True(reserved.Succeeded);

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            CancellationToken.None);

        Assert.Equal(WorldInteractionResultCode.Rejected, result.Code);
        Assert.Equal("interaction.player_in_battle", result.FailureCode);
    }

    [Fact]
    public async Task Interaction_resolves_active_merchant_npc_and_routes_explicit_handler()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, result.Code);
        Assert.Equal("Merchant", result.Handler);
        Assert.Empty(result.NetworkBytes);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.NpcInteractionResolved);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.MerchantServiceResolved);
    }

    [Fact]
    public async Task Interaction_rejects_missing_target()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(long.MaxValue, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.TargetNotFound, result.Code);
        Assert.Equal("interaction.target_not_found", result.FailureCode);
    }

    [Fact]
    public async Task Interaction_rejects_inactive_target()
    {
        var fixture = Fixture.Create();
        fixture.Source.Objects.Add(fixture.Npc with
        {
            State = fixture.Npc.State with { Lifecycle = "Destroyed" }
        });

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.TargetInactive, result.Code);
    }

    [Fact]
    public async Task Interaction_rejects_target_on_different_map()
    {
        var fixture = Fixture.Create();
        var targetNpc = Assert.Single(fixture.Target.Objects.OfType<NpcObject>());

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(targetNpc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.DifferentMap, result.Code);
    }

    [Fact]
    public async Task Interaction_rejects_different_world_instance()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Sessions.Get(fixture.Session.SessionId).Value!;
        fixture.Sessions.Set(binding with { WorldInstanceId = "world:other" });

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.DifferentInstance, result.Code);
    }

    [Fact]
    public async Task Interaction_rejects_cross_character_ownership()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary) with
            {
                CharacterId = 999
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.OwnershipMismatch, result.Code);
    }

    [Fact]
    public async Task Interaction_rejects_forged_target_template()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary) with
            {
                TargetTemplateId = fixture.Npc.State.NpcTemplateId + 1
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Rejected, result.Code);
        Assert.Equal("interaction.target_template_mismatch", result.FailureCode);
    }

    [Fact]
    public async Task Production_range_policy_remains_evidence_blocked()
    {
        var fixture = Fixture.Create(rangePolicy: new EvidenceBlockedInteractionRangePolicy());

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.RangeEvidenceBlocked, result.Code);
        Assert.Equal("interaction.range_evidence_blocked", result.FailureCode);
    }

    [Fact]
    public async Task Exact_build_npc_range_policy_allows_each_axis_at_most_three()
    {
        var fixture = Fixture.Create(rangePolicy: new OfficialClientNpcInteractionRangePolicy());

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, result.Code);
        Assert.Equal(InteractionRangePolicyStatus.Verified, new OfficialClientNpcInteractionRangePolicy().Status);
        Assert.Equal(3, OfficialClientNpcInteractionRangePolicy.MaximumAxisDelta);
    }

    [Theory]
    [InlineData(13, 13, true)]
    [InlineData(13, 14, false)]
    [InlineData(14, 13, false)]
    [InlineData(7, 7, true)]
    [InlineData(6, 10, false)]
    public void Exact_build_npc_range_policy_uses_independent_absolute_axis_deltas(
        int targetX,
        int targetY,
        bool expected)
    {
        var fixture = Fixture.Create();
        var target = fixture.Npc with
        {
            State = fixture.Npc.State with { Position = new WorldPosition3(targetX, targetY) }
        };

        var result = new OfficialClientNpcInteractionRangePolicy().Evaluate(fixture.Player.State, target);

        Assert.Equal(expected, result.Allowed);
        Assert.Equal(
            expected ? string.Empty : "interaction.out_of_range",
            result.FailureCode);
    }

    [Fact]
    public async Task Deterministic_test_range_policy_rejects_out_of_range()
    {
        var fixture = Fixture.Create(rangePolicy: new DeterministicTestInteractionRangePolicy(1));

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.OutOfRange, result.Code);
    }

    [Fact]
    public async Task Unsupported_npc_interaction_is_safely_rejected()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.Dialog),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.UnsupportedInteraction, result.Code);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Invalid_merchant_binding_is_rejected()
    {
        var fixture = Fixture.Create(invalidMerchantBinding: true);

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.Merchant),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Rejected, result.Code);
        Assert.Equal("merchant.binding_invalid", result.FailureCode);
    }

    [Fact]
    public async Task Merchant_context_reaches_frozen_buy_backend()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.Merchant) with
        {
            MerchantTransaction = new InventoryTransactionRequest(
                Guid.NewGuid(),
                "inventory-buy-1",
                fixture.Character.CharacterId,
                fixture.Session.SessionId,
                fixture.Session.AccountId,
                InventoryOperationType.MerchantBuy,
                0,
                "UntrustedClientValueIsReplaced",
                [new InventoryMutationRequest(ItemTemplateId: 100, Quantity: 2)],
                0,
                999999,
                Now,
                InventoryMutationAuthorityKind.TrustedServer)
        };

        var result = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, result.Code);
        Assert.NotNull(result.InventoryResult);
        Assert.Equal(80, result.InventoryResult!.CurrencyAfter);
        Assert.Equal(2, result.InventoryResult.InventorySnapshot!.Slots.Sum(slot => slot.Quantity));
    }

    [Fact]
    public async Task Duplicate_interaction_returns_completed_result_without_second_execution()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary);

        var first = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);
        var second = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, first.Code);
        Assert.Equal(WorldInteractionResultCode.DuplicateCompleted, second.Code);
        Assert.True(second.IsDuplicate);
        Assert.Single(
            fixture.Events.Events,
            value => value.Kind == WorldInteractionEventKind.MerchantServiceResolved);
    }

    [Fact]
    public async Task Same_idempotency_key_with_changed_payload_is_replay_conflict()
    {
        var fixture = Fixture.Create();
        var first = fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary);
        var changed = first with
        {
            InteractionId = Guid.NewGuid(),
            TargetRuntimeEntityId = fixture.Portal.Identity.RuntimeObjectId,
            TargetTemplateId = fixture.Portal.Identity.TemplateId,
            RequestedInteractionType = RequestedInteractionType.Portal
        };

        Assert.Equal(
            WorldInteractionResultCode.Success,
            (await fixture.Coordinator.ExecuteAsync(first, TestContext.Current.CancellationToken)).Code);
        Assert.Equal(
            WorldInteractionResultCode.ReplayConflict,
            (await fixture.Coordinator.ExecuteAsync(changed, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public async Task Replay_conflict_discovered_after_cooldown_entry_is_not_reported_as_duplicate()
    {
        var idempotency = new ConflictOnSecondLookupIdempotencyStore();
        var fixture = Fixture.Create(idempotency: idempotency);
        var request = fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary);

        var result = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, idempotency.LookupCount);
        Assert.Equal(WorldInteractionResultCode.ReplayConflict, result.Code);
        Assert.Equal("interaction.replay_conflict", result.FailureCode);
        Assert.Single(
            fixture.Events.Events,
            value => value.Kind == WorldInteractionEventKind.InteractionRequested);
    }

    [Fact]
    public void In_memory_interaction_idempotency_store_is_capacity_bounded()
    {
        var store = new InMemoryInteractionIdempotencyStore(maximumEntries: 1);
        var fixture = Fixture.Create();
        var first = fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.Dialog);
        var second = fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.Dialog);
        var firstResult = WorldInteractionResult.Rejected(first, WorldInteractionResultCode.Success, "");
        var secondResult = WorldInteractionResult.Rejected(second, WorldInteractionResultCode.Success, "");

        store.Complete("first", "payload-1", firstResult);
        store.Complete("second", "payload-2", secondResult);

        Assert.False(store.Find("first", "payload-1").Found);
        Assert.True(store.Find("second", "payload-2").Found);
    }

    [Fact]
    public async Task Portal_transition_moves_player_and_rebinds_session()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, result.Code);
        Assert.False(fixture.Source.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        Assert.False(fixture.Source.EntityRegistry.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        var targetPlayer = Assert.IsType<PlayerObject>(fixture.Target.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Value);
        Assert.Equal(TargetMapId, targetPlayer.State.MapId);
        Assert.Equal(new WorldPosition3(128, 256), targetPlayer.State.Position);
        var rebound = fixture.Sessions.Get(fixture.Session.SessionId).Value!;
        Assert.Equal(TargetMapId, rebound.MapSession.MapId);
        Assert.Equal(1, rebound.PlayerRuntimeVersion);
        Assert.Equal(0, KeyedLockEntryCount(fixture.PortalTransitions));
    }

    [Fact]
    public async Task Portal_transition_clears_source_and_rebuilds_target_replication()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, result.Code);
        var transition = Assert.Single(fixture.PortalTransitions.Transitions);
        Assert.True(transition.ReplicationCleared);
        Assert.True(transition.ReplicationRebuilt);
        Assert.DoesNotContain(fixture.Source.Replication.Visibility.Snapshot, value => value.SessionId == fixture.Session.SessionId);
        Assert.Contains(fixture.Target.Replication.Visibility.Snapshot, value => value.SessionId == fixture.Session.SessionId);
    }

    [Fact]
    public async Task Portal_transition_persists_versioned_location_for_reconnect()
    {
        var fixture = Fixture.Create();

        await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);
        var persisted = await fixture.PortalStore.LoadAsync(
            fixture.Character.CharacterId,
            TestContext.Current.CancellationToken);

        Assert.Equal(TargetMapId, persisted.CurrentMapId);
        Assert.Equal(new WorldPosition3(128, 256), persisted.RawPosition);
        Assert.Equal(1, persisted.RuntimeVersion);
        Assert.Equal(fixture.Portal.Identity.TemplateId, persisted.LastPortalTemplateId);
        Assert.NotNull(persisted.LastTransitionId);
    }

    [Fact]
    public async Task Duplicate_portal_request_commits_once()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal);

        var first = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);
        var duplicate = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);
        var persisted = await fixture.PortalStore.LoadAsync(
            fixture.Character.CharacterId,
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, first.Code);
        Assert.Equal(WorldInteractionResultCode.DuplicateCompleted, duplicate.Code);
        Assert.Equal(1, persisted.RuntimeVersion);
        Assert.Single(fixture.Target.Objects.OfType<PlayerObject>());
    }

    [Fact]
    public async Task Concurrent_duplicate_portal_requests_have_one_commit()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken)));
        var persisted = await fixture.PortalStore.LoadAsync(
            fixture.Character.CharacterId,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, persisted.RuntimeVersion);
        Assert.Single(fixture.Target.Objects.OfType<PlayerObject>());
        Assert.Single(results, result => result.Code == WorldInteractionResultCode.Success);
        Assert.All(
            results.Where(result => result.Code != WorldInteractionResultCode.Success),
            result => Assert.Contains(result.Code, new[]
            {
                WorldInteractionResultCode.DuplicateCompleted,
                WorldInteractionResultCode.CooldownActive
            }));
    }

    [Fact]
    public async Task Portal_version_conflict_is_rejected_before_runtime_mutation()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal) with
        {
            ExpectedPlayerRuntimeVersion = 9
        };

        var result = await fixture.Coordinator.ExecuteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.VersionConflict, result.Code);
        Assert.True(fixture.Source.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        Assert.False(fixture.Target.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
    }

    [Theory]
    [InlineData(WorldInteractionFailurePoint.PersistenceFailureAfterSourceRemoval)]
    [InlineData(WorldInteractionFailurePoint.SessionRebindFailure)]
    [InlineData(WorldInteractionFailurePoint.TargetRegistryAddFailure)]
    [InlineData(WorldInteractionFailurePoint.ReplicationClearFailure)]
    [InlineData(WorldInteractionFailurePoint.ReplicationRebuildFailure)]
    [InlineData(WorldInteractionFailurePoint.DisconnectDuringTransition)]
    [InlineData(WorldInteractionFailurePoint.ReconnectDuringTransition)]
    public async Task Portal_failure_rolls_back_source_runtime(WorldInteractionFailurePoint point)
    {
        var fixture = Fixture.Create();
        fixture.FailureInjection.Point = point;
        if (point == WorldInteractionFailurePoint.PersistenceFailureAfterSourceRemoval)
        {
            fixture.PortalStore.FailureInjection.Point = point;
        }

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(fixture.Source.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        Assert.True(fixture.Source.EntityRegistry.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        Assert.False(fixture.Target.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        Assert.Contains(fixture.Source.ActiveSessions, value => value.SessionId == fixture.Session.SessionId);
        var rebound = fixture.Sessions.Get(fixture.Session.SessionId).Value!;
        Assert.Equal(SourceMapId, rebound.MapSession.MapId);
        Assert.False(rebound.IsTransitioning);
    }

    [Fact]
    public async Task Persistence_failure_before_runtime_mutation_leaves_source_untouched()
    {
        var fixture = Fixture.Create();
        fixture.FailureInjection.Point = WorldInteractionFailurePoint.PersistenceFailureBeforeRuntimeMutation;

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.PersistenceFailure, result.Code);
        Assert.True(fixture.Source.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
        Assert.False(fixture.Target.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded);
    }

    [Fact]
    public async Task Runtime_failure_after_database_commit_reloads_persisted_target()
    {
        var fixture = Fixture.Create();
        fixture.FailureInjection.Point = WorldInteractionFailurePoint.RuntimeFailureAfterDatabaseCommit;

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);
        var persisted = await fixture.PortalStore.LoadAsync(
            fixture.Character.CharacterId,
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.RuntimeTransitionFailure, result.Code);
        Assert.Equal(TargetMapId, persisted.CurrentMapId);
        Assert.Equal(TargetMapId, fixture.Sessions.Get(fixture.Session.SessionId).Value!.MapSession.MapId);
        Assert.Single(fixture.Target.Objects.OfType<PlayerObject>());
    }

    [Fact]
    public async Task Audit_failure_isolated_from_committed_portal_transition()
    {
        var fixture = Fixture.Create();
        fixture.FailureInjection.Point = WorldInteractionFailurePoint.AuditFailure;

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.Success, result.Code);
        Assert.Empty(fixture.Audit.Records);
        Assert.Equal(TargetMapId, fixture.Sessions.Get(fixture.Session.SessionId).Value!.MapSession.MapId);
    }

    [Fact]
    public async Task Rollback_failure_marks_recovery_required_without_dual_map_registration()
    {
        var fixture = Fixture.Create();
        fixture.FailureInjection.Point = WorldInteractionFailurePoint.SessionRebindFailure;
        fixture.FailureInjection.RollbackFailureEnabled = true;

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.Equal(WorldInteractionResultCode.RuntimeTransitionFailure, result.Code);
        var transition = Assert.Single(fixture.PortalTransitions.Transitions);
        Assert.Equal(PortalTransitionState.RecoveryRequired, transition.State);
        Assert.Equal("RecoveryRequired", transition.RollbackStatus);
        var inSource = fixture.Source.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded;
        var inTarget = fixture.Target.Objects.Get(fixture.Player.Identity.RuntimeObjectId).Succeeded;
        Assert.NotEqual(inSource, inTarget);
    }

    [Fact]
    public async Task Portal_transition_emits_semantic_events_and_audit()
    {
        var fixture = Fixture.Create();

        await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Portal.Identity.RuntimeObjectId, RequestedInteractionType.Portal),
            TestContext.Current.CancellationToken);

        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.PortalTransitionStarted);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.PlayerLeavingMap);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.SessionMapRebound);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.ReplicationScopeRebuilt);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.PortalTransitionCommitted);
        Assert.Contains(fixture.Audit.Records, value =>
            value.Handler == "Portal" &&
            value.SourceMapId == SourceMapId &&
            value.TargetMapId == TargetMapId &&
            value.SessionRebound &&
            value.ReplicationRebuilt);
    }

    [Fact]
    public async Task Inspector_filters_failed_interactions_and_returns_read_only_snapshots()
    {
        var fixture = Fixture.Create(rangePolicy: new EvidenceBlockedInteractionRangePolicy());
        await fixture.Coordinator.ExecuteAsync(
            fixture.Request(fixture.Npc.Identity.RuntimeObjectId, RequestedInteractionType.NpcPrimary),
            TestContext.Current.CancellationToken);

        var snapshot = fixture.Inspector.Capture(
            new WorldInteractionInspectorQuery(FailedOnly: true, EvidenceBlockedOnly: true),
            Now);

        var item = Assert.Single(snapshot.Interactions);
        Assert.Equal(WorldInteractionResultCode.RangeEvidenceBlocked, item.Result);
        Assert.Equal(InteractionRangePolicyStatus.EvidenceBlocked, item.RangePolicyStatus);
        Assert.NotEmpty(snapshot.NpcServices);
        Assert.NotEmpty(snapshot.Portals);
        Assert.All(snapshot.Portals, portal => Assert.Equal("SerializerBlockedByEvidence", portal.ProtocolStatus));
        Assert.IsAssignableFrom<IReadOnlyList<InteractionInspectorItem>>(snapshot.Interactions);
    }

    [Fact]
    public void Handler_registry_is_explicit_and_does_not_scan_by_reflection()
    {
        var fixture = Fixture.Create();

        Assert.True(fixture.Handlers.Resolve(RequestedInteractionType.Merchant).Succeeded);
        Assert.True(fixture.Handlers.Resolve(RequestedInteractionType.Portal).Succeeded);
        Assert.False(fixture.Handlers.Resolve(RequestedInteractionType.Dialog).Succeeded);
    }

    [Fact]
    public void Replication_clear_session_removes_known_state_and_targeted_pending_queues()
    {
        var fixture = Fixture.Create();
        fixture.Source.Replication.RecalculateVisibility(
            fixture.Binding.MapSession,
            fixture.Player.State.Position,
            fixture.Source.Objects.ActiveObjects,
            fixture.Player.State.VisibilityRadius,
            Now);
        Assert.Contains(
            fixture.Source.Replication.SpawnQueue.Snapshot,
            value => value.TargetSessionId == fixture.Session.SessionId);

        fixture.Source.Replication.ClearSession(fixture.Session.SessionId);

        Assert.DoesNotContain(
            fixture.Source.Replication.SpawnQueue.Snapshot,
            value => value.TargetSessionId == fixture.Session.SessionId);
        Assert.DoesNotContain(
            fixture.Source.Replication.Visibility.Snapshot,
            value => value.SessionId == fixture.Session.SessionId);
    }

    [Fact]
    public void Portal_and_merchant_protocol_serializers_remain_blocked_with_zero_bytes()
    {
        var fixture = Fixture.Create();

        var portal = new PortalSerializer().Serialize(fixture.Portal.State);
        var merchant = new MerchantSerializer().Serialize(Assert.Single(fixture.Source.Objects.OfType<MerchantObject>()).State);
        var npc = new NpcSerializer().Serialize(fixture.Npc.State);

        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, portal.Status);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, merchant.Status);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, npc.Status);
        Assert.Empty(portal.Frame);
        Assert.Empty(merchant.Frame);
        Assert.Empty(npc.Frame);
    }

    [Fact]
    public void Portal_content_validator_quarantines_missing_target_map_and_disabled_content()
    {
        PortalDefinition[] definitions =
        [
            new(
                1,
                new PortalEndpoint(SourceMapId, new WorldPosition3(1, 1)),
                new PortalEndpoint(999, new WorldPosition3(2, 2)),
                "Unknown",
                "Unknown",
                "tests"),
            new(
                2,
                new PortalEndpoint(SourceMapId, new WorldPosition3(1, 1)),
                new PortalEndpoint(TargetMapId, new WorldPosition3(2, 2)),
                "Unknown",
                "Unknown",
                "tests",
                Enabled: false)
        ];

        var result = new RuntimeContentValidator().ValidatePortals(
            definitions,
            new HashSet<int> { SourceMapId, TargetMapId });

        Assert.Empty(result.Valid);
        Assert.Equal(2, result.Quarantined.Count);
        Assert.Contains(result.Issues, issue => issue.Code == "content.missing_map_reference");
        Assert.Contains(result.Issues, issue => issue.Code == "content.disabled");
    }

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 3, 0, 0, TimeSpan.Zero);
    private const int SourceMapId = 100;
    private const int TargetMapId = 200;

    private static class TestContext
    {
        public static TestCancellationContext Current { get; } = new();
    }

    private sealed record TestCancellationContext
    {
        public CancellationToken CancellationToken => System.Threading.CancellationToken.None;
    }

    private sealed class Fixture
    {
        private Fixture(
            WorldRuntime world,
            MapRuntime source,
            MapRuntime target,
            RuntimeSession session,
            CharacterSummary character,
            WorldSessionBinding binding,
            InMemoryWorldInteractionSessionRegistry sessions,
            NpcObject npc,
            PortalObject portal,
            InteractionHandlerRegistry handlers,
            InMemoryPortalTransitionStore portalStore,
            PortalTransitionCoordinator portalTransitions,
            InMemoryWorldInteractionEventSink events,
            InMemoryWorldInteractionAuditLedger audit,
            WorldInteractionFailureInjection failureInjection,
            IWorldInteractionCoordinator coordinator,
            WorldInteractionInspector inspector)
        {
            World = world;
            Source = source;
            Target = target;
            Session = session;
            Character = character;
            Binding = binding;
            Sessions = sessions;
            Npc = npc;
            Portal = portal;
            Handlers = handlers;
            PortalStore = portalStore;
            PortalTransitions = portalTransitions;
            Events = events;
            Audit = audit;
            FailureInjection = failureInjection;
            Coordinator = coordinator;
            Inspector = inspector;
            Player = Assert.Single(source.Objects.OfType<PlayerObject>());
        }

        public WorldRuntime World { get; }
        public MapRuntime Source { get; }
        public MapRuntime Target { get; }
        public RuntimeSession Session { get; }
        public CharacterSummary Character { get; }
        public WorldSessionBinding Binding { get; }
        public InMemoryWorldInteractionSessionRegistry Sessions { get; }
        public PlayerObject Player { get; }
        public NpcObject Npc { get; }
        public PortalObject Portal { get; }
        public InteractionHandlerRegistry Handlers { get; }
        public InMemoryPortalTransitionStore PortalStore { get; }
        public PortalTransitionCoordinator PortalTransitions { get; }
        public InMemoryWorldInteractionEventSink Events { get; }
        public InMemoryWorldInteractionAuditLedger Audit { get; }
        public WorldInteractionFailureInjection FailureInjection { get; }
        public IWorldInteractionCoordinator Coordinator { get; }
        public WorldInteractionInspector Inspector { get; }

        public WorldInteractionRequest Request(
            long targetRuntimeEntityId,
            RequestedInteractionType type,
            string? idempotencyKey = null)
        {
            var target = World.ActiveMaps.Values
                .Select(map => map.Objects.Get(targetRuntimeEntityId))
                .FirstOrDefault(result => result.Succeeded)
                ?.Value;
            return new WorldInteractionRequest(
                Guid.NewGuid(),
                idempotencyKey ?? Guid.NewGuid().ToString("N"),
                Session.SessionId,
                Character.CharacterId,
                Player.Identity.RuntimeObjectId,
                targetRuntimeEntityId,
                target?.Identity.TemplateId,
                type,
                0,
                null,
                "TrustedInternalHeadlessTest",
                Now,
                Guid.NewGuid().ToString("N"));
        }

        public static Fixture Create(
            IInteractionRangePolicy? rangePolicy = null,
            bool invalidMerchantBinding = false,
            IWorldBattleReservationRegistry? battleReservations = null,
            IInteractionIdempotencyStore? idempotency = null)
        {
            var content = Content(invalidMerchantBinding);
            var factory = new MapRuntimeFactory();
            var session = RuntimeSession.Connected("connection-1", "127.0.0.1:1000", Now) with
            {
                SessionId = "session-1",
                AccountId = 1,
                CharacterId = 10,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.InWorld
            };
            var character = new CharacterSummary(
                10,
                1,
                "RuntimeTester",
                "Class1",
                "Gender1",
                "LifeSkill1",
                1,
                "Test",
                SourceMapId,
                10,
                10,
                "Active",
                Now,
                null);
            var bind = new WorldSessionBinder(factory).Bind(
                session,
                character,
                content,
                WorldContentMode.MariaDbAuthoritative);
            Assert.True(bind.Succeeded);
            var binding = bind.Value!;
            var targetCreate = factory.Create(content, TargetMapId);
            Assert.True(targetCreate.Succeeded);
            var target = targetCreate.Value!.MapRuntime;
            var world = new WorldRuntime();
            world.Add(binding.MapRuntime);
            world.Add(target);
            var sessions = new InMemoryWorldInteractionSessionRegistry();
            sessions.Seed(binding);
            var portalStore = new InMemoryPortalTransitionStore();
            portalStore.Seed(new PortalLocationState(
                character.CharacterId,
                SourceMapId,
                new WorldPosition3(character.PositionX, character.PositionY),
                WorldDirection.Unknown,
                0,
                null,
                null,
                Now));
            var events = new InMemoryWorldInteractionEventSink();
            var audit = new InMemoryWorldInteractionAuditLedger();
            var failure = new WorldInteractionFailureInjection();
            var portalTransitions = new PortalTransitionCoordinator(
                world,
                content,
                factory,
                sessions,
                portalStore,
                events,
                audit,
                failure);
            var merchantMappings = invalidMerchantBinding
                ? Array.Empty<MerchantMapping>()
                : binding.MapRuntime.Definition.MerchantMappings.ToArray();
            var merchantCatalog = new MerchantDefinitionCatalog(merchantMappings);
            var inventoryAudit = new InMemoryInventoryAuditLedger();
            var inventoryStore = new InMemoryInventoryPersistenceStore(inventoryAudit);
            inventoryStore.Seed(new InventoryPersistenceBundle(
                new PlayerInventorySnapshot(Guid.NewGuid(), character.CharacterId, 32, 0, 0, "Clean", []),
                new CurrencyWalletSnapshot(character.CharacterId, [new CurrencyBalance("Gold", 100, 0, "Clean")])));
            var itemCatalog = ItemCatalog();
            var nextItemId = 10_000L;
            var inventory = new InventoryTransactionCoordinator(
                itemCatalog,
                merchantCatalog,
                inventoryStore,
                inventoryAudit,
                () => Interlocked.Increment(ref nextItemId),
                inventoryStore.FailureInjection);
            var handlers = new InteractionHandlerRegistry(
            [
                new MerchantInteractionHandler(merchantCatalog, inventory, events),
                new PortalInteractionHandler(portalTransitions)
            ]);
            var router = new NpcInteractionRouter(handlers, events);
            var effectiveRange = rangePolicy ?? new DeterministicTestInteractionRangePolicy(100);
            var coordinator = new WorldInteractionCoordinator(
                new RuntimeInteractionTargetResolver(world, sessions),
                new RuntimeInteractionEligibilityPolicy(effectiveRange),
                router,
                idempotency ?? new InMemoryInteractionIdempotencyStore(),
                new InFlightInteractionCooldownStore(),
                events,
                audit,
                failure,
                battleReservations);
            var inspector = new WorldInteractionInspector(audit, portalTransitions, world, effectiveRange);
            return new Fixture(
                world,
                binding.MapRuntime,
                target,
                session,
                character,
                binding,
                sessions,
                Assert.Single(binding.MapRuntime.Objects.OfType<NpcObject>()),
                Assert.Single(binding.MapRuntime.Objects.OfType<PortalObject>()),
                handlers,
                portalStore,
                portalTransitions,
                events,
                audit,
                failure,
                coordinator,
                inspector);
        }

        private static ItemDefinitionCatalog ItemCatalog()
        {
            var mapper = new ItemContentMapper();
            var validation = new ItemContentValidator().Validate(
            [
                mapper.Map(new ItemDatabaseRecord(
                    100,
                    "item_100",
                    "Item 100",
                    null,
                    "Material",
                    "Stackable",
                    20,
                    "None",
                    "Tradable",
                    "Sellable",
                    10,
                    5,
                    "Gold",
                    null,
                    null,
                    false,
                    true,
                    "{}",
                    "test-items-v1",
                    "tests"))
            ]);
            Assert.Empty(validation.Quarantined);
            return validation.Catalog;
        }

        private static WorldContentSnapshot Content(bool invalidMerchantBinding)
        {
            var merchantId = 4001;
            var sourceNpc = new NpcPlacementRecord(
                5001,
                1001,
                SourceMapId,
                new WorldPosition3(12, 10),
                WorldDirection.South,
                "Always",
                merchantId,
                "tests",
                1,
                "tests",
                "Merchant NPC",
                "Unknown",
                "Merchant");
            var merchant = new MerchantMapping(
                merchantId,
                invalidMerchantBinding ? 9999 : sourceNpc.NpcTemplateId,
                sourceNpc.PlacementId,
                "tests",
                "Merchant",
                "Default",
                "Gold",
                [new MerchantItemDefinition(100, 10)]);
            var portal = new PortalDefinition(
                3001,
                new PortalEndpoint(SourceMapId, new WorldPosition3(14, 10)),
                new PortalEndpoint(TargetMapId, new WorldPosition3(128, 256)),
                "ContactArea",
                "Unknown",
                "tests",
                "Portal");
            var source = Map(
                SourceMapId,
                [sourceNpc],
                [portal],
                invalidMerchantBinding ? [] : [merchant]);
            var targetNpc = new NpcPlacementRecord(
                6001,
                2001,
                TargetMapId,
                new WorldPosition3(130, 256),
                WorldDirection.North,
                "Always",
                null,
                "tests",
                1,
                "tests",
                "Target NPC");
            var target = Map(TargetMapId, [targetNpc], [], []);
            return new WorldContentSnapshot(
                new Dictionary<int, MapDefinition>
                {
                    [SourceMapId] = source,
                    [TargetMapId] = target
                },
                []);
        }

        private static MapDefinition Map(
            int mapId,
            IReadOnlyList<NpcPlacementRecord> npcs,
            IReadOnlyList<PortalDefinition> portals,
            IReadOnlyList<MerchantMapping> merchants) =>
            new(
                mapId,
                mapId,
                $"Map {mapId}",
                "tests",
                new MapBounds(0, 0, 1000, 1000),
                [new SpawnPoint("Default", new WorldPosition3(10, 10), WorldDirection.South, "tests")],
                npcs,
                [],
                portals,
                merchants,
                []);
    }

    private static int KeyedLockEntryCount(object coordinator)
    {
        var locks = coordinator.GetType()
            .GetField("_locks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(coordinator)!;
        var entries = (System.Collections.IDictionary)locks.GetType()
            .GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(locks)!;
        return entries.Count;
    }

    private sealed class ConflictOnSecondLookupIdempotencyStore : IInteractionIdempotencyStore
    {
        private int _lookupCount;

        public int LookupCount => Volatile.Read(ref _lookupCount);

        public InteractionReplayLookup Find(string idempotencyKey, string payloadHash) =>
            Interlocked.Increment(ref _lookupCount) == 1
                ? new InteractionReplayLookup(false, true, null)
                : new InteractionReplayLookup(true, false, WorldInteractionResult.Rejected(
                    new WorldInteractionRequest(
                        Guid.NewGuid(), idempotencyKey, "stale", 1, 1, 1, 1,
                        RequestedInteractionType.NpcPrimary, 0, null, "test", Now, "stale"),
                    WorldInteractionResultCode.Success,
                    string.Empty));

        public void Complete(string idempotencyKey, string payloadHash, WorldInteractionResult result)
        {
            throw new InvalidOperationException("A replay conflict must stop before completion.");
        }
    }
}
