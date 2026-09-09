using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class ClientSkillMetadataPersistenceTests
{
    [Fact]
    public void MigrationPublishesCompleteClientCatalogAndReadableFields()
    {
        var sql = Read("database", "schema", "102_publish_official_client_skill_metadata.sql");

        Assert.Contains("`god2_game`.`skill_client_metadata`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_game`.`skill_client_metadata_mappings`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2`.`official_skill_book_catalog`", sql, StringComparison.Ordinal);
        Assert.Contains("`mp_cost`", sql, StringComparison.Ordinal);
        Assert.Contains("`attack_range`", sql, StringComparison.Ordinal);
        Assert.Contains("`target_scope_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("ExactNameIgnoringSpacing", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_skill_client_metadata_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `MP消耗`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `攻擊距離`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `作用範圍`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("skill_row.`enabled`=1", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionRuntimeLoadsAndExposesClientSkillMetadata()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRuntime.cs");
        var repository = Read("src", "God2.ClassicServer.Persistence", "MariaDbClientSkillMetadataCatalogRepository.cs");

        Assert.Contains("IClientSkillMetadataRuntime", source, StringComparison.Ordinal);
        Assert.Contains("MariaDbClientSkillMetadataCatalogRepository", source, StringComparison.Ordinal);
        Assert.Contains("ClientSkillMetadataCount", source, StringComparison.Ordinal);
        Assert.Contains("ResolveClientSkillMetadata", source, StringComparison.Ordinal);
        Assert.Contains("skill_client_metadata_mappings", repository, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "MariaDb")]
    public async Task FormalRepositoryLoadsCompleteOfficialClientCatalog()
    {
        var password = Environment.GetEnvironmentVariable("GOD2_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var options = new DatabaseOptions("127.0.0.1", 3306, "god2", "god2_server", password, 2, "ConfigValue");
        var catalog = await new MariaDbClientSkillMetadataCatalogRepository(options)
            .LoadAsync(CancellationToken.None);

        Assert.Equal(386, catalog.RecordCount);
        var sword = catalog.Resolve(6501).Value!;
        Assert.Contains("蒼穹訣。劍氣", sword.NameZhTw, StringComparison.Ordinal);
        Assert.Equal(5, sword.MpCost);
        Assert.Equal(3, sword.AttackRange);
        Assert.Equal("一體", sword.TargetScopeZhTw);
        Assert.Equal(ClientSkillTargetShape.SingleTarget, sword.TargetShape);
        Assert.Equal("OfficialClientLiveVerified", sword.TargetShapeEvidenceStatus);
    }

    [Fact]
    public void LiveVerifiedTargetShapeMigrationDoesNotPromoteUnknownCombatSemantics()
    {
        var sql = Read("database", "schema", "121_publish_live_verified_skill_target_shape.sql");

        Assert.Contains("`target_shape`='SingleTarget'", sql, StringComparison.Ordinal);
        Assert.Contains("`TargetScopeZhTw`='單體'", sql, StringComparison.Ordinal);
        Assert.Contains("OfficialClientLiveVerified", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("target_side", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("damage", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`enabled`=1", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

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
