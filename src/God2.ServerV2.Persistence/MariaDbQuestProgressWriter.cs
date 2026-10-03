using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbQuestProgressWriter : IQuestProgressWriter
{
    private readonly string _connectionString;
    private readonly IQuestProgressEvidenceGate _evidence;

    public MariaDbQuestProgressWriter(
        MariaDbAuthenticationOptions options,
        IQuestProgressEvidenceGate evidence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(evidence);
        _connectionString = options.BuildConnectionString();
        _evidence = evidence;
    }

    public async ValueTask<QuestProgressResult> ApplyAsync(
        QuestProgressRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var result = await ApplyInTransactionAsync(
                connection, transaction, request, cancellationToken);
            if (result.Status == QuestProgressStatus.Applied)
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

    // Caller owns the transaction. Roll back on exceptions or non-Applied results.
    public async ValueTask<QuestProgressResult> ApplyInTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        QuestProgressRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection != connection)
            throw new ArgumentException("Transaction connection mismatch.");

        var progressEvent = request.Event;

        MySqlCommand Command(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 15;
            command.CommandText = sql;
            command.Parameters.AddWithValue(
                "@character", progressEvent.CharacterId);
            command.Parameters.AddWithValue(
                "@instance", request.QuestInstanceId.ToString("D"));
            command.Parameters.AddWithValue(
                "@event", progressEvent.EventId.ToString("D"));
            return command;
        }

        QuestProgressResult Reject(QuestProgressStatus status) =>
            new(status, request.QuestInstanceId, progressEvent.EventId,
                -1, -1, false, Array.Empty<QuestObjectiveProgress>());

        using (var command = Command("""
            SELECT character_id FROM god2_player.characters
            WHERE character_id=@character AND deleted_at_utc IS NULL
            FOR UPDATE;
            """))
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
                return Reject(QuestProgressStatus.CharacterMissing);
        }

        long questId;
        long version;
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
                return Reject(QuestProgressStatus.QuestInstanceMissing);

            questId = reader.GetInt64(0);
            version = reader.GetInt64(1);
            state = reader.GetString(2);
            definitionFingerprint = reader.GetString(3);
        }

        // Retry versions can change, but the original event identity/payload cannot.
        var eventFingerprint = Hash(JsonSerializer.Serialize(progressEvent));

        using (var command = Command("""
            SELECT CharacterId, EventFingerprint, ResultJson
            FROM god2_player.v2_quest_progress_events
            WHERE QuestInstanceId=@instance AND EventId=@event
            FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt64(0) != progressEvent.CharacterId ||
                    reader.GetString(1) != eventFingerprint)
                    return Reject(QuestProgressStatus.EventConflict);

                var recorded = JsonSerializer.Deserialize<QuestProgressResult>(
                    reader.GetString(2));
                if (recorded is null ||
                    recorded.Status != QuestProgressStatus.Applied ||
                    recorded.QuestInstanceId != request.QuestInstanceId ||
                    recorded.EventId != progressEvent.EventId)
                    throw new InvalidDataException("Invalid quest event result.");

                return recorded with { Status = QuestProgressStatus.Replayed };
            }
        }

        if (state != "Accepted")
            return Reject(QuestProgressStatus.QuestNotActive);
        if (version != request.ExpectedQuestVersion)
            return Reject(QuestProgressStatus.VersionConflict);

        var objectives = new List<QuestObjectiveProgress>();
        using (var command = Command("""
            SELECT ObjectiveId, ObjectiveKind, TargetId,
                   RequiredCount, CurrentCount
            FROM god2_player.v2_quest_objective_progress
            WHERE QuestInstanceId=@instance
            ORDER BY ObjectiveId FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Enum.TryParse<QuestObjectiveKind>(
                        reader.GetString(1), out var kind) ||
                    !Enum.IsDefined(kind))
                    throw new InvalidDataException("Unknown objective kind.");

                objectives.Add(new QuestObjectiveProgress(
                    reader.GetInt64(0), kind, reader.GetInt64(2),
                    reader.GetInt64(3), reader.GetInt64(4)));
            }
        }

        var plan = QuestProgressPlanner.Plan(
            new QuestProgressSnapshot(
                request.QuestInstanceId,
                progressEvent.CharacterId, version, objectives),
            progressEvent);

        if (plan.Failure == QuestProgressFailure.EmptyObjectives)
            return Reject(QuestProgressStatus.EmptyObjectives);
        if (!plan.Succeeded)
            throw new InvalidDataException("Quest planning identity mismatch.");

        var objectiveFingerprint = Hash(JsonSerializer.Serialize(
            objectives.Select(value => value with { CurrentCount = 0 }).ToArray()));

        if (!await _evidence.IsApprovedAsync(
                new QuestProgressDefinitionIdentity(
                    questId, definitionFingerprint, objectiveFingerprint),
                cancellationToken))
            return Reject(QuestProgressStatus.EvidenceBlocked);

        var changed = plan.ChangedObjectives.Count > 0;
        var ready = changed && plan.Ready;
        var nextVersion = changed ? checked(version + 1) : version;

        foreach (var objective in plan.ChangedObjectives)
        {
            var before = objectives.Single(
                value => value.ObjectiveId == objective.ObjectiveId);
            using var command = Command("""
                UPDATE god2_player.v2_quest_objective_progress
                SET CurrentCount=@after, UpdatedAtUtc=UTC_TIMESTAMP(6)
                WHERE QuestInstanceId=@instance
                  AND ObjectiveId=@objective AND CurrentCount=@before;
                """);
            command.Parameters.AddWithValue("@objective", objective.ObjectiveId);
            command.Parameters.AddWithValue("@before", before.CurrentCount);
            command.Parameters.AddWithValue("@after", objective.CurrentCount);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("Quest objective CAS failed.");
        }

        if (changed)
        {
            using var command = Command("""
                UPDATE god2_player.v2_quest_instances
                SET QuestVersion=@next, State=@state,
                    CompletionEventId=@completion,
                    ReadyAtUtc=@readyAt, UpdatedAtUtc=UTC_TIMESTAMP(6)
                WHERE QuestInstanceId=@instance AND CharacterId=@character
                  AND State='Accepted' AND QuestVersion=@expected;
                """);
            command.Parameters.AddWithValue("@next", nextVersion);
            command.Parameters.AddWithValue("@expected", version);
            command.Parameters.AddWithValue("@state", ready ? "Ready" : "Accepted");
            command.Parameters.AddWithValue(
                "@completion",
                ready ? progressEvent.EventId.ToString("D") : (object)DBNull.Value);
            command.Parameters.AddWithValue(
                "@readyAt", ready ? DateTime.UtcNow : (object)DBNull.Value);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("Quest progress CAS failed.");
        }

        var result = new QuestProgressResult(
            QuestProgressStatus.Applied,
            request.QuestInstanceId, progressEvent.EventId,
            version, nextVersion, ready, plan.ChangedObjectives);

        using (var command = Command("""
            INSERT INTO god2_player.v2_quest_progress_events
                (QuestInstanceId, EventId, CharacterId, EventFingerprint,
                 QuestVersionBefore, QuestVersionAfter, ResultJson)
            VALUES (@instance,@event,@character,@fingerprint,
                    @before,@after,@result);
            """))
        {
            command.Parameters.AddWithValue("@fingerprint", eventFingerprint);
            command.Parameters.AddWithValue("@before", version);
            command.Parameters.AddWithValue("@after", nextVersion);
            command.Parameters.AddWithValue(
                "@result", JsonSerializer.Serialize(result));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Validate(QuestProgressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Event);
        if (request.QuestInstanceId == Guid.Empty ||
            request.ExpectedQuestVersion < 0 ||
            request.Event.EventId == Guid.Empty ||
            request.Event.CharacterId <= 0 ||
            request.Event.TargetId <= 0 ||
            request.Event.Count <= 0 ||
            !Enum.IsDefined(request.Event.Kind) ||
            (request.Event.Kind != QuestObjectiveKind.DefeatMonster &&
             request.Event.Count != 1))
            throw new ArgumentException("Invalid quest progress request.");
    }
}
