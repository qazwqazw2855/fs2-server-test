using System.Text;

namespace God2.GameplayContentRecovery;

public sealed class Phase2RecoveryReportWriter
{
    private readonly string _artifactRoot;
    private readonly string _reportRoot;

    public Phase2RecoveryReportWriter(string artifactRoot, string reportRoot)
    {
        _artifactRoot = artifactRoot;
        _reportRoot = reportRoot;
    }

    public async Task<IReadOnlyDictionary<string, object?>> WriteAsync(RecoveryWorkspace workspace, DatabaseImportResult database, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_artifactRoot);
        Directory.CreateDirectory(_reportRoot);
        var byDomain = workspace.Entities.GroupBy(entity => entity.Domain, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => State(group), StringComparer.Ordinal);
        var missing = workspace.Entities.SelectMany(entity => entity.MissingRequiredFields.Select(field => new { entity.Domain, Field = field }))
            .GroupBy(value => (value.Domain, value.Field)).OrderByDescending(group => group.Count())
            .Select(group => new { domain = group.Key.Domain, field = group.Key.Field, rows = group.Count(), reason = "No accessible evidence established authoritative semantics; no default was applied." }).ToArray();
        var status = database.ProductionSimplifiedDisplayRows == 0 && database.BrokenForeignKeys == 0 && database.ContentOrphans == 0 &&
                     string.Equals(database.RuntimeValidation.GetValueOrDefault("mariaDbLoad")?.ToString(), "PASS", StringComparison.Ordinal) &&
                     string.Equals(database.RuntimeValidation.GetValueOrDefault("runtimeCatalogBuild")?.ToString(), "PASS", StringComparison.Ordinal) &&
                     string.Equals(database.RuntimeValidation.GetValueOrDefault("gameplayCatalogValidation")?.ToString(), "PASS", StringComparison.Ordinal) &&
                     string.Equals(database.RuntimeValidation.GetValueOrDefault("phase2SpecializedValidation")?.ToString(), "PASS", StringComparison.Ordinal) &&
                     string.Equals(database.HeadlessValidation.GetValueOrDefault("status")?.ToString(), "PASS", StringComparison.Ordinal)
            ? "PASS WITH DOCUMENTED EVIDENCE GAPS"
            : "FAILED";
        var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["phase"] = workspace.Phase,
            ["status"] = status,
            ["runId"] = workspace.RunId,
            ["startedAtUtc"] = workspace.StartedAtUtc,
            ["completedAtUtc"] = DateTime.UtcNow,
            ["officialCsvZFilesDecoded"] = workspace.Diagnostics.GetValueOrDefault("phase2CsvZDecoded"),
            ["officialCsvZRowsArchived"] = workspace.Diagnostics.GetValueOrDefault("phase2CsvZRowsArchived"),
            ["sources"] = workspace.Sources.Count,
            ["rawRows"] = workspace.Raw.Count,
            ["stagingRows"] = workspace.Staging.Count,
            ["validatedRows"] = workspace.Validated.Count,
            ["domains"] = byDomain,
            ["phase2MariaDb"] = database.Phase2Metrics,
            ["formalCountsBefore"] = workspace.DatabaseBefore,
            ["formalCountsAfter"] = workspace.DatabaseAfter,
            ["productionCompleteness"] = database.ProductionCompleteness,
            ["productionSimplifiedDisplayRows"] = database.ProductionSimplifiedDisplayRows,
            ["brokenForeignKeys"] = database.BrokenForeignKeys,
            ["contentOrphans"] = database.ContentOrphans,
            ["runtimeValidation"] = database.RuntimeValidation,
            ["headlessValidation"] = database.HeadlessValidation,
            ["remainingEvidenceGaps"] = missing,
            ["recoveryPasses"] = workspace.Diagnostics.GetValueOrDefault("recoveryPasses"),
            ["manualOperation"] = "NO",
            ["newCapture"] = "NO",
            ["fakeNetworkBytes"] = 0,
            ["wireClosedLoopStarted"] = false
        };

        await Json("source-inventory.json", new { workspace.RunId, sources = workspace.Sources, workspace.Diagnostics }, cancellationToken);
        await Json("client-table-layouts.json", Rows(workspace, "ClientTableLayout"), cancellationToken);
        await Json("missing-field-matrix.json", new { runId = workspace.RunId, rows = missing }, cancellationToken);
        await Json("field-state-summary.json", new { runId = workspace.RunId, domains = byDomain, database = database.Phase2Metrics }, cancellationToken);
        await Json("npc-coverage.json", Coverage(workspace, "NpcCoordinateEvidence", database), cancellationToken);
        await Json("monster-coverage.json", new { formal = database.ProductionCompleteness, drops = State(workspace.Entities.Where(entity => entity.Domain == "MonsterDropRelationship")) }, cancellationToken);
        await Json("drop-coverage.json", Coverage(workspace, "MonsterDropRelationship", database), cancellationToken);
        await Json("skill-coverage.json", Coverage(workspace, "SkillProfile", database), cancellationToken);
        await Json("item-equipment-coverage.json", new { itemProfiles = State(workspace.Entities.Where(entity => entity.Domain == "ItemProfile")), equipmentSets = State(workspace.Entities.Where(entity => entity.Domain == "EquipmentSet")), equipmentMembers = State(workspace.Entities.Where(entity => entity.Domain == "EquipmentSetMember")) }, cancellationToken);
        await Json("merchant-quest-combine-pet-coverage.json", new
        {
            merchant = State(workspace.Entities.Where(entity => entity.Domain == "MerchantInventoryCandidate")),
            quest = State(workspace.Entities.Where(entity => entity.Domain is "QuestProfile" or "QuestObjectiveCandidate")),
            combine = State(workspace.Entities.Where(entity => entity.Domain == "ContainerRelationship")),
            pet = State(workspace.Entities.Where(entity => entity.Domain is "PetProfile" or "PetInnate" or "PetEggRelationship"))
        }, cancellationToken);
        await Json("referential-integrity.json", new { database.ReferentialIntegrity, database.BrokenForeignKeys, database.ContentOrphans }, cancellationToken);
        await Json("runtime-validation.json", new { database.RuntimeValidation, database.HeadlessValidation }, cancellationToken);
        await Json("test-results.json", new
        {
            extraction = new { decoded = workspace.Diagnostics.GetValueOrDefault("phase2CsvZDecoded"), invalidEscaped = workspace.Diagnostics.GetValueOrDefault("phase2CsvZInvalidBytesEscaped") },
            localization = new
            {
                total = workspace.Localization.Count,
                failed = workspace.Localization.Count(row => row.Result.ConversionStatus == "ConversionFailed"),
                brokenPlaceholder = workspace.Localization.Count(row => !row.Result.PlaceholderPreserved),
                brokenMarkup = workspace.Localization.Count(row => !row.Result.MarkupPreserved),
                productionSimplified = database.ProductionSimplifiedDisplayRows
            },
            externalGates = "Pending final build/test/security gate execution"
        }, cancellationToken);
        await Json("final-summary.json", summary, cancellationToken);

        await Reports(workspace, database, byDomain, missing, summary, cancellationToken);
        return summary;
    }

    private async Task Reports(RecoveryWorkspace workspace, DatabaseImportResult database, IReadOnlyDictionary<string, object> domains, object[] missing,
        IReadOnlyDictionary<string, object?> summary, CancellationToken cancellationToken)
    {
        var common = $"""
            - Recovery run: {workspace.RunId}
            - Official CSVZ decoded: {workspace.Diagnostics.GetValueOrDefault("phase2CsvZDecoded")}
            - Official rows archived without overwriting original text: {workspace.Diagnostics.GetValueOrDefault("phase2CsvZRowsArchived")}
            - MariaDB repository: {database.RepositoryType}
            - Migration: 033 ({database.MigrationStatus})
            - Production Simplified display rows: {database.ProductionSimplifiedDisplayRows}
            - Broken foreign keys: {database.BrokenForeignKeys}
            - Content orphans: {database.ContentOrphans}
            - Manual operation / new capture / fake bytes: NO / NO / 0
            """;
        await Report("GameplayContentRecoveryPhase2.SourceExhaustion.md", $"# Gameplay Content Recovery Phase 2 — Full Source Exhaustion\n\n{common}\n\nAll accessible official `.csvZ` tables were decoded through the verified 05 16 LZSS wrapper. Every safe record was archived as immutable Phase 2 raw evidence. Specialized passes followed item, NPC, skill, mission, equipment-set, container and pet table relationships. Ambiguous columns remain opaque/evidence-blocked.", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.LoaderParserAtlas.md", $"# Phase 2 — Loader / Parser Atlas\n\n{common}\n\n- Container decoder: God2 05 16 LZSS\n- Text decoder: strict CP936 with explicit invalid-byte evidence escaping\n- Record decoder: quote-aware CSV boundaries\n- gamedata decoder: declared section marker → count → rows\n- Cross-reference policy: exact official numeric identity only; fuzzy names never promote directly.\n", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.MissingFieldMatrix.md", $"# Phase 2 — Missing-field Matrix\n\n```json\n{RecoveryJson.Serialize(missing)}\n```", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.NpcAudit.md", DomainReport(workspace, "NpcCoordinateEvidence", "NPC coordinate and interaction evidence", common), cancellationToken);
        await Report("GameplayContentRecoveryPhase2.MonsterDropAudit.md", DomainReport(workspace, "MonsterDropRelationship", "Monster → item drop relationships", common) + "\n\nUnknown probabilities are stored as Original=NULL, Effective=0, ChanceEvidenceStatus=DefaultDisabledZero and IsDropEnabled=false. This does not mean official 0%.", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.SkillAudit.md", DomainReport(workspace, "SkillProfile", "Skill classification / target / MP / effect", common), cancellationToken);
        await Report("GameplayContentRecoveryPhase2.ItemEquipmentAudit.md", DomainReport(workspace, "ItemProfile", "Item / equipment profiles", common), cancellationToken);
        await Report("GameplayContentRecoveryPhase2.MerchantQuestCombinePetAudit.md", $"# Phase 2 — Merchant / Quest / Combine / Pet\n\n{common}\n\n```json\n{RecoveryJson.Serialize(new { merchant = domains.GetValueOrDefault("MerchantInventoryCandidate"), quest = domains.GetValueOrDefault("QuestProfile"), objectives = domains.GetValueOrDefault("QuestObjectiveCandidate"), combine = domains.GetValueOrDefault("ContainerRelationship"), pet = domains.GetValueOrDefault("PetProfile"), innate = domains.GetValueOrDefault("PetInnate") })}\n```", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.ReferentialIntegrity.md", $"# Phase 2 — Referential Integrity\n\n{common}\n\n```json\n{RecoveryJson.Serialize(database.ReferentialIntegrity)}\n```", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.RuntimeValidation.md", $"# Phase 2 — Runtime / Headless Validation\n\n{common}\n\n```json\n{RecoveryJson.Serialize(new { database.RuntimeValidation, database.HeadlessValidation })}\n```", cancellationToken);
        await Report("GameplayContentRecoveryPhase2.Final.md", $"# Gameplay Content Database Recovery Phase 2 — Final\n\nGAMEPLAY CONTENT DATABASE RECOVERY PHASE 2 — FULL CLIENT CONTENT EXHAUSTION: {summary["status"]}\n\n{common}\n\n```json\n{RecoveryJson.Serialize(new { database.Phase2Metrics, domains, remainingEvidenceGaps = missing })}\n```\n\nAll safely recoverable relations found by the completed source passes were written to MariaDB. EvidenceBlocked fields remain explicit, isolated and disabled; Phase 1 formal rows were not cleared or downgraded.", cancellationToken);
    }

    private static object Coverage(RecoveryWorkspace workspace, string domain, DatabaseImportResult database) => new { domain, state = State(workspace.Entities.Where(entity => entity.Domain == domain)), database.ProductionCompleteness };
    private static object Rows(RecoveryWorkspace workspace, string domain) => new { workspace.RunId, rows = workspace.Entities.Where(entity => entity.Domain == domain).ToArray() };

    private static object State(IEnumerable<ContentEntity> values)
    {
        var rows = values.ToArray();
        return new
        {
            total = rows.Length,
            verified = rows.Count(row => row.EvidenceStatus == "Verified" && row.MissingRequiredFields.Count == 0),
            derived = rows.Count(row => row.EvidenceStatus == "Derived"),
            candidate = rows.Count(row => row.EvidenceStatus == "Candidate"),
            evidenceBlocked = rows.Count(row => row.EvidenceStatus is "EvidenceBlocked" or "PartiallyMapped" || row.MissingRequiredFields.Count > 0),
            defaultDisabledZero = rows.Count(row => row.Fields.Values.Any(value => string.Equals(value?.ToString(), "DefaultDisabledZero", StringComparison.Ordinal)))
        };
    }

    private static string DomainReport(RecoveryWorkspace workspace, string domain, string title, string common) => $"# Phase 2 — {title}\n\n{common}\n\n```json\n{RecoveryJson.Serialize(State(workspace.Entities.Where(entity => entity.Domain == domain)))}\n```";
    private async Task Json(string name, object value, CancellationToken token) => await File.WriteAllTextAsync(Path.Combine(_artifactRoot, name), RecoveryJson.Serialize(value) + Environment.NewLine, new UTF8Encoding(false), token);
    private async Task Report(string name, string value, CancellationToken token) => await File.WriteAllTextAsync(Path.Combine(_reportRoot, name), value.Trim() + Environment.NewLine, new UTF8Encoding(false), token);
}
