using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.GameplayContentRecovery;

public sealed partial class Site17173EvidenceExtractor
{
    private readonly string _seedPath;
    private readonly ZhTwLocalization _localization;
    private readonly HttpClient _httpClient;

    public Site17173EvidenceExtractor(string seedPath, ZhTwLocalization localization)
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
        if (!string.Equals(root.GetProperty("gameIdentity").GetProperty("host").GetString(), "xjz.17173.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("17173 seed does not identify xjz.17173.com.");
        }

        var policy = root.GetProperty("policy");
        var maximumPages = policy.GetProperty("maximumPages").GetInt32();
        var maximumDepth = policy.GetProperty("maximumDepth").GetInt32();
        var queue = new Queue<PageRequest>();
        foreach (var seed in root.GetProperty("sources").EnumerateArray())
        {
            queue.Enqueue(new PageRequest(
                seed.GetProperty("id").GetString()!,
                new Uri(seed.GetProperty("url").GetString()!, UriKind.Absolute),
                seed.GetProperty("pathPrefix").GetString()!,
                seed.GetProperty("domain").GetString()!,
                0));
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageResults = new List<object>();
        var referenceIndex = BuildReferenceIndex(workspace);
        while (queue.Count > 0 && visited.Count < maximumPages)
        {
            var batch = new List<PageRequest>(8);
            while (queue.Count > 0 && batch.Count < 8 && visited.Count < maximumPages)
            {
                var candidate = queue.Dequeue();
                if (visited.Add(candidate.Url.GetLeftPart(UriPartial.Path)))
                {
                    batch.Add(candidate);
                }
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var fetchedPages = await Task.WhenAll(batch.Select(async page =>
            {
                var bytes = await _httpClient.GetByteArrayAsync(page.Url, cancellationToken);
                return new FetchedPage(page, page.Url.GetLeftPart(UriPartial.Path), bytes, DecodeHtml(bytes));
            }));

            foreach (var fetched in fetchedPages)
            {
                var page = fetched.Page;
                var title = CleanText(TitlePattern().Match(fetched.Html).Groups[1].Value);
                if (!string.Equals(page.Url.Host, "xjz.17173.com", StringComparison.OrdinalIgnoreCase) ||
                    (!title.Contains("仙界传", StringComparison.Ordinal) && !title.Contains("仙界傳", StringComparison.Ordinal)))
                {
                    throw new InvalidDataException($"17173 game identity mismatch: {page.Url} ({title})");
                }

                var sourceHash = ContentHash.Sha256(fetched.Bytes);
                var rows = ExtractTableRows(fetched.Html);
                workspace.Sources.Add(new SourceInventoryEntry(
                    $"{page.SeedId}:{ContentHash.Sha256(fetched.Canonical)[..16]}",
                    "17173SupplementalEvidence",
                    fetched.Canonical,
                    sourceHash,
                    fetched.Bytes.LongLength,
                    rows.Count,
                    "17173HtmlTableExtractor/v1;meta-charset",
                    "HistoricalGuideEvidence"));

                var extracted = ExtractRows(workspace, page, sourceHash, rows, referenceIndex);
                pageResults.Add(new { url = fetched.Canonical, page.Domain, page.Depth, title, tableRows = rows.Count, extracted });

                if (page.Depth < maximumDepth)
                {
                    foreach (var link in ExtractLinks(fetched.Html, page.Url, page.PathPrefix))
                    {
                        queue.Enqueue(page with { Url = link, Depth = page.Depth + 1 });
                    }
                }
            }
        }

        workspace.Diagnostics["site17173"] = new
        {
            host = "xjz.17173.com",
            pagesVisited = visited.Count,
            maximumPages,
            rawHtmlPersisted = false,
            authority = "HistoricalGuideEvidence",
            promotion = "StageOnlyUntilCurrentPatchPacketOrClientEvidenceMatches",
            pages = pageResults
        };
    }

    private static int ExtractRows(
        RecoveryWorkspace workspace,
        PageRequest page,
        string sourceHash,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, Reference[]> referenceIndex)
    {
        var pageMonsterReferences = page.Url.AbsolutePath.StartsWith("/monster/", StringComparison.OrdinalIgnoreCase)
            ? rows.SelectMany(row =>
                    FindReferences(row, referenceIndex).Where(reference => reference.Entity.Domain == "MonsterTemplate"))
                .GroupBy(reference => reference.Entity.AuthorityKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray()
            : [];
        var count = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var cells = rows[index].Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            if (cells.Length == 0 || IsNavigation(cells))
            {
                continue;
            }

            var joined = string.Join(" | ", cells);
            var rowMatches = FindReferences(cells, referenceIndex)
                .Take(16)
                .ToArray();
            var matches = rowMatches
                .Concat(cells.Contains("等级") || cells.Contains("掉落物") ? pageMonsterReferences : [])
                .GroupBy(reference => (reference.Name, reference.Entity.AuthorityKey))
                .Select(group => group.First())
                .ToArray();
            var structured = ParseStructured(page, cells, matches);
            if (structured is null && matches.Length == 0 && !LooksLikeGameplayFact(cells))
            {
                continue;
            }

            var domain = structured?.Domain ?? page.Domain;
            var authorityKey = $"17173:{ContentHash.Sha256(page.Url.GetLeftPart(UriPartial.Path))[..12]}:row-{index + 1}";
            var fields = structured?.Fields ?? new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["cells"] = cells,
                ["officialClientReferences"] = matches.Select(match => match.Entity.AuthorityKey).Distinct(StringComparer.Ordinal).ToArray()
            };
            fields["patchIdentity"] = null;
            fields["currentPatchVerified"] = false;
            var sourceTextCorrupt = joined.Contains('\ufffd');
            var missing = (structured?.Missing ?? (matches.Length > 0 ? ["PatchIdentity", "AuthoritativeFieldBinding"] : ["OfficialClientIdentity", "PatchIdentity", "AuthoritativeFieldBinding"])).ToList();
            if (!missing.Contains("CurrentPatchEvidence", StringComparer.Ordinal))
            {
                missing.Add("CurrentPatchEvidence");
            }
            if (sourceTextCorrupt)
            {
                fields["sourceContainsReplacementCharacter"] = true;
                missing.Add("SourceTextCorrupt");
            }

            var name = sourceTextCorrupt ? null : structured?.Name ?? matches.FirstOrDefault()?.Entity.Name ?? cells.FirstOrDefault(value => value.Any(IsCjk));
            var description = sourceTextCorrupt ? null : structured?.Description;
            AddFact(workspace, new ContentEntity(
                domain,
                authorityKey,
                page.Url.GetLeftPart(UriPartial.Path),
                $"17173:table-row:{index + 1}",
                index + 1,
                sourceHash,
                name,
                description,
                "zh-Hans",
                sourceTextCorrupt ? "SourceCorrupt" : structured is not null && matches.Length > 0 ? "HistoricalGuide+ClientExactName" : matches.Length > 0 ? "CrossReferencedNames" : "HistoricalGuide",
                sourceTextCorrupt ? "EvidenceBlocked" : "PartiallyMapped",
                fields,
                missing,
                "Historical 17173 guide table fact; structured values remain staged until current-patch packet or client evidence agrees, while absent values remain unknown."), joined);
            count++;
        }

        return count;
    }

    private static ParsedFact? ParseStructured(PageRequest page, IReadOnlyList<string> cells, IReadOnlyList<Reference> matches)
    {
        if (page.Url.AbsolutePath.StartsWith("/skill/", StringComparison.OrdinalIgnoreCase) ||
            page.Url.AbsolutePath.StartsWith("/god2/data/", StringComparison.OrdinalIgnoreCase))
        {
            var mpCell = cells.FirstOrDefault(value => MpPattern().IsMatch(value));
            var name = cells.FirstOrDefault(value => value.Any(IsCjk) && !value.StartsWith("Lv", StringComparison.OrdinalIgnoreCase));
            if (mpCell is not null && name is not null && int.TryParse(MpPattern().Match(mpCell).Groups[1].Value, out var mp))
            {
                var target = cells.FirstOrDefault(value => value is "一体" or "全体" or "自身" or "被动");
                var targetPolicy = target switch
                {
                    "一体" => "SingleTarget",
                    "全体" => "AllTargets",
                    "自身" => "Self",
                    "被动" => "Passive",
                    _ => null
                };
                return new ParsedFact(
                    "SkillNumericEvidence",
                    name,
                    cells.LastOrDefault(value => value.Any(IsCjk) && !string.Equals(value, name, StringComparison.Ordinal)),
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["skillName"] = name,
                        ["officialClientReferences"] = matches.Select(match => match.Entity.AuthorityKey).Distinct(StringComparer.Ordinal).ToArray(),
                        ["mpCost"] = mp,
                        ["mpCostPolicy"] = "Fixed",
                        ["targetLabel"] = target,
                        ["targetPolicy"] = targetPolicy,
                        ["cells"] = cells
                    },
                    matches.Count > 0
                        ? targetPolicy is null ? ["TargetPolicy"] : []
                        : targetPolicy is null ? ["OfficialSkillId", "TargetPolicy"] : ["OfficialSkillId"]);
            }
        }

        if (page.Url.AbsolutePath.StartsWith("/monster/", StringComparison.OrdinalIgnoreCase))
        {
            if (cells.Contains("等级") && cells.Contains("HP"))
            {
                var levelIndex = cells.ToList().FindIndex(value => value == "等级");
                var hpIndex = cells.ToList().FindIndex(value => value == "HP");
                var level = levelIndex >= 0 && levelIndex + 1 < cells.Count ? ParseInteger(cells[levelIndex + 1]) : null;
                var hp = hpIndex >= 0 && hpIndex + 1 < cells.Count ? ParseInteger(cells[hpIndex + 1]) : null;
                return new ParsedFact(
                    "MonsterStatEvidence",
                    matches.FirstOrDefault()?.Entity.Name,
                    null,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["monsterTemplateReferences"] = matches.Where(match => match.Entity.Domain == "MonsterTemplate").Select(match => match.Entity.AuthorityKey).Distinct(StringComparer.Ordinal).ToArray(),
                        ["levelCandidate"] = level,
                        ["maxHpCandidate"] = hp,
                        ["maxMp"] = null,
                        ["mpPolicy"] = "Unknown",
                        ["cells"] = cells
                    },
                    matches.Any(match => match.Entity.Domain == "MonsterTemplate") ? ["MaxMpPolicy", "CombatStats"] : ["MonsterTemplateIdentity", "MaxMpPolicy", "CombatStats"]);
            }

            if (cells.Contains("掉落物"))
            {
                return new ParsedFact(
                    "MonsterDropEvidence",
                    matches.FirstOrDefault(match => match.Entity.Domain == "MonsterTemplate")?.Entity.Name,
                    null,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["monsterTemplateReferences"] = matches.Where(match => match.Entity.Domain == "MonsterTemplate").Select(match => match.Entity.AuthorityKey).Distinct(StringComparer.Ordinal).ToArray(),
                        ["itemReferences"] = matches.Where(match => match.Entity.Domain == "Item").Select(match => match.Entity.AuthorityKey).Distinct(StringComparer.Ordinal).ToArray(),
                        ["dropChance"] = null,
                        ["dropWeight"] = null,
                        ["dropPolicy"] = "KnownItemsProbabilityUnknown",
                        ["cells"] = cells
                    },
                    ["DropChance", "QuantityRange"]);
            }
        }

        return null;
    }

    private Dictionary<string, Reference[]> BuildReferenceIndex(RecoveryWorkspace workspace) => workspace.Entities
        .Where(entity => entity.Domain is "Map" or "NpcTemplate" or "MonsterTemplate" or "Item" or "Skill" or "Quest")
        .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
        .Select(entity => new Reference(NormalizeIdentity(_localization.Convert(entity.Name!, entity.SourceHash).ConvertedText), entity))
        .Where(reference => reference.Name.Length >= 2)
        .GroupBy(reference => reference.Name, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group.GroupBy(reference => (reference.Entity.Domain, reference.Entity.AuthorityKey)).Select(item => item.First()).ToArray(),
            StringComparer.Ordinal);

    private static IEnumerable<Reference> FindReferences(IReadOnlyList<string> cells, IReadOnlyDictionary<string, Reference[]> referenceIndex)
    {
        foreach (var cell in cells)
        {
            var normalized = NormalizeIdentity(cell);
            if (referenceIndex.TryGetValue(normalized, out var references))
            {
                foreach (var reference in references)
                {
                    yield return reference;
                }
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractTableRows(string html)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (Match row in TableRowPattern().Matches(html))
        {
            var cells = CellPattern().Matches(row.Groups[1].Value)
                .Select(match => CleanText(match.Groups[1].Value))
                .Where(value => value.Length > 0)
                .ToArray();
            if (cells.Length > 0)
            {
                rows.Add(cells);
            }
        }

        return rows;
    }

    private static IEnumerable<Uri> ExtractLinks(string html, Uri page, string pathPrefix)
    {
        foreach (Match match in LinkPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (!Uri.TryCreate(page, href, out var link) ||
                !string.Equals(link.Host, "xjz.17173.com", StringComparison.OrdinalIgnoreCase) ||
                !link.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) ||
                !link.AbsolutePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new Uri(link.GetLeftPart(UriPartial.Path));
        }
    }

    private static string DecodeHtml(byte[] bytes)
    {
        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var charset = CharsetPattern().Match(header).Groups[1].Value;
        var encoding = charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || charset.Length == 0
            ? new UTF8Encoding(false, true)
            : Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return encoding.GetString(bytes);
    }

    private static string CleanText(string html)
    {
        var text = BreakPattern().Replace(html, " / ");
        text = TagPattern().Replace(text, string.Empty);
        return WhitespacePattern().Replace(WebUtility.HtmlDecode(text).Replace('\u00a0', ' ').Replace('\u3000', ' '), " ").Trim(' ', '/', '|');
    }

    private static void AddFact(RecoveryWorkspace workspace, ContentEntity entity, string rawText)
    {
        workspace.Entities.Add(entity);
        workspace.Raw.Add(new RawEvidenceRecord(
            ContentHash.StableId(entity.Domain, entity.SourceFile, entity.SourceIdentity, rawText),
            entity.Domain,
            "17173SupplementalEvidence",
            entity.SourceFile,
            entity.SourceIdentity,
            entity.SourceRow,
            null,
            entity.SourceHash,
            entity.OriginalLanguage,
            rawText,
            RecoveryJson.Compact(new { entity.Fields, entity.MissingRequiredFields, entity.TransformationRule }),
            ContentHash.Sha256(rawText),
            entity.Confidence,
            entity.EvidenceStatus));
    }

    private static bool LooksLikeGameplayFact(IReadOnlyList<string> cells) =>
        cells.Any(value => value.Any(IsCjk)) &&
        (cells.Any(value => value.Any(char.IsDigit)) || cells.Any(value => GameplayLabels.Contains(value)));

    private static bool IsNavigation(IReadOnlyList<string> cells) => cells.Any(value =>
        value is "研发公司" or "运营公司" or "适用系统" or "发行时间" or "收费机制" or "专区编辑" or "快速通道" or "相关链接");

    private static int? ParseInteger(string value) => int.TryParse(DigitsPattern().Match(value).Value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static string NormalizeIdentity(string value) => IdentityNoisePattern().Replace(value, string.Empty).ToLowerInvariant();

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    private static readonly HashSet<string> GameplayLabels = new(StringComparer.Ordinal)
    {
        "等级", "HP", "MP", "掉落物", "名称", "消耗", "范围", "距离", "说明", "坐标", "任务", "奖励", "合成"
    };

    private sealed record PageRequest(string SeedId, Uri Url, string PathPrefix, string Domain, int Depth);
    private sealed record FetchedPage(PageRequest Page, string Canonical, byte[] Bytes, string Html);
    private sealed record Reference(string Name, ContentEntity Entity);
    private sealed record ParsedFact(string Domain, string? Name, string? Description, Dictionary<string, object?> Fields, IReadOnlyList<string> Missing);

    [GeneratedRegex("(?is)<title[^>]*>(.*?)</title>")]
    private static partial Regex TitlePattern();

    [GeneratedRegex("(?is)<tr[^>]*>(.*?)</tr>")]
    private static partial Regex TableRowPattern();

    [GeneratedRegex("(?is)<t[dh][^>]*>(.*?)</t[dh]>")]
    private static partial Regex CellPattern();

    [GeneratedRegex("(?is)href\\s*=\\s*[\"']([^\"'#]+)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("(?i)charset\\s*=\\s*[\"']?([a-z0-9_-]+)")]
    private static partial Regex CharsetPattern();

    [GeneratedRegex("(?is)<(?:br\\s*/?|/p|/div|/li)>")]
    private static partial Regex BreakPattern();

    [GeneratedRegex("(?is)<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("(\\d+)")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex("(?i)(\\d+)\\s*MP")]
    private static partial Regex MpPattern();

    [GeneratedRegex("[\\s\\p{P}\\p{S}]+")]
    private static partial Regex IdentityNoisePattern();
}
