using System.Security.Cryptography;
using System.Text.Json;
using God2.RecoveredDatabaseImporter;

namespace God2.RecoveredDatabaseImporter.Tests;

public sealed class RecoveryImporterTests
{
    [Fact]
    public async Task ImportAsync_ValidBundle_WritesVerifiedReadOnlyArtifacts()
    {
        using var fixture = new RecoveryBundleFixture();
        var importer = new God2RecoveryBundleImporter();

        var result = await importer.ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        Assert.Equal(RecoveryBundleContract.ImportResultSchemaVersion, result.SchemaVersion);
        Assert.Equal("RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS", result.Status);
        Assert.Equal(13, result.StagedFileCount);
        Assert.False(result.ProductionDatabaseConnectionAttempted);
        Assert.False(result.ProductionDatabaseMutationAttempted);
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "bundle-validation.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "gap-matrix.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "staging-migration-candidate.sql")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "runtime-change-plan.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "protocol-change-plan.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "replay-validation-plan.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "integration-contract.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "importer-summary.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "GOD2-RECOVERY-BUNDLE-IMPORTER-REPORT.md")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "compiled-server-contracts.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "schema-migration-verification.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "semantic-replay-results.json")));
        Assert.True(File.Exists(Path.Combine(result.ArtifactRoot, "offline-verification.json")));
        Assert.True(File.Exists(result.ResultManifestPath));
    }

    [Fact]
    public async Task ImportAsync_ResultManifest_CoversAndHashesEveryGeneratedArtifact()
    {
        using var fixture = new RecoveryBundleFixture();
        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.ResultManifestPath, CancellationToken.None));
        Assert.Equal("PASS", document.RootElement.GetProperty("status").GetString());
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var actual = Directory.EnumerateFiles(result.ArtifactRoot, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(result.ResultManifestPath), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(actual.Length, entries.Length);

        foreach (var entry in entries)
        {
            var relativePath = entry.GetProperty("relativePath").GetString()!;
            var path = Path.Combine(result.ArtifactRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), relativePath);
            Assert.Equal(new FileInfo(path).Length, entry.GetProperty("sizeBytes").GetInt64());
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, CancellationToken.None)).ToLowerInvariant();
            Assert.Equal(entry.GetProperty("sha256").GetString(), hash);
        }
    }

    [Fact]
    public async Task ImportAsync_IntegrationContract_ProvesZeroProductionDatabaseAndNetworkMutation()
    {
        using var fixture = new RecoveryBundleFixture();
        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        using var contract = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "integration-contract.json"),
            CancellationToken.None));
        Assert.Equal("READ_ONLY_DRY_RUN", contract.RootElement.GetProperty("importerMode").GetString());
        Assert.False(contract.RootElement.GetProperty("captureExecutableMayMutateProductionDatabase").GetBoolean());
        Assert.False(contract.RootElement.GetProperty("importerMayMutateProductionDatabase").GetBoolean());
        Assert.False(contract.RootElement.GetProperty("productionDatabaseConnectionAttempted").GetBoolean());
        Assert.False(contract.RootElement.GetProperty("productionDatabaseMutationAttempted").GetBoolean());

        using var protocol = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "protocol-change-plan.json"),
            CancellationToken.None));
        Assert.Equal(0, protocol.RootElement.GetProperty("fakeNetworkBytes").GetInt32());
    }

    [Fact]
    public async Task ImportAsync_OfflineCompiledSchemaMigrationAndReplayProofs_AreExecuted()
    {
        using var fixture = new RecoveryBundleFixture();
        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        using var compiled = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "compiled-server-contracts.json"), CancellationToken.None));
        Assert.Equal("LOADED_COMPILED_ASSEMBLY_REFLECTION_READ_ONLY", compiled.RootElement.GetProperty("mode").GetString());
        var contracts = compiled.RootElement.GetProperty("contracts").EnumerateArray().ToArray();
        Assert.Equal(13, contracts.Length);
        Assert.All(contracts, contract => Assert.Equal("COMPILED_CONTRACT_VERIFIED", contract.GetProperty("status").GetString()));

        using var migration = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "schema-migration-verification.json"), CancellationToken.None));
        var expectedMigrationCount = Directory.EnumerateFiles(
            Path.Combine(fixture.RepositoryRoot, "database", "schema"),
            "*.sql",
            SearchOption.TopDirectoryOnly).Count();
        Assert.Equal(expectedMigrationCount, migration.RootElement.GetProperty("parsedMigrationCount").GetInt32());
        Assert.True(migration.RootElement.GetProperty("candidateAdditiveOnly").GetBoolean());
        Assert.True(
            migration.RootElement.GetProperty("candidateAppliedToModel").GetBoolean(),
            string.Join("; ", migration.RootElement.GetProperty("findings").EnumerateArray().Select(value => value.GetString())));
        Assert.True(migration.RootElement.GetProperty("candidateSecondApplyNoOp").GetBoolean());
        Assert.False(migration.RootElement.GetProperty("productionConnectionAttempted").GetBoolean());
        Assert.False(migration.RootElement.GetProperty("productionMutationAttempted").GetBoolean());
        Assert.Empty(migration.RootElement.GetProperty("findings").EnumerateArray());

        using var replay = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "semantic-replay-results.json"), CancellationToken.None));
        Assert.Equal("REPLAY_FIXTURE_EXECUTED", replay.RootElement.GetProperty("fixtureStatus").GetString());
        Assert.Equal(1, replay.RootElement.GetProperty("equivalentCount").GetInt32());
        Assert.Equal(0, replay.RootElement.GetProperty("counterexampleCount").GetInt32());
        var replayCase = replay.RootElement.GetProperty("cases")[0];
        Assert.Matches("^[0-9a-f]{64}$", replayCase.GetProperty("plaintextPacketHash").GetString());
        Assert.Matches("^[0-9a-f]{64}$", replayCase.GetProperty("semanticEventHash").GetString());
    }

    [Fact]
    public async Task ImportAsync_ReplayMismatch_EmitsCounterexampleAndDoesNotPromoteDomain()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("replay/semantic-replay/cases.jsonl",
        [
            new
            {
                schemaVersion = "god2-semantic-replay-case-v1",
                authority = "DERIVED",
                caseId = "protocol-counterexample",
                domain = "Protocol",
                initialState = new { session = new { sequence = 7 } },
                mutations = new[] { new { operation = "Increment", path = "session.sequence", value = 1 } },
                expectedState = new { session = new { sequence = 9 } }
            }
        ], "DERIVED", "god2-semantic-replay-case-v1");

        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);
        using var replay = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "semantic-replay-results.json"), CancellationToken.None));
        Assert.Equal(1, replay.RootElement.GetProperty("counterexampleCount").GetInt32());
        var counterexample = replay.RootElement.GetProperty("cases")[0].GetProperty("oracle").GetProperty("counterexample");
        Assert.Equal("$.session.sequence", counterexample.GetProperty("firstDivergencePath").GetString());
        Assert.Equal("9", counterexample.GetProperty("expectedState").GetString());
        Assert.Equal("8", counterexample.GetProperty("actualState").GetString());
        Assert.Equal(0, result.ReadyDomainCount);
    }

    [Fact]
    public async Task ImportAsync_MissingReplayFixture_RemainsEvidenceBlocked()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.Remove("replay/semantic-replay/cases.jsonl");

        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        using var offline = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "offline-verification.json"), CancellationToken.None));
        Assert.Equal("EVIDENCE_BLOCKED", offline.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "EVIDENCE_BLOCKED_REPLAY_FIXTURE_UNAVAILABLE",
            offline.RootElement.GetProperty("replay").GetProperty("fixtureStatus").GetString());
        Assert.Equal(0, result.ReadyDomainCount);
        Assert.Equal(result.GapCount, result.BlockedDomainCount);
    }

    [Fact]
    public void OfflineMigrationVerifier_DestructiveCandidate_IsBlockedWithoutDatabaseAccess()
    {
        using var fixture = new RecoveryBundleFixture();
        var candidate = StagingMigrationCandidateGenerator.Generate("fixture-package") + "\nDELETE FROM `accounts`;";

        var result = OfflineMigrationModelVerifier.Verify(fixture.RepositoryRoot, candidate);

        Assert.False(result.CandidateAdditiveOnly);
        Assert.False(result.ProductionConnectionAttempted);
        Assert.False(result.ProductionMutationAttempted);
        Assert.Contains(result.Findings, value => value.Contains("forbidden", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_StagingMigrationCandidate_IsAdditiveAndAuthoritySafe()
    {
        using var fixture = new RecoveryBundleFixture();
        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        var sql = await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "staging-migration-candidate.sql"),
            CancellationToken.None);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `recovery_bundle_staging_records`", sql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (`import_run_id`)", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`import_run_id`, `gameplay_domain`, `record_identity`)", sql, StringComparison.Ordinal);
        Assert.Contains("`authority` <> 'UNKNOWN_SERVER_ONLY'", sql, StringComparison.Ordinal);
        Assert.Contains("`authoritative_rate` IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_InvalidBundle_DoesNotCreateArtifactOrStagingDirectory()
    {
        using var fixture = new RecoveryBundleFixture { ManifestHashOverridePath = "canonical/items.jsonl" };
        var importer = new God2RecoveryBundleImporter();

        var exception = await Assert.ThrowsAsync<RecoveryImportException>(() => importer.ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None));

        Assert.Equal("manifest.file_hash_mismatch", exception.Code);
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public async Task ImportAsync_GapMatrixAndPlans_AreMachineReadableAndEvidenceGated()
    {
        using var fixture = new RecoveryBundleFixture();
        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, fixture.CreateArchive(), fixture.OutputRoot),
            CancellationToken.None);

        using var gaps = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "gap-matrix.json"),
            CancellationToken.None));
        var rows = gaps.RootElement.GetProperty("gaps").EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("domain").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("overallStatus").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("requiredAction").GetString()));
            Assert.Contains(row.GetProperty("overallStatus").GetString(), new[]
            {
                "EVIDENCE_BLOCKED",
                "OFFLINE_CONTRACT_AND_REPLAY_VERIFIED"
            });
        });
        Assert.Equal(1, result.ReadyDomainCount);
        Assert.Equal(result.GapCount - 1, result.BlockedDomainCount);
        var protocolRow = Assert.Single(rows, row => row.GetProperty("domain").GetString() == "Protocol");
        Assert.Equal("OFFLINE_CONTRACT_AND_REPLAY_VERIFIED", protocolRow.GetProperty("overallStatus").GetString());
        Assert.DoesNotContain(rows, row => row.GetProperty("overallStatus").GetString() == "CANDIDATE_READY_FOR_REPLAY");
        var drop = Assert.Single(rows, row => row.GetProperty("domain").GetString() == "Drop");
        Assert.Equal("NON_PROMOTABLE_EVIDENCE_ONLY", drop.GetProperty("bundleStatus").GetString());
        Assert.Equal("EVIDENCE_BLOCKED", drop.GetProperty("overallStatus").GetString());

        using var replay = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "replay-validation-plan.json"),
            CancellationToken.None));
        Assert.Equal("CPU_REPLAY_ORACLE_FINAL_AUTHORITY", replay.RootElement.GetProperty("authority").GetString());
        Assert.Contains("firstDivergencePath", replay.RootElement.GetProperty("counterexampleContract").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task ImportAsync_PersistedArtifacts_UseOnlyRelativeOrRedactedPaths()
    {
        using var fixture = new RecoveryBundleFixture();
        var bundlePath = fixture.CreateArchive();
        var result = await new God2RecoveryBundleImporter().ImportAsync(
            new RecoveryImportOptions(fixture.RepositoryRoot, bundlePath, fixture.OutputRoot),
            CancellationToken.None);

        using var validation = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "bundle-validation.json"),
            CancellationToken.None));
        var persistedSourcePath = validation.RootElement.GetProperty("sourceZipPath").GetString()!;
        Assert.Equal(Path.GetFileName(bundlePath), persistedSourcePath);
        Assert.False(Path.IsPathRooted(persistedSourcePath));

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.ArtifactRoot, "importer-summary.json"),
            CancellationToken.None));
        Assert.Equal(".", summary.RootElement.GetProperty("artifactRoot").GetString());
        Assert.Equal("staging", summary.RootElement.GetProperty("stagingRoot").GetString());
        Assert.Equal("result-manifest.json", summary.RootElement.GetProperty("resultManifestPath").GetString());
        Assert.False(Path.IsPathRooted(summary.RootElement.GetProperty("stagingRoot").GetString()!));
        Assert.False(Path.IsPathRooted(summary.RootElement.GetProperty("resultManifestPath").GetString()!));
    }

    [Fact]
    public void ScanServer_PathNameAlone_DoesNotClaimCapability()
    {
        var root = Path.Combine(Path.GetTempPath(), "God2ImporterInventoryTests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "src", "Fake");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "God2ClassicServer.sln"),
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Fake\", \"src\\Fake\\Fake.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n");
            File.WriteAllText(
                Path.Combine(sourceRoot, "MonsterCombatRuntime.cs"),
                "namespace Fake; public sealed class OrdinaryComponent { }");

            var inventory = WorkspaceInventoryScanner.ScanServer(root);

            Assert.False(Assert.Single(inventory.Capabilities, value => value.Domain == "Monster").Present);
            Assert.False(Assert.Single(inventory.Capabilities, value => value.Domain == "StateMachine").Present);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UltimatePackager_ResolvesPortableImporterPathsAgainstCandidateRunRoot()
    {
        using var fixture = new RecoveryBundleFixture();
        var script = File.ReadAllText(Path.Combine(
            fixture.RepositoryRoot,
            "tools",
            "God2.PacketCapture",
            "ultimate",
            "Build-UltimateRelease.ps1"));

        Assert.Contains("function Resolve-ImporterRunRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("if ($value -eq '.')", script, StringComparison.Ordinal);
        Assert.Contains("Assert-NoReparsePoints $candidate.Directory", script, StringComparison.Ordinal);
        Assert.Contains("Assert-SafeRelativePath $value", script, StringComparison.Ordinal);
        Assert.Contains("if (-not (Test-IsWithin $root $resolved))", script, StringComparison.Ordinal);
        Assert.Contains("if ($artifactRootValue -ne '.')", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-ImporterRunRelativePath $candidate.Directory $artifactRootValue -AllowRunRoot", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-ImporterRunRelativePath $candidate.Directory $stagingRootValue", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-ImporterRunRelativePath $candidate.Directory $summaryManifestValue", script, StringComparison.Ordinal);
        Assert.Contains("$packageId -eq $validatedRecoveryBundle.PackageId", script, StringComparison.Ordinal);
        Assert.Contains("$sourceZipSha256 -ieq $validatedRecoveryBundle.SourceZipSHA256", script, StringComparison.Ordinal);
        Assert.Contains("$bundleValidationManifestSha256 -ieq $validatedRecoveryBundle.ManifestSHA256", script, StringComparison.Ordinal);
        Assert.Contains("$bundleValidationSourcePath -eq $nativeZipInfo.Name", script, StringComparison.Ordinal);
        Assert.DoesNotContain("(Get-FullPath $artifactRootValue)", script, StringComparison.Ordinal);
    }
}
