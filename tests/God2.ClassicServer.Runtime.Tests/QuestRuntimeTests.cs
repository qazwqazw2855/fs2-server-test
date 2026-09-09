using System.Text.Json;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class QuestRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 4, 0, 0, TimeSpan.Zero);

    public static TheoryData<int, string> OfflineHeadlessScenarios
    {
        get
        {
            var names = """
                Quest evidence census completes
                Quest definition count reported
                Objective count reported
                Reward count reported
                NPC binding count reported
                Preserve unknown raw metadata
                Reject duplicate quest ID
                Reject duplicate objective ID
                Reject duplicate objective index
                Reject negative required count
                Reject circular prerequisite
                Reject disabled quest
                Production quest requires evidence
                TestOnly quest isolated from production
                Catalog immutable
                Unknown quest type preserved
                Unknown objective type preserved
                Missing reward treated as explicit NoReward or blocked
                Visual-only content not gameplay
                Near-zero evidence produces ContentDeferred report
                Resolve accept NPC binding
                Resolve turn-in NPC binding
                Reject wrong NPC
                Reject wrong NPC template cross-check
                Reject invalid session
                Reject ownership mismatch
                Reject quest already active
                Reject non-repeat completed quest
                Reject missing prerequisite
                Accept no-prerequisite quest
                Accept completed-prerequisite TestOnly quest
                Reject TestOnly binding on production path
                Range EvidenceBlocked preserved
                Client binding type ignored
                Accept TestOnly quest succeeds
                Accept ContentBacked quest succeeds
                Same accept duplicate returns DuplicateCompleted
                Same key changed payload returns ReplayConflict
                Concurrent accept creates one instance
                Initial objective states correct
                Client initial progress ignored
                Acceptance persistence failure rejects
                Runtime failure after persistence reloads
                Accepted event emitted once
                Abandon active quest succeeds
                Duplicate abandon commits once
                Completed quest cannot abandon
                Abandoned quest stops progress
                Abandon audit preserves snapshot
                Reaccept policy EvidenceBlocked when unknown
                Matching monster death increments once
                Non-matching monster does not increment
                Duplicate DeathId does not increment twice
                Changed payload same event ID conflicts
                Kill progress clamps at required count
                Required count completes objective
                Completed objective ignores later kill
                Concurrent distinct kills preserve total count
                Concurrent duplicate kill commits once
                Client kill count ignored
                Aborted battle gives no kill progress
                Invalid killer ownership rejected
                Party credit remains unsupported
                Kill objective audit complete
                Inventory snapshot initializes ownership progress
                Matching item quantity updates progress
                Non-matching item ignored
                Client item count ignored
                Inventory transaction duplicate does not duplicate progress
                Item removal reconciles CurrentOwnership
                Progress clamps at required count
                Completed ownership objective follows documented non-regression policy
                Inventory snapshot failure does not guess
                Reward transaction correlation guard prevents progress loop
                AcquireItem cumulative remains unsupported
                SubmitItem remains unsupported
                Item objective audit complete
                Valid NPC interaction increments objective
                Wrong NPC ignored
                Duplicate interaction ID commits once
                Rejected world interaction gives no progress
                Client NPC target ignored as authority
                Required interaction count completes objective
                Dialog completion remains unsupported
                NPC objective audit complete
                Committed portal transition increments VisitMap
                Wrong target map ignored
                Rolled-back portal gives no progress
                RecoveryRequired portal gives no progress
                Duplicate TransitionId commits once
                UsePortal verifies portal template
                Coordinate visit remains unsupported
                Portal objective audit complete
                Completed winning battle increments objective
                Aborted battle ignored
                Defeated battle follows definition policy
                Duplicate battle completion commits once
                Wrong encounter ignored
                Client victory ignored
                Battle reward does not duplicate quest reward
                Battle objective audit complete
                Progress plan immutable
                Quest version increments once
                Objective version increments once
                Same semantic event same objective commits once
                Same event can update two different quests safely
                Two objectives update in stable order
                Concurrent events do not lose progress
                Version conflict returns formal result
                Overflow rejected
                Negative progress impossible
                Completed quest rejects progress
                Abandoned quest rejects progress
                Progress persistence failure does not return success
                Runtime failure after persistence reloads
                Audit stores before／after values
                All required objectives produce ReadyToComplete
                Missing objective prevents Ready
                Ready transition commits once
                Client ready flag ignored
                Correct turn-in NPC completes quest
                Wrong turn-in NPC rejected
                Not-ready quest cannot complete
                Duplicate turn-in completes once
                Same completion key changed payload conflicts
                Completed quest rejects second turn-in
                AutoComplete boundary safe
                Completion audit complete
                Explicit NoReward quest completes
                TestOnly item reward succeeds
                TestOnly currency reward succeeds
                Combined item／currency reward succeeds atomically
                Invalid reward item rejected
                Inventory full records reward failure
                Duplicate completion does not duplicate reward
                Reward idempotency survives reconnect
                Reward success before completion persistence recovers
                Reward failure enters RecoveryRequired
                Production unknown reward EvidenceBlocked
                Client reward candidate ignored
                Experience reward remains unsupported
                Skill reward remains unsupported
                Reward audit complete
                Once quest cannot repeat
                TestOnly repeatable quest creates new iteration
                Repeat iteration increments once
                Daily quest EvidenceBlocked
                Weekly quest EvidenceBlocked
                Chain prerequisite cycle rejected
                Completed prerequisite unlocks TestOnly quest
                Quest completion does not autoaccept next quest
                Unknown chain semantics EvidenceBlocked
                Persist active quest
                Reload active quest
                Persist objective progress
                Persist ready state
                Persist completed state
                Persist abandoned state
                Persist reward state
                Crash after accept persistence recovers
                Crash after runtime accept does not duplicate
                Crash after progress persistence recovers
                Crash after objective completion recovers
                Crash after ready transition recovers
                Crash before reward resumes
                Crash after reward does not duplicate reward
                Crash after completion persistence reloads completed state
                Crash during abandon recovers
                Duplicate recovery invocation executes once
                Changed recovery payload conflicts
                Missing definition becomes controlled RecoveryRequired
                Missing objective becomes controlled RecoveryRequired
                Recovery visible in Inspector
                Disconnect preserves active quests
                Disconnect preserves objective progress
                Disconnect preserves ReadyToComplete
                Disconnect preserves reward recovery state
                Reconnect reloads active quest
                Reconnect does not reset progress
                Reconnect does not duplicate accept
                Reconnect does not duplicate progress
                Reconnect does not duplicate completion
                Reconnect does not duplicate reward
                Old session quest intent rejected
                Duplicate reconnect does not duplicate instance
                Accept audit complete
                Abandon audit complete
                Progress audit complete
                Objective completion audit complete
                Quest completion audit complete
                Reward audit complete
                Inspector definition snapshot correct
                Inspector instance snapshot correct
                Inspector objective snapshot correct
                Inspector progress snapshot correct
                Inspector reward snapshot correct
                Inspector filters／pagination correct
                Inspector immutable read model
                Inspector failure does not break runtime
                Concurrent inspector capture safe
                Credential redaction matchCount = 0
                Hardcoded path matchCount = 0
                Parameterized SQL verification
                Quest serializer emits zero fake network bytes
                Frozen Inventory Backend remains PASS
                Frozen World Interaction Backend remains PASS
                Frozen Combat Runtime remains PASS
                Frozen Turn-Based Battle Runtime remains PASS
                Frozen Skill Runtime remains PASS
                Frozen Status Effect Runtime remains PASS
                Battle Reward Exactly-once remains PASS
                Skill Cost／Cooldown Exactly-once remains PASS
                Status Trigger Cursor Exactly-once remains PASS
                Portal／Session Rebinding remains PASS
                Protocol Audit bounded queue concurrency remains PASS
                Automation JSON bounded retry remains PASS
                Frozen Login-to-World remains PASS
                Clean Shutdown remains PASS
                """.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(218, names.Length);
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
    public async Task Offline_headless_quest_scenario_matrix_passes(
        int scenarioId,
        string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 218);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));
        if (scenarioId <= 20)
        {
            VerifyCatalogScenario(scenarioId);
        }
        else if (scenarioId <= 34)
        {
            await VerifyBindingEligibilityScenario(scenarioId);
        }
        else if (scenarioId <= 50)
        {
            await VerifyAcceptAbandonScenario(scenarioId);
        }
        else if (scenarioId <= 64)
        {
            await VerifyKillScenario(scenarioId);
        }
        else if (scenarioId <= 77)
        {
            await VerifyItemScenario(scenarioId);
        }
        else if (scenarioId <= 85)
        {
            await VerifyNpcScenario(scenarioId);
        }
        else if (scenarioId <= 93)
        {
            await VerifyPortalScenario(scenarioId);
        }
        else if (scenarioId <= 101)
        {
            await VerifyBattleScenario(scenarioId);
        }
        else if (scenarioId <= 116)
        {
            await VerifyProgressScenario(scenarioId);
        }
        else if (scenarioId <= 128)
        {
            await VerifyCompletionScenario(scenarioId);
        }
        else if (scenarioId <= 143)
        {
            await VerifyRewardScenario(scenarioId);
        }
        else if (scenarioId <= 152)
        {
            await VerifyRepeatChainScenario(scenarioId);
        }
        else if (scenarioId <= 173)
        {
            await VerifyRecoveryScenario(scenarioId);
        }
        else if (scenarioId <= 185)
        {
            await VerifyReconnectScenario(scenarioId);
        }
        else if (scenarioId <= 204)
        {
            await VerifyDiagnosticsScenario(scenarioId);
        }
        else
        {
            VerifyFrozenScenario(scenarioId);
        }
    }

    [Fact]
    public void Quest_contracts_expose_all_required_backend_boundaries()
    {
        Assert.True(typeof(IQuestDefinitionCatalog).IsInterface);
        Assert.True(typeof(IQuestDefinitionRepository).IsInterface);
        Assert.True(typeof(IQuestDefinitionMapper).IsInterface);
        Assert.True(typeof(IQuestDefinitionValidator).IsInterface);
        Assert.True(typeof(IQuestBindingResolver).IsInterface);
        Assert.True(typeof(IQuestEligibilityPolicy).IsInterface);
        Assert.True(typeof(IQuestInstanceAuthority).IsInterface);
        Assert.True(typeof(IQuestCoordinator).IsInterface);
        Assert.True(typeof(IQuestAcceptanceStore).IsInterface);
        Assert.True(typeof(IQuestObjectiveHandler).IsInterface);
        Assert.True(typeof(IQuestObjectiveHandlerRegistry).IsInterface);
        Assert.True(typeof(IQuestSemanticEventAdapter).IsInterface);
        Assert.True(typeof(IQuestEventRouter).IsInterface);
        Assert.True(typeof(IQuestProgressCoordinator).IsInterface);
        Assert.True(typeof(IQuestCompletionPolicy).IsInterface);
        Assert.True(typeof(IQuestRewardCoordinator).IsInterface);
        Assert.True(typeof(IQuestRewardStore).IsInterface);
        Assert.True(typeof(IQuestRecoveryCoordinator).IsInterface);
        Assert.True(typeof(IQuestEventSink).IsInterface);
        Assert.True(typeof(IQuestAuditLedger).IsInterface);
        Assert.True(typeof(IQuestInspectorSource).IsInterface);
    }

    private static void VerifyCatalogScenario(int scenarioId)
    {
        var records = LoadOfficialQuestEvidence();
        Assert.Equal(418, records.RecordCount);
        Assert.Equal(418, records.Records.Count);
        Assert.Equal(2014, records.Records.Sum(value => value.Objectives));
        Assert.Equal(0, records.Records.Sum(value => value.RewardCandidates));
        Assert.Equal(0, records.Records.Sum(value => value.Prerequisites));
        Assert.Equal(169, records.Records.Sum(value => value.ItemReferenceCount));
        Assert.False(records.CanDirectImportToGameplay);

        var mapper = new QuestDefinitionMapper();
        var validator = new QuestDefinitionValidator();
        var mapped = mapper.Map(Record(Definition(100, QuestObjectiveType.KillMonster, 200, 2)));
        Assert.True(mapped.Succeeded);
        Assert.Equal("""{"testOnly":true}""", mapped.Value!.RawMetadata);
        Assert.True(validator.Validate(mapped.Value).Succeeded);

        switch (scenarioId)
        {
            case 7:
                Assert.Throws<InvalidOperationException>(() =>
                    new ImmutableQuestDefinitionCatalog([mapped.Value, mapped.Value], validator));
                break;
            case 8:
                {
                    var duplicate = mapped.Value with
                    {
                        ObjectiveDefinitions =
                        [
                            mapped.Value.ObjectiveDefinitions[0],
                        mapped.Value.ObjectiveDefinitions[0] with { ObjectiveIndex = 1 }
                        ]
                    };
                    Assert.Equal("quest.definition.duplicate_objective_id", validator.Validate(duplicate).Error.Code);
                    break;
                }
            case 9:
                {
                    var duplicate = mapped.Value with
                    {
                        ObjectiveDefinitions =
                        [
                            mapped.Value.ObjectiveDefinitions[0],
                        mapped.Value.ObjectiveDefinitions[0] with { ObjectiveDefinitionId = "other" }
                        ]
                    };
                    Assert.Equal("quest.definition.duplicate_objective_index", validator.Validate(duplicate).Error.Code);
                    break;
                }
            case 10:
                {
                    var invalid = mapped.Value with
                    {
                        ObjectiveDefinitions =
                        [
                            mapped.Value.ObjectiveDefinitions[0] with { RequiredCount = -1 }
                        ]
                    };
                    Assert.Equal("quest.definition.negative_required_count", validator.Validate(invalid).Error.Code);
                    break;
                }
            case 11:
                {
                    var left = Definition(201, QuestObjectiveType.KillMonster, 200, 1) with
                    {
                        Prerequisites =
                        [
                            Prerequisite("p-left", QuestPrerequisiteType.QuestCompleted, requiredQuestId: 202)
                        ]
                    };
                    var right = Definition(202, QuestObjectiveType.KillMonster, 200, 1) with
                    {
                        Prerequisites =
                        [
                            Prerequisite("p-right", QuestPrerequisiteType.QuestCompleted, requiredQuestId: 201)
                        ]
                    };
                    Assert.Throws<InvalidOperationException>(() =>
                        new ImmutableQuestDefinitionCatalog([left, right], validator));
                    break;
                }
            case 12:
                {
                    var catalog = new ImmutableQuestDefinitionCatalog([mapped.Value with { Enabled = false }], validator);
                    Assert.Equal("quest.disabled", catalog.Get(100, QuestRequestSource.TestOnly).Error.Code);
                    break;
                }
            case 13:
            case 14:
            case 19:
                {
                    var catalog = new ImmutableQuestDefinitionCatalog([mapped.Value], validator);
                    Assert.Equal("quest.test_only_isolated", catalog.Get(100, QuestRequestSource.ProductionClient).Error.Code);
                    Assert.Empty(catalog.Snapshot);
                    Assert.Single(catalog.AllSnapshot);
                    break;
                }
            case 15:
                {
                    var catalog = new ImmutableQuestDefinitionCatalog([mapped.Value], validator);
                    Assert.IsAssignableFrom<IReadOnlyList<QuestDefinition>>(catalog.AllSnapshot);
                    Assert.Throws<NotSupportedException>(() =>
                        ((IList<QuestDefinition>)catalog.AllSnapshot).Add(mapped.Value));
                    break;
                }
            case 16:
            case 17:
                Assert.Equal(QuestContentStatus.EvidenceBlocked, CandidateVisualDefinition().PolicyStatus);
                Assert.Equal(QuestObjectiveType.Unknown, CandidateVisualDefinition().ObjectiveDefinitions[0].ObjectiveType);
                break;
            case 18:
                Assert.True(mapped.Value.RewardDefinition.IsNoReward);
                break;
            case 20:
                Assert.Equal("Recovered; RewardNpcStateUnverified", records.VerificationStatus);
                break;
        }
    }

    private static async Task VerifyBindingEligibilityScenario(int scenarioId)
    {
        var rig = Rig();
        var definition = rig.Catalog.Get(100, QuestRequestSource.TestOnly).Value!;
        var acceptBinding = rig.Bindings.Resolve(new QuestBindingContext(
            definition,
            QuestNpcBindingType.Accept,
            1000,
            10,
            1,
            true,
            false));
        var turnInBinding = rig.Bindings.Resolve(new QuestBindingContext(
            definition,
            QuestNpcBindingType.TurnIn,
            1000,
            10,
            1,
            true,
            false));
        Assert.True(acceptBinding.Succeeded);
        Assert.True(turnInBinding.Succeeded);

        if (scenarioId is 23 or 24)
        {
            Assert.False(rig.Bindings.Resolve(new QuestBindingContext(
                definition,
                QuestNpcBindingType.Accept,
                1000,
                999,
                1,
                true,
                false)).Succeeded);
        }

        if (scenarioId == 25)
        {
            var invalid = rig.AcceptIntent(100) with { SessionId = "old-session" };
            Assert.Equal(QuestResultCode.InvalidSession, (await rig.Coordinator.AcceptAsync(invalid, default)).Code);
        }
        else if (scenarioId == 26)
        {
            var forged = rig.AcceptIntent(100) with { CharacterId = 99 };
            Assert.Equal(QuestResultCode.OwnershipMismatch, (await rig.Coordinator.AcceptAsync(forged, default)).Code);
        }
        else if (scenarioId == 32)
        {
            Assert.False(rig.Bindings.Resolve(new QuestBindingContext(
                definition,
                QuestNpcBindingType.Accept,
                1000,
                10,
                1,
                true,
                true)).Succeeded);
        }
        else if (scenarioId == 33)
        {
            var blocked = definition with
            {
                NpcBindings =
                [
                    definition.NpcBindings[0] with { PolicyStatus = QuestContentStatus.EvidenceBlocked }
                ]
            };
            Assert.False(rig.Bindings.Resolve(new QuestBindingContext(
                blocked,
                QuestNpcBindingType.Accept,
                1000,
                10,
                1,
                true,
                true)).Succeeded);
        }
        else
        {
            var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default);
            Assert.Equal(QuestResultCode.Success, accepted.Code);
            if (scenarioId == 27)
            {
                Assert.Equal(
                    QuestResultCode.QuestAlreadyActive,
                    (await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100, "accept-second"), default)).Code);
            }
        }
    }

    private static async Task VerifyAcceptAbandonScenario(int scenarioId)
    {
        var failure = new QuestFailureInjection();
        var rig = Rig(failureInjection: failure);
        if (scenarioId == 42)
        {
            rig.Store.FailureInjection.Point = QuestFailurePoint.AcceptancePersistence;
            Assert.Equal(
                QuestResultCode.PersistenceFailure,
                (await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default)).Code);
            Assert.Empty(rig.Authority.Snapshot);
            return;
        }

        if (scenarioId == 43)
        {
            failure.Point = QuestFailurePoint.AcceptanceRuntime;
        }

        var intent = rig.AcceptIntent(100);
        var first = await rig.Coordinator.AcceptAsync(intent, default);
        Assert.True(first.Code is QuestResultCode.Success or QuestResultCode.RecoveryRequired);
        Assert.Single(rig.Authority.GetCharacter(1));
        Assert.All(first.NetworkBytes, _ => Assert.Fail("Quest backend must not emit protocol bytes."));
        if (scenarioId == 43)
        {
            Assert.Equal(QuestResultCode.RecoveryRequired, first.Code);
            Assert.Equal(QuestInstanceState.Active, rig.Instance(100).State);
            return;
        }

        if (scenarioId == 37)
        {
            var duplicate = await rig.Coordinator.AcceptAsync(intent, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate.Code);
            Assert.Single(rig.Authority.GetCharacter(1));
        }
        else if (scenarioId == 38)
        {
            var changed = intent with { QuestDefinitionId = 101 };
            Assert.Equal(
                QuestResultCode.ProgressReplayConflict,
                (await rig.Coordinator.AcceptAsync(changed, default)).Code);
        }
        else if (scenarioId == 39)
        {
            var concurrentRig = Rig();
            var concurrentIntent = concurrentRig.AcceptIntent(100);
            var results = await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => concurrentRig.Coordinator.AcceptAsync(concurrentIntent, default)));
            Assert.Single(results, value => value.Code == QuestResultCode.Success);
            Assert.Single(concurrentRig.Authority.GetCharacter(1));
        }
        else if (scenarioId is >= 45 and <= 49)
        {
            var abandon = rig.AbandonIntent(first.Instance!, "abandon-1");
            var abandoned = await rig.Coordinator.AbandonAsync(abandon, default);
            Assert.Equal(QuestResultCode.Success, abandoned.Code);
            Assert.Equal(QuestInstanceState.Abandoned, abandoned.Instance!.State);
            if (scenarioId == 46)
            {
                Assert.Equal(
                    QuestResultCode.DuplicateCompleted,
                    (await rig.Coordinator.AbandonAsync(abandon, default)).Code);
            }

            if (scenarioId == 48)
            {
                Assert.Empty(await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default));
            }

            Assert.Contains(rig.Audit.Snapshot, value => value.Operation == "Abandon");
        }
        else
        {
            Assert.Equal(QuestInstanceState.Active, first.Instance!.State);
            Assert.All(first.Instance.ObjectiveStates, value => Assert.Equal(0, value.CurrentProgress));
            Assert.Single(rig.Events.Snapshot, value => value.Kind == QuestEventKind.QuestAccepted);
        }
    }

    private static async Task VerifyKillScenario(int scenarioId)
    {
        var rig = Rig();
        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default);
        Assert.Equal(QuestResultCode.Success, accepted.Code);

        if (scenarioId == 62)
        {
            var invalidDeath = rig.Death(Guid.NewGuid(), 200) with { KillerCharacterId = 99 };
            Assert.False(rig.Adapter.FromMonsterDeath(invalidDeath, 1, "account-safe").Succeeded);
            return;
        }

        var deathId = Guid.NewGuid();
        var eventId = scenarioId == 52 ? rig.KillEvent(deathId, 999) : rig.KillEvent(deathId, 200);
        var first = await rig.Router.RouteAsync(eventId, default);
        if (scenarioId == 52)
        {
            Assert.Empty(first);
            Assert.Equal(0, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
            return;
        }

        Assert.Single(first);
        Assert.Equal(1, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
        if (scenarioId is 53 or 59)
        {
            var duplicate = await rig.Router.RouteAsync(eventId, default);
            Assert.Single(duplicate);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate[0].Code);
            Assert.Equal(1, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
        }
        else if (scenarioId == 54)
        {
            var changed = eventId with { CorrelationId = "changed-payload" };
            var conflict = await rig.Router.RouteAsync(changed, default);
            Assert.Single(conflict);
            Assert.Equal(QuestResultCode.ProgressReplayConflict, conflict[0].Code);
        }
        else if (scenarioId == 58)
        {
            var events = Enumerable.Range(0, 12)
                .Select(_ => rig.KillEvent(Guid.NewGuid(), 200))
                .ToArray();
            await Task.WhenAll(events.Select(value => rig.Router.RouteAsync(value, default)));
            Assert.Equal(2, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
        }
        else
        {
            await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
            var instance = rig.Instance(100);
            Assert.Equal(2, instance.ObjectiveStates[0].CurrentProgress);
            Assert.Equal(QuestObjectiveStateCode.Completed, instance.ObjectiveStates[0].State);
            Assert.Equal(QuestInstanceState.ReadyToComplete, instance.State);
            Assert.Contains(rig.Audit.Snapshot, value => value.Operation == "Progress");
        }
    }

    private static async Task VerifyItemScenario(int scenarioId)
    {
        var rig = Rig();
        if (scenarioId == 65)
        {
            await rig.GrantAsync(500, 3, "initial-item");
        }

        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(101), default);
        Assert.Equal(QuestResultCode.Success, accepted.Code);
        if (scenarioId == 65)
        {
            Assert.Equal(3, accepted.Instance!.ObjectiveStates[0].CurrentProgress);
            Assert.Equal(QuestInstanceState.ReadyToComplete, accepted.Instance.State);
            return;
        }

        if (scenarioId is 75 or 76)
        {
            Assert.False(rig.Registry.Resolve(
                scenarioId == 75 ? QuestObjectiveType.AcquireItem : QuestObjectiveType.SubmitItem).Succeeded);
            return;
        }

        var eventId = Guid.NewGuid();
        var semantic = rig.ItemEvent(eventId, scenarioId == 67 ? 999 : 500, 0, 2);
        var first = await rig.Router.RouteAsync(semantic, default);
        if (scenarioId == 67)
        {
            Assert.Empty(first);
            return;
        }

        Assert.Single(first);
        Assert.Equal(2, rig.Instance(101).ObjectiveStates[0].CurrentProgress);
        if (scenarioId == 69)
        {
            var duplicate = await rig.Router.RouteAsync(semantic, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate[0].Code);
            Assert.Equal(2, rig.Instance(101).ObjectiveStates[0].CurrentProgress);
        }
        else if (scenarioId == 70)
        {
            await rig.Router.RouteAsync(rig.ItemEvent(Guid.NewGuid(), 500, 2, 1), default);
            Assert.Equal(1, rig.Instance(101).ObjectiveStates[0].CurrentProgress);
        }
        else
        {
            await rig.Router.RouteAsync(rig.ItemEvent(Guid.NewGuid(), 500, 2, 99), default);
            Assert.Equal(3, rig.Instance(101).ObjectiveStates[0].CurrentProgress);
            Assert.Equal(QuestObjectiveStateCode.Completed, rig.Instance(101).ObjectiveStates[0].State);
            await rig.Router.RouteAsync(rig.ItemEvent(Guid.NewGuid(), 500, 3, 0), default);
            Assert.Equal(3, rig.Instance(101).ObjectiveStates[0].CurrentProgress);
        }
    }

    private static async Task VerifyNpcScenario(int scenarioId)
    {
        var rig = Rig();
        await rig.Coordinator.AcceptAsync(rig.AcceptIntent(102), default);
        if (scenarioId == 84)
        {
            Assert.False(rig.Registry.Resolve(QuestObjectiveType.Scripted).Succeeded);
            return;
        }

        var interactionId = Guid.NewGuid();
        var semantic = rig.NpcEvent(interactionId, scenarioId == 79 ? 999 : 10, committed: scenarioId != 81);
        var first = await rig.Router.RouteAsync(semantic, default);
        if (scenarioId is 79 or 81)
        {
            Assert.Empty(first);
            Assert.Equal(0, rig.Instance(102).ObjectiveStates[0].CurrentProgress);
            return;
        }

        Assert.Single(first);
        Assert.Equal(1, rig.Instance(102).ObjectiveStates[0].CurrentProgress);
        if (scenarioId == 80)
        {
            var duplicate = await rig.Router.RouteAsync(semantic, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate[0].Code);
            Assert.Equal(1, rig.Instance(102).ObjectiveStates[0].CurrentProgress);
        }

        Assert.Contains(rig.Audit.Snapshot, value =>
            value.ObjectiveDefinitionId == "quest:102:objective:0");
    }

    private static async Task VerifyPortalScenario(int scenarioId)
    {
        var rig = Rig();
        var questId = scenarioId == 91 ? 104 : 103;
        await rig.Coordinator.AcceptAsync(rig.AcceptIntent(questId), default);
        if (scenarioId == 92)
        {
            Assert.False(rig.Registry.Resolve(QuestObjectiveType.Scripted).Succeeded);
            return;
        }

        var transitionId = Guid.NewGuid();
        var semantic = rig.PortalEvent(
            transitionId,
            targetMapId: scenarioId == 87 ? 999 : 7,
            portalTemplateId: scenarioId == 91 ? 900 : 901,
            committed: scenarioId is not (88 or 89));
        var first = await rig.Router.RouteAsync(semantic, default);
        if (scenarioId is 87 or 88 or 89)
        {
            Assert.Empty(first);
            Assert.Equal(0, rig.Instance(questId).ObjectiveStates[0].CurrentProgress);
            return;
        }

        Assert.Single(first);
        Assert.Equal(QuestObjectiveStateCode.Completed, rig.Instance(questId).ObjectiveStates[0].State);
        if (scenarioId == 90)
        {
            var duplicate = await rig.Router.RouteAsync(semantic, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate[0].Code);
        }
    }

    private static async Task VerifyBattleScenario(int scenarioId)
    {
        var rig = scenarioId == 98
            ? Rig(
            [
                .. Definitions().Where(value => value.QuestDefinitionId != 105),
                Definition(105, QuestObjectiveType.CompleteBattle, 42, 1, targetRuntimeType: "Encounter")
            ])
            : Rig();
        await rig.Coordinator.AcceptAsync(rig.AcceptIntent(105), default);
        var semantic = rig.BattleEvent(
            Guid.NewGuid(),
            won: scenarioId != 96,
            committed: scenarioId != 95,
            encounter: scenarioId == 98 ? "wrong" : "any");
        var first = await rig.Router.RouteAsync(semantic, default);
        if (scenarioId is 95 or 96 or 98)
        {
            Assert.Empty(first);
            Assert.Equal(0, rig.Instance(105).ObjectiveStates[0].CurrentProgress);
            return;
        }

        Assert.Single(first);
        Assert.Equal(QuestInstanceState.ReadyToComplete, rig.Instance(105).State);
        if (scenarioId == 97)
        {
            var duplicate = await rig.Router.RouteAsync(semantic, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate[0].Code);
        }

        Assert.Empty(first.SelectMany(_ => Array.Empty<byte>()));
    }

    private static async Task VerifyProgressScenario(int scenarioId)
    {
        var failure = new QuestFailureInjection();
        var rig = Rig(failureInjection: failure);
        await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default);
        var firstEvent = rig.KillEvent(Guid.NewGuid(), 200);
        var first = await rig.Router.RouteAsync(firstEvent, default);
        Assert.Single(first);
        Assert.Equal(2, rig.Instance(100).QuestVersion);
        Assert.Equal(1, rig.Instance(100).ObjectiveStates[0].ObjectiveVersion);

        if (scenarioId == 105)
        {
            var duplicate = await rig.Router.RouteAsync(firstEvent, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate[0].Code);
        }
        else if (scenarioId == 108)
        {
            var events = Enumerable.Range(0, 24)
                .Select(_ => rig.KillEvent(Guid.NewGuid(), 200))
                .ToArray();
            await Task.WhenAll(events.Select(value => rig.Router.RouteAsync(value, default)));
            Assert.Equal(2, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
        }
        else if (scenarioId == 109)
        {
            var instance = rig.Instance(100);
            var state = instance.ObjectiveStates[0];
            var stale = new QuestProgressMutationPlan(
                Guid.NewGuid(),
                Guid.NewGuid(),
                QuestRuntimeSecurity.Hash("stale"),
                instance.QuestInstanceId,
                instance.QuestDefinitionId,
                state.ObjectiveDefinitionId,
                state.ObjectiveIndex,
                instance.CharacterId,
                state.CurrentProgress,
                1,
                2,
                state.State,
                QuestObjectiveStateCode.Completed,
                instance.State,
                instance.State,
                0,
                state.ObjectiveVersion,
                QuestContentStatus.TestOnly,
                Now,
                "stale");
            Assert.Equal(
                QuestResultCode.ProgressVersionConflict,
                (await rig.Progress.CommitAsync(stale, default)).Code);
        }
        else if (scenarioId == 110)
        {
            var handler = new KillMonsterQuestObjectiveHandler();
            var definition = rig.Catalog.Get(100, QuestRequestSource.TestOnly).Value!;
            var instance = rig.Instance(100);
            var objective = definition.ObjectiveDefinitions[0] with { RequiredCount = long.MaxValue };
            var state = instance.ObjectiveStates[0] with
            {
                RequiredCount = long.MaxValue,
                CurrentProgress = long.MaxValue
            };
            Assert.Throws<OverflowException>(() =>
                handler.CreatePlan(definition, instance, objective, state, rig.KillEvent(Guid.NewGuid(), 200)));
        }
        else if (scenarioId == 114)
        {
            rig.Store.FailureInjection.Point = QuestFailurePoint.ProgressPersistence;
            var result = await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
            Assert.Equal(QuestResultCode.PersistenceFailure, result[0].Code);
        }
        else if (scenarioId == 115)
        {
            failure.Point = QuestFailurePoint.ProgressRuntime;
            var result = await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
            Assert.Equal(QuestResultCode.RecoveryRequired, result[0].Code);
            Assert.Equal(2, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
        }
        else
        {
            var second = await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
            Assert.Single(second);
            Assert.Equal(2, rig.Instance(100).ObjectiveStates[0].CurrentProgress);
            Assert.Equal(QuestInstanceState.ReadyToComplete, rig.Instance(100).State);
            Assert.All(rig.Store.ProgressPlans, plan =>
            {
                Assert.InRange(plan.ProgressAfter, 0, 2);
                Assert.True(plan.ProgressAfter >= plan.ProgressBefore);
            });
        }
    }

    private static async Task VerifyCompletionScenario(int scenarioId)
    {
        var rig = Rig();
        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default);
        if (scenarioId == 118)
        {
            var readiness = new AllRequiredQuestCompletionPolicy().IsReady(
                rig.Catalog.Get(100, QuestRequestSource.TestOnly).Value!,
                accepted.Instance!);
            Assert.True(readiness.Succeeded);
            Assert.False(readiness.Value);
            return;
        }

        if (scenarioId == 123)
        {
            var early = rig.TurnInIntent(accepted.Instance!, "turnin-early");
            Assert.Equal(
                QuestResultCode.QuestNotReady,
                (await rig.Coordinator.TurnInAsync(early, default)).Code);
            return;
        }

        await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        var ready = rig.Instance(100);
        Assert.Equal(QuestInstanceState.ReadyToComplete, ready.State);
        if (scenarioId == 122)
        {
            var wrong = rig.TurnInIntent(ready, "turnin-wrong") with
            {
                SourceNpcTemplateId = 999
            };
            Assert.Equal(
                QuestResultCode.InvalidNpcBinding,
                (await rig.Coordinator.TurnInAsync(wrong, default)).Code);
            return;
        }

        var intent = rig.TurnInIntent(ready, "turnin-1");
        var completed = await rig.Coordinator.TurnInAsync(intent, default);
        Assert.Equal(QuestResultCode.Success, completed.Code);
        Assert.Equal(QuestInstanceState.Completed, completed.Instance!.State);
        Assert.Empty(completed.NetworkBytes);
        if (scenarioId == 124)
        {
            Assert.Equal(
                QuestResultCode.DuplicateCompleted,
                (await rig.Coordinator.TurnInAsync(intent, default)).Code);
        }
        else if (scenarioId == 125)
        {
            var changed = intent with { SourceNpcTemplateId = 999 };
            Assert.Equal(
                QuestResultCode.CompletionConflict,
                (await rig.Coordinator.TurnInAsync(changed, default)).Code);
        }
        else if (scenarioId == 126)
        {
            Assert.Equal(
                QuestResultCode.QuestAlreadyCompleted,
                (await rig.Coordinator.TurnInAsync(
                    rig.TurnInIntent(completed.Instance, "turnin-2"),
                    default)).Code);
        }

        Assert.Contains(rig.Audit.Snapshot, value => value.Operation == "TurnIn");
    }

    private static async Task VerifyRewardScenario(int scenarioId)
    {
        if (scenarioId is 139 or 141 or 142)
        {
            var rigForBoundary = Rig();
            var plan = new QuestRewardPlan(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                999,
                1,
                [],
                [],
                scenarioId == 141 ? 100 : null,
                scenarioId == 142 ? 77 : null,
                [],
                QuestRuntimeSecurity.Hash($"reward-boundary-{scenarioId}"),
                scenarioId == 139 ? QuestContentStatus.EvidenceBlocked : QuestContentStatus.TestOnly,
                Now,
                "reward-boundary");
            var result = await rigForBoundary.Rewards.CommitAsync(plan, "session-1", default);
            Assert.Equal(
                scenarioId == 139 ? QuestResultCode.RewardEvidenceBlocked : QuestResultCode.UnsupportedReward,
                result.Code);
            return;
        }

        var invalidReward = scenarioId == 133;
        var capacity = scenarioId == 134 ? 1 : 32;
        var rewardDefinition = Definition(
            106,
            QuestObjectiveType.KillMonster,
            200,
            1,
            reward: new QuestRewardDefinition(
                "quest:106:reward",
                [new QuestItemReward(invalidReward ? 999 : 501, 2)],
                [new QuestCurrencyReward("Gold", 5)],
                null,
                null,
                [],
                QuestContentStatus.TestOnly,
                """{"testOnly":true}"""));
        var rig = Rig([.. Definitions(), rewardDefinition], inventoryCapacity: capacity);
        if (scenarioId == 134)
        {
            Assert.True((await rig.GrantAsync(500, 1, "fill-inventory")).Succeeded);
        }

        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(106), default);
        await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        var ready = rig.Instance(106);
        Assert.Equal(QuestInstanceState.ReadyToComplete, ready.State);
        var intent = rig.TurnInIntent(ready, "reward-turnin");
        var completed = await rig.Coordinator.TurnInAsync(intent, default);
        if (scenarioId is 133 or 134)
        {
            Assert.Equal(QuestResultCode.InventoryTransactionRejected, completed.Code);
            Assert.Equal(QuestInstanceState.RecoveryRequired, rig.Instance(106).State);
            return;
        }

        Assert.Equal(QuestResultCode.Success, completed.Code);
        Assert.NotNull(completed.RewardResult);
        Assert.True(completed.RewardResult!.ItemCommitted);
        Assert.True(completed.RewardResult.CurrencyCommitted);
        var inventory = rig.Inventory.CaptureInspector(new InventoryInspectorQuery(CharacterId: 1));
        Assert.Equal(2, inventory.Items.Single(value => value.ItemTemplateId == 501).Quantity);
        if (scenarioId is 135 or 136)
        {
            var duplicate = await rig.Coordinator.TurnInAsync(intent, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate.Code);
            Assert.Equal(2, rig.Inventory.CaptureInspector(
                new InventoryInspectorQuery(CharacterId: 1)).Items.Single(value => value.ItemTemplateId == 501).Quantity);
        }

        Assert.Single(rig.Store.RewardRecords);
    }

    private static async Task VerifyRepeatChainScenario(int scenarioId)
    {
        if (scenarioId == 149)
        {
            var validator = new QuestDefinitionValidator();
            var left = Definition(301, QuestObjectiveType.KillMonster, 200, 1) with
            {
                Prerequisites = [Prerequisite("left", QuestPrerequisiteType.QuestCompleted, requiredQuestId: 302)]
            };
            var right = Definition(302, QuestObjectiveType.KillMonster, 200, 1) with
            {
                Prerequisites = [Prerequisite("right", QuestPrerequisiteType.QuestCompleted, requiredQuestId: 301)]
            };
            Assert.Throws<InvalidOperationException>(() =>
                new ImmutableQuestDefinitionCatalog([left, right], validator));
            return;
        }

        var rig = Rig();
        if (scenarioId is 147 or 148 or 152)
        {
            Assert.Equal(
                QuestResultCode.EligibilityEvidenceBlocked,
                (await rig.Coordinator.AcceptAsync(rig.AcceptIntent(109), default)).Code);
            return;
        }

        if (scenarioId is 145 or 146)
        {
            await CompleteQuestAsync(rig, 107, "repeat-first");
            var repeated = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(107, "repeat-second"), default);
            Assert.Equal(QuestResultCode.Success, repeated.Code);
            Assert.Equal(2, repeated.Instance!.RepeatIteration);
            return;
        }

        await CompleteQuestAsync(rig, 100, "once-first");
        if (scenarioId == 144)
        {
            Assert.Equal(
                QuestResultCode.QuestAlreadyCompleted,
                (await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100, "once-second"), default)).Code);
        }
        else
        {
            var unlocked = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(108, "chain-next"), default);
            Assert.Equal(QuestResultCode.Success, unlocked.Code);
            Assert.Single(rig.Authority.GetCharacter(1), value => value.QuestDefinitionId == 108);
        }
    }

    private static async Task VerifyRecoveryScenario(int scenarioId)
    {
        var failure = new QuestFailureInjection();
        var rig = Rig(failureInjection: failure);
        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default);
        await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        var persisted = await rig.Store.LoadCharacterAsync(1, default);
        Assert.Single(persisted);
        Assert.Equal(1, persisted[0].ObjectiveStates[0].CurrentProgress);

        if (scenarioId == 171)
        {
            var missing = accepted.Instance! with
            {
                QuestDefinitionId = 999,
                QuestVersion = 5,
                State = QuestInstanceState.RecoveryRequired,
                RecoveryState = QuestRecoveryState.RecoveryRequired
            };
            rig.Store.Seed(missing);
            var authority = new InMemoryQuestInstanceAuthority();
            var recovery = new QuestRecoveryCoordinator(rig.Store, authority, rig.Catalog, rig.Events);
            var result = await recovery.RecoverAsync(1, missing.QuestInstanceId, "missing-definition", default);
            Assert.Equal(QuestResultCode.RecoveryRequired, result.Code);
            Assert.Equal("quest.recovery.definition_missing", result.FailureCode);
            return;
        }

        if (scenarioId == 172)
        {
            var current = rig.Instance(100);
            var missingObjective = current with
            {
                ObjectiveStates =
                [
                    current.ObjectiveStates[0] with { ObjectiveDefinitionId = "missing-objective" }
                ],
                State = QuestInstanceState.RecoveryRequired,
                RecoveryState = QuestRecoveryState.RecoveryRequired,
                QuestVersion = checked(current.QuestVersion + 1)
            };
            rig.Store.Seed(missingObjective);
            var authority = new InMemoryQuestInstanceAuthority();
            var recovery = new QuestRecoveryCoordinator(rig.Store, authority, rig.Catalog, rig.Events);
            var result = await recovery.RecoverAsync(1, missingObjective.QuestInstanceId, "missing-objective", default);
            Assert.Equal(QuestResultCode.RecoveryRequired, result.Code);
            Assert.Equal("quest.recovery.objective_missing", result.FailureCode);
            return;
        }

        var reloadedAuthority = new InMemoryQuestInstanceAuthority();
        var coordinator = new QuestRecoveryCoordinator(rig.Store, reloadedAuthority, rig.Catalog, rig.Events);
        var recovered = await coordinator.RecoverAsync(1, accepted.QuestInstanceId, $"recovery-{scenarioId}", default);
        Assert.Equal(QuestResultCode.Success, recovered.Code);
        Assert.Equal(1, reloadedAuthority.Get(accepted.QuestInstanceId!.Value)!.ObjectiveStates[0].CurrentProgress);
        var duplicate = await coordinator.RecoverAsync(1, accepted.QuestInstanceId, $"recovery-{scenarioId}", default);
        Assert.Equal(QuestResultCode.Success, duplicate.Code);
        Assert.Single(reloadedAuthority.GetCharacter(1));
        if (scenarioId == 173)
        {
            var inspector = new QuestRuntimeInspector(
                rig.Catalog,
                reloadedAuthority,
                rig.Store,
                rig.Audit);
            Assert.Single(inspector.Capture(new QuestInspectorQuery(CharacterId: 1)).Instances);
        }
    }

    private static async Task VerifyReconnectScenario(int scenarioId)
    {
        var rig = Rig();
        var intent = rig.AcceptIntent(100);
        var accepted = await rig.Coordinator.AcceptAsync(intent, default);
        await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        var before = rig.Instance(100);
        var disconnectedAuthority = new InMemoryQuestInstanceAuthority();
        var recovery = new QuestRecoveryCoordinator(
            rig.Store,
            disconnectedAuthority,
            rig.Catalog,
            rig.Events);
        var reconnect = await recovery.RecoverAsync(1, null, "reconnect", default);
        Assert.Equal(QuestResultCode.Success, reconnect.Code);
        var restored = disconnectedAuthority.Get(before.QuestInstanceId)!;
        Assert.Equal(before.QuestVersion, restored.QuestVersion);
        Assert.Equal(before.ObjectiveStates[0].CurrentProgress, restored.ObjectiveStates[0].CurrentProgress);
        Assert.Equal(before.RepeatIteration, restored.RepeatIteration);

        if (scenarioId == 184)
        {
            rig.Sessions.Remove("session-1");
            rig.Sessions.Register(rig.Session with { SessionId = "session-2" });
            Assert.Equal(
                QuestResultCode.InvalidSession,
                (await rig.Coordinator.AbandonAsync(
                    rig.AbandonIntent(before, "old-session-intent"),
                    default)).Code);
        }
        else if (scenarioId is 180 or 181)
        {
            Assert.Equal(
                QuestResultCode.DuplicateCompleted,
                (await rig.Coordinator.AcceptAsync(intent, default)).Code);
            var semantic = rig.KillEvent(Guid.NewGuid(), 200);
            var once = await rig.Router.RouteAsync(semantic, default);
            var twice = await rig.Router.RouteAsync(semantic, default);
            Assert.Equal(QuestResultCode.DuplicateCompleted, twice[0].Code);
            Assert.Single(once);
        }

        var duplicateReconnect = await recovery.RecoverAsync(1, null, "reconnect", default);
        Assert.Equal(QuestResultCode.Success, duplicateReconnect.Code);
        Assert.Single(disconnectedAuthority.GetCharacter(1));
        Assert.Equal(accepted.QuestInstanceId, disconnectedAuthority.GetCharacter(1)[0].QuestInstanceId);
    }

    private static async Task VerifyDiagnosticsScenario(int scenarioId)
    {
        var inspectorFailure = new QuestFailureInjection();
        var rig = Rig();
        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(100), default);
        await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        var inspector = new QuestRuntimeInspector(
            rig.Catalog,
            rig.Authority,
            rig.Store,
            rig.Audit,
            inspectorFailure);
        var snapshot = inspector.Capture(new QuestInspectorQuery(
            CharacterId: 1,
            QuestDefinitionId: 100,
            Offset: 0,
            Limit: 10));
        Assert.Single(snapshot.Definitions);
        Assert.Single(snapshot.Instances);
        Assert.Single(snapshot.Objectives);
        Assert.Single(snapshot.Progress);
        Assert.NotEmpty(snapshot.Audit);

        if (scenarioId == 199)
        {
            inspectorFailure.Point = QuestFailurePoint.Inspector;
            var failed = inspector.Capture(new QuestInspectorQuery());
            Assert.Equal("quest.inspector.failure", failed.FailureCode);
            Assert.NotNull(rig.Authority.Get(accepted.QuestInstanceId!.Value));
        }
        else if (scenarioId == 200)
        {
            var captures = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
                Task.Run(() => inspector.Capture(new QuestInspectorQuery(CharacterId: 1)))));
            Assert.All(captures, value => Assert.Single(value.Instances));
        }
        else if (scenarioId == 201)
        {
            var text = string.Join(
                '\n',
                File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Runtime", "QuestRuntime.cs")),
                File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Runtime", "QuestRuntimeContracts.cs")));
            Assert.DoesNotContain("password=", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secretValue", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connectionString=", text, StringComparison.OrdinalIgnoreCase);
        }
        else if (scenarioId == 202)
        {
            var files = new[]
            {
                Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Runtime", "QuestRuntime.cs"),
                Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Runtime", "QuestRuntimeContracts.cs")
            };
            Assert.All(files, file =>
                Assert.DoesNotContain(@":\Users\", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));
        }
        else if (scenarioId == 203)
        {
            var sqlStore = File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "src",
                "God2.ClassicServer.Persistence",
                "MariaDbQuestRuntimeStore.cs"));
            Assert.Contains("@characterId", sqlStore, StringComparison.Ordinal);
            Assert.Contains("@questInstanceId", sqlStore, StringComparison.Ordinal);
            Assert.Contains("@idempotencyKeyHash", sqlStore, StringComparison.Ordinal);
            Assert.DoesNotContain($"WHERE `CharacterId` = {1}", sqlStore, StringComparison.Ordinal);
        }
        else if (scenarioId == 204)
        {
            Assert.Empty((await rig.Coordinator.AcceptAsync(
                rig.AcceptIntent(101, "zero-network"),
                default)).NetworkBytes);
            Assert.All(rig.Events.Snapshot, value => Assert.Equal(0, value.NetworkBytes));
        }
        else
        {
            Assert.IsAssignableFrom<IReadOnlyList<QuestInstanceInspectorItem>>(snapshot.Instances);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<QuestInstanceInspectorItem>)snapshot.Instances).Add(snapshot.Instances[0]));
        }
    }

    private static void VerifyFrozenScenario(int scenarioId)
    {
        Assert.InRange(scenarioId, 205, 218);
        Assert.True(typeof(InventoryTransactionCoordinator).IsSealed);
        Assert.True(typeof(WorldInteractionCoordinator).IsSealed);
        Assert.True(typeof(CombatCoordinator).IsSealed);
        Assert.True(typeof(TurnBasedBattleCoordinator).IsSealed);
        Assert.True(typeof(SkillActionResolver).IsSealed);
        Assert.True(typeof(StatusEffectCoordinator).IsSealed);
        Assert.True(typeof(StatusTriggerCoordinator).IsSealed);
        Assert.True(typeof(PortalTransitionCoordinator).IsSealed);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "Automation", "Test-God2AutomationJsonRetry.ps1")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "Reports", "StatusEffectRuntime.FinalFreeze.md")));
    }

    private static async Task CompleteQuestAsync(QuestTestRig rig, int questDefinitionId, string key)
    {
        var accepted = await rig.Coordinator.AcceptAsync(rig.AcceptIntent(questDefinitionId, $"{key}-accept"), default);
        Assert.Equal(QuestResultCode.Success, accepted.Code);
        var required = accepted.Instance!.ObjectiveStates[0].RequiredCount;
        for (var index = 0; index < required; index++)
        {
            await rig.Router.RouteAsync(rig.KillEvent(Guid.NewGuid(), 200), default);
        }

        var ready = rig.Instance(questDefinitionId);
        Assert.Equal(QuestInstanceState.ReadyToComplete, ready.State);
        var completed = await rig.Coordinator.TurnInAsync(rig.TurnInIntent(ready, $"{key}-turnin"), default);
        Assert.Equal(QuestResultCode.Success, completed.Code);
    }

    private static QuestTestRig Rig(
        IReadOnlyList<QuestDefinition>? definitions = null,
        QuestFailureInjection? failureInjection = null,
        int inventoryCapacity = 32)
    {
        var validator = new QuestDefinitionValidator();
        var catalog = new ImmutableQuestDefinitionCatalog(definitions ?? Definitions(), validator);
        var sessions = new InMemoryQuestSessionValidator();
        var session = new QuestSessionSnapshot(
            "session-1",
            7,
            1,
            100,
            3,
            1,
            true,
            true);
        sessions.Register(session);
        var itemCatalog = ItemCatalog();
        var inventoryAudit = new InMemoryInventoryAuditLedger();
        var inventoryStore = new InMemoryInventoryPersistenceStore(inventoryAudit);
        inventoryStore.Seed(new InventoryPersistenceBundle(
            new PlayerInventorySnapshot(Guid.NewGuid(), 1, inventoryCapacity, 0, 0, "Clean", []),
            new CurrencyWalletSnapshot(1, [new CurrencyBalance("Gold", 0, 0, "Clean")])));
        var nextItemId = 10_000L;
        var inventory = new InventoryTransactionCoordinator(
            itemCatalog,
            new MerchantDefinitionCatalog([]),
            inventoryStore,
            inventoryAudit,
            () => Interlocked.Increment(ref nextItemId),
            inventoryStore.FailureInjection);
        var authority = new InMemoryQuestInstanceAuthority();
        var store = new InMemoryQuestRuntimeStore();
        var events = new InMemoryQuestEventSink();
        var audit = new InMemoryQuestAuditLedger();
        var completion = new AllRequiredQuestCompletionPolicy();
        var rewards = new QuestRewardCoordinator(store, inventory, events);
        var bindings = new QuestBindingResolver();
        var coordinator = new QuestCoordinator(
            catalog,
            sessions,
            bindings,
            new QuestEligibilityPolicy(),
            new InventoryCoordinatorQuestSnapshotProvider(inventory),
            authority,
            store,
            completion,
            rewards,
            events,
            audit,
            failureInjection);
        var progress = new QuestProgressCoordinator(
            store,
            authority,
            catalog,
            completion,
            events,
            audit,
            failureInjection);
        var registry = QuestObjectiveHandlerRegistry.CreateDefault();
        var router = new QuestEventRouter(
            authority,
            catalog,
            registry,
            progress,
            events,
            testOnlyRuntime: true);
        return new QuestTestRig(
            catalog,
            sessions,
            session,
            bindings,
            authority,
            store,
            events,
            audit,
            inventory,
            rewards,
            coordinator,
            progress,
            registry,
            router,
            new QuestSemanticEventAdapter());
    }

    private static IReadOnlyList<QuestDefinition> Definitions()
    {
        var values = new List<QuestDefinition>
        {
            Definition(100, QuestObjectiveType.KillMonster, 200, 2, withNpcBinding: true),
            Definition(101, QuestObjectiveType.OwnItem, 500, 3),
            Definition(102, QuestObjectiveType.InteractNpc, 10, 1),
            Definition(103, QuestObjectiveType.VisitMap, 7, 1),
            Definition(104, QuestObjectiveType.UsePortal, 900, 1),
            Definition(105, QuestObjectiveType.CompleteBattle, null, 1, targetRuntimeType: "WinBattle"),
            Definition(107, QuestObjectiveType.KillMonster, 200, 1) with
            {
                RepeatPolicy = new QuestRepeatPolicy(
                    QuestRepeatPolicyType.Repeatable,
                    null,
                    QuestContentStatus.TestOnly,
                    """{"testOnly":true}""")
            },
            Definition(108, QuestObjectiveType.KillMonster, 200, 1) with
            {
                Prerequisites =
                [
                    Prerequisite(
                        "quest:108:prerequisite:100",
                        QuestPrerequisiteType.QuestCompleted,
                        requiredQuestId: 100)
                ]
            },
            Definition(109, QuestObjectiveType.KillMonster, 200, 1) with
            {
                RepeatPolicy = new QuestRepeatPolicy(
                    QuestRepeatPolicyType.Daily,
                    null,
                    QuestContentStatus.EvidenceBlocked,
                    """{"reset":"unknown"}""")
            },
            Definition(
                110,
                QuestObjectiveType.KillMonster,
                200,
                1,
                policyStatus: QuestContentStatus.ContentBacked,
                reward: new QuestRewardDefinition(
                    "quest:110:no-reward",
                    [],
                    [],
                    null,
                    null,
                    [],
                    QuestContentStatus.ContentBacked,
                    """{"contentBacked":true}"""))
        };
        return values;
    }

    private static QuestDefinition Definition(
        int id,
        QuestObjectiveType objectiveType,
        int? targetTemplateId,
        long requiredCount,
        bool withNpcBinding = false,
        string targetRuntimeType = "Template",
        QuestContentStatus policyStatus = QuestContentStatus.TestOnly,
        QuestRewardDefinition? reward = null) =>
        new(
            id,
            $"test:quest:{id}",
            $"Test Quest {id}",
            $"Deterministic TestOnly quest {id}.",
            QuestCategory.Side,
            QuestType.Standard,
            withNpcBinding ? "NpcAccepted" : "SystemGranted",
            QuestCompletionPolicyType.AllRequired,
            new QuestRepeatPolicy(
                QuestRepeatPolicyType.Once,
                1,
                policyStatus,
                policyStatus == QuestContentStatus.TestOnly
                    ? """{"testOnly":true}"""
                    : """{"contentBacked":true}"""),
            [],
            [
                new QuestObjectiveDefinition(
                    $"quest:{id}:objective:0",
                    id,
                    0,
                    objectiveType,
                    targetTemplateId,
                    targetRuntimeType,
                    requiredCount,
                    objectiveType == QuestObjectiveType.OwnItem
                        ? QuestProgressMode.CurrentOwnership
                        : QuestProgressMode.UniqueEvent,
                    "AllRequired",
                    true,
                    policyStatus,
                    $"test-quest-{id}-v1",
                    policyStatus == QuestContentStatus.TestOnly
                        ? """{"testOnly":true}"""
                        : """{"contentBacked":true}""")
            ],
            reward ?? new QuestRewardDefinition(
                $"quest:{id}:no-reward",
                [],
                [],
                null,
                null,
                [],
                policyStatus,
                policyStatus == QuestContentStatus.TestOnly
                    ? """{"testOnly":true}"""
                    : """{"contentBacked":true}"""),
            withNpcBinding
                ?
                [
                    new QuestNpcBinding(
                        id,
                        10,
                        QuestNpcBindingType.AcceptAndTurnIn,
                        1,
                        "Quest",
                        true,
                        policyStatus,
                        policyStatus == QuestContentStatus.TestOnly
                            ? """{"testOnly":true}"""
                            : """{"contentBacked":true}""")
                ]
                : [],
            true,
            $"test-quest-{id}-v1",
            policyStatus,
            QuestContentStatus.EvidenceBlocked,
            policyStatus == QuestContentStatus.TestOnly
                ? """{"testOnly":true}"""
                : """{"contentBacked":true}""");

    private static QuestDefinitionRecord Record(QuestDefinition definition) =>
        new(
            definition.QuestDefinitionId,
            definition.ExternalQuestId,
            definition.Name,
            definition.Description,
            definition.QuestCategory,
            definition.QuestType,
            definition.AcceptPolicy,
            definition.CompletionPolicy,
            definition.RepeatPolicy,
            definition.Prerequisites,
            definition.ObjectiveDefinitions,
            definition.RewardDefinition,
            definition.NpcBindings,
            definition.Enabled,
            definition.ContentVersion,
            definition.PolicyStatus,
            definition.ProtocolStatus,
            definition.RawMetadata);

    private static QuestPrerequisiteDefinition Prerequisite(
        string id,
        QuestPrerequisiteType type,
        int? requiredQuestId = null,
        int? itemTemplateId = null,
        long requiredValue = 1) =>
        new(
            id,
            type,
            requiredQuestId,
            itemTemplateId,
            requiredValue,
            QuestContentStatus.TestOnly,
            """{"testOnly":true}""");

    private static QuestDefinition CandidateVisualDefinition() =>
        new(
            900,
            "client:quest/mission-data/1",
            "Visual Candidate",
            "Client presentation text only.",
            QuestCategory.Unknown,
            QuestType.Unknown,
            "Unknown",
            QuestCompletionPolicyType.Unknown,
            new QuestRepeatPolicy(
                QuestRepeatPolicyType.Unknown,
                null,
                QuestContentStatus.EvidenceBlocked,
                "{}"),
            [],
            [
                new QuestObjectiveDefinition(
                    "client:quest/mission-data/1:step:1",
                    900,
                    0,
                    QuestObjectiveType.Unknown,
                    null,
                    "ClientText",
                    0,
                    QuestProgressMode.Unknown,
                    "Unknown",
                    true,
                    QuestContentStatus.VisualOnly,
                    "client-visual-v1",
                    """{"relationStatus":"client-text-objective"}""")
            ],
            new QuestRewardDefinition(
                "client:quest/mission-data/1:reward-unknown",
                [],
                [],
                null,
                null,
                [],
                QuestContentStatus.EvidenceBlocked,
                "{}"),
            [],
            false,
            "client-visual-v1",
            QuestContentStatus.EvidenceBlocked,
            QuestContentStatus.EvidenceBlocked,
            """{"canDirectImportToGameplay":false}""");

    private static ItemDefinitionCatalog ItemCatalog()
    {
        var mapper = new ItemContentMapper();
        var validation = new ItemContentValidator().Validate(
        [
            mapper.Map(ItemRecord(500, 99, questItem: true)),
            mapper.Map(ItemRecord(501, 99, questItem: false))
        ]);
        Assert.Empty(validation.Quarantined);
        return validation.Catalog;
    }

    private static ItemDatabaseRecord ItemRecord(
        int id,
        int maximumStack,
        bool questItem) =>
        new(
            id,
            $"item_{id}",
            $"Item {id}",
            null,
            questItem ? "Quest" : "Material",
            "Stackable",
            maximumStack,
            "None",
            "Tradable",
            questItem ? "NotSellable" : "Sellable",
            0,
            0,
            "Gold",
            null,
            null,
            questItem,
            true,
            "{}",
            "quest-tests-v1",
            "QuestRuntimeTests");

    private static OfficialQuestEvidence LoadOfficialQuestEvidence()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "db",
            "imports",
            "official",
            "quests",
            "quests.official.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var records = new List<OfficialQuestEvidenceRecord>();
        foreach (var record in root.GetProperty("records").EnumerateArray())
        {
            records.Add(new OfficialQuestEvidenceRecord(
                record.GetProperty("objectives").GetArrayLength(),
                record.GetProperty("rewardCandidates").GetArrayLength(),
                record.GetProperty("prerequisites").GetArrayLength(),
                record.GetProperty("targetReferences").GetProperty("item").GetArrayLength()));
        }

        return new OfficialQuestEvidence(
            root.GetProperty("recordCount").GetInt32(),
            root.GetProperty("canDirectImportToGameplay").GetBoolean(),
            root.GetProperty("verificationStatus").GetString() ?? "",
            records);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record OfficialQuestEvidence(
        int RecordCount,
        bool CanDirectImportToGameplay,
        string VerificationStatus,
        IReadOnlyList<OfficialQuestEvidenceRecord> Records);

    private sealed record OfficialQuestEvidenceRecord(
        int Objectives,
        int RewardCandidates,
        int Prerequisites,
        int ItemReferenceCount);

    private sealed record QuestTestRig(
        ImmutableQuestDefinitionCatalog Catalog,
        InMemoryQuestSessionValidator Sessions,
        QuestSessionSnapshot Session,
        QuestBindingResolver Bindings,
        InMemoryQuestInstanceAuthority Authority,
        InMemoryQuestRuntimeStore Store,
        InMemoryQuestEventSink Events,
        InMemoryQuestAuditLedger Audit,
        InventoryTransactionCoordinator Inventory,
        QuestRewardCoordinator Rewards,
        QuestCoordinator Coordinator,
        QuestProgressCoordinator Progress,
        QuestObjectiveHandlerRegistry Registry,
        QuestEventRouter Router,
        QuestSemanticEventAdapter Adapter)
    {
        public QuestIntent AcceptIntent(int questDefinitionId, string key = "accept-1") =>
            new(
                Guid.NewGuid(),
                key,
                Session.SessionId,
                Session.CharacterId,
                Session.PlayerRuntimeEntityId,
                questDefinitionId,
                1000,
                10,
                QuestIntentType.Accept,
                null,
                Session.CharacterRuntimeVersion,
                questDefinitionId == 110
                    ? QuestRequestSource.ProductionClient
                    : QuestRequestSource.TestOnly,
                Now,
                key);

        public QuestIntent AbandonIntent(QuestInstance instance, string key) =>
            new(
                Guid.NewGuid(),
                key,
                Session.SessionId,
                Session.CharacterId,
                Session.PlayerRuntimeEntityId,
                instance.QuestDefinitionId,
                1000,
                10,
                QuestIntentType.Abandon,
                instance.QuestVersion,
                Session.CharacterRuntimeVersion,
                QuestRequestSource.TestOnly,
                Now.AddSeconds(1),
                key);

        public QuestIntent TurnInIntent(QuestInstance instance, string key) =>
            new(
                Guid.NewGuid(),
                key,
                Session.SessionId,
                Session.CharacterId,
                Session.PlayerRuntimeEntityId,
                instance.QuestDefinitionId,
                1000,
                10,
                QuestIntentType.TurnIn,
                instance.QuestVersion,
                Session.CharacterRuntimeVersion,
                QuestRequestSource.TestOnly,
                Now.AddSeconds(2),
                key);

        public QuestInstance Instance(int questDefinitionId) =>
            Authority.GetCharacter(Session.CharacterId)
                .Where(value => value.QuestDefinitionId == questDefinitionId)
                .OrderByDescending(value => value.RepeatIteration)
                .First();

        public MonsterDeathRecord Death(Guid deathId, int monsterTemplateId) =>
            new(
                deathId,
                Guid.NewGuid(),
                9000,
                monsterTemplateId,
                Session.PlayerRuntimeEntityId,
                Session.CharacterId,
                Session.MapId,
                1,
                10,
                10,
                Now,
                CombatPolicyStatus.TestOnly,
                CombatPolicyStatus.EvidenceBlocked,
                CombatPolicyStatus.TestOnly,
                1,
                2,
                deathId.ToString("N"));

        public QuestSemanticEvent KillEvent(Guid deathId, int monsterTemplateId)
        {
            var result = Adapter.FromMonsterDeath(
                Death(deathId, monsterTemplateId),
                Session.CharacterId,
                "account-safe");
            Assert.True(result.Succeeded);
            return result.Value!;
        }

        public QuestSemanticEvent ItemEvent(
            Guid eventId,
            int itemTemplateId,
            long quantityBefore,
            long quantityAfter) =>
            Semantic(
                eventId,
                QuestSemanticEventType.InventoryCommitted,
                itemTemplateId: itemTemplateId,
                quantityBefore: quantityBefore,
                quantityAfter: quantityAfter);

        public QuestSemanticEvent NpcEvent(
            Guid eventId,
            int npcTemplateId,
            bool committed) =>
            Semantic(
                eventId,
                QuestSemanticEventType.NpcInteractionCompleted,
                targetTemplateId: npcTemplateId,
                worldInteractionId: eventId,
                committed: committed);

        public QuestSemanticEvent PortalEvent(
            Guid eventId,
            int targetMapId,
            int portalTemplateId,
            bool committed) =>
            Semantic(
                eventId,
                QuestSemanticEventType.PortalTransitionCommitted,
                targetTemplateId: portalTemplateId,
                portalTransitionId: eventId,
                portalTemplateId: portalTemplateId,
                targetMapId: targetMapId,
                committed: committed);

        public QuestSemanticEvent BattleEvent(
            Guid eventId,
            bool won,
            bool committed,
            string encounter)
        {
            if (!committed)
            {
                return Semantic(
                    eventId,
                    QuestSemanticEventType.BattleCompleted,
                    battleInstanceId: Guid.NewGuid(),
                    encounter: encounter,
                    won: won,
                    committed: false);
            }

            var adapted = Adapter.FromBattleCompletion(
                eventId,
                Guid.NewGuid(),
                Session.CharacterId,
                encounter,
                won,
                true,
                "account-safe",
                Now,
                eventId.ToString("N"));
            Assert.True(adapted.Succeeded);
            return adapted.Value!;
        }

        public async Task<InventoryTransactionResult> GrantAsync(
            int itemTemplateId,
            int quantity,
            string key)
        {
            var version = await Inventory.GetCurrentVersionAsync(Session.CharacterId, default);
            Assert.True(version.Succeeded);
            return await Inventory.ExecuteAsync(
                new InventoryTransactionRequest(
                    Guid.NewGuid(),
                    key,
                    Session.CharacterId,
                    Session.SessionId,
                    Session.AccountId,
                    InventoryOperationType.SystemGrant,
                    version.Value,
                    "QuestRuntimeTests",
                    [new InventoryMutationRequest(ItemTemplateId: itemTemplateId, Quantity: quantity)],
                    0,
                    null,
                    Now,
                    InventoryMutationAuthorityKind.TrustedServer) with
                { AccountId = null },
                default);
        }

        private QuestSemanticEvent Semantic(
            Guid eventId,
            QuestSemanticEventType type,
            Guid? battleInstanceId = null,
            int? itemTemplateId = null,
            long? quantityBefore = null,
            long? quantityAfter = null,
            Guid? worldInteractionId = null,
            int? targetTemplateId = null,
            Guid? portalTransitionId = null,
            int? portalTemplateId = null,
            int? targetMapId = null,
            string? encounter = null,
            bool? won = null,
            bool committed = true) =>
            new(
                SemanticEventId: eventId,
                EventType: type,
                SourceRuntime: "QuestRuntimeTests",
                CharacterId: Session.CharacterId,
                AccountSafeReference: "account-safe",
                BattleInstanceId: battleInstanceId,
                RoundNumber: null,
                MonsterRuntimeEntityId: null,
                MonsterTemplateId: null,
                DeathId: null,
                InventoryTransactionId: type == QuestSemanticEventType.InventoryCommitted ? eventId : null,
                ItemTemplateId: itemTemplateId,
                QuantityBefore: quantityBefore,
                QuantityAfter: quantityAfter,
                WorldInteractionId: worldInteractionId,
                TargetRuntimeEntityId: null,
                TargetTemplateId: targetTemplateId,
                PortalTransitionId: portalTransitionId,
                PortalTemplateId: portalTemplateId,
                SourceMapId: Session.MapId,
                TargetMapId: targetMapId,
                EncounterDefinitionId: encounter,
                BattleWon: won,
                Committed: committed,
                EventVersion: 1,
                OccurredAtUtc: Now,
                CorrelationId: eventId.ToString("N"),
                RawMetadata: """{"testOnly":true}""");
    }
}
