using System.Text;

namespace God2.GameplayContentRecovery;

public sealed class RecoveryReportWriter
{
    private readonly string _artifactRoot;
    private readonly string _reportRoot;

    public RecoveryReportWriter(string artifactRoot, string reportRoot)
    {
        _artifactRoot = artifactRoot;
        _reportRoot = reportRoot;
    }

    public async Task<IReadOnlyDictionary<string, object?>> WriteAsync(
        RecoveryWorkspace workspace,
        DatabaseImportResult database,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_artifactRoot);
        Directory.CreateDirectory(_reportRoot);
        var coverage = BuildCoverage(workspace, database);
        var localization = BuildLocalization(workspace, database);
        var status = DetermineStatus(workspace, database);
        var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["phase"] = workspace.Phase,
            ["status"] = status,
            ["runId"] = workspace.RunId,
            ["startedAtUtc"] = workspace.StartedAtUtc,
            ["completedAtUtc"] = DateTime.UtcNow,
            ["sourceFiles"] = workspace.Sources.Count,
            ["rawRows"] = workspace.Raw.Count,
            ["stagingRows"] = workspace.Staging.Count,
            ["validatedRows"] = workspace.Validated.Count,
            ["productionManifestRows"] = workspace.Promotions.Count,
            ["formalTextRowsUpdated"] = database.FormalTextRowsUpdated,
            ["localizationRowsUpserted"] = database.LocalizationRowsUpserted,
            ["verifiedGuideFieldsPromoted"] = database.VerifiedGuideFieldsPromoted,
            ["coverage"] = coverage,
            ["localization"] = localization,
            ["referentialIntegrity"] = database.ReferentialIntegrity,
            ["productionCompleteness"] = database.ProductionCompleteness,
            ["runtimeValidation"] = database.RuntimeValidation,
            ["headlessValidation"] = database.HeadlessValidation,
            ["database"] = new
            {
                engine = "MariaDB",
                database.ServerVersion,
                database.DatabaseName,
                database.RepositoryType,
                database.MigrationStatus,
                before = workspace.DatabaseBefore,
                after = workspace.DatabaseAfter
            },
            ["manualOperation"] = "NO",
            ["newCapture"] = "NO",
            ["fakeNetworkBytes"] = 0,
            ["wireClosedLoopStarted"] = false,
            ["remainingEvidenceGaps"] = workspace.Entities
                .SelectMany(entity => entity.MissingRequiredFields.Select(field => $"{entity.Domain}:{field}"))
                .GroupBy(value => value, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .Select(group => new { gap = group.Key, rows = group.Count() })
                .ToArray()
        };

