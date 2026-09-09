namespace God2.ClassicServer.Persistence.Tests;

public sealed class ItemEffectVisualCatalogMigrationTests
{
    [Fact]
    public void Migration_publishes_only_a_static_client_visual_catalog_and_blocks_runtime_use()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "139_publish_verified_client_item_effect_visual_catalog.sql"));

        Assert.Contains("`god2_game`.`client_item_effect_visuals`", sql, StringComparison.Ordinal);
        Assert.Contains("`ck_client_item_effect_visual_runtime_blocked` CHECK (`runtime_eligible`=0)", sql, StringComparison.Ordinal);
        Assert.Contains("'VerifiedExactClientStaticVisualCatalog'", sql, StringComparison.Ordinal);
        Assert.Contains("'DisplayTextOnlyServerAuthorityBlocked'", sql, StringComparison.Ordinal);
        Assert.Contains("c83717a53d06d1736393212de975206110145553f80e71349ac11a4863f155da", sql, StringComparison.Ordinal);
        Assert.Contains("7a702d8c432d7afbbc4eea33e5e98869c2d51cd4489486ac2a2225365a84c010", sql, StringComparison.Ordinal);
        Assert.Contains("(8435,'ItemEft3','/Data2/Item/Itm8435.rom','/Data2/Item/Itm8435.rtg'", sql, StringComparison.Ordinal);
        Assert.Contains("(8529,'GodItemEft','/Data2/Item/Itm8529.rom','/Data2/Item/Itm8529.rtg',735", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`god2_game`.`item_effects`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_game`.`item_usage_rules`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
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
