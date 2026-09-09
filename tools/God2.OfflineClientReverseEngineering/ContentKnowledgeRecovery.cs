using System.Security.Cryptography;
using System.Text.Json;
using God2.ClassicServer.Runtime;

namespace God2.OfflineClientReverseEngineering;

public sealed record MapCollisionEvidence(
    string MapResource,
    string SourceSha256,
    int MacroWidth,
    int MacroHeight,
    int Width,
    int Height,
    int BlockCount,
    int MissingBlocks,
    int WalkableCells,
    int OverlapConflicts,
    string PathResult,
    int PathLength,
    int ExpandedNodes);

public sealed record MapRecoverySnapshot(
    string SchemaVersion,
    int CandidateFileCount,
    int ParsedMapCount,
    int FailedMapCount,
    int AStarPassedCount,
    long TotalWalkableCells,
    IReadOnlyList<MapCollisionEvidence> Maps,
    IReadOnlyList<object> Failures,
    bool MovementProtocolModified);

public static class MapContentRecovery
{
    public static MapRecoverySnapshot Recover(string mapRoot)
    {
        var files = Directory.EnumerateFiles(mapRoot, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".mbd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var maps = new List<MapCollisionEvidence>();
        var failures = new List<object>();
        foreach (var file in files)
        {
            try
            {
                var map = MbdCollisionMapReader.Read(file);
                var grid = map.CreateNavigationGrid();
                var (start, goal) = FindReachablePair(grid);
                var path = start is null || goal is null
                    ? new NavigationResult(NavigationResultCode.Unreachable, [], 0, true, false)
                    : grid.FindPath(start.Value, goal.Value);
                maps.Add(new MapCollisionEvidence(
                    Path.GetRelativePath(mapRoot, file).Replace('\\', '/'),
                    map.SourceSha256,
                    map.MacroWidth,
                    map.MacroHeight,
                    map.Width,
                    map.Height,
                    map.BlockOffsets.Count,
                    map.MissingBlockCount,
                    map.WalkableCellCount,
                    map.OverlapConflictCount,
                    path.ResultCode.ToString(),
                    path.Path.Count,
                    path.ExpandedNodes));
            }
            catch (Exception exception)
            {
                failures.Add(new
                {
                    MapResource = Path.GetRelativePath(mapRoot, file).Replace('\\', '/'),
                    FailureType = exception.GetType().Name,
                    exception.Message
                });
            }
        }

        return new MapRecoverySnapshot(
            "offline-map-collision-v1",
            files.Length,
            maps.Count,
            failures.Count,
            maps.Count(item => item.PathResult == nameof(NavigationResultCode.Success)),
            maps.Sum(item => (long)item.WalkableCells),
            maps,
            failures,
            MovementProtocolModified: false);
    }

    private static (NavigationPoint? Start, NavigationPoint? Goal) FindReachablePair(NavigationGrid grid)
    {
        NavigationPoint? start = null;
        for (var y = 0; y < grid.Height && start is null; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var point = new NavigationPoint(x, y);
                if (grid.IsWalkable(point))
                {
                    start = point;
                    break;
                }
            }
        }
        if (start is null)
        {
            return (null, null);
        }

        var visited = new HashSet<NavigationPoint> { start.Value };
        var queue = new Queue<NavigationPoint>();
        queue.Enqueue(start.Value);
        var farthest = start.Value;
        var farthestDistance = 0;
        var directions = new[] { new NavigationPoint(-1, 0), new NavigationPoint(1, 0), new NavigationPoint(0, -1), new NavigationPoint(0, 1) };
        while (queue.TryDequeue(out var current) && visited.Count < 25_000)
        {
            var distance = Math.Abs(current.X - start.Value.X) + Math.Abs(current.Y - start.Value.Y);
            if (distance > farthestDistance)
            {
                farthest = current;
                farthestDistance = distance;
            }
            foreach (var direction in directions)
            {
                var next = new NavigationPoint(current.X + direction.X, current.Y + direction.Y);
                if (grid.IsWalkable(next) && visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return (start, farthest);
    }
}

public sealed record KnowledgeCategorySummary(
    string Domain,
    string SourceCategory,
    string SourceArtifact,
    int DeclaredRecordCount,
    int RelatedSourceRecordCount,
    int IndexedRecordCount,
    string VerificationStatus,
    bool CanDirectImportToGameplay,
    IReadOnlyList<string> RecoveredFields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<KnowledgeEntityIndex> Entities);

public sealed record KnowledgeEntityIndex(
    string Id,
    string? Name,
    IReadOnlyDictionary<string, string> References,
    string RecordHash);

public sealed record KnowledgeRecoverySnapshot(
    string SchemaVersion,
    int DomainCount,
    int SourceCategoryCount,
    int DeclaredRecordCount,
    int IndexedEntityCount,
    int CrossReferenceCount,
    IReadOnlyList<KnowledgeCategorySummary> Domains,
    IReadOnlyList<KnowledgeReferenceEdge> Edges);

public sealed record KnowledgeReferenceEdge(
    string SourceDomain,
    string SourceEntityId,
    string SourcePath,
    string TargetDomain,
    string TargetId,
    string ResolutionStatus);

public static class OfficialKnowledgeRecovery
{
    private static readonly IReadOnlyDictionary<string, DomainDefinition> DomainSources = new Dictionary<string, DomainDefinition>(StringComparer.Ordinal)
    {
        ["Character"] = new(["characters"], ["skills", "equipment"]),
        ["Skill"] = new(["skills"], ["effects"]),
        ["Effect"] = new(["effects"], ["skills"]),
        ["Status"] = new(["effects"], ["skills"]),
        ["Item"] = new(["items"], []),
        ["Equipment"] = new(["equipment"], ["items"]),
        ["Monster"] = new(["monsters"], ["drop_tables", "spawns"]),
        ["NPC"] = new(["npcs"], ["dialogs", "spawns"]),
        ["Quest"] = new(["quests"], ["rewards", "dialogs"]),
        ["Map"] = new(["maps"], ["spawns"]),
        ["Portal"] = new(["portals"], ["maps"]),
        ["Mount"] = new(["immortals"], ["items"]),
        ["Pet"] = new(["battle_pets"], ["monsters", "items"]),
        ["Shop"] = new(["merchants"], ["items"]),
        ["Crafting"] = new(["containers"], ["items", "rewards"])
    };

    public static KnowledgeRecoverySnapshot Recover(string officialRoot, string knowledgeRoot, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(knowledgeRoot);
        var sources = LoadSources(officialRoot);
        var domains = new List<KnowledgeCategorySummary>();
        foreach (var pair in DomainSources)
        {
            var domainEntities = new Dictionary<string, KnowledgeEntityIndex>(StringComparer.Ordinal);
            var recovered = new SortedSet<string>(StringComparer.Ordinal);
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            var declared = 0;
            var relatedDeclared = 0;
            var directImport = true;
            var statuses = new SortedSet<string>(StringComparer.Ordinal);
            var usedSources = new List<string>();
            var primarySources = pair.Value.PrimarySources.ToHashSet(StringComparer.Ordinal);
            var allSources = pair.Value.PrimarySources.Concat(pair.Value.RelatedSources).ToArray();
            foreach (var category in allSources)
            {
                if (!sources.TryGetValue(category, out var source))
                {
                    missing.Add($"sourceCategory:{category}");
                    if (primarySources.Contains(category)) directImport = false;
                    continue;
                }
                if (primarySources.Contains(category))
                {
                    declared += source.RecordCount;
                    directImport &= source.CanDirectImport;
                }
                else
                {
                    relatedDeclared += source.RecordCount;
                }
                statuses.Add(source.Status);
                usedSources.Add(Path.GetRelativePath(officialRoot, source.Path).Replace('\\', '/'));
                foreach (var item in source.RecoveredFields) recovered.Add(item);
                foreach (var item in source.MissingFields) missing.Add(item);
                if (primarySources.Contains(category))
                {
                    foreach (var entity in source.Entities) domainEntities.TryAdd(entity.Id, entity);
                }
            }

            directImport &= pair.Value.PrimarySources.Length > 0 && pair.Value.PrimarySources.All(sources.ContainsKey);

            var summary = new KnowledgeCategorySummary(
                pair.Key,
                $"primary:{string.Join('+', pair.Value.PrimarySources)};related:{string.Join('+', pair.Value.RelatedSources)}",
                string.Join(';', usedSources),
                declared,
                relatedDeclared,
                domainEntities.Count,
                statuses.Count == 0 ? "EvidenceBlocked" : string.Join(" | ", statuses),
                directImport,
                recovered.ToArray(),
                missing.ToArray(),
                domainEntities.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
            domains.Add(summary);
            var domainRoot = Path.Combine(knowledgeRoot, pair.Key);
            Directory.CreateDirectory(domainRoot);
            File.WriteAllText(Path.Combine(domainRoot, "catalog.json"), JsonSerializer.Serialize(summary, options));
        }

        var domainIds = domains.ToDictionary(
            item => item.Domain,
            item => item.Entities.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var edges = domains.SelectMany(domain => domain.Entities.SelectMany(entity => entity.References.Select(reference =>
        {
            var targetDomain = TargetDomain(reference.Key);
            var resolution = targetDomain is null
                ? "Untyped"
                : domainIds.TryGetValue(targetDomain, out var ids) && ids.Contains(reference.Value)
                    ? "Resolved"
                    : "Unresolved";
            return new KnowledgeReferenceEdge(
                domain.Domain,
                entity.Id,
                reference.Key,
                targetDomain ?? "Unknown",
                reference.Value,
                resolution);
        }))).OrderBy(edge => edge.SourceDomain, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourceEntityId, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
            .ToArray();
        var graph = new KnowledgeRecoverySnapshot(
            "offline-official-knowledge-v2",
            domains.Count,
            sources.Count,
            sources.Values.Sum(item => item.RecordCount),
            domains.Sum(item => item.IndexedRecordCount),
            edges.Length,
            domains,
            edges);
        File.WriteAllText(Path.Combine(knowledgeRoot, "knowledge-graph.json"), JsonSerializer.Serialize(graph, options));
        return graph;
    }

    private static Dictionary<string, SourceCatalog> LoadSources(string root)
    {
        var result = new Dictionary<string, SourceCatalog>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.official.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(file, 128L * 1024 * 1024, "Official knowledge source"));
            var rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("category", out var categoryElement)) continue;
            var category = categoryElement.GetString();
            if (string.IsNullOrWhiteSpace(category)) continue;
            var entities = new List<KnowledgeEntityIndex>();
            if (rootElement.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                var entityIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in records.EnumerateArray())
                {
                    var identity = FindIdentity(record, "id", "Id", "skillId", "itemId", "questId", "monsterId", "npcId");
                    var id = identity.Value ?? $"{category}:{index}";
                    if (!entityIds.Add(id))
                    {
                        throw new InvalidDataException($"Official category '{category}' contains duplicate entity ID '{id}'.");
                    }
                    var name = FirstString(record, "name", "displayName", "displayNameCandidate", "resourceName", "title");
                    var references = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    CollectReferences(record, string.Empty, references, depth: 0, identity.PropertyName);
                    entities.Add(new KnowledgeEntityIndex(
                        id,
                        name,
                        references,
                        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(record.GetRawText())))));
                    index++;
                }
            }

            if (!result.TryAdd(category, new SourceCatalog(
                file,
                rootElement.TryGetProperty("recordCount", out var count) && count.TryGetInt32(out var parsedCount) ? parsedCount : entities.Count,
                rootElement.TryGetProperty("verificationStatus", out var status) ? status.GetString() ?? "Unknown" : "Unknown",
                rootElement.TryGetProperty("canDirectImportToGameplay", out var direct) && direct.ValueKind is JsonValueKind.True,
                ReadStrings(rootElement, "recoveredFields"),
                ReadStrings(rootElement, "missingFields"),
                entities)))
            {
                throw new InvalidDataException($"Multiple official source artifacts declare category '{category}'.");
            }
        }
        return result;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        }
        return null;
    }

    private static (string? Value, string? PropertyName) FindIdentity(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return (null, null);
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return (value.GetString(), name);
            if (value.ValueKind == JsonValueKind.Number) return (value.GetRawText(), name);
        }
        return (null, null);
    }

    private static void CollectReferences(
        JsonElement element,
        string path,
        IDictionary<string, string> output,
        int depth,
        string? identityProperty)
    {
        if (depth > 8 || output.Count >= 4_096) return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                var indexedPath = $"{path}[{index}]";
                if (IsReferenceCollection(path) && item.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    output[indexedPath] = Scalar(item);
                }
                else
                {
                    CollectReferences(item, indexedPath, output, depth + 1, identityProperty);
                }
                index++;
            }
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        foreach (var property in element.EnumerateObject())
        {
            var nextPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
            var isRootIdentity = depth == 0 && string.Equals(property.Name, identityProperty, StringComparison.Ordinal);
            if (!isRootIdentity && IsReferenceName(property.Name) &&
                property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                output[nextPath] = Scalar(property.Value);
            }
            else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                CollectReferences(property.Value, nextPath, output, depth + 1, identityProperty);
            }
        }
    }

    private static bool IsReferenceName(string name) =>
        name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("Reference", StringComparison.OrdinalIgnoreCase);

    private static bool IsReferenceCollection(string path) =>
        path.EndsWith("Ids", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("References", StringComparison.OrdinalIgnoreCase);

    private static string Scalar(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static string? TargetDomain(string path)
    {
        var normalized = path.Replace("Ids", "Id", StringComparison.OrdinalIgnoreCase);
        if (normalized.Contains("characterId", StringComparison.OrdinalIgnoreCase) || normalized.Contains("actorId", StringComparison.OrdinalIgnoreCase)) return "Character";
        if (normalized.Contains("skillId", StringComparison.OrdinalIgnoreCase)) return "Skill";
        if (normalized.Contains("itemId", StringComparison.OrdinalIgnoreCase)) return "Item";
        if (normalized.Contains("equipmentId", StringComparison.OrdinalIgnoreCase)) return "Equipment";
        if (normalized.Contains("questId", StringComparison.OrdinalIgnoreCase)) return "Quest";
        if (normalized.Contains("monsterId", StringComparison.OrdinalIgnoreCase)) return "Monster";
        if (normalized.Contains("npcId", StringComparison.OrdinalIgnoreCase)) return "NPC";
        if (normalized.Contains("portalId", StringComparison.OrdinalIgnoreCase)) return "Portal";
        if (normalized.Contains("mapId", StringComparison.OrdinalIgnoreCase)) return "Map";
        if (normalized.Contains("mountId", StringComparison.OrdinalIgnoreCase)) return "Mount";
        if (normalized.Contains("petId", StringComparison.OrdinalIgnoreCase)) return "Pet";
        if (normalized.Contains("effectId", StringComparison.OrdinalIgnoreCase)) return "Effect";
        if (normalized.Contains("statusId", StringComparison.OrdinalIgnoreCase)) return "Status";
        return null;
    }

    private static string[] ReadStrings(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];

    private sealed record SourceCatalog(
        string Path,
        int RecordCount,
        string Status,
        bool CanDirectImport,
        IReadOnlyList<string> RecoveredFields,
        IReadOnlyList<string> MissingFields,
        IReadOnlyList<KnowledgeEntityIndex> Entities);

    private sealed record DomainDefinition(string[] PrimarySources, string[] RelatedSources);
}
