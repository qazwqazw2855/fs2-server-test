using System.Globalization;
using System.Text;
using System.Text.Json;

namespace God2.GameplayContentRecovery;

public sealed record OfficialItemCatalogWriteResult(
    int RecordCount,
    int ExistingFormalBaselineCount,
    int MissingCanonicalBindingCount,
    string SourceSha256,
    long SourceSizeBytes,
    string OutputPath);

public static class OfficialItemCatalogWriter
{
    private const string RelativeSourcePath = "Data2/Patch/Comm/gamedata.csvZ";
    private static readonly string[] ItemSections =
    [
        "WPN", "EQU", "GOD", "MAP", "MED01", "MED02", "TLI", "PET", "MAT01", "MIS", "SPI", "SKB", "PFD",
        "GWP", "GEH", "GEB", "GEQ", "PEQ", "STR", "KIT01", "KIT02", "NST", "EGG", "SPP", "CBK", "SCD",
        "EQC", "CBF01", "CAD", "BEB", "VPT", "CBF02", "ELE", "MIS02", "MED03", "TWP", "TEQ", "AMU", "BAR",
        "GEQ02", "MEQ", "COM", "NCP", "NMP", "MIS03", "UPS", "SSW", "SES"
    ];

    public static async Task<OfficialItemCatalogWriteResult> WriteAsync(
        string clientRoot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var baselineKeys = ReadPersistedFormalBaselineKeys(fullOutputPath);
        if (baselineKeys.Count != 17407)
        {
            throw new InvalidDataException($"Persisted formal item baseline must contain 17,407 keys; actual={baselineKeys.Count}.");
        }

        var sourcePath = Path.Combine(clientRoot, RelativeSourcePath.Replace('/', Path.DirectorySeparatorChar));
        var sourceInfo = new FileInfo(sourcePath);
        var document = await God2PackedFile.ReadCsvZAsync(sourcePath, cancellationToken);
        var sections = GameDataSections.Parse(document.Rows);
        var records = new List<object>();
        var exactKeys = new HashSet<(string Section, int ClientItemId)>();
        var missingBindingCount = 0;
        foreach (var sectionName in ItemSections)
        {
            if (!sections.TryGetValue(sectionName, out var section))
            {
                throw new InvalidDataException($"Exact-client item section is missing: {sectionName}.");
            }
            foreach (var row in section.Rows)
            {
                if (row.Fields.Count == 0 || !int.TryParse(row.Fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var clientItemId))
                {
                    throw new InvalidDataException($"Invalid exact-client {sectionName} item row {row.RecordIndex}.");
                }
                if (!exactKeys.Add((sectionName, clientItemId)))
                {
                    throw new InvalidDataException($"Duplicate exact-client item identity: {sectionName}:{clientItemId}.");
                }

                var presentInFormalBaseline = baselineKeys.Contains((sectionName, clientItemId));
                if (!presentInFormalBaseline)
                {
                    missingBindingCount++;
                }
                var authorityKey = $"client:item/{sectionName.ToLowerInvariant()}/{clientItemId}";
                records.Add(new
                {
                    id = authorityKey,
                    clientItemId,
                    serverAuthoritativeId = (int?)null,
                    idSemantics = new
                    {
                        templateId = authorityKey,
                        clientDisplayId = IntOrNull(At(row.Fields, 3)),
                        resourceIndex = NullIfEmpty(At(row.Fields, 2)),
                        itemInstanceId = (string?)null,
                        serverAuthoritativeId = (int?)null
                    },
                    itemType = sectionName,
                    subtype = At(row.Fields, 1),
                    name = At(row.Fields, 4),
                    description = At(row.Fields, 7),
                    priceCandidates = new
                    {
                        systemSell = LongOrNull(At(row.Fields, 5)),
                        systemRecycle = LongOrNull(At(row.Fields, 6)),
                        confidence = "client-display-candidate"
                    },
                    stackLimit = (int?)null,
                    equipmentSlot = (string?)null,
                    requirements = Array.Empty<object>(),
                    durability = (int?)null,
                    weight = (int?)null,
                    statCandidates = row.Fields.Skip(8).Take(6).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                    resourceReferences = Array.Empty<object>(),
                    crossReferences = new
                    {
                        skillReferences = Array.Empty<object>(),
                        questReferences = Array.Empty<object>(),
                        merchantReferences = Array.Empty<object>()
                    },
                    sourcePath = RelativeSourcePath,
                    sourceSha256 = document.SourceHash,
                    sourceRecordIndex = row.RecordIndex,
                    rawFields = row.Fields,
                    presentInFormalBaseline,
                    formalCanonicalBindingStatus = presentInFormalBaseline
                        ? "ExistingFormalBaselineIdentity"
                        : "EvidenceBlockedMissingCanonicalItemBinding",
                    runtimeEligible = false,
                    unknownFields = new[]
                    {
                        "stackLimit", "equipmentSlot", "requirements", "durability", "weight", "serverAuthoritativeId",
                        "itemInstanceId", "columnsAfterKnownDisplayFields", "gameplaySemanticValidation", "packetRuntimeValidation"
                    },
                    confidence = "verified-exact-current-client-static-row"
                });
            }
        }

        if (records.Count != 17418 || exactKeys.Count != 17418 || missingBindingCount != 11)
        {
            throw new InvalidDataException($"Exact-current item catalog split is invalid; rows={records.Count}, unique={exactKeys.Count}, missingBinding={missingBindingCount}.");
        }

        var root = new
        {
            category = "items",
            source = RelativeSourcePath,
            format = "Decoded exact-current 05 16 LZSS + CP936 table sections",
            sourceSizeBytes = sourceInfo.Length,
            recordCount = records.Count,
            existingFormalBaselineCount = records.Count - missingBindingCount,
            missingCanonicalBindingCount = missingBindingCount,
            recoveredFields = new[]
            {
                "clientItemId", "confidence", "crossReferences", "description", "id", "idSemantics", "itemType", "name",
                "priceCandidates", "rawFields", "resourceReferences", "sourcePath", "sourceRecordIndex", "sourceSha256", "subtype",
                "presentInFormalBaseline", "formalCanonicalBindingStatus", "runtimeEligible"
            },
            missingFields = new[]
            {
                "durability", "equipmentSlot", "itemInstanceId", "requirements", "serverAuthoritativeId", "stackLimit", "weight",
                "crossReferenceEvidence", "gameplaySemanticValidation", "packetRuntimeValidation"
            },
            verificationStatus = "VerifiedExactCurrentClientStaticCatalog; GameplaySemanticsUnverified",
            recoveryProgress = "All 17,418 exact-current item identities and raw rows recovered; 17,407 overlap the formal baseline and 11 remain separately binding-blocked.",
            canDirectImportToGameplay = false,
            notes = "Exact client identity/resource/name/raw flags are static evidence. Server template binding, acquisition, effect, stack limit, response and persistence remain gated.",
            records
        };

        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await File.WriteAllTextAsync(fullOutputPath, RecoveryJson.Serialize(root) + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
        return new(records.Count, records.Count - missingBindingCount, missingBindingCount, document.SourceHash, sourceInfo.Length, fullOutputPath);
    }

    private static HashSet<(string Section, int ClientItemId)> ReadPersistedFormalBaselineKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("records").EnumerateArray()
            .Where(record => !record.TryGetProperty("presentInFormalBaseline", out var present) || present.GetBoolean())
            .Select(record => (
                record.GetProperty("itemType").GetString() ?? throw new InvalidDataException("Persisted item type is missing."),
                record.GetProperty("clientItemId").GetInt32()))
            .ToHashSet();
    }

    private static string At(IReadOnlyList<string> fields, int index) => index < fields.Count ? fields[index] : string.Empty;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static int? IntOrNull(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static long? LongOrNull(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
