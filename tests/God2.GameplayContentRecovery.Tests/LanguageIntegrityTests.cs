using System.Text.Json;
using God2.GameplayContentRecovery;

namespace God2.GameplayContentRecovery.Tests;

public sealed class LanguageIntegrityTests
{
    [Fact]
    public void Mojibake_policy_rejects_replacement_controls_and_known_utf8_misdecoding()
    {
        Assert.Equal("UnicodeReplacementCharacter", LanguageIntegrityPolicy.DetectMojibake("服務端�就緒"));
        Assert.Equal("UnexpectedControlCharacter", LanguageIntegrityPolicy.DetectMojibake("服務端\u0001就緒"));
        Assert.Equal("KnownUtf8MojibakeSequence", LanguageIntegrityPolicy.DetectMojibake("鏈嶅嫏绔凡灏辩窉"));
        Assert.Null(LanguageIntegrityPolicy.DetectMojibake("服務端已就緒"));
    }

    [Fact]
    public void Simplified_glyph_detection_does_not_misclassify_traditional_phrase_normalization()
    {
        var localization = new ZhTwLocalization(God2Glossary.Create());

        Assert.True(localization.ContainsSimplifiedGlyph("数据库连接成功"));
        Assert.True(localization.ContainsSimplifiedGlyph("不可存洞仓"));
        Assert.True(localization.ContainsSimplifiedGlyph("台版必須另行校準"));
        Assert.True(localization.ContainsSimplifiedGlyph("倒霉殭屍帽"));
        Assert.False(localization.ContainsSimplifiedGlyph("資料庫連線成功"));
        Assert.False(localization.ContainsSimplifiedGlyph("MariaDB 內存在且具必要權限"));
        Assert.False(localization.ContainsSimplifiedGlyph("淳于添、三尸星、占卜、所云"));
        Assert.False(localization.ContainsSimplifiedGlyph("一台機器發霉了"));
    }

    [Fact]
    public void Shipped_localization_values_are_traditional_and_not_mojibake()
    {
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "localization"), "*.json", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "config"), "*.json", SearchOption.TopDirectoryOnly));

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var value in ReadStrings(document.RootElement))
            {
                Assert.Null(LanguageIntegrityPolicy.DetectMojibake(value));
                Assert.False(
                    localization.ContainsSimplifiedGlyph(value),
                    $"Simplified Chinese remains in {Path.GetRelativePath(root, file)}: {LanguageIntegrityPolicy.SafeSample(value)}");
            }
        }
    }

    [Fact]
    public void Runtime_source_and_automation_text_are_traditional_and_not_mojibake()
    {
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var root = RepositoryRoot();
        var files = EnumerateRuntimeTextFiles(root);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.Null(LanguageIntegrityPolicy.DetectMojibake(text));
            var glyphs = localization.FindSimplifiedGlyphs(text);
            Assert.True(
                glyphs.Length == 0,
                $"Simplified Chinese glyphs ({glyphs}) remain in {Path.GetRelativePath(root, file)}");
        }
    }

    [Fact]
    public void Formal_language_validation_script_uses_admin_secret_without_printing_it()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Validate-God2TraditionalChinese.ps1"));

        Assert.Contains("Initialize-God2DatabaseAdminPasswordEnvironment", script, StringComparison.Ordinal);
        Assert.Contains("--audit-only true", script, StringComparison.Ordinal);
        Assert.Contains("GOD2_DB_ADMIN_PASSWORD", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $env:GOD2_DB_ADMIN_PASSWORD", script, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString() ?? string.Empty;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var value in ReadStrings(property.Value))
                {
                    yield return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in ReadStrings(item))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRuntimeTextFiles(string root)
    {
        var roots = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "Automation"),
            Path.Combine(root, "scripts"),
            Path.Combine(root, "tools", "God2.ZhTwDatabaseViews")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cs" or ".ps1" or ".json")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
