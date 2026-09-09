using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.RecoveredDatabaseImporter;

namespace God2.RecoveredDatabaseImporter.Tests;

internal sealed class RecoveryBundleFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "God2RecoveryImporterTests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, FixtureFile> _files = new(StringComparer.Ordinal);

    public RecoveryBundleFixture()
    {
        Directory.CreateDirectory(_root);
        SetBuildIdentity();
        AddJson("manifest/environment.json", new
        {
            schemaVersion = "god2-environment-v1",
            authority = "OBSERVED",
            os = "Windows"
        }, "OBSERVED", "god2-environment-v1");
        AddJsonLines("canonical/maps.jsonl",
        [
            new { schemaVersion = "god2-map-v1", authority = "VERIFIED", id = 1, nameZhTw = "起源村" }
        ], "VERIFIED", "god2-map-v1");
        AddJsonLines("canonical/monsters.jsonl",
        [
            new { schemaVersion = "god2-monster-v1", authority = "VERIFIED", id = 1, nameZhTw = "石像怪" }
        ], "VERIFIED", "god2-monster-v1");
        AddJsonLines("canonical/items.jsonl",
        [
            new { schemaVersion = "god2-item-v1", authority = "VERIFIED", id = 10, nameZhTw = "測試石" }
        ], "VERIFIED", "god2-item-v1");
        AddJsonLines("canonical/monster-drops.jsonl",
        [
            new
            {
                schemaVersion = "god2-monster-drop-v1",
                authority = "UNKNOWN_SERVER_ONLY",
                monsterId = 1,
                itemId = 10,
                authoritativeRate = (decimal?)null,
                rateAuthority = "UNKNOWN_SERVER_ONLY",
                defaultDisabledRate = 0m,
                enabled = false
            }
        ], "UNKNOWN_SERVER_ONLY", "god2-monster-drop-v1");
        AddJson("protocol/language-neutral-idl.json", new
        {
            schemaVersion = "god2-language-neutral-idl-v1",
            authority = "DERIVED",
            messages = Array.Empty<object>()
        }, "DERIVED", "god2-language-neutral-idl-v1");
        AddJson("runtime/formulas.json", new
        {
            schemaVersion = "god2-formulas-v1",
            authority = "DERIVED",
            formulas = Array.Empty<object>()
        }, "DERIVED", "god2-formulas-v1");
        AddJson("runtime/runtime-state-machines.json", new
        {
            schemaVersion = "god2-runtime-state-machines-v1",
            authority = "DERIVED",
            machines = Array.Empty<object>()
        }, "DERIVED", "god2-runtime-state-machines-v1");
        AddJson("replay/oracle-contract.json", new
        {
            schemaVersion = "god2-oracle-contract-v1",
            authority = "DERIVED",
            comparison = "SemanticState"
        }, "DERIVED", "god2-oracle-contract-v1");
        AddJsonLines("replay/semantic-replay/cases.jsonl",
        [
            new
            {
                schemaVersion = "god2-semantic-replay-case-v1",
                authority = "DERIVED",
                caseId = "protocol-login-state-001",
                domain = "Protocol",
                plaintextPacket = new { direction = "ClientToServer", opcode = "LoginRequest", fields = new { sequence = 7 } },
                semanticEvent = new { eventType = "LoginAccepted", sessionStage = "Authenticated" },
                initialState = new { session = new { stage = "Connected", sequence = 7, obsolete = true } },
                mutations = new object[]
                {
                    new { operation = "Set", path = "session.stage", value = "Authenticated" },
                    new { operation = "Increment", path = "session.sequence", value = 1 },
                    new { operation = "Remove", path = "session.obsolete" }
                },
                expectedState = new { session = new { stage = "Authenticated", sequence = 8 } },
                expectedResponse = new { result = "Accepted", nextStage = "Authenticated" },
                actualResponse = new { nextStage = "Authenticated", result = "Accepted" }
            }
        ], "DERIVED", "god2-semantic-replay-case-v1");
        AddJson("integration/server-ready.json", new
        {
            schemaVersion = "god2-server-ready-v1",
            authority = "DERIVED",
            status = "CANDIDATE_ONLY"
        }, "DERIVED", "god2-server-ready-v1");
        AddJson("integration/database-ready.json", new
        {
            schemaVersion = "god2-database-ready-v1",
            authority = "DERIVED",
            status = "CANDIDATE_ONLY"
        }, "DERIVED", "god2-database-ready-v1");
    }

    public string ArchivePrefix { get; set; } = string.Empty;

    public string? ManifestHashOverridePath { get; set; }

    public string? OmitArchivePath { get; set; }

    public bool OmitManifestClientBuild { get; set; }

    public bool OmitManifestValidationStatus { get; set; }

    public string ManifestValidationStatus { get; set; } = RecoveryBundleContract.ExpectedClientIdentityStatus;

    public string ManifestClientSha256 { get; set; } = RecoveryBundleContract.ExpectedClientSha256;

    public bool? ManifestExactBindingValidated { get; set; } = true;

    public string? ManifestRecoveryAttestationStatus { get; set; } = RecoveryBundleContract.ExpectedRecoveryAttestationStatus;

    public string? ManifestComputedSha256 { get; set; } = RecoveryBundleContract.ExpectedClientSha256;

    public string? ManifestComputedFileVersion { get; set; } = RecoveryBundleContract.ExpectedClientVersion;

    public string? ManifestComputedArchitecture { get; set; } = RecoveryBundleContract.ExpectedClientArchitecture;

    public (string Path, string Content)? ExtraArchiveFile { get; set; }

    public (string Path, string Content)? DuplicateArchiveFile { get; set; }

    public string RepositoryRoot => FindRepositoryRoot();

    public string OutputRoot => Path.Combine(_root, "output");

    public string SetJson(string path, object value, string authority = "DERIVED", string schemaVersion = "fixture-v1")
    {
        AddJson(path, value, authority, schemaVersion);
        return path;
    }

    public string SetJsonLines(string path, IEnumerable<object> values, string authority, string schemaVersion)
    {
        AddJsonLines(path, values, authority, schemaVersion);
        return path;
    }

    public string SetRaw(string path, string content, string authority = "DERIVED", string schemaVersion = "fixture-v1")
    {
        _files[path] = new FixtureFile(
            Encoding.UTF8.GetBytes(content),
            authority,
            schemaVersion,
            "deterministic-test-fixture");
        return path;
    }

    public void SetBuildIdentity(
        string? bindingStatus = RecoveryBundleContract.ExpectedClientIdentityStatus,
        string executable = RecoveryBundleContract.ExpectedClientExecutable,
        string architecture = RecoveryBundleContract.ExpectedClientArchitecture,
        string fileVersion = RecoveryBundleContract.ExpectedClientVersion,
        string sha256 = RecoveryBundleContract.ExpectedClientSha256,
        bool? exactBindingValidated = true,
        string? recoveryAttestationStatus = RecoveryBundleContract.ExpectedRecoveryAttestationStatus,
        string? computedSha256 = RecoveryBundleContract.ExpectedClientSha256,
        string? computedFileVersion = RecoveryBundleContract.ExpectedClientVersion,
        string? computedArchitecture = RecoveryBundleContract.ExpectedClientArchitecture,
        string descriptorAuthority = "VERIFIED")
    {
        var value = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "god2-client-build-identity-v1",
            ["executable"] = executable,
            ["architecture"] = architecture,
            ["fileVersion"] = fileVersion,
            ["sha256"] = sha256
        };
        if (bindingStatus is not null)
        {
            value["bindingStatus"] = bindingStatus;
        }
        if (exactBindingValidated is not null)
        {
            value["exactBindingValidated"] = exactBindingValidated;
        }
        if (recoveryAttestationStatus is not null)
        {
            value["recoveryAttestationStatus"] = recoveryAttestationStatus;
        }
        if (computedSha256 is not null)
        {
            value["computedSha256"] = computedSha256;
        }
        if (computedFileVersion is not null)
        {
            value["computedFileVersion"] = computedFileVersion;
        }
        if (computedArchitecture is not null)
        {
            value["computedArchitecture"] = computedArchitecture;
        }
        AddJson(
            "manifest/build-identity.json",
            value,
            descriptorAuthority,
            "god2-client-build-identity-v1");
    }

    public void SetDescriptorAuthority(string path, string authority)
    {
        var current = _files[path];
        _files[path] = current with { Authority = authority };
    }

    public void Remove(string path)
    {
        if (!_files.Remove(path))
        {
            throw new KeyNotFoundException($"Fixture path was not found: {path}");
        }
    }

    public string CreateArchive()
    {
        var path = Path.Combine(_root, $"bundle-{Guid.NewGuid():N}.zip");
        var entries = _files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
        {
            relativePath = pair.Key,
            sizeBytes = pair.Value.Bytes.LongLength,
            sha256 = string.Equals(pair.Key, ManifestHashOverridePath, StringComparison.Ordinal)
                ? new string('0', 64)
                : Sha256(pair.Value.Bytes),
            provenance = pair.Value.Provenance,
            authority = pair.Value.Authority,
            schemaVersion = pair.Value.SchemaVersion
        }).ToArray();
        var manifestClientBuild = new Dictionary<string, object?>
        {
            ["executable"] = RecoveryBundleContract.ExpectedClientExecutable,
            ["architecture"] = RecoveryBundleContract.ExpectedClientArchitecture,
            ["fileVersion"] = RecoveryBundleContract.ExpectedClientVersion,
            ["sha256"] = ManifestClientSha256
        };
        if (!OmitManifestValidationStatus)
        {
            manifestClientBuild["validationStatus"] = ManifestValidationStatus;
        }
        if (ManifestExactBindingValidated is not null)
        {
            manifestClientBuild["exactBindingValidated"] = ManifestExactBindingValidated;
        }
        if (ManifestRecoveryAttestationStatus is not null)
        {
            manifestClientBuild["recoveryAttestationStatus"] = ManifestRecoveryAttestationStatus;
        }
        if (ManifestComputedSha256 is not null)
        {
            manifestClientBuild["computedSha256"] = ManifestComputedSha256;
        }
        if (ManifestComputedFileVersion is not null)
        {
            manifestClientBuild["computedFileVersion"] = ManifestComputedFileVersion;
        }
        if (ManifestComputedArchitecture is not null)
        {
            manifestClientBuild["computedArchitecture"] = ManifestComputedArchitecture;
        }
        var manifestRoot = new Dictionary<string, object?>
        {
            ["schemaVersion"] = RecoveryBundleContract.ManifestSchemaVersion,
            ["packageId"] = "fixture-package"
        };
        if (!OmitManifestClientBuild)
        {
            manifestRoot["clientBuild"] = manifestClientBuild;
        }
        manifestRoot["files"] = entries;
        var manifest = JsonSerializer.SerializeToUtf8Bytes(manifestRoot, JsonOptions);

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        Write(archive, ArchivePrefix + "manifest/package-manifest.json", manifest);
        foreach (var pair in _files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!string.Equals(pair.Key, OmitArchivePath, StringComparison.Ordinal))
            {
                Write(archive, ArchivePrefix + pair.Key, pair.Value.Bytes);
            }
        }
        if (ExtraArchiveFile is { } extra)
        {
            Write(archive, extra.Path, Encoding.UTF8.GetBytes(extra.Content));
        }
        if (DuplicateArchiveFile is { } duplicate)
        {
            Write(archive, ArchivePrefix + duplicate.Path, Encoding.UTF8.GetBytes(duplicate.Content));
        }
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void AddJson(string path, object value, string authority, string schemaVersion) =>
        _files[path] = new FixtureFile(
            JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions),
            authority,
            schemaVersion,
            "deterministic-test-fixture");

    private void AddJsonLines(string path, IEnumerable<object> values, string authority, string schemaVersion)
    {
        var text = string.Join("\n", values.Select(value => JsonSerializer.Serialize(value, JsonOptions))) + "\n";
        _files[path] = new FixtureFile(Encoding.UTF8.GetBytes(text), authority, schemaVersion, "deterministic-test-fixture");
    }

    private static void Write(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed record FixtureFile(byte[] Bytes, string Authority, string SchemaVersion, string Provenance);
}
