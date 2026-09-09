using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Application.Shutdown;
using God2.ClassicServer.Application.Startup;
using God2.ClassicServer.ConsoleHost;
using God2.ClassicServer.Infrastructure;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;
using MySqlConnector;

var options = AutomationProbeOptions.Parse(args);
if (string.Equals(options.Mode, "map-identity-evidence-host", StringComparison.Ordinal))
{
    return await RunMapIdentityEvidenceHostAsync(options);
}

if (string.Equals(options.Mode, "map-identity-inventory", StringComparison.Ordinal))
{
    return await RunMapIdentityInventoryAsync(options);
}

if (string.Equals(options.Mode, "m1-character-position-cas", StringComparison.Ordinal))
{
    return await RunM1CharacterPositionCasAsync(options);
}

if (string.Equals(options.Mode, "m2-content-slice-inventory", StringComparison.Ordinal))
{
    return await RunM2ContentSliceInventoryAsync(options);
}

if (string.Equals(options.Mode, "m2-slice-verification", StringComparison.Ordinal))
{
    return await RunM2SliceVerificationAsync(options);
}

if (string.Equals(options.Mode, "m2-runtime-slice-validation", StringComparison.Ordinal))
{
    return await RunM2RuntimeSliceValidationAsync(options);
}

if (string.Equals(options.Mode, "m3-npc-replication-validation", StringComparison.Ordinal))
{
    return await RunM3NpcReplicationValidationAsync(options);
}

if (string.Equals(options.Mode, "m4-npc-interaction-validation", StringComparison.Ordinal))
{
    return await RunM4NpcInteractionValidationAsync(options);
}

if (string.Equals(options.Mode, "m4-world-movement-validation", StringComparison.Ordinal))
{
    return await RunM4WorldMovementValidationAsync(options);
}

if (string.Equals(options.Mode, "m5-battle-slice-validation", StringComparison.Ordinal))
{
    return await RunM5BattleSliceValidationAsync(options);
}

if (string.Equals(options.Mode, "m6-inventory-persistence-validation", StringComparison.Ordinal))
{
    return await RunM6InventoryPersistenceValidationAsync(options);
}

if (string.Equals(options.Mode, "m6-inventory-physical-fixture", StringComparison.Ordinal))
{
    return await RunM6InventoryPhysicalFixtureAsync(options);
}

if (string.Equals(options.Mode, "m7-character-lifecycle-physical-fixture", StringComparison.Ordinal))
{
    return await RunM7CharacterLifecyclePhysicalFixtureAsync(options);
}

if (string.Equals(options.Mode, "m8-content-expansion-validation", StringComparison.Ordinal))
{
    return await RunM8ContentExpansionValidationAsync(options);
}

if (string.Equals(options.Mode, "m9-production-backup-restore", StringComparison.Ordinal))
{
    return await M9ProductionBackupRestoreProbe.RunAsync(options);
}

if (string.Equals(options.Mode, "m9a-production-runtime-world", StringComparison.Ordinal))
{
    return await M9AProductionRuntimeWorldProbe.RunAsync(options);
}

var result = options.Mode switch
{
    "mariadb-preflight" => await MariaDbPreflightAsync(options),
    "account-credential-preflight" => await AccountCredentialPreflightAsync(options),
    "server-ready" => await ServerReadyAsync(options),
    "migration-verify" => await MigrationVerifyAsync(options),
    _ => AutomationProbeResult.Blocked(
        "Automation Probe",
        "automation.probe.mode_invalid",
        "Mode",
        "CommandLine",
        "Known probe mode.",
        $"Unsupported mode: {options.Mode}",
        "Use --mode mariadb-preflight, --mode account-credential-preflight, or --mode server-ready.")
};

WriteJson(result);
return result.OverallStatus == "PASS" ? 0 : 1;

static async Task<int> RunM8ContentExpansionValidationAsync(AutomationProbeOptions options)
{
    const string schemaVersion = "god2-roadmap-m8-content-expansion-validation-v1";
    try
    {
        var root = Path.GetFullPath(options.BaseDirectory);
        var paths = new AppPathProvider(root);
        var configuration = new JsonServerConfigurationLoader(paths).Load();
        if (!configuration.Succeeded || configuration.Value is null)
        {
            WriteJsonArtifact(new
            {
                schemaVersion,
                status = "BLOCKED",
                reason = configuration.Error.Code,
                authority = "MariaDB",
                secretRetained = false,
                connectionStringRetained = false,
                networkBytesEmitted = false,
                fakeNetworkBytes = false
            }, options.OutputPath, options.BaseDirectory);
            return 51;
        }

        var migration = await new SqlFileMigrationRunner(
            configuration.Value.Database,
            paths.DatabaseSchemaDirectory).VerifyAsync(CancellationToken.None);
        if (!migration.Succeeded || migration.Value is null ||
            migration.Value.Migrations.All(item => item.Version != "046") ||
            migration.Value.Migrations.All(item => item.Version != "047") ||
            migration.Value.Migrations.All(item => item.Version != "048"))
        {
            throw new InvalidOperationException($"M8 migrations 046-048 are unavailable: {migration.Error.Code}.");
        }

        var staticData = new MariaDbStaticDataLoader(configuration.Value.Database);
        var loaded = await staticData.LoadAsync(CancellationToken.None);
        if (!loaded.Succeeded || loaded.Value is null)
        {
            throw new InvalidOperationException($"M8 formal MariaDB catalog load failed: {loaded.Error.Code}.");
        }

        var formalBuilt = await staticData.BuildAsync(loaded.Value, CancellationToken.None);
        if (!formalBuilt.Succeeded)
        {
            throw new InvalidOperationException($"M8 formal runtime catalog build failed: {formalBuilt.Error.Code}.");
        }

        var promotedRuntime = new MariaDbPromotedGameplayContentRuntime(configuration.Value.Database, staticData);
        var promotedBuilt = await promotedRuntime.BuildAsync(loaded.Value, CancellationToken.None);
        if (!promotedBuilt.Succeeded)
        {
            throw new InvalidOperationException($"M8 promoted runtime catalog build failed: {promotedBuilt.Error.Code}.");
        }

        var referential = await new MariaDbStaticDataValidator(configuration.Value.Database).ValidateAsync(CancellationToken.None);
        if (!referential.Succeeded)
        {
            throw new InvalidOperationException($"M8 formal referential integrity failed: {referential.Error.Code}.");
        }

        var snapshot = promotedRuntime.PublishedSnapshot;
        var questLookupPass = snapshot.QuestProfiles.Count > 0 && snapshot.QuestProfiles.Keys.All(questId =>
        {
            var resolved = promotedRuntime.ResolveQuest(questId);
            return resolved.Succeeded && resolved.Value is { } value && value.QuestId == questId &&
                !string.IsNullOrWhiteSpace(value.NameZhTw) && value.ContentReleaseId == snapshot.ReleaseId;
        });
        var equipmentLookupPass = snapshot.EquipmentSets.Count > 0 && snapshot.EquipmentSetMembers.Count > 0 &&
            snapshot.EquipmentSets.Keys.All(setId =>
            {
                var resolved = promotedRuntime.ResolveEquipmentSet(setId);
                return resolved.Succeeded && resolved.Value is { } value && value.SetId == setId &&
                    value.Members.Count >= value.RequiredPieces && value.ContentReleaseId == snapshot.ReleaseId;
            });
        var petInnateLookupPass = snapshot.PetInnates.Count > 0 && snapshot.PetInnates.Keys.All(innateId =>
        {
            var resolved = promotedRuntime.ResolvePetInnate(innateId);
            return resolved.Succeeded && resolved.Value is { } value && value.InnateId == innateId &&
                !string.IsNullOrWhiteSpace(value.NameZhTw) && value.ContentReleaseId == snapshot.ReleaseId;
        });
        if (!questLookupPass || !equipmentLookupPass || !petInnateLookupPass)
        {
            throw new InvalidOperationException("M8 headless promoted-content lookup failed closed.");
        }

        var result = new
        {
            schemaVersion,
            status = snapshot.EvidenceBlockedFamilies.Count == 0 ? "PASS" : "PASS WITH DOCUMENTED EVIDENCE GAPS",
            authority = "MariaDB",
            repositoryActuallyUsed = nameof(MariaDbPromotedGameplayContentRuntime),
            release = new
            {
                snapshot.ReleaseId,
                snapshot.SourceRunId,
                formalCatalogManifestVersion = FormalRuntimeCatalogManifestBuilder.Version,
                snapshot.FormalCatalogFingerprint,
                snapshot.FormalCatalogRecordCount,
                status = "Active"
            },
            runtime = new
            {
                formalCatalog = "PASS",
                promotedCatalog = "PASS",
                referentialIntegrity = "PASS",
                questProfiles = snapshot.QuestProfiles.Count,
                questObjectives = snapshot.QuestObjectives.Count,
                equipmentSets = snapshot.EquipmentSets.Count,
                equipmentSetMembers = snapshot.EquipmentSetMembers.Count,
                petInnates = snapshot.PetInnates.Count
            },
            headless = new
            {
                questIdentityLookup = questLookupPass ? "PASS" : "FAIL",
                equipmentSetMemberLookup = equipmentLookupPass ? "PASS" : "FAIL",
                petInnateIdentityLookup = petInnateLookupPass ? "PASS" : "FAIL",
                status = questLookupPass && equipmentLookupPass && petInnateLookupPass ? "PASS" : "FAIL"
            },
            productionCoverage = snapshot.ProductionCoverage,
            evidenceBlockedFamilies = snapshot.EvidenceBlockedFamilies,
            officialClientFamilyExpansion = "BLOCKED_BY_M2_TO_M7_WIRE_EVIDENCE",
            productionMutationAllowed = false,
            manualOperation = false,
            newCapture = false,
            secretRetained = false,
            connectionStringRetained = false,
            networkBytesEmitted = false,
            fakeNetworkBytes = false
        };
        WriteJsonArtifact(result, options.OutputPath, options.BaseDirectory);
        return 0;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException or IOException)
    {
        WriteJsonArtifact(new
        {
            schemaVersion,
            status = "BLOCKED",
            reason = "mariadb.m8_content_expansion_validation_failed",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false,
            networkBytesEmitted = false,
            fakeNetworkBytes = false
        }, options.OutputPath, options.BaseDirectory);
        return 52;
    }
}

static async Task<AutomationProbeResult> MigrationVerifyAsync(AutomationProbeOptions options)
{
    var root = Path.GetFullPath(options.BaseDirectory);
    var paths = new AppPathProvider(root);
    var configuration = new JsonServerConfigurationLoader(paths).Load();
    if (!configuration.Succeeded || configuration.Value is null)
    {
        return AutomationProbeResult.Blocked(
            "Migration Verify",
            configuration.Error.Code,
            "Configuration",
            "config/database.json",
            "Readable database configuration.",
            configuration.Error.Message,
            "Correct the configuration before retrying migrations.");
    }

    var verified = await new SqlFileMigrationRunner(
        configuration.Value.Database,
        paths.DatabaseSchemaDirectory).VerifyAsync(CancellationToken.None);
    return verified.Succeeded
        ? new AutomationProbeResult(
            "PASS",
            [ProbeCheck.Pass(
                "Migration Verify",
                "Schema Migration",
                paths.DatabaseSchemaDirectory,
                "All migrations apply with checksum integrity.",
                "schema current")],
            [],
            [],
            null,
            verified.Value?.ConsoleLines ?? [])
        : AutomationProbeResult.Blocked(
            "Migration Verify",
            verified.Error.Code,
            "Schema Migration",
            verified.Error.Source,
            "All migrations apply with checksum integrity.",
            Redact(verified.Error.Message, options.PasswordEnvironmentVariable),
            "Correct the failing migration with a new version; do not rewrite an applied migration.");
}

static async Task<int> RunM7CharacterLifecyclePhysicalFixtureAsync(AutomationProbeOptions options)
{
    var root = Path.GetFullPath(options.BaseDirectory);
    var configuration = new JsonServerConfigurationLoader(new AppPathProvider(root)).Load();
    if (!configuration.Succeeded || configuration.Value is null)
    {
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m7-character-lifecycle-physical-fixture-v2",
            status = "BLOCKED",
            reason = configuration.Error.Code,
            secretRetained = false,
            connectionStringRetained = false
        }, options.OutputPath, options.BaseDirectory);
        return 51;
    }

    var database = configuration.Value.Database;
    var fixtureIdentity = Guid.NewGuid().ToString("N");
    var ownerLogin = $"fixture_m7_owner_{fixtureIdentity[..10]}";
    var foreignLogin = $"fixture_m7_foreign_{fixtureIdentity[..10]}";
    var firstName = $"M7A{fixtureIdentity[..7]}";
    var secondName = $"M7B{fixtureIdentity[..7]}";
    var renamedName = $"M7R{fixtureIdentity[..7]}";
    long? ownerAccountId = null;
    long? foreignAccountId = null;
    long? characterId = null;
    var cleanupStatus = "NOT_RUN";
    var fixtureStatus = "FAILED";
    var failureCode = "fixture.not_run";
    var failureLocation = "NONE";
    var fixtureStage = "configuration";
    var creationProfiles = 0;
    var productionCreationProfiles = 0;
    var migration044Current = false;
    var migration045Current = false;
    var unresolvedProfileFailsClosed = false;
    var concurrentCreateWinners = 0;
    var concurrentCreateLimitRejections = 0;
    var wrongOwnerRenameRejected = false;
    var ownerRenameSucceeded = false;
    var duplicateNameRolledBack = false;
    var wrongOwnerDeleteRejected = false;
    var ownerDeleteSucceeded = false;
    var deletedCharacterHidden = false;
    var repeatedDeleteRejected = false;
    var durableCreateReplaySucceeded = false;
    var durableReplayConflictRejected = false;
    var durableRenameReplaySucceeded = false;
    var durableDeleteReplaySucceeded = false;
    var deletedNameReusable = false;
    try
    {
        fixtureStage = "inspect-creation-authority";
        int mapId;
        await using (var connection = new MySqlConnection(BuildConfiguredDatabaseConnectionString(database)))
        {
            await connection.OpenAsync();
            await using (var migration = connection.CreateCommand())
            {
                var migrationPath = Path.Combine(root, "database", "schema", "044_character_creation_authority.sql");
                var expectedChecksum = SqlMigrationFile.ComputeChecksum(
                    await File.ReadAllTextAsync(migrationPath, Encoding.UTF8));
                migration.CommandText = """
                    SELECT `Name`, `Checksum`
                    FROM `__SchemaVersion`
                    WHERE `Version` = '044'
                    LIMIT 1;
                    """;
                await using var reader = await migration.ExecuteReaderAsync();
                migration044Current = await reader.ReadAsync() &&
                    string.Equals(reader.GetString(0), "044_character_creation_authority.sql", StringComparison.Ordinal) &&
                    !reader.IsDBNull(1) &&
                    string.Equals(reader.GetString(1), expectedChecksum, StringComparison.OrdinalIgnoreCase);
            }

            await using (var migration = connection.CreateCommand())
            {
                var migrationPath = Path.Combine(root, "database", "schema", "045_character_lifecycle_integrity.sql");
                var expectedChecksum = SqlMigrationFile.ComputeChecksum(
                    await File.ReadAllTextAsync(migrationPath, Encoding.UTF8));
                migration.CommandText = """
                    SELECT `Name`, `Checksum`
                    FROM `__SchemaVersion`
                    WHERE `Version` = '045'
                    LIMIT 1;
                    """;
                await using var reader = await migration.ExecuteReaderAsync();
                migration045Current = await reader.ReadAsync() &&
                    string.Equals(reader.GetString(0), "045_character_lifecycle_integrity.sql", StringComparison.Ordinal) &&
                    !reader.IsDBNull(1) &&
                    string.Equals(reader.GetString(1), expectedChecksum, StringComparison.OrdinalIgnoreCase);
            }

            await using (var profiles = connection.CreateCommand())
            {
                profiles.CommandText = """
                    SELECT COUNT(*),
                           SUM(CASE WHEN `enabled` = 1 THEN 1 ELSE 0 END)
                    FROM `god2_game`.`character_creation_profiles`;
                    """;
                await using var reader = await profiles.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    throw new InvalidOperationException("Character creation profile inventory returned no aggregate row.");
                }

                creationProfiles = Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
                productionCreationProfiles = reader.IsDBNull(1)
                    ? 0
                    : Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
            }

            await using (var map = connection.CreateCommand())
            {
                map.CommandText = """
                    SELECT map.`Id`
                    FROM `maps` map
                    JOIN `client_map_identities` identity ON identity.`MapId` = map.`Id`
                    WHERE identity.`ClientBuildId` = @clientBuildId
                      AND identity.`ProductionEnabled` = 1
                      AND map.`Width` > 0 AND map.`Height` > 0
                    ORDER BY identity.`ClientMapId`
                    LIMIT 1;
                    """;
                map.Parameters.AddWithValue("@clientBuildId", OfficialServerSelectionWireCodec.ClientBuildId);
                var value = await map.ExecuteScalarAsync();
                mapId = value is null
                    ? throw new InvalidOperationException("No production-enabled Client map identity is available to isolate the repository fixture.")
                    : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            fixtureStage = "create-fixture-accounts";
            foreach (var login in new[] { ownerLogin, foreignLogin })
            {
                await using var account = connection.CreateCommand();
                account.CommandText = """
                    INSERT INTO `accounts`
                        (`LoginName`, `PasswordHash`, `Status`, `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`)
                    VALUES
                        (@loginName, 'fixture-hash-v1', 'Active', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @token);
                    """;
                account.Parameters.AddWithValue("@loginName", login);
                account.Parameters.AddWithValue("@token", fixtureIdentity);
                await account.ExecuteNonQueryAsync();
                if (string.Equals(login, ownerLogin, StringComparison.Ordinal))
                {
                    ownerAccountId = account.LastInsertedId;
                }
                else
                {
                    foreignAccountId = account.LastInsertedId;
                }
            }
        }

        var creationAuthority = new MariaDbCharacterCreationAuthority(
            database,
            OfficialServerSelectionWireCodec.ClientBuildId);
        var unresolved = await creationAuthority.ResolveAsync(
            new CharacterCreateRequest(
                "FixtureProbe",
                "__fixture_no_class__",
                "__fixture_no_gender__",
                "__fixture_no_life_skill__",
                "__fixture_no_appearance__",
                fixtureIdentity),
            CancellationToken.None);
        unresolvedProfileFailsClosed = !unresolved.Succeeded &&
            unresolved.Error.Code == "character.creation_profile_evidence_blocked";

        if (ownerAccountId is not long ownerId || foreignAccountId is not long foreignId)
        {
            throw new InvalidOperationException("The isolated fixture accounts were not created.");
        }

        fixtureStage = "concurrent-create";
        var repository = new MariaDbCharacterRepository(database);
        var concurrentCreates = await Task.WhenAll(
            repository.CreateAsync(
                ownerId,
                new CharacterCreateRequest(firstName, "Class1", "Gender1", "LifeSkill1", "Fixture", $"{fixtureIdentity}:a"),
                mapId,
                0,
                0,
                1,
                CancellationToken.None),
            repository.CreateAsync(
                ownerId,
                new CharacterCreateRequest(secondName, "Class1", "Gender1", "LifeSkill1", "Fixture", $"{fixtureIdentity}:b"),
                mapId,
                0,
                0,
                1,
                CancellationToken.None));
        concurrentCreateWinners = concurrentCreates.Count(result => result.Succeeded);
        concurrentCreateLimitRejections = concurrentCreates.Count(result =>
            !result.Succeeded && result.Error.Code == "character.limit_reached");
        var winner = concurrentCreates.Single(result => result.Succeeded).Value!;
        var createdCharacterId = winner.CharacterId;
        characterId = createdCharacterId;
        var winnerRequestId = winner.Name == firstName ? $"{fixtureIdentity}:a" : $"{fixtureIdentity}:b";
        var winnerRequest = new CharacterCreateRequest(
            winner.Name,
            "Class1",
            "Gender1",
            "LifeSkill1",
            "Fixture",
            winnerRequestId);
        var replayRepository = new MariaDbCharacterRepository(database);
        var createReplay = await replayRepository.CreateAsync(
            ownerId,
            winnerRequest,
            mapId,
            0,
            0,
            1,
            CancellationToken.None);
        durableCreateReplaySucceeded = createReplay.Succeeded &&
            createReplay.Value?.CharacterId == createdCharacterId;
        var replayConflict = await replayRepository.CreateAsync(
            ownerId,
            winnerRequest with { Name = winner.Name == firstName ? secondName : firstName },
            mapId,
            0,
            0,
            1,
            CancellationToken.None);
        durableReplayConflictRejected = !replayConflict.Succeeded &&
            replayConflict.Error.Code == "character.replay_conflict";

        fixtureStage = "ownership-and-rename";
        var wrongRename = await repository.RenameAsync(
            foreignId,
            createdCharacterId,
            renamedName,
            $"{fixtureIdentity}:wrong-rename",
            CancellationToken.None);
        wrongOwnerRenameRejected = !wrongRename.Succeeded && wrongRename.Error.Code == "character.ownership_rejected";
        var rename = await repository.RenameAsync(
            ownerId,
            createdCharacterId,
            renamedName,
            $"{fixtureIdentity}:rename",
            CancellationToken.None);
        ownerRenameSucceeded = rename.Succeeded && rename.Value?.Name == renamedName;
        var renameReplay = await replayRepository.RenameAsync(
            ownerId,
            createdCharacterId,
            renamedName,
            $"{fixtureIdentity}:rename",
            CancellationToken.None);
        durableRenameReplaySucceeded = renameReplay.Succeeded && renameReplay.Value?.Name == renamedName;

        fixtureStage = "rollback-on-duplicate-name";
        var duplicate = await repository.CreateAsync(
            foreignId,
            new CharacterCreateRequest(renamedName, "Class1", "Gender1", "LifeSkill1", "Fixture", $"{fixtureIdentity}:duplicate"),
            mapId,
            0,
            0,
            1,
            CancellationToken.None);
        duplicateNameRolledBack = !duplicate.Succeeded &&
            duplicate.Error.Code == "character.name_duplicate" &&
            (await repository.ListByAccountAsync(foreignId, CancellationToken.None)).Count == 0;

        fixtureStage = "ownership-and-delete";
        var wrongDelete = await repository.DeleteAsync(
            foreignId,
            createdCharacterId,
            $"{fixtureIdentity}:wrong-delete",
            CancellationToken.None);
        wrongOwnerDeleteRejected = !wrongDelete.Succeeded && wrongDelete.Error.Code == "character.ownership_rejected";
        var delete = await repository.DeleteAsync(
            ownerId,
            createdCharacterId,
            $"{fixtureIdentity}:delete",
            CancellationToken.None);
        ownerDeleteSucceeded = delete.Succeeded &&
            string.Equals(delete.Value?.Status, "Deleted", StringComparison.Ordinal);
        var deleteReplay = await replayRepository.DeleteAsync(
            ownerId,
            createdCharacterId,
            $"{fixtureIdentity}:delete",
            CancellationToken.None);
        durableDeleteReplaySucceeded = deleteReplay.Succeeded &&
            deleteReplay.Value?.CharacterId == createdCharacterId &&
            string.Equals(deleteReplay.Value.Status, "Deleted", StringComparison.Ordinal);
        deletedCharacterHidden = (await repository.ListByAccountAsync(ownerId, CancellationToken.None)).Count == 0 &&
            await repository.FindByIdAsync(createdCharacterId, CancellationToken.None) is null;
        var repeatedDelete = await repository.DeleteAsync(
            ownerId,
            createdCharacterId,
            $"{fixtureIdentity}:repeated-delete",
            CancellationToken.None);
        repeatedDeleteRejected = !repeatedDelete.Succeeded && repeatedDelete.Error.Code == "character.not_found";

        var reusedName = await replayRepository.CreateAsync(
            ownerId,
            new CharacterCreateRequest(renamedName, "Class1", "Gender1", "LifeSkill1", "Fixture", $"{fixtureIdentity}:reuse-name"),
            mapId,
            0,
            0,
            1,
            CancellationToken.None);
        deletedNameReusable = reusedName.Succeeded && reusedName.Value?.Name == renamedName;

        var passed = migration044Current &&
            migration045Current &&
            unresolvedProfileFailsClosed &&
            concurrentCreateWinners == 1 &&
            concurrentCreateLimitRejections == 1 &&
            wrongOwnerRenameRejected &&
            ownerRenameSucceeded &&
            duplicateNameRolledBack &&
            wrongOwnerDeleteRejected &&
            ownerDeleteSucceeded &&
            deletedCharacterHidden &&
            repeatedDeleteRejected &&
            durableCreateReplaySucceeded &&
            durableReplayConflictRejected &&
            durableRenameReplaySucceeded &&
            durableDeleteReplaySucceeded &&
            deletedNameReusable;
        fixtureStatus = passed ? "PASS" : "FAILED";
        failureCode = passed ? "NONE" : "character_lifecycle.fixture_assertion_failed";
        fixtureStage = "completed";
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        fixtureStatus = "FAILED";
        failureCode = $"character_lifecycle.fixture_exception.{fixtureStage}.{exception.GetType().Name}";
        failureLocation = exception.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? exception.TargetSite?.Name ?? "UNKNOWN";
    }
    finally
    {
        try
        {
            await using var connection = new MySqlConnection(BuildConfiguredDatabaseConnectionString(database));
            await connection.OpenAsync();
            await using var cleanup = await connection.BeginTransactionAsync();
            foreach (var account in new[]
                     {
                         (Id: ownerAccountId, Login: ownerLogin),
                         (Id: foreignAccountId, Login: foreignLogin)
                     })
            {
                if (account.Id is null)
                {
                    continue;
                }

                await using (var deleteCharacters = connection.CreateCommand())
                {
                    deleteCharacters.Transaction = cleanup;
                    deleteCharacters.CommandText = "DELETE FROM `characters` WHERE `AccountId` = @accountId;";
                    deleteCharacters.Parameters.AddWithValue("@accountId", account.Id.Value);
                    await deleteCharacters.ExecuteNonQueryAsync();
                }

                await using var deleteAccount = connection.CreateCommand();
                deleteAccount.Transaction = cleanup;
                deleteAccount.CommandText = "DELETE FROM `accounts` WHERE `Id` = @accountId AND `LoginName` = @loginName;";
                deleteAccount.Parameters.AddWithValue("@accountId", account.Id.Value);
                deleteAccount.Parameters.AddWithValue("@loginName", account.Login);
                if (await deleteAccount.ExecuteNonQueryAsync() != 1)
                {
                    throw new InvalidOperationException("Fixture account cleanup did not match exactly one scoped row.");
                }
            }

            await cleanup.CommitAsync();
            cleanupStatus = "PASS";
        }
        catch
        {
            cleanupStatus = "FAILED";
        }
    }

    if (!string.Equals(cleanupStatus, "PASS", StringComparison.Ordinal))
    {
        fixtureStatus = "FAILED";
        failureCode = "character_lifecycle.fixture_cleanup_failed";
    }

    WriteJsonArtifact(new
    {
        schemaVersion = "god2-roadmap-m7-character-lifecycle-physical-fixture-v2",
        status = fixtureStatus,
        failureCode,
        failureLocation,
        fixtureStage,
        authority = "MariaDB",
        repositoryActuallyUsed = nameof(MariaDbCharacterRepository),
        creationAuthorityActuallyUsed = nameof(MariaDbCharacterCreationAuthority),
        migration044Current,
        migration045Current,
        fixtureNamespace = "ephemeral-two-account-character-by-scoped-identity",
        fixtureCleanup = cleanupStatus,
        creationProfiles,
        productionCreationProfiles,
        unresolvedProfileFailsClosed,
        concurrentCreateWinners,
        concurrentCreateLimitRejections,
        wrongOwnerRenameRejected,
        ownerRenameSucceeded,
        duplicateNameRolledBack,
        wrongOwnerDeleteRejected,
        ownerDeleteSucceeded,
        deletedCharacterHidden,
        repeatedDeleteRejected,
        durableCreateReplaySucceeded,
        durableReplayConflictRejected,
        durableRenameReplaySucceeded,
        durableDeleteReplaySucceeded,
        deletedNameReusable,
        existingPlayerRowsTouched = false,
        secretRetained = false,
        connectionStringRetained = false,
        networkBytesEmitted = false,
        fakeNetworkBytes = false
    }, options.OutputPath, options.BaseDirectory);
    return string.Equals(fixtureStatus, "PASS", StringComparison.Ordinal) ? 0 : 2;
}

static async Task<int> RunMapIdentityInventoryAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJson(new
        {
            schemaVersion = "god2-map-identity-inventory-v1",
            status = "BLOCKED",
            reason = "mariadb.password_missing",
            secretRetained = false,
            connectionStringRetained = false,
            maps = Array.Empty<MapIdentityInventoryRow>()
        });
        return 51;
    }

    try
    {
        await using var connection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `Id`, `Code`, `Name`, `Width`, `Height`, NULL AS `OriginalName`, `NameZhTw`,
                   'formal-runtime-map' AS `EvidenceStatus`, `ContentRecoveryRunId`, '' AS `SourceReference`,
                   '' AS `PayloadSha256`, NULL AS `PayloadJson`
            FROM `maps`
            ORDER BY `Id`;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var maps = new List<MapIdentityInventoryRow>();
        while (await reader.ReadAsync())
        {
            maps.Add(new MapIdentityInventoryRow(
                reader.GetInt32("Id"),
                reader.GetString("Code"),
                reader.GetString("Name"),
                reader.IsDBNull(reader.GetOrdinal("Width")) ? null : reader.GetInt32("Width"),
                reader.IsDBNull(reader.GetOrdinal("Height")) ? null : reader.GetInt32("Height"),
                reader.IsDBNull(reader.GetOrdinal("OriginalName")) ? null : reader.GetString("OriginalName"),
                reader.IsDBNull(reader.GetOrdinal("NameZhTw")) ? null : reader.GetString("NameZhTw"),
                reader.GetString("EvidenceStatus"),
                reader.IsDBNull(reader.GetOrdinal("ContentRecoveryRunId"))
                    ? null
                    : Convert.ToString(reader.GetValue(reader.GetOrdinal("ContentRecoveryRunId")), System.Globalization.CultureInfo.InvariantCulture),
                reader.GetString("SourceReference"),
                reader.GetString("PayloadSha256"),
                MapPayloadIdentity.Parse(reader.IsDBNull(reader.GetOrdinal("PayloadJson")) ? null : reader.GetString("PayloadJson"))));
        }

        await reader.DisposeAsync();
        await using var identityCommand = connection.CreateCommand();
        identityCommand.CommandText = """
            SELECT `MapId`, `ClientBuildId`, `ClientMapId`, `ClientAreaId`, `ResourceIdentity`,
                   `CoordinateScaleX`, `CoordinateScaleY`, `CoordinateOffsetX`, `CoordinateOffsetY`,
                   'formal-runtime-identity' AS `IdentityEvidenceStatus`,
                   'formal-runtime-coordinate' AS `CoordinateEvidenceStatus`, `ProductionEnabled`,
                   COALESCE(source.`SourceType`,'') AS `SourceType`, COALESCE(source.`SourceHash`,'') AS `SourceHash`, '' AS `EvidenceReference`
            FROM `client_map_identities` identity
            LEFT JOIN `god2_research`.`world_identity_source_archive` source
              ON source.`FormalTable`='client_map_identities'
             AND source.`RecordIdentity`=CONCAT(CAST(identity.`MapId` AS CHAR), ':', identity.`ClientBuildId`)
            ORDER BY `MapId`, `ClientBuildId`;
            """;
        await using var identityReader = await identityCommand.ExecuteReaderAsync();
        var clientMapIdentities = new List<ClientMapIdentityInventoryRow>();
        while (await identityReader.ReadAsync())
        {
            clientMapIdentities.Add(new ClientMapIdentityInventoryRow(
                identityReader.GetInt32("MapId"),
                identityReader.GetString("ClientBuildId"),
                checked((ushort)identityReader.GetInt32("ClientMapId")),
                checked((byte)identityReader.GetInt32("ClientAreaId")),
                identityReader.GetString("ResourceIdentity"),
                identityReader.GetDecimal("CoordinateScaleX"),
                identityReader.GetDecimal("CoordinateScaleY"),
                identityReader.GetDecimal("CoordinateOffsetX"),
                identityReader.GetDecimal("CoordinateOffsetY"),
                identityReader.GetString("IdentityEvidenceStatus"),
                identityReader.GetString("CoordinateEvidenceStatus"),
                identityReader.GetBoolean("ProductionEnabled"),
                identityReader.GetString("SourceType"),
                identityReader.GetString("SourceHash"),
                identityReader.GetString("EvidenceReference")));
        }

        await identityReader.DisposeAsync();
        await using var recoveryCommand = connection.CreateCommand();
        recoveryCommand.CommandText = """
            SELECT `RunId`, `AuthorityKey`,
                   JSON_UNQUOTE(JSON_EXTRACT(`NormalizedData`, '$.resourceKey')) AS `ResourceKey`,
                   CAST(JSON_UNQUOTE(JSON_EXTRACT(`NormalizedData`, '$.width')) AS SIGNED) AS `Width`,
                   CAST(JSON_UNQUOTE(JSON_EXTRACT(`NormalizedData`, '$.height')) AS SIGNED) AS `Height`,
                   `SourceHash`, `NormalizedHash`, `EvidenceStatus`, `LocalizationStatus`
            FROM `god2_research`.`content_validated_records`
            WHERE `Domain` = 'Map'
            ORDER BY `RunId`, `AuthorityKey`;
            """;
        await using var recoveryReader = await recoveryCommand.ExecuteReaderAsync();
        var recoveredMapEvidence = new List<RecoveredMapEvidenceInventoryRow>();
        while (await recoveryReader.ReadAsync())
        {
            recoveredMapEvidence.Add(new RecoveredMapEvidenceInventoryRow(
                Convert.ToString(
                    recoveryReader.GetValue(recoveryReader.GetOrdinal("RunId")),
                    System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                recoveryReader.GetString("AuthorityKey"),
                recoveryReader.IsDBNull(recoveryReader.GetOrdinal("ResourceKey")) ? null : recoveryReader.GetString("ResourceKey"),
                recoveryReader.IsDBNull(recoveryReader.GetOrdinal("Width")) ? null : recoveryReader.GetInt32("Width"),
                recoveryReader.IsDBNull(recoveryReader.GetOrdinal("Height")) ? null : recoveryReader.GetInt32("Height"),
                recoveryReader.GetString("SourceHash"),
                recoveryReader.GetString("NormalizedHash"),
                recoveryReader.GetString("EvidenceStatus"),
                recoveryReader.GetString("LocalizationStatus")));
        }

        await recoveryReader.DisposeAsync();
        await using var characterMapCommand = connection.CreateCommand();
        characterMapCommand.CommandText = """
            SELECT `MapId`, COUNT(*) AS `CharacterCount`
            FROM `characters`
            GROUP BY `MapId`
            ORDER BY `MapId`;
            """;
        await using var characterMapReader = await characterMapCommand.ExecuteReaderAsync();
        var characterMapUsage = new List<CharacterMapUsageInventoryRow>();
        while (await characterMapReader.ReadAsync())
        {
            characterMapUsage.Add(new CharacterMapUsageInventoryRow(
                characterMapReader.GetInt32("MapId"),
                checked((int)characterMapReader.GetInt64("CharacterCount"))));
        }

        WriteJson(new
        {
            schemaVersion = "god2-map-identity-inventory-v1",
            status = "PASS",
            authority = "MariaDB.maps",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false,
            rowCount = maps.Count,
            maps,
            clientMapIdentityRowCount = clientMapIdentities.Count,
            clientMapIdentities,
            recoveredMapEvidenceRowCount = recoveredMapEvidence.Count,
            recoveredMapEvidence,
            characterMapUsage
        });
        return 0;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        WriteJson(new
        {
            schemaVersion = "god2-map-identity-inventory-v1",
            status = "BLOCKED",
            reason = "mariadb.map_identity_inventory_failed",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            secretRetained = false,
            connectionStringRetained = false,
            maps = Array.Empty<MapIdentityInventoryRow>()
        });
        return 52;
    }
}

static async Task<int> RunM2ContentSliceInventoryAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-content-slice-inventory-v1",
            status = "BLOCKED",
            reason = "mariadb.password_missing",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 51;
    }

    try
    {
        await using var connection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await connection.OpenAsync();
        var runs = await QueryRowsAsync(connection, """
            SELECT `RunId`, `Phase`, `Status`, `ExtractorVersion`, `StartedAtUtc`, `CompletedAtUtc`
            FROM `content_recovery_runs`
            WHERE `Phase` IN ('GameplayContentRecoveryPhase2','GameplayContentRecoveryPhase3')
            ORDER BY `StartedAtUtc` DESC;
            """);
        var mapIdentities = await QueryRowsAsync(connection, """
            SELECT identity.`MapId`, map.`Code` AS `MapCode`, map.`NameZhTw`, map.`Width`, map.`Height`,
                   identity.`ClientMapId`, identity.`ClientAreaId`, identity.`ResourceIdentity`,
                   'formal-runtime-identity' AS `IdentityEvidenceStatus`,
                   'formal-runtime-coordinate' AS `CoordinateEvidenceStatus`, identity.`ProductionEnabled`,
                   COALESCE(source.`SourceType`,'') AS `SourceType`, COALESCE(source.`SourceHash`,'') AS `SourceHash`, '' AS `EvidenceReference`
            FROM `client_map_identities` identity
            JOIN `maps` map ON map.`Id`=identity.`MapId`
            LEFT JOIN `god2_research`.`world_identity_source_archive` source
              ON source.`FormalTable`='client_map_identities'
             AND source.`RecordIdentity`=CONCAT(CAST(identity.`MapId` AS CHAR), ':', identity.`ClientBuildId`)
            WHERE identity.`ClientBuildId`='god2-opt-6b127086e0c0'
              AND identity.`ClientMapId` IN (3,19)
            ORDER BY identity.`ClientMapId`;
            """);
        var evidenceShape = await QueryRowsAsync(connection, """
            SELECT 'Validated' AS `Source`, `Domain`, '' AS `FieldName`, COUNT(*) AS `RowCount`
            FROM `god2_research`.`content_validated_records`
            GROUP BY `Domain`
            UNION ALL
            SELECT 'FieldEvidence' AS `Source`, `Domain`, `FieldName`, COUNT(*) AS `RowCount`
            FROM `god2_research`.`content_field_evidence`
            GROUP BY `Domain`, `FieldName`
            UNION ALL
            SELECT 'ProductionManifest' AS `Source`, `Domain`, '' AS `FieldName`, COUNT(*) AS `RowCount`
            FROM `god2_research`.`content_production_manifest`
            GROUP BY `Domain`
            ORDER BY `Source`, `Domain`, `FieldName`;
            """);
        var npcCoordinateEvidence = await QueryRowsAsync(connection, """
            SELECT evidence.`CoordinateEvidenceId`, evidence.`RunId`, evidence.`NpcId`, evidence.`ClientNpcId`,
                   evidence.`NpcNameZhTw`, evidence.`MapId`, evidence.`MapNameZhTw`,
                   evidence.`PositionX`, evidence.`PositionY`, evidence.`Direction`,
                   evidence.`CoordinateEvidenceStatus`, evidence.`IdentityEvidenceStatus`,
                   evidence.`BoundsValidationStatus`, evidence.`ProductionSpawnEnabled`,
                   evidence.`SourceType`, evidence.`SourceFile`, evidence.`SourceIdentity`,
                   evidence.`SourceHash`, evidence.`Confidence`,
                   npc.`Code` AS `NpcCode`, npc.`NameZhTw` AS `FormalNpcNameZhTw`,
                   npc.`NpcType`, npc.`InteractionFamily`, npc.`OfficialIdentityStatus`,
                   map.`Code` AS `MapCode`, map.`NameZhTw` AS `FormalMapNameZhTw`,
                   identity.`ClientMapId`, identity.`ClientAreaId`, identity.`ResourceIdentity`
            FROM `god2_research`.`npc_coordinate_evidence` evidence
            LEFT JOIN `npcs` npc ON npc.`Id`=evidence.`NpcId`
            LEFT JOIN `maps` map ON map.`Id`=evidence.`MapId`
            LEFT JOIN `client_map_identities` identity
              ON identity.`MapId`=evidence.`MapId`
             AND identity.`ClientBuildId`='god2-opt-6b127086e0c0'
            ORDER BY
              CASE evidence.`CoordinateEvidenceStatus`
                WHEN 'Verified' THEN 0 WHEN 'Derived' THEN 1 WHEN 'Candidate' THEN 2 ELSE 3 END,
              CASE evidence.`Confidence` WHEN 'High' THEN 0 WHEN 'Medium' THEN 1 ELSE 2 END,
              evidence.`NpcNameZhTw`, evidence.`CoordinateEvidenceId`;
            """);
        var npcFieldEvidence = await QueryRowsAsync(connection, """
            SELECT `EvidenceId`, `RunId`, `AuthorityKey`, `FieldName`, `ValueJson`, `EvidenceState`,
                   `SourceType`, `SourceFile`, `SourceIdentity`, `SourceHash`, `Confidence`, `Reason`
            FROM `god2_research`.`content_field_evidence`
            WHERE LOWER(`Domain`) LIKE '%npc%'
            ORDER BY `AuthorityKey`, `FieldName`, `EvidenceState`, `EvidenceId`;
            """);
        var npcValidatedRecords = await QueryRowsAsync(connection, """
            SELECT validated.`RunId`, validated.`AuthorityKey`, validated.`NormalizedData`,
                   validated.`NormalizedHash`, validated.`EvidenceStatus`, validated.`SourceHash`,
                   manifest.`TargetRowIdentity`, manifest.`EvidenceStatus` AS `ProductionEvidenceStatus`
            FROM `god2_research`.`content_validated_records` validated
            LEFT JOIN `god2_research`.`content_production_manifest` manifest
              ON LOWER(manifest.`Domain`) LIKE '%npc%' AND manifest.`AuthorityKey`=validated.`AuthorityKey`
            WHERE LOWER(validated.`Domain`) LIKE '%npc%'
            ORDER BY validated.`RunId`, validated.`AuthorityKey`;
            """);
        var monsterProfiles = await QueryRowsAsync(connection, """
            SELECT monster.`Id`, monster.`Code`, monster.`NameZhTw`, monster.`Level`, monster.`MaxHp`, monster.`MaxMp`,
                   monster.`MpPolicy`, monster.`Attack`, monster.`Defense`, monster.`ExperienceReward`,
                   monster.`CurrencyRewardMinimum`, monster.`CurrencyRewardMaximum`, monster.`DropPolicy`,
                   monster.`EvidenceStatus`, monster.`ContentRecoveryRunId`,
                   semantic.`RunId` AS `SemanticRunId`,
                   CASE WHEN semantic.`Level` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END AS `LevelEvidenceStatus`,
                   semantic.`HpPolicy`,
                   semantic.`MpPolicy` AS `SemanticMpPolicy`, semantic.`PhysicalAttack`, semantic.`MagicAttack`,
                   semantic.`PhysicalDefense`, semantic.`MagicDefense`, semantic.`Speed`, semantic.`Initiative`,
                   semantic.`Accuracy`, semantic.`Evasion`, semantic.`CriticalRate`, semantic.`Element`,
                   semantic.`Race`, semantic.`AiFamily`,
                   CASE WHEN semantic.`PhysicalAttack` IS NOT NULL AND semantic.`PhysicalDefense` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END AS `StatEvidenceStatus`,
                   CASE WHEN semantic.`ExperienceReward` IS NOT NULL AND semantic.`CurrencyMinimum` IS NOT NULL AND semantic.`CurrencyMaximum` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END AS `RewardEvidenceStatus`,
                   semantic.`DropPolicyStatus`, semantic.`ProductionEnabled`,
                   spawn.`MapId`, spawn.`AreaId`, spawn.`PositionX`, spawn.`PositionY`, spawn.`Direction`,
                   spawn.`SpawnGroup`, spawn.`EncounterGroup`, spawn.`FormationGroup`, spawn.`MinCount`,
                   spawn.`MaxCount`, spawn.`RespawnSeconds`, spawn.`EncounterRadius`, spawn.`SpawnCondition`,
                   spawn.`MapRelationshipStatus`, spawn.`CoordinateEvidenceStatus`, spawn.`RespawnPolicy`,
                   spawn.`EvidenceStatus` AS `SpawnEvidenceStatus`, spawn.`ProductionSpawnEnabled`,
                   (SELECT COUNT(*) FROM `monster_drop_relationships` dropRow WHERE dropRow.`MonsterId`=monster.`Id`) AS `DropRelationshipCount`,
                   (SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` objective WHERE objective.`MonsterId`=monster.`Id`) AS `QuestTargetCount`
            FROM `monsters` monster
            LEFT JOIN `monster_semantic_profiles` semantic
              ON semantic.`MonsterId`=monster.`Id`
             AND semantic.`RunId`=(SELECT `RunId` FROM `content_recovery_runs` WHERE `Phase`='GameplayContentRecoveryPhase3' ORDER BY `StartedAtUtc` DESC LIMIT 1)
            LEFT JOIN `monster_spawn_semantics` spawn
              ON spawn.`MonsterId`=monster.`Id` AND spawn.`RunId`=semantic.`RunId`
            ORDER BY `DropRelationshipCount` DESC, `QuestTargetCount` DESC, monster.`Id`;
            """);
        var monsterFieldEvidence = await QueryRowsAsync(connection, """
            SELECT `EvidenceId`, `RunId`, `AuthorityKey`, `FieldName`, `ValueJson`, `EvidenceState`,
                   `SourceType`, `SourceFile`, `SourceIdentity`, `SourceHash`, `Confidence`, `Reason`
            FROM `god2_research`.`content_field_evidence`
            WHERE LOWER(`Domain`) LIKE '%monster%'
            ORDER BY `AuthorityKey`, `FieldName`, `EvidenceState`, `EvidenceId`;
            """);
        var monsterValidatedRecords = await QueryRowsAsync(connection, """
            SELECT validated.`RunId`, validated.`AuthorityKey`, validated.`NormalizedData`,
                   validated.`NormalizedHash`, validated.`EvidenceStatus`, validated.`SourceHash`,
                   manifest.`TargetRowIdentity`, manifest.`EvidenceStatus` AS `ProductionEvidenceStatus`
            FROM `god2_research`.`content_validated_records` validated
            LEFT JOIN `god2_research`.`content_production_manifest` manifest
              ON LOWER(manifest.`Domain`) LIKE '%monster%' AND manifest.`AuthorityKey`=validated.`AuthorityKey`
            WHERE LOWER(validated.`Domain`) LIKE '%monster%'
            ORDER BY validated.`RunId`, validated.`AuthorityKey`;
            """);
        var monsterDropRelationships = await QueryRowsAsync(connection, """
            SELECT relationship.`RelationshipId`, relationship.`RunId`, relationship.`MonsterId`, monster.`NameZhTw` AS `MonsterNameZhTw`,
                   relationship.`ItemId`, item.`NameZhTw` AS `ItemNameZhTw`, relationship.`MinimumQuantity`, relationship.`MaximumQuantity`,
                   relationship.`DeclaredDropChance`, relationship.`EffectiveDropChance`, relationship.`Weight`, relationship.`RollType`,
                   CASE WHEN relationship.`DeclaredDropChance` IS NULL THEN 'DefaultDisabledZero' WHEN relationship.`EffectiveDropChance`>=0 THEN 'Derived' ELSE 'EvidenceBlocked' END AS `ChanceEvidenceStatus`,
                   relationship.`DropRelationshipStatus`,
                   CASE WHEN relationship.`MinimumQuantity` IS NOT NULL AND relationship.`MaximumQuantity` IS NOT NULL AND relationship.`MinimumQuantity`>0 AND relationship.`MaximumQuantity`>=relationship.`MinimumQuantity` THEN 'Derived' ELSE 'EvidenceBlocked' END AS `QuantityEvidenceStatus`,
                   relationship.`ProductionDropEnabled`, COALESCE(source.`SourceType`,'') AS `SourceType`, COALESCE(source.`SourceFile`,'') AS `SourceFile`,
                   COALESCE(source.`SourceIdentity`,'') AS `SourceIdentity`, COALESCE(source.`SourceHash`,'') AS `SourceHash`, COALESCE(source.`Confidence`,'') AS `Confidence`
            FROM `monster_drop_relationships` relationship
            JOIN `monsters` monster ON monster.`Id`=relationship.`MonsterId`
            JOIN `items` item ON item.`Id`=relationship.`ItemId`
            LEFT JOIN `god2_research`.`relationship_source_archive` source
                ON source.`FormalTable`='monster_drop_relationships' AND source.`RelationshipId`=relationship.`RelationshipId`
            ORDER BY relationship.`MonsterId`, relationship.`ItemId`, relationship.`RelationshipId`;
            """);
        var clientLayouts = await QueryRowsAsync(connection, """
            SELECT `LayoutId`, `RunId`, `SourceFile`, `SourceHash`, `Decoder`, `RecordCount`,
                   `MaximumFieldCount`, `HeaderJson`, `RecordBoundaryStatus`, `LoaderEvidenceStatus`
            FROM `god2_research`.`content_client_table_layouts`
            WHERE LOWER(`SourceFile`) REGEXP 'npc|map|scene|monster|enemy|battle|encounter|formation|reward|drop'
               OR LOWER(`HeaderJson`) REGEXP 'npc|map|scene|monster|enemy|battle|encounter|formation|reward|drop|hp|mp'
            ORDER BY `SourceFile`, `LayoutId`;
            """);
        var historicalObservations = await QueryRowsAsync(connection, """
            SELECT `ObservationId`, `RunId`, `EventName`, `GameplayDataJson`, `EvidenceStatus`,
                   `SourceFile`, `SourceHash`, `ObservedAtUtc`
            FROM `god2_research`.`historical_gameplay_observations`
            WHERE LOWER(`EventName`) REGEXP 'npc|monster|spawn|battle|drop|map|reward'
               OR LOWER(`GameplayDataJson`) REGEXP 'npc|monster|spawn|battle|drop|map|reward'
            ORDER BY `EventName`, `ObservationId`;
            """);

        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-content-slice-inventory-v1",
            status = "PASS",
            authority = "MariaDB",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false,
            runs,
            mapIdentities,
            evidenceShape,
            npcCoordinateEvidence,
            npcFieldEvidence,
            npcValidatedRecords,
            monsterProfiles,
            monsterFieldEvidence,
            monsterValidatedRecords,
            monsterDropRelationships,
            clientLayouts,
            historicalObservations
        });
        return 0;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-content-slice-inventory-v1",
            status = "BLOCKED",
            reason = "mariadb.m2_content_slice_inventory_failed",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 52;
    }
    finally
    {
        password = null;
    }
}

