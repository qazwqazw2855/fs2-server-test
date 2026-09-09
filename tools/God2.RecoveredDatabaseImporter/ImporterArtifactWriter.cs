using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace God2.RecoveredDatabaseImporter;

public static class ImporterArtifactWriter
{
    public static async Task<RecoveryImportResult> WriteAsync(
        string artifactRoot,
        string stagingRoot,
        RecoveryBundleInspection inspection,
        ServerInventory server,
        DatabaseInventory database,
        IReadOnlyList<RecoveryGap> gaps,
        RecoveryIntegrationPlans plans,
        OfflineRecoveryVerification offline,
        string migrationCandidate,
        int stagedFiles,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(Path.Combine(artifactRoot, "bundle-validation.json"), new
        {
            schemaVersion = "god2-recovery-bundle-validation-v1",
            status = "PASS",
            sourceZipPath = Path.GetFileName(inspection.SourceZipPath),
            inspection.SourceZipSizeBytes,
            inspection.SourceZipSha256,
            inspection.ManifestSha256,
            inspection.Manifest,
            inspection.ClientBuild,
            inspection.ContentValidation,
            integrityValidatedBeforeStaging = true,
            productionDatabaseConnectionAttempted = false,
            productionDatabaseMutationAttempted = false
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "server-inventory.json"), server, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "database-inventory.json"), database, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "compiled-server-contracts.json"), offline.CompiledServer, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "schema-migration-verification.json"), offline.MigrationModel, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "semantic-replay-results.json"), offline.Replay, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "offline-verification.json"), offline, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "gap-matrix.json"), new
        {
            schemaVersion = "god2-recovery-gap-matrix-v1",
            packageId = inspection.Manifest.PackageId,
            gaps
        }, cancellationToken);
        await WriteUtf8AtomicAsync(
            Path.Combine(artifactRoot, "staging-migration-candidate.sql"),
            migrationCandidate,
            cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "runtime-change-plan.json"), new
        {
            schemaVersion = "god2-runtime-change-plan-v1",
            status = "CANDIDATE_ONLY",
            productionMutationPerformed = false,
            items = plans.Runtime
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "protocol-change-plan.json"), new
        {
            schemaVersion = "god2-protocol-change-plan-v1",
            status = "CANDIDATE_ONLY",
            fakeNetworkBytes = 0,
            items = plans.Protocol
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "replay-validation-plan.json"), new
        {
            schemaVersion = "god2-replay-validation-plan-v1",
            authority = "CPU_REPLAY_ORACLE_FINAL_AUTHORITY",
            requiredOrder = new[] { "Unit", "Integration", "GoldenReplay", "Headless", "GoldenRegression", "AutomatedOfficialClientLast" },
            counterexampleContract = new[] { "sourceEvidence", "expectedState", "actualState", "responsibleSubsystem", "firstDivergencePath" },
            items = plans.Replay
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(artifactRoot, "integration-contract.json"), new
        {
            schemaVersion = "god2-server-db-integration-contract-v1",
            importerMode = "READ_ONLY_DRY_RUN",
            captureExecutableMayMutateProductionDatabase = false,
            importerMayMutateProductionDatabase = false,
            productionDatabaseConnectionAttempted = false,
            productionDatabaseMutationAttempted = false,
            stagingDirectory = Path.GetRelativePath(artifactRoot, stagingRoot).Replace('\\', '/'),
            clientBuild = inspection.ClientBuild,
            authorityPromotion = new[] { "VERIFIED", "DERIVED" },
            unknownDropPolicy = new
            {
                authoritativeRate = (decimal?)null,
                defaultDisabledRate = 0m,
                enabled = false,
                authority = "UNKNOWN_SERVER_ONLY"
            },
            offlineVerificationStatus = offline.Status,
            compiledSourceComparison = offline.CompiledServer.Mode,
            schemaMigrationComparison = offline.MigrationModel.Mode,
            semanticReplayComparison = offline.Replay.Mode
        }, cancellationToken);

        var ready = gaps.Count(value => value.OverallStatus == "OFFLINE_CONTRACT_AND_REPLAY_VERIFIED");
        var blocked = gaps.Count - ready;
        var resultManifestPath = Path.Combine(artifactRoot, "result-manifest.json");
        var result = new RecoveryImportResult(
            RecoveryBundleContract.ImportResultSchemaVersion,
            "RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS",
            inspection.Manifest.PackageId,
            inspection.SourceZipSha256,
            artifactRoot,
            stagingRoot,
            stagedFiles,
            gaps.Count,
            ready,
            blocked,
            ProductionDatabaseConnectionAttempted: false,
            ProductionDatabaseMutationAttempted: false,
            resultManifestPath,
            DateTimeOffset.UtcNow);
        await WriteJsonAsync(Path.Combine(artifactRoot, "importer-summary.json"), new
        {
            result.SchemaVersion,
            result.Status,
            result.PackageId,
            result.SourceZipSha256,
            artifactRoot = ".",
            stagingRoot = Path.GetRelativePath(artifactRoot, stagingRoot).Replace('\\', '/'),
            result.StagedFileCount,
            result.GapCount,
            result.ReadyDomainCount,
            result.BlockedDomainCount,
            result.ProductionDatabaseConnectionAttempted,
            result.ProductionDatabaseMutationAttempted,
            resultManifestPath = Path.GetRelativePath(artifactRoot, resultManifestPath).Replace('\\', '/'),
            result.CompletedAtUtc
        }, cancellationToken);
        await WriteUtf8AtomicAsync(
            Path.Combine(artifactRoot, "GOD2-RECOVERY-BUNDLE-IMPORTER-REPORT.md"),
            BuildReport(result, inspection, database, gaps, offline),
            cancellationToken);

        var entries = await BuildResultEntriesAsync(artifactRoot, resultManifestPath, cancellationToken);
        var pendingManifestPath = string.Concat(resultManifestPath, ".pending-", Guid.NewGuid().ToString("N"));
        try
        {
            await WriteJsonAsync(pendingManifestPath, new
            {
                schemaVersion = "god2-recovery-import-result-manifest-v1",
                status = "PASS",
                algorithm = "SHA-256",
                manifestSelfHashIncluded = false,
                entries
            }, cancellationToken);
            await VerifyResultManifestAsync(artifactRoot, pendingManifestPath, entries, cancellationToken);
            File.Move(pendingManifestPath, resultManifestPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(pendingManifestPath))
            {
                File.Delete(pendingManifestPath);
            }
        }
        return result;
    }

    private static string BuildReport(
        RecoveryImportResult result,
        RecoveryBundleInspection inspection,
        DatabaseInventory database,
        IReadOnlyList<RecoveryGap> gaps,
        OfflineRecoveryVerification offline)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# God2 Recovery Bundle Importer Report");
        builder.AppendLine();
        builder.AppendLine($"Status: `{result.Status}`");
        builder.AppendLine();
        builder.AppendLine($"- Package: `{result.PackageId}`");
        builder.AppendLine($"- ZIP SHA-256: `{result.SourceZipSha256}`");
        builder.AppendLine($"- Client: `{inspection.ClientBuild.Executable}` / `{inspection.ClientBuild.Architecture}` / `{inspection.ClientBuild.FileVersion}` / `{inspection.ClientBuild.Sha256}`");
        builder.AppendLine($"- Manifest files staged: `{result.StagedFileCount}`");
        builder.AppendLine($"- SQL migration head inspected: `{database.MigrationHead}` / `{database.MigrationHeadSha256}`");
        builder.AppendLine("- Production DB connections: `0`");
        builder.AppendLine("- Production DB mutations: `0`");
        builder.AppendLine("- Fake network bytes: `0`");
        builder.AppendLine($"- Compiled contracts verified: `{offline.CompiledServer.Contracts.Count(value => value.Status == "COMPILED_CONTRACT_VERIFIED")}/{offline.CompiledServer.Contracts.Count}`");
        builder.AppendLine($"- Migration model: additive=`{offline.MigrationModel.CandidateAdditiveOnly}`, applied=`{offline.MigrationModel.CandidateAppliedToModel}`, second-apply-no-op=`{offline.MigrationModel.CandidateSecondApplyNoOp}`");
        builder.AppendLine($"- Semantic replay: `{offline.Replay.EquivalentCount}` equivalent / `{offline.Replay.CounterexampleCount}` counterexamples / `{offline.Replay.FixtureStatus}`");
        builder.AppendLine();
        builder.AppendLine("| Domain | Bundle | Server | Database | Result |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var gap in gaps)
        {
            builder.AppendLine($"| {Escape(gap.Domain)} | {Escape(gap.BundleStatus)} | {Escape(gap.ServerStatus)} | {Escape(gap.DatabaseStatus)} | {Escape(gap.OverallStatus)} |");
        }
        builder.AppendLine();
        builder.AppendLine("This is a validated staging and change-plan result. It does not claim SERVER RESTORED, DATABASE RESTORED, or closed-loop replay PASS. Domains without authoritative bundle evidence remain `UNKNOWN_SERVER_ONLY` / `EVIDENCE_BLOCKED`.");
        return builder.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static async Task<object[]> BuildResultEntriesAsync(
        string root,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var entries = new List<object>(files.Length);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new
            {
                relativePath = Path.GetRelativePath(root, path).Replace('\\', '/'),
                sizeBytes = new FileInfo(path).Length,
                sha256 = await HashFileAsync(path, cancellationToken)
            });
        }
        return entries.ToArray();
    }

    private static async Task VerifyResultManifestAsync(
        string root,
        string manifestPath,
        IReadOnlyList<object> entries,
        CancellationToken cancellationToken)
    {
        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        var manifestEntries = manifestDocument.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        if (manifestEntries.Length != entries.Count)
        {
            throw new RecoveryImportException("result_manifest.count_mismatch", "Result manifest entry count changed during verification.");
        }
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifestEntries)
        {
            var relativePath = entry.GetProperty("relativePath").GetString() ?? string.Empty;
            if (!declared.Add(relativePath))
            {
                throw new RecoveryImportException("result_manifest.duplicate", $"Duplicate result entry: {relativePath}");
            }
            var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                throw new RecoveryImportException("result_manifest.path_invalid", $"Result manifest path is missing or unsafe: {relativePath}");
            }
            if (new FileInfo(path).Length != entry.GetProperty("sizeBytes").GetInt64() ||
                !string.Equals(await HashFileAsync(path, cancellationToken), entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new RecoveryImportException("result_manifest.integrity_mismatch", $"Result artifact integrity mismatch: {relativePath}");
            }
        }
        var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(declared))
        {
            throw new RecoveryImportException("result_manifest.coverage_mismatch", "Result manifest does not cover every generated artifact.");
        }
    }

    private static Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
        WriteUtf8AtomicAsync(path, JsonSerializer.Serialize(value, RecoveryJson.Options) + Environment.NewLine, cancellationToken);

    private static async Task WriteUtf8AtomicAsync(string path, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = string.Concat(path, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false, true), cancellationToken);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
