using System.Text;
using System.Text.Json;

namespace God2.AdvancedHeadlessVerification;

public sealed record RevalidationEvidenceEntry(
    string EvidenceId,
    string EvidenceType,
    string BuildIdentityId,
    string SourceManifestHash,
    string AssemblyHash,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    string ToolVersionHash,
    string ArtifactHash,
    string Result,
    string FreshnessStatus,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Supersedes,
    string? FailureReason,
    string Artifact);

public sealed record RevalidationEvidenceValidation(
    bool Passed,
    bool BuildIdentityMatches,
    bool SourceHashMatches,
    bool AssemblyHashesMatch,
    bool ArtifactHashesMatch,
    bool FreshnessManifestMatches,
    bool OldEvidenceMixed,
    string SecurityFreshness,
    IReadOnlyList<string> Blockers);

public static class RevalidationFinalizer
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedEvidenceArtifacts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build"] = "build-summary.json",
            ["regression"] = "regression-summary.json",
            ["code-review"] = "code-review-remediation-summary.json",
            ["mariadb"] = "mariadb-summary.json",
            ["map"] = "map-summary.json",
            ["soak"] = "soak-summary.json",
            ["security"] = "security-scan-summary.json"
        };

    private static readonly string[] SupportingReports =
    [
        "AdvancedFinalFreezeRevalidation.Build.md", "AdvancedFinalFreezeRevalidation.Regression.md",
        "AdvancedFinalFreezeRevalidation.MariaDb.md", "AdvancedFinalFreezeRevalidation.Map.md",
        "AdvancedFinalFreezeRevalidation.Soak.md", "AdvancedFinalFreezeRevalidation.Security.md",
        "AdvancedFinalFreezeRevalidation.Freshness.md", "AdvancedFinalFreezeRevalidation.Evidence.md",
        "AdvancedFinalFreezeRevalidation.Deferred.md"
    ];

    public static void BindEvidence(string root, string runId)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var definitions = new[]
        {
            Evidence(root, runRoot, identity, "build", "Build", "build-summary.json", identity.BuildOutputManifestHash,
                [], []),
            Evidence(root, runRoot, identity, "regression", "Regression", "regression-summary.json", Composite(identity.TestAssemblyHashes),
                ["build"], ["Artifacts/OfflineClientReverseEngineering/verification"]),
            Evidence(root, runRoot, identity, "code-review", "CodeReviewRemediation", "code-review-remediation-summary.json", Composite(identity.TestAssemblyHashes),
                ["build", "regression"], ["Reports/CodeReview.Remediation.Final.md"]),
            Evidence(root, runRoot, identity, "mariadb", "MariaDb", "mariadb-summary.json", Composite(identity.RuntimeAssemblyHashes),
                ["build", "regression"], ["Artifacts/AdvancedHeadlessVerification/mariadb-results.json"]),
            Evidence(root, runRoot, identity, "map", "MapMbdAStar", "map-summary.json", identity.OfflineReverseEngineeringToolHash,
                ["build"], ["Artifacts/OfflineClientReverseEngineering/map-collision-results.json"]),
            Evidence(root, runRoot, identity, "soak", "ResourceSoak", "soak-summary.json", Composite(identity.RuntimeAssemblyHashes),
                ["build", "regression", "mariadb"], ["Artifacts/AdvancedHeadlessVerification/resource-samples.json"]),
            Evidence(root, runRoot, identity, "security", "PacketCredentialSafety", "security-scan-summary.json", identity.AdvancedVerificationToolHash,
                ["build", "regression", "soak"], ["Artifacts/ChineseLabeledCaptureImport/Security/security-summary.json"])
        };
        var allFresh = definitions.Length == ExpectedEvidenceArtifacts.Count &&
            definitions.All(value => value.FreshnessStatus == "Fresh" && value.Result == "PASS" &&
                value.BuildIdentityId == identity.BuildIdentityId && value.SourceManifestHash == identity.SourceManifestHash &&
                ExpectedEvidenceArtifacts.TryGetValue(value.EvidenceId, out var artifact) && artifact == value.Artifact);
        RevalidationJson.Write(Path.Combine(runRoot, "evidence-manifest.json"), new
        {
            SchemaVersion = "advanced-final-freeze-evidence-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Evidence = definitions
        });
        RevalidationJson.Write(Path.Combine(runRoot, "evidence-freshness.json"), new
        {
            SchemaVersion = "advanced-final-freeze-freshness-v1",
            identity.RunId,
            identity.BuildIdentityId,
            Status = allFresh ? "PASS" : "FAIL",
            AllowedStatuses = new[] { "Fresh", "Stale", "Missing", "Mismatch", "Invalid" },
            FreshCount = definitions.Count(value => value.FreshnessStatus == "Fresh"),
            NonFreshCount = definitions.Count(value => value.FreshnessStatus != "Fresh"),
            Entries = definitions.Select(value => new { value.EvidenceId, value.FreshnessStatus, value.FailureReason })
        });
        VerificationGuard.Require(allFresh, "Not all Final Freeze evidence is fresh and passing.");
    }

    public static RevalidationEvidenceValidation ValidateEvidenceBindings(
        string runRoot,
        RevalidationBuildIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);
        ArgumentNullException.ThrowIfNull(identity);
        var blockers = new List<string>();
        var identityMatches = true;
        var sourceMatches = true;
        var assemblyMatches = true;
        var artifactHashesMatch = true;
        var freshnessMatches = true;
        var oldEvidenceMixed = false;
        var securityFreshness = "Missing";

        try
        {
            using var manifest = Load(Path.Combine(runRoot, "evidence-manifest.json"));
            var root = manifest.RootElement;
            identityMatches = root.GetProperty("runId").GetString() == identity.RunId &&
                root.GetProperty("buildIdentityId").GetString() == identity.BuildIdentityId;
            sourceMatches = root.GetProperty("sourceManifestHash").GetString() == identity.SourceManifestHash;
            Require(identityMatches, "EvidenceManifestIdentity");
            Require(sourceMatches, "EvidenceManifestSource");

            var entries = root.GetProperty("evidence").EnumerateArray()
                .Select(value => JsonSerializer.Deserialize<RevalidationEvidenceEntry>(value.GetRawText(), RevalidationJson.Options)
                    ?? throw new InvalidDataException("Evidence entry is invalid."))
                .ToArray();
            var duplicateIds = entries.GroupBy(value => value.EvidenceId, StringComparer.Ordinal)
                .Any(group => group.Count() != 1);
            oldEvidenceMixed = duplicateIds || entries.Length != ExpectedEvidenceArtifacts.Count ||
                entries.Any(value => !ExpectedEvidenceArtifacts.TryGetValue(value.EvidenceId, out var artifact) ||
                    !string.Equals(artifact, value.Artifact, StringComparison.Ordinal));
            Require(!oldEvidenceMixed, "EvidenceCatalog");

            foreach (var entry in entries)
            {
                if (!ExpectedEvidenceArtifacts.TryGetValue(entry.EvidenceId, out var expectedArtifact))
                {
                    continue;
                }
                var entryIdentityMatches = entry.BuildIdentityId == identity.BuildIdentityId;
                var entrySourceMatches = entry.SourceManifestHash == identity.SourceManifestHash;
                identityMatches &= entryIdentityMatches;
                sourceMatches &= entrySourceMatches;
                Require(entryIdentityMatches, $"EvidenceIdentity:{entry.EvidenceId}");
                Require(entrySourceMatches, $"EvidenceSource:{entry.EvidenceId}");
                Require(entry.Result == "PASS" && entry.FreshnessStatus == "Fresh" && entry.FailureReason is null,
                    $"EvidenceFreshness:{entry.EvidenceId}");
                Require(entry.StartTimeUtc != DateTimeOffset.MinValue && entry.EndTimeUtc >= entry.StartTimeUtc &&
                    entry.EndTimeUtc <= DateTimeOffset.UtcNow.AddMinutes(5), $"EvidenceTime:{entry.EvidenceId}");

                var expectedAssemblyHash = ExpectedAssemblyHash(identity, entry.EvidenceId);
                var entryAssemblyMatches = string.Equals(entry.AssemblyHash, expectedAssemblyHash, StringComparison.Ordinal);
                assemblyMatches &= entryAssemblyMatches;
                Require(entryAssemblyMatches, $"EvidenceAssembly:{entry.EvidenceId}");

                var artifactPath = Path.GetFullPath(Path.Combine(runRoot, expectedArtifact.Replace('/', Path.DirectorySeparatorChar)));
                var allowedRoot = Path.GetFullPath(runRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var hashMatches = artifactPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(artifactPath) &&
                    string.Equals(RevalidationJson.Hash(artifactPath), entry.ArtifactHash, StringComparison.OrdinalIgnoreCase);
                artifactHashesMatch &= hashMatches;
                Require(hashMatches, $"EvidenceArtifactHash:{entry.EvidenceId}");
                if (entry.EvidenceId == "security") securityFreshness = entry.FreshnessStatus;
            }

            using var freshness = Load(Path.Combine(runRoot, "evidence-freshness.json"));
            var freshRoot = freshness.RootElement;
            var freshEntries = freshRoot.GetProperty("entries").EnumerateArray().ToArray();
            freshnessMatches = freshRoot.GetProperty("runId").GetString() == identity.RunId &&
                freshRoot.GetProperty("buildIdentityId").GetString() == identity.BuildIdentityId &&
                freshRoot.GetProperty("status").GetString() == "PASS" &&
                freshRoot.GetProperty("freshCount").GetInt32() == ExpectedEvidenceArtifacts.Count &&
                freshRoot.GetProperty("nonFreshCount").GetInt32() == 0 &&
                freshEntries.Length == ExpectedEvidenceArtifacts.Count &&
                freshEntries.All(value => ExpectedEvidenceArtifacts.ContainsKey(value.GetProperty("evidenceId").GetString() ?? string.Empty) &&
                    value.GetProperty("freshnessStatus").GetString() == "Fresh");
            Require(freshnessMatches, "EvidenceFreshnessManifest");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or KeyNotFoundException)
        {
            blockers.Add($"EvidenceValidation:{exception.GetType().Name}");
            identityMatches = false;
            sourceMatches = false;
            assemblyMatches = false;
            artifactHashesMatch = false;
            freshnessMatches = false;
        }

        return new RevalidationEvidenceValidation(
            blockers.Count == 0,
            identityMatches,
            sourceMatches,
            assemblyMatches,
            artifactHashesMatch,
            freshnessMatches,
            oldEvidenceMixed,
            securityFreshness,
            blockers.Distinct(StringComparer.Ordinal).ToArray());

        void Require(bool condition, string blocker)
        {
            if (!condition) blockers.Add(blocker);
        }
    }

    public static AdvancedFinalFreezeGateResult Finalize(string root, string runId)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        var identityVerification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var build = Load(Path.Combine(runRoot, "build-summary.json"));
        var regression = Load(Path.Combine(runRoot, "regression-summary.json"));
        var maria = Load(Path.Combine(runRoot, "mariadb-summary.json"));
        var map = Load(Path.Combine(runRoot, "map-summary.json"));
        var soak = Load(Path.Combine(runRoot, "soak-summary.json"));
        var security = RevalidationJson.Read<RevalidationSecuritySummary>(Path.Combine(runRoot, "security-scan-summary.json"));
        var freshness = Load(Path.Combine(runRoot, "evidence-freshness.json"));
        var evidence = Load(Path.Combine(runRoot, "evidence-manifest.json"));
        var evidenceValidation = ValidateEvidenceBindings(runRoot, identity);
        WriteSupportingReports(root, runId, identity, build.RootElement, regression.RootElement, maria.RootElement,
            map.RootElement, soak.RootElement, security, freshness.RootElement, evidence.RootElement);
        var reportIntegrity = WriteReportIntegrity(root, runRoot, identity);

        var negativeResults = AdvancedFinalFreezeGate.RequiredNegativeScenarios().Select(scenario =>
        {
            var result = AdvancedFinalFreezeGate.Evaluate(scenario.Input);
            return new
            {
                scenario.Id,
                scenario.ExpectedFreezeReady,
                ActualFreezeReady = result.FreezeReady,
                Status = result.FreezeReady == scenario.ExpectedFreezeReady ? "PASS" : "FAIL",
                result.Blockers
            };
        }).ToArray();
        var negativePassed = negativeResults.All(value => value.Status == "PASS");
        RevalidationJson.Write(Path.Combine(runRoot, "negative-gate-results.json"), new
        {
            SchemaVersion = "advanced-final-freeze-negative-gates-v1",
            identity.RunId,
            identity.BuildIdentityId,
            Status = negativePassed ? "PASS" : "FAIL",
            CaseCount = negativeResults.Length,
            Results = negativeResults
        });

        var suites = regression.RootElement.GetProperty("suites").EnumerateArray().ToArray();
        var suite = suites.ToDictionary(value => value.GetProperty("name").GetString()!, StringComparer.Ordinal);
        var mariaResult = maria.RootElement.GetProperty("result");
        var soakResult = soak.RootElement.GetProperty("result");
        var soakFinal = soakResult.GetProperty("final");
        using var server = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "config", "server.json")));
        var mode = server.RootElement.GetProperty("battleEngineMode").GetString() ?? string.Empty;
        using var codeReview = Load(Path.Combine(runRoot, "code-review-remediation-summary.json"));
        var codeReviewRoot = codeReview.RootElement;
        var codeReviewPassed = codeReviewRoot.GetProperty("runId").GetString() == identity.RunId &&
            codeReviewRoot.GetProperty("buildIdentityId").GetString() == identity.BuildIdentityId &&
            codeReviewRoot.GetProperty("sourceManifestHash").GetString() == identity.SourceManifestHash &&
            codeReviewRoot.GetProperty("status").GetString() == "PASS";
        var trxPresent = suites.SelectMany(value => value.GetProperty("artifacts").EnumerateArray())
            .All(value => File.Exists(Path.Combine(runRoot, value.GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        var buildBinding = evidenceValidation.BuildIdentityMatches && evidenceValidation.SourceHashMatches &&
            evidenceValidation.AssemblyHashesMatch && evidenceValidation.ArtifactHashesMatch;
        var input = new AdvancedFinalFreezeGateInput(
            codeReviewPassed,
            build.RootElement.GetProperty("status").GetString() == "PASS",
            SuitePassed("Full"), SuitePassed("Protocol"), SuitePassed("Runtime"), SuitePassed("Headless"),
            regression.RootElement.GetProperty("advancedFast").GetProperty("status").GetString() == "PASS",
            mariaResult.GetProperty("status").GetString() == "PASS",
            map.RootElement.GetProperty("status").GetString() == "PASS" && map.RootElement.GetProperty("failedMapCount").GetInt32() == 0,
            soakResult.GetProperty("status").GetString() == "PASS",
            soakResult.GetProperty("durationSeconds").GetDouble(), security.Status == "PASS", evidenceValidation.SecurityFreshness,
            buildBinding, identityVerification.AssemblyHashesUnchanged, identityVerification.SourceHashMatches,
            evidenceValidation.Passed, reportIntegrity,
            suites.All(value => value.GetProperty("total").GetInt32() > 0), trxPresent,
            suites.All(value => value.GetProperty("status").GetString() == "PASS"),
            suites.All(value => value.GetProperty("uniqueAssemblyCount").GetInt32() == value.GetProperty("artifacts").GetArrayLength()),
            security.HardcodedPassFindings != 0, security.CredentialFindings, security.SensitivePayloadFindings,
            security.HardcodedAbsolutePathFindings, security.FakeNetworkBytes,
            soakFinal.GetProperty("queueDepth").GetInt32() + soakFinal.GetProperty("outboxBacklog").GetInt32() + soakFinal.GetProperty("journalBacklog").GetInt32(),
            soakFinal.GetProperty("keyedLockCount").GetInt32(), soakFinal.GetProperty("keyedLockReferenceCount").GetInt32(),
            soakResult.GetProperty("dbConnectionLeakCount").GetInt32(), evidenceValidation.OldEvidenceMixed, security.ImportedEvidenceProvenanceValid,
            security.OriginalZipAvailable, security.OriginalZipAvailable, security.OriginalZipRequiredForRevalidation,
            mode.StartsWith("ActorPrimary", StringComparison.Ordinal), mode == "LegacyPrimary", "NOT REQUIRED", "NOT REQUIRED");
        var gate = AdvancedFinalFreezeGate.Evaluate(input);
        if (!negativePassed)
        {
            gate = new AdvancedFinalFreezeGateResult(AdvancedFinalFreezeGate.BlockedStatus, false,
                gate.Blockers.Append("NegativeGateTests").Distinct(StringComparer.Ordinal).ToArray());
        }
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var finalSummary = CreateFinalSummary(identity, suite, build.RootElement, regression.RootElement, mariaResult, map.RootElement,
            soakResult, security, evidenceValidation, reportIntegrity, input, gate);
        RevalidationJson.Write(Path.Combine(runRoot, "final-summary.json"), finalSummary);
        WriteFinalReport(root, runId, finalSummary, gate);
        build.Dispose(); regression.Dispose(); maria.Dispose(); map.Dispose(); soak.Dispose(); freshness.Dispose(); evidence.Dispose();
        VerificationGuard.Require(gate.FreezeReady, $"Final Freeze remains blocked: {string.Join(',', gate.Blockers)}");
        return gate;

        bool SuitePassed(string name) => suite.TryGetValue(name, out var value) && value.GetProperty("status").GetString() == "PASS" &&
            value.GetProperty("failed").GetInt32() == 0 && value.GetProperty("skipped").GetInt32() == 0;
    }

    private static RevalidationEvidenceEntry Evidence(
        string root, string runRoot, RevalidationBuildIdentity identity, string id, string type, string artifact,
        string assemblyHash, IReadOnlyList<string> dependencies, IReadOnlyList<string> supersedes)
    {
        var path = Path.Combine(runRoot, artifact);
        if (!File.Exists(path))
        {
            return new RevalidationEvidenceEntry(id, type, identity.BuildIdentityId, identity.SourceManifestHash, assemblyHash,
                DateTimeOffset.MinValue, DateTimeOffset.MinValue, identity.AdvancedVerificationToolHash, string.Empty,
                "FAIL", "Missing", dependencies, supersedes, "Required artifact is missing.", artifact);
        }
        try
        {
            using var document = Load(path);
            var json = document.RootElement;
            var boundRun = json.GetProperty("runId").GetString();
            var boundIdentity = json.GetProperty("buildIdentityId").GetString();
            var boundSource = json.GetProperty("sourceManifestHash").GetString();
            var result = Result(json);
            var start = ReadTime(json, "startTimeUtc");
            var end = ReadTime(json, "endTimeUtc");
            var freshness = boundRun != identity.RunId || boundIdentity != identity.BuildIdentityId || boundSource != identity.SourceManifestHash ? "Mismatch" :
                result != "PASS" ? "Invalid" : end < start ? "Invalid" : "Fresh";
            return new RevalidationEvidenceEntry(id, type, boundIdentity ?? string.Empty, boundSource ?? string.Empty, assemblyHash,
                start, end, identity.AdvancedVerificationToolHash, RevalidationJson.Hash(path), result, freshness,
                dependencies, supersedes, freshness == "Fresh" ? null : "Evidence binding, result, or time interval is invalid.", artifact);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or KeyNotFoundException)
        {
            return new RevalidationEvidenceEntry(id, type, identity.BuildIdentityId, identity.SourceManifestHash, assemblyHash,
                DateTimeOffset.MinValue, DateTimeOffset.MinValue, identity.AdvancedVerificationToolHash, RevalidationJson.Hash(path),
                "FAIL", "Invalid", dependencies, supersedes, exception.GetType().Name, artifact);
        }
    }

    private static string Result(JsonElement json)
    {
        if (json.TryGetProperty("status", out var status)) return status.GetString() ?? "FAIL";
        if (json.TryGetProperty("result", out var result) && result.TryGetProperty("status", out status)) return status.GetString() ?? "FAIL";
        return "FAIL";
    }

    private static DateTimeOffset ReadTime(JsonElement json, string name)
    {
        if (json.TryGetProperty(name, out var value) && value.TryGetDateTimeOffset(out var parsed)) return parsed.ToUniversalTime();
        if (json.TryGetProperty("result", out var result) && result.TryGetProperty(name, out value) && value.TryGetDateTimeOffset(out parsed)) return parsed.ToUniversalTime();
        if (json.TryGetProperty("debug", out var debug) && debug.TryGetProperty(name, out value) && value.TryGetDateTimeOffset(out parsed)) return parsed.ToUniversalTime();
        if (json.TryGetProperty("release", out var release) && release.TryGetProperty(name, out value) && value.TryGetDateTimeOffset(out parsed)) return parsed.ToUniversalTime();
        throw new InvalidDataException($"Evidence timestamp '{name}' is missing.");
    }

    private static string Composite(IReadOnlyDictionary<string, string> values) => RevalidationJson.HashText(
        string.Join('\n', values.OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => $"{value.Key}|{value.Value}")));

    private static string ExpectedAssemblyHash(RevalidationBuildIdentity identity, string evidenceId) => evidenceId switch
    {
        "build" => identity.BuildOutputManifestHash,
        "regression" or "code-review" => Composite(identity.TestAssemblyHashes),
        "mariadb" or "soak" => Composite(identity.RuntimeAssemblyHashes),
        "map" => identity.OfflineReverseEngineeringToolHash,
        "security" => identity.AdvancedVerificationToolHash,
        _ => string.Empty
    };

    private static JsonDocument Load(string path) => JsonDocument.Parse(File.ReadAllBytes(path));

    private static void WriteSupportingReports(
        string root, string runId, RevalidationBuildIdentity identity, JsonElement build, JsonElement regression,
        JsonElement maria, JsonElement map, JsonElement soak, RevalidationSecuritySummary security,
        JsonElement freshness, JsonElement evidence)
    {
        var reports = Path.Combine(root, "Reports");
        Directory.CreateDirectory(reports);
        var suites = regression.GetProperty("suites").EnumerateArray().ToArray();
        WriteReport(reports, SupportingReports[0], $"""
# Advanced Final Freeze Revalidation — Build

RunId: `{runId}`  
BuildIdentityId: `{identity.BuildIdentityId}`

- Status: {build.GetProperty("status").GetString()}
- Debug: {build.GetProperty("debug").GetProperty("status").GetString()}
- Release: {build.GetProperty("release").GetProperty("status").GetString()}
- Warnings: {build.GetProperty("warningCount").GetInt32()}
- Errors: {build.GetProperty("errorCount").GetInt32()}
- Source manifest: `{identity.SourceManifestHash}`
- Build output manifest: `{identity.BuildOutputManifestHash}`
""");
        WriteReport(reports, SupportingReports[1], $"""
# Advanced Final Freeze Revalidation — Regression

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

{string.Join(Environment.NewLine, suites.Select(value => $"- {value.GetProperty("name").GetString()}: {value.GetProperty("passed").GetInt32()}/{value.GetProperty("total").GetInt32()} {value.GetProperty("status").GetString()}; failed={value.GetProperty("failed").GetInt32()}; skipped={value.GetProperty("skipped").GetInt32()}"))}
- Advanced Fast: {regression.GetProperty("advancedFast").GetProperty("status").GetString()}
- Formatter: {regression.GetProperty("formatter").GetProperty("status").GetString()}
- Configuration JSON: {regression.GetProperty("configurationJson").GetProperty("status").GetString()}
- Fake Network Bytes: {regression.GetProperty("advancedFast").GetProperty("fakeNetworkBytes").GetInt32()}
""");
        var mariaResult = maria.GetProperty("result");
        WriteReport(reports, SupportingReports[2], $"""
# Advanced Final Freeze Revalidation — MariaDB

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

- Status: {mariaResult.GetProperty("status").GetString()}
- Integration cases: {mariaResult.GetProperty("integrationCaseCount").GetInt32()}
- Exactly-once: {mariaResult.GetProperty("exactlyOnceVerificationCount").GetInt32()}
- Rollbacks: {mariaResult.GetProperty("transactionRollbackCount").GetInt32()}
- Recovery: {mariaResult.GetProperty("recoveryCount").GetInt32()}
""");
        WriteReport(reports, SupportingReports[3], $"""
# Advanced Final Freeze Revalidation — Map

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

- Status: {map.GetProperty("status").GetString()}
- MBD parsed: {map.GetProperty("parsedMapCount").GetInt32()}/{map.GetProperty("candidateFileCount").GetInt32()}
- A*: {map.GetProperty("aStarPassedCount").GetInt32()}/{map.GetProperty("parsedMapCount").GetInt32()}
- Failures: {map.GetProperty("failedMapCount").GetInt32()}
- Movement protocol modified: {map.GetProperty("movementProtocolModified").GetBoolean()}
""");
        var soakResult = soak.GetProperty("result");
        var soakFinal = soakResult.GetProperty("final");
        WriteReport(reports, SupportingReports[4], $"""
# Advanced Final Freeze Revalidation — Resource Soak

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

- Status: {soakResult.GetProperty("status").GetString()}
- Start: {soakResult.GetProperty("startTimeUtc").GetDateTimeOffset():O}
- End: {soakResult.GetProperty("endTimeUtc").GetDateTimeOffset():O}
- Actual duration: {soakResult.GetProperty("durationSeconds").GetDouble():F3} seconds
- Gameplay loops: {soakResult.GetProperty("completedGameplayLoops").GetInt64()}
- Working set trend: {soakResult.GetProperty("workingSetTrend").GetString()}
- Heap / LOH: {soakResult.GetProperty("heapTrend").GetString()} / {soakResult.GetProperty("lohTrend").GetString()}
- Threads / handles: {soakResult.GetProperty("threadTrend").GetString()} / {soakResult.GetProperty("handleTrend").GetString()}
- Keyed locks final: {soakFinal.GetProperty("keyedLockCount").GetInt32()} / refs {soakFinal.GetProperty("keyedLockReferenceCount").GetInt32()}
- Queue / outbox / journal final: {soakFinal.GetProperty("queueDepth").GetInt32()} / {soakFinal.GetProperty("outboxBacklog").GetInt32()} / {soakFinal.GetProperty("journalBacklog").GetInt32()}
""");
        WriteReport(reports, SupportingReports[5], $"""
# Advanced Final Freeze Revalidation — Security

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

- Status: {security.Status}
- Credential findings: {security.CredentialFindings}
- Sensitive payload findings: {security.SensitivePayloadFindings}
- Hardcoded absolute path findings: {security.HardcodedAbsolutePathFindings}
- Hardcoded PASS findings: {security.HardcodedPassFindings}
- Fake Network Bytes: {security.FakeNetworkBytes}
- Imported provenance: {(security.ImportedEvidenceProvenanceValid ? "PASS" : "FAIL")}
- Original ZIP available / required: {security.OriginalZipAvailable} / {security.OriginalZipRequiredForRevalidation}
""");
        WriteReport(reports, SupportingReports[6], $"""
# Advanced Final Freeze Revalidation — Freshness

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

- Status: {freshness.GetProperty("status").GetString()}
- Fresh: {freshness.GetProperty("freshCount").GetInt32()}
- Non-fresh: {freshness.GetProperty("nonFreshCount").GetInt32()}
- Allowed states: Fresh, Stale, Missing, Mismatch, Invalid. Only Fresh supports the gate.
""");
        WriteReport(reports, SupportingReports[7], $"""
# Advanced Final Freeze Revalidation — Evidence

RunId: `{runId}`; BuildIdentityId: `{identity.BuildIdentityId}`

The evidence manifest contains {evidence.GetProperty("evidence").GetArrayLength()} hash-bound entries. Every entry records source, assembly, tool, artifact, time interval, dependencies, superseded evidence, result, and freshness.
""");
        WriteReport(reports, SupportingReports[8], $"""
# Advanced Final Freeze Revalidation — Deferred

RunId: `{runId}`

- User manual gameplay: NOT REQUIRED
- New manual capture: NOT REQUIRED
- Original ZIP re-upload: NOT REQUIRED
- Existing protocol promotion gaps remain evidence-gated and were not silently promoted by this freeze.
""");
    }

    private static bool WriteReportIntegrity(string root, string runRoot, RevalidationBuildIdentity identity)
    {
        var reportEntries = SupportingReports.Select(name => RevalidationJson.Describe(root, Path.Combine(root, "Reports", name))).ToArray();
        var result = new
        {
            SchemaVersion = "advanced-final-freeze-report-integrity-v1",
            identity.RunId,
            identity.BuildIdentityId,
            Status = reportEntries.Length == SupportingReports.Length ? "PASS" : "FAIL",
            Reports = reportEntries,
            ManifestHash = RevalidationJson.CompositeHash(reportEntries)
        };
        RevalidationJson.Write(Path.Combine(runRoot, "report-integrity.json"), result);
        return result.Status == "PASS" && reportEntries.All(value => RevalidationJson.Hash(Path.Combine(root, value.RelativePath.Replace('/', Path.DirectorySeparatorChar))) == value.Sha256);
    }

    private static object CreateFinalSummary(
        RevalidationBuildIdentity identity, IReadOnlyDictionary<string, JsonElement> suite, JsonElement build, JsonElement regression,
        JsonElement maria, JsonElement map, JsonElement soak, RevalidationSecuritySummary security,
        RevalidationEvidenceValidation evidenceValidation, bool reportIntegrity, AdvancedFinalFreezeGateInput input,
        AdvancedFinalFreezeGateResult gate)
    {
        var final = soak.GetProperty("final");
        return new
        {
            SchemaVersion = "advanced-final-freeze-final-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            BuildOutputHash = identity.BuildOutputManifestHash,
            BuildResult = build.GetProperty("status").GetString(),
            FullTests = Count("Full"),
            ProtocolTests = Count("Protocol"),
            RuntimeTests = Count("Runtime"),
            HeadlessTests = Count("Headless"),
            AdvancedFast = regression.GetProperty("advancedFast"),
            MariaDbCases = maria.GetProperty("integrationCaseCount").GetInt32(),
            MbdParseCount = map.GetProperty("parsedMapCount").GetInt32(),
            AStarCount = map.GetProperty("aStarPassedCount").GetInt32(),
            SoakStartUtc = soak.GetProperty("startTimeUtc").GetDateTimeOffset(),
            SoakEndUtc = soak.GetProperty("endTimeUtc").GetDateTimeOffset(),
            SoakActualDurationSeconds = soak.GetProperty("durationSeconds").GetDouble(),
            GameplayLoopCount = soak.GetProperty("completedGameplayLoops").GetInt64(),
            PeakWorkingSetBytes = soak.GetProperty("peak").GetProperty("workingSetBytes").GetInt64(),
            FinalWorkingSetBytes = final.GetProperty("workingSetBytes").GetInt64(),
            HeapTrend = soak.GetProperty("heapTrend").GetString(),
            ThreadTrend = soak.GetProperty("threadTrend").GetString(),
            HandleTrend = soak.GetProperty("handleTrend").GetString(),
            KeyedLockTrend = soak.GetProperty("keyedLockTrend").GetString(),
            QueueOutboxJournalFinalCount = final.GetProperty("queueDepth").GetInt32() + final.GetProperty("outboxBacklog").GetInt32() + final.GetProperty("journalBacklog").GetInt32(),
            ActorLeakCount = soak.GetProperty("battleActorLeakCount").GetInt32(),
            SessionLeakCount = soak.GetProperty("sessionLeakCount").GetInt32(),
            DbConnectionLeakCount = soak.GetProperty("dbConnectionLeakCount").GetInt32(),
            DuplicateRewardCount = soak.GetProperty("duplicateRewardCount").GetInt32(),
            DuplicateQuestCompletionCount = soak.GetProperty("duplicateQuestCompletionCount").GetInt32(),
            InventoryDriftCount = soak.GetProperty("inventoryDriftCount").GetInt32(),
            EquipmentDriftCount = soak.GetProperty("equipmentDriftCount").GetInt32(),
            PartyDriftCount = soak.GetProperty("partyDriftCount").GetInt32(),
            MountPetDriftCount = soak.GetProperty("mountPetDriftCount").GetInt32(),
            security.CredentialFindings,
            security.SensitivePayloadFindings,
            security.HardcodedAbsolutePathFindings,
            security.HardcodedPassFindings,
            security.FakeNetworkBytes,
            EvidenceFreshness = evidenceValidation.Passed ? "PASS" : "FAIL",
            BuildBinding = input.BuildIdentityMatches && input.AssemblyHashesUnchanged && input.SourceHashMatches ? "PASS" : "FAIL",
            ReportIntegrity = reportIntegrity ? "PASS" : "FAIL",
            OriginalZipAvailability = security.OriginalZipAvailable,
            OriginalZipRequired = security.OriginalZipRequiredForRevalidation,
            ActorPrimary = input.ActorPrimaryEnabled ? "ENABLED" : "NOT ENABLED",
            LegacyPrimary = input.LegacyPrimaryDefault ? "DEFAULT" : "NOT DEFAULT",
            input.UserManualGameplay,
            input.NewManualCapture,
            gate.FinalStatus,
            RemainingBlocker = gate.Blockers
        };

        object Count(string name)
        {
            var value = suite[name];
            return new { Total = value.GetProperty("total").GetInt32(), Passed = value.GetProperty("passed").GetInt32(), Failed = value.GetProperty("failed").GetInt32(), Skipped = value.GetProperty("skipped").GetInt32() };
        }
    }

    private static void WriteFinalReport(string root, string runId, object summary, AdvancedFinalFreezeGateResult gate)
    {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(summary, RevalidationJson.Options));
        var json = document.RootElement;
        WriteReport(Path.Combine(root, "Reports"), "AdvancedFinalFreezeRevalidation.Final.md", $"""
# Advanced Final Freeze Fresh Evidence Revalidation

RunId: `{runId}`  
BuildIdentityId: `{json.GetProperty("buildIdentityId").GetString()}`

- Build: {json.GetProperty("buildResult").GetString()}
- Full / Protocol / Runtime / Headless: {Tests("fullTests")} / {Tests("protocolTests")} / {Tests("runtimeTests")} / {Tests("headlessTests")}
- MariaDB: {json.GetProperty("mariaDbCases").GetInt32()} cases
- MBD / A*: {json.GetProperty("mbdParseCount").GetInt32()} / {json.GetProperty("aStarCount").GetInt32()}
- Soak: {json.GetProperty("soakActualDurationSeconds").GetDouble():F3} seconds; loops={json.GetProperty("gameplayLoopCount").GetInt64()}
- Security findings: credentials={json.GetProperty("credentialFindings").GetInt32()}, payload={json.GetProperty("sensitivePayloadFindings").GetInt32()}, paths={json.GetProperty("hardcodedAbsolutePathFindings").GetInt32()}, hardcoded PASS={json.GetProperty("hardcodedPassFindings").GetInt32()}
- Evidence freshness / binding / report integrity: {json.GetProperty("evidenceFreshness").GetString()} / {json.GetProperty("buildBinding").GetString()} / {json.GetProperty("reportIntegrity").GetString()}
- Original ZIP available / required: {json.GetProperty("originalZipAvailability").GetBoolean()} / {json.GetProperty("originalZipRequired").GetBoolean()}
- ActorPrimary: {json.GetProperty("actorPrimary").GetString()}; LegacyPrimary: {json.GetProperty("legacyPrimary").GetString()}
- User Manual Gameplay: {json.GetProperty("userManualGameplay").GetString()}
- New Manual Capture: {json.GetProperty("newManualCapture").GetString()}
- Original ZIP Re-upload: {(json.GetProperty("originalZipRequired").GetBoolean() ? "REQUIRED" : "NOT REQUIRED")}

Final Status: **{gate.FinalStatus}**

Remaining blocker: {(gate.Blockers.Count == 0 ? "None" : string.Join(", ", gate.Blockers))}
""");

        string Tests(string name)
        {
            var value = json.GetProperty(name);
            return $"{value.GetProperty("passed").GetInt32()}/{value.GetProperty("total").GetInt32()}";
        }
    }

    private static void WriteReport(string reports, string name, string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        RevalidationSecurityScanner.ValidateGeneratedPublicReport(normalized);
        File.WriteAllText(Path.Combine(reports, name), normalized, new UTF8Encoding(false));
    }
}
