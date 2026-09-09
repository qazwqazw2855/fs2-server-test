using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.GameplayContentRecovery;

public sealed record OfficialNpcCatalogMigrationResult(
    int AppearanceCount,
    int ValidSelectorCount,
    int CoordinateCandidateCount,
    int ExactIdentityMatchCount,
    int EnabledSpawnCount,
    int UnsupportedFamilyCount,
    int AmbiguousOrConflictingCount,
    string GameDataSha256,
    string DayMissionSha256,
    string OutputPath);

public static class OfficialNpcCatalogMigrationWriter
{
    private const string ClientBuildId = "god2-opt-6b127086e0c0";
    private const string Type0OpaqueHex = "000000CF010000";
    private const string Type0OpaqueSha256 = "C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838";
    private const string Type2OpaqueHex = "EA680FC2034A01";
    private const string Type2OpaqueSha256 = "C0CBF5CDFAC3669D695E66D9C789AAA027360488024C1C5E4323311364A419C0";
    private static readonly HashSet<int> IndividuallyCapturedHandles = [1504, 3793, 3954, 4638];
    private static readonly Regex Coordinate = new(
        @"(?<map>[^\d\r\n()（）]+?)\s+(?<npc>[^\d\r\n()（）]+?)[(（]\s*(?<x>\d+)\s*[/,，]\s*(?<y>\d+)\s*[)）]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<OfficialNpcCatalogMigrationResult> WriteAsync(
        string repositoryRoot,
        string clientRoot,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(clientRoot);
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var gameData = await God2PackedFile.ReadCsvZAsync(
            Path.Combine(root, "Data2", "Patch", "Comm", "gamedata.csvZ"), cancellationToken);
        var sections = GameDataSections.Parse(gameData.Rows);
        var appearances = ParseAppearances(sections["NPCAppearData"], localization, gameData.SourceHash);
        var maps = await ParseMapsAsync(root, sections, localization, gameData.SourceHash, cancellationToken);

        var dayMission = await God2PackedFile.ReadCsvZAsync(
            Path.Combine(root, "Data2", "Patch", "Comm", "DayMissionDesc.csvZ"), cancellationToken);
        var coordinates = ParseDayMissionCoordinates(dayMission, localization)
            .Concat(await ParseGuideCoordinatesAsync(repositoryRoot, cancellationToken))
            .ToArray();
        var matchedCoordinates = MatchCoordinates(appearances, maps, coordinates, out var exactMatches);
        var selected = SelectRuntimeSpawns(matchedCoordinates, out var rejected);

        var sql = BuildSql(appearances, matchedCoordinates, selected, gameData.SourceHash, dayMission.SourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, sql, new UTF8Encoding(false), cancellationToken);

        return new OfficialNpcCatalogMigrationResult(
            appearances.Count,
            appearances.Count(row => row.SelectorValid),
            coordinates.Length,
            exactMatches,
            selected.Count,
            appearances.Count(row => row.ResourceType is not (0 or 2)),
            rejected,
            gameData.SourceHash,
            dayMission.SourceHash,
            Path.GetFullPath(outputPath));
    }

