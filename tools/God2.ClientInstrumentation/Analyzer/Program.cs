using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using MySqlConnector;

namespace God2.ClientInstrumentation.Analyzer;

internal static class Program
{
    private const int OfficialWireStartMapId = 19;
    private const int OfficialWireStartPositionX = 28;
    private const int OfficialWireStartPositionY = 34;

    private const uint RecordMagic = 0x31523247; // G2R1
    private const int RecordHeaderSize = 188;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Usage();
                return 2;
            }

            return args[0].ToLowerInvariant() switch
            {
                "ensure-accounts" => await EnsureAccountsAsync(args.Skip(1).ToArray()),
                "prepare-four-classes" => await PrepareFourClassesAsync(args.Skip(1).ToArray()),
                "ensure-local-account" => await EnsureLocalAccountAsync(args.Skip(1).ToArray()),
                "ensure-local-character" => await EnsureLocalCharacterAsync(args.Skip(1).ToArray()),
                "analyze" => await AnalyzeAsync(args.Skip(1).ToArray()),
                "world-matrix" => await WorldMatrixAsync(args.Skip(1).ToArray()),
                "chinese-labeled-import" => await ChineseLabeledCaptureImporter.RunAsync(args.Skip(1).ToArray()),
                "live-classification-selftest" => LiveClassificationSelfTest(),
                "selftest" => await SelfTestAsync(args.Skip(1).ToArray()),
                "endpoint" => await EndpointAsync(args.Skip(1).ToArray()),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Usage();
        return 2;
    }

    private static void Usage()
    {
        Console.Error.WriteLine("commands: ensure-accounts --repo-root <path> --run-dir <path> | prepare-four-classes --repo-root <path> --run-dir <path> | ensure-local-account --repo-root <path> --run-dir <path> --account <name> | ensure-local-character --repo-root <path> --run-dir <path> --account <name> --character-name <name> | analyze --run-dir <path> --report <path> | world-matrix --run-dir <path> --markers <path> --out <path> | chinese-labeled-import --repo-root <path> --zip <path> | live-classification-selftest | selftest --run-dir <path> --case send|WSASend|recv|WSARecv | endpoint --repo-root <path> --run-dir <path> [--port 2592]");
    }

    private static async Task<int> EnsureLocalCharacterAsync(string[] args)
    {
        var repoRoot = Required(args, "--repo-root");
        var runDir = Required(args, "--run-dir");
        var accountName = Required(args, "--account");
        var characterName = Required(args, "--character-name");
        var db = DatabaseOptions.Load(Path.Combine(repoRoot, "config", "database.json"));
        var password = db.ResolvePassword();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"MariaDB password is not configured from {db.PasswordSource}.");
        }

        await using var connection = new MySqlConnection(db.BuildConnectionString(password, includeDatabase: true));
        await connection.OpenAsync();
        await using var accountCommand = connection.CreateCommand();
        accountCommand.CommandText = "SELECT `account_id` FROM `god2_player`.`accounts` WHERE `username`=@username AND `status`='Active' LIMIT 1;";
        accountCommand.Parameters.AddWithValue("@username", accountName);
        var accountValue = await accountCommand.ExecuteScalarAsync();
        if (accountValue is null)
        {
            throw new InvalidOperationException("The canonical local test account does not exist or is inactive.");
        }

        var repositoryOptions = new God2.ClassicServer.Application.Configuration.DatabaseOptions(
            db.Host,
            db.Port,
            db.DatabaseName,
            db.Username,
            password,
            db.ConnectionTimeoutSeconds,
            "ConfigValue");
        var repository = new God2.ClassicServer.Persistence.MariaDbCharacterRepository(repositoryOptions);
        var accountId = Convert.ToInt64(accountValue, CultureInfo.InvariantCulture);
        var existing = await repository.ListByAccountAsync(accountId, CancellationToken.None);
        God2.ClassicServer.Runtime.CharacterSummary character;
        var action = "Reused";
        if (existing.Count == 0)
        {
            var request = new God2.ClassicServer.Runtime.CharacterCreateRequest(
                characterName,
                "Swordsman",
                "Female",
                "LifeSkill1",
                "Appearance1",
                $"automation-create:{accountId}:{characterName}");
            var creationAuthority = new God2.ClassicServer.Persistence.MariaDbCharacterCreationAuthority(
                repositoryOptions,
                God2.ClassicServer.Runtime.OfficialNpcReplicationWireCodec.ClientBuildId);
            var spawn = await creationAuthority.ResolveAsync(request, CancellationToken.None);
            if (!spawn.Succeeded || spawn.Value is null)
            {
                throw new InvalidOperationException($"Canonical character spawn resolution failed: {spawn.Error.Code} {spawn.Error.Message}");
            }

            var create = await repository.CreateAsync(
                accountId,
                request,
                spawn.Value.MapId,
                spawn.Value.PositionX,
                spawn.Value.PositionY,
                1,
                CancellationToken.None);
            if (!create.Succeeded || create.Value is null)
            {
                throw new InvalidOperationException($"Canonical character creation failed: {create.Error.Code} {create.Error.Message}");
            }

            character = create.Value;
            action = "Created";
        }
        else if (existing.Count == 1)
        {
            character = existing[0];
        }
        else
        {
            throw new InvalidOperationException("The local test account has more than one active character.");
        }

        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "analysis", "local-test-character.json"),
            JsonSerializer.Serialize(new
            {
                preparedAtUtc = DateTimeOffset.UtcNow,
                database = "god2_player",
                account = "MASKED",
                action,
                character = new
                {
                    character.CharacterId,
                    character.Name,
                    character.Class,
                    character.Gender,
                    character.LifeSkill,
                    character.Level,
                    character.MapId,
                    character.PositionX,
                    character.PositionY,
                    character.Status
                }
            }, JsonOptions.Indented));
        Console.WriteLine("Local test character ready");
        return 0;
    }

    private static async Task<int> EnsureLocalAccountAsync(string[] args)
    {
        var repoRoot = Required(args, "--repo-root");
        var runDir = Required(args, "--run-dir");
        var accountName = Required(args, "--account");
        var localPassword = Environment.GetEnvironmentVariable("GOD2_LOCAL_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(localPassword))
        {
            throw new InvalidOperationException("Local test password is not configured from GOD2_LOCAL_TEST_PASSWORD.");
        }

        var databasePath = Path.Combine(repoRoot, "config", "database.json");
        var schemaDir = Path.Combine(repoRoot, "database", "schema");
        var db = DatabaseOptions.Load(databasePath);
        var password = db.ResolvePassword();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"MariaDB password is not configured from {db.PasswordSource}.");
        }

        await EnsureDatabaseAndSchemaAsync(db, password, schemaDir);
        await using var connection = new MySqlConnection(db.BuildConnectionString(password, includeDatabase: true));
        await connection.OpenAsync();

        var schema = await ReadCanonicalAccountsSchemaAsync(connection);
        var now = DateTime.UtcNow;
        await UpsertCanonicalAccountAsync(connection, accountName, PasswordHash.Hash(localPassword), now);
        var verification = await ReadCanonicalAccountVerificationAsync(connection, accountName);

        var output = new
        {
            preparedAtUtc = DateTimeOffset.UtcNow,
            database = "god2_player",
            accountPersisted = true,
            passwordPolicy = "masked; supplied only through GOD2_LOCAL_TEST_PASSWORD process environment",
            passwordFormat = "pbkdf2-sha256$100000$base64salt$base64hash",
            credentialPolicy = "account, password, and session values are not written to logs, reports, or artifacts",
            schema,
            verification
        };
        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "analysis", "local-test-account.json"),
            JsonSerializer.Serialize(output, JsonOptions.Indented));
        Console.WriteLine("Local test account ready");
        return 0;
    }

    private static async Task<int> EnsureAccountsAsync(string[] args)
    {
        var repoRoot = Required(args, "--repo-root");
        var runDir = Required(args, "--run-dir");
        var databasePath = Path.Combine(repoRoot, "config", "database.json");
        var schemaDir = Path.Combine(repoRoot, "database", "schema");
        var db = DatabaseOptions.Load(databasePath);
        var password = db.ResolvePassword();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"MariaDB password is not configured from {db.PasswordSource}.");
        }

        await EnsureDatabaseAndSchemaAsync(db, password, schemaDir);
        await using var connection = new MySqlConnection(db.BuildConnectionString(password, includeDatabase: true));
        await connection.OpenAsync();

        var accounts = TestAccountProvider.Load(repoRoot);
        var now = DateTime.UtcNow;
        foreach (var account in accounts)
        {
            await UpsertAccountAsync(connection, account.Account, PasswordHash.Hash(account.Password), now);
        }

        var output = new
        {
            preparedAtUtc = DateTimeOffset.UtcNow,
            database = db.DatabaseName,
            accounts = accounts.Select(static account => new
            {
                key = account.Key,
                @class = account.Class,
                ready = true
            }).ToArray(),
            credentialPolicy = "account and password values are read only from config/test-accounts.local.json and are not written to logs, reports, or artifacts"
        };
        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "analysis", "test-accounts.json"),
            JsonSerializer.Serialize(output, JsonOptions.Indented));
        Console.WriteLine("Test accounts ready");
        return 0;
    }

    private static async Task<int> PrepareFourClassesAsync(string[] args)
    {
        var repoRoot = Path.GetFullPath(Required(args, "--repo-root"));
        var runDir = Path.GetFullPath(Required(args, "--run-dir"));
        var databasePath = Path.Combine(repoRoot, "config", "database.json");
        var schemaDir = Path.Combine(repoRoot, "database", "schema");
        var db = DatabaseOptions.Load(databasePath);
        var password = db.ResolvePassword();
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"MariaDB password is not configured from {db.PasswordSource}.");
        }

        var accounts = TestAccountProvider.Load(repoRoot);
        await EnsureDatabaseAndSchemaAsync(db, password, schemaDir);
        await using var connection = new MySqlConnection(db.BuildConnectionString(password, includeDatabase: true));
        await connection.OpenAsync();

        var prepared = new List<object>(accounts.Count);
        foreach (var account in accounts)
        {
            var now = DateTime.UtcNow;
            PreparedCharacter? character = null;
            var action = string.Empty;
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                try
                {
                    await UpsertAccountAsync(
                        connection,
                        account.Account,
                        PasswordHash.Hash(account.Password),
                        now,
                        transaction);
                    var accountId = await ReadAccountIdAsync(connection, account.Account, transaction);
                    character = await ReadSingleRuntimeVisibleCharacterAsync(connection, accountId, transaction);
                    action = "Reused";
                    if (character is null)
                    {
                        var characterName = await SelectAvailableCharacterNameAsync(connection, account.Key, accountId, transaction);
                        var characterId = await InsertCharacterAsync(
                            connection,
                            accountId,
                            characterName,
                            account.Class,
                            now,
                            transaction);
                        character = await ReadCharacterAsync(connection, characterId, transaction)
                            ?? throw new InvalidOperationException($"Prepared character could not be reloaded for {account.Key}.");
                        action = "Created";
                    }
                    else if (!IsPreparedOfficialWireCharacter(character, account.Class))
                    {
                        var characterName = IsOfficialWireCharacterName(character.Name)
                            ? character.Name
                            : await SelectAvailableCharacterNameAsync(connection, account.Key, accountId, transaction);
                        await PrepareOfficialWireCharacterAsync(
                            connection,
                            character.Id,
                            characterName,
                            account.Class,
                            now,
                            transaction);
                        character = await ReadCharacterAsync(connection, character.Id, transaction)
                            ?? throw new InvalidOperationException($"Remediated character could not be reloaded for {account.Key}.");
                        action = "RemediatedForOfficialWire";
                    }

                    character = await ReadSingleRuntimeVisibleCharacterAsync(connection, accountId, transaction)
                        ?? throw new InvalidOperationException($"Character preparation produced no character for {account.Key}.");
                    if (!IsPreparedOfficialWireCharacter(character, account.Class))
                    {
                        throw new InvalidOperationException($"Character preparation invariants were not satisfied for {account.Key}.");
                    }

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            if (character is null)
            {
                throw new InvalidOperationException($"Character preparation produced no character for {account.Key}.");
            }

            var inventoryCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `god2_player`.`character_inventory` WHERE `character_id` = @characterId AND `enabled` = 1 AND `deleted_at_utc` IS NULL;",
                character.Id);
            var equipmentCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `god2_player`.`character_equipment` WHERE `character_id` = @characterId AND `enabled` = 1;",
                character.Id);
            var completedQuestCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `quest_instances` WHERE `CharacterId` = @characterId AND `State` = 'Completed';",
                character.Id);
            var npcInteractionCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `god2_player`.`world_interaction_audit` WHERE `CharacterId` = @characterId AND `InteractionType` = 'Npc';",
                character.Id);
            var portalUsageCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM `god2_player`.`world_interaction_audit` WHERE `CharacterId` = @characterId AND `InteractionType` = 'Portal';",
                character.Id);
            var battleCount = await CountAsync(
                connection,
                "SELECT COUNT(DISTINCT `BattleInstanceId`) FROM `battle_participants` WHERE `CharacterId` = @characterId;",
                character.Id);

            prepared.Add(new
            {
                key = account.Key,
                assignedClass = account.Class,
                characterAction = action,
                characterId = character.Id,
                actualClass = character.Class,
                level = character.Level,
                mapId = character.MapId,
                position = new { x = character.PositionX, y = character.PositionY },
                initialEquipment = new { slotCount = equipmentCount, evidence = "database/god2_player.character_equipment" },
                initialInventory = new { slotCount = inventoryCount, evidence = "database/god2_player.character_inventory" },
                initialSkills = new
                {
                    status = "EvidenceBlocked",
                    reason = "No production player-skill ownership table or recovered ownership records exist."
                },
                beginnerQuest = new
                {
                    completedCount = completedQuestCount,
                    status = completedQuestCount > 0 ? "ObservedCompleted" : "NotObserved"
                },
                npcInteractions = new { count = npcInteractionCount },
                portalUsage = new { count = portalUsageCount },
                battles = new { count = battleCount }
            });
        }

        var output = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            configuration = new
            {
                source = "config/test-accounts.local.json",
                entryCount = accounts.Count,
                accountsUnique = true,
                credentialsPersistedToArtifact = false
            },
            characters = prepared.ToArray(),
            credentialPolicy = "Account and password values are never written to output, reports, logs, or artifacts."
        };
        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "analysis", "four-class-preparation.json"),
            JsonSerializer.Serialize(output, JsonOptions.Indented));
        Console.WriteLine("Four-class preparation ready");
        return 0;
    }

    private static async Task<long> ReadAccountIdAsync(
        MySqlConnection connection,
        string loginName,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `Id` FROM `accounts` WHERE `LoginName` = @loginName LIMIT 1;";
        command.Parameters.AddWithValue("@loginName", loginName);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull
            ? throw new InvalidOperationException("Prepared test account row was not found.")
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<PreparedCharacter?> ReadSingleRuntimeVisibleCharacterAsync(
        MySqlConnection connection,
        long accountId,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT `Id`, `Name`, `Class`, `Level`, `MapId`, `PositionX`, `PositionY`, `Status`, `DeletedAtUtc`
            FROM `characters`
            WHERE `AccountId` = @accountId AND `Status` <> 'Deleted'
            ORDER BY `Id`
            LIMIT 2
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var character = ReadPreparedCharacter(reader);
        if (await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "Test-account preparation requires exactly one runtime-visible character; multiple non-deleted characters were found.");
        }

        return character;
    }

    private static async Task<PreparedCharacter?> ReadCharacterAsync(
        MySqlConnection connection,
        long characterId,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT `Id`, `Name`, `Class`, `Level`, `MapId`, `PositionX`, `PositionY`, `Status`, `DeletedAtUtc`
            FROM `characters`
            WHERE `Id` = @characterId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPreparedCharacter(reader) : null;
    }

    private static PreparedCharacter ReadPreparedCharacter(MySqlDataReader reader) => new(
        reader.GetInt64("Id"),
        reader.GetString("Name"),
        reader.GetString("Class"),
        reader.GetInt32("Level"),
        reader.GetInt32("MapId"),
        reader.GetInt32("PositionX"),
        reader.GetInt32("PositionY"),
        reader.GetString("Status"),
        reader.IsDBNull(reader.GetOrdinal("DeletedAtUtc")) ? null : reader.GetDateTime("DeletedAtUtc"));

    private static async Task<string> SelectAvailableCharacterNameAsync(
        MySqlConnection connection,
        string key,
        long accountId,
        MySqlTransaction? transaction = null)
    {
        var suffix = key.Length == 0 ? 'X' : char.ToUpperInvariant(key[^1]);
        var stem = $"G2{suffix}";
        var accountSuffix = unchecked((uint)accountId).ToString("X8", CultureInfo.InvariantCulture);
        foreach (var candidate in new[] { stem, $"{stem}{accountSuffix}" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM `characters` WHERE `Name` = @name;";
            command.Parameters.AddWithValue("@name", candidate);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No deterministic character name is available for {key}.");
    }

    private static bool IsOfficialWireCharacterName(string name) =>
        name.Length is >= 1 and <= 11 && name.All(character => character is >= '!' and <= '~');

    private static bool IsVerifiedOfficialWireLocation(PreparedCharacter character) =>
        (character.MapId == 19 && character.PositionX == 28 && character.PositionY == 34) ||
        (character.MapId == 3 && character.PositionX == 196 && character.PositionY == 139);

    private static bool IsPreparedOfficialWireCharacter(PreparedCharacter character, string assignedClass) =>
        string.Equals(character.Class, assignedClass, StringComparison.Ordinal) &&
        string.Equals(character.Status, "Active", StringComparison.Ordinal) &&
        character.DeletedAtUtc is null &&
        IsOfficialWireCharacterName(character.Name) &&
        IsVerifiedOfficialWireLocation(character);

    private static async Task PrepareOfficialWireCharacterAsync(
        MySqlConnection connection,
        long characterId,
        string characterName,
        string assignedClass,
        DateTime now,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `characters`
            SET `Name` = @name,
                `Class` = @class,
                `Status` = 'Active',
                `DeletedAtUtc` = NULL,
                `MapId` = @mapId,
                `PositionX` = @positionX,
                `PositionY` = @positionY,
                `CurrentDirection` = 'Unknown',
                `UpdatedAtUtc` = @now,
                `ConcurrencyToken` = @token
            WHERE `Id` = @characterId;
            """;
        command.Parameters.AddWithValue("@name", characterName);
        command.Parameters.AddWithValue("@class", assignedClass);
        command.Parameters.AddWithValue("@mapId", OfficialWireStartMapId);
        command.Parameters.AddWithValue("@positionX", OfficialWireStartPositionX);
        command.Parameters.AddWithValue("@positionY", OfficialWireStartPositionY);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@characterId", characterId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertCharacterAsync(
        MySqlConnection connection,
        long accountId,
        string name,
        string assignedClass,
        DateTime now,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `characters`
                (`AccountId`, `Name`, `Class`, `Gender`, `LifeSkill`, `Appearance`, `MapId`, `PositionX`,
                 `PositionY`, `CurrentDirection`, `RuntimeVersion`, `Status`, `Level`, `CreatedAtUtc`,
                 `UpdatedAtUtc`, `LastPlayedAtUtc`, `DeletedAtUtc`, `ConcurrencyToken`)
            VALUES
                (@accountId, @name, @class, 'Unknown', 'LifeSkill1', 'Unknown', @mapId, @positionX,
                 @positionY, 'Unknown', 0, 'Active', 1, @now, @now, NULL, NULL, @token);
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@class", assignedClass);
        command.Parameters.AddWithValue("@mapId", OfficialWireStartMapId);
        command.Parameters.AddWithValue("@positionX", OfficialWireStartPositionX);
        command.Parameters.AddWithValue("@positionY", OfficialWireStartPositionY);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync();
        return command.LastInsertedId;
    }

    private static async Task<long> CountAsync(MySqlConnection connection, string sql, long characterId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@characterId", characterId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task EnsureDatabaseAndSchemaAsync(DatabaseOptions db, string password, string schemaDir)
    {
        await using (var serverConnection = new MySqlConnection(db.BuildConnectionString(password, includeDatabase: false)))
        {
            await serverConnection.OpenAsync();
            await using var exists = serverConnection.CreateCommand();
            exists.CommandText = """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @databaseName AND TABLE_NAME = 'accounts';
                """;
            exists.Parameters.AddWithValue("@databaseName", db.DatabaseName);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1)
            {
                return;
            }

            await using var create = serverConnection.CreateCommand();
            create.CommandText = $"CREATE DATABASE IF NOT EXISTS `{EscapeIdentifier(db.DatabaseName)}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await create.ExecuteNonQueryAsync();
        }

        await using var connection = new MySqlConnection(db.BuildConnectionString(password, includeDatabase: true));
        await connection.OpenAsync();
        foreach (var file in Directory.EnumerateFiles(schemaDir, "*.sql").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var sql = await File.ReadAllTextAsync(file);
            foreach (var statement in SplitSqlStatements(sql))
            {
                if (string.IsNullOrWhiteSpace(statement))
                {
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.CommandTimeout = 30;
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static IEnumerable<string> SplitSqlStatements(string sql)
    {
        var builder = new StringBuilder();
        foreach (var line in sql.Replace("\r\n", "\n").Split('\n'))
        {
            builder.AppendLine(line);
            if (line.TrimEnd().EndsWith(';'))
            {
                var statement = builder.ToString().Trim();
                builder.Clear();
                if (statement.EndsWith(';'))
                {
                    statement = statement[..^1];
                }

                yield return statement;
            }
        }

        var tail = builder.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    private static async Task UpsertAccountAsync(
        MySqlConnection connection,
        string loginName,
        string hash,
        DateTime now,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 30;
        command.CommandText = """
            INSERT INTO `accounts`
                (`LoginName`, `PasswordHash`, `Status`, `CreatedAtUtc`, `UpdatedAtUtc`, `LastLoginAtUtc`,
                 `FailedLoginCount`, `LockedUntilUtc`, `CurrentSessionId`, `ConcurrencyToken`)
            VALUES
                (@loginName, @passwordHash, 'Active', @now, @now, NULL, 0, NULL, NULL, @token)
            ON DUPLICATE KEY UPDATE
                `PasswordHash` = VALUES(`PasswordHash`),
                `Status` = 'Active',
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`),
                `FailedLoginCount` = 0,
                `LockedUntilUtc` = NULL,
                `ConcurrencyToken` = VALUES(`ConcurrencyToken`);
            """;
        command.Parameters.AddWithValue("@loginName", loginName);
        command.Parameters.AddWithValue("@passwordHash", hash);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertCanonicalAccountAsync(
        MySqlConnection connection,
        string loginName,
        string hash,
        DateTime now)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            INSERT INTO `god2_player`.`accounts`
                (`username`, `password_hash`, `status`, `failed_login_count`, `locked_until_utc`,
                 `last_login_at_utc`, `created_at_utc`, `updated_at_utc`, `current_session_id`, `concurrency_token`)
            VALUES
                (@loginName, @passwordHash, 'Active', 0, NULL, NULL, @now, @now, NULL, @token)
            ON DUPLICATE KEY UPDATE
                `password_hash` = VALUES(`password_hash`),
                `status` = 'Active',
                `failed_login_count` = 0,
                `locked_until_utc` = NULL,
                `current_session_id` = NULL,
                `updated_at_utc` = VALUES(`updated_at_utc`),
                `concurrency_token` = VALUES(`concurrency_token`);
            """;
        command.Parameters.AddWithValue("@loginName", loginName);
        command.Parameters.AddWithValue("@passwordHash", hash);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<object>> ReadCanonicalAccountsSchemaAsync(MySqlConnection connection)
    {
        var rows = new List<object>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, COLUMN_KEY, EXTRA
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'god2_player' AND TABLE_NAME = 'accounts'
            ORDER BY ORDINAL_POSITION;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                column = reader.GetString("COLUMN_NAME"),
                type = reader.GetString("COLUMN_TYPE"),
                nullable = reader.GetString("IS_NULLABLE"),
                defaultValue = reader.IsDBNull(reader.GetOrdinal("COLUMN_DEFAULT")) ? null : reader.GetString("COLUMN_DEFAULT"),
                key = reader.GetString("COLUMN_KEY"),
                extra = reader.GetString("EXTRA")
            });
        }

        return rows;
    }

    private static async Task<object> ReadCanonicalAccountVerificationAsync(
        MySqlConnection connection,
        string loginName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `account_id`, `username`, `status`, `failed_login_count`, `locked_until_utc`, `current_session_id`
            FROM `god2_player`.`accounts`
            WHERE `username` = @loginName;
            """;
        command.Parameters.AddWithValue("@loginName", loginName);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Canonical local test account verification row was not found.");
        }

        return new
        {
            accountIdPresent = reader.GetInt64("account_id") > 0,
            loginNameMatchesRequested = string.Equals(reader.GetString("username"), loginName, StringComparison.Ordinal),
            status = reader.GetString("status"),
            failedLoginCount = reader.GetInt32("failed_login_count"),
            locked = !reader.IsDBNull(reader.GetOrdinal("locked_until_utc")),
            hasCurrentSession = !reader.IsDBNull(reader.GetOrdinal("current_session_id"))
        };
    }

    private static async Task<IReadOnlyList<object>> ReadAccountsSchemaAsync(MySqlConnection connection)
    {
        var rows = new List<object>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, COLUMN_KEY, EXTRA
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'accounts'
            ORDER BY ORDINAL_POSITION;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                column = reader.GetString("COLUMN_NAME"),
                type = reader.GetString("COLUMN_TYPE"),
                nullable = reader.GetString("IS_NULLABLE"),
                defaultValue = reader.IsDBNull(reader.GetOrdinal("COLUMN_DEFAULT")) ? null : reader.GetString("COLUMN_DEFAULT"),
                key = reader.GetString("COLUMN_KEY"),
                extra = reader.GetString("EXTRA")
            });
        }

        return rows;
    }

    private static async Task<object> ReadAccountVerificationAsync(MySqlConnection connection, string loginName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `Id`, `LoginName`, `Status`, `FailedLoginCount`, `LockedUntilUtc`, `CurrentSessionId`
            FROM `accounts`
            WHERE `LoginName` = @loginName;
            """;
        command.Parameters.AddWithValue("@loginName", loginName);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Local test account verification row was not found.");
        }

        return new
        {
            accountIdPresent = reader.GetInt64("Id") > 0,
            loginNameMatchesRequested = string.Equals(reader.GetString("LoginName"), loginName, StringComparison.Ordinal),
            status = reader.GetString("Status"),
            failedLoginCount = reader.GetInt32("FailedLoginCount"),
            locked = !reader.IsDBNull(reader.GetOrdinal("LockedUntilUtc")),
            hasCurrentSession = !reader.IsDBNull(reader.GetOrdinal("CurrentSessionId"))
        };
    }

    private static async Task<int> AnalyzeAsync(string[] args)
    {
        var runDir = Path.GetFullPath(Required(args, "--run-dir"));
        var reportPath = Path.GetFullPath(Required(args, "--report"));
        var manifest = await LoadManifestAsync(Path.Combine(runDir, "source-manifest.json"));
        var stopSummary = await LoadStopSummaryAsync(Path.Combine(runDir, "stop-summary.json"));
        var tracePath = Path.Combine(runDir, "sensitive", "trace.bin");
        var markersPath = Path.Combine(runDir, "markers.jsonl");
        var records = TraceReader.Read(tracePath).ToArray();
        var markers = await LoadMarkersAsync(markersPath);

        var c2s208 = records
            .Where(r => r.Direction == Direction.ClientToServer && (r.TransferredLength == 208 || r.RequestedLength == 208 || r.Frame208))
            .OrderBy(r => r.WallUnixMs)
            .ToArray();

        var samples = AssignSamples(markers, c2s208);
        var frames = samples
            .Where(item => item.Value is not null)
            .ToDictionary(item => item.Key, item => item.Value!, StringComparer.Ordinal);

        var diff = DifferentialResult.From(frames);
        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "analysis", "differential-report.json"),
            JsonSerializer.Serialize(diff, JsonOptions.Indented));
        var repoRoot = FindRepoRoot(runDir);
        var liveClassification = LivePacketClassifier.Classify(records, manifest.RunId, manifest.Sha256, "analyze");
        await LivePacketArtifactWriter.WriteAsync(liveClassification, repoRoot, Path.Combine(runDir, "analysis", "live-classification"));

        var report = ReportWriter.Write(manifest, stopSummary, records, samples, diff);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, report, Encoding.UTF8);
        Console.WriteLine(reportPath);
        return samples.Values.Count(v => v is not null) == 4 ? 0 : 3;
    }

    private static async Task<int> SelfTestAsync(string[] args)
    {
        var runDir = Path.GetFullPath(Required(args, "--run-dir"));
        var caseName = NormalizeSelfTestCase(Required(args, "--case"));
        var tracePath = Path.Combine(runDir, "sensitive", "trace.bin");
        TraceRecord[] records;
        var analyzerParseCompleted = false;
        string? analyzerError = null;
        try
        {
            records = TraceReader.Read(tracePath).ToArray();
            analyzerParseCompleted = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            records = Array.Empty<TraceRecord>();
            analyzerError = ex.Message;
        }

        var expectedApi = ExpectedSelfTestApi(caseName);
        var expectedDirection = ExpectedSelfTestDirection(caseName);
        var expectedPayload = SelfTestPayload(caseName);
        var expectedApiResult = caseName is "WSASend" or "WSARecv" or "WSARecvOverlapped" ? 0 : expectedPayload.Length;
        var matchingRecords = records
            .Where(r => r.Api == expectedApi && r.Direction == expectedDirection)
            .ToArray();
        var payloadRecord = matchingRecords.FirstOrDefault(r =>
            r.TransferredLength == expectedPayload.Length &&
            r.Payload.Length == expectedPayload.Length &&
            r.Payload.SequenceEqual(expectedPayload));

        var hookInstalled = matchingRecords.Length > 0;
        var apiCallResult = payloadRecord?.CallResult;
        var bytesTransferred = payloadRecord?.TransferredLength ?? 0;
        var payloadMatch = payloadRecord is not null;
        var apiCallResultOk = apiCallResult == expectedApiResult;
        var traceIntegrity = analyzerParseCompleted && analyzerError is null;
        var pass = hookInstalled && payloadMatch && apiCallResultOk && traceIntegrity;

        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        var result = new
        {
            Case = caseName,
            HookInstalled = hookInstalled,
            ApiCallResult = apiCallResult,
            ApiCallResultExpected = expectedApiResult,
            BytesTransferred = bytesTransferred,
            TraceRecordCount = records.Length,
            MatchingRecordCount = matchingRecords.Length,
            PayloadMatch = payloadMatch,
            AnalyzerParseCompleted = analyzerParseCompleted,
            TraceIntegrity = traceIntegrity,
            AnalyzerError = analyzerError,
            TraceFileSize = File.Exists(tracePath) ? new FileInfo(tracePath).Length : 0
        };
        await File.WriteAllTextAsync(
            Path.Combine(runDir, "analysis", $"instrumentation-selftest-{caseName}.json"),
            JsonSerializer.Serialize(result, JsonOptions.Indented));
        if (!pass)
        {
            throw new InvalidOperationException($"Trace selftest failed: case={caseName}, HookInstalled={hookInstalled}, ApiCallResult={apiCallResult}, BytesTransferred={bytesTransferred}, TraceRecordCount={records.Length}, PayloadMatch={payloadMatch}, AnalyzerParseCompleted={analyzerParseCompleted}. {analyzerError}");
        }

        Console.WriteLine($"Instrumentation trace selftest PASS case={caseName} records={records.Length} bytes={result.TraceFileSize}");
        return 0;
    }

    private static int LiveClassificationSelfTest()
    {
        var frames = new[]
        {
            SyntheticTrace(1, Direction.ClientToServer, Convert.FromHexString("05007ACCEC")),
            SyntheticTrace(2, Direction.ClientToServer, Convert.FromHexString("0A0080BAD7C34DA69488")),
            SyntheticTrace(3, Direction.ClientToServer, FrameWithLength(20, 0x41)),
            SyntheticTrace(4, Direction.ClientToServer, FrameWithLength(12, 0x42)),
            SyntheticTrace(5, Direction.ClientToServer, Convert.FromHexString("0800112233445566")),
            SyntheticTrace(6, Direction.ClientToServer, Convert.FromHexString("0800112233445566")),
            SyntheticTrace(7, Direction.ClientToServer, Convert.FromHexString("0800112233445566")),
            SyntheticTrace(8, Direction.ServerToClient, Combine(FrameWithLength(8, 0x51), FrameWithLength(9, 0x52)))
        };
        var bundle = LivePacketClassifier.Classify(frames, "selftest", "synthetic", "selftest");
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Movement frame classified KnownInfrastructureTraffic"] = bundle.Frames.Any(f => f.PacketFamily == "Movement" && f.Classification == "KnownInfrastructureTraffic"),
            ["Heartbeat frame classified KnownInfrastructureTraffic"] = bundle.Frames.Any(f => f.PacketFamily == "Heartbeat" && f.Classification == "KnownInfrastructureTraffic"),
            ["Movement excluded from General Unknown"] = bundle.Counters.MovementGeneralUnknownCount == 0,
            ["Heartbeat excluded from General Unknown"] = bundle.Counters.HeartbeatGeneralUnknownCount == 0,
            ["Movement excluded from differential dataset"] = bundle.DifferentialSummary.MovementExcluded == 1,
            ["Heartbeat excluded from differential dataset"] = bundle.DifferentialSummary.HeartbeatExcluded == 1,
            ["Known packet routed to Evidence bucket"] = bundle.EvidenceRoutes.Any(r => r.PacketFamily == "BattleCommand20"),
            ["Known packet not duplicated into Unknown"] = bundle.Counters.KnownPacketDuplication == 0,
            ["Unknown new signature enters actionable queue"] = bundle.UnknownActionable.Any(c => c.FrameLength == 8),
            ["Repeated unknown samples cluster"] = bundle.Clusters.Any(c => c.FrameLength == 8 && c.SampleCount >= 3 && c.PromotionStage == "ObservedRepeated"),
            ["Candidate cannot mutate Runtime"] = bundle.Gates.CandidateRuntimeMutationBlocked,
            ["DecoderVerified required for semantic mapping"] = bundle.Gates.DecoderVerifiedRequiredForSemanticMapping,
            ["SerializerVerified required for production bytes"] = bundle.Gates.SerializerVerifiedRequiredForProductionBytes,
            ["Fake Network Bytes remain 0"] = bundle.Gates.FakeNetworkBytes == 0,
            ["Frame Reconstruction splits concatenated frames"] = bundle.Frames.Count(f => f.RawEvidenceReference.Contains("seq-8", StringComparison.Ordinal)) == 2,
            ["ActorPrimary remains disabled"] = bundle.Gates.ActorPrimary == "disabled",
            ["LegacyPrimary remains default"] = bundle.Gates.LegacyPrimary == "default"
        };
        var failed = checks.Where(c => !c.Value).Select(c => c.Key).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { pass = failed.Length == 0, checks }, JsonOptions.Indented));
        return failed.Length == 0 ? 0 : 1;
    }

    private static TraceRecord SyntheticTrace(ulong sequence, Direction direction, byte[] payload) =>
        new(sequence, 1_000 + (long)sequence * 100, ApiKind.Send, direction, 100, payload.Length, 0, (uint)payload.Length, (uint)payload.Length, (uint)payload.Length, false, 2592, 40000, 0, [], payload);

    private static byte[] FrameWithLength(int length, byte seed)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)length);
        for (var i = 2; i < frame.Length; i++)
        {
            frame[i] = (byte)(seed + i);
        }

        return frame;
    }

    private static byte[] Combine(params byte[][] values)
    {
        var result = new byte[values.Sum(v => v.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            Buffer.BlockCopy(value, 0, result, offset, value.Length);
            offset += value.Length;
        }

        return result;
    }

    private static async Task<int> WorldMatrixAsync(string[] args)
    {
        var runDir = Path.GetFullPath(Required(args, "--run-dir"));
        var markersPath = Path.GetFullPath(Required(args, "--markers"));
        var outputPath = Path.GetFullPath(Required(args, "--out"));
        var tracePath = Path.Combine(runDir, "sensitive", "trace.bin");
        var records = TraceReader.Read(tracePath).OrderBy(r => r.WallUnixMs).ToArray();
        var markers = await LoadWorldMarkersAsync(markersPath);
        var result = WorldPacketMatrix.From(records, markers);
        var repoRoot = FindRepoRoot(runDir);
        var liveClassification = LivePacketClassifier.Classify(records, Path.GetFileName(runDir), "unknown", "world-matrix");
        await LivePacketArtifactWriter.WriteAsync(liveClassification, repoRoot, Path.Combine(runDir, "analysis", "live-classification"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, JsonOptions.Indented));
        Console.WriteLine(outputPath);
        return result.ConfirmedMovementPacket is null ? 4 : 0;
    }

    private static async Task<IReadOnlyList<WorldScenarioMarker>> LoadWorldMarkersAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("World capture marker file not found.", path);
        }

        var markers = new List<WorldScenarioMarker>();
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var marker = JsonSerializer.Deserialize<WorldScenarioMarker>(line, JsonOptions.CaseInsensitive);
            if (marker is not null)
            {
                markers.Add(marker);
            }
        }

        return markers;
    }

    private static string NormalizeSelfTestCase(string value) =>
        value.ToLowerInvariant() switch
        {
            "send" => "send",
            "wsasend" => "WSASend",
            "recv" => "recv",
            "wsarecv" => "WSARecv",
            "wsarecvoverlapped" => "WSARecvOverlapped",
            _ => throw new ArgumentException($"Unknown selftest case: {value}")
        };

    private static ApiKind ExpectedSelfTestApi(string caseName) =>
        caseName switch
        {
            "send" => ApiKind.Send,
            "WSASend" => ApiKind.WSASend,
            "recv" => ApiKind.Recv,
            "WSARecv" => ApiKind.WSARecv,
            "WSARecvOverlapped" => ApiKind.WSARecv,
            _ => throw new ArgumentException($"Unknown selftest case: {caseName}")
        };

    private static Direction ExpectedSelfTestDirection(string caseName) =>
        caseName is "send" or "WSASend" ? Direction.ClientToServer : Direction.ServerToClient;

    private static byte[] SelfTestPayload(string caseName)
    {
        var length = caseName is "send" or "WSASend" ? 208 : 64;
        var seed = caseName switch
        {
            "send" => 0x11,
            "WSASend" => 0x22,
            "recv" => 0x33,
            "WSARecv" => 0x44,
            "WSARecvOverlapped" => 0x55,
            _ => throw new ArgumentException($"Unknown selftest case: {caseName}")
        };
        var payload = new byte[length];
        payload[0] = (byte)(length & 0xFF);
        payload[1] = (byte)((length >> 8) & 0xFF);
        payload[2] = 0x42; // non-authentication protocol fixture
        for (var i = 3; i + 1 < payload.Length; i++)
        {
            payload[i] = (byte)((seed + i * 31) & 0xFF);
        }
        byte checksum = 0;
        for (var i = 0; i + 1 < payload.Length; i++)
        {
            checksum = (byte)(checksum + (byte)(payload[i] + 0x3C));
        }
        payload[^1] = checksum;
        return payload;
    }

    private static async Task<int> EndpointAsync(string[] args)
    {
        var repoRoot = Path.GetFullPath(Required(args, "--repo-root"));
        var runDir = Path.GetFullPath(Required(args, "--run-dir"));
        var port = OptionalInt(args, "--port", 2592);
        var durationSeconds = OptionalInt(args, "--duration-seconds", 1800);
        var loginEvidenceDir = Path.Combine(repoRoot, "src", "God2.ClassicServer.Protocol", "Evidence", "ProtocolEvidenceRecovery", "VerifiedRaw", "Login");
        var handshake = await File.ReadAllBytesAsync(Path.Combine(loginEvidenceDir, "login-server-handshake-19-54b82a7b7d2d.bin"));
        var followup = await File.ReadAllBytesAsync(Path.Combine(loginEvidenceDir, "login-server-followup-6-bad8acdca13f.bin"));

        Directory.CreateDirectory(Path.Combine(runDir, "sensitive", "endpoint"));
        Directory.CreateDirectory(Path.Combine(runDir, "analysis"));
        var endpointLog = Path.Combine(runDir, "analysis", "instrumentation-endpoint.jsonl");
        await File.AppendAllTextAsync(endpointLog, JsonSerializer.Serialize(new
        {
            eventName = "endpoint-started",
            timestampUtc = DateTimeOffset.UtcNow,
            bind = $"127.0.0.1:{port}",
            mode = "instrumentation-handshake-only",
            noFormalAuthentication = true,
            noSuccessReplay = true
        }) + Environment.NewLine);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(backlog: 4);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cts.Token);
                try
                {
                    await HandleEndpointClientAsync(client, handshake, followup, runDir, endpointLog, cts.Token);
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is InvalidOperationException)
                {
                    await AppendEndpointStatusAsync(endpointLog, "client-transport-error", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
            await File.AppendAllTextAsync(endpointLog, JsonSerializer.Serialize(new
            {
                eventName = "endpoint-stopped",
                timestampUtc = DateTimeOffset.UtcNow
            }) + Environment.NewLine);
        }

        return 0;
    }

    private static async Task HandleEndpointClientAsync(TcpClient client, byte[] handshake, byte[] followup, string runDir, string endpointLog, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        await AppendEndpointEventAsync(endpointLog, "accepted", connectionId, remote, 0, null);
        await using var stream = client.GetStream();
        await stream.WriteAsync(handshake, cancellationToken);
        await AppendEndpointEventAsync(endpointLog, "sent-server-handshake", connectionId, remote, handshake.Length, Sha256Hex(handshake));

        var clientHandshake = await ReadFrameAsync(stream, cancellationToken);
        if (clientHandshake.Length == 0)
        {
            await AppendEndpointEventAsync(endpointLog, "client-disconnected-before-handshake", connectionId, remote, 0, null);
            return;
        }

        if (clientHandshake.Length > 0)
        {
            await SaveSensitiveEndpointFrameAsync(runDir, connectionId, "client-handshake", clientHandshake);
            await AppendEndpointEventAsync(endpointLog, "received-client-handshake", connectionId, remote, clientHandshake.Length, Sha256Hex(clientHandshake));
        }

        await stream.WriteAsync(followup, cancellationToken);
        await AppendEndpointEventAsync(endpointLog, "sent-server-followup", connectionId, remote, followup.Length, Sha256Hex(followup));

        var loginRequest = await ReadFrameAsync(stream, cancellationToken);
        if (loginRequest.Length > 0)
        {
            await SaveSensitiveEndpointFrameAsync(runDir, connectionId, "login-request-candidate", loginRequest);
            await AppendEndpointEventAsync(endpointLog, "received-login-request-candidate", connectionId, remote, loginRequest.Length, Sha256Hex(loginRequest));
        }
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactOrEmptyAsync(stream, 2, TimeSpan.FromSeconds(30), cancellationToken);
        if (header.Length != 2)
        {
            return Array.Empty<byte>();
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (length < 2 || length > 4096)
        {
            return header;
        }

        var body = await ReadExactOrEmptyAsync(stream, length - 2, TimeSpan.FromSeconds(30), cancellationToken);
        if (body.Length != length - 2)
        {
            return header.Concat(body).ToArray();
        }

        return header.Concat(body).ToArray();
    }

    private static async Task<byte[]> ReadExactOrEmptyAsync(NetworkStream stream, int length, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            while (offset < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), timeoutCts.Token);
                if (read == 0)
                {
                    break;
                }
                offset += read;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        if (offset == length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, offset);
        return buffer;
    }

    private static async Task SaveSensitiveEndpointFrameAsync(string runDir, string connectionId, string role, byte[] frame)
    {
        var path = Path.Combine(runDir, "sensitive", "endpoint", $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{connectionId}-{role}-{frame.Length}.bin");
        await File.WriteAllBytesAsync(path, frame);
    }

    private static Task AppendEndpointEventAsync(string endpointLog, string eventName, string connectionId, string remote, int length, string? sha256) =>
        File.AppendAllTextAsync(endpointLog, JsonSerializer.Serialize(new
        {
            eventName,
            timestampUtc = DateTimeOffset.UtcNow,
            connectionId,
            remote,
            length,
            sha256
        }) + Environment.NewLine);

    private static Task AppendEndpointStatusAsync(string endpointLog, string eventName, string detail) =>
        File.AppendAllTextAsync(endpointLog, JsonSerializer.Serialize(new
        {
            eventName,
            timestampUtc = DateTimeOffset.UtcNow,
            detail
        }) + Environment.NewLine);

    private static Dictionary<string, TraceRecord?> AssignSamples(IReadOnlyList<SampleMarker> markers, IReadOnlyList<TraceRecord> c2s208)
    {
        var result = new Dictionary<string, TraceRecord?>(StringComparer.Ordinal)
        {
            ["A"] = null,
            ["B"] = null,
            ["C"] = null,
            ["D"] = null
        };

        var orderedMarkers = markers.OrderBy(m => m.MarkerUnixMs).ToArray();
        for (var i = 0; i < orderedMarkers.Length; i++)
        {
            var marker = orderedMarkers[i];
            var end = i + 1 < orderedMarkers.Length ? orderedMarkers[i + 1].MarkerUnixMs : long.MaxValue;
            result[marker.Sample] = c2s208.FirstOrDefault(r => r.WallUnixMs >= marker.MarkerUnixMs && r.WallUnixMs < end);
        }

        return result;
    }

    private static async Task<IReadOnlyList<SampleMarker>> LoadMarkersAsync(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<SampleMarker>();
        }

        var markers = new List<SampleMarker>();
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            markers.Add(new SampleMarker(
                doc.RootElement.GetProperty("sample").GetString() ?? "",
                doc.RootElement.GetProperty("markerUnixMs").GetInt64()));
        }

        return markers;
    }

    private static async Task<ClientManifest> LoadManifestAsync(string path)
    {
        if (!File.Exists(path))
        {
            var runDir = Path.GetDirectoryName(path) ?? "";
            return new ClientManifest(
                Path.GetFileName(runDir),
                "",
                "x86",
                "unknown");
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var client = doc.RootElement.GetProperty("client");
        return new ClientManifest(
            doc.RootElement.GetProperty("runId").GetString() ?? "",
            client.GetProperty("exePath").GetString() ?? "",
            client.GetProperty("architecture").GetString() ?? "",
            client.GetProperty("sha256").GetString() ?? "");
    }

    private static async Task<StopSummary?> LoadStopSummaryAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return new StopSummary(
            doc.RootElement.GetProperty("detach").GetString() ?? "unknown",
            doc.RootElement.GetProperty("clientSha256After").GetString() ?? "",
            doc.RootElement.GetProperty("clientBinaryUnchanged").GetBoolean());
    }

    private static string Required(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        throw new ArgumentException($"Missing required argument: {name}");
    }

    private static int OptionalInt(string[] args, string name, int defaultValue)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string EscapeIdentifier(string value) => value.Replace("`", "``", StringComparison.Ordinal);

    private static string FindRepoRoot(string start)
    {
        var current = new DirectoryInfo(Path.GetFullPath(start));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

internal sealed record ClientManifest(string RunId, string ClientExePath, string Architecture, string Sha256);

internal sealed record StopSummary(string Detach, string ClientSha256After, bool ClientBinaryUnchanged);

internal sealed record SampleMarker(string Sample, long MarkerUnixMs);

internal enum ApiKind : ushort
{
    Send = 1,
    WSASend = 2,
    Recv = 3,
    WSARecv = 4,
    PostDecrypt = 5,
    PreEncrypt = 6,
    HandlerDecoded = 7,
    BattleActorSnapshot = 8,
    BattleStateSnapshot = 9,
    BattleUiVitalObservation = 10,
    MonsterIntelligenceConsumerSnapshot = 11
}

internal enum Direction : ushort
{
    ClientToServer = 1,
    ServerToClient = 2
}

internal sealed record TraceRecord(
    ulong Sequence,
    long WallUnixMs,
    ApiKind Api,
    Direction Direction,
    uint Socket,
    int CallResult,
    uint LastError,
    uint RequestedLength,
    uint TransferredLength,
    uint CapturedLength,
    bool Frame208,
    ushort LocalPort,
    ushort RemotePort,
    uint ReturnAddress,
    IReadOnlyList<uint> Stack,
    byte[] Payload)
{
    public string PayloadSha256 => Convert.ToHexString(SHA256.HashData(Payload));
}

internal static class TraceReader
{
    public static IEnumerable<TraceRecord> Read(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        foreach (var record in Read(stream, path))
        {
            yield return record;
        }
    }

    public static IEnumerable<TraceRecord> Read(Stream stream, string sourceName)
    {
        var fileHeader = new byte[24];
        if (!ReadExact(stream, fileHeader))
        {
            yield break;
        }

        if (!fileHeader.AsSpan(0, 7).SequenceEqual("G2TRC01"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(fileHeader.AsSpan(8, 4)) != 1)
        {
            throw new InvalidDataException($"Invalid trace file header: {sourceName}");
        }

        long previousRecordOffset = -1;
        uint previousPayloadSize = 0;
        while (stream.Position < stream.Length)
        {
            var recordOffset = stream.Position;
            var header = new byte[ProgramRecord.HeaderSize];
            var read = ReadSome(stream, header);
            if (read == 0)
            {
                yield break;
            }

            if (read != header.Length)
            {
                throw new InvalidDataException(BuildTraceDiagnostic(sourceName, stream, recordOffset, previousRecordOffset, previousPayloadSize, header.AsSpan(0, read).ToArray(), "Truncated trace record header"));
            }

            var record = ProgramRecord.Parse(header);
            if (record.Magic != ProgramRecord.MagicValue)
            {
                throw new InvalidDataException(BuildTraceDiagnostic(sourceName, stream, recordOffset, previousRecordOffset, previousPayloadSize, header, "Invalid trace record magic"));
            }
            if (record.Version != ProgramRecord.VersionValue || record.HeaderSizeValue != ProgramRecord.HeaderSize)
            {
                throw new InvalidDataException(BuildTraceDiagnostic(sourceName, stream, recordOffset, previousRecordOffset, previousPayloadSize, header, $"Invalid trace record header version={record.Version} headerSize={record.HeaderSizeValue}"));
            }
            if (record.CapturedLength > ProgramRecord.MaxPayload)
            {
                throw new InvalidDataException(BuildTraceDiagnostic(sourceName, stream, recordOffset, previousRecordOffset, previousPayloadSize, header, $"Invalid trace record payload size={record.CapturedLength}"));
            }

            var payload = new byte[record.CapturedLength];
            if (payload.Length > 0 && !ReadExact(stream, payload))
            {
                throw new InvalidDataException(BuildTraceDiagnostic(sourceName, stream, recordOffset, previousRecordOffset, previousPayloadSize, header, $"Truncated trace record payload size={record.CapturedLength}"));
            }

            previousRecordOffset = recordOffset;
            previousPayloadSize = record.CapturedLength;
            yield return record.ToTraceRecord(payload);
        }
    }

    private static int ReadSome(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }
        return offset;
    }

    private static bool ReadExact(Stream stream, byte[] buffer) => ReadSome(stream, buffer) == buffer.Length;

    private static string BuildTraceDiagnostic(string path, Stream stream, long currentOffset, long previousRecordOffset, uint previousPayloadSize, byte[] actualBytes, string reason)
    {
        var fileLength = stream.Length;
        var remainingBytes = Math.Max(0, fileLength - currentOffset);
        var expectedMagic = BitConverter.GetBytes(ProgramRecord.MagicValue);
        var dumpLength = (int)Math.Min(64, remainingBytes);
        if (dumpLength < 32 && remainingBytes >= 32)
        {
            dumpLength = 32;
        }

        var dump = new byte[dumpLength];
        var restorePosition = stream.Position;
        stream.Position = currentOffset;
        _ = stream.Read(dump, 0, dump.Length);
        stream.Position = restorePosition;

        return string.Join(Environment.NewLine, new[]
        {
            reason,
            $"file: {path}",
            $"file length: {fileLength}",
            $"current offset: {currentOffset}",
            $"remaining bytes: {remainingBytes}",
            $"expected magic: {Hex(expectedMagic)}",
            $"actual bytes: {Hex(actualBytes.Take(Math.Min(8, actualBytes.Length)).ToArray())}",
            $"previous record offset: {previousRecordOffset}",
            $"previous payload size: {previousPayloadSize}",
            $"hex dump: {Hex(dump)}"
        });
    }

    private static string Hex(byte[] bytes) => bytes.Length == 0 ? "<empty>" : Convert.ToHexString(bytes);
}

internal readonly record struct ProgramRecord(
    uint Magic,
    ushort Version,
    ushort HeaderSizeValue,
    ulong Sequence,
    long WallUnixMs,
    ulong Qpc,
    uint ProcessId,
    uint ThreadId,
    ApiKind Api,
    Direction Direction,
    uint Socket,
    int CallResult,
    uint LastError,
    uint RequestedLength,
    uint TransferredLength,
    uint CapturedLength,
    bool Frame208,
    uint Flags,
    ushort LocalFamily,
    ushort RemoteFamily,
    ushort LocalPort,
    ushort RemotePort,
    uint ReturnAddress,
    IReadOnlyList<uint> Stack)
{
    public const uint MagicValue = 0x31523247;
    public const ushort VersionValue = 1;
    public const int HeaderSize = 188;
    public const uint MaxPayload = 4096;

    public static ProgramRecord Parse(ReadOnlySpan<byte> bytes)
    {
        var stack = new uint[16];
        for (var i = 0; i < stack.Length; i++)
        {
            stack[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(124 + i * 4, 4));
        }

        return new ProgramRecord(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[0..4]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..6]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..16]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[16..24]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..32]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..36]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..40]),
            (ApiKind)BinaryPrimitives.ReadUInt16LittleEndian(bytes[40..42]),
            (Direction)BinaryPrimitives.ReadUInt16LittleEndian(bytes[42..44]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..48]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[48..52]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[52..56]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[56..60]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[60..64]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[64..68]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[68..72]) != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[72..76]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[76..78]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[78..80]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[80..82]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[82..84]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[116..120]),
            stack.Take((int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes[120..124]), 16)).ToArray());
    }

    public TraceRecord ToTraceRecord(byte[] payload) =>
        new(Sequence, WallUnixMs, Api, Direction, Socket, CallResult, LastError, RequestedLength, TransferredLength, CapturedLength, Frame208, LocalPort, RemotePort, ReturnAddress, Stack, payload);
}

