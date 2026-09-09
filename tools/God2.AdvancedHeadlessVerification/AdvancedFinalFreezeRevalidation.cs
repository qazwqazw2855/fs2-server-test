using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using God2.OfflineClientReverseEngineering;

namespace God2.AdvancedHeadlessVerification;

public sealed record RevalidationFileHash(string RelativePath, long Length, string Sha256);

public sealed record RevalidationSourceManifest(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Solution,
    IReadOnlyList<RevalidationFileHash> Files,
    string SourceManifestHash,
    string ProjectManifestHash);

public sealed record RevalidationBuildIdentity(
    string SchemaVersion,
    string RunId,
    DateTimeOffset GeneratedAtUtc,
    string BuildIdentityId,
    string Solution,
    IReadOnlyList<string> Configurations,
    IReadOnlyList<string> TargetFrameworks,
    string SourceManifestHash,
    string ProjectManifestHash,
    string BuildOutputManifestHash,
    IReadOnlyDictionary<string, string> CriticalAssemblyHashes,
    IReadOnlyDictionary<string, string> TestAssemblyHashes,
    IReadOnlyDictionary<string, string> RuntimeAssemblyHashes,
    IReadOnlyDictionary<string, string> ProtocolAssemblyHashes,
    IReadOnlyDictionary<string, string> HeadlessAssemblyHashes,
    string OfflineReverseEngineeringToolHash,
    string AdvancedVerificationToolHash,
    string DatabaseMigrationHead,
    string DatabaseMigrationHeadHash,
    string ClientBuildSafeReference,
    string ClientBuildSha256,
    IReadOnlyDictionary<string, string> EvidenceSchemaVersions,
    IReadOnlyList<RevalidationFileHash> BuildOutputs);

public sealed record RevalidationIdentityVerification(
    bool SourceHashMatches,
    bool AssemblyHashesUnchanged,
    int SourceFileCount,
    int AssemblyFileCount);

public sealed record RevalidationTestSuite(
    string Name,
    string Status,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    int UniqueAssemblyCount,
    int DuplicateTrxCount,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    string ArtifactHash,
    IReadOnlyList<string> Artifacts,
    IReadOnlyDictionary<string, string> AssemblyHashes);

public static class RevalidationPaths
{
    public static string RunRoot(string repositoryRoot, string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("RunId may contain only letters, digits, hyphens, and underscores.", nameof(runId));
        }
        return Path.Combine(repositoryRoot, "Artifacts", "AdvancedFinalFreezeRevalidation", runId);
    }
}

public static class RevalidationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
    }

    public static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Options)
        ?? throw new InvalidDataException($"Required revalidation artifact '{Path.GetFileName(path)}' is invalid.");

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string CompositeHash(IEnumerable<RevalidationFileHash> files)
    {
        var canonical = string.Join('\n', files.OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .Select(item => $"{item.RelativePath}|{item.Length}|{item.Sha256}"));
        return HashText(canonical);
    }

    public static RevalidationFileHash Describe(string root, string path) => new(
        Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length, Hash(path));
}