    internal static IReadOnlyList<NpcAppearance> ParseAppearances(
        GameDataSection section,
        ZhTwLocalization localization,
        string sourceHash) =>
        section.Rows.Select(row =>
        {
            var handleValid = int.TryParse(row.Fields.ElementAtOrDefault(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle) && handle > 0;
            var selectorValid = int.TryParse(row.Fields.ElementAtOrDefault(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var selector) &&
                selector > 0 && selector % 1000 is >= 1 and <= 255;
            var original = row.Fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
            var localized = localization.Convert(original, sourceHash).ConvertedText.Trim();
            return new NpcAppearance(
                handleValid ? handle : 0,
                original,
                localized,
                selectorValid ? selector : 0,
                selectorValid ? selector / 1000 : -1,
                selectorValid ? selector % 1000 - 1 : -1,
                row.RecordIndex,
                JsonSerializer.Serialize(row.Fields),
                handleValid && selectorValid);
        }).ToArray();

    private static async Task<IReadOnlyList<OfficialMap>> ParseMapsAsync(
        string clientRoot,
        IReadOnlyDictionary<string, GameDataSection> sections,
        ZhTwLocalization localization,
        string sourceHash,
        CancellationToken cancellationToken)
    {
        var resources = (await ClientMapResourceInventory.BuildAsync(clientRoot, cancellationToken)).Resources
            .ToDictionary(resource => resource.AuthorityKey, StringComparer.OrdinalIgnoreCase);
        return OfficialClientExtractor.ParseOfficialMapDefinitions(sections["Map_City_Coordniate"])
            .Select(definition =>
            {
                resources.TryGetValue(definition.ResourceAuthorityKey, out var resource);
                return new OfficialMap(
                    OfficialMapMigrationWriter.ResolveCanonicalMapId(definition.ClientAreaId, definition.ClientMapId),
                    definition.DisplayName,
                    localization.Convert(definition.DisplayName, sourceHash).ConvertedText.Trim(),
                    resource?.GridWidth is > 0 ? checked(resource.GridWidth.Value * 21 - 1) : null,
                    resource?.GridHeight is > 0 ? checked(resource.GridHeight.Value * 21 - 1) : null);
            }).ToArray();
    }

    private static IReadOnlyList<NpcCoordinate> ParseDayMissionCoordinates(
        CsvZDocument document,
        ZhTwLocalization localization)
    {
        var rows = new List<NpcCoordinate>();
        foreach (var row in document.Rows)
        {
            foreach (var text in row.Fields.Where(value => Coordinate.IsMatch(value)))
            {
                foreach (Match match in Coordinate.Matches(text))
                {
                    rows.Add(new NpcCoordinate(
                        Normalize(localization.Convert(match.Groups["npc"].Value.Trim(), document.SourceHash).ConvertedText),
                        Normalize(localization.Convert(match.Groups["map"].Value.Trim(), document.SourceHash).ConvertedText),
                        int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
                        int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                        2,
                        "OfficialDayMissionDesc",
                        $"Data2/Patch/Comm/DayMissionDesc.csvZ:{row.RecordIndex}",
                        document.SourceHash,
                        "Derived"));
                }
            }
        }

        return rows.Distinct().ToArray();
    }

    private static async Task<IReadOnlyList<NpcCoordinate>> ParseGuideCoordinatesAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(repositoryRoot, "Artifacts", "GameplayContentRecoveryPhase1", "verified-guide-evidence.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var rows = new List<NpcCoordinate>();
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (Text(row, "domain") != "NpcSpawnEvidence" || !row.TryGetProperty("fields", out var fields) ||
                !Number(fields, "x", out var x) || !Number(fields, "y", out var y))
            {
                continue;
            }

            var npc = Text(fields, "npcNameZhTw") ?? Text(row, "name");
            var map = Text(fields, "mapNameZhTw") ?? Text(fields, "mapName");
            if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(map))
            {
                continue;
            }

            rows.Add(new NpcCoordinate(
                Normalize(npc),
                Normalize(map),
                x,
                y,
                1,
                "BahamutGuideCoordinate",
                $"{Text(row, "sourceFile")}:{Number(row, "sourceRow")}",
                Text(row, "sourceHash") ?? string.Empty,
                "Candidate"));
        }

        return rows.Distinct().ToArray();
    }

    private static IReadOnlyList<MatchedNpcCoordinate> MatchCoordinates(
        IReadOnlyList<NpcAppearance> appearances,
        IReadOnlyList<OfficialMap> maps,
        IReadOnlyList<NpcCoordinate> coordinates,
        out int exactMatches)
    {
        var appearanceByName = appearances.Where(row => row.Valid && !string.IsNullOrWhiteSpace(row.NameZhTw))
            .GroupBy(row => Normalize(row.NameZhTw), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var mapByName = maps.SelectMany(map => new[] { map.NameOriginal, map.NameZhTw }.Select(Normalize).Distinct().Select(name => (name, map)))
            .GroupBy(pair => pair.name, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.map.MapId).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().map, StringComparer.Ordinal);

        var matched = new List<MatchedNpcCoordinate>();
        foreach (var coordinate in coordinates)
        {
            if (!appearanceByName.TryGetValue(coordinate.NpcName, out var identities) || identities.Length != 1 ||
                !mapByName.TryGetValue(coordinate.MapName, out var map))
            {
                continue;
            }

            matched.Add(new MatchedNpcCoordinate(identities[0], map, coordinate));
        }

        exactMatches = matched.Count;
        return matched;
    }

