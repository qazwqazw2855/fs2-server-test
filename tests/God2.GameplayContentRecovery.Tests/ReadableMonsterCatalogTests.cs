using CatalogBuilder = God2.ReadableDatabaseBuilder.ReadableDatabaseBuilder;

namespace God2.GameplayContentRecovery.Tests;

public sealed class ReadableMonsterCatalogTests
{
    [Fact]
    public void MergeEncounterMonsterCatalog_DeduplicatesNamesAndCopiesOnlyExactStats()
    {
        var formal = new List<Dictionary<string, object?>>
        {
            new(StringComparer.Ordinal)
            {
                ["怪物編號"] = 14,
                ["名稱"] = "雲鶴",
                ["等級"] = 12,
                ["HP"] = 567L,
                ["經驗"] = 283L
            }
        };
        var encounters = new List<Dictionary<string, object?>>
        {
            new(StringComparer.Ordinal) { ["目錄順序"] = 30L, ["名稱"] = "雲鶴" },
            new(StringComparer.Ordinal) { ["目錄順序"] = 10L, ["名稱"] = "褐蝸螺" },
            new(StringComparer.Ordinal) { ["目錄順序"] = 40L, ["名稱"] = "雲鶴" }
        };

        var result = CatalogBuilder.MergeEncounterMonsterCatalog(formal, encounters);

        Assert.Equal(2, result.Count);
        Assert.Equal("褐蝸螺", result[0]["名稱"]);
        Assert.Equal(1, result[0]["怪物編號"]);
        Assert.False(result[0].ContainsKey("等級"));
        Assert.Equal("雲鶴", result[1]["名稱"]);
        Assert.Equal(12, result[1]["等級"]);
        Assert.Equal(567L, result[1]["HP"]);
        Assert.Equal(283L, result[1]["經驗"]);
        Assert.DoesNotContain(result, row => row.ContainsKey("目錄順序"));
    }

    [Fact]
    public void MergeEncounterMonsterCatalog_UsesFormalRowsWhenEncounterCatalogIsUnavailable()
    {
        var formal = new List<Dictionary<string, object?>>
        {
            new(StringComparer.Ordinal) { ["怪物編號"] = 1, ["名稱"] = "海柱" }
        };

        var result = CatalogBuilder.MergeEncounterMonsterCatalog(formal, []);

        var monster = Assert.Single(result);
        Assert.Equal("海柱", monster["名稱"]);
        Assert.Equal(1, monster["怪物編號"]);
    }
}
