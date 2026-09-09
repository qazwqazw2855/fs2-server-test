using God2.GameplayContentRecovery;
using MySqlConnector;
using System.Globalization;

internal sealed record FormalLanguageFinding(
    string Schema,
    string Table,
    string Column,
    string RowIdentity,
    string FindingType,
    string Sample,
    string? TraditionalSample);

internal sealed record FormalLanguageAuditResult(
    string Status,
    int Schemas,
    int TextColumnsScanned,
    long TextRowsScanned,
    int DisplayColumnsScanned,
    int SimplifiedFindingCount,
    int MojibakeFindingCount,
    int MetadataCommentsScanned,
    IReadOnlyDictionary<string, long> SimplifiedGlyphCounts,
    bool FindingsTruncated,
    IReadOnlyList<FormalLanguageFinding> Findings);

internal static class FormalLanguageIntegrityAuditor
{
    private static readonly string[] Schemas = ["god2", "god2_game", "god2_player", "god2_game_meta"];
    private const int MaximumReportedFindings = 200;
    private static readonly string[] ForbiddenMetadataLanguageMarkers =
    [
        "簡體",
        "\u7b80\u4f53",
        "Simplified Chinese",
        "zh-CN",
        "zh_CN",
        "zh-Hans"
    ];

    public static async Task<FormalLanguageAuditResult> AuditAsync(
        MySqlConnection connection,
        ZhTwLocalization localization,
        CancellationToken cancellationToken)
    {
        var findings = new List<FormalLanguageFinding>();
        var columns = await ReadTextColumnsAsync(connection, cancellationToken);
        long rowsScanned = 0;
        var displayColumns = 0;
        var simplifiedCount = 0;
        var mojibakeCount = 0;
        var simplifiedGlyphCounts = new Dictionary<char, long>();

        foreach (var column in columns)
        {
            if (LanguageIntegrityPolicy.IsSensitiveColumn(column.Column))
            {
                continue;
            }

            var inspectSimplified = LanguageIntegrityPolicy.IsDisplayTextColumn(column.Column);
            if (inspectSimplified)
            {
                displayColumns++;
            }

            var primaryKeys = await ReadPrimaryKeysAsync(connection, column.Schema, column.Table, cancellationToken);
            var identitySql = primaryKeys.Count == 0
                ? "'<no-primary-key>'"
                : $"CONCAT_WS(CHAR(31),{string.Join(',', primaryKeys.Select(value => $"COALESCE(CAST({Quote(value)} AS CHAR),'<NULL>')"))})";
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {identitySql},{Quote(column.Column)} FROM {Quote(column.Schema)}.{Quote(column.Table)} WHERE {Quote(column.Column)} IS NOT NULL AND {Quote(column.Column)}<>'';";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsScanned++;
                var identity = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
                var value = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty;
                var mojibake = LanguageIntegrityPolicy.DetectMojibake(value);
                if (mojibake is not null)
                {
                    mojibakeCount++;
                    AddFinding(findings, column, identity, mojibake, value, null);
                }

                var simplifiedGlyphs = inspectSimplified ? localization.FindSimplifiedGlyphs(value) : string.Empty;
                if (simplifiedGlyphs.Length != 0)
                {
                    simplifiedCount++;
                    foreach (var glyph in simplifiedGlyphs)
                    {
                        simplifiedGlyphCounts[glyph] = simplifiedGlyphCounts.GetValueOrDefault(glyph) + 1;
                    }
                    var converted = localization.ConvertForFormalDatabase(value);
                    AddFinding(findings, column, identity, $"SimplifiedChineseGlyphs:{simplifiedGlyphs}", value, converted);
                }
            }
        }

        var metadataComments = await AuditMetadataCommentsAsync(
            connection,
            localization,
            findings,
            cancellationToken,
            counts =>
            {
                simplifiedCount += counts.Simplified;
                mojibakeCount += counts.Mojibake;
            });

