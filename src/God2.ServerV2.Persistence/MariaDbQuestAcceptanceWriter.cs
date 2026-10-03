using System.Data;
using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbQuestAcceptanceWriter
    : IQuestAcceptanceWriter
{
    private readonly string _connectionString;
    private readonly IQuestAcceptanceDefinitionSource _definitions;
    private readonly IQuestAcceptanceEvidenceGate _evidence;

    public MariaDbQuestAcceptanceWriter(
        MariaDbAuthenticationOptions options,
        IQuestAcceptanceDefinitionSource definitions,
        IQuestAcceptanceEvidenceGate evidence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(evidence);
        _connectionString = options.BuildConnectionString();
        _definitions = definitions;
        _evidence = evidence;
    }

    public async ValueTask<QuestAcceptanceResult> AcceptAsync(
        QuestAcceptanceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.QuestInstanceId == Guid.Empty ||
            request.CharacterId <= 0 || request.QuestId <= 0 ||
            request.ExpectedDefinitionFingerprint is not { Length: 64 } ||
            request.ExpectedDefinitionFingerprint.Any(
                x => !((x >= '0' && x <= '9') || (x >= 'a' && x <= 'f'))))
            throw new ArgumentException("Invalid quest acceptance request.");

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        MySqlCommand Command(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 15;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", request.CharacterId);
            command.Parameters.AddWithValue("@quest", request.QuestId);
            command.Parameters.AddWithValue(
                "@instance", request.QuestInstanceId.ToString("D"));
            return command;
        }

        QuestAcceptanceResult Result(QuestAcceptanceStatus status) =>
            new(status, request.QuestInstanceId,
                request.ExpectedDefinitionFingerprint);

        try
        {
            var result = await Register();
            if (result.Status == QuestAcceptanceStatus.Accepted)
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

        async ValueTask<QuestAcceptanceResult> Register()
        {
            int? level;
            using (var command = Command("""
                SELECT level FROM god2_player.characters
                WHERE character_id=@character AND deleted_at_utc IS NULL
                FOR UPDATE;
                """))
            {
                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Result(QuestAcceptanceStatus.CharacterMissing);
                level = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            }

            // A retry cannot create another instance or replace snapshots.
            using (var command = Command("""
                SELECT CharacterId, QuestId, DefinitionFingerprint
                FROM god2_player.v2_quest_instances
                WHERE QuestInstanceId=@instance FOR UPDATE;
                """))
            {
                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var matches =
                        reader.GetInt64(0) == request.CharacterId &&
                        reader.GetInt64(1) == request.QuestId &&
                        reader.GetString(2) ==
                            request.ExpectedDefinitionFingerprint;
                    await reader.DisposeAsync();

                    if (!matches)
                        return Result(QuestAcceptanceStatus.InstanceConflict);

                    using var snapshot = Command("""
                        SELECT COUNT(*)
                        FROM god2_player.v2_quest_reward_snapshots
                        WHERE QuestInstanceId=@instance;
                        """);
                    if (Convert.ToInt64(
                            await snapshot.ExecuteScalarAsync(cancellationToken)) != 1)
                        throw new InvalidDataException(
                            "Accepted quest reward snapshot is missing.");

                    using var objectiveCountCommand = Command("""
                        SELECT COUNT(*)
                        FROM god2_player.v2_quest_objective_progress
                        WHERE QuestInstanceId=@instance;
                        """);
                    if (Convert.ToInt64(
                            await objectiveCountCommand.ExecuteScalarAsync(cancellationToken)) == 0)
                        throw new InvalidDataException(
                            "Accepted quest objectives are missing.");

                    return Result(QuestAcceptanceStatus.Replayed);
                }
            }

            // First slice: one acceptance per character/quest, across all states.
            // Character locking serializes different instance IDs for one owner.
            using (var command = Command("""
                SELECT QuestInstanceId FROM god2_player.v2_quest_instances
                WHERE CharacterId=@character AND QuestId=@quest
                LIMIT 1 FOR UPDATE;
                """))
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is not null)
                    return Result(QuestAcceptanceStatus.AlreadyAccepted);
            }

            using (var command = Command("""
                SELECT enabled FROM god2_game.quests
                WHERE quest_id=@quest LOCK IN SHARE MODE;
                """))
            {
                var enabled = await command.ExecuteScalarAsync(cancellationToken);
                if (enabled is null)
                    return Result(QuestAcceptanceStatus.QuestMissing);
                if (!Convert.ToBoolean(enabled))
                    return Result(QuestAcceptanceStatus.QuestDisabled);
            }

            var definition = await _definitions.GetAsync(
                request.QuestId, cancellationToken);
            if (definition is null)
                return Result(QuestAcceptanceStatus.DefinitionMissing);
            if (definition.QuestId != request.QuestId)
                throw new InvalidDataException("Quest definition identity mismatch.");

            var prepared = QuestAcceptanceDefinitionValidator.Prepare(definition);
            if (prepared.DefinitionFingerprint !=
                request.ExpectedDefinitionFingerprint)
                return Result(QuestAcceptanceStatus.DefinitionConflict);

            // Unknown formal eligibility cannot be silently filled in.
            if (!await _evidence.IsApprovedAsync(
                    prepared, request.CharacterId, cancellationToken))
                return Result(QuestAcceptanceStatus.EvidenceBlocked);

            if (level is null || level < 1)
                return Result(QuestAcceptanceStatus.CharacterLevelUnknown);
            if (level < prepared.MinimumLevel ||
                prepared.MaximumLevel is int maximum && level > maximum)
                return Result(QuestAcceptanceStatus.LevelRejected);

            // Read back the frozen strings, not caller-owned mutable collections.
            var objectives = JsonSerializer.Deserialize<QuestObjectiveProgress[]>(
                prepared.ObjectivesJson)
                ?? throw new InvalidDataException("Invalid objective snapshot.");

            using (var command = Command("""
                INSERT INTO god2_player.v2_quest_instances
                    (QuestInstanceId,CharacterId,QuestId,DefinitionFingerprint,
                     QuestVersion,State)
                VALUES (@instance,@character,@quest,@fingerprint,0,'Accepted');
                """))
            {
                command.Parameters.AddWithValue(
                    "@fingerprint", prepared.DefinitionFingerprint);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var objective in objectives)
            {
                using var command = Command("""
                    INSERT INTO god2_player.v2_quest_objective_progress
                        (QuestInstanceId,ObjectiveId,ObjectiveKind,TargetId,
                         RequiredCount,CurrentCount)
                    VALUES (@instance,@objective,@kind,@target,@required,0);
                    """);
                command.Parameters.AddWithValue("@objective", objective.ObjectiveId);
                command.Parameters.AddWithValue("@kind", objective.Kind.ToString());
                command.Parameters.AddWithValue("@target", objective.TargetId);
                command.Parameters.AddWithValue("@required", objective.RequiredCount);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var command = Command("""
                INSERT INTO god2_player.v2_quest_reward_snapshots
                    (QuestInstanceId,RewardFingerprint,RewardJson,EvidenceReference)
                VALUES (@instance,@fingerprint,@json,@evidence);
                """))
            {
                command.Parameters.AddWithValue(
                    "@fingerprint", prepared.RewardFingerprint);
                command.Parameters.AddWithValue("@json", prepared.RewardJson);
                command.Parameters.AddWithValue(
                    "@evidence", prepared.EvidenceReference);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return Result(QuestAcceptanceStatus.Accepted);
        }
    }
}