internal sealed record DifferentialResult(
    IReadOnlyDictionary<string, SampleEvidence> Samples,
    IReadOnlyList<int> AccountCorrelatedOffsets,
    IReadOnlyList<int> PasswordCorrelatedOffsets,
    IReadOnlyList<int> SessionCorrelatedOffsets,
    string CredentialForm,
    int? CredentialBlockOffset,
    int? CredentialBlockLength,
    bool LoginCredentialFieldsVerified,
    bool LoginRuntimeMutationEnabled)
{
    public static DifferentialResult From(IReadOnlyDictionary<string, TraceRecord> frames)
    {
        var sampleEvidence = frames.ToDictionary(
            item => item.Key,
            item => new SampleEvidence(item.Value.Sequence, item.Value.Api.ToString(), item.Value.Payload.Length, item.Value.PayloadSha256, $"0x{item.Value.ReturnAddress:X8}"),
            StringComparer.Ordinal);

        var account = PairDiff(frames, "A", "C");
        var password = PairDiff(frames, "A", "B");
        var session = PairDiff(frames, "A", "D");
        var credentialOffsets = account.Concat(password).Distinct().Order().ToArray();
        var blockOffset = credentialOffsets.Length == 0 ? (int?)null : credentialOffsets.Min();
        var blockLength = credentialOffsets.Length == 0 ? (int?)null : credentialOffsets.Max() - credentialOffsets.Min() + 1;
        var form = Classify(frames);
        var fieldsVerified = account.Count > 0 && password.Count > 0 && frames.Count == 4;
        return new DifferentialResult(
            sampleEvidence,
            account,
            password,
            session,
            form,
            blockOffset,
            blockLength,
            fieldsVerified,
            LoginRuntimeMutationEnabled: false);
    }

    private static IReadOnlyList<int> PairDiff(IReadOnlyDictionary<string, TraceRecord> frames, string left, string right)
    {
        if (!frames.TryGetValue(left, out var a) || !frames.TryGetValue(right, out var b))
        {
            return Array.Empty<int>();
        }

        var count = Math.Min(a.Payload.Length, b.Payload.Length);
        var offsets = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (a.Payload[i] != b.Payload[i])
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static string Classify(IReadOnlyDictionary<string, TraceRecord> frames)
    {
        if (!frames.TryGetValue("A", out var a))
        {
            return "unknown-no-sample-a";
        }

        var ascii = Encoding.ASCII.GetString(a.Payload);
        if (ascii.Contains("testalpha", StringComparison.Ordinal) || ascii.Contains("passone", StringComparison.Ordinal))
        {
            return "plaintext";
        }

        return "opaque-transform-or-encrypted-block";
    }
}

internal sealed record SampleEvidence(ulong Sequence, string Api, int Length, string Sha256, string ReturnAddress);

internal sealed record WorldScenarioMarker(
    string Scenario,
    string Phase,
    long WallUnixMs,
    string? Screenshot,
    string? Stage,
    int? PlayerScreenX,
    int? PlayerScreenY);

internal sealed record WorldPacketSample(
    ulong Sequence,
    string Direction,
    string Api,
    uint Socket,
    int Length,
    string EncodedHex,
    string DecodedHex,
    string OpcodeCandidate,
    string PayloadSha256,
    string ReturnRva,
    long WallUnixMs);

internal sealed record WorldScenarioAnalysis(
    string Scenario,
    long StartUnixMs,
    long EndUnixMs,
    string? BeforeScreenshot,
    string? AfterScreenshot,
    IReadOnlyList<WorldPacketSample> Packets,
    IReadOnlyDictionary<string, int> LengthCounts,
    IReadOnlyList<string> NewVersusIdle,
    IReadOnlyList<WorldByteDiff> DiffVersusIdle);

internal sealed record WorldByteDiff(
    string PacketKey,
    IReadOnlyList<int> ChangedOffsets,
    string LeftHex,
    string RightHex);

internal sealed record WorldUnknownClassification(
    string Name,
    int Length,
    string Direction,
    int Count,
    string Timing,
    string Classification,
    string Evidence);

internal sealed record LivePacketClassificationBundle(
    string SchemaVersion,
    string GeneratedAtUtc,
    string CaptureSessionId,
    string AutomationRunId,
    string ClientBuildId,
    string CaptureStage,
    IReadOnlyList<LivePacketFrameClassification> Frames,
    IReadOnlyList<LivePacketCluster> Clusters,
    IReadOnlyList<LiveUnknownActionable> UnknownActionable,
    IReadOnlyList<LiveEvidenceRoute> EvidenceRoutes,
    LiveDifferentialSummary DifferentialSummary,
    LiveClassificationGates Gates,
    LiveClassificationCounters Counters,
    IReadOnlyList<KnownInfrastructurePolicy> KnownInfrastructureTraffic);

internal sealed record LivePacketFrameClassification(
    string SampleId,
    ulong SocketSequence,
    string Direction,
    string ConnectionPhase,
    string HookSource,
    int FrameLength,
    int PayloadLength,
    string OpcodeDispatchCandidate,
    string PrefixSignature,
    string SuffixSignature,
    string PayloadHash,
    long RelativeTimestampMs,
    string? PreviousFrame,
    string? NextFrame,
    string CurrentProtocolState,
    string CurrentUiState,
    string CurrentAutomationAction,
    string CurrentRuntimeSemanticEvent,
    string AccountAlias,
    string CharacterClass,
    int? CharacterLevel,
    string BattleIdSafeReference,
    string RoundSafeReference,
    string CaptureStage,
    string RawEvidenceReference,
    string Classification,
    string PacketFamily,
    string PromotionStage,
    string Confidence,
    string ClassificationReason,
    bool ExcludedFromGeneralUnknown,
    bool ExcludedFromDifferential,
    bool RuntimeMutationBlocked,
    bool FakeNetworkBytesAllowed);

internal sealed record LiveTraceFrame(
    ulong Sequence,
    ulong SourceSequence,
    int FrameIndex,
    long WallUnixMs,
    ApiKind Api,
    Direction Direction,
    uint Socket,
    ushort LocalPort,
    ushort RemotePort,
    uint ReturnAddress,
    byte[] Payload)
{
    public string PayloadSha256 => Convert.ToHexString(SHA256.HashData(Payload));
}

internal sealed record LivePacketCluster(
    string ClusterId,
    int SampleCount,
    string Direction,
    int FrameLength,
    string Prefix,
    string FirstSeen,
    string LastSeen,
    IReadOnlyList<string> AssociatedActions,
    IReadOnlyList<string> AssociatedStates,
    IReadOnlyList<string> CandidateFamilies,
    string Confidence,
    IReadOnlyList<string> RawReferences,
    string ExclusionReason,
    string NextRequiredControlledAction,
    string PromotionStage);

internal sealed record LiveUnknownActionable(
    string ClusterId,
    int SampleCount,
    string Direction,
    int FrameLength,
    string Prefix,
    string FirstSeen,
    string LastSeen,
    IReadOnlyList<string> AssociatedActions,
    IReadOnlyList<string> AssociatedStates,
    IReadOnlyList<string> CandidateFamilies,
    string Confidence,
    IReadOnlyList<string> RawReferences,
    string ExclusionReason,
    string NextRequiredControlledAction);

internal sealed record LiveEvidenceRoute(
    string SampleId,
    string EvidenceBucket,
    string PacketFamily,
    string Direction,
    string StateBefore,
    string StateAfter,
    string PreviousFrame,
    string NextFrame,
    string AutomationAction,
    string RuntimeSemanticEvent,
    string Confidence,
    bool DecoderVerified,
    bool SerializerVerified,
    bool RuntimeMutationAllowed);

internal sealed record LiveDifferentialSummary(
    int InputFrames,
    int FramesAfterKnownInfrastructureExclusion,
    int MovementExcluded,
    int HeartbeatExcluded,
    int KnownPacketExcludedFromUnknown,
    int UnknownQueueBeforeFiltering,
    int UnknownQueueAfterFiltering,
    int UnknownActionableClusters,
    IReadOnlyList<string> DynamicDimensions);

internal sealed record LiveClassificationGates(
    bool DecoderVerifiedRequiredForSemanticMapping,
    bool SerializerVerifiedRequiredForProductionBytes,
    bool CandidateRuntimeMutationBlocked,
    int RuntimeMutationBlockedCount,
    int FakeNetworkBytes,
    string ActorPrimary,
    string LegacyPrimary);

internal sealed record LiveClassificationCounters(
    int TotalFrames,
    int KnownVerified,
    int KnownCandidate,
    int ObservedRepeated,
    int UnknownNew,
    int KnownInfrastructureTraffic,
    int MovementGeneralUnknownCount,
    int HeartbeatGeneralUnknownCount,
    int KnownPacketDuplication,
    int ObservedOnceCount,
    int ObservedRepeatedCount,
    int CrossValidatedCount,
    int DecoderVerifiedChanges,
    int SerializerVerifiedChanges);

internal sealed record KnownInfrastructurePolicy(
    string Name,
    string Status,
    string CapturePolicy,
    string AnalysisPolicy,
    string UnknownQueuePolicy,
    IReadOnlyList<string> ReenableConditions);

internal static class LivePacketClassifier
{
    private static readonly IReadOnlyDictionary<string, string> ExactKnownHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["8820E11D5D650E1705BE8B749608E731A66BEF2CD4D41DEEC4856D6D61C3C7D2"] = "LoginAuthenticationCandidate",
        ["D3BC28936D009125B0ED76FDFF1B3A3BE9AE887BA81B7D425DBBF9BB0A262731"] = "CharacterCreateCandidate",
        ["56D6A4C8E7CFE7481798879BCC1EE132C3427B9AF62ED02F9840363EDF6691FC"] = "ShopInsufficientFunds",
        ["5424D07422B55CCF4E2816D2DEE9DE59E2220B74A661EA034B94B2AEAC9F6274"] = "OutOfCombatHeal",
        ["3CEBEDCEFF9EAD2D7EE609BB869C033A1B3649FD61010BB580413FD8156E64C9"] = "Crafting",
        ["E47FB4451668FB27AEBC7AF96351236F9834B36F419179D0CA9474B6F3BC1C97"] = "EquipmentSwitch",
        ["B73D32F0C8617FD49D201B982C997EC10FF0A53A979FF0502307D0498949FD41"] = "EquipmentSwitch",
        ["24EB163505AB3369F07A506A81AF267549D8D6D63F982DCC64567258E4139164"] = "BattleSettlementConfirmation"
    };

    public static LivePacketClassificationBundle Classify(IReadOnlyList<TraceRecord> records, string automationRunId, string clientBuildId, string captureStage)
    {
        var ordered = ReconstructFrames(records)
            .Where(r => r.Direction is Direction.ClientToServer or Direction.ServerToClient)
            .Where(r => r.Payload.Length > 0)
            .OrderBy(r => r.Sequence)
            .ToArray();
        var firstTimestamp = ordered.Length == 0 ? 0 : ordered.Min(r => r.WallUnixMs);
        var frames = new List<LivePacketFrameClassification>();
        for (var index = 0; index < ordered.Length; index++)
        {
            frames.Add(ClassifyFrame(ordered[index], index == 0 ? null : ordered[index - 1], index + 1 < ordered.Length ? ordered[index + 1] : null, firstTimestamp, captureStage));
        }

        var clusters = frames
            .Where(f => !f.ExcludedFromDifferential)
            .GroupBy(f => $"{f.Direction}:{f.FrameLength}:{f.PrefixSignature}:{f.SuffixSignature}", StringComparer.Ordinal)
            .Select(g => ToCluster(g.ToArray()))
            .OrderBy(c => c.ClusterId, StringComparer.Ordinal)
            .ToArray();
        var unknownActionable = clusters
            .Where(c => c.ExclusionReason == "ActionableUnknown")
            .Select(c => new LiveUnknownActionable(
                c.ClusterId,
                c.SampleCount,
                c.Direction,
                c.FrameLength,
                c.Prefix,
                c.FirstSeen,
                c.LastSeen,
                c.AssociatedActions,
                c.AssociatedStates,
                c.CandidateFamilies,
                c.Confidence,
                c.RawReferences,
                c.ExclusionReason,
                c.NextRequiredControlledAction))
            .ToArray();
        var evidenceRoutes = frames
            .Where(f => f.Classification is "KnownVerified" or "KnownCandidate" or "ObservedRepeated")
            .Where(f => f.PacketFamily is not "Movement" and not "Heartbeat")
            .Select(f => new LiveEvidenceRoute(
                f.SampleId,
                f.PacketFamily,
                f.PacketFamily,
                f.Direction,
                f.CurrentProtocolState,
                f.CurrentProtocolState,
                f.PreviousFrame ?? "",
                f.NextFrame ?? "",
                f.CurrentAutomationAction,
                f.CurrentRuntimeSemanticEvent,
                f.Confidence,
                DecoderVerified: false,
                SerializerVerified: false,
                RuntimeMutationAllowed: false))
            .ToArray();
        var movementExcluded = frames.Count(f => f.PacketFamily == "Movement" && f.Classification == "KnownInfrastructureTraffic");
        var heartbeatExcluded = frames.Count(f => f.PacketFamily == "Heartbeat" && f.Classification == "KnownInfrastructureTraffic");
        var knownExcluded = frames.Count(f => f.ExcludedFromGeneralUnknown && f.Classification != "KnownInfrastructureTraffic");
        var runtimeBlocked = frames.Count(f => f.RuntimeMutationBlocked);
        return new LivePacketClassificationBundle(
            "live-packet-classification-v1",
            DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            $"capture-{automationRunId}",
            automationRunId,
            clientBuildId,
            captureStage,
            frames,
            clusters,
            unknownActionable,
            evidenceRoutes,
            new LiveDifferentialSummary(
                frames.Count,
                frames.Count(f => !f.ExcludedFromDifferential),
                movementExcluded,
                heartbeatExcluded,
                knownExcluded,
                frames.Count,
                unknownActionable.Length,
                unknownActionable.Length,
                ["Same Family", "Same Direction", "Same Frame Length", "Same Prefix", "Same Suffix", "Changed Bytes", "Constant Bytes", "Session Dynamic", "Battle Dynamic", "Unknown Dynamic", "Encrypted/Opaque"]),
            new LiveClassificationGates(
                DecoderVerifiedRequiredForSemanticMapping: true,
                SerializerVerifiedRequiredForProductionBytes: true,
                CandidateRuntimeMutationBlocked: true,
                RuntimeMutationBlockedCount: runtimeBlocked,
                FakeNetworkBytes: 0,
                ActorPrimary: "disabled",
                LegacyPrimary: "default"),
            new LiveClassificationCounters(
                frames.Count,
                frames.Count(f => f.Classification == "KnownVerified"),
                frames.Count(f => f.Classification == "KnownCandidate"),
                frames.Count(f => f.Classification == "ObservedRepeated"),
                frames.Count(f => f.Classification == "UnknownNew"),
                frames.Count(f => f.Classification == "KnownInfrastructureTraffic"),
                frames.Count(f => f.PacketFamily == "Movement" && !f.ExcludedFromGeneralUnknown),
                frames.Count(f => f.PacketFamily == "Heartbeat" && !f.ExcludedFromGeneralUnknown),
                0,
                frames.Count(f => f.PromotionStage == "ObservedOnce"),
                frames.Count(f => f.PromotionStage == "ObservedRepeated"),
                frames.Count(f => f.PromotionStage == "CrossValidated"),
                0,
                0),
            KnownInfrastructurePolicies());
    }

    internal static IReadOnlyList<LiveTraceFrame> ReconstructFrames(IReadOnlyList<TraceRecord> records)
    {
        var frames = new List<LiveTraceFrame>();
        var streams = new Dictionary<(Direction Direction, uint Socket), LiveFrameStreamState>();
        foreach (var record in records.OrderBy(record => record.Sequence))
        {
            // PostDecrypt records are already complete application frames.  They
            // are consumed by the handler-level pipeline and must not be appended
            // to an encrypted TCP stream a second time.
            if (record.Payload.Length == 0 || record.Api is ApiKind.PostDecrypt or ApiKind.PreEncrypt or ApiKind.HandlerDecoded or ApiKind.BattleActorSnapshot or ApiKind.BattleStateSnapshot or ApiKind.BattleUiVitalObservation or ApiKind.MonsterIntelligenceConsumerSnapshot)
            {
                continue;
            }

            var key = (record.Direction, record.Socket);
            if (!streams.TryGetValue(key, out var state))
            {
                state = new LiveFrameStreamState();
                streams.Add(key, state);
            }

            state.Append(record);
            while (state.TryTakeFrame(out var frame, out var source, out var frameIndex))
            {
                frames.Add(new LiveTraceFrame(
                    checked((source.Sequence * 1000) + (ulong)frameIndex),
                    source.Sequence,
                    frameIndex,
                    source.WallUnixMs,
                    source.Api,
                    source.Direction,
                    source.Socket,
                    source.LocalPort,
                    source.RemotePort,
                    source.ReturnAddress,
                    frame));
            }
        }

        return frames;
    }

    private sealed class LiveFrameStreamState
    {
        private readonly List<byte> _buffer = [];
        private TraceRecord? _source;
        private int _frameIndex;

        public void Append(TraceRecord record)
        {
            if (_buffer.Count == 0)
            {
                _source = record;
                _frameIndex = 0;
            }

            _buffer.AddRange(record.Payload);
        }

        public bool TryTakeFrame(out byte[] frame, out TraceRecord source, out int frameIndex)
        {
            frame = [];
            source = null!;
            frameIndex = _frameIndex;
            if (_buffer.Count < 2)
            {
                return false;
            }

            source = _source ?? throw new InvalidOperationException("Frame stream source is missing.");

            var length = _buffer[0] | (_buffer[1] << 8);
            if (length is < 2 or > 4096)
            {
                _buffer.Clear();
                _source = null;
                return false;
            }

            if (_buffer.Count < length)
            {
                return false;
            }

            frame = _buffer.GetRange(0, length).ToArray();
            _buffer.RemoveRange(0, length);
            _frameIndex++;
            if (_buffer.Count == 0)
            {
                _source = null;
            }
            return true;
        }
    }

    private static LivePacketFrameClassification ClassifyFrame(LiveTraceFrame record, LiveTraceFrame? previous, LiveTraceFrame? next, long firstTimestamp, string captureStage)
    {
        var payload = record.Payload;
        var hash = record.PayloadSha256;
        var hex = Convert.ToHexString(payload);
        var prefix = $"sha256:{hash[..12]}";
        var suffix = $"sha256:{hash[^12..]}";
        var direction = record.Direction.ToString();
        var family = "Unknown";
        var classification = "UnknownNew";
        var promotion = "ObservedOnce";
        var confidence = "Low";
        var reason = "New signature not matched to known infrastructure or current-build evidence.";
        var excludedUnknown = false;
        var excludedDifferential = false;

        if (record.Direction == Direction.ClientToServer && IsHeartbeat(payload))
        {
            family = "Heartbeat";
            classification = "KnownInfrastructureTraffic";
            promotion = "CrossValidated";
            confidence = "High";
            reason = "Server-supported keepalive; excluded by policy from unknown and differential datasets.";
            excludedUnknown = true;
            excludedDifferential = true;
        }
        else if (record.Direction == Direction.ClientToServer && IsMovement(payload))
        {
            family = "Movement";
            classification = "KnownInfrastructureTraffic";
            promotion = "CrossValidated";
            confidence = "MediumHigh";
            reason = "Server-supported movement family; excluded by policy from unknown and differential datasets.";
            excludedUnknown = true;
            excludedDifferential = true;
        }
        else if (ExactKnownHashes.TryGetValue(hash, out var knownFamily))
        {
            family = knownFamily;
            classification = knownFamily is "OutOfCombatHeal" or "EquipmentSwitch" or "BattleSettlementConfirmation"
                ? "ObservedRepeated"
                : "KnownCandidate";
            promotion = classification == "ObservedRepeated" ? "ObservedRepeated" : "ObservedOnce";
            confidence = classification == "ObservedRepeated" ? "MediumHigh" : "Medium";
            reason = "Exact current-build evidence signature matched; routed to evidence bucket and excluded from general unknown.";
            excludedUnknown = true;
        }
        else if (record.Direction == Direction.ClientToServer && payload.Length == 20 && HasValidLengthPrefix(payload))
        {
            family = "BattleCommand20";
            classification = "KnownCandidate";
            confidence = "Medium";
            reason = "Structural current-build battle command carrier length matched; action semantics remain blocked.";
            excludedUnknown = true;
        }
        else if (record.Direction == Direction.ClientToServer && payload.Length == 12 && HasValidLengthPrefix(payload))
        {
            family = "BattleCommand12";
            classification = "KnownCandidate";
            confidence = "Medium";
            reason = "Structural current-build battle boundary carrier length matched; action semantics remain blocked.";
            excludedUnknown = true;
        }
        else if (record.Direction == Direction.ServerToClient && TryDecodedServerFamily(payload, out var serverFamily))
        {
            family = serverFamily;
            classification = serverFamily is "BattleInitialization" or "BattleSettlement" ? "ObservedRepeated" : "KnownCandidate";
            promotion = classification == "ObservedRepeated" ? "ObservedRepeated" : "ObservedOnce";
            confidence = "Medium";
            reason = "Decoded current-build server family matched; serializer and semantic mutation remain blocked.";
            excludedUnknown = true;
        }

        var runtimeBlocked = family is "BattleCommand20" or "BattleCommand12" or "BattleAction" or "BattleEffect" or "BattleSettlement" or "BattleInitialization" or "OutOfCombatHeal" or "EquipmentSwitch" or "Crafting" or "ShopInsufficientFunds";
        return new LivePacketFrameClassification(
            $"seq-{record.SourceSequence}.{record.FrameIndex}",
            record.Sequence,
            direction,
            GuessConnectionPhase(record),
            record.Api.ToString(),
            payload.Length,
            Math.Max(0, payload.Length - 2),
            payload.Length >= 4 ? hex.Substring(4, 4) : "",
            prefix,
            suffix,
            hash,
            firstTimestamp == 0 ? 0 : Math.Max(0, record.WallUnixMs - firstTimestamp),
            previous is null ? null : $"seq-{previous.Sequence}",
            next is null ? null : $"seq-{next.Sequence}",
            GuessProtocolState(record, family),
            "Unknown",
            "Unmarked",
            "Unmarked",
            "SafeReferenceOnly",
            "Unknown",
            null,
            "SafeReferenceOnly",
            "SafeReferenceOnly",
            captureStage,
            $"sensitive/trace.bin#seq-{record.SourceSequence}.{record.FrameIndex}",
            classification,
            family,
            promotion,
            confidence,
            reason,
            excludedUnknown,
            excludedDifferential,
            runtimeBlocked,
            FakeNetworkBytesAllowed: false);
    }

    private static LivePacketCluster ToCluster(IReadOnlyList<LivePacketFrameClassification> frames)
    {
        var first = frames[0];
        var known = first.ExcludedFromGeneralUnknown;
        var repeated = frames.Count >= 3;
        var clusterHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes($"{first.Direction}:{first.FrameLength}:{first.PrefixSignature}:{first.SuffixSignature}")))[..12];
        return new LivePacketCluster(
            $"cluster-{clusterHash}",
            frames.Count,
            first.Direction,
            first.FrameLength,
            first.PrefixSignature,
            frames.Min(f => f.RelativeTimestampMs).ToString(CultureInfo.InvariantCulture),
            frames.Max(f => f.RelativeTimestampMs).ToString(CultureInfo.InvariantCulture),
            frames.Select(f => f.CurrentAutomationAction).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            frames.Select(f => f.CurrentProtocolState).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            frames.Select(f => f.PacketFamily).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            repeated ? "Medium" : first.Confidence,
            frames.Select(f => f.RawEvidenceReference).Take(12).ToArray(),
            known ? "KnownOrEvidenceRouted" : "ActionableUnknown",
            known ? "None" : "Controlled action capture with explicit marker and state snapshot.",
            known ? first.PromotionStage : repeated ? "ObservedRepeated" : "ObservedOnce");
    }

    private static bool IsHeartbeat(ReadOnlySpan<byte> frame) =>
        frame.SequenceEqual(Convert.FromHexString("05007ACCEC")) ||
        frame.SequenceEqual(Convert.FromHexString("05003D09A5")) ||
        (frame.Length == 5 && frame[0] == 0x05 && frame[1] == 0x00);

    private static bool IsMovement(ReadOnlySpan<byte> frame) =>
        frame.Length == 10 &&
        frame[0] == 0x0A &&
        frame[1] == 0x00;

    private static bool HasValidLengthPrefix(ReadOnlySpan<byte> frame) =>
        frame.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]) == frame.Length;

    private static bool TryDecodedServerFamily(ReadOnlySpan<byte> frame, out string family)
    {
        family = "";
        if (!HasValidLengthPrefix(frame) || frame.Length < 4)
        {
            return false;
        }

        var decoded = DecodeOfficialFrame(frame);
        var candidate = decoded.Length > 2 ? decoded[2] : (byte)0;
        family = candidate switch
        {
            0x6F => "MapInitialization",
            0x8D or 0xD6 => "BattleInitialization",
            0xD5 or 0xD7 or 0xD8 or 0xD9 or 0xDA => "BattleEffect",
            0xE6 => "BattleSettlement",
            _ => ""
        };
        return family.Length > 0;
    }

    private static byte[] DecodeOfficialFrame(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        if (decoded.Length < 3)
        {
            return decoded;
        }

        var key = decoded[2];
        for (var index = 3; index < decoded.Length; index++)
        {
            decoded[index] ^= key;
            key = decoded[index - 1];
        }

        return decoded;
    }

    private static string GuessConnectionPhase(LiveTraceFrame record) =>
        record.LocalPort == 2592 || record.RemotePort == 2592 ? "Game" : "Unknown";

    private static string GuessProtocolState(LiveTraceFrame record, string family) =>
        family is "Movement" or "Heartbeat" or "MapInitialization" or "BattleInitialization" or "BattleEffect" or "BattleSettlement" or "BattleCommand20" or "BattleCommand12"
            ? "InWorld"
            : record.Direction == Direction.ClientToServer && record.Payload.Length >= 200
                ? "Login"
                : "Unknown";

    private static IReadOnlyList<KnownInfrastructurePolicy> KnownInfrastructurePolicies() =>
    [
        new(
            "Movement",
            "ServerSupported",
            "IgnoreByDefault",
            "Exclude",
            "Never",
            ["ExplicitRegressionRequest", "MovementFailure", "CoordinateDesync", "DirectionFailure", "PortalPositionFailure"]),
        new(
            "Heartbeat",
            "ServerSupported",
            "IgnoreByDefault",
            "Exclude",
            "Never",
            ["ExplicitRegressionRequest", "HeartbeatFailure", "KeepAliveFailure", "DisconnectDetectionFailure"])
    ];
}

