using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.RecoveredDatabaseImporter;

public sealed partial class RecoveryBundleReader
{
    private const string ManifestRelativePath = "manifest/package-manifest.json";
    private const string BuildIdentityRelativePath = "manifest/build-identity.json";

    public async Task<RecoveryBundleInspection> InspectAsync(string zipPath, CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(zipPath);
        if (!File.Exists(sourcePath))
        {
            throw new RecoveryImportException("bundle.not_found", $"Recovery Bundle was not found: {sourcePath}");
        }

        var sourceSize = new FileInfo(sourcePath).Length;
        var sourceHashBefore = await HashFileAsync(sourcePath, cancellationToken);
        RecoveryBundleManifest manifest;
        ClientBuildIdentity buildIdentity;
        ContentValidationSummary contentSummary;
        string rootPrefix;
        string manifestHash;

        try
        {
            await using var archiveStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
            var archiveMap = ValidateArchiveAndCreateMap(archive, out rootPrefix);
            var manifestEntry = archiveMap[ManifestRelativePath];
            var manifestBytes = await ReadBoundedAsync(manifestEntry, RecoveryBundleContract.MaximumManifestBytes, cancellationToken);
            manifestHash = Sha256(manifestBytes);
            manifest = ParseManifest(manifestBytes);
            ValidateManifestAgainstArchive(manifest, archiveMap);

            var schemaValidator = new RecoveryBundleSchemaValidator();
            buildIdentity = await ValidateFilesAsync(
                manifest,
                archiveMap,
                schemaValidator,
                cancellationToken);
            contentSummary = schemaValidator.Complete();
        }
        catch (RecoveryImportException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException or DecoderFallbackException)
        {
            throw new RecoveryImportException("bundle.invalid", exception.Message, exception);
        }

        var sourceHashAfter = await HashFileAsync(sourcePath, cancellationToken);
        if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecoveryImportException("bundle.changed_during_validation", "Recovery Bundle changed while it was being validated.");
        }