    private static IReadOnlyList<RuntimeNpcSpawn> SelectRuntimeSpawns(
        IReadOnlyList<MatchedNpcCoordinate> matched,
        out int rejected)
    {
        var selected = new List<RuntimeNpcSpawn>();
        rejected = 0;
        foreach (var group in matched.GroupBy(row => row.Appearance.Handle))
        {
            var highestPriority = group.Max(row => row.Coordinate.Priority);
            var best = group.Where(row => row.Coordinate.Priority == highestPriority)
                .DistinctBy(row => (row.Map.MapId, row.Coordinate.X, row.Coordinate.Y))
                .ToArray();
            if (best.Length != 1)
            {
                rejected++;
                continue;
            }

            var row = best[0];
            if (IndividuallyCapturedHandles.Contains(row.Appearance.Handle) ||
                row.Appearance.ResourceType is not (0 or 2) ||
                !string.Equals(row.Coordinate.Status, "Derived", StringComparison.Ordinal) ||
                row.Coordinate.X < 0 || row.Coordinate.Y < 0 ||
                row.Map.MaximumX is not { } maximumX || row.Map.MaximumY is not { } maximumY ||
                row.Coordinate.X > maximumX || row.Coordinate.Y > maximumY)
            {
                rejected++;
                continue;
            }

            selected.Add(new RuntimeNpcSpawn(
                row.Appearance,
                row.Map,
                row.Coordinate,
                ComputeSpawnHash(row.Appearance, row.Coordinate.X, row.Coordinate.Y)));
        }

        return selected.OrderBy(row => row.Appearance.Handle).ToArray();
    }

