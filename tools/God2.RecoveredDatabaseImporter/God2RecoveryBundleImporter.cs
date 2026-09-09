using System.Globalization;

namespace God2.RecoveredDatabaseImporter;

public sealed class God2RecoveryBundleImporter
{
    private readonly RecoveryBundleReader _reader = new();

    public async Task<RecoveryImportResult> ImportAsync(
        RecoveryImportOptions options,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = Path.GetFullPath(options.RepositoryRoot);
        var outputRoot = Path.GetFullPath(options.OutputRoot);
        if (!Directory.Exists(repositoryRoot))
        {
            throw new RecoveryImportException("repository.not_found", $"Repository root was not found: {repositoryRoot}");
        }

        // No artifact or staging write occurs before all package integrity, schema,
        // exact-build, authority, localization, and reference gates have passed.
        var inspection = await _reader.InspectAsync(options.BundlePath, cancellationToken);
        var server = WorkspaceInventoryScanner.ScanServer(repositoryRoot);
        var database = WorkspaceInventoryScanner.ScanDatabase(repositoryRoot);

        var runName = string.Concat(
            inspection.Manifest.PackageId,
            "-",
            inspection.SourceZipSha256[..12],
            "-",
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture));
        var artifactRoot = Path.Combine(outputRoot, runName);
        var stagingRoot = Path.Combine(artifactRoot, "staging");
        Directory.CreateDirectory(artifactRoot);
        var stagedFiles = await _reader.StageAsync(inspection, stagingRoot, cancellationToken);
        var migrationCandidate = StagingMigrationCandidateGenerator.Generate(inspection.Manifest.PackageId);
        var offline = await OfflineRecoveryVerifier.VerifyAsync(
            repositoryRoot,
            stagingRoot,
            migrationCandidate,
            server.Projects,
            cancellationToken);
        var gaps = RecoveryGapMatrixBuilder.Build(inspection, server, database, offline);
        var plans = RecoveryGapMatrixBuilder.BuildPlans(gaps);

        return await ImporterArtifactWriter.WriteAsync(
            artifactRoot,
            stagingRoot,
            inspection,
            server,
            database,
            gaps,
            plans,
            offline,
            migrationCandidate,
            stagedFiles,
            cancellationToken);
    }
}
