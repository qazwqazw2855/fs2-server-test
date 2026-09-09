using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.AdvancedHeadlessVerification;

public sealed record RevalidationSecuritySummary(
    string SchemaVersion,
    string RunId,
    string BuildIdentityId,
    string SourceManifestHash,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    string Status,
    int ScannedFileCount,
    int ScannedTextFileCount,
    int BinaryMetadataFileCount,
    int RestrictedHashOnlyFileCount,
    int CredentialFindings,
    int PlaintextPasswordFindings,
    int ReusableTokenFindings,
    int SensitivePayloadFindings,
    int HardcodedAbsolutePathFindings,
    int HistoricalGeneratedPathMetadataCount,
    int HardcodedPassFindings,
    int StaleEvidenceAcceptedFindings,
    int BuildBindingMismatchFindings,
    int FakeNetworkBytes,
    int DpapiRemnantFindings,
    int LocalAccountFileCopyFindings,
    bool LocalAccountFileExists,
    bool LocalAccountFileExcludedFromSourceManifest,
    bool ImportedEvidenceProvenanceValid,
    int ImportedSessionCount,
    int ProvenanceArtifactCount,
    int ProvenanceMissingCount,
    int ProvenanceHashMismatchCount,
    bool OriginalZipAvailable,
    bool OriginalZipRequiredForRevalidation,
    bool RestrictedRawEvidenceCopiedToPublicReports,
    bool AssemblyHashesUnchanged,
    IReadOnlyDictionary<string, int> ScopeFileCounts,
    string BuildBinding);

public sealed record RevalidationSecurityFileInspection(
    int PlaintextPasswordFindings,
    int ReusableTokenFindings,
    int SensitivePayloadFindings,
    int DpapiRemnantFindings)
{
    public int CredentialFindings => PlaintextPasswordFindings + ReusableTokenFindings;
}

public sealed record RevalidationSecurityFinding(
    string RelativePath,
    string Category,
    int Count,
    bool Restricted);

