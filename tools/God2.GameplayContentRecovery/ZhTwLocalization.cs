using System.Text;
using System.Text.RegularExpressions;
using OpenccNetLib;

namespace God2.GameplayContentRecovery;

public sealed partial class ZhTwLocalization
{
    // OpenCC maps these glyphs in some conversion directions, but they are
    // valid Traditional Chinese in God2 names or established phrases (for
    // example 后羿、北斗、千里、比干、辟邪、老少咸宜、小丑、粽子、
    // 淳于、三尸星、占卜、所云). 台 and 霉 are also valid in other
    // contexts, so the known simplified phrases that use them are checked
    // separately below.
    private const string TraditionalSharedGlyphs = "粽斗里后丑伙岩干辟床凶咸于尸占云台霉吁征杰采";
    private static readonly (string Phrase, char Glyph)[] ContextualSimplifiedPhrases =
    [
        ("台版", '台'),
        ("倒霉", '霉')
    ];
    private readonly Opencc _toTaiwan = new(OpenccConfig.S2Twp);
    private readonly Opencc _toSimplified = new(OpenccConfig.Tw2Sp);
    private readonly Opencc _glyphToTraditional = new(OpenccConfig.S2T);
    private readonly IReadOnlyList<GlossaryEntry> _glossary;
    private readonly Dictionary<string, GlossaryEntry> _exactGlossary;
    private readonly Dictionary<string, LocalizationResult> _conversionCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _simplifiedCache = new(StringComparer.Ordinal);
    private readonly Dictionary<char, bool> _simplifiedGlyphCache = [];

    public ZhTwLocalization(IReadOnlyList<GlossaryEntry> glossary)
    {
        _glossary = glossary
            .OrderByDescending(entry => entry.SimplifiedText.Length)
            .ThenBy(entry => entry.SimplifiedText, StringComparer.Ordinal)
            .ToArray();
        _exactGlossary = glossary.ToDictionary(entry => entry.SimplifiedText, StringComparer.Ordinal);
    }

    public IReadOnlyList<GlossaryEntry> Glossary => _glossary;