internal static class LivePacketArtifactWriter
{
    public static async Task WriteAsync(LivePacketClassificationBundle bundle, string repoRoot, string runOutputRoot)
    {
        Directory.CreateDirectory(runOutputRoot);
        var evidenceRoot = Path.Combine(repoRoot, "protocol", "evidence", "current-build");
        Directory.CreateDirectory(evidenceRoot);

        await WriteBothAsync(bundle, "live-classification.json", evidenceRoot, runOutputRoot);
        await WriteBothAsync(bundle.KnownInfrastructureTraffic.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal), "known-infrastructure-traffic.json", evidenceRoot, runOutputRoot);
        await WriteBothAsync(bundle.Clusters, "packet-clusters.json", evidenceRoot, runOutputRoot);
        await WriteBothAsync(bundle.UnknownActionable, "unknown-actionable.json", evidenceRoot, runOutputRoot);
        await WriteBothAsync(bundle.EvidenceRoutes, "evidence-routing.json", evidenceRoot, runOutputRoot);
        await WriteBothAsync(bundle.DifferentialSummary, "differential-summary.json", evidenceRoot, runOutputRoot);
        await WriteBothAsync(bundle.Gates, "classification-gates.json", evidenceRoot, runOutputRoot);
    }

    private static async Task WriteBothAsync<T>(T value, string name, string evidenceRoot, string runOutputRoot)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions.Indented);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, name), json, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runOutputRoot, name), json, Encoding.UTF8);
    }
}