        await WriteJsonAsync("source-inventory.json", new { runId = workspace.RunId, sources = workspace.Sources }, cancellationToken);
        await WriteJsonAsync("extraction-manifest.json", new
        {
            runId = workspace.RunId,
            extractorVersion = workspace.ExtractorVersion,
            sourceHashes = workspace.Sources.Select(source => new { source.SourceIdentity, source.SourceHash, source.RecordCount }).ToArray(),
            domainRows = workspace.Raw.GroupBy(row => row.Domain, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        }, cancellationToken);
        await WriteJsonAsync("normalized-dataset-manifest.json", new
        {
            runId = workspace.RunId,
            stagingRows = workspace.Staging.Count,
            validatedRows = workspace.Validated.Count,
            productionRows = workspace.Promotions.Count,
            stagingByDomain = workspace.Staging.GroupBy(row => row.Domain, StringComparer.Ordinal).ToDictionary(group => group.Key, group => new { rows = group.Count(), hashes = group.Select(row => row.ConvertedTextHash).Where(hash => hash is not null).Distinct(StringComparer.Ordinal).Count() }, StringComparer.Ordinal),
            mariaDbBefore = workspace.DatabaseBefore,
            mariaDbAfter = workspace.DatabaseAfter
        }, cancellationToken);
        await WriteJsonAsync("verified-guide-evidence.json", new
        {
            policy = "UserVerifiedGuideEvidence; promote only unambiguous structured fields after exact official-client identity match",
            promotedFields = database.VerifiedGuideFieldsPromoted,
            rows = workspace.Entities.Where(entity => entity.SourceFile.Contains("17173.com", StringComparison.OrdinalIgnoreCase) || entity.SourceFile.Contains("gamer.com.tw", StringComparison.OrdinalIgnoreCase)).ToArray()
        }, cancellationToken);
        await WriteJsonAsync("localization-column-inventory.json", new { runId = workspace.RunId, columns = database.LocalizationColumnInventory }, cancellationToken);
        await WriteJsonAsync("localization-glossary.json", new
        {
            glossaryVersion = RecoveryVersions.GlossaryVersion,
            converter = RecoveryVersions.ConverterName,
            converterVersion = RecoveryVersions.ConverterVersion,
            targetLanguage = RecoveryVersions.TargetLanguage,
            entries = God2Glossary.Create()
        }, cancellationToken);
        await WriteJsonAsync("localization-conversion.json", new { runId = workspace.RunId, summary = localization, rows = workspace.Localization }, cancellationToken);
        await WriteJsonAsync("localization-review-required.json", new
        {
            runId = workspace.RunId,
            rows = workspace.Localization.Where(row => row.Result.LanguageReviewStatus == "LanguageReviewRequired").ToArray()
        }, cancellationToken);
        await WriteJsonAsync("map-coverage.json", DomainCoverage(workspace, "Map"), cancellationToken);
        await WriteJsonAsync("npc-coverage.json", new { templates = DomainCoverage(workspace, "NpcTemplate"), spawnEvidence = DomainCoverage(workspace, "NpcSpawnEvidence"), appearances = DomainCoverage(workspace, "NpcAppearance") }, cancellationToken);
        await WriteJsonAsync("monster-coverage.json", new { templates = DomainCoverage(workspace, "MonsterTemplate"), encounterNames = DomainCoverage(workspace, "EncounterName") }, cancellationToken);
        await WriteJsonAsync("drop-coverage.json", DomainCoverage(workspace, "MonsterDropEvidence"), cancellationToken);
        await WriteJsonAsync("skill-coverage.json", new { skills = DomainCoverage(workspace, "Skill"), familyEvidence = DomainCoverage(workspace, "SkillFamilyEvidence") }, cancellationToken);
        await WriteJsonAsync("item-reference-coverage.json", DomainCoverage(workspace, "Item"), cancellationToken);
        await WriteJsonAsync("referential-integrity.json", new { runId = workspace.RunId, database = database.ReferentialIntegrity, validation = workspace.Validation.Where(item => item.Gate.Contains("Reference", StringComparison.Ordinal) || item.Gate.Contains("Duplicate", StringComparison.Ordinal)).ToArray() }, cancellationToken);
        await WriteJsonAsync("runtime-validation.json", new { runId = workspace.RunId, runtime = database.RuntimeValidation, headless = database.HeadlessValidation }, cancellationToken);
        await WriteJsonAsync("test-results.json", new
        {
            runId = workspace.RunId,
            contentPipeline = new
            {
                validationRows = workspace.Validation.Count,
                pass = workspace.Validation.Count(item => item.Status == "PASS"),
                evidenceGap = workspace.Validation.Count(item => item.Severity == "EvidenceGap"),
                errors = workspace.Validation.Count(item => item.Status == "FAIL" && item.Severity == "Error"),
                brokenPlaceholder = workspace.Localization.Count(item => !item.Result.PlaceholderPreserved),
                brokenMarkup = workspace.Localization.Count(item => !item.Result.MarkupPreserved),
                conversionFailed = workspace.Localization.Count(item => item.Result.ConversionStatus == "ConversionFailed")
            },
            externalGates = "Pending final build/test/security gate execution"
        }, cancellationToken);
        await WriteJsonAsync("final-summary.json", summary, cancellationToken);

        await WriteReportsAsync(workspace, database, coverage, localization, summary, cancellationToken);
        return summary;
    }

