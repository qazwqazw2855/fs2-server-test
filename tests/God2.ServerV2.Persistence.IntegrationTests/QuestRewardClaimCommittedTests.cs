using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class QuestRewardClaimCommittedTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Owned_transactions_commit_once_and_roll_back_claim_failure()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var user = Required("GOD2_QUEST_FIXTURE_USER");
        Assert.StartsWith("questtest_", user);
        var characterId = long.Parse(Required("GOD2_QUEST_FIXTURE_CHARACTER_ID"));
        Assert.True(characterId > 1);

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_QUEST_FIXTURE_PASSWORD"));

        var instanceIds = Required("GOD2_QUEST_FIXTURE_INSTANCES")
            .Split(',').Select(Guid.Parse).ToArray();
        Assert.Equal(3, instanceIds.Length);

        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var initial = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.NotNull(initial);
        Assert.Empty(initial.Slots);
        Assert.Equal(0, initial.Version);

        var identities = new Dictionary<Guid, QuestRewardEvidenceIdentity>();
        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.name, c.enabled, c.admin_note,
                       q.QuestInstanceId, q.QuestId,
                       q.DefinitionFingerprint,
                       s.RewardFingerprint, s.EvidenceReference
                FROM god2_player.characters c
                JOIN god2_player.v2_quest_instances q
                  ON q.CharacterId=c.character_id
                JOIN god2_player.v2_quest_reward_snapshots s
                  ON s.QuestInstanceId=q.QuestInstanceId
                WHERE c.character_id=@character;
                """;
            command.Parameters.AddWithValue("@character", characterId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Assert.StartsWith("questfixture_", reader.GetString(0));
                Assert.False(reader.GetBoolean(1));
                Assert.Equal("QuestRewardCommittedFixture", reader.GetString(2));
                var id = reader.GetGuid(3);
                Assert.Contains(id, instanceIds);
                Assert.Equal($"FixtureOnly:{id:D}", reader.GetString(7));
                identities.Add(id, new QuestRewardEvidenceIdentity(
                    reader.GetInt64(4), reader.GetString(5),
                    reader.GetString(6), reader.GetString(7)));
            }
        }
        Assert.Equal(3, identities.Count);

        var gate = new FixtureEvidenceGate(identities.Values.ToArray());
        var writer = new MariaDbQuestRewardClaimWriter(options, gate);
        var request = new QuestRewardClaimRequest(
            Guid.NewGuid(), characterId, instanceIds[0], 0,
            initial.InventoryId, 0, 0);

        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 8).Select(async index =>
        {
            await start.Task;
            return await writer.ClaimAsync(
                request with { TransactionId = Guid.NewGuid() },
                CancellationToken.None);
        }).ToArray();
        start.SetResult(true);
        var results = await Task.WhenAll(attempts);

        var granted = Assert.Single(
            results, value => value.Status == QuestRewardClaimStatus.Claimed);
        Assert.Equal(7, results.Count(
            value => value.Status == QuestRewardClaimStatus.Replayed));
        Assert.All(results, value =>
        {
            Assert.Equal(granted.TransactionId, value.TransactionId);
            Assert.Equal(0, value.InventoryVersionBefore);
            Assert.Equal(2, value.InventoryVersionAfter);
        });

        var afterFirst = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.NotNull(afterFirst);
        Assert.Equal(2, afterFirst.Version);
        Assert.Equal(2, afterFirst.MutationSequence);
        Assert.Equal(2, afterFirst.Slots.Sum(value => value.Quantity));

        // New writer / new connection, with different request versions.
        var replay = await new MariaDbQuestRewardClaimWriter(options, gate)
            .ClaimAsync(request with
            {
                TransactionId = Guid.NewGuid(),
                ExpectedQuestVersion = 1,
                ExpectedInventoryVersion = 2,
                ExpectedMutationSequence = 2
            }, CancellationToken.None);
        Assert.Equal(QuestRewardClaimStatus.Replayed, replay.Status);
        Assert.Equal(granted.TransactionId, replay.TransactionId);

        var fullRequest = request with
        {
            TransactionId = Guid.NewGuid(),
            QuestInstanceId = instanceIds[1],
            ExpectedInventoryVersion = 2,
            ExpectedMutationSequence = 2
        };
        var full = await writer.ClaimAsync(fullRequest, CancellationToken.None);
        Assert.Equal(QuestRewardClaimStatus.InsufficientCapacity, full.Status);

        // Reuse a transaction ID already claimed by another instance.
        // The unique constraint fails only after the two grants were written.
        var faultRequest = fullRequest with
        {
            TransactionId = granted.TransactionId,
            QuestInstanceId = instanceIds[2]
        };
        var exception = await Assert.ThrowsAsync<MySqlException>(
            async () => await writer.ClaimAsync(
                faultRequest, CancellationToken.None));
        Assert.Equal(1062, exception.Number);

        var afterFailure = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.Equal(
            JsonSerializer.Serialize(afterFirst),
            JsonSerializer.Serialize(afterFailure));

        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            async Task<long> Count(string sql)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@character", characterId);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }

            Assert.Equal(1, await Count("""
                SELECT COUNT(*) FROM god2_player.v2_quest_reward_claims
                WHERE CharacterId=@character;
                """));
            Assert.Equal(2, await Count("""
                SELECT COUNT(*) FROM god2_player.v2_quest_instances
                WHERE CharacterId=@character AND State='Ready' AND QuestVersion=0;
                """));
            Assert.Equal(2, await Count("""
                SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
                WHERE CharacterId=@character;
                """));
            Assert.Equal(2, await Count("""
                SELECT COUNT(*) FROM god2_player.inventory_audit_ledger
                WHERE CharacterId=@character;
                """));
        }

        // The failed instance is still claimable using a fresh transaction ID.
        var retry = await writer.ClaimAsync(
            faultRequest with { TransactionId = Guid.NewGuid() },
            CancellationToken.None);
        Assert.Equal(QuestRewardClaimStatus.Claimed, retry.Status);
        Assert.Equal(2, retry.InventoryVersionBefore);
        Assert.Equal(4, retry.InventoryVersionAfter);

        var final = await repository.GetByCharacterAsync(
            characterId, CancellationToken.None);
        Assert.NotNull(final);
        Assert.Equal(4, final.Version);
        Assert.Equal(4, final.MutationSequence);
        Assert.Equal(4, final.Slots.Sum(value => value.Quantity));
    }

    private sealed class FixtureEvidenceGate(
        IReadOnlyList<QuestRewardEvidenceIdentity> allowed)
        : IQuestRewardEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            QuestRewardEvidenceIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(allowed.Contains(identity));
        }
    }
}
