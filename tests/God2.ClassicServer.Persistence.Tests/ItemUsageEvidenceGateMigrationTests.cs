namespace God2.ClassicServer.Persistence.Tests;

public sealed class ItemUsageEvidenceGateMigrationTests
{
    [Fact]
    public void Migration_publishes_only_new_static_identities_and_fail_closes_all_item_use_authority()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "140_gate_exact_client_item_usage_flags.sql"));
        Assert.Contains("`client_item_usage_flag_additions`", sql, StringComparison.Ordinal);
        Assert.Contains("('CBK',9530", sql, StringComparison.Ordinal);
        Assert.Contains("('COM',26275", sql, StringComparison.Ordinal);
        Assert.Contains("('MIS03',31408", sql, StringComparison.Ordinal);
        Assert.Contains("('SCD',9707", sql, StringComparison.Ordinal);
        Assert.Contains("52cf0d96a89e9451f625fd5e70beefdf9c84539e0c93bfe2f654c3206cc77b07", sql, StringComparison.Ordinal);
        Assert.Contains("actual_count<>17407", sql, StringComparison.Ordinal);
        Assert.Contains("`runtime_eligible`=0", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=0", sql, StringComparison.Ordinal);
        Assert.Contains("EvidenceBlockedMissingOfficialItemUseTransition", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `god2_game`.`item_registry`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_repository_requires_the_explicit_item_use_runtime_gate()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRepository.cs"));
        Assert.Contains("rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1", source, StringComparison.Ordinal);
        Assert.Contains("WHERE `enabled`=1 AND `runtime_eligible`=1", source, StringComparison.Ordinal);
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
