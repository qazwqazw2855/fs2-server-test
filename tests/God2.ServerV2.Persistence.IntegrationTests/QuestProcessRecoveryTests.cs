using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class QuestProcessRecoveryTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Separate_process_reads_completed_quest_without_dispatch()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        Assert.Equal("127.0.0.1", Required("GOD2_DB_HOST"));
        Assert.Equal("3308", Required("GOD2_DB_PORT"));
        var character = long.Parse(Required("GOD2_QUEST_ACCEPT_CHARACTER_ID"));
        Assert.True(character > 1);
        var user = Required("GOD2_QUEST_FIXTURE_USER");
        Assert.StartsWith("questtest_", user);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_QUEST_FIXTURE_PASSWORD"));

        async Task<string> ReadCommittedState()
        {
            await using var connection =
                new MySqlConnection(options.BuildConnectionString());
            await connection.OpenAsync();

            async Task<long> Scalar(string sql, Guid? instance = null)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@character", character);
                if (instance.HasValue)
                    command.Parameters.AddWithValue(
                        "@instance", instance.Value.ToString("D"));
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }

            Assert.Equal(1, await Scalar("""
                SELECT COUNT(*) FROM god2_player.characters
                WHERE character_id=@character AND enabled=0
                  AND admin_note='QuestRewardCommittedFixture'
                  AND name LIKE 'questfixture_%_accept';
                """));
            Assert.Equal(1, await Scalar("""
                SELECT COUNT(*) FROM god2_player.v2_quest_instances
                WHERE CharacterId=@character;
                """));

            Guid instance;
            string definitionFingerprint;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT QuestInstanceId, State, QuestVersion,
                           DefinitionFingerprint
                    FROM god2_player.v2_quest_instances
                    WHERE CharacterId=@character;
                    """;
                command.Parameters.AddWithValue("@character", character);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                instance = reader.GetGuid(0);
                Assert.Equal("Completed", reader.GetString(1));
                Assert.Equal(2L, reader.GetInt64(2));
                definitionFingerprint = reader.GetString(3);
                Assert.Equal(64, definitionFingerprint.Length);
                Assert.False(await reader.ReadAsync());
            }

            Assert.Equal(1, await Scalar("""
                SELECT COUNT(*) FROM god2_player.v2_quest_objective_progress
                WHERE QuestInstanceId=@instance AND ObjectiveId=1
                  AND ObjectiveKind='DefeatMonster' AND TargetId=100
                  AND RequiredCount=2 AND CurrentCount=2;
                """, instance));
            Assert.Equal(1, await Scalar("""
                SELECT COUNT(*) FROM god2_player.v2_quest_reward_snapshots
                WHERE QuestInstanceId=@instance;
                """, instance));

            QuestProgressResult progress;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT ResultJson FROM god2_player.v2_quest_progress_events
                    WHERE QuestInstanceId=@instance AND CharacterId=@character;
                    """;
                command.Parameters.AddWithValue("@instance", instance.ToString("D"));
                command.Parameters.AddWithValue("@character", character);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                progress = JsonSerializer.Deserialize<QuestProgressResult>(
                    reader.GetString(0))!;
                Assert.NotNull(progress);
                Assert.Equal(QuestProgressStatus.Applied, progress.Status);
                Assert.Equal(instance, progress.QuestInstanceId);
                Assert.Equal(0L, progress.QuestVersionBefore);
                Assert.Equal(1L, progress.QuestVersionAfter);
                Assert.True(progress.Ready);
                Assert.False(await reader.ReadAsync());
            }

            QuestRewardClaimResult claim;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT ResultJson FROM god2_player.v2_quest_reward_claims
                    WHERE QuestInstanceId=@instance AND CharacterId=@character;
                    """;
                command.Parameters.AddWithValue("@instance", instance.ToString("D"));
                command.Parameters.AddWithValue("@character", character);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                claim = JsonSerializer.Deserialize<QuestRewardClaimResult>(
                    reader.GetString(0))!;
                Assert.NotNull(claim);
                Assert.Equal(QuestRewardClaimStatus.Claimed, claim.Status);
                Assert.Equal(instance, claim.QuestInstanceId);
                Assert.Equal(0L, claim.InventoryVersionBefore);
                Assert.Equal(1L, claim.InventoryVersionAfter);
                Assert.False(await reader.ReadAsync());
            }

            var inventory = await new MariaDbCharacterInventorySnapshotRepository(options)
                .GetByCharacterAsync(character, CancellationToken.None);
            Assert.NotNull(inventory);
            Assert.Equal(1L, inventory.Version);
            Assert.Equal(1L, inventory.MutationSequence);
            var slot = Assert.Single(inventory.Slots);
            Assert.Equal(253231541L, slot.ItemId);
            Assert.Equal(1, slot.Quantity);

            return JsonSerializer.Serialize(new {
                instance, definitionFingerprint, progress, claim, inventory
            });
        }

        // Each read opens fresh connections; no acceptance, event or claim writer.
        var first = await ReadCommittedState();
        var second = await ReadCommittedState();
        Assert.Equal(first, second);
    }
}
