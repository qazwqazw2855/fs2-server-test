using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public abstract class MariaDbRuntimeRepository
{
    private readonly DatabaseOptions _options;

    protected MariaDbRuntimeRepository(DatabaseOptions options)
    {
        _options = options;
    }

    protected static int CommandTimeoutSeconds => 30;

    protected async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var password = MariaDbDatabaseBootstrapper.ResolvePassword(_options);
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("MariaDB password is not configured for the formal runtime repository.");
        }

        var connection = new MySqlConnection(
            MariaDbDatabaseBootstrapper.BuildConnectionString(_options, password, _options.DatabaseName));
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var runtimeMode = connection.CreateCommand();
            runtimeMode.CommandText = "SET @god2_runtime_mode := 1;";
            await runtimeMode.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    protected static DateTimeOffset ReadUtc(MySqlDataReader reader, string name) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(name), DateTimeKind.Utc));

    protected static DateTimeOffset? ReadNullableUtc(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : ReadUtc(reader, name);
}

public sealed class MariaDbAccountRepository : MariaDbRuntimeRepository, IProductionAccountRepository
{
    private const string AccountColumns = """
        `account_id` AS `Id`, `username` AS `LoginName`, `password_hash` AS `PasswordHash`,
        `status` AS `Status`, `created_at_utc` AS `CreatedAtUtc`, `last_login_at_utc` AS `LastLoginAtUtc`,
        `failed_login_count` AS `FailedLoginCount`, `locked_until_utc` AS `LockedUntilUtc`,
        `current_session_id` AS `CurrentSessionId`, `concurrency_token` AS `ConcurrencyToken`
        """;

    public MariaDbAccountRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public Task<AccountRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
        FindAsync("`username` = @value", username, cancellationToken);

    public Task<AccountRecord?> FindByIdAsync(long accountId, CancellationToken cancellationToken) =>
        FindAsync("`account_id` = @value", accountId, cancellationToken);

    internal const string ReplaceCurrentSessionCommandText = """
        UPDATE `god2_player`.`accounts`
        SET `last_login_at_utc` = @now,
            `updated_at_utc` = @now,
            `failed_login_count` = 0,
            `locked_until_utc` = NULL,
            `current_session_id` = @replacementSessionId,
            `concurrency_token` = @token
        WHERE `account_id` = @accountId
          AND `current_session_id` <=> @expectedCurrentSessionId;
        """;

    public async Task<OperationResult> ReplaceCurrentSessionAsync(
        long accountId,
        string? expectedCurrentSessionId,
        string replacementSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = ReplaceCurrentSessionCommandText;
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        command.Parameters.AddWithValue(
            "@expectedCurrentSessionId",
            expectedCurrentSessionId is null ? DBNull.Value : expectedCurrentSessionId);
        command.Parameters.AddWithValue("@replacementSessionId", replacementSessionId);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@accountId", accountId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 1
            ? OperationResult.Success
            : OperationResult.Failure(
                "account.session_conflict",
                "Account session ownership changed before the replacement could be committed.");
    }

