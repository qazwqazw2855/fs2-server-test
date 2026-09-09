using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace God2.OfflineClientReverseEngineering;

public sealed record TestSuiteEvidence(
    string Name,
    string Status,
    int? Total,
    int? Passed,
    int? Failed,
    int? Skipped,
    DateTimeOffset? CompletedAtUtc,
    string? Artifact,
    string? ArtifactSha256);

public sealed record AdvancedVerificationEvidence(
    string Status,
    bool Current,
    int? Exhaustive,
    int? Property,
    int? DifferentialEvents,
    int? Schedules,
    int? Failures,
    string Artifact);

public sealed record MariaDbVerificationEvidence(
    string Status,
    bool Current,
    int? Cases,
    int? Rollbacks,
    int? Recovery,
    int? ExactlyOnce,
    int? Inconsistency,
    string Artifact);

public sealed record ResourceVerificationEvidence(
    string Status,
    bool Current,
    double? DurationSeconds,
    long? CompletedLoops,
    int? ActorLeaks,
    int? SessionLeaks,
    int? DbConnectionLeaks,
    string Artifact);

public sealed record SecurityVerificationEvidence(
    string Status,
    bool Current,
    int? CredentialFindings,
    int? HardcodedPathFindings,
    int? FakeNetworkBytes,
    string Artifact);

public sealed record RuntimeModeEvidence(
    string Status,
    string? BattleEngineMode,
    bool? ActorPrimaryEnabled,
    bool? LegacyPrimaryDefault,
    string Artifact);

