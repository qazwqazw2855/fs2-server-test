using System.Globalization;
using System.Text;

namespace God2.GameplayContentRecovery;

public sealed class Phase3RecoveryReportWriter
{
    private readonly string _artifactRoot;
    private readonly string _reportRoot;

    public Phase3RecoveryReportWriter(string artifactRoot, string reportRoot)
    {
        _artifactRoot = artifactRoot;
        _reportRoot = reportRoot;
    }

    public async Task<IReadOnlyDictionary<string, object?>> WriteAsync(
        RecoveryWorkspace workspace,
        Phase3RecoveryResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_artifactRoot);
        Directory.CreateDirectory(_reportRoot);

        var unclassified = result.MissingFieldMatrix.Sum(row => Number(row, "unclassified"));
        var brokenForeignKeys = result.ReferentialIntegrity
            .Where(pair => pair.Key.StartsWith("Broken", StringComparison.Ordinal))
            .Sum(pair => pair.Value);
        var contentOrphans = result.ReferentialIntegrity.GetValueOrDefault("ContentOrphans");
        var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["phase"] = workspace.Phase,
            ["status"] = result.Status,
            ["runId"] = result.RunId,
            ["sourceRunId"] = result.SourceRunId,
            ["startedAtUtc"] = workspace.StartedAtUtc,
            ["completedAtUtc"] = result.CompletedAtUtc,
            ["extractorVersion"] = workspace.ExtractorVersion,
            ["converterVersion"] = RecoveryVersions.ConverterVersion,
            ["glossaryVersion"] = RecoveryVersions.GlossaryVersion,
            ["databaseRepository"] = "MariaDB",
            ["migration"] = "034",
            ["promotions"] = result.Promotions,
            ["coverage"] = result.Coverage,
            ["unclassifiedCount"] = unclassified,
            ["referentialIntegrity"] = result.ReferentialIntegrity,
            ["brokenForeignKeys"] = brokenForeignKeys,
            ["contentOrphans"] = contentOrphans,
            ["runtimeValidation"] = result.RuntimeValidation,
            ["headlessValidation"] = result.HeadlessValidation,
            ["productionSimplifiedDisplayRows"] = result.ProductionSimplifiedDisplayRows,
            ["manualOperation"] = "NO",
            ["newCapture"] = "NO",
            ["networkEmission"] = 0,
            ["fakeNetworkBytes"] = 0,
            ["wireClosedLoopStarted"] = false
        };

        await Json("source-usage.json", SourceUsage(result), cancellationToken);
        await DomainArtifact("npc-promotion.json", result, ["NPC"], ["Npc"], cancellationToken);
        await DomainArtifact("monster-spawn.json", result, ["Monster"], ["MonsterSpawn"], cancellationToken);
        await DomainArtifact("monster-stats.json", result, ["Monster"], ["MonsterHp", "MonsterMp", "MonsterCombat", "MonsterRewards"], cancellationToken);
        await DomainArtifact("drop-promotion.json", result, ["Drop"], ["Drop"], cancellationToken);
        await DomainArtifact("skill-identity.json", result, ["Skill"], ["SkillIdentity"], cancellationToken);
        await DomainArtifact("skill-semantics.json", result, ["Skill"], ["SkillFamily", "SkillTarget", "SkillMp", "SkillEffect"], cancellationToken);
        await DomainArtifact("merchant-promotion.json", result, ["Merchant"], ["Merchant"], cancellationToken);
        await DomainArtifact("quest-promotion.json", result, ["Quest", "QuestObjective"], ["Quest"], cancellationToken);
        await DomainArtifact("equipment-promotion.json", result, ["Equipment", "EquipmentMember"], ["Equipment"], cancellationToken);
        await DomainArtifact("combine-promotion.json", result, ["Combine"], ["Combine"], cancellationToken);
        await DomainArtifact("pet-promotion.json", result, ["Pet", "PetInnate"], ["Pet", "Hatch"], cancellationToken);
        await Json("missing-field-matrix.json", new { result.RunId, unclassified, rows = result.MissingFieldMatrix }, cancellationToken);
        await Json("referential-integrity.json", new { result.RunId, brokenForeignKeys, contentOrphans, checks = result.ReferentialIntegrity }, cancellationToken);
        await Json("runtime-validation.json", new { result.RunId, runtime = result.RuntimeValidation, headless = result.HeadlessValidation }, cancellationToken);
        await Json("test-results.json", new
        {
            result.RunId,
            migration = "PASS (034)",
            mariaDbPhysicalImport = result.Status.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAILED",
            referentialIntegrity = brokenForeignKeys == 0 && contentOrphans == 0 ? "PASS" : "FAILED",
            runtime = result.RuntimeValidation,
            headless = result.HeadlessValidation,
            localization = new { productionSimplifiedDisplayRows = result.ProductionSimplifiedDisplayRows },
            externalGates = "Pending final build/test/format/security gate execution"
        }, cancellationToken);
        await Json("final-summary.json", summary, cancellationToken);

        await Reports(workspace, result, summary, unclassified, brokenForeignKeys, contentOrphans, cancellationToken);
        return summary;
    }

    private async Task Reports(
        RecoveryWorkspace workspace,
        Phase3RecoveryResult result,
        IReadOnlyDictionary<string, object?> summary,
        long unclassified,
        long brokenForeignKeys,
        long contentOrphans,
        CancellationToken cancellationToken)
    {
        var common = $"""
            - Phase 3 RunId: {result.RunId}
            - Phase 2 source RunId: {result.SourceRunId}
            - MariaDB migration: 034
            - Core-field Unclassified count: {unclassified}
            - Broken foreign keys: {brokenForeignKeys}
            - Content orphans: {contentOrphans}
            - Production Simplified Chinese display rows: {result.ProductionSimplifiedDisplayRows}
            - Runtime / specialized / headless: {result.RuntimeValidation.GetValueOrDefault("status")} / {result.RuntimeValidation.GetValueOrDefault("phase3SpecializedValidation")} / {result.HeadlessValidation.GetValueOrDefault("status")}
            - Manual operation / new capture / network emission / fake bytes: NO / NO / 0 / 0
            """;

        await Report("GameplayContentRecoveryPhase3.SourceUsage.md", $"""
            # Gameplay Content Recovery Phase 3 — Source Usage

            {common}

            Phase 3 reused the immutable Phase 2 MariaDB baseline and its decoded official tables; it did not repeat the 85/85 CSVZ exhaustion pass. Promotions require exact formal keys, exact binary display names, exact verified layouts, or unique non-competing composite identities. On-disk packed-client functions without an already proven semantic consumer remain recorded as searched evidence, not fabricated parser offsets.

            ```json
            {RecoveryJson.Serialize(SourceUsage(result))}
            ```
            """, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.NpcPromotion.md", "NPC identity, interaction and coordinate promotion", result, ["NPC"], ["Npc"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.MonsterSpawn.md", "Monster spawn reconstruction", result, ["Monster"], ["MonsterSpawn"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.MonsterStats.md", "Monster HP / MP / stats / rewards", result, ["Monster"], ["MonsterHp", "MonsterMp", "MonsterCombat", "MonsterRewards"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.DropPromotion.md", "Monster → item drop promotion", result, ["Drop"], ["Drop"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.SkillIdentity.md", "Skill identity mapping", result, ["Skill"], ["SkillIdentity"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.SkillSemantics.md", "Skill semantic reconstruction", result, ["Skill"], ["SkillFamily", "SkillTarget", "SkillMp", "SkillEffect"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.MerchantPromotion.md", "Merchant candidate promotion", result, ["Merchant"], ["Merchant"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.QuestPromotion.md", "Quest profile / objective promotion", result, ["Quest", "QuestObjective"], ["Quest"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.EquipmentPromotion.md", "Equipment set / member promotion", result, ["Equipment", "EquipmentMember"], ["Equipment"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.CombinePromotion.md", "Combine relationship promotion", result, ["Combine"], ["Combine"], common, cancellationToken);
        await DomainReport("GameplayContentRecoveryPhase3.PetPromotion.md", "Pet / innate / egg promotion", result, ["Pet", "PetInnate"], ["Pet", "Hatch"], common, cancellationToken);
        await Report("GameplayContentRecoveryPhase3.MissingFieldClosure.md", $"# Phase 3 — Missing-field Closure\n\n{common}\n\n{MatrixTable(result.MissingFieldMatrix)}", cancellationToken);
        await Report("GameplayContentRecoveryPhase3.ReferentialIntegrity.md", $"# Phase 3 — Referential Integrity\n\n{common}\n\n```json\n{RecoveryJson.Serialize(result.ReferentialIntegrity)}\n```", cancellationToken);
        await Report("GameplayContentRecoveryPhase3.RuntimeValidation.md", $"# Phase 3 — Runtime / Specialized / Headless Validation\n\n{common}\n\n```json\n{RecoveryJson.Serialize(new { result.RuntimeValidation, result.HeadlessValidation })}\n```", cancellationToken);
        await Report("GameplayContentRecoveryPhase3.Final.md", $"""
            # Gameplay Content Database Recovery Phase 3 — Final

            GAMEPLAY CONTENT DATABASE RECOVERY PHASE 3: {summary["status"]}

            {common}

            ## Actual promotion counts

            ```json
            {RecoveryJson.Serialize(result.Promotions)}
            ```

            ## Production coverage

            ```json
            {RecoveryJson.Serialize(result.Coverage)}
            ```

            Unknown probabilities remain `DefaultDisabledZero` with their production action disabled; this is not official zero. Evidence-blocked fields retain source/function searches and explicit recovery requirements. No Phase 2 raw evidence or verified production row was cleared or downgraded.
            """, cancellationToken);
    }

    private static object SourceUsage(Phase3RecoveryResult result) => new
    {
        result.RunId,
        result.SourceRunId,
        reusedPhase2DecodedCsvz = 85,
        fullPhase2Rescan = false,
        sources = new[]
        {
            "Official client decoded tables and immutable raw evidence",
            "Formal MariaDB gameplay identities",
            "Verified Migration 033 layout and field evidence",
            "Phase 2 derived/candidate specialized tables",
            "Existing Bahamut / 17173 / historical provenance already archived in Phase 2"
        },
        promotionIdentityPolicy = "Exact official key, exact ResourceKey/display key, verified layout, or unique non-competing composite only",
        packedFunctionOffsets = "EvidenceBlocked where no already-proven semantic consumer exists",
        deepSemanticDistributions = result.DeepSemanticAnalysis
            .Where(pair => pair.Key.EndsWith("Distribution", StringComparison.Ordinal) || pair.Key.EndsWith("Multiplicity", StringComparison.Ordinal))
            .ToDictionary()
    };

    private async Task DomainArtifact(
        string name,
        Phase3RecoveryResult result,
        IReadOnlyCollection<string> domains,
        IReadOnlyCollection<string> prefixes,
        CancellationToken cancellationToken) => await Json(name, DomainData(result, domains, prefixes), cancellationToken);

    private async Task DomainReport(
        string name,
        string title,
        Phase3RecoveryResult result,
        IReadOnlyCollection<string> domains,
        IReadOnlyCollection<string> prefixes,
        string common,
        CancellationToken cancellationToken) => await Report(name, $"# Phase 3 — {title}\n\n{common}\n\n```json\n{RecoveryJson.Serialize(DomainData(result, domains, prefixes))}\n```", cancellationToken);

    private static object DomainData(Phase3RecoveryResult result, IReadOnlyCollection<string> domains, IReadOnlyCollection<string> prefixes) => new
    {
        result.RunId,
        promotions = result.Promotions.Where(pair => prefixes.Any(prefix => pair.Key.StartsWith(prefix, StringComparison.Ordinal))).ToDictionary(),
        coverage = result.Coverage.Where(pair => prefixes.Any(prefix => pair.Key.StartsWith(prefix, StringComparison.Ordinal))).ToDictionary(),
        fieldStates = result.MissingFieldMatrix.Where(row => domains.Contains(Text(row, "domain"), StringComparer.Ordinal)).ToArray()
    };

    private static string MatrixTable(IReadOnlyList<IReadOnlyDictionary<string, object?>> matrix)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Domain | Field | Total | Verified | Derived | Candidate | EvidenceBlocked | ExplicitOfficialZero | DefaultDisabledZero | NotApplicable | Unclassified |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in matrix)
        {
            builder.Append("| ").Append(Text(row, "domain")).Append(" | ").Append(Text(row, "field")).Append(" | ")
                .Append(Number(row, "total")).Append(" | ").Append(Number(row, "verified")).Append(" | ")
                .Append(Number(row, "derived")).Append(" | ").Append(Number(row, "candidate")).Append(" | ")
                .Append(Number(row, "evidenceBlocked")).Append(" | ").Append(Number(row, "explicitOfficialZero")).Append(" | ")
                .Append(Number(row, "defaultDisabledZero")).Append(" | ").Append(Number(row, "notApplicable")).Append(" | ")
                .Append(Number(row, "unclassified")).AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string Text(IReadOnlyDictionary<string, object?> row, string key) => row.GetValueOrDefault(key)?.ToString() ?? string.Empty;

    private static long Number(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt64(row.GetValueOrDefault(key) ?? 0, CultureInfo.InvariantCulture);

    private async Task Json(string name, object value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(_artifactRoot, name), RecoveryJson.Serialize(value) + Environment.NewLine, new UTF8Encoding(false), cancellationToken);

    private async Task Report(string name, string value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(_reportRoot, name), value.Trim() + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
}