static async Task<int> RunM5BattleSliceValidationAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m5-battle-slice-validation-v2",
            status = "BLOCKED",
            reason = "mariadb.password_missing",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false
        }, options.OutputPath, options.BaseDirectory);
        return 51;
    }

    try
    {
        await using var connection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await connection.OpenAsync();
        var phase3RunId = Convert.ToString(await ExecuteScalarAsync(connection, """
            SELECT `RunId`
            FROM `content_recovery_runs`
            WHERE `Phase`='GameplayContentRecoveryPhase3'
            ORDER BY `StartedAtUtc` DESC
            LIMIT 1;
            """), System.Globalization.CultureInfo.InvariantCulture);
        var monsterTotal = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `monsters`;"));
        var enabledRuntimeSpawns = Convert.ToInt64(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*) FROM `god2_game`.`monster_spawns` WHERE `enabled`=1;
            """));
        var semanticMonsterProfiles = Convert.ToInt64(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM `monster_semantic_profiles`
            WHERE `RunId`=(
                SELECT `RunId` FROM `content_recovery_runs`
                WHERE `Phase`='GameplayContentRecoveryPhase3'
                ORDER BY `StartedAtUtc` DESC LIMIT 1);
            """));
        var encounterEligibleMonsterSpawns = await QueryM5MonsterSpawnIdentitySetAsync(connection, """
            SELECT DISTINCT semantic.`MonsterId`, spawn.`MapId`, spawn.`PositionX`, spawn.`PositionY`
            FROM `monster_semantic_profiles` semantic
            JOIN `monster_spawn_semantics` spawn
              ON spawn.`RunId`=semantic.`RunId` AND spawn.`MonsterId`=semantic.`MonsterId`
            WHERE semantic.`RunId`=(
                    SELECT `RunId` FROM `content_recovery_runs`
                    WHERE `Phase`='GameplayContentRecoveryPhase3'
                    ORDER BY `StartedAtUtc` DESC LIMIT 1)
              AND semantic.`ProductionEnabled`=1
              AND spawn.`ProductionSpawnEnabled`=1
              AND spawn.`MapId` IS NOT NULL
              AND spawn.`PositionX` IS NOT NULL AND spawn.`PositionY` IS NOT NULL
              AND spawn.`EncounterGroup` IS NOT NULL AND spawn.`EncounterGroup` <> ''
              AND spawn.`FormationGroup` IS NOT NULL AND spawn.`FormationGroup` <> ''
              AND spawn.`MapRelationshipStatus` IN ('Verified','Derived')
              AND spawn.`CoordinateEvidenceStatus` IN ('Verified','Derived')
              AND spawn.`RespawnPolicy` IN ('Verified','Derived','Fixed')
              AND spawn.`EvidenceStatus` IN ('Verified','Derived')
              AND semantic.`Level` IS NOT NULL AND semantic.`MaxHp` > 0
              AND semantic.`HpPolicy` = 'Fixed'
              AND semantic.`MaxMp` IS NOT NULL
              AND semantic.`MpPolicy` IN ('ExplicitOfficialZero','Fixed')
              AND semantic.`PhysicalAttack` IS NOT NULL AND semantic.`MagicAttack` IS NOT NULL
              AND semantic.`PhysicalDefense` IS NOT NULL AND semantic.`MagicDefense` IS NOT NULL
              AND semantic.`Speed` IS NOT NULL AND semantic.`Initiative` IS NOT NULL
              AND semantic.`Accuracy` IS NOT NULL AND semantic.`Evasion` IS NOT NULL
              AND semantic.`AiFamily` IS NOT NULL AND semantic.`AiFamily` <> ''
              AND semantic.`ExperienceReward` IS NOT NULL
              AND semantic.`CurrencyMinimum` IS NOT NULL AND semantic.`CurrencyMaximum` IS NOT NULL
              AND semantic.`DropPolicyStatus` IN ('Verified','Derived','ExplicitOfficialZero');
            """);
        var encounterEligibleMonsterIds = encounterEligibleMonsterSpawns
            .Select(spawn => spawn.MonsterTemplateId)
            .ToHashSet();
        var skillTotal = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `skills`;"));
        var semanticSkillProfiles = Convert.ToInt64(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM `skill_semantic_profiles`
            WHERE `RunId`=(
                SELECT `RunId` FROM `content_recovery_runs`
                WHERE `Phase`='GameplayContentRecoveryPhase3'
                ORDER BY `StartedAtUtc` DESC LIMIT 1);
            """));
        var semanticSkillCandidateIds = await QueryInt32SetAsync(connection, """
            SELECT DISTINCT `SkillId`
            FROM `skill_semantic_profiles`
            WHERE `RunId`=(
                    SELECT `RunId` FROM `content_recovery_runs`
                    WHERE `Phase`='GameplayContentRecoveryPhase3'
                    ORDER BY `StartedAtUtc` DESC LIMIT 1)
              AND `ProductionEnabled`=1
              AND `SkillFamily` <> 'Unknown'
              AND `TargetPolicy` <> 'Unknown'
              AND `MpCostPolicy` IN ('ExplicitOfficialZero','Fixed')
              AND `MpCost` IS NOT NULL
              AND JSON_LENGTH(`EffectReferencesJson`) > 0;
            """);

        var root = Path.GetFullPath(options.BaseDirectory);
        var paths = new AppPathProvider(root);
        var configuration = new JsonServerConfigurationLoader(paths).Load();
        if (!configuration.Succeeded || configuration.Value is null)
        {
            throw new InvalidOperationException(
                $"M5 production configuration is unavailable: {configuration.Error.Code}.");
        }

        var staticData = new MariaDbStaticDataLoader(configuration.Value.Database);
        var loaded = await staticData.LoadAsync(CancellationToken.None);
        if (!loaded.Succeeded || loaded.Value is null)
        {
            throw new InvalidOperationException(
                $"M5 production static data load failed: {loaded.Error.Code}.");
        }

        var built = await staticData.BuildAsync(loaded.Value, CancellationToken.None);
        if (!built.Succeeded)
        {
            throw new InvalidOperationException(
                $"M5 production static data build failed: {built.Error.Code}.");
        }

        var worldRepository = new MariaDbWorldContentRepository(staticData);
        var worldContent = await worldRepository.LoadAsync(CancellationToken.None);
        var runtimeSpawnIdentities = staticData.PublishedSnapshot.Spawns.Values
            .Where(spawn => spawn.ProductionEnabled &&
                            spawn.EvidenceStatus is "Verified" or "Derived")
            .Select(spawn => new RoadMapM5MonsterSpawnIdentity(
                spawn.MonsterId,
                spawn.MapId,
                spawn.PositionX,
                spawn.PositionY))
            .ToHashSet();
        var semanticallyMatchedRuntimeSpawns = RoadMapM5IdentityAlignment.CountAlignedMonsterSpawns(
            encounterEligibleMonsterSpawns,
            runtimeSpawnIdentities);
        var repositoryMonsterSpawns = worldContent.Maps.Values
            .SelectMany(map => map.MonsterSpawns)
            .Count(spawn => spawn.Enabled);
        var repositoryMonsterSpawnsIdentities = worldContent.Maps.Values
            .SelectMany(map => map.MonsterSpawns)
            .Where(spawn => spawn.Enabled)
            .Select(spawn => new RoadMapM5MonsterSpawnIdentity(
                spawn.MonsterTemplateId,
                spawn.MapId,
                spawn.Position.X,
                spawn.Position.Y))
            .ToHashSet();
        var matchingRepositoryMonsterSpawns = RoadMapM5IdentityAlignment.CountAlignedMonsterSpawns(
            encounterEligibleMonsterSpawns,
            repositoryMonsterSpawnsIdentities);
        var runtimeMonsterEntities = 0;
        var runtimeMonsterSpawnIdentities = new HashSet<RoadMapM5MonsterSpawnIdentity>();
        foreach (var map in worldContent.Maps.Values.Where(map => map.MonsterSpawns.Any(spawn => spawn.Enabled)))
        {
            var runtime = new MapRuntimeFactory().Create(worldContent, map.MapId);
            if (!runtime.Succeeded || runtime.Value is null)
            {
                throw new InvalidOperationException(
                    $"M5 MapRuntime construction failed for map {map.MapId}: {runtime.Error.Code}.");
            }

            var monsters = runtime.Value.MapRuntime.Objects
                .OfType<MonsterObject>()
                .ToArray();
            runtimeMonsterEntities += monsters.Length;
            runtimeMonsterSpawnIdentities.UnionWith(monsters.Select(monster =>
                new RoadMapM5MonsterSpawnIdentity(
                    monster.State.MonsterTemplateId,
                    monster.State.MapId,
                    monster.State.Position.X,
                    monster.State.Position.Y)));
        }

        var matchingRuntimeMonsterSpawns = RoadMapM5IdentityAlignment.CountAlignedMonsterSpawns(
            encounterEligibleMonsterSpawns,
            runtimeMonsterSpawnIdentities);

        var skillRecords = await new MariaDbSkillDefinitionRepository(configuration.Value.Database)
            .LoadAsync(CancellationToken.None);
        var skillMapper = new SkillDefinitionMapper();
        var skillValidator = new SkillDefinitionValidator();
        var runtimeStructurallyValidatedSkillDefinitions = 0;
        var runtimeExecutionEligibleSkills = 0;
        var runtimeExecutionEligibleSkillIds = new HashSet<int>();
        foreach (var record in skillRecords)
        {
            var mapped = skillMapper.Map(record);
            if (!mapped.Succeeded || mapped.Value is null || !skillValidator.Validate(mapped.Value).Succeeded)
            {
                continue;
            }

            runtimeStructurallyValidatedSkillDefinitions++;
            if (IsProductionRuntimeSkill(mapped.Value))
            {
                runtimeExecutionEligibleSkills++;
                runtimeExecutionEligibleSkillIds.Add(mapped.Value.SkillDefinitionId);
            }
        }

        var matchingRepositorySkills = RoadMapM5IdentityAlignment.CountAlignedIds(
            semanticSkillCandidateIds,
            skillRecords.Select(record => record.SkillDefinitionId));
        var matchingRuntimeExecutionEligibleSkills = RoadMapM5IdentityAlignment.CountAlignedIds(
            semanticSkillCandidateIds,
            runtimeExecutionEligibleSkillIds);

        var readiness = RoadMapM5BattleReadiness.Evaluate(new RoadMapM5BattleReadinessInput(
            encounterEligibleMonsterSpawns.Count,
            semanticallyMatchedRuntimeSpawns,
            matchingRepositoryMonsterSpawns,
            matchingRuntimeMonsterSpawns,
            RoadMapM5BattleRuntimeCapabilities.FullMonsterEncounterSemanticsEnabled,
            semanticSkillCandidateIds.Count,
            matchingRepositorySkills,
            matchingRuntimeExecutionEligibleSkills,
            OfficialBattleCommandWireCodec.RuntimeMutationEnabled,
            OfficialBattleCommandWireCodec.ServerResultSerializerEnabled));
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m5-battle-slice-validation-v2",
            status = readiness.Ready ? "PASS" : "BLOCKED",
            authority = "MariaDB",
            worldRepositoryActuallyUsed = worldRepository.GetType().Name,
            skillRepositoryActuallyUsed = nameof(MariaDbSkillDefinitionRepository),
            queryMode = "ReadOnly",
            phase3RunId,
            counts = new
            {
                monsterTotal,
                semanticMonsterProfiles,
                enabledRuntimeSpawns,
                encounterEligibleMonsters = encounterEligibleMonsterIds.Count,
                encounterEligibleSpawnIdentities = encounterEligibleMonsterSpawns.Count,
                semanticallyMatchedRuntimeSpawns,
                repositoryMonsterSpawns,
                matchingRepositoryMonsterSpawns,
                runtimeMonsterEntities,
                matchingRuntimeMonsterSpawns,
                skillTotal,
                semanticSkillProfiles,
                semanticSkillCandidates = semanticSkillCandidateIds.Count,
                repositorySkillDefinitions = skillRecords.Count,
                matchingRepositorySkills,
                runtimeStructurallyValidatedSkillDefinitions,
                runtimeExecutionEligibleSkills,
                matchingRuntimeExecutionEligibleSkills
            },
            runtimeCapabilities = new
            {
                fullMonsterEncounterSemanticsEnabled =
                    RoadMapM5BattleRuntimeCapabilities.FullMonsterEncounterSemanticsEnabled
            },
            wire = new
            {
                commandRuntimeMutationEnabled = OfficialBattleCommandWireCodec.RuntimeMutationEnabled,
                serverResultSerializerEnabled = OfficialBattleCommandWireCodec.ServerResultSerializerEnabled
            },
            firstBrokenNode = readiness.FirstBrokenNode,
            productionMutationAllowed = readiness.Ready,
            secretRetained = false,
            connectionStringRetained = false,
            networkBytesEmitted = false,
            fakeNetworkBytes = false
        }, options.OutputPath, options.BaseDirectory);
        return readiness.Ready ? 0 : 2;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m5-battle-slice-validation-v2",
            status = "BLOCKED",
            reason = "mariadb.m5_battle_slice_validation_failed",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false,
            networkBytesEmitted = false,
            fakeNetworkBytes = false
        }, options.OutputPath, options.BaseDirectory);
        return 52;
    }
}

static async Task<int> RunM6InventoryPersistenceValidationAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m6-inventory-persistence-validation-v1",
            status = "BLOCKED",
            reason = "mariadb.password_missing",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false
        }, options.OutputPath, options.BaseDirectory);
        return 51;
    }

    try
    {
        var root = Path.GetFullPath(options.BaseDirectory);
        var paths = new AppPathProvider(root);
        var configuration = new JsonServerConfigurationLoader(paths).Load();
        if (!configuration.Succeeded || configuration.Value is null)
        {
            throw new InvalidOperationException($"M6 production configuration is unavailable: {configuration.Error.Code}.");
        }

        await using var connection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await connection.OpenAsync();
        var characterCount = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `characters` WHERE `DeletedAtUtc` IS NULL;"));
        var inventoryStateRows = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `god2_player`.`player_inventory_state`;"));
        var inventorySlotRows = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `god2_player`.`character_inventory` WHERE `deleted_at_utc` IS NULL;"));
        var currencyRows = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `god2_player`.`player_currency_balances`;"));
        var idempotencyRows = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `god2_player`.`inventory_transaction_idempotency`;"));
        var auditRows = Convert.ToInt64(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM `god2_player`.`inventory_audit_ledger`;"));
        var evidenceCompleteRewardProfiles = Convert.ToInt64(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM `monster_semantic_profiles`
            WHERE `RunId`=(
                SELECT `RunId` FROM `content_recovery_runs`
                WHERE `Phase`='GameplayContentRecoveryPhase3'
                ORDER BY `StartedAtUtc` DESC LIMIT 1)
              AND `ProductionEnabled`=1
              AND `CurrencyMinimum` IS NOT NULL
              AND `CurrencyMaximum` IS NOT NULL
              AND `CurrencyMinimum`>=0
              AND `CurrencyMaximum`>=`CurrencyMinimum`;
            """));
        var productionEnabledDropEntries = Convert.ToInt64(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM `monster_drop_relationships`
            WHERE `ProductionDropEnabled`=1
              AND `IsDropEnabled`=1
              AND `DropRelationshipStatus` IN ('Verified','Derived')
              AND `MinimumQuantity`>0
              AND `MaximumQuantity`>=`MinimumQuantity`
              AND `DeclaredDropChance` IS NOT NULL
              AND `DeclaredDropChance`>0;
            """));
        var defaultDisabledZeroViolations = Convert.ToInt64(await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM `monster_drop_relationships`
            WHERE `ProductionDropEnabled`=0
              AND `DeclaredDropChance` IS NULL
              AND (`EffectiveDropChance`<>0 OR `IsDropEnabled`<>0);
            """));

        var inventoryRuntime = new MariaDbGameplayInventoryRuntime(configuration.Value.Database);
        var initialized = await inventoryRuntime.BuildAsync([], CancellationToken.None);
        long? sampledCharacterId = null;
        var restartReloadIdentityStable = false;
        if (characterCount > 0)
        {
            sampledCharacterId = Convert.ToInt64(await ExecuteScalarAsync(connection, """
                SELECT `Id` FROM `characters`
                WHERE `DeletedAtUtc` IS NULL
                ORDER BY `Id` LIMIT 1;
                """));
            var first = await new MariaDbGameplayInventoryRepository(configuration.Value.Database)
                .LoadAsync(sampledCharacterId.Value, CancellationToken.None);
            var second = await new MariaDbGameplayInventoryRepository(configuration.Value.Database)
                .LoadAsync(sampledCharacterId.Value, CancellationToken.None);
            restartReloadIdentityStable = InventoryBundlesEqual(first, second);
        }

        var readiness = RoadMapM6InventoryReadiness.Evaluate(new RoadMapM6InventoryReadinessInput(
            M5OfficialBattleLoopComplete: false,
            ProductionInventoryAuthorityConnected:
                initialized.Succeeded &&
                string.Equals(inventoryRuntime.PersistenceAuthority, nameof(MariaDbGameplayInventoryRepository), StringComparison.Ordinal),
            RuntimeCatalogItemCount: inventoryRuntime.CatalogItemCount,
            RuntimeCatalogFatalIssueCount: inventoryRuntime.FatalIssueCount,
            MariaDbVersionCompareAndSwapEnabled: RoadMapM6InventoryCapabilities.MariaDbVersionCompareAndSwapEnabled,
            MariaDbExactlyOnceEnabled: RoadMapM6InventoryCapabilities.MariaDbExactlyOnceEnabled,
            RestartReloadIdentityStable: restartReloadIdentityStable,
            EvidenceCompleteMonsterRewardProfiles: checked((int)evidenceCompleteRewardProfiles),
            ProductionEnabledDropEntries: checked((int)productionEnabledDropEntries),
            DefaultDisabledZeroViolations: checked((int)defaultDisabledZeroViolations),
            ProductionBattleRewardPolicyEnabled: RoadMapM6InventoryCapabilities.ProductionBattleRewardPolicyEnabled,
            OfficialInventorySerializerEnabled: RoadMapM6InventoryCapabilities.OfficialInventorySerializerEnabled,
            OfficialClientInventoryAcceptancePassed: false));

        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m6-inventory-persistence-validation-v1",
            status = readiness.Ready ? "PASS" : "BLOCKED",
            authority = "MariaDB",
            queryMode = "ReadOnly",
            contentCatalogRepositoryActuallyUsed = nameof(MariaDbGameplayContentCatalogRepository),
            inventoryRepositoryActuallyUsed = inventoryRuntime.PersistenceAuthority,
            runtimeActuallyUsed = nameof(MariaDbGameplayInventoryRuntime),
            counts = new
            {
                characterCount,
                inventoryStateRows,
                inventorySlotRows,
                currencyRows,
                idempotencyRows,
                auditRows,
                runtimeCatalogItems = inventoryRuntime.CatalogItemCount,
                quarantinedCatalogItems = inventoryRuntime.QuarantinedItemCount,
                fatalCatalogIssues = inventoryRuntime.FatalIssueCount,
                evidenceCompleteRewardProfiles,
                productionEnabledDropEntries,
                defaultDisabledZeroViolations
            },
            transactionCapabilities = new
            {
                mariaDbVersionCompareAndSwapEnabled = RoadMapM6InventoryCapabilities.MariaDbVersionCompareAndSwapEnabled,
                mariaDbExactlyOnceEnabled = RoadMapM6InventoryCapabilities.MariaDbExactlyOnceEnabled,
                restartReloadIdentityStable,
                sampledCharacter = sampledCharacterId is null ? "NONE" : "HASHED",
                productionMutationExecuted = false
            },
            rewardAndWireCapabilities = new
            {
                m5OfficialBattleLoopComplete = false,
                productionBattleRewardPolicyEnabled = RoadMapM6InventoryCapabilities.ProductionBattleRewardPolicyEnabled,
                officialInventorySerializerEnabled = RoadMapM6InventoryCapabilities.OfficialInventorySerializerEnabled,
                officialClientInventoryAcceptancePassed = false
            },
            firstBrokenNode = readiness.FirstBrokenNode,
            secretRetained = false,
            connectionStringRetained = false,
            sensitiveInventoryPayloadRetained = false,
            networkBytesEmitted = false,
            fakeNetworkBytes = false
        }, options.OutputPath, options.BaseDirectory);
        return readiness.Ready ? 0 : 2;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m6-inventory-persistence-validation-v1",
            status = "BLOCKED",
            reason = "mariadb.m6_inventory_validation_failed",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false,
            sensitiveInventoryPayloadRetained = false,
            networkBytesEmitted = false,
            fakeNetworkBytes = false
        }, options.OutputPath, options.BaseDirectory);
        return 52;
    }
    finally
    {
        password = null;
    }
}