    private async Task WriteReportsAsync(
        RecoveryWorkspace workspace,
        DatabaseImportResult database,
        IReadOnlyDictionary<string, object?> coverage,
        IReadOnlyDictionary<string, object?> localization,
        IReadOnlyDictionary<string, object?> summary,
        CancellationToken cancellationToken)
    {
        var sourceInventory = $"""
            # Gameplay Content Recovery Phase 1 — Source Inventory

            - Official Client identity: God2 / 封神2：仙界傳
            - Client and resource files inventoried: {workspace.Sources.Count(source => source.SourceIdentity.StartsWith("client:", StringComparison.Ordinal))}
            - Packed CSV tables decoded with verified 05 16 LZSS + CP936: {workspace.Diagnostics.GetValueOrDefault("csvZDecoded")}
            - Bahamut supplemental sources (board 8395 only): {workspace.Sources.Count(source => source.SourceType == "BahamutSupplementalEvidence")}
            - 17173 xjz supplemental pages: {workspace.Sources.Count(source => source.SourceType == "17173SupplementalEvidence")}
            - Wrong-game board 6784 accepted: 0
            - Raw evidence rows imported: {workspace.Raw.Count}
            - Source hashes missing: {workspace.Sources.Count(source => string.IsNullOrWhiteSpace(source.SourceHash))}

            Client resources provide identity authority. Per user verification, Bahamut and 17173 guide facts may promote an unambiguous structured field after an exact official-client identity match. Missing probabilities and conflicting or ambiguous identities remain isolated. No complete third-party article body is stored; source URLs, page hashes, individual factual rows and parser provenance are retained.
            """;
        await WriteReportAsync("GameplayContentRecoveryPhase1.SourceInventory.md", sourceInventory, cancellationToken);

        var localizationAudit = BuildLocalizationReport(workspace, database, localization);
        await WriteReportAsync("GameplayContentRecoveryPhase1.LocalizationAudit.md", localizationAudit, cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.LocalizationConversion.md", localizationAudit, cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.MapAudit.md", BuildDomainReport(workspace, "Map", "Map / Area", ["AuthoritativeMapId", "DisplayName", "DefaultSpawn"]), cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.NpcAudit.md", BuildDomainReport(workspace, "NpcTemplate", "NPC Templates / Coordinates", ["MapId", "SpawnX", "SpawnY", "InteractionBinding"]), cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.MonsterAudit.md", BuildDomainReport(workspace, "MonsterTemplate", "Monster Templates / Stats / Spawns", ["Level", "MaxHP", "MpPolicy", "CombatStats", "Spawn"]), cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.DropAudit.md", BuildDomainReport(workspace, "MonsterDropEvidence", "Monster Rewards / Drops", ["DropChance", "QuantityRange", "PatchIdentity"]), cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.SkillAudit.md", BuildDomainReport(workspace, "Skill", "Skills", ["SkillFamily", "TargetPolicy", "MpCostPolicy", "EffectSemantics"]), cancellationToken);
        await WriteReportAsync("GameplayContentRecoveryPhase1.ItemReferenceAudit.md", BuildDomainReport(workspace, "Item", "Items Required by Drops", ["MaximumStackPolicy", "BindPolicy", "TradePolicy"]), cancellationToken);

        var referential = new StringBuilder("# Gameplay Content Recovery Phase 1 — Referential Integrity\n\n");
        foreach (var pair in database.ReferentialIntegrity)
        {
            referential.AppendLine($"- {pair.Key}: {pair.Value}");
        }
        referential.AppendLine($"- Broken foreign keys: {database.BrokenForeignKeys}");
        referential.AppendLine($"- Content orphans: {database.ContentOrphans}");
        await WriteReportAsync("GameplayContentRecoveryPhase1.ReferentialIntegrity.md", referential.ToString(), cancellationToken);

        var runtime = $"""
            # Gameplay Content Recovery Phase 1 — Runtime Validation

            ```json
            {RecoveryJson.Serialize(new { database.RuntimeValidation, database.HeadlessValidation })}
            ```

            The production repository reads the formal MariaDB tables. EvidenceBlocked records remain present for provenance but are not reclassified as complete gameplay definitions; current item validation quarantines rows whose stack, category, bind, trade or price policies are unresolved.
            """;
        await WriteReportAsync("GameplayContentRecoveryPhase1.RuntimeValidation.md", runtime, cancellationToken);

        var final = $"""
            # Gameplay Content Database Recovery Phase 1 — Final

            GAMEPLAY CONTENT DATABASE RECOVERY PHASE 1: {summary["status"]}

            - Raw evidence rows: {workspace.Raw.Count}
            - Staging rows: {workspace.Staging.Count}
            - Validated localization/structure rows: {workspace.Validated.Count}
            - Formal MariaDB display rows updated: {database.FormalTextRowsUpdated}
            - Verified guide fields promoted: {database.VerifiedGuideFieldsPromoted}
            - Production manifest rows: {workspace.Promotions.Count}
            - Production Simplified Chinese display rows: {database.ProductionSimplifiedDisplayRows}
            - Broken foreign keys: {database.BrokenForeignKeys}
            - Content orphans: {database.ContentOrphans}
            - Runtime repository: {database.RepositoryType}
            - MariaDB server: {database.ServerVersion}
            - Manual operation: NO
            - New capture: NO
            - Fake Network Bytes: 0
            - New Wire Closed Loop: NOT STARTED

            The official client content was decoded directly from CP936 source bytes and localized through pinned OpenccNetLib S2Twp plus the versioned project glossary. Original Simplified text remains only in Raw/Staging/audit provenance. User-verified guide values are promoted only when their table field is unambiguous and the displayed name uniquely cross-references the official client. Missing probabilities or gameplay values remain EvidenceBlocked rather than being defaulted.

            ```json
            {RecoveryJson.Serialize(new { coverage, localization })}
            ```
            """;
        await WriteReportAsync("GameplayContentRecoveryPhase1.Final.md", final, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> BuildCoverage(RecoveryWorkspace workspace, DatabaseImportResult database)
    {
        var maps = workspace.Entities.Where(entity => entity.Domain == "Map").ToArray();
        var npcs = workspace.Entities.Where(entity => entity.Domain == "NpcTemplate").ToArray();
        var monsters = workspace.Entities.Where(entity => entity.Domain == "MonsterTemplate").ToArray();
        var skills = workspace.Entities.Where(entity => entity.Domain == "Skill").ToArray();
        var items = workspace.Entities.Where(entity => entity.Domain == "Item").ToArray();
        var drops = workspace.Entities.Where(entity => entity.Domain == "MonsterDropEvidence").ToArray();
        var production = database.ProductionCompleteness;
        var mapsTotal = production["MapsTotal"];
        var mapsVerified = production["MapsVerified"];
        var npcTotal = production["NpcTemplatesTotal"];
        var monsterTotal = production["MonsterTemplatesTotal"];
        var skillsTotal = production["SkillsTotal"];
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mapsTotal"] = mapsTotal,
            ["mapsVerified"] = mapsVerified,
            ["mapsBlocked"] = mapsTotal - mapsVerified,
            ["mapExtractedEvidenceRows"] = maps.Length,
            ["npcTemplatesTotal"] = npcTotal,
            ["npcTemplatesVerified"] = npcs.Count(entity => entity.MissingRequiredFields.Count == 0),
            ["npcTemplatesBlocked"] = npcTotal - npcs.Count(entity => entity.MissingRequiredFields.Count == 0),
            ["npcCoordinatesCompletePercent"] = Percent(production["NpcCoordinatesComplete"], npcTotal),
            ["npcSupplementalCoordinateRows"] = workspace.Entities.Count(entity => entity.Domain == "NpcSpawnEvidence"),
            ["monsterTemplatesTotal"] = monsterTotal,
            ["monsterTemplatesCompletePercent"] = Percent(monsters.Count(entity => entity.MissingRequiredFields.Count == 0), monsterTotal),
            ["monsterSpawnCompletePercent"] = Percent(production["MonsterSpawnTemplatesComplete"], monsterTotal),
            ["monsterLevelCompletePercent"] = Percent(production["MonsterLevelComplete"], monsterTotal),
            ["monsterHpCompletePercent"] = Percent(production["MonsterHpComplete"], monsterTotal),
            ["monsterMpPolicyCompletePercent"] = Percent(production["MonsterMpPolicyComplete"], monsterTotal),
            ["monsterCombatStatsCompletePercent"] = Percent(production["MonsterCombatStatsComplete"], monsterTotal),
            ["monsterRewardCompletePercent"] = Percent(production["MonsterRewardComplete"], monsterTotal),
            ["monsterDropPolicyCompletePercent"] = Percent(production["MonsterDropPolicyComplete"], monsterTotal),
            ["monsterDropTableCompletePercent"] = Percent(production["MonsterDropTableComplete"], monsterTotal),
            ["monsterDropTableEvidenceRows"] = drops.Length,
            ["dropItemReferenceCompletePercent"] = Percent(production["DropItemReferencesComplete"], production["DropEntriesTotal"]),
            ["skillsTotal"] = skillsTotal,
            ["skillsClassifiedPercent"] = Percent(production["SkillsClassified"], skillsTotal),
            ["skillTargetPolicyCompletePercent"] = Percent(production["SkillTargetPolicyComplete"], skillsTotal),
            ["skillMpCostPolicyCompletePercent"] = Percent(production["SkillMpCostPolicyComplete"], skillsTotal),
            ["skillEffectsCompletePercent"] = Percent(skills, entity => !entity.MissingRequiredFields.Contains("EffectSemantics", StringComparer.Ordinal)),
            ["itemsTotal"] = production["ItemsTotal"],
            ["itemExtractedEvidenceRows"] = items.Length,
            ["itemsRequiredByDropsCompletePercent"] = Percent(production["DropItemReferencesComplete"], production["ItemsRequiredByDrops"])
        };
    }

    private static IReadOnlyDictionary<string, object?> BuildLocalization(RecoveryWorkspace workspace, DatabaseImportResult database) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["localizedTextRowsTotal"] = workspace.Localization.Count,
            ["alreadyTraditionalRows"] = workspace.Localization.Count(row => row.Result.ConversionStatus == "TraditionalVerified"),
            ["convertedToTraditionalRows"] = workspace.Localization.Count(row => row.Result.ConversionStatus == "ConvertedToTraditional"),
            ["mixedLanguageNormalizedRows"] = workspace.Localization.Count(row => row.Result.ConversionStatus == "MixedLanguageNormalized"),
            ["languageReviewRequiredRows"] = workspace.Localization.Count(row => row.Result.LanguageReviewStatus == "LanguageReviewRequired"),
            ["conversionFailedRows"] = workspace.Localization.Count(row => row.Result.ConversionStatus == "ConversionFailed"),
            ["productionSimplifiedChineseRows"] = database.ProductionSimplifiedDisplayRows,
            ["brokenPlaceholderRows"] = workspace.Localization.Count(row => !row.Result.PlaceholderPreserved),
            ["brokenMarkupRows"] = workspace.Localization.Count(row => !row.Result.MarkupPreserved),
            ["brokenControlCodeRows"] = workspace.Localization.Count(row => !row.Result.ControlCodesPreserved),
            ["converter"] = RecoveryVersions.ConverterName,
            ["converterVersion"] = RecoveryVersions.ConverterVersion,
            ["glossaryVersion"] = RecoveryVersions.GlossaryVersion
        };