    public LocalizationResult Convert(string input, string sourceHash)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_conversionCache.TryGetValue(input, out var cached))
        {
            return cached with { SourceHash = sourceHash };
        }

        var tokens = TokenPattern().Matches(input).Select(match => match.Value).ToArray();
        var markupBefore = MarkupPattern().Matches(input).Select(match => match.Value).ToArray();
        var controlsBefore = ControlPattern().Matches(input).Select(match => match.Value).ToArray();
        var protectedText = Protect(input, tokens);

        string converted;
        string method;
        if (_exactGlossary.TryGetValue(input, out var exact))
        {
            converted = exact.TraditionalText;
            method = exact.Source.StartsWith("Official", StringComparison.Ordinal)
                ? "OfficialZhTwExact"
                : "ProjectGlossaryExact";
        }
        else
        {
            var glossaryApplied = protectedText;
            var usedGlossary = false;
            foreach (var entry in _glossary)
            {
                if (!glossaryApplied.Contains(entry.SimplifiedText, StringComparison.Ordinal))
                {
                    continue;
                }

                glossaryApplied = glossaryApplied.Replace(entry.SimplifiedText, entry.TraditionalText, StringComparison.Ordinal);
                usedGlossary = true;
            }

            converted = ConvertToFixedPoint(glossaryApplied);
            method = usedGlossary ? "ProjectGlossaryThenOpenCC-S2Twp" : "OpenCC-S2Twp";
            converted = Restore(converted, tokens);
        }

        var convertedTokens = TokenPattern().Matches(converted).Select(match => match.Value).ToArray();
        var markupAfter = MarkupPattern().Matches(converted).Select(match => match.Value).ToArray();
        var controlsAfter = ControlPattern().Matches(converted).Select(match => match.Value).ToArray();
        var placeholdersPreserved = tokens.SequenceEqual(convertedTokens, StringComparer.Ordinal);
        var markupPreserved = markupBefore.SequenceEqual(markupAfter, StringComparer.Ordinal);
        var controlsPreserved = controlsBefore.SequenceEqual(controlsAfter, StringComparer.Ordinal);
        var validUnicode = !converted.Contains('\ufffd') && IsValidUtf16(converted);
        var idempotent = string.Equals(_toTaiwan.Convert(Protect(converted, convertedTokens), punctuation: false), Protect(converted, convertedTokens), StringComparison.Ordinal);

        var containsCjk = converted.Any(IsCjk);
        var changed = !string.Equals(input, converted, StringComparison.Ordinal);
        var containsTraditionalEvidence = !string.Equals(_toSimplified.Convert(protectedText, punctuation: false), protectedText, StringComparison.Ordinal);
        var conversionStatus = !placeholdersPreserved || !markupPreserved || !controlsPreserved || !validUnicode || !idempotent
            ? "ConversionFailed"
            : !containsCjk
                ? "NotApplicable"
                : !changed
                    ? "TraditionalVerified"
                    : containsTraditionalEvidence
                        ? "MixedLanguageNormalized"
                        : "ConvertedToTraditional";
        var reviewStatus = conversionStatus == "ConversionFailed" ? "LanguageReviewRequired" : "NotRequired";

        var result = new LocalizationResult(
            input,
            DetectOriginalLanguage(input, converted, containsTraditionalEvidence),
            converted,
            method,
            conversionStatus,
            reviewStatus,
            placeholdersPreserved,
            markupPreserved,
            controlsPreserved,
            sourceHash,
            ContentHash.Sha256(converted));
        _conversionCache[input] = result;
        return result;
    }

    public bool ContainsConvertibleSimplified(string text)
    {
        if (_simplifiedCache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var tokens = TokenPattern().Matches(text).Select(match => match.Value).ToArray();
        var protectedText = Protect(text, tokens);
        var result = !string.Equals(_toTaiwan.Convert(protectedText, punctuation: false), protectedText, StringComparison.Ordinal);
        _simplifiedCache[text] = result;
        return result;
    }

    public string ConvertForFormalDatabase(string text)
    {
        var converted = Convert(text, string.Empty).ConvertedText;
        if (ContainsSimplifiedGlyph(converted))
        {
            converted = _glyphToTraditional.Convert(converted, punctuation: false);
            converted = converted.Replace("倒霉", "倒楣", StringComparison.Ordinal);
            converted = converted.Replace("准", "準", StringComparison.Ordinal);
            converted = converted.Replace("几", "幾", StringComparison.Ordinal);
            converted = converted.Replace("划", "劃", StringComparison.Ordinal);
            converted = converted.Replace("游", "遊", StringComparison.Ordinal);
        }

        return converted;
    }

    public bool ContainsSimplifiedGlyph(string text)
    {
        return FindSimplifiedGlyphs(text).Length != 0;
    }

    public string FindSimplifiedGlyphs(string text)
    {
        var result = new HashSet<char>();
        foreach (var character in text.Where(IsCjk))
        {
            if (TraditionalSharedGlyphs.Contains(character))
            {
                continue;
            }

            if (!_simplifiedGlyphCache.TryGetValue(character, out var simplified))
            {
                var value = character.ToString();
                simplified = !string.Equals(
                    _glyphToTraditional.Convert(value, punctuation: false),
                    value,
                    StringComparison.Ordinal);
                _simplifiedGlyphCache[character] = simplified;
            }

            if (simplified)
            {
                result.Add(character);
            }
        }

        foreach (var (phrase, glyph) in ContextualSimplifiedPhrases)
        {
            if (text.Contains(phrase, StringComparison.Ordinal))
            {
                result.Add(glyph);
            }
        }

        return string.Concat(result.Order());
    }

    private static string DetectOriginalLanguage(string input, string converted, bool containsTraditionalEvidence)
    {
        if (!input.Any(IsCjk))
        {
            return "NotApplicable";
        }

        if (string.Equals(input, converted, StringComparison.Ordinal))
        {
            return "zh-Hant";
        }

        return containsTraditionalEvidence ? "Mixed-zh-Hans-zh-Hant" : "zh-Hans";
    }

    private static string Protect(string input, IReadOnlyList<string> tokens)
    {
        var cursor = 0;
        return TokenPattern().Replace(input, _ => $"\ue000{cursor++:X4}\ue001");
    }

    private static string Restore(string converted, IReadOnlyList<string> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            converted = converted.Replace($"\ue000{index:X4}\ue001", tokens[index], StringComparison.Ordinal);
        }

        return converted;
    }

    private string ConvertToFixedPoint(string value)
    {
        var converted = _toTaiwan.Convert(value, punctuation: false);
        for (var pass = 1; pass < 4; pass++)
        {
            var next = _toTaiwan.Convert(converted, punctuation: false);
            if (string.Equals(next, converted, StringComparison.Ordinal))
            {
                return converted;
            }

            converted = next;
        }

        return converted;
    }

    private static bool IsValidUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCjk(char character) => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';

    [GeneratedRegex(@"\{[^{}\r\n]+\}|%(?:\d+\$)?[a-zA-Z]|\\r\\n|\\[nr]|\[(?:item|skill|resource):[^\]\r\n]+\]|<[^>\r\n]+>|[A-Za-z][A-Za-z0-9]*(?:[_./\\-][A-Za-z0-9]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"<[^>\r\n]+>", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupPattern();

    [GeneratedRegex(@"\\r\\n|\\[nr]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlPattern();
}

public static class God2Glossary
{
    public static IReadOnlyList<GlossaryEntry> Create() =>
    [
        Official("昆仑仙界3", "崑崙仙界3", "Map", "db/imports/official/maps/maps.official.json", "Historical official-client visual label paired with the resource group."),
        Official("枫华镇", "楓華鎮", "Map", "Bahamut board 8395 and existing official-client resource evidence", "Exact Taiwan game place name corroborated by independent sources."),
        Project("圣樵原野", "聖樵原野", "Map"),
        Project("服务器", "伺服器", "System"),
        Project("数据库", "資料庫", "System"),
        Project("登录", "登入", "System"),
        Project("鼠标", "滑鼠", "UI"),
        Project("信息", "資訊", "UI"),
        Project("网络", "網路", "System"),
        Project("文件", "檔案", "UI"),
        Project("软件", "軟體", "UI"),
        Project("硬盘", "硬碟", "UI")
    ];

    private static GlossaryEntry Official(string simplified, string traditional, string category, string identity, string notes) =>
        new(simplified, traditional, category, "OfficialZhTwCrossReference", identity, "High", "God2 content recovery cross-reference", notes);

    private static GlossaryEntry Project(string simplified, string traditional, string category) =>
        new(simplified, traditional, category, "ProjectVerifiedTerminology", "God2 zh-TW glossary v1", "High", "God2 localization policy", "Phrase override applied before the pinned offline OpenCC converter.");
}
