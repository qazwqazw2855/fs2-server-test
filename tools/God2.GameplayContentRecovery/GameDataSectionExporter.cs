using System.Text;

namespace God2.GameplayContentRecovery;

public sealed record GameDataSectionExportRow(
    int SourceLine,
    int SectionRowIndex,
    IReadOnlyList<string> Fields);

public sealed record GameDataSectionExportResult(
    string Section,
    string SourcePath,
    string SourceSha256,
    int MarkerSourceLine,
    int DeclaredCount,
    int ExportedCount,
    string OutputPath,
    IReadOnlyList<GameDataSectionExportRow> Rows);

public static class GameDataSectionExporter
{
    public static async Task<GameDataSectionExportResult> WriteAsync(
        string clientRoot,
        string sectionName,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            throw new ArgumentException("A gamedata section name is required.", nameof(sectionName));
        }

        var sourcePath = Path.Combine(clientRoot, "Data2", "Patch", "Comm", "gamedata.csvZ");
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Official gamedata.csvZ was not found.", sourcePath);
        }

        var document = await God2PackedFile.ReadCsvZAsync(sourcePath, cancellationToken);
        var sections = GameDataSections.Parse(document.Rows);
        if (!sections.TryGetValue(sectionName, out var section))
        {
            throw new InvalidDataException($"Official gamedata section was not found: {sectionName}.");
        }

        var rows = section.Rows.Select((row, index) => new GameDataSectionExportRow(
            row.RecordIndex,
            index,
            row.Fields)).ToArray();
        var result = new GameDataSectionExportResult(
            section.Name,
            "Data2/Patch/Comm/gamedata.csvZ",
            document.SourceHash,
            section.MarkerRecordIndex,
            section.DeclaredCount,
            rows.Length,
            Path.GetFullPath(outputPath),
            rows);

        Directory.CreateDirectory(Path.GetDirectoryName(result.OutputPath)
            ?? throw new InvalidDataException("Gamedata section output directory is invalid."));
        await File.WriteAllTextAsync(
            result.OutputPath,
            RecoveryJson.Serialize(result),
            new UTF8Encoding(false),
            cancellationToken);
        return result;
    }
}
