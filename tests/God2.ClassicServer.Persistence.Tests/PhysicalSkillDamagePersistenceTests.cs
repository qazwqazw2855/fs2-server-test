using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class PhysicalSkillDamagePersistenceTests
{
    [Fact]
    public void MigrationPublishesFortyEightMeasuredRangesAndReadableView()
    {
        var sql = Read("database", "schema", "100_publish_community_physical_skill_coefficients.sql");
        var rowCount = System.Text.RegularExpressions.Regex.Matches(
            sql,
            "(?m)^\\s*\\('(Blade|Sword|Staff|Whip|Spear|ThrowingKnife)',[1-8],").Count;

        Assert.Equal(48, rowCount);
        Assert.Contains("`minimum_multiplier`", sql, StringComparison.Ordinal);
        Assert.Contains("`midpoint_multiplier`", sql, StringComparison.Ordinal);
        Assert.Contains("`maximum_multiplier`", sql, StringComparison.Ordinal);
        Assert.Contains("CommunityCorroborated", sql, StringComparison.Ordinal);
        Assert.Contains("CommunityMeasured", sql, StringComparison.Ordinal);
        Assert.Contains("sn=2481", sql, StringComparison.Ordinal);
        Assert.Contains("sn=1794", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_physical_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("('Bow',", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("('Axe',", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRuntimeLoadsAndExposesPhysicalSkillCoefficientEngine()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRuntime.cs");
        var repository = Read("src", "God2.ClassicServer.Persistence", "MariaDbPhysicalSkillDamageCatalogRepository.cs");

        Assert.Contains("IPhysicalSkillDamageRuntime", source, StringComparison.Ordinal);
        Assert.Contains("MariaDbPhysicalSkillDamageCatalogRepository", source, StringComparison.Ordinal);
        Assert.Contains("PhysicalSkillDamageCoefficientCount", source, StringComparison.Ordinal);
        Assert.Contains("ResolvePhysicalSkillCoefficient", source, StringComparison.Ordinal);
        Assert.Contains("ResolvePhysicalSkillMultiplier", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("`primary_source_url`", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceSplitMigrationKeepsPhysicalFormulaParametersInFormalTableOnly()
    {
        var sql = Read("database", "schema", "167_split_physical_skill_damage_evidence.sql");

        Assert.Contains("`god2_research`.`physical_skill_damage_coefficient_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_physical_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_physical_skill_coefficients_sources`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `independent_source_count`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `primary_source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `corroborating_source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_physical_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("`完整傷害公式`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`primary_source_url` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "MariaDb")]
    public async Task FormalRepositoryLoadsAllFortyEightRanges()
    {
        var password = Environment.GetEnvironmentVariable("GOD2_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var options = new DatabaseOptions("127.0.0.1", 3306, "god2", "god2_server", password, 2, "ConfigValue");
        var catalog = await new MariaDbPhysicalSkillDamageCatalogRepository(options).LoadAsync(CancellationToken.None);

        Assert.Equal(48, catalog.RuleCount);
        var blade = catalog.Resolve(PhysicalSkillFamily.Blade, 1).Value!;
        Assert.Equal((1.5m, 1.8m, 2.1m),
            (blade.MinimumMultiplier, blade.MidpointMultiplier, blade.MaximumMultiplier));
        Assert.Equal(1, blade.IndependentSourceCount);
        Assert.Equal("god2_research.physical_skill_damage_coefficient_evidence", blade.PrimarySource);
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
