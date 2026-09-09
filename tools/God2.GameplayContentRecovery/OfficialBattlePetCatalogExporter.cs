using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace God2.GameplayContentRecovery;

public sealed record OfficialBattlePetStaticRecord(
    string Id,
    int ClientCatalogId,
    string RawSubtype,
    string ResourceKey,
    int ClientDisplayId,
    string Name,
    string RawName,
    string SourcePath,
    string SourceSha256,
    int SourceRecordIndex,
    IReadOnlyList<string> RawFields,
    string IdentityEvidenceStatus,
    string RuntimeEvidenceStatus,
    bool RuntimeEligible,
    IReadOnlyList<string> MissingRuntimeFields);

public sealed record OfficialBattlePetCatalog(
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
    IReadOnlyList<OfficialBattlePetStaticRecord> Records);

public static partial class OfficialBattlePetCatalogExporter
{
    private const string RelativeSourcePath = "Data2/Patch/Comm/gamedata.csvZ";

    public static async Task<OfficialBattlePetCatalog> WriteAsync(
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
        if (!sections.TryGetValue("PET", out var section))
        {
            throw new InvalidDataException("The exact-client PET section was not found.");
        }

        var records = Parse(section, document.SourceHash, new ZhTwLocalization(God2Glossary.Create()));
        var catalog = new OfficialBattlePetCatalog(
            "battle_pets",
            RelativeSourcePath,
            "Decoded Official Client PET Section",
            document.SourceSize,
            records.Count,
            ["clientCatalogId", "rawSubtype", "resourceKey", "clientDisplayId", "name", "sourceRecordIndex", "rawFields"],
            ["speciesAuthorityId", "baseStats", "growth", "skills", "ownershipRules", "feedingRules", "battleWireProfile"],
            "VerifiedExactClientStaticIdentity; RuntimeSemanticsEvidenceBlocked",
            "Recovered the complete 178-row PET static identity/resource catalog from the exact formal client table. All gameplay semantics remain evidence-gated.",
            false,
            "Only identity/resource/name and raw client fields are classified. Do not create owned pets, infer stats/growth/skills, or enable battle replication from this catalog.",
            new
            {
                evidenceClass = "CrossVersionCorroboration",
                packageOuterSha256 = "6586d69f51bef4395b66fe2bf6da3d1228bc67a952c09751b77d220da58cac9c",
                packageNestedSha256 = "a6c150d1009c186138e70dfbb332b27a0edcc71fcfa44d3f25f4e1259d48dc28",
                packageGameDataSha256 = "1b28750be2e39056a120386d04cbaea9a4cce625075e6a95ba2ae38194bef2dd",
                crossVersionPetRows = 94,
                exactClientPetRows = records.Count,
                crossVersionFightPetSkillRows = 198,
                exactClientFightPetSkillRows = 283,
                boundary = "The package supplies older cross-version table coverage. This catalog is generated only from the exact formal-client PET section; skill semantics are not promoted."
            },
            records);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new InvalidDataException("Battle-pet catalog output directory is invalid."));
        await File.WriteAllTextAsync(
            Path.GetFullPath(outputPath),
            RecoveryJson.Serialize(catalog) + Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);
        return catalog;
    }

    public static IReadOnlyList<OfficialBattlePetStaticRecord> Parse(
        GameDataSection section,
        string sourceSha256,
        ZhTwLocalization localization)
    {
        if (!string.Equals(section.Name, "PET", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected PET section, received {section.Name}.");
        }

        var records = new List<OfficialBattlePetStaticRecord>(section.Rows.Count);
        var catalogIds = new HashSet<int>();
        var resourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in section.Rows)
        {
            if (row.Fields.Count < 5 ||
                !int.TryParse(row.Fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var catalogId) ||
                !int.TryParse(row.Fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var displayId) ||
                !PetResourceKeyPattern().IsMatch(row.Fields[2]) ||
                string.IsNullOrWhiteSpace(row.Fields[4]))
            {
                throw new InvalidDataException($"Invalid PET row at source record {row.RecordIndex}.");
            }

            if (!catalogIds.Add(catalogId) || !resourceKeys.Add(row.Fields[2]))
            {
                throw new InvalidDataException($"Duplicate PET identity at source record {row.RecordIndex}.");
            }

            var localizedName = localization.Convert(row.Fields[4], sourceSha256);
            if (localizedName.ConversionStatus == "ConversionFailed")
            {
                throw new InvalidDataException($"PET name localization failed at source record {row.RecordIndex}.");
            }

            records.Add(new OfficialBattlePetStaticRecord(
                $"client:battle-pet:pet/{catalogId}",
                catalogId,
                row.Fields[1],
                row.Fields[2],
                displayId,
                localizedName.ConvertedText,
                row.Fields[4],
                RelativeSourcePath,
                sourceSha256,
                row.RecordIndex,
                row.Fields,
                "VerifiedExactClientStaticIdentity",
                "EvidenceBlockedMissingStatsGrowthSkillsOwnershipAndWire",
                false,
                ["speciesAuthorityId", "baseStats", "growth", "skills", "ownershipRules", "battleWireProfile"]));
        }

        return records;
    }

    [GeneratedRegex("^Pet\\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex PetResourceKeyPattern();
}
