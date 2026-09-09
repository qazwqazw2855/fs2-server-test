using God2.ClassicServer.Infrastructure;
using God2.GameplayContentRecovery;

var options = Arguments.Parse(args);
if (!string.IsNullOrWhiteSpace(options.SearchClientCsvText))
{
    var matches = new List<object>();
    foreach (var path in Directory.EnumerateFiles(options.ClientRoot, "*.csvZ", SearchOption.AllDirectories)
                 .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
    {
        var document = await God2PackedFile.ReadCsvZAsync(path, CancellationToken.None);
        foreach (var row in document.Rows.Where(row => row.RawLine.Contains(options.SearchClientCsvText, StringComparison.Ordinal)))
        {
            matches.Add(new
            {
                path = Path.GetRelativePath(options.ClientRoot, path).Replace('\\', '/'),
                document.SourceHash,
                row.RecordIndex,
                fields = row.Fields
            });
        }
    }
    Console.WriteLine(RecoveryJson.Serialize(new { query = options.SearchClientCsvText, matchCount = matches.Count, matches }));
    return 0;
}

if (options.ListGameDataSections)
{
    var sourcePath = Path.Combine(options.ClientRoot, "Data2", "Patch", "Comm", "gamedata.csvZ");
    var document = await God2PackedFile.ReadCsvZAsync(sourcePath, CancellationToken.None);
    var sections = GameDataSections.Parse(document.Rows)
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new { name = pair.Key, count = pair.Value.DeclaredCount, markerSourceLine = pair.Value.MarkerRecordIndex })
        .ToArray();
    Console.WriteLine(RecoveryJson.Serialize(new
    {
        sourcePath = "Data2/Patch/Comm/gamedata.csvZ",
        document.SourceHash,
        sectionCount = sections.Length,
        sections
    }));
    return 0;
}

if (options.FlowInventory)
{
    var inventory = await FlowRomInventory.BuildAsync(options.ClientRoot, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(inventory));
    return inventory.Succeeded ? 0 : 4;
}

if (options.MapResourceInventory)
{
    var inventory = await ClientMapResourceInventory.BuildAsync(options.ClientRoot, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(inventory));
    return inventory.Succeeded ? 0 : 4;
}

