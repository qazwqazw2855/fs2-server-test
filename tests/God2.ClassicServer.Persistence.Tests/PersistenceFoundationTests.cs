using System.Data;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class PersistenceFoundationTests
{
    [Fact]
    public void Roadmap_m3_npc_wire_identity_migration_is_additive_and_evidence_pinned()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "041_roadmap_m3_npc_wire_identity.sql"));

        Assert.Contains("`OfficialResourceType`", sql, StringComparison.Ordinal);
        Assert.Contains("`OfficialResourceOrdinal`", sql, StringComparison.Ordinal);
        Assert.Contains("`OfficialSelectorHighBits`", sql, StringComparison.Ordinal);
        Assert.Contains("`WireEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("'83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B'", sql, StringComparison.Ordinal);
        Assert.Contains("'7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644'", sql, StringComparison.Ordinal);
        Assert.Contains("'C0CBF5CDFAC3669D695E66D9C789AAA027360488024C1C5E4323311364A419C0'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `npcs`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roadmap_m3_partial_migration_recovery_skips_only_completed_atomic_alter()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "041_roadmap_m3_npc_wire_identity.sql"));

        var remaining = SqlFileMigrationRunner.SqlAfterFirstStatement(sql);

        Assert.DoesNotContain("ALTER TABLE", remaining, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, CountOccurrences(remaining, "UPDATE `npc_spawns`"));
        Assert.Contains("`Id`=316049902", remaining, StringComparison.Ordinal);
        Assert.Contains("`Id`=2124220824", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_m2_monster_spawn_gate_migration_disables_unproven_legacy_rows()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "042_roadmap_m2_monster_spawn_production_gate.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS `SpawnRadius`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `SpawnCount`", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT 'EvidenceBlocked'", sql, StringComparison.Ordinal);
        Assert.Contains("DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `spawns`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Packet_capture_protocol_evidence_migration_persists_evidence_without_gameplay_promotion()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "049");
        var sql = File.ReadAllText(migration.Path);
        var importPath = Path.Combine(
            root,
            "db",
            "imports",
            "evidence",
            "packet_capture",
            "captured_packet_evidence_20260806.database.json");

        Assert.Equal("049_packet_capture_protocol_evidence.sql", migration.Name);
        Assert.Contains("`packet_capture_evidence_sources`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_sessions`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_semantic_families`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_protocol_applications`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_content_application_gates`", sql, StringComparison.Ordinal);
        Assert.Contains("'God2_opt.exe'", sql, StringComparison.Ordinal);
        Assert.Contains("'x86'", sql, StringComparison.Ordinal);
        Assert.Contains("2, 5522, 5522, 5522, 2618, 1290, 13, 50, 5, 0", sql, StringComparison.Ordinal);
        Assert.Contains("'capture-20260806-client-action-c2s-20-candidate'", sql, StringComparison.Ordinal);
        Assert.Contains("'EvidenceOnly', 'Inferred', 0", sql, StringComparison.Ordinal);
        Assert.Contains("'WorldState', 'ServerToClient', 20, 'EvidenceOnly', 'Inferred', 0, 5, 110", sql, StringComparison.Ordinal);
        Assert.Contains("'WorldState', 'ServerToClient', 14, 'EvidenceOnly', 'Inferred', 0, 3, 74", sql, StringComparison.Ordinal);
        Assert.Contains("`VerifiedGameplayClassificationCount` = 0", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionEnabled` = 0", sql, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(sql, "INSERT INTO `packet_capture_"));
        Assert.DoesNotContain("INSERT INTO `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `content_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(importPath), "Packet capture database evidence import JSON is missing.");
        using var document = JsonDocument.Parse(File.ReadAllText(importPath));
        var evidence = document.RootElement;
        Assert.Equal("captured-packet-evidence-20260806-v1", evidence.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, evidence.GetProperty("sessions").GetArrayLength());
        Assert.Equal(7, evidence.GetProperty("semanticFamilies").GetArrayLength());
        Assert.Equal(50, evidence.GetProperty("representativeCandidates").GetArrayLength());
        Assert.Equal(0, evidence.GetProperty("aggregateTotals").GetProperty("verifiedGameplayClassifications").GetInt32());

        var serverApplication = evidence.GetProperty("serverApplication");
        Assert.Equal(0, serverApplication.GetProperty("verifiedPromotions").GetInt32());
        Assert.Equal(5, serverApplication.GetProperty("evidenceOnlyKnowledgeIds").GetArrayLength());

        foreach (var candidate in evidence.GetProperty("representativeCandidates").EnumerateArray())
        {
            Assert.Equal("Candidate", candidate.GetProperty("evidenceLevel").GetString());
        }
    }

    [Fact]
    public void Packet_capture_batch2_migration_is_additive_and_keeps_classification_candidate_only()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "050");
        var sql = File.ReadAllText(migration.Path);
        var importPath = Path.Combine(
            root,
            "db",
            "imports",
            "evidence",
            "packet_capture",
            "captured_packet_evidence_20260806_batch2.database.json");

        Assert.Equal("050_packet_capture_protocol_evidence_batch2.sql", migration.Name);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `RuntimeSessionId`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `AnalysisRunId`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `CaptureSource`", sql, StringComparison.Ordinal);
        Assert.Contains("'captured-packet-evidence-20260806-batch2'", sql, StringComparison.Ordinal);
        Assert.Contains("'OptInX86Dll'", sql, StringComparison.Ordinal);
        Assert.Contains("'InjectedWinsock'", sql, StringComparison.Ordinal);
        Assert.Contains("'God2_opt.exe', 6948, 'x86'", sql, StringComparison.Ordinal);
        Assert.Contains("1, 4779, 4779, 4779, 2451, 1334, 12, 50, 5, 0", sql, StringComparison.Ordinal);
        Assert.Contains("'Heartbeat', 'ClientToServer', 5, 'EvidenceOnly', 'Inferred', 0, 3, 266", sql, StringComparison.Ordinal);
        Assert.Contains("'WorldState', 'ServerToClient', 20, 'EvidenceOnly', 'Inferred', 0, 3, 53", sql, StringComparison.Ordinal);
        Assert.Contains("'WorldState', 'ServerToClient', 14, 'EvidenceOnly', 'Inferred', 0, 2, 61", sql, StringComparison.Ordinal);
        Assert.Contains("`VerifiedGameplayClassificationCount`=VALUES(`VerifiedGameplayClassificationCount`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `content_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(importPath), "Packet capture batch2 database evidence import JSON is missing.");
        using var document = JsonDocument.Parse(File.ReadAllText(importPath));
        var evidence = document.RootElement;
        Assert.Equal("captured-packet-evidence-20260806-batch2-v1", evidence.GetProperty("schemaVersion").GetString());
        Assert.Equal(1, evidence.GetProperty("sessions").GetArrayLength());
        Assert.Equal(7, evidence.GetProperty("semanticFamilies").GetArrayLength());
        Assert.Equal(50, evidence.GetProperty("representativeCandidates").GetArrayLength());
        Assert.Equal(0, evidence.GetProperty("aggregateTotals").GetProperty("verifiedGameplayClassifications").GetInt32());
        Assert.Equal(2451, evidence.GetProperty("aggregateTotals").GetProperty("candidateFrames").GetInt32());
        Assert.Equal(1334, evidence.GetProperty("aggregateTotals").GetProperty("semanticCandidateClusters").GetInt32());
        Assert.Equal(12, evidence.GetProperty("aggregateTotals").GetProperty("highConfidenceSemanticCandidates").GetInt32());

        var serverApplication = evidence.GetProperty("serverApplication");
        Assert.Equal(0, serverApplication.GetProperty("verifiedPromotions").GetInt32());
        Assert.Equal(5, serverApplication.GetProperty("evidenceOnlyKnowledgeIds").GetArrayLength());

        foreach (var candidate in evidence.GetProperty("representativeCandidates").EnumerateArray())
        {
            Assert.Equal("Candidate", candidate.GetProperty("evidenceLevel").GetString());
        }
    }

    [Fact]
    public void Evidence_package_migration_persists_decoded_and_handler_aggregates_without_gameplay_mutation()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "051");
        var sql = File.ReadAllText(migration.Path);
        var evidencePath = Path.Combine(
            root,
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion",
            "God2Evidence.20260806.102928.json");
        var importPath = Path.Combine(
            root,
            "db",
            "imports",
            "evidence",
            "packet_capture",
            "god2_evidence_package_20260806_102928.database.json");

        Assert.Equal("051_god2_evidence_package_decoded_handler_evidence.sql", migration.Name);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `PackageSha256`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `DecodedMessageCount`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_decoded_stage_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_handler_family_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'god2-evidence-package-20260806-102928'", sql, StringComparison.Ordinal);
        Assert.Contains("'6c8d52617f652f522610abdb3d4db1b381e0a21a08b8e60e4a574ac66f7e9951'", sql, StringComparison.Ordinal);
        Assert.Contains("1, 3482, 1685, 1685, 0, 64, 0, 0, 7, 0", sql, StringComparison.Ordinal);
        Assert.Contains("3482, 5797, 1685, 630, 1685, 767, 2715, 44327", sql, StringComparison.Ordinal);
        Assert.Contains("'PreEncrypt', 'ClientToServer', 786, 483, 29, 786, 5, 208", sql, StringComparison.Ordinal);
        Assert.Contains("'PostDecrypt', 'ServerToClient', 899, 490, 35, 899, 5, 1254", sql, StringComparison.Ordinal);
        Assert.Equal(8, CountOccurrences(sql, "'EvidenceOnly', 'Unknown', 0, 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json')"));
        Assert.Equal(7, CountOccurrences(sql, "'EvidenceOnly', 'Inferred', 0"));
        Assert.Contains("'FormulaRecovery', 'EvidenceBlocked', 3", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionEnabled` = 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `monsters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `characters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `characters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `inventory", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `inventory", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(evidencePath));
        using (var evidenceDocument = JsonDocument.Parse(File.ReadAllText(evidencePath)))
        {
            var evidence = evidenceDocument.RootElement;
            Assert.Equal(5797, evidence.GetProperty("session").GetProperty("captureRecordCount").GetInt32());
            Assert.Equal(1685, evidence.GetProperty("decodedFrameEvidence").GetProperty("lengthPrefixValidatedCount").GetInt32());
            Assert.Equal(64, evidence.GetProperty("decodedFrameEvidence").GetProperty("directionalOpcodeFamilyCount").GetInt32());
            Assert.Equal(630, evidence.GetProperty("handlerEvidence").GetProperty("handlerObservationCount").GetInt32());
            Assert.Equal(0, evidence.GetProperty("serverApplication").GetProperty("verifiedGameplayPromotions").GetInt32());
            Assert.False(evidence.GetProperty("sensitiveDataPolicy").GetProperty("rawPayloadRetainedInRepository").GetBoolean());
            Assert.False(evidence.GetProperty("sensitiveDataPolicy").GetProperty("credentialTextRetainedInRepository").GetBoolean());
        }

        Assert.True(File.Exists(importPath));
        using var importDocument = JsonDocument.Parse(File.ReadAllText(importPath));
        var import = importDocument.RootElement;
        Assert.Equal(1685, import.GetProperty("decodedMessageCount").GetInt32());
        Assert.Equal(8, import.GetProperty("handlerFamilies").GetArrayLength());
        Assert.Equal(0, import.GetProperty("verifiedGameplayClassificationCount").GetInt32());
        Assert.False(import.GetProperty("productionPolicy").GetProperty("productionEnabled").GetBoolean());
        Assert.False(import.GetProperty("productionPolicy").GetProperty("rawSensitivePayloadStored").GetBoolean());
    }

    [Fact]
    public void Evidence_package_20260807_supplement_persists_only_structural_and_strong_correlation_evidence()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "058");
        var sql = File.ReadAllText(migration.Path);
        var evidencePath = Path.Combine(
            root, "src", "God2.ClassicServer.Protocol", "Evidence", "OfficialProtocolCompletion",
            "God2Evidence.20260807.Supplement.json");
        var importPath = Path.Combine(
            root, "db", "imports", "evidence", "packet_capture",
            "god2_evidence_package_20260807_supplement.database.json");

        Assert.Equal("058_god2_evidence_package_20260807_supplement.sql", migration.Name);
        Assert.Contains("`packet_capture_supplemental_frame_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_stage_correlation_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_frame_handler_correlation_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'god2-evidence-package-20260807-supplement'", sql, StringComparison.Ordinal);
        Assert.Contains("2, 2833, 1344, 1344, 1004, 0, 0, 0, 62, 0", sql, StringComparison.Ordinal);
        Assert.Contains("1547, 2475, 727, 201, 727, 312, 1235, 20467", sql, StringComparison.Ordinal);
        Assert.Contains("1286, 2254, 617, 351, 617, 286, 1000, 24580", sql, StringComparison.Ordinal);
        Assert.Equal(17, CountOccurrences(sql, "'Strong', 'EvidenceOnly', 'Unknown', 'NotVerified', 0"));
        Assert.Contains("'GameplayRuntimeMutation', 'EvidenceBlocked', 0", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionEnabled` = 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `characters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `inventory", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `quests`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);

        using var evidenceDocument = JsonDocument.Parse(File.ReadAllText(evidencePath));
        var evidence = evidenceDocument.RootElement;
        var supplemental = evidence.GetProperty("supplementalDecodedFrameEvidence");
        Assert.Equal(62, supplemental.GetProperty("familyCount").GetInt32());
        Assert.Equal(87, supplemental.GetProperty("observationCount").GetInt32());
        Assert.Equal(85, supplemental.GetProperty("uniqueFrameCount").GetInt32());
        Assert.Equal(17, evidence.GetProperty("newPackageCorrelationEvidence")
            .GetProperty("outerFrameHandlerGroups").GetArrayLength());
        Assert.Equal(0, evidence.GetProperty("serverApplication")
            .GetProperty("verifiedGameplayPromotions").GetInt32());
        Assert.False(evidence.GetProperty("productionPolicy").GetProperty("productionEnabled").GetBoolean());

        using var importDocument = JsonDocument.Parse(File.ReadAllText(importPath));
        var import = importDocument.RootElement;
        Assert.Equal(2, import.GetProperty("sessions").GetArrayLength());
        Assert.Equal(62, import.GetProperty("aggregateTotals")
            .GetProperty("supplementalStructuralFamilies").GetInt32());
        Assert.Equal(0, import.GetProperty("aggregateTotals")
            .GetProperty("verifiedGameplayClassifications").GetInt32());
        Assert.False(import.GetProperty("serverApplication").GetProperty("productionEnabled").GetBoolean());
    }

    [Fact]
    public async Task MariaDb_probe_fails_when_json_password_is_missing()
    {
        var probe = new MariaDbDatabaseBootstrapper();

        var result = await probe.ProbeAsync(new DatabaseOptions("127.0.0.1", 3306, "god2", "god2", string.Empty, 1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("mariadb.password_missing", result.Error.Code);
    }

    [Fact]
    public void Migration_files_are_discovered_in_version_order()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "002_second.sql"), "SELECT 2;");
        File.WriteAllText(Path.Combine(temp.Path, "001_first.sql"), "SELECT 1;");

        var files = SqlMigrationFile.Discover(temp.Path);

        Assert.Equal(["001", "002"], files.Select(file => file.Version));
    }

    [Fact]
    public void Migration_report_outputs_required_console_lines()
    {
        var report = new MigrationReport(
        [
            new MigrationExecution("001", "001_accounts.sql", MigrationStatus.Applied),
            new MigrationExecution("002", "002_characters.sql", MigrationStatus.Applied)
        ]);

        Assert.Contains("Migration 001 Applied", report.ConsoleLines);
        Assert.Contains("Migration 002 Applied", report.ConsoleLines);
        Assert.Equal("Database Ready", report.ConsoleLines[^1]);
    }

    [Fact]
    public void Character_lifecycle_migration_adds_formal_life_skill_and_delete_state_columns()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "022_character_lifecycle.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS `LifeSkill`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `Class`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `Gender`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `Status`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `DeletedAtUtc`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_m7_character_creation_authority_is_additive_and_fail_closed()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "044");
        var sql = File.ReadAllText(migration.Path);
        var repositorySource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeRepositories.cs"));

        Assert.Equal("044_character_creation_authority.sql", migration.Name);
        Assert.Contains("`character_creation_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionEnabled` = 0 OR `EvidenceStatus` IN ('Verified', 'Derived')", sql, StringComparison.Ordinal);
        Assert.Contains("No profile is seeded here", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `character_creation_profiles`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM `god2_game`.`character_creation_profiles` profile", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`character_classes` class_row", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`class_level_stats` level_stats", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`maps` map_row", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("profile.`enabled` = 1", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("class_row.`enabled` = 1", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("level_stats.`enabled` = 1", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("level_stats.`base_max_hp` IS NOT NULL", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("level_stats.`base_max_mp` IS NOT NULL", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.`evidence_status`", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("map_row.`enabled` = 1", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", repositorySource, StringComparison.Ordinal);
        Assert.Contains("`status` NOT IN ('Deleted','已刪除') AND `enabled` = 1 AND `deleted_at_utc` IS NULL", repositorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Character_lifecycle_integrity_migration_supports_active_name_reuse_and_durable_replay()
    {
        var root = RepositoryRoot();
        var sql = File.ReadAllText(Path.Combine(root, "database", "schema", "045_character_lifecycle_integrity.sql"));
        var repositorySource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeRepositories.cs"));

        Assert.Contains("DROP INDEX IF EXISTS `UX_Characters_Name`", sql, StringComparison.Ordinal);
        Assert.Contains("GENERATED ALWAYS AS", sql, StringComparison.Ordinal);
        Assert.Contains("`UX_Characters_ActiveName`", sql, StringComparison.Ordinal);
        Assert.Contains("`character_lifecycle_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`AccountId`, `IdempotencyKeyHash`)", sql, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", sql, StringComparison.Ordinal);
        Assert.Contains("FindLifecycleReplayAsync", repositorySource, StringComparison.Ordinal);
        Assert.Contains("StoreLifecycleReplayAsync", repositorySource, StringComparison.Ordinal);
        Assert.Contains("profile.`class_code` = @class", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("Class1", MariaDbCharacterCreationAuthority.ResolveCommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Gameplay_inventory_migration_adds_formal_transaction_runtime_tables()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "023_inventory_transactions.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS `PersistentInventoryItemId`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `ItemCategory`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `StackPolicy`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `CurrencyType`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `player_inventory_state`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `player_currency_balances`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `inventory_transaction_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `inventory_audit_ledger`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_m6_inventory_commit_locks_and_compares_mariadb_authority()
    {
        var sql = MariaDbGameplayInventoryRepository.ExpectedInventoryStateLockCommandText;
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));

        Assert.Contains("FROM `god2_player`.`player_inventory_state`", sql, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT `character_id`", source, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_player`.`characters`", source, StringComparison.Ordinal);
        Assert.Contains("inventory.version_conflict", source, StringComparison.Ordinal);
        Assert.Contains("inventory.replay_completed", source, StringComparison.Ordinal);
        Assert.Contains("ResolveReplayOrFailureAsync(commit, expectedState", source, StringComparison.Ordinal);
        Assert.Contains("ReadInt64(reader, \"InventoryVersionBefore\")", source, StringComparison.Ordinal);
        Assert.Contains("Convert.ToInt64(reader.GetValue", source, StringComparison.Ordinal);
        Assert.Contains("ReadString(reader, \"TransactionId\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_m6_inventory_authority_and_identity_migration_is_additive_and_fail_closed()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "043");
        var sql = File.ReadAllText(migration.Path);
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));

        Assert.Equal("043_inventory_authority_and_global_item_identity.sql", migration.Name);
        Assert.Contains("`inventory_item_identity_sequence`", sql, StringComparison.Ordinal);
        Assert.Contains("AUTO_INCREMENT=8000000000000000000", sql, StringComparison.Ordinal);
        Assert.Contains("UX_InventorySlots_PersistentInventoryItemId", sql, StringComparison.Ordinal);
        Assert.Contains("MODIFY COLUMN `PersistentInventoryItemId` bigint NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN `god2_player`.`accounts`", source, StringComparison.Ordinal);
        Assert.Contains("`a`.`current_session_id` = @sessionId", source, StringComparison.Ordinal);
        Assert.Contains("`c`.`account_id` = @accountId", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.RepeatableRead", source, StringComparison.Ordinal);
        Assert.Contains("ReservePersistentItemIdsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void World_interaction_migration_adds_versioned_location_idempotency_and_audit()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "024_world_interactions.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS `CurrentDirection`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `RuntimeVersion`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `LastPortalTemplateId`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `LastPortalTransitionId`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `world_interaction_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `world_interaction_audit`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_runner_fails_when_schema_directory_is_missing()
    {
        var runner = new SqlFileMigrationRunner(
            new DatabaseOptions("127.0.0.1", 3306, "god2", "god2", "test-password", 1),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var result = await runner.VerifyAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("migration.schema_directory_missing", result.Error.Code);
    }

    [Fact]
    public void Canonical_invariant_repair_includes_non_generated_columns_reported_as_null_by_mariadb()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "143_repair_canonical_catalog_invariants.sql"));

        Assert.Contains("`GENERATION_EXPRESSION` IS NULL OR", sql, StringComparison.Ordinal);
        Assert.Contains("Canonical column comment repair incomplete", sql, StringComparison.Ordinal);
        Assert.Contains("Canonical character life-skill repair incomplete", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Traditional_chinese_migration_repairs_runtime_text_without_rewriting_raw_evidence()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "144_enforce_traditional_chinese_runtime_text.sql"));

        Assert.Contains("'活动结束后','活動結束後'", sql, StringComparison.Ordinal);
        Assert.Contains("'倒霉','倒楣'", sql, StringComparison.Ordinal);
        Assert.Contains("'仓','倉'", sql, StringComparison.Ordinal);
        Assert.Contains("'台版','臺版'", sql, StringComparison.Ordinal);
        Assert.Contains("Traditional Chinese runtime text normalization incomplete", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET `raw_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET `original_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET `source_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET `payload", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formal_runtime_catalog_queries_only_official_canonical_runtime_tables()
    {
        var query = FormalRuntimeDataCatalog.BuildCountQuery();

        Assert.DoesNotContain("official_import_", query, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            FormalRuntimeDataCatalog.Tables,
            table => Assert.Contains(
                $"FROM `{table.SchemaName}`.`{table.TableName}`",
                query,
                StringComparison.Ordinal));
        Assert.Contains(("Item", "god2_game", "items"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Weapon", "god2_game", "weapons"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Equipment", "god2_game", "equipment"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Magic Treasure", "god2_game", "magic_treasures"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Item Set Bonus", "god2_game", "item_set_bonuses"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Map", "god2_game", "maps"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("NPC Spawn", "god2_game", "npc_spawns"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Monster Combat Stat Design Rule", "god2_game", "monster_combat_stat_design_rules"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Monster Drop Design Rule", "god2_game", "monster_drop_design_rules"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Monster Spawn Design Rule", "god2_game", "monster_spawn_design_rules"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Class Stat Growth", "god2_game", "class_stat_growth"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Immortal Rank", "god2_game", "immortal_ranks"), FormalRuntimeDataCatalog.Tables);
        Assert.Contains(("Pet Growth Archetype", "god2_game", "pet_growth_archetypes"), FormalRuntimeDataCatalog.Tables);
        Assert.DoesNotContain(
            FormalRuntimeDataCatalog.Tables,
            table => table.TableName is "localization_entries" or "client_map_identities");
    }

    [Fact]
    public void Client_map_identity_materializer_preserves_build_transform_and_evidence()
    {
        using var reader = SingleRowReader(
            ("MapId", typeof(int), 1785918668),
            ("ClientBuildId", typeof(string), "god2-opt-6b127086e0c0"),
            ("ClientMapId", typeof(int), 19),
            ("ClientAreaId", typeof(int), 4),
            ("ResourceIdentity", typeof(string), "r_b720effd89db741cf7bb9830f6a01bee"),
            ("CoordinateScaleX", typeof(decimal), 1.25m),
            ("CoordinateScaleY", typeof(decimal), 1.5m),
            ("CoordinateOffsetX", typeof(decimal), -10m),
            ("CoordinateOffsetY", typeof(decimal), 20m),
            ("IdentityEvidenceStatus", typeof(string), "Verified"),
            ("CoordinateEvidenceStatus", typeof(string), "Derived"),
            ("ProductionEnabled", typeof(bool), true),
            ("EvidenceReference", typeof(string), "fixture-evidence"));

        var identity = MariaDbStaticDataLoader.MaterializeClientMapIdentity(reader);

        Assert.Equal(1785918668, identity.MapId);
        Assert.Equal((ushort)19, identity.ClientMapId);
        Assert.Equal((byte)4, identity.ClientAreaId);
        Assert.Equal(1.25m, identity.CoordinateScaleX);
        Assert.Equal("Verified", identity.IdentityEvidenceStatus);
        Assert.True(identity.ProductionEnabled);
    }

    [Fact]
    public void Formal_mariadb_repositories_can_be_composed_without_opening_test_connections()
    {
        var options = new DatabaseOptions(
            "127.0.0.1",
            3306,
            "god2",
            "god2",
            string.Empty,
            1);

        var accounts = new MariaDbAccountRepository(options);
        var characters = new MariaDbCharacterRepository(options);
        var gameplayContent = new MariaDbGameplayContentCatalogRepository(options);
        var promotedGameplayContent = new MariaDbPromotedGameplayContentRuntime(options);
        var inventory = new MariaDbGameplayInventoryRepository(options);
        var portalTransitions = new MariaDbPortalTransitionStore(options);

        Assert.IsAssignableFrom<God2.ClassicServer.Runtime.IAccountRepository>(accounts);
        Assert.IsAssignableFrom<God2.ClassicServer.Runtime.ICharacterRepository>(characters);
        Assert.NotNull(gameplayContent);
        Assert.IsAssignableFrom<IRuntimeCacheBuilder>(promotedGameplayContent);
        Assert.IsAssignableFrom<IInventoryPersistenceStore>(inventory);
        Assert.IsAssignableFrom<IPortalTransitionStore>(portalTransitions);
        Assert.IsAssignableFrom<IWorldMovementStore>(portalTransitions);
    }

    [Fact]
    public void Portal_location_materialization_accepts_the_char36_guid_type_returned_by_mysqlconnector()
    {
        var transitionId = Guid.Parse("c9cf4d0d-9de8-4664-ae96-7a3badd90b5b");
        using var reader = SingleRowReader(
            ("Id", typeof(long), 9L),
            ("MapId", typeof(int), 170015007),
            ("PositionX", typeof(int), 48),
            ("PositionY", typeof(int), 81),
            ("CurrentDirection", typeof(string), "Unknown"),
            ("RuntimeVersion", typeof(long), 29L),
            ("LastPortalTemplateId", typeof(int), 1346895879),
            ("LastPortalTransitionId", typeof(Guid), transitionId),
            ("UpdatedAtUtc", typeof(DateTime), new DateTime(2026, 8, 12, 22, 56, 22, DateTimeKind.Unspecified)));

        var state = MariaDbPortalTransitionStore.MaterializeLocation(reader);

        Assert.Equal(9, state.CharacterId);
        Assert.Equal(170015007, state.CurrentMapId);
        Assert.Equal(new WorldPosition3(48, 81), state.RawPosition);
        Assert.Equal(29, state.RuntimeVersion);
        Assert.Equal(transitionId, state.LastTransitionId);
    }

    [Fact]
    public async Task ProductionWorldSessionBindingFailsBeforeDatabaseAccessWhenContentReleaseIsNotReady()
    {
        var options = new DatabaseOptions("127.0.0.1", 3306, "god2", "god2", string.Empty, 1);
        var coordinator = new MariaDbWorldSessionCoordinator(
            new MariaDbStaticDataLoader(options),
            new BlockedGameplayContentAuthority());
        var session = RuntimeSession.Connected("connection", "127.0.0.1:1000", DateTimeOffset.UnixEpoch) with
        {
            SessionId = "session",
            AccountId = 10,
            CharacterId = 20,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var character = new CharacterSummary(
            20, 10, "Player", "Class1", "Gender1", "LifeSkill1", 1, "Default",
            19, 17, 11, "Active", DateTimeOffset.UnixEpoch, null);

        var result = await coordinator.BindAsync(session, character, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("content_release.runtime_not_ready", result.Error.Code);
        Assert.Equal(nameof(MariaDbWorldSessionCoordinator), result.Error.Source);
    }

    [Fact]
    public void M8PromotedGameplayContentSnapshotStartsEmptyAndFailClosed()
    {
        var snapshot = PromotedGameplayContentSnapshot.Empty;

        Assert.Empty(snapshot.ReleaseId);
        Assert.Empty(snapshot.SourceRunId);
        Assert.Empty(snapshot.FormalCatalogFingerprint);
        Assert.Equal(0, snapshot.FormalCatalogRecordCount);
        Assert.Empty(snapshot.QuestProfiles);
        Assert.Empty(snapshot.QuestObjectives);
        Assert.Empty(snapshot.EquipmentSets);
        Assert.Empty(snapshot.EquipmentSetMembers);
        Assert.Empty(snapshot.PetInnates);
        Assert.Empty(snapshot.ProductionCoverage);
        Assert.Empty(snapshot.EvidenceBlockedFamilies);
    }

    [Fact]
    public void M8FormalCatalogManifestIsDeterministicAndChangesWithRuntimeContent()
    {
        var firstSnapshot = FormalRuntimeStaticSnapshot.Empty with
        {
            Maps = new Dictionary<int, MapStaticData>
            {
                [19] = new(19, "map-19", "雜貨店", 10, 30)
            },
            Counts = [new StaticDataLoadCount("Map", 1)],
            TableFingerprints = FormalRuntimeDataCatalog.Tables.ToDictionary(
                table => table.TableName,
                _ => new string('A', 64),
                StringComparer.Ordinal)
        };
        var reorderedSnapshot = firstSnapshot with
        {
            Counts = [new StaticDataLoadCount("Map", 1)]
        };
        var changedSnapshot = firstSnapshot with
        {
            Maps = new Dictionary<int, MapStaticData>
            {
                [19] = new(19, "map-19", "道具店", 10, 30)
            }
        };
        var unmaterializedTableChanged = firstSnapshot with
        {
            TableFingerprints = FormalRuntimeDataCatalog.Tables.ToDictionary(
                table => table.TableName,
                table => table.TableName == "status_effects" ? new string('B', 64) : new string('A', 64),
                StringComparer.Ordinal)
        };

        var first = FormalRuntimeCatalogManifestBuilder.Build(firstSnapshot);
        var reordered = FormalRuntimeCatalogManifestBuilder.Build(reorderedSnapshot);
        var changed = FormalRuntimeCatalogManifestBuilder.Build(changedSnapshot);
        var changedUnmaterializedTable = FormalRuntimeCatalogManifestBuilder.Build(unmaterializedTableChanged);

        Assert.Equal(FormalRuntimeCatalogManifestBuilder.Version, first.Version);
        Assert.Equal(1, first.RecordCount);
        Assert.Equal(first.Sha256, reordered.Sha256);
        Assert.NotEqual(first.Sha256, changed.Sha256);
        Assert.NotEqual(first.Sha256, changedUnmaterializedTable.Sha256);
        Assert.Throws<InvalidOperationException>(() =>
            FormalRuntimeCatalogManifestBuilder.Build(firstSnapshot with
            {
                TableFingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
            }));
    }

    [Fact]
    public void M8ProductionDisplayTextValidationProtectsTraditionalTextTokensAndRejectsUnsafeText()
    {
        MariaDbPromotedGameplayContentRuntime.ValidateDisplayText(
            ["獲得 {0} 個物品 <color=red>[item:123]</color>\\nmonster_fire_01"]);

        Assert.Throws<InvalidOperationException>(() =>
            MariaDbPromotedGameplayContentRuntime.ValidateDisplayText(["获得 {0} 个物品"]));
        Assert.Throws<InvalidOperationException>(() =>
            MariaDbPromotedGameplayContentRuntime.ValidateDisplayText(["損壞\uFFFD文字"]));
        Assert.Throws<InvalidOperationException>(() =>
            MariaDbPromotedGameplayContentRuntime.ValidateDisplayText(["獲得 {0 個物品"]));
        Assert.Throws<InvalidOperationException>(() =>
            MariaDbPromotedGameplayContentRuntime.ValidateDisplayText(["<color=red未關閉"]));
    }

    [Fact]
    public void M8EvidenceBlockedFamiliesRequireCompleteDistinctCoverageInsteadOfOnePassingRow()
    {
        var complete = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Maps"] = 1,
            ["ClientMappedMaps"] = 1,
            ["NpcTemplates"] = 1,
            ["NpcProductionSpawns"] = 1,
            ["MonsterTemplates"] = 1,
            ["MonsterProductionSpawns"] = 1,
            ["MonsterExecutionProfiles"] = 1,
            ["Skills"] = 1,
            ["SkillExecutionProfiles"] = 1,
            ["Quests"] = 1,
            ["PromotedQuestProfiles"] = 1,
            ["MonsterDropPolicyResolved"] = 1,
            ["DropRelationshipCandidates"] = 2,
            ["ResolvedDropRelationships"] = 2,
            ["Merchants"] = 1,
            ["MerchantInventoryCandidates"] = 2,
            ["ResolvedMerchantInventory"] = 2,
            ["MerchantInventoryCovered"] = 1,
            ["QuestProfilesWithResolvedObjectives"] = 1,
            ["QuestObjectiveCandidates"] = 2,
            ["ResolvedQuestObjectives"] = 2,
            ["PromotedEquipmentSets"] = 1,
            ["ResolvedEquipmentBonuses"] = 1,
            ["CombineCandidates"] = 2,
            ["ResolvedCombineRecipes"] = 2,
            ["PetEggRelationshipCandidates"] = 1,
            ["ResolvedPetEggRelationships"] = 1
        };

        Assert.Empty(MariaDbPromotedGameplayContentRuntime.FindEvidenceBlockedFamilies(complete));

        complete["ResolvedDropRelationships"] = 1;
        complete["ResolvedMerchantInventory"] = 1;
        complete["ResolvedQuestObjectives"] = 1;
        complete["ResolvedEquipmentBonuses"] = 0;
        complete["ResolvedCombineRecipes"] = 1;
        complete["ResolvedPetEggRelationships"] = 0;
        var blocked = MariaDbPromotedGameplayContentRuntime.FindEvidenceBlockedFamilies(complete);

        Assert.Contains("Drop", blocked);
        Assert.Contains("MerchantInventory", blocked);
        Assert.Contains("QuestObjectiveCandidates", blocked);
        Assert.Contains("EquipmentBonus", blocked);
        Assert.Contains("Combine", blocked);
        Assert.Contains("PetEggHatch", blocked);
    }

    [Fact]
    public void M8PromotedLookupsReportAuthorityNotReadyBeforeMissingIdentity()
    {
        var runtime = new MariaDbPromotedGameplayContentRuntime(new DatabaseOptions(
            "127.0.0.1", 3306, "god2", "god2", string.Empty, 1));

        Assert.Equal("content_release.runtime_not_ready", runtime.ResolveQuest(1).Error.Code);
        Assert.Equal("content_release.runtime_not_ready", runtime.ResolveEquipmentSet(1).Error.Code);
        Assert.Equal("content_release.runtime_not_ready", runtime.ResolvePetInnate(1).Error.Code);
    }

    [Fact]
    public void M8ContentReleaseIdentityMaterializerAcceptsMariaDbGuidValues()
    {
        var identity = Guid.Parse("598a069d-415c-439a-9a44-a23ca4369531");
        using var reader = SingleRowReader(("ReleaseId", typeof(Guid), identity));

        var materialized = MariaDbPromotedGameplayContentRuntime.ReadDatabaseIdentity(reader, 0);

        Assert.Equal("598a069d-415c-439a-9a44-a23ca4369531", materialized);
    }

    [Fact]
    public void M8ProductionRepositoriesPreferTraditionalChineseDisplayColumns()
    {
        var root = RepositoryRoot();
        var staticData = File.ReadAllText(Path.Combine(root, "src", "God2.ClassicServer.Persistence", "MariaDbRuntimeData.cs"));
        var inventory = File.ReadAllText(Path.Combine(root, "src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRepository.cs"));
        var skill = File.ReadAllText(Path.Combine(root, "src", "God2.ClassicServer.Persistence", "MariaDbSkillRuntimeStore.cs"));
        var quest = File.ReadAllText(Path.Combine(root, "src", "God2.ClassicServer.Persistence", "MariaDbQuestRuntimeStore.cs"));

        Assert.True(CountOccurrences(staticData, "`name_zh_tw`") >= 7);
        Assert.Contains("`name_zh_tw` AS `RuntimeDisplayName`", inventory, StringComparison.Ordinal);
        Assert.Contains("`name_zh_tw` AS `Name`", inventory, StringComparison.Ordinal);
        Assert.Contains("COALESCE(`NameZhTw`, `Name`) AS `Name`", skill, StringComparison.Ordinal);
        Assert.Contains("COALESCE(`NameZhTw`, `Name`) AS `Name`", quest, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_account_session_replacement_uses_expected_owner_compare_and_swap()
    {
        var sql = MariaDbAccountRepository.ReplaceCurrentSessionCommandText;

        Assert.Contains("UPDATE `god2_player`.`accounts`", sql, StringComparison.Ordinal);
        Assert.Contains("`current_session_id` <=> @expectedCurrentSessionId", sql, StringComparison.Ordinal);
        Assert.Contains("`current_session_id` = @replacementSessionId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`current_session_id` IS NULL OR", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formal_runtime_cache_does_not_publish_without_a_complete_loaded_candidate()
    {
        var options = new DatabaseOptions(
            "127.0.0.1",
            3306,
            "god2",
            "god2",
            string.Empty,
            1);
        var cache = new MariaDbStaticDataLoader(options);

        var result = await cache.BuildAsync(
            [new StaticDataLoadCount("Item", 17_407)],
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(cache.PublishedSnapshot.Items);
        Assert.Empty(cache.PublishedSnapshot.Counts);
        Assert.Equal(0, cache.PublishedSnapshot.EstimatedManagedBytes);
    }

    [Fact]
    public void Formal_map_materializer_reads_integer_dimensions()
    {
        using var reader = SingleRowReader(
            ("Id", typeof(int), 1),
            ("Code", typeof(string), "client-map"),
            ("Name", typeof(string), "Client Map"),
            ("Width", typeof(int), 20),
            ("Height", typeof(int), 52),
            ("ResourceIdentity", typeof(string), "island03/cityi2/cityi2.mdt"),
            ("SourceIdentity", typeof(string), "client:map/island03/cityi2/cityi2"),
            ("PayloadSha256", typeof(string), new string('A', 64)),
            ("EvidenceStatus", typeof(string), "Verified"));

        var map = MariaDbStaticDataLoader.MaterializeMap(reader);

        Assert.Equal(1, map.Id);
        Assert.Equal(20, map.Width);
        Assert.Equal(52, map.Height);
        Assert.Equal("island03/cityi2/cityi2.mdt", map.ResourceIdentity);
        Assert.Equal("client:map/island03/cityi2/cityi2", map.SourceIdentity);
        Assert.Equal("Verified", map.EvidenceStatus);
    }

    [Fact]
    public void Formal_map_materializer_preserves_missing_dimensions_as_null()
    {
        using var reader = SingleRowReader(
            ("Id", typeof(int), 2),
            ("Code", typeof(string), "client-indoor-map"),
            ("Name", typeof(string), "Client Indoor Map"),
            ("Width", typeof(int), null),
            ("Height", typeof(int), null),
            ("ResourceIdentity", typeof(string), null),
            ("SourceIdentity", typeof(string), null),
            ("PayloadSha256", typeof(string), null),
            ("EvidenceStatus", typeof(string), null));

        var map = MariaDbStaticDataLoader.MaterializeMap(reader);

        Assert.Null(map.Width);
        Assert.Null(map.Height);
        Assert.Null(map.ResourceIdentity);
    }

    [Fact]
    public void Formal_runtime_materializers_preserve_official_nullable_fields_as_null()
    {
        using var portalReader = SingleRowReader(
            ("Id", typeof(long), 1L),
            ("SourceMapId", typeof(int), null),
            ("SourceX", typeof(int), null),
            ("SourceY", typeof(int), null),
            ("TargetMapId", typeof(int), null),
            ("TargetX", typeof(int), null),
            ("TargetY", typeof(int), null),
            ("Name", typeof(string), "Recovered portal"));
        var portal = MariaDbStaticDataLoader.MaterializePortal(portalReader);
        Assert.Equal(0, portal.SourceRadius);

        using var monsterReader = SingleRowReader(
            ("Id", typeof(int), 1),
            ("Code", typeof(string), "monster"),
            ("Name", typeof(string), "Monster"),
            ("Level", typeof(int), null),
            ("MaxHp", typeof(long), null),
            ("Attack", typeof(int), null),
            ("Defense", typeof(int), null),
            ("MaxMp", typeof(long), null),
            ("MagicAttack", typeof(int), null),
            ("MagicDefense", typeof(int), null),
            ("Metal", typeof(int), null),
            ("Wood", typeof(int), null),
            ("Water", typeof(int), null),
            ("Fire", typeof(int), null),
            ("Earth", typeof(int), null));
        var monster = MariaDbStaticDataLoader.MaterializeMonster(monsterReader);

        using var itemReader = SingleRowReader(
            ("Id", typeof(int), 1),
            ("Code", typeof(string), "item"),
            ("Name", typeof(string), "Item"),
            ("ItemType", typeof(string), null),
            ("MaxStack", typeof(int), null),
            ("SellPrice", typeof(long), null));
        var item = MariaDbStaticDataLoader.MaterializeItem(itemReader);

        Assert.Null(portal.SourceMapId);
        Assert.Null(portal.SourceX);
        Assert.Null(portal.TargetMapId);
        Assert.Null(monster.Level);
        Assert.Null(monster.MaxHp);
        Assert.Null(monster.MaxMp);
        Assert.Null(monster.MagicAttack);
        Assert.Null(monster.MagicDefense);
        Assert.Null(item.ItemType);
        Assert.Null(item.MaxStack);
        Assert.Null(item.SellPrice);
    }

    [Fact]
    public void Monster_runtime_blackbox_columns_persist_mp_magic_and_element_stats()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "373_add_monster_combat_runtime_mp_magic_element_columns.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS `MaximumMp`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `CurrentMp`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `MagicAttackPower`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `MagicDefense`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS `Metal`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `最大MP`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `目前MP`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `魔法攻擊`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `魔法防禦`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_designed_baseline_monster_ai_does_not_fake_monster_skill_evidence()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "374_seed_service_designed_baseline_monster_ai_profiles.sql"));

        Assert.Contains("普通攻擊型", sql, StringComparison.Ordinal);
        Assert.Contains("'BasicAttack'", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE `god2_game`.`monsters`", sql, StringComparison.Ordinal);
        Assert.Contains("`ai_profile_id` = 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`monster_skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("官方", sql.Replace("沒有官方技能證據", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Monster_ai_repository_projects_service_designed_rules_into_runtime_definitions()
    {
        var rows = new[]
        {
            new CanonicalCatalogRow(new Dictionary<string, object?>
            {
                ["monster_id"] = 27,
                ["ai_profile_id"] = 1,
                ["profile_note"] = "ServiceDesigned; black-box baseline",
                ["priority"] = 100,
                ["skill_id"] = null,
                ["target_selector"] = "CurrentTargetOrNearestEnemy",
                ["cooldown_rounds"] = 0,
                ["fallback_action"] = "BasicAttack",
                ["rule_note"] = "ServiceDesigned fallback rule"
            })
        };

        var definitions = MariaDbMonsterAiRepository.BuildDefinitionsByMonster(rows);

        var definition = Assert.Single(definitions).Value;
        Assert.Equal(27, definition.MonsterTemplateId);
        Assert.Empty(definition.Skills);
        Assert.Equal(MonsterTargetPolicy.LowestHp, definition.TargetPolicy);
        Assert.Equal("ServiceDesignedReplaceablePolicy", definition.EvidenceConfidence);

        var actor = new MonsterAiParticipant(Guid.NewGuid(), true, 38, 38, 40, 0, true, new HashSet<string>());
        var enemy = new MonsterAiParticipant(Guid.NewGuid(), false, 100, 100, 0, 10, true, new HashSet<string>());
        var decision = new DataDrivenMonsterAiPolicy().Decide(new MonsterAiContext(1, definition, actor, [actor, enemy], new Dictionary<int, int>()));

        Assert.Equal(MonsterAiActionType.BasicAttack, decision.Action);
        Assert.Equal(enemy.ParticipantId, decision.TargetId);
    }

    [Fact]
    public void Formal_monster_materializer_reads_mp_magic_and_element_stats_for_blackbox_combat()
    {
        using var monsterReader = SingleRowReader(
            ("Id", typeof(int), 27),
            ("Code", typeof(string), "monster_27"),
            ("Name", typeof(string), "絨毛鴨"),
            ("Level", typeof(int), 1),
            ("MaxHp", typeof(long), 38L),
            ("Attack", typeof(int), 29),
            ("Defense", typeof(int), 3),
            ("MaxMp", typeof(long), 40L),
            ("MagicAttack", typeof(int), 29),
            ("MagicDefense", typeof(int), 4),
            ("Metal", typeof(int), 0),
            ("Wood", typeof(int), 200),
            ("Water", typeof(int), 0),
            ("Fire", typeof(int), 0),
            ("Earth", typeof(int), 0));

        var monster = MariaDbStaticDataLoader.MaterializeMonster(monsterReader);

        Assert.Equal(40, monster.MaxMp);
        Assert.Equal(29, monster.MagicAttack);
        Assert.Equal(4, monster.MagicDefense);
        Assert.Equal(200, monster.Wood);
    }

    [Fact]
    public void MariaDb_world_content_repository_builds_runtime_content_only_from_placed_records()
    {
        var snapshot = new FormalRuntimeStaticSnapshot(
            new Dictionary<int, MapStaticData>
            {
                [10] = new(10, "map-10", "Map Ten", 512, 512),
                [11] = new(11, "map-11", "Map Eleven", 512, 512)
            },
            new Dictionary<long, PortalStaticData>
            {
                [30] = new(30, 10, 11, 12, 11, 13, 14, "Portal")
            },
            new Dictionary<int, NpcStaticData>
            {
                [20] = new(20, "npc-placed", "Placed NPC", 10, 100, 200),
                [21] = new(21, "npc-unplaced", "Unplaced NPC", null, null, null)
            },
            new Dictionary<int, NpcSpawnStaticData>
            {
                [101] = new(
                    101, 20, 10, 100, 200, null, "Always", "god2-opt-6b127086e0c0", 1504,
                    "npc-placed", "Placed NPC", "data2/rom/npc/npc2643.ROM", "Service", "Merchant",
                    "canonical-npc-spawn-identity", "canonical-npc-spawn-coordinate", "canonical-npc-spawn-service", true, "Reports/RoadMap.M2.RealContentSlice.md"),
                [102] = new(
                    102, 20, 10, 110, 210, null, "Always", "god2-opt-6b127086e0c0", 4638,
                    "npc-placed", "Placed NPC", "data2/rom/npc/npc2643.ROM", "Service", "Merchant",
                    "canonical-npc-spawn-identity", "canonical-npc-spawn-coordinate", "canonical-npc-spawn-service", true, "Reports/RoadMap.M2.RealContentSlice.md")
            },
            new Dictionary<int, MonsterStaticData>
            {
                [40] = new(40, "monster-template", "Template", 5, 100, 10, 5, 80, 12, 7, 1, 2, 3, 4, 5)
            },
            new Dictionary<long, SpawnStaticData>
            {
                [60] = new(60, 10, 40, 300, 400, 120, 0, 1, "canonical-monster-spawn", true),
                [61] = new(61, 10, 40, 301, 401, 120)
            },
            new Dictionary<int, ItemStaticData>(),
            new Dictionary<int, SkillStaticData>(),
            new Dictionary<int, QuestStaticData>(),
            new Dictionary<int, MerchantStaticData>
            {
                [50] = new(50, 20, "Merchant"),
                [51] = new(51, 21, "Unmapped Merchant")
            },
            new Dictionary<string, MerchantItemStaticData>(),
            new Dictionary<int, DialogStaticData>(),
            new Dictionary<int, DropTableStaticData>(),
            new Dictionary<string, LocalizationStaticData>(),
            new Dictionary<string, ClientMapIdentityStaticData>(),
            Array.AsReadOnly(Array.Empty<StaticDataLoadCount>()),
            0);

        var content = MariaDbWorldContentRepository.Build(snapshot);

        var map = content.Maps[10];
        Assert.Equal(2, map.NpcPlacements.Count);
        var monsterSpawn = Assert.Single(map.MonsterSpawns);
        Assert.Equal(80, monsterSpawn.MaximumMp);
        Assert.Equal(12, monsterSpawn.MagicAttackPower);
        Assert.Equal(7, monsterSpawn.MagicDefense);
        Assert.Equal((1, 2, 3, 4, 5), (monsterSpawn.Metal, monsterSpawn.Wood, monsterSpawn.Water, monsterSpawn.Fire, monsterSpawn.Earth));
        Assert.All(map.NpcPlacements, placement => Assert.Equal(20, placement.NpcTemplateId));
        Assert.Equal([101, 102], map.NpcPlacements.Select(placement => placement.PlacementId).Order().ToArray());
        Assert.Equal(
            [new WorldPosition3(100, 200), new WorldPosition3(110, 210)],
            map.NpcPlacements.Select(placement => placement.Position).OrderBy(position => position.X).ToArray());
        Assert.All(map.NpcPlacements, placement =>
        {
            Assert.Equal("data2/rom/npc/npc2643.ROM", placement.Appearance);
            Assert.Equal("Merchant", placement.InteractionType);
            Assert.StartsWith("MariaDB:npc_spawns.Id=", placement.Source, StringComparison.Ordinal);
        });
        Assert.Single(map.Portals);
        Assert.Single(map.MerchantMappings);
        Assert.Contains(content.ValidationErrors, error => error.Contains("content.disabled", StringComparison.Ordinal));
        Assert.Contains(content.ValidationErrors, error => error.Contains("content.invalid_merchant_binding", StringComparison.Ordinal));
    }

    [Fact]
    public void MariaDb_world_content_repository_converts_official_resource_grid_dimensions_to_client_coordinate_bounds()
    {
        var snapshot = EmptyRuntimeSnapshot(
            new MapStaticData(
                1675308248,
                "r_acc5885731badfb4d1edda4362c2c99d",
                "崑崙仙界3",
                12,
                12,
                "cityi2/cityi2.mdt",
                "client:map/island03/cityi2/cityi2",
                new string('A', 64),
                "Verified"),
            new ClientMapIdentityStaticData(
                1675308248,
                "god2-opt-6b127086e0c0",
                3,
                4,
                "cityi2/cityi2.mdt",
                1m,
                1m,
                0m,
                0m,
                "Verified",
                "Verified",
                true,
                "Reports/RoadMap.M1.MapIdentityEvidence.md"));

        var content = MariaDbWorldContentRepository.Build(snapshot);
        var map = content.Maps[1675308248];

        Assert.Equal(new MapBounds(0, 0, 251, 251), map.Bounds);
        Assert.Equal("client:map/island03/cityi2/cityi2", map.RegionId);
        Assert.Empty(map.ValidationErrors);
    }

    [Fact]
    public void MariaDb_world_content_repository_fails_closed_when_promoted_identity_does_not_match_payload_resource()
    {
        var snapshot = EmptyRuntimeSnapshot(
            new MapStaticData(10, "map-code", "Map", 12, 12, "official/map.mdt"),
            new ClientMapIdentityStaticData(
                10,
                "god2-opt-6b127086e0c0",
                3,
                4,
                "different/map.mdt",
                1m,
                1m,
                0m,
                0m,
                "Verified",
                "Verified",
                true,
                "fixture"));

        var content = MariaDbWorldContentRepository.Build(snapshot);

        Assert.Contains(
            content.Maps[10].ValidationErrors,
            error => error.Contains("Client map identity resource mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Live_static_data_validation_passes_when_development_database_is_available()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "config", "database.json")));
        var root = document.RootElement;
        var password = root.GetProperty("password").GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var validator = new MariaDbStaticDataValidator(new DatabaseOptions(
            root.GetProperty("host").GetString() ?? "127.0.0.1",
            root.GetProperty("port").GetInt32(),
            root.GetProperty("databaseName").GetString() ?? "god2",
            root.GetProperty("username").GetString() ?? "god2_server",
            password,
            root.GetProperty("connectionTimeoutSeconds").GetInt32()));

        var exception = await Record.ExceptionAsync(() => validator.ValidateAsync(CancellationToken.None));
        Assert.Null(exception);

        var result = await validator.ValidateAsync(CancellationToken.None);
        Assert.True(result.Succeeded, $"{result.Error.Code}:{result.Error.Source}");
    }

    private static DataTableReader SingleRowReader(params (string Name, Type Type, object? Value)[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column.Name, column.Type);
        }

        table.Rows.Add(columns.Select(column => column.Value ?? DBNull.Value).ToArray());
        var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        return reader;
    }

    private static FormalRuntimeStaticSnapshot EmptyRuntimeSnapshot(
        MapStaticData map,
        ClientMapIdentityStaticData identity) => new(
        new Dictionary<int, MapStaticData> { [map.Id] = map },
        new Dictionary<long, PortalStaticData>(),
        new Dictionary<int, NpcStaticData>(),
        new Dictionary<int, NpcSpawnStaticData>(),
        new Dictionary<int, MonsterStaticData>(),
        new Dictionary<long, SpawnStaticData>(),
        new Dictionary<int, ItemStaticData>(),
        new Dictionary<int, SkillStaticData>(),
        new Dictionary<int, QuestStaticData>(),
        new Dictionary<int, MerchantStaticData>(),
        new Dictionary<string, MerchantItemStaticData>(),
        new Dictionary<int, DialogStaticData>(),
        new Dictionary<int, DropTableStaticData>(),
        new Dictionary<string, LocalizationStaticData>(),
        new Dictionary<string, ClientMapIdentityStaticData>
        {
            [$"{map.Id}\u001fgod2-opt-6b127086e0c0"] = identity
        },
        Array.AsReadOnly(Array.Empty<StaticDataLoadCount>()),
        0);

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"god2-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class BlockedGameplayContentAuthority : IProductionGameplayContentAuthority
    {
        public string ActiveReleaseId => string.Empty;

        public bool IsReady => false;

        public OperationResult RequireReady() =>
            OperationResult.Failure("content_release.runtime_not_ready", "No active gameplay content release is ready.");

        public OperationResult<ProductionQuestContent> ResolveQuest(int questId) =>
            OperationResult<ProductionQuestContent>.Failure("content_release.runtime_not_ready", "Not ready.");

        public OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId) =>
            OperationResult<ProductionEquipmentSetContent>.Failure("content_release.runtime_not_ready", "Not ready.");

        public OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId) =>
            OperationResult<ProductionPetInnateContent>.Failure("content_release.runtime_not_ready", "Not ready.");
    }
}
