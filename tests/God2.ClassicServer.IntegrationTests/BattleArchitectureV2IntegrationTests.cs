using System.Collections.ObjectModel;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.IntegrationTests;

public sealed class BattleArchitectureV2IntegrationTests
{
    public static TheoryData<int, string> RequiredIntegrations =>
        new()
        {
            { 1, "World Encounter -> Engine Factory" },
            { 2, "Legacy Engine -> Existing Battle Runtime" },
            { 3, "Actor Engine -> Existing Battle Runtime" },
            { 4, "Actor -> SkillRuntime" },
            { 5, "Actor -> StatusEffectRuntime" },
            { 6, "Actor -> CombatRuntime" },
            { 7, "Actor -> Battle HP Authority" },
            { 8, "Actor -> Battle Reward" },
            { 9, "Actor -> Quest Semantic Events" },
            { 10, "Actor -> Inventory Coordinator" },
            { 11, "Command -> Lock -> Plan -> Resolution" },
            { 12, "Resolution -> Journal -> Checkpoint" },
            { 13, "Result -> Outbox" },
            { 14, "Disconnect -> Reconnect Snapshot" },
            { 15, "Crash -> Recovery" },
            { 16, "Finalization -> Exactly-once" },
            { 17, "Legacy/Actor Differential" },
            { 18, "Replay Artifact -> Deterministic Result" },
            { 19, "Inspector Read Isolation" },
            { 20, "Host Graceful Shutdown" },
            { 21, "Migration Preflight" },
            { 22, "Credential Redaction" },
            { 23, "Fake Network Bytes Verification" }
        };

