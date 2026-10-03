using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbQuestRewardClaimWriter : IQuestRewardClaimWriter
{
    private readonly string _connectionString;
    private readonly MariaDbInventoryGrantWriter _grants;
    private readonly IQuestRewardEvidenceGate _evidence;

    public MariaDbQuestRewardClaimWriter(
        MariaDbAuthenticationOptions options,
        IQuestRewardEvidenceGate evidence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(evidence);
        _connectionString = options.BuildConnectionString();
        _grants = new MariaDbInventoryGrantWriter(options);
        _evidence = evidence;
    }

    public async ValueTask<QuestRewardClaimResult> ClaimAsync(
        QuestRewardClaimRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var result = await ClaimInTransactionAsync(
                connection, transaction, request, cancellationToken);

            if (result.Status == QuestRewardClaimStatus.Claimed)
                await transaction.CommitAsync(cancellationToken);
            else
                await transaction.RollbackAsync(CancellationToken.None);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // Caller must roll back on any exception or any result other than Claimed.
    // A rejection can occur after an earlier reward has written inventory rows.
    public async ValueTask<QuestRewardClaimResult> ClaimInTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        QuestRewardClaimRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection != connection)
            throw new ArgumentException("Transaction connection mismatch.");

        MySqlCommand Command(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 15;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", request.CharacterId);
            command.Parameters.AddWithValue(
                "@instance", request.QuestInstanceId.ToString("D"));
            return command;
        }

        QuestRewardClaimResult Reject(QuestRewardClaimStatus status) =>
            new(status, request.TransactionId, request.QuestInstanceId, -1, -1);

        // Match inventory grants: character is always the first authority lock.
        using (var command = Command("""
            SELECT character_id FROM god2_player.characters
            WHERE character_id=@character AND deleted_at_utc IS NULL
            FOR UPDATE;
            """))
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
                return Reject(QuestRewardClaimStatus.CharacterMissing);
        }

        long questId;
        long questVersion;
        string state;
        string definitionFingerprint;

        using (var command = Command("""
            SELECT QuestId, QuestVersion, State, DefinitionFingerprint
            FROM god2_player.v2_quest_instances
            WHERE QuestInstanceId=@instance AND CharacterId=@character
            FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Reject(QuestRewardClaimStatus.QuestInstanceMissing);

            questId = reader.GetInt64(0);
            questVersion = reader.GetInt64(1);
            state = reader.GetString(2);
            definitionFingerprint = reader.GetString(3);
        }

        string rewardFingerprint;
        string rewardJson;
        string evidenceReference;

        using (var command = Command("""
            SELECT RewardFingerprint, RewardJson, EvidenceReference
            FROM god2_player.v2_quest_reward_snapshots
            WHERE QuestInstanceId=@instance FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Reject(QuestRewardClaimStatus.RewardSnapshotMissing);

            rewardFingerprint = reader.GetString(0);
            rewardJson = reader.GetString(1);
            evidenceReference = reader.GetString(2);
        }

        // Fingerprints use lowercase SHA256 of the exact persisted UTF-8 text.
        if (!IsHash(definitionFingerprint) ||
            !IsHash(rewardFingerprint) ||
            Hash(rewardJson) != rewardFingerprint)
            return Reject(QuestRewardClaimStatus.RewardSnapshotInvalid);

        // Instance identity, rather than the caller's transaction ID, prevents
        // repeated claims with different request identifiers or versions.
        using (var command = Command("""
            SELECT CharacterId, ClaimTransactionId, RewardFingerprint,
                   InventoryVersionBefore, InventoryVersionAfter
            FROM god2_player.v2_quest_reward_claims
            WHERE QuestInstanceId=@instance FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt64(0) != request.CharacterId ||
                    reader.GetString(2) != rewardFingerprint ||
                    state != "Completed")
                    return Reject(QuestRewardClaimStatus.ClaimConflict);

                return new QuestRewardClaimResult(
                    QuestRewardClaimStatus.Replayed,
                    reader.GetGuid(1),
                    request.QuestInstanceId,
                    reader.GetInt64(3),
                    reader.GetInt64(4));
            }
        }

        if (state != "Ready")
            return Reject(QuestRewardClaimStatus.QuestNotReady);
        if (questVersion != request.ExpectedQuestVersion)
            return Reject(QuestRewardClaimStatus.QuestVersionConflict);

        var nextQuestVersion = checked(questVersion + 1);

        if (string.IsNullOrWhiteSpace(evidenceReference) ||
            !await _evidence.IsApprovedAsync(
                new QuestRewardEvidenceIdentity(
                    questId, definitionFingerprint,
                    rewardFingerprint, evidenceReference),
                cancellationToken))
            return Reject(QuestRewardClaimStatus.RewardEvidenceBlocked);

        QuestRewardEntry[]? rewards;
        try
        {
            rewards = JsonSerializer.Deserialize<QuestRewardEntry[]>(rewardJson);
        }
        catch (JsonException)
        {
            return Reject(QuestRewardClaimStatus.RewardSnapshotInvalid);
        }

        if (rewards is null || rewards.Any(value => value is null))
            return Reject(QuestRewardClaimStatus.RewardSnapshotInvalid);

        Guid inventoryId;
        int capacity;
        long version;
        long sequence;
        string dirtyState;

        using (var command = Command("""
            SELECT InventoryId, Capacity, InventoryVersion,
                   MutationSequence, DirtyState
            FROM god2_player.player_inventory_state
            WHERE CharacterId=@character FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Reject(QuestRewardClaimStatus.InventoryMissing);

            inventoryId = reader.GetGuid(0);
            capacity = reader.GetInt32(1);
            version = reader.GetInt64(2);
            sequence = reader.GetInt64(3);
            dirtyState = reader.GetString(4);
        }

        if (inventoryId != request.InventoryId ||
            version != request.ExpectedInventoryVersion ||
            sequence != request.ExpectedMutationSequence)
            return Reject(QuestRewardClaimStatus.InventoryVersionConflict);

        var slots = new List<CharacterInventorySlot>();
        using (var command = Command("""
            SELECT slot_index, item_id, quantity,
                   bind_state, item_instance_metadata
            FROM god2_player.character_inventory
            WHERE character_id=@character
              AND enabled=1 AND deleted_at_utc IS NULL
            ORDER BY slot_index FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var quantity = reader.GetInt64(2);
                if (quantity <= 0 || quantity > int.MaxValue)
                    throw new InvalidDataException("Invalid inventory quantity.");

                slots.Add(new CharacterInventorySlot(
                    reader.GetInt32(0), reader.GetInt64(1), (int)quantity,
                    reader.GetString(3), reader.GetString(4)));
            }
        }

        var rules = new Dictionary<long, ItemStackRule>();
        foreach (var itemId in rewards
            .Where(value => value.ItemId is > 0 and <= int.MaxValue)
            .Select(value => value.ItemId!.Value)
            .Distinct().Order())
        {
            using var command = Command("""
                SELECT item_id, name_zh_tw, maximum_stack, enabled
                FROM god2_game.items
                WHERE item_id=@item LOCK IN SHARE MODE;
                """);
            command.Parameters.AddWithValue("@item", itemId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                rules.Add(itemId, new ItemStackRule(
                    reader.GetInt64(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.GetBoolean(3)));
            }
        }

        var preview = QuestRewardBatchPlanner.Plan(
            new CharacterInventorySnapshot(
                inventoryId, request.CharacterId, capacity,
                version, sequence, dirtyState, slots),
            rewards, rules);

        if (!preview.Succeeded)
        {
            return Reject(preview.Failure switch
            {
                QuestRewardBatchFailure.UnsupportedReward =>
                    QuestRewardClaimStatus.UnsupportedReward,
                QuestRewardBatchFailure.ItemMissing =>
                    QuestRewardClaimStatus.ItemMissing,
                QuestRewardBatchFailure.ItemDisabled =>
                    QuestRewardClaimStatus.ItemDisabled,
                QuestRewardBatchFailure.InsufficientCapacity =>
                    QuestRewardClaimStatus.InsufficientCapacity,
                _ => QuestRewardClaimStatus.RewardSnapshotInvalid
            });
        }

        // Each item grant advances the inventory version and sequence.
        // Preflight overflow for the entire batch before writing any reward.
        var finalVersion = checked(version + rewards.Length);
        var finalSequence = checked(sequence + rewards.Length);
        var currentVersion = version;
        var currentSequence = sequence;

        foreach (var reward in rewards.OrderBy(value => value.RewardId))
        {
            var identity =
                $"God2.ServerV2.QuestReward/1:" +
                $"{request.QuestInstanceId:D}:{reward.RewardId}";
            var grantTransactionId = new Guid(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));

            var result = await _grants.GrantInTransactionAsync(
                connection, transaction,
                new InventoryGrantRequest(
                    grantTransactionId,
                    identity,
                    $"QuestReward:{request.QuestInstanceId:D}:{reward.RewardId}",
                    request.CharacterId,
                    inventoryId,
                    currentVersion,
                    currentSequence,
                    reward.ItemId!.Value,
                    reward.Quantity!.Value),
                cancellationToken);

            // A prior per-item replay without a completed claim is inconsistent:
            // all item grants and the claim must commit together.
            if (result.Status != InventoryGrantStatus.Granted)
            {
                return Reject(result.Status switch
                {
                    InventoryGrantStatus.ItemMissing =>
                        QuestRewardClaimStatus.ItemMissing,
                    InventoryGrantStatus.ItemDisabled =>
                        QuestRewardClaimStatus.ItemDisabled,
                    InventoryGrantStatus.InsufficientCapacity =>
                        QuestRewardClaimStatus.InsufficientCapacity,
                    InventoryGrantStatus.VersionConflict =>
                        QuestRewardClaimStatus.InventoryVersionConflict,
                    _ => QuestRewardClaimStatus.ClaimConflict
                });
            }

            currentVersion = checked(currentVersion + 1);
            currentSequence = checked(currentSequence + 1);
        }

        if (currentVersion != finalVersion || currentSequence != finalSequence)
            throw new InvalidDataException("Reward batch version mismatch.");

        var claimed = new QuestRewardClaimResult(
            QuestRewardClaimStatus.Claimed,
            request.TransactionId, request.QuestInstanceId,
            version, finalVersion);

        using (var command = Command("""
            INSERT INTO god2_player.v2_quest_reward_claims
                (QuestInstanceId, CharacterId, ClaimTransactionId,
                 RewardFingerprint, InventoryVersionBefore,
                 InventoryVersionAfter, ResultJson)
            VALUES (@instance, @character, @transaction,
                    @fingerprint, @before, @after, @result);
            """))
        {
            command.Parameters.AddWithValue(
                "@transaction", request.TransactionId.ToString("D"));
            command.Parameters.AddWithValue("@fingerprint", rewardFingerprint);
            command.Parameters.AddWithValue("@before", version);
            command.Parameters.AddWithValue("@after", finalVersion);
            command.Parameters.AddWithValue(
                "@result", JsonSerializer.Serialize(claimed));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var command = Command("""
            UPDATE god2_player.v2_quest_instances
            SET State='Completed', QuestVersion=@next,
                CompletedAtUtc=UTC_TIMESTAMP(6),
                UpdatedAtUtc=UTC_TIMESTAMP(6)
            WHERE QuestInstanceId=@instance AND CharacterId=@character
              AND State='Ready' AND QuestVersion=@expected;
            """))
        {
            command.Parameters.AddWithValue("@next", nextQuestVersion);
            command.Parameters.AddWithValue("@expected", questVersion);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("Quest completion CAS failed.");
        }

        return claimed;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsHash(string value) =>
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Validate(QuestRewardClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionId == Guid.Empty ||
            request.QuestInstanceId == Guid.Empty ||
            request.InventoryId == Guid.Empty ||
            request.CharacterId <= 0 ||
            request.ExpectedQuestVersion < 0 ||
            request.ExpectedInventoryVersion < 0 ||
            request.ExpectedMutationSequence < 0)
            throw new ArgumentException("Invalid quest reward claim request.");
    }
}