public static class RevalidationIdentityBuilder
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".ps1", ".sln", ".sql", ".json"
    };

    public static RevalidationBuildIdentity Create(string repositoryRoot, string runId)
    {
        var runRoot = RevalidationPaths.RunRoot(repositoryRoot, runId);
        Directory.CreateDirectory(runRoot);
        var sourceFiles = EnumerateSourceFiles(repositoryRoot)
            .Select(path => RevalidationJson.Describe(repositoryRoot, path)).ToArray();
        var projectFiles = sourceFiles.Where(item => Path.GetExtension(item.RelativePath) is ".csproj" or ".props" or ".targets" or ".sln").ToArray();
        var sourceManifest = new RevalidationSourceManifest(
            "advanced-final-freeze-source-v1", DateTimeOffset.UtcNow, "God2ClassicServer.sln", sourceFiles,
            RevalidationJson.CompositeHash(sourceFiles), RevalidationJson.CompositeHash(projectFiles));
        RevalidationJson.Write(Path.Combine(runRoot, "source-manifest.json"), sourceManifest);

        var outputPaths = EnumerateBuildOutputs(repositoryRoot).ToArray();
        VerificationGuard.Require(outputPaths.Any(path => path.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)), "Debug build outputs are missing.");
        VerificationGuard.Require(outputPaths.Any(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)), "Release build outputs are missing.");
        var outputFiles = outputPaths.Select(path => RevalidationJson.Describe(repositoryRoot, path)).ToArray();
        var outputHash = RevalidationJson.CompositeHash(outputFiles);
        var critical = outputFiles.Where(item => Path.GetFileName(item.RelativePath).StartsWith("God2", StringComparison.Ordinal) &&
                (item.RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || item.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(item => item.RelativePath, item => item.Sha256, StringComparer.Ordinal);
        var tests = Filter(critical, value => value.StartsWith("tests/", StringComparison.OrdinalIgnoreCase));
        var runtime = Filter(critical, value => Path.GetFileName(value).Equals("God2.ClassicServer.Runtime.dll", StringComparison.OrdinalIgnoreCase));
        var protocol = Filter(critical, value => Path.GetFileName(value).Contains("Protocol", StringComparison.OrdinalIgnoreCase));
        var headless = Filter(critical, value => Path.GetFileName(value).Contains("HeadlessGameplay", StringComparison.OrdinalIgnoreCase));
        var advancedTool = RequiredAssemblyHash(critical, "tools/God2.AdvancedHeadlessVerification/bin/Debug/net10.0/God2.AdvancedHeadlessVerification.dll");
        var offlineTool = RequiredAssemblyHash(critical, "tools/God2.OfflineClientReverseEngineering/bin/Debug/net10.0/God2.OfflineClientReverseEngineering.dll");
        var migration = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "database", "schema"), "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Last();
        var offlineSummaryPath = Path.Combine(repositoryRoot, "Artifacts", "OfflineClientReverseEngineering", "final-summary.json");
        using var offlineSummary = JsonDocument.Parse(File.ReadAllBytes(offlineSummaryPath));
        var clientBuildId = offlineSummary.RootElement.GetProperty("ClientBuildId").GetString() ?? "unknown";
        var clientSha = offlineSummary.RootElement.GetProperty("ClientSha256").GetString() ?? "unknown";
        var frameworks = Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsSourcePath)
            .SelectMany(ReadTargetFrameworks).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var identitySeed = string.Join('|', runId, sourceManifest.SourceManifestHash, sourceManifest.ProjectManifestHash,
            outputHash, advancedTool, offlineTool, Path.GetFileName(migration), RevalidationJson.Hash(migration), clientBuildId, clientSha);
        var identityId = $"affr-{RevalidationJson.HashText(identitySeed)[..24].ToLowerInvariant()}";
        var identity = new RevalidationBuildIdentity(
            "advanced-final-freeze-build-identity-v1", runId, DateTimeOffset.UtcNow, identityId, "God2ClassicServer.sln",
            ["Debug", "Release"], frameworks, sourceManifest.SourceManifestHash, sourceManifest.ProjectManifestHash,
            outputHash, critical, tests, runtime, protocol, headless, offlineTool, advancedTool,
            Path.GetFileName(migration), RevalidationJson.Hash(migration), clientBuildId, clientSha,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SourceManifest"] = "advanced-final-freeze-source-v1",
                ["BuildIdentity"] = "advanced-final-freeze-build-identity-v1",
                ["EvidenceManifest"] = "advanced-final-freeze-evidence-v1",
                ["SecurityScan"] = "advanced-final-freeze-security-v1",
                ["ResourceSoak"] = "advanced-final-freeze-soak-v1"
            }, outputFiles);
        RevalidationJson.Write(Path.Combine(runRoot, "build-identity.json"), identity);
        return identity;
    }

    public static RevalidationIdentityVerification VerifyCurrent(string repositoryRoot, RevalidationBuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var assembliesUnchanged = true;
        foreach (var pair in identity.CriticalAssemblyHashes)
        {
            var fullPath = Path.Combine(repositoryRoot, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                assembliesUnchanged = false;
                break;
            }
            if (!string.Equals(RevalidationJson.Hash(fullPath), pair.Value, StringComparison.Ordinal))
            {
                assembliesUnchanged = false;
                break;
            }
        }
        var currentSource = EnumerateSourceFiles(repositoryRoot).Select(path => RevalidationJson.Describe(repositoryRoot, path)).ToArray();
        var sourceMatches = string.Equals(
            RevalidationJson.CompositeHash(currentSource),
            identity.SourceManifestHash,
            StringComparison.Ordinal);
        VerificationGuard.Require(assembliesUnchanged, "A critical assembly disappeared or changed after BuildIdentity creation.");
        VerificationGuard.Require(sourceMatches, "Source manifest changed after BuildIdentity creation.");
        return new RevalidationIdentityVerification(
            sourceMatches,
            assembliesUnchanged,
            currentSource.Length,
            identity.CriticalAssemblyHashes.Count);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var roots = new[] { "src", "tests", "tools", "Automation", "database/schema" };
        var paths = roots.Select(value => Path.Combine(root, value)).Where(Directory.Exists)
            .SelectMany(value => Directory.EnumerateFiles(value, "*", SearchOption.AllDirectories))
            .Where(IsSourcePath)
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path)))
            .Concat([Path.Combine(root, "God2ClassicServer.sln")])
            .Concat(Directory.Exists(Path.Combine(root, "config"))
                ? Directory.EnumerateFiles(Path.Combine(root, "config"), "*.json", SearchOption.TopDirectoryOnly)
                    .Where(path => !path.EndsWith(".local.json", StringComparison.OrdinalIgnoreCase)) : [])
            .Concat(new[]
            {
                Path.Combine(root, "tools", "God2.CurrentSourceMariaDbFixture", "Program.cs"),
                Path.Combine(root, "tools", "God2.CurrentSourceMariaDbFixture", "God2.CurrentSourceMariaDbFixture.csproj"),
                Path.Combine(root, "tools", "God2.CurrentSourceMariaDbFixture", "Invoke-CurrentSourceMariaDbFixture.ps1")
            })
            .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal);
        return paths;
    }

    private static bool IsSourcePath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}Artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}Reports{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}Automation{Path.DirectorySeparatorChar}State{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateBuildOutputs(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}Reports{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) &&
                           Path.GetExtension(path) is ".dll" or ".exe")
            .Where(path => Path.GetFileName(path).StartsWith("God2", StringComparison.Ordinal))
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal);

    private static Dictionary<string, string> Filter(IReadOnlyDictionary<string, string> values, Func<string, bool> predicate) =>
        values.Where(pair => predicate(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string RequiredAssemblyHash(IReadOnlyDictionary<string, string> values, string relativePath) =>
        values.TryGetValue(relativePath, out var value) ? value : throw new InvalidDataException($"Required assembly output is missing: {relativePath}");

    private static IEnumerable<string> ReadTargetFrameworks(string project)
    {
        var document = XDocument.Load(project, LoadOptions.None);
        return document.Descendants().Where(value => value.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(value => value.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