    public async Task<OperationResult> MarkLoginFailureAsync(
        long accountId,
        int lockThreshold,
        TimeSpan lockDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`accounts`
            SET `failed_login_count` = `failed_login_count` + 1,
                `locked_until_utc` = CASE
                    WHEN `failed_login_count` + 1 >= @lockThreshold THEN @lockedUntil
                    ELSE `locked_until_utc`
                END,
                `updated_at_utc` = @now,
                `concurrency_token` = @token
            WHERE `account_id` = @accountId;
            """;
        command.Parameters.AddWithValue("@lockThreshold", Math.Max(1, lockThreshold));
        command.Parameters.AddWithValue("@lockedUntil", now.Add(lockDuration).UtcDateTime);
        command.Parameters.AddWithValue("@now", now.UtcDateTime);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@accountId", accountId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0
            ? OperationResult.Failure("account.not_found", "Account not found.")
            : OperationResult.Success;
    }

    public async Task ClearCurrentSessionAsync(
        long accountId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`accounts`
            SET `current_session_id` = NULL,
                `updated_at_utc` = UTC_TIMESTAMP(6),
                `concurrency_token` = @token
            WHERE `account_id` = @accountId AND `current_session_id` = @sessionId;
            """;
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<AccountRecord?> FindAsync(
        string predicate,
        object value,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"SELECT {AccountColumns} FROM `god2_player`.`accounts` WHERE {predicate} LIMIT 1;";
        command.Parameters.AddWithValue("@value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAccount(reader) : null;
    }

    private static AccountRecord ReadAccount(MySqlDataReader reader)
    {
        var statusText = reader.GetString("Status");
        var status = ParseAccountStatus(statusText);
        return new AccountRecord(
            reader.GetInt64("Id"),
            reader.GetString("LoginName"),
            reader.GetString("PasswordHash"),
            status,
            ReadUtc(reader, "CreatedAtUtc"),
            ReadNullableUtc(reader, "LastLoginAtUtc"),
            reader.GetInt32("FailedLoginCount"),
            ReadNullableUtc(reader, "LockedUntilUtc"),
            reader.IsDBNull(reader.GetOrdinal("CurrentSessionId")) ? null : reader.GetString("CurrentSessionId"),
            reader.GetString("ConcurrencyToken"));
    }

    private static AccountStatus ParseAccountStatus(string value) => value switch
    {
        "啟用" => AccountStatus.Active,
        "停用" => AccountStatus.Disabled,
        "鎖定" => AccountStatus.Locked,
        _ when Enum.TryParse<AccountStatus>(value, ignoreCase: true, out var parsed) => parsed,
        _ => AccountStatus.Disabled
    };
}

public sealed class MariaDbCharacterCreationAuthority : MariaDbRuntimeRepository, IProductionCharacterCreationAuthority
{
    public const string ResolveCommandText = """
        SELECT profile.`map_id` AS `MapId`, profile.`position_x` AS `PositionX`,
               profile.`position_y` AS `PositionY`, 'Verified' AS `EvidenceStatus`,
               'formal-runtime-enabled' AS `EvidenceReference`
        FROM `god2_game`.`character_creation_profiles` profile
        JOIN `god2_game`.`character_classes` class_row
          ON class_row.`code` = profile.`class_code`
        JOIN `god2_game`.`class_level_stats` level_stats
          ON level_stats.`class_id` = class_row.`class_id`
         AND level_stats.`level` = 1
        JOIN `god2_game`.`maps` map_row ON map_row.`map_id` = profile.`map_id`
        WHERE profile.`client_build_id` = @clientBuildId
          AND profile.`class_code` = @class
          AND profile.`gender_code` = @gender
          AND profile.`life_skill_code` = @lifeSkill
          AND profile.`enabled` = 1
          AND class_row.`enabled` = 1
          AND level_stats.`enabled` = 1
          AND level_stats.`base_max_hp` IS NOT NULL
          AND level_stats.`base_max_mp` IS NOT NULL
          AND map_row.`enabled` = 1
          AND profile.`position_x` BETWEEN 0 AND ((map_row.`width` * 21) - 1)
          AND profile.`position_y` BETWEEN 0 AND ((map_row.`height` * 21) - 1)
        LIMIT 2;
        """;

    private readonly string _clientBuildId;

    public MariaDbCharacterCreationAuthority(DatabaseOptions options, string clientBuildId)
        : base(options)
    {
        _clientBuildId = clientBuildId;
    }

    public async Task<OperationResult<CharacterCreationSpawn>> ResolveAsync(
        CharacterCreateRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = ResolveCommandText;
        command.Parameters.AddWithValue("@clientBuildId", _clientBuildId);
        command.Parameters.AddWithValue("@class", request.Class);
        command.Parameters.AddWithValue("@gender", request.Gender);
        command.Parameters.AddWithValue("@lifeSkill", request.LifeSkill);

        var matches = new List<CharacterCreationSpawn>(capacity: 2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new CharacterCreationSpawn(
                reader.GetInt32("MapId"),
                reader.GetInt32("PositionX"),
                reader.GetInt32("PositionY"),
                reader.GetString("EvidenceStatus"),
                reader.GetString("EvidenceReference")));
        }

        return matches.Count == 1 && matches[0].IsProductionEligible
            ? OperationResult<CharacterCreationSpawn>.Success(matches[0])
            : OperationResult<CharacterCreationSpawn>.Failure(
                "character.creation_profile_evidence_blocked",
                matches.Count == 0
                    ? "No production-enabled character creation profile matches the official client selection."
                    : "Character creation profile resolution is ambiguous or not production eligible.");
    }
}

public sealed class MariaDbCharacterRepository : MariaDbRuntimeRepository, IProductionCharacterRepository
{
    private const string CharacterColumns = """
        `character_id` AS `Id`, `account_id` AS `AccountId`, `name` AS `Name`,
        `class_code` AS `Class`, `gender_code` AS `Gender`, `life_skill_code` AS `LifeSkill`,
        `level` AS `Level`, `appearance_code` AS `Appearance`, `map_id` AS `MapId`,
        `position_x` AS `PositionX`, `position_y` AS `PositionY`, `status` AS `Status`,
        `created_at_utc` AS `CreatedAtUtc`, `last_played_at_utc` AS `LastPlayedAtUtc`,
        `current_hp` AS `CurrentHitPoints`, `current_mp` AS `CurrentMagicPoints`,
        `max_hp` AS `MaximumHitPoints`, `max_mp` AS `MaximumMagicPoints`,
        `remaining_stat_points` AS `RemainingStatPoints`,
        CASE WHEN `constitution_base` IS NULL AND `constitution_bonus` IS NULL THEN NULL
             ELSE COALESCE(`constitution_base`, 0) + COALESCE(`constitution_bonus`, 0) END AS `Constitution`,
        CASE WHEN `strength_base` IS NULL AND `strength_bonus` IS NULL THEN NULL
             ELSE COALESCE(`strength_base`, 0) + COALESCE(`strength_bonus`, 0) END AS `Strength`,
        CASE WHEN `intelligence_base` IS NULL AND `intelligence_bonus` IS NULL THEN NULL
             ELSE COALESCE(`intelligence_base`, 0) + COALESCE(`intelligence_bonus`, 0) END AS `Intelligence`,
        CASE WHEN `speed_base` IS NULL AND `speed_bonus` IS NULL THEN NULL
             ELSE COALESCE(`speed_base`, 0) + COALESCE(`speed_bonus`, 0) END AS `Speed`
        """;

