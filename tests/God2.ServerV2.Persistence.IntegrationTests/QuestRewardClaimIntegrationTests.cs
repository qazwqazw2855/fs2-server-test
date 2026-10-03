using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class QuestRewardClaimIntegrationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("capacity")]
    [InlineData("not-ready")]
    [InlineData("blocked")]
    [InlineData("unsupported")]
    [InlineData("claim-failure")]
    [Trait("Category", "Integration")]
    public async Task Claim_is_atomic_and_bound_to_the_quest_instance(
        string scenario)
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var user = Required("GOD2_GRANT_FAULT_USER");
        Assert.StartsWith("grantfault_", user);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_GRANT_FAULT_PASSWORD"));

        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var original = await repository.GetByCharacterAsync(
            1, CancellationToken.None);
        Assert.NotNull(original);

        await using var connection =
            new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();

        async Task Execute(string sql, MySqlTransaction? transaction = null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        async Task<long> Scalar(
            string sql, MySqlTransaction? transaction = null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        // Connection-local fixtures; do not create or alter persistent tables.
        await Execute("""
            CREATE TEMPORARY TABLE god2_player.v2_quest_instances (
                QuestInstanceId CHAR(36) NOT NULL PRIMARY KEY,
                CharacterId BIGINT NOT NULL,
                QuestId BIGINT NOT NULL,
                QuestVersion BIGINT NOT NULL,
                State VARCHAR(32) NOT NULL,
                DefinitionFingerprint CHAR(64) NOT NULL,
                CompletedAtUtc DATETIME(6) NULL,
                UpdatedAtUtc DATETIME(6) NOT NULL
            ) ENGINE=InnoDB;

            CREATE TEMPORARY TABLE god2_player.v2_quest_reward_snapshots (
                QuestInstanceId CHAR(36) NOT NULL PRIMARY KEY,
                RewardFingerprint CHAR(64) NOT NULL,
                RewardJson LONGTEXT NOT NULL,
                EvidenceReference VARCHAR(500) NOT NULL
            ) ENGINE=InnoDB;

            CREATE TEMPORARY TABLE god2_player.v2_quest_reward_claims (
                QuestInstanceId CHAR(36) NOT NULL PRIMARY KEY,
                CharacterId BIGINT NOT NULL,
                ClaimTransactionId CHAR(36) NOT NULL UNIQUE,
                RewardFingerprint CHAR(64) NOT NULL,
                InventoryVersionBefore BIGINT NOT NULL,
                InventoryVersionAfter BIGINT NOT NULL,
                ResultJson LONGTEXT NOT NULL
            ) ENGINE=InnoDB;
            """);

        // Permit audit writes only to this connection-local fixture.
        await Execute("""
            CREATE TEMPORARY TABLE god2_player.inventory_audit_ledger (
            AuditId CHAR(36) NOT NULL PRIMARY KEY,
            TransactionId CHAR(36) NOT NULL,
            IdempotencySafeId VARCHAR(32) NOT NULL,
            CharacterId BIGINT NOT NULL,
            SessionId VARCHAR(64) NOT NULL,
            OperationType VARCHAR(32) NOT NULL,
            Source VARCHAR(128) NOT NULL,
            ItemTemplateId INT NULL,
            InventoryItemId BIGINT NULL,
            QuantityBefore INT NOT NULL,
            QuantityAfter INT NOT NULL,
            CurrencyType VARCHAR(32) NOT NULL,
            CurrencyBefore BIGINT NOT NULL,
            CurrencyAfter BIGINT NOT NULL,
            InventoryVersionBefore BIGINT NOT NULL,
            InventoryVersionAfter BIGINT NOT NULL,
            Result VARCHAR(32) NOT NULL,
            FailureCode VARCHAR(128) NOT NULL,
            CreatedAtUtc DATETIME(6) NOT NULL,
            CompletedAtUtc DATETIME(6) NOT NULL,
            CorrelationId VARCHAR(64) NOT NULL
            ) ENGINE=InnoDB;
            """);

        if (scenario == "claim-failure")
        {
            await Execute("""
                ALTER TABLE god2_player.v2_quest_reward_claims
                ADD CONSTRAINT fixture_reject_claim
                CHECK (InventoryVersionAfter < 0);
                """);
        }

        var instanceId = Guid.NewGuid();
        var definitionFingerprint = new string('a', 64);
        var rewards = new QuestRewardEntry[]
        {
            new(1, "Item", 253231541, 1),
            new(2, scenario == "unsupported" ? "Experience" : "Item",
                253231541, 1)
        };
        var rewardJson = JsonSerializer.Serialize(rewards);
        var rewardFingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rewardJson)))
            .ToLowerInvariant();
        var evidenceIdentity = new QuestRewardEvidenceIdentity(
            999, definitionFingerprint, rewardFingerprint,
            $"FixtureOnly:{instanceId:D}");

        long reservedBefore;
        long replayBefore;
        long auditBefore;

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            try
            {
                // Lock before reading counts or changing the fixture inventory.
                await Scalar("""
                    SELECT character_id FROM god2_player.characters
                    WHERE character_id=1 FOR UPDATE;
                    """, transaction);

                reservedBefore = await Scalar("""
                    SELECT COUNT(*) FROM
                    god2_player.inventory_item_identity_sequence;
                    """, transaction);
                replayBefore = await Scalar("""
                    SELECT COUNT(*) FROM
                    god2_player.inventory_transaction_idempotency;
                    """, transaction);
                auditBefore = await Scalar("""
                    SELECT COUNT(*) FROM god2_player.inventory_audit_ledger;
                    """, transaction);

                await Execute("""
                    DELETE FROM god2_player.character_inventory
                    WHERE character_id=1;
                    """, transaction);

                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE god2_player.player_inventory_state
                        SET Capacity=@capacity, InventoryVersion=7,
                            MutationSequence=11
                        WHERE CharacterId=1;

                        INSERT INTO god2_player.v2_quest_instances
                            (QuestInstanceId, CharacterId, QuestId,
                             QuestVersion, State, DefinitionFingerprint,
                             UpdatedAtUtc)
                        VALUES (@instance,1,999,3,@state,@definition,
                                UTC_TIMESTAMP(6));

                        INSERT INTO god2_player.v2_quest_reward_snapshots
                            (QuestInstanceId, RewardFingerprint,
                             RewardJson, EvidenceReference)
                        VALUES (@instance,@fingerprint,@json,@evidence);
                        """;
                    command.Parameters.AddWithValue(
                        "@capacity", scenario == "capacity" ? 1 : 3);
                    command.Parameters.AddWithValue(
                        "@instance", instanceId.ToString("D"));
                    command.Parameters.AddWithValue(
                        "@state", scenario == "not-ready" ? "Accepted" : "Ready");
                    command.Parameters.AddWithValue(
                        "@definition", definitionFingerprint);
                    command.Parameters.AddWithValue(
                        "@fingerprint", rewardFingerprint);
                    command.Parameters.AddWithValue("@json", rewardJson);
                    command.Parameters.AddWithValue(
                        "@evidence", evidenceIdentity.EvidenceReference);
                    await command.ExecuteNonQueryAsync();
                }

                IQuestRewardEvidenceGate gate =
                    scenario == "blocked"
                        ? new BlockedQuestRewardEvidenceGate()
                        : new FixtureEvidenceGate(evidenceIdentity);

                var writer = new MariaDbQuestRewardClaimWriter(options, gate);
                var request = new QuestRewardClaimRequest(
                    Guid.NewGuid(), 1, instanceId, 3,
                    original.InventoryId, 7, 11);

                if (scenario == "claim-failure")
                {
                    var exception = await Assert.ThrowsAsync<MySqlException>(
                        async () => await writer.ClaimInTransactionAsync(
                            connection, transaction, request,
                            CancellationToken.None));
                    Assert.Equal(4025, exception.Number);

                    // Both grants were written before the final claim failed.
                    Assert.Equal(2, await Scalar("""
                        SELECT COUNT(*) FROM god2_player.character_inventory
                        WHERE character_id=1;
                        """, transaction));
                    Assert.Equal(replayBefore + 2, await Scalar("""
                        SELECT COUNT(*) FROM
                        god2_player.inventory_transaction_idempotency;
                        """, transaction));
                    Assert.Equal(0, await Scalar("""
                        SELECT COUNT(*) FROM god2_player.v2_quest_reward_claims;
                        """, transaction));
                }
                else
                {
                    var result = await writer.ClaimInTransactionAsync(
                        connection, transaction, request,
                        CancellationToken.None);

                    var expected = scenario switch
                    {
                        "success" => QuestRewardClaimStatus.Claimed,
                        "capacity" => QuestRewardClaimStatus.InsufficientCapacity,
                        "not-ready" => QuestRewardClaimStatus.QuestNotReady,
                        "blocked" => QuestRewardClaimStatus.RewardEvidenceBlocked,
                        _ => QuestRewardClaimStatus.UnsupportedReward
                    };
                    Assert.Equal(expected, result.Status);

                    if (scenario == "success")
                    {
                        Assert.Equal(7, result.InventoryVersionBefore);
                        Assert.Equal(9, result.InventoryVersionAfter);
                        Assert.Equal(2, await Scalar("""
                            SELECT SUM(quantity)
                            FROM god2_player.character_inventory
                            WHERE character_id=1;
                            """, transaction));
                        Assert.Equal(1, await Scalar("""
                            SELECT COUNT(*)
                            FROM god2_player.v2_quest_instances
                            WHERE State='Completed' AND QuestVersion=4;
                            """, transaction));

                        var replay = await writer.ClaimInTransactionAsync(
                            connection, transaction,
                            request with
                            {
                                TransactionId = Guid.NewGuid(),
                                ExpectedQuestVersion = 4,
                                ExpectedInventoryVersion = 9,
                                ExpectedMutationSequence = 13
                            },
                            CancellationToken.None);

                        Assert.Equal(
                            QuestRewardClaimStatus.Replayed, replay.Status);
                        Assert.Equal(request.TransactionId, replay.TransactionId);
                        Assert.Equal(replayBefore + 2, await Scalar("""
                            SELECT COUNT(*) FROM
                            god2_player.inventory_transaction_idempotency;
                            """, transaction));
                        Assert.Equal(1, await Scalar("""
                            SELECT COUNT(*)
                            FROM god2_player.v2_quest_reward_claims;
                            """, transaction));
                    }
                    else
                    {
                        Assert.Equal(0, await Scalar("""
                            SELECT COUNT(*)
                            FROM god2_player.character_inventory
                            WHERE character_id=1;
                            """, transaction));
                        Assert.Equal(0, await Scalar("""
                            SELECT COUNT(*)
                            FROM god2_player.v2_quest_reward_claims;
                            """, transaction));
                        Assert.Equal(7, await Scalar("""
                            SELECT InventoryVersion
                            FROM god2_player.player_inventory_state
                            WHERE CharacterId=1;
                            """, transaction));
                    }
                }
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        }

        var restored = await repository.GetByCharacterAsync(
            1, CancellationToken.None);
        Assert.Equal(
            JsonSerializer.Serialize(original),
            JsonSerializer.Serialize(restored));

        Assert.Equal(reservedBefore, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_item_identity_sequence;
            """));
        Assert.Equal(replayBefore, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency;
            """));
        Assert.Equal(auditBefore, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_audit_ledger;
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.v2_quest_instances;
            """));
    }

    // Test-only approval of one exact fixture identity.
    private sealed class FixtureEvidenceGate(
        QuestRewardEvidenceIdentity expected) : IQuestRewardEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            QuestRewardEvidenceIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(identity == expected);
        }
    }
}
