using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace God2.GameplayContentRecovery;

public sealed class OfficialClientExtractor
{
    private static readonly HashSet<string> InventoryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csvZ", ".ROMZ", ".ROM", ".Can", ".mbd", ".mbdZ", ".mdtZ", ".hmdZ", ".mmbZ", ".mdlZ",
        ".rtb", ".ini", ".iniZ", ".xml", ".txt", ".txtZ", ".dat", ".exe", ".dll", ".godZ", ".pkg"
    };

    private static readonly string[] ItemSections =
    [
        "WPN", "EQU", "GOD", "MAP", "MED01", "MED02", "TLI", "PET", "MAT01", "MIS", "SPI", "SKB", "PFD",
        "GWP", "GEH", "GEB", "GEQ", "PEQ", "STR", "KIT01", "KIT02", "NST", "EGG", "SPP", "CBK", "SCD",
        "EQC", "CBF01", "CAD", "BEB", "VPT", "CBF02", "ELE", "MIS02", "MED03", "TWP", "TEQ", "AMU", "BAR",
        "GEQ02", "MEQ", "COM", "NCP", "NMP", "MIS03", "UPS", "SSW", "SES"
    ];

    private readonly string _repositoryRoot;
    private readonly string _clientRoot;

    public OfficialClientExtractor(string repositoryRoot, string clientRoot)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _clientRoot = Path.GetFullPath(clientRoot);
    }

    public async Task ExtractAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_clientRoot))
        {
            throw new DirectoryNotFoundException($"Official client root is missing: {_clientRoot}");
        }

        await InventoryClientAsync(workspace, cancellationToken);
        await InventoryWorkspaceSourcesAsync(workspace, cancellationToken);

        var csvDocuments = new Dictionary<string, CsvZDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_clientRoot, "*.csvZ", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CsvZDocument document;
            try
            {
                document = await God2PackedFile.ReadCsvZAsync(path, cancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"Official client CSVZ decode failed: {RelativeClientPath(path)}: {exception.Message}", exception);
            }
            var relative = RelativeClientPath(path);
            csvDocuments[relative] = document;
            ReplaceInventoryRecordCount(workspace, $"client:{relative}", document.Rows.Count, $"God2-0516-LZSS+{document.TextDecoder}");
        }

        workspace.Diagnostics["csvZDecoded"] = csvDocuments.Count;
        workspace.Diagnostics["csvZDecodedLengthBytes"] = csvDocuments.Values.Sum(document => (long)document.ExpectedDecodedLength);
        workspace.Diagnostics["csvZInvalidBytesEscaped"] = csvDocuments.Values.Sum(document => document.InvalidByteCount);
        workspace.Diagnostics["csvZFilesWithInvalidBytes"] = csvDocuments.Values.Count(document => document.InvalidByteCount > 0);

        var gameData = RequireCsv(csvDocuments, "Data2/Patch/Comm/gamedata.csvZ");
        var sections = GameDataSections.Parse(gameData.Rows);
        workspace.Diagnostics["gameDataSections"] = sections.ToDictionary(pair => pair.Key, pair => pair.Value.DeclaredCount, StringComparer.Ordinal);
        ExtractItems(workspace, gameData, sections);
        ExtractGameDataNpcEvidence(workspace, gameData, sections);
        ExtractEncounterNames(workspace, gameData, sections);
        ExtractNpcTemplates(workspace, RequireCsv(csvDocuments, "Data2/Patch/NPC.csvZ"));
        ExtractMonsters(workspace, RequireCsv(csvDocuments, "Data2/Patch/FightEny.csvZ"));
        ExtractSkills(workspace, csvDocuments);
        ExtractQuestAndDialogEvidence(workspace, csvDocuments);
        if (!sections.TryGetValue("Map_City_Coordniate", out var officialMapSection))
        {
            throw new InvalidDataException("Official Map_City_Coordniate section is missing from gamedata.csvZ.");
        }
        await ExtractMapResourcesAsync(workspace, officialMapSection, gameData.SourceHash, cancellationToken);
        await ExtractClientStaticReferencesAsync(workspace, cancellationToken);
    }

    private async Task InventoryClientAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var paths = Directory.EnumerateFiles(_clientRoot, "*", SearchOption.AllDirectories)
            .Where(path => InventoryExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !IsExcludedClientPath(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var records = new SourceInventoryEntry[paths.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, paths.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (index, token) =>
        {
            var path = paths[index];
            var relative = RelativeClientPath(path);
            records[index] = new SourceInventoryEntry(
                $"client:{relative}",
                SourceType(path),
                relative,
                await ContentHash.Sha256FileAsync(path, token),
                new FileInfo(path).Length,
                null,
                null,
                ClassifyClientSource(path));
        });
        workspace.Sources.AddRange(records);
    }

    private async Task InventoryWorkspaceSourcesAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var roots = new[]
        {
            Path.Combine(_repositoryRoot, "database", "schema"),
            Path.Combine(_repositoryRoot, "db", "imports", "official"),
            Path.Combine(_repositoryRoot, "db", "imports", "supplemental"),
            Path.Combine(_repositoryRoot, "src")
        };
        var files = roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".sql" or ".json" or ".cs")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(_repositoryRoot, path).Replace('\\', '/');
            workspace.Sources.Add(new SourceInventoryEntry(
                $"workspace:{relative}",
                "ExistingRepositoryData",
                relative,
                await ContentHash.Sha256FileAsync(path, cancellationToken),
                new FileInfo(path).Length,
                null,
                null,
                relative.StartsWith("database/schema/", StringComparison.Ordinal) ? "MariaDbSchema" : "ExistingContentOrRuntime"));
        }
    }

    private void ExtractItems(RecoveryWorkspace workspace, CsvZDocument gameData, IReadOnlyDictionary<string, GameDataSection> sections)
    {
        foreach (var sectionName in ItemSections)
        {
            if (!sections.TryGetValue(sectionName, out var section))
            {
                continue;
            }

            foreach (var row in section.Rows)
            {
                var fields = row.Fields;
                if (fields.Count < 5 || !int.TryParse(fields[0], out var clientItemId))
                {
                    continue;
                }

                var displaySegments = fields.Skip(7)
                    .Where(value => !string.IsNullOrWhiteSpace(value) && value.Any(IsCjkOrMarkup))
                    .ToArray();
                var authorityKey = $"client:item/{sectionName.ToLowerInvariant()}/{clientItemId}";
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["itemId"] = clientItemId,
                    ["itemFamily"] = sectionName,
                    ["subtype"] = At(fields, 1),
                    ["resourceKey"] = At(fields, 2),
                    ["displayNameKeyCandidate"] = IntOrNull(At(fields, 3)),
                    ["systemSellPriceCandidate"] = LongOrNull(At(fields, 5)),
                    ["systemRecyclePriceCandidate"] = LongOrNull(At(fields, 6)),
                    ["displaySegments"] = displaySegments,
                    ["maximumStack"] = null,
                    ["stackPolicy"] = "Unknown",
                    ["equipmentSlot"] = null,
                    ["rawFieldCount"] = fields.Count
                };
                AddEntity(workspace, new ContentEntity(
                    "Item",
                    authorityKey,
                    RelativeClientPath(gameData.Path),
                    $"gamedata:{sectionName}:{row.RecordIndex}",
                    row.RecordIndex,
                    gameData.SourceHash,
                    At(fields, 4),
                    displaySegments.Length == 0 ? null : string.Join("\n", displaySegments),
                    "zh-Hans",
                    "VerifiedDecode",
                    "PartiallyMapped",
                    normalized,
                    ["MaximumStackPolicy", "BindPolicy", "TradePolicy"],
                    "Official gamedata item row; display-bearing fields retained, numeric semantics are not defaulted."), row.RawLine, fields);
            }
        }
    }

    private void ExtractNpcTemplates(RecoveryWorkspace workspace, CsvZDocument npc)
    {
        var headerIndex = npc.Rows.ToList().FindIndex(row => row.Fields.Count > 0 && row.Fields[0].StartsWith("索引", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("NPC.csvZ header was not found.");
        }

        foreach (var row in npc.Rows.Skip(headerIndex + 1))
        {
            var fields = row.Fields;
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var index = IntOrNull(At(fields, 0));
            var authorityKey = $"client:npc-template/{(index is null ? $"row-{row.RecordIndex}" : index.Value.ToString())}";
            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["npcTemplateIdCandidate"] = index,
                ["resourceKey"] = NormalizeResource(At(fields, 1)),
                ["actionCandidate"] = At(fields, 2),
                ["paletteCandidate"] = At(fields, 3),
                ["templateXCandidate"] = IntOrNull(At(fields, 4)),
                ["templateYCandidate"] = IntOrNull(At(fields, 5)),
                ["npcTypeCode"] = IntOrNull(At(fields, 6)),
                ["npcTypeLabel"] = At(fields, 9),
                ["mapId"] = null,
                ["spawnX"] = null,
                ["spawnY"] = null
            };
            AddEntity(workspace, new ContentEntity(
                "NpcTemplate",
                authorityKey,
                RelativeClientPath(npc.Path),
                $"npc.csv:{row.RecordIndex}",
                row.RecordIndex,
                npc.SourceHash,
                At(fields, 8),
                null,
                "zh-Hans",
                "VerifiedDecode",
                "PartiallyMapped",
                normalized,
                ["AuthoritativeNpcTemplateId", "MapId", "SpawnX", "SpawnY", "InteractionBinding"],
                "NPC.csv template row; template-position columns remain candidates and are not treated as world spawn coordinates."), row.RawLine, fields);
        }
    }

    private void ExtractGameDataNpcEvidence(RecoveryWorkspace workspace, CsvZDocument gameData, IReadOnlyDictionary<string, GameDataSection> sections)
    {
        foreach (var sectionName in new[] { "NPCAppearData", "NPCList", "NPCItemList" })
        {
            if (!sections.TryGetValue(sectionName, out var section))
            {
                continue;
            }

            var domain = sectionName switch
            {
                "NPCAppearData" => "NpcAppearance",
                "NPCItemList" => "MerchantEvidence",
                _ => "NpcGrouping"
            };
            foreach (var row in section.Rows)
            {
                var fields = row.Fields;
                var identity = At(fields, 0) ?? $"row-{row.RecordIndex}";
                var name = sectionName == "NPCAppearData" ? At(fields, 1) : null;
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["section"] = sectionName,
                    ["sourceIdentityCandidate"] = identity,
                    ["rawFields"] = fields,
                    ["semantics"] = sectionName == "NPCItemList" ? "Merchant/item relation candidates; column meanings unresolved" : "Client grouping/presentation evidence"
                };
                AddEntity(workspace, new ContentEntity(
                    domain,
                    $"client:gamedata:{sectionName.ToLowerInvariant()}:{identity}:{row.RecordIndex}",
                    RelativeClientPath(gameData.Path),
                    $"gamedata:{sectionName}:{row.RecordIndex}",
                    row.RecordIndex,
                    gameData.SourceHash,
                    name,
                    null,
                    "zh-Hans",
                    "VerifiedDecode",
                    "PartiallyMapped",
                    normalized,
                    ["AuthoritativeColumnSemantics", "ServerIdentityBinding"],
                    "Raw client table preserved without inferring undocumented field semantics."), row.RawLine, fields);
            }
        }
    }

    private void ExtractEncounterNames(RecoveryWorkspace workspace, CsvZDocument gameData, IReadOnlyDictionary<string, GameDataSection> sections)
    {
        if (!sections.TryGetValue("EnyNameTotal", out var section))
        {
            return;
        }

        foreach (var row in section.Rows)
        {
            var fields = row.Fields;
            if (!int.TryParse(At(fields, 0), out var encounterId) || string.IsNullOrWhiteSpace(At(fields, 1)))
            {
                continue;
            }

            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["encounterLocalId"] = encounterId,
                ["categoryOrModelCandidates"] = fields.Skip(2).ToArray(),
                ["monsterTemplateBinding"] = null
            };
            AddEntity(workspace, new ContentEntity(
                "EncounterName",
                $"client:encounter-name/{encounterId}:{row.RecordIndex}",
                RelativeClientPath(gameData.Path),
                $"gamedata:EnyNameTotal:{row.RecordIndex}",
                row.RecordIndex,
                gameData.SourceHash,
                At(fields, 1),
                null,
                "zh-Hans",
                "VerifiedDecode",
                "PartiallyMapped",
                normalized,
                ["MonsterTemplateBinding", "MapBinding", "SpawnCoordinates", "CombatStats"],
                "EnyNameTotal encounter/name row; local identity is not promoted as a server monster template ID."), row.RawLine, fields);
        }
    }

    private void ExtractMonsters(RecoveryWorkspace workspace, CsvZDocument fightEny)
    {
        var headerIndex = fightEny.Rows.ToList().FindIndex(row => row.Fields.Count > 1 && row.Fields[0].Contains("敵人", StringComparison.Ordinal) && row.Fields[1].Contains("代號", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            headerIndex = fightEny.Rows.ToList().FindIndex(row => row.Fields.Count > 1 && row.Fields[0].Contains("敌人", StringComparison.Ordinal));
        }

        if (headerIndex < 0)
        {
            throw new InvalidDataException("FightEny.csvZ header was not found.");
        }

        foreach (var row in fightEny.Rows.Skip(headerIndex + 1))
        {
            var fields = row.Fields;
            if (fields.Count < 6 || !int.TryParse(At(fields, 1), out var monsterId))
            {
                continue;
            }

            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["monsterTemplateIdCandidate"] = monsterId,
                ["animationFamilyCandidate"] = At(fields, 2),
                ["paletteCandidate"] = At(fields, 3),
                ["fileIdentityCandidate"] = At(fields, 4),
                ["resourceKey"] = NormalizeResource(At(fields, 5)),
                ["allyResourceKey"] = NormalizeResource(At(fields, 6)),
                ["level"] = null,
                ["maxHp"] = null,
                ["maxMp"] = null,
                ["mpPolicy"] = "Unknown",
                ["dropPolicy"] = "Unknown"
            };
            AddEntity(workspace, new ContentEntity(
                "MonsterTemplate",
                $"client:monster/fight-eny/{monsterId}",
                RelativeClientPath(fightEny.Path),
                $"fight-eny:{monsterId}",
                row.RecordIndex,
                fightEny.SourceHash,
                At(fields, 0),
                null,
                "zh-Hans",
                "VerifiedDecode",
                "PartiallyMapped",
                normalized,
                ["Level", "MaxHP", "MpPolicy", "CombatStats", "Spawn", "RewardProfile", "DropPolicy"],
                "FightEny client visual template; missing gameplay stats are explicitly unknown."), row.RawLine, fields);
        }
    }

    private void ExtractSkills(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> csvDocuments)
    {
        var paths = new[]
        {
            "Data/Patch/Skill.csvZ",
            "Data2/Patch/SpgEft.csvZ",
            "Data2/Patch/Comm/SpgEft.csvZ",
            "Data2/Patch/Comm/NewMount_skill_Desc.csvZ",
            "Data2/Patch/Comm/NewCombatPet_Innate.csvZ",
            "Data2/Patch/Comm/NewCombatPet_AllInnate.csvZ"
        };
        foreach (var path in paths)
        {
            if (!csvDocuments.TryGetValue(path, out var document))
            {
                continue;
            }

            var startRecord = 1;
            var first = document.Rows.FirstOrDefault()?.Fields;
            if (first is { Count: > 2 } && int.TryParse(first[2], out var declaredStart))
            {
                startRecord = Math.Max(1, declaredStart);
            }

            foreach (var row in document.Rows.Where(row => row.RecordIndex >= startRecord))
            {
                var fields = row.Fields;
                if (fields.Count < 2 || IsSectionMarker(fields))
                {
                    continue;
                }

                var clientSkillId = IntOrNull(At(fields, 1)) ?? IntOrNull(At(fields, 0));
                var name = fields.Count > 6 ? At(fields, 6) : fields.Count > 2 ? At(fields, 2) : null;
                if (string.IsNullOrWhiteSpace(name) && clientSkillId is null)
                {
                    continue;
                }

                var description = fields.Count > 7 ? At(fields, 7) : null;
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["clientSkillIdCandidate"] = clientSkillId,
                    ["sourceTable"] = path,
                    ["skillFamily"] = InferSkillFamily(name, description),
                    ["targetPolicy"] = null,
                    ["mpCost"] = null,
                    ["mpCostPolicy"] = "Unknown",
                    ["effectResourceCandidates"] = fields.Where(IsResourcePath).Select(NormalizeResource).ToArray(),
                    ["rawFieldCount"] = fields.Count
                };
                var familyKnown = normalized["skillFamily"] is not null;
                AddEntity(workspace, new ContentEntity(
                    "Skill",
                    $"client:skill/{StablePath(path)}/row-{row.RecordIndex}",
                    RelativeClientPath(document.Path),
                    $"skill-table:{path}:{row.RecordIndex}",
                    row.RecordIndex,
                    document.SourceHash,
                    name,
                    description,
                    "zh-Hans",
                    "VerifiedDecode",
                    "PartiallyMapped",
                    normalized,
                    familyKnown ? ["TargetPolicy", "MpCostPolicy", "EffectSemantics"] : ["SkillFamily", "TargetPolicy", "MpCostPolicy", "EffectSemantics"],
                    "Client skill/presentation row; only lexical family classification is derived, MP and target policy are not defaulted."), row.RawLine, fields);
            }
        }
    }

    private void ExtractQuestAndDialogEvidence(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> csvDocuments)
    {
        foreach (var pair in csvDocuments.Where(pair =>
                     pair.Key.Contains("Mission", StringComparison.OrdinalIgnoreCase) ||
                     pair.Key.EndsWith("message.csvZ", StringComparison.OrdinalIgnoreCase)))
        {
            var domain = pair.Key.EndsWith("message.csvZ", StringComparison.OrdinalIgnoreCase) ? "Dialog" : "Quest";
            foreach (var row in pair.Value.Rows)
            {
                var displayIndex = row.Fields.ToList().FindIndex(value => value.Any(IsCjkOrMarkup));
                var display = displayIndex < 0 ? null : row.Fields[displayIndex];
                if (string.IsNullOrWhiteSpace(display))
                {
                    continue;
                }

                var identity = IntOrNull(At(row.Fields, 0))?.ToString() ?? $"row-{row.RecordIndex}";
                AddEntity(workspace, new ContentEntity(
                    domain,
                    $"client:{domain.ToLowerInvariant()}/{StablePath(pair.Key)}/{identity}:{row.RecordIndex}",
                    RelativeClientPath(pair.Value.Path),
                    $"{domain.ToLowerInvariant()}:{pair.Key}:{row.RecordIndex}",
                    row.RecordIndex,
                    pair.Value.SourceHash,
                    display,
                    row.Fields.Skip(displayIndex + 1).FirstOrDefault(field => field.Any(IsCjkOrMarkup)),
                    "zh-Hans",
                    "VerifiedDecode",
                    "PartiallyMapped",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["sourceTable"] = pair.Key,
                        ["sourceIdCandidate"] = IntOrNull(At(row.Fields, 0)),
                        ["rawFields"] = row.Fields
                    },
                    ["AuthoritativeIdentity", "RuntimeBinding"],
                    "Quest/dialog display evidence; relation and state-machine semantics remain unresolved."), row.RawLine, row.Fields);
            }
        }
    }

    private async Task ExtractMapResourcesAsync(
        RecoveryWorkspace workspace,
        GameDataSection officialMapSection,
        string gameDataSourceHash,
        CancellationToken cancellationToken)
    {
        var officialDefinitions = ParseOfficialMapDefinitions(officialMapSection);
        var officialMaps = officialDefinitions
            .GroupBy(definition => definition.ResourceAuthorityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(definition => definition.SourceRow).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOfficialMaps = 0;
        foreach (var canPath in Directory.EnumerateFiles(_clientRoot, "*.Can", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var bytes = await File.ReadAllBytesAsync(canPath, cancellationToken);
            if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 8) != "CAN v1.0")
            {
                continue;
            }

            var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
            if (count < 0 || 12L + count * 64L > bytes.LongLength)
            {
                throw new InvalidDataException($"Invalid CAN record count in {canPath}.");
            }

            var canHash = await ContentHash.Sha256FileAsync(canPath, cancellationToken);
            var area = Path.GetFileNameWithoutExtension(canPath).ToLowerInvariant();
            for (var index = 0; index < count; index++)
            {
                var offset = 12 + index * 64;
                var type = bytes[offset];
                var nameBytes = bytes.AsSpan(offset + 1, 63);
                var terminator = nameBytes.IndexOf((byte)0);
                var resourceName = Encoding.ASCII.GetString(terminator < 0 ? nameBytes : nameBytes[..terminator]).Replace('\\', '/').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    continue;
                }

                var authorityKey = $"client:map/{area}/{resourceName.Replace(".mdt", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(".hmd", string.Empty, StringComparison.OrdinalIgnoreCase)}";
                if (!seen.Add(authorityKey))
                {
                    continue;
                }

                var dimensions = await ResolveMapDimensionsAsync(canPath, resourceName, cancellationToken);
                officialMaps.TryGetValue(authorityKey, out var matchingOfficialMaps);
                var officialMap = matchingOfficialMaps?.FirstOrDefault();
                if (matchingOfficialMaps is not null)
                {
                    matchedOfficialMaps += matchingOfficialMaps.Length;
                }
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["resourceKey"] = resourceName,
                    ["area"] = area,
                    ["canRecordType"] = type,
                    ["canRecordIndex"] = index,
                    ["width"] = dimensions.Width,
                    ["height"] = dimensions.Height,
                    ["walkableResourceReference"] = null,
                    ["collisionResourceReference"] = null,
                    ["worldMapCoordinate"] = officialMap is null ? null : new Dictionary<string, int>
                    {
                        ["x"] = officialMap.WorldMapX,
                        ["y"] = officialMap.WorldMapY
                    },
                    ["numericMapId"] = officialMap?.ClientMapId,
                    ["clientAreaId"] = officialMap?.ClientAreaId,
                    ["mapCategory"] = officialMap?.MapCategory,
                    ["officialCode"] = officialMap?.Code,
                    ["officialResourcePath"] = officialMap?.ResourcePath,
                    ["officialWorldWidth"] = officialMap?.WorldWidth,
                    ["officialWorldHeight"] = officialMap?.WorldHeight,
                    ["floorCount"] = officialMap?.FloorCount,
                    ["floorNames"] = officialMap?.FloorNames,
                    ["fieldMusic"] = officialMap?.FieldMusic,
                    ["battleMusic"] = officialMap?.BattleMusic,
                    ["officialSourceRow"] = officialMap?.SourceRow,
                    ["officialSourceHash"] = officialMap is null ? null : gameDataSourceHash,
                    ["officialIdentities"] = matchingOfficialMaps?.Select(definition => new Dictionary<string, object?>
                    {
                        ["clientAreaId"] = definition.ClientAreaId,
                        ["clientMapId"] = definition.ClientMapId,
                        ["displayName"] = definition.DisplayName,
                        ["worldMapX"] = definition.WorldMapX,
                        ["worldMapY"] = definition.WorldMapY,
                        ["sourceRow"] = definition.SourceRow
                    }).ToArray()
                };
                var missing = officialMap is null
                    ? new[] { "AuthoritativeMapId", "DisplayName", "DefaultSpawn", "CollisionSemantics" }
                    : dimensions.Width is null || dimensions.Height is null
                        ? new[] { "DefaultSpawn", "PortalPlacement", "NavigationDimensions" }
                        : new[] { "DefaultSpawn", "PortalPlacement" };
                AddEntity(workspace, new ContentEntity(
                    "Map",
                    authorityKey,
                    RelativeClientPath(canPath),
                    $"can:{RelativeClientPath(canPath)}:{index}",
                    index,
                    canHash,
                    officialMap?.DisplayName,
                    null,
                    officialMap is null ? "NotApplicable" : "zh-Hans",
                    officialMap is null ? "VerifiedBinaryStructure" : "VerifiedDecode",
                    officialMap is null ? "Derived" : "Verified",
                    normalized,
                    missing,
                    officialMap is null
                        ? "CAN resource relation plus navigation dimensions; official numeric map identity remains unresolved."
                        : "Official Map_City_Coordniate identity/world-map coordinate joined to CAN and MDT/MBD or HMD navigation evidence; gameplay spawn and portal placement remain gated."), resourceName, [resourceName]);
            }
        }

        workspace.Diagnostics["officialMapDefinitions"] = officialDefinitions.Count;
        workspace.Diagnostics["officialMapUniqueResources"] = officialMaps.Count;
        workspace.Diagnostics["officialMapResourcesMatched"] = matchedOfficialMaps;
        workspace.Diagnostics["officialMapResourcesUnmatched"] = officialDefinitions.Count - matchedOfficialMaps;
    }

    internal static IReadOnlyList<OfficialMapDefinition> ParseOfficialMapDefinitions(GameDataSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (!string.Equals(section.Name, "Map_City_Coordniate", StringComparison.Ordinal) || section.DeclaredCount != 144)
        {
            throw new InvalidDataException($"Expected Map_City_Coordniate with 144 rows, got {section.Name} with {section.DeclaredCount}.");
        }

        var definitions = new List<OfficialMapDefinition>(section.Rows.Count);
        foreach (var row in section.Rows)
        {
            if (row.Fields.Count < 14 ||
                !int.TryParse(row.Fields[3], out var clientAreaId) ||
                !int.TryParse(row.Fields[4], out var mapCategory) ||
                !int.TryParse(row.Fields[5], out var clientMapId) ||
                !int.TryParse(row.Fields[6], out var worldMapX) ||
                !int.TryParse(row.Fields[7], out var worldMapY) ||
                !int.TryParse(row.Fields[13], out var floorCount))
            {
                throw new InvalidDataException($"Invalid official map definition at gamedata row {row.RecordIndex}.");
            }

            var resourcePath = CorrectOfficialMapResourcePath(NormalizeOfficialMapResourcePath(row.Fields[10]));
            definitions.Add(new OfficialMapDefinition(
                row.RecordIndex,
                row.Fields[0],
                row.Fields[1],
                row.Fields[2],
                clientAreaId,
                mapCategory,
                clientMapId,
                worldMapX,
                worldMapY,
                row.Fields[8],
                row.Fields[9],
                resourcePath,
                $"client:map/{resourcePath}",
                IntOrNull(row.Fields[11]),
                IntOrNull(row.Fields[12]),
                floorCount,
                row.Fields.Skip(14).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()));
        }

        if (definitions.Select(definition => (definition.ClientAreaId, definition.ClientMapId)).Distinct().Count() != section.DeclaredCount)
        {
            throw new InvalidDataException("Official map definitions contain duplicate Client Area/Map identities.");
        }

        return definitions.AsReadOnly();
    }

    private static string NormalizeOfficialMapResourcePath(string resourcePath) => resourcePath
        .Replace('\\', '/')
        .TrimStart('/')
        .Replace(".mdt", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace(".hmd", string.Empty, StringComparison.OrdinalIgnoreCase)
        .ToLowerInvariant();

    private static string CorrectOfficialMapResourcePath(string normalizedPath) => normalizedPath switch
    {
        "south002/citys04/citys04" => "south002/mazes05/mazes05",
        "array/9tyr2/9tyr" => "array/array06/array06",
        "array/array011/array011" => "array/array11/array11",
        "array/array012/array012" => "array/array12/array12",
        "array/array013/array013" => "array/array13/array13",
        "north004/citywei01/citywie01" => "north004/citywei01/citywei01",
        "north004/citywei02/citywie02" => "north004/citywei02/citywei02",
        "north004/citywei03/citywie03" => "north004/citywei03/citywei03",
        "north004/mazewei06/mazewei07" => "north004/mazewei07/mazewei07",
        _ => normalizedPath
    };

    internal sealed record OfficialMapDefinition(
        int SourceRow,
        string RegionName,
        string Code,
        string DisplayName,
        int ClientAreaId,
        int MapCategory,
        int ClientMapId,
        int WorldMapX,
        int WorldMapY,
        string FieldMusic,
        string BattleMusic,
        string ResourcePath,
        string ResourceAuthorityKey,
        int? WorldWidth,
        int? WorldHeight,
        int FloorCount,
        IReadOnlyList<string> FloorNames);

    private async Task<(int? Width, int? Height)> ResolveMapDimensionsAsync(string canPath, string resourceName, CancellationToken cancellationToken)
    {
        var baseName = resourceName.Replace(".mdt", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(".hmd", string.Empty, StringComparison.OrdinalIgnoreCase);
        var leaf = Path.GetFileName(baseName);
        var relativeDirectory = Path.GetDirectoryName(baseName.Replace('/', Path.DirectorySeparatorChar));
        var root = Path.GetDirectoryName(canPath) ?? _clientRoot;
        var mbd = Path.Combine(root, relativeDirectory ?? string.Empty, $"{leaf}.mbd");
        foreach (var candidate in new[] { mbd, $"{mbd}Z" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(candidate, cancellationToken);
            if (candidate.EndsWith('Z'))
            {
                bytes = God2PackedFile.Decode(bytes);
            }

            if (bytes.Length >= 16 && Encoding.ASCII.GetString(bytes, 0, 8) == "MBD v1.2")
            {
                return (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4)));
            }
        }

        return (null, null);
    }

    private async Task ExtractClientStaticReferencesAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(_clientRoot, "God2_opt.exe");
        if (!File.Exists(executable))
        {
            return;
        }

        var bytes = await File.ReadAllBytesAsync(executable, cancellationToken);
        var hash = await ContentHash.Sha256FileAsync(executable, cancellationToken);
        var references = ExtractAsciiStrings(bytes)
            .Where(value => value.Contains(".csv", StringComparison.OrdinalIgnoreCase) || value.Contains(".rom", StringComparison.OrdinalIgnoreCase) || value.Contains("lang", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(2000)
            .ToArray();
        foreach (var reference in references)
        {
            AddRawOnly(workspace, "ClientStaticAnalysis", "ClientBinaryString", RelativeClientPath(executable), $"binary-string:{reference}", null, hash, "NotApplicable", reference,
                RecoveryJson.Compact(new { reference, relation = "Resource loader/path reference candidate" }), "Derived", "PartiallyMapped");
        }

        workspace.Diagnostics["clientStaticResourceReferences"] = references.Length;
    }

    private void AddEntity(RecoveryWorkspace workspace, ContentEntity entity, string rawText, IReadOnlyList<string> rawFields)
    {
        workspace.Entities.Add(entity);
        var rawMetadata = RecoveryJson.Compact(new
        {
            entity.AuthorityKey,
            rawFields,
            normalizedFields = entity.Fields,
            entity.MissingRequiredFields,
            entity.TransformationRule
        });
        AddRawOnly(workspace, entity.Domain, "OfficialClientResource", entity.SourceFile, entity.SourceIdentity, entity.SourceRow, entity.SourceHash,
            entity.OriginalLanguage, entity.Name ?? entity.Description, rawMetadata, entity.Confidence, entity.EvidenceStatus, rawText);
    }

    private static void AddRawOnly(
        RecoveryWorkspace workspace,
        string domain,
        string sourceType,
        string sourceFile,
        string sourceIdentity,
        int? sourceRow,
        string sourceHash,
        string originalLanguage,
        string? originalText,
        string metadata,
        string confidence,
        string evidenceStatus,
        string? payload = null)
    {
        var sourcePayload = payload ?? metadata;
        workspace.Raw.Add(new RawEvidenceRecord(
            ContentHash.StableId(domain, sourceFile, sourceIdentity, sourceRow, sourcePayload),
            domain,
            sourceType,
            sourceFile,
            sourceIdentity,
            sourceRow,
            null,
            sourceHash,
            originalLanguage,
            originalText,
            metadata,
            ContentHash.Sha256(sourcePayload),
            confidence,
            evidenceStatus));
    }

    private static CsvZDocument RequireCsv(IReadOnlyDictionary<string, CsvZDocument> documents, string path) =>
        documents.TryGetValue(path, out var document) ? document : throw new FileNotFoundException($"Required official client table is missing: {path}");

    private void ReplaceInventoryRecordCount(RecoveryWorkspace workspace, string identity, int count, string decoder)
    {
        var index = workspace.Sources.FindIndex(source => string.Equals(source.SourceIdentity, identity, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var source = workspace.Sources[index];
        workspace.Sources[index] = source with { RecordCount = count, Decoder = decoder };
    }

    private bool IsExcludedClientPath(string path)
    {
        var relative = RelativeClientPath(path);
        return relative.StartsWith("Save/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("logs/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("tmp/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("cursor/", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(path).Equals(".dmp", StringComparison.OrdinalIgnoreCase);
    }

    private string RelativeClientPath(string path) => Path.GetRelativePath(_clientRoot, path).Replace('\\', '/');

    private static string SourceType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csvz" => "OfficialClientPackedTable",
        ".can" or ".mbd" or ".mbdz" or ".mdtz" or ".hmdz" => "OfficialClientMapResource",
        ".rom" or ".romz" => "OfficialClientModelResource",
        ".exe" or ".dll" => "OfficialClientBinary",
        _ => "OfficialClientResource"
    };

    private static string ClassifyClientSource(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csvz" => "StructuredTable",
        ".can" or ".mbd" or ".mbdz" or ".mdtz" or ".hmdz" => "MapOrCollision",
        ".rom" or ".romz" or ".rtb" => "ModelAnimationPresentation",
        ".exe" or ".dll" => "LoaderParserStaticAnalysis",
        _ => "SupportingResource"
    };

    private static bool IsSectionMarker(IReadOnlyList<string> fields) => fields.Count == 2 && int.TryParse(fields[1], out _);

    private static string? InferSkillFamily(string? name, string? description)
    {
        var text = string.Concat(name, " ", description);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var candidate in new[] { "刀", "劍", "剑", "斧", "杖", "槍", "枪", "鞭", "弓", "飛刀", "飞刀", "金", "木", "水", "火", "土", "陣", "阵", "回復", "回复", "封印", "中毒", "麻痺", "麻痹", "混亂", "混乱", "石化" })
        {
            if (text.Contains(candidate, StringComparison.Ordinal))
            {
                return candidate switch
                {
                    "剑" => "劍",
                    "枪" => "槍",
                    "飞刀" => "飛刀",
                    "阵" => "陣法",
                    "陣" => "陣法",
                    "回复" => "回復",
                    "麻痹" => "麻痺",
                    "混乱" => "混亂",
                    _ => candidate
                };
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractAsciiStrings(byte[] bytes)
    {
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7e)
            {
                builder.Append((char)value);
            }
            else
            {
                if (builder.Length >= 6)
                {
                    yield return builder.ToString();
                }

                builder.Clear();
            }
        }

        if (builder.Length >= 6)
        {
            yield return builder.ToString();
        }
    }

    private static string StablePath(string value) => new(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

    private static bool IsResourcePath(string value) => value.Contains('\\') || value.Contains('/') || value.EndsWith(".rom", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeResource(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('\\', '/');

    private static string? At(IReadOnlyList<string> fields, int index) => index >= 0 && index < fields.Count && !string.IsNullOrWhiteSpace(fields[index]) ? fields[index].Trim() : null;

    private static int? IntOrNull(string? value) => int.TryParse(value, out var parsed) ? parsed : null;

    private static long? LongOrNull(string? value) => long.TryParse(value, out var parsed) ? parsed : null;

    private static bool IsCjkOrMarkup(char character) => character is >= '\u3400' and <= '\u9fff' or '<' or '{' or '[';
}
