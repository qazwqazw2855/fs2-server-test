using System.Text;
using System.Text.RegularExpressions;
using God2.ClassicServer.Persistence;

namespace God2.RecoveredDatabaseImporter;

public static partial class WorkspaceInventoryScanner
{
    public static ServerInventory ScanServer(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var solutionPath = Path.Combine(root, "God2ClassicServer.sln");
        if (!File.Exists(solutionPath))
        {
            throw new RecoveryImportException("server.solution_missing", $"God2ClassicServer.sln is missing: {solutionPath}");
        }

        var projects = File.ReadLines(solutionPath)
            .Select(line => SolutionProjectPattern().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceRoot = Path.Combine(root, "src");
        var sourceFiles = Directory.Exists(sourceRoot)
            ? Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !ContainsBuildOutputSegment(Path.GetRelativePath(sourceRoot, path)))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        var compiled = CompiledServerContractScanner.Scan(projects);
        var capabilities = compiled.Contracts.Select(contract => new ServerCapability(
            contract.Domain,
            contract.Status == "COMPILED_CONTRACT_VERIFIED",
            contract.VerifiedSymbols)).ToArray();

        return new ServerInventory(
            Path.GetRelativePath(root, solutionPath).Replace('\\', '/'),
            projects.Length,
            sourceFiles.Length,
            projects,
            capabilities);
    }

    public static DatabaseInventory ScanDatabase(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var schemaRoot = Path.Combine(root, "database", "schema");
        var migrations = SqlMigrationFile.Discover(schemaRoot);
        if (migrations.Count == 0)
        {
            throw new RecoveryImportException("database.migrations_missing", $"No SQL migrations were found: {schemaRoot}");
        }

        var model = new OfflineSqlSchemaModel();
        foreach (var migration in migrations)
        {
            model.Apply(File.ReadAllText(migration.Path, Encoding.UTF8));
        }

        var head = migrations[^1];
        var headSql = File.ReadAllText(head.Path, Encoding.UTF8);
        return new DatabaseInventory(
            "OFFLINE_DETERMINISTIC_MIGRATION_MODEL_READ_ONLY",
            ProductionConnectionAttempted: false,
            ProductionMutationAttempted: false,
            migrations.Count,
            head.Name,
            SqlMigrationFile.ComputeChecksum(headSql),
            model.Columns.Keys.Order(StringComparer.Ordinal).ToArray(),
            model.Columns);
    }

