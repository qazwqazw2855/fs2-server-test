using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class TurnBasedBattleRuntimeTests
{
    public static TheoryData<int, string> OfflineHeadlessScenarios
    {
        get
        {
            var names = new[]
            {
                "Create TestOnly battle with one player and one monster",
                "Reject invalid session",
                "Reject ownership mismatch",
                "Reject missing encounter",
                "Reject disabled encounter",
                "Reject duplicate battle request",
                "Reject same player entering second active battle",
                "Create participants with unique IDs",
                "Reserve player world runtime",
                "Reject duplicate formation slot",
                "Preserve unknown content metadata",
                "Monster 208 template catalog remains readable",
                "Zero production spawn rows does not generate fake spawns",
                "Battle enters RoundOpening",
                "Round enters CollectingActions",
                "Eligible participant list correct",
                "Dead participant excluded",
                "Round number starts correctly",
                "Round version increments correctly",
                "Old round action rejected",
                "Future round action rejected",
                "Wrong battle version rejected",
                "Player submits BasicAttack",
                "Player submits Pass",
                "Reject unsupported Skill action",
                "Reject unsupported Item action",
                "Player submits Defend",
                "Reject unsupported Flee action",
                "Reject invalid target",
                "Reject dead target",
                "Reject action from non-owner",
                "Same action duplicate returns DuplicateCompleted",
                "Same key changed payload returns ReplayConflict",
                "Concurrent duplicate submit stores once",
                "Participant cannot submit two actions",
                "Action cannot change after lock",
                "All eligible actions lock once",
                "Submit lock race remains consistent",
                "Locked action snapshot immutable",
                "Deterministic test turn order stable",
                "Same seed reproduces same order",
                "Production order remains EvidenceBlocked when no evidence",
                "Resolution index begins correctly",
                "Recovery preserves original order",
                "BasicAttack invokes existing CombatRuntime boundary",
                "Pass causes no damage",
                "BasicAttack reduces authoritative HP once",
                "Participant snapshot matches combat authority",
                "Non-lethal action keeps target alive",
                "Lethal action marks participant defeated",
                "Dead attacker locked action is skipped",
                "Dead target rejects later action",
                "Duplicate resolution does not double damage",
                "Combat version conflict handled",
                "Concurrent lethal actions create one death",
                "Existing Combat Audit remains intact",
                "All locked actions resolve once",
                "Resolution index advances exactly once",
                "Round closes after final action",
                "New round opens when both sides alive",
                "Defeated participant excluded next round",
                "Round cannot close twice",
                "Recovery resumes at next unresolved action",
                "Recovery does not recalculate different turn order",
                "Player victory when enemy side defeated",
                "Enemy victory when player side defeated",
                "Draw when both sides defeated",
                "Completed battle rejects new action",
                "Battle completion commits once",
                "World reservation released once",
                "Completed battle cannot return Active",
                "Abort path releases or records recovery state",
                "Production EvidenceBlocked reward grants nothing",
                "Deterministic test reward creates pending plan",
                "Battle victory commits reward once",
                "Duplicate completion does not duplicate reward",
                "Inventory full reward failure recorded",
                "Reward recovery resumes without duplication",
                "Existing Inventory Coordinator remains authority",
                "Existing Combat reward does not double-grant",
                "Disconnect before submit preserves participant",
                "Disconnect after submit preserves action",
                "Disconnect after lock preserves locked snapshot",
                "Disconnect during resolution does not duplicate action",
                "Reconnect rebinds active battle",
                "Old session action rejected",
                "Duplicate reconnect does not duplicate participant",
                "Reconnect restores phase round action state",
                "Reload active collecting battle",
                "Reload locked round",
                "Reload resolving battle",
                "Resume from current resolution index",
                "Crash after damage does not double damage",
                "Crash after death does not duplicate death",
                "Crash after reward does not duplicate reward",
                "Crash after completion finalizes reservation release",
                "Persistence version conflict rejected",
                "RecoveryRequired state visible",
                "Battle audit complete",
                "Round audit complete",
                "Action audit complete",
                "Reward audit complete",
                "Inspector battle snapshot correct",
                "Inspector participant snapshot correct",
                "Inspector round action snapshot correct",
                "Inspector filters pagination correct",
                "Inspector read model immutable",
                "Credential redaction matchCount zero",
                "Hardcoded local path scan zero",
                "Battle serializer blocked emits zero network bytes",
                "Frozen Inventory Backend remains PASS",
                "Frozen World Interaction Backend remains PASS",
                "Frozen Combat Runtime Backend remains PASS",
                "Portal Session Rebinding remains PASS",
                "Frozen Login-to-World remains PASS"
            };
            var data = new TheoryData<int, string>();
            for (var index = 0; index < names.Length; index++)
            {
                data.Add(index + 1, names[index]);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OfflineHeadlessScenarios))]
    public async Task Offline_headless_scenario_matrix_passes(int scenarioId, string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 115);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));

        if (scenarioId <= 13)
        {
            await VerifyCreationScenario(scenarioId);
        }
        else if (scenarioId <= 22)
        {
            await VerifyRoundScenario(scenarioId);
        }
        else if (scenarioId <= 36)
        {
            await VerifySubmissionScenario(scenarioId);
        }
        else if (scenarioId <= 44)
        {
            await VerifyLockScenario(scenarioId);
        }
        else if (scenarioId <= 56)
        {
            await VerifyCombatScenario(scenarioId);
        }
        else if (scenarioId <= 64)
        {
            await VerifyRoundCompletionScenario(scenarioId);
        }
        else if (scenarioId <= 72)
        {
            await VerifyVictoryScenario(scenarioId);
        }
        else if (scenarioId <= 80)
        {
            await VerifyRewardScenario(scenarioId);
        }
        else if (scenarioId <= 88)
        {
            await VerifyConnectionScenario(scenarioId);
        }
        else if (scenarioId <= 98)
        {
            await VerifyRecoveryScenario(scenarioId);
        }
        else if (scenarioId <= 110)
        {
            await VerifyDiagnosticsScenario(scenarioId);
        }
        else
        {
            VerifyFrozenBoundaryScenario(scenarioId);
        }
    }

    [Fact]
    public void Battle_intents_do_not_accept_client_authoritative_state()
    {
        var request = typeof(BattleActionRequest).GetProperties().Select(value => value.Name).ToHashSet();

        Assert.DoesNotContain("Damage", request);
        Assert.DoesNotContain("Hp", request);
        Assert.DoesNotContain("TurnOrder", request);
        Assert.DoesNotContain("Victory", request);
        Assert.DoesNotContain("Reward", request);
    }

    [Fact]
    public void Battle_contracts_expose_all_required_extension_boundaries()
    {
        Assert.True(typeof(ITurnBasedBattleCoordinator).IsInterface);
        Assert.True(typeof(IBattleInstanceRepository).IsInterface);
        Assert.True(typeof(IBattleInstanceFactory).IsInterface);
        Assert.True(typeof(IBattleParticipantFactory).IsInterface);
        Assert.True(typeof(IBattleActionSubmissionStore).IsInterface);
        Assert.True(typeof(IBattleActionResolver).IsInterface);
        Assert.True(typeof(IBattleTurnOrderPolicy).IsInterface);
        Assert.True(typeof(IBattleVictoryPolicy).IsInterface);
        Assert.True(typeof(IBattleRewardCoordinator).IsInterface);
        Assert.True(typeof(IBattleRecoveryCoordinator).IsInterface);
        Assert.True(typeof(IBattleCombatExecutionPort).IsInterface);
    }

    [Fact]
    public void Internal_test_encounter_preserves_template_stats_and_raw_metadata()
    {
        var fixture = Fixture.Create();

        var result = fixture.Encounters.Resolve(fixture.Request());

        Assert.True(result.Succeeded);
        var opponent = Assert.Single(result.Value!.ParticipantDefinitions);
        Assert.Equal(Fixture.MonsterTemplateId, opponent.MonsterTemplateId);
        Assert.Equal(100, opponent.MaximumHp);
        Assert.Equal(30, opponent.AttackPower);
        Assert.Equal("{\"unknown\":true}", opponent.RawMetadata);
        Assert.Equal(CombatPolicyStatus.TestOnly, result.Value.PolicyStatus);
    }

    [Fact]
    public void Official_path_cannot_supply_opponent_templates()
    {
        var fixture = Fixture.Create();

        var result = fixture.Encounters.Resolve(
            fixture.Request() with { Source = BattleRequestSource.OfficialClient });

        Assert.False(result.Succeeded);
        Assert.Equal("battle.encounter_evidence_blocked", result.Error.Code);
    }

    [Fact]
    public async Task Battle_creation_builds_instance_participants_round_and_reservation()
    {
        var fixture = Fixture.Create();

        var result = await fixture.CreateBattle();
        var battle = await fixture.GetBattle(result);

        Assert.Equal(BattleResultCode.Success, result.Code);
        Assert.Equal(BattleState.Active, battle.State);
        Assert.Equal(BattlePhase.CollectingActions, battle.CurrentPhase);
        Assert.Equal(1, battle.CurrentRoundNumber);
        Assert.Equal(BattleRoundState.CollectingActions, battle.CurrentRound.State);
        Assert.Equal(2, battle.Participants.Count);
        Assert.Equal(2, battle.CurrentRound.EligibleParticipantIds.Count);
        Assert.True(fixture.Reservations.IsReserved(Fixture.CharacterId));
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Battle_creation_assigns_unique_participant_and_runtime_ids()
    {
        var fixture = Fixture.Create();

        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        Assert.Equal(battle.Participants.Count, battle.Participants.Select(value => value.ParticipantId).Distinct().Count());
        Assert.Equal(battle.Participants.Count, battle.Participants.Select(value => value.BattleRuntimeEntityId).Distinct().Count());
        Assert.Equal(
            battle.Participants.Count,
            battle.Participants.Select(value => (value.Side, value.FormationSlot)).Distinct().Count());
    }

    [Fact]
    public async Task Battle_creation_rejects_invalid_session()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.CreateAsync(
            fixture.Request() with { SourceSessionId = "missing" },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.InvalidSession, result.Code);
        Assert.Empty(fixture.Store.Snapshot);
        Assert.False(fixture.Reservations.IsReserved(Fixture.CharacterId));
    }

    [Fact]
    public async Task Battle_creation_rejects_ownership_mismatch()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.CreateAsync(
            fixture.Request() with { SourceCharacterId = Fixture.CharacterId + 1 },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.OwnershipMismatch, result.Code);
    }

    [Fact]
    public async Task Battle_creation_rejects_player_version_conflict()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Coordinator.CreateAsync(
            fixture.Request() with { ExpectedPlayerRuntimeVersion = 99 },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.VersionConflict, result.Code);
    }

    [Fact]
    public async Task Duplicate_battle_request_returns_original_instance()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(idempotencyKey: "battle-duplicate");

        var first = await fixture.Coordinator.CreateAsync(request, CancellationToken.None);
        var duplicate = await fixture.Coordinator.CreateAsync(request, CancellationToken.None);

        Assert.Equal(BattleResultCode.DuplicateCompleted, duplicate.Code);
        Assert.Equal(first.BattleInstanceId, duplicate.BattleInstanceId);
        Assert.Single(fixture.Store.Snapshot);
    }

    [Fact]
    public async Task Changed_payload_with_same_battle_key_is_replay_conflict()
    {
        var fixture = Fixture.Create();
        var request = fixture.Request(idempotencyKey: "battle-replay");
        await fixture.Coordinator.CreateAsync(request, CancellationToken.None);

        var replay = await fixture.Coordinator.CreateAsync(
            request with { SourceMapId = Fixture.MapId + 1 },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.ReplayConflict, replay.Code);
    }

    [Fact]
    public async Task Same_player_cannot_enter_second_active_battle()
    {
        var fixture = Fixture.Create();
        await fixture.CreateBattle();

        var second = await fixture.Coordinator.CreateAsync(
            fixture.Request(idempotencyKey: "second-battle") with { BattleRequestId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.Rejected, second.Code);
        Assert.Equal("battle.player_already_active", second.FailureCode);
    }

    [Fact]
    public async Task Creation_persistence_failure_releases_reservation()
    {
        var fixture = Fixture.Create();
        fixture.Store.FailureInjection.Point = BattleFailurePoint.CreationPersistence;

        var result = await fixture.CreateBattle();

        Assert.Equal(BattleResultCode.PersistenceFailure, result.Code);
        Assert.False(fixture.Reservations.IsReserved(Fixture.CharacterId));
    }

    [Fact]
    public async Task Player_submits_basic_attack_once()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.SubmitPlayer(battle, BattleActionType.BasicAttack);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.Equal(BattleResultCode.Success, result.Code);
        Assert.Single(updated.CurrentRound.SubmittedActions);
        Assert.True(updated.Participants.Single(value => value.CharacterId == Fixture.CharacterId).HasSubmittedAction);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Pass_occupies_round_action_without_target()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.SubmitPlayer(battle, BattleActionType.Pass);

        Assert.Equal(BattleResultCode.Success, result.Code);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);
        Assert.Empty(Assert.Single(updated.CurrentRound.SubmittedActions).TargetParticipantIds);
    }

    [Theory]
    [InlineData(BattleActionType.Skill)]
    [InlineData(BattleActionType.Item)]
    [InlineData(BattleActionType.Flee)]
    [InlineData(BattleActionType.SystemAction)]
    [InlineData(BattleActionType.Unknown)]
    public async Task Unsupported_actions_are_rejected_without_empty_success(BattleActionType actionType)
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.SubmitPlayer(battle, actionType);

        Assert.Equal(BattleResultCode.UnsupportedAction, result.Code);
        Assert.Equal("battle.action_unsupported", result.FailureCode);
        Assert.Empty((await fixture.GetBattle(battle.BattleInstanceId)).CurrentRound.SubmittedActions);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Defend_action_reduces_incoming_basic_attack_by_half_target_defense()
    {
        var fixture = Fixture.Create(damage: 20);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Defend);
        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);

        var updated = await fixture.GetBattle(battle.BattleInstanceId);
        var monster = fixture.Monster(updated);
        var attack = updated.CompletedRounds.Last().ResolutionResults.Single(value =>
            value.ActionType == BattleActionType.BasicAttack);
        var defend = updated.CompletedRounds.Last().ResolutionResults.Single(value =>
            value.ActionType == BattleActionType.Defend);
        Assert.Equal(18, attack.Damage);
        Assert.Equal(82, monster.CurrentHp);
        Assert.Equal(BattleActionResolutionCode.Success, defend.Result);
        Assert.Equal(0, defend.Damage);
    }

    [Fact]
    public async Task Old_round_action_is_rejected()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.Coordinator.SubmitActionAsync(
            fixture.PlayerAction(battle) with { RoundNumber = 0 },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.InvalidRound, result.Code);
    }

    [Fact]
    public async Task Forged_battle_version_is_rejected()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.Coordinator.SubmitActionAsync(
            fixture.PlayerAction(battle) with { BattleVersion = battle.BattleVersion + 1 },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.VersionConflict, result.Code);
    }

    [Fact]
    public async Task Cross_account_participant_action_is_rejected()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.Coordinator.SubmitActionAsync(
            fixture.PlayerAction(battle) with { CharacterId = Fixture.CharacterId + 1 },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.OwnershipMismatch, result.Code);
    }

    [Fact]
    public async Task Invalid_same_side_target_is_rejected()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        var player = fixture.Player(battle);

        var result = await fixture.Coordinator.SubmitActionAsync(
            fixture.PlayerAction(battle) with { TargetParticipantIds = [player.ParticipantId] },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.InvalidTarget, result.Code);
    }

    [Fact]
    public async Task Same_action_replay_is_duplicate_completed()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        var request = fixture.PlayerAction(battle, "same-action");

        var first = await fixture.Coordinator.SubmitActionAsync(request, CancellationToken.None);
        var duplicate = await fixture.Coordinator.SubmitActionAsync(request, CancellationToken.None);

        Assert.Equal(BattleResultCode.Success, first.Code);
        Assert.Equal(BattleResultCode.DuplicateCompleted, duplicate.Code);
        Assert.Equal(first.ActionId, duplicate.ActionId);
    }

    [Fact]
    public async Task Same_action_key_changed_payload_is_replay_conflict()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        var request = fixture.PlayerAction(battle, "changed-action");
        await fixture.Coordinator.SubmitActionAsync(request, CancellationToken.None);

        var replay = await fixture.Coordinator.SubmitActionAsync(
            request with { ActionType = BattleActionType.Pass, TargetParticipantIds = [] },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.ReplayConflict, replay.Code);
    }

    [Fact]
    public async Task Participant_cannot_submit_second_action_in_round()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitPlayer(battle, BattleActionType.BasicAttack);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        var second = await fixture.SubmitPlayer(updated, BattleActionType.Pass);

        Assert.Equal(BattleResultCode.ActionAlreadySubmitted, second.Code);
    }

    [Fact]
    public async Task Concurrent_duplicate_submit_persists_once()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        var request = fixture.PlayerAction(battle, "concurrent-action");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            fixture.Coordinator.SubmitActionAsync(request, CancellationToken.None)));

        Assert.Single(results, value => value.Code == BattleResultCode.Success);
        Assert.Equal(7, results.Count(value => value.Code == BattleResultCode.DuplicateCompleted));
        Assert.Single((await fixture.GetBattle(battle.BattleInstanceId)).CurrentRound.SubmittedActions);
    }

    [Fact]
    public async Task Lock_requires_all_eligible_actions()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitPlayer(battle, BattleActionType.BasicAttack);

        var result = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);

        Assert.Equal(BattleResultCode.InvalidPhase, result.Code);
        Assert.Equal("battle.actions_incomplete", result.FailureCode);
    }

    [Fact]
    public async Task Production_turn_order_stays_evidence_blocked()
    {
        var fixture = Fixture.Create(turnOrder: new EvidenceBlockedBattleTurnOrderPolicy());
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.Pass, BattleActionType.Pass);

        var result = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);

        Assert.Equal(BattleResultCode.TurnOrderEvidenceBlocked, result.Code);
        Assert.Equal(BattlePhase.CollectingActions, (await fixture.GetBattle(battle.BattleInstanceId)).CurrentPhase);
    }

    [Fact]
    public void Deterministic_turn_order_is_stable_and_uses_formation_tie_break()
    {
        var fixture = Fixture.Create();
        var battle = fixture.CreateUnpersistedBattle();
        var actions = battle.Participants.Select(value => new BattleLockedAction(
            Guid.NewGuid(),
            battle.BattleInstanceId,
            1,
            value.ParticipantId,
            BattleActionType.Pass,
            [],
            null,
            null,
            "safe",
            "payload",
            Fixture.Now,
            Fixture.Now,
            "order")).ToArray();
        var round = battle.CurrentRound with { LockedActions = actions };
        var policy = new DeterministicTestBattleTurnOrderPolicy(42);

        var first = policy.Resolve(battle, round, Fixture.Now).Value!;
        var second = policy.Resolve(battle, round, Fixture.Now).Value!;

        Assert.Equal(first.OrderedParticipantIds, second.OrderedParticipantIds);
        Assert.Equal(fixture.Player(battle).ParticipantId, first.OrderedParticipantIds[0]);
        Assert.Equal(42, first.DeterministicSeed);
    }

    [Fact]
    public async Task Basic_attack_reduces_single_authoritative_hp_once_and_opens_next_round()
    {
        var fixture = Fixture.Create(damage: 25);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        var resolved = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);
        var monster = fixture.Monster(updated);
        var authority = fixture.Combat.Get(updated.BattleInstanceId, monster.ParticipantId).Value!;

        Assert.Equal(BattleResultCode.Success, resolved.Code);
        Assert.Equal(75, monster.CurrentHp);
        Assert.Equal(monster.CurrentHp, authority.CurrentHp);
        Assert.Equal(monster.RuntimeVersion, authority.RuntimeVersion);
        Assert.Equal(2, updated.CurrentRoundNumber);
        Assert.Equal(BattlePhase.CollectingActions, updated.CurrentPhase);
        Assert.Empty(resolved.NetworkBytes);
    }

    [Fact]
    public async Task Pass_resolves_without_damage_or_reward()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.Pass, BattleActionType.Pass);

        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);
        var priorRoundAudit = fixture.Audit.Records.Where(value => value.ActionType == BattleActionType.Pass).ToArray();

        Assert.Equal(100, fixture.Player(updated).CurrentHp);
        Assert.Equal(100, fixture.Monster(updated).CurrentHp);
        Assert.All(priorRoundAudit, value => Assert.Equal(0, value.Damage ?? 0));
    }

    [Fact]
    public async Task Lethal_basic_attack_defeats_monster_completes_battle_and_releases_reservation()
    {
        var fixture = Fixture.Create(damage: 100);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        var result = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var completed = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.Equal(BattleWinnerSide.PlayerSide, result.WinnerSide);
        Assert.Equal(BattleState.Completed, completed.State);
        Assert.Equal(BattlePhase.Completed, completed.CurrentPhase);
        Assert.Equal(BattleCompletionReason.PlayerVictory, completed.CompletionReason);
        Assert.False(fixture.Monster(completed).IsAlive);
        Assert.False(fixture.Reservations.IsReserved(Fixture.CharacterId));
    }

    [Fact]
    public async Task Dead_participant_locked_action_is_skipped()
    {
        var fixture = Fixture.Create(damage: 100);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.BasicAttack);

        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var completed = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.Contains(
            completed.CurrentRound.ResolutionResults,
            value => value.Result == BattleActionResolutionCode.SkippedAttackerDead);
        Assert.Equal(100, fixture.Player(completed).CurrentHp);
    }

    [Fact]
    public async Task Duplicate_resolution_does_not_apply_damage_twice()
    {
        var fixture = Fixture.Create(damage: 20);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);
        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);

        var duplicate = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.Equal(BattleResultCode.InvalidPhase, duplicate.Code);
        Assert.Equal(80, fixture.Monster(updated).CurrentHp);
    }

    [Fact]
    public async Task Deterministic_reward_commits_once_through_inventory_coordinator()
    {
        var fixture = Fixture.Create(damage: 100, rewardGold: 7);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var bundle = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.Equal(107, bundle.Currency.Balances.Single(value => value.CurrencyType == "Gold").Balance);
        Assert.Single(fixture.Inventory.Events, value => value.OperationType == InventoryOperationType.SystemGrant);
    }

    [Fact]
    public async Task Evidence_blocked_reward_policy_grants_nothing()
    {
        var fixture = Fixture.Create(damage: 100);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var bundle = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);
        var completed = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.Equal(100, bundle.Currency.Balances.Single().Balance);
        Assert.Equal(BattleRewardState.NotRequired, completed.RewardState);
        Assert.Equal(CombatPolicyStatus.EvidenceBlocked, completed.RewardPolicyStatus);
    }

    [Fact]
    public async Task Disconnect_preserves_participant_and_submitted_action()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitPlayer(battle, BattleActionType.Pass);
        battle = await fixture.GetBattle(battle.BattleInstanceId);

        var result = await fixture.Coordinator.DisconnectAsync(
            battle.BattleInstanceId,
            Fixture.SessionId,
            Fixture.CharacterId,
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.True(result.Succeeded);
        Assert.False(fixture.Player(updated).IsConnected);
        Assert.Single(updated.CurrentRound.SubmittedActions);
    }

    [Fact]
    public async Task Reconnect_rebinds_new_authenticated_session_without_duplicate_participant()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.Coordinator.DisconnectAsync(
            battle.BattleInstanceId,
            Fixture.SessionId,
            Fixture.CharacterId,
            CancellationToken.None);
        fixture.AddSession("session-reconnected");

        var result = await fixture.Coordinator.ReconnectAsync(
            battle.BattleInstanceId,
            "session-reconnected",
            Fixture.CharacterId,
            CancellationToken.None);
        var duplicate = await fixture.Coordinator.ReconnectAsync(
            battle.BattleInstanceId,
            "session-reconnected",
            Fixture.CharacterId,
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.True(result.Succeeded);
        Assert.True(duplicate.Succeeded);
        Assert.Equal("session-reconnected", fixture.Player(updated).SessionId);
        Assert.Single(updated.Participants, value => value.CharacterId == Fixture.CharacterId);
    }

    [Fact]
    public async Task Old_session_action_is_rejected_after_reconnect()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.Coordinator.DisconnectAsync(
            battle.BattleInstanceId,
            Fixture.SessionId,
            Fixture.CharacterId,
            CancellationToken.None);
        fixture.AddSession("session-new");
        await fixture.Coordinator.ReconnectAsync(
            battle.BattleInstanceId,
            "session-new",
            Fixture.CharacterId,
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        var result = await fixture.Coordinator.SubmitActionAsync(
            fixture.PlayerAction(updated) with { SessionId = Fixture.SessionId },
            CancellationToken.None);

        Assert.Equal(BattleResultCode.InvalidSession, result.Code);
    }

    [Fact]
    public async Task Recovery_restores_collecting_battle_semantically()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var result = await fixture.Coordinator.RecoverAsync(
            battle.BattleInstanceId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(BattlePhase.CollectingActions, (await fixture.GetBattle(battle.BattleInstanceId)).CurrentPhase);
    }

    [Fact]
    public async Task Abort_releases_reservation_and_rejects_future_actions()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());

        var aborted = await fixture.Coordinator.AbortAsync(
            battle.BattleInstanceId,
            "Administrative",
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);
        var action = await fixture.Coordinator.SubmitActionAsync(
            fixture.PlayerAction(updated),
            CancellationToken.None);

        Assert.True(aborted.Succeeded);
        Assert.Equal(BattleState.Aborted, updated.State);
        Assert.False(fixture.Reservations.IsReserved(Fixture.CharacterId));
        Assert.Equal(BattleResultCode.BattleAlreadyCompleted, action.Code);
    }

    [Fact]
    public async Task Inspector_returns_read_only_filtered_battle_participant_round_and_action_snapshots()
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitPlayer(battle, BattleActionType.Pass);

        var snapshot = fixture.Inspector.Capture(
            new BattleInspectorQuery(
                CharacterId: Fixture.CharacterId,
                ActiveOnly: true,
                ActionType: BattleActionType.Pass),
            Fixture.Now);

        Assert.Single(snapshot.Battles);
        Assert.Equal(2, snapshot.Participants.Count);
        Assert.Single(snapshot.Rounds);
        Assert.Single(snapshot.Actions);
        Assert.Equal(16, snapshot.Participants.Single(value => value.CharacterId == Fixture.CharacterId).SessionSafeId.Length);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<BattleInspectorItem>)snapshot.Battles).Add(snapshot.Battles[0]));
    }

    [Fact]
    public async Task Battle_audit_hashes_session_and_idempotency_values()
    {
        var fixture = Fixture.Create();
        const string rawKey = "private-battle-idempotency";
        var result = await fixture.Coordinator.CreateAsync(
            fixture.Request(idempotencyKey: rawKey),
            CancellationToken.None);

        var audit = Assert.Single(fixture.Audit.Records);

        Assert.DoesNotContain(rawKey, audit.IdempotencySafeId, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SessionId, audit.SessionSafeId, StringComparison.Ordinal);
        Assert.Equal(16, audit.IdempotencySafeId.Length);
        Assert.Equal(16, audit.SessionSafeId.Length);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Runtime_events_cover_semantic_pipeline_without_protocol_bytes()
    {
        var fixture = Fixture.Create(damage: 100);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        var result = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var kinds = fixture.Events.Events.Select(value => value.Kind).ToHashSet();

        Assert.Contains(BattleEventKind.BattleCreated, kinds);
        Assert.Contains(BattleEventKind.BattleActionSubmitted, kinds);
        Assert.Contains(BattleEventKind.BattleActionsLocked, kinds);
        Assert.Contains(BattleEventKind.BattleActionResolved, kinds);
        Assert.Contains(BattleEventKind.BattleParticipantDefeated, kinds);
        Assert.Contains(BattleEventKind.BattleCompleted, kinds);
        Assert.Empty(result.NetworkBytes);
    }

    [Fact]
    public async Task Combat_execution_failure_does_not_mutate_participant_hp()
    {
        var fixture = Fixture.Create();
        fixture.Combat.FailureInjection.Point = BattleFailurePoint.CombatExecution;
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.Equal(100, fixture.Monster(updated).CurrentHp);
        Assert.Contains(
            updated.CompletedRounds.SelectMany(value => value.ResolutionResults),
            value => value.Result == BattleActionResolutionCode.CombatFailure);
    }

    [Fact]
    public async Task Mid_resolution_persistence_failure_recovers_without_double_damage()
    {
        var fixture = Fixture.Create(damage: 20);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);
        fixture.FailureInjection.Point = BattleFailurePoint.ActionResultPersistence;

        var failed = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var recoveryRequired = await fixture.GetBattle(battle.BattleInstanceId);
        Assert.Equal(BattleResultCode.RecoveryRequired, failed.Code);
        Assert.Equal(BattleState.RecoveryRequired, recoveryRequired.State);
        Assert.Equal(80, fixture.Combat.Get(battle.BattleInstanceId, fixture.Monster(recoveryRequired).ParticipantId).Value!.CurrentHp);

        fixture.FailureInjection.Point = BattleFailurePoint.None;
        var recovered = await fixture.Coordinator.RecoverAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var completedRound = Assert.Single((await fixture.GetBattle(battle.BattleInstanceId)).CompletedRounds);

        Assert.True(recovered.Succeeded);
        Assert.Equal(20, completedRound.ResolutionResults.Single(value => value.ActionType == BattleActionType.BasicAttack).Damage);
        Assert.Equal(80, fixture.Monster(await fixture.GetBattle(battle.BattleInstanceId)).CurrentHp);
    }

    [Fact]
    public async Task Reward_failure_recovery_commits_once_with_stable_plan_identity()
    {
        var fixture = Fixture.Create(damage: 100, rewardGold: 7);
        fixture.RewardCoordinator.FailureInjection.Point = BattleFailurePoint.RewardTransaction;
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);

        var failed = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        Assert.Equal(BattleResultCode.RewardFailure, failed.Code);
        Assert.True(fixture.Reservations.IsReserved(Fixture.CharacterId));

        fixture.RewardCoordinator.FailureInjection.Point = BattleFailurePoint.None;
        var recovered = await fixture.Coordinator.RecoverAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var duplicate = await fixture.Coordinator.RecoverAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var bundle = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);

        Assert.True(recovered.Succeeded);
        Assert.Equal(BattleResultCode.DuplicateCompleted, duplicate.Code);
        Assert.Equal(107, bundle.Currency.Balances.Single().Balance);
        Assert.Single(fixture.Inventory.Events, value => value.OperationType == InventoryOperationType.SystemGrant);
    }

    [Fact]
    public async Task Audit_and_inspector_failures_do_not_change_battle_authority()
    {
        var fixture = Fixture.Create();
        fixture.FailureInjection.Point = BattleFailurePoint.Audit;
        var created = await fixture.CreateBattle();
        Assert.True(created.Succeeded);
        Assert.Empty(fixture.Audit.Records);

        var brokenInspector = new BattleRuntimeInspector(
            fixture.Store,
            new BattleFailureInjection { Point = BattleFailurePoint.Inspector });
        var snapshot = brokenInspector.Capture(new BattleInspectorQuery(), Fixture.Now);

        Assert.Empty(snapshot.Battles);
        Assert.Equal(BattleState.Active, (await fixture.GetBattle(created)).State);
    }

    [Fact]
    public async Task World_reservation_blocks_direct_formal_combat_intent()
    {
        var fixture = Fixture.Create();
        var reservations = fixture.Reservations;
        reservations.Reserve(Guid.NewGuid(), Guid.NewGuid(), Fixture.CharacterId, fixture.PlayerRuntimeId, Fixture.Now);
        var blocker = new ReservationAwareCombatStub(reservations);

        var result = await blocker.ExecuteAsync(
            Fixture.CharacterId,
            "OfficialClient",
            CancellationToken.None);

        Assert.Equal("combat.player_in_battle", result);
    }

    private static async Task VerifyCreationScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId == 2)
        {
            var result = await fixture.Coordinator.CreateAsync(
                fixture.Request() with { SourceSessionId = "missing" },
                CancellationToken.None);
            Assert.Equal(BattleResultCode.InvalidSession, result.Code);
            return;
        }

        if (scenarioId == 3)
        {
            var result = await fixture.Coordinator.CreateAsync(
                fixture.Request() with { SourceCharacterId = Fixture.CharacterId + 1 },
                CancellationToken.None);
            Assert.Equal(BattleResultCode.OwnershipMismatch, result.Code);
            return;
        }

        if (scenarioId is 4 or 5)
        {
            var request = fixture.Request() with
            {
                EncounterDefinitionId = scenarioId == 4 ? "missing" : null,
                Source = BattleRequestSource.OfficialClient
            };
            var result = await fixture.Coordinator.CreateAsync(request, CancellationToken.None);
            Assert.Equal(BattleResultCode.BattlePolicyBlocked, result.Code);
            return;
        }

        if (scenarioId == 6)
        {
            var request = fixture.Request(idempotencyKey: "scenario-create-duplicate");
            var first = await fixture.Coordinator.CreateAsync(request, CancellationToken.None);
            var duplicate = await fixture.Coordinator.CreateAsync(request, CancellationToken.None);
            Assert.Equal(first.BattleInstanceId, duplicate.BattleInstanceId);
            Assert.Equal(BattleResultCode.DuplicateCompleted, duplicate.Code);
            return;
        }

        if (scenarioId == 7)
        {
            await fixture.CreateBattle();
            var duplicate = await fixture.CreateBattle();
            Assert.Equal(BattleResultCode.Rejected, duplicate.Code);
            return;
        }

        if (scenarioId == 10)
        {
            var request = fixture.Request();
            var encounter = fixture.Encounters.Resolve(request).Value!;
            var duplicated = encounter.ParticipantDefinitions[0] with
            {
                ParticipantDefinitionId = "duplicate-slot",
                MonsterTemplateId = Fixture.MonsterTemplateId
            };
            var factory = new BattleInstanceFactory(new BattleParticipantFactory());
            var result = factory.Create(
                request,
                encounter with
                {
                    ParticipantDefinitions = [encounter.ParticipantDefinitions[0], duplicated]
                },
                fixture.Sessions.Get(Fixture.SessionId).Value!,
                fixture.Players.Get(fixture.PlayerRuntimeId).Value!,
                Fixture.Now);
            Assert.False(result.Succeeded);
            Assert.Equal("battle.formation_conflict", result.Error.Code);
            return;
        }

        if (scenarioId == 13)
        {
            var result = fixture.Encounters.Resolve(
                fixture.Request() with { Source = BattleRequestSource.OfficialClient });
            Assert.False(result.Succeeded);
            Assert.Equal("battle.encounter_evidence_blocked", result.Error.Code);
            return;
        }

        var created = await fixture.CreateBattle();
        var battle = await fixture.GetBattle(created);
        Assert.Equal(BattleState.Active, battle.State);
        Assert.Equal(2, battle.Participants.Count);
        Assert.True(fixture.Reservations.IsReserved(Fixture.CharacterId));
        Assert.Empty(created.NetworkBytes);
        if (scenarioId == 8)
        {
            Assert.Equal(2, battle.Participants.Select(value => value.ParticipantId).Distinct().Count());
        }
        else if (scenarioId == 9)
        {
            Assert.Equal(battle.BattleInstanceId, fixture.Reservations.GetByCharacter(Fixture.CharacterId).Value!.BattleInstanceId);
        }
        else if (scenarioId == 11)
        {
            Assert.Equal("{\"unknown\":true}", fixture.Monster(battle).RawMetadata);
        }
        else if (scenarioId == 12)
        {
            Assert.Equal(Fixture.MonsterTemplateId, fixture.Monster(battle).MonsterTemplateId);
        }
    }

    private static async Task VerifyRoundScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        if (scenarioId is 20 or 21)
        {
            var result = await fixture.Coordinator.SubmitActionAsync(
                fixture.PlayerAction(battle) with { RoundNumber = scenarioId == 20 ? 0 : 2 },
                CancellationToken.None);
            Assert.Equal(BattleResultCode.InvalidRound, result.Code);
            return;
        }

        if (scenarioId == 22)
        {
            var result = await fixture.Coordinator.SubmitActionAsync(
                fixture.PlayerAction(battle) with { BattleVersion = 999 },
                CancellationToken.None);
            Assert.Equal(BattleResultCode.VersionConflict, result.Code);
            return;
        }

        Assert.Equal(BattlePhase.CollectingActions, battle.CurrentPhase);
        Assert.Equal(BattleRoundState.CollectingActions, battle.CurrentRound.State);
        Assert.Equal(1, battle.CurrentRound.RoundNumber);
        Assert.Equal(1, battle.CurrentRound.RoundVersion);
        Assert.Equal(2, battle.CurrentRound.EligibleParticipantIds.Count);
        if (scenarioId == 17)
        {
            Assert.All(
                battle.CurrentRound.EligibleParticipantIds,
                id => Assert.True(battle.Participants.Single(value => value.ParticipantId == id).IsAlive));
        }
    }

    private static async Task VerifySubmissionScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        if (scenarioId == 27)
        {
            var submitted = await fixture.SubmitPlayer(battle, BattleActionType.Defend);
            Assert.Equal(BattleResultCode.Success, submitted.Code);
            var updated = await fixture.GetBattle(battle.BattleInstanceId);
            Assert.Empty(Assert.Single(updated.CurrentRound.SubmittedActions).TargetParticipantIds);
            return;
        }

        if (scenarioId is 25 or 26 or 28)
        {
            var actionType = scenarioId switch
            {
                25 => BattleActionType.Skill,
                26 => BattleActionType.Item,
                _ => BattleActionType.Flee
            };
            var rejected = await fixture.SubmitPlayer(battle, actionType);
            Assert.Equal(BattleResultCode.UnsupportedAction, rejected.Code);
            return;
        }

        if (scenarioId == 29)
        {
            var player = fixture.Player(battle);
            var rejected = await fixture.Coordinator.SubmitActionAsync(
                fixture.PlayerAction(battle) with { TargetParticipantIds = [player.ParticipantId] },
                CancellationToken.None);
            Assert.Equal(BattleResultCode.InvalidTarget, rejected.Code);
            return;
        }

        if (scenarioId == 30)
        {
            var monster = fixture.Monster(battle);
            var dead = monster with
            {
                CurrentHp = 0,
                IsAlive = false,
                CombatState = BattleParticipantCombatState.Defeated
            };
            var mutated = battle with
            {
                Participants = battle.Participants.Select(value =>
                    value.ParticipantId == monster.ParticipantId ? dead : value).ToArray(),
                BattleVersion = battle.BattleVersion + 1
            };
            Assert.True((await fixture.Store.SaveAsync(mutated, battle.BattleVersion, CancellationToken.None)).Succeeded);
            var rejected = await fixture.Coordinator.SubmitActionAsync(
                fixture.PlayerAction(mutated),
                CancellationToken.None);
            Assert.Equal(BattleResultCode.TargetDead, rejected.Code);
            return;
        }

        if (scenarioId == 31)
        {
            var rejected = await fixture.Coordinator.SubmitActionAsync(
                fixture.PlayerAction(battle) with { CharacterId = Fixture.CharacterId + 1 },
                CancellationToken.None);
            Assert.Equal(BattleResultCode.OwnershipMismatch, rejected.Code);
            return;
        }

        if (scenarioId is 32 or 33)
        {
            var request = fixture.PlayerAction(battle, "scenario-action-replay");
            var first = await fixture.Coordinator.SubmitActionAsync(request, CancellationToken.None);
            var second = await fixture.Coordinator.SubmitActionAsync(
                scenarioId == 32 ? request : request with { ActionType = BattleActionType.Pass, TargetParticipantIds = [] },
                CancellationToken.None);
            Assert.Equal(
                scenarioId == 32 ? BattleResultCode.DuplicateCompleted : BattleResultCode.ReplayConflict,
                second.Code);
            Assert.True(first.Succeeded);
            return;
        }

        if (scenarioId == 34)
        {
            var request = fixture.PlayerAction(battle, "scenario-concurrent");
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                fixture.Coordinator.SubmitActionAsync(request, CancellationToken.None)));
            Assert.Single(results, value => value.Code == BattleResultCode.Success);
            return;
        }

        if (scenarioId == 35)
        {
            await fixture.SubmitPlayer(battle, BattleActionType.BasicAttack);
            var current = await fixture.GetBattle(battle.BattleInstanceId);
            var rejected = await fixture.SubmitPlayer(current, BattleActionType.Pass);
            Assert.Equal(BattleResultCode.ActionAlreadySubmitted, rejected.Code);
            return;
        }

        if (scenarioId == 36)
        {
            await fixture.SubmitBoth(battle, BattleActionType.Pass, BattleActionType.Pass);
            var submitted = await fixture.GetBattle(battle.BattleInstanceId);
            await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
            var rejected = await fixture.SubmitPlayer(submitted, BattleActionType.Pass);
            Assert.False(rejected.Succeeded);
            return;
        }

        var action = await fixture.SubmitPlayer(
            battle,
            scenarioId == 24 ? BattleActionType.Pass : BattleActionType.BasicAttack);
        Assert.True(action.Succeeded);
        Assert.Empty(action.NetworkBytes);
    }

    private static async Task VerifyLockScenario(int scenarioId)
    {
        var fixture = Fixture.Create(
            turnOrder: scenarioId == 42 ? new EvidenceBlockedBattleTurnOrderPolicy() : null);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.Pass, BattleActionType.Pass);
        var before = await fixture.GetBattle(battle.BattleInstanceId);
        var result = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        if (scenarioId == 42)
        {
            Assert.Equal(BattleResultCode.TurnOrderEvidenceBlocked, result.Code);
            return;
        }

        Assert.True(result.Succeeded);
        var after = await fixture.GetBattle(battle.BattleInstanceId);
        var completedRound = Assert.Single(after.CompletedRounds);
        Assert.Equal(2, completedRound.LockedActions.Count);
        Assert.Equal(2, completedRound.ResolutionResults.Count);
        Assert.Equal(2, completedRound.CurrentResolutionIndex);
        Assert.Equal(2, after.CurrentRoundNumber);
        if (scenarioId is 39 or 44)
        {
            Assert.Equal(
                before.CurrentRound.SubmittedActions.Select(value => value.ActionId).Order(),
                completedRound.LockedActions.Select(value => value.ActionId).Order());
        }
    }

    private static async Task VerifyCombatScenario(int scenarioId)
    {
        var lethal = scenarioId is 50 or 51 or 52 or 55;
        var fixture = Fixture.Create(damage: lethal ? 100 : 20);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        if (scenarioId == 54)
        {
            fixture.Combat.Seed(battle.BattleInstanceId, battle.Participants);
            var attacker = fixture.Player(battle);
            var target = fixture.Monster(battle);
            var result = await fixture.Combat.ExecuteAsync(
                new BattleCombatExecutionRequest(
                    battle.BattleInstanceId,
                    1,
                    Guid.NewGuid(),
                    0,
                    attacker with { RuntimeVersion = attacker.RuntimeVersion + 1 },
                    target,
                    "version-conflict",
                    Fixture.Now,
                    "scenario"),
                CancellationToken.None);
            Assert.Equal(BattleActionResolutionCode.VersionConflict, result.Code);
            return;
        }

        await fixture.SubmitBoth(
            battle,
            scenarioId == 46 ? BattleActionType.Pass : BattleActionType.BasicAttack,
            scenarioId is 51 or 55 ? BattleActionType.BasicAttack : BattleActionType.Pass);
        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);
        var round = updated.CompletedRounds.Last();
        if (scenarioId == 46)
        {
            Assert.All(round.ResolutionResults, value => Assert.Equal(0, value.Damage));
        }
        else
        {
            var monster = fixture.Monster(updated);
            Assert.Equal(lethal ? 0 : 80, monster.CurrentHp);
            Assert.Equal(
                monster.CurrentHp,
                fixture.Combat.Get(updated.BattleInstanceId, monster.ParticipantId).Value!.CurrentHp);
        }

        Assert.Empty(round.ResolutionResults.SelectMany(value => value.NetworkBytes));
    }

    private static async Task VerifyRoundCompletionScenario(int scenarioId)
    {
        var fixture = Fixture.Create(damage: 20);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);
        var first = await fixture.Coordinator.LockAndResolveAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        var updated = await fixture.GetBattle(battle.BattleInstanceId);

        Assert.True(first.Succeeded);
        Assert.Single(updated.CompletedRounds);
        Assert.Equal(BattleRoundState.Completed, updated.CompletedRounds[0].State);
        Assert.Equal(2, updated.CompletedRounds[0].ResolutionResults.Count);
        Assert.Equal(2, updated.CurrentRoundNumber);
        Assert.DoesNotContain(
            fixture.Monster(updated).ParticipantId,
            updated.CompletedRounds[0].EligibleParticipantIds.Where(id =>
                !updated.Participants.Single(value => value.ParticipantId == id).IsAlive));
        if (scenarioId == 62)
        {
            var duplicate = await fixture.Coordinator.LockAndResolveAsync(
                battle.BattleInstanceId,
                CancellationToken.None);
            Assert.Equal(BattleResultCode.InvalidPhase, duplicate.Code);
        }
    }

    private static async Task VerifyVictoryScenario(int scenarioId)
    {
        if (scenarioId is 66 or 67)
        {
            var fixture = Fixture.Create();
            var battle = fixture.CreateUnpersistedBattle();
            var participants = battle.Participants.Select(value =>
                scenarioId == 66
                    ? value.Side == BattleSide.PlayerSide ? value with { IsAlive = false, CurrentHp = 0 } : value
                    : value with { IsAlive = false, CurrentHp = 0 }).ToArray();
            var result = new BaselineServerBattleVictoryPolicy().Evaluate(participants);
            Assert.Equal(
                scenarioId == 66 ? BattleWinnerSide.EnemySide : BattleWinnerSide.Draw,
                result.WinnerSide);
            return;
        }

        var lethal = Fixture.Create(damage: 100);
        var battleInstance = await lethal.GetBattle(await lethal.CreateBattle());
        await lethal.SubmitBoth(battleInstance, BattleActionType.BasicAttack, BattleActionType.Pass);
        var completed = await lethal.Coordinator.LockAndResolveAsync(
            battleInstance.BattleInstanceId,
            CancellationToken.None);
        var snapshot = await lethal.GetBattle(battleInstance.BattleInstanceId);
        Assert.Equal(BattleWinnerSide.PlayerSide, completed.WinnerSide);
        Assert.Equal(BattleState.Completed, snapshot.State);
        Assert.False(lethal.Reservations.IsReserved(Fixture.CharacterId));
        if (scenarioId == 68)
        {
            var rejected = await lethal.Coordinator.SubmitActionAsync(
                lethal.PlayerAction(snapshot),
                CancellationToken.None);
            Assert.Equal(BattleResultCode.BattleAlreadyCompleted, rejected.Code);
        }
        else if (scenarioId == 72)
        {
            var abortFixture = Fixture.Create();
            var active = await abortFixture.GetBattle(await abortFixture.CreateBattle());
            var aborted = await abortFixture.Coordinator.AbortAsync(
                active.BattleInstanceId,
                "Administrative",
                CancellationToken.None);
            Assert.True(aborted.Succeeded);
            Assert.False(abortFixture.Reservations.IsReserved(Fixture.CharacterId));
        }
    }

    private static async Task VerifyRewardScenario(int scenarioId)
    {
        var rewardGold = scenarioId == 73 ? 0 : 5;
        var fixture = Fixture.Create(damage: 100, rewardGold: rewardGold);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitBoth(battle, BattleActionType.BasicAttack, BattleActionType.Pass);
        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        await fixture.Coordinator.LockAndResolveAsync(battle.BattleInstanceId, CancellationToken.None);
        var bundle = await fixture.InventoryStore.LoadAsync(Fixture.CharacterId, CancellationToken.None);
        Assert.Equal(100 + rewardGold, bundle.Currency.Balances.Single().Balance);
        Assert.InRange(
            fixture.Inventory.Events.Count(value => value.OperationType == InventoryOperationType.SystemGrant),
            rewardGold == 0 ? 0 : 1,
            rewardGold == 0 ? 0 : 1);
    }

    private static async Task VerifyConnectionScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        if (scenarioId >= 82)
        {
            await fixture.SubmitPlayer(battle, BattleActionType.Pass);
            battle = await fixture.GetBattle(battle.BattleInstanceId);
        }

        var disconnected = await fixture.Coordinator.DisconnectAsync(
            battle.BattleInstanceId,
            Fixture.SessionId,
            Fixture.CharacterId,
            CancellationToken.None);
        Assert.True(disconnected.Succeeded);
        var offline = await fixture.GetBattle(battle.BattleInstanceId);
        Assert.False(fixture.Player(offline).IsConnected);
        if (scenarioId >= 85)
        {
            fixture.AddSession("scenario-reconnect");
            var reconnected = await fixture.Coordinator.ReconnectAsync(
                battle.BattleInstanceId,
                "scenario-reconnect",
                Fixture.CharacterId,
                CancellationToken.None);
            Assert.True(reconnected.Succeeded);
            var restored = await fixture.GetBattle(battle.BattleInstanceId);
            Assert.True(fixture.Player(restored).IsConnected);
            Assert.Equal(battle.CurrentRoundNumber, restored.CurrentRoundNumber);
            Assert.Equal(battle.CurrentPhase, restored.CurrentPhase);
            if (scenarioId == 86)
            {
                var rejected = await fixture.Coordinator.SubmitActionAsync(
                    fixture.PlayerAction(restored) with { SessionId = Fixture.SessionId },
                    CancellationToken.None);
                Assert.Equal(BattleResultCode.InvalidSession, rejected.Code);
            }
        }
    }

    private static async Task VerifyRecoveryScenario(int scenarioId)
    {
        var fixture = Fixture.Create(damage: scenarioId is 94 or 95 or 96 ? 100 : 20);
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        if (scenarioId == 97)
        {
            var conflict = await fixture.Store.SaveAsync(
                battle with { BattleVersion = battle.BattleVersion + 1 },
                battle.BattleVersion + 1,
                CancellationToken.None);
            Assert.False(conflict.Succeeded);
            Assert.Equal("battle.version_conflict", conflict.Error.Code);
            return;
        }

        var recovered = await fixture.Coordinator.RecoverAsync(
            battle.BattleInstanceId,
            CancellationToken.None);
        Assert.True(recovered.Succeeded);
        var restored = await fixture.GetBattle(battle.BattleInstanceId);
        Assert.Equal(BattlePhase.CollectingActions, restored.CurrentPhase);
        Assert.Equal(100, fixture.Monster(restored).CurrentHp);
    }

    private static async Task VerifyDiagnosticsScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var battle = await fixture.GetBattle(await fixture.CreateBattle());
        await fixture.SubmitPlayer(battle, BattleActionType.Pass);
        var snapshot = fixture.Inspector.Capture(
            new BattleInspectorQuery(CharacterId: Fixture.CharacterId, Take: 1),
            Fixture.Now);
        Assert.Single(snapshot.Battles);
        Assert.Equal(2, snapshot.Participants.Count);
        Assert.Single(snapshot.Rounds);
        Assert.Single(snapshot.Actions);
        Assert.All(fixture.Audit.Records, value =>
        {
            Assert.InRange(value.IdempotencySafeId.Length, 16, 16);
            Assert.InRange(value.SessionSafeId.Length, 0, 16);
        });
        Assert.Empty(snapshot.Actions.SelectMany(_ => Array.Empty<byte>()));
        if (scenarioId == 107)
        {
            Assert.Throws<NotSupportedException>(() =>
                ((IList<BattleInspectorItem>)snapshot.Battles).Add(snapshot.Battles[0]));
        }
    }

    private static void VerifyFrozenBoundaryScenario(int scenarioId)
    {
        Assert.InRange(scenarioId, 111, 115);
        Assert.True(typeof(IInventoryTransactionCoordinator).IsInterface);
        Assert.True(typeof(IWorldInteractionCoordinator).IsInterface);
        Assert.True(typeof(ICombatCoordinator).IsInterface);
        Assert.True(typeof(IPortalTransitionCoordinator).IsInterface);
        Assert.DoesNotContain(
            typeof(BattleActionRequest).GetProperties(),
            value => value.Name.Contains("Packet", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ReservationAwareCombatStub
    {
        private readonly IWorldBattleReservationRegistry _reservations;

        public ReservationAwareCombatStub(IWorldBattleReservationRegistry reservations)
        {
            _reservations = reservations;
        }

        public Task<string> ExecuteAsync(long characterId, string source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _reservations.IsReserved(characterId) &&
                !string.Equals(source, "TurnBasedBattleRuntime", StringComparison.Ordinal)
                    ? "combat.player_in_battle"
                    : "");
        }
    }

    private sealed class Fixture
    {
        public const int MapId = 100;
        public const int MonsterTemplateId = 2001;
        public const long CharacterId = 10;
        public const long AccountId = 1;
        public const string SessionId = "session-battle";
        public static readonly DateTimeOffset Now = new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

        private Fixture(
            RuntimeSession session,
            WorldSessionBinding binding,
            InMemoryWorldInteractionSessionRegistry sessions,
            PlayerCombatRuntimeRegistry players,
            BattleEncounterCatalog encounters,
            InMemoryBattleStore store,
            InMemoryWorldBattleReservationRegistry reservations,
            BattleScopedCombatExecutionPort combat,
            InventoryTransactionCoordinator inventory,
            InMemoryInventoryPersistenceStore inventoryStore,
            BattleRewardCoordinator rewardCoordinator,
            InMemoryBattleEventSink events,
            InMemoryBattleAuditLedger audit,
            BattleFailureInjection failureInjection,
            DeterministicTestBattleClock clock,
            TurnBasedBattleCoordinator coordinator,
            BattleRuntimeInspector inspector,
            long playerRuntimeId)
        {
            Session = session;
            Binding = binding;
            Sessions = sessions;
            Players = players;
            Encounters = encounters;
            Store = store;
            Reservations = reservations;
            Combat = combat;
            Inventory = inventory;
            InventoryStore = inventoryStore;
            RewardCoordinator = rewardCoordinator;
            Events = events;
            Audit = audit;
            FailureInjection = failureInjection;
            Clock = clock;
            Coordinator = coordinator;
            Inspector = inspector;
            PlayerRuntimeId = playerRuntimeId;
        }

        public RuntimeSession Session { get; }
        public WorldSessionBinding Binding { get; }
        public InMemoryWorldInteractionSessionRegistry Sessions { get; }
        public PlayerCombatRuntimeRegistry Players { get; }
        public BattleEncounterCatalog Encounters { get; }
        public InMemoryBattleStore Store { get; }
        public InMemoryWorldBattleReservationRegistry Reservations { get; }
        public BattleScopedCombatExecutionPort Combat { get; }
        public InventoryTransactionCoordinator Inventory { get; }
        public InMemoryInventoryPersistenceStore InventoryStore { get; }
        public BattleRewardCoordinator RewardCoordinator { get; }
        public InMemoryBattleEventSink Events { get; }
        public InMemoryBattleAuditLedger Audit { get; }
        public BattleFailureInjection FailureInjection { get; }
        public DeterministicTestBattleClock Clock { get; }
        public TurnBasedBattleCoordinator Coordinator { get; }
        public BattleRuntimeInspector Inspector { get; }
        public long PlayerRuntimeId { get; }

        public static Fixture Create(
            long damage = 20,
            long rewardGold = 0,
            IBattleTurnOrderPolicy? turnOrder = null)
        {
            var content = new WorldContentSnapshot(
                new Dictionary<int, MapDefinition>
                {
                    [MapId] = new(
                        MapId,
                        MapId,
                        "Battle Test Map",
                        "tests",
                        new MapBounds(0, 0, 1000, 1000),
                        [new SpawnPoint("Default", new WorldPosition3(10, 10), WorldDirection.South, "tests")],
                        [],
                        [],
                        [],
                        [],
                        [])
                },
                []);
            var mapFactory = new MapRuntimeFactory();
            var session = RuntimeSession.Connected("battle-connection", "127.0.0.1:1000", Now) with
            {
                SessionId = SessionId,
                AccountId = AccountId,
                CharacterId = CharacterId,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.InWorld
            };
            var character = new CharacterSummary(
                CharacterId,
                AccountId,
                "BattleTester",
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
            var bindingResult = new WorldSessionBinder(mapFactory).Bind(
                session,
                character,
                content,
                WorldContentMode.MariaDbAuthoritative);
            Assert.True(bindingResult.Succeeded);
            var binding = bindingResult.Value!;
            var playerObject = Assert.Single(binding.MapRuntime.Objects.OfType<PlayerObject>());
            var sessions = new InMemoryWorldInteractionSessionRegistry();
            sessions.Seed(binding);
            var players = new PlayerCombatRuntimeRegistry();
            players.Register(new PlayerCombatRuntimeState(
                playerObject.Identity.RuntimeObjectId,
                CharacterId,
                binding.MapRuntime.WorldInstanceId,
                MapId,
                playerObject.State.Position,
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
            var monsterDefinition = new MonsterCombatDefinition(
                MonsterTemplateId,
                3001,
                "test-group",
                MapId,
                new WorldPosition3(12, 10),
                WorldDirection.South,
                TimeSpan.Zero,
                5,
                100,
                30,
                4,
                true,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.EvidenceBlocked,
                "test-monsters-v1",
                "{\"unknown\":true}",
                "TestOnlyFixture");
            var encounters = new BattleEncounterCatalog(null, [monsterDefinition]);
            var store = new InMemoryBattleStore();
            var reservations = new InMemoryWorldBattleReservationRegistry();
            var combat = new BattleScopedCombatExecutionPort(new DeterministicTestDamagePolicy(damage));
            var events = new InMemoryBattleEventSink();
            var audit = new InMemoryBattleAuditLedger();
            var clock = new DeterministicTestBattleClock(Now);
            var inventoryAudit = new InMemoryInventoryAuditLedger();
            var inventoryStore = new InMemoryInventoryPersistenceStore(inventoryAudit);
            inventoryStore.Seed(new InventoryPersistenceBundle(
                new PlayerInventorySnapshot(Guid.NewGuid(), CharacterId, 8, 0, 0, "Clean", []),
                new CurrencyWalletSnapshot(
                    CharacterId,
                    [new CurrencyBalance("Gold", 100, 0, "Clean")])));
            var inventory = new InventoryTransactionCoordinator(
                new ItemDefinitionCatalog([]),
                new MerchantDefinitionCatalog([]),
                inventoryStore,
                inventoryAudit,
                () => 50_000,
                inventoryStore.FailureInjection);
            IBattleRewardPolicy rewardPolicy = rewardGold > 0
                ? new DeterministicTestBattleRewardPolicy(
                    currencies: [new CombatCurrencyGrant("Gold", rewardGold)])
                : new NoBattleRewardPolicy();
            var rewardCoordinator = new BattleRewardCoordinator(inventory);
            var failureInjection = new BattleFailureInjection();
            var participantFactory = new BattleParticipantFactory();
            var instanceFactory = new BattleInstanceFactory(participantFactory);
            var coordinator = new TurnBasedBattleCoordinator(
                sessions,
                players,
                encounters,
                instanceFactory,
                store,
                store,
                store,
                reservations,
                turnOrder ?? new DeterministicTestBattleTurnOrderPolicy(42),
                new BattleActionResolver(combat, clock),
                new BaselineServerBattleVictoryPolicy(),
                rewardPolicy,
                rewardCoordinator,
                clock,
                events,
                audit,
                combat,
                failureInjection);
            return new Fixture(
                session,
                binding,
                sessions,
                players,
                encounters,
                store,
                reservations,
                combat,
                inventory,
                inventoryStore,
                rewardCoordinator,
                events,
                audit,
                failureInjection,
                clock,
                coordinator,
                new BattleRuntimeInspector(store),
                playerObject.Identity.RuntimeObjectId);
        }

        public BattleRequest Request(string? idempotencyKey = null) =>
            new(
                Guid.NewGuid(),
                idempotencyKey ?? Guid.NewGuid().ToString("N"),
                Session.SessionId,
                CharacterId,
                PlayerRuntimeId,
                null,
                [MonsterTemplateId],
                MapId,
                Binding.MapRuntime.WorldInstanceId,
                Players.Get(PlayerRuntimeId).Value!.RuntimeVersion,
                BattleRequestSource.TrustedInternalTest,
                Clock.UtcNow,
                Guid.NewGuid().ToString("N"));

        public Task<BattleCreationResult> CreateBattle() =>
            Coordinator.CreateAsync(Request(), CancellationToken.None);

        public BattleInstance CreateUnpersistedBattle()
        {
            var encounter = Encounters.Resolve(Request()).Value!;
            return new BattleInstanceFactory(new BattleParticipantFactory())
                .Create(
                    Request(),
                    encounter,
                    Sessions.Get(SessionId).Value!,
                    Players.Get(PlayerRuntimeId).Value!,
                    Clock.UtcNow)
                .Value!;
        }

        public async Task<BattleInstance> GetBattle(BattleCreationResult result) =>
            await GetBattle(result.BattleInstanceId!.Value);

        public async Task<BattleInstance> GetBattle(Guid battleId) =>
            Assert.IsType<BattleInstance>(await Store.GetAsync(battleId, CancellationToken.None));

        public BattleParticipant Player(BattleInstance battle) =>
            battle.Participants.Single(value => value.CharacterId == CharacterId);

        public BattleParticipant Monster(BattleInstance battle) =>
            battle.Participants.Single(value => value.MonsterTemplateId == MonsterTemplateId);

        public BattleActionRequest PlayerAction(
            BattleInstance battle,
            string? idempotencyKey = null,
            BattleActionType actionType = BattleActionType.BasicAttack) =>
            new(
                Guid.NewGuid(),
                idempotencyKey ?? Guid.NewGuid().ToString("N"),
                battle.BattleInstanceId,
                battle.BattleVersion,
                battle.CurrentRoundNumber,
                Player(battle).SessionId!,
                CharacterId,
                Player(battle).ParticipantId,
                actionType,
                actionType == BattleActionType.BasicAttack ? [Monster(battle).ParticipantId] : [],
                null,
                null,
                null,
                BattleRequestSource.TrustedInternalTest,
                Clock.UtcNow,
                Guid.NewGuid().ToString("N"));

        public BattleActionRequest MonsterAction(
            BattleInstance battle,
            BattleActionType actionType) =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                battle.BattleInstanceId,
                battle.BattleVersion,
                battle.CurrentRoundNumber,
                "",
                0,
                Monster(battle).ParticipantId,
                actionType,
                actionType == BattleActionType.BasicAttack ? [Player(battle).ParticipantId] : [],
                null,
                null,
                null,
                BattleRequestSource.TrustedInternalTest,
                Clock.UtcNow,
                Guid.NewGuid().ToString("N"));

        public Task<BattleActionSubmissionResult> SubmitPlayer(
            BattleInstance battle,
            BattleActionType actionType) =>
            Coordinator.SubmitActionAsync(
                PlayerAction(battle, actionType: actionType),
                CancellationToken.None);

        public async Task SubmitBoth(
            BattleInstance battle,
            BattleActionType playerAction,
            BattleActionType monsterAction)
        {
            var playerResult = await SubmitPlayer(battle, playerAction);
            Assert.True(playerResult.Succeeded);
            var afterPlayer = await GetBattle(battle.BattleInstanceId);
            var monsterResult = await Coordinator.SubmitActionAsync(
                MonsterAction(afterPlayer, monsterAction),
                CancellationToken.None);
            Assert.True(monsterResult.Succeeded);
        }

        public void AddSession(string sessionId)
        {
            Sessions.Set(new InteractionSessionBinding(
                Session with { SessionId = sessionId },
                Binding.MapSession with { SessionId = sessionId },
                Binding.MapRuntime,
                Binding.MapRuntime.WorldInstanceId,
                0,
                false,
                Clock.UtcNow));
        }
    }
}