    private static string BuildSql(
        IReadOnlyList<NpcAppearance> appearances,
        IReadOnlyList<MatchedNpcCoordinate> matchedCoordinates,
        IReadOnlyList<RuntimeNpcSpawn> spawns,
        string gameDataSha,
        string dayMissionSha)
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- Migration 116: official NPC appearance catalog plus strictly cross-matched coordinate evidence.");
        sql.AppendLine("-- Only resource families 0 and 2 are runtime-enabled because each family has two exact observed 0x72 profiles.");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_game`.`npc_appearance_identities` (");
        sql.AppendLine("    `appearance_identity_id` bigint NOT NULL,`client_build_id` varchar(64) NOT NULL,`client_entity_handle` int unsigned NOT NULL,");
        sql.AppendLine("    `name_zh_tw` varchar(150) NOT NULL,`official_selector` int unsigned NOT NULL,");
        sql.AppendLine("    `official_resource_type` tinyint unsigned NOT NULL,`official_resource_ordinal` tinyint unsigned NOT NULL,");
        sql.AppendLine("    `source_row` int NOT NULL,`source_sha256` char(64) NOT NULL,`raw_fields_json` json NOT NULL,");
        sql.AppendLine("    `identity_evidence_status` varchar(30) NOT NULL,`runtime_family_status` varchar(30) NOT NULL,`enabled` tinyint(1) NOT NULL DEFAULT 1,");
        sql.AppendLine("    PRIMARY KEY (`appearance_identity_id`),UNIQUE KEY `ux_npc_appearance_handle` (`client_build_id`,`client_entity_handle`),");
        sql.AppendLine("    KEY `ix_npc_appearance_name` (`name_zh_tw`),KEY `ix_npc_appearance_selector` (`official_resource_type`,`official_resource_ordinal`),");
        sql.AppendLine("    CONSTRAINT `ck_npc_appearance_enabled` CHECK (`enabled` IN (0,1))");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Official NPCAppearData identity catalog';");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`npc_appearance_identities` (`appearance_identity_id`,`client_build_id`,`client_entity_handle`,`name_zh_tw`,`official_selector`,`official_resource_type`,`official_resource_ordinal`,`source_row`,`source_sha256`,`raw_fields_json`,`identity_evidence_status`,`runtime_family_status`,`enabled`) VALUES");
        for (var index = 0; index < appearances.Count; index++)
        {
            var row = appearances[index];
            sql.Append("    (").Append(1_320_000_000L + row.Handle).Append(',').Append(Q(ClientBuildId)).Append(',').Append(row.Handle).Append(',')
                .Append(Q(row.NameZhTw)).Append(',').Append(row.Selector).Append(',')
                .Append(row.ResourceType).Append(',').Append(row.ResourceOrdinal).Append(',').Append(row.SourceRow).Append(',').Append(Q(gameDataSha)).Append(',')
                .Append(Q(row.RawFieldsJson)).Append(",'Verified',")
                .Append(row.ResourceType is 0 or 2 ? "'Derived'" : "'EvidenceBlocked'").Append(",1)")
                .AppendLine(index + 1 == appearances.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`),`official_selector`=VALUES(`official_selector`),`official_resource_type`=VALUES(`official_resource_type`),`official_resource_ordinal`=VALUES(`official_resource_ordinal`),`source_row`=VALUES(`source_row`),`source_sha256`=VALUES(`source_sha256`),`raw_fields_json`=VALUES(`raw_fields_json`),`identity_evidence_status`=VALUES(`identity_evidence_status`),`runtime_family_status`=VALUES(`runtime_family_status`),`enabled`=VALUES(`enabled`);");
        sql.AppendLine();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `god2_game`.`npc_coordinate_evidence` (");
        sql.AppendLine("    `coordinate_evidence_id` bigint NOT NULL,`client_build_id` varchar(64) NOT NULL,`client_entity_handle` int unsigned NOT NULL,");
        sql.AppendLine("    `map_id` bigint NOT NULL,`position_x` int NOT NULL,`position_y` int NOT NULL,`source_kind` varchar(50) NOT NULL,");
        sql.AppendLine("    `source_reference` varchar(512) NOT NULL,`source_sha256` char(64) NOT NULL,`evidence_status` varchar(30) NOT NULL,");
        sql.AppendLine("    `runtime_enabled` tinyint(1) NOT NULL DEFAULT 0,PRIMARY KEY (`coordinate_evidence_id`),");
        sql.AppendLine("    KEY `ix_npc_coordinate_handle` (`client_build_id`,`client_entity_handle`),KEY `ix_npc_coordinate_map` (`map_id`),");
        sql.AppendLine("    CONSTRAINT `fk_npc_coordinate_appearance` FOREIGN KEY (`client_build_id`,`client_entity_handle`) REFERENCES `god2_game`.`npc_appearance_identities` (`client_build_id`,`client_entity_handle`),");
        sql.AppendLine("    CONSTRAINT `fk_npc_coordinate_map` FOREIGN KEY (`map_id`) REFERENCES `god2_game`.`maps` (`map_id`),");
        sql.AppendLine("    CONSTRAINT `ck_npc_coordinate_runtime` CHECK (`runtime_enabled`=0 OR `evidence_status` IN ('Verified','Derived'))");
        sql.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Cross-matched NPC coordinate evidence; Candidate rows remain disabled';");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`npc_coordinate_evidence` (`coordinate_evidence_id`,`client_build_id`,`client_entity_handle`,`map_id`,`position_x`,`position_y`,`source_kind`,`source_reference`,`source_sha256`,`evidence_status`,`runtime_enabled`) VALUES");
        for (var index = 0; index < matchedCoordinates.Count; index++)
        {
            var row = matchedCoordinates[index];
            var runtimeEnabled = spawns.Any(spawn =>
                spawn.Appearance.Handle == row.Appearance.Handle &&
                spawn.Map.MapId == row.Map.MapId &&
                spawn.Coordinate.X == row.Coordinate.X &&
                spawn.Coordinate.Y == row.Coordinate.Y &&
                string.Equals(spawn.Coordinate.SourceReference, row.Coordinate.SourceReference, StringComparison.Ordinal));
            var stableId = StableCoordinateId(row.Appearance.Handle, row.Map.MapId, row.Coordinate.X, row.Coordinate.Y, row.Coordinate.SourceReference);
            sql.Append("    (").Append(stableId).Append(',').Append(Q(ClientBuildId)).Append(',').Append(row.Appearance.Handle).Append(',').Append(row.Map.MapId).Append(',')
                .Append(row.Coordinate.X).Append(',').Append(row.Coordinate.Y).Append(',').Append(Q(row.Coordinate.SourceKind)).Append(',')
                .Append(Q(row.Coordinate.SourceReference)).Append(',').Append(Q(row.Coordinate.SourceSha256)).Append(',').Append(Q(row.Coordinate.Status)).Append(',')
                .Append(runtimeEnabled ? "1)" : "0)").AppendLine(index + 1 == matchedCoordinates.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE `client_entity_handle`=VALUES(`client_entity_handle`),`map_id`=VALUES(`map_id`),`position_x`=VALUES(`position_x`),`position_y`=VALUES(`position_y`),`source_kind`=VALUES(`source_kind`),`source_reference`=VALUES(`source_reference`),`source_sha256`=VALUES(`source_sha256`),`evidence_status`=VALUES(`evidence_status`),`runtime_enabled`=VALUES(`runtime_enabled`);");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`npcs` (`npc_id`,`code`,`name_zh_tw`,`npc_type`,`resource_id`,`resource_key`,`interaction_family`,`default_dialog_id`,`merchant_id`,`quest_provider`,`evidence_status`,`enabled`,`admin_note`) VALUES");
        for (var index = 0; index < spawns.Count; index++)
        {
            var row = spawns[index];
            sql.Append("    (").Append(1_300_000_000L + row.Appearance.Handle).Append(',').Append(Q($"official_npc_handle_{row.Appearance.Handle}")).Append(',')
                .Append(Q(row.Appearance.NameZhTw)).Append(",'WorldNpc',NULL,")
                .Append(Q($"official-resource/type-{row.Appearance.ResourceType}/ordinal-{row.Appearance.ResourceOrdinal}"))
                .Append(",'Unknown',NULL,NULL,0,'Derived',1,")
                .Append(Q($"Migration 116 exact name/map cross-match; services remain evidence-blocked; {row.Coordinate.SourceReference}")).Append(')')
                .AppendLine(index + 1 == spawns.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`),`resource_key`=VALUES(`resource_key`),`evidence_status`=VALUES(`evidence_status`),`enabled`=VALUES(`enabled`),`admin_note`=VALUES(`admin_note`);");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO `god2_game`.`npc_spawns` (`spawn_id`,`npc_id`,`npc_name_cache`,`map_id`,`map_name_cache`,`position_x`,`position_y`,`direction`,`instance_key`,`client_build_id`,`observed_client_entity_handle`,`official_resource_type`,`official_resource_ordinal`,`official_selector_high_bits`,`official_direction_code`,`official_state_code`,`wire_evidence_status`,`identity_evidence_status`,`coordinate_evidence_status`,`service_evidence_status`,`application_message_sha256`,`opaque_template_sha256`,`enabled`,`admin_note`) VALUES");
        for (var index = 0; index < spawns.Count; index++)
        {
            var row = spawns[index];
            var opaqueSha = row.Appearance.ResourceType == 0 ? Type0OpaqueSha256 : Type2OpaqueSha256;
            sql.Append("    (").Append(1_310_000_000L + row.Appearance.Handle).Append(',').Append(1_300_000_000L + row.Appearance.Handle).Append(',')
                .Append(Q(row.Appearance.NameZhTw)).Append(',').Append(row.Map.MapId).Append(',').Append(Q(row.Map.NameZhTw)).Append(',')
                .Append(row.Coordinate.X).Append(',').Append(row.Coordinate.Y).Append(",4,NULL,").Append(Q(ClientBuildId)).Append(',').Append(row.Appearance.Handle).Append(',')
                .Append(row.Appearance.ResourceType).Append(',').Append(row.Appearance.ResourceOrdinal).Append(",3,4,1,'Derived','Verified',")
                .Append(Q(row.Coordinate.Status)).Append(",'EvidenceBlocked',").Append(Q(row.ApplicationSha256)).Append(',').Append(Q(opaqueSha)).Append(",1,")
                .Append(Q($"{row.Coordinate.SourceKind}; {row.Coordinate.SourceReference}; source_sha={row.Coordinate.SourceSha256}")).Append(')')
                .AppendLine(index + 1 == spawns.Count ? string.Empty : ",");
        }
        sql.AppendLine("ON DUPLICATE KEY UPDATE `npc_id`=VALUES(`npc_id`),`npc_name_cache`=VALUES(`npc_name_cache`),`map_id`=VALUES(`map_id`),`map_name_cache`=VALUES(`map_name_cache`),`position_x`=VALUES(`position_x`),`position_y`=VALUES(`position_y`),`direction`=VALUES(`direction`),`official_resource_type`=VALUES(`official_resource_type`),`official_resource_ordinal`=VALUES(`official_resource_ordinal`),`official_selector_high_bits`=VALUES(`official_selector_high_bits`),`official_direction_code`=VALUES(`official_direction_code`),`official_state_code`=VALUES(`official_state_code`),`wire_evidence_status`=VALUES(`wire_evidence_status`),`identity_evidence_status`=VALUES(`identity_evidence_status`),`coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),`service_evidence_status`=VALUES(`service_evidence_status`),`application_message_sha256`=VALUES(`application_message_sha256`),`opaque_template_sha256`=VALUES(`opaque_template_sha256`),`enabled`=VALUES(`enabled`),`admin_note`=VALUES(`admin_note`);");
        sql.AppendLine();
        sql.AppendLine($"-- Official gamedata SHA-256: {gameDataSha}");
        sql.AppendLine($"-- Official DayMissionDesc SHA-256: {dayMissionSha}");
        return sql.ToString();
    }

    private static string ComputeSpawnHash(NpcAppearance appearance, int x, int y)
    {
        var decoded = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, 24);
        decoded[2] = 0x72;
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(3, 4), checked((uint)appearance.Handle));
        decoded[7] = checked((byte)(appearance.ResourceOrdinal + 1));
        decoded[8] = checked((byte)((3 << 5) | appearance.ResourceType));
        decoded[11] = (4 << 5) | 1;
        Convert.FromHexString(appearance.ResourceType == 0 ? Type0OpaqueHex : Type2OpaqueHex).CopyTo(decoded, 12);
        var positionMode = appearance.ResourceType == 0 ? 0U : 2U;
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(19, 4), checked(((uint)y << 17) | ((uint)x << 2) | positionMode));
        var checksum = 0;
        for (var index = 0; index < decoded.Length - 1; index++)
        {
            checksum = (checksum + decoded[index] + 0x3C) & 0xFF;
        }
        decoded[^1] = (byte)checksum;
        return Convert.ToHexString(SHA256.HashData(decoded.AsSpan(2, 21)));
    }

    private static long StableCoordinateId(int handle, int mapId, int x, int y, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{handle}|{mapId}|{x}|{y}|{source}"));
        return 1_330_000_000L + BinaryPrimitives.ReadUInt32LittleEndian(hash) % 600_000_000L;
    }

    private static string Normalize(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character))).Trim();
    private static string Q(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Number(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }
    private static int? Number(JsonElement element, string name) => Number(element, name, out var value) ? value : null;

    internal sealed record NpcAppearance(int Handle, string NameOriginal, string NameZhTw, int Selector, int ResourceType, int ResourceOrdinal, int SourceRow, string RawFieldsJson, bool Valid)
    {
        public bool SelectorValid => Selector > 0 && ResourceType is >= 0 and <= 6 && ResourceOrdinal is >= 0 and <= 254;
    }
    private sealed record OfficialMap(int MapId, string NameOriginal, string NameZhTw, int? MaximumX, int? MaximumY);
    private sealed record NpcCoordinate(string NpcName, string MapName, int X, int Y, int Priority, string SourceKind, string SourceReference, string SourceSha256, string Status);
    private sealed record MatchedNpcCoordinate(NpcAppearance Appearance, OfficialMap Map, NpcCoordinate Coordinate);
    private sealed record RuntimeNpcSpawn(NpcAppearance Appearance, OfficialMap Map, NpcCoordinate Coordinate, string ApplicationSha256);
}
