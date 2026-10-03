using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class QuestProgressClosedLoopTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Trusted_events_progress_once_then_allow_atomic_reward_claim()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var user = Required("GOD2_QUEST_FIXTURE_USER");
        Assert.StartsWith("questtest_", user);
        var character = long.Parse(Required("GOD2_QUEST_LOOP_CHARACTER_ID"));
        Assert.True(character > 1);
        var ids = Required("GOD2_QUEST_FIXTURE_INSTANCES")
            .Split(',').Select(Guid.Parse).ToArray();
        Assert.Equal(4, ids.Length);
        var instance = ids[3];

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_QUEST_FIXTURE_PASSWORD"));

        QuestProgressDefinitionIdentity progressIdentity;
        QuestRewardEvidenceIdentity rewardIdentity;
        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.name,c.enabled,c.admin_note,
                       q.QuestId,q.DefinitionFingerprint,q.State,q.QuestVersion,
                       s.RewardFingerprint,s.EvidenceReference
                FROM god2_player.characters c
                JOIN god2_player.v2_quest_instances q
                  ON q.CharacterId=c.character_id
                JOIN god2_player.v2_quest_reward_snapshots s
                  ON s.QuestInstanceId=q.QuestInstanceId
                WHERE c.character_id=@character AND q.QuestInstanceId=@instance;
                """;
            command.Parameters.AddWithValue("@character", character);
            command.Parameters.AddWithValue("@instance", instance.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.StartsWith("questfixture_", reader.GetString(0));
            Assert.False(reader.GetBoolean(1));
            Assert.Equal("QuestRewardCommittedFixture", reader.GetString(2));
            Assert.Equal("Accepted", reader.GetString(5));
            Assert.Equal(0, reader.GetInt64(6));
            Assert.Equal($"FixtureOnly:{instance:D}", reader.GetString(8));

            var objectives = new QuestObjectiveProgress[]
            {
                new(1, QuestObjectiveKind.DefeatMonster, 100, 2, 0),
                new(2, QuestObjectiveKind.InteractNpc, 200, 1, 0)
            };
            var objectiveHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(objectives))))
                .ToLowerInvariant();

            progressIdentity = new(
                reader.GetInt64(3), reader.GetString(4), objectiveHash);
            rewardIdentity = new(
                reader.GetInt64(3), reader.GetString(4),
                reader.GetString(7), reader.GetString(8));
        }

        var gate = new FixtureProgressGate(progressIdentity);
        var writer = new MariaDbQuestProgressWriter(options, gate);
        var kill = new QuestProgressEvent(
            Guid.NewGuid(), character, QuestObjectiveKind.DefeatMonster, 100, 2);
        var request = new QuestProgressRequest(instance, 0, kill);

        var blocked = await new MariaDbQuestProgressWriter(
            options, new BlockedQuestProgressEvidenceGate())
            .ApplyAsync(request, CancellationToken.None);
        Assert.Equal(QuestProgressStatus.EvidenceBlocked, blocked.Status);

        var unrelated = await writer.ApplyAsync(
            request with { Event = kill with { EventId = Guid.NewGuid(), TargetId = 999 } },
            CancellationToken.None);
        Assert.Equal(QuestProgressStatus.Applied, unrelated.Status);
        Assert.Empty(unrelated.ChangedObjectives);
        Assert.Equal(0, unrelated.QuestVersionAfter);
        Assert.False(unrelated.Ready);

        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await start.Task;
            return await writer.ApplyAsync(request, CancellationToken.None);
        }).ToArray();
        start.SetResult(true);
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, value => value.Status == QuestProgressStatus.Applied);
        Assert.Equal(7, results.Count(
            value => value.Status == QuestProgressStatus.Replayed));
        Assert.All(results, value =>
        {
            Assert.Equal(1, value.QuestVersionAfter);
            Assert.False(value.Ready);
            Assert.Equal(2, Assert.Single(value.ChangedObjectives).CurrentCount);
        });

        var conflict = await writer.ApplyAsync(
            request with { Event = kill with { Count = 3 } },
            CancellationToken.None);
        Assert.Equal(QuestProgressStatus.EventConflict, conflict.Status);

        var interaction = new QuestProgressRequest(
            instance, 0, new QuestProgressEvent(
                Guid.NewGuid(), character, QuestObjectiveKind.InteractNpc, 200, 1));
        var stale = await writer.ApplyAsync(interaction, CancellationToken.None);
        Assert.Equal(QuestProgressStatus.VersionConflict, stale.Status);

        var ready = await writer.ApplyAsync(
            interaction with { ExpectedQuestVersion = 1 },
            CancellationToken.None);
        Assert.Equal(QuestProgressStatus.Applied, ready.Status);
        Assert.True(ready.Ready);
        Assert.Equal(2, ready.QuestVersionAfter);

        var inventoryRepository =
            new MariaDbCharacterInventorySnapshotRepository(options);
        var beforeClaim = await inventoryRepository.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(beforeClaim);

        var claim = await new MariaDbQuestRewardClaimWriter(
            options, new FixtureRewardGate(rewardIdentity))
            .ClaimAsync(new QuestRewardClaimRequest(
                Guid.NewGuid(), character, instance, 2,
                beforeClaim.InventoryId, beforeClaim.Version,
                beforeClaim.MutationSequence),
                CancellationToken.None);
        Assert.Equal(QuestRewardClaimStatus.Claimed, claim.Status);

        var afterClaim = await inventoryRepository.GetByCharacterAsync(
            character, CancellationToken.None);
        Assert.NotNull(afterClaim);
        Assert.Equal(beforeClaim.Version + 2, afterClaim.Version);
        Assert.Equal(
            beforeClaim.Slots.Sum(value => value.Quantity) + 2,
            afterClaim.Slots.Sum(value => value.Quantity));

        var replay = await new MariaDbQuestProgressWriter(options, gate)
            .ApplyAsync(request with { ExpectedQuestVersion = 3 },
                CancellationToken.None);
        Assert.Equal(QuestProgressStatus.Replayed, replay.Status);
        Assert.Equal(1, replay.QuestVersionAfter);

        var inactive = await writer.ApplyAsync(
            request with
            {
                ExpectedQuestVersion = 3,
                Event = kill with { EventId = Guid.NewGuid() }
            }, CancellationToken.None);
        Assert.Equal(QuestProgressStatus.QuestNotActive, inactive.Status);

        await using (var connection =
            new MySqlConnection(options.BuildConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM god2_player.v2_quest_progress_events
                   WHERE QuestInstanceId=@instance),
                  (SELECT COUNT(*) FROM god2_player.v2_quest_instances
                   WHERE QuestInstanceId=@instance
                     AND State='Completed' AND QuestVersion=3),
                  (SELECT COUNT(*) FROM god2_player.v2_quest_objective_progress
                   WHERE QuestInstanceId=@instance
                     AND CurrentCount=RequiredCount);
                """;
            command.Parameters.AddWithValue("@instance", instance.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetInt64(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.Equal(2, reader.GetInt64(2));
        }
    }

    private sealed class FixtureProgressGate(
        QuestProgressDefinitionIdentity expected) : IQuestProgressEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            QuestProgressDefinitionIdentity identity, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(identity == expected);
        }
    }

    private sealed class FixtureRewardGate(
        QuestRewardEvidenceIdentity expected) : IQuestRewardEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            QuestRewardEvidenceIdentity identity, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(identity == expected);
        }
    }
}
