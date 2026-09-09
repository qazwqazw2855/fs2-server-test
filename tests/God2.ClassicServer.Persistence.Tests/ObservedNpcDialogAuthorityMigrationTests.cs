namespace God2.ClassicServer.Persistence.Tests;

public sealed class ObservedNpcDialogAuthorityMigrationTests
{
    private static readonly string MigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "124_finalize_observed_npc_dialog_authority.sql");

    [Fact]
    public void Migration_promotes_only_the_formally_accepted_3793_dialog_slice()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("`observed_client_entity_handle` = 3793", sql, StringComparison.Ordinal);
        Assert.Contains("`service_evidence_status` = 'CompleteObservedDialogSlice'", sql, StringComparison.Ordinal);
        Assert.Contains("`service_evidence_status` IN ('OpenOnly','CompleteObservedDialogSlice')", sql, StringComparison.Ordinal);
        Assert.Contains("every other selector remains evidence-blocked", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("observed_client_entity_handle` = 1504", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("observed_client_entity_handle` = 4638", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_disables_incomplete_catalog_dialogs_without_deleting_evidence()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("UPDATE `god2_game`.`npc_dialogs`", sql, StringComparison.Ordinal);
        Assert.Contains("dialog_row.`enabled` = 0", sql, StringComparison.Ordinal);
        Assert.Contains("dialog_row.`npc_id` IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("NULLIF(TRIM(dialog_row.`body_zh_tw`),'') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("NULLIF(TRIM(dialog_row.`dialog_type`),'') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_game`.`npc_dialog_options`", sql, StringComparison.Ordinal);
        Assert.Contains("CatalogOnlyEvidenceBlocked", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
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