internal sealed record WorldPacketMatrix(
    string SchemaVersion,
    string GeneratedAtUtc,
    string TraceSource,
    string MarkerSource,
    IReadOnlyList<WorldScenarioAnalysis> Scenarios,
    WorldPacketSample? ConfirmedMovementPacket,
    IReadOnlyDictionary<string, string> MovementFieldMap,
    IReadOnlyList<WorldUnknownClassification> UnknownClassifications,
    IReadOnlyList<string> PlayerSpawnCandidates,
    IReadOnlyList<string> Gaps)
{
    public static WorldPacketMatrix From(IReadOnlyList<TraceRecord> records, IReadOnlyList<WorldScenarioMarker> markers)
    {
        var scenarioNames = markers
            .Select(m => m.Scenario)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var byScenario = new Dictionary<string, WorldScenarioAnalysis>(StringComparer.Ordinal);
        foreach (var scenario in scenarioNames)
        {
            var start = markers.FirstOrDefault(m => m.Scenario == scenario && m.Phase == "start");
            var end = markers.LastOrDefault(m => m.Scenario == scenario && m.Phase == "end");
            if (start is null || end is null)
            {
                continue;
            }

            var packets = records
                .Where(r => r.WallUnixMs >= start.WallUnixMs && r.WallUnixMs <= end.WallUnixMs)
                .Where(r => r.Direction is Direction.ClientToServer or Direction.ServerToClient)
                .Where(r => r.Payload.Length > 0)
                .Select(ToWorldPacketSample)
                .ToArray();
            var lengthCounts = packets
                .GroupBy(p => $"{p.Direction}:{p.Length}", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            byScenario[scenario] = new WorldScenarioAnalysis(
                scenario,
                start.WallUnixMs,
                end.WallUnixMs,
                start.Screenshot,
                end.Screenshot,
                packets,
                lengthCounts,
                [],
                []);
        }

        if (byScenario.TryGetValue("Idle30s", out var idle))
        {
            foreach (var scenario in byScenario.Values.Where(s => s.Scenario != "Idle30s").ToArray())
            {
                var newKeys = scenario.Packets
                    .Select(PacketKey)
                    .Except(idle.Packets.Select(PacketKey), StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var diffs = BuildDiffs(idle.Packets, scenario.Packets);
                byScenario[scenario.Scenario] = scenario with
                {
                    NewVersusIdle = newKeys,
                    DiffVersusIdle = diffs
                };
            }
        }

        var analyses = byScenario.Values.OrderBy(v => v.StartUnixMs).ToArray();
        var movement = analyses
            .Where(a => !string.Equals(a.Scenario, "Idle30s", StringComparison.Ordinal))
            .SelectMany(a => a.Packets.Select(p => (a.Scenario, Packet: p)))
            .Where(item => item.Packet.Direction == nameof(Direction.ClientToServer))
            .Where(item => item.Packet.Length is > 5 and <= 16)
            .Where(item => !item.Packet.EncodedHex.StartsWith("0500", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Packet.EncodedHex, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Select(item => item.Scenario).Distinct(StringComparer.Ordinal).Count())
            .ThenByDescending(g => g.Count())
            .Select(g => g.First().Packet)
            .FirstOrDefault();

        var unknowns = ClassifyUnknowns(analyses, records);
        var spawnCandidates = analyses
            .SelectMany(a => a.Packets)
            .Where(p => p.Direction == nameof(Direction.ServerToClient) && (p.EncodedHex.Contains("6B65726F", StringComparison.OrdinalIgnoreCase) || p.DecodedHex.Contains("6B65726F", StringComparison.OrdinalIgnoreCase)))
            .Select(p => $"seq={p.Sequence};len={p.Length};rva={p.ReturnRva};opcodeCandidate={p.OpcodeCandidate}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var gaps = new List<string>();
        if (movement is null)
        {
            gaps.Add("No non-heartbeat short C2S movement packet appeared in the marked movement windows.");
        }
        if (spawnCandidates.Length == 0)
        {
            gaps.Add("PlayerSpawn candidate not present in captured window; use bootstrap packet-decode evidence to split the 1778-byte stream.");
        }

        return new WorldPacketMatrix(
            "world-packet-matrix-v1",
            DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            "sensitive/trace.bin",
            "world-capture-markers.jsonl",
            analyses,
            movement,
            BuildMovementFieldMap(movement),
            unknowns,
            spawnCandidates,
            gaps);
    }

    private static WorldPacketSample ToWorldPacketSample(TraceRecord record)
    {
        var hex = Convert.ToHexString(record.Payload);
        var decoded = TryDecodeOfficialFrame(record.Payload);
        return new WorldPacketSample(
            record.Sequence,
            record.Direction.ToString(),
            record.Api.ToString(),
            record.Socket,
            record.Payload.Length,
            hex,
            decoded,
            record.Payload.Length >= 4 ? hex.Substring(4, 4) : string.Empty,
            record.PayloadSha256,
            $"0x{record.ReturnAddress:X8}",
            record.WallUnixMs);
    }

    private static string TryDecodeOfficialFrame(byte[] frame)
    {
        if (frame.Length < 3 || frame.Length != BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(0, 2)))
        {
            return Convert.ToHexString(frame);
        }

        var decoded = frame.ToArray();
        var key = decoded[2];
        for (var index = 3; index < decoded.Length; index++)
        {
            decoded[index] ^= key;
            key = decoded[index - 1];
        }

        return Convert.ToHexString(decoded);
    }

    private static string PacketKey(WorldPacketSample packet) => $"{packet.Direction}:{packet.Length}:{packet.EncodedHex}";

    private static IReadOnlyList<WorldByteDiff> BuildDiffs(IReadOnlyList<WorldPacketSample> idle, IReadOnlyList<WorldPacketSample> scenario)
    {
        var diffs = new List<WorldByteDiff>();
        foreach (var candidate in scenario.Where(p => p.Direction == nameof(Direction.ClientToServer)))
        {
            var baseline = idle.FirstOrDefault(p => p.Direction == candidate.Direction && p.Length == candidate.Length);
            if (baseline is null || baseline.EncodedHex == candidate.EncodedHex)
            {
                continue;
            }

            var left = Convert.FromHexString(baseline.EncodedHex);
            var right = Convert.FromHexString(candidate.EncodedHex);
            var count = Math.Min(left.Length, right.Length);
            var offsets = Enumerable.Range(0, count).Where(i => left[i] != right[i]).ToArray();
            diffs.Add(new WorldByteDiff($"{candidate.Direction}:{candidate.Length}", offsets, baseline.EncodedHex, candidate.EncodedHex));
        }

        return diffs;
    }

    private static IReadOnlyDictionary<string, string> BuildMovementFieldMap(WorldPacketSample? movement)
    {
        if (movement is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = "not-confirmed"
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["frameLength"] = "offset 0..1 uint16le",
            ["opcodeCandidate"] = "offset 2..3 encrypted candidate; decode/signature still pending",
            ["directionOrStepCandidate"] = "offsets varying against Idle30s and per-direction captures",
            ["serverPositionUpdate"] = "pending live server parser promotion"
        };
    }

    private static IReadOnlyList<WorldUnknownClassification> ClassifyUnknowns(IReadOnlyList<WorldScenarioAnalysis> analyses, IReadOnlyList<TraceRecord> records)
    {
        var matrixPackets = analyses.SelectMany(a => a.Packets.Select(p => (a.Scenario, Packet: p))).ToArray();
        var worldSocket = records
            .Where(r => r.Direction == Direction.ClientToServer && r.Payload.Length == 5)
            .GroupBy(r => r.Socket)
            .OrderByDescending(g => g.Count())
            .Select(g => (uint?)g.Key)
            .FirstOrDefault();
        var entryPackets = records
            .Where(r => !worldSocket.HasValue || r.Socket == worldSocket.Value)
            .Where(r => r.Direction == Direction.ClientToServer)
            .Select(r => ("WorldSocketAll", Packet: ToWorldPacketSample(r)))
            .ToArray();
        var packets = matrixPackets.Concat(entryPackets).ToArray();
        return new[]
        {
            ClassifyLength(packets, 41, "WorldEntryAck"),
            ClassifyLength(packets, 71, "WorldBootstrapFollowUp"),
            ClassifyLength(packets, 12, "WorldUiStateAck")
        };
    }

    private static WorldUnknownClassification ClassifyLength(IReadOnlyList<(string Scenario, WorldPacketSample Packet)> packets, int length, string name)
    {
        var matches = packets.Where(item => item.Packet.Direction == nameof(Direction.ClientToServer) && item.Packet.Length == length).ToArray();
        var scenarios = matches.Select(m => m.Scenario).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var timing = scenarios.Length == 0 ? "not-observed" : string.Join(",", scenarios);
        var movementRelated = scenarios.Any(s => s.StartsWith("Move", StringComparison.Ordinal) || s.Contains("Movement", StringComparison.OrdinalIgnoreCase));
        var classification = matches.Length == 0
            ? "not-observed-in-marked-window"
            : movementRelated
                ? $"{name}-movement-correlated"
                : $"{name}-entry-bootstrap-correlated";
        var evidence = matches.Length == 0
            ? "No matching C2S packet in marked capture windows."
            : $"count={matches.Length}; firstSeq={matches[0].Packet.Sequence}; firstOpcode={matches[0].Packet.OpcodeCandidate}; firstRva={matches[0].Packet.ReturnRva}";
        return new WorldUnknownClassification(name, length, nameof(Direction.ClientToServer), matches.Length, timing, classification, evidence);
    }
}

internal static class ReportWriter
{
    public static string Write(
        ClientManifest manifest,
        StopSummary? stop,
        IReadOnlyList<TraceRecord> records,
        IReadOnlyDictionary<string, TraceRecord?> samples,
        DifferentialResult diff)
    {
        var c2s = records.Count(r => r.Direction == Direction.ClientToServer);
        var s2c = records.Count(r => r.Direction == Direction.ServerToClient);
        var sendBoundary = records.Any(r => r.Api is ApiKind.Send or ApiKind.WSASend);
        var request208 = samples.Values.Any(v => v is not null);
        var writerCandidates = samples.Values.Where(v => v is not null).Select(v => v!.ReturnAddress).Distinct().Count();
        var builderCandidates = samples.Values
            .Where(v => v is not null)
            .SelectMany(v => v!.Stack.Skip(1))
            .Where(v => v != 0)
            .Distinct()
            .Count();
        var savedCount = samples.Values.Count(v => v is not null);
        var missing = samples.Where(item => item.Value is null).Select(item => item.Key).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("# Client Login Instrumentation Report");
        sb.AppendLine();
        sb.AppendLine($"GeneratedAtUtc: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine($"RunId: {manifest.RunId}");
        sb.AppendLine($"ClientArchitecture: {manifest.Architecture}");
        sb.AppendLine($"ClientSha256Before: {manifest.Sha256}");
        sb.AppendLine($"ClientSha256After: {stop?.ClientSha256After ?? "not-checked"}");
        sb.AppendLine($"ClientBinaryUnchanged: {(stop?.ClientBinaryUnchanged == true ? "true" : "false")}");
        sb.AppendLine($"InstrumentationMethod: x86 LoadLibraryW DLL injection with IAT and dynamic Winsock hooks");
        sb.AppendLine($"SendBoundaryFound: {sendBoundary}");
        sb.AppendLine($"ApplicationBuffer208Found: {request208}");
        sb.AppendLine($"TraceRecordsClientToServer: {c2s}");
        sb.AppendLine($"TraceRecordsServerToClient: {s2c}");
        sb.AppendLine($"SamplesSaved: {savedCount}/4");
        sb.AppendLine($"MissingSamples: {(missing.Length == 0 ? "none" : string.Join(",", missing))}");
        sb.AppendLine($"BuilderCandidateCount: {builderCandidates}");
        sb.AppendLine($"WriterCandidateCount: {writerCandidates}");
        sb.AppendLine($"AccountCorrelatedWriterFound: {diff.AccountCorrelatedOffsets.Count > 0}");
        sb.AppendLine($"PasswordCorrelatedWriterFound: {diff.PasswordCorrelatedOffsets.Count > 0}");
        sb.AppendLine($"SessionCorrelatedBytesFound: {diff.SessionCorrelatedOffsets.Count > 0}");
        sb.AppendLine($"CredentialFinalForm: {diff.CredentialForm}");
        sb.AppendLine($"CredentialBlockOffset: {diff.CredentialBlockOffset?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        sb.AppendLine($"CredentialBlockLength: {diff.CredentialBlockLength?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        sb.AppendLine($"LoginCredentialFieldsVerified: {diff.LoginCredentialFieldsVerified}");
        sb.AppendLine($"LoginRuntimeMutationEnabled: {diff.LoginRuntimeMutationEnabled}");
        sb.AppendLine($"Detach: {stop?.Detach ?? "not-run"}");
        sb.AppendLine();
        sb.AppendLine("## Sample Evidence");
        foreach (var sample in new[] { "A", "B", "C", "D" })
        {
            if (!diff.Samples.TryGetValue(sample, out var evidence))
            {
                sb.AppendLine($"- {sample}: missing");
                continue;
            }

            sb.AppendLine($"- {sample}: sequence={evidence.Sequence}; api={evidence.Api}; length={evidence.Length}; sha256={evidence.Sha256}; return={evidence.ReturnAddress}");
        }

        sb.AppendLine();
        sb.AppendLine("## Verification Decision");
        if (diff.LoginCredentialFieldsVerified)
        {
            sb.AppendLine("Credential field differential evidence exists for the controlled samples. Runtime mutation remains disabled until parser offsets are promoted in production code with tests.");
        }
        else
        {
            sb.AppendLine(missing.Length == 0
                ? "All samples exist, but account/password differential offsets were not separable in this trace."
                : $"Evidence is incomplete. Only the following exact samples are missing: {string.Join(", ", missing)}.");
        }

        return sb.ToString();
    }
}

internal sealed record PreparedCharacter(
    long Id,
    string Name,
    string Class,
    int Level,
    int MapId,
    int PositionX,
    int PositionY,
    string Status,
    DateTime? DeletedAtUtc);

internal sealed record DatabaseOptions(
    string Host,
    int Port,
    string DatabaseName,
    string Username,
    string Password,
    string PasswordSource,
    string PasswordEnvironmentVariable,
    int ConnectionTimeoutSeconds)
{
    public static DatabaseOptions Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new DatabaseOptions(
            root.GetProperty("host").GetString() ?? "127.0.0.1",
            root.GetProperty("port").GetInt32(),
            root.GetProperty("databaseName").GetString() ?? "god2",
            root.GetProperty("username").GetString() ?? "god2_server",
            root.TryGetProperty("password", out var password) ? password.GetString() ?? "" : "",
            root.GetProperty("passwordSource").GetString() ?? "EnvironmentVariable",
            root.GetProperty("passwordEnvironmentVariable").GetString() ?? "GOD2_DB_PASSWORD",
            root.GetProperty("connectionTimeoutSeconds").GetInt32());
    }

    public string? ResolvePassword() =>
        string.Equals(PasswordSource, "ConfigValue", StringComparison.OrdinalIgnoreCase)
            ? Password
            : Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);

    public string BuildConnectionString(string password, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = Username,
            Password = password,
            ConnectionTimeout = (uint)Math.Max(1, ConnectionTimeoutSeconds),
            SslMode = MySqlSslMode.Disabled,
            AllowUserVariables = true
        };
        if (includeDatabase)
        {
            builder.Database = DatabaseName;
        }

        return builder.ConnectionString;
    }
}

internal static class PasswordHash
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
}