        return new RecoveryBundleInspection(
            sourcePath,
            sourceSize,
            sourceHashAfter,
            rootPrefix,
            manifestHash,
            manifest,
            buildIdentity,
            contentSummary,
            DateTimeOffset.UtcNow);
    }

    public async Task<int> StageAsync(
        RecoveryBundleInspection inspection,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(stagingRoot);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        await using var archiveStream = new FileStream(inspection.SourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var currentSourceHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(currentSourceHash, inspection.SourceZipSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecoveryImportException("bundle.changed_before_staging", "Recovery Bundle changed after validation and before staging.");
        }
        archiveStream.Position = 0;
        Directory.CreateDirectory(root);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var archiveMap = ValidateArchiveAndCreateMap(archive, out var currentPrefix);
        if (!string.Equals(currentPrefix, inspection.ArchiveRootPrefix, StringComparison.Ordinal))
        {
            throw new RecoveryImportException("bundle.root_changed", "Recovery Bundle root changed after validation.");
        }

        var staged = 0;
        foreach (var descriptor in inspection.Manifest.Files.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(root, descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new RecoveryImportException("staging.path_escape", $"Staging path escaped its root: {descriptor.RelativePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = archiveMap[descriptor.RelativePath].Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long length = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                length = checked(length + read);
                if (length > descriptor.SizeBytes)
                {
                    throw new RecoveryImportException("staging.length_overrun", $"Staged entry exceeded its declared length: {descriptor.RelativePath}");
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);

            var stagedHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (length != descriptor.SizeBytes || !string.Equals(stagedHash, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new RecoveryImportException("staging.integrity_mismatch", $"Staged file failed integrity verification: {descriptor.RelativePath}");
            }
            staged++;
        }

        return staged;
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchiveAndCreateMap(ZipArchive archive, out string rootPrefix)
    {
        if (archive.Entries.Count > RecoveryBundleContract.MaximumFileCount)
        {
            throw new RecoveryImportException("bundle.too_many_entries", $"Recovery Bundle exceeds {RecoveryBundleContract.MaximumFileCount} entries.");
        }

        var normalizedEntries = new List<(ZipArchiveEntry Entry, string Path)>();
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeArchivePath(entry.FullName);
            ValidateArchivePath(normalized, entry.FullName);
            if (IsSymbolicLink(entry))
            {
                throw new RecoveryImportException("bundle.symbolic_link", $"Symbolic links are not permitted in Recovery Bundles: {entry.FullName}");
            }
            if (IsDirectory(entry))
            {
                continue;
            }
            if (entry.Length < 0 || entry.Length > RecoveryBundleContract.MaximumEntryBytes)
            {
                throw new RecoveryImportException("bundle.entry_too_large", $"Recovery Bundle entry exceeds the size limit: {entry.FullName}");
            }
            totalLength = checked(totalLength + entry.Length);
            if (totalLength > RecoveryBundleContract.MaximumTotalUncompressedBytes)
            {
                throw new RecoveryImportException("bundle.uncompressed_size_limit", "Recovery Bundle exceeds the total uncompressed size limit.");
            }
            if (entry.Length > 1024 * 1024 && (entry.CompressedLength <= 0 || entry.Length / Math.Max(1d, entry.CompressedLength) > 250d))
            {
                throw new RecoveryImportException("bundle.compression_ratio_limit", $"Recovery Bundle entry has an unsafe compression ratio: {entry.FullName}");
            }
            normalizedEntries.Add((entry, normalized));
        }

        var manifests = normalizedEntries
            .Where(value =>
            {
                if (!value.Path.EndsWith(ManifestRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                var prefixLength = value.Path.Length - ManifestRelativePath.Length;
                return prefixLength == 0 || value.Path[prefixLength - 1] == '/';
            })
            .ToArray();
        if (manifests.Length != 1)
        {
            throw new RecoveryImportException("manifest.count_invalid", $"Expected exactly one {ManifestRelativePath}; found {manifests.Length}.");
        }

        rootPrefix = manifests[0].Path[..^ManifestRelativePath.Length];
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in normalizedEntries)
        {
            if (!value.Path.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new RecoveryImportException("bundle.multiple_roots", $"Archive entry is outside the Recovery Bundle root: {value.Path}");
            }
            var relative = value.Path[rootPrefix.Length..];
            ValidateRelativePath(relative);
            if (!result.TryAdd(relative, value.Entry))
            {
                throw new RecoveryImportException("bundle.duplicate_entry", $"Duplicate Recovery Bundle entry: {relative}");
            }
        }
        return result;
    }

    private static RecoveryBundleManifest ParseManifest(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 128
        });
        EnsureUniqueProperties(document.RootElement, "$manifest");
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new RecoveryImportException("manifest.root_invalid", "Package manifest must be a JSON object.");
        }

        var root = document.RootElement;
        var schemaVersion = RequiredString(root, "schemaVersion", "manifest.schema_version_missing");
        if (!string.Equals(schemaVersion, RecoveryBundleContract.ManifestSchemaVersion, StringComparison.Ordinal))
        {
            throw new RecoveryImportException("manifest.schema_version_unsupported", $"Unsupported package manifest schema: {schemaVersion}");
        }
        var packageId = RequiredString(root, "packageId", "manifest.package_id_missing");
        if (packageId.Length > 128 || !PackageIdPattern().IsMatch(packageId))
        {
            throw new RecoveryImportException("manifest.package_id_invalid", "packageId must contain only letters, digits, dot, dash, or underscore.");
        }

        if (!TryGetProperty(root, "clientBuild", out var clientBuildElement))
        {
            throw new RecoveryImportException(
                "manifest.client_build_missing",
                "Package manifest must contain clientBuild with an exact computed and validated TARGET identity.");
        }
        var clientBuild = ParseClientBuild(
            clientBuildElement,
            "manifest.clientBuild",
            "validationStatus",
            "manifest.client_build_validation_status_missing");
        ValidateExpectedBuild(clientBuild, "manifest.clientBuild");

        if (!TryGetProperty(root, "files", out var filesElement) && !TryGetProperty(root, "entries", out filesElement))
        {
            throw new RecoveryImportException("manifest.files_missing", "Package manifest does not contain files[].");
        }
        if (filesElement.ValueKind != JsonValueKind.Array || filesElement.GetArrayLength() == 0)
        {
            throw new RecoveryImportException("manifest.files_invalid", "Package manifest files[] must be a non-empty array.");
        }

        var files = new List<RecoveryBundleFileDescriptor>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in filesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new RecoveryImportException("manifest.file_invalid", "Every files[] entry must be an object.");
            }
            var relativePath = RequiredString(item, "relativePath", "manifest.file_path_missing");
            ValidateRelativePath(relativePath);
            if (string.Equals(relativePath, ManifestRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new RecoveryImportException("manifest.self_hash_forbidden", "Package manifest must not list itself.");
            }
            if (!paths.Add(relativePath))
            {
                throw new RecoveryImportException("manifest.duplicate_file", $"Duplicate manifest file: {relativePath}");
            }

            var size = RequiredInt64(item, "sizeBytes", "manifest.file_size_missing");
            if (size < 0 || size > RecoveryBundleContract.MaximumEntryBytes)
            {
                throw new RecoveryImportException("manifest.file_size_invalid", $"Invalid declared size for {relativePath}.");
            }
            var sha256 = RequiredString(item, "sha256", "manifest.file_hash_missing").ToLowerInvariant();
            if (!Sha256Pattern().IsMatch(sha256))
            {
                throw new RecoveryImportException("manifest.file_hash_invalid", $"Invalid SHA-256 for {relativePath}.");
            }
            var provenance = RequiredText(item, "provenance", "manifest.file_provenance_missing");
            var authority = RequiredString(item, "authority", "manifest.file_authority_missing").ToUpperInvariant();
            if (!RecoveryBundleContract.AllowedAuthorities.Contains(authority))
            {
                throw new RecoveryImportException("manifest.file_authority_invalid", $"Invalid authority for {relativePath}: {authority}");
            }
            var fileSchema = RequiredString(item, "schemaVersion", "manifest.file_schema_missing");
            if (fileSchema.Length > 128 || !SchemaVersionPattern().IsMatch(fileSchema))
            {
                throw new RecoveryImportException("manifest.file_schema_invalid", $"Invalid schemaVersion for {relativePath}: {fileSchema}");
            }
            files.Add(new RecoveryBundleFileDescriptor(relativePath, size, sha256, provenance, authority, fileSchema));
        }

        if (!paths.Contains(BuildIdentityRelativePath))
        {
            throw new RecoveryImportException("manifest.build_identity_missing", $"Manifest must list {BuildIdentityRelativePath}.");
        }

        return new RecoveryBundleManifest(schemaVersion, packageId, clientBuild, files);
    }

    private static void ValidateManifestAgainstArchive(
        RecoveryBundleManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> archiveMap)
    {
        var declared = manifest.Files.Select(value => value.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = archiveMap.Keys.Where(value => !string.Equals(value, ManifestRelativePath, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = declared.Except(actual, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var unlisted = actual.Except(declared, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            throw new RecoveryImportException("manifest.file_missing", $"Manifest-listed file is missing: {missing[0]}");
        }
        if (unlisted.Length != 0)
        {
            throw new RecoveryImportException("manifest.unlisted_file", $"Archive contains an unlisted file: {unlisted[0]}");
        }

        foreach (var descriptor in manifest.Files)
        {
            if (archiveMap[descriptor.RelativePath].Length != descriptor.SizeBytes)
            {
                throw new RecoveryImportException("manifest.file_size_mismatch", $"File size mismatch: {descriptor.RelativePath}");
            }
        }
    }

    private static async Task<ClientBuildIdentity> ValidateFilesAsync(
        RecoveryBundleManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> archiveMap,
        RecoveryBundleSchemaValidator schemaValidator,
        CancellationToken cancellationToken)
    {
        ClientBuildIdentity? buildIdentity = null;
        foreach (var descriptor in manifest.Files.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archiveMap[descriptor.RelativePath];
            var actualHash = await HashEntryAsync(entry, descriptor.SizeBytes, cancellationToken);
            if (!string.Equals(actualHash, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new RecoveryImportException("manifest.file_hash_mismatch", $"SHA-256 mismatch: {descriptor.RelativePath}");
            }

            if (descriptor.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (descriptor.SizeBytes > RecoveryBundleContract.MaximumStructuredJsonBytes)
                {
                    throw new RecoveryImportException("schema.json_size_limit", $"JSON document is too large for bounded validation: {descriptor.RelativePath}");
                }
                await using var stream = entry.Open();
                using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 256
                }, cancellationToken);
                EnsureUniqueProperties(document.RootElement, descriptor.RelativePath);
                schemaValidator.ValidateJsonDocument(descriptor.RelativePath, descriptor, document.RootElement, isJsonLine: false);
                if (string.Equals(descriptor.RelativePath, BuildIdentityRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(descriptor.Authority, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RecoveryImportException(
                            "build_identity.authority_not_promotable",
                            $"Exact TARGET build identity requires VERIFIED descriptor authority; found {descriptor.Authority}.");
                    }
                    buildIdentity = ParseBuildIdentity(document.RootElement);
                    ValidateExpectedBuild(buildIdentity, BuildIdentityRelativePath);
                }
            }
            else if (descriptor.RelativePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                await ValidateJsonLinesAsync(entry, descriptor, schemaValidator, cancellationToken);
            }
        }

        var validatedBuild = buildIdentity ?? throw new RecoveryImportException(
            "build_identity.not_validated",
            "Client build identity was not validated.");
        if (manifest.ClientBuild != validatedBuild)
        {
            throw new RecoveryImportException(
                "build_identity.manifest_mismatch",
                "manifest.clientBuild and manifest/build-identity.json must contain byte-for-byte equivalent identity values and the same trusted validation status.");
        }
        return validatedBuild;
    }

    private static async Task ValidateJsonLinesAsync(
        ZipArchiveEntry entry,
        RecoveryBundleFileDescriptor descriptor,
        RecoveryBundleSchemaValidator validator,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false);
        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            if (line.Length > RecoveryBundleContract.MaximumJsonLineChars)
            {
                throw new RecoveryImportException("schema.jsonl_line_size_limit", $"JSONL line exceeds the bound: {descriptor.RelativePath}:{lineNumber}");
            }
            try
            {
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 256
                });
                EnsureUniqueProperties(document.RootElement, $"{descriptor.RelativePath}:{lineNumber}");
                validator.ValidateJsonDocument(descriptor.RelativePath, descriptor, document.RootElement, isJsonLine: true);
            }
            catch (JsonException exception)
            {
                throw new RecoveryImportException("schema.jsonl_invalid", $"Invalid JSONL at {descriptor.RelativePath}:{lineNumber}: {exception.Message}", exception);
            }
        }
    }

    private static ClientBuildIdentity ParseBuildIdentity(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new RecoveryImportException("build_identity.root_invalid", "build-identity.json must be an object.");
        }
        return ParseClientBuild(
            root,
            BuildIdentityRelativePath,
            "bindingStatus",
            "build_identity.binding_status_missing");
    }

    private static ClientBuildIdentity ParseClientBuild(
        JsonElement element,
        string source,
        string statusProperty,
        string missingStatusCode)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new RecoveryImportException("build_identity.invalid", $"Client build identity must be an object: {source}");
        }
        var executable = RequiredString(element, "executable", "build_identity.executable_missing");
        var architecture = RequiredString(element, "architecture", "build_identity.architecture_missing");
        var version = TryGetString(element, "fileVersion") ?? RequiredString(element, "version", "build_identity.version_missing");
        var sha256 = RequiredString(element, "sha256", "build_identity.sha256_missing");
        var validationStatus = RequiredString(element, statusProperty, missingStatusCode);
        if (!string.Equals(validationStatus, RecoveryBundleContract.ExpectedClientIdentityStatus, StringComparison.Ordinal))
        {
            throw new RecoveryImportException(
                "build_identity.validation_status_not_trusted",
                $"Exact TARGET identity at {source} is not computed and validated: expected {RecoveryBundleContract.ExpectedClientIdentityStatus}; found {validationStatus}.");
        }

        var exactBindingValidated = RequiredBoolean(
            element,
            "exactBindingValidated",
            "build_identity.exact_binding_missing");
        var recoveryAttestationStatus = RequiredString(
            element,
            "recoveryAttestationStatus",
            "build_identity.recovery_attestation_missing");
        var computedSha256 = RequiredString(
            element,
            "computedSha256",
            "build_identity.computed_sha256_missing");
        var computedFileVersion = RequiredString(
            element,
            "computedFileVersion",
            "build_identity.computed_version_missing");
        var computedArchitecture = RequiredString(
            element,
            "computedArchitecture",
            "build_identity.computed_architecture_missing");
        return new ClientBuildIdentity(
            executable,
            architecture,
            version,
            sha256,
            validationStatus,
            exactBindingValidated,
            recoveryAttestationStatus,
            computedSha256,
            computedFileVersion,
            computedArchitecture);
    }

    private static void ValidateExpectedBuild(ClientBuildIdentity build, string source)
    {
        if (!string.Equals(build.ValidationStatus, RecoveryBundleContract.ExpectedClientIdentityStatus, StringComparison.Ordinal))
        {
            throw new RecoveryImportException(
                "build_identity.validation_status_not_trusted",
                $"Exact TARGET identity at {source} is not computed and validated: expected {RecoveryBundleContract.ExpectedClientIdentityStatus}; found {build.ValidationStatus}.");
        }
        if (!build.ExactBindingValidated)
        {
            throw new RecoveryImportException(
                "build_identity.exact_binding_not_validated",
                $"Exact TARGET identity at {source} does not declare exactBindingValidated=true.");
        }
        if (!string.Equals(
                build.RecoveryAttestationStatus,
                RecoveryBundleContract.ExpectedRecoveryAttestationStatus,
                StringComparison.Ordinal))
        {
            throw new RecoveryImportException(
                "build_identity.recovery_attestation_not_trusted",
                $"Exact TARGET identity at {source} lacks the trusted recovery build/session/process attestation: expected {RecoveryBundleContract.ExpectedRecoveryAttestationStatus}; found {build.RecoveryAttestationStatus}.");
        }
        if (!string.Equals(build.Executable, RecoveryBundleContract.ExpectedClientExecutable, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(build.Architecture, RecoveryBundleContract.ExpectedClientArchitecture, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(build.FileVersion, RecoveryBundleContract.ExpectedClientVersion, StringComparison.Ordinal) ||
            !string.Equals(build.Sha256, RecoveryBundleContract.ExpectedClientSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecoveryImportException(
                "build_identity.mismatch",
                $"Recovery Bundle is not bound to the exact TARGET build at {source}: expected {RecoveryBundleContract.ExpectedClientExecutable}/{RecoveryBundleContract.ExpectedClientArchitecture}/{RecoveryBundleContract.ExpectedClientVersion}/{RecoveryBundleContract.ExpectedClientSha256}.");
        }
        if (!string.Equals(build.ComputedSha256, RecoveryBundleContract.ExpectedClientSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(build.ComputedFileVersion, RecoveryBundleContract.ExpectedClientVersion, StringComparison.Ordinal) ||
            !string.Equals(build.ComputedArchitecture, RecoveryBundleContract.ExpectedClientArchitecture, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(build.ComputedSha256, build.Sha256, StringComparison.Ordinal) ||
            !string.Equals(build.ComputedFileVersion, build.FileVersion, StringComparison.Ordinal) ||
            !string.Equals(build.ComputedArchitecture, build.Architecture, StringComparison.Ordinal))
        {
            throw new RecoveryImportException(
                "build_identity.computed_identity_mismatch",
                $"Computed TARGET identity at {source} must exactly match its declared SHA-256, file version, and architecture.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(ZipArchiveEntry entry, long maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes || entry.Length > int.MaxValue)
        {
            throw new RecoveryImportException("bundle.bounded_read_limit", $"Entry exceeds bounded-read limit: {entry.FullName}");
        }
        await using var source = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        await source.CopyToAsync(output, cancellationToken);
        if (output.Length != entry.Length)
        {
            throw new RecoveryImportException("bundle.entry_truncated", $"Entry length changed while reading: {entry.FullName}");
        }
        return output.ToArray();
    }

    private static async Task<string> HashEntryAsync(ZipArchiveEntry entry, long expectedLength, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            length = checked(length + read);
            if (length > expectedLength)
            {
                throw new RecoveryImportException("bundle.entry_length_overrun", $"Entry exceeded declared length: {entry.FullName}");
            }
            hash.AppendData(buffer, 0, read);
        }
        if (length != expectedLength)
        {
            throw new RecoveryImportException("bundle.entry_length_mismatch", $"Entry length mismatch: {entry.FullName}");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void EnsureUniqueProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(RecoveryBundleSchemaValidator.Normalize(property.Name)))
                {
                    throw new RecoveryImportException("schema.duplicate_property", $"Duplicate JSON property at {path}.{property.Name}");
                }
                EnsureUniqueProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                EnsureUniqueProperties(item, $"{path}[{index++}]");
            }
        }
    }

    private static string RequiredString(JsonElement element, string propertyName, string code)
    {
        var value = TryGetString(element, propertyName);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new RecoveryImportException(code, $"Required string is missing: {propertyName}");
    }

    private static string RequiredText(JsonElement element, string propertyName, string code)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            throw new RecoveryImportException(code, $"Required value is missing: {propertyName}");
        }
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new RecoveryImportException(code, $"Required value is empty: {propertyName}");
    }

    private static long RequiredInt64(JsonElement element, string propertyName, string code)
    {
        if (!TryGetProperty(element, propertyName, out var value) || !value.TryGetInt64(out var result))
        {
            throw new RecoveryImportException(code, $"Required integer is missing: {propertyName}");
        }
        return result;
    }

    private static bool RequiredBoolean(JsonElement element, string propertyName, string code)
    {
        if (!TryGetProperty(element, propertyName, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new RecoveryImportException(code, $"Required Boolean is missing: {propertyName}");
        }
        return value.GetBoolean();
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        var normalized = RecoveryBundleSchemaValidator.Normalize(propertyName);
        foreach (var property in element.EnumerateObject())
        {
            if (RecoveryBundleSchemaValidator.Normalize(property.Name) == normalized)
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static void ValidateRelativePath(string value)
    {
        var normalized = NormalizeArchivePath(value);
        ValidateArchivePath(normalized, value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new RecoveryImportException("manifest.path_not_normalized", $"Manifest path must use forward slashes: {value}");
        }
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            throw new RecoveryImportException("manifest.path_is_directory", $"Manifest entries must identify files: {value}");
        }
    }

    private static void ValidateArchivePath(string normalized, string original)
    {
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(':') || normalized.Contains('\0'))
        {
            throw new RecoveryImportException("bundle.unsafe_path", $"Unsafe archive path: {original}");
        }
        var segments = normalized.Split('/', StringSplitOptions.None);
        foreach (var segment in segments.Where(value => value.Length != 0))
        {
            if (segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                throw new RecoveryImportException("bundle.unsafe_path", $"Unsafe archive path: {original}");
            }
        }
        if (segments.Take(Math.Max(0, segments.Length - 1)).Any(value => value.Length == 0) ||
            (segments[^1].Length == 0 && !normalized.EndsWith("/", StringComparison.Ordinal)))
        {
            throw new RecoveryImportException("bundle.unsafe_path", $"Unsafe archive path: {original}");
        }
    }

    private static string NormalizeArchivePath(string value) => value.Replace('\\', '/');

    private static bool IsDirectory(ZipArchiveEntry entry) => string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/", StringComparison.Ordinal);

    private static bool IsSymbolicLink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaVersionPattern();
}
