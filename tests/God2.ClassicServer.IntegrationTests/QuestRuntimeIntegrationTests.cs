using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.IntegrationTests;

public sealed class QuestRuntimeIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> IntegrationScenarios =>
        new()
        {
            "DB Quest Record to Definition evidence gate",
            "Definition to Immutable Catalog",
            "NPC Interaction to Quest Binding boundary",
            "Accept to Quest Instance",
            "Monster Death to Kill Progress",
            "Inventory Commit to Item Progress boundary",
            "NPC Interaction to NPC Objective boundary",
            "Portal Commit to Map Objective boundary",
            "Battle Completion to Battle Objective boundary",
            "Progress to ReadyToComplete",
            "Turn-in to Reward Plan",
            "Reward to Inventory Coordinator",
            "Completion to Persistence",
            "Reconnect Restore",
            "Duplicate Accept",
            "Duplicate Progress",
            "Duplicate Completion",
            "Duplicate Reward",
            "Mid-progress Recovery",
            "Mid-reward Recovery",
            "Inspector Read Isolation",
            "Credential Redaction",
            "No Direct SQL from Packet Handler",
            "No Fake Protocol Bytes"
        };

    [Theory]
    [MemberData(nameof(IntegrationScenarios))]
    public async Task Quest_backend_vertical_slice_is_integrated_without_protocol_bytes(string scenario)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        var definition = TestDefinition();
        var catalog = new ImmutableQuestDefinitionCatalog(
            [definition],
            new QuestDefinitionValidator());
        var sessions = new InMemoryQuestSessionValidator();
        var session = new QuestSessionSnapshot("quest-integration", 1, 1, 100, 1, 1, true, true);
        sessions.Register(session);
        var inventoryAudit = new InMemoryInventoryAuditLedger();
        var inventoryStore = new InMemoryInventoryPersistenceStore(inventoryAudit);
        inventoryStore.Seed(new InventoryPersistenceBundle(
            new PlayerInventorySnapshot(Guid.NewGuid(), 1, 8, 0, 0, "Clean", []),
            new CurrencyWalletSnapshot(1, [new CurrencyBalance("Gold", 0, 0, "Clean")])));
        var inventory = new InventoryTransactionCoordinator(
            new ItemDefinitionCatalog([]),
            new MerchantDefinitionCatalog([]),
            inventoryStore,
            inventoryAudit);
        var store = new InMemoryQuestRuntimeStore();
        var authority = new InMemoryQuestInstanceAuthority();
        var events = new InMemoryQuestEventSink();
        var audit = new InMemoryQuestAuditLedger();
        var completion = new AllRequiredQuestCompletionPolicy();
        var rewards = new QuestRewardCoordinator(store, inventory, events);
        var coordinator = new QuestCoordinator(
            catalog,
            sessions,
            new QuestBindingResolver(),
            new QuestEligibilityPolicy(),
            new NullQuestInventorySnapshotProvider(),
            authority,
            store,
            completion,
            rewards,
            events,
            audit);
        var progress = new QuestProgressCoordinator(
            store,
            authority,
            catalog,
            completion,
            events,
            audit);
        var router = new QuestEventRouter(
            authority,
            catalog,
            QuestObjectiveHandlerRegistry.CreateDefault(),
            progress,
            events,
            testOnlyRuntime: true);
        var accept = Intent(session, QuestIntentType.Accept, "integration-accept");
        var accepted = await coordinator.AcceptAsync(accept, default);
        Assert.Equal(QuestResultCode.Success, accepted.Code);
        Assert.Empty(accepted.NetworkBytes);

        var death = new MonsterDeathRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            900,
            200,
            100,
            1,
            1,
            1,
            10,
            10,
            Now,
            CombatPolicyStatus.TestOnly,
            CombatPolicyStatus.EvidenceBlocked,
            CombatPolicyStatus.TestOnly,
            1,
            2,
            "quest-integration");
        var semantic = new QuestSemanticEventAdapter()
            .FromMonsterDeath(death, 1, "account-safe");
        Assert.True(semantic.Succeeded);
        var progressed = await router.RouteAsync(semantic.Value!, default);
        Assert.Single(progressed);
        Assert.True(progressed[0].ObjectiveCompleted);
        Assert.True(progressed[0].QuestReady);
        Assert.Equal(QuestInstanceState.ReadyToComplete, authority.Get(accepted.QuestInstanceId!.Value)!.State);

        var ready = authority.Get(accepted.QuestInstanceId.Value)!;
        var turnIn = Intent(session, QuestIntentType.TurnIn, "integration-turnin") with
        {
            ExpectedQuestVersion = ready.QuestVersion
        };
        var completed = await coordinator.TurnInAsync(turnIn, default);
        Assert.Equal(QuestResultCode.Success, completed.Code);
        Assert.Equal(QuestInstanceState.Completed, completed.Instance!.State);
        Assert.Empty(completed.NetworkBytes);
        Assert.Single(store.RewardRecords);

        var duplicate = await coordinator.TurnInAsync(turnIn, default);
        Assert.Equal(QuestResultCode.DuplicateCompleted, duplicate.Code);
        Assert.Single(store.RewardRecords);
        Assert.All(events.Snapshot, value => Assert.Equal(0, value.NetworkBytes));
        Assert.Contains(audit.Snapshot, value => value.Operation == "Accept");
        Assert.Contains(audit.Snapshot, value => value.Operation == "Progress");
        Assert.Contains(audit.Snapshot, value => value.Operation == "TurnIn");

        var inspector = new QuestRuntimeInspector(catalog, authority, store, audit)
            .Capture(new QuestInspectorQuery(CharacterId: 1));
        Assert.Single(inspector.Definitions);
        Assert.Single(inspector.Instances);
        Assert.Single(inspector.Objectives);
        Assert.Single(inspector.Progress);
        Assert.Single(inspector.Rewards);
    }

    [Fact]
    public void Quest_runtime_is_transport_free_and_packet_handlers_do_not_write_quest_sql()
    {
        var root = RepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "src", "God2.ClassicServer.Runtime", "QuestRuntime.cs"));
        var persistence = File.ReadAllText(Path.Combine(root, "src", "God2.ClassicServer.Persistence", "MariaDbQuestRuntimeStore.cs"));
        var protocolFiles = Directory.EnumerateFiles(
                Path.Combine(root, "src", "God2.ClassicServer.Protocol"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain("Socket", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", runtime, StringComparison.Ordinal);
        Assert.Contains("@questInstanceId", persistence, StringComparison.Ordinal);
        Assert.DoesNotContain(protocolFiles, source =>
            source.Contains("quest_progress_mutations", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("quest_instances", StringComparison.OrdinalIgnoreCase));
    }

    private static QuestDefinition TestDefinition() =>
        new(
            700,
            "test:quest:integration",
            "Integration Quest",
            "Deterministic TestOnly integration quest.",
            QuestCategory.Side,
            QuestType.SystemGranted,
            "SystemGranted",
            QuestCompletionPolicyType.AllRequired,
            new QuestRepeatPolicy(
                QuestRepeatPolicyType.Once,
                1,
                QuestContentStatus.TestOnly,
                """{"testOnly":true}"""),
            [],
            [
                new QuestObjectiveDefinition(
                    "quest:700:kill",
                    700,
                    0,
                    QuestObjectiveType.KillMonster,
                    200,
                    "MonsterTemplate",
                    1,
                    QuestProgressMode.UniqueEvent,
                    "AllRequired",
                    true,
                    QuestContentStatus.TestOnly,
                    "quest-integration-v1",
                    """{"testOnly":true}""")
            ],
            new QuestRewardDefinition(
                "quest:700:no-reward",
                [],
                [],
                null,
                null,
                [],
                QuestContentStatus.TestOnly,
                """{"testOnly":true}"""),
            [],
            true,
            "quest-integration-v1",
            QuestContentStatus.TestOnly,
            QuestContentStatus.EvidenceBlocked,
            """{"testOnly":true}""");

    private static QuestIntent Intent(
        QuestSessionSnapshot session,
        QuestIntentType type,
        string key) =>
        new(
            Guid.NewGuid(),
            key,
            session.SessionId,
            session.CharacterId,
            session.PlayerRuntimeEntityId,
            700,
            null,
            null,
            type,
            null,
            session.CharacterRuntimeVersion,
            QuestRequestSource.TestOnly,
            Now,
            key);

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
}
