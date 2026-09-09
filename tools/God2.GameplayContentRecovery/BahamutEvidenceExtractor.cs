using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.GameplayContentRecovery;

public sealed partial class BahamutEvidenceExtractor
{
    private readonly string _seedPath;
    private readonly ZhTwLocalization _localization;
    private readonly HttpClient _httpClient;

    public BahamutEvidenceExtractor(string seedPath, ZhTwLocalization localization)
    {
        _seedPath = seedPath;
        _localization = localization;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; God2GameplayContentRecovery/1.0; local research)");
    }

    public async Task ExtractAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        using var seedDocument = JsonDocument.Parse(await File.ReadAllTextAsync(_seedPath, cancellationToken));
        var root = seedDocument.RootElement;
        if (root.GetProperty("gameIdentity").GetProperty("bahamutBoardId").GetInt32() != 8395)
        {
            throw new InvalidDataException("Bahamut source seed does not identify the 封神2 board (8395).");
        }

        var sourceResults = new List<object>();
        foreach (var source in root.GetProperty("sources").EnumerateArray())
        {
            var id = source.GetProperty("id").GetString() ?? throw new InvalidDataException("Bahamut source id is missing.");
            var url = source.GetProperty("url").GetString() ?? throw new InvalidDataException("Bahamut source URL is missing.");
            if (!url.Contains("bsn=8395", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Wrong-game Bahamut source rejected: {url}");
            }

            var htmlBytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
            var html = System.Text.Encoding.UTF8.GetString(htmlBytes);
            var sourceHash = ContentHash.Sha256(html);
            var title = WebUtility.HtmlDecode(TitlePattern().Match(html).Groups[1].Value).Trim();
            var expected = source.GetProperty("expectedTitleContains").GetString() ?? string.Empty;
            if (!title.Contains(expected, StringComparison.Ordinal) || !title.Contains("封神2", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Bahamut title identity mismatch for {url}: {title}");
            }

            var lines = ExtractArticleLines(html);
            workspace.Sources.Add(new SourceInventoryEntry(
                id,
                "BahamutSupplementalEvidence",
                url,
                sourceHash,
                htmlBytes.LongLength,
                lines.Count,
                "BahamutArticleHtmlToText/v1",
                "SupplementalCommunityEvidence"));

            var parser = source.GetProperty("parser").GetString() ?? string.Empty;
            var extracted = parser switch
            {
                "MonsterLevelAndDropLines" => ExtractMonsterDrops(workspace, id, url, sourceHash, lines),
                "MonsterProfileBlocks" => ExtractMonsterProfiles(workspace, id, url, sourceHash, lines),
                "MonsterSkillTable" => ExtractMonsterSkills(workspace, id, url, sourceHash, lines),
                "MonsterSpeedLines" => ExtractMonsterSpeeds(workspace, id, url, sourceHash, lines),
                "NpcCoordinateLines" => ExtractNpcCoordinates(workspace, id, url, sourceHash, lines, source.TryGetProperty("mapName", out var map) ? map.GetString() : null),
                "SkillFamilyEffectLines" => ExtractSkillFamilies(workspace, id, url, sourceHash, lines),
                _ => ExtractGenericEvidence(workspace, id, url, sourceHash, lines, parser)
            };
            sourceResults.Add(new { id, url, title, parser, lineCount = lines.Count, extracted });
        }

        workspace.Diagnostics["bahamutSources"] = sourceResults;
    }

    private int ExtractMonsterDrops(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines)
    {
        var monsters = workspace.Entities.Where(entity => entity.Domain == "MonsterTemplate" && !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => (Name: NormalizeIdentity(_localization.Convert(entity.Name!, entity.SourceHash).ConvertedText), Entity: entity))
            .Where(item => item.Name.Length >= 2)
            .OrderByDescending(item => item.Name.Length)
            .ToArray();
        var items = workspace.Entities.Where(entity => entity.Domain == "Item" && !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => (Name: NormalizeIdentity(_localization.Convert(entity.Name!, entity.SourceHash).ConvertedText), Entity: entity))
            .Where(item => item.Name.Length >= 2)
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.Name.Length)
            .ToArray();
        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var normalizedLine = NormalizeIdentity(line);
            var monster = monsters.FirstOrDefault(candidate => normalizedLine.StartsWith(candidate.Name, StringComparison.Ordinal));
            if (monster.Entity is null)
            {
                continue;
            }

            var remainder = normalizedLine[monster.Name.Length..];
            var levelMatch = LeadingNumberPattern().Match(remainder);
            if (!levelMatch.Success || !int.TryParse(levelMatch.Groups[1].Value, out var level))
            {
                continue;
            }

            var dropText = remainder[levelMatch.Length..];
            var itemReferences = items.Where(candidate => dropText.Contains(candidate.Name, StringComparison.Ordinal))
                .Select(candidate => candidate.Entity.AuthorityKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var authorityKey = $"{sourceId}:line-{index + 1}:{monster.Entity.AuthorityKey}";
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["monsterTemplateReference"] = monster.Entity.AuthorityKey,
                ["levelCandidate"] = level,
                ["itemReferences"] = itemReferences,
                ["dropChance"] = null,
                ["dropWeight"] = null,
                ["dropPolicy"] = itemReferences.Length > 0 ? "KnownItemsProbabilityUnknown" : "Unknown",
                ["sourceVersionCaveat"] = "Mainland official-site repost; Taiwan client identity cross-reference required"
            };
            AddExternalEntity(workspace, new ContentEntity(
                "MonsterDropEvidence",
                authorityKey,
                url,
                $"{sourceId}:line-{index + 1}",
                index + 1,
                sourceHash,
                monster.Entity.Name,
                null,
                "zh-Hant-TW",
                itemReferences.Length > 0 ? "CrossReferencedNames" : "CommunityReported",
                itemReferences.Length > 0 ? "Derived" : "PartiallyMapped",
                fields,
                itemReferences.Length > 0 ? ["DropChance", "QuantityRange", "PatchIdentity"] : ["ItemIdentity", "DropChance", "QuantityRange", "PatchIdentity"],
                "User-verified Bahamut fact row cross-referenced by exact zh-TW official-client monster and item display names; probabilities remain null."), line);
            count++;
        }

        return count;
    }

    private int ExtractMonsterProfiles(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines)
    {
        var monsters = BuildMonsterReferences(workspace);
        var items = workspace.Entities.Where(entity => entity.Domain == "Item" && !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => (Name: NormalizeIdentity(_localization.Convert(entity.Name!, entity.SourceHash).ConvertedText), Entity: entity))
            .Where(item => item.Name.Length >= 2)
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.Name.Length)
            .ToArray();
        (string Name, ContentEntity Entity)? currentMonster = null;
        var count = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var normalizedLine = NormalizeIdentity(line);
            var lineMonster = monsters.FirstOrDefault(candidate =>
                normalizedLine.Equals(candidate.Name, StringComparison.Ordinal) ||
                normalizedLine.StartsWith(candidate.Name + "lv", StringComparison.Ordinal) ||
                normalizedLine.StartsWith(candidate.Name + "等級", StringComparison.Ordinal) ||
                normalizedLine.StartsWith(candidate.Name + "等级", StringComparison.Ordinal));
            if (lineMonster.Entity is not null)
            {
                currentMonster = lineMonster;
            }

            if (currentMonster is null)
            {
                continue;
            }

            var levelMatch = MonsterLevelPattern().Match(line);
            var hpMatch = MonsterHpPattern().Match(line);
            if (levelMatch.Success || hpMatch.Success)
            {
                var levelMinimum = levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var parsedLevel) ? parsedLevel : (int?)null;
                var levelMaximum = levelMatch.Success && int.TryParse(levelMatch.Groups[2].Value, out var parsedMaximum) ? parsedMaximum : levelMinimum;
                var maximumHp = hpMatch.Success && long.TryParse(hpMatch.Groups[1].Value, out var parsedHp) ? parsedHp : (long?)null;
                var authorityKey = $"{sourceId}:profile-{index + 1}:{currentMonster.Value.Entity.AuthorityKey}";
                AddExternalEntity(workspace, new ContentEntity(
                    "MonsterStatEvidence",
                    authorityKey,
                    url,
                    $"{sourceId}:line-{index + 1}",
                    index + 1,
                    sourceHash,
                    currentMonster.Value.Entity.Name,
                    null,
                    "zh-Hant-TW",
                    "HistoricalGuide+ClientExactName",
                    "PartiallyMapped",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["officialClientReferences"] = new[] { currentMonster.Value.Entity.AuthorityKey },
                        ["monsterTemplateReferences"] = currentMonster.Value.Entity.Domain == "MonsterTemplate" ? new[] { currentMonster.Value.Entity.AuthorityKey } : [],
                        ["encounterNameReferences"] = currentMonster.Value.Entity.Domain == "EncounterName" ? new[] { currentMonster.Value.Entity.AuthorityKey } : [],
                        ["levelCandidate"] = levelMinimum == levelMaximum ? levelMinimum : null,
                        ["levelMinimumCandidate"] = levelMinimum,
                        ["levelMaximumCandidate"] = levelMaximum,
                        ["maxHpCandidate"] = maximumHp,
                        ["patchIdentity"] = null,
                        ["currentPatchVerified"] = false
                    },
                    ["CurrentPatchEvidence", "CombatStats", "Spawn"],
                    "Historical Bahamut monster profile matched to an exact current-client display name; values remain staged until current-patch evidence agrees."), line);
                count++;
            }

            var dropMatch = MonsterDropPattern().Match(line);
            if (dropMatch.Success)
            {
                var dropText = NormalizeIdentity(dropMatch.Groups[1].Value);
                var itemReferences = items.Where(candidate => dropText.Contains(candidate.Name, StringComparison.Ordinal))
                    .Select(candidate => candidate.Entity.AuthorityKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var authorityKey = $"{sourceId}:drops-{index + 1}:{currentMonster.Value.Entity.AuthorityKey}";
                AddExternalEntity(workspace, new ContentEntity(
                    "MonsterDropEvidence",
                    authorityKey,
                    url,
                    $"{sourceId}:line-{index + 1}",
                    index + 1,
                    sourceHash,
                    currentMonster.Value.Entity.Name,
                    null,
                    "zh-Hant-TW",
                    itemReferences.Length > 0 ? "HistoricalGuide+ClientExactNames" : "HistoricalGuide+ClientExactMonsterName",
                    "PartiallyMapped",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["officialClientReferences"] = new[] { currentMonster.Value.Entity.AuthorityKey },
                        ["monsterTemplateReferences"] = currentMonster.Value.Entity.Domain == "MonsterTemplate" ? new[] { currentMonster.Value.Entity.AuthorityKey } : [],
                        ["encounterNameReferences"] = currentMonster.Value.Entity.Domain == "EncounterName" ? new[] { currentMonster.Value.Entity.AuthorityKey } : [],
                        ["itemReferences"] = itemReferences,
                        ["dropChance"] = null,
                        ["quantityRange"] = null,
                        ["dropPolicy"] = itemReferences.Length > 0 ? "KnownItemsProbabilityUnknown" : "UnresolvedItemText",
                        ["patchIdentity"] = null,
                        ["currentPatchVerified"] = false
                    },
                    itemReferences.Length > 0
                        ? ["CurrentPatchEvidence", "DropChance", "QuantityRange"]
                        : ["CurrentPatchEvidence", "ItemIdentity", "DropChance", "QuantityRange"],
                    "Historical Bahamut drop list; item probabilities and current-patch identity remain unknown."), line);
                count++;
            }
        }

        return count;
    }

    private int ExtractMonsterSkills(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines)
    {
        var monsters = BuildMonsterReferences(workspace);
        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var normalizedLine = NormalizeIdentity(lines[index]);
            var monster = monsters.FirstOrDefault(candidate => normalizedLine.StartsWith(candidate.Name, StringComparison.Ordinal));
            if (monster.Entity is null)
            {
                continue;
            }

            var skillLabels = MonsterSkillTokenPattern().Matches(lines[index])
                .Select(match => match.Value.Trim())
                .Where(value => value.Length > 0 && !value.StartsWith('(') && !value.StartsWith('（'))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (skillLabels.Length == 0 && !lines[index].Contains("無", StringComparison.Ordinal))
            {
                continue;
            }

            AddExternalEntity(workspace, new ContentEntity(
                "MonsterSkillEvidence",
                $"{sourceId}:skills-{index + 1}:{monster.Entity.AuthorityKey}",
                url,
                $"{sourceId}:line-{index + 1}",
                index + 1,
                sourceHash,
                monster.Entity.Name,
                null,
                "zh-Hant-TW",
                "HistoricalObservedGuide+ClientExactMonsterName",
                "PartiallyMapped",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["officialClientReference"] = monster.Entity.AuthorityKey,
                    ["officialClientReferenceDomain"] = monster.Entity.Domain,
                    ["skillLabels"] = skillLabels,
                    ["observedNoSkill"] = skillLabels.Length == 0,
                    ["parenthesizedClaimsExcluded"] = true,
                    ["patchIdentity"] = null,
                    ["currentPatchVerified"] = false
                },
                ["CurrentPatchEvidence", "OfficialSkillIdBinding", "UseProbability", "TriggerPolicy"],
                "Historical observed monster-skill labels; parenthesized unobserved claims are excluded and current-patch binding is required."), lines[index]);
            count++;
        }

        return count;
    }

    private int ExtractMonsterSpeeds(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines)
    {
        var monsters = BuildMonsterReferences(workspace);
        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var normalizedLine = NormalizeIdentity(lines[index]);
            var consumed = new List<(int Start, int End)>();
            foreach (var monster in monsters)
            {
                var start = normalizedLine.IndexOf(monster.Name, StringComparison.Ordinal);
                if (start < 0 || consumed.Any(range => start < range.End && start + monster.Name.Length > range.Start))
                {
                    continue;
                }

                var remainder = normalizedLine[(start + monster.Name.Length)..];
                var speedMatch = LeadingNumberPattern().Match(remainder);
                if (!speedMatch.Success || !int.TryParse(speedMatch.Groups[1].Value, out var speed))
                {
                    continue;
                }

                consumed.Add((start, start + monster.Name.Length));
                AddExternalEntity(workspace, new ContentEntity(
                    "MonsterSpeedEvidence",
                    $"{sourceId}:speed-{index + 1}:{monster.Entity.AuthorityKey}",
                    url,
                    $"{sourceId}:line-{index + 1}",
                    index + 1,
                    sourceHash,
                    monster.Entity.Name,
                    null,
                    "zh-Hant-TW",
                    "HistoricalApproximation+ClientExactName",
                    "PartiallyMapped",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["officialClientReference"] = monster.Entity.AuthorityKey,
                        ["officialClientReferenceDomain"] = monster.Entity.Domain,
                        ["speedCandidate"] = speed,
                        ["reportedTolerancePercent"] = 3,
                        ["patchIdentity"] = null,
                        ["currentPatchVerified"] = false
                    },
                    ["CurrentPatchEvidence", "ExactSpeed"],
                    "Historical player-measured monster speed; the source states an approximate three-percent variation and the value is not production-ready."), lines[index]);
                count++;
            }
        }

        return count;
    }

    private (string Name, ContentEntity Entity)[] BuildMonsterReferences(RecoveryWorkspace workspace) => workspace.Entities
        .Where(entity => (entity.Domain is "MonsterTemplate" or "EncounterName") && !string.IsNullOrWhiteSpace(entity.Name))
        .Select(entity => (Name: NormalizeIdentity(_localization.Convert(entity.Name!, entity.SourceHash).ConvertedText), Entity: entity))
        .Where(item => item.Name.Length >= 2)
        .GroupBy(item => item.Name, StringComparer.Ordinal)
        .Select(group => group.OrderBy(item => item.Entity.Domain == "MonsterTemplate" ? 0 : 1).First())
        .OrderByDescending(item => item.Name.Length)
        .ToArray();

    private int ExtractNpcCoordinates(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines, string? mapName)
    {
        var officialNpcs = workspace.Entities.Where(entity => entity.Domain == "NpcTemplate" && !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => (Name: NormalizeIdentity(_localization.Convert(entity.Name!, entity.SourceHash).ConvertedText), Entity: entity))
            .Where(item => item.Name.Length >= 2)
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Entity.AuthorityKey).ToArray(), StringComparer.Ordinal);
        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            foreach (Match match in CoordinatePattern().Matches(lines[index]))
            {
                var name = match.Groups[1].Value.Trim(' ', '　', '：', ':', '-', '—');
                if (name.Length is < 2 or > 64 || name.Contains("座標", StringComparison.Ordinal))
                {
                    continue;
                }

                var normalizedName = NormalizeIdentity(name);
                officialNpcs.TryGetValue(normalizedName, out var npcReferences);
                npcReferences ??= [];
                if (!int.TryParse(match.Groups[2].Value, out var x) || !int.TryParse(match.Groups[3].Value, out var y))
                {
                    continue;
                }

                var authorityKey = $"{sourceId}:line-{index + 1}:{ContentHash.Sha256($"{name}:{x}:{y}")[..16]}";
                var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["npcNameZhTw"] = name,
                    ["npcTemplateReferences"] = npcReferences,
                    ["mapNameZhTw"] = mapName,
                    ["mapId"] = null,
                    ["x"] = x,
                    ["y"] = y,
                    ["coordinatePolicy"] = sourceId.Contains("fenghua", StringComparison.Ordinal) ? "ApproximateMovingNpc" : "PlayerMaintainedCoordinate",
                    ["patchIdentity"] = null
                };
                AddExternalEntity(workspace, new ContentEntity(
                    "NpcSpawnEvidence",
                    authorityKey,
                    url,
                    $"{sourceId}:line-{index + 1}",
                    index + 1,
                    sourceHash,
                    name,
                    null,
                    "zh-Hant-TW",
                    npcReferences.Length > 0 ? "CrossReferencedName" : "CommunityReported",
                    npcReferences.Length > 0 ? "Derived" : "PartiallyMapped",
                    fields,
                    npcReferences.Length > 0 ? ["AuthoritativeMapId", "PatchIdentity", "ExactVsMovingCoordinate"] : ["NpcTemplateIdentity", "AuthoritativeMapId", "PatchIdentity", "ExactVsMovingCoordinate"],
                    "User-verified Bahamut NPC coordinate fact; only exact official-client name matches are linked and no numeric map ID is inferred."), lines[index]);
                count++;
            }
        }

        return count;
    }

    private static int ExtractSkillFamilies(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines)
    {
        var currentClass = "Unknown";
        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line is "劍客" or "仙道" or "謀士" or "藥師")
            {
                currentClass = line;
                continue;
            }

            var match = SkillFamilyPattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var family = match.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            var range = match.Groups[2].Value;
            var effect = match.Groups[3].Value.Trim();
            AddExternalEntity(workspace, new ContentEntity(
                "SkillFamilyEvidence",
                $"{sourceId}:line-{index + 1}:{family}",
                url,
                $"{sourceId}:line-{index + 1}",
                index + 1,
                sourceHash,
                family,
                effect,
                "zh-Hant-TW",
                "PublishedGuideRepost",
                "PartiallyMapped",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["owningClass"] = currentClass,
                    ["skillFamily"] = family,
                    ["rangeCandidate"] = int.TryParse(range, out var parsedRange) ? parsedRange : null,
                    ["effectDescription"] = effect,
                    ["targetPolicy"] = range == "自身" ? "Self" : null,
                    ["mpCost"] = null,
                    ["mpCostPolicy"] = "Unknown"
                },
                ["SkillIdBinding", "MpCostPolicy", "ExactTargetPolicy"],
                "Bahamut/e-play family effect evidence; it is not bound to an individual official client SkillId."), line);
            count++;
        }

        return count;
    }

    private static int ExtractGenericEvidence(RecoveryWorkspace workspace, string sourceId, string url, string sourceHash, IReadOnlyList<string> lines, string parser)
    {
        var count = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length < 4 || !line.Any(character => character is >= '\u3400' and <= '\u9fff'))
            {
                continue;
            }

            var domain = parser switch
            {
                "PetStatLines" => "PetEvidence",
                "MapRewardLines" => "MapRewardEvidence",
                _ => "ExternalEvidence"
            };
            AddExternalRaw(workspace, domain, url, $"{sourceId}:line-{index + 1}", index + 1, sourceHash, line,
                RecoveryJson.Compact(new { parser, evidenceStatus = "PartiallyMapped", promotion = "CrossReferenceRequired" }));
            count++;
        }

        return count;
    }

    private static IReadOnlyList<string> ExtractArticleLines(string html)
    {
        var match = ArticlePattern().Match(html);
        if (!match.Success)
        {
            throw new InvalidDataException("Bahamut article content block was not found.");
        }

        var text = BreakPattern().Replace(match.Groups[1].Value, "\n");
        text = TagPattern().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ').Replace('\u3000', ' ');
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => WhitespacePattern().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static void AddExternalEntity(RecoveryWorkspace workspace, ContentEntity entity, string rawLine)
    {
        workspace.Entities.Add(entity);
        var metadata = RecoveryJson.Compact(new { entity.Fields, entity.MissingRequiredFields, entity.TransformationRule });
        AddExternalRaw(workspace, entity.Domain, entity.SourceFile, entity.SourceIdentity, entity.SourceRow, entity.SourceHash, rawLine, metadata, entity.Confidence, entity.EvidenceStatus);
    }

    private static void AddExternalRaw(
        RecoveryWorkspace workspace,
        string domain,
        string sourceFile,
        string sourceIdentity,
        int? sourceRow,
        string sourceHash,
        string rawLine,
        string metadata,
        string confidence = "CommunityReported",
        string evidenceStatus = "PartiallyMapped")
    {
        workspace.Raw.Add(new RawEvidenceRecord(
            ContentHash.StableId(domain, sourceFile, sourceIdentity, rawLine),
            domain,
            "BahamutSupplementalEvidence",
            sourceFile,
            sourceIdentity,
            sourceRow,
            null,
            sourceHash,
            "zh-Hant-TW",
            rawLine,
            metadata,
            ContentHash.Sha256(rawLine),
            confidence,
            evidenceStatus));
    }

    private static string NormalizeIdentity(string value) => IdentityNoisePattern().Replace(value, string.Empty).ToLowerInvariant();

    [GeneratedRegex("(?is)<title>(.*?)</title>")]
    private static partial Regex TitlePattern();

    [GeneratedRegex("(?is)<div[^>]+class=\"[^\"]*c-article__content[^\"]*\"[^>]*>(.*?)</div>")]
    private static partial Regex ArticlePattern();

    [GeneratedRegex("(?is)<(?:br\\s*/?|/p|/li|p[^>]*|li[^>]*)>")]
    private static partial Regex BreakPattern();

    [GeneratedRegex("(?is)<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("^[^0-9]*(\\d{1,3})")]
    private static partial Regex LeadingNumberPattern();

    [GeneratedRegex(@"(?i)(?:等級|等级|lv)\s*[:：]?\s*(\d{1,3})(?:\s*[-~～]\s*(\d{1,3}))?")]
    private static partial Regex MonsterLevelPattern();

    [GeneratedRegex(@"(?i)(?:血量|hp)\s*[:：]?\s*(\d{1,9})")]
    private static partial Regex MonsterHpPattern();

    [GeneratedRegex(@"(?:掉落物品|掉落物|掉落)\s*[:：]\s*(.+)$")]
    private static partial Regex MonsterDropPattern();

    [GeneratedRegex(@"(?<![（(])(?:刀|劍|斧|杖|槍|鞭|弓|飛刀|金|木|土|水|火|毒|亂|眠|封|回|體|力|速|石)\s*\d+(?:\s*\+\s*\d+)?")]
    private static partial Regex MonsterSkillTokenPattern();

    [GeneratedRegex(@"([^()（）\d]{2,64})[（(]\s*(\d{1,4})\s*[,，/.]\s*(\d{1,4})\s*[)）]")]
    private static partial Regex CoordinatePattern();

    [GeneratedRegex(@"^(刀|劍|斧|杖|槍|鞭|弓|飛\s*刀|金|木|土|水|火|魚鱗陣|衡軛陣|鋒矢陣|偃月陣|方圓陣|鶴翼陣|回復類|狀態攻擊類|狀態解除類|提升屬性類)\s*(\d+|自身|無|依[^ ]*)\s+(.+)$")]
    private static partial Regex SkillFamilyPattern();

    [GeneratedRegex(@"[\s\p{P}\p{S}]+")]
    private static partial Regex IdentityNoisePattern();
}
