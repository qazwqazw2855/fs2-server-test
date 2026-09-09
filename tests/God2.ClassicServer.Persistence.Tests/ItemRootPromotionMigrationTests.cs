using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class ItemRootPromotionMigrationTests
{
    [Fact]
    public void Migration419PromotesReadableLegacyItemRootsWithoutGuessingSplitStats()
    {
        var schema = ReadSchema("419_promote_readable_legacy_items_to_formal_item_roots.sql");

        Assert.Contains("item_root_promotion_audit", schema, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO god2_game.items", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_item.NameZhTw", schema, StringComparison.Ordinal);
        Assert.Contains("description_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("has_formal_equipment", schema, StringComparison.Ordinal);
        Assert.Contains("has_formal_weapon", schema, StringComparison.Ordinal);
        Assert.Contains("has_formal_magic_treasure", schema, StringComparison.Ordinal);
        Assert.Contains("不解析 DescriptionZhTw 內的攻防文字", schema, StringComparison.Ordinal);
        Assert.Contains("不猜裝備數值", schema, StringComparison.Ordinal);
        Assert.Contains("不搬 SourceHash/RunId/payload", schema, StringComparison.Ordinal);
        Assert.Contains("已補正式道具主表，待拆表與使用規則稽核", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO god2_game.equipment", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO god2_game.weapons", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO god2_game.magic_treasures", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceHash", schema.Replace("不搬 SourceHash/RunId/payload", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("RunId", schema.Replace("不搬 SourceHash/RunId/payload", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        var schemaWithoutPolicyText = schema.Replace("不搬 SourceHash/RunId/payload", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Payload", schemaWithoutPolicyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM `god2`", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE `god2`", schema, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSchema(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(baseDirectory))
        {
            var candidate = Path.Combine(baseDirectory, "database", "schema", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            baseDirectory = Directory.GetParent(baseDirectory)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate database/schema/{fileName}");
    }
}
