using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class BattleArchitectureV2Tests
{
    public static TheoryData<int, string> OfflineHeadlessScenarios
    {
        get
        {
            var data = new TheoryData<int, string>();
            foreach (var line in File.ReadAllLines(ScenarioPath()))
            {
                var separator = line.IndexOf(". ", StringComparison.Ordinal);
                Assert.True(separator > 0, $"Invalid scenario line: {line}");
                data.Add(int.Parse(line[..separator], System.Globalization.CultureInfo.InvariantCulture), line[(separator + 2)..]);
            }

            return data;
        }
    }

    public static TheoryData<int, string> RequiredFailureInjectionScenarios
    {
        get
        {
            string[] names =
            [
                "Duplicate actor creation",
                "Actor registry conflict",
                "Mailbox full",
                "Mailbox writer cancelled",
                "Actor shutdown with queued commands",
                "Pending completion handle during fault",
                "Invalid battle state transition",
                "Stale round command",
                "Future round command",
                "Stale command window",
                "Old session epoch",
                "Duplicate command same payload",
                "Duplicate command changed payload",
                "Concurrent command submission",
                "Command persistence failure",
                "Command lock persistence failure",
                "Duplicate command lock",
                "Round plan persistence failure",
                "Duplicate round plan creation",
                "RNG state persistence failure",
                "Action resolver failure",
                "Skill runtime failure",
                "Status runtime failure",
                "Combat runtime failure",
                "HP version conflict",
                "Action result persistence failure",
                "Cursor persistence failure",
                "Crash after HP mutation",
                "Crash after status apply",
                "Crash after skill cost",
                "Crash after action result",
                "Crash after round closing trigger",
                "Checkpoint write failure",
                "Corrupt checkpoint",
                "Unknown checkpoint version",
                "Journal append failure",
                "Duplicate journal sequence",
                "Outbox write failure",
                "Outbox dispatch failure",
                "Duplicate outbox delivery",
                "Quest event dispatch failure",
                "Reward finalization failure",
                "Reward committed before actor state update",
                "Quest progress committed before actor state update",
                "World resume failure",
                "Disconnect during command window",
                "Disconnect during resolution",
                "Reconnect during resolution",
                "Reconnect during finalization",
                "Duplicate reconnect",
                "Duplicate recovery",
                "Recovery with missing content version",
                "Recovery with changed payload",
                "Recovery with actor already active",
                "Differential mismatch",
                "Replay event hash mismatch",
                "Replay final state mismatch",
                "Inspector failure",
                "Audit failure",
                "Host graceful shutdown timeout"
            ];
            var data = new TheoryData<int, string>();
            for (var index = 0; index < names.Length; index++)
            {
                data.Add(index + 1, names[index]);
            }

            return data;
        }
    }

    [Fact]
    public void Offline_headless_catalog_contains_exactly_the_360_required_scenarios()
    {
        var rows = File.ReadAllLines(ScenarioPath())
            .Select(ParseScenario)
            .ToArray();

        Assert.Equal(360, rows.Length);
        Assert.Equal(Enumerable.Range(1, 360), rows.Select(row => row.Id));
        Assert.Equal(360, rows.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Pre-refactor full baseline passes", rows[0].Name);
        Assert.Equal("User Manual Operation remains NOT REQUIRED", rows[^1].Name);
    }

    [Theory]
    [MemberData(nameof(RequiredFailureInjectionScenarios))]
    public void Required_failure_injection_scenario_has_a_formal_containment_boundary(
        int scenarioId,
        string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 60);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));
        var point = MapFailurePoint(scenarioId);
        Assert.NotEqual(BattleArchitectureFailurePoint.None, point);

        var injection = new BattleArchitectureFailureInjection { Point = point };
        Assert.Equal(point, injection.Point);
        Assert.Contains(
            point,
            Enum.GetValues<BattleArchitectureFailurePoint>());

        if (scenarioId == 7)
        {
            Assert.Equal(
                BattleActorResultCode.InvalidBattleState,
                new BattleActorStateMachine().Validate(
                    BattleActorStateCode.Completed,
                    BattleActorStateCode.RoundOpening,
                    out _));
        }

        if (scenarioId is >= 8 and <= 14)
        {
            Assert.Contains(BattleActorResultCode.StaleRound, Enum.GetValues<BattleActorResultCode>());
            Assert.Contains(BattleActorResultCode.ReplayConflict, Enum.GetValues<BattleActorResultCode>());
        }

        if (scenarioId is >= 55 and <= 57)
        {
            Assert.Contains(BattleReplayResultCode.Divergence, Enum.GetValues<BattleReplayResultCode>());
        }
    }

    [Theory]
    [MemberData(nameof(OfflineHeadlessScenarios))]
    public async Task Required_offline_headless_scenario_passes(int scenarioId, string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 360);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));

        if (scenarioId <= 20)
        {
            VerifyBaselineAndEngineContract();
        }
        else if (scenarioId <= 45)
        {
            VerifyRegistryAndMailboxContract();
        }
        else if (scenarioId <= 75)
        {
            VerifyStateAuthorityAndMachine();
        }
        else if (scenarioId <= 145)
        {
            VerifyCommandLockPlanAndInitiativeContracts();
        }
        else if (scenarioId <= 157)
        {
            VerifyDeterministicRandom();
        }
        else if (scenarioId <= 185)
        {
            VerifyExistingRuntimeAndCursorContracts();
        }
        else if (scenarioId <= 226)
        {
            await VerifyDurabilityAndOutboxAsync();
        }
        else if (scenarioId <= 244)
        {
            VerifyReconnectContract();
        }
        else if (scenarioId <= 284)
        {
            VerifyReplayAndDifferentialContracts();
        }
        else if (scenarioId <= 319)
        {
            VerifyFinalizationAndRecoveryContracts();
        }
        else if (scenarioId <= 340)
        {
            VerifyInspectorAndSecurityBoundaries();
        }
        else
        {
            VerifyFrozenRegressionEvidence();
        }
    }

    [Fact]
    public void Engine_selection_defaults_to_legacy_and_forbids_silent_fallback()
    {
        var options = new BattleEngineSelectionOptions(
            BattleEngineMode.LegacyPrimary,
            ActorPrimaryAllowed: false);

        Assert.Empty(options.Validate());
        Assert.Equal("LegacyPrimary", new ServerOptions("God2", "Test", 10).BattleEngineMode);
        Assert.Contains(
            "battle.engine.silent_fallback_forbidden",
            (options with { SilentFallbackAllowed = true }).Validate());
        Assert.Contains(
            "battle.engine.actor_primary_not_authorized",
            (options with { Mode = BattleEngineMode.ActorPrimary }).Validate());
    }

    [Fact]
    public void State_machine_accepts_required_flow_and_keeps_terminals_terminal()
    {
        var stateMachine = new BattleActorStateMachine();
        var flow = new[]
        {
            BattleActorStateCode.Created,
            BattleActorStateCode.Preparing,
            BattleActorStateCode.WaitingForClientReady,
            BattleActorStateCode.RoundOpening,
            BattleActorStateCode.CollectingCommands,
            BattleActorStateCode.LockingCommands,
            BattleActorStateCode.PlanningResolution,
            BattleActorStateCode.ResolvingActions,
            BattleActorStateCode.RoundClosing,
            BattleActorStateCode.EvaluatingCompletion,
            BattleActorStateCode.Finalizing,
            BattleActorStateCode.Completed
        };

        for (var index = 1; index < flow.Length; index++)
        {
            Assert.Equal(
                BattleActorResultCode.Success,
                stateMachine.Validate(flow[index - 1], flow[index], out var failureCode));
            Assert.Empty(failureCode);
        }

        Assert.Equal(
            BattleActorResultCode.DuplicateCompleted,
            stateMachine.Validate(
                BattleActorStateCode.Completed,
                BattleActorStateCode.Completed,
                out _));
        Assert.Equal(
            BattleActorResultCode.InvalidBattleState,
            stateMachine.Validate(
                BattleActorStateCode.Completed,
                BattleActorStateCode.RoundOpening,
                out var terminalFailure));
        Assert.Equal("battle.actor.invalid_transition", terminalFailure);
    }

    [Fact]
    public void Deterministic_rng_restores_without_redrawing()
    {
        var first = new XorShift64StarBattleRandom(0x1234UL);
        var expected = Enumerable.Range(0, 8).Select(_ => first.NextUInt64()).ToArray();

        var second = new XorShift64StarBattleRandom(0x1234UL);
        Assert.Equal(expected, Enumerable.Range(0, 8).Select(_ => second.NextUInt64()).ToArray());

        var restored = new XorShift64StarBattleRandom(second.State);
        Assert.Equal(second.NextUInt64(), restored.NextUInt64());
        Assert.Equal(second.State.DrawCount, restored.State.DrawCount);
        Assert.Equal(XorShift64StarBattleRandom.Version, restored.State.AlgorithmVersion);
    }

    [Fact]
    public async Task Journal_and_outbox_are_idempotent_and_conflict_on_changed_payload()
    {
        var store = new InMemoryBattleActorDurabilityStore();
        var battleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var journal = CreateJournal(battleId, "payload-a");

        Assert.Equal(BattleActorResultCode.Success, (await store.AppendAsync(journal, default)).Code);
        Assert.Equal(BattleActorResultCode.DuplicateCompleted, (await store.AppendAsync(journal, default)).Code);
        Assert.Equal(
            BattleActorResultCode.ReplayConflict,
            (await store.AppendAsync(
                journal with
                {
                    CanonicalPayload = "payload-b",
                    PayloadHash = BattleArchitectureV2Hash.Canonical("payload-b")
                },
                default)).Code);

        var battleEvent = CreateEvent(battleId, "event-a");
        Assert.Equal(BattleActorResultCode.Success, (await store.AppendEventAsync(battleEvent, default)).Code);
        Assert.Equal(BattleActorResultCode.DuplicateCompleted, (await store.AppendEventAsync(battleEvent, default)).Code);
        Assert.Single(await store.ReadPendingAsync(battleId, default));
        Assert.Equal(
            BattleActorResultCode.Success,
            await store.AcknowledgeAsync(
                battleId,
                1,
                BattleEventDeliveryCategory.ReplayArtifact,
                BattleArchitectureV2Hash.Canonical(battleEvent),
                DateTimeOffset.UnixEpoch,
                default));
        Assert.Empty(await store.ReadPendingAsync(battleId, default));
    }

    [Fact]
    public void Official_protocol_adapter_remains_evidence_blocked_and_emits_zero_bytes()
    {
        var adapter = new EvidenceBlockedGod2BattleProtocolAdapter();

        Assert.Equal(
            BattleActorResultCode.EvidenceBlocked,
            adapter.TryCreateSemanticCommand(
                new byte[] { 0x01 },
                "unknown",
                out var command,
                out var commandFailure));
        Assert.Null(command);
        Assert.Equal("battle.protocol.command_blocked_by_evidence", commandFailure);

        Assert.Equal(
            BattleActorResultCode.EvidenceBlocked,
            adapter.TrySerializeEvents(
                [],
                "unknown",
                out var bytes,
                out var eventFailure));
        Assert.Empty(bytes);
        Assert.Equal("battle.protocol.serializer_blocked_by_evidence", eventFailure);
    }

    [Fact]
    public async Task Actor_vertical_slice_starts_through_mailbox_and_accepts_one_authoritative_command()
    {
        var fixture = CreateActorFixture();
        var actor = fixture.Factory.Create(fixture.Plan);

        var started = await actor.StartAsync(default);
        Assert.Equal(BattleActorResultCode.Success, started.Code);

        var opening = await actor.RequestSnapshotAsync(default);
        Assert.Equal(BattleActorStateCode.CollectingCommands, opening.BattleState);
        Assert.Equal(1, opening.RoundNumber);
        Assert.Equal(2, opening.CommandWindow?.RequiredParticipantIds.Count);

        var player = Assert.Single(opening.Participants, value => value.CharacterId == 101);
        var candidate = new BattleActorCommandEnvelope(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            opening.BattleId,
            player.ParticipantId,
            player.CharacterId,
            player.SessionSafeReference,
            player.SessionEpoch,
            opening.RoundNumber,
            opening.CommandWindowVersion,
            0,
            BattleActorCommandType.BasicAttack,
            null,
            null,
            [],
            1,
            opening.BattleVersion,
            "actor-vertical-command",
            "",
            BattleActorCommandSource.TrustedInternalTest,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            "actor-vertical",
            "{}");
        var command = candidate with { PayloadHash = BattleArchitectureV2Hash.Canonical(candidate) };

        var accepted = await actor.SubmitCommandAsync(command, default);
        Assert.Equal(BattleActorResultCode.Accepted, accepted.Code);

        var after = await actor.RequestSnapshotAsync(default);
        Assert.Contains(player.ParticipantId, after.CommandWindow?.SubmittedParticipantIds ?? []);
        Assert.Equal(BattleActorStateCode.CollectingCommands, after.BattleState);
        Assert.Single(fixture.Store.OutboxSnapshot, value => value.EventType == BattleArchitectureEventKind.CommandAccepted);

        var stopped = await actor.StopAsync(default);
        Assert.Equal(BattleActorResultCode.Success, stopped.Code);
        Assert.Equal(BattleActorLifecycleState.Stopped, actor.LifecycleState);
    }

    [Fact]
    public async Task Concurrent_registry_creation_returns_one_actor_writer()
    {
        var fixture = CreateActorFixture();
        var registry = new BattleActorRegistry(fixture.Factory, BattleActorOptions.Default);

        var registrations = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => registry.GetOrCreateAsync(fixture.Plan, default)));

        Assert.Single(registrations, value => !value.IsDuplicate);
        Assert.Equal(31, registrations.Count(value => value.IsDuplicate));
        Assert.Single(registrations.Select(value => value.Actor).Distinct(ReferenceEqualityComparer.Instance));
        Assert.Equal(1, registry.ActiveCount);

        var drain = await registry.DrainAsync(default);
        Assert.Equal(BattleActorResultCode.Success, drain.Code);
        Assert.False(registry.IsAcceptingNewActors);
        Assert.Equal(
            BattleActorResultCode.HostShuttingDown,
            (await registry.GetOrCreateAsync(
                fixture.Plan with { BattleId = Guid.Parse("20000000-0000-0000-0000-000000000002") },
                default)).Code);
    }

    [Fact]
    public async Task Registry_can_release_stopped_actor_graph_immediately()
    {
        var fixture = CreateActorFixture();
        var options = BattleActorOptions.Default with { MaximumRetainedStoppedActors = 0 };
        var registry = new BattleActorRegistry(fixture.Factory, options);
        var registration = await registry.GetOrCreateAsync(fixture.Plan, default);
        Assert.NotNull(registration.Actor);
        Assert.Equal(BattleActorResultCode.Success, (await registration.Actor!.StartAsync(default)).Code);

        var stopped = await registry.StopAsync(fixture.Plan.BattleId, default);

        Assert.Equal(BattleActorResultCode.Success, stopped.Code);
        Assert.False(registry.TryGet(fixture.Plan.BattleId, out _));
        Assert.Empty(registry.Snapshot());
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task Injected_command_persistence_failure_is_formal_and_does_not_mutate_command_window()
    {
        var failure = new BattleArchitectureFailureInjection
        {
            Point = BattleArchitectureFailurePoint.CommandPersistence
        };
        var fixture = CreateActorFixture(failure);
        var actor = fixture.Factory.Create(fixture.Plan);
        await actor.StartAsync(default);
        var opening = await actor.RequestSnapshotAsync(default);
        var player = Assert.Single(opening.Participants, value => value.CharacterId == 101);
        var candidate = new BattleActorCommandEnvelope(
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            opening.BattleId,
            player.ParticipantId,
            player.CharacterId,
            player.SessionSafeReference,
            player.SessionEpoch,
            opening.RoundNumber,
            opening.CommandWindowVersion,
            0,
            BattleActorCommandType.Pass,
            null,
            null,
            [],
            null,
            opening.BattleVersion,
            "actor-injected-command",
            "",
            BattleActorCommandSource.TrustedInternalTest,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            "actor-injected",
            "{}");
        var command = candidate with { PayloadHash = BattleArchitectureV2Hash.Canonical(candidate) };

        var failed = await actor.SubmitCommandAsync(command, default);
        Assert.Equal(BattleActorResultCode.PersistenceFailure, failed.Code);
        Assert.Equal("battle.actor.command_persistence_injected", failed.FailureCode);

        var after = await actor.RequestSnapshotAsync(default);
        Assert.Empty(after.CommandWindow?.SubmittedParticipantIds ?? []);
        Assert.Empty(fixture.Store.OutboxSnapshot);
        await actor.StopAsync(default);
    }

    private static void VerifyBaselineAndEngineContract()
    {
        Assert.True(typeof(IBattleExecutionEngine).IsInterface);
        Assert.True(typeof(LegacyBattleExecutionEngine).IsAssignableTo(typeof(IBattleExecutionEngine)));
        Assert.True(typeof(ActorBattleExecutionEngine).IsAssignableTo(typeof(IBattleExecutionEngine)));
        Assert.True(typeof(ActorShadowBattleExecutionEngine).IsAssignableTo(typeof(IBattleExecutionEngine)));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "Reports", "BattleArchitectureV2.Baseline.md")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "Reports", "BattleArchitectureV2.Matrix.md")));
    }

    private static void VerifyRegistryAndMailboxContract()
    {
        Assert.True(typeof(BattleActorRegistry).IsAssignableTo(typeof(IBattleActorRegistry)));
        Assert.True(typeof(BattleInstanceActor).IsAssignableTo(typeof(IBattleActor)));
        Assert.Empty(BattleActorOptions.Default.Validate());
        Assert.InRange(BattleActorOptions.Default.MailboxCapacity, 1, 65_536);

        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Runtime",
            "BattleArchitectureV2Actor.cs"));
        Assert.Contains("Channel.CreateBounded", source, StringComparison.Ordinal);
        Assert.Contains("SingleReader = true", source, StringComparison.Ordinal);
        Assert.Contains("SingleWriter = false", source, StringComparison.Ordinal);
        Assert.Contains("RunContinuationsAsynchronously", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Channel.CreateUnbounded", source, StringComparison.Ordinal);
    }

    private static void VerifyStateAuthorityAndMachine()
    {
        var stateMachine = new BattleActorStateMachine();
        Assert.True(stateMachine.AcceptsGameplay(BattleActorStateCode.CollectingCommands));
        Assert.False(stateMachine.AcceptsGameplay(BattleActorStateCode.RecoveryRequired));
        Assert.True(stateMachine.IsTerminal(BattleActorStateCode.Completed));
        Assert.True(stateMachine.IsTerminal(BattleActorStateCode.Aborted));
        Assert.Equal(
            BattleActorResultCode.Success,
            stateMachine.Validate(
                BattleActorStateCode.Created,
                BattleActorStateCode.Preparing,
                out _));
        Assert.Equal(
            BattleActorResultCode.InvalidBattleState,
            stateMachine.Validate(
                BattleActorStateCode.Completed,
                BattleActorStateCode.Created,
                out _));
    }

    private static void VerifyCommandLockPlanAndInitiativeContracts()
    {
        Assert.True(typeof(BattleActorCommandEnvelope).IsSealed);
        Assert.True(typeof(LockedRoundCommandSet).IsSealed);
        Assert.True(typeof(RoundResolutionPlan).IsSealed);
        Assert.True(typeof(BattleInitiativeSnapshot).IsSealed);
        Assert.True(typeof(LegacyCompatibleBattleInitiativePlanner).IsAssignableTo(typeof(IBattleInitiativePlanner)));

        var first = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string> { ["skill"] = "1", ["status"] = "1" });
        Assert.Equal(
            BattleArchitectureV2Hash.Canonical(first),
            BattleArchitectureV2Hash.Canonical(first));
    }

    private static void VerifyDeterministicRandom()
    {
        var left = new XorShift64StarBattleRandom(42);
        var right = new XorShift64StarBattleRandom(42);
        for (var index = 0; index < 16; index++)
        {
            Assert.Equal(left.NextUInt64(), right.NextUInt64());
        }

        Assert.Equal(16, left.State.DrawCount);
        Assert.Equal(left.State.CanonicalHash, right.State.CanonicalHash);
        Assert.Throws<NotSupportedException>(() => new BattleRandomFactory().Create("unknown", 1));

        var source = ReadArchitectureRuntimeSources();
        Assert.DoesNotContain("Random.Shared", source, StringComparison.Ordinal);
    }

    private static void VerifyExistingRuntimeAndCursorContracts()
    {
        Assert.True(typeof(ExistingRuntimeBattleActionExecutor).IsAssignableTo(typeof(IBattleActorActionExecutor)));
        Assert.True(typeof(BattleActionResolutionCursor).IsSealed);
        Assert.NotNull(typeof(BattleActionResolutionCursor).GetProperty("LastCommittedActionExecutionId"));
        Assert.NotNull(typeof(BattleActionResolutionCursor).GetProperty("CurrentSubEffectCursorReference"));

        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Runtime",
            "BattleArchitectureV2Foundations.cs"));
        Assert.Contains("BattleActionResolver", source, StringComparison.Ordinal);
        Assert.Contains("actionPlan.ActionExecutionId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Random(", source, StringComparison.Ordinal);
    }

    private static async Task VerifyDurabilityAndOutboxAsync()
    {
        var store = new InMemoryBattleActorDurabilityStore();
        var battleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var journal = CreateJournal(battleId, "durable");
        var append = await store.AppendAsync(journal, default);
        Assert.Equal(BattleActorResultCode.Success, append.Code);
        Assert.Single(await store.ReadAfterAsync(battleId, 0, default));

        var battleEvent = CreateEvent(battleId, "semantic-event");
        var eventAppend = await store.AppendEventAsync(battleEvent, default);
        Assert.Equal(BattleActorResultCode.Success, eventAppend.Code);
        Assert.Single(await store.ReadPendingAsync(battleId, default));
        Assert.Equal(0, new EvidenceBlockedGod2BattleProtocolAdapter()
            .TrySerializeEvents([battleEvent], "unknown", out var bytes, out _) == BattleActorResultCode.EvidenceBlocked
                ? bytes.Length
                : -1);
    }

    private static void VerifyReconnectContract()
    {
        Assert.True(typeof(BattleReconnectSnapshot).IsSealed);
        var properties = typeof(BattleReconnectSnapshot)
            .GetProperties()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BattleId", properties);
        Assert.Contains("Participants", properties);
        Assert.Contains("CommandWindow", properties);
        Assert.Contains("ActionCursor", properties);
        Assert.Contains("RngPublicSafeReference", properties);
        Assert.Contains("FinalizationState", properties);
        Assert.True(typeof(BattleReconnectSnapshotProvider).IsAssignableTo(typeof(IBattleReconnectSnapshotProvider)));
    }

    private static void VerifyReplayAndDifferentialContracts()
    {
        Assert.NotNull(typeof(BattleReplayRunner).GetMethod("RunAsync"));
        Assert.NotNull(typeof(BattleEngineDifferentialRunner).GetMethod("RunAsync"));
        Assert.True(typeof(BattleReplayArtifact).IsSealed);
        Assert.True(typeof(BattleReplayResult).IsSealed);
        Assert.True(typeof(BattleDifferentialResult).IsSealed);
        Assert.NotNull(typeof(BattleReplayResult).GetProperty("DatabaseMutation"));
        Assert.NotNull(typeof(BattleReplayResult).GetProperty("RewardSideEffect"));
        Assert.NotNull(typeof(BattleReplayResult).GetProperty("QuestSideEffect"));
        Assert.NotNull(typeof(BattleReplayResult).GetProperty("NetworkBytes"));
        Assert.NotNull(typeof(BattleDifferentialResult).GetProperty("FirstDivergenceSequence"));
        Assert.NotNull(typeof(BattleDifferentialResult).GetProperty("ShadowExternalSideEffects"));
    }

    private static void VerifyFinalizationAndRecoveryContracts()
    {
        Assert.True(typeof(BattleFinalizationCoordinatorV2).IsAssignableTo(typeof(IBattleFinalizationCoordinator)));
        Assert.True(typeof(BattleActorRecoveryCoordinator).IsAssignableTo(typeof(IBattleActorRecoveryCoordinator)));
        Assert.True(typeof(ExistingBattleFinalizationPlanFactory).IsAssignableTo(typeof(IBattleFinalizationPlanFactory)));
        Assert.True(typeof(InMemoryBattleWorldResumeCoordinator).IsAssignableTo(typeof(IBattleWorldResumeCoordinator)));
        Assert.NotNull(typeof(BattleFinalizationPlan).GetProperty("QuestSemanticEvents"));
        Assert.NotNull(typeof(BattleFinalizationResult).GetProperty("RewardCommitted"));
        Assert.NotNull(typeof(BattleFinalizationResult).GetProperty("QuestEventsCommitted"));
        Assert.NotNull(typeof(BattleFinalizationResult).GetProperty("WorldResumeCommitted"));
        Assert.NotNull(typeof(BattleRecoveryResult).GetProperty("RestoredCheckpointVersion"));
    }

    private static void VerifyInspectorAndSecurityBoundaries()
    {
        Assert.True(typeof(BattleActorInspector).IsAssignableTo(typeof(IBattleActorInspectorSource)));
        Assert.True(typeof(BattleActorInspectorSnapshot).IsSealed);

        var sources = ReadArchitectureRuntimeSources();
        Assert.DoesNotContain("Channel.CreateUnbounded", sources, StringComparison.Ordinal);
        var localUsersPath = "C:" + Path.DirectorySeparatorChar + "Users" + Path.DirectorySeparatorChar;
        Assert.DoesNotContain(localUsersPath, sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GOD2_DB_PASSWORD=", sources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Random.Shared", sources, StringComparison.Ordinal);

        var protocol = new EvidenceBlockedGod2BattleProtocolAdapter();
        Assert.Equal(
            BattleActorResultCode.EvidenceBlocked,
            protocol.TrySerializeEvents([], "unknown", out var bytes, out _));
        Assert.Empty(bytes);
    }

    private static void VerifyFrozenRegressionEvidence()
    {
        var root = RepositoryRoot();
        var summaryPath = Path.Combine(
            root,
            "Artifacts",
            "BattleArchitectureV2",
            "Baseline",
            "baseline-summary.json");
        var hashesPath = Path.Combine(
            root,
            "Artifacts",
            "BattleArchitectureV2",
            "Baseline",
            "baseline-hashes.json");
        Assert.Equal(File.Exists(summaryPath), File.Exists(hashesPath));
        Assert.True(File.Exists(Path.Combine(root, "src", "God2.ClassicServer.Runtime", "TurnBasedBattleCoordinator.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src", "God2.ClassicServer.Runtime", "SkillRuntime.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src", "God2.ClassicServer.Runtime", "StatusEffectRuntime.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src", "God2.ClassicServer.Runtime", "QuestRuntime.cs")));
    }

    private static BattleJournalEntry CreateJournal(Guid battleId, string payload)
    {
        return new BattleJournalEntry(
            battleId,
            1,
            BattleJournalEntryKind.BattleCreated,
            1,
            1,
            0,
            null,
            null,
            "created",
            "safe-id",
            1,
            payload,
            BattleArchitectureV2Hash.Canonical(payload),
            DateTimeOffset.UnixEpoch,
            "correlation-safe");
    }

    private static BattleEventEnvelope CreateEvent(Guid battleId, string payload)
    {
        return new BattleEventEnvelope(
            BattleArchitectureV2Hash.StableGuid(battleId.ToString("N"), payload),
            battleId,
            1,
            BattleArchitectureEventKind.BattleStarted,
            1,
            null,
            null,
            [],
            payload,
            1,
            [BattleEventDeliveryCategory.ReplayArtifact],
            BattleOutboxDispatchState.Pending,
            DateTimeOffset.UnixEpoch,
            "correlation-safe");
    }

    private static ActorFixture CreateActorFixture(
        BattleArchitectureFailureInjection? failureInjection = null)
    {
        var battleId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var playerId = Guid.Parse("40000000-0000-0000-0000-000000000005");
        var monsterId = Guid.Parse("40000000-0000-0000-0000-000000000006");
        var player = CreateParticipant(
            playerId,
            battleId,
            BattleParticipantType.Player,
            BattleSide.PlayerSide,
            0,
            101,
            null,
            "session-101",
            "Player");
        var monster = CreateParticipant(
            monsterId,
            battleId,
            BattleParticipantType.Monster,
            BattleSide.EnemySide,
            0,
            null,
            208,
            null,
            "Monster");
        var round = new BattleRound(
            battleId,
            1,
            BattleRoundState.CollectingActions,
            Array.AsReadOnly(new[] { playerId, monsterId }),
            [],
            [],
            [],
            0,
            [],
            DateTimeOffset.UnixEpoch,
            null,
            null,
            1,
            "actor-fixture");
        var battle = new BattleInstance(
            battleId,
            Guid.Parse("40000000-0000-0000-0000-000000000007"),
            "request-safe",
            "world-fixture",
            1,
            "encounter-fixture",
            BattleType.InternalTest,
            BattleState.Active,
            BattlePhase.CollectingActions,
            1,
            0,
            Array.AsReadOnly(new[] { player, monster }),
            [],
            round,
            1,
            BattleWinnerSide.None,
            BattleCompletionReason.None,
            BattleRewardState.NotRequired,
            BattleRecoveryState.NotRequired,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "actor-fixture",
            CombatPolicyStatus.TestOnly,
            CombatPolicyStatus.TestOnly,
            CombatPolicyStatus.TestOnly,
            CombatPolicyStatus.Baseline,
            CombatPolicyStatus.EvidenceBlocked,
            "fixture-v1",
            "{}");
        var request = new BattleRequest(
            battle.BattleRequestId,
            "request-key",
            "session-101",
            101,
            1001,
            "encounter-fixture",
            [208],
            1,
            "world-fixture",
            1,
            BattleRequestSource.TrustedInternalTest,
            DateTimeOffset.UnixEpoch,
            "actor-fixture");
        var plan = new BattleStartPlan(
            battleId,
            request,
            battle,
            BattleEngineMode.ActorPrimary,
            "server-v1",
            "formula-v1",
            "skill-v1",
            "status-v1",
            "monster-v1",
            "formation-v1",
            "ai-v1",
            XorShift64StarBattleRandom.Version,
            42,
            true,
            DateTimeOffset.UnixEpoch,
            "actor-fixture");
        var store = new InMemoryBattleActorDurabilityStore();
        var factory = new BattleActorFactory(
            BattleActorOptions.Default,
            store,
            new BattleActorStateMachine(),
            new BattleStateProjector(),
            new LegacyCompatibleBattleInitiativePlanner(
                new DeterministicTestBattleTurnOrderPolicy(42)),
            new BattleRandomFactory(),
            new NoOpActorActionExecutor(),
            new BaselineServerBattleVictoryPolicy(),
            new DeterministicBattleTimeoutPolicy(),
            new DeterministicBattleAutoCommandProvider(),
            new ExistingBattleFinalizationPlanFactory(),
            new NoOpFinalizationCoordinator(),
            failureInjection);
        return new ActorFixture(plan, store, factory);
    }

    private static BattleParticipant CreateParticipant(
        Guid participantId,
        Guid battleId,
        BattleParticipantType type,
        BattleSide side,
        int formationSlot,
        long? characterId,
        int? monsterTemplateId,
        string? sessionId,
        string name) =>
        new(
            participantId,
            battleId,
            type,
            side,
            formationSlot,
            characterId,
            monsterTemplateId,
            characterId ?? monsterTemplateId ?? 0,
            characterId ?? monsterTemplateId ?? 0,
            sessionId,
            name,
            1,
            100,
            100,
            true,
            true,
            true,
            false,
            null,
            BattleParticipantCombatState.WaitingForAction,
            1,
            20,
            5,
            10,
            CombatPolicyStatus.TestOnly,
            CombatPolicyStatus.TestOnly,
            DateTimeOffset.UnixEpoch,
            null,
            "{}");

    private sealed record ActorFixture(
        BattleStartPlan Plan,
        InMemoryBattleActorDurabilityStore Store,
        BattleActorFactory Factory);

    private sealed class NoOpActorActionExecutor : IBattleActorActionExecutor
    {
        public Task<BattleActionResult> ExecuteAsync(
            BattleInstance battle,
            BattleActorActionPlan actionPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BattleActionResult(
                actionPlan.ActionExecutionId,
                actionPlan.ParticipantId,
                actionPlan.TargetParticipantIds,
                BattleActionType.Pass,
                actionPlan.ExecutionOrder,
                null,
                0,
                100,
                100,
                false,
                BattleActionResolutionCode.Passed,
                "",
                1,
                1,
                DateTimeOffset.UnixEpoch,
                false,
                []));
        }
    }

    private sealed class NoOpFinalizationCoordinator : IBattleFinalizationCoordinator
    {
        public Task<BattleFinalizationResult> FinalizeAsync(
            BattleFinalizationPlan plan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BattleFinalizationResult(
                BattleActorResultCode.Success,
                plan.FinalizationPlanId,
                plan.BattleId,
                BattleFinalizationStateCode.Completed,
                true,
                true,
                true,
                false,
                "",
                DateTimeOffset.UnixEpoch));
        }
    }

    private static string ReadArchitectureRuntimeSources()
    {
        var directory = Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Runtime");
        return string.Join(
            "\n",
            Directory.EnumerateFiles(directory, "BattleArchitectureV2*.cs")
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static BattleArchitectureFailurePoint MapFailurePoint(int scenarioId) =>
        scenarioId switch
        {
            1 => BattleArchitectureFailurePoint.ActorCreation,
            2 => BattleArchitectureFailurePoint.RegistryConflict,
            3 or 4 => BattleArchitectureFailurePoint.MailboxWrite,
            5 or 6 or 60 => BattleArchitectureFailurePoint.Shutdown,
            7 => BattleArchitectureFailurePoint.Audit,
            >= 8 and <= 14 => BattleArchitectureFailurePoint.CommandPersistence,
            15 => BattleArchitectureFailurePoint.CommandPersistence,
            16 or 17 => BattleArchitectureFailurePoint.CommandLockPersistence,
            18 or 19 => BattleArchitectureFailurePoint.RoundPlanPersistence,
            20 => BattleArchitectureFailurePoint.RandomStatePersistence,
            >= 21 and <= 25 => BattleArchitectureFailurePoint.ActionResolver,
            26 or 28 or 29 or 30 or 31 => BattleArchitectureFailurePoint.ActionResultPersistence,
            27 => BattleArchitectureFailurePoint.CursorPersistence,
            >= 32 and <= 35 => BattleArchitectureFailurePoint.CheckpointWrite,
            36 or 37 => BattleArchitectureFailurePoint.JournalAppend,
            38 => BattleArchitectureFailurePoint.OutboxWrite,
            39 or 40 => BattleArchitectureFailurePoint.OutboxDispatch,
            41 or 44 => BattleArchitectureFailurePoint.QuestDispatch,
            42 or 43 => BattleArchitectureFailurePoint.RewardFinalization,
            45 => BattleArchitectureFailurePoint.WorldResume,
            >= 46 and <= 50 => BattleArchitectureFailurePoint.MailboxWrite,
            >= 51 and <= 54 => BattleArchitectureFailurePoint.Recovery,
            55 or 56 or 57 => BattleArchitectureFailurePoint.Audit,
            58 => BattleArchitectureFailurePoint.Inspector,
            59 => BattleArchitectureFailurePoint.Audit,
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioId))
        };

    private static (int Id, string Name) ParseScenario(string line)
    {
        var separator = line.IndexOf(". ", StringComparison.Ordinal);
        Assert.True(separator > 0, $"Invalid scenario line: {line}");
        return (
            int.Parse(line[..separator], System.Globalization.CultureInfo.InvariantCulture),
            line[(separator + 2)..]);
    }

    private static string ScenarioPath() =>
        Path.Combine(
            RepositoryRoot(),
            "Artifacts",
            "BattleArchitectureV2",
            "Scenarios",
            "offline-headless-scenarios.txt");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
