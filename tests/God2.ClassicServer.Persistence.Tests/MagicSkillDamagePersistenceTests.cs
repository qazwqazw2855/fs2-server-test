using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class MagicSkillDamagePersistenceTests
{
    [Fact]
    public void MigrationPublishesOnlyExplicitlyMeasuredMagicCoefficients()
    {
        var sql = Read("database", "schema", "101_publish_community_magic_skill_formula.sql");

        Assert.Contains("('Metal',1,1.0,1.1,1.2", sql, StringComparison.Ordinal);
        Assert.Contains("('Metal',2,0.7,0.8,0.9", sql, StringComparison.Ordinal);
        Assert.Contains("`attacker_same_element_divisor`", sql, StringComparison.Ordinal);
        Assert.Contains("`target_weak_element_divisor`", sql, StringComparison.Ordinal);
        Assert.Contains("`target_counter_element_divisor`", sql, StringComparison.Ordinal);
        Assert.Contains("`target_same_element_divisor`", sql, StringComparison.Ordinal);
        Assert.Contains("'MainlandCommunityTest','TaiwanUnverified'", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_magic_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("('Wood',", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("('Water',", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("('Fire',", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("('Earth',", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRuntimeLoadsMagicFormulaParametersFromMariaDb()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRuntime.cs");
        var repository = Read("src", "God2.ClassicServer.Persistence", "MariaDbMagicSkillDamageCatalogRepository.cs");

        Assert.Contains("IMagicSkillDamageRuntime", source, StringComparison.Ordinal);
        Assert.Contains("MariaDbMagicSkillDamageCatalogRepository", source, StringComparison.Ordinal);
        Assert.Contains("MagicSkillDamageCoefficientCount", source, StringComparison.Ordinal);
        Assert.Contains("CalculateMagicSkillDamage", source, StringComparison.Ordinal);
        Assert.Contains("`attacker_same_element_divisor`", repository, StringComparison.Ordinal);
        Assert.Contains("`xiandao_meditation_level1_bonus`", repository, StringComparison.Ordinal);
        Assert.Contains("`xiandao_meditation_level2_bonus`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("`source_url`", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionMigrationMakesMeasuredFormulaProductionDataForAllTierOneElements()
    {
        var sql = Read("database", "schema", "147_promote_measured_magic_formula.sql");

        Assert.Contains("`xiandao_meditation_level1_bonus`", sql, StringComparison.Ordinal);
        Assert.Contains("`xiandao_meditation_level2_bonus`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TRIGGER IF EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("information_schema`.`COLUMNS", sql, StringComparison.Ordinal);
        Assert.Contains("('Metal',1,1.0,1.1,1.2", sql, StringComparison.Ordinal);
        Assert.Contains("('Wood',1,1.0,1.1,1.2", sql, StringComparison.Ordinal);
        Assert.Contains("('Water',1,1.0,1.1,1.2", sql, StringComparison.Ordinal);
        Assert.Contains("('Fire',1,1.0,1.1,1.2", sql, StringComparison.Ordinal);
        Assert.Contains("('Earth',1,1.0,1.1,1.2", sql, StringComparison.Ordinal);
        Assert.Contains("('Metal',2,0.7,0.8,0.9", sql, StringComparison.Ordinal);
        Assert.Contains("'ProductionCompatibilityAccepted'", sql, StringComparison.Ordinal);
        Assert.Contains("'ProductionAccepted'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceSplitMigrationKeepsMagicFormulaParametersInFormalTableOnly()
    {
        var sql = Read("database", "schema", "166_split_magic_skill_damage_evidence.sql");

        Assert.Contains("`god2_research`.`magic_skill_damage_coefficient_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_magic_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `meditation_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_magic_skill_damage_coefficients_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("`完整傷害公式`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("coefficient.`source_url` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "MariaDb")]
    public async Task FormalRepositoryLoadsBothMeasuredMetalRanges()
    {
        var password = Environment.GetEnvironmentVariable("GOD2_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var options = new DatabaseOptions("127.0.0.1", 3306, "god2", "god2_server", password, 2, "ConfigValue");
        var catalog = await new MariaDbMagicSkillDamageCatalogRepository(options).LoadAsync(CancellationToken.None);

        Assert.Equal(6, catalog.RuleCount);
        Assert.Equal(1.1m, catalog.Resolve(BattleElement.Metal, 1).Value!.MidpointMultiplier);
        Assert.Equal(0.8m, catalog.Resolve(BattleElement.Metal, 2).Value!.MidpointMultiplier);
        Assert.Equal(1.1m, catalog.Resolve(BattleElement.Wood, 1).Value!.MidpointMultiplier);
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
