using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class QuestAcceptanceClosedLoopTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Acceptance_registers_once_then_progress_and_claim_complete()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        var character = long.Parse(Required("GOD2_QUEST_ACCEPT_CHARACTER_ID"));
        Assert.True(character > 1);
        var user = Required("GOD2_QUEST_FIXTURE_USER");
        Assert.StartsWith("questtest_", user);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_QUEST_FIXTURE_PASSWORD"));

        await using var connection =
            new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();

        async Task<long> Scalar(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", character);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.characters
            WHERE character_id=@character AND enabled=0 AND level=1
              AND admin_note='QuestRewardCommittedFixture'
              AND name LIKE 'questfixture_%_accept';
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_instances
            WHERE CharacterId=@character;
            """));

        var questId = await Scalar("""
            SELECT quest_id FROM god2_game.quests
            WHERE enabled=1 ORDER BY quest_id LIMIT 1;
            """);

        // Synthetic reviewed definition for this disabled fixture only.
        // It does not promote the referenced formal quest's unknown content.
        var definition = new QuestAcceptanceDefinition(
            questId, false, 1, 1, "FixtureOnly:acceptance-closed-loop",
            [
                new QuestObjectiveProgress(
                    1, QuestObjectiveKind.DefeatMonster, 100, 2, 0)
            ],
            [new QuestRewardEntry(1, "Item", 253231541, 1)]);
        var snapshot = QuestAcceptanceDefinitionValidator.Prepare(definition);
        var source = new FixtureSource(definition);
        var gate = new FixtureGate(snapshot, character);
        var request = new QuestAcceptanceRequest(
            Guid.NewGuid(), character, questId, snapshot.DefinitionFingerprint);

        // This user can write instances/objectives, but cannot insert
        // reward snapshots. Failure occurs after the preceding writes.
        var faultUser = Required("GOD2_QUEST_ACCEPT_FAULT_USER");
        Assert.StartsWith("questfault_", faultUser);
        var faultOptions = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            faultUser, Required("GOD2_QUEST_FIXTURE_PASSWORD"));
        var faultWriter = new MariaDbQuestAcceptanceWriter(
            faultOptions, source, gate);
        var failure = await Assert.ThrowsAsync<MySqlException>(async () =>
            await faultWriter.AcceptAsync(request, CancellationToken.None));
        Assert.Equal(1142, failure.Number);
        Assert.Contains("INSERT", failure.Message);
        Assert.Contains("v2_quest_reward_snapshots", failure.Message);

        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_instances
            WHERE CharacterId=@character;
            """));
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                  (SELECT COUNT(*)
                   FROM god2_player.v2_quest_objective_progress
                   WHERE QuestInstanceId=@instance)
                  +
                  (SELECT COUNT(*)
                   FROM god2_player.v2_quest_reward_snapshots
                   WHERE QuestInstanceId=@instance);
                """;
            command.Parameters.AddWithValue(
                "@instance", request.QuestInstanceId.ToString("D"));
            Assert.Equal(0, Convert.ToInt64(
                await command.ExecuteScalarAsync()));
        }

        var blocked = new MariaDbQuestAcceptanceWriter(
            options, source, new BlockedQuestAcceptanceEvidenceGate());
        Assert.Equal(QuestAcceptanceStatus.EvidenceBlocked,
            (await blocked.AcceptAsync(request, CancellationToken.None)).Status);
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_instances
            WHERE CharacterId=@character;
            """));

        var writer = new MariaDbQuestAcceptanceWriter(options, source, gate);
        Assert.Equal(QuestAcceptanceStatus.DefinitionConflict,
            (await writer.AcceptAsync(
                request with { ExpectedDefinitionFingerprint = new string('0', 64) },
                CancellationToken.None)).Status);

        // Include the failed request unchanged to exercise safe retry.
        var requests = new[] { request }.Concat(Enumerable.Range(0, 7)
            .Select(_ => request with { QuestInstanceId = Guid.NewGuid() }))
            .ToArray();
        var results = await Task.WhenAll(requests.Select(async value =>
            await new MariaDbQuestAcceptanceWriter(options, source, gate)
                .AcceptAsync(value, CancellationToken.None)));

        var accepted = Assert.Single(results,
            x => x.Status == QuestAcceptanceStatus.Accepted);
        Assert.Equal(7, results.Count(
            x => x.Status == QuestAcceptanceStatus.AlreadyAccepted));
        var original = requests.Single(
            x => x.QuestInstanceId == accepted.QuestInstanceId);

        Assert.Equal(QuestAcceptanceStatus.Replayed,
            (await writer.AcceptAsync(original, CancellationToken.None)).Status);
        Assert.Equal(QuestAcceptanceStatus.InstanceConflict,
            (await writer.AcceptAsync(
                original with { ExpectedDefinitionFingerprint = new string('0', 64) },
                CancellationToken.None)).Status);
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_instances
            WHERE CharacterId=@character AND State='Accepted' AND QuestVersion=0;
            """));

        var progressWriter = new MariaDbQuestProgressWriter(options, gate);
        var progress = await progressWriter.ApplyAsync(
            new QuestProgressRequest(original.QuestInstanceId, 0,
                new QuestProgressEvent(
                    Guid.NewGuid(), character,
                    QuestObjectiveKind.DefeatMonster, 100, 2)),
            CancellationToken.None);
        Assert.Equal(QuestProgressStatus.Applied, progress.Status);
        Assert.True(progress.Ready);
        Assert.Equal(1, progress.QuestVersionAfter);

        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var inventory = await repository.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(inventory);
        Assert.Empty(inventory.Slots);

        var claimWriter = new MariaDbQuestRewardClaimWriter(options, gate);
        var claimRequest = new QuestRewardClaimRequest(
            Guid.NewGuid(), character, original.QuestInstanceId, 1,
            inventory.InventoryId, inventory.Version, inventory.MutationSequence);
        var claimed = await claimWriter.ClaimAsync(
            claimRequest, CancellationToken.None);
        Assert.Equal(QuestRewardClaimStatus.Claimed, claimed.Status);

        var restored = await repository.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(restored);
        var slot = Assert.Single(restored.Slots);
        Assert.Equal(253231541, slot.ItemId);
        Assert.Equal(1, slot.Quantity);

        Assert.Equal(QuestAcceptanceStatus.Replayed,
            (await writer.AcceptAsync(original, CancellationToken.None)).Status);
        Assert.Equal(QuestAcceptanceStatus.AlreadyAccepted,
            (await writer.AcceptAsync(
                original with { QuestInstanceId = Guid.NewGuid() },
                CancellationToken.None)).Status);
        Assert.Equal(QuestRewardClaimStatus.Replayed,
            (await claimWriter.ClaimAsync(
                claimRequest with { TransactionId = Guid.NewGuid() },
                CancellationToken.None)).Status);
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_instances
            WHERE CharacterId=@character AND State='Completed' AND QuestVersion=2;
            """));
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_reward_claims
            WHERE CharacterId=@character;
            """));
    }

    private sealed class FixtureSource(QuestAcceptanceDefinition definition)
        : IQuestAcceptanceDefinitionSource
    {
        public ValueTask<QuestAcceptanceDefinition?> GetAsync(
            long questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestAcceptanceDefinition?>(
                questId == definition.QuestId ? definition : null);
    }

    private sealed class FixtureGate(
        QuestAcceptanceSnapshot approved, long character)
        : IQuestAcceptanceEvidenceGate,
          IQuestProgressEvidenceGate,
          IQuestRewardEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            QuestAcceptanceSnapshot snapshot, long characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot == approved && characterId == character);

        public ValueTask<bool> IsApprovedAsync(
            QuestProgressDefinitionIdentity identity,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(identity == new QuestProgressDefinitionIdentity(
                approved.QuestId, approved.DefinitionFingerprint,
                approved.ObjectivesFingerprint));

        public ValueTask<bool> IsApprovedAsync(
            QuestRewardEvidenceIdentity identity,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(identity == new QuestRewardEvidenceIdentity(
                approved.QuestId, approved.DefinitionFingerprint,
                approved.RewardFingerprint, approved.EvidenceReference));
    }
}
