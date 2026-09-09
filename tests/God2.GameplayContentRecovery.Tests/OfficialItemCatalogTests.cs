using System.Text.Json;
using God2.ReadableDatabaseBuilder;

namespace God2.GameplayContentRecovery.Tests;

public sealed class OfficialItemCatalogTests
{
    [Fact]
    public async Task Exact_current_official_catalog_has_all_rows_and_preserves_gameplay_boundaries()
    {
        var path = Path.Combine(RepositoryRoot(), "db", "imports", "official", "items", "items.official.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        Assert.Equal(17418, root.GetProperty("recordCount").GetInt32());
        Assert.Equal(17407, root.GetProperty("existingFormalBaselineCount").GetInt32());
        Assert.Equal(11, root.GetProperty("missingCanonicalBindingCount").GetInt32());
        Assert.Equal("Data2/Patch/Comm/gamedata.csvZ", root.GetProperty("source").GetString());
        Assert.Contains("VerifiedExactCurrentClientStaticCatalog", root.GetProperty("verificationStatus").GetString(), StringComparison.Ordinal);
        Assert.False(root.GetProperty("canDirectImportToGameplay").GetBoolean());

        var records = root.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(17418, records.Length);
        Assert.Single(records.Select(row => row.GetProperty("sourceSha256").GetString()).Distinct());
        Assert.All(records, row =>
        {
            Assert.Equal("c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", row.GetProperty("sourceSha256").GetString());
            Assert.False(row.GetProperty("runtimeEligible").GetBoolean());
            Assert.Null(row.GetProperty("serverAuthoritativeId").GetInt32OrNull());
        });

        var additions = records.Where(row => !row.GetProperty("presentInFormalBaseline").GetBoolean()).ToArray();
        Assert.Equal(11, additions.Length);
        Assert.All(additions, row => Assert.Equal("EvidenceBlockedMissingCanonicalItemBinding", row.GetProperty("formalCanonicalBindingStatus").GetString()));
        Assert.Contains(additions, row => row.GetProperty("itemType").GetString() == "COM" && row.GetProperty("clientItemId").GetInt32() == 26275);

        var skillBooks = await OfficialSkillBookCatalogParser.ParseAsync(path, new TraditionalChineseConverter());
        Assert.Equal(386, skillBooks.Count);
        var observed = Assert.Single(skillBooks, row => row.ClientItemId == 6501);
        Assert.Equal(5, observed.MpCost);
        Assert.Equal(3, observed.AttackRange);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

internal static class JsonElementNullableExtensions
{
    public static int? GetInt32OrNull(this JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();
}