    private static object DomainCoverage(RecoveryWorkspace workspace, string domain)
    {
        var rows = workspace.Entities.Where(entity => entity.Domain == domain).ToArray();
        return new
        {
            domain,
            total = rows.Length,
            verified = rows.Count(entity => entity.MissingRequiredFields.Count == 0 && (entity.EvidenceStatus is "Verified" or "Derived")),
            partiallyMapped = rows.Count(entity => entity.EvidenceStatus == "PartiallyMapped"),
            evidenceBlocked = rows.Count(entity => entity.MissingRequiredFields.Count > 0),
            sourceFiles = rows.Select(entity => entity.SourceFile).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            missingFields = rows.SelectMany(entity => entity.MissingRequiredFields).GroupBy(value => value, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        };
    }

    private static string DetermineStatus(RecoveryWorkspace workspace, DatabaseImportResult database)
    {
        if (database.ProductionSimplifiedDisplayRows > 0 || workspace.Localization.Any(row => row.Result.ConversionStatus == "ConversionFailed"))
        {
            return "FAILED";
        }

        var coreBlocked = workspace.Entities.Any(entity =>
            (entity.Domain is "NpcTemplate" or "MonsterTemplate" or "Skill") &&
            entity.MissingRequiredFields.Count > 0);
        return coreBlocked ? "PARTIAL" : database.BrokenForeignKeys == 0 ? "PASS WITH DOCUMENTED EVIDENCE GAPS" : "PARTIAL";
    }