static bool InventoryBundlesEqual(InventoryPersistenceBundle left, InventoryPersistenceBundle right) =>
    left.Inventory.InventoryId == right.Inventory.InventoryId &&
    left.Inventory.CharacterId == right.Inventory.CharacterId &&
    left.Inventory.Capacity == right.Inventory.Capacity &&
    left.Inventory.Version == right.Inventory.Version &&
    left.Inventory.MutationSequence == right.Inventory.MutationSequence &&
    left.Inventory.DirtyState == right.Inventory.DirtyState &&
    left.Inventory.Slots.SequenceEqual(right.Inventory.Slots) &&
    left.Currency.CharacterId == right.Currency.CharacterId &&
    left.Currency.Balances.SequenceEqual(right.Currency.Balances);

static async Task<int> RunM6InventoryPhysicalFixtureAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJsonArtifact(new
        {
            schemaVersion = "god2-roadmap-m6-inventory-physical-fixture-v1",
            status = "NOT_RUN_SECRET_UNAVAILABLE",
            reason = "mariadb.password_missing",
            secretRetained = false,
            connectionStringRetained = false
        }, options.OutputPath, options.BaseDirectory);
        return 51;
    }

    var fixtureIdentity = Guid.NewGuid().ToString("N");
    var fixtureLogin = $"fixture_m6_{fixtureIdentity[..12]}";
    var fixtureCharacterName = $"M6{fixtureIdentity[..10]}";
    var fixtureSessionId = $"m6:{fixtureIdentity}";
    long? accountId = null;
    long? characterId = null;
    var cleanupStatus = "NOT_RUN";
    var fixtureStatus = "FAILED";
    var failureCode = "fixture.not_run";
    var failureLocation = "NONE";
    var fixtureStage = "create-fixture";
    InventoryTransactionResult? firstGrant = null;
    InventoryTransactionResult? duplicateGrant = null;
    InventoryTransactionResult[] concurrentDuplicateResults = [];
    InventoryTransactionResult[] raceResults = [];
    OperationResult? rollbackAttempt = null;
    var rollbackPreserved = false;
    var restartReloaded = false;
    var validSessionAuthority = false;
    var wrongAccountRejected = false;
    var staleSessionRejected = false;
    var globallyUniquePersistentItemIds = false;
    try
    {
        var root = Path.GetFullPath(options.BaseDirectory);
        var configuration = new JsonServerConfigurationLoader(new AppPathProvider(root)).Load();
        if (!configuration.Succeeded || configuration.Value is null)
        {
            throw new InvalidOperationException($"M6 fixture configuration is unavailable: {configuration.Error.Code}.");
        }

        var database = configuration.Value.Database;
        int itemTemplateId;
        await using (var connection = new MySqlConnection(BuildConfiguredDatabaseConnectionString(database)))
        {
            await connection.OpenAsync();
            await using (var account = connection.CreateCommand())
            {
                account.CommandText = """
                    INSERT INTO `accounts`
                        (`LoginName`, `PasswordHash`, `Status`, `CurrentSessionId`, `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`)
                    VALUES
                        (@loginName, 'fixture-hash-v1', 'Active', @sessionId, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @token);
                    """;
                account.Parameters.AddWithValue("@loginName", fixtureLogin);
                account.Parameters.AddWithValue("@sessionId", fixtureSessionId);
                account.Parameters.AddWithValue("@token", fixtureIdentity);
                await account.ExecuteNonQueryAsync();
                accountId = account.LastInsertedId;
            }

            await using (var character = connection.CreateCommand())
            {
                character.CommandText = """
                    INSERT INTO `characters`
                        (`AccountId`, `Name`, `MapId`, `PositionX`, `PositionY`, `Level`,
                         `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`)
                    VALUES
                        (@accountId, @name, 19, 28, 34, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @token);
                    """;
                character.Parameters.AddWithValue("@accountId", accountId.Value);
                character.Parameters.AddWithValue("@name", fixtureCharacterName);
                character.Parameters.AddWithValue("@token", fixtureIdentity);
                await character.ExecuteNonQueryAsync();
                characterId = character.LastInsertedId;
            }

        }

        fixtureStage = "load-content-catalog";
        var contentCatalog = await new MariaDbGameplayContentCatalogRepository(database)
            .LoadAsync(CancellationToken.None);
        itemTemplateId = contentCatalog.ItemCatalog.Definitions.Keys.Order().First();

        fixtureStage = "initialize-runtimes";
        var firstRuntime = new MariaDbGameplayInventoryRuntime(database);
        var secondRuntime = new MariaDbGameplayInventoryRuntime(database);
        if (!(await firstRuntime.BuildAsync([], CancellationToken.None)).Succeeded ||
            !(await secondRuntime.BuildAsync([], CancellationToken.None)).Succeeded)
        {
            throw new InvalidOperationException("M6 fixture could not initialize the production inventory runtimes.");
        }

        var transactionId = Guid.NewGuid();
        var firstRequest = new InventoryTransactionRequest(
            transactionId,
            $"m6:{fixtureIdentity}:grant",
            characterId.Value,
            fixtureSessionId,
            null,
            InventoryOperationType.SystemGrant,
            0,
            "RoadMapM6PhysicalFixture",
            [new InventoryMutationRequest(ItemTemplateId: itemTemplateId, Quantity: 1)],
            5,
            null,
            DateTimeOffset.UtcNow,
            InventoryMutationAuthorityKind.TrustedServer);

        var repository = new MariaDbGameplayInventoryRepository(database);
        var playerAuthorityRequest = firstRequest with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = $"m6:{fixtureIdentity}:player-authority",
            AccountId = accountId,
            OperationType = InventoryOperationType.AddItem,
            RequestedCurrencyMutation = 0,
            AuthorityKind = InventoryMutationAuthorityKind.PlayerSession
        };
        validSessionAuthority = (await repository.ValidateAuthorityAsync(playerAuthorityRequest, CancellationToken.None)).Succeeded;
        wrongAccountRejected = !(await repository.ValidateAuthorityAsync(
            playerAuthorityRequest with { AccountId = accountId.Value + 1 },
            CancellationToken.None)).Succeeded;
        staleSessionRejected = !(await repository.ValidateAuthorityAsync(
            playerAuthorityRequest with { SessionId = $"stale:{fixtureIdentity}" },
            CancellationToken.None)).Succeeded;
        fixtureStage = "initial-grant";
        firstGrant = await firstRuntime.ExecuteAsync(firstRequest, CancellationToken.None);
        fixtureStage = "duplicate-replay";
        duplicateGrant = await secondRuntime.ExecuteAsync(firstRequest, CancellationToken.None);

        var concurrentDuplicateRequest = firstRequest with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = $"m6:{fixtureIdentity}:concurrent-duplicate",
            ExpectedInventoryVersion = 1,
            RequestedCurrencyMutation = 2,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        fixtureStage = "concurrent-duplicate-replay";
        concurrentDuplicateResults = await Task.WhenAll(
            firstRuntime.ExecuteAsync(concurrentDuplicateRequest, CancellationToken.None),
            secondRuntime.ExecuteAsync(concurrentDuplicateRequest, CancellationToken.None));

        var raceRequestA = firstRequest with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = $"m6:{fixtureIdentity}:race-a",
            ExpectedInventoryVersion = 2,
            RequestedCurrencyMutation = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var raceRequestB = raceRequestA with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = $"m6:{fixtureIdentity}:race-b"
        };
        fixtureStage = "concurrent-version-race";
        raceResults = await Task.WhenAll(
            firstRuntime.ExecuteAsync(raceRequestA, CancellationToken.None),
            secondRuntime.ExecuteAsync(raceRequestB, CancellationToken.None));

        fixtureStage = "load-before-rollback";
        var beforeRollback = await repository.LoadAsync(characterId.Value, CancellationToken.None);
        globallyUniquePersistentItemIds =
            beforeRollback.Inventory.Slots.Select(slot => slot.PersistentInventoryItemId).Distinct().Count() ==
            beforeRollback.Inventory.Slots.Count;
        var invalidIdentityReservation = await repository.ReservePersistentItemIdsAsync(1, CancellationToken.None);
        if (!invalidIdentityReservation.Succeeded || invalidIdentityReservation.Value is null)
        {
            throw new InvalidOperationException("M6 fixture could not reserve an invalid-row rollback identity.");
        }

        var invalidSlot = new InventorySlot(
            beforeRollback.Inventory.Slots.Count == 0 ? 0 : beforeRollback.Inventory.Slots.Max(slot => slot.SlotIndex) + 1,
            invalidIdentityReservation.Value.Single(),
            RuntimeObjectIds.Static(RuntimeObjectKind.Item, fixtureIdentity.GetHashCode(StringComparison.Ordinal) & 0x7FFFFFFF, "M6Fixture").RuntimeObjectId,
            0,
            int.MaxValue,
            1,
            "None",
            "{}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);
        var invalidAfter = beforeRollback.Inventory with
        {
            Version = beforeRollback.Inventory.Version + 1,
            MutationSequence = beforeRollback.Inventory.MutationSequence + 1,
            DirtyState = "Dirty",
            Slots = beforeRollback.Inventory.Slots.Concat([invalidSlot]).ToArray()
        };
        var rollbackRequest = firstRequest with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = $"m6:{fixtureIdentity}:rollback",
            ExpectedInventoryVersion = beforeRollback.Inventory.Version,
            RequestedMutations = [new InventoryMutationRequest(ItemTemplateId: int.MaxValue, Quantity: 1)],
            RequestedCurrencyMutation = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var rollbackAudit = new InventoryAuditRecord(
            Guid.NewGuid(),
            rollbackRequest.TransactionId,
            "fixture-rollback",
            characterId.Value,
            rollbackRequest.SessionId,
            rollbackRequest.OperationType,
            rollbackRequest.Source,
            null,
            int.MaxValue,
            invalidSlot.PersistentInventoryItemId,
            0,
            1,
            "Gold",
            beforeRollback.Currency.Balances.Single(balance => balance.CurrencyType == "Gold").Balance,
            beforeRollback.Currency.Balances.Single(balance => balance.CurrencyType == "Gold").Balance,
            beforeRollback.Inventory.Version,
            invalidAfter.Version,
            InventoryTransactionResultCode.Success,
            "",
            rollbackRequest.CreatedAtUtc,
            DateTimeOffset.UtcNow,
            fixtureIdentity);
        fixtureStage = "rollback-injection";
        rollbackAttempt = await repository.CommitAsync(
            new InventoryPersistenceCommit(
                rollbackRequest,
                beforeRollback.Inventory,
                invalidAfter,
                beforeRollback.Currency,
                beforeRollback.Currency,
                rollbackAudit,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixtureIdentity))).ToLowerInvariant()),
            CancellationToken.None);
        var afterRollback = await repository.LoadAsync(characterId.Value, CancellationToken.None);
        var rollbackReplay = await repository.FindCompletedAsync(
            rollbackRequest.IdempotencyKey,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixtureIdentity))).ToLowerInvariant(),
            CancellationToken.None);
        rollbackPreserved = !rollbackAttempt.Succeeded &&
            InventoryBundlesEqual(beforeRollback, afterRollback) &&
            !rollbackReplay.Found;

        fixtureStage = "restart-reload";
        var restartedRuntime = new MariaDbGameplayInventoryRuntime(database);
        var restartedBuild = await restartedRuntime.BuildAsync([], CancellationToken.None);
        var restartedVersion = restartedBuild.Succeeded
            ? await restartedRuntime.GetCurrentVersionAsync(characterId.Value, CancellationToken.None)
            : OperationResult<long>.Failure("inventory.restart_build_failed", "Restarted inventory runtime did not initialize.");
        var restartedReplay = restartedBuild.Succeeded && restartedVersion.Succeeded
            ? await restartedRuntime.ExecuteAsync(firstRequest, CancellationToken.None)
            : null;
        var restartedInspector = restartedBuild.Succeeded && restartedVersion.Succeeded
            ? restartedRuntime.CaptureInspector(new InventoryInspectorQuery(CharacterId: characterId.Value))
            : null;
        var restartedInventory = restartedInspector?.Inventories.SingleOrDefault();
        restartReloaded = restartedBuild.Succeeded &&
            restartedVersion.Succeeded &&
            restartedVersion.Value == beforeRollback.Inventory.Version &&
            restartedInventory is not null &&
            restartedInventory.InventoryVersion == beforeRollback.Inventory.Version &&
            restartedInspector!.Items.Count == beforeRollback.Inventory.Slots.Count &&
            restartedInspector.Items.Sum(item => item.Quantity) == beforeRollback.Inventory.Slots.Sum(slot => slot.Quantity) &&
            restartedReplay?.InventorySnapshot is not null &&
            restartedReplay.CurrencySnapshot is not null &&
            restartedReplay.InventorySnapshot.InventoryId == beforeRollback.Inventory.InventoryId &&
            restartedReplay.InventorySnapshot.Slots.SequenceEqual(beforeRollback.Inventory.Slots) &&
            restartedReplay.CurrencySnapshot.Balances.SequenceEqual(beforeRollback.Currency.Balances);

        var passed = firstGrant.Code == InventoryTransactionResultCode.Success &&
            duplicateGrant.Code == InventoryTransactionResultCode.DuplicateCompleted &&
            concurrentDuplicateResults.Count(result => result.Code == InventoryTransactionResultCode.Success) == 1 &&
            concurrentDuplicateResults.Count(result => result.Code == InventoryTransactionResultCode.DuplicateCompleted) == 1 &&
            raceResults.Count(result => result.Code == InventoryTransactionResultCode.Success) == 1 &&
            raceResults.Count(result => result.Code == InventoryTransactionResultCode.VersionConflict) == 1 &&
            beforeRollback.Inventory.Version == 3 &&
            beforeRollback.Inventory.Slots.Sum(slot => slot.Quantity) == 3 &&
            beforeRollback.Currency.Balances.Single(balance => balance.CurrencyType == "Gold").Balance == 10 &&
            firstGrant.InventorySnapshot is not null &&
            duplicateGrant.InventorySnapshot is not null &&
            duplicateGrant.CurrencySnapshot is not null &&
            duplicateGrant.InventoryVersionAfter == duplicateGrant.InventorySnapshot.Version &&
            globallyUniquePersistentItemIds &&
            validSessionAuthority &&
            wrongAccountRejected &&
            staleSessionRejected &&
            rollbackPreserved &&
            restartReloaded;
        fixtureStatus = passed ? "PASS" : "FAILED";
        failureCode = passed ? "NONE" : "inventory.fixture_assertion_failed";
        fixtureStage = "completed";
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        fixtureStatus = "FAILED";
        failureCode = $"inventory.fixture_exception.{fixtureStage}.{exception.GetType().Name}";
        failureLocation = exception.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? exception.TargetSite?.Name ?? "UNKNOWN";
    }
    finally
    {
        try
        {
            var configuration = new JsonServerConfigurationLoader(new AppPathProvider(Path.GetFullPath(options.BaseDirectory))).Load();
            if (!configuration.Succeeded || configuration.Value is null)
            {
                throw new InvalidOperationException("Fixture cleanup configuration is unavailable.");
            }

            await using var connection = new MySqlConnection(BuildConfiguredDatabaseConnectionString(configuration.Value.Database));
            await connection.OpenAsync();
            await using var cleanup = await connection.BeginTransactionAsync();
            if (characterId is not null)
            {
                foreach (var deleteSpec in new[]
                         {
                             ("god2_player", "inventory_audit_ledger", "CharacterId"),
                             ("god2_player", "inventory_transaction_idempotency", "CharacterId"),
                             ("god2_player", "character_inventory", "character_id"),
                             ("god2_player", "character_equipment", "character_id"),
                             ("god2_player", "player_currency_balances", "CharacterId"),
                             ("god2_player", "player_inventory_state", "CharacterId")
                         })
                {
                    await using var delete = connection.CreateCommand();
                    delete.Transaction = cleanup;
                    delete.CommandText = $"DELETE FROM `{deleteSpec.Item1}`.`{deleteSpec.Item2}` WHERE `{deleteSpec.Item3}`=@characterId;";
                    delete.Parameters.AddWithValue("@characterId", characterId.Value);
                    await delete.ExecuteNonQueryAsync();
                }

                await using var deleteCharacter = connection.CreateCommand();
                deleteCharacter.Transaction = cleanup;
                deleteCharacter.CommandText = "DELETE FROM `characters` WHERE `Id`=@characterId AND `AccountId`=@accountId AND `Name`=@name;";
                deleteCharacter.Parameters.AddWithValue("@characterId", characterId.Value);
                deleteCharacter.Parameters.AddWithValue("@accountId", accountId!.Value);
                deleteCharacter.Parameters.AddWithValue("@name", fixtureCharacterName);
                if (await deleteCharacter.ExecuteNonQueryAsync() != 1)
                {
                    throw new InvalidOperationException("Fixture character cleanup identity did not match exactly one row.");
                }
            }

            if (accountId is not null)
            {
                await using var deleteAccount = connection.CreateCommand();
                deleteAccount.Transaction = cleanup;
                deleteAccount.CommandText = "DELETE FROM `accounts` WHERE `Id`=@accountId AND `LoginName`=@loginName;";
                deleteAccount.Parameters.AddWithValue("@accountId", accountId.Value);
                deleteAccount.Parameters.AddWithValue("@loginName", fixtureLogin);
                if (await deleteAccount.ExecuteNonQueryAsync() != 1)
                {
                    throw new InvalidOperationException("Fixture account cleanup identity did not match exactly one row.");
                }
            }

            await cleanup.CommitAsync();
            cleanupStatus = "PASS";
        }
        catch
        {
            cleanupStatus = "FAILED";
        }

        password = null;
    }

    if (!string.Equals(cleanupStatus, "PASS", StringComparison.Ordinal))
    {
        fixtureStatus = "FAILED";
        failureCode = "inventory.fixture_cleanup_failed";
    }

    WriteJsonArtifact(new
    {
        schemaVersion = "god2-roadmap-m6-inventory-physical-fixture-v2",
        status = fixtureStatus,
        failureCode,
        failureLocation,
        fixtureStage,
        authority = "MariaDB",
        repositoryActuallyUsed = nameof(MariaDbGameplayInventoryRepository),
        runtimeActuallyUsed = nameof(MariaDbGameplayInventoryRuntime),
        fixtureNamespace = "ephemeral-account-character-by-scoped-identity",
        fixtureCleanup = cleanupStatus,
        initialGrant = firstGrant?.Code.ToString() ?? "NOT_RUN",
        duplicateReplay = duplicateGrant?.Code.ToString() ?? "NOT_RUN",
        concurrentDuplicateWinners = concurrentDuplicateResults.Count(result => result.Code == InventoryTransactionResultCode.Success),
        concurrentDuplicateReplays = concurrentDuplicateResults.Count(result => result.Code == InventoryTransactionResultCode.DuplicateCompleted),
        concurrentDifferentRequestWinners = raceResults.Count(result => result.Code == InventoryTransactionResultCode.Success),
        concurrentVersionConflicts = raceResults.Count(result => result.Code == InventoryTransactionResultCode.VersionConflict),
        replaySnapshotHydrated = duplicateGrant?.InventorySnapshot is not null && duplicateGrant.CurrencySnapshot is not null,
        validSessionAuthority,
        wrongAccountRejected,
        staleSessionRejected,
        globallyUniquePersistentItemIds,
        rollbackPreserved,
        restartReloaded,
        productionMutationExecuted = true,
        existingPlayerRowsTouched = false,
        sensitiveInventoryPayloadRetained = false,
        secretRetained = false,
        connectionStringRetained = false,
        networkBytesEmitted = false,
        fakeNetworkBytes = false
    }, options.OutputPath, options.BaseDirectory);
    return string.Equals(fixtureStatus, "PASS", StringComparison.Ordinal) ? 0 : 2;
}

static async Task<int> RunM2SliceVerificationAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-slice-verification-v1",
            status = "BLOCKED",
            reason = "mariadb.password_missing",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 51;
    }

    try
    {
        await using var connection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await connection.OpenAsync();
        var formalNpc = await QueryRowsAsync(connection, """
            SELECT `Id`, `Code`, `Name`, NULL AS `OriginalName`, `NameZhTw`, `MapId`, `PositionX`, `PositionY`,
                   `NpcType`, `InteractionFamily`, `CoordinateEvidenceStatus`, `ProductionSpawnEnabled`,
                   `OfficialIdentityStatus`, 'formal-runtime-npc' AS `EvidenceStatus`, `LocalizationStatus`, `ContentRecoveryRunId`,
                   '' AS `SourceReference`, '' AS `PayloadSha256`
            FROM `npcs`
            WHERE `Id`=1075128734;
            """);
        var productionIdentity = await QueryRowsAsync(connection, """
            SELECT manifest.`RunId`, manifest.`Domain`, manifest.`AuthorityKey`, manifest.`TargetRowIdentity`,
                   manifest.`NormalizedHash`, manifest.`EvidenceStatus`, manifest.`LocalizationStatus`,
                   validated.`SourceHash`, validated.`NormalizedHash` AS `ValidatedNormalizedHash`,
                   validated.`NormalizedData`, validated.`EvidenceStatus` AS `ValidatedEvidenceStatus`
            FROM `god2_research`.`content_production_manifest` manifest
            JOIN `god2_research`.`content_validated_records` validated
              ON validated.`RunId`=manifest.`RunId`
             AND validated.`Domain`=manifest.`Domain`
             AND validated.`AuthorityKey`=manifest.`AuthorityKey`
            WHERE LOWER(manifest.`Domain`) LIKE '%npc%'
              AND manifest.`TargetRowIdentity`='1075128734'
              AND manifest.`AuthorityKey`='client:npc-template/row-249';
            """);
        var map19 = await QueryRowsAsync(connection, """
            SELECT map.`Id`, map.`Code`, map.`NameZhTw`, map.`Width`, map.`Height`, '' AS `PayloadSha256`,
                   identity.`ClientMapId`, identity.`ClientAreaId`, identity.`ResourceIdentity`,
                   'formal-runtime-identity' AS `IdentityEvidenceStatus`,
                   'formal-runtime-coordinate' AS `CoordinateEvidenceStatus`, identity.`ProductionEnabled`
            FROM `maps` map
            JOIN `client_map_identities` identity ON identity.`MapId`=map.`Id`
            WHERE map.`Id`=557790525 AND identity.`ClientBuildId`='god2-opt-6b127086e0c0';
            """);
        var completeMonsters = await QueryRowsAsync(connection, """
            SELECT monster.`Id`, monster.`Code`, monster.`NameZhTw`, monster.`Level`, monster.`MaxHp`, monster.`MaxMp`,
                   monster.`MpPolicy`, monster.`Attack`, monster.`Defense`, monster.`ExperienceReward`,
                   monster.`CurrencyRewardMinimum`, monster.`CurrencyRewardMaximum`, monster.`DropPolicy`,
                   semantic.`AiFamily`,
                   CASE WHEN semantic.`PhysicalAttack` IS NOT NULL AND semantic.`PhysicalDefense` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END AS `StatEvidenceStatus`,
                   CASE WHEN semantic.`ExperienceReward` IS NOT NULL AND semantic.`CurrencyMinimum` IS NOT NULL AND semantic.`CurrencyMaximum` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END AS `RewardEvidenceStatus`,
                   semantic.`DropPolicyStatus`, spawn.`MapId`, spawn.`PositionX`, spawn.`PositionY`,
                   spawn.`EncounterGroup`, spawn.`FormationGroup`, spawn.`ProductionSpawnEnabled`,
                   (SELECT COUNT(*) FROM `monster_drop_relationships` dropRow
                    WHERE dropRow.`MonsterId`=monster.`Id` AND dropRow.`DropRelationshipStatus` IN ('Verified','Derived')) AS `DropRelationshipCount`
            FROM `monsters` monster
            LEFT JOIN `monster_semantic_profiles` semantic ON semantic.`MonsterId`=monster.`Id`
            LEFT JOIN `monster_spawn_semantics` spawn
              ON spawn.`MonsterId`=monster.`Id` AND spawn.`RunId`=semantic.`RunId`
            WHERE monster.`Level` IS NOT NULL
              AND monster.`MaxHp` IS NOT NULL
              AND monster.`MaxMp` IS NOT NULL
              AND monster.`MpPolicy` IN ('ExplicitOfficialZero','Fixed','Formula')
              AND monster.`Attack` IS NOT NULL
              AND monster.`Defense` IS NOT NULL
              AND monster.`ExperienceReward` IS NOT NULL
              AND spawn.`ProductionSpawnEnabled`=1
              AND semantic.`AiFamily` IS NOT NULL
              AND spawn.`FormationGroup` IS NOT NULL
            ORDER BY monster.`Id`;
            """);
        var promotedNpcTables = await QueryRowsAsync(connection, """
            SELECT tableInfo.`TABLE_NAME` AS `TableName`
            FROM information_schema.`TABLES` tableInfo
            WHERE tableInfo.`TABLE_SCHEMA`=DATABASE()
              AND tableInfo.`TABLE_NAME` IN ('npc_client_identities','npc_spawns')
            ORDER BY tableInfo.`TABLE_NAME`;
            """);
        var npcClientIdentities = promotedNpcTables.Any(row =>
                string.Equals(Convert.ToString(row.GetValueOrDefault("TableName"), System.Globalization.CultureInfo.InvariantCulture), "npc_client_identities", StringComparison.Ordinal))
            ? await QueryRowsAsync(connection, """
                SELECT `NpcId`, `ClientBuildId`, `ResourceType`, `ResourceOrdinal`, `NpcCsvSourceRow`,
                       `ResourceKey`, 'formal-runtime-identity' AS `IdentityEvidenceStatus`, COALESCE(source.`SourceHash`,'') AS `SourceHash`, '' AS `EvidenceReference`
                FROM `npc_client_identities` identity
                LEFT JOIN `god2_research`.`world_identity_source_archive` source
                  ON source.`FormalTable`='npc_client_identities'
                 AND source.`RecordIdentity`=CONCAT(CAST(identity.`NpcId` AS CHAR), ':', identity.`ClientBuildId`)
                ORDER BY identity.`NpcId`, identity.`ClientBuildId`;
                """)
            : [];
        var npcSpawns = promotedNpcTables.Any(row =>
                string.Equals(Convert.ToString(row.GetValueOrDefault("TableName"), System.Globalization.CultureInfo.InvariantCulture), "npc_spawns", StringComparison.Ordinal))
            ? await QueryRowsAsync(connection, """
                SELECT `Id`, `NpcId`, `MapId`, `PositionX`, `PositionY`, `Direction`, `ClientBuildId`,
                       `ObservedClientEntityHandle`, 'formal-runtime-identity' AS `IdentityEvidenceStatus`,
                       'formal-runtime-coordinate' AS `CoordinateEvidenceStatus`,
                       'formal-runtime-service' AS `ServiceEvidenceStatus`, `ProductionEnabled`, COALESCE(source.`SourceHash`,'') AS `SourceHash`, '' AS `EvidenceReference`
                FROM `npc_spawns` spawn
                LEFT JOIN `god2_research`.`world_identity_source_archive` source
                  ON source.`FormalTable`='npc_spawns'
                 AND source.`RecordIdentity`=CAST(spawn.`Id` AS CHAR)
                ORDER BY spawn.`Id`;
                """)
            : [];

        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-slice-verification-v1",
            status = "PASS",
            authority = "MariaDB",
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false,
            formalNpc,
            productionIdentity,
            map19,
            completeMonsterCandidateCount = completeMonsters.Count,
            completeMonsters,
            promotedNpcTables,
            npcClientIdentities,
            npcSpawns
        });
        return 0;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-slice-verification-v1",
            status = "BLOCKED",
            reason = "mariadb.m2_slice_verification_failed",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            queryMode = "ReadOnly",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 52;
    }
}

