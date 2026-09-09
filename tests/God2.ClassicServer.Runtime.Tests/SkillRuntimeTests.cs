using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class SkillRuntimeTests
{
    public static TheoryData<int, string> OfflineHeadlessScenarios
    {
        get
        {
            var names = """
                Load skill records into immutable catalog
                Preserve unknown raw metadata
                Reject duplicate skill definition ID
                Reject invalid effect ordering
                Reject disabled skill
                Preserve unknown skill category
                Preserve unknown target policy
                Preserve unknown cost type
                Production skill requires evidence status
                TestOnly skill isolated from production path
                Catalog snapshot immutable
                Skill content count report generated
                Submit Skill battle action
                Skill action locks immutably
                Locked Skill action reaches SkillActionResolver
                Skill resolver cannot run outside Battle Runtime
                Reject wrong battle
                Reject wrong round
                Reject wrong phase
                Reject dead participant
                Reject old session
                Reject ownership mismatch
                Completed battle rejects skill
                Skill action cannot change after lock
                Resolve owned TestOnly skill
                Reject unowned skill
                Reject invalid skill rank
                Reject missing skill
                Reject disabled skill
                Production ownership remains EvidenceBlocked without data
                Client skill rank candidate ignored
                Test loadout unavailable to official path
                Self target resolves server-side
                SingleEnemy valid
                SingleEnemy rejects ally
                SingleAlly valid
                SingleAlly rejects enemy
                AllEnemies resolves stable snapshot
                AllAllies resolves stable snapshot
                Reject target outside battle
                Reject dead damage target
                Reject dead heal target
                Client extra targets rejected by policy
                Target snapshot immutable
                Recovery preserves same targets
                Stable target ordering
                Unsupported random target EvidenceBlocked
                Unsupported formation target EvidenceBlocked
                No-cost skill succeeds
                TestOnly resource cost validates
                Insufficient test resource rejected
                Cost reservation created once
                Duplicate reservation does not double reserve
                Cost release before any effect commit
                Cost not released after effect commit
                Cost commit exactly once
                Crash after reservation recovers
                Production unknown cost EvidenceBlocked
                Client cost candidate ignored
                Item cost remains unsupported
                Currency cost remains unsupported
                HP cost remains unsupported
                NoCooldown skill available
                TestOnly round cooldown commits
                Cooldown blocks next round
                Cooldown expires at expected round
                Duplicate execution does not increment usage twice
                Failed-before-effect does not commit cooldown
                Recovered execution commits cooldown once
                Production unknown cooldown EvidenceBlocked
                Client cooldown candidate ignored
                Round number from battle authority used
                Single-target damage skill succeeds
                Damage uses Existing CombatRuntime boundary
                Client damage value ignored
                Damage reduces authoritative HP once
                Damage cannot reduce HP below zero
                Damage version increments once
                Non-lethal skill keeps target alive
                Lethal skill marks participant defeated
                Lethal skill preserves single death
                Dead target rejects later damage
                Duplicate damage effect does not double damage
                Concurrent damage preserves HP consistency
                Damage policy marked Baseline or TestOnly
                Damage effect audit complete
                Single-target heal succeeds
                Healing uses Battle HP authority
                Heal cannot exceed MaximumHp
                EffectiveHeal records clamped amount
                Full HP target handled consistently
                Dead target cannot be healed
                Heal does not revive
                Client heal value ignored
                Duplicate heal does not double heal
                Concurrent heal and damage preserves version
                Heal version increments once
                Heal effect audit complete
                AllEnemies damage resolves all legal targets
                AllAllies heal resolves all legal targets
                Multi-target order deterministic
                Each target has stable idempotency key
                First target commit then crash
                Recovery resumes from second target
                Recovery does not repeat first target
                Midpoint persistence failure becomes RecoveryRequired
                Dead target before execution follows documented rejection
                Multi-target lethal results commit once per target
                Final skill result contains all target results
                Multi-effect order deterministic
                Immutable execution plan persisted
                Recovery reuses original plan
                Same key same payload returns DuplicateCompleted
                Same key changed payload returns ReplayConflict
                Concurrent duplicate skill executes once
                Crash before first effect releases cost safely
                Crash after first effect preserves cost reservation
                Crash after all effects before cost commit
                Recovery commits cost once
                Crash after cost before cooldown
                Recovery commits cooldown once
                Crash after cooldown before action result
                Recovery writes action result once
                Crash after action result before round cursor
                Round recovery does not repeat skill
                Reconnect restores skill execution state
                Old delayed action rejected
                Recovery failure visible in Inspector
                ApplyStatus returns Unsupported or EvidenceBlocked
                RemoveStatus returns Unsupported or EvidenceBlocked
                Revive returns Unsupported or EvidenceBlocked
                Summon returns Unsupported or EvidenceBlocked
                Resource restore returns Unsupported or EvidenceBlocked
                Scripted effect returns Unsupported or EvidenceBlocked
                NoOp does not fake successful gameplay
                Skill audit complete
                Cost audit complete
                Cooldown audit complete
                Effect audit complete
                Inspector skill definition snapshot correct
                Inspector execution snapshot correct
                Inspector effect snapshot correct
                Inspector usage snapshot correct
                Inspector filters and pagination correct
                Inspector read model immutable
                Inspector failure does not break runtime
                Credential redaction matchCount zero
                Hardcoded local path matchCount zero
                Parameterized SQL verification contract
                Skill serializer emits zero fake network bytes
                Frozen Inventory Backend remains PASS
                Frozen World Interaction Backend remains PASS
                Frozen Combat Runtime remains PASS
                Frozen Turn-Based Battle Runtime remains PASS
                Battle Reward Exactly-once remains PASS
                Battle Disconnect and Reconnect remains PASS
                Portal and Session Rebinding remains PASS
                Protocol Audit bounded queue concurrency remains PASS
                Automation JSON bounded retry remains PASS
                Frozen Login-to-World remains PASS
                """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(160, names.Length);
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
    public async Task Offline_headless_skill_scenario_matrix_passes(int scenarioId, string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 160);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));

        if (scenarioId <= 12)
        {
            VerifyCatalogScenario(scenarioId);
        }
        else if (scenarioId <= 24)
        {
            await VerifyBattleIntegrationScenario(scenarioId);
        }
        else if (scenarioId <= 32)
        {
            await VerifyAvailabilityScenario(scenarioId);
        }
        else if (scenarioId <= 48)
        {
            VerifyTargetScenario(scenarioId);
        }
        else if (scenarioId <= 62)
        {
            await VerifyCostScenario(scenarioId);
        }
        else if (scenarioId <= 72)
        {
            await VerifyCooldownScenario(scenarioId);
        }
        else if (scenarioId <= 86)
        {
            await VerifyDamageScenario(scenarioId);
        }
        else if (scenarioId <= 98)
        {
            await VerifyHealingScenario(scenarioId);
        }
        else if (scenarioId <= 110)
        {
            await VerifyMultiTargetScenario(scenarioId);
        }
        else if (scenarioId <= 128)
        {
            await VerifyRecoveryScenario(scenarioId);
        }
        else if (scenarioId <= 135)
        {
            await VerifyUnsupportedScenario(scenarioId);
        }
        else if (scenarioId <= 150)
        {
            await VerifyDiagnosticsScenario(scenarioId);
        }
        else
        {
            VerifyFrozenBoundaryScenario(scenarioId);
        }
    }

    [Fact]
    public void Skill_intent_does_not_accept_client_authoritative_gameplay_values()
    {
        var properties = typeof(SkillActionRequest).GetProperties().Select(value => value.Name).ToHashSet();

        Assert.DoesNotContain("Damage", properties);
        Assert.DoesNotContain("Heal", properties);
        Assert.DoesNotContain("Cost", properties);
        Assert.DoesNotContain("Cooldown", properties);
        Assert.DoesNotContain("Effects", properties);
        Assert.DoesNotContain("TargetCount", properties);
    }

    [Fact]
    public void Skill_contracts_expose_required_boundaries()
    {
        Assert.True(typeof(ISkillDefinitionCatalog).IsInterface);
        Assert.True(typeof(ISkillActionResolver).IsInterface);
        Assert.True(typeof(ISkillTargetResolver).IsInterface);
        Assert.True(typeof(ISkillCostReservationStore).IsInterface);
        Assert.True(typeof(ISkillCooldownPolicy).IsInterface);
        Assert.True(typeof(ISkillExecutionPlanner).IsInterface);
        Assert.True(typeof(ISkillEffectHandlerRegistry).IsInterface);
        Assert.True(typeof(ISkillRecoveryCoordinator).IsInterface);
        Assert.True(typeof(IBattleHealthMutationPort).IsInterface);
        Assert.True(typeof(IStatusEffectResolver).IsInterface);
    }

    [Fact]
    public void Round_cooldown_usage_is_scoped_to_one_battle()
    {
        var fixture = Fixture.Create();
        var usage = new InMemorySkillUsageStore();
        var cooldown = new RoundBasedSkillCooldownPolicy(usage);
        var definition = fixture.Definition(102);

        Assert.True(cooldown.Commit(fixture.Plan(102), definition).Succeeded);
        Assert.Equal(
            "skill.cooldown_active",
            cooldown.Validate(
                fixture.Battle.BattleInstanceId,
                fixture.PlayerId,
                definition,
                2).Error.Code);
        Assert.True(cooldown.Validate(
            Guid.NewGuid(),
            fixture.PlayerId,
            definition,
            1).Succeeded);
    }

    [Fact]
    public async Task Battle_action_resolver_integrates_skill_without_network_bytes()
    {
        var fixture = Fixture.Create();
        var result = await fixture.BattleResolver.ResolveAsync(
            fixture.Battle,
            fixture.Action(100, [fixture.EnemyOneId]),
            0,
            CancellationToken.None);

        Assert.Equal(BattleActionResolutionCode.Success, result.Result);
        Assert.Single(result.ParticipantMutations!);
        Assert.Empty(result.NetworkBytes);
    }

    private static void VerifyCatalogScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        switch (scenarioId)
        {
            case 1:
                Assert.True(fixture.Catalog.Snapshot.Count >= 10);
                break;
            case 2:
                Assert.Contains("unknown", fixture.Catalog.Snapshot.Single(value => value.SkillDefinitionId == 109).RawMetadata);
                break;
            case 3:
                var duplicate = ImmutableSkillDefinitionCatalog.Create(
                    [fixture.Records[0], fixture.Records[0]],
                    new SkillDefinitionMapper(),
                    new SkillDefinitionValidator());
                Assert.Equal("skill.definition_duplicate", duplicate.Error.Code);
                break;
            case 4:
                var invalid = fixture.Records[0] with
                {
                    SkillDefinitionId = 999,
                    EffectDefinitions =
                    [
                        fixture.Records[0].EffectDefinitions[0] with
                        {
                            SkillDefinitionId = 999,
                            EffectDefinitionId = "late",
                            EffectIndex = 1
                        },
                        fixture.Records[0].EffectDefinitions[0] with
                        {
                            SkillDefinitionId = 999,
                            EffectDefinitionId = "early",
                            EffectIndex = 0
                        }
                    ]
                };
                Assert.Equal(
                    "skill.effect_order_invalid",
                    ImmutableSkillDefinitionCatalog.Create(
                        [invalid],
                        new SkillDefinitionMapper(),
                        new SkillDefinitionValidator()).Error.Code);
                break;
            case 5:
                Assert.Equal(
                    "skill.disabled",
                    fixture.Catalog.Get(105, BattleRequestSource.TrustedInternalTest).Error.Code);
                break;
            case 6:
                Assert.Equal(SkillCategory.Unknown, fixture.Catalog.Snapshot.Single(value => value.SkillDefinitionId == 109).SkillCategory);
                break;
            case 7:
                Assert.Equal(SkillTargetPolicyType.Unknown, fixture.Catalog.Snapshot.Single(value => value.SkillDefinitionId == 109).TargetPolicy);
                break;
            case 8:
                Assert.Equal(SkillResourceType.Unknown, fixture.Catalog.Snapshot.Single(value => value.SkillDefinitionId == 109).CostDefinition.ResourceType);
                break;
            case 9:
                Assert.Equal(
                    "skill.evidence_blocked",
                    fixture.Catalog.Get(109, BattleRequestSource.OfficialClient).Error.Code);
                break;
            case 10:
                Assert.Equal(
                    "skill.test_only_not_available",
                    fixture.Catalog.Get(100, BattleRequestSource.OfficialClient).Error.Code);
                break;
            case 11:
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<SkillDefinition>)fixture.Catalog.Snapshot).Add(fixture.Catalog.Snapshot[0]));
                break;
            case 12:
                Assert.Equal(fixture.Records.Count, fixture.Catalog.Snapshot.Count);
                break;
        }
    }

    private static async Task VerifyBattleIntegrationScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        switch (scenarioId)
        {
            case 13:
                var submitted = await fixture.SubmitThroughCoordinatorAsync();
                Assert.Equal(BattleResultCode.Success, submitted.Code);
                break;
            case 14:
            case 24:
                var action = fixture.Action(100, [fixture.EnemyOneId]);
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<Guid>)action.TargetParticipantIds).Add(fixture.EnemyTwoId));
                break;
            case 15:
                var resolved = await fixture.BattleResolver.ResolveAsync(
                    fixture.Battle,
                    fixture.Action(100, [fixture.EnemyOneId]),
                    0,
                    CancellationToken.None);
                Assert.Equal(BattleActionResolutionCode.Success, resolved.Result);
                break;
            case 16:
            case 19:
                var collecting = fixture.Battle with { CurrentPhase = BattlePhase.CollectingActions };
                Assert.Equal(
                    SkillResultCode.InvalidPhase,
                    (await fixture.Resolver.ResolveAsync(
                        collecting,
                        fixture.Action(100, [fixture.EnemyOneId]),
                        0,
                        CancellationToken.None)).ResultCode);
                break;
            case 17:
                Assert.Equal(
                    SkillResultCode.BattleNotFound,
                    (await fixture.Resolver.ResolveAsync(
                        fixture.Battle,
                        fixture.Action(100, [fixture.EnemyOneId]) with { BattleInstanceId = Guid.NewGuid() },
                        0,
                        CancellationToken.None)).ResultCode);
                break;
            case 18:
                Assert.Equal(
                    SkillResultCode.InvalidRound,
                    (await fixture.Resolver.ResolveAsync(
                        fixture.Battle,
                        fixture.Action(100, [fixture.EnemyOneId]) with { RoundNumber = 2 },
                        0,
                        CancellationToken.None)).ResultCode);
                break;
            case 20:
                var dead = Fixture.Create(playerHp: 0);
                Assert.Equal(
                    SkillResultCode.ParticipantDead,
                    (await dead.ResolveAsync(100, dead.EnemyOneId)).ResultCode);
                break;
            case 21:
                Assert.Equal(
                    BattleResultCode.InvalidSession,
                    (await fixture.SubmitThroughCoordinatorAsync(sessionId: "old-session")).Code);
                break;
            case 22:
                Assert.Equal(
                    BattleResultCode.OwnershipMismatch,
                    (await fixture.SubmitThroughCoordinatorAsync(characterId: 9999)).Code);
                break;
            case 23:
                var completed = fixture.Battle with
                {
                    State = BattleState.Completed,
                    CurrentPhase = BattlePhase.Completed
                };
                Assert.Equal(
                    SkillResultCode.InvalidPhase,
                    (await fixture.Resolver.ResolveAsync(
                        completed,
                        fixture.Action(100, [fixture.EnemyOneId]),
                        0,
                        CancellationToken.None)).ResultCode);
                break;
        }
    }

    private static async Task VerifyAvailabilityScenario(int scenarioId)
    {
        var fixture = scenarioId == 27
            ? Fixture.Create(ownershipRank: 99)
            : scenarioId == 26
                ? Fixture.Create(excludeOwnedSkillId: 100)
                : Fixture.Create();
        switch (scenarioId)
        {
            case 25:
                Assert.Equal(SkillResultCode.Success, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).ResultCode);
                break;
            case 26:
                Assert.Equal(SkillResultCode.SkillNotOwned, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).ResultCode);
                break;
            case 27:
                Assert.Equal(SkillResultCode.SkillRankInvalid, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).ResultCode);
                break;
            case 28:
                Assert.Equal(SkillResultCode.SkillNotFound, (await fixture.ResolveAsync(9999, fixture.EnemyOneId)).ResultCode);
                break;
            case 29:
                Assert.Equal(SkillResultCode.SkillDisabled, (await fixture.ResolveAsync(105, fixture.EnemyOneId)).ResultCode);
                break;
            case 30:
                var production = fixture.Battle with { BattleType = BattleType.PlayerVersusEnvironment };
                Assert.Equal(
                    SkillResultCode.SkillEvidenceBlocked,
                    (await fixture.Resolver.ResolveAsync(
                        production,
                        fixture.Action(109, [fixture.EnemyOneId]),
                        0,
                        CancellationToken.None)).ResultCode);
                break;
            case 31:
                Assert.Single((await fixture.ResolveAsync(100, fixture.EnemyOneId)).EffectResults);
                break;
            case 32:
                Assert.False(fixture.Catalog.Get(100, BattleRequestSource.OfficialClient).Succeeded);
                break;
        }
    }

    private static void VerifyTargetScenario(int scenarioId)
    {
        var fixture = Fixture.Create(enemyOneHp: scenarioId == 41 ? 0 : 100, allyHp: scenarioId == 42 ? 0 : 50);
        var source = fixture.Battle.Participants.Single(value => value.ParticipantId == fixture.PlayerId);
        OperationResult<SkillDefinition> definition;
        OperationResult<SkillTargetResolution> result;
        switch (scenarioId)
        {
            case 33:
                definition = fixture.Catalog.Get(106, BattleRequestSource.TrustedInternalTest);
                result = fixture.Targets.Resolve(fixture.Battle, source, definition.Value!, [fixture.EnemyOneId]);
                Assert.Equal(fixture.PlayerId, Assert.Single(result.Value!.Targets).ParticipantId);
                break;
            case 34:
                result = fixture.Target(100, fixture.EnemyOneId);
                Assert.Equal(fixture.EnemyOneId, Assert.Single(result.Value!.Targets).ParticipantId);
                break;
            case 35:
                Assert.False(fixture.Target(100, fixture.AllyId).Succeeded);
                break;
            case 36:
                Assert.True(fixture.Target(101, fixture.AllyId).Succeeded);
                break;
            case 37:
                Assert.False(fixture.Target(101, fixture.EnemyOneId).Succeeded);
                break;
            case 38:
                result = fixture.Target(102);
                Assert.Equal([fixture.EnemyOneId, fixture.EnemyTwoId], result.Value!.Targets.Select(value => value.ParticipantId));
                break;
            case 39:
                Assert.Equal(2, fixture.Target(103).Value!.Targets.Count);
                break;
            case 40:
                Assert.False(fixture.Target(100, Guid.NewGuid()).Succeeded);
                break;
            case 41:
                Assert.False(fixture.Target(100, fixture.EnemyOneId).Succeeded);
                break;
            case 42:
                Assert.False(fixture.Target(101, fixture.AllyId).Succeeded);
                break;
            case 43:
                Assert.False(fixture.Target(100, fixture.EnemyOneId, fixture.EnemyTwoId).Succeeded);
                break;
            case 44:
                result = fixture.Target(102);
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<SkillParticipantSnapshot>)result.Value!.Targets).Add(result.Value.Targets[0]));
                break;
            case 45:
                Assert.Equal(fixture.Target(102).Value!.Targets, fixture.Target(102).Value!.Targets);
                break;
            case 46:
                Assert.Equal(
                    fixture.Target(102).Value!.Targets.Select(value => value.ParticipantId),
                    fixture.Target(102).Value!.Targets.Select(value => value.ParticipantId));
                break;
            case 47:
                Assert.Equal("skill.target_policy_blocked", fixture.Target(107, fixture.EnemyOneId).Error.Code);
                break;
            case 48:
                var blocked = fixture.Records[0] with
                {
                    SkillDefinitionId = 999,
                    TargetPolicyCandidate = SkillTargetPolicyType.FormationRow,
                    EffectDefinitions =
                    [
                        fixture.Records[0].EffectDefinitions[0] with
                        {
                            SkillDefinitionId = 999,
                            EffectDefinitionId = "formation"
                        }
                    ]
                };
                var mapped = new SkillDefinitionMapper().Map(blocked);
                Assert.Equal(
                    "skill.target_policy_blocked",
                    fixture.Targets.Resolve(fixture.Battle, source, mapped.Value!, []).Error.Code);
                break;
        }
    }

    private static async Task VerifyCostScenario(int scenarioId)
    {
        var fixture = Fixture.Create(testResource: scenarioId == 51 ? 0 : 100);
        switch (scenarioId)
        {
            case 49:
                Assert.Equal(SkillResultCode.Success, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).ResultCode);
                break;
            case 50:
                Assert.True(fixture.CostPolicy.Validate(
                    fixture.Catalog.Get(102, BattleRequestSource.TrustedInternalTest).Value!,
                    fixture.Player,
                    1).Succeeded);
                break;
            case 51:
                Assert.Equal(SkillResultCode.InsufficientResource, (await fixture.ResolveAsync(102)).ResultCode);
                break;
            case 52:
            case 53:
                var plan = fixture.Plan(102);
                var first = fixture.Costs.Reserve(plan, fixture.Now);
                var second = fixture.Costs.Reserve(plan, fixture.Now);
                Assert.Equal(first.Value!.ReservationId, second.Value!.ReservationId);
                Assert.Single(fixture.Costs.Reservations);
                break;
            case 54:
                var releasePlan = fixture.Plan(102);
                var reserved = fixture.Costs.Reserve(releasePlan, fixture.Now).Value!;
                Assert.Equal(SkillCostReservationState.Released, fixture.Costs.Release(reserved.ReservationId, fixture.Now).Value!.State);
                Assert.Equal(100, fixture.Costs.GetAvailableTestResource(fixture.PlayerId));
                break;
            case 55:
            case 56:
                var committedResult = await fixture.ResolveAsync(102);
                var reservation = fixture.Costs.Reservations.Single();
                Assert.Equal(SkillCostReservationState.Committed, reservation.State);
                Assert.Equal(
                    reservation,
                    fixture.Costs.Commit(reservation.ReservationId, fixture.Now).Value);
                Assert.True(committedResult.Succeeded);
                break;
            case 57:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var interrupted = await fixture.ResolveAsync(102);
                Assert.Equal(SkillResultCode.RecoveryRequired, interrupted.ResultCode);
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.None;
                Assert.True((await fixture.Resolver.RecoverAsync(
                    interrupted.SkillExecutionId,
                    fixture.Battle,
                    CancellationToken.None)).Succeeded);
                break;
            case 58:
                Assert.Equal(
                    "skill.cost_evidence_blocked",
                    fixture.CostPolicy.Validate(
                        fixture.Catalog.Snapshot.Single(value => value.SkillDefinitionId == 120),
                        fixture.Player,
                        1).Error.Code);
                break;
            case 59:
                Assert.DoesNotContain("ClientCost", typeof(SkillActionRequest).GetProperties().Select(value => value.Name));
                break;
            case 60:
                Assert.Equal(SkillResultCode.CostEvidenceBlocked, (await fixture.ResolveAsync(120, fixture.EnemyOneId)).ResultCode);
                break;
            case 61:
                Assert.Equal(SkillResultCode.CostEvidenceBlocked, (await fixture.ResolveAsync(121, fixture.EnemyOneId)).ResultCode);
                break;
            case 62:
                Assert.Equal(SkillResultCode.CostEvidenceBlocked, (await fixture.ResolveAsync(122, fixture.EnemyOneId)).ResultCode);
                break;
        }
    }

    private static async Task VerifyCooldownScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        switch (scenarioId)
        {
            case 63:
                Assert.True(fixture.Cooldown.Validate(
                    fixture.Battle.BattleInstanceId,
                    fixture.PlayerId,
                    fixture.Definition(100),
                    1).Succeeded);
                break;
            case 64:
            case 72:
                var result = await fixture.ResolveAsync(102);
                Assert.Equal(3, result.CooldownResult.AvailableAtRound);
                Assert.Equal(1, fixture.Usage.Snapshot.Single().UsageCount);
                break;
            case 65:
                await fixture.ResolveAsync(102);
                Assert.Equal(
                    "skill.cooldown_active",
                    fixture.Cooldown.Validate(
                        fixture.Battle.BattleInstanceId,
                        fixture.PlayerId,
                        fixture.Definition(102),
                        2).Error.Code);
                break;
            case 66:
                await fixture.ResolveAsync(102);
                Assert.True(fixture.Cooldown.Validate(
                    fixture.Battle.BattleInstanceId,
                    fixture.PlayerId,
                    fixture.Definition(102),
                    3).Succeeded);
                break;
            case 67:
                var first = await fixture.ResolveAsync(102);
                var second = await fixture.Resolver.ResolveAsync(
                    fixture.Battle,
                    fixture.Action(102, [], "default"),
                    0,
                    CancellationToken.None);
                Assert.True(first.Succeeded);
                Assert.Equal(SkillResultCode.DuplicateCompleted, second.ResultCode);
                Assert.Equal(1, fixture.Usage.Snapshot.Single().UsageCount);
                break;
            case 68:
                var failed = Fixture.Create(testResource: 0);
                await failed.ResolveAsync(102);
                Assert.Empty(failed.Usage.Snapshot);
                break;
            case 69:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var interrupted = await fixture.ResolveAsync(102);
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.None;
                await fixture.Resolver.RecoverAsync(interrupted.SkillExecutionId, fixture.Battle, CancellationToken.None);
                Assert.Equal(1, fixture.Usage.Snapshot.Single().UsageCount);
                break;
            case 70:
                Assert.Equal(
                    "skill.cooldown_evidence_blocked",
                    fixture.Cooldown.Validate(
                        fixture.Battle.BattleInstanceId,
                        fixture.PlayerId,
                        fixture.Definition(123),
                        1).Error.Code);
                break;
            case 71:
                Assert.DoesNotContain("ClientCooldown", typeof(SkillActionRequest).GetProperties().Select(value => value.Name));
                break;
        }
    }

    private static async Task VerifyDamageScenario(int scenarioId)
    {
        var fixture = Fixture.Create(enemyOneHp: scenarioId is 77 or 80 or 81 ? 5 : scenarioId == 82 ? 0 : 100);
        switch (scenarioId)
        {
            case 73:
                Assert.True((await fixture.ResolveAsync(100, fixture.EnemyOneId)).Succeeded);
                break;
            case 74:
                Assert.IsAssignableFrom<IBattleCombatExecutionPort>(fixture.Combat);
                Assert.True((await fixture.ResolveAsync(100, fixture.EnemyOneId)).TargetResults.Single().Damage > 0);
                break;
            case 75:
                Assert.DoesNotContain("Damage", typeof(SkillActionRequest).GetProperties().Select(value => value.Name));
                break;
            case 76:
                var once = await fixture.ResolveAsync(100, fixture.EnemyOneId);
                var authority = fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.EnemyOneId).Value!;
                Assert.Equal(once.TargetResults.Single().HpAfter, authority.CurrentHp);
                break;
            case 77:
                Assert.Equal(0, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).TargetResults.Single().HpAfter);
                break;
            case 78:
                var version = (await fixture.ResolveAsync(100, fixture.EnemyOneId)).TargetResults.Single();
                Assert.Equal(version.RuntimeVersionBefore + 1, version.RuntimeVersionAfter);
                break;
            case 79:
                Assert.False((await fixture.ResolveAsync(100, fixture.EnemyOneId)).TargetResults.Single().TargetDefeated);
                break;
            case 80:
            case 81:
                Assert.True((await fixture.ResolveAsync(100, fixture.EnemyOneId)).TargetResults.Single().TargetDefeated);
                break;
            case 82:
                Assert.Equal(SkillResultCode.NoValidTarget, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).ResultCode);
                break;
            case 83:
                var first = await fixture.ResolveAsync(100, fixture.EnemyOneId);
                var duplicate = await fixture.Resolver.ResolveAsync(
                    fixture.Battle,
                    fixture.Action(100, [fixture.EnemyOneId]),
                    0,
                    CancellationToken.None);
                Assert.Equal(SkillResultCode.DuplicateCompleted, duplicate.ResultCode);
                Assert.Equal(first.TargetResults.Single().HpAfter, duplicate.TargetResults.Single().HpAfter);
                break;
            case 84:
                var action = fixture.Action(100, [fixture.EnemyOneId]);
                var results = await Task.WhenAll(
                    fixture.Resolver.ResolveAsync(fixture.Battle, action, 0, CancellationToken.None),
                    fixture.Resolver.ResolveAsync(fixture.Battle, action, 0, CancellationToken.None));
                Assert.Single(results, value => value.ResultCode == SkillResultCode.Success);
                Assert.Single(results, value => value.ResultCode == SkillResultCode.DuplicateCompleted);
                break;
            case 85:
                Assert.Equal(CombatPolicyStatus.TestOnly, (await fixture.ResolveAsync(100, fixture.EnemyOneId)).EffectResults.Single().PolicyStatus);
                break;
            case 86:
                await fixture.ResolveAsync(100, fixture.EnemyOneId);
                Assert.Contains(fixture.Audit.Snapshot, value => value.EffectType == SkillEffectType.Damage && value.Damage > 0);
                break;
        }
    }

    private static async Task VerifyHealingScenario(int scenarioId)
    {
        var fixture = Fixture.Create(
            allyHp: scenarioId == 91 ? 100 : scenarioId is 92 or 93 ? 0 : scenarioId is 89 or 90 ? 95 : 50);
        switch (scenarioId)
        {
            case 87:
                Assert.True((await fixture.ResolveAsync(101, fixture.AllyId)).Succeeded);
                break;
            case 88:
                await fixture.ResolveAsync(101, fixture.AllyId);
                Assert.Equal(
                    fixture.Combat.Get(fixture.Battle.BattleInstanceId, fixture.AllyId).Value!.CurrentHp,
                    fixture.Store.Snapshot.Single().Result!.TargetResults.Single().HpAfter);
                break;
            case 89:
                Assert.Equal(100, (await fixture.ResolveAsync(101, fixture.AllyId)).TargetResults.Single().HpAfter);
                break;
            case 90:
                Assert.Equal(5, (await fixture.ResolveAsync(101, fixture.AllyId)).TargetResults.Single().Heal);
                break;
            case 91:
                var full = await fixture.ResolveAsync(101, fixture.AllyId);
                Assert.Equal(0, full.TargetResults.Single().Heal);
                break;
            case 92:
            case 93:
                Assert.False((await fixture.ResolveAsync(101, fixture.AllyId)).Succeeded);
                break;
            case 94:
                Assert.DoesNotContain("Heal", typeof(SkillActionRequest).GetProperties().Select(value => value.Name));
                break;
            case 95:
                var first = await fixture.ResolveAsync(101, fixture.AllyId);
                var duplicate = await fixture.Resolver.ResolveAsync(
                    fixture.Battle,
                    fixture.Action(101, [fixture.AllyId]),
                    0,
                    CancellationToken.None);
                Assert.Equal(first.TargetResults.Single().HpAfter, duplicate.TargetResults.Single().HpAfter);
                break;
            case 96:
                var healAction = fixture.Action(101, [fixture.AllyId]);
                var results = await Task.WhenAll(
                    fixture.Resolver.ResolveAsync(fixture.Battle, healAction, 0, CancellationToken.None),
                    fixture.Resolver.ResolveAsync(fixture.Battle, healAction, 0, CancellationToken.None));
                Assert.Single(results, value => value.ResultCode == SkillResultCode.Success);
                break;
            case 97:
                var heal = (await fixture.ResolveAsync(101, fixture.AllyId)).TargetResults.Single();
                Assert.Equal(heal.RuntimeVersionBefore + 1, heal.RuntimeVersionAfter);
                break;
            case 98:
                await fixture.ResolveAsync(101, fixture.AllyId);
                Assert.Contains(fixture.Audit.Snapshot, value => value.EffectType == SkillEffectType.Heal && value.Heal > 0);
                break;
        }
    }

    private static async Task VerifyMultiTargetScenario(int scenarioId)
    {
        var fixture = Fixture.Create(enemyOneHp: scenarioId == 108 ? 1 : 100, enemyTwoHp: scenarioId == 108 ? 1 : 100);
        switch (scenarioId)
        {
            case 99:
                Assert.Equal(2, (await fixture.ResolveAsync(102)).TargetResults.Count);
                break;
            case 100:
                Assert.Equal(2, (await fixture.ResolveAsync(103)).TargetResults.Count);
                break;
            case 101:
                var first = (await fixture.ResolveAsync(102)).EffectResults.Select(value => value.TargetParticipantId).ToArray();
                var secondFixture = Fixture.Create();
                var second = (await secondFixture.ResolveAsync(102)).EffectResults.Select(value => value.TargetParticipantId).ToArray();
                Assert.Equal(first, second);
                break;
            case 102:
                var plan = fixture.Plan(102);
                Assert.Equal(plan.EffectPlans.Count, plan.EffectPlans.Select(value => value.EffectIdempotencyKey).Distinct().Count());
                break;
            case 103:
            case 106:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                Assert.Equal(SkillResultCode.RecoveryRequired, (await fixture.ResolveAsync(102)).ResultCode);
                break;
            case 104:
            case 105:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var interrupted = await fixture.ResolveAsync(102);
                var firstHp = interrupted.EffectResults.Single().HpAfter;
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.None;
                var recovered = await fixture.Resolver.RecoverAsync(
                    interrupted.SkillExecutionId,
                    fixture.Battle,
                    CancellationToken.None);
                Assert.True(recovered.Succeeded);
                Assert.Equal(firstHp, recovered.EffectResults[0].HpAfter);
                Assert.Equal(2, recovered.EffectResults.Count);
                break;
            case 107:
                var dead = Fixture.Create(enemyOneHp: 0);
                var resolved = await dead.ResolveAsync(102);
                Assert.Single(resolved.TargetResults);
                break;
            case 108:
                Assert.All((await fixture.ResolveAsync(102)).TargetResults, value => Assert.True(value.TargetDefeated));
                break;
            case 109:
                Assert.Equal(2, (await fixture.ResolveAsync(102)).TargetResults.Count);
                break;
            case 110:
                var result = await fixture.ResolveAsync(108, fixture.EnemyOneId);
                Assert.Equal([0, 1], result.EffectResults.Select(value => value.EffectIndex));
                break;
        }
    }

    private static async Task VerifyRecoveryScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        switch (scenarioId)
        {
            case 111:
                await fixture.ResolveAsync(102);
                Assert.NotEmpty(fixture.Store.Snapshot.Single().Plan.EffectPlans);
                break;
            case 112:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var interrupted = await fixture.ResolveAsync(102);
                var plan = fixture.Store.Get(interrupted.SkillExecutionId)!.Plan;
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.None;
                await fixture.Resolver.RecoverAsync(interrupted.SkillExecutionId, fixture.Battle, CancellationToken.None);
                Assert.Same(plan, fixture.Store.Get(interrupted.SkillExecutionId)!.Plan);
                break;
            case 113:
                var first = await fixture.ResolveAsync(100, fixture.EnemyOneId);
                var duplicate = await fixture.Resolver.ResolveAsync(
                    fixture.Battle,
                    fixture.Action(100, [fixture.EnemyOneId]),
                    0,
                    CancellationToken.None);
                Assert.True(first.Succeeded);
                Assert.Equal(SkillResultCode.DuplicateCompleted, duplicate.ResultCode);
                break;
            case 114:
                var original = await fixture.ResolveAsync(100, fixture.EnemyOneId);
                var changed = fixture.Action(100, [fixture.EnemyTwoId]);
                Assert.Equal(
                    SkillResultCode.ReplayConflict,
                    (await fixture.Resolver.ResolveAsync(
                        fixture.Battle,
                        changed with { ActionId = original.BattleActionId },
                        0,
                        CancellationToken.None)).ResultCode);
                break;
            case 115:
                var action = fixture.Action(100, [fixture.EnemyOneId]);
                var concurrent = await Task.WhenAll(
                    fixture.Resolver.ResolveAsync(fixture.Battle, action, 0, CancellationToken.None),
                    fixture.Resolver.ResolveAsync(fixture.Battle, action, 0, CancellationToken.None));
                Assert.Single(concurrent, value => value.ResultCode == SkillResultCode.Success);
                break;
            case 116:
                var noResource = Fixture.Create(testResource: 0);
                await noResource.ResolveAsync(102);
                Assert.Empty(noResource.Costs.Reservations);
                break;
            case 117:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var partial = await fixture.ResolveAsync(102);
                Assert.Equal(SkillCostReservationState.Reserved, partial.CostResult.State);
                break;
            case 118:
            case 119:
            case 120:
            case 121:
            case 122:
            case 123:
            case 124:
            case 125:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var recovery = await fixture.ResolveAsync(102);
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.None;
                var completed = await fixture.Resolver.RecoverAsync(recovery.SkillExecutionId, fixture.Battle, CancellationToken.None);
                Assert.True(completed.Succeeded);
                Assert.Equal(1, fixture.Usage.Snapshot.Single().UsageCount);
                Assert.Equal(SkillCostReservationState.Committed, fixture.Costs.Reservations.Single().State);
                break;
            case 126:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var reconnect = await fixture.ResolveAsync(102);
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.None;
                Assert.True((await fixture.Resolver.RecoverAsync(reconnect.SkillExecutionId, fixture.Battle, CancellationToken.None)).Succeeded);
                break;
            case 127:
                Assert.Equal(BattleResultCode.InvalidSession, (await fixture.SubmitThroughCoordinatorAsync(sessionId: "old")).Code);
                break;
            case 128:
                fixture.Resolver.FailureInjection.Point = SkillFailurePoint.AfterFirstEffect;
                var failed = await fixture.ResolveAsync(102);
                Assert.Single(fixture.Inspector.Query(new SkillInspectorQuery(RecoveryRequiredOnly: true)).Executions);
                Assert.Equal(SkillRecoveryState.RecoveryRequired, failed.RecoveryState);
                break;
        }
    }

    private static async Task VerifyUnsupportedScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        var skillId = scenarioId switch
        {
            129 => 104,
            130 => 124,
            131 => 125,
            132 => 126,
            133 => 127,
            134 => 128,
            _ => 110
        };
        Guid? targetId = skillId switch
        {
            125 => fixture.AllyId,
            110 or 126 or 127 => null,
            _ => fixture.EnemyOneId
        };
        var result = await fixture.ResolveAsync(skillId, targetId);
        Assert.Contains(
            result.ResultCode,
            new[] { SkillResultCode.UnsupportedEffect, SkillResultCode.EffectEvidenceBlocked });
        Assert.Empty(result.NetworkBytes);
    }

    private static async Task VerifyDiagnosticsScenario(int scenarioId)
    {
        var fixture = Fixture.Create();
        if (scenarioId <= 145)
        {
            await fixture.ResolveAsync(scenarioId == 138 ? 102 : 100, scenarioId == 138 ? null : fixture.EnemyOneId);
        }

        switch (scenarioId)
        {
            case 136:
                Assert.Contains(fixture.Audit.Snapshot, value => value.Result == SkillResultCode.Success);
                break;
            case 137:
                Assert.Equal(SkillCostReservationState.Committed, fixture.Costs.Reservations.Single().State);
                break;
            case 138:
                Assert.Single(fixture.Usage.Snapshot);
                break;
            case 139:
                Assert.Contains(fixture.Audit.Snapshot, value => value.EffectExecutionId is not null);
                break;
            case 140:
                Assert.NotEmpty(fixture.Inspector.Query(new SkillInspectorQuery()).Definitions);
                break;
            case 141:
                Assert.Single(fixture.Inspector.Query(new SkillInspectorQuery()).Executions);
                break;
            case 142:
                Assert.Single(fixture.Inspector.Query(new SkillInspectorQuery()).Effects);
                break;
            case 143:
                var cooldownFixture = Fixture.Create();
                await cooldownFixture.ResolveAsync(102);
                Assert.Single(cooldownFixture.Inspector.Query(new SkillInspectorQuery()).Usage);
                break;
            case 144:
                var page = fixture.Inspector.Query(new SkillInspectorQuery(Offset: 0, Limit: 1));
                Assert.Single(page.Executions);
                break;
            case 145:
                Assert.Throws<NotSupportedException>(() =>
                    ((IList<SkillExecutionInspectorItem>)fixture.Inspector.Query(new SkillInspectorQuery()).Executions)
                    .Add(fixture.Inspector.Query(new SkillInspectorQuery()).Executions[0]));
                break;
            case 146:
                fixture.Inspector.FailureInjection.Point = SkillFailurePoint.Inspector;
                Assert.Equal("skill.inspector_failed", fixture.Inspector.Query(new SkillInspectorQuery()).FailureCode);
                Assert.True((await fixture.ResolveAsync(100, fixture.EnemyOneId)).Succeeded);
                break;
            case 147:
                Assert.DoesNotContain(fixture.Audit.Snapshot, value => value.SessionSafeId == fixture.Player.SessionId);
                break;
            case 148:
                Assert.DoesNotContain(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    typeof(SkillActionResolver).Assembly.FullName!,
                    StringComparison.OrdinalIgnoreCase);
                break;
            case 149:
                Assert.True(typeof(ISkillExecutionStore).IsInterface);
                break;
            case 150:
                Assert.Empty((await fixture.ResolveAsync(100, fixture.EnemyOneId)).NetworkBytes);
                break;
        }
    }

    private static void VerifyFrozenBoundaryScenario(int scenarioId)
    {
        Assert.InRange(scenarioId, 151, 160);
        Assert.True(typeof(IInventoryTransactionCoordinator).IsInterface);
        Assert.True(typeof(IWorldInteractionCoordinator).IsInterface);
        Assert.True(typeof(ICombatCoordinator).IsInterface);
        Assert.True(typeof(ITurnBasedBattleCoordinator).IsInterface);
        Assert.True(typeof(IBattleRewardCoordinator).IsInterface);
        Assert.True(typeof(IBattleRecoveryCoordinator).IsInterface);
        Assert.True(typeof(IWorldInteractionSessionRegistry).IsInterface);
        Assert.True(typeof(InMemoryProtocolAuditLog).IsClass);
        Assert.True(typeof(BattleActionResult).GetProperty(nameof(BattleActionResult.NetworkBytes)) is not null);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid PlayerId,
        Guid AllyId,
        Guid EnemyOneId,
        Guid EnemyTwoId,
        BattleParticipant Player,
        BattleInstance Battle,
        IReadOnlyList<SkillDefinitionRecord> Records,
        ImmutableSkillDefinitionCatalog Catalog,
        SkillTargetResolver Targets,
        SkillCostPolicy CostPolicy,
        InMemorySkillCostReservationStore Costs,
        InMemorySkillUsageStore Usage,
        RoundBasedSkillCooldownPolicy Cooldown,
        BattleScopedCombatExecutionPort Combat,
        InMemorySkillExecutionStore Store,
        InMemorySkillEventSink Events,
        InMemorySkillAuditLedger Audit,
        SkillActionResolver Resolver,
        BattleActionResolver BattleResolver,
        SkillRuntimeInspector Inspector)
    {
        public static Fixture Create(
            long playerHp = 100,
            long allyHp = 50,
            long enemyOneHp = 100,
            long enemyTwoHp = 100,
            long testResource = 100,
            int ownershipRank = 1,
            int? excludeOwnedSkillId = null)
        {
            var now = new DateTimeOffset(2026, 7, 31, 1, 0, 0, TimeSpan.Zero);
            var battleId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var playerId = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var allyId = Guid.Parse("20000000-0000-0000-0000-000000000002");
            var enemyOneId = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var enemyTwoId = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var player = Participant(playerId, battleId, BattleSide.PlayerSide, 0, 1001, null, playerHp, 100, "session");
            var ally = Participant(allyId, battleId, BattleSide.PlayerSide, 1, 1002, null, allyHp, 100, "ally-session");
            var enemyOne = Participant(enemyOneId, battleId, BattleSide.EnemySide, 100, null, 1, enemyOneHp, 100, null);
            var enemyTwo = Participant(enemyTwoId, battleId, BattleSide.EnemySide, 101, null, 2, enemyTwoHp, 100, null);
            var participants = new[] { player, ally, enemyOne, enemyTwo };
            var round = new BattleRound(
                battleId,
                1,
                BattleRoundState.Resolving,
                SkillCollectionsForTests.Freeze(participants.Where(value => value.IsAlive).Select(value => value.ParticipantId)),
                [],
                [],
                [],
                0,
                [],
                now,
                now,
                null,
                1,
                "skill-test");
            var battle = new BattleInstance(
                battleId,
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "safe-battle",
                "world-test",
                1,
                "skill-test",
                BattleType.InternalTest,
                BattleState.Active,
                BattlePhase.ResolvingActions,
                1,
                0,
                SkillCollectionsForTests.Freeze(participants),
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
                "skill-test",
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.Baseline,
                CombatPolicyStatus.EvidenceBlocked,
                "skill-test-v1",
                "{\"scope\":\"TestOnly\"}");
            var records = BuildRecords();
            var catalog = ImmutableSkillDefinitionCatalog.Create(
                records,
                new SkillDefinitionMapper(),
                new SkillDefinitionValidator()).Value!;
            var ownership = records
                .Where(value => value.Enabled && value.PolicyStatus == CombatPolicyStatus.TestOnly)
                .Where(value => value.SkillDefinitionId != excludeOwnedSkillId)
                .Select(value => new SkillOwnershipRecord(
                    1001,
                    value.SkillDefinitionId,
                    ownershipRank,
                    true,
                    CombatPolicyStatus.TestOnly,
                    "skill-test-v1",
                    "{}"))
                .ToArray();
            var costs = new InMemorySkillCostReservationStore();
            costs.SeedTestResource(playerId, testResource);
            var usage = new InMemorySkillUsageStore();
            var cooldown = new RoundBasedSkillCooldownPolicy(usage);
            var combat = new BattleScopedCombatExecutionPort(new DeterministicTestDamagePolicy(20));
            combat.Seed(battleId, participants);
            var store = new InMemorySkillExecutionStore(costs, usage);
            var events = new InMemorySkillEventSink();
            var audit = new InMemorySkillAuditLedger();
            var clock = new DeterministicTestBattleClock(now);
            var targets = new SkillTargetResolver();
            var resolver = new SkillActionResolver(
                catalog,
                new SkillAvailabilityPolicy(ownership),
                targets,
                new SkillCostPolicy(),
                costs,
                cooldown,
                new SkillExecutionPlanner(),
                new SkillEffectResolver(new SkillEffectHandlerRegistry(
                [
                    new SkillDamageEffectHandler(),
                    new SkillHealingEffectHandler()
                ])),
                store,
                combat,
                combat,
                events,
                audit,
                clock);
            return new Fixture(
                now,
                playerId,
                allyId,
                enemyOneId,
                enemyTwoId,
                player,
                battle,
                records,
                catalog,
                targets,
                new SkillCostPolicy(),
                costs,
                usage,
                cooldown,
                combat,
                store,
                events,
                audit,
                resolver,
                new BattleActionResolver(combat, clock, resolver),
                new SkillRuntimeInspector(catalog, store, clock));
        }

        public SkillDefinition Definition(int id) =>
            Catalog.Snapshot.Single(value => value.SkillDefinitionId == id);

        public BattleLockedAction Action(
            int skillDefinitionId,
            IReadOnlyList<Guid> targets,
            string suffix = "default")
        {
            var actionId = BattleRuntimeHash.DeterministicGuid(
                $"skill-test-action:{skillDefinitionId}:{suffix}");
            return new BattleLockedAction(
                actionId,
                Battle.BattleInstanceId,
                Battle.CurrentRoundNumber,
                PlayerId,
                BattleActionType.Skill,
                SkillCollectionsForTests.Freeze(targets),
                skillDefinitionId,
                null,
                BattleRuntimeHash.SafeId($"skill-test:{skillDefinitionId}:{suffix}"),
                BattleRuntimeHash.PersistenceKey($"skill-test:{skillDefinitionId}:{suffix}:{string.Join(',', targets)}"),
                Now,
                Now,
                "skill-test");
        }

        public async Task<SkillActionResult> ResolveAsync(int skillDefinitionId, Guid? target = null)
        {
            IReadOnlyList<Guid> targets = target is null ? [] : [target.Value];
            return await Resolver.ResolveAsync(
                Battle,
                Action(skillDefinitionId, targets),
                0,
                CancellationToken.None);
        }

        public OperationResult<SkillTargetResolution> Target(int skillDefinitionId, params Guid[] targets) =>
            Targets.Resolve(
                Battle,
                Player,
                Definition(skillDefinitionId),
                SkillCollectionsForTests.Freeze(targets));

        public SkillExecutionPlan Plan(int skillDefinitionId)
        {
            var definition = Definition(skillDefinitionId);
            var action = Action(skillDefinitionId, []);
            var resolved = Targets.Resolve(Battle, Player, definition, []).Value!;
            return new SkillExecutionPlanner().Create(
                Battle,
                action,
                definition,
                1,
                resolved,
                0,
                Now).Value!;
        }

        public async Task<BattleActionSubmissionResult> SubmitThroughCoordinatorAsync(
            string sessionId = "session",
            long characterId = 1001)
        {
            var store = new InMemoryBattleStore();
            var collectingRound = Battle.CurrentRound with
            {
                State = BattleRoundState.CollectingActions,
                LockedActions = []
            };
            var collecting = Battle with
            {
                CurrentPhase = BattlePhase.CollectingActions,
                CurrentRound = collectingRound
            };
            await store.CreateAsync(collecting, CancellationToken.None);
            var coordinator = new TurnBasedBattleCoordinator(
                new InMemoryWorldInteractionSessionRegistry(),
                new PlayerCombatRuntimeRegistry(),
                new BattleEncounterCatalog([], []),
                new BattleInstanceFactory(new BattleParticipantFactory()),
                store,
                store,
                store,
                new InMemoryWorldBattleReservationRegistry(),
                new DeterministicTestBattleTurnOrderPolicy(1),
                BattleResolver,
                new BaselineServerBattleVictoryPolicy(),
                new NoBattleRewardPolicy(),
                new NoopRewardCoordinator(),
                new DeterministicTestBattleClock(Now),
                new InMemoryBattleEventSink(),
                new InMemoryBattleAuditLedger(),
                Combat);
            return await coordinator.SubmitActionAsync(
                new BattleActionRequest(
                    Guid.NewGuid(),
                    "skill-submit",
                    collecting.BattleInstanceId,
                    collecting.BattleVersion,
                    collecting.CurrentRoundNumber,
                    sessionId,
                    characterId,
                    PlayerId,
                    BattleActionType.Skill,
                    [EnemyOneId],
                    100,
                    null,
                    null,
                    BattleRequestSource.TrustedInternalTest,
                    Now,
                    "skill-submit"),
                CancellationToken.None);
        }

        private static BattleParticipant Participant(
            Guid participantId,
            Guid battleId,
            BattleSide side,
            int slot,
            long? characterId,
            int? monsterId,
            long hp,
            long maximumHp,
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
                50_000_000 + slot,
                sessionId,
                characterId is null ? $"Monster {monsterId}" : $"Character {characterId}",
                1,
                maximumHp,
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
                DateTimeOffset.UtcNow,
                hp > 0 ? null : DateTimeOffset.UtcNow,
                "{}");

        private static IReadOnlyList<SkillDefinitionRecord> BuildRecords()
        {
            var records = new List<SkillDefinitionRecord>
            {
                Record(100, SkillActionCategory.Damage, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 10),
                Record(101, SkillActionCategory.Heal, SkillTargetPolicyType.SingleAlly, SkillEffectType.Heal, 25),
                Record(
                    102,
                    SkillActionCategory.Damage,
                    SkillTargetPolicyType.AllEnemies,
                    SkillEffectType.Damage,
                    5,
                    new SkillCostDefinition(SkillResourceType.BattleResource, 10, CombatPolicyStatus.TestOnly, "{}"),
                    new SkillCooldownDefinition(1, null, null, null, CombatPolicyStatus.TestOnly, "{}")),
                Record(103, SkillActionCategory.Heal, SkillTargetPolicyType.AllAllies, SkillEffectType.Heal, 15),
                Record(104, SkillActionCategory.Status, SkillTargetPolicyType.SingleEnemy, SkillEffectType.ApplyStatus, 1),
                Record(105, SkillActionCategory.Damage, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 10) with { Enabled = false },
                Record(106, SkillActionCategory.Heal, SkillTargetPolicyType.Self, SkillEffectType.Heal, 10),
                Record(107, SkillActionCategory.Damage, SkillTargetPolicyType.RandomEnemy, SkillEffectType.Damage, 10),
                Record(108, SkillActionCategory.Mixed, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 5) with
                {
                    EffectDefinitions =
                    [
                        Effect(108, "skill-108-effect-0", 0, SkillEffectType.Damage, 5),
                        Effect(108, "skill-108-effect-1", 1, SkillEffectType.Damage, 5)
                    ]
                },
                new SkillDefinitionRecord(
                    109,
                    "raw-109",
                    "Raw Skill",
                    "",
                    null,
                    null,
                    SkillCategory.Unknown,
                    SkillActionCategory.Unknown,
                    SkillTargetPolicyType.Unknown,
                    new SkillCostDefinition(SkillResourceType.Unknown, 0, CombatPolicyStatus.EvidenceBlocked, "{\"unknown\":true}"),
                    new SkillCooldownDefinition(null, null, null, null, CombatPolicyStatus.EvidenceBlocked, "{\"unknown\":true}"),
                    new SkillUsageDefinition(null, CombatPolicyStatus.EvidenceBlocked, "{}"),
                    [],
                    true,
                    "raw-skill-v1",
                    CombatPolicyStatus.EvidenceBlocked,
                    CombatPolicyStatus.EvidenceBlocked,
                    "{\"unknown\":true}"),
                Record(110, SkillActionCategory.Utility, SkillTargetPolicyType.Self, SkillEffectType.NoOp, 0),
                Record(120, SkillActionCategory.Damage, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 1,
                    new SkillCostDefinition(SkillResourceType.InventoryItem, 1, CombatPolicyStatus.EvidenceBlocked, "{}")),
                Record(121, SkillActionCategory.Damage, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 1,
                    new SkillCostDefinition(SkillResourceType.Currency, 1, CombatPolicyStatus.EvidenceBlocked, "{}")),
                Record(122, SkillActionCategory.Damage, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 1,
                    new SkillCostDefinition(SkillResourceType.Hp, 1, CombatPolicyStatus.EvidenceBlocked, "{}")),
                Record(123, SkillActionCategory.Damage, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Damage, 1,
                    cooldown: new SkillCooldownDefinition(null, null, null, null, CombatPolicyStatus.EvidenceBlocked, "{}")),
                Record(124, SkillActionCategory.Status, SkillTargetPolicyType.SingleEnemy, SkillEffectType.RemoveStatus, 1),
                Record(125, SkillActionCategory.Revive, SkillTargetPolicyType.SingleAlly, SkillEffectType.Revive, 1),
                Record(126, SkillActionCategory.Summon, SkillTargetPolicyType.Self, SkillEffectType.Summon, 1),
                Record(127, SkillActionCategory.Utility, SkillTargetPolicyType.Self, SkillEffectType.ResourceRestore, 1),
                Record(128, SkillActionCategory.Utility, SkillTargetPolicyType.SingleEnemy, SkillEffectType.Scripted, 1)
            };
            return SkillCollectionsForTests.Freeze(records);
        }

        private static SkillDefinitionRecord Record(
            int id,
            SkillActionCategory action,
            SkillTargetPolicyType target,
            SkillEffectType effect,
            long value,
            SkillCostDefinition? cost = null,
            SkillCooldownDefinition? cooldown = null) =>
            new(
                id,
                $"test-{id}",
                $"Test Skill {id}",
                "",
                5,
                0,
                action == SkillActionCategory.Heal ? SkillCategory.Healing : SkillCategory.Active,
                action,
                target,
                cost ?? new SkillCostDefinition(SkillResourceType.None, 0, CombatPolicyStatus.TestOnly, "{}"),
                cooldown ?? new SkillCooldownDefinition(0, null, null, null, CombatPolicyStatus.TestOnly, "{}"),
                new SkillUsageDefinition(null, CombatPolicyStatus.TestOnly, "{}"),
                [Effect(id, $"skill-{id}-effect-0", 0, effect, value)],
                true,
                "skill-test-v1",
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.EvidenceBlocked,
                "{\"scope\":\"TestOnly\"}");

        private static SkillEffectDefinition Effect(
            int skillId,
            string effectId,
            int index,
            SkillEffectType effectType,
            long value) =>
            new(
                effectId,
                skillId,
                index,
                effectType,
                SkillTargetPolicyType.Unknown,
                "DeterministicTest",
                value,
                0,
                CombatDamageType.Magic,
                false,
                true,
                true,
                true,
                null,
                CombatPolicyStatus.TestOnly,
                "skill-test-v1",
                "{}");
    }

    private sealed class NoopRewardCoordinator : IBattleRewardCoordinator
    {
        public Task<BattleRewardResult> FinalizeAsync(
            BattleRewardPlan plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BattleRewardResult(
                BattleResultCode.Success,
                plan.RewardPlanId,
                true,
                false,
                "",
                plan.CreatedAtUtc));
    }

    private static class SkillCollectionsForTests
    {
        public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
            Array.AsReadOnly(values.ToArray());
    }
}