        return new FormalLanguageAuditResult(
            simplifiedCount == 0 && mojibakeCount == 0 ? "PASS" : "FAIL",
            Schemas.Length,
            columns.Count(column => !LanguageIntegrityPolicy.IsSensitiveColumn(column.Column)),
            rowsScanned,
            displayColumns,
            simplifiedCount,
            mojibakeCount,
            metadataComments,
            simplifiedGlyphCounts.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            simplifiedCount + mojibakeCount > findings.Count,
            findings.AsReadOnly());
    }

    private static async Task<int> AuditMetadataCommentsAsync(
        MySqlConnection connection,
        ZhTwLocalization localization,
        List<FormalLanguageFinding> findings,
        CancellationToken cancellationToken,
        Action<(int Simplified, int Mojibake)> addCounts)
    {
        var inspected = 0;
        var simplified = 0;
        var mojibake = 0;

        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = """
                SELECT `TABLE_SCHEMA`,`TABLE_NAME`,`TABLE_COMMENT`
                FROM `information_schema`.`TABLES`
                WHERE `TABLE_SCHEMA` IN ('god2','god2_game','god2_player','god2_game_meta')
                  AND `TABLE_TYPE`='BASE TABLE'
                  AND `TABLE_COMMENT`<>''
                ORDER BY `TABLE_SCHEMA`,`TABLE_NAME`;
                """;
            await using var tableReader = await tableCommand.ExecuteReaderAsync(cancellationToken);
            while (await tableReader.ReadAsync(cancellationToken))
            {
                inspected++;
                var schema = tableReader.GetString(0);
                var table = tableReader.GetString(1);
                var value = tableReader.GetString(2);
                var descriptor = new TextColumn(schema, table, "<table-comment>");
                if (LanguageIntegrityPolicy.DetectMojibake(value) is { } issue)
                {
                    mojibake++;
                    AddFinding(findings, descriptor, "<table-comment>", issue, value, null);
                }

                if (ContainsForbiddenMetadataLanguageMarker(value) || localization.ContainsSimplifiedGlyph(value))
                {
                    simplified++;
                    AddFinding(
                        findings,
                        descriptor,
                        "<table-comment>",
                        "SimplifiedOrBilingualChineseMetadata",
                        value,
                        localization.ConvertForFormalDatabase(value));
                }
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`COLUMN_NAME`,column_row.`COLUMN_COMMENT`
            FROM `information_schema`.`COLUMNS` column_row
            JOIN `information_schema`.`TABLES` table_row
              ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
             AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
            WHERE column_row.`TABLE_SCHEMA` IN ('god2','god2_game','god2_player','god2_game_meta')
              AND table_row.`TABLE_TYPE`='BASE TABLE'
              AND column_row.`COLUMN_COMMENT`<>''
            ORDER BY column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`ORDINAL_POSITION`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            inspected++;
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var column = reader.GetString(2);
            var value = reader.GetString(3);
            var descriptor = new TextColumn(schema, table, column);
            if (LanguageIntegrityPolicy.DetectMojibake(value) is { } issue)
            {
                mojibake++;
                AddFinding(findings, descriptor, "<column-comment>", issue, value, null);
            }

            if (ContainsForbiddenMetadataLanguageMarker(value) || localization.ContainsSimplifiedGlyph(value))
            {
                simplified++;
                AddFinding(
                    findings,
                    descriptor,
                    "<column-comment>",
                    "SimplifiedOrBilingualChineseMetadata",
                    value,
                    localization.ConvertForFormalDatabase(value));
            }
        }

        addCounts((simplified, mojibake));
        return inspected;
    }

    private static void AddFinding(
        List<FormalLanguageFinding> findings,
        TextColumn column,
        string identity,
        string findingType,
        string value,
        string? converted)
    {
        if (findings.Count >= MaximumReportedFindings)
        {
            return;
        }

        findings.Add(new FormalLanguageFinding(
            column.Schema,
            column.Table,
            column.Column,
            identity.Replace((char)31, '|'),
            findingType,
            LanguageIntegrityPolicy.SafeSample(value),
            converted is null ? null : LanguageIntegrityPolicy.SafeSample(converted)));
    }

    private static async Task<IReadOnlyList<TextColumn>> ReadTextColumnsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`COLUMN_NAME`
            FROM `information_schema`.`COLUMNS` column_row
            JOIN `information_schema`.`TABLES` table_row
              ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
             AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
            WHERE column_row.`TABLE_SCHEMA` IN ('god2','god2_game','god2_player','god2_game_meta')
              AND table_row.`TABLE_TYPE`='BASE TABLE'
              AND column_row.`DATA_TYPE` IN ('char','varchar','tinytext','text','mediumtext','longtext')
            ORDER BY column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`ORDINAL_POSITION`;
            """;
        var result = new List<TextColumn>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TextColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    private static bool ContainsForbiddenMetadataLanguageMarker(string value) =>
        ForbiddenMetadataLanguageMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<string>> ReadPrimaryKeysAsync(
        MySqlConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_NAME`
            FROM `information_schema`.`KEY_COLUMN_USAGE`
            WHERE `CONSTRAINT_SCHEMA`=@schema AND `TABLE_NAME`=@table AND `CONSTRAINT_NAME`='PRIMARY'
            ORDER BY `ORDINAL_POSITION`;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static string Quote(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private sealed record TextColumn(string Schema, string Table, string Column);
}
