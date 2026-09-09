using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class MerchantInventoryGapEvidenceAuditMigrationTests
{
    [Fact]
    public void Migration417PublishesMerchantInventoryGapAuditWithoutGuessingShopItems()
    {
        var schema = ReadSchema("417_publish_merchant_inventory_gap_evidence_audit.sql");

        Assert.Contains("merchant_inventory_gap_evidence_audit", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_merchant_id", schema, StringComparison.Ordinal);
        Assert.Contains("candidate_item_count", schema, StringComparison.Ordinal);
        Assert.Contains("production_sale_candidate_count", schema, StringComparison.Ordinal);
        Assert.Contains("formal_inventory_count", schema, StringComparison.Ordinal);
        Assert.Contains("缺商店販售商品證據", schema, StringComparison.Ordinal);
        Assert.Contains("不能猜商店賣哪些道具、價格或數量", schema, StringComparison.Ordinal);
        Assert.Contains("不得用商店名稱推測商品", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_apply_automatically = 0", schema, StringComparison.Ordinal);
        Assert.Contains("缺商品候選證據，暫停自動補", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO god2_game.merchant_inventory", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`merchant_inventory`", schema, StringComparison.OrdinalIgnoreCase);
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
