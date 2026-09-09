using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.OfflineClientReverseEngineering;

public sealed record RuntimeCaptureProvenance(
    string SchemaVersion,
    string ClientBuildId,
    string ClientSha256,
    string SessionId,
    int ProcessId,
    string CodeArtifact,
    string CodeSha256,
    string LengthTableArtifact,
    string LengthTableSha256,
    bool SameProcess,
    bool CaptureAttested,
    string BuildAssociationStatus,
    DateTimeOffset EarliestArtifactUtc,
    DateTimeOffset LatestArtifactUtc);

public sealed record RuntimeCaptureSelection(
    string CodePath,
    string LengthTablePath,
    RuntimeCaptureProvenance Provenance)
{
    private static readonly Regex CodeName = new("^pid-(?<pid>[0-9]+)-rva-1100\\.bin$", RegexOptions.CultureInvariant);
    private static readonly Regex TableName = new("^pid-(?<pid>[0-9]+)-rva-493F90\\.bin$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static RuntimeCaptureSelection? Resolve(
        string workspaceRoot,
        string outputRoot,
        string clientBuildId,
        string clientSha256,
        JsonSerializerOptions options)
    {
        var existingManifest = Path.Combine(outputRoot, "runtime-capture-manifest.json");
        if (File.Exists(existingManifest))
        {
            return ResolveManifest(workspaceRoot, outputRoot, existingManifest, clientBuildId, clientSha256);
        }

        var codeRoot = Path.Combine(outputRoot, "runtime-memory-readonly");
        var tableRoot = Path.Combine(outputRoot, "runtime-table-readonly");
        var codeFiles = Enumerate(codeRoot, CodeName);
        var tableFiles = Enumerate(tableRoot, TableName);
        if (codeFiles.Count == 0 && tableFiles.Count == 0)
        {
            return null;
        }
        if (codeFiles.Count != 1 || tableFiles.Count != 1)
        {
            throw new InvalidDataException("Runtime capture selection is ambiguous. Keep one complete PID pair or provide a validated capture manifest.");
        }

        var code = codeFiles.Single();
        var table = tableFiles.Single();
        if (code.ProcessId != table.ProcessId)
        {
            throw new InvalidDataException("Runtime code and length-table snapshots were not captured from the same process ID.");
        }

        var codeHash = Hash(code.Path);
        var tableHash = Hash(table.Path);
        var codeTime = File.GetLastWriteTimeUtc(code.Path);
        var tableTime = File.GetLastWriteTimeUtc(table.Path);
        var earliest = new DateTimeOffset(codeTime <= tableTime ? codeTime : tableTime, TimeSpan.Zero);
        var latest = new DateTimeOffset(codeTime >= tableTime ? codeTime : tableTime, TimeSpan.Zero);
        if (latest - earliest > TimeSpan.FromMinutes(15))
        {
            throw new InvalidDataException("Runtime code and length-table artifacts are outside the bounded capture-session time window.");
        }

        var codeAttestation = ReadAttestation(codeRoot, code.Path, code.ProcessId, clientSha256, codeHash, "0x00001100", 256);
        var tableAttestation = ReadAttestation(tableRoot, table.Path, table.ProcessId, clientSha256, tableHash, "0x00493F90", 256);
        var attested = codeAttestation && tableAttestation;
        var sessionMaterial = $"{clientSha256}|{code.ProcessId}|{codeHash}|{tableHash}";
        var sessionId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionMaterial)))[..24];
        var provenance = new RuntimeCaptureProvenance(
            "offline-runtime-capture-provenance-v1",
            clientBuildId,
            clientSha256,
            sessionId,
            code.ProcessId,
            Relative(workspaceRoot, code.Path),
            codeHash,
            Relative(workspaceRoot, table.Path),
            tableHash,
            SameProcess: true,
            CaptureAttested: attested,
            attested ? "CaptureTimeExecutableSha256Matched" : "LegacyCaptureHashBoundButNotCaptureTimeBuildAttested",
            earliest,
            latest);
        File.WriteAllText(
            Path.Combine(outputRoot, "runtime-capture-manifest.json"),
            JsonSerializer.Serialize(provenance, options));
        return new RuntimeCaptureSelection(code.Path, table.Path, provenance);
    }

    private static RuntimeCaptureSelection ResolveManifest(
        string workspaceRoot,
        string outputRoot,
        string manifestPath,
        string clientBuildId,
        string clientSha256)
    {
        var provenance = JsonSerializer.Deserialize<RuntimeCaptureProvenance>(
            BoundedFile.ReadAllBytes(manifestPath, 1024 * 1024, "Runtime capture manifest"))
            ?? throw new InvalidDataException("Runtime capture manifest is empty.");
        if (provenance.SchemaVersion != "offline-runtime-capture-provenance-v1" ||
            provenance.ClientBuildId != clientBuildId ||
            !string.Equals(provenance.ClientSha256, clientSha256, StringComparison.OrdinalIgnoreCase) ||
            !provenance.SameProcess)
        {
            throw new InvalidDataException("Runtime capture manifest does not match the current client build and provenance policy.");
        }

        var codePath = ResolveContainedPath(workspaceRoot, outputRoot, provenance.CodeArtifact);
        var tablePath = ResolveContainedPath(workspaceRoot, outputRoot, provenance.LengthTableArtifact);
        var codeMatch = CodeName.Match(Path.GetFileName(codePath));
        var tableMatch = TableName.Match(Path.GetFileName(tablePath));
        if (!codeMatch.Success || !tableMatch.Success ||
            int.Parse(codeMatch.Groups["pid"].Value, System.Globalization.CultureInfo.InvariantCulture) != provenance.ProcessId ||
            int.Parse(tableMatch.Groups["pid"].Value, System.Globalization.CultureInfo.InvariantCulture) != provenance.ProcessId ||
            !string.Equals(Hash(codePath), provenance.CodeSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Hash(tablePath), provenance.LengthTableSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Runtime capture manifest file identity or content hash validation failed.");
        }

        return new RuntimeCaptureSelection(codePath, tablePath, provenance);
    }

    private static List<CaptureFile> Enumerate(string root, Regex pattern)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<CaptureFile>();
        foreach (var path in Directory.EnumerateFiles(root, "*.bin").OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var match = pattern.Match(Path.GetFileName(path));
            if (match.Success)
            {
                result.Add(new CaptureFile(path, int.Parse(match.Groups["pid"].Value, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
        return result;
    }

    private static bool ReadAttestation(
        string root,
        string artifactPath,
        int processId,
        string clientSha256,
        string artifactSha256,
        string expectedRva,
        int expectedBefore)
    {
        var manifest = Path.Combine(root, "memory-dump-result.json");
        if (!File.Exists(manifest)) return false;
        using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(manifest, 1024 * 1024, "Runtime capture attestation"));
        var rootElement = document.RootElement;
        if (!rootElement.TryGetProperty("targetPid", out var pid) || pid.GetInt32() != processId ||
            !rootElement.TryGetProperty("clientExecutableSha256", out var executableHash) ||
            !string.Equals(executableHash.GetString(), clientSha256, StringComparison.OrdinalIgnoreCase) ||
            !rootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return results.EnumerateArray().Any(item =>
            item.TryGetProperty("binSha256", out var hash) &&
            string.Equals(hash.GetString(), artifactSha256, StringComparison.OrdinalIgnoreCase) &&
            item.TryGetProperty("rva", out var rva) &&
            string.Equals(rva.GetString(), expectedRva, StringComparison.OrdinalIgnoreCase) &&
            item.TryGetProperty("before", out var before) && before.GetInt32() == expectedBefore &&
            item.TryGetProperty("bytesRead", out var bytesRead) && bytesRead.GetInt64() == new FileInfo(artifactPath).Length);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string ResolveContainedPath(string workspaceRoot, string outputRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Runtime capture manifest paths must be workspace-relative.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
        var allowedRoot = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            throw new InvalidDataException("Runtime capture manifest path escapes the expected artifact root or is missing.");
        }
        return fullPath;
    }

    private sealed record CaptureFile(string Path, int ProcessId);
}
