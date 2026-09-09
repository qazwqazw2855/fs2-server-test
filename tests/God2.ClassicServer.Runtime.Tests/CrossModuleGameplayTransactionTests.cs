using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class CrossModuleGameplayTransactionTests
{
    [Fact]
    public async Task Commit_updates_all_modules_atomically()
    {
        var (store, coordinator, request) = Fixture();
        var result = await coordinator.CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.Committed, result.Code);
        Assert.Equal(1, result.State.RewardCommitCount);
        Assert.Contains(request.BattleId, result.State.SettledBattles);
        Assert.Contains(request.QuestId, result.State.CompletedQuests);
        Assert.Equal(2, result.State.Inventory[request.ItemId]);
        Assert.Equal(1_250, result.State.Experience);
        Assert.Equal(4, result.State.Level);
        Assert.Single(result.State.Outbox);
        Assert.Equal(2, store.Snapshot().Version);
    }

    [Fact]
    public async Task Default_progression_fails_closed_without_verified_level_experience()
    {
        var (store, _, request) = Fixture();
        var before = store.Snapshot();

        var result = await new CrossModuleGameplayTransactionCoordinator(store).CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.RolledBack, result.Code);
        Assert.Equal(
            "EvidenceBlockedMissingVerifiedLevelExperience",
            EvidenceBlockedCrossModuleProgressionPolicy.EvidenceStatus);
        Assert.Equal(EvidenceBlockedCrossModuleProgressionPolicy.FailureCode, result.FailureCode);
        Assert.Equivalent(before, result.State, strict: true);
        Assert.Equivalent(before, store.Snapshot(), strict: true);
    }

    [Fact]
    public async Task Default_progression_allows_transaction_that_awards_no_experience()
    {
        var (store, _, request) = Fixture();

        var result = await new CrossModuleGameplayTransactionCoordinator(store)
            .CommitAsync(request with { ExperienceGain = 0 });

        Assert.Equal(CrossModuleTransactionResultCode.Committed, result.Code);
        Assert.Equal(900, result.State.Experience);
        Assert.Equal(1, result.State.Level);
    }

    [Theory]
    [InlineData(CrossModuleFailurePoint.BattleSettlement)]
    [InlineData(CrossModuleFailurePoint.Reward)]
    [InlineData(CrossModuleFailurePoint.QuestCompletion)]
    [InlineData(CrossModuleFailurePoint.Inventory)]
    [InlineData(CrossModuleFailurePoint.Experience)]
    [InlineData(CrossModuleFailurePoint.LevelUp)]
    [InlineData(CrossModuleFailurePoint.Outbox)]
    [InlineData(CrossModuleFailurePoint.BeforeCommit)]
    public async Task Every_precommit_failure_rolls_back_the_whole_aggregate(CrossModuleFailurePoint failure)
    {
        var (store, coordinator, request) = Fixture();
        store.FailurePoint = failure;
        var before = store.Snapshot();

        var result = await coordinator.CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.RolledBack, result.Code);
        Assert.Equivalent(before, store.Snapshot(), strict: true);
    }

    [Fact]
    public async Task Commit_response_lost_replay_is_exactly_once()
    {
        var (store, coordinator, request) = Fixture();
        store.FailurePoint = CrossModuleFailurePoint.CommitResponseLost;
        var lost = await coordinator.CommitAsync(request);
        store.FailurePoint = CrossModuleFailurePoint.None;
        var replay = await coordinator.CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.RecoveryRequired, lost.Code);
        Assert.Equal(CrossModuleTransactionResultCode.DuplicateCommitted, replay.Code);
        Assert.Equal(1, store.Snapshot().RewardCommitCount);
        Assert.Equal(2, store.Snapshot().Inventory[request.ItemId]);
    }

    [Fact]
    public async Task Restart_recovers_prepared_transaction_once()
    {
        var (store, coordinator, request) = Fixture();
        store.FailurePoint = CrossModuleFailurePoint.CrashAfterPrepare;
        var crash = await coordinator.CommitAsync(request);
        store.FailurePoint = CrossModuleFailurePoint.None;

        var recovered = store.Recover();
        var replay = await new CrossModuleGameplayTransactionCoordinator(store).CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.RecoveryRequired, crash.Code);
        Assert.Single(recovered);
        Assert.Equal(CrossModuleTransactionResultCode.DuplicateCommitted, replay.Code);
        Assert.Equal(1, store.Snapshot().RewardCommitCount);
    }

    [Fact]
    public async Task Changed_duplicate_is_rejected()
    {
        var (_, coordinator, request) = Fixture();
        await coordinator.CommitAsync(request);

        var changed = await coordinator.CommitAsync(request with { ItemQuantity = 3 });

        Assert.Equal(CrossModuleTransactionResultCode.Conflict, changed.Code);
    }

    [Fact]
    public async Task Changed_transaction_identifier_is_part_of_the_idempotency_payload()
    {
        var (_, coordinator, request) = Fixture();
        await coordinator.CommitAsync(request);

        var changed = await coordinator.CommitAsync(request with { TransactionId = Guid.NewGuid() });

        Assert.Equal(CrossModuleTransactionResultCode.Conflict, changed.Code);
    }

    [Fact]
    public async Task Same_battle_cannot_be_rewarded_again_with_a_different_idempotency_key()
    {
        var (store, coordinator, request) = Fixture();
        await coordinator.CommitAsync(request);

        var duplicateBattle = await coordinator.CommitAsync(request with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = "another-key",
            ExpectedVersion = 2
        });

        Assert.Equal(CrossModuleTransactionResultCode.Conflict, duplicateBattle.Code);
        Assert.Equal("gameplay.transaction.battle_already_settled", duplicateBattle.FailureCode);
        Assert.Equal(1, store.Snapshot().RewardCommitCount);
    }

    [Fact]
    public async Task Concurrent_duplicate_commits_once()
    {
        var (store, coordinator, request) = Fixture();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => coordinator.CommitAsync(request)));

        Assert.Single(results, item => item.Code == CrossModuleTransactionResultCode.Committed);
        Assert.Equal(31, results.Count(item => item.Code == CrossModuleTransactionResultCode.DuplicateCommitted));
        Assert.Equal(1, store.Snapshot().RewardCommitCount);
    }

    [Fact]
    public async Task Coordinators_for_the_same_store_share_the_transaction_gate()
    {
        var (store, coordinator, request) = Fixture();
        var secondCoordinator = new CrossModuleGameplayTransactionCoordinator(
            store,
            new ConservativeCrossModuleProgressionPolicy());

        var results = await Task.WhenAll(coordinator.CommitAsync(request), secondCoordinator.CommitAsync(request));

        Assert.Single(results, item => item.Code == CrossModuleTransactionResultCode.Committed);
        Assert.Single(results, item => item.Code == CrossModuleTransactionResultCode.DuplicateCommitted);
        Assert.Equal(1, store.Snapshot().RewardCommitCount);
    }

    [Fact]
    public async Task Recovery_rejects_stale_prepared_candidate_instead_of_overwriting_committed_state()
    {
        var (store, coordinator, request) = Fixture();
        store.FailurePoint = CrossModuleFailurePoint.CrashAfterPrepare;
        var other = request with
        {
            TransactionId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            IdempotencyKey = "cross-module-key-2",
            BattleId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            QuestId = 3002,
            ItemId = 5002
        };

        await coordinator.CommitAsync(request);
        await coordinator.CommitAsync(other);
        store.FailurePoint = CrossModuleFailurePoint.None;

        var recovered = store.Recover();
        var snapshot = store.Snapshot();

        Assert.Equal(2, recovered.Count);
        Assert.Single(recovered, item => item.Code == CrossModuleTransactionResultCode.Committed);
        Assert.Single(recovered, item => item.Code == CrossModuleTransactionResultCode.Conflict);
        Assert.Equal(1, snapshot.RewardCommitCount);
        Assert.Equal(2, snapshot.Version);
        Assert.Single(snapshot.Inventory);
        Assert.Single(snapshot.CompletedQuests);
        Assert.Single(snapshot.SettledBattles);
    }

    [Fact]
    public async Task Published_state_collections_are_immutable()
    {
        var (_, coordinator, request) = Fixture();
        var result = await coordinator.CommitAsync(request);

        Assert.Throws<NotSupportedException>(() => ((IDictionary<int, int>)result.State.Inventory).Add(999, 1));
        Assert.Throws<NotSupportedException>(() => ((ISet<int>)result.State.CompletedQuests).Add(999));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.State.Outbox).Add("tamper"));
    }

    [Fact]
    public async Task Progression_policy_never_decreases_an_existing_level()
    {
        var state = new CrossModuleGameplayState(
            7, 1, 0, 50,
            new Dictionary<int, int>(), new HashSet<int>(), new HashSet<Guid>(), [], 0);
        var store = new InMemoryCrossModuleGameplayStore(state);
        var request = Fixture().Request with { ExpectedVersion = 1, ExperienceGain = 1 };

        var result = await new CrossModuleGameplayTransactionCoordinator(
            store,
            new ConservativeCrossModuleProgressionPolicy()).CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.Committed, result.Code);
        Assert.Equal(50, result.State.Level);
    }

    [Fact]
    public async Task Progression_policy_never_decreases_out_of_catalog_imported_progress()
    {
        var state = new CrossModuleGameplayState(
            7, 1, 2_000_000, 101,
            new Dictionary<int, int>(), new HashSet<int>(), new HashSet<Guid>(), [], 0);
        var store = new InMemoryCrossModuleGameplayStore(state);
        var request = Fixture().Request with { ExpectedVersion = 1, ExperienceGain = 1 };

        var result = await new CrossModuleGameplayTransactionCoordinator(
            store,
            new ConservativeCrossModuleProgressionPolicy()).CommitAsync(request);

        Assert.Equal(CrossModuleTransactionResultCode.Committed, result.Code);
        Assert.Equal(2_000_000, result.State.Experience);
        Assert.Equal(101, result.State.Level);
    }

    [Fact]
    public async Task Arithmetic_overflow_rolls_back_without_publishing_partial_state()
    {
        var (store, coordinator, request) = Fixture();
        var before = store.Snapshot();

        var result = await coordinator.CommitAsync(request with { ExperienceGain = long.MaxValue });

        Assert.Equal(CrossModuleTransactionResultCode.RolledBack, result.Code);
        Assert.Equal("gameplay.transaction.arithmetic_overflow", result.FailureCode);
        Assert.Equivalent(before, store.Snapshot(), strict: true);
    }

    private static (InMemoryCrossModuleGameplayStore Store, CrossModuleGameplayTransactionCoordinator Coordinator, CrossModuleTransactionRequest Request) Fixture()
    {
        var state = new CrossModuleGameplayState(
            7, 1, 900, 1,
            new Dictionary<int, int>(),
            new HashSet<int>(),
            new HashSet<Guid>(),
            Array.Empty<string>(),
            0);
        var store = new InMemoryCrossModuleGameplayStore(state);
        var request = new CrossModuleTransactionRequest(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "cross-module-key-1",
            7,
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            3001,
            5001,
            2,
            350,
            1);
        return (store, new CrossModuleGameplayTransactionCoordinator(
            store,
            new ConservativeCrossModuleProgressionPolicy()), request);
    }
}