static async Task<int> RunM2RuntimeSliceValidationAsync(AutomationProbeOptions options)
{
    const int mapId = 557790525;
    const int npcTemplateId = 1075128734;
    int[] expectedPlacementIds = [316049902, 2124220824];
    (int X, int Y)[] expectedCoordinates = [(17, 8), (14, 14)];

    var root = Path.GetFullPath(options.BaseDirectory);
    var paths = new AppPathProvider(root);
    var configuration = new JsonServerConfigurationLoader(paths).Load();
    if (!configuration.Succeeded || configuration.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-runtime-slice-validation-v1",
            status = "BLOCKED",
            reason = configuration.Error.Code,
            diagnostic = configuration.Error.Message,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 51;
    }

    var loader = new MariaDbStaticDataLoader(configuration.Value.Database);
    var loaded = await loader.LoadAsync(CancellationToken.None);
    if (!loaded.Succeeded || loaded.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-runtime-slice-validation-v1",
            status = "BLOCKED",
            reason = loaded.Error.Code,
            diagnostic = loaded.Error.Message,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 52;
    }

    var built = await loader.BuildAsync(loaded.Value, CancellationToken.None);
    if (!built.Succeeded)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-runtime-slice-validation-v1",
            status = "BLOCKED",
            reason = built.Error.Code,
            diagnostic = built.Error.Message,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 53;
    }

    var repository = new MariaDbWorldContentRepository(loader);
    var content = await repository.LoadAsync(CancellationToken.None);
    if (!content.Maps.TryGetValue(mapId, out var map))
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m2-runtime-slice-validation-v1",
            status = "BLOCKED",
            reason = "m2.map19_runtime_missing",
            authority = repository.AuthorityKind.ToString(),
            secretRetained = false,
            connectionStringRetained = false
        });
        return 54;
    }

    var placements = map.NpcPlacements
        .Where(placement => placement.NpcTemplateId == npcTemplateId)
        .OrderBy(placement => placement.PlacementId)
        .ToArray();
    var created = new MapRuntimeFactory().Create(content, mapId);
    var runtimeNpcs = created.Succeeded && created.Value is not null
        ? created.Value.MapRuntime.Objects.OfType<NpcObject>()
            .Where(npc => npc.State.NpcTemplateId == npcTemplateId)
            .OrderBy(npc => npc.State.PlacementId)
            .ToArray()
        : [];

    var placementIdsMatch = placements.Select(placement => placement.PlacementId).Order().SequenceEqual(expectedPlacementIds.Order());
    var placementCoordinatesMatch = placements
        .Select(placement => ((int)placement.Position.X, (int)placement.Position.Y))
        .OrderBy(position => position.Item1)
        .ThenBy(position => position.Item2)
        .SequenceEqual(expectedCoordinates.OrderBy(position => position.X).ThenBy(position => position.Y));
    var runtimeCoordinatesMatch = runtimeNpcs
        .Select(npc => ((int)npc.State.Position.X, (int)npc.State.Position.Y))
        .OrderBy(position => position.Item1)
        .ThenBy(position => position.Item2)
        .SequenceEqual(expectedCoordinates.OrderBy(position => position.X).ThenBy(position => position.Y));
    var semanticsMatch = placements.All(placement =>
        placement.Enabled &&
        string.Equals(placement.Name, "闆滆波鑰侀梿", StringComparison.Ordinal) &&
        string.Equals(placement.Appearance, "data2/rom/npc/npc2643.ROM", StringComparison.Ordinal) &&
        string.Equals(placement.InteractionType, "Merchant", StringComparison.Ordinal));
    var runtimeSemanticsMatch = runtimeNpcs.All(npc =>
        npc.State.Enabled &&
        string.Equals(npc.State.Name, "闆滆波鑰侀梿", StringComparison.Ordinal) &&
        string.Equals(npc.State.Appearance, "data2/rom/npc/npc2643.ROM", StringComparison.Ordinal) &&
        string.Equals(npc.State.InteractionType, "Merchant", StringComparison.Ordinal));
    var passed = repository.AuthorityKind == WorldContentAuthorityKind.MariaDb &&
        placements.Length == 2 &&
        runtimeNpcs.Length == 2 &&
        placementIdsMatch &&
        placementCoordinatesMatch &&
        runtimeCoordinatesMatch &&
        semanticsMatch &&
        runtimeSemanticsMatch &&
        created.Succeeded &&
        created.Value is not null &&
        created.Value.ValidationErrors.Count == 0;

    WriteJson(new
    {
        schemaVersion = "god2-roadmap-m2-runtime-slice-validation-v1",
        status = passed ? "PASS" : "BLOCKED",
        authority = repository.AuthorityKind.ToString(),
        runtimeCatalog = new
        {
            npcSpawnCount = loader.PublishedSnapshot.NpcSpawns.Count,
            mapCount = loader.PublishedSnapshot.Maps.Count,
            m2MapFound = true
        },
        normalizedMap = new
        {
            id = map.MapId,
            map.Name,
            map.SceneId,
            bounds = new { map.Bounds.MinX, map.Bounds.MinY, map.Bounds.MaxX, map.Bounds.MaxY },
            clientIdentity = map.ClientIdentity is null ? null : new
            {
                map.ClientIdentity.ClientMapId,
                map.ClientIdentity.ClientAreaId,
                map.ClientIdentity.ResourceIdentity,
                map.ClientIdentity.CoordinateScaleX,
                map.ClientIdentity.CoordinateScaleY,
                map.ClientIdentity.CoordinateOffsetX,
                map.ClientIdentity.CoordinateOffsetY,
                map.ClientIdentity.ClientBuildId,
                map.ClientIdentity.IdentityEvidenceStatus
            }
        },
        repositoryPlacements = placements.Select(placement => new
        {
            placement.PlacementId,
            placement.NpcTemplateId,
            placement.MapId,
            x = placement.Position.X,
            y = placement.Position.Y,
            placement.Name,
            placement.Appearance,
            placement.InteractionType,
            placement.Enabled,
            placement.Source
        }),
        runtimeObjects = runtimeNpcs.Select(npc => new
        {
            npc.State.PlacementId,
            npc.State.NpcTemplateId,
            npc.State.MapId,
            x = npc.State.Position.X,
            y = npc.State.Position.Y,
            npc.State.Name,
            npc.State.Appearance,
            npc.State.InteractionType,
            npc.State.Enabled
        }),
        assertions = new
        {
            placementIdsMatch,
            placementCoordinatesMatch,
            runtimeCoordinatesMatch,
            semanticsMatch,
            runtimeSemanticsMatch,
            mapRuntimeCreated = created.Succeeded,
            validationErrors = created.Value?.ValidationErrors ?? []
        },
        networkEmission = 0,
        fakeNetworkBytes = 0,
        manualOperation = false,
        newCapture = false,
        secretRetained = false,
        connectionStringRetained = false
    });
    return passed ? 0 : 55;
}

static async Task<int> RunM3NpcReplicationValidationAsync(AutomationProbeOptions options)
{
    const int mapId = 557790525;
    const int npcTemplateId = 1075128734;
    var root = Path.GetFullPath(options.BaseDirectory);
    var paths = new AppPathProvider(root);
    var configuration = new JsonServerConfigurationLoader(paths).Load();
    if (!configuration.Succeeded || configuration.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m3-npc-replication-validation-v1",
            status = "BLOCKED",
            reason = configuration.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 61;
    }

    var loader = new MariaDbStaticDataLoader(configuration.Value.Database);
    var loaded = await loader.LoadAsync(CancellationToken.None);
    if (!loaded.Succeeded || loaded.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m3-npc-replication-validation-v1",
            status = "BLOCKED",
            reason = loaded.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 62;
    }

    var built = await loader.BuildAsync(loaded.Value, CancellationToken.None);
    if (!built.Succeeded)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m3-npc-replication-validation-v1",
            status = "BLOCKED",
            reason = built.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 63;
    }

    var repository = new MariaDbWorldContentRepository(loader);
    var content = await repository.LoadAsync(CancellationToken.None);
    if (!content.Maps.ContainsKey(mapId))
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m3-npc-replication-validation-v1",
            status = "BLOCKED",
            reason = "m3.map19_runtime_missing",
            authority = repository.AuthorityKind.ToString(),
            secretRetained = false,
            connectionStringRetained = false
        });
        return 64;
    }

    var created = new MapRuntimeFactory().Create(content, mapId);
    var runtimeNpcs = created.Succeeded && created.Value is not null
        ? created.Value.MapRuntime.Objects.OfType<NpcObject>()
            .Where(npc => npc.State.NpcTemplateId == npcTemplateId)
            .OrderBy(npc => npc.State.PlacementId)
            .ToArray()
        : [];
    var serializerResults = runtimeNpcs.Select(npc =>
    {
        var result = new NpcSerializer().Serialize(npc.State);
        var decoded = result.Status == OfficialSerializerStatus.Ready
            ? OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(result.Frame)
            : [];
        var identity = npc.State.WireIdentity;
        return new
        {
            npc.State.PlacementId,
            identity?.EntityHandle,
            identity?.ClientBuildId,
            identity?.EvidenceStatus,
            identity?.SpawnMessageSha256,
            serializerStatus = result.Status.ToString(),
            encodedFrameLength = result.Frame.Length,
            decodedOpcode = decoded.Length > 2 ? $"0x{decoded[2]:X2}" : "NONE",
            decodedApplicationSha256 = decoded.Length == 24
                ? Convert.ToHexString(SHA256.HashData(decoded.AsSpan(2, 21)))
                : null,
            decodedX = decoded.Length == 24
                ? (int)((BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(19, 4)) >> 2) & 0x7FFF)
                : (int?)null,
            decodedY = decoded.Length == 24
                ? (int)(BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(19, 4)) >> 17)
                : (int?)null,
            checksumValid = decoded.Length > 0 && decoded[^1] == OfficialLoginWireTransform.ComputeChecksum(decoded),
            secretRetained = false
        };
    }).ToArray();

    var bootstrapCharacter = new CharacterSummary(
        0x035F7FF4,
        1,
        "RoadHero",
        "Class1",
        "Gender1",
        "LifeSkill1",
        1,
        "Default",
        19,
        28,
        34,
        "Active",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);
    var legacyBootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(bootstrapCharacter);
    var authoritativeBootstrap = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(bootstrapCharacter);
    RuntimeReplicationEmissionResult? headlessEmission = null;
    RuntimeReplicationEmissionResult? headlessPreflight = null;
    if (created.Succeeded && created.Value is not null)
    {
        var mapSession = new MapSession(
            "m3-headless-session",
            mapId,
            bootstrapCharacter.CharacterId,
            0x70000000 + bootstrapCharacter.CharacterId,
            WorldContentMode.MariaDbAuthoritative);
        created.Value.MapRuntime.Replication.RecalculateInitialVisibility(
            mapSession,
            new WorldPosition3(28, 34),
            created.Value.MapRuntime.Objects.ActiveObjects,
            24,
            DateTimeOffset.UnixEpoch);
        var emitter = new RuntimeReplicationEmitter();
        headlessPreflight = emitter.PreflightSpawnQueue(created.Value.MapRuntime, mapSession.SessionId);
        headlessEmission = await emitter.FlushSpawnQueueAsync(
            created.Value.MapRuntime,
            mapSession.SessionId,
            new RecordingRuntimeFrameSender(),
            CancellationToken.None);
    }
    var passed = repository.AuthorityKind == WorldContentAuthorityKind.MariaDb &&
        runtimeNpcs.Length == 2 &&
        serializerResults.All(result =>
            result.serializerStatus == OfficialSerializerStatus.Ready.ToString() &&
            result.encodedFrameLength == 24 &&
            result.decodedOpcode == "0x72" &&
            result.checksumValid &&
            string.Equals(result.SpawnMessageSha256, result.decodedApplicationSha256, StringComparison.OrdinalIgnoreCase)) &&
        authoritativeBootstrap.Length == legacyBootstrap.Length - 84 &&
        headlessPreflight is { BlockedFrames: 0 } &&
        headlessEmission is { SentFrames: 2, BlockedFrames: 0 };

    WriteJson(new
    {
        schemaVersion = "god2-roadmap-m3-npc-replication-validation-v1",
        status = passed ? "PASS" : "BLOCKED",
        authority = repository.AuthorityKind.ToString(),
        repository = nameof(MariaDbWorldContentRepository),
        migration = "041",
        clientBuildId = OfficialNpcReplicationWireCodec.ClientBuildId,
        clientSha256 = OfficialNpcReplicationWireCodec.ClientSha256,
        runtimeNpcCount = runtimeNpcs.Length,
        serializerResults,
        bootstrap = new
        {
            legacyLength = legacyBootstrap.Length,
            authoritativeLength = authoritativeBootstrap.Length,
            removedLegacyEntityBytes = legacyBootstrap.Length - authoritativeBootstrap.Length,
            legacyWorldEntitiesExcluded = authoritativeBootstrap.Length == legacyBootstrap.Length - 84
        },
        replicationQueue = new
        {
            initialSnapshotPolicy = "BuildPinnedVerifiedNpcOnly",
            preflightBlocked = headlessPreflight?.BlockedFrames,
            headlessFramesSent = headlessEmission?.SentFrames,
            headlessFramesBlocked = headlessEmission?.BlockedFrames,
            remainingSpawnQueue = created.Value?.MapRuntime.Replication.SpawnQueue.Count
        },
        monsterReplicationStatus = "BLOCKED_BY_EVIDENCE",
        transactionalOutboxStatus = "NOT_YET_DURABLE",
        officialClientAcceptance = "NOT_RUN",
        externalNetworkEmission = 0,
        fakeNetworkBytes = 0,
        manualOperation = false,
        newCapture = false,
        secretRetained = false,
        connectionStringRetained = false
    });
    Array.Clear(legacyBootstrap);
    Array.Clear(authoritativeBootstrap);
    return passed ? 0 : 65;
}

static async Task<int> RunM4NpcInteractionValidationAsync(AutomationProbeOptions options)
{
    const int mapId = 557790525;
    const ushort merchantNpcHandle = 1504;
    var root = Path.GetFullPath(options.BaseDirectory);
    var paths = new AppPathProvider(root);
    var configuration = new JsonServerConfigurationLoader(paths).Load();
    if (!configuration.Succeeded || configuration.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m4-npc-interaction-validation-v1",
            status = "BLOCKED",
            reason = configuration.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 66;
    }

    var loader = new MariaDbStaticDataLoader(configuration.Value.Database);
    var loaded = await loader.LoadAsync(CancellationToken.None);
    if (!loaded.Succeeded || loaded.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m4-npc-interaction-validation-v1",
            status = "BLOCKED",
            reason = loaded.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 67;
    }

    var built = await loader.BuildAsync(loaded.Value, CancellationToken.None);
    if (!built.Succeeded)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m4-npc-interaction-validation-v1",
            status = "BLOCKED",
            reason = built.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 68;
    }

    var promotedContent = new MariaDbPromotedGameplayContentRuntime(configuration.Value.Database, loader);
    var promotedBuilt = await promotedContent.BuildAsync(loaded.Value, CancellationToken.None);
    if (!promotedBuilt.Succeeded)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m4-npc-interaction-validation-v1",
            status = "BLOCKED",
            reason = promotedBuilt.Error.Code,
            authority = "MariaDB",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 68;
    }

    IWorldSessionCoordinator worldSessions = new MariaDbWorldSessionCoordinator(loader, promotedContent);
    var session = RuntimeSession.Connected("m4-physical-session", "127.0.0.1:1000", DateTimeOffset.UnixEpoch) with
    {
        AccountId = 1,
        CharacterId = 1,
        IsAuthenticated = true,
        ProtocolStage = ProtocolStage.WorldEntering
    };
    var character = new CharacterSummary(
        1,
        1,
        "M4Probe",
        "Class1",
        "Gender1",
        "LifeSkill1",
        1,
        "Default",
        mapId,
        17,
        11,
        "Active",
        DateTimeOffset.UnixEpoch,
        null);
    var bound = await worldSessions.BindAsync(session, character, CancellationToken.None);
    if (!bound.Succeeded || bound.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m4-npc-interaction-validation-v1",
            status = "BLOCKED",
            reason = bound.Error.Code,
            authority = worldSessions.AuthorityKind.ToString(),
            repository = nameof(MariaDbWorldContentRepository),
            secretRetained = false,
            connectionStringRetained = false
        });
        return 69;
    }

    session = session with { ProtocolStage = ProtocolStage.InWorld };
    var sessionUpdated = worldSessions.UpdateSession(session);
    var runtimeNpc = bound.Value.MapRuntime.Objects.OfType<NpcObject>()
        .SingleOrDefault(npc => npc.State.WireIdentity?.EntityHandle == merchantNpcHandle);
    var merchant = runtimeNpc?.State.MerchantId is int merchantId
        ? bound.Value.MapRuntime.Definition.MerchantMappings.SingleOrDefault(candidate =>
            candidate.MerchantId == merchantId &&
            candidate.NpcTemplateId == runtimeNpc.State.NpcTemplateId)
        : null;
    var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(worldSessions);
    var openFrame = EncodeM4InteractionFrame(OfficialNpcInteractionWireCodec.OpenOpcode, merchantNpcHandle);
    var closeFrame = EncodeM4InteractionFrame(OfficialNpcInteractionWireCodec.MerchantCloseOpcode, merchantNpcHandle);
    var opened = await loop.ExecuteFrameAsync(
        session.SessionId,
        1,
        "m4-physical-open",
        openFrame,
        CancellationToken.None);
    var closed = await loop.ExecuteFrameAsync(
        session.SessionId,
        2,
        "m4-physical-close",
        closeFrame,
        CancellationToken.None);
    var replay = await loop.ExecuteFrameAsync(
        session.SessionId,
        3,
        "m4-physical-close-replay",
        closeFrame,
        CancellationToken.None);
    var decodedResponse = opened.EncodedResponse.IsEmpty
        ? []
        : OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(opened.EncodedResponse.Span);
    var passed = worldSessions.AuthorityKind == WorldContentAuthorityKind.MariaDb &&
        sessionUpdated.Succeeded &&
        runtimeNpc is not null &&
        merchant is not null &&
        opened.Code == OfficialNpcInteractionTransactionCode.MerchantOpened &&
        opened.RuntimeValidated &&
        string.Equals(opened.Handler, "Merchant", StringComparison.Ordinal) &&
        decodedResponse.Length == 58 && decodedResponse[2] == 0x7A &&
        decodedResponse[^1] == OfficialLoginWireTransform.ComputeChecksum(decodedResponse) &&
        closed.Code == OfficialNpcInteractionTransactionCode.InteractionClosed &&
        closed.RuntimeValidated && closed.EncodedResponse.IsEmpty &&
        string.Equals(replay.FailureCode, "interaction.close_without_active_state", StringComparison.Ordinal) &&
        replay.EncodedResponse.IsEmpty;

    WriteJson(new
    {
        schemaVersion = "god2-roadmap-m4-npc-interaction-validation-v1",
        status = passed ? "PASS" : "BLOCKED",
        authority = worldSessions.AuthorityKind.ToString(),
        repository = nameof(MariaDbWorldContentRepository),
        clientBuildId = OfficialNpcInteractionWireCodec.ClientBuildId,
        mapId,
        merchantNpc = new
        {
            present = runtimeNpc is not null,
            placementId = runtimeNpc?.State.PlacementId,
            npcTemplateId = runtimeNpc?.State.NpcTemplateId,
            entityHandle = runtimeNpc?.State.WireIdentity?.EntityHandle,
            merchantId = runtimeNpc?.State.MerchantId,
            merchantBindingResolved = merchant is not null,
            source = runtimeNpc?.Identity.Source
        },
        open = new
        {
            code = opened.Code.ToString(),
            opened.FailureCode,
            opened.Handler,
            opened.RuntimeValidated,
            responseOpcode = decodedResponse.Length > 2 ? $"0x{decodedResponse[2]:X2}" : "NONE",
            responseLength = decodedResponse.Length,
            opened.ResponseApplicationSha256
        },
        close = new
        {
            code = closed.Code.ToString(),
            closed.FailureCode,
            closed.Handler,
            closed.RuntimeValidated,
            responseLength = closed.EncodedResponse.Length,
            replayFailureCode = replay.FailureCode,
            replayResponseLength = replay.EncodedResponse.Length
        },
        ordinaryMovementPositionAuthority = "FAIL_CLOSED_UNTIL_COORDINATE_TRANSFORM_IS_EVIDENCE_PROVEN",
        officialClientAcceptance = "NOT_RUN",
        externalNetworkEmission = 0,
        fakeNetworkBytes = 0,
        manualOperation = false,
        newCapture = false,
        secretRetained = false,
        connectionStringRetained = false
    });
    Array.Clear(openFrame);
    Array.Clear(closeFrame);
    Array.Clear(decodedResponse);
    worldSessions.Unbind(session.SessionId);
    return passed ? 0 : 70;
}

