using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class NpcDialogGapEvidenceAuditMigrationTests
{
    [Fact]
    public void Migration415PublishesNpcDialogEvidenceAuditAndDisablesUnsafeAutoPromotion()
    {
        var schema = ReadSchema("415_publish_npc_dialog_gap_evidence_audit.sql");

        Assert.Contains("npc_dialog_gap_evidence_audit", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_dialog_id", schema, StringComparison.Ordinal);
        Assert.Contains("has_formal_npc", schema, StringComparison.Ordinal);
        Assert.Contains("has_zh_tw_text", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_promote_now", schema, StringComparison.Ordinal);
        Assert.Contains("缺 NPC 對應與繁中對話文字", schema, StringComparison.Ordinal);
        Assert.Contains("不能把空白或代碼當成正式對話內容", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_apply_automatically = 0", schema, StringComparison.Ordinal);
        Assert.Contains("缺證據，暫停自動補", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO god2_game.npc_dialogs", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`npc_dialogs`", schema, StringComparison.OrdinalIgnoreCase);
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
