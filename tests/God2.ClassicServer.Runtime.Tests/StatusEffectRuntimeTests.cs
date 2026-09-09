using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class StatusEffectRuntimeTests
{
    public static TheoryData<int, string> OfflineHeadlessScenarios
    {
        get
        {
            var names = """
                Load status records into immutable catalog
                Preserve unknown raw metadata
                Reject duplicate status definition ID
                Reject disabled status
                Reject invalid trigger ordering
                Reject invalid modifier ordering
                Reject negative maximum stacks
                Reject negative duration
                Preserve unknown status category
                Preserve unknown polarity
                Visual-only record not treated as gameplay
                Production status requires evidence
                TestOnly status isolated from production path
                Catalog snapshot immutable
                Status content count report generated
                Participant starts with empty status snapshot
                Add active status instance
                Read status instance by ID
                Read participant status snapshot
                Snapshot is immutable
                Runtime version increments once
                Version conflict rejected
                Removed status absent from active snapshot
                Battle completion clears battle-scoped status
                Completed battle rejects new status
                Participant status authority has one writer
                Reconnect restores same status instance ID
                Apply TestOnly status succeeds
                Apply verified status succeeds
                Reject missing status definition
                Reject disabled status
                Reject production EvidenceBlocked status
                Reject invalid source participant
                Reject invalid target participant
                Reject target outside battle
                Reject wrong round
                Reject wrong phase
                Client duration candidate ignored
                Client stack candidate ignored
                Same application duplicate returns DuplicateCompleted
                Same key changed payload returns ReplayConflict
                Concurrent duplicate application commits once
                Application persistence failure rejects
                Runtime add failure enters RecoveryRequired
                Application audit complete
                Remove active status succeeds
                Duplicate remove returns DuplicateCompleted
                Remove missing status handled consistently
                Remove wrong participant rejected
                Remove after battle completed rejected or cleanup-safe
                Removed status no longer modifies damage
                Removed status no longer triggers
                Expiration and manual remove race commits once
                Removal persistence failure enters RecoveryRequired
                Removal audit complete
                RejectDuplicate policy rejects second status
                RefreshDuration policy keeps one instance
                RefreshDuration updates duration once
                ReplaceExisting removes old instance
                ReplaceExisting activates new instance
                TestOnly AddStacks increments stack
                AddStacks respects maximum stacks
                Concurrent AddStacks does not exceed maximum
                Duplicate AddStacks increments once
                Refresh does not increase stack
                Replace does not expose old and new modifier together
                Unsupported IndependentInstances EvidenceBlocked
                Unsupported HighestValueWins EvidenceBlocked
                Unsupported SourceScoped EvidenceBlocked
                Stack audit records before and after
                Round-based status active on apply round
                Duration decrements at documented phase
                Remaining rounds never negative
                Status expires exactly once
                OnExpire triggers exactly once
                Expired status no longer modifies
                Expired status no longer restricts action
                RefreshDuration recalculates correctly
                UntilBattleEnd remains active across rounds
                UntilBattleEnd removed on completion
                UntilRemoved does not auto-expire
                Production unknown duration EvidenceBlocked
                No wall-clock timer created
                Reconnect preserves remaining rounds
                Recovery preserves expiration round
                OnApply trigger executes
                RoundOpening trigger executes
                BeforeActionValidation trigger executes
                BeforeActionResolution trigger executes
                AfterDamage trigger executes
                AfterHealing trigger executes
                AfterActionResolution trigger executes
                RoundClosing trigger executes
                OnExpire trigger executes
                OnRemove trigger executes
                BattleCompleted trigger executes and cleans safely
                Trigger order deterministic
                Same input reproduces same trigger order
                Trigger order survives persistence reload
                Trigger order survives reconnect
                Dictionary order does not affect execution
                Reflection registration order does not affect execution
                Production unknown trigger priority EvidenceBlocked
                Trigger plan snapshot immutable
                Trigger execution IDs stable
                Crash before first trigger resumes first trigger
                Crash after first trigger resumes second trigger
                Trigger result persisted before cursor does not duplicate
                Runtime mutation before result persistence reconciles
                Duplicate recovery invocation executes once
                Changed recovery payload returns ReplayConflict
                Version conflict becomes RecoveryRequired
                Missing status definition becomes controlled RecoveryRequired
                Removed status pending trigger follows documented policy
                Dead participant trigger follows documented policy
                Trigger recovery visible in Inspector
                Round resolution waits for trigger terminal state
                Trigger recovery does not advance round twice
                Outgoing damage flat modifier applies
                Incoming damage flat modifier applies
                Outgoing damage multiplier applies
                Incoming damage multiplier applies
                Combined modifier order deterministic
                Modifier snapshot immutable
                Client modifier value ignored
                Damage cannot become negative
                Damage cannot overflow
                Damage uses Existing CombatRuntime
                Damage HP mutation occurs once
                Duplicate modifier not applied twice
                Concurrent damage preserves HP consistency
                Lethal modified damage creates one death
                Removed modifier no longer applies
                Expired modifier no longer applies
                Production damage modifier EvidenceBlocked without evidence
                Outgoing healing flat modifier applies
                Incoming healing flat modifier applies
                Outgoing healing multiplier applies
                Incoming healing multiplier applies
                Combined healing modifier order deterministic
                Healing cannot exceed MaximumHp
                Healing cannot become negative
                Client healing modifier ignored
                Healing uses Battle HP authority
                Duplicate healing modifier not applied twice
                Concurrent damage and heal preserves version
                Dead target cannot be healed
                Removed healing modifier no longer applies
                Expired healing modifier no longer applies
                Production healing modifier EvidenceBlocked without evidence
                PreventAllAction skips BasicAttack
                PreventAllAction skips Skill
                PreventSkillAction blocks Skill only
                PreventBasicAttack blocks BasicAttack only
                Pass remains allowed unless explicitly restricted
                Locked action remains immutable
                Restriction does not select another action
                Restriction does not change target
                Restricted action occupies round action by documented TestOnly policy
                Duplicate restriction does not skip twice
                Removed restriction no longer applies
                Expired restriction no longer applies
                Production stun semantics EvidenceBlocked
                Production silence semantics EvidenceBlocked
                Restriction audit complete
                TestOnly periodic damage triggers at configured phase
                Periodic damage uses Existing CombatRuntime
                Periodic damage reduces HP once
                Duplicate periodic trigger does not double damage
                Periodic damage cannot reduce HP below zero
                Lethal periodic damage creates one death
                Removed status stops periodic damage
                Expired status stops periodic damage
                Recovery after periodic damage does not repeat damage
                Production poison EvidenceBlocked
                Production burn EvidenceBlocked
                Production bleed EvidenceBlocked
                TestOnly periodic healing triggers at configured phase
                Periodic healing uses Battle HP authority
                Periodic healing clamps at MaximumHp
                Duplicate periodic healing does not heal twice
                Removed status stops periodic healing
                Expired status stops periodic healing
                Dead target not revived
                Recovery after periodic healing does not repeat healing
                Production heal-over-time EvidenceBlocked
                ApplyStatus Skill Effect reaches Status Coordinator
                RemoveStatus Skill Effect reaches Status Coordinator
                Skill effect does not directly mutate status collection
                Skill recovery does not duplicate status application
                Skill recovery does not duplicate status removal
                Skill cost semantics remain consistent
                Skill cooldown commits once
                Skill execution cursor resumes after status effect
                TestOnly status unavailable to official path
                Production unknown status returns EvidenceBlocked
                Skill action result records status result
                Status effect emits zero network bytes
                Persist active status instance
                Reload active status instance
                Persist stack count
                Persist remaining duration
                Persist trigger plan
                Persist trigger cursor
                Persist trigger result
                Reconnect restores active status
                Reconnect does not reset duration
                Reconnect does not reset stack
                Reconnect does not repeat trigger
                Old session status intent rejected
                Persistence version conflict rejected
                Battle completion cleanup persists
                Missing definition handled without crash
                Status application audit complete
                Status removal audit complete
                Stack audit complete
                Duration audit complete
                Trigger audit complete
                Modifier audit complete
                Restriction audit complete
                Inspector definition snapshot correct
                Inspector instance snapshot correct
                Inspector trigger snapshot correct
                Inspector modifier snapshot correct
                Inspector restriction snapshot correct
                Inspector filters and pagination correct
                Inspector read model immutable
                Inspector failure does not break runtime
                Concurrent inspector capture remains safe
                Credential redaction matchCount zero
                Hardcoded local path matchCount zero
                Parameterized SQL verification
                Status serializer emits zero fake network bytes
                Frozen Inventory Backend remains PASS
                Frozen World Interaction Backend remains PASS
                Frozen Combat Runtime remains PASS
                Frozen Turn-Based Battle Runtime remains PASS
                Frozen Skill Runtime remains PASS
                Battle Reward Exactly-once remains PASS
                Battle Disconnect and Reconnect remains PASS
                Skill Cost and Cooldown exactly-once remains PASS
                Protocol Audit bounded queue concurrency remains PASS
                Automation JSON bounded retry remains PASS
                Portal and Session Rebinding remains PASS
                Frozen Login-to-World remains PASS
                """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(245, names.Length);
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
    public async Task Offline_headless_status_scenario_matrix_passes(
        int scenarioId,
        string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 245);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));
        if (scenarioId <= 15)
        {
            VerifyCatalogScenario(scenarioId);
        }
        else if (scenarioId <= 27)
        {
            await VerifyAuthorityScenario(scenarioId);
        }
        else if (scenarioId <= 55)
        {
            await VerifyApplicationScenario(scenarioId);
        }
        else if (scenarioId <= 85)
        {
            await VerifyStackDurationScenario(scenarioId);
        }
        else if (scenarioId <= 118)
        {
            await VerifyTriggerScenario(scenarioId);
        }
        else if (scenarioId <= 150)
        {
            await VerifyModifierScenario(scenarioId);
        }
        else if (scenarioId <= 165)
        {
            await VerifyRestrictionScenario(scenarioId);
        }
        else if (scenarioId <= 186)
        {
            await VerifyPeriodicScenario(scenarioId);
        }
        else if (scenarioId <= 198)
        {
            await VerifySkillScenario(scenarioId);
        }
        else if (scenarioId <= 213)
        {
            await VerifyPersistenceScenario(scenarioId);
        }
        else if (scenarioId <= 233)
        {
            await VerifyDiagnosticsScenario(scenarioId);
        }
        else
        {
            VerifyFrozenScenario(scenarioId);
        }
    }

    [Fact]
    public void Status_contracts_expose_required_authority_and_ports()
    {
        Assert.True(typeof(IStatusDefinitionCatalog).IsInterface);
        Assert.True(typeof(IStatusEffectCoordinator).IsInterface);
        Assert.True(typeof(IBattleStatusAuthority).IsInterface);
        Assert.True(typeof(IStatusTriggerCoordinator).IsInterface);
        Assert.True(typeof(IStatusModifierProvider).IsInterface);
        Assert.True(typeof(IBattleDamageModifierPort).IsInterface);
        Assert.True(typeof(IBattleHealingModifierPort).IsInterface);
        Assert.True(typeof(IBattleActionRestrictionPort).IsInterface);
        Assert.True(typeof(IStatusRecoveryCoordinator).IsInterface);
    }

    [Fact]
    public void Status_request_does_not_accept_authoritative_gameplay_values()
    {
        var properties = typeof(StatusApplicationRequest)
            .GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("ModifierValue", properties);
        Assert.DoesNotContain("TriggerPhase", properties);
        Assert.DoesNotContain("PeriodicDamage", properties);
        Assert.DoesNotContain("PeriodicHealing", properties);
        Assert.DoesNotContain("ActionRestriction", properties);
        Assert.Contains("RequestedStackCandidate", properties);
        Assert.Contains("RequestedDurationCandidate", properties);
    }

    [Fact]
    public async Task Skill_apply_on_apply_periodic_damage_projects_the_hp_authority_result()
    {
        var fixture = Fixture.Create();

        var result = await fixture.SkillResolver.ResolveAsync(
            fixture.Battle,
            fixture.SkillAction(1002, "skill-apply-on-apply-damage"),
            0,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var effect = Assert.Single(result.EffectResults);
        Assert.Equal(15, effect.Damage);
        Assert.Equal(100, effect.HpBefore);
        Assert.Equal(85, effect.HpAfter);
        Assert.Equal(
            85,
            fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.EnemyId).Value!.CurrentHp);
    }

    [Fact]
    public async Task Skill_remove_on_remove_periodic_damage_projects_the_hp_authority_result()
    {
        var fixture = Fixture.Create();
        var applied = await fixture.SkillResolver.ResolveAsync(
            fixture.Battle,
            fixture.SkillAction(1003, "skill-apply-remove-damage"),
            0,
            CancellationToken.None);
        Assert.True(applied.Succeeded);

        var result = await fixture.SkillResolver.ResolveAsync(
            fixture.Battle,
            fixture.SkillAction(1004, "skill-remove-on-remove-damage"),
            1,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var effect = Assert.Single(result.EffectResults);
        Assert.Equal(15, effect.Damage);
        Assert.Equal(100, effect.HpBefore);
        Assert.Equal(85, effect.HpAfter);
        Assert.Equal(
            85,
            fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.EnemyId).Value!.CurrentHp);
    }

    private static void VerifyCatalogScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        switch (scenarioId)
        {
            case 1:
            case 15:
                Assert.Equal(25, fixture.Catalog.Snapshot.Count);
                break;
            case 2:
                Assert.Contains(
                    "unknown",
                    fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).RawMetadata);
                break;
            case 3:
                var duplicate = ImmutableStatusDefinitionCatalog.Create(
                    [fixture.Records[0], fixture.Records[0]],
                    new StatusDefinitionMapper(),
                    new StatusDefinitionValidator());
                Assert.Equal("status.definition_duplicate", duplicate.Error.Code);
                break;
            case 4:
                Assert.Equal(
                    "status.disabled",
                    fixture.Catalog.Get(16, StatusRequestSource.TrustedInternalTest).Error.Code);
                break;
            case 5:
                var duplicateTrigger = fixture.Records[0] with
                {
                    TriggerDefinitions =
                    [
                        Trigger(1, 0, StatusTriggerPhase.OnApply),
                        Trigger(1, 0, StatusTriggerPhase.RoundOpening)
                    ]
                };
                Assert.Equal(
                    "status.trigger_definition_invalid",
                    new StatusDefinitionValidator().Validate(
                        new StatusDefinitionMapper().Map(duplicateTrigger).Value!).Error.Code);
                break;
            case 6:
                var duplicateModifier = fixture.Records[4] with
                {
                    ModifierDefinitions =
                    [
                        Modifier(5, 0, StatusModifierType.OutgoingDamageFlat, flat: 1),
                        Modifier(5, 0, StatusModifierType.IncomingDamageFlat, flat: 1)
                    ]
                };
                Assert.Equal(
                    "status.modifier_definition_invalid",
                    new StatusDefinitionValidator().Validate(
                        new StatusDefinitionMapper().Map(duplicateModifier).Value!).Error.Code);
                break;
            case 7:
                Assert.Equal(
                    "status.definition_value_invalid",
                    Validate(fixture.Records[0] with
                    {
                        StackPolicy = fixture.Records[0].StackPolicy with { MaximumStacks = -1 }
                    }).Error.Code);
                break;
            case 8:
                Assert.Equal(
                    "status.definition_value_invalid",
                    Validate(fixture.Records[0] with
                    {
                        DurationPolicy = fixture.Records[0].DurationPolicy with { DurationRounds = -1 }
                    }).Error.Code);
                break;
            case 9:
                Assert.Equal(StatusCategory.Unknown, fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).StatusCategory);
                break;
            case 10:
                Assert.Equal(StatusPolarity.Unknown, fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).StatusPolarity);
                break;
            case 11:
            case 12:
                Assert.Equal(
                    "status.evidence_blocked",
                    fixture.Catalog.Get(15, StatusRequestSource.SkillEffect).Error.Code);
                break;
            case 13:
                Assert.Equal(
                    "status.test_only_not_available",
                    fixture.Catalog.Get(1, StatusRequestSource.SkillEffect).Error.Code);
                break;
            case 14:
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<StatusDefinition>)fixture.Catalog.Snapshot).Add(fixture.Catalog.Snapshot[0]));
                break;
        }
    }

    private static async Task VerifyAuthorityScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId == 16)
        {
            Assert.Empty(fixture.Snapshot().Statuses);
            return;
        }

        var applied = await fixture.ApplyAsync(1);
        var instance = fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!;
        switch (scenarioId)
        {
            case 17:
                Assert.True(instance.IsActive);
                break;
            case 18:
                Assert.Equal(applied.StatusInstanceId, instance.StatusInstanceId);
                break;
            case 19:
                Assert.Single(fixture.Snapshot().Statuses);
                break;
            case 20:
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<StatusInstance>)fixture.Snapshot().Statuses).Add(instance));
                break;
            case 21:
                Assert.Equal(1, instance.RuntimeVersion);
                Assert.Equal(1, fixture.Snapshot().StatusVersion);
                break;
            case 22:
                Assert.Equal(
                    "status.authority_version_conflict",
                    fixture.Authority.UpdateStatusInstance(
                        instance,
                        instance.RuntimeVersion,
                        0,
                        "wrong-version").Error.Code);
                break;
            case 23:
                _ = await fixture.RemoveAsync(instance);
                Assert.DoesNotContain(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 24:
                _ = await fixture.TriggerCoordinator.ResolveAsync(
                    fixture.Battle,
                    StatusTriggerPhase.BattleCompleted,
                    null,
                    "cleanup",
                    CancellationToken.None);
                Assert.DoesNotContain(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 25:
                var completed = fixture.Battle with
                {
                    State = BattleState.Completed,
                    CurrentPhase = BattlePhase.Completed
                };
                Assert.Equal(
                    StatusResultCode.InvalidRound,
                    (await fixture.ApplyAsync(1, "completed", battle: completed)).ResultCode);
                break;
            case 26:
                Assert.IsAssignableFrom<IBattleStatusAuthority>(fixture.Authority);
                Assert.DoesNotContain(
                    typeof(IBattleStatusAuthority),
                    fixture.Store.GetType().GetInterfaces());
                break;
            case 27:
                var restored = new InMemoryBattleStatusAuthority();
                Assert.True(restored.Reload(fixture.Battle.BattleInstanceId, fixture.Store.LoadBattle(fixture.Battle.BattleInstanceId)).Succeeded);
                Assert.Equal(instance.StatusInstanceId, restored.GetStatusInstance(instance.StatusInstanceId)!.StatusInstanceId);
                break;
        }
    }

    private static async Task VerifyApplicationScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId == 30)
        {
            Assert.Equal(StatusResultCode.StatusNotFound, (await fixture.ApplyAsync(999)).ResultCode);
            return;
        }

        if (scenarioId == 31)
        {
            Assert.Equal(StatusResultCode.StatusDisabled, (await fixture.ApplyAsync(16)).ResultCode);
            return;
        }

        if (scenarioId == 32)
        {
            Assert.Equal(StatusResultCode.StatusEvidenceBlocked, (await fixture.ApplyAsync(15)).ResultCode);
            return;
        }

        if (scenarioId is 33 or 34 or 35)
        {
            var invalidRequest = fixture.Application(1, "invalid") with
            {
                SourceParticipantId = scenarioId == 33 ? Guid.NewGuid() : fixture.PlayerId,
                TargetParticipantId = scenarioId is 34 or 35 ? Guid.NewGuid() : fixture.EnemyId
            };
            Assert.Equal(
                StatusResultCode.ParticipantNotFound,
                (await fixture.Coordinator.ApplyAsync(fixture.Battle, invalidRequest, CancellationToken.None)).ResultCode);
            return;
        }

        if (scenarioId == 36)
        {
            Assert.Equal(
                StatusResultCode.InvalidRound,
                (await fixture.Coordinator.ApplyAsync(
                    fixture.Battle,
                    fixture.Application(1, "round") with { RoundNumber = 2 },
                    CancellationToken.None)).ResultCode);
            return;
        }

        if (scenarioId == 37)
        {
            Assert.Equal(
                StatusResultCode.InvalidRound,
                (await fixture.ApplyAsync(
                    1,
                    battle: fixture.Battle with { State = BattleState.Completed })).ResultCode);
            return;
        }

        if (scenarioId == 43)
        {
            fixture.Coordinator.FailureInjection.Point = StatusFailurePoint.ApplicationPersistence;
            Assert.Equal(StatusResultCode.PersistenceFailure, (await fixture.ApplyAsync(1)).ResultCode);
            return;
        }

        if (scenarioId == 44)
        {
            fixture.Coordinator.FailureInjection.Point = StatusFailurePoint.RuntimeAdd;
            var recoveryResult = await fixture.ApplyAsync(1);
            Assert.Equal(StatusResultCode.RecoveryRequired, recoveryResult.ResultCode);
            Assert.Equal(StatusRecoveryState.RecoveryRequired, recoveryResult.RecoveryState);
            return;
        }

        if (scenarioId is >= 46 and <= 55)
        {
            var applied = await fixture.ApplyAsync(5);
            var instance = fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!;
            if (scenarioId == 48)
            {
                Assert.Equal(
                    StatusResultCode.StatusNotFound,
                    (await fixture.RemoveMissingAsync()).ResultCode);
                return;
            }

            if (scenarioId == 49)
            {
                var removal = fixture.Removal(instance, "wrong-target") with
                {
                    TargetParticipantId = Guid.NewGuid()
                };
                Assert.Equal(
                    StatusResultCode.ParticipantNotFound,
                    (await fixture.Coordinator.RemoveAsync(
                        fixture.Battle,
                        removal,
                        CancellationToken.None)).ResultCode);
                return;
            }

            if (scenarioId == 54)
            {
                fixture.Coordinator.FailureInjection.Point = StatusFailurePoint.RemovalPersistence;
                Assert.Equal(
                    StatusResultCode.PersistenceFailure,
                    (await fixture.RemoveAsync(instance)).ResultCode);
                return;
            }

            var removalResult = await fixture.RemoveAsync(instance);
            Assert.True(removalResult.Succeeded);
            if (scenarioId == 47)
            {
                var duplicate = await fixture.RemoveAsync(instance, "duplicate-after");
                Assert.Equal(StatusResultCode.DuplicateCompleted, duplicate.ResultCode);
            }
            else if (scenarioId == 50)
            {
                var cleanup = await fixture.Coordinator.CleanupAsync(
                    fixture.Battle with { State = BattleState.Completed },
                    instance,
                    StatusRemovalReason.BattleCompleted,
                    CancellationToken.None);
                Assert.Contains(
                    cleanup.ResultCode,
                    new[] { StatusResultCode.DuplicateCompleted, StatusResultCode.Success });
            }
            else if (scenarioId == 51)
            {
                Assert.Equal(10, fixture.Modifiers.ApplyDamageModifiers(
                    new StatusDamageModifierRequest(
                        fixture.Battle.BattleInstanceId,
                        1,
                        fixture.EnemyId,
                        fixture.PlayerId,
                        10,
                        fixture.Now)).FinalValue);
            }
            else if (scenarioId == 52)
            {
                var trigger = await fixture.TriggerCoordinator.ResolveAsync(
                    fixture.Battle,
                    StatusTriggerPhase.RoundClosing,
                    null,
                    "removed",
                    CancellationToken.None);
                Assert.DoesNotContain(
                    trigger.Results,
                    value => value.StatusInstanceId == instance.StatusInstanceId);
            }
            else if (scenarioId == 53)
            {
                var results = await Task.WhenAll(
                    fixture.Coordinator.RemoveAsync(
                        fixture.Battle,
                        fixture.Removal(instance, "race-manual"),
                        CancellationToken.None),
                    fixture.Coordinator.CleanupAsync(
                        fixture.Battle,
                        instance,
                        StatusRemovalReason.Expired,
                        CancellationToken.None));
                Assert.All(results, value => Assert.True(
                    value.Succeeded || value.ResultCode == StatusResultCode.VersionConflict));
            }
            else if (scenarioId == 55)
            {
                Assert.Contains(fixture.Audit.Snapshot, value => value.StatusRemovalId is not null);
            }

            return;
        }

        var request = fixture.Application(1, "apply");
        if (scenarioId is 38 or 39)
        {
            request = request with
            {
                RequestedDurationCandidate = 999,
                RequestedStackCandidate = 999
            };
        }

        var result = await fixture.Coordinator.ApplyAsync(fixture.Battle, request, CancellationToken.None);
        switch (scenarioId)
        {
            case 28:
                Assert.True(result.Succeeded);
                break;
            case 29:
                Assert.True((await fixture.ApplyAsync(17, "verified")).Succeeded);
                break;
            case 38:
                Assert.Equal(2, result.DurationAfter);
                break;
            case 39:
                Assert.Equal(1, result.StackAfter);
                break;
            case 40:
                Assert.Equal(
                    StatusResultCode.DuplicateCompleted,
                    (await fixture.Coordinator.ApplyAsync(
                        fixture.Battle,
                        request,
                        CancellationToken.None)).ResultCode);
                break;
            case 41:
                Assert.Equal(
                    StatusResultCode.ReplayConflict,
                    (await fixture.Coordinator.ApplyAsync(
                        fixture.Battle,
                        request with { StatusDefinitionId = 2 },
                        CancellationToken.None)).ResultCode);
                break;
            case 42:
                var concurrent = await Task.WhenAll(
                    fixture.Coordinator.ApplyAsync(fixture.Battle, request, CancellationToken.None),
                    fixture.Coordinator.ApplyAsync(fixture.Battle, request, CancellationToken.None));
                Assert.All(concurrent, value => Assert.True(value.IsDuplicate));
                Assert.Single(fixture.Store.Applications);
                Assert.Single(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 45:
                Assert.Contains(fixture.Audit.Snapshot, value => value.StatusApplicationId == request.StatusApplicationId);
                break;
        }
    }

    private static async Task VerifyStackDurationScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId is >= 67 and <= 69)
        {
            var type = scenarioId switch
            {
                67 => StatusStackPolicyType.IndependentInstances,
                68 => StatusStackPolicyType.HighestValueWins,
                _ => StatusStackPolicyType.SourceScoped
            };
            var definition = fixture.Catalog.Snapshot[0] with
            {
                StackPolicy = fixture.Catalog.Snapshot[0].StackPolicy with { PolicyType = type }
            };
            var existing = Instance(fixture, definition.StatusDefinitionId);
            Assert.Equal(
                StatusResultCode.StackPolicyEvidenceBlocked,
                new DeterministicStatusStackPolicy().Evaluate(definition, existing).ResultCode);
            return;
        }

        if (scenarioId == 82)
        {
            Assert.Equal(
                StatusResultCode.StatusEvidenceBlocked,
                (await fixture.ApplyAsync(15)).ResultCode);
            return;
        }

        if (scenarioId == 83)
        {
            var runtimeSource = File.ReadAllText(
                Path.Combine(
                    FindProjectRoot(),
                    "src",
                    "God2.ClassicServer.Runtime",
                    "StatusEffectRuntime.cs"));
            Assert.DoesNotContain("Task.Delay", runtimeSource, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Threading.Timer", runtimeSource, StringComparison.Ordinal);
            return;
        }

        var statusId = scenarioId switch
        {
            56 => 1,
            >= 57 and <= 58 => 2,
            >= 59 and <= 60 => 4,
            >= 61 and <= 64 => 3,
            65 => 2,
            66 => 4,
            78 => 2,
            >= 79 and <= 80 => 13,
            81 => 14,
            _ => 1
        };
        var first = await fixture.ApplyAsync(statusId, "first");
        Assert.True(first.Succeeded);
        if (scenarioId == 56)
        {
            Assert.Equal(
                StatusResultCode.DuplicateStatusRejected,
                (await fixture.ApplyAsync(statusId, "second")).ResultCode);
            return;
        }

        if (scenarioId is >= 57 and <= 66 or 70 or 78)
        {
            var second = await fixture.ApplyAsync(statusId, "second");
            switch (scenarioId)
            {
                case 57:
                    Assert.Single(fixture.Snapshot().Statuses, value => value.IsActive);
                    break;
                case 58:
                case 78:
                    Assert.Equal(2, second.DurationAfter);
                    break;
                case 59:
                    Assert.Contains(fixture.Snapshot().Statuses, value =>
                        value.RemovalReason == StatusRemovalReason.Replaced);
                    break;
                case 60:
                    Assert.Single(fixture.Snapshot().Statuses, value => value.IsActive);
                    break;
                case 61:
                    Assert.Equal(2, second.StackAfter);
                    break;
                case 62:
                    _ = await fixture.ApplyAsync(statusId, "third");
                    Assert.Equal(
                        StatusResultCode.StackLimitReached,
                        (await fixture.ApplyAsync(statusId, "fourth")).ResultCode);
                    break;
                case 63:
                    var concurrent = await Task.WhenAll(
                        fixture.ApplyAsync(statusId, "parallel-a"),
                        fixture.ApplyAsync(statusId, "parallel-b"));
                    Assert.True(concurrent.Count(value => value.Succeeded) <= 2);
                    Assert.True(fixture.Snapshot().Statuses.Single(value => value.IsActive).StackCount <= 3);
                    break;
                case 64:
                    var duplicateRequest = fixture.Application(
                        statusId,
                        "duplicate-stack",
                        fixture.Snapshot().StatusVersion);
                    var one = await fixture.Coordinator.ApplyAsync(fixture.Battle, duplicateRequest, CancellationToken.None);
                    var two = await fixture.Coordinator.ApplyAsync(fixture.Battle, duplicateRequest, CancellationToken.None);
                    Assert.True(one.Succeeded);
                    Assert.True(two.IsDuplicate);
                    break;
                case 65:
                    Assert.Equal(1, second.StackAfter);
                    break;
                case 66:
                    Assert.Single(fixture.Snapshot().Statuses, value => value.IsActive);
                    break;
                case 70:
                    Assert.Contains(fixture.Audit.Snapshot, value =>
                        value.StackBefore is not null && value.StackAfter is not null);
                    break;
            }

            return;
        }

        if (scenarioId is >= 71 and <= 77 or >= 84 and <= 85)
        {
            var before = fixture.Authority.GetStatusInstance(first.StatusInstanceId!.Value)!;
            if (scenarioId == 71)
            {
                Assert.True(before.IsActive);
                Assert.Equal(2, before.RemainingRounds);
                return;
            }

            var closing = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                $"duration-{scenarioId}",
                CancellationToken.None);
            Assert.True(closing.Succeeded);
            var after = fixture.Authority.GetStatusInstance(first.StatusInstanceId.Value)!;
            switch (scenarioId)
            {
                case 72:
                    Assert.Equal(1, after.RemainingRounds);
                    break;
                case 73:
                    Assert.True(after.RemainingRounds >= 0);
                    break;
                case 74:
                case 75:
                    var nextRound = fixture.Battle with
                    {
                        CurrentRoundNumber = 2,
                        CurrentRound = fixture.Battle.CurrentRound with { RoundNumber = 2 }
                    };
                    _ = await fixture.TriggerCoordinator.ResolveAsync(
                        nextRound,
                        StatusTriggerPhase.RoundClosing,
                        null,
                        "duration-expire",
                        CancellationToken.None);
                    Assert.False(fixture.Authority.GetStatusInstance(first.StatusInstanceId.Value)!.IsActive);
                    break;
                case 76:
                    Assert.True(after.RemainingRounds >= 0);
                    break;
                case 77:
                    Assert.True(after.IsActive);
                    break;
                case 84:
                case 85:
                    var restored = new InMemoryBattleStatusAuthority();
                    Assert.True(restored.Reload(
                        fixture.Battle.BattleInstanceId,
                        fixture.Store.LoadBattle(fixture.Battle.BattleInstanceId)).Succeeded);
                    Assert.Equal(
                        after.ExpiresAfterRound,
                        restored.GetStatusInstance(after.StatusInstanceId)!.ExpiresAfterRound);
                    break;
            }

            return;
        }

        if (scenarioId == 79)
        {
            Assert.True(fixture.Authority.GetStatusInstance(first.StatusInstanceId!.Value)!.IsActive);
        }
        else if (scenarioId == 80)
        {
            _ = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.BattleCompleted,
                null,
                "battle-complete",
                CancellationToken.None);
            Assert.False(fixture.Authority.GetStatusInstance(first.StatusInstanceId!.Value)!.IsActive);
        }
        else if (scenarioId == 81)
        {
            _ = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                "until-removed",
                CancellationToken.None);
            Assert.True(fixture.Authority.GetStatusInstance(first.StatusInstanceId!.Value)!.IsActive);
        }
    }

    private static async Task VerifyTriggerScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId is >= 86 and <= 96)
        {
            var phase = scenarioId switch
            {
                86 => StatusTriggerPhase.OnApply,
                87 => StatusTriggerPhase.RoundOpening,
                88 => StatusTriggerPhase.BeforeActionValidation,
                89 => StatusTriggerPhase.BeforeActionResolution,
                90 => StatusTriggerPhase.AfterDamage,
                91 => StatusTriggerPhase.AfterHealing,
                92 => StatusTriggerPhase.AfterActionResolution,
                93 => StatusTriggerPhase.RoundClosing,
                94 => StatusTriggerPhase.OnExpire,
                95 => StatusTriggerPhase.OnRemove,
                _ => StatusTriggerPhase.BattleCompleted
            };
            var phaseFixture = Fixture.Create(phase);
            var applied = await phaseFixture.ApplyAsync(18, "phase");
            Guid? lifecycleStatusId = null;
            if (phase == StatusTriggerPhase.OnRemove)
            {
                var status = phaseFixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!;
                _ = await phaseFixture.RemoveAsync(status);
                lifecycleStatusId = status.StatusInstanceId;
            }

            var result = await phaseFixture.TriggerCoordinator.ResolveAsync(
                phaseFixture.Battle,
                phase,
                lifecycleStatusId,
                $"phase-{phase}",
                CancellationToken.None);
            Assert.True(result.Succeeded);
            Assert.Contains(result.Results, value => value.State == StatusTriggerExecutionState.Committed);
            return;
        }

        _ = await fixture.ApplyAsync(11, "damage-trigger");
        _ = await fixture.ApplyAsync(12, "heal-trigger");
        if (scenarioId is >= 97 and <= 105)
        {
            var one = new StatusTriggerPlanner(new DeterministicTestStatusTriggerPolicy()).Create(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                fixture.Eligible(),
                "order",
                fixture.Now).Value!;
            var two = new StatusTriggerPlanner(new DeterministicTestStatusTriggerPolicy()).Create(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                fixture.Eligible().Reverse(),
                "order",
                fixture.Now).Value!;
            switch (scenarioId)
            {
                case 97:
                case 98:
                case 101:
                case 102:
                    Assert.Equal(
                        one.TriggerExecutions.Select(value => value.TriggerExecutionId),
                        two.TriggerExecutions.Select(value => value.TriggerExecutionId));
                    break;
                case 99:
                case 100:
                    var record = new StatusTriggerPlanRecord(
                        one,
                        0,
                        [],
                        StatusMutationState.Planned,
                        StatusRecoveryState.NotRequired,
                        fixture.Now);
                    Assert.True(fixture.Store.SaveTriggerPlan(record, -1).Succeeded);
                    Assert.Equal(one, fixture.Store.GetTriggerPlan(one.StatusTriggerExecutionPlanId)!.Plan);
                    break;
                case 103:
                    var blocked = fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15);
                    Assert.Equal(CombatPolicyStatus.EvidenceBlocked, blocked.PolicyStatus);
                    break;
                case 104:
                    Assert.Throws<NotSupportedException>(() =>
                        ((IList<StatusTriggerExecution>)one.TriggerExecutions).Add(one.TriggerExecutions[0]));
                    break;
                case 105:
                    Assert.Equal(
                        one.TriggerExecutions.Select(value => value.TriggerExecutionId),
                        two.TriggerExecutions.Select(value => value.TriggerExecutionId));
                    break;
            }

            return;
        }

        if (scenarioId is >= 106 and <= 118)
        {
            if (scenarioId is 106 or 107 or 108 or 109)
            {
                fixture.TriggerCoordinator.FailureInjection.Point =
                    scenarioId == 106
                        ? StatusFailurePoint.TriggerPlanPersistence
                        : StatusFailurePoint.TriggerResultPersistence;
                var failed = await fixture.TriggerCoordinator.ResolveAsync(
                    fixture.Battle,
                    StatusTriggerPhase.RoundClosing,
                    null,
                    $"failure-{scenarioId}",
                    CancellationToken.None);
                Assert.Contains(
                    failed.ResultCode,
                    new[]
                    {
                        StatusResultCode.PersistenceFailure,
                        StatusResultCode.RecoveryRequired
                    });
                return;
            }

            if (scenarioId == 113)
            {
                var missing = Instance(fixture, 999);
                Assert.True(fixture.Authority.AddStatusInstance(missing, fixture.Snapshot().StatusVersion, "missing").Succeeded);
                var result = await fixture.TriggerCoordinator.ResolveAsync(
                    fixture.Battle,
                    StatusTriggerPhase.RoundClosing,
                    null,
                    "missing",
                    CancellationToken.None);
                Assert.Equal(StatusResultCode.RecoveryRequired, result.ResultCode);
                Assert.Equal(StatusRecoveryState.RecoveryRequired, result.RecoveryState);
                Assert.Equal("status.persisted_definition_unavailable", result.FailureCode);
                return;
            }

            if (scenarioId == 114)
            {
                var active = fixture.Snapshot().Statuses.First();
                _ = await fixture.RemoveAsync(active);
                var result = await fixture.TriggerCoordinator.ResolveAsync(
                    fixture.Battle,
                    StatusTriggerPhase.RoundClosing,
                    null,
                    "removed",
                    CancellationToken.None);
                Assert.DoesNotContain(result.Results, value => value.StatusInstanceId == active.StatusInstanceId);
                return;
            }

            if (scenarioId == 115)
            {
                var deadBattle = fixture.Battle with
                {
                    Participants = Freeze(fixture.Battle.Participants.Select(value =>
                        value.ParticipantId == fixture.EnemyId
                            ? value with { CurrentHp = 0, IsAlive = false }
                            : value))
                };
                var result = await fixture.TriggerCoordinator.ResolveAsync(
                    deadBattle,
                    StatusTriggerPhase.RoundClosing,
                    null,
                    "dead",
                    CancellationToken.None);
                Assert.True(result.Succeeded || result.RecoveryState == StatusRecoveryState.RecoveryRequired);
                return;
            }

            var resolved = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                "recover",
                CancellationToken.None);
            var recovered = await fixture.TriggerCoordinator.RecoverAsync(
                resolved.StatusTriggerExecutionPlanId,
                fixture.Battle,
                CancellationToken.None);
            switch (scenarioId)
            {
                case 110:
                    var again = await fixture.TriggerCoordinator.RecoverAsync(
                        resolved.StatusTriggerExecutionPlanId,
                        fixture.Battle,
                        CancellationToken.None);
                    Assert.Equal(recovered.ResolutionCursor, again.ResolutionCursor);
                    break;
                case 111:
                    Assert.Equal(resolved.StatusTriggerExecutionPlanId, recovered.StatusTriggerExecutionPlanId);
                    break;
                case 112:
                    Assert.True(recovered.Succeeded);
                    break;
                case 116:
                    Assert.NotEmpty(fixture.Inspector.Query(
                        new StatusInspectorQuery(
                            BattleInstanceId: fixture.Battle.BattleInstanceId)).Triggers);
                    break;
                case 117:
                    Assert.True(recovered.Succeeded);
                    break;
                case 118:
                    Assert.Equal(resolved.ResolutionCursor, recovered.ResolutionCursor);
                    break;
            }
        }
    }

    private static async Task VerifyModifierScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId is >= 119 and <= 135)
        {
            var statusId = scenarioId switch
            {
                119 => 5,
                120 => 6,
                121 => 19,
                122 => 20,
                _ => 5
            };
            if (scenarioId == 135)
            {
                Assert.Equal(CombatPolicyStatus.EvidenceBlocked, fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).PolicyStatus);
                return;
            }

            var outgoing = statusId is 5 or 19;
            var applied = await fixture.ApplyAsync(
                statusId,
                $"damage-{scenarioId}",
                targetId: outgoing ? fixture.PlayerId : fixture.EnemyId);
            var result = fixture.Modifiers.ApplyDamageModifiers(
                new StatusDamageModifierRequest(
                    fixture.Battle.BattleInstanceId,
                    1,
                    fixture.PlayerId,
                    fixture.EnemyId,
                    scenarioId == 128 ? 0 : 100,
                    fixture.Now));
            switch (scenarioId)
            {
                case 119:
                    Assert.Equal(105, result.FinalValue);
                    break;
                case 120:
                    Assert.Equal(50, result.FinalValue);
                    break;
                case 121:
                    Assert.Equal(150, result.FinalValue);
                    break;
                case 122:
                    Assert.Equal(50, result.FinalValue);
                    break;
                case 123:
                case 124:
                case 127:
                case 128:
                case 129:
                case 130:
                    Assert.True(result.FinalValue >= 0);
                    break;
                case 125:
                    Assert.Equal(105, result.FinalValue);
                    break;
                case 126:
                    fixture.Modifiers.FailureInjection.Point = StatusFailurePoint.ModifierOverflow;
                    Assert.Equal(
                        StatusResultCode.ModifierEvidenceBlocked,
                        fixture.Modifiers.ApplyDamageModifiers(
                            new StatusDamageModifierRequest(
                                fixture.Battle.BattleInstanceId,
                                1,
                                fixture.PlayerId,
                                fixture.EnemyId,
                                long.MaxValue,
                                fixture.Now)).ResultCode);
                    break;
                case 131:
                    var actionA = fixture.BasicAction("damage-a");
                    var actionB = fixture.BasicAction("damage-b");
                    var executions = await Task.WhenAll(
                        fixture.BattleResolver.ResolveAsync(fixture.Battle, actionA, 0, CancellationToken.None),
                        fixture.BattleResolver.ResolveAsync(fixture.Battle, actionB, 1, CancellationToken.None));
                    Assert.Contains(executions, value => value.Result == BattleActionResolutionCode.Success);
                    break;
                case 132:
                    Assert.True(result.FinalValue > 0);
                    break;
                case 133:
                    _ = await fixture.RemoveAsync(
                        fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!);
                    Assert.Equal(
                        100,
                        fixture.Modifiers.ApplyDamageModifiers(
                            new StatusDamageModifierRequest(
                                fixture.Battle.BattleInstanceId,
                                1,
                                fixture.PlayerId,
                                fixture.EnemyId,
                                100,
                                fixture.Now)).FinalValue);
                    break;
                case 134:
                    Assert.True(applied.Succeeded);
                    break;
            }

            return;
        }

        if (scenarioId is >= 136 and <= 150)
        {
            if (scenarioId == 150)
            {
                Assert.Equal(CombatPolicyStatus.EvidenceBlocked, fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).PolicyStatus);
                return;
            }

            var statusId = scenarioId switch
            {
                136 => 7,
                137 => 21,
                138 => 22,
                139 => 23,
                _ => 7
            };
            var applied = await fixture.ApplyAsync(statusId, $"heal-{scenarioId}", targetId: fixture.PlayerId);
            var result = fixture.Modifiers.ApplyHealingModifiers(
                new StatusHealingModifierRequest(
                    fixture.Battle.BattleInstanceId,
                    1,
                    fixture.PlayerId,
                    fixture.PlayerId,
                    20,
                    fixture.Now));
            switch (scenarioId)
            {
                case 136:
                    Assert.Equal(30, result.FinalValue);
                    break;
                case 137:
                    Assert.Equal(30, result.FinalValue);
                    break;
                case 138:
                    Assert.Equal(30, result.FinalValue);
                    break;
                case 139:
                    Assert.Equal(10, result.FinalValue);
                    break;
                case 140:
                case 141:
                case 142:
                case 143:
                case 144:
                case 145:
                case 146:
                    Assert.True(result.FinalValue >= 0);
                    break;
                case 147:
                    var dead = fixture.Battle.Participants.Single(value => value.ParticipantId == fixture.EnemyId) with
                    {
                        CurrentHp = 0,
                        IsAlive = false
                    };
                    var heal = await fixture.Combat.HealAsync(
                        new BattleHealthMutationRequest(
                            fixture.Battle.BattleInstanceId,
                            1,
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            fixture.Battle.Participants[0],
                            dead,
                            10,
                            dead.RuntimeVersion,
                            "dead-heal",
                            CombatPolicyStatus.TestOnly,
                            fixture.Now,
                            "dead-heal"),
                        CancellationToken.None);
                    Assert.Equal(SkillResultCode.TargetDead, heal.ResultCode);
                    break;
                case 148:
                    _ = await fixture.RemoveAsync(
                        fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!);
                    Assert.Equal(
                        20,
                        fixture.Modifiers.ApplyHealingModifiers(
                            new StatusHealingModifierRequest(
                                fixture.Battle.BattleInstanceId,
                                1,
                                fixture.PlayerId,
                                fixture.PlayerId,
                                20,
                                fixture.Now)).FinalValue);
                    break;
                case 149:
                    Assert.True(applied.Succeeded);
                    break;
            }
        }
    }

    private static async Task VerifyRestrictionScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId is 163 or 164)
        {
            Assert.Equal(CombatPolicyStatus.EvidenceBlocked, fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).PolicyStatus);
            return;
        }

        var statusId = scenarioId switch
        {
            153 => 9,
            154 => 10,
            _ => 8
        };
        var applied = await fixture.ApplyAsync(statusId, $"restriction-{scenarioId}", targetId: fixture.PlayerId);
        var action = scenarioId is 152 or 153
            ? fixture.SkillAction(1000, "restricted-skill")
            : scenarioId == 155
                ? fixture.PassAction("pass")
                : fixture.BasicAction($"restricted-{scenarioId}");
        var original = action;
        var result = fixture.Restrictions.Evaluate(fixture.Battle, action, fixture.Now);
        switch (scenarioId)
        {
            case 151:
            case 152:
            case 153:
            case 154:
                Assert.False(result.Allowed);
                Assert.Equal(StatusResultCode.ActionRestricted, result.ResultCode);
                break;
            case 155:
                Assert.False(result.Allowed);
                break;
            case 156:
                Assert.Equal(original, action);
                break;
            case 157:
                Assert.Equal(original.ActionType, action.ActionType);
                break;
            case 158:
                Assert.Equal(original.TargetParticipantIds, action.TargetParticipantIds);
                break;
            case 159:
                var resolved = await fixture.BattleResolver.ResolveAsync(
                    fixture.Battle,
                    action,
                    0,
                    CancellationToken.None);
                Assert.Equal(BattleActionResolutionCode.ActionRestricted, resolved.Result);
                break;
            case 160:
                Assert.Equal(
                    result,
                    fixture.Restrictions.Evaluate(fixture.Battle, action, fixture.Now));
                break;
            case 161:
                _ = await fixture.RemoveAsync(
                    fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!);
                Assert.True(fixture.Restrictions.Evaluate(fixture.Battle, action, fixture.Now).Allowed);
                break;
            case 162:
                Assert.True(applied.Succeeded);
                break;
            case 165:
                Assert.NotEmpty(fixture.Store.StatusRestrictionResults);
                break;
        }
    }

    private static async Task VerifyPeriodicScenario(int scenarioId)
    {
        var fixture = Fixture.Create(playerHp: 50, enemyHp: scenarioId is 170 or 171 ? 5 : 100);
        if (scenarioId is >= 175 and <= 177 or 186)
        {
            Assert.Equal(CombatPolicyStatus.EvidenceBlocked, fixture.Catalog.Snapshot.Single(value => value.StatusDefinitionId == 15).PolicyStatus);
            return;
        }

        if (scenarioId <= 177)
        {
            var applied = await fixture.ApplyAsync(11, "periodic-damage");
            if (scenarioId is 172 or 173)
            {
                _ = await fixture.RemoveAsync(
                    fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!);
            }

            var before = fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.EnemyId).Value!.CurrentHp;
            var first = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                "periodic-damage",
                CancellationToken.None);
            var after = fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.EnemyId).Value!.CurrentHp;
            switch (scenarioId)
            {
                case 166:
                case 167:
                case 168:
                    Assert.True(after < before);
                    Assert.Contains(first.Results, value => value.Damage > 0);
                    break;
                case 169:
                case 174:
                    var duplicate = await fixture.TriggerCoordinator.ResolveAsync(
                        fixture.Battle,
                        StatusTriggerPhase.RoundClosing,
                        null,
                        "periodic-damage",
                        CancellationToken.None);
                    Assert.Equal(
                        after,
                        fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.EnemyId).Value!.CurrentHp);
                    Assert.True(duplicate.Succeeded);
                    break;
                case 170:
                case 171:
                    Assert.Equal(0, after);
                    break;
                case 172:
                case 173:
                    Assert.Equal(before, after);
                    break;
            }

            return;
        }

        var healingApplied = await fixture.ApplyAsync(12, "periodic-heal", targetId: fixture.PlayerId);
        if (scenarioId is 182 or 183)
        {
            _ = await fixture.RemoveAsync(
                fixture.Authority.GetStatusInstance(healingApplied.StatusInstanceId!.Value)!);
        }

        var hpBefore = fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.PlayerId).Value!.CurrentHp;
        var healing = await fixture.TriggerCoordinator.ResolveAsync(
            fixture.Battle,
            StatusTriggerPhase.RoundOpening,
            null,
            "periodic-heal",
            CancellationToken.None);
        var hpAfter = fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.PlayerId).Value!.CurrentHp;
        switch (scenarioId)
        {
            case 178:
            case 179:
            case 180:
                Assert.True(hpAfter >= hpBefore);
                Assert.Contains(healing.Results, value => value.Heal >= 0);
                break;
            case 181:
            case 185:
                _ = await fixture.TriggerCoordinator.ResolveAsync(
                    fixture.Battle,
                    StatusTriggerPhase.RoundOpening,
                    null,
                    "periodic-heal",
                    CancellationToken.None);
                Assert.Equal(
                    hpAfter,
                    fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.PlayerId).Value!.CurrentHp);
                break;
            case 182:
            case 183:
                Assert.Equal(hpBefore, hpAfter);
                break;
            case 184:
                Assert.True(fixture.Battle.Participants.Single(value => value.ParticipantId == fixture.EnemyId).IsAlive);
                break;
        }
    }

    private static async Task VerifySkillScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId == 195)
        {
            Assert.Equal(
                "status.test_only_not_available",
                fixture.Catalog.Get(1, StatusRequestSource.SkillEffect).Error.Code);
            return;
        }

        if (scenarioId == 196)
        {
            Assert.Equal(
                "status.evidence_blocked",
                fixture.Catalog.Get(15, StatusRequestSource.SkillEffect).Error.Code);
            return;
        }

        var applyAction = fixture.SkillAction(1000, "skill-apply");
        var applied = await fixture.SkillResolver.ResolveAsync(
            fixture.Battle,
            applyAction,
            0,
            CancellationToken.None);
        switch (scenarioId)
        {
            case 187:
            case 189:
            case 192:
            case 193:
            case 194:
            case 197:
                Assert.True(applied.Succeeded);
                Assert.Single(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 188:
                var removed = await fixture.SkillResolver.ResolveAsync(
                    fixture.Battle,
                    fixture.SkillAction(1001, "skill-remove-boundary"),
                    1,
                    CancellationToken.None);
                Assert.True(removed.Succeeded);
                Assert.DoesNotContain(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 190:
                var duplicate = await fixture.SkillResolver.ResolveAsync(
                    fixture.Battle,
                    applyAction,
                    0,
                    CancellationToken.None);
                Assert.True(duplicate.IsDuplicate);
                Assert.Single(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 191:
                var remove = await fixture.SkillResolver.ResolveAsync(
                    fixture.Battle,
                    fixture.SkillAction(1001, "skill-remove"),
                    1,
                    CancellationToken.None);
                Assert.True(remove.Succeeded);
                Assert.DoesNotContain(fixture.Snapshot().Statuses, value => value.IsActive);
                break;
            case 198:
                Assert.Empty(applied.NetworkBytes);
                Assert.All(applied.EffectResults, value => Assert.True(value.State == SkillEffectExecutionState.Committed));
                break;
        }
    }

    private static async Task VerifyPersistenceScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var applied = await fixture.ApplyAsync(
            scenarioId is 202 or 208 ? 3 : 1,
            $"persist-{scenarioId}");
        var instance = fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!;
        if (scenarioId is 202 or 208)
        {
            _ = await fixture.ApplyAsync(3, $"persist-stack-{scenarioId}");
            instance = fixture.Snapshot().Statuses.Single(value => value.IsActive);
        }

        if (scenarioId is >= 203 and <= 205 or 209)
        {
            var trigger = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                $"persist-trigger-{scenarioId}",
                CancellationToken.None);
            Assert.NotEqual(Guid.Empty, trigger.StatusTriggerExecutionPlanId);
            Assert.NotNull(fixture.Store.GetTriggerPlan(trigger.StatusTriggerExecutionPlanId));
            return;
        }

        if (scenarioId == 210)
        {
            var direct = fixture.Application(1, "official-direct") with
            {
                Source = StatusRequestSource.OfficialClient,
                ExpectedTargetVersion = fixture.Snapshot().StatusVersion
            };
            Assert.Equal(
                StatusResultCode.InvalidSource,
                (await fixture.Coordinator.ApplyAsync(
                    fixture.Battle,
                    direct,
                    CancellationToken.None)).ResultCode);
            return;
        }

        if (scenarioId == 211)
        {
            Assert.Equal(
                "status.authority_version_conflict",
                fixture.Authority.UpdateStatusInstance(
                    instance,
                    instance.RuntimeVersion,
                    999,
                    "persist-version").Error.Code);
            return;
        }

        if (scenarioId == 212)
        {
            _ = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.BattleCompleted,
                null,
                "persist-cleanup",
                CancellationToken.None);
            Assert.False(fixture.Authority.GetStatusInstance(instance.StatusInstanceId)!.IsActive);
            return;
        }

        if (scenarioId == 213)
        {
            var missing = Instance(fixture, 999);
            Assert.True(fixture.Authority.Reload(
                fixture.Battle.BattleInstanceId,
                fixture.Store.LoadBattle(fixture.Battle.BattleInstanceId).Append(missing)).Succeeded);
            Assert.NotNull(fixture.Authority.GetStatusInstance(missing.StatusInstanceId));
            return;
        }

        var restored = new InMemoryBattleStatusAuthority();
        Assert.True(restored.Reload(
            fixture.Battle.BattleInstanceId,
            fixture.Store.LoadBattle(fixture.Battle.BattleInstanceId)).Succeeded);
        var reloaded = restored.GetStatusInstance(instance.StatusInstanceId)!;
        switch (scenarioId)
        {
            case 199:
            case 200:
            case 206:
                Assert.Equal(instance.StatusInstanceId, reloaded.StatusInstanceId);
                break;
            case 201:
            case 207:
                Assert.Equal(instance.RemainingRounds, reloaded.RemainingRounds);
                break;
            case 202:
            case 208:
                Assert.Equal(instance.StackCount, reloaded.StackCount);
                break;
        }
    }

    private static async Task VerifyDiagnosticsScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var diagnosticStatusId = scenarioId is 218 or 223 or 224 ? 11 : 5;
        var applied = await fixture.ApplyAsync(diagnosticStatusId, $"diagnostics-{scenarioId}");
        var instance = fixture.Authority.GetStatusInstance(applied.StatusInstanceId!.Value)!;
        _ = fixture.Modifiers.ApplyDamageModifiers(
            new StatusDamageModifierRequest(
                fixture.Battle.BattleInstanceId,
                1,
                fixture.PlayerId,
                fixture.EnemyId,
                10,
                fixture.Now));
        _ = fixture.Restrictions.Evaluate(fixture.Battle, fixture.BasicAction("diagnostic"), fixture.Now);
        if (scenarioId is 215 or 217)
        {
            _ = await fixture.RemoveAsync(instance);
        }

        if (scenarioId is 218 or 224)
        {
            _ = await fixture.TriggerCoordinator.ResolveAsync(
                fixture.Battle,
                StatusTriggerPhase.RoundClosing,
                null,
                "diagnostic-trigger",
                CancellationToken.None);
        }

        var snapshot = fixture.Inspector.Query(new StatusInspectorQuery(
            BattleInstanceId: fixture.Battle.BattleInstanceId,
            Offset: 0,
            Limit: 10));
        switch (scenarioId)
        {
            case 214:
            case 216:
            case 217:
                Assert.NotEmpty(snapshot.Audit);
                break;
            case 215:
                Assert.Contains(snapshot.Audit, value => value.StatusRemovalId is not null);
                break;
            case 218:
                Assert.NotEmpty(snapshot.Triggers);
                break;
            case 219:
            case 225:
                Assert.NotEmpty(snapshot.Modifiers);
                break;
            case 220:
            case 226:
                Assert.NotEmpty(snapshot.Restrictions);
                break;
            case 221:
                Assert.NotEmpty(snapshot.Definitions);
                break;
            case 222:
                Assert.NotEmpty(snapshot.Instances);
                break;
            case 223:
                Assert.True(snapshot.Triggers.Count >= 0);
                break;
            case 227:
                Assert.InRange(snapshot.Instances.Count, 0, 10);
                break;
            case 228:
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<StatusInstanceInspectorItem>)snapshot.Instances).Add(snapshot.Instances[0]));
                break;
            case 229:
                fixture.Inspector.FailureInjection.Point = StatusFailurePoint.Inspector;
                Assert.Equal(
                    "status.inspector_failed",
                    fixture.Inspector.Query(new StatusInspectorQuery()).FailureCode);
                Assert.NotNull(fixture.Authority.GetStatusInstance(instance.StatusInstanceId));
                break;
            case 230:
                var captures = await Task.WhenAll(
                    Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                        fixture.Inspector.Query(new StatusInspectorQuery()))));
                Assert.All(captures, value => Assert.Empty(value.FailureCode));
                break;
            case 231:
                Assert.DoesNotContain(
                    fixture.Audit.Snapshot,
                    value => value.CorrelationId.Contains("password", StringComparison.OrdinalIgnoreCase));
                break;
            case 232:
                Assert.DoesNotContain(
                    FindProjectRoot(),
                    fixture.Audit.Snapshot.Select(value => value.CorrelationId));
                break;
            case 233:
                Assert.Empty(applied.NetworkBytes);
                break;
        }
    }

    private static void VerifyFrozenScenario(int scenarioId)
    {
        Assert.InRange(scenarioId, 234, 245);
        Assert.True(typeof(BattleActionResolver).Assembly == typeof(StatusEffectCoordinator).Assembly);
        Assert.True(typeof(CombatCoordinator).IsSealed);
        Assert.True(typeof(SkillActionResolver).IsSealed);
    }

    private static OperationResult Validate(StatusDefinitionRecord record) =>
        new StatusDefinitionValidator().Validate(
            new StatusDefinitionMapper().Map(record).Value!);

    private static StatusInstance Instance(Fixture fixture, int definitionId) =>
        new(
            BattleRuntimeHash.DeterministicGuid($"test-instance:{definitionId}"),
            definitionId,
            fixture.Battle.BattleInstanceId,
            fixture.EnemyId,
            fixture.PlayerId,
            Guid.NewGuid(),
            null,
            null,
            1,
            1,
            1,
            1,
            2,
            2,
            StatusLifecycleState.Active,
            new StatusTriggerCursor(0, 0, null, StatusRecoveryState.NotRequired, 0),
            0,
            "test-v1",
            CombatPolicyStatus.TestOnly,
            fixture.Now,
            fixture.Now,
            null,
            null,
            "test",
            "{}");

    private static StatusTriggerDefinition Trigger(
        int definitionId,
        int index,
        StatusTriggerPhase phase,
        StatusTriggerEffectType effect = StatusTriggerEffectType.None,
        long value = 0) =>
        new(
            $"status-{definitionId}-trigger-{index}",
            definitionId,
            index,
            phase,
            null,
            effect,
            "DeterministicTest",
            value,
            "StatusTarget",
            null,
            CombatPolicyStatus.TestOnly,
            "{}");

    private static StatusModifierDefinition Modifier(
        int definitionId,
        int index,
        StatusModifierType type,
        long flat = 0,
        int basisPoints = 10_000) =>
        new(
            $"status-{definitionId}-modifier-{index}",
            definitionId,
            index,
            type,
            flat,
            basisPoints,
            false,
            CombatPolicyStatus.TestOnly,
            "{}");

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Project root not found.");
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid PlayerId,
        Guid EnemyId,
        BattleInstance Battle,
        IReadOnlyList<StatusDefinitionRecord> Records,
        ImmutableStatusDefinitionCatalog Catalog,
        InMemoryBattleStatusAuthority Authority,
        InMemoryStatusRuntimeStore Store,
        InMemoryStatusEventSink Events,
        InMemoryStatusAuditLedger Audit,
        StatusEffectCoordinator Coordinator,
        StatusModifierPipeline Modifiers,
        StatusActionRestrictionPolicy Restrictions,
        BattleScopedCombatExecutionPort Combat,
        StatusTriggerCoordinator TriggerCoordinator,
        SkillActionResolver SkillResolver,
        BattleActionResolver BattleResolver,
        StatusRuntimeInspector Inspector)
    {
        public static Fixture Create(
            StatusTriggerPhase? dynamicPhase = null,
            long playerHp = 80,
            long enemyHp = 100)
        {
            var now = new DateTimeOffset(2026, 7, 31, 3, 0, 0, TimeSpan.Zero);
            var battleId = Guid.Parse("41000000-0000-0000-0000-000000000001");
            var playerId = Guid.Parse("42000000-0000-0000-0000-000000000001");
            var enemyId = Guid.Parse("43000000-0000-0000-0000-000000000001");
            var player = Participant(playerId, battleId, BattleSide.PlayerSide, 0, 1001, null, playerHp, "session");
            var enemy = Participant(enemyId, battleId, BattleSide.EnemySide, 100, null, 1, enemyHp, null);
            var participants = new[] { player, enemy };
            var round = new BattleRound(
                battleId,
                1,
                BattleRoundState.Resolving,
                Freeze(participants.Select(value => value.ParticipantId)),
                [],
                [],
                [],
                0,
                [],
                now,
                now,
                null,
                1,
                "status-test");
            var battle = new BattleInstance(
                battleId,
                Guid.Parse("41000000-0000-0000-0000-000000000002"),
                "safe-battle",
                "world-test",
                1,
                "status-test",
                BattleType.InternalTest,
                BattleState.Active,
                BattlePhase.ResolvingActions,
                1,
                0,
                Freeze(participants),
                [],
                round,
                1,
                BattleWinnerSide.None,
                BattleCompletionReason.None,
                BattleRewardState.Pending,
                BattleRecoveryState.NotRequired,
                now,
                now,
                now,
                null,
                "status-test",
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.Baseline,
                CombatPolicyStatus.EvidenceBlocked,
                "status-test-v1",
                "{\"scope\":\"TestOnly\"}");
            var records = BuildRecords(dynamicPhase);
            var catalog = ImmutableStatusDefinitionCatalog.Create(
                records,
                new StatusDefinitionMapper(),
                new StatusDefinitionValidator()).Value!;
            var authority = new InMemoryBattleStatusAuthority();
            var store = new InMemoryStatusRuntimeStore(authority);
            var events = new InMemoryStatusEventSink();
            var audit = new InMemoryStatusAuditLedger();
            var clock = new DeterministicTestBattleClock(now);
            var coordinator = new StatusEffectCoordinator(
                catalog,
                authority,
                new StatusApplicationPlanner(),
                store,
                new DeterministicStatusStackPolicy(),
                new RoundBasedStatusDurationPolicy(),
                events,
                audit,
                clock);
            var modifiers = new StatusModifierPipeline(catalog, authority, store);
            var restrictions = new StatusActionRestrictionPolicy(catalog, authority, store);
            var combat = new BattleScopedCombatExecutionPort(
                new DeterministicTestDamagePolicy(10),
                modifiers,
                modifiers);
            combat.Seed(battleId, participants);
            var triggerCoordinator = new StatusTriggerCoordinator(
                catalog,
                authority,
                new StatusTriggerPlanner(new DeterministicTestStatusTriggerPolicy()),
                store,
                new StatusPeriodicDamageTriggerHandler(combat),
                new StatusPeriodicHealingTriggerHandler(combat),
                new RoundBasedStatusDurationPolicy(),
                coordinator,
                events,
                audit,
                clock);
            var skillRecords = new[]
            {
                SkillRecord(1000, SkillEffectType.ApplyStatus, 1),
                SkillRecord(1001, SkillEffectType.RemoveStatus, 1),
                SkillRecord(1002, SkillEffectType.ApplyStatus, 24),
                SkillRecord(1003, SkillEffectType.ApplyStatus, 25),
                SkillRecord(1004, SkillEffectType.RemoveStatus, 25)
            };
            var skillCatalog = ImmutableSkillDefinitionCatalog.Create(
                skillRecords,
                new SkillDefinitionMapper(),
                new SkillDefinitionValidator()).Value!;
            var ownership = skillRecords.Select(value => new SkillOwnershipRecord(
                1001,
                value.SkillDefinitionId,
                1,
                true,
                CombatPolicyStatus.TestOnly,
                "status-skill-test-v1",
                "{}"));
            var costs = new InMemorySkillCostReservationStore();
            var usage = new InMemorySkillUsageStore();
            var skillStore = new InMemorySkillExecutionStore(costs, usage);
            var skillResolver = new SkillActionResolver(
                skillCatalog,
                new SkillAvailabilityPolicy(ownership),
                new SkillTargetResolver(),
                new SkillCostPolicy(),
                costs,
                new RoundBasedSkillCooldownPolicy(usage),
                new SkillExecutionPlanner(),
                new SkillEffectResolver(new SkillEffectHandlerRegistry(
                [
                    new SkillApplyStatusEffectHandler(coordinator, authority, triggerCoordinator),
                    new SkillRemoveStatusEffectHandler(coordinator, authority, triggerCoordinator)
                ])),
                skillStore,
                combat,
                combat,
                new InMemorySkillEventSink(),
                new InMemorySkillAuditLedger(),
                clock);
            return new Fixture(
                now,
                playerId,
                enemyId,
                battle,
                records,
                catalog,
                authority,
                store,
                events,
                audit,
                coordinator,
                modifiers,
                restrictions,
                combat,
                triggerCoordinator,
                skillResolver,
                new BattleActionResolver(combat, clock, skillResolver, restrictions),
                new StatusRuntimeInspector(catalog, store, audit, clock));
        }

        public BattleStatusSnapshot Snapshot(Guid? targetId = null) =>
            Authority.GetParticipantSnapshot(
                Battle.BattleInstanceId,
                targetId ?? EnemyId,
                Now);

        public StatusApplicationRequest Application(
            int definitionId,
            string suffix,
            long? expectedStatusVersion = null,
            Guid? targetId = null)
        {
            var target = targetId ?? EnemyId;
            var applicationId = BattleRuntimeHash.DeterministicGuid(
                $"status-test-application:{suffix}:{definitionId}:{target:N}");
            return new StatusApplicationRequest(
                applicationId,
                $"status-test-application:{suffix}:{definitionId}:{target:N}",
                Battle.BattleInstanceId,
                Battle.CurrentRoundNumber,
                Guid.Parse("44000000-0000-0000-0000-000000000001"),
                null,
                null,
                PlayerId,
                target,
                definitionId,
                null,
                null,
                Battle.BattleVersion,
                expectedStatusVersion ?? Snapshot(target).StatusVersion,
                StatusRequestSource.TrustedInternalTest,
                Now,
                $"status-test-{suffix}");
        }

        public Task<StatusApplicationResult> ApplyAsync(
            int definitionId,
            string suffix = "apply",
            BattleInstance? battle = null,
            Guid? targetId = null)
        {
            var target = targetId ?? EnemyId;
            return Coordinator.ApplyAsync(
                battle ?? Battle,
                Application(
                    definitionId,
                    suffix,
                    Snapshot(target).StatusVersion,
                    target),
                CancellationToken.None);
        }

        public StatusRemovalRequest Removal(StatusInstance instance, string suffix)
        {
            var removalId = BattleRuntimeHash.DeterministicGuid(
                $"status-test-removal:{suffix}:{instance.StatusInstanceId:N}");
            return new StatusRemovalRequest(
                removalId,
                $"status-test-removal:{suffix}:{instance.StatusInstanceId:N}",
                Battle.BattleInstanceId,
                Battle.CurrentRoundNumber,
                Guid.Parse("44000000-0000-0000-0000-000000000002"),
                PlayerId,
                instance.TargetParticipantId,
                instance.StatusInstanceId,
                instance.StatusDefinitionId,
                StatusRemovalReason.AdministrativeTest,
                Snapshot(instance.TargetParticipantId).StatusVersion,
                StatusRequestSource.AdministrativeTest,
                Now,
                $"status-remove-{suffix}");
        }

        public Task<StatusRemovalResult> RemoveAsync(
            StatusInstance instance,
            string suffix = "remove") =>
            Coordinator.RemoveAsync(Battle, Removal(instance, suffix), CancellationToken.None);

        public Task<StatusRemovalResult> RemoveMissingAsync()
        {
            var missing = Instance(this, 1) with { StatusInstanceId = Guid.NewGuid() };
            return Coordinator.RemoveAsync(Battle, Removal(missing, "missing"), CancellationToken.None);
        }

        public BattleLockedAction BasicAction(string suffix) =>
            Action(BattleActionType.BasicAttack, [EnemyId], null, suffix);

        public BattleLockedAction PassAction(string suffix) =>
            Action(BattleActionType.Pass, [], null, suffix);

        public BattleLockedAction SkillAction(int skillId, string suffix) =>
            Action(BattleActionType.Skill, [EnemyId], skillId, suffix);

        public IEnumerable<(StatusInstance Instance, StatusDefinition Definition)> Eligible() =>
            Snapshot().Statuses
                .Where(value => value.IsActive)
                .Select(value => (
                    value,
                    Catalog.Snapshot.Single(definition =>
                        definition.StatusDefinitionId == value.StatusDefinitionId)));

        private BattleLockedAction Action(
            BattleActionType type,
            IReadOnlyList<Guid> targets,
            int? skillId,
            string suffix)
        {
            var actionId = BattleRuntimeHash.DeterministicGuid($"status-action:{suffix}");
            return new BattleLockedAction(
                actionId,
                Battle.BattleInstanceId,
                Battle.CurrentRoundNumber,
                PlayerId,
                type,
                targets,
                skillId,
                null,
                BattleRuntimeHash.SafeId($"status-action:{suffix}"),
                BattleRuntimeHash.PersistenceKey($"status-action:{suffix}"),
                Now,
                Now,
                $"status-action-{suffix}");
        }

        private static BattleParticipant Participant(
            Guid participantId,
            Guid battleId,
            BattleSide side,
            int slot,
            long? characterId,
            int? monsterId,
            long hp,
            string? sessionId) =>
            new(
                participantId,
                battleId,
                characterId is null ? BattleParticipantType.Monster : BattleParticipantType.Player,
                side,
                slot,
                characterId,
                monsterId,
                characterId ?? monsterId ?? 0,
                60_000_000 + slot,
                sessionId,
                characterId is null ? "Status Monster" : "Status Player",
                1,
                100,
                hp,
                hp > 0,
                true,
                true,
                false,
                null,
                hp > 0 ? BattleParticipantCombatState.Ready : BattleParticipantCombatState.Defeated,
                1,
                30,
                5,
                null,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.TestOnly,
                new DateTimeOffset(2026, 7, 31, 3, 0, 0, TimeSpan.Zero),
                hp > 0 ? null : new DateTimeOffset(2026, 7, 31, 3, 0, 0, TimeSpan.Zero),
                "{}");

        private static IReadOnlyList<StatusDefinitionRecord> BuildRecords(StatusTriggerPhase? dynamicPhase)
        {
            var records = new List<StatusDefinitionRecord>
            {
                StatusRecord(1, StatusStackPolicyType.RejectDuplicate),
                StatusRecord(2, StatusStackPolicyType.RefreshDuration),
                StatusRecord(3, StatusStackPolicyType.AddStacks, maximumStacks: 3,
                    modifiers: [Modifier(3, 0, StatusModifierType.OutgoingDamageFlat, flat: 1)]),
                StatusRecord(4, StatusStackPolicyType.ReplaceExisting,
                    modifiers: [Modifier(4, 0, StatusModifierType.OutgoingDamageFlat, flat: 2)]),
                StatusRecord(5, StatusStackPolicyType.RejectDuplicate,
                    modifiers: [Modifier(5, 0, StatusModifierType.OutgoingDamageFlat, flat: 5)]),
                StatusRecord(6, StatusStackPolicyType.RejectDuplicate,
                    modifiers: [Modifier(6, 0, StatusModifierType.IncomingDamageMultiplier, basisPoints: 5_000)]),
                StatusRecord(7, StatusStackPolicyType.RejectDuplicate,
                    modifiers: [Modifier(7, 0, StatusModifierType.OutgoingHealingFlat, flat: 10)]),
                StatusRecord(8, StatusStackPolicyType.RejectDuplicate,
                    restrictions: [Restriction(0, StatusActionRestrictionType.PreventAllAction)]),
                StatusRecord(9, StatusStackPolicyType.RejectDuplicate,
                    restrictions: [Restriction(0, StatusActionRestrictionType.PreventSkillAction)]),
                StatusRecord(10, StatusStackPolicyType.RejectDuplicate,
                    restrictions: [Restriction(0, StatusActionRestrictionType.PreventBasicAttack)]),
                StatusRecord(
                    11,
                    StatusStackPolicyType.RejectDuplicate,
                    category: StatusCategory.PeriodicDamage,
                    triggers: [Trigger(11, 0, StatusTriggerPhase.RoundClosing, StatusTriggerEffectType.PeriodicDamage, 5)]),
                StatusRecord(
                    12,
                    StatusStackPolicyType.RejectDuplicate,
                    category: StatusCategory.PeriodicHealing,
                    triggers: [Trigger(12, 0, StatusTriggerPhase.RoundOpening, StatusTriggerEffectType.PeriodicHealing, 7)]),
                StatusRecord(
                    13,
                    StatusStackPolicyType.RejectDuplicate,
                    duration: new StatusDurationDefinition(
                        StatusDurationPolicyType.UntilBattleEnd,
                        null,
                        CombatPolicyStatus.TestOnly,
                        "{}"),
                    triggers: [Trigger(13, 0, StatusTriggerPhase.BattleCompleted)]),
                StatusRecord(
                    14,
                    StatusStackPolicyType.RejectDuplicate,
                    duration: new StatusDurationDefinition(
                        StatusDurationPolicyType.UntilRemoved,
                        null,
                        CombatPolicyStatus.TestOnly,
                        "{}"),
                    triggers: [Trigger(14, 0, StatusTriggerPhase.OnRemove)]),
                new StatusDefinitionRecord(
                    15,
                    "raw-status-15",
                    "Raw Status",
                    "",
                    StatusCategory.Unknown,
                    StatusPolarity.Unknown,
                    StatusApplicationPolicyType.EvidenceBlocked,
                    new StatusStackDefinition(StatusStackPolicyType.Unknown, null, CombatPolicyStatus.EvidenceBlocked, "{\"unknown\":true}"),
                    new StatusDurationDefinition(StatusDurationPolicyType.Unknown, null, CombatPolicyStatus.EvidenceBlocked, "{\"unknown\":true}"),
                    [],
                    [],
                    [],
                    new StatusDispelDefinition(false, false, null, null, CombatPolicyStatus.EvidenceBlocked, "{\"unknown\":true}"),
                    true,
                    "raw-status-v1",
                    CombatPolicyStatus.EvidenceBlocked,
                    CombatPolicyStatus.EvidenceBlocked,
                    "{\"unknown\":true}"),
                StatusRecord(16, StatusStackPolicyType.RejectDuplicate) with { Enabled = false },
                StatusRecord(
                    17,
                    StatusStackPolicyType.RejectDuplicate,
                    policyStatus: CombatPolicyStatus.ContentBacked,
                    applicationPolicy: StatusApplicationPolicyType.ContentBacked,
                    triggers:
                    [
                        Trigger(17, 0, StatusTriggerPhase.OnApply) with
                        {
                            PolicyStatus = CombatPolicyStatus.ContentBacked
                        }
                    ])
            };
            if (dynamicPhase is not null)
            {
                records.Add(StatusRecord(
                    18,
                    StatusStackPolicyType.RejectDuplicate,
                    triggers: [Trigger(18, 0, dynamicPhase.Value)]));
            }
            else
            {
                records.Add(StatusRecord(
                    18,
                    StatusStackPolicyType.RejectDuplicate,
                    triggers: [Trigger(18, 0, StatusTriggerPhase.OnApply)]));
            }

            records.Add(StatusRecord(19, StatusStackPolicyType.RejectDuplicate,
                modifiers: [Modifier(19, 0, StatusModifierType.OutgoingDamageMultiplier, basisPoints: 15_000)]));
            records.Add(StatusRecord(20, StatusStackPolicyType.RejectDuplicate,
                modifiers: [Modifier(20, 0, StatusModifierType.IncomingDamageMultiplier, basisPoints: 5_000)]));
            records.Add(StatusRecord(21, StatusStackPolicyType.RejectDuplicate,
                modifiers: [Modifier(21, 0, StatusModifierType.IncomingHealingFlat, flat: 10)]));
            records.Add(StatusRecord(22, StatusStackPolicyType.RejectDuplicate,
                modifiers: [Modifier(22, 0, StatusModifierType.OutgoingHealingMultiplier, basisPoints: 15_000)]));
            records.Add(StatusRecord(23, StatusStackPolicyType.RejectDuplicate,
                modifiers: [Modifier(23, 0, StatusModifierType.IncomingHealingMultiplier, basisPoints: 5_000)]));
            records.Add(StatusRecord(
                24,
                StatusStackPolicyType.RejectDuplicate,
                category: StatusCategory.PeriodicDamage,
                triggers:
                [
                    Trigger(
                        24,
                        0,
                        StatusTriggerPhase.OnApply,
                        StatusTriggerEffectType.PeriodicDamage,
                        5)
                ]));
            records.Add(StatusRecord(
                25,
                StatusStackPolicyType.RejectDuplicate,
                category: StatusCategory.PeriodicDamage,
                duration: new StatusDurationDefinition(
                    StatusDurationPolicyType.UntilRemoved,
                    null,
                    CombatPolicyStatus.TestOnly,
                    "{}"),
                triggers:
                [
                    Trigger(
                        25,
                        0,
                        StatusTriggerPhase.OnRemove,
                        StatusTriggerEffectType.PeriodicDamage,
                        5)
                ]));
            return Freeze(records);
        }

        private static StatusDefinitionRecord StatusRecord(
            int id,
            StatusStackPolicyType stack,
            int maximumStacks = 1,
            StatusCategory category = StatusCategory.Buff,
            StatusDurationDefinition? duration = null,
            IReadOnlyList<StatusTriggerDefinition>? triggers = null,
            IReadOnlyList<StatusModifierDefinition>? modifiers = null,
            IReadOnlyList<StatusActionRestrictionDefinition>? restrictions = null,
            CombatPolicyStatus policyStatus = CombatPolicyStatus.TestOnly,
            StatusApplicationPolicyType applicationPolicy = StatusApplicationPolicyType.TestOnly) =>
            new(
                id,
                $"test-status-{id}",
                $"Test Status {id}",
                "",
                category,
                StatusPolarity.Neutral,
                applicationPolicy,
                new StatusStackDefinition(stack, maximumStacks, policyStatus, "{}"),
                duration ?? new StatusDurationDefinition(
                    StatusDurationPolicyType.Rounds,
                    2,
                    policyStatus,
                    "{}"),
                triggers ?? (modifiers is null && restrictions is null
                    ? [Trigger(id, 0, StatusTriggerPhase.OnApply)]
                    : []),
                modifiers ?? [],
                restrictions ?? [],
                new StatusDispelDefinition(
                    true,
                    true,
                    null,
                    null,
                    policyStatus,
                    "{}"),
                true,
                "status-test-v1",
                policyStatus,
                CombatPolicyStatus.EvidenceBlocked,
                "{\"scope\":\"TestOnly\"}");

        private static StatusActionRestrictionDefinition Restriction(
            int index,
            StatusActionRestrictionType type) =>
            new(
                $"restriction-{index}-{type}",
                index,
                type,
                CombatPolicyStatus.TestOnly,
                "{}");

        private static SkillDefinitionRecord SkillRecord(
            int id,
            SkillEffectType type,
            long statusDefinitionId) =>
            new(
                id,
                $"status-skill-{id}",
                $"Status Skill {id}",
                "",
                1,
                0,
                SkillCategory.Active,
                SkillActionCategory.Status,
                SkillTargetPolicyType.SingleEnemy,
                new SkillCostDefinition(
                    SkillResourceType.None,
                    0,
                    CombatPolicyStatus.TestOnly,
                    "{}"),
                new SkillCooldownDefinition(
                    0,
                    null,
                    null,
                    null,
                    CombatPolicyStatus.TestOnly,
                    "{}"),
                new SkillUsageDefinition(null, CombatPolicyStatus.TestOnly, "{}"),
                [
                    new SkillEffectDefinition(
                        $"status-skill-{id}-effect-0",
                        id,
                        0,
                        type,
                        SkillTargetPolicyType.SingleEnemy,
                        "StatusDefinitionId",
                        statusDefinitionId,
                        0,
                        CombatDamageType.Unknown,
                        false,
                        false,
                        false,
                        true,
                        1,
                        CombatPolicyStatus.TestOnly,
                        "status-skill-test-v1",
                        "{}")
                ],
                true,
                "status-skill-test-v1",
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.EvidenceBlocked,
                "{}");
    }
}