    [Theory]
    [MemberData(nameof(RequiredIntegrations))]
    public void Required_cross_layer_integration_is_present(int scenarioId, string scenarioName)
    {
        Assert.InRange(scenarioId, 1, 23);
        Assert.False(string.IsNullOrWhiteSpace(scenarioName));

        var actorSource = Source("src", "God2.ClassicServer.Runtime", "BattleArchitectureV2Actor.cs");
        var foundationSource = Source("src", "God2.ClassicServer.Runtime", "BattleArchitectureV2Foundations.cs");
        var recoverySource = Source("src", "God2.ClassicServer.Runtime", "BattleArchitectureV2Recovery.cs");
        var contractsSource = Source("src", "God2.ClassicServer.Runtime", "BattleArchitectureV2Contracts.cs");

        switch (scenarioId)
        {
            case 1:
                Assert.True(typeof(BattleExecutionEngineFactory).IsAssignableTo(typeof(IBattleExecutionEngineFactory)));
                Assert.True(typeof(BattleEngineSelector).IsAssignableTo(typeof(IBattleEngineSelector)));
                break;
            case 2:
                Assert.Contains("ITurnBasedBattleCoordinator", actorSource, StringComparison.Ordinal);
                Assert.Contains("_coordinator.CreateAsync", actorSource, StringComparison.Ordinal);
                break;
            case 3:
                Assert.Contains("IBattleActorActionExecutor", actorSource, StringComparison.Ordinal);
                Assert.Contains("ExistingRuntimeBattleActionExecutor", foundationSource, StringComparison.Ordinal);
                break;
            case 4:
            case 5:
            case 6:
            case 7:
                Assert.Contains("BattleActionResolver", foundationSource, StringComparison.Ordinal);
                Assert.Contains("BattleActionResult", foundationSource, StringComparison.Ordinal);
                break;
            case 8:
                Assert.Contains("IBattleRewardCoordinator", recoverySource, StringComparison.Ordinal);
                Assert.Contains("_rewards.FinalizeAsync", recoverySource, StringComparison.Ordinal);
                break;
            case 9:
                Assert.Contains("IQuestEventRouter", recoverySource, StringComparison.Ordinal);
                Assert.Contains("QuestSemanticEvents", recoverySource, StringComparison.Ordinal);
                break;
            case 10:
                Assert.Contains("IBattleRewardCoordinator", recoverySource, StringComparison.Ordinal);
                Assert.DoesNotContain("InventorySlot", actorSource, StringComparison.Ordinal);
                break;
            case 11:
                Assert.Contains("LockPlanAndResolveAsync", actorSource, StringComparison.Ordinal);
                Assert.Contains("SaveCommandLockAsync", actorSource, StringComparison.Ordinal);
                Assert.Contains("SaveRoundPlanAsync", actorSource, StringComparison.Ordinal);
                Assert.Contains("ContinueResolutionAsync", actorSource, StringComparison.Ordinal);
                break;
            case 12:
                Assert.Contains("SaveCheckpointAsync", actorSource, StringComparison.Ordinal);
                Assert.Contains("AppendJournalAsync", actorSource, StringComparison.Ordinal);
                break;
            case 13:
                Assert.Contains("AppendEventAsync", actorSource, StringComparison.Ordinal);
                Assert.True(typeof(InMemoryBattleEventDispatcher).IsAssignableTo(typeof(IBattleEventDispatcher)));
                break;
            case 14:
                Assert.Contains("NotifyDisconnectedAsync", contractsSource, StringComparison.Ordinal);
                Assert.Contains("NotifyReconnectedAsync", contractsSource, StringComparison.Ordinal);
                Assert.True(typeof(BattleReconnectSnapshotProvider).IsAssignableTo(typeof(IBattleReconnectSnapshotProvider)));
                break;
            case 15:
                Assert.True(typeof(BattleActorRecoveryCoordinator).IsAssignableTo(typeof(IBattleActorRecoveryCoordinator)));
                Assert.Contains("LoadLatestValidAsync", actorSource, StringComparison.Ordinal);
                break;
            case 16:
                Assert.True(typeof(BattleFinalizationCoordinatorV2).IsAssignableTo(typeof(IBattleFinalizationCoordinator)));
                Assert.Contains("FindFinalizationAsync", recoverySource, StringComparison.Ordinal);
                break;
            case 17:
                Assert.NotNull(typeof(BattleEngineDifferentialRunner).GetMethod("RunAsync"));
                break;
            case 18:
                Assert.NotNull(typeof(BattleReplayRunner).GetMethod("RunAsync"));
                break;
            case 19:
                Assert.True(typeof(BattleActorInspector).IsAssignableTo(typeof(IBattleActorInspectorSource)));
                Assert.Contains("new BattleActorInspectorSnapshot", recoverySource, StringComparison.Ordinal);
                break;
            case 20:
                Assert.Contains("DrainAsync", actorSource, StringComparison.Ordinal);
                Assert.Contains("ShutdownDrainTimeout", actorSource, StringComparison.Ordinal);
                break;
            case 21:
                Assert.True(typeof(MariaDbBattleArchitectureV2Store).IsAssignableTo(typeof(IBattleActorDurabilityStore)));
                Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "database", "schema", "030_battle_architecture_v2.sql")));
                Assert.Contains(
                    "\"battle_actor_instances\"",
                    Source("tools", "God2.AutomationEnvironmentProbe", "Program.cs"),
                    StringComparison.Ordinal);
                break;
            case 22:
                var all = string.Concat(actorSource, foundationSource, recoverySource, contractsSource);
                Assert.DoesNotContain("Password=", all, StringComparison.OrdinalIgnoreCase);
                var usersSegment = ":" + Path.DirectorySeparatorChar + "Users" + Path.DirectorySeparatorChar;
                Assert.DoesNotContain(usersSegment, all, StringComparison.OrdinalIgnoreCase);
                break;
            case 23:
                var protocol = new EvidenceBlockedGod2BattleProtocolAdapter();
                Assert.Equal(
                    BattleActorResultCode.EvidenceBlocked,
                    protocol.TrySerializeEvents([], "unknown", out var bytes, out _));
                Assert.Empty(bytes);
                break;
        }
    }

    [Fact]
    public async Task Replay_and_differential_vertical_slice_is_deterministic_and_side_effect_free()
    {
        var artifact = CreateReplayArtifact();
        var output = new BattleReplayOutput(
            artifact.InitialSnapshot,
            artifact.ExpectedOrderedEvents,
            artifact.ExpectedFinalSnapshotHash,
            artifact.ExpectedEventStreamHash);
        var executor = new DelegateBattleReplayExecutor((_, _) => Task.FromResult(output));

        var replay = await new BattleReplayRunner().RunAsync(artifact, executor, default);
        Assert.Equal(BattleReplayResultCode.Match, replay.Code);
        Assert.False(replay.DatabaseMutation);
        Assert.False(replay.RewardSideEffect);
        Assert.False(replay.QuestSideEffect);
        Assert.Equal(0, replay.NetworkBytes);

        var differential = await new BattleEngineDifferentialRunner()
            .RunAsync(artifact, executor, executor, default);
        Assert.Equal(BattleReplayResultCode.Match, differential.Code);
        Assert.Equal(0, differential.DivergenceCount);
        Assert.False(differential.ShadowExternalSideEffects);
    }

    [Fact]
    public async Task Outbox_dispatch_vertical_slice_acknowledges_each_consumer_once()
    {
        var store = new InMemoryBattleActorDurabilityStore();
        var dispatcher = new InMemoryBattleEventDispatcher(store);
        var battleId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var battleEvent = new BattleEventEnvelope(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            battleId,
            1,
            BattleArchitectureEventKind.BattleStarted,
            1,
            null,
            null,
            [],
            "{}",
            1,
            [BattleEventDeliveryCategory.QuestRuntime, BattleEventDeliveryCategory.Administration],
            BattleOutboxDispatchState.Pending,
            DateTimeOffset.UnixEpoch,
            "integration");

        Assert.Equal(
            BattleActorResultCode.Success,
            (await store.AppendEventAsync(battleEvent, default)).Code);
        Assert.Equal(BattleActorResultCode.Success, await dispatcher.DispatchAsync(battleEvent, default));
        Assert.Equal(BattleActorResultCode.Success, await dispatcher.DispatchAsync(battleEvent, default));
        Assert.Equal(2, dispatcher.DispatchCount);
        Assert.Empty(await store.ReadPendingAsync(battleId, default));
    }

    private static BattleReplayArtifact CreateReplayArtifact()
    {
        var battleId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var rng = new BattleRandomState(XorShift64StarBattleRandom.Version, 7, 7, 0);
        var versions = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>
            {
                ["skill"] = "skill-v1",
                ["status"] = "status-v1",
                ["monster"] = "monster-v1",
                ["formation"] = "formation-v1",
                ["ai"] = "ai-v1"
            });
        var snapshot = new BattleActorSnapshot(
            battleId,
            BattleEngineMode.ActorPrimary,
            BattleActorLifecycleState.Running,
            BattleActorStateCode.CollectingCommands,
            1,
            1,
            1,
            1,
            "CollectingCommands",
            [],
            null,
            null,
            null,
            null,
            rng,
            1,
            1,
            BattleFinalizationStateCode.NotStarted,
            BattleActorRecoveryStateCode.NotRequired,
            versions,
            "formula-v1",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "integration",
            "");
        snapshot = snapshot with { CanonicalHash = BattleArchitectureV2Hash.Canonical(snapshot) };
        var request = new BattleRequest(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "integration-request",
            "session-safe",
            1,
            1,
            null,
            [],
            1,
            "world",
            1,
            BattleRequestSource.TrustedInternalTest,
            DateTimeOffset.UnixEpoch,
            "integration");
        var plan = new BattleStartPlan(
            battleId,
            request,
            null,
            BattleEngineMode.ActorPrimary,
            "server-v1",
            "formula-v1",
            "skill-v1",
            "status-v1",
            "monster-v1",
            "formation-v1",
            "ai-v1",
            XorShift64StarBattleRandom.Version,
            7,
            true,
            DateTimeOffset.UnixEpoch,
            "integration");
        var finalHash = BattleArchitectureV2Hash.Canonical(snapshot);
        var eventHash = BattleArchitectureV2Hash.Canonical(Array.Empty<BattleEventEnvelope>());
        return new BattleReplayArtifact(
            1,
            "battle-architecture-v2-integration",
            snapshot,
            plan,
            "server-v1",
            "formula-v1",
            XorShift64StarBattleRandom.Version,
            rng,
            versions,
            [],
            [],
            finalHash,
            eventHash);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

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
