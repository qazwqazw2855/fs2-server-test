using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace God2.GameplayContentRecovery;

public sealed record OfficialImmortalStaticRecord(
    string Id,
    int ClientCatalogId,
    int ClientDisplayId,
    string ResourceKey,
    string Name,
    string RawName,
    int ClientPresentationValue,
    string SourcePath,
    string SourceSha256,
    int SourceRecordIndex,
    IReadOnlyList<string> RawFields,
    string IdentityEvidenceStatus,
    string RuntimeEvidenceStatus,
    bool RuntimeEligible,
    IReadOnlyList<string> MissingRuntimeFields);

public sealed record OfficialImmortalCatalog(
    string Category,
    string Source,
    string Format,
    long SourceSizeBytes,
    int RecordCount,
    IReadOnlyList<string> RecoveredFields,
    IReadOnlyList<string> MissingFields,
    string VerificationStatus,
    string RecoveryProgress,
    bool CanDirectImportToGameplay,
    string Notes,
    object CrossVersionCorroboration,
    IReadOnlyList<OfficialImmortalStaticRecord> Records);

public static partial class OfficialImmortalCatalogExporter
{
    private const string RelativeSourcePath = "Data2/Patch/Comm/gamedata.csvZ";

    public static async Task<OfficialImmortalCatalog> WriteAsync(
        string clientRoot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(clientRoot, "Data2", "Patch", "Comm", "gamedata.csvZ");
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Official gamedata.csvZ was not found.", sourcePath);
        }

        var document = await God2PackedFile.ReadCsvZAsync(sourcePath, cancellationToken);
        var sections = GameDataSections.Parse(document.Rows);
        if (!sections.TryGetValue("GOD", out var section))
        {
            throw new InvalidDataException("The exact-client GOD section was not found.");
        }

        var localization = new ZhTwLocalization(God2Glossary.Create());
        var records = Parse(section, document.SourceHash, localization);
        var catalog = new OfficialImmortalCatalog(
            "immortals",
            RelativeSourcePath,
            "Decoded Official Client GOD Section",
            document.SourceSize,
            records.Count,
            [
                "clientCatalogId", "clientDisplayId", "resourceKey", "name",
                "clientPresentationValue", "sourceRecordIndex", "rawFields"
            ],
            [
                "loginWireResourceId", "baseHp", "baseMp", "attributes", "skills",
                "ownershipRules", "growthFormula", "spawnOrGrantRules"
            ],
            "VerifiedExactClientStaticIdentity; RuntimeSemanticsEvidenceBlocked",
            "Recovered the complete 34-row GOD static identity/resource catalog from the exact formal client table. Runtime ownership/login/stat/skill semantics remain evidence-gated.",
            false,
            "Static catalog identity is importable only into an evidence catalog. It must not create owned immortals, widen the login wire allowlist, infer HP/MP, or enable skills.",
            new
            {
                evidenceClass = "CrossVersionCorroboration",
                packageOuterSha256 = "6586d69f51bef4395b66fe2bf6da3d1228bc67a952c09751b77d220da58cac9c",
                packageNestedSha256 = "a6c150d1009c186138e70dfbb332b27a0edcc71fcfa44d3f25f4e1259d48dc28",
                packageGameDataSha256 = "1b28750be2e39056a120386d04cbaea9a4cce625075e6a95ba2ae38194bef2dd",
                matchingPrefixRows = 30,
                exactClientAdditionalRows = 4,
                boundary = "The package confirms the first 30 identity/resource rows from another client build; the exact formal-client source remains authoritative for this 34-row catalog."
            },
            records);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new InvalidDataException("Immortal catalog output directory is invalid."));
        await File.WriteAllTextAsync(
            Path.GetFullPath(outputPath),
            RecoveryJson.Serialize(catalog) + Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);
        return catalog;
    }

    public static IReadOnlyList<OfficialImmortalStaticRecord> Parse(
        GameDataSection section,
        string sourceSha256,
        ZhTwLocalization localization)
    {
        if (!string.Equals(section.Name, "GOD", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected GOD section, received {section.Name}.");
        }

        var records = new List<OfficialImmortalStaticRecord>(section.Rows.Count);
        var seenCatalogIds = new HashSet<int>();
        var seenResourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in section.Rows)
        {
            if (row.Fields.Count != 6 ||
                !int.TryParse(row.Fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var catalogId) ||
                !int.TryParse(row.Fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var displayId) ||
                !int.TryParse(row.Fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var presentationValue) ||
                !GodResourceKeyPattern().IsMatch(row.Fields[2]) ||
                string.IsNullOrWhiteSpace(row.Fields[4]))
            {
                throw new InvalidDataException($"Invalid GOD row at source record {row.RecordIndex}.");
            }

            if (!seenCatalogIds.Add(catalogId) || !seenResourceKeys.Add(row.Fields[2]))
            {
                throw new InvalidDataException($"Duplicate GOD identity at source record {row.RecordIndex}.");
            }

            var localizedName = localization.Convert(row.Fields[4], sourceSha256);
            if (localizedName.ConversionStatus == "ConversionFailed")
            {
                throw new InvalidDataException($"GOD name localization failed at source record {row.RecordIndex}.");
            }

            records.Add(new OfficialImmortalStaticRecord(
                $"client:immortal:god/{catalogId}",
                catalogId,
                displayId,
                row.Fields[2],
                localizedName.ConvertedText,
                row.Fields[4],
                presentationValue,
                RelativeSourcePath,
                sourceSha256,
                row.RecordIndex,
                row.Fields,
                "VerifiedExactClientStaticIdentity",
                "EvidenceBlockedMissingLoginStatsOwnershipAndSkills",
                false,
                ["loginWireResourceId", "baseHp", "baseMp", "attributes", "skills", "ownershipRules"]));
        }

        return records;
    }

    [GeneratedRegex("^God\\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex GodResourceKeyPattern();
}
