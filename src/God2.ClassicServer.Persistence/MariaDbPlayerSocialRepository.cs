using System.Data;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbPlayerSocialRepository : MariaDbRuntimeRepository, IPlayerSocialRepository
{
    internal const string ActiveRelationshipQuery = """
        SELECT `character_id_low`,`character_id_high`,`relationship_kind`,`created_at_utc`,`version`
        FROM `god2_player`.`player_relationships`
        WHERE (`character_id_low` = @characterId OR `character_id_high` = @characterId)
          AND `relationship_kind` = @kind AND `status` = 'Active'
        ORDER BY IF(`character_id_low` = @characterId, `character_id_high`, `character_id_low`);
        """;

    public MariaDbPlayerSocialRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<SocialOperationResult> RequestNameCardAsync(
        NameCardRequestCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fingerprint = $"NameCardRequest:{command.InvitationId:N}:{command.RequesterId}:{command.TargetId}";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var replay = await ReadReplayAsync(connection, transaction, command.RequesterId, command.IdempotencyKey, fingerprint, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var result = await LockCharactersAsync(connection, transaction, command.RequesterId, command.TargetId, cancellationToken) != 2
            ? Failure(SocialOperationResultCode.InvalidIdentity)
            : await RequestAfterLockAsync(connection, transaction, command, now, cancellationToken);
        await WriteOperationAsync(connection, transaction, command.RequesterId, command.IdempotencyKey, fingerprint, "NameCardRequest", result, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SocialOperationResult> RespondToNameCardAsync(
        NameCardResponseCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fingerprint = $"NameCardResponse:{command.InvitationId:N}:{command.ActorId}:{command.Accept}";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var replay = await ReadReplayAsync(connection, transaction, command.ActorId, command.IdempotencyKey, fingerprint, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var invitation = await FindInvitationAsync(connection, transaction, command.InvitationId, forUpdate: false, cancellationToken);
        SocialOperationResult result;
        if (invitation is null)
        {
            result = Failure(SocialOperationResultCode.InvitationNotFound);
        }
        else if (invitation.TargetId != command.ActorId)
        {
            result = new SocialOperationResult(SocialOperationResultCode.NotRecipient, invitation, null, false);
        }
        else if (invitation.State != SocialInvitationState.Pending)
        {
            result = new SocialOperationResult(SocialOperationResultCode.InvalidState, invitation, null, false);
        }
        else
        {
            await LockCharactersAsync(connection, transaction, invitation.RequesterId, invitation.TargetId, cancellationToken);
            invitation = await FindInvitationAsync(connection, transaction, command.InvitationId, forUpdate: true, cancellationToken);
            if (invitation is null)
            {
                result = Failure(SocialOperationResultCode.InvitationNotFound);
            }
            else if (invitation.State != SocialInvitationState.Pending)
            {
                result = new SocialOperationResult(SocialOperationResultCode.InvalidState, invitation, null, false);
            }
            else
            {
                var block = await ResolveBlockAsync(connection, transaction, invitation.RequesterId, invitation.TargetId, cancellationToken);
                if (block is not null)
                {
                    result = new SocialOperationResult(block.Value, invitation, null, false);
                }
                else
                {
                    var state = command.Accept ? SocialInvitationState.Accepted : SocialInvitationState.Rejected;
                    await using (var update = CreateCommand(connection, transaction, """
                        UPDATE `god2_player`.`player_social_invitations`
                        SET `status`=@status,`responded_at_utc`=@now,`response_actor_id`=@actorId,`version`=`version`+1
                        WHERE `invitation_id`=@invitationId AND `status`='Pending';
                        """))
                    {
                        update.Parameters.AddWithValue("@status", state.ToString());
                        update.Parameters.AddWithValue("@now", now.UtcDateTime);
                        update.Parameters.AddWithValue("@actorId", command.ActorId);
                        update.Parameters.AddWithValue("@invitationId", command.InvitationId.ToString("D"));
                        await update.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var changed = invitation with
                    {
                        State = state,
                        RespondedAtUtc = now,
                        Version = invitation.Version + 1
                    };
                    PlayerRelationshipSnapshot? relationship = null;
                    if (command.Accept)
                    {
                        relationship = await UpsertRelationshipAsync(
                            connection,
                            transaction,
                            invitation.RequesterId,
                            invitation.TargetId,
                            now,
                            cancellationToken);
                    }

                    result = new SocialOperationResult(SocialOperationResultCode.Success, changed, relationship, true);
                }
            }
        }

        await WriteOperationAsync(connection, transaction, command.ActorId, command.IdempotencyKey, fingerprint, "NameCardResponse", result, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SocialOperationResult> SetBlockAsync(
        SocialBlockCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fingerprint = $"SetBlock:{command.ActorId}:{command.TargetId}:{command.Blocked}";
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var replay = await ReadReplayAsync(connection, transaction, command.ActorId, command.IdempotencyKey, fingerprint, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        SocialOperationResult result;
        if (await LockCharactersAsync(connection, transaction, command.ActorId, command.TargetId, cancellationToken) != 2)
        {
            result = Failure(SocialOperationResultCode.InvalidIdentity);
        }
        else
        {
            var mutations = command.Blocked
                ? await InsertBlockAsync(connection, transaction, command, now, cancellationToken)
                : await DeleteBlockAsync(connection, transaction, command, cancellationToken);
            if (command.Blocked)
            {
                mutations += await EndRelationshipAsync(connection, transaction, command.ActorId, command.TargetId, now, cancellationToken);
                mutations += await CancelPendingInvitationsAsync(connection, transaction, command.ActorId, command.TargetId, now, cancellationToken);
            }

            result = new SocialOperationResult(SocialOperationResultCode.Success, null, null, mutations > 0, command.Blocked);
        }

        await WriteOperationAsync(connection, transaction, command.ActorId, command.IdempotencyKey, fingerprint, "SetBlock", result, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<PlayerRelationshipSnapshot>> ListRelationshipsAsync(
        long characterId,
        PlayerRelationshipKind kind,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = ActiveRelationshipQuery;
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@kind", kind.ToString());
        var relationships = new List<PlayerRelationshipSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var low = reader.GetInt64("character_id_low");
            var high = reader.GetInt64("character_id_high");
            relationships.Add(new PlayerRelationshipSnapshot(
                Enum.Parse<PlayerRelationshipKind>(reader.GetString("relationship_kind")),
                characterId,
                low == characterId ? high : low,
                ReadUtc(reader, "created_at_utc"),
                reader.GetInt64("version")));
        }

        return relationships;
    }

    public async Task<bool> IsBlockedAsync(long actorId, long targetId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM `god2_player`.`player_social_blocks` WHERE `blocker_character_id`=@actorId AND `blocked_character_id`=@targetId);";
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@targetId", targetId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<SocialOperationResult> RequestAfterLockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        NameCardRequestCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var block = await ResolveBlockAsync(connection, transaction, command.RequesterId, command.TargetId, cancellationToken);
        if (block is not null)
        {
            return Failure(block.Value);
        }

        if (await RelationshipExistsAsync(connection, transaction, command.RequesterId, command.TargetId, cancellationToken))
        {
            return Failure(SocialOperationResultCode.AlreadyConnected);
        }

        await using (var pending = CreateCommand(connection, transaction, """
            SELECT `invitation_id` FROM `god2_player`.`player_social_invitations`
            WHERE `kind`='NameCard' AND `status`='Pending'
              AND ((`requester_character_id`=@first AND `target_character_id`=@second)
                OR (`requester_character_id`=@second AND `target_character_id`=@first))
            LIMIT 1 FOR UPDATE;
            """))
        {
            pending.Parameters.AddWithValue("@first", command.RequesterId);
            pending.Parameters.AddWithValue("@second", command.TargetId);
            if (await pending.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return Failure(SocialOperationResultCode.InvitationPending);
            }
        }

        await using (var collision = CreateCommand(connection, transaction,
                         "SELECT EXISTS(SELECT 1 FROM `god2_player`.`player_social_invitations` WHERE `invitation_id`=@invitationId);"))
        {
            collision.Parameters.AddWithValue("@invitationId", command.InvitationId.ToString("D"));
            if (Convert.ToInt32(await collision.ExecuteScalarAsync(cancellationToken)) == 1)
            {
                return Failure(SocialOperationResultCode.ReplayConflict);
            }
        }

        await using (var insert = CreateCommand(connection, transaction, """
            INSERT INTO `god2_player`.`player_social_invitations`
                (`invitation_id`,`kind`,`requester_character_id`,`target_character_id`,`status`,`requested_at_utc`,`version`)
            VALUES (@invitationId,'NameCard',@requesterId,@targetId,'Pending',@now,1);
            """))
        {
            insert.Parameters.AddWithValue("@invitationId", command.InvitationId.ToString("D"));
            insert.Parameters.AddWithValue("@requesterId", command.RequesterId);
            insert.Parameters.AddWithValue("@targetId", command.TargetId);
            insert.Parameters.AddWithValue("@now", now.UtcDateTime);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var invitation = new SocialInvitationSnapshot(
            command.InvitationId,
            SocialInvitationKind.NameCard,
            command.RequesterId,
            command.TargetId,
            SocialInvitationState.Pending,
            now,
            null,
            1);
        return new SocialOperationResult(SocialOperationResultCode.Success, invitation, null, true);
    }

    private static async Task<SocialOperationResult?> ReadReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long actorId,
        string requestId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT `operation_fingerprint`,`result_code`,`invitation_id`,`blocked`
            FROM `god2_player`.`player_social_operations`
            WHERE `actor_character_id`=@actorId AND `request_id`=@requestId
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@requestId", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!string.Equals(reader.GetString("operation_fingerprint"), fingerprint, StringComparison.Ordinal))
        {
            return Failure(SocialOperationResultCode.ReplayConflict);
        }

        bool? blocked = reader.IsDBNull(reader.GetOrdinal("blocked"))
            ? null
            : reader.GetBoolean("blocked");
        return new SocialOperationResult(SocialOperationResultCode.DuplicateCompleted, null, null, false, blocked);
    }

    private static async Task WriteOperationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long actorId,
        string requestId,
        string fingerprint,
        string operationKind,
        SocialOperationResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_player`.`player_social_operations`
                (`actor_character_id`,`request_id`,`operation_kind`,`operation_fingerprint`,`result_code`,`invitation_id`,`related_character_id`,`mutated`,`blocked`,`created_at_utc`)
            VALUES (@actorId,@requestId,@operationKind,@fingerprint,@resultCode,@invitationId,@relatedCharacterId,@mutated,@blocked,@now);
            """);
        command.Parameters.AddWithValue("@actorId", actorId);
        command.Parameters.AddWithValue("@requestId", requestId);
        command.Parameters.AddWithValue("@operationKind", operationKind);
        command.Parameters.AddWithValue("@fingerprint", fingerprint);
        command.Parameters.AddWithValue("@resultCode", result.ResultCode.ToString());
        command.Parameters.AddWithValue("@invitationId", result.Invitation is null ? DBNull.Value : result.Invitation.InvitationId.ToString("D"));
        long? relatedCharacterId = result.Relationship is null
            ? null
            : result.Relationship.CharacterId == actorId
                ? result.Relationship.RelatedCharacterId
                : result.Relationship.CharacterId;
        command.Parameters.AddWithValue("@relatedCharacterId", relatedCharacterId is null ? DBNull.Value : relatedCharacterId.Value);
        command.Parameters.AddWithValue("@mutated", result.Mutated);
        command.Parameters.AddWithValue("@blocked", result.Blocked is null ? DBNull.Value : result.Blocked.Value);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> LockCharactersAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long first,
        long second,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT `character_id` FROM `god2_player`.`characters`
            WHERE `character_id` IN (@first,@second) AND `status` <> 'Deleted' AND `enabled`=1 AND `deleted_at_utc` IS NULL
            ORDER BY `character_id` FOR UPDATE;
            """);
        command.Parameters.AddWithValue("@first", first);
        command.Parameters.AddWithValue("@second", second);
        var count = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
        }
        return count;
    }

    private static async Task<SocialOperationResultCode?> ResolveBlockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long requesterId,
        long targetId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT `blocker_character_id`,`blocked_character_id`
            FROM `god2_player`.`player_social_blocks`
            WHERE (`blocker_character_id`=@requesterId AND `blocked_character_id`=@targetId)
               OR (`blocker_character_id`=@targetId AND `blocked_character_id`=@requesterId)
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("@requesterId", requesterId);
        command.Parameters.AddWithValue("@targetId", targetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetInt64("blocker_character_id") == requesterId)
            {
                return SocialOperationResultCode.BlockedByActor;
            }
            return SocialOperationResultCode.BlockedByTarget;
        }
        return null;
    }

    private static async Task<bool> RelationshipExistsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long first,
        long second,
        CancellationToken cancellationToken)
    {
        var (low, high) = Pair(first, second);
        await using var command = CreateCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM `god2_player`.`player_relationships`
            WHERE `character_id_low`=@low AND `character_id_high`=@high AND `relationship_kind`='NameCard' AND `status`='Active');
            """);
        command.Parameters.AddWithValue("@low", low);
        command.Parameters.AddWithValue("@high", high);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<SocialInvitationSnapshot?> FindInvitationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid invitationId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, $"""
            SELECT `invitation_id`,`kind`,`requester_character_id`,`target_character_id`,`status`,
                   `requested_at_utc`,`responded_at_utc`,`version`
            FROM `god2_player`.`player_social_invitations`
            WHERE `invitation_id`=@invitationId{(forUpdate ? " FOR UPDATE" : string.Empty)};
            """);
        command.Parameters.AddWithValue("@invitationId", invitationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SocialInvitationSnapshot(
            MaterializeGuid(reader.GetValue(reader.GetOrdinal("invitation_id"))),
            Enum.Parse<SocialInvitationKind>(reader.GetString("kind")),
            reader.GetInt64("requester_character_id"),
            reader.GetInt64("target_character_id"),
            Enum.Parse<SocialInvitationState>(reader.GetString("status")),
            ReadUtc(reader, "requested_at_utc"),
            ReadNullableUtc(reader, "responded_at_utc"),
            reader.GetInt64("version"));
    }

    internal static Guid MaterializeGuid(object value) => value switch
    {
        Guid guid => guid,
        string text when Guid.TryParse(text, out var guid) => guid,
        _ => throw new InvalidDataException($"Unsupported MariaDB GUID value type: {value.GetType().FullName}.")
    };

    private static async Task<PlayerRelationshipSnapshot> UpsertRelationshipAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long first,
        long second,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (low, high) = Pair(first, second);
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO `god2_player`.`player_relationships`
                (`character_id_low`,`character_id_high`,`relationship_kind`,`status`,`created_at_utc`,`ended_at_utc`,`version`)
            VALUES (@low,@high,'NameCard','Active',@now,NULL,1)
            ON DUPLICATE KEY UPDATE `status`='Active',`created_at_utc`=@now,`ended_at_utc`=NULL,`version`=`version`+1;
            """);
        command.Parameters.AddWithValue("@low", low);
        command.Parameters.AddWithValue("@high", high);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new PlayerRelationshipSnapshot(PlayerRelationshipKind.NameCard, first, second, now, 1);
    }

    private static async Task<int> InsertBlockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SocialBlockCommand block,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT IGNORE INTO `god2_player`.`player_social_blocks`
                (`blocker_character_id`,`blocked_character_id`,`created_at_utc`)
            VALUES (@actorId,@targetId,@now);
            """);
        command.Parameters.AddWithValue("@actorId", block.ActorId);
        command.Parameters.AddWithValue("@targetId", block.TargetId);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> DeleteBlockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SocialBlockCommand block,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            DELETE FROM `god2_player`.`player_social_blocks`
            WHERE `blocker_character_id`=@actorId AND `blocked_character_id`=@targetId;
            """);
        command.Parameters.AddWithValue("@actorId", block.ActorId);
        command.Parameters.AddWithValue("@targetId", block.TargetId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> EndRelationshipAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long first,
        long second,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (low, high) = Pair(first, second);
        await using var command = CreateCommand(connection, transaction, """
            UPDATE `god2_player`.`player_relationships`
            SET `status`='Ended',`ended_at_utc`=@now,`version`=`version`+1
            WHERE `character_id_low`=@low AND `character_id_high`=@high
              AND `relationship_kind`='NameCard' AND `status`='Active';
            """);
        command.Parameters.AddWithValue("@low", low);
        command.Parameters.AddWithValue("@high", high);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CancelPendingInvitationsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long first,
        long second,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            UPDATE `god2_player`.`player_social_invitations`
            SET `status`='Cancelled',`responded_at_utc`=@now,`version`=`version`+1
            WHERE `kind`='NameCard' AND `status`='Pending'
              AND ((`requester_character_id`=@first AND `target_character_id`=@second)
                OR (`requester_character_id`=@second AND `target_character_id`=@first));
            """);
        command.Parameters.AddWithValue("@first", first);
        command.Parameters.AddWithValue("@second", second);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MySqlCommand CreateCommand(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = commandText;
        return command;
    }

    private static (long Low, long High) Pair(long first, long second) =>
        (Math.Min(first, second), Math.Max(first, second));

    private static SocialOperationResult Failure(SocialOperationResultCode code) =>
        new(code, null, null, false);
}