if (options.WriteOfficialMapMigration)
{
    var outputPath = options.OfficialMapMigrationOutput ?? Path.Combine(options.RepositoryRoot, "database", "schema", "114_publish_official_map_catalog.sql");
    var result = await OfficialMapMigrationWriter.WriteAsync(options.ClientRoot, outputPath, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.DefinitionCount == 144 && result.EnabledCount + result.EvidenceBlockedCount == 144 ? 0 : 4;
}

if (options.WriteOfficialPortalLinkMigration)
{
    var outputPath = options.OfficialPortalLinkMigrationOutput ?? Path.Combine(options.RepositoryRoot, "database", "schema", "115_publish_official_portal_resource_links.sql");
    var result = await OfficialPortalLinkMigrationWriter.WriteAsync(options.RepositoryRoot, options.ClientRoot, outputPath, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.LinkCount == 65 ? 0 : 4;
}

if (options.WriteOfficialNpcCatalogMigration)
{
    var outputPath = options.OfficialNpcCatalogMigrationOutput ?? Path.Combine(options.RepositoryRoot, "database", "schema", "116_publish_official_npc_appearance_and_coordinates.sql");
    var result = await OfficialNpcCatalogMigrationWriter.WriteAsync(options.RepositoryRoot, options.ClientRoot, outputPath, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.AppearanceCount == 4449 && result.ValidSelectorCount == 4449 && result.CoordinateCandidateCount >= 100 ? 0 : 4;
}

if (options.WriteOfficialNpcSourceRowMigration)
{
    var outputPath = options.OfficialNpcSourceRowMigrationOutput ?? Path.Combine(options.RepositoryRoot, "database", "schema", "117_catalog_all_official_npc_appearance_source_rows.sql");
    var result = await OfficialNpcSourceRowMigrationWriter.WriteAsync(options.ClientRoot, outputPath, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.SourceRowCount == 4449 && result.UniqueHandleCount == 4447 && result.DuplicateHandleRowCount == 2 ? 0 : 4;
}

if (options.ExportGameDataSection)
{
    var sectionName = options.GameDataSectionName
        ?? throw new ArgumentException("--gamedata-section-name is required when exporting a section.");
    var outputPath = options.GameDataSectionOutput
        ?? Path.Combine(options.RepositoryRoot, "Artifacts", "OfficialGameDataSections", $"{sectionName}.json");
    var result = await GameDataSectionExporter.WriteAsync(
        options.ClientRoot,
        sectionName,
        outputPath,
        CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(new
    {
        result.Section,
        result.SourceSha256,
        result.MarkerSourceLine,
        result.DeclaredCount,
        result.ExportedCount,
        result.OutputPath
    }));
    return result.ExportedCount == result.DeclaredCount ? 0 : 4;
}

if (options.WriteOfficialImmortalCatalog)
{
    var outputPath = options.OfficialImmortalCatalogOutput
        ?? Path.Combine(options.RepositoryRoot, "db", "imports", "official", "immortals", "immortals.official.json");
    var result = await OfficialImmortalCatalogExporter.WriteAsync(
        options.ClientRoot,
        outputPath,
        CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(new
    {
        result.RecordCount,
        result.VerificationStatus,
        OutputPath = Path.GetFullPath(outputPath)
    }));
    return result.RecordCount == 34 && result.Records.All(record => !record.RuntimeEligible) ? 0 : 4;
}

if (options.WriteOfficialBattlePetCatalog)
{
    var outputPath = options.OfficialBattlePetCatalogOutput
        ?? Path.Combine(options.RepositoryRoot, "db", "imports", "official", "battle_pets", "battle_pets.official.json");
    var result = await OfficialBattlePetCatalogExporter.WriteAsync(
        options.ClientRoot,
        outputPath,
        CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(new
    {
        result.RecordCount,
        result.VerificationStatus,
        OutputPath = Path.GetFullPath(outputPath)
    }));
    return result.RecordCount == 178 && result.Records.All(record => !record.RuntimeEligible) ? 0 : 4;
}

if (options.WriteOfficialItemEffectVisualCatalog)
{
    var catalogOutput = options.OfficialItemEffectVisualCatalogOutput
        ?? Path.Combine(options.RepositoryRoot, "Artifacts", "OfficialClientStaticCatalogs", "item-effect-visuals.exact.json");
    var migrationOutput = options.OfficialItemEffectVisualMigrationOutput
        ?? Path.Combine(options.RepositoryRoot, "database", "schema", "139_publish_verified_client_item_effect_visual_catalog.sql");
    var result = await OfficialItemEffectVisualCatalogWriter.WriteAsync(
        options.ClientRoot, catalogOutput, migrationOutput, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.RecordCount == 102 && result.ItemEft3Count == 72 && result.GodItemEftCount == 30 ? 0 : 4;
}

if (options.WriteOfficialItemUsageFlagCatalog)
{
    var catalogOutput = options.OfficialItemUsageFlagCatalogOutput
        ?? Path.Combine(options.RepositoryRoot, "Artifacts", "OfficialClientStaticCatalogs", "item-usage-flags.exact.json");
    var migrationOutput = options.OfficialItemUsageFlagMigrationOutput
        ?? Path.Combine(options.RepositoryRoot, "database", "schema", "140_gate_exact_client_item_usage_flags.sql");
    var result = await OfficialItemUsageFlagCatalogWriter.WriteAsync(
        options.RepositoryRoot, options.ClientRoot, catalogOutput, migrationOutput, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.RecordCount == 17418 && result.FormalBaselineCount == 17407 && result.ExactCurrentAdditionCount == 11 ? 0 : 4;
}

if (options.WriteOfficialItemCatalog)
{
    var output = options.OfficialItemCatalogOutput
        ?? Path.Combine(options.RepositoryRoot, "db", "imports", "official", "items", "items.official.json");
    var result = await OfficialItemCatalogWriter.WriteAsync(options.ClientRoot, output, CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(result));
    return result.RecordCount == 17418 && result.ExistingFormalBaselineCount == 17407 && result.MissingCanonicalBindingCount == 11 ? 0 : 4;
}

var configuration = new JsonServerConfigurationLoader(new AppPathProvider(options.RepositoryRoot)).Load();
if (!configuration.Succeeded || configuration.Value is null)
{
    Console.Error.WriteLine($"Configuration failed: {configuration.Error.Code}");
    return 2;
}

if (string.IsNullOrWhiteSpace(configuration.Value.Database.Password))
{
    Console.Error.WriteLine("GOD2_DB_PASSWORD is required; no content write was attempted.");
    return 3;
}

if (options.SyncMapResources)
{
    var builderPassword = Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_PASSWORD");
    if (string.IsNullOrWhiteSpace(builderPassword))
    {
        Console.Error.WriteLine("GOD2_DB_BUILDER_PASSWORD is required; no canonical map resource write was attempted.");
        return 3;
    }
    var inventory = await ClientMapResourceInventory.BuildAsync(options.ClientRoot, CancellationToken.None);
    var executablePath = Path.Combine(options.ClientRoot, "God2_opt.exe");
    var executableBytes = await File.ReadAllBytesAsync(executablePath, CancellationToken.None);
    var executableHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(executableBytes)).ToLowerInvariant();
    var buildId = $"god2-opt-{executableHash[..12]}";
    var builderOptions = configuration.Value.Database with
    {
        Username = Environment.GetEnvironmentVariable("GOD2_DB_BUILDER_USERNAME") ?? "god2_catalog_builder",
        Password = builderPassword
    };
    var result = await new ClientMapResourceCatalogStore(builderOptions).SynchronizeAsync(
        inventory.Resources,
        buildId,
        executableHash,
        CancellationToken.None);
    Console.WriteLine(RecoveryJson.Serialize(new
    {
        inventory.ResourceCount,
        inventory.IndoorMapResourceCount,
        inventory.DimensionCompleteCount,
        buildId,
        result
    }));
    return inventory.Succeeded && result.Succeeded ? 0 : 4;
}

var workspace = new RecoveryWorkspace
{
    Phase = options.Phase switch
    {
        "phase2" => RecoveryVersions.Phase2,
        "phase3" => RecoveryVersions.Phase3,
        _ => RecoveryVersions.Phase
    },
    ExtractorVersion = options.Phase switch
    {
        "phase2" => RecoveryVersions.ExtractorPhase2,
        "phase3" => RecoveryVersions.ExtractorPhase3,
        _ => RecoveryVersions.Extractor
    },
    RunId = Guid.NewGuid().ToString(),
    StartedAtUtc = DateTime.UtcNow
};
var glossary = God2Glossary.Create();
var localization = new ZhTwLocalization(glossary);

try
{
    if (options.ScanOnly)
    {
        var findings = await new MariaDbContentRecoveryStore(
            configuration.Value.Database,
            Path.Combine(options.RepositoryRoot, "database", "schema"),
            localization).FindProductionSimplifiedAsync(CancellationToken.None);
        Console.WriteLine(RecoveryJson.Serialize(new { productionSimplifiedDisplayRows = findings.Count, findings }));
        return findings.Count == 0 ? 0 : 4;
    }

    if (options.PruneHistory)
    {
        var result = await new RecoveryHistoryPruner(configuration.Value.Database).PruneAsync(CancellationToken.None);
        Console.WriteLine(RecoveryJson.Serialize(result));
        return 0;
    }

    if (options.AnalysisOnly)
    {
        if (options.Phase != "phase3")
        {
            throw new ArgumentException("--analysis-only is available only for phase3.");
        }

        var analysis = await new Phase3DeepSemanticAnalyzer(configuration.Value.Database).AnalyzeAsync(CancellationToken.None);
        Console.WriteLine(RecoveryJson.Serialize(analysis));
        return 0;
    }

    if (options.Phase == "phase3")
    {
        Console.WriteLine("stage=phase3-deep-semantic-exhaustion-and-promotion");
        var result = await new Phase3SemanticRecovery(
            configuration.Value.Database,
            Path.Combine(options.RepositoryRoot, "database", "schema"),
            localization).RecoverAsync(workspace, CancellationToken.None);
        var phase3ArtifactRoot = Path.Combine(options.RepositoryRoot, "Artifacts", workspace.Phase);
        var phase3ReportRoot = Path.Combine(options.RepositoryRoot, "Reports");
        var phase3Summary = await new Phase3RecoveryReportWriter(phase3ArtifactRoot, phase3ReportRoot)
            .WriteAsync(workspace, result, CancellationToken.None);
        Console.WriteLine($"status={phase3Summary["status"]}");
        Console.WriteLine($"productionSimplifiedDisplayRows={result.ProductionSimplifiedDisplayRows}");
        Console.WriteLine($"artifactRoot={phase3ArtifactRoot}");
        return result.Status.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 4;
    }

    if (options.Phase == "phase2")
    {
        Console.WriteLine("stage=phase2-full-client-content-exhaustion");
        await new Phase2ClientExhaustionExtractor(options.RepositoryRoot, options.ClientRoot, localization).ExtractAsync(workspace, CancellationToken.None);
        Console.WriteLine($"sourceFiles={workspace.Sources.Count};rawRows={workspace.Raw.Count};phase2Entities={workspace.Entities.Count}");
    }
    else
    {
        Console.WriteLine("stage=source-inventory-and-official-client-extraction");
        await new OfficialClientExtractor(options.RepositoryRoot, options.ClientRoot).ExtractAsync(workspace, CancellationToken.None);
        Console.WriteLine($"sourceFiles={workspace.Sources.Count};rawRows={workspace.Raw.Count};entities={workspace.Entities.Count}");

        Console.WriteLine("stage=bahamut-8395-supplemental-evidence");
        await new BahamutEvidenceExtractor(
            Path.Combine(options.RepositoryRoot, "db", "imports", "supplemental", "bahamut", "phase1-sources.json"),
            localization).ExtractAsync(workspace, CancellationToken.None);
        Console.WriteLine($"bahamutSources={workspace.Sources.Count(source => source.SourceType == "BahamutSupplementalEvidence")}");

        Console.WriteLine("stage=17173-xjz-supplemental-evidence");
        await new Site17173EvidenceExtractor(
            Path.Combine(options.RepositoryRoot, "db", "imports", "supplemental", "17173", "phase1-sources.json"),
            localization).ExtractAsync(workspace, CancellationToken.None);
        Console.WriteLine($"site17173Sources={workspace.Sources.Count(source => source.SourceType == "17173SupplementalEvidence")}");
    }

    RecoveryDeduplication.BeforeTransform(workspace);
    Console.WriteLine("stage=localization-normalization-validation");
    new ContentRecoveryPipeline(localization).TransformAndValidate(workspace);
    RecoveryDeduplication.AfterTransform(workspace);
    Console.WriteLine($"stagingRows={workspace.Staging.Count};localizationRows={workspace.Localization.Count};validatedRows={workspace.Validated.Count}");

    Console.WriteLine("stage=mariadb-physical-import-runtime-validation");
    var database = await new MariaDbContentRecoveryStore(
        configuration.Value.Database,
        Path.Combine(options.RepositoryRoot, "database", "schema"),
        localization).ImportAsync(workspace, CancellationToken.None);

    var artifactRoot = Path.Combine(options.RepositoryRoot, "Artifacts", workspace.Phase);
    var reportRoot = Path.Combine(options.RepositoryRoot, "Reports");
    var summary = options.Phase == "phase2"
        ? await new Phase2RecoveryReportWriter(artifactRoot, reportRoot).WriteAsync(workspace, database, CancellationToken.None)
        : await new RecoveryReportWriter(artifactRoot, reportRoot).WriteAsync(workspace, database, CancellationToken.None);
    Console.WriteLine($"status={summary["status"]}");
    Console.WriteLine($"productionSimplifiedDisplayRows={database.ProductionSimplifiedDisplayRows}");
    Console.WriteLine($"brokenForeignKeys={database.BrokenForeignKeys};contentOrphans={database.ContentOrphans}");
    Console.WriteLine($"artifactRoot={artifactRoot}");
    return summary.TryGetValue("status", out var finalStatus) && finalStatus?.ToString()?.StartsWith("PASS", StringComparison.Ordinal) == true ? 0 : 4;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Content recovery failed: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

internal sealed record Arguments(
    string RepositoryRoot,
    string ClientRoot,
    bool ScanOnly,
    bool AnalysisOnly,
    bool PruneHistory,
    bool FlowInventory,
    bool MapResourceInventory,
    bool SyncMapResources,
    bool WriteOfficialMapMigration,
    string? OfficialMapMigrationOutput,
    bool WriteOfficialPortalLinkMigration,
    string? OfficialPortalLinkMigrationOutput,
    bool WriteOfficialNpcCatalogMigration,
    string? OfficialNpcCatalogMigrationOutput,
    bool WriteOfficialNpcSourceRowMigration,
    string? OfficialNpcSourceRowMigrationOutput,
    string? SearchClientCsvText,
    bool ListGameDataSections,
    bool ExportGameDataSection,
    string? GameDataSectionName,
    string? GameDataSectionOutput,
    bool WriteOfficialImmortalCatalog,
    string? OfficialImmortalCatalogOutput,
    bool WriteOfficialBattlePetCatalog,
    string? OfficialBattlePetCatalogOutput,
    bool WriteOfficialItemEffectVisualCatalog,
    string? OfficialItemEffectVisualCatalogOutput,
    string? OfficialItemEffectVisualMigrationOutput,
    bool WriteOfficialItemUsageFlagCatalog,
    string? OfficialItemUsageFlagCatalogOutput,
    string? OfficialItemUsageFlagMigrationOutput,
    bool WriteOfficialItemCatalog,
    string? OfficialItemCatalogOutput,
    string Phase)
{
    public static Arguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments must be --name value pairs.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        var repositoryRoot = Path.GetFullPath(values.GetValueOrDefault("repository-root") ?? Directory.GetCurrentDirectory());
        var clientRoot = Path.GetFullPath(values.GetValueOrDefault("client-root") ?? throw new ArgumentException("--client-root is required."));
        var scanOnly = bool.TryParse(values.GetValueOrDefault("scan-only"), out var parsedScanOnly) && parsedScanOnly;
        var analysisOnly = bool.TryParse(values.GetValueOrDefault("analysis-only"), out var parsedAnalysisOnly) && parsedAnalysisOnly;
        var pruneHistory = bool.TryParse(values.GetValueOrDefault("prune-history"), out var parsedPruneHistory) && parsedPruneHistory;
        var flowInventory = bool.TryParse(values.GetValueOrDefault("flow-inventory"), out var parsedFlowInventory) && parsedFlowInventory;
        var mapResourceInventory = bool.TryParse(values.GetValueOrDefault("map-resource-inventory"), out var parsedMapResourceInventory) && parsedMapResourceInventory;
        var syncMapResources = bool.TryParse(values.GetValueOrDefault("sync-map-resources"), out var parsedSyncMapResources) && parsedSyncMapResources;
        var writeOfficialMapMigration = bool.TryParse(values.GetValueOrDefault("write-official-map-migration"), out var parsedWriteOfficialMapMigration) && parsedWriteOfficialMapMigration;
        var officialMapMigrationOutput = values.GetValueOrDefault("official-map-migration-output");
        var writeOfficialPortalLinkMigration = bool.TryParse(values.GetValueOrDefault("write-official-portal-link-migration"), out var parsedWriteOfficialPortalLinkMigration) && parsedWriteOfficialPortalLinkMigration;
        var officialPortalLinkMigrationOutput = values.GetValueOrDefault("official-portal-link-migration-output");
        var writeOfficialNpcCatalogMigration = bool.TryParse(values.GetValueOrDefault("write-official-npc-catalog-migration"), out var parsedWriteOfficialNpcCatalogMigration) && parsedWriteOfficialNpcCatalogMigration;
        var officialNpcCatalogMigrationOutput = values.GetValueOrDefault("official-npc-catalog-migration-output");
        var writeOfficialNpcSourceRowMigration = bool.TryParse(values.GetValueOrDefault("write-official-npc-source-row-migration"), out var parsedWriteOfficialNpcSourceRowMigration) && parsedWriteOfficialNpcSourceRowMigration;
        var officialNpcSourceRowMigrationOutput = values.GetValueOrDefault("official-npc-source-row-migration-output");
        var searchClientCsvText = values.GetValueOrDefault("search-client-csv-text");
        var listGameDataSections = bool.TryParse(values.GetValueOrDefault("list-gamedata-sections"), out var parsedListGameDataSections) && parsedListGameDataSections;
        var exportGameDataSection = bool.TryParse(values.GetValueOrDefault("export-gamedata-section"), out var parsedExportGameDataSection) && parsedExportGameDataSection;
        var gameDataSectionName = values.GetValueOrDefault("gamedata-section-name");
        var gameDataSectionOutput = values.GetValueOrDefault("gamedata-section-output");
        var writeOfficialImmortalCatalog = bool.TryParse(values.GetValueOrDefault("write-official-immortal-catalog"), out var parsedWriteOfficialImmortalCatalog) && parsedWriteOfficialImmortalCatalog;
        var officialImmortalCatalogOutput = values.GetValueOrDefault("official-immortal-catalog-output");
        var writeOfficialBattlePetCatalog = bool.TryParse(values.GetValueOrDefault("write-official-battle-pet-catalog"), out var parsedWriteOfficialBattlePetCatalog) && parsedWriteOfficialBattlePetCatalog;
        var officialBattlePetCatalogOutput = values.GetValueOrDefault("official-battle-pet-catalog-output");
        var writeOfficialItemEffectVisualCatalog = bool.TryParse(values.GetValueOrDefault("write-official-item-effect-visual-catalog"), out var parsedWriteOfficialItemEffectVisualCatalog) && parsedWriteOfficialItemEffectVisualCatalog;
        var officialItemEffectVisualCatalogOutput = values.GetValueOrDefault("official-item-effect-visual-catalog-output");
        var officialItemEffectVisualMigrationOutput = values.GetValueOrDefault("official-item-effect-visual-migration-output");
        var writeOfficialItemUsageFlagCatalog = bool.TryParse(values.GetValueOrDefault("write-official-item-usage-flag-catalog"), out var parsedWriteOfficialItemUsageFlagCatalog) && parsedWriteOfficialItemUsageFlagCatalog;
        var officialItemUsageFlagCatalogOutput = values.GetValueOrDefault("official-item-usage-flag-catalog-output");
        var officialItemUsageFlagMigrationOutput = values.GetValueOrDefault("official-item-usage-flag-migration-output");
        var writeOfficialItemCatalog = bool.TryParse(values.GetValueOrDefault("write-official-item-catalog"), out var parsedWriteOfficialItemCatalog) && parsedWriteOfficialItemCatalog;
        var officialItemCatalogOutput = values.GetValueOrDefault("official-item-catalog-output");
        var phase = values.GetValueOrDefault("phase") ?? "phase1";
        if (phase is not ("phase1" or "phase2" or "phase3"))
        {
            throw new ArgumentException("--phase must be phase1, phase2, or phase3.");
        }
        return new Arguments(repositoryRoot, clientRoot, scanOnly, analysisOnly, pruneHistory, flowInventory, mapResourceInventory, syncMapResources, writeOfficialMapMigration, officialMapMigrationOutput, writeOfficialPortalLinkMigration, officialPortalLinkMigrationOutput, writeOfficialNpcCatalogMigration, officialNpcCatalogMigrationOutput, writeOfficialNpcSourceRowMigration, officialNpcSourceRowMigrationOutput, searchClientCsvText, listGameDataSections, exportGameDataSection, gameDataSectionName, gameDataSectionOutput, writeOfficialImmortalCatalog, officialImmortalCatalogOutput, writeOfficialBattlePetCatalog, officialBattlePetCatalogOutput, writeOfficialItemEffectVisualCatalog, officialItemEffectVisualCatalogOutput, officialItemEffectVisualMigrationOutput, writeOfficialItemUsageFlagCatalog, officialItemUsageFlagCatalogOutput, officialItemUsageFlagMigrationOutput, writeOfficialItemCatalog, officialItemCatalogOutput, phase);
    }
}