static async Task<int> RunM4WorldMovementValidationAsync(AutomationProbeOptions options)
{
    var root = Path.GetFullPath(options.BaseDirectory);
    var configuration = new JsonServerConfigurationLoader(new AppPathProvider(root)).Load();
    if (!configuration.Succeeded || configuration.Value is null)
    {
        WriteJson(new
        {
            schemaVersion = "god2-roadmap-m4-world-movement-validation-v1",
            status = "BLOCKED",
            reason = configuration.Error.Code,
            authority = "MariaDB",
            fixtureCleanup = "NOT_REQUIRED",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 70;
    }

    var database = configuration.Value.Database;
    var fixtureIdentity = Guid.NewGuid().ToString("N");
    var fixtureLogin = $"fixture_move_{fixtureIdentity[..12]}";
    var fixtureCharacterName = $"Mv{fixtureIdentity[..10]}";
    long? accountId = null;
    long? characterId = null;
    var cleanupStatus = "NOT_RUN";
    string status;
    string reason;
    PortalLocationState? before = null;
    PortalLocationState? after = null;
    OperationResult? committed = null;
    OperationResult? staleReplay = null;
    OperationResult<WorldSessionBinding>? worldRebind = null;
    int? databaseMap19 = null;
    int? databaseMap3 = null;
    try
    {
        await using (var connection = new MySqlConnection(BuildConfiguredDatabaseConnectionString(database)))
        {
            await connection.OpenAsync();
            await using (var account = connection.CreateCommand())
            {
                account.CommandText = """
                    INSERT INTO `accounts`
                        (`LoginName`, `PasswordHash`, `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`)
                    VALUES
                        (@loginName, @passwordHash, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @concurrencyToken);
                    """;
                account.Parameters.AddWithValue("@loginName", fixtureLogin);
                account.Parameters.AddWithValue("@passwordHash", "fixture-hash-v1");
                account.Parameters.AddWithValue("@concurrencyToken", fixtureIdentity);
                await account.ExecuteNonQueryAsync();
                accountId = account.LastInsertedId;
            }

            await using (var character = connection.CreateCommand())
            {
                character.CommandText = """
                    INSERT INTO `characters`
                        (`AccountId`, `Name`, `MapId`, `PositionX`, `PositionY`, `Level`,
                         `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`)
                    VALUES
                        (@accountId, @name, @mapId, @positionX, @positionY, 1,
                         UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), @concurrencyToken);
                    """;
                character.Parameters.AddWithValue("@accountId", accountId.Value);
                character.Parameters.AddWithValue("@name", fixtureCharacterName);
                character.Parameters.AddWithValue("@mapId", 19);
                character.Parameters.AddWithValue("@positionX", 17);
                character.Parameters.AddWithValue("@positionY", 12);
                character.Parameters.AddWithValue("@concurrencyToken", fixtureIdentity);
                await character.ExecuteNonQueryAsync();
                characterId = character.LastInsertedId;
            }
        }

        var store = new MariaDbPortalTransitionStore(database);
        before = await store.LoadAsync(characterId.Value, CancellationToken.None);
        var request = new WorldMovementPersistenceRequest(
            characterId.Value,
            19,
            new WorldPosition3(17, 12),
            new WorldPosition3(17, 11),
            WorldDirection.North,
            DateTimeOffset.UtcNow);
        committed = await store.CommitMovementAsync(request, CancellationToken.None);
        after = await store.LoadAsync(characterId.Value, CancellationToken.None);
        staleReplay = await store.CommitMovementAsync(request, CancellationToken.None);

        var staticData = new MariaDbStaticDataLoader(database);
        var loaded = await staticData.LoadAsync(CancellationToken.None);
        if (!loaded.Succeeded || loaded.Value is null ||
            !(await staticData.BuildAsync(loaded.Value, CancellationToken.None)).Succeeded)
        {
            throw new InvalidOperationException("MariaDB runtime content could not be loaded for portal rebinding validation.");
        }

        var content = await new MariaDbWorldContentRepository(staticData).LoadAsync(CancellationToken.None);
        var map19 = content.Maps.Values.SingleOrDefault(map =>
            map.ClientIdentity is { ClientMapId: 19, ProductionEnabled: true });
        var map3 = content.Maps.Values.SingleOrDefault(map =>
            map.ClientIdentity is { ClientMapId: 3, ProductionEnabled: true });
        databaseMap19 = map19?.MapId;
        databaseMap3 = map3?.MapId;
        if (map19 is null || map3 is null)
        {
            throw new InvalidOperationException("Promoted MariaDB map identities 19 and 3 are required for portal rebinding validation.");
        }

        var promotedContent = new MariaDbPromotedGameplayContentRuntime(database, staticData);
        var promotedBuilt = await promotedContent.BuildAsync(loaded.Value, CancellationToken.None);
        if (!promotedBuilt.Succeeded)
        {
            throw new InvalidOperationException($"Active gameplay content release could not be loaded for portal rebinding validation: {promotedBuilt.Error.Code}.");
        }

        var worldSessions = new MariaDbWorldSessionCoordinator(staticData, promotedContent);
        var entering = RuntimeSession.Connected("m4-movement-physical-session", "127.0.0.1:1000", DateTimeOffset.UnixEpoch) with
        {
            AccountId = accountId.Value,
            CharacterId = characterId.Value,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var runtimeCharacter = new CharacterSummary(
            characterId.Value,
            accountId.Value,
            fixtureCharacterName,
            "Class1",
            "Gender1",
            "LifeSkill1",
            1,
            "Default",
            map19.MapId,
            17,
            11,
            "Active",
            DateTimeOffset.UnixEpoch,
            null);
        var runtimeBound = await worldSessions.BindAsync(entering, runtimeCharacter, CancellationToken.None);
        var inWorld = entering with { ProtocolStage = ProtocolStage.InWorld };
        if (!runtimeBound.Succeeded || !worldSessions.UpdateSession(inWorld).Succeeded)
        {
            throw new InvalidOperationException("MariaDB Map 19 runtime binding failed during portal rebinding validation.");
        }

        worldRebind = await worldSessions.RebindAsync(
            inWorld,
            runtimeCharacter with { MapId = map3.MapId, PositionX = 196, PositionY = 139 },
            CancellationToken.None);
        worldSessions.Unbind(inWorld.SessionId);

        var passed = before.CurrentMapId == 19 &&
            before.RawPosition == new WorldPosition3(17, 12) &&
            committed.Succeeded &&
            after.CurrentMapId == 19 &&
            after.RawPosition == new WorldPosition3(17, 11) &&
            after.Direction == WorldDirection.North &&
            after.RuntimeVersion == checked(before.RuntimeVersion + 1) &&
            !staleReplay.Succeeded &&
            string.Equals(staleReplay.Error.Code, "movement.position_conflict", StringComparison.Ordinal) &&
            worldRebind.Succeeded &&
            worldRebind.Value is { MapSession.MapId: var reboundMapId } && reboundMapId == map3.MapId;
        status = passed ? "PASS" : "BLOCKED";
        reason = passed ? "NONE" : "movement.physical_assertion_failed";
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        status = "BLOCKED";
        reason = $"movement.physical_exception.{exception.GetType().Name}";
    }
    finally
    {
        try
        {
            await using var connection = new MySqlConnection(BuildConfiguredDatabaseConnectionString(database));
            await connection.OpenAsync();
            var characterCleanupPassed = true;
            if (characterId is not null)
            {
                await using var deleteCharacter = connection.CreateCommand();
                deleteCharacter.CommandText = """
                    DELETE FROM `characters`
                    WHERE `Id` = @characterId AND `AccountId` = @accountId AND `Name` = @characterName;
                    """;
                deleteCharacter.Parameters.AddWithValue("@characterId", characterId.Value);
                deleteCharacter.Parameters.AddWithValue("@accountId", accountId!.Value);
                deleteCharacter.Parameters.AddWithValue("@characterName", fixtureCharacterName);
                characterCleanupPassed = await deleteCharacter.ExecuteNonQueryAsync() == 1;
            }

            var accountCleanupPassed = true;
            if (accountId is not null)
            {
                await using var deleteAccount = connection.CreateCommand();
                deleteAccount.CommandText = """
                    DELETE FROM `accounts`
                    WHERE `Id` = @accountId AND `LoginName` = @loginName;
                    """;
                deleteAccount.Parameters.AddWithValue("@accountId", accountId.Value);
                deleteAccount.Parameters.AddWithValue("@loginName", fixtureLogin);
                accountCleanupPassed = await deleteAccount.ExecuteNonQueryAsync() == 1;
            }

            cleanupStatus = characterCleanupPassed && accountCleanupPassed ? "PASS" : "FAILED";
        }
        catch
        {
            cleanupStatus = "FAILED";
        }
    }

    if (!string.Equals(cleanupStatus, "PASS", StringComparison.Ordinal))
    {
        status = "BLOCKED";
        reason = "movement.fixture_cleanup_failed";
    }

    WriteJson(new
    {
        schemaVersion = "god2-roadmap-m4-world-movement-validation-v1",
        status,
        reason,
        authority = "MariaDb",
        repository = nameof(MariaDbPortalTransitionStore),
        movementStore = nameof(IWorldMovementStore),
        fixtureNamespace = "ephemeral-account-character-by-scoped-identity",
        fixtureCleanup = cleanupStatus,
        before = before is null ? null : new
        {
            before.CurrentMapId,
            x = before.RawPosition.X,
            y = before.RawPosition.Y,
            before.Direction,
            before.RuntimeVersion
        },
        commit = new
        {
            succeeded = committed?.Succeeded,
            failureCode = committed?.Error.Code
        },
        after = after is null ? null : new
        {
            after.CurrentMapId,
            x = after.RawPosition.X,
            y = after.RawPosition.Y,
            after.Direction,
            after.RuntimeVersion
        },
        staleReplay = new
        {
            rejected = staleReplay is not null && !staleReplay.Succeeded,
            failureCode = staleReplay?.Error.Code
        },
        postPortalWorldRebind = new
        {
            succeeded = worldRebind?.Succeeded,
            failureCode = worldRebind?.Error.Code,
            databaseMap19,
            databaseMap3,
            destinationX = 196,
            destinationY = 139,
            sourcePlayerRemoved = worldRebind?.Succeeded == true,
            destinationPlayerCreated = worldRebind?.Succeeded == true
        },
        externalNetworkEmission = 0,
        fakeNetworkBytes = 0,
        manualOperation = false,
        newCapture = false,
        secretRetained = false,
        connectionStringRetained = false
    });
    return string.Equals(status, "PASS", StringComparison.Ordinal) ? 0 : 71;
}

static byte[] EncodeM4InteractionFrame(byte opcode, ushort entityHandle)
{
    var decoded = new byte[OfficialNpcInteractionWireCodec.CloseFrameLength];
    BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)decoded.Length));
    decoded[2] = opcode;
    BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(3), entityHandle);
    decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
    var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decoded);
    Array.Clear(decoded);
    return encoded;
}

static async Task<List<Dictionary<string, object?>>> QueryRowsAsync(MySqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }
        rows.Add(row);
    }
    return rows;
}