public sealed record OfflineVerificationEvidence(
    DateTimeOffset LatestSourceUtc,
    string SourceFingerprintSha256,
    IReadOnlyDictionary<string, TestSuiteEvidence> Tests,
    AdvancedVerificationEvidence Advanced,
    MariaDbVerificationEvidence MariaDb,
    ResourceVerificationEvidence Resources,
    SecurityVerificationEvidence Security,
    RuntimeModeEvidence RuntimeMode)
{
    public bool AllCurrentTestsPass => Tests.Values.All(item =>
        item.Status == "PASS" && item.Failed == 0 && item.Skipped == 0);

    public static OfflineVerificationEvidence Load(string workspaceRoot)
    {
        var sourceFiles = EnumerateSourceFiles(workspaceRoot);
        if (sourceFiles.Length == 0)
        {
            throw new InvalidDataException(
                $"No verification source files were found under repository root '{Path.GetFullPath(workspaceRoot)}'. " +
                "Pass the God2 Classic Server repository root with --root.");
        }
        var latestSource = sourceFiles.Max(File.GetLastWriteTimeUtc);
        var sourceFingerprint = Fingerprint(workspaceRoot, sourceFiles);
        var verificationRoot = Path.Combine(workspaceRoot, "Artifacts", "OfflineClientReverseEngineering", "verification");
        var tests = new Dictionary<string, TestSuiteEvidence>(StringComparer.Ordinal)
        {
            ["Full"] = ReadTrx(workspaceRoot, Path.Combine(verificationRoot, "full.trx"), "Full", latestSource),
            ["Protocol"] = ReadTrx(workspaceRoot, Path.Combine(verificationRoot, "protocol.trx"), "Protocol", latestSource),
            ["Runtime"] = ReadTrx(workspaceRoot, Path.Combine(verificationRoot, "runtime.trx"), "Runtime", latestSource),
            ["Headless"] = ReadTrx(workspaceRoot, Path.Combine(verificationRoot, "headless.trx"), "Headless", latestSource)
        };

        return new OfflineVerificationEvidence(
            new DateTimeOffset(latestSource, TimeSpan.Zero),
            sourceFingerprint,
            tests,
            ReadAdvanced(workspaceRoot, latestSource),
            ReadMariaDb(workspaceRoot, latestSource),
            ReadResources(workspaceRoot, latestSource),
            ReadSecurity(workspaceRoot, latestSource),
            ReadRuntimeMode(workspaceRoot));
    }

    private static TestSuiteEvidence ReadTrx(string root, string path, string name, DateTime latestSource)
    {
        var files = File.Exists(path)
            ? [path]
            : Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)))
                ? Directory.EnumerateFiles(
                    Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)),
                    "*.trx",
                    SearchOption.AllDirectories).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        if (files.Length == 0) return new TestSuiteEvidence(name, "UNVERIFIED", null, null, null, null, null, null, null);
        try
        {
            files = files
                .Select(file => new { File = file, Identity = ReadTrxIdentity(file) })
                .GroupBy(item => item.Identity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => File.GetLastWriteTimeUtc(item.File))
                    .ThenByDescending(item => item.File, StringComparer.OrdinalIgnoreCase)
                    .First().File)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var total = 0;
            var passed = 0;
            var failed = 0;
            var skipped = 0;
            DateTimeOffset? completed = null;
            foreach (var file in files)
            {
                var document = LoadBoundedXml(file);
                var counters = document.Descendants().FirstOrDefault(item => item.Name.LocalName == "Counters");
                var times = document.Descendants().FirstOrDefault(item => item.Name.LocalName == "Times");
                if (counters is null) throw new InvalidDataException("TRX counters are missing.");
                total += Attribute(counters, "total");
                passed += Attribute(counters, "passed");
                var aggregateFailed = Attribute(counters, "failed");
                var detailedFailed = Attribute(counters, "error") + Attribute(counters, "timeout") + Attribute(counters, "aborted");
                failed += Math.Max(aggregateFailed, detailedFailed);
                skipped += Attribute(counters, "notExecuted") + Attribute(counters, "inconclusive") + Attribute(counters, "notRunnable");
                var finishTime = times?.Attribute("finish") is { } finish && DateTimeOffset.TryParse(finish.Value, out var parsed)
                    ? parsed.ToUniversalTime()
                    : new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                if (completed is null || finishTime > completed) completed = finishTime;
            }
            var current = files.All(file => File.GetLastWriteTimeUtc(file) >= latestSource);
            var status = !current ? "STALE" : total > 0 && failed == 0 && skipped == 0 && passed == total ? "PASS" : "FAIL";
            return new TestSuiteEvidence(
                name, status, total, passed, failed, skipped, completed,
                string.Join(';', files.Select(file => Relative(root, file))), HashSet(files));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or FormatException or IOException or UnauthorizedAccessException or OverflowException)
        {
            return new TestSuiteEvidence(name, "INVALID", null, null, null, null, null, string.Join(';', files.Select(file => Relative(root, file))), null);
        }
    }

    private static string ReadTrxIdentity(string path)
    {
        var document = LoadBoundedXml(path);
        var codeBases = document.Descendants()
            .Where(item => item.Name.LocalName == "TestMethod")
            .Select(item => item.Attribute("codeBase")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileName(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return codeBases.Length > 0
            ? string.Join(';', codeBases)
            : $"unknown:{Path.GetFileName(path)}";
    }

    private static RuntimeModeEvidence ReadRuntimeMode(string root)
    {
        var path = Path.Combine(root, "config", "server.json");
        if (!File.Exists(path)) return new("UNVERIFIED", null, null, null, Relative(root, path));
        try
        {
            using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 1024 * 1024, "Server configuration"));
            var json = document.RootElement;
            var mode = json.TryGetProperty("battleEngineMode", out var value) ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(mode)) return new("INVALID", null, null, null, Relative(root, path));
            var actor = mode is "ActorPrimary" or "ActorPrimaryWithLegacyFallbackDisabled";
            return new("CURRENT", mode, actor, mode == "LegacyPrimary", Relative(root, path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new("INVALID", null, null, null, Relative(root, path));
        }
    }

    private static AdvancedVerificationEvidence ReadAdvanced(string root, DateTime latestSource)
    {
        var path = Path.Combine(root, "Artifacts", "AdvancedHeadlessVerification", "fast-results.json");
        if (!File.Exists(path)) return new("UNVERIFIED", false, null, null, null, null, null, Relative(root, path));
        try
        {
            using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 64L * 1024 * 1024, "Advanced verification evidence"));
            var json = document.RootElement;
            var current = File.GetLastWriteTimeUtc(path) >= latestSource;
            var exhaustive = RequiredObject(json, "exhaustive");
            var property = RequiredObject(json, "property");
            var differential = RequiredObject(json, "differential");
            var concurrency = RequiredObject(json, "concurrency");
            var failures = GetInt(json, "unresolvedInvariantFailures") + GetInt(exhaustive, "failures") +
                GetInt(differential, "mismatchCount") + GetInt(concurrency, "failureCount");
            var artifactStatus = GetString(json, "status");
            return new(
                !current ? "STALE" : artifactStatus == "PASS" && failures == 0 ? "PASS" : "FAIL",
                current,
                GetInt(exhaustive, "caseCount"),
                GetInt(property, "generatedCases"),
                GetInt(differential, "eventCount"),
                GetInt(concurrency, "scheduleCount"),
                failures,
                Relative(root, path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new("INVALID", false, null, null, null, null, null, Relative(root, path));
        }
    }

    private static MariaDbVerificationEvidence ReadMariaDb(string root, DateTime latestSource)
    {
        var path = Path.Combine(root, "Artifacts", "AdvancedHeadlessVerification", "mariadb-results.json");
        if (!File.Exists(path)) return new("UNVERIFIED", false, null, null, null, null, null, Relative(root, path));
        try
        {
            using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 64L * 1024 * 1024, "MariaDB verification evidence"));
            var json = document.RootElement;
            var current = File.GetLastWriteTimeUtc(path) >= latestSource;
            var inconsistencies = GetInt(json, "dbInconsistencyCount") + GetInt(json, "duplicateRewardCount") +
                GetInt(json, "duplicateQuestCompletionCount") + GetInt(json, "dbConnectionLeakCount");
            return new(
                !current ? "STALE" : GetString(json, "status") == "PASS" && inconsistencies == 0 ? "PASS" : "FAIL",
                current,
                GetInt(json, "integrationCaseCount"),
                GetInt(json, "transactionRollbackCount"),
                GetInt(json, "recoveryCount"),
                GetInt(json, "exactlyOnceVerificationCount"),
                inconsistencies,
                Relative(root, path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new("INVALID", false, null, null, null, null, null, Relative(root, path));
        }
    }

    private static ResourceVerificationEvidence ReadResources(string root, DateTime latestSource)
    {
        var path = Path.Combine(root, "Artifacts", "AdvancedHeadlessVerification", "resource-samples.json");
        if (!File.Exists(path)) return new("UNVERIFIED", false, null, null, null, null, null, Relative(root, path));
        try
        {
            using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 256L * 1024 * 1024, "Resource verification evidence"));
            var json = document.RootElement;
            var current = File.GetLastWriteTimeUtc(path) >= latestSource;
            var actor = GetInt(json, "battleActorLeakCount");
            var session = GetInt(json, "sessionLeakCount");
            var db = GetInt(json, "dbConnectionLeakCount");
            return new(
                !current ? "STALE" : GetString(json, "status") == "PASS" && actor + session + db == 0 ? "PASS" : "FAIL",
                current,
                GetDouble(json, "durationSeconds"),
                GetLong(json, "completedGameplayLoops"),
                actor, session, db,
                Relative(root, path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new("INVALID", false, null, null, null, null, null, Relative(root, path));
        }
    }

    private static SecurityVerificationEvidence ReadSecurity(string root, DateTime latestSource)
    {
        var path = Path.Combine(root, "Artifacts", "ChineseLabeledCaptureImport", "Security", "security-summary.json");
        if (!File.Exists(path)) return new("UNVERIFIED", false, null, null, null, Relative(root, path));
        try
        {
            using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 16L * 1024 * 1024, "Security verification evidence"));
            var json = document.RootElement;
            var current = File.GetLastWriteTimeUtc(path) >= latestSource;
            var credential = RequiredInt(json, "credentialFindings");
            var hardcoded = RequiredInt(json, "hardcodedPathFindings");
            var fake = RequiredInt(json, "fakeNetworkBytes");
            return new(!current ? "STALE" : credential + hardcoded + fake == 0 ? "PASS" : "FAIL", current, credential, hardcoded, fake, Relative(root, path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new("INVALID", false, null, null, null, Relative(root, path));
        }
    }

    private static string[] EnumerateSourceFiles(string root) =>
        new[] { "src", "tests", "tools", "Automation" }
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".ps1")
            .Append(Path.Combine(root, "God2ClassicServer.sln"))
            .Append(Path.Combine(root, "config", "server.json"))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Fingerprint(string root, IEnumerable<string> paths)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths)
        {
            var relative = Relative(root, path);
            incremental.AppendData(Encoding.UTF8.GetBytes(relative));
            AppendBoundedFile(incremental, path, 128L * 1024 * 1024, "Source fingerprint input");
        }
        return Convert.ToHexString(incremental.GetHashAndReset());
    }

    private static int Attribute(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, out var value) ? value : 0;
    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static long GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static double GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : 0;
    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    private static JsonElement RequiredObject(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"Required verification object '{name}' is missing.");
    private static int RequiredInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidDataException($"Required verification integer '{name}' is missing.");
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string HashSet(IEnumerable<string> paths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
            AppendBoundedFile(hash, path, 128L * 1024 * 1024, "Verification artifact");
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static XDocument LoadBoundedXml(string path)
    {
        var bytes = BoundedFile.ReadAllBytes(path, 128L * 1024 * 1024, "TRX evidence");
        using var stream = new MemoryStream(bytes, writable: false);
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static void AppendBoundedFile(IncrementalHash hash, string path, long maximumBytes, string label)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 0 || info.Length > maximumBytes)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes:N0}-byte input budget.");
        }

        var buffer = new byte[64 * 1024];
        long total = 0;
        using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"{label} changed or exceeded the {maximumBytes:N0}-byte input budget.");
            }
            hash.AppendData(buffer.AsSpan(0, read));
        }
    }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