public static partial class RevalidationSecurityScanner
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".ps1", ".md", ".json", ".jsonl", ".trx", ".xml", ".txt", ".log", ".sql", ".sln", ".config", ".env"
    };

    public static RevalidationSecuritySummary Run(string root, string runId)
    {
        var started = DateTimeOffset.UtcNow;
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        var identityVerification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var sourceManifest = RevalidationJson.Read<RevalidationSourceManifest>(Path.Combine(runRoot, "source-manifest.json"));
        var localAccount = Path.Combine(root, "config", "test-accounts.local.json");
        var sourceContainsLocalAccount = sourceManifest.Files.Any(value => value.RelativePath.EndsWith("test-accounts.local.json", StringComparison.OrdinalIgnoreCase));
        var publicLocalCopies = CountUnexpectedLocalAccountCopies(root, localAccount);

        var scopeRoots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src"] = Path.Combine(root, "src"),
            ["tests"] = Path.Combine(root, "tests"),
            ["tools"] = Path.Combine(root, "tools"),
            ["Automation"] = Path.Combine(root, "Automation"),
            ["Reports"] = Path.Combine(root, "Reports"),
            ["Artifacts"] = Path.Combine(root, "Artifacts"),
            ["protocol/evidence"] = Path.Combine(root, "protocol", "evidence"),
            ["Knowledge"] = Path.Combine(root, "Knowledge"),
            ["config"] = Path.Combine(root, "config")
        };
        var scopeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var scanned = 0;
        var textFiles = 0;
        var binaryMetadata = 0;
        var restrictedHashOnly = 0;
        var credential = 0;
        var plaintextPassword = 0;
        var reusableTokens = 0;
        var absolutePaths = 0;
        var historicalPaths = 0;
        var dpapi = 0;
        var sensitivePayload = 0;
        var findingDetails = new List<RevalidationSecurityFinding>();
        foreach (var scope in scopeRoots)
        {
            if (!Directory.Exists(scope.Value))
            {
                scopeCounts[scope.Key] = 0;
                continue;
            }
            var files = Directory.EnumerateFiles(scope.Value, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.EndsWith("test-accounts.local.json", StringComparison.OrdinalIgnoreCase)).ToArray();
            scopeCounts[scope.Key] = files.Length;
            foreach (var file in files)
            {
                scanned++;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var restricted = relative.Contains("/sensitive/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("Artifacts/ClientInstrumentation/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("Automation/State/", StringComparison.OrdinalIgnoreCase);
                var extension = Path.GetExtension(file);
                if (restricted)
                {
                    restrictedHashOnly++;
                    _ = RevalidationJson.Hash(file);
                    var restrictedInspection = InspectFile(file, scanSensitivePayload: false, scanNonSecretIdentifiers: false);
                    plaintextPassword += restrictedInspection.PlaintextPasswordFindings;
                    reusableTokens += restrictedInspection.ReusableTokenFindings;
                    dpapi += restrictedInspection.DpapiRemnantFindings;
                    AddFindingDetails(findingDetails, relative, restrictedInspection, restricted: true);
                    continue;
                }
                if (!TextExtensions.Contains(extension) || new FileInfo(file).Length > 16L * 1024 * 1024)
                {
                    binaryMetadata++;
                    _ = RevalidationJson.Hash(file);
                    continue;
                }
                textFiles++;
                string content;
                try { content = File.ReadAllText(file, Encoding.UTF8); }
                catch (DecoderFallbackException) { binaryMetadata++; continue; }
                var publicText = relative.StartsWith("Reports/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith($"Artifacts/AdvancedFinalFreezeRevalidation/{runId}/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("protocol/evidence/", StringComparison.OrdinalIgnoreCase);
                var textInspection = InspectContent(content, publicText,
                    ignoreUnquotedCodeExpressions: extension is ".cs" or ".ps1");
                plaintextPassword += textInspection.PlaintextPasswordFindings;
                reusableTokens += textInspection.ReusableTokenFindings;
                sensitivePayload += textInspection.SensitivePayloadFindings;
                dpapi += textInspection.DpapiRemnantFindings;
                AddFindingDetails(findingDetails, relative, textInspection, restricted: false);
                var pathCount = AbsolutePathRegex().Matches(content).Count;
                if (pathCount > 0)
                {
                    var historicalMetadata = (relative.StartsWith("Artifacts/", StringComparison.OrdinalIgnoreCase) &&
                            !relative.StartsWith($"Artifacts/AdvancedFinalFreezeRevalidation/{runId}/", StringComparison.OrdinalIgnoreCase)) ||
                        relative.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
                        relative.Contains("/Evidence/", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".trx", StringComparison.OrdinalIgnoreCase);
                    if (historicalMetadata) historicalPaths += pathCount;
                    else
                    {
                        absolutePaths += pathCount;
                        findingDetails.Add(new RevalidationSecurityFinding(relative, "HardcodedAbsolutePath", pathCount, false));
                    }
                }
            }
        }
        credential = plaintextPassword + reusableTokens;

        var restrictedPublicPaths = new[] { Path.Combine(root, "Reports"), Path.Combine(runRoot), Path.Combine(root, "protocol", "evidence") }
            .Where(Directory.Exists).SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".bin" or ".pcap" or ".cap" ||
                path.Contains($"{Path.DirectorySeparatorChar}sensitive{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        sensitivePayload += restrictedPublicPaths.Length;
        findingDetails.AddRange(restrictedPublicPaths.Select(path => new RevalidationSecurityFinding(
            Path.GetRelativePath(root, path).Replace('\\', '/'), "RestrictedRawEvidenceInPublicScope", 1, false)));
        var provenance = ValidateImportedProvenance(root);
        var originalZipAvailable = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ElevatedAutomationHost.zip"));
        var regressionPath = Path.Combine(runRoot, "regression-summary.json");
        using var regression = JsonDocument.Parse(File.ReadAllBytes(regressionPath));
        var fakeNetworkBytes = regression.RootElement.GetProperty("advancedFast").GetProperty("fakeNetworkBytes").GetInt32();
        var hardcodedPassDetails = DetectHardcodedPass(root, runRoot);
        var hardcodedPass = hardcodedPassDetails.Sum(value => value.Count);
        findingDetails.AddRange(hardcodedPassDetails);
        var prerequisite = ValidatePrerequisiteEvidence(runRoot, identity);
        var buildBindingPassed = prerequisite.BindingMismatchCount == 0 && identityVerification.AssemblyHashesUnchanged &&
            identityVerification.SourceHashMatches;
        var passed = credential == 0 && sensitivePayload == 0 && absolutePaths == 0 && hardcodedPass == 0 &&
            fakeNetworkBytes == 0 && dpapi == 0 && publicLocalCopies == 0 && !sourceContainsLocalAccount && provenance.Valid &&
            prerequisite.StaleCount == 0 && buildBindingPassed;
        var status = passed ? "PASS" : "FAIL";
        var summary = new RevalidationSecuritySummary(
            "advanced-final-freeze-security-v1", runId, identity.BuildIdentityId, identity.SourceManifestHash,
            started, DateTimeOffset.UtcNow, status, scanned, textFiles, binaryMetadata, restrictedHashOnly,
            credential, plaintextPassword, reusableTokens, sensitivePayload, absolutePaths, historicalPaths,
            hardcodedPass, prerequisite.StaleCount, prerequisite.BindingMismatchCount, fakeNetworkBytes, dpapi, publicLocalCopies, File.Exists(localAccount), !sourceContainsLocalAccount,
            provenance.Valid, provenance.SessionCount, provenance.ArtifactCount, provenance.MissingCount, provenance.HashMismatchCount,
            originalZipAvailable, false, restrictedPublicPaths.Length != 0, identityVerification.AssemblyHashesUnchanged, scopeCounts,
            buildBindingPassed ? "PASS" : "FAIL");
        RevalidationJson.Write(Path.Combine(runRoot, "Security", "scan-details.json"), new
        {
            summary.SchemaVersion,
            summary.RunId,
            summary.BuildIdentityId,
            summary.StartTimeUtc,
            summary.EndTimeUtc,
            summary.ScopeFileCounts,
            summary.ScannedFileCount,
            summary.ScannedTextFileCount,
            summary.BinaryMetadataFileCount,
            summary.RestrictedHashOnlyFileCount,
            Findings = findingDetails
                .OrderBy(value => value.RelativePath, StringComparer.Ordinal)
                .ThenBy(value => value.Category, StringComparer.Ordinal)
                .ToArray(),
            Policy = new
            {
                LocalSecretContentExported = false,
                RestrictedPayloadContentExported = false,
                HistoricalGeneratedAbsolutePathsAreMetadataNotCurrentHardcodedPaths = true
            }
        });
        RevalidationJson.Write(Path.Combine(runRoot, "security-scan-summary.json"), summary);
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        VerificationGuard.Require(status == "PASS", "Fresh credential and sensitive-data scan found a release-blocking condition.");
        return summary;
    }

    public static RevalidationSecurityFileInspection InspectFileForTesting(
        string path,
        bool scanSensitivePayload = true) => InspectFile(path, scanSensitivePayload, scanNonSecretIdentifiers: true);

    public static void ValidateGeneratedPublicReport(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var inspection = InspectContent(content, scanSensitivePayload: true);
        VerificationGuard.Require(inspection.CredentialFindings == 0 && inspection.SensitivePayloadFindings == 0 &&
            inspection.DpapiRemnantFindings == 0 && AbsolutePathRegex().Matches(content).Count == 0,
            "Generated public report contains credential, sensitive payload, DPAPI, or absolute-path material.");
    }

    public static string SanitizeDiagnostic(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sanitized = CredentialAssignmentRegex().Replace(value, match => $"{match.Groups[1].Value}=<redacted>");
        sanitized = SensitivePayloadRegex().Replace(sanitized, "<sensitive-payload>");
        sanitized = DpapiBlobRegex().Replace(sanitized, "<dpapi-blob>");
        return DiagnosticAbsolutePathRegex().Replace(sanitized, "<absolute-path>");
    }

    public static int CountUnexpectedLocalAccountCopies(string root, string approvedPath)
    {
        var approved = Path.GetFullPath(approvedPath);
        return Directory.EnumerateFiles(root, "test-accounts.local.json", SearchOption.AllDirectories)
            .Count(path => !string.Equals(Path.GetFullPath(path), approved, StringComparison.OrdinalIgnoreCase));
    }

    private static RevalidationSecurityFileInspection InspectFile(
        string path,
        bool scanSensitivePayload,
        bool scanNonSecretIdentifiers)
    {
        var info = new FileInfo(path);
        if (TextExtensions.Contains(info.Extension) && info.Length <= 16L * 1024 * 1024)
        {
            return InspectContent(File.ReadAllText(path, Encoding.UTF8), scanSensitivePayload,
                ignoreUnquotedCodeExpressions: info.Extension is ".cs" or ".ps1",
                scanNonSecretIdentifiers: scanNonSecretIdentifiers);
        }

        var passwords = 0;
        var tokens = 0;
        var payloads = 0;
        var dpapi = 0;
        using var stream = File.OpenRead(path);
        var buffer = new byte[64 * 1024];
        var carry = string.Empty;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var current = carry + Encoding.Latin1.GetString(buffer, 0, read);
            var inspection = InspectContent(current, scanSensitivePayload, ignoreUnquotedCodeExpressions: false,
                scanNonSecretIdentifiers: scanNonSecretIdentifiers, ignoreMatchesEndingAtOrBefore: carry.Length);
            passwords += inspection.PlaintextPasswordFindings;
            tokens += inspection.ReusableTokenFindings;
            payloads += inspection.SensitivePayloadFindings;
            dpapi += inspection.DpapiRemnantFindings;
            carry = current.Length <= 512 ? current : current[^512..];
        }
        return new RevalidationSecurityFileInspection(passwords, tokens, payloads, dpapi);
    }

    private static RevalidationSecurityFileInspection InspectContent(
        string content,
        bool scanSensitivePayload,
        bool ignoreUnquotedCodeExpressions = false,
        bool scanNonSecretIdentifiers = true,
        int ignoreMatchesEndingAtOrBefore = 0)
    {
        var passwords = 0;
        var tokens = 0;
        foreach (Match match in CredentialAssignmentRegex().Matches(content))
        {
            if (match.Index + match.Length <= ignoreMatchesEndingAtOrBefore) continue;
            var kind = match.Groups[1].Value;
            var value = match.Groups.Cast<Group>().Skip(2).First(group => group.Success).Value.Trim();
            var secretKind = IsSecretKind(kind);
            if (match.Groups[4].Success && ignoreUnquotedCodeExpressions) continue;
            if (!secretKind && !scanNonSecretIdentifiers) continue;
            if (IsUniversalPlaceholder(value) || (!secretKind && IsSafeProductIdentifier(value))) continue;
            if (kind.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("passwd", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("pwd", StringComparison.OrdinalIgnoreCase))
            {
                passwords++;
            }
            else
            {
                tokens++;
            }
        }
        return new RevalidationSecurityFileInspection(
            passwords,
            tokens,
            scanSensitivePayload ? SensitivePayloadRegex().Matches(content).Cast<Match>()
                .Count(match => match.Index + match.Length > ignoreMatchesEndingAtOrBefore) : 0,
            DpapiBlobRegex().Matches(content).Cast<Match>()
                .Count(match => match.Index + match.Length > ignoreMatchesEndingAtOrBefore));
    }

    private static void AddFindingDetails(
        ICollection<RevalidationSecurityFinding> details,
        string relative,
        RevalidationSecurityFileInspection inspection,
        bool restricted)
    {
        if (inspection.PlaintextPasswordFindings > 0)
            details.Add(new RevalidationSecurityFinding(relative, "PlaintextPassword", inspection.PlaintextPasswordFindings, restricted));
        if (inspection.ReusableTokenFindings > 0)
            details.Add(new RevalidationSecurityFinding(relative, "ReusableTokenOrIdentifier", inspection.ReusableTokenFindings, restricted));
        if (inspection.SensitivePayloadFindings > 0)
            details.Add(new RevalidationSecurityFinding(relative, "SensitivePayload", inspection.SensitivePayloadFindings, restricted));
        if (inspection.DpapiRemnantFindings > 0)
            details.Add(new RevalidationSecurityFinding(relative, "DpapiBlobInScannedContent", inspection.DpapiRemnantFindings, restricted));
    }

    private static IReadOnlyList<RevalidationSecurityFinding> DetectHardcodedPass(string root, string runRoot)
    {
        var findings = new List<RevalidationSecurityFinding>();
        var finalSummary = Path.Combine(runRoot, "final-summary.json");
        if (File.Exists(finalSummary))
            findings.Add(new RevalidationSecurityFinding(Path.GetRelativePath(root, finalSummary).Replace('\\', '/'),
                "PreexistingFinalSummary", 1, false));
        var candidates = new[]
        {
            Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "RevalidationExecution.cs"),
            Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "RevalidationFinalizer.cs"),
            Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "RevalidationSecurity.cs"),
            Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "MariaDbVerification.cs"),
            Path.Combine(root, "tools", "God2.AdvancedHeadlessVerification", "ResourceSoak.cs")
        };
        foreach (var path in candidates.Where(File.Exists))
        {
            var count = HardcodedResultAssignmentRegex().Matches(File.ReadAllText(path, Encoding.UTF8)).Count;
            if (count > 0)
                findings.Add(new RevalidationSecurityFinding(Path.GetRelativePath(root, path).Replace('\\', '/'),
                    "HardcodedResultAssignment", count, false));
        }
        return findings;
    }

    private static (int StaleCount, int BindingMismatchCount) ValidatePrerequisiteEvidence(
        string runRoot,
        RevalidationBuildIdentity identity)
    {
        var stale = 0;
        var mismatch = 0;
        var artifacts = new[]
        {
            "build-summary.json", "regression-summary.json", "code-review-remediation-summary.json",
            "mariadb-summary.json", "map-summary.json", "soak-summary.json"
        };
        foreach (var artifact in artifacts)
        {
            var path = Path.Combine(runRoot, artifact);
            if (!File.Exists(path))
            {
                stale++;
                mismatch++;
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                var json = document.RootElement;
                if (json.GetProperty("runId").GetString() != identity.RunId ||
                    json.GetProperty("buildIdentityId").GetString() != identity.BuildIdentityId ||
                    json.GetProperty("sourceManifestHash").GetString() != identity.SourceManifestHash)
                {
                    mismatch++;
                }
                var result = ReadResult(json);
                var start = ReadEvidenceTime(json, "startTimeUtc");
                var end = ReadEvidenceTime(json, "endTimeUtc");
                if (result != "PASS" || start == DateTimeOffset.MinValue || end < start ||
                    end > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    stale++;
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException or IOException)
            {
                _ = exception;
                stale++;
                mismatch++;
            }
        }
        return (stale, mismatch);
    }

    private static string ReadResult(JsonElement json)
    {
        if (json.TryGetProperty("status", out var status)) return status.GetString() ?? "FAIL";
        if (json.TryGetProperty("result", out var result) && result.TryGetProperty("status", out status))
        {
            return status.GetString() ?? "FAIL";
        }
        return "FAIL";
    }

    private static DateTimeOffset ReadEvidenceTime(JsonElement json, string name)
    {
        if (json.TryGetProperty(name, out var value) && value.TryGetDateTimeOffset(out var parsed)) return parsed.ToUniversalTime();
        if (json.TryGetProperty("result", out var result) && result.TryGetProperty(name, out value) && value.TryGetDateTimeOffset(out parsed)) return parsed.ToUniversalTime();
        if (json.TryGetProperty("debug", out var debug) && debug.TryGetProperty(name, out value) && value.TryGetDateTimeOffset(out parsed)) return parsed.ToUniversalTime();
        return DateTimeOffset.MinValue;
    }

    private static (bool Valid, int SessionCount, int ArtifactCount, int MissingCount, int HashMismatchCount) ValidateImportedProvenance(string root)
    {
        var manifestPath = Path.Combine(root, "Artifacts", "ChineseLabeledCaptureImport", "Inventory", "import-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var sessions = document.RootElement.GetProperty("sessions");
        var artifactCount = 0;
        var missing = 0;
        var mismatch = 0;
        var allowedRoot = Path.GetFullPath(Path.Combine(root, "Artifacts", "ClientInstrumentation", "ElevatedAutomationHost")) + Path.DirectorySeparatorChar;
        foreach (var session in sessions.EnumerateArray())
        {
            var sessionId = session.GetProperty("captureSessionId").GetString() ?? string.Empty;
            foreach (var property in session.GetProperty("hashes").EnumerateObject())
            {
                artifactCount++;
                var path = Path.GetFullPath(Path.Combine(allowedRoot, sessionId, property.Name.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { missing++; continue; }
                if (!string.Equals(RevalidationJson.Hash(path), property.Value.GetString(), StringComparison.OrdinalIgnoreCase)) mismatch++;
            }
        }
        var manifestValid = document.RootElement.GetProperty("sourceArchiveUnmodified").GetBoolean() &&
            document.RootElement.GetProperty("restrictedRawEvidence").GetBoolean() &&
            document.RootElement.GetProperty("importedSessionCount").GetInt32() == sessions.GetArrayLength();
        return (manifestValid && missing == 0 && mismatch == 0, sessions.GetArrayLength(), artifactCount, missing, mismatch);
    }

    private static bool IsSecretKind(string kind) => kind.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("passwd", StringComparison.OrdinalIgnoreCase) || kind.Equals("pwd", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("token", StringComparison.OrdinalIgnoreCase) || kind.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
        kind.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeProductIdentifier(string value) =>
        value.StartsWith("god2", StringComparison.OrdinalIgnoreCase) &&
        (value.EndsWith("test", StringComparison.OrdinalIgnoreCase) || value.EndsWith("_server", StringComparison.OrdinalIgnoreCase));

    private static bool IsUniversalPlaceholder(string value) => value.Equals("MASKED", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("REDACTED", StringComparison.OrdinalIgnoreCase) || value.Equals("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("EXAMPLE", StringComparison.OrdinalIgnoreCase) || value.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("NONE", StringComparison.OrdinalIgnoreCase) || value.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("fixture", StringComparison.OrdinalIgnoreCase) || value.Contains("example", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("sample", StringComparison.OrdinalIgnoreCase) || value.Contains("revalidation", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("demo", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith('<') || value.StartsWith('$') || value.StartsWith('%');

    [GeneratedRegex("(?i)(?<![A-Za-z0-9_])(password|passwd|pwd|token|cookie|session[_-]?secret|authorization|account|username|phone|mobile)\\s*[\\\"']?\\s*[:=]\\s*(?:\\\"([^\\\"\\r\\n]{4,})\\\"|'([^'\\r\\n]{4,})'|([^;,\\s\\r\\n]{4,}))")]
    private static partial Regex CredentialAssignmentRegex();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z]:[\\/]")]
    private static partial Regex AbsolutePathRegex();
    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_])[A-Z]:[\\/][^\r\n]*")]
    private static partial Regex DiagnosticAbsolutePathRegex();
    [GeneratedRegex(@"AQAAANCMnd8B[A-Za-z0-9+/=]{24,}")]
    private static partial Regex DpapiBlobRegex();
    [GeneratedRegex("(?i)(?:payload|packet|authorization|cookie|session(?:[_-]?data)?)\\s*[\\\"']?\\s*[:=]\\s*[\\\"']?(?:[A-F0-9]{96,}|[A-Za-z0-9+/]{128,}={0,2})")]
    private static partial Regex SensitivePayloadRegex();
    [GeneratedRegex("(?m)\\b(?:BuildResult|BuildBinding|Status|WarningCount|ErrorCount)\\s*=\\s*(?:\\\"PASS\\\"|0)\\s*[,;}]")]
    private static partial Regex HardcodedResultAssignmentRegex();
}