    internal const string CreateLifeSkillRowsCommandText = """
        INSERT INTO `god2_player`.`character_life_skills`
            (`character_id`, `life_skill_id`, `level`, `experience`, `proficiency`, `progress_value`,
             `is_unlocked`, `is_active`, `admin_note`)
        SELECT @characterId, skill.`life_skill_id`, NULL, NULL, NULL, NULL, 0, 0,
               '角色建立交易同步建立固定四項生活技能；等級與熟練度等待正式遊戲證據。'
        FROM `god2_game`.`life_skills` skill
        WHERE skill.`enabled` = 1
          AND skill.`code` IN ('ArmorForging', 'WeaponForging', 'PillAlchemy', 'MagicTreasureForging')
        ORDER BY skill.`life_skill_id`;
        """;

    public MariaDbCharacterRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT {CharacterColumns}
            FROM `god2_player`.`characters`
            WHERE `account_id` = @accountId
              AND `status` NOT IN ('Deleted','已刪除') AND `enabled` = 1 AND `deleted_at_utc` IS NULL
            ORDER BY `created_at_utc`, `character_id`;
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        var characters = new List<CharacterSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            characters.Add(ReadCharacter(reader));
        }

        return characters;
    }

    public async Task<CharacterSummary?> FindByIdAsync(long characterId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"SELECT {CharacterColumns} FROM `god2_player`.`characters` WHERE `character_id` = @characterId AND `status` NOT IN ('Deleted','已刪除') AND `enabled` = 1 AND `deleted_at_utc` IS NULL LIMIT 1;";
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCharacter(reader) : null;
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM `god2_player`.`characters`
                WHERE `name` = @name
                  AND `status` NOT IN ('Deleted','已刪除') AND `enabled` = 1 AND `deleted_at_utc` IS NULL
            );
            """;
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<OperationResult<CharacterSummary>> CreateAsync(
        long accountId,
        CharacterCreateRequest request,
        int mapId,
        int positionX,
        int positionY,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(request.RequestId))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.request_id_invalid",
                "Character lifecycle request id is missing or exceeds the supported length.");
        }

        if (!OfficialCharacterIdentityPolicy.IsProductionName(request.Name))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.name_invalid",
                "Character name cannot be represented by the current production official-client projection.");
        }

        if (!OfficialCharacterIdentityPolicy.HasProductionCharacterListProfile(request.Class))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.class_profile_evidence_blocked",
                "Character class does not have a production-enabled official-client character-list profile.");
        }

        var birthProfile = OfficialCharacterBirthProfiles.ResolveClientClass(request.Class);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var payloadHash = LifecyclePayloadHash(
                "Create",
                accountId,
                request.Name,
                request.Class,
                request.Gender,
                request.LifeSkill,
                request.Appearance,
                mapId,
                positionX,
                positionY,
                maximumCharacters);

            await using (var accountLock = connection.CreateCommand())
            {
                accountLock.Transaction = transaction;
                accountLock.CommandTimeout = CommandTimeoutSeconds;
                accountLock.CommandText = "SELECT `account_id` FROM `god2_player`.`accounts` WHERE `account_id` = @accountId FOR UPDATE;";
                accountLock.Parameters.AddWithValue("@accountId", accountId);
                if (await accountLock.ExecuteScalarAsync(cancellationToken) is null)
                {
                    return OperationResult<CharacterSummary>.Failure(
                        "character.account_not_found",
                        "The character owner account does not exist.");
                }
            }

            var replay = await FindLifecycleReplayAsync(
                connection,
                transaction,
                accountId,
                "Create",
                request.RequestId,
                payloadHash,
                cancellationToken);
            if (replay is not null)
            {
                return replay;
            }

            await using (var count = connection.CreateCommand())
            {
                count.Transaction = transaction;
                count.CommandTimeout = CommandTimeoutSeconds;
                count.CommandText = """
                    SELECT COUNT(*) FROM `god2_player`.`characters`
                    WHERE `account_id` = @accountId
                      AND `status` NOT IN ('Deleted','已刪除') AND `enabled` = 1 AND `deleted_at_utc` IS NULL;
                    """;
                count.Parameters.AddWithValue("@accountId", accountId);
                if (Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken)) >= maximumCharacters)
                {
                    return OperationResult<CharacterSummary>.Failure(
                        "character.limit_reached",
                        "The account has reached the active character limit.");
                }
            }

            long characterId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = """
                INSERT INTO `god2_player`.`characters`
                    (`account_id`, `name`, `class_id`, `class_code`, `class_name_cache`, `gender_code`, `life_skill_code`, `appearance_code`,
                     `map_id`, `position_x`, `position_y`, `level`,
                     `current_hp`, `max_hp`, `current_mp`, `max_mp`,
                     `constitution_base`, `constitution_bonus`, `strength_base`, `strength_bonus`,
                     `intelligence_base`, `intelligence_bonus`, `speed_base`, `speed_bonus`,
                     `metal_base`, `metal_bonus`, `wood_base`, `wood_bonus`, `water_base`, `water_bonus`,
                     `fire_base`, `fire_bonus`, `earth_base`, `earth_bonus`,
                     `physical_attack_base`, `physical_attack_bonus`, `physical_defense_base`, `physical_defense_bonus`,
                     `magic_attack_base`, `magic_attack_bonus`, `magic_defense_base`, `magic_defense_bonus`,
                     `status`, `enabled`,
                     `created_at_utc`, `updated_at_utc`, `concurrency_token`)
                VALUES
                    (@accountId, @name, @classId, @class, @className, @gender, @lifeSkill, @appearance, @mapId, @positionX,
                     @positionY, 1,
                     @maximumHp, @maximumHp, @maximumMp, @maximumMp,
                     @constitution, 0, @strength, 0, @intelligence, 0, @speed, 0,
                     0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                     @physicalAttack, 0, @physicalDefense, 0, @magicAttack, 0, @magicDefense, 0,
                     '啟用', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @token);
                """;
                command.Parameters.AddWithValue("@accountId", accountId);
                command.Parameters.AddWithValue("@name", request.Name);
                command.Parameters.AddWithValue("@classId", (int)birthProfile.Class + 1);
                command.Parameters.AddWithValue("@class", birthProfile.ClientClassCode);
                command.Parameters.AddWithValue("@className", birthProfile.NameZhTw);
                command.Parameters.AddWithValue("@gender", request.Gender);
                command.Parameters.AddWithValue("@lifeSkill", request.LifeSkill);
                command.Parameters.AddWithValue("@appearance", request.Appearance);
                command.Parameters.AddWithValue("@mapId", mapId);
                command.Parameters.AddWithValue("@positionX", positionX);
                command.Parameters.AddWithValue("@positionY", positionY);
                command.Parameters.AddWithValue("@maximumHp", birthProfile.MaximumHp);
                command.Parameters.AddWithValue("@maximumMp", birthProfile.MaximumMp);
                command.Parameters.AddWithValue("@constitution", birthProfile.PrimaryAttributes.Constitution);
                command.Parameters.AddWithValue("@strength", birthProfile.PrimaryAttributes.Strength);
                command.Parameters.AddWithValue("@intelligence", birthProfile.PrimaryAttributes.Intelligence);
                command.Parameters.AddWithValue("@speed", birthProfile.PrimaryAttributes.Speed);
                command.Parameters.AddWithValue("@physicalAttack", birthProfile.PhysicalAttack);
                command.Parameters.AddWithValue("@physicalDefense", birthProfile.PhysicalDefense);
                command.Parameters.AddWithValue("@magicAttack", birthProfile.MagicAttack);
                command.Parameters.AddWithValue("@magicDefense", birthProfile.MagicDefense);
                command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
                await command.ExecuteNonQueryAsync(cancellationToken);
                characterId = command.LastInsertedId;
            }

            if (characterId is <= 0 or > uint.MaxValue)
            {
                return OperationResult<CharacterSummary>.Failure(
                    "character.identity_not_serializable",
                    "Character identifier cannot be represented by the current official-client player projection.");
            }

            await using (var lifeSkills = connection.CreateCommand())
            {
                lifeSkills.Transaction = transaction;
                lifeSkills.CommandTimeout = CommandTimeoutSeconds;
                lifeSkills.CommandText = CreateLifeSkillRowsCommandText;
                lifeSkills.Parameters.AddWithValue("@characterId", characterId);
                var insertedLifeSkills = await lifeSkills.ExecuteNonQueryAsync(cancellationToken);
                if (insertedLifeSkills != 4)
                {
                    throw new InvalidOperationException(
                        $"Character creation requires exactly four canonical life-skill rows; inserted {insertedLifeSkills}.");
                }
            }

            var created = await FindCharacterAsync(connection, transaction, characterId, forUpdate: false, cancellationToken);
            if (created is null)
            {
                return OperationResult<CharacterSummary>.Failure(
                    "character.create_failed",
                    "Character was not found inside the creation transaction.");
            }

            await StoreLifecycleReplayAsync(
                connection,
                transaction,
                accountId,
                "Create",
                request.RequestId,
                payloadHash,
                created,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<CharacterSummary>.Success(created);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return OperationResult<CharacterSummary>.Failure("character.name_duplicate", "Character name already exists.");
        }
    }

    public async Task<OperationResult<CharacterSummary>> DeleteAsync(
        long accountId,
        long characterId,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(requestId))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.request_id_invalid",
                "Character lifecycle request id is missing or exceeds the supported length.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var accountLock = await LockAccountAsync(connection, transaction, accountId, cancellationToken);
        if (!accountLock.Succeeded)
        {
            return OperationResult<CharacterSummary>.Failure(accountLock.Error.Code, accountLock.Error.Message);
        }

        var payloadHash = LifecyclePayloadHash("Delete", accountId, characterId);
        var replay = await FindLifecycleReplayAsync(
            connection,
            transaction,
            accountId,
            "Delete",
            requestId,
            payloadHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var character = await FindCharacterAsync(connection, transaction, characterId, forUpdate: true, cancellationToken);
        var validation = ValidateOwnership(character, accountId);
        if (!validation.Succeeded)
        {
            return OperationResult<CharacterSummary>.Failure(validation.Error.Code, validation.Error.Message);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`characters`
            SET `status` = '已刪除', `enabled` = 0, `deleted_at_utc` = UTC_TIMESTAMP(6),
                `updated_at_utc` = UTC_TIMESTAMP(6), `concurrency_token` = @token
            WHERE `character_id` = @characterId AND `account_id` = @accountId AND `status` NOT IN ('Deleted','已刪除');
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.concurrent_mutation_rejected",
                "Character deletion lost its conditional ownership update.");
        }

        var deleted = character! with { Status = "已刪除" };

        await StoreLifecycleReplayAsync(
            connection,
            transaction,
            accountId,
            "Delete",
            requestId,
            payloadHash,
            deleted,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult<CharacterSummary>.Success(deleted);
    }

    public async Task<OperationResult<CharacterSummary>> RenameAsync(
        long accountId,
        long characterId,
        string newName,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!OfficialCharacterIdentityPolicy.IsLifecycleRequestId(requestId))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.request_id_invalid",
                "Character lifecycle request id is missing or exceeds the supported length.");
        }

        if (!OfficialCharacterIdentityPolicy.IsProductionName(newName))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.name_invalid",
                "Character name cannot be represented by the current production official-client projection.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var accountLock = await LockAccountAsync(connection, transaction, accountId, cancellationToken);
            if (!accountLock.Succeeded)
            {
                return OperationResult<CharacterSummary>.Failure(accountLock.Error.Code, accountLock.Error.Message);
            }

            var payloadHash = LifecyclePayloadHash("Rename", accountId, characterId, newName);
            var replay = await FindLifecycleReplayAsync(
                connection,
                transaction,
                accountId,
                "Rename",
                requestId,
                payloadHash,
                cancellationToken);
            if (replay is not null)
            {
                return replay;
            }

            var character = await FindCharacterAsync(connection, transaction, characterId, forUpdate: true, cancellationToken);
            var validation = ValidateOwnership(character, accountId);
            if (!validation.Succeeded)
            {
                return OperationResult<CharacterSummary>.Failure(validation.Error.Code, validation.Error.Message);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                UPDATE `god2_player`.`characters`
                SET `name` = @name, `updated_at_utc` = UTC_TIMESTAMP(6), `concurrency_token` = @token
                WHERE `character_id` = @characterId AND `account_id` = @accountId AND `status` NOT IN ('Deleted','已刪除');
                """;
            command.Parameters.AddWithValue("@characterId", characterId);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@name", newName);
            command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return OperationResult<CharacterSummary>.Failure(
                    "character.concurrent_mutation_rejected",
                    "Character rename lost its conditional ownership update.");
            }

            var renamed = await FindCharacterAsync(connection, transaction, characterId, forUpdate: false, cancellationToken);
            if (renamed is null)
            {
                return OperationResult<CharacterSummary>.Failure("character.not_found", "Character not found.");
            }

            await StoreLifecycleReplayAsync(
                connection,
                transaction,
                accountId,
                "Rename",
                requestId,
                payloadHash,
                renamed,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<CharacterSummary>.Success(renamed);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return OperationResult<CharacterSummary>.Failure("character.name_duplicate", "Character name already exists.");
        }
    }

    public async Task<OperationResult<CharacterSummary>> TouchPlayedAsync(
        long characterId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var update = await UpdateAsync(
            characterId,
            "`last_played_at_utc` = @now, `updated_at_utc` = @now, `concurrency_token` = @token",
            [("@now", now.UtcDateTime)],
            cancellationToken);
        if (!update.Succeeded)
        {
            return OperationResult<CharacterSummary>.Failure(update.Error.Code, update.Error.Message, update.Error.Source);
        }

        var touched = await FindByIdAsync(characterId, cancellationToken);
        return touched is null
            ? OperationResult<CharacterSummary>.Failure("character.not_found", "Character not found.")
            : OperationResult<CharacterSummary>.Success(touched);
    }

    private async Task<OperationResult> UpdateAsync(
        long characterId,
        string assignments,
        IReadOnlyList<(string Name, object Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"UPDATE `god2_player`.`characters` SET {assignments} WHERE `character_id` = @characterId AND `status` NOT IN ('Deleted','已刪除');";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 0
            ? OperationResult.Failure("character.not_found", "Character not found.")
            : OperationResult.Success;
    }

    private static CharacterSummary ReadCharacter(MySqlDataReader reader) =>
        new(
            reader.GetInt64("Id"),
            reader.GetInt64("AccountId"),
            reader.GetString("Name"),
            reader.GetString("Class"),
            reader.GetString("Gender"),
            reader.GetString("LifeSkill"),
            reader.GetInt32("Level"),
            reader.GetString("Appearance"),
            reader.GetInt32("MapId"),
            reader.GetInt32("PositionX"),
            reader.GetInt32("PositionY"),
            reader.GetString("Status"),
            ReadUtc(reader, "CreatedAtUtc"),
            ReadNullableUtc(reader, "LastPlayedAtUtc"),
            reader.IsDBNull("CurrentHitPoints") ? null : reader.GetInt64("CurrentHitPoints"),
            reader.IsDBNull("CurrentMagicPoints") ? null : reader.GetInt64("CurrentMagicPoints"),
            reader.IsDBNull("MaximumHitPoints") ? null : reader.GetInt64("MaximumHitPoints"),
            reader.IsDBNull("MaximumMagicPoints") ? null : reader.GetInt64("MaximumMagicPoints"),
            reader.IsDBNull("RemainingStatPoints") ? null : reader.GetInt64("RemainingStatPoints"),
            reader.IsDBNull("Constitution") ? null : reader.GetInt64("Constitution"),
            reader.IsDBNull("Strength") ? null : reader.GetInt64("Strength"),
            reader.IsDBNull("Intelligence") ? null : reader.GetInt64("Intelligence"),
            reader.IsDBNull("Speed") ? null : reader.GetInt64("Speed"));

    private static async Task<OperationResult> LockAccountAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `account_id` FROM `god2_player`.`accounts` WHERE `account_id` = @accountId FOR UPDATE;";
        command.Parameters.AddWithValue("@accountId", accountId);
        return await command.ExecuteScalarAsync(cancellationToken) is null
            ? OperationResult.Failure("character.account_not_found", "The character owner account does not exist.")
            : OperationResult.Success;
    }

    private static async Task<OperationResult<CharacterSummary>?> FindLifecycleReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        string operation,
        string requestId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `operation` AS `Operation`, `lifecycle_fingerprint_sha256` AS `LifecycleFingerprintSha256`,
                   `result_code` AS `ResultCode`, `result_json` AS `ResultJson`
            FROM `god2_player`.`character_lifecycle_idempotency`
            WHERE `account_id` = @accountId AND `idempotency_key_hash` = @idempotencyKeyHash
            LIMIT 1 FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@idempotencyKeyHash", LifecycleHash(requestId));

        string? storedOperation = null;
        string? storedPayloadHash = null;
        string? resultCode = null;
        string? resultJson = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            storedOperation = reader.GetString("Operation");
            storedPayloadHash = reader.GetString("LifecycleFingerprintSha256");
            resultCode = reader.GetString("ResultCode");
            resultJson = reader.GetString("ResultJson");
        }

        if (!string.Equals(NormalizeLifecycleOperation(storedOperation), operation, StringComparison.Ordinal) ||
            !string.Equals(storedPayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.replay_conflict",
                "The character lifecycle idempotency key was reused with a different operation or payload.");
        }

        if (!string.Equals(NormalizeLifecycleResultCode(resultCode), "Success", StringComparison.Ordinal))
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.replay_record_invalid",
                "The persisted character lifecycle replay result is not supported.");
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<CharacterSummary>(resultJson!);
            return snapshot is not null && snapshot.AccountId == accountId
                ? OperationResult<CharacterSummary>.Success(snapshot)
                : OperationResult<CharacterSummary>.Failure(
                    "character.replay_record_invalid",
                    "The persisted character lifecycle replay snapshot is invalid.");
        }
        catch (JsonException)
        {
            return OperationResult<CharacterSummary>.Failure(
                "character.replay_record_invalid",
                "The persisted character lifecycle replay snapshot could not be decoded.");
        }
    }

    private static async Task StoreLifecycleReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long accountId,
        string operation,
        string requestId,
        string payloadHash,
        CharacterSummary result,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`character_lifecycle_idempotency`
                (`account_id`, `idempotency_key_hash`, `operation`, `lifecycle_fingerprint_sha256`, `result_code`, `result_json`, `created_at_utc`)
            VALUES
                (@accountId, @idempotencyKeyHash, @operation, @payloadHash, '成功', @resultJson, UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@idempotencyKeyHash", LifecycleHash(requestId));
        command.Parameters.AddWithValue("@operation", FormatLifecycleOperation(operation));
        command.Parameters.AddWithValue("@payloadHash", payloadHash);
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string LifecyclePayloadHash(string operation, params object?[] values) =>
        LifecycleHash(CanonicalLifecycleValue(operation, values));

    private static string NormalizeLifecycleOperation(string? value) => value switch
    {
        "建立" => "Create",
        "改名" => "Rename",
        "刪除" => "Delete",
        _ => value ?? string.Empty
    };

    private static string FormatLifecycleOperation(string value) => value switch
    {
        "Create" => "建立",
        "Rename" => "改名",
        "Delete" => "刪除",
        _ => value
    };

    private static string NormalizeLifecycleResultCode(string? value) => value switch
    {
        "成功" => "Success",
        _ => value ?? string.Empty
    };

    private static string CanonicalLifecycleValue(string operation, IReadOnlyList<object?> values)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, operation);
        foreach (var value in values)
        {
            AppendCanonical(builder, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return builder.ToString();
    }

    private static void AppendCanonical(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append(';');

    private static string LifecycleHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<CharacterSummary?> FindCharacterAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT {CharacterColumns}
            FROM `god2_player`.`characters`
            WHERE `character_id` = @characterId AND `status` NOT IN ('Deleted','已刪除')
            LIMIT 1{(forUpdate ? " FOR UPDATE" : string.Empty)};
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCharacter(reader) : null;
    }

    private static OperationResult ValidateOwnership(CharacterSummary? character, long accountId)
    {
        if (character is null)
        {
            return OperationResult.Failure("character.not_found", "Character not found.");
        }

        return character.AccountId == accountId
            ? OperationResult.Success
            : OperationResult.Failure(
                "character.ownership_rejected",
                "Character ownership does not match the authenticated account.");
    }
}
