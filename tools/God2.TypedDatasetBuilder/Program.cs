using System.Globalization;
using System.Text;
using System.Text.Json;

namespace God2.TypedDatasetBuilder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var root = Path.GetFullPath(ParseValue(args, "--repository-root") ?? Directory.GetCurrentDirectory());
        var artifactRoot = Path.Combine(root, "Artifacts", "RecoveryFinal");
        var inventoryPath = Path.Combine(artifactRoot, "god2-client-source-inventory.csv");
        Directory.CreateDirectory(artifactRoot);

        if (!File.Exists(inventoryPath))
        {
            Console.Error.WriteLine("Recovery inventory is missing. Run God2.ExistingClientDataRecovery first.");
            return 2;
        }

        var rows = ReadCsv(inventoryPath);
        var passRows = rows.Where(row => Get(row, "Recoverability") == "RecoverableFromGod2").ToList();
        var blockedRows = rows.Where(row => Get(row, "Recoverability") != "RecoverableFromGod2").ToList();

        var blockedPath = Path.Combine(artifactRoot, "typed-dataset-builder-blocked-datasets.csv");
        var blockedBuilder = new StringBuilder();
        blockedBuilder.AppendLine("DatasetKey,Domain,SourceFile,Recoverability,BlockReason");
        foreach (var row in blockedRows)
        {
            blockedBuilder.AppendCsvLine(
                Get(row, "DatasetKey"),
                Get(row, "Domain"),
                Get(row, "SourceFile"),
                Get(row, "Recoverability"),
                Get(row, "BlockReason"));
        }
        await File.WriteAllTextAsync(blockedPath, blockedBuilder.ToString(), new UTF8Encoding(false));

        var report = $"""
            # Typed Dataset Builder Report

            Status: BLOCKED - FORMAL DATABASE NOT READY

            | Metric | Value |
            | --- | ---: |
            | Inventory datasets | {rows.Count} |
            | PASS datasets accepted for typed records | {passRows.Count} |
            | BLOCKED datasets rejected | {blockedRows.Count} |
            | Formal records emitted | 0 |
            | Formal dataset artifact directories emitted | 0 |

            The builder did not emit `schema.md`, `records.csv`, `records.json`, or `source-map.csv` for any dataset because no dataset reached the contract's PASS gate. This is intentional fail-closed behavior, not a partial recovery.
            """;
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "typed-dataset-builder-report.md"), report, new UTF8Encoding(false));

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "BLOCKED - FORMAL DATABASE NOT READY",
            inventoryDatasetCount = rows.Count,
            passDatasetCount = passRows.Count,
            blockedDatasetCount = blockedRows.Count,
            formalRecordsEmitted = 0,
            artifactRoot
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        return passRows.Count == rows.Count && passRows.Count > 0 ? 0 : 4;
    }

    private static string? ParseValue(string[] args, string key)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : string.Empty;

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false));
        var header = SplitCsvLine(reader.ReadLine() ?? string.Empty);
        var rows = new List<Dictionary<string, string>>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = SplitCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Count; index++)
            {
                row[header[index]] = index < values.Count ? values[index] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (quoted)
            {
                if (ch == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}

internal static class CsvExtensions
{
    public static void AppendCsvLine(this StringBuilder builder, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(Convert.ToString(values[index], CultureInfo.InvariantCulture) ?? string.Empty));
        }

        builder.AppendLine();
    }

    private static string Escape(string value)
    {
        return value.Contains('"', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