static async Task<HashSet<int>> QueryInt32SetAsync(MySqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    var values = new HashSet<int>();
    while (await reader.ReadAsync())
    {
        if (!reader.IsDBNull(0))
        {
            values.Add(Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    return values;
}

static async Task<HashSet<RoadMapM5MonsterSpawnIdentity>> QueryM5MonsterSpawnIdentitySetAsync(
    MySqlConnection connection,
    string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    var values = new HashSet<RoadMapM5MonsterSpawnIdentity>();
    while (await reader.ReadAsync())
    {
        if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
        {
            throw new InvalidDataException("M5 eligible Monster spawn identity contains a NULL required field.");
        }

        values.Add(new RoadMapM5MonsterSpawnIdentity(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3)));
    }

    return values;
}

static async Task<int> RunM1CharacterPositionCasAsync(AutomationProbeOptions options)
{
    var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(password))
    {
        WriteJson(new
        {
            schemaVersion = "god2-m1-character-position-cas-v1",
            status = "BLOCKED",
            failureCode = "mariadb.password_missing",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 51;
    }

    if (options.CharacterId <= 0 ||
        options.ExpectedCharacterMapId < 0 || options.ExpectedCharacterPositionX < 0 || options.ExpectedCharacterPositionY < 0 ||
        options.TargetCharacterMapId < 0 || options.TargetCharacterPositionX < 0 || options.TargetCharacterPositionY < 0)
    {
        WriteJson(new
        {
            schemaVersion = "god2-m1-character-position-cas-v1",
            status = "BLOCKED",
            failureCode = "m1.position.arguments_invalid",
            secretRetained = false,
            connectionStringRetained = false
        });
        return 52;
    }

    try
    {
        await using var connection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT characterRow.`Id`, characterRow.`Name`, characterRow.`MapId`,
                   characterRow.`PositionX`, characterRow.`PositionY`, characterRow.`ConcurrencyToken`,
                   accountRow.`CurrentSessionId`, map.`Width`, map.`Height`,
                   identity.`ClientMapId`, identity.`ClientAreaId`, identity.`ResourceIdentity`,
                   identity.`CoordinateScaleX`, identity.`CoordinateScaleY`,
                   identity.`CoordinateOffsetX`, identity.`CoordinateOffsetY`,
                   'formal-runtime-identity' AS `IdentityEvidenceStatus`,
                   'formal-runtime-coordinate' AS `CoordinateEvidenceStatus`,
                   identity.`ProductionEnabled`
            FROM `characters` characterRow
            JOIN `accounts` accountRow ON accountRow.`Id` = characterRow.`AccountId`
            JOIN `maps` map ON map.`Id` = characterRow.`MapId`
            JOIN `client_map_identities` identity
              ON identity.`MapId` = characterRow.`MapId`
             AND identity.`ClientBuildId` = 'god2-opt-6b127086e0c0'
            WHERE characterRow.`Id` = @characterId
              AND characterRow.`Status` <> 'Deleted'
            LIMIT 1
            FOR UPDATE;
            """;
        select.Parameters.AddWithValue("@characterId", options.CharacterId);
        await using var reader = await select.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await transaction.RollbackAsync();
            WriteJson(new
            {
                schemaVersion = "god2-m1-character-position-cas-v1",
                status = "BLOCKED",
                failureCode = "m1.position.character_or_identity_missing",
                characterId = options.CharacterId,
                secretRetained = false,
                connectionStringRetained = false
            });
            return 53;
        }

        var characterName = reader.GetString("Name");
        var currentMapId = reader.GetInt32("MapId");
        var currentX = reader.GetInt32("PositionX");
        var currentY = reader.GetInt32("PositionY");
        var concurrencyToken = reader.GetString("ConcurrencyToken");
        var sessionActive = !reader.IsDBNull(reader.GetOrdinal("CurrentSessionId")) &&
                            !string.IsNullOrWhiteSpace(reader.GetString("CurrentSessionId"));
        var width = reader.GetInt32("Width");
        var height = reader.GetInt32("Height");
        var clientMapId = checked((ushort)reader.GetInt32("ClientMapId"));
        var clientAreaId = checked((byte)reader.GetInt32("ClientAreaId"));
        var resourceIdentity = reader.GetString("ResourceIdentity");
        var identityVerified = reader.GetBoolean("ProductionEnabled") &&
                               reader.GetDecimal("CoordinateScaleX") == 1m &&
                               reader.GetDecimal("CoordinateScaleY") == 1m &&
                               reader.GetDecimal("CoordinateOffsetX") == 0m &&
                               reader.GetDecimal("CoordinateOffsetY") == 0m;
        await reader.DisposeAsync();

        var expectedMatches = currentMapId == options.ExpectedCharacterMapId &&
                              currentX == options.ExpectedCharacterPositionX &&
                              currentY == options.ExpectedCharacterPositionY;
        var targetUsesLockedMap = options.TargetCharacterMapId == currentMapId;
        var maxX = checked((width * OfficialClientWorldCoordinateGrid.UnitsPerResourceCell) - 1);
        var maxY = checked((height * OfficialClientWorldCoordinateGrid.UnitsPerResourceCell) - 1);
        var targetInBounds = options.TargetCharacterPositionX <= Math.Min(maxX, OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate) &&
                             options.TargetCharacterPositionY <= Math.Min(maxY, OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate);
        if (sessionActive || !identityVerified || !expectedMatches || !targetUsesLockedMap || !targetInBounds)
        {
            await transaction.RollbackAsync();
            WriteJson(new
            {
                schemaVersion = "god2-m1-character-position-cas-v1",
                status = "BLOCKED",
                failureCode = sessionActive ? "m1.position.session_active" :
                    !identityVerified ? "m1.position.identity_not_verified" :
                    !expectedMatches ? "m1.position.expected_state_mismatch" :
                    !targetUsesLockedMap ? "m1.position.target_map_not_locked" :
                    "m1.position.target_out_of_bounds",
                characterId = options.CharacterId,
                observed = new { mapId = currentMapId, x = currentX, y = currentY },
                mapIdentity = new { clientMapId, clientAreaId, resourceIdentity, width, height, maxX, maxY },
                secretRetained = false,
                connectionStringRetained = false
            });
            return 54;
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE `characters`
            SET `MapId` = @targetMapId,
                `PositionX` = @targetX,
                `PositionY` = @targetY,
                `UpdatedAtUtc` = UTC_TIMESTAMP(6),
                `ConcurrencyToken` = REPLACE(UUID(), '-', '')
            WHERE `Id` = @characterId
              AND `Status` <> 'Deleted'
              AND `MapId` = @expectedMapId
              AND `PositionX` = @expectedX
              AND `PositionY` = @expectedY
              AND `ConcurrencyToken` = @concurrencyToken;
            """;
        update.Parameters.AddWithValue("@targetMapId", options.TargetCharacterMapId);
        update.Parameters.AddWithValue("@targetX", options.TargetCharacterPositionX);
        update.Parameters.AddWithValue("@targetY", options.TargetCharacterPositionY);
        update.Parameters.AddWithValue("@characterId", options.CharacterId);
        update.Parameters.AddWithValue("@expectedMapId", options.ExpectedCharacterMapId);
        update.Parameters.AddWithValue("@expectedX", options.ExpectedCharacterPositionX);
        update.Parameters.AddWithValue("@expectedY", options.ExpectedCharacterPositionY);
        update.Parameters.AddWithValue("@concurrencyToken", concurrencyToken);
        if (await update.ExecuteNonQueryAsync() != 1)
        {
            await transaction.RollbackAsync();
            WriteJson(new
            {
                schemaVersion = "god2-m1-character-position-cas-v1",
                status = "BLOCKED",
                failureCode = "m1.position.concurrent_change",
                characterId = options.CharacterId,
                secretRetained = false,
                connectionStringRetained = false
            });
            return 55;
        }

        await transaction.CommitAsync();
        WriteJson(new
        {
            schemaVersion = "god2-m1-character-position-cas-v1",
            status = "PASS",
            repository = "MariaDB.characters",
            characterId = options.CharacterId,
            characterName,
            before = new { mapId = currentMapId, x = currentX, y = currentY },
            after = new
            {
                mapId = options.TargetCharacterMapId,
                x = options.TargetCharacterPositionX,
                y = options.TargetCharacterPositionY
            },
            mapIdentity = new { clientMapId, clientAreaId, resourceIdentity, width, height, maxX, maxY },
            compareAndSwap = true,
            sessionInactive = true,
            secretRetained = false,
            connectionStringRetained = false
        });
        return 0;
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException or OverflowException)
    {
        WriteJson(new
        {
            schemaVersion = "god2-m1-character-position-cas-v1",
            status = "BLOCKED",
            failureCode = "m1.position.database_failure",
            diagnostic = Redact(exception.Message, options.PasswordEnvironmentVariable),
            secretRetained = false,
            connectionStringRetained = false
        });
        return 56;
    }
}

static async Task<int> RunMapIdentityEvidenceHostAsync(AutomationProbeOptions options)
{
    var root = Path.GetFullPath(options.BaseDirectory);
    var stopFile = Path.GetFullPath(options.StopFile);
    var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!stopFile.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("The evidence-host stop file must remain under the repository root.");
        return 41;
    }

    var paths = new AppPathProvider(root);
    var configurationResult = new JsonServerConfigurationLoader(paths).Load();
    if (!configurationResult.Succeeded || configurationResult.Value is null)
    {
        Console.Error.WriteLine($"Configuration failed: {configurationResult.Error.Code}");
        return 42;
    }

    using var cancellation = new CancellationTokenSource();
    var stopWatcher = WatchStopFileAsync(stopFile, cancellation);
    var configuration = configurationResult.Value;
    var evidenceLocation = MapIdentityEvidenceLocation.Resolve(
        options.EvidenceClientMapId,
        options.EvidencePositionX,
        options.EvidencePositionY);
    var accountRepository = new MariaDbAccountRepository(configuration.Database);
    var characterRepository = new MariaDbCharacterRepository(configuration.Database);
    var runtime = UnifiedRuntimeComposition.CreateProduction(
        configuration.Server.MaximumPlayers,
        accountRepository,
        characterRepository);
    var staticDataCache = new MariaDbStaticDataLoader(configuration.Database);
    IWorldSessionCoordinator worldSessions = new MapIdentityEvidenceWorldSessionCoordinator(evidenceLocation);
    IWorldBootstrapProjector worldProjector = new MapIdentityEvidenceWorldBootstrapProjector(
        evidenceLocation,
        options.EvidenceBootstrapPlayerIdentity);
    var evidenceMovementStore = new InMemoryPortalTransitionStore();
    var evidencePortalRuntime = new MapIdentityEvidencePortalRuntime(runtime.SessionAuthority, evidenceLocation);
    var portalClosedLoop = new OfficialPortalClosedLoop(evidencePortalRuntime, evidencePortalRuntime);
    var networkHost = new TcpNetworkHost(
        runtime.PacketFactory,
        runtime.ProtocolConnectionRuntime,
        runtime.SessionAuthority,
        runtime.AuthenticationService,
        runtime.CharacterListQuery,
        portalTransitionStore: evidenceMovementStore,
        worldSessionCoordinator: worldSessions,
        worldBootstrapProjector: worldProjector,
        maximumConnections: configuration.Security.ConnectionLimit,
        logger: new TextWriterServerLogger(Console.Out, "Information"),
        portalClosedLoop: portalClosedLoop,
        worldMovementStore: evidenceMovementStore,
        officialWorldWireProfile: options.EvidenceBootstrapPlayerIdentity switch
        {
            "CurrentCreateProfileMinimal" => OfficialWorldWireProfile.CurrentCreateEvidence,
            "GoldenIdentity" => OfficialWorldWireProfile.LegacyMap19GoldenIdentityEvidence,
            _ => OfficialWorldWireProfile.LegacyMap19
        });
    var pipeline = new StartupPipeline(
        new MariaDbDatabaseBootstrapper(),
        new SqlFileMigrationRunner(configuration.Database, paths.DatabaseSchemaDirectory),
        new MariaDbStaticDataValidator(configuration.Database),
        staticDataCache,
        staticDataCache,
        runtime.SessionAuthority,
        networkHost);
    var shutdown = new ShutdownCoordinator(
    [
        runtime.SessionAuthority,
        networkHost,
        runtime.CommandDispatcher
    ]);

    try
    {
        var startup = await pipeline.RunAsync(
            configuration,
            value => Console.WriteLine($"[{value.Number}] {value.Name}: {(value.Result.Succeeded ? "PASS" : value.Result.Error.Code)}"),
            cancellation.Token);
        if (!startup.IsReady)
        {
            Console.Error.WriteLine($"Map identity evidence host startup failed: exit={startup.ExitCode}.");
            return startup.ExitCode;
        }

        Console.WriteLine($"Map Identity Evidence Host Ready; authority=TestOnlyEvidence; clientMap={evidenceLocation.ClientMapId}; position={evidenceLocation.X},{evidenceLocation.Y}; bootstrapPlayerIdentity={options.EvidenceBootstrapPlayerIdentity}; packetCapture=false; persistence=MariaDbIdentityOnly.");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        return 0;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        return 0;
    }
    finally
    {
        cancellation.Cancel();
        await shutdown.StopOnceAsync(CancellationToken.None);
        await stopWatcher;
    }
}

static async Task WatchStopFileAsync(string stopFile, CancellationTokenSource cancellation)
{
    while (!cancellation.IsCancellationRequested)
    {
        if (File.Exists(stopFile))
        {
            cancellation.Cancel();
            return;
        }

        try
        {
            await Task.Delay(250, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
    }
}

static async Task<AutomationProbeResult> MariaDbPreflightAsync(AutomationProbeOptions options)
{
    var checks = new List<ProbeCheck>();
    var counts = new List<StaticDataCount>();
    var assemblyDiagnostics = new List<AssemblyDiagnostic>
    {
        AssemblyDiagnostic.FromType(typeof(MySqlConnection)),
        AssemblyDiagnostic.FromType(typeof(Program))
    };

    try
    {
        var password = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
        {
            checks.Add(ProbeCheck.Blocked(
                "MariaDB Authentication",
                "mariadb.password_missing",
                "GOD2_DB_PASSWORD",
                "ProcessEnvironment",
                "Password environment variable is available to the .NET 10 probe.",
                "Password environment variable was empty.",
                "Re-run the DPAPI secret setup and do not pass the secret on the command line."));
            return new AutomationProbeResult("BLOCKED", checks, counts, assemblyDiagnostics, null);
        }

        await using var serverConnection = new MySqlConnection(BuildConnectionString(options, password, databaseName: null));
        await serverConnection.OpenAsync();
        checks.Add(ProbeCheck.Pass(
            "MariaDB Authentication",
            "DB Username/GOD2_DB_PASSWORD",
            "MySqlConnector .NET 10",
            "Server-level connection opens.",
            "authenticated"));

        var databaseExists = Convert.ToInt32(await ExecuteScalarAsync(
            serverConnection,
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @schema;",
            ("@schema", options.DatabaseName)));

        if (databaseExists <= 0)
        {
            checks.Add(ProbeCheck.Blocked(
                "Database Exists",
                "mariadb.database_missing",
                "DB Schema/Database",
                "INFORMATION_SCHEMA",
                "Database exists before server start.",
                "database not found",
                "Run the formal migration/import flow; do not fabricate data."));
            return new AutomationProbeResult("BLOCKED", checks, counts, assemblyDiagnostics, null);
        }

        checks.Add(ProbeCheck.Pass(
            "Database Exists",
            "DB Schema/Database",
            "INFORMATION_SCHEMA",
            "Database exists.",
            "database found"));

        await using var databaseConnection = new MySqlConnection(BuildConnectionString(options, password, options.DatabaseName));
        await databaseConnection.OpenAsync();
        checks.Add(ProbeCheck.Pass(
            "Database Select",
            "DB Schema/Database",
            "MySqlConnector .NET 10",
            "Database connection opens.",
            "selected"));

        var requiredTables = new[]
        {
            "accounts", "characters", "maps", "portals", "items", "skills", "npcs", "monsters", "spawns", "quests",
            "merchants", "merchant_items", "immortals", "battle_pets", "drop_tables",
            "rewards", "dialogs", "status_effects", "localization_entries",
            "skill_executions", "skill_effect_executions", "skill_cost_reservations",
            "battle_skill_usage", "skill_idempotency", "skill_audit",
            "battle_status_participant_versions", "battle_status_instances",
            "battle_status_applications", "battle_status_removals",
            "battle_status_trigger_plans", "battle_status_trigger_results",
            "battle_status_idempotency", "battle_status_recovery", "battle_status_audit",
            "quest_instances", "quest_objective_states", "quest_operation_idempotency",
            "quest_progress_mutations", "quest_reward_finalization",
            "quest_recovery_state", "quest_audit",
            "battle_actor_instances", "battle_command_journal", "battle_round_plans",
            "battle_action_results", "battle_round_checkpoints", "battle_event_outbox",
            "battle_finalizations", "battle_actor_recovery",
            "__SchemaVersion"
        };
        var missingTables = new List<string>();
        foreach (var table in requiredTables)
        {
            var exists = Convert.ToInt32(await ExecuteScalarAsync(
                serverConnection,
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table;",
                ("@schema", options.DatabaseName),
                ("@table", table)));
            if (exists <= 0)
            {
                missingTables.Add(table);
            }
        }

        if (missingTables.Count > 0)
        {
            checks.Add(ProbeCheck.Blocked(
                "Required Schema Check",
                "mariadb.schema_missing",
                "Required Schema/Table",
                "INFORMATION_SCHEMA",
                "All required runtime tables exist.",
                "missing: " + string.Join(", ", missingTables),
                "Run migration verification; do not skip schema."));
            return new AutomationProbeResult("BLOCKED", checks, counts, assemblyDiagnostics, null);
        }

        checks.Add(ProbeCheck.Pass(
            "Required Schema Check",
            "Required Schema/Table",
            "INFORMATION_SCHEMA",
            "All required runtime tables exist.",
            "all required tables found"));

        foreach (var table in new[] { "maps", "npcs", "monsters", "spawns", "portals", "merchants", "merchant_items", "skills", "quests" })
        {
            var count = Convert.ToInt64(await ExecuteScalarAsync(databaseConnection, $"SELECT COUNT(*) FROM `{table}`;"));
            counts.Add(new StaticDataCount(table, count));
        }

        checks.Add(ProbeCheck.Pass(
            "Static Data Query",
            "Runtime Content Tables",
            "MariaDB",
            "Maps/NPC/Monster/Spawn/Portal/Merchant/Skill/Quest queries succeed.",
            "queries completed"));
        checks.Add(ProbeCheck.Pass(
            "Runtime Content Load Boundary",
            "Runtime Content",
            "MariaDB count queries",
            "Content can be queried before server start.",
            "content query boundary passed"));

        return new AutomationProbeResult("PASS", checks, counts, assemblyDiagnostics, null);
    }
    catch (ReflectionTypeLoadException exception)
    {
        checks.Add(ProbeCheck.Blocked(
            "MariaDB Preflight",
            "automation.reflection_type_load_failed",
            "MySqlConnector",
            "MySqlConnector .NET 10",
            "All required assemblies load in the probe runtime.",
            exception.Message,
            "Inspect loader diagnostics and resolve the assembly/version conflict."));
        return new AutomationProbeResult(
            "BLOCKED",
            checks,
            counts,
            assemblyDiagnostics,
            ReflectionLoadDiagnostic.From(exception));
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        checks.Add(ProbeCheck.Blocked(
            "MariaDB Preflight",
            "mariadb.connection_failed",
            "Database",
            "MySqlConnector .NET 10",
            "Authenticate and query schema.",
            Redact(exception.Message, options.PasswordEnvironmentVariable),
            "Fix MariaDB account, schema, permissions, or connectivity, then rerun preflight."));
        return new AutomationProbeResult("BLOCKED", checks, counts, assemblyDiagnostics, null);
    }
}

static async Task<AutomationProbeResult> AccountCredentialPreflightAsync(AutomationProbeOptions options)
{
    var checks = new List<ProbeCheck>();
    var assemblies = new List<AssemblyDiagnostic>
    {
        AssemblyDiagnostic.FromType(typeof(MySqlConnection)),
        AssemblyDiagnostic.FromType(typeof(PasswordVerifier))
    };

    var databasePassword = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
    var accountName = Environment.GetEnvironmentVariable(options.AccountEnvironmentVariable);
    var accountPassword = Environment.GetEnvironmentVariable(options.AccountPasswordEnvironmentVariable);
    if (string.IsNullOrEmpty(databasePassword) ||
        string.IsNullOrWhiteSpace(accountName) ||
        string.IsNullOrEmpty(accountPassword))
    {
        checks.Add(ProbeCheck.Blocked(
            "Official Account Credential Preflight",
            "automation.account_credential_environment_missing",
            "Database/account credential environment",
            "ProcessEnvironment",
            "Database password, account name, and account password are supplied through environment variables.",
            "one or more required values were empty",
            "Load the existing protected/local automation credentials without placing secrets on the command line."));
        return new AutomationProbeResult("BLOCKED", checks, [], assemblies, null);
    }

    try
    {
        await using var connection = new MySqlConnection(BuildConnectionString(options, databasePassword, options.DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `Id`, `PasswordHash`, `Status`, `LockedUntilUtc`, `CurrentSessionId`,
                   (SELECT COUNT(*) FROM `characters` WHERE `AccountId` = `accounts`.`Id` AND `Status` <> 'Deleted') AS `CharacterCount`
            FROM `accounts`
            WHERE `LoginName` = @loginName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@loginName", accountName);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            checks.Add(ProbeCheck.Blocked(
                "Official Account Exists",
                "automation.account_not_found",
                "Official automation account",
                "MariaDB accounts",
                "The configured account exists in the formal MariaDB repository.",
                "not found",
                "Repair the isolated automation account provisioning; do not enable an authentication bypass."));
            return new AutomationProbeResult("BLOCKED", checks, [], assemblies, null);
        }

        var passwordMatches = new PasswordVerifier().Verify(accountPassword, reader.GetString("PasswordHash"));
        var status = reader.GetString("Status");
        var lockedUntil = reader.IsDBNull(reader.GetOrdinal("LockedUntilUtc"))
            ? (DateTime?)null
            : DateTime.SpecifyKind(reader.GetDateTime("LockedUntilUtc"), DateTimeKind.Utc);
        var locked = string.Equals(status, "Locked", StringComparison.OrdinalIgnoreCase) ||
                     (lockedUntil.HasValue && lockedUntil.Value > DateTime.UtcNow);
        var currentSessionPresent = !reader.IsDBNull(reader.GetOrdinal("CurrentSessionId")) &&
                                    !string.IsNullOrWhiteSpace(reader.GetString("CurrentSessionId"));
        var characterCount = reader.GetInt64("CharacterCount");

        checks.Add(passwordMatches
            ? ProbeCheck.Pass(
                "Official Account Password",
                "Official automation credential",
                "PasswordVerifier + MariaDB accounts",
                "The configured password verifies against the formal repository hash.",
                "verified")
            : ProbeCheck.Blocked(
                "Official Account Password",
                "automation.account_password_mismatch",
                "Official automation credential",
                "PasswordVerifier + MariaDB accounts",
                "The configured password verifies against the formal repository hash.",
                "verification failed",
                "Repair the isolated automation account credential; do not enable an authentication bypass."));
        checks.Add(!locked && string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            ? ProbeCheck.Pass(
                "Official Account Status",
                "Account Status/LockedUntilUtc",
                "MariaDB accounts",
                "The account is active and not locked.",
                "active; not locked")
            : ProbeCheck.Blocked(
                "Official Account Status",
                "automation.account_unavailable",
                "Account Status/LockedUntilUtc",
                "MariaDB accounts",
                "The account is active and not locked.",
                $"status={status}; locked={locked}",
                "Recover the isolated automation account using the formal account administration path."));
        checks.Add(ProbeCheck.Pass(
            "Official Account Session Observation",
            "CurrentSessionId",
            "MariaDB accounts",
            "The current session lease state is observed without mutation.",
            $"leasePresent={currentSessionPresent}"));
        checks.Add(characterCount > 0
            ? ProbeCheck.Pass(
                "Official Account Character Availability",
                "characters.AccountId",
                "MariaDB characters",
                "At least one non-deleted character is available for automated world entry.",
                $"characterCount={characterCount}")
            : ProbeCheck.Blocked(
                "Official Account Character Availability",
                "automation.account_character_missing",
                "characters.AccountId",
                "MariaDB characters",
                "At least one non-deleted character is available for automated world entry.",
                "characterCount=0",
                "Provision the isolated automation character through an authorized lifecycle flow."));

        var overall = checks.All(check => check.Status == "PASS") ? "PASS" : "BLOCKED";
        return new AutomationProbeResult(overall, checks, [], assemblies, null);
    }
    catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
    {
        checks.Add(ProbeCheck.Blocked(
            "Official Account Credential Preflight",
            "automation.account_preflight_failed",
            "Official automation account",
            "MariaDB",
            "Read-only credential and account-state verification completes.",
            Redact(exception.Message, options.PasswordEnvironmentVariable),
            "Repair the automation environment or formal repository connectivity and rerun."));
        return new AutomationProbeResult("BLOCKED", checks, [], assemblies, null);
    }
}

static async Task<AutomationProbeResult> ServerReadyAsync(AutomationProbeOptions options)
{
    var checks = new List<ProbeCheck>();
    var output = new ReadySignalTextWriter();
    using var cancellation = new CancellationTokenSource();
    var runTask = ConsoleEntry.RunAsync(new ConsoleEntryContext(options.BaseDirectory, output), cancellation.Token);
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(options.TimeoutSeconds));
    var completed = await Task.WhenAny(output.Ready.Task, runTask, timeoutTask);

    if (completed == output.Ready.Task && output.Ready.Task.Result)
    {
        cancellation.Cancel();
        var exitCode = await AwaitServerExitAsync(runTask);
        checks.Add(ProbeCheck.Pass(
            "Server Ready",
            "ConsoleHost",
            "AutomationEnvironmentProbe",
            "ConsoleEntry reaches Server Ready and shuts down after cancellation.",
            $"ready; exitCode={exitCode}"));
        return new AutomationProbeResult("PASS", checks, [], [AssemblyDiagnostic.FromType(typeof(ConsoleEntry))], null, output.Lines);
    }

    if (completed == runTask)
    {
        var exitCode = await runTask;
        checks.Add(ProbeCheck.Blocked(
            "Server Ready",
            "server.exited_before_ready",
            "ConsoleHost",
            "AutomationEnvironmentProbe",
            "Server Ready appears before process exit.",
            $"process exited; exitCode={exitCode}",
            "Inspect startup output for the failing stage."));
        return new AutomationProbeResult("BLOCKED", checks, [], [AssemblyDiagnostic.FromType(typeof(ConsoleEntry))], null, output.Lines);
    }

    cancellation.Cancel();
    _ = await AwaitServerExitAsync(runTask);
    checks.Add(ProbeCheck.Blocked(
        "Server Ready",
        "server.ready_timeout",
        "ConsoleHost",
        "AutomationEnvironmentProbe",
        "Server Ready appears within timeout.",
        $"timeout={options.TimeoutSeconds}s",
        "Inspect startup output and database readiness."));
    return new AutomationProbeResult("BLOCKED", checks, [], [AssemblyDiagnostic.FromType(typeof(ConsoleEntry))], null, output.Lines);
}

static async Task<int> AwaitServerExitAsync(Task<int> runTask)
{
    try
    {
        return await runTask.WaitAsync(TimeSpan.FromSeconds(20));
    }
    catch (TimeoutException)
    {
        return -1;
    }
}

static bool IsProductionRuntimeSkill(SkillDefinition definition)
{
    static bool IsProductionPolicy(CombatPolicyStatus status) =>
        status is CombatPolicyStatus.Verified or CombatPolicyStatus.ContentBacked;

    return definition.Enabled &&
           definition.SkillCategory is not (SkillCategory.Passive or SkillCategory.Unknown) &&
           definition.ActionCategory != SkillActionCategory.Unknown &&
           definition.TargetPolicy != SkillTargetPolicyType.Unknown &&
           IsProductionPolicy(definition.PolicyStatus) &&
           IsProductionPolicy(definition.ProtocolStatus) &&
           IsProductionPolicy(definition.CostDefinition.PolicyStatus) &&
           IsProductionPolicy(definition.CooldownDefinition.PolicyStatus) &&
           IsProductionPolicy(definition.UsageDefinition.PolicyStatus) &&
           definition.EffectDefinitions.Count > 0 &&
           definition.EffectDefinitions.All(effect =>
               effect.EffectType != SkillEffectType.Unknown &&
               effect.TargetScope != SkillTargetPolicyType.Unknown &&
               !string.IsNullOrWhiteSpace(effect.ValuePolicy) &&
               IsProductionPolicy(effect.PolicyStatus));
}

static string BuildConnectionString(AutomationProbeOptions options, string password, string? databaseName)
{
    var builder = new MySqlConnectionStringBuilder
    {
        Server = options.Host,
        Port = (uint)options.Port,
        UserID = options.Username,
        Password = password,
        CharacterSet = "utf8mb4",
        ConnectionTimeout = (uint)Math.Max(1, options.TimeoutSeconds),
        DefaultCommandTimeout = 30,
        Pooling = false,
        SslMode = MySqlSslMode.Preferred
    };

    if (!string.IsNullOrWhiteSpace(databaseName))
    {
        builder.Database = databaseName;
    }

    return builder.ConnectionString;
}

static string BuildConfiguredDatabaseConnectionString(
    God2.ClassicServer.Application.Configuration.DatabaseOptions options)
{
    var builder = new MySqlConnectionStringBuilder
    {
        Server = options.Host,
        Port = (uint)options.Port,
        Database = options.DatabaseName,
        UserID = options.Username,
        Password = options.Password,
        CharacterSet = "utf8mb4",
        ConnectionTimeout = (uint)Math.Max(1, options.ConnectionTimeoutSeconds),
        DefaultCommandTimeout = 30,
        Pooling = false,
        SslMode = MySqlSslMode.Preferred
    };
    return builder.ConnectionString;
}

static async Task<object?> ExecuteScalarAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    return await command.ExecuteScalarAsync();
}

static string Redact(string value, string passwordEnvironmentVariable)
{
    var secret = Environment.GetEnvironmentVariable(passwordEnvironmentVariable);
    return string.IsNullOrEmpty(secret) ? value : value.Replace(secret, "<secret>", StringComparison.Ordinal);
}

static void WriteJson(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}

static void WriteJsonArtifact(object value, string outputPath, string baseDirectory)
{
    var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        return;
    }

    var root = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var fullPath = Path.GetFullPath(outputPath);
    if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Automation probe output must remain under the configured base directory.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, json, new UTF8Encoding(false));
}

sealed record AutomationProbeOptions(
    string Mode,
    string Host,
    int Port,
    string DatabaseName,
    string Username,
    string PasswordEnvironmentVariable,
    string AccountEnvironmentVariable,
    string AccountPasswordEnvironmentVariable,
    int TimeoutSeconds,
    string BaseDirectory,
    string OutputPath,
    string BuildIdentityPath,
    string StopFile,
    int EvidenceClientMapId,
    string EvidenceBootstrapPlayerIdentity,
    int EvidencePositionX,
    int EvidencePositionY,
    long CharacterId,
    int ExpectedCharacterMapId,
    int ExpectedCharacterPositionX,
    int ExpectedCharacterPositionY,
    int TargetCharacterMapId,
    int TargetCharacterPositionX,
    int TargetCharacterPositionY)
{
    public static AutomationProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = args[index][2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            values[name] = value;
        }

        return new AutomationProbeOptions(
            values.GetValueOrDefault("mode", "mariadb-preflight"),
            values.GetValueOrDefault("host", "127.0.0.1"),
            int.TryParse(values.GetValueOrDefault("port", "3306"), out var port) ? port : 3306,
            values.GetValueOrDefault("database", "god2"),
            values.GetValueOrDefault("username", "god2_server"),
            values.GetValueOrDefault("password-env", "GOD2_DB_PASSWORD"),
            values.GetValueOrDefault("account-env", "GOD2_AUTOMATION_ACCOUNT"),
            values.GetValueOrDefault("account-password-env", "GOD2_AUTOMATION_ACCOUNT_PASSWORD"),
            int.TryParse(values.GetValueOrDefault("timeout", "30"), out var timeout) ? timeout : 30,
            values.GetValueOrDefault("base-directory", AppContext.BaseDirectory),
            values.GetValueOrDefault("out", string.Empty),
            values.GetValueOrDefault("build-identity", string.Empty),
            values.GetValueOrDefault("stop-file", Path.Combine(AppContext.BaseDirectory, "map-identity-evidence-host.stop")),
            int.TryParse(values.GetValueOrDefault("evidence-client-map", "19"), out var evidenceClientMapId) ? evidenceClientMapId : 19,
            values.GetValueOrDefault("evidence-bootstrap-player-identity", "Dynamic"),
            int.TryParse(values.GetValueOrDefault("evidence-position-x", "-1"), out var evidencePositionX) ? evidencePositionX : -1,
            int.TryParse(values.GetValueOrDefault("evidence-position-y", "-1"), out var evidencePositionY) ? evidencePositionY : -1,
            long.TryParse(values.GetValueOrDefault("character-id", "-1"), out var characterId) ? characterId : -1,
            int.TryParse(values.GetValueOrDefault("expected-map-id", "-1"), out var expectedCharacterMapId) ? expectedCharacterMapId : -1,
            int.TryParse(values.GetValueOrDefault("expected-x", "-1"), out var expectedCharacterPositionX) ? expectedCharacterPositionX : -1,
            int.TryParse(values.GetValueOrDefault("expected-y", "-1"), out var expectedCharacterPositionY) ? expectedCharacterPositionY : -1,
            int.TryParse(values.GetValueOrDefault("target-map-id", "-1"), out var targetCharacterMapId) ? targetCharacterMapId : -1,
            int.TryParse(values.GetValueOrDefault("target-x", "-1"), out var targetCharacterPositionX) ? targetCharacterPositionX : -1,
            int.TryParse(values.GetValueOrDefault("target-y", "-1"), out var targetCharacterPositionY) ? targetCharacterPositionY : -1);
    }
}

sealed record MapIdentityInventoryRow(
    int Id,
    string Code,
    string Name,
    int? Width,
    int? Height,
    string? OriginalName,
    string? NameZhTw,
    string EvidenceStatus,
    string? ContentRecoveryRunId,
    string SourceReference,
    string PayloadSha256,
    MapPayloadIdentity PayloadIdentity);

sealed record ClientMapIdentityInventoryRow(
    int MapId,
    string ClientBuildId,
    ushort ClientMapId,
    byte ClientAreaId,
    string ResourceIdentity,
    decimal CoordinateScaleX,
    decimal CoordinateScaleY,
    decimal CoordinateOffsetX,
    decimal CoordinateOffsetY,
    string IdentityEvidenceStatus,
    string CoordinateEvidenceStatus,
    bool ProductionEnabled,
    string SourceType,
    string SourceHash,
    string EvidenceReference);

sealed record RecoveredMapEvidenceInventoryRow(
    string RunId,
    string AuthorityKey,
    string? ResourceKey,
    int? Width,
    int? Height,
    string SourceHash,
    string NormalizedHash,
    string EvidenceStatus,
    string LocalizationStatus);

sealed record CharacterMapUsageInventoryRow(int MapId, int CharacterCount);

sealed record MapPayloadIdentity(
    string? RecordKey,
    string? ResourceName,
    string? SourcePath,
    int? GridWidthCandidate,
    int? GridHeightCandidate)
{
    public static MapPayloadIdentity Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new(null, null, null, null, null);
        }

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var mbd = root.TryGetProperty("mbd", out var mbdElement) && mbdElement.ValueKind == JsonValueKind.Object
            ? mbdElement
            : default;
        return new(
            GetString(root, "id"),
            GetString(root, "resourceName"),
            GetString(root, "sourcePath"),
            GetInt32(mbd, "gridWidthCandidate"),
            GetInt32(mbd, "gridHeightCandidate"));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}