    private static string BuildLocalizationReport(RecoveryWorkspace workspace, DatabaseImportResult database, IReadOnlyDictionary<string, object?> localization) => $"""
        # Gameplay Content Recovery Phase 1 — zh-TW Localization Audit

        - Converter: {RecoveryVersions.ConverterName} {RecoveryVersions.ConverterVersion} (offline, pinned)
        - Glossary: {RecoveryVersions.GlossaryVersion}
        - Target locale: zh-TW
        - Localized text rows: {workspace.Localization.Count}
        - Already Traditional: {localization["alreadyTraditionalRows"]}
        - Converted to Traditional: {localization["convertedToTraditionalRows"]}
        - Mixed language normalized: {localization["mixedLanguageNormalizedRows"]}
        - Language review required: {localization["languageReviewRequiredRows"]}
        - Conversion failed: {localization["conversionFailedRows"]}
        - Broken placeholders: {localization["brokenPlaceholderRows"]}
        - Broken markup: {localization["brokenMarkupRows"]}
        - Production Simplified Chinese display rows: {database.ProductionSimplifiedDisplayRows}

        Raw OriginalText remains unchanged. Identifiers, ResourceKey values, paths, placeholders, markup and control tokens are excluded from linguistic conversion. Unknown JSON blobs are inventoried but never blindly replaced.
        """;