    [GeneratedRegex("^Project\\(.*?\\) = \\\".*?\\\", \\\"(?<path>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex SolutionProjectPattern();

    private static bool ContainsBuildOutputSegment(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

}

public static class RecoveryGapMatrixBuilder
{
    private sealed record DomainSpecification(
        string Domain,
        string[] BundleTerms,
        string ServerDomain,
        string[] DatabaseTables,
        string ImplementationTarget);

    private static readonly DomainSpecification[] Specifications =
    [
        new("Protocol", ["protocol/"], "Protocol", [], "src/God2.ClassicServer.Protocol"),
        new("Monster", ["canonical/monsters", "canonical/monster-"], "Monster", ["monsters"], "Runtime monster catalog"),
        new("Npc", ["canonical/npcs", "canonical/npc-"], "Npc", ["npcs"], "Runtime NPC catalog"),
        new("Map", ["canonical/maps", "canonical/map-"], "Map", ["maps"], "Runtime map catalog"),
        new("Portal", ["canonical/portals"], "Portal", ["portals"], "Portal state machine"),
        new("Quest", ["canonical/quests", "canonical/quest-"], "Quest", ["quests"], "Quest runtime"),
        new("Item", ["canonical/items", "canonical/equipment"], "Item", ["items"], "Item and inventory runtime"),
        new("Skill", ["canonical/skills"], "Skill", ["skills"], "Skill runtime"),
        new("Drop", ["canonical/monster-drops", "canonical/drop"], "Drop", ["drop_tables"], "Drop authority runtime"),
        new("Formula", ["runtime/formulas"], "Formula", [], "Formula registry"),
        new("StateMachine", ["runtime/runtime-state-machines", "protocol/protocol-state-machines"], "StateMachine", [], "Runtime state-machine registry"),
        new("ObjectResolver", ["runtime/object-layouts"], "ObjectResolver", [], "Runtime object identity registry"),
        new("Mutation", ["runtime/state-mutations"], "Mutation", ["runtime_state"], "Authoritative mutation pipeline")
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> RequiredDatabaseColumns =
        new Dictionary<string, IReadOnlyDictionary<string, string[]>>(StringComparer.Ordinal)
        {
            ["Monster"] = new Dictionary<string, string[]> { ["monsters"] = ["Id", "Code", "Name", "Level", "MaxHp"] },
            ["Npc"] = new Dictionary<string, string[]> { ["npcs"] = ["Id", "Code", "Name", "MapId"] },
            ["Map"] = new Dictionary<string, string[]> { ["maps"] = ["Id", "Code", "Name", "Width", "Height"] },
            ["Portal"] = new Dictionary<string, string[]> { ["portals"] = ["Id", "SourceMapId", "TargetMapId"] },
            ["Quest"] = new Dictionary<string, string[]> { ["quests"] = ["Id", "Code", "Name", "RequiredLevel"] },
            ["Item"] = new Dictionary<string, string[]> { ["items"] = ["Id", "Code", "Name", "ItemType", "MaxStack"] },
            ["Skill"] = new Dictionary<string, string[]> { ["skills"] = ["Id", "Code", "Name", "MaxLevel"] },
            ["Drop"] = new Dictionary<string, string[]> { ["drop_tables"] = ["Id", "MonsterId", "Name"] },
            ["Mutation"] = new Dictionary<string, string[]> { ["runtime_state"] = ["Id"] }
        };

    public static IReadOnlyList<RecoveryGap> Build(
        RecoveryBundleInspection bundle,
        ServerInventory server,
        DatabaseInventory database,
        OfflineRecoveryVerification? offline = null)
    {
        var serverCapabilities = server.Capabilities.ToDictionary(value => value.Domain, StringComparer.Ordinal);
        var databaseTables = database.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var compiledContracts = offline?.CompiledServer.Contracts.ToDictionary(value => value.Domain, StringComparer.Ordinal);
        var replayDomains = offline?.Replay.Cases
            .Where(value => value.Status == "SEMANTICALLY_EQUIVALENT")
            .Select(value => value.Domain)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return Specifications.Select(specification =>
        {
            var matchingEvidence = bundle.Manifest.Files
                .Where(value => specification.BundleTerms.Any(term => value.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(value => value.RelativePath, StringComparer.Ordinal)
                .ToArray();
            var bundleEvidence = matchingEvidence
                .Select(value => $"{value.RelativePath} [{value.Authority}]")
                .ToArray();
            var bundlePresent = matchingEvidence.Any(value => RecoveryBundleContract.CanPromote(value.Authority));
            var capability = serverCapabilities.GetValueOrDefault(specification.ServerDomain);
            var serverPresent = capability?.Present == true;
            var requiredColumns = RequiredDatabaseColumns.GetValueOrDefault(specification.Domain);
            var databasePresent = specification.DatabaseTables.Length == 0 ||
                                  (specification.DatabaseTables.All(databaseTables.Contains) &&
                                   (requiredColumns is null || requiredColumns.All(requirement =>
                                       database.Columns.TryGetValue(requirement.Key, out var columns) &&
                                       requirement.Value.All(column => columns.Contains(column, StringComparer.OrdinalIgnoreCase)))));
            var compiledPresent = compiledContracts?.GetValueOrDefault(specification.ServerDomain)?.Status == "COMPILED_CONTRACT_VERIFIED";
            var migrationVerified = offline?.MigrationModel is
            {
                CandidateAdditiveOnly: true,
                CandidateAppliedToModel: true,
                CandidateSecondApplyNoOp: true
            };
            var replayVerified = replayDomains.Contains(specification.Domain);
            var offlineReady = bundlePresent && serverPresent && compiledPresent && databasePresent && migrationVerified && replayVerified;
            var overall = offlineReady ? "OFFLINE_CONTRACT_AND_REPLAY_VERIFIED" : "EVIDENCE_BLOCKED";
            var action = matchingEvidence.Length == 0
                ? $"Acquire authoritative {specification.Domain} evidence; keep server values UNKNOWN_SERVER_ONLY."
                : !bundlePresent
                    ? $"Evidence for {specification.Domain} is non-promotable; preserve its current authority and keep production integration disabled."
                : !serverPresent
                        ? $"Implement {specification.ImplementationTarget} from validated bundle contracts."
                    : !databasePresent
                            ? $"Generate an additive human-readable staging migration for {specification.Domain}; do not mutate production data."
                        : !migrationVerified
                            ? $"Validate the additive {specification.Domain} migration by deterministic model application and idempotence replay."
                        : !replayVerified
                            ? $"Add a validated deterministic {specification.Domain} replay fixture and prove semantic equivalence."
                            : $"Offline compiled contract, schema migration, and semantic replay are verified for {specification.Domain}; production integration remains disabled.";

            return new RecoveryGap(
                specification.Domain,
                bundlePresent ? "PRESENT_VALIDATED" : matchingEvidence.Length == 0 ? "MISSING_EVIDENCE" : "NON_PROMOTABLE_EVIDENCE_ONLY",
                serverPresent ? "COMPILED_CONTRACT_VERIFIED" : "CHANGE_REQUIRED",
                specification.DatabaseTables.Length == 0 ? "NOT_APPLICABLE" : databasePresent ? "OFFLINE_MIGRATION_MODEL_VERIFIED" : "MIGRATION_REQUIRED",
                overall,
                bundleEvidence,
                capability?.EvidenceFiles ?? [],
                requiredColumns is null
                    ? specification.DatabaseTables.Where(databaseTables.Contains).ToArray()
                    : requiredColumns.Where(value => database.Columns.ContainsKey(value.Key))
                        .Select(value => $"{value.Key}({string.Join(',', value.Value)})")
                        .ToArray(),
                action);
        }).ToArray();
    }

    public static RecoveryIntegrationPlans BuildPlans(IReadOnlyList<RecoveryGap> gaps)
    {
        var runtime = gaps.Where(value => value.Domain != "Protocol")
            .Select(value => Plan(value, value.Domain switch
            {
                "Formula" => "src/God2.ClassicServer.Runtime formula registry",
                "StateMachine" => "src/God2.ClassicServer.Runtime state machines",
                "Mutation" => "src/God2.ClassicServer.Runtime mutation pipeline",
                _ => $"src/God2.ClassicServer.Runtime {value.Domain} integration"
            }))
            .ToArray();
        var protocol = gaps.Where(value => value.Domain is "Protocol" or "StateMachine")
            .Select(value => Plan(value, "src/God2.ClassicServer.Protocol parser/serializer contracts"))
            .ToArray();
        var replay = gaps.Where(value => value.BundleStatus == "PRESENT_VALIDATED")
            .Select(value => new ChangePlanItem(
                value.Domain,
                "PLANNED",
                "Golden replay and semantic oracle",
                $"Replay validated {value.Domain} evidence against expected semantic state and emit a counterexample on first divergence.",
                "Expected state, actual state, source evidence, responsible subsystem, and first divergence path are mandatory."))
            .ToArray();
        return new RecoveryIntegrationPlans(runtime, protocol, replay);
    }

    private static ChangePlanItem Plan(RecoveryGap gap, string target) => new(
        gap.Domain,
        gap.OverallStatus,
        target,
        gap.RequiredAction,
        gap.OverallStatus == "EVIDENCE_BLOCKED"
            ? "No production promotion; preserve UNKNOWN_SERVER_ONLY."
            : "Build, unit, integration, headless, golden replay, and semantic oracle must pass before promotion.");
}

public static class StagingMigrationCandidateGenerator
{
    public static string Generate(string packageId) => $$"""
        -- God2 Recovery Bundle isolated staging migration candidate
        -- Package: {{SanitizeComment(packageId)}}
        -- This file is generated only; the importer never opens or mutates a production database.

        CREATE TABLE IF NOT EXISTS `recovery_bundle_import_runs` (
            `import_run_id` char(64) NOT NULL,
            `package_id` varchar(128) NOT NULL,
            `package_sha256` char(64) NOT NULL,
            `client_build_sha256` char(64) NOT NULL,
            `validation_status` varchar(64) NOT NULL,
            `created_at_utc` datetime(6) NOT NULL,
            PRIMARY KEY (`import_run_id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS `recovery_bundle_staging_records` (
            `import_run_id` char(64) NOT NULL,
            `record_identity` varchar(255) NOT NULL,
            `gameplay_domain` varchar(64) NOT NULL,
            `schema_version` varchar(96) NOT NULL,
            `authority` varchar(32) NOT NULL,
            `source_relative_path` varchar(512) NOT NULL,
            `source_sha256` char(64) NOT NULL,
            `provenance_json` json NOT NULL,
            `normalized_content_json` json NOT NULL,
            `authoritative_rate` decimal(18,9) NULL,
            `default_disabled_rate` decimal(18,9) NOT NULL DEFAULT 0,
            `enabled` tinyint(1) NOT NULL DEFAULT 0,
            PRIMARY KEY (`import_run_id`, `gameplay_domain`, `record_identity`),
            CONSTRAINT `fk_recovery_staging_import_run`
                FOREIGN KEY (`import_run_id`) REFERENCES `recovery_bundle_import_runs` (`import_run_id`) ON DELETE CASCADE,
            CONSTRAINT `ck_recovery_staging_authority`
                CHECK (`authority` IN ('VERIFIED','DERIVED','OBSERVED','HYPOTHESIS','UNKNOWN','UNKNOWN_SERVER_ONLY','REJECTED')),
            CONSTRAINT `ck_recovery_staging_unknown_rate`
                CHECK (`authority` <> 'UNKNOWN_SERVER_ONLY' OR (`authoritative_rate` IS NULL AND `default_disabled_rate` = 0 AND `enabled` = 0))
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS `recovery_bundle_gap_matrix` (
            `import_run_id` char(64) NOT NULL,
            `gameplay_domain` varchar(64) NOT NULL,
            `bundle_status` varchar(64) NOT NULL,
            `server_status` varchar(64) NOT NULL,
            `database_status` varchar(64) NOT NULL,
            `required_action` varchar(1024) NOT NULL,
            PRIMARY KEY (`import_run_id`, `gameplay_domain`),
            CONSTRAINT `fk_recovery_gap_import_run`
                FOREIGN KEY (`import_run_id`) REFERENCES `recovery_bundle_import_runs` (`import_run_id`) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        """;

    private static string SanitizeComment(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