readonly record struct MapIdentityEvidenceLocation(int ClientMapId, byte ClientAreaId, int X, int Y)
{
    public static MapIdentityEvidenceLocation Resolve(int clientMapId, int requestedX = -1, int requestedY = -1)
    {
        var baseline = clientMapId switch
        {
            19 => new MapIdentityEvidenceLocation(19, 4, 28, 34),
            3 => new MapIdentityEvidenceLocation(3, 4, 196, 139),
            _ => throw new ArgumentOutOfRangeException(nameof(clientMapId), "Only evidence-pinned Client maps 19 and 3 are supported.")
        };
        if (requestedX == -1 && requestedY == -1)
        {
            return baseline;
        }

        if (requestedX is < 0 or > 0x7FFF || requestedY is < 0 or > 0x7FFF)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedX),
                "Evidence positions must provide both X and Y in the recovered unsigned 15-bit range.");
        }

        if (clientMapId != 19 && (requestedX != baseline.X || requestedY != baseline.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clientMapId),
                "Arbitrary position evidence is currently locked to the statically proven Map 19 consumer.");
        }

        return baseline with { X = requestedX, Y = requestedY };
    }
}

sealed class MapIdentityEvidenceWorldSessionCoordinator : IWorldSessionCoordinator
{
    private readonly ConcurrentDictionary<string, WorldSessionBinding> _bindings = new(StringComparer.Ordinal);
    private readonly WorldSessionBinder _binder = new(new MapRuntimeFactory());
    private readonly WorldContentSnapshot _emptySnapshot = new(new Dictionary<int, MapDefinition>(), []);
    private readonly MapIdentityEvidenceLocation _location;

    public MapIdentityEvidenceWorldSessionCoordinator(MapIdentityEvidenceLocation location) => _location = location;

    public WorldContentAuthorityKind AuthorityKind => WorldContentAuthorityKind.TestOnly;

    public int ActiveBindingCount => _bindings.Count;

    public Task<OperationResult<WorldSessionBinding>> BindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsAuthenticated ||
            session.ProtocolStage != ProtocolStage.WorldEntering ||
            session.AccountId != character.AccountId ||
            session.CharacterId != character.CharacterId)
        {
            return Task.FromResult(OperationResult<WorldSessionBinding>.Failure(
                "map_identity_evidence.session_invalid",
                "The evidence projection requires the authenticated MariaDB character identity."));
        }

        var evidenceCharacter = character with { MapId = _location.ClientMapId, PositionX = _location.X, PositionY = _location.Y };
        var bound = _binder.Bind(session, evidenceCharacter, _emptySnapshot, WorldContentMode.FrozenCompatibility);
        if (!bound.Succeeded || bound.Value is null)
        {
            return Task.FromResult(bound);
        }

        if (!_bindings.TryAdd(session.SessionId, bound.Value))
        {
            return Task.FromResult(OperationResult<WorldSessionBinding>.Failure(
                "map_identity_evidence.session_already_bound",
                "The evidence session is already bound."));
        }

        return Task.FromResult(bound);
    }

    public OperationResult<WorldSessionBinding> GetBinding(string sessionId) =>
        _bindings.TryGetValue(sessionId, out var binding)
            ? OperationResult<WorldSessionBinding>.Success(binding)
            : OperationResult<WorldSessionBinding>.Failure(
                "map_identity_evidence.session_not_bound",
                "The evidence session is not bound.");

    public OperationResult<WorldSessionBinding> UpdateSession(RuntimeSession session)
    {
        while (_bindings.TryGetValue(session.SessionId, out var binding))
        {
            if (binding.Character.CharacterId != session.CharacterId || binding.Character.AccountId != session.AccountId)
            {
                return OperationResult<WorldSessionBinding>.Failure(
                    "map_identity_evidence.identity_changed",
                    "The authenticated MariaDB identity changed after evidence binding.");
            }

            var updated = binding with { Session = session };
            if (_bindings.TryUpdate(session.SessionId, updated, binding))
            {
                return OperationResult<WorldSessionBinding>.Success(updated);
            }
        }

        return OperationResult<WorldSessionBinding>.Failure(
            "map_identity_evidence.session_not_bound",
            "The evidence session is not bound.");
    }

    public bool Unbind(string sessionId) => _bindings.TryRemove(sessionId, out _);
}

sealed class MapIdentityEvidenceWorldBootstrapProjector : IWorldBootstrapProjector
{
    private readonly MapIdentityEvidenceLocation _location;
    private readonly string _playerIdentityMode;

    public MapIdentityEvidenceWorldBootstrapProjector(
        MapIdentityEvidenceLocation location,
        string playerIdentityMode)
    {
        _location = location;
        _playerIdentityMode = playerIdentityMode switch
        {
            "Dynamic" or "GoldenCharacterId" or "GoldenCharacterName" or "GoldenIdentity" or
                "CurrentCreateProfile" or "CurrentCreateProfileMinimal" or
                "LegacyTailOmit2" or "LegacyTailOmit3" or "LegacyTailOmit4" or
                "LegacyTailOmit5To10" or "LegacyTailOmit5To7" or "LegacyTailOmit8To10" or
                "LegacyTailOmit5" or "LegacyTailOmit6" or "LegacyTailOmit7" or
                "LegacyTailOmit8" or "LegacyTailOmit9" or "LegacyTailOmit10" or
                "StaticRecordsOmit0To14" or "StaticRecordsOmit15To28" or
                "StaticRecordsOmit0To7" or "StaticRecordsOmit8To14" or
                "StaticRecordsOmit15To22" or "StaticRecordsOmit23To28" or
                "StaticRecordsOmit15To17" or "StaticRecordsOmit18To20" or
                "StaticRecordOmit15" or "StaticRecordOmit16" or "StaticRecordOmit17" or
                "StaticRecordOmit18" or "StaticRecordOmit19" or "StaticRecordOmit20" or
                "SceneZero25To123" or "SceneZero124To318" or
                "SceneZero25To64" or "SceneZero65To123" or
                "SceneZero25To26" or "SceneZero103To123" or
                "SceneZero65To83" or "SceneZero84To102" or
                "SceneZero65" or "SceneZero66To70" or "SceneEmptyHeader26" => playerIdentityMode,
            _ => throw new ArgumentOutOfRangeException(
                nameof(playerIdentityMode),
                "Evidence bootstrap player identity mode is unsupported.")
        };
    }

    public OperationResult<WorldBootstrapProjection> Project(WorldSessionBinding binding)
    {
        if (binding.MapSession.ContentMode != WorldContentMode.FrozenCompatibility ||
            binding.Character.MapId != _location.ClientMapId ||
            binding.Character.PositionX != _location.X ||
            binding.Character.PositionY != _location.Y)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                "map_identity_evidence.projection_outside_lock",
                "The evidence projector is locked to the selected verified portal arrival state.");
        }

        try
        {
            var golden = OfficialClientWorldProtocolFrames.ParseCurrentPlayerSpawn();
            var projectedCharacter = _playerIdentityMode switch
            {
                "GoldenCharacterId" => binding.Character with { CharacterId = golden.CharacterIdCandidate },
                "GoldenCharacterName" => binding.Character with { Name = golden.CharacterName },
                "GoldenIdentity" => binding.Character with
                {
                    CharacterId = golden.CharacterIdCandidate,
                    Name = golden.CharacterName
                },
                _ => binding.Character
            };
            var bootstrapCharacter = _location.ClientMapId == 19
                ? projectedCharacter with { MapId = 19, PositionX = 28, PositionY = 34 }
                : projectedCharacter;
            var bootstrap = _playerIdentityMode == "CurrentCreateProfileMinimal"
                ? OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldFirstFollowUp()
                    .Concat(OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(
                        bootstrapCharacter.CharacterId,
                        bootstrapCharacter.Name))
                    .ToArray()
                : OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(bootstrapCharacter);
            if (_playerIdentityMode == "CurrentCreateProfile")
            {
                OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(
                        projectedCharacter.CharacterId,
                        projectedCharacter.Name)
                    .CopyTo(bootstrap, 6);
            }

            var omittedFrames = _playerIdentityMode switch
            {
                "LegacyTailOmit2" => [2],
                "LegacyTailOmit3" => [3],
                "LegacyTailOmit4" => [4],
                "LegacyTailOmit5To10" => [5, 6, 7, 8, 9, 10],
                "LegacyTailOmit5To7" => [5, 6, 7],
                "LegacyTailOmit8To10" => [8, 9, 10],
                "LegacyTailOmit5" => [5],
                "LegacyTailOmit6" => [6],
                "LegacyTailOmit7" => [7],
                "LegacyTailOmit8" => [8],
                "LegacyTailOmit9" => [9],
                "LegacyTailOmit10" => [10],
                _ => Array.Empty<int>()
            };
            if (omittedFrames.Length > 0)
            {
                bootstrap = OmitLegacyBootstrapFrames(bootstrap, omittedFrames);
            }

            var omittedStaticRecords = _playerIdentityMode switch
            {
                "StaticRecordsOmit0To14" => Enumerable.Range(0, 15).ToArray(),
                "StaticRecordsOmit15To28" => Enumerable.Range(15, 14).ToArray(),
                "StaticRecordsOmit0To7" => Enumerable.Range(0, 8).ToArray(),
                "StaticRecordsOmit8To14" => Enumerable.Range(8, 7).ToArray(),
                "StaticRecordsOmit15To22" => Enumerable.Range(15, 8).ToArray(),
                "StaticRecordsOmit23To28" => Enumerable.Range(23, 6).ToArray(),
                "StaticRecordsOmit15To17" => Enumerable.Range(15, 3).ToArray(),
                "StaticRecordsOmit18To20" => Enumerable.Range(18, 3).ToArray(),
                "StaticRecordOmit15" => [15],
                "StaticRecordOmit16" => [16],
                "StaticRecordOmit17" => [17],
                "StaticRecordOmit18" => [18],
                "StaticRecordOmit19" => [19],
                "StaticRecordOmit20" => [20],
                _ => Array.Empty<int>()
            };
            if (omittedStaticRecords.Length > 0)
            {
                bootstrap = OmitLegacyStaticApplicationRecords(bootstrap, omittedStaticRecords);
            }

            var sceneZeroRange = _playerIdentityMode switch
            {
                "SceneZero25To123" => (Offset: 25, Length: 99),
                "SceneZero124To318" => (Offset: 124, Length: 195),
                "SceneZero25To64" => (Offset: 25, Length: 40),
                "SceneZero65To123" => (Offset: 65, Length: 59),
                "SceneZero25To26" => (Offset: 25, Length: 2),
                "SceneZero103To123" => (Offset: 103, Length: 21),
                "SceneZero65To83" => (Offset: 65, Length: 19),
                "SceneZero84To102" => (Offset: 84, Length: 19),
                "SceneZero65" => (Offset: 65, Length: 1),
                "SceneZero66To70" => (Offset: 66, Length: 5),
                _ => (Offset: 0, Length: 0)
            };
            if (sceneZeroRange.Length > 0)
            {
                bootstrap = ZeroLegacySceneApplicationRange(
                    bootstrap,
                    sceneZeroRange.Offset,
                    sceneZeroRange.Length);
            }
            if (_playerIdentityMode == "SceneEmptyHeader26")
            {
                bootstrap = TruncateLegacySceneToEmptyHeader(bootstrap);
            }

            if (_location.ClientMapId == 19 && (_location.X != 28 || _location.Y != 34))
            {
                if (_playerIdentityMode is "CurrentCreateProfile" or "CurrentCreateProfileMinimal")
                {
                    return OperationResult<WorldBootstrapProjection>.Failure(
                        "map_identity_evidence.position_profile_unsupported",
                        "Arbitrary Map 19 position evidence requires the complete legacy evidence profile.");
                }

                var transition = OfficialPortalWireCodec.SerializeTestOnlyMap19PositionEvidence(
                    OfficialPortalWireCodec.ClientBuildId,
                    _location.X,
                    _location.Y);
                if (!transition.Succeeded || transition.Value is null)
                {
                    return OperationResult<WorldBootstrapProjection>.Failure(
                        "map_identity_evidence.position_serialization_failed",
                        "The statically recovered Map 19 position consumer could not be projected.",
                        transition.FailureCode);
                }

                var encodedPrelude = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                    transition.Value.PreludeDecodedFrame.Span);
                var encodedPosition = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                    transition.Value.MapTransitionDecodedFrame.Span);
                bootstrap = bootstrap.Concat(encodedPrelude).Concat(encodedPosition).ToArray();
                Array.Clear(encodedPrelude);
                Array.Clear(encodedPosition);
            }
            return OperationResult<WorldBootstrapProjection>.Success(new WorldBootstrapProjection(
                bootstrap,
                nameof(MapIdentityEvidenceWorldBootstrapProjector),
                OfficialClientWorldProtocolFrames.EvidenceId,
                _location.ClientMapId,
                binding.MapSession.PlayerRuntimeEntityId,
                checked((ushort)_location.ClientMapId),
                _location.ClientAreaId,
                checked((ushort)_location.X),
                checked((ushort)_location.Y)));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                "map_identity_evidence.projection_failed",
                "The verified evidence bootstrap could not be projected.",
                exception.GetType().Name);
        }
    }

    private static byte[] OmitLegacyBootstrapFrames(byte[] bootstrap, IReadOnlyCollection<int> omittedFrames)
    {
        var sequence = OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence();
        var frozenLength = sequence.Sum(frame => frame.Length);
        if (bootstrap.Length != frozenLength)
        {
            Array.Clear(bootstrap);
            throw new InvalidOperationException("Legacy-tail omission evidence requires the exact frozen bootstrap length.");
        }

        var result = new byte[sequence.Where(frame => !omittedFrames.Contains(frame.Index)).Sum(frame => frame.Length)];
        var destinationOffset = 0;
        foreach (var frame in sequence)
        {
            if (omittedFrames.Contains(frame.Index))
            {
                continue;
            }

            bootstrap.AsSpan(frame.Offset, frame.Length).CopyTo(result.AsSpan(destinationOffset));
            destinationOffset += frame.Length;
        }

        Array.Clear(bootstrap);
        return result;
    }

    private static byte[] OmitLegacyStaticApplicationRecords(
        byte[] bootstrap,
        IReadOnlyCollection<int> omittedRecordIndexes)
    {
        const int frameOffset = 454;
        const int frameLength = 752;
        var records = new (int Offset, int Length)[]
        {
            (2, 21), (23, 61), (84, 17), (101, 17), (118, 25), (143, 29),
            (172, 2), (174, 2), (176, 17), (193, 9), (202, 37), (239, 97),
            (336, 37), (373, 37), (410, 5), (415, 9), (424, 4), (428, 4),
            (432, 4), (436, 5), (441, 5), (446, 89), (535, 36), (571, 16),
            (587, 16), (603, 16), (619, 16), (635, 16), (651, 16), (667, 21),
            (688, 21), (709, 21), (730, 21)
        };
        if (bootstrap.Length != OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence().Sum(frame => frame.Length))
        {
            Array.Clear(bootstrap);
            throw new InvalidOperationException("Static-record omission evidence requires the exact frozen bootstrap length.");
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(frameOffset, frameLength));
        var retainedRecords = records
            .Select((record, index) => (record, index))
            .Where(item => !omittedRecordIndexes.Contains(item.index))
            .ToArray();
        var decodedLength = 3 + retainedRecords.Sum(item => item.record.Length);
        var rebuiltDecoded = new byte[decodedLength];
        BinaryPrimitives.WriteUInt16LittleEndian(rebuiltDecoded, checked((ushort)decodedLength));
        var destinationOffset = 2;
        foreach (var item in retainedRecords)
        {
            decoded.AsSpan(item.record.Offset, item.record.Length).CopyTo(rebuiltDecoded.AsSpan(destinationOffset));
            destinationOffset += item.record.Length;
        }
        rebuiltDecoded[^1] = OfficialLoginWireTransform.ComputeChecksum(rebuiltDecoded);
        var rebuiltEncoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(rebuiltDecoded);
        var result = new byte[bootstrap.Length - frameLength + rebuiltEncoded.Length];
        bootstrap.AsSpan(0, frameOffset).CopyTo(result);
        rebuiltEncoded.CopyTo(result, frameOffset);
        bootstrap.AsSpan(frameOffset + frameLength).CopyTo(result.AsSpan(frameOffset + rebuiltEncoded.Length));

        Array.Clear(decoded);
        Array.Clear(rebuiltDecoded);
        Array.Clear(rebuiltEncoded);
        Array.Clear(bootstrap);
        return result;
    }

    private static byte[] ZeroLegacySceneApplicationRange(byte[] bootstrap, int offset, int length)
    {
        const int frameOffset = 134;
        const int frameLength = 320;
        if (bootstrap.Length != OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence().Sum(frame => frame.Length) ||
            offset < 3 ||
            length <= 0 ||
            offset + length >= frameLength)
        {
            Array.Clear(bootstrap);
            throw new InvalidOperationException("Scene-range evidence requires the exact frozen bootstrap and a payload-only range.");
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(frameOffset, frameLength));
        decoded.AsSpan(offset, length).Clear();
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decoded);
        encoded.CopyTo(bootstrap, frameOffset);

        Array.Clear(decoded);
        Array.Clear(encoded);
        return bootstrap;
    }

    private static byte[] TruncateLegacySceneToEmptyHeader(byte[] bootstrap)
    {
        const int frameOffset = 134;
        const int frameLength = 320;
        const int emptyFrameLength = 26;
        if (bootstrap.Length != OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence().Sum(frame => frame.Length))
        {
            Array.Clear(bootstrap);
            throw new InvalidOperationException("Empty-scene evidence requires the exact frozen bootstrap length.");
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(frameOffset, frameLength));
        var emptyDecoded = new byte[emptyFrameLength];
        decoded.AsSpan(0, emptyFrameLength - 1).CopyTo(emptyDecoded);
        BinaryPrimitives.WriteUInt16LittleEndian(emptyDecoded, emptyFrameLength);
        emptyDecoded[^1] = OfficialLoginWireTransform.ComputeChecksum(emptyDecoded);
        var emptyEncoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(emptyDecoded);
        var result = new byte[bootstrap.Length - frameLength + emptyFrameLength];
        bootstrap.AsSpan(0, frameOffset).CopyTo(result);
        emptyEncoded.CopyTo(result, frameOffset);
        bootstrap.AsSpan(frameOffset + frameLength).CopyTo(result.AsSpan(frameOffset + emptyFrameLength));

        Array.Clear(decoded);
        Array.Clear(emptyDecoded);
        Array.Clear(emptyEncoded);
        Array.Clear(bootstrap);
        return result;
    }
}

sealed class MapIdentityEvidencePortalRuntime :
    IOfficialPortalCommandContextResolver,
    IWorldInteractionCoordinator,
    IOfficialPortalSessionSeeder,
    IOfficialPortalSessionLifecycle
{
    private readonly OfficialPortalPhase2RuntimeService _inner;
    private readonly MapIdentityEvidenceLocation _location;

    public MapIdentityEvidencePortalRuntime(InMemorySessionAuthority sessionAuthority, MapIdentityEvidenceLocation location)
    {
        _location = location;
        _inner = new OfficialPortalPhase2RuntimeService(sessionAuthority, new InMemoryPortalTransitionStore());
    }

    public OperationResult<OfficialPortalRuntimeBinding> Resolve(string sessionId) => _inner.Resolve(sessionId);

    public Task<WorldInteractionResult> ExecuteAsync(
        WorldInteractionRequest request,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(request, cancellationToken);

    public Task<OperationResult> SeedSessionAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        // Portal authorization remains locked to an observed destination. The independent
        // TestOnly world projector may subsequently exercise the statically recovered
        // arbitrary position consumer without widening production portal eligibility.
        var seedCharacter = _location.ClientMapId == 19
            ? character with { MapId = 19, PositionX = 28, PositionY = 34 }
            : character with { MapId = 3, PositionX = 196, PositionY = 139 };
        return _inner.SeedSessionAsync(session, seedCharacter, cancellationToken);
    }

    public bool RemoveSession(string sessionId) => _inner.RemoveSession(sessionId);
}

sealed record AutomationProbeResult(
    string OverallStatus,
    IReadOnlyList<ProbeCheck> Checks,
    IReadOnlyList<StaticDataCount> StaticDataCounts,
    IReadOnlyList<AssemblyDiagnostic> Assemblies,
    ReflectionLoadDiagnostic? ReflectionTypeLoadException,
    IReadOnlyList<string>? ServerOutput = null)
{
    public static AutomationProbeResult Blocked(
        string name,
        string errorCode,
        string settingName,
        string source,
        string expected,
        string actualState,
        string safeRemediation) =>
        new(
            "BLOCKED",
            [ProbeCheck.Blocked(name, errorCode, settingName, source, expected, actualState, safeRemediation)],
            [],
            [],
            null);
}

sealed record ProbeCheck(
    string Name,
    string Status,
    string ErrorCode,
    string SettingName,
    string Source,
    string Expected,
    string ActualState,
    string SafeRemediation)
{
    public static ProbeCheck Pass(string name, string settingName, string source, string expected, string actualState) =>
        new(name, "PASS", string.Empty, settingName, source, expected, actualState, string.Empty);

    public static ProbeCheck Blocked(
        string name,
        string errorCode,
        string settingName,
        string source,
        string expected,
        string actualState,
        string safeRemediation) =>
        new(name, "BLOCKED", errorCode, settingName, source, expected, actualState, safeRemediation);
}

sealed record StaticDataCount(string Table, long Count);

sealed record AssemblyDiagnostic(string AssemblyName, string TypeName, string DllPath)
{
    public static AssemblyDiagnostic FromType(Type type) =>
        new(
            type.Assembly.GetName().FullName ?? type.Assembly.FullName ?? "<unknown-assembly>",
            type.FullName ?? type.Name,
            type.Assembly.Location);
}

sealed record ReflectionLoadDiagnostic(
    string Message,
    IReadOnlyList<LoaderExceptionDiagnostic> LoaderExceptions,
    IReadOnlyList<TypeLoadDiagnostic> Types)
{
    public static ReflectionLoadDiagnostic From(ReflectionTypeLoadException exception) =>
        new(
            exception.Message,
            exception.LoaderExceptions.Select(LoaderExceptionDiagnostic.From).ToArray(),
            exception.Types.Select(TypeLoadDiagnostic.From).ToArray());
}

sealed record LoaderExceptionDiagnostic(
    string ExceptionType,
    string Message,
    string? AssemblyName,
    string? FusionLog,
    string? Source,
    string? TargetSite)
{
    public static LoaderExceptionDiagnostic From(Exception? exception) =>
        exception is null
            ? new("null", string.Empty, null, null, null, null)
            : new(
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception is FileNotFoundException fileNotFound ? fileNotFound.FileName : null,
                exception is FileNotFoundException fusionException ? fusionException.FusionLog : null,
                exception.Source,
                exception.TargetSite?.ToString());
}

sealed record TypeLoadDiagnostic(string? TypeName, string? AssemblyName, string? DllPath)
{
    public static TypeLoadDiagnostic From(Type? type) =>
        new(type?.FullName, type?.Assembly.GetName().FullName, type?.Assembly.Location);
}

sealed class ReadySignalTextWriter : TextWriter
{
    private readonly object _sync = new();
    private readonly List<string> _lines = [];

    public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_sync)
            {
                return _lines.ToArray();
            }
        }
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void WriteLine(string? value)
    {
        var line = value ?? string.Empty;
        lock (_sync)
        {
            _lines.Add(line);
        }

        if (line.Contains("Server Ready", StringComparison.Ordinal))
        {
            Ready.TrySetResult(true);
        }
    }

    public override void Write(char value)
    {
    }
}