    private static string BuildDomainReport(RecoveryWorkspace workspace, string domain, string title, IReadOnlyList<string> criticalFields)
    {
        var rows = workspace.Entities.Where(entity => entity.Domain == domain).ToArray();
        var builder = new StringBuilder($"# Gameplay Content Recovery Phase 1 — {title}\n\n");
        builder.AppendLine($"- Total extracted rows: {rows.Length}");
        builder.AppendLine($"- Fully promotion-complete rows: {rows.Count(entity => entity.MissingRequiredFields.Count == 0)}");
        builder.AppendLine($"- EvidenceBlocked rows: {rows.Count(entity => entity.MissingRequiredFields.Count > 0)}");
        foreach (var field in criticalFields)
        {
            builder.AppendLine($"- Missing {field}: {rows.Count(entity => entity.MissingRequiredFields.Contains(field, StringComparer.Ordinal))}");
        }
        builder.AppendLine();
        builder.AppendLine("Missing gameplay values remain null/Unknown in Staging; no zero, probability, coordinate, target or policy default is introduced.");
        return builder.ToString();
    }

    private static double Percent<T>(IReadOnlyCollection<T> rows, Func<T, bool> predicate) => rows.Count == 0 ? 100.0 : Math.Round(rows.Count(predicate) * 100.0 / rows.Count, 2);

    private static double Percent(long complete, long total) => total == 0 ? 0.0 : Math.Round(complete * 100.0 / total, 2);

    private async Task WriteJsonAsync(string name, object value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(_artifactRoot, name), RecoveryJson.Serialize(value) + Environment.NewLine, new UTF8Encoding(false), cancellationToken);

    private async Task WriteReportAsync(string name, string value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(_reportRoot, name), value.Trim() + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
}
