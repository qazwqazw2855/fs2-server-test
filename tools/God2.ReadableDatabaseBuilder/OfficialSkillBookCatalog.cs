using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenccNetLib;

namespace God2.ReadableDatabaseBuilder;

public sealed class TraditionalChineseConverter
{
    private readonly Opencc _converter = new(OpenccConfig.S2Twp);
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    public string Convert(string value)
    {
        if (_cache.TryGetValue(value, out var cached))
        {
            return cached;
        }

        var converted = _converter.Convert(value, punctuation: false);
        _cache[value] = converted;
        return converted;
    }
}

public static class OfficialSkillBookCatalogParser
{
    public static async Task<IReadOnlyList<OfficialSkillBookEntry>> ParseAsync(
        string path,
        TraditionalChineseConverter converter)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        var seen = new HashSet<int>();
        var entries = new List<OfficialSkillBookEntry>();

        foreach (var item in document.RootElement.GetProperty("records").EnumerateArray())
        {
            if (!item.TryGetProperty("itemType", out var type) || type.GetString() != "SKB")
            {
                continue;
            }

            var clientItemId = item.GetProperty("clientItemId").GetInt32();
            if (!seen.Add(clientItemId))
            {
                throw new InvalidOperationException($"Duplicate official SKB client item id: {clientItemId}.");
            }

            var rawStats = item.TryGetProperty("statCandidates", out var statCandidates) && statCandidates.ValueKind == JsonValueKind.Array
                ? statCandidates.EnumerateArray()
                    .Select(value => value.GetString()?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !NumericOnlyRegex.IsMatch(value))
                    .Cast<string>()
                    .ToArray()
                : [];
            var convertedStats = rawStats.Select(converter.Convert).ToArray();
            var classificationIndex = Array.FindIndex(convertedStats, IsClassification);
            var classification = classificationIndex >= 0 ? convertedStats[classificationIndex] : null;
            var (mode, category, level) = ParseClassification(classification, clientItemId);
            var scopeIndex = Array.FindIndex(convertedStats, value => value.StartsWith("範圍", StringComparison.Ordinal) || value.StartsWith("范围", StringComparison.Ordinal) || value.StartsWith("鑼冨洿", StringComparison.Ordinal) || value.StartsWith("绡勫湇", StringComparison.Ordinal));
            var scope = scopeIndex >= 0 ? StripPrefix(convertedStats[scopeIndex]) : null;

            var effectLines = convertedStats
                .Select((value, index) => new { value, index })
                .Where(item => item.index != classificationIndex && item.index != scopeIndex)
                .Select(item => item.value)
                .Where(value => !MpCostRegex.IsMatch(value))
                .Where(value => !AttackRangeRegex.IsMatch(value))
                .Where(value => !NumericOnlyRegex.IsMatch(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            entries.Add(new OfficialSkillBookEntry(
                clientItemId,
                TryGetInt(item.GetProperty("idSemantics"), "clientDisplayId"),
                converter.Convert(item.GetProperty("name").GetString() ?? throw new InvalidOperationException($"SKB {clientItemId} has no name.")),
                ConvertNullable(converter, TryGetString(item, "description")),
                mode,
                category,
                level,
                MatchInt(convertedStats, MpCostRegex),
                MatchInt(convertedStats, AttackRangeRegex),
                scope,
                effectLines.Length == 0 ? null : string.Join("；", effectLines),
                TryGetNestedInt(item, "priceCandidates", "systemSell"),
                TryGetNestedInt(item, "priceCandidates", "systemRecycle")));
        }

        return entries.OrderBy(entry => entry.ClientItemId).ToArray();
    }

    private static bool IsClassification(string value) =>
        value.StartsWith("技能", StringComparison.Ordinal) ||
        value.StartsWith("被動技能", StringComparison.Ordinal) ||
        value.StartsWith("主動技能", StringComparison.Ordinal) ||
        value.StartsWith("特殊技能", StringComparison.Ordinal) ||
        value.StartsWith("鎶€鑳?", StringComparison.Ordinal) ||
        value.StartsWith("琚嫊鎶€鑳?", StringComparison.Ordinal) ||
        value.StartsWith("琚姩鎶€鑳?", StringComparison.Ordinal) ||
        value.StartsWith("涓诲嫊鎶€鑳?", StringComparison.Ordinal) ||
        value.StartsWith("涓诲姩鎶€鑳?", StringComparison.Ordinal);

    private static (string? Mode, string? Category, int? Level) ParseClassification(string? value, int clientItemId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null, null);
        }

        var mode = new[] { "被動技能", "主動技能", "特殊技能", "技能", "琚嫊鎶€鑳?", "琚姩鎶€鑳?", "涓诲嫊鎶€鑳?", "涓诲姩鎶€鑳?", "鎶€鑳?" }
            .FirstOrDefault(candidate => value.StartsWith(candidate, StringComparison.Ordinal));
        var category = value.Contains("劍技", StringComparison.Ordinal) || value.Contains("鍔嶆妧", StringComparison.Ordinal) || value.Contains("鍓戞妧", StringComparison.Ordinal)
            ? "劍技"
            : null;
        var levelMatch = Regex.Match(value, @"(\d+)", RegexOptions.CultureInvariant);
        var level = levelMatch.Success
            ? int.Parse(levelMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : clientItemId is 6501 ? (int?)1 : clientItemId is 6502 ? (int?)2 : null;
        return (mode, category, level);
    }

    private static string StripPrefix(string value)
    {
        foreach (var prefix in new[] { "範圍", "范围", "鑼冨洿", "绡勫湇" })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return value.Trim();
    }

    private static int? MatchInt(IEnumerable<string> values, Regex regex)
    {
        foreach (var value in values)
        {
            var match = regex.Match(value);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static int? TryGetNestedInt(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var nested) ? TryGetInt(nested, property) : null;

    private static int? TryGetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string? TryGetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ConvertNullable(TraditionalChineseConverter converter, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : converter.Convert(value);

    private static readonly Regex NumericOnlyRegex = new(@"^\s*[+-]?\d+(?:\.\d+)?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex MpCostRegex = new(@"(?:MP|消耗|娑堣€?)[^\d]*(\d+)|(\d+)\s*MP", RegexOptions.CultureInvariant);
    private static readonly Regex AttackRangeRegex = new(@"(?:攻擊距離|攻击距离|璺濈|距離)[^\d]*(\d+)", RegexOptions.CultureInvariant);
}
