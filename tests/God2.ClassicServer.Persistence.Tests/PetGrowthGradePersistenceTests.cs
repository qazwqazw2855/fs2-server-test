using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class PetGrowthGradePersistenceTests
{
    [Fact]
    public void MigrationPublishesFourGradesAndTwelveVerifiedLevelBands()
    {
        var sql = Read("Database", "schema", "089_publish_pet_growth_grades.sql");

        Assert.Contains("`pet_growth_grade_rules`", sql, StringComparison.Ordinal);
        Assert.Contains("('Normal',1,19,4,1", sql, StringComparison.Ordinal);
        Assert.Contains("('Top',1,19,5,1", sql, StringComparison.Ordinal);
        Assert.Contains("('LateBreakthrough',50,99,10,1", sql, StringComparison.Ordinal);
        Assert.Contains("('Breakthrough',20,49,8,1", sql, StringComparison.Ordinal);
        Assert.Equal(
            12,
            System.Text.RegularExpressions.Regex.Matches(
                sql,
                "(?m)^\\s*\\('(Normal|Top|LateBreakthrough|Breakthrough)',").Count);
        Assert.DoesNotContain("probability", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationAddsReadableGradeStateToPetInstances()
    {
        var sql = Read("Database", "schema", "089_publish_pet_growth_grades.sql");

        Assert.Contains("`initial_growth_quality`", sql, StringComparison.Ordinal);
        Assert.Contains("`growth_grade_status`", sql, StringComparison.Ordinal);
        Assert.Contains("`growth_grade_confirmed_level`", sql, StringComparison.Ordinal);
        Assert.Contains("`last_automatic_growth_total`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_pet_growth_grades_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `戰寵品級`", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN 'LateBreakthrough' THEN '晚破'", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN 'Breakthrough' THEN '破頂'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryLoadsFormalRulesWithoutMachineCodesInTheReadableView()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbPetGrowthGradeCatalogRepository.cs");

        Assert.Contains("FROM `god2_game`.`pet_growth_grade_rules`", source, StringComparison.Ordinal);
        Assert.Contains("new PetGrowthGradeRule", source, StringComparison.Ordinal);
        Assert.Contains("PetInitialGrowthQuality", source, StringComparison.Ordinal);
        Assert.Contains("PetGrowthGradeCatalog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`source_reference_zh_tw`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRuntimeLoadsAndExposesPetGrowthGradeEngine()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRuntime.cs");

        Assert.Contains("IPetGrowthGradeRuntime", source, StringComparison.Ordinal);
        Assert.Contains("MariaDbPetGrowthGradeCatalogRepository", source, StringComparison.Ordinal);
        Assert.Contains("PetGrowthGradeRuleCount", source, StringComparison.Ordinal);
        Assert.Contains("ClassifyPetInitialGrowth", source, StringComparison.Ordinal);
        Assert.Contains("ObservePetLevelUp", source, StringComparison.Ordinal);
        Assert.Contains("ResolvePetAutomaticGrowth", source, StringComparison.Ordinal);
        Assert.Contains("CalculatePetCoreStats", source, StringComparison.Ordinal);
        Assert.Contains("IPetLifecycleCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("AcquirePetAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPetLevelUpAsync", source, StringComparison.Ordinal);
        Assert.Contains("TeachPetFourthSkillAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPetDeathAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticGrowthMigrationPublishesPerStatValuesAndFixedBaseStats()
    {
        var sql = Read("Database", "schema", "092_publish_pet_automatic_growth_allocations.sql");

        Assert.Contains("`pet_growth_archetypes`", sql, StringComparison.Ordinal);
        Assert.Contains("`pet_automatic_growth_allocations`", sql, StringComparison.Ordinal);
        Assert.Contains("`constitution_delta`", sql, StringComparison.Ordinal);
        Assert.Contains("`strength_delta`", sql, StringComparison.Ordinal);
        Assert.Contains("`intelligence_delta`", sql, StringComparison.Ordinal);
        Assert.Contains("`speed_delta`", sql, StringComparison.Ordinal);
        Assert.Contains("`base_constitution`=100", sql, StringComparison.Ordinal);
        Assert.Contains("`base_strength`=50", sql, StringComparison.Ordinal);
        Assert.Contains("`base_intelligence`=50", sql, StringComparison.Ordinal);
        Assert.Contains("`base_speed`=100", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_pet_category_growth_values_readable`", sql, StringComparison.Ordinal);
        Assert.Equal(
            564,
            System.Text.RegularExpressions.Regex.Matches(
                sql,
                "(?m)^\\s*\\([0-9]+,'(Normal|Top|Breakthrough|LateBreakthrough)',").Count);
    }

    [Fact]
    public void RepositoryLoadsOnlyVerifiedPerStatAllocations()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbPetGrowthGradeCatalogRepository.cs");

        Assert.Contains("FROM `god2_game`.`pet_categories`", source, StringComparison.Ordinal);
        Assert.Contains("JOIN `god2_game`.`pet_automatic_growth_allocations`", source, StringComparison.Ordinal);
        Assert.Contains("WHERE allocation.`enabled`=1", source, StringComparison.Ordinal);
        Assert.Contains("new PetAutomaticGrowthAllocation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("allocation.`evidence_status`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("allocation.`source_url`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceSplitMigrationKeepsPetGrowthRuntimeTablesNumericAndReadable()
    {
        var sql = Read("database", "schema", "169_split_pet_growth_evidence.sql");

        Assert.Contains("`god2_research`.`pet_growth_grade_rule_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`pet_automatic_growth_allocation_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_growth_grade_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_pet_auto_growth_values`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_article_sn`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_url`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_pet_category_growth_values_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_pet_growth_manual_fill_queue`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("allocation.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("allocation.`source_url` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalPetRepositoriesLoadGrowthCaptureStatsSkillsAndDeploymentState()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbCanonicalCatalogRepositories.cs");

        Assert.Contains("`pet_category_id`", source, StringComparison.Ordinal);
        Assert.Contains("`level_20_skill_name_zh_tw`", source, StringComparison.Ordinal);
        Assert.Contains("`acquisition_origin`", source, StringComparison.Ordinal);
        Assert.Contains("`initial_growth_quality`", source, StringComparison.Ordinal);
        Assert.Contains("`growth_grade_status`", source, StringComparison.Ordinal);
        Assert.Contains("`is_deployed`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticDataValidationChecksPetGrowthAndOwnedPetAuthority()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbRuntimeData.cs");

        Assert.Contains("pet_templates.growth_authority", source, StringComparison.Ordinal);
        Assert.Contains("category_row.`category_id` IS NULL", source, StringComparison.Ordinal);
        Assert.Contains("category_row.`enabled`=1 AND category_row.`growth_archetype_id` IS NULL", source, StringComparison.Ordinal);
        Assert.Contains("character_pets.authority", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PetLifecycleMigrationAddsTransactionalAuthorityAndNormalizedSkillMappings()
    {
        var sql = Read("Database", "schema", "097_add_pet_lifecycle_transactions.sql");

        Assert.Contains("`pet_operation_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("`pet_operation_audit`", sql, StringComparison.Ordinal);
        Assert.Contains("`pet_skill_learning_items`", sql, StringComparison.Ordinal);
        Assert.Contains("`pet_template_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_pet_skill_mapping_queue`", sql, StringComparison.Ordinal);
        Assert.Contains("未對應者不發布", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT IGNORE INTO `god2_game`.`pet_template_skills`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM `god2_game`.`pet_template_skills`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void WikiRefreshFillsVerifiedGrowthGapsWithoutDowngradingExpandedSkillNames()
    {
        var sql = Read("Database", "schema", "098_refresh_general_pet_wiki_data.sql");

        Assert.Contains("(65,'魔女類'", sql, StringComparison.Ordinal);
        Assert.Contains("(66,'鬼類'", sql, StringComparison.Ordinal);
        Assert.Contains("(18,'LateBreakthrough',50,99,4,5,0,1,10,'4體5力1速'", sql, StringComparison.Ordinal);
        Assert.Contains("(6,'Top',1,19,3,2,0,0,5,'3體2力'", sql, StringComparison.Ordinal);
        Assert.Contains("'WikiVerified'", sql, StringComparison.Ordinal);
        Assert.Contains("Existing expanded Traditional Chinese skill names remain authoritative", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET `level_20_skill_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Equal(
            37,
            System.Text.RegularExpressions.Regex.Matches(
                sql,
                "(?m)^\\s*\\([0-9]+,'(Normal|Top|Breakthrough|LateBreakthrough)',").Count);
    }

    [Fact]
    public void PetInnateSkillMigrationPublishesReadableIdentityAndUnlockMappingsButBlocksUnknownEffects()
    {
        var sql = Read("Database", "schema", "099_publish_pet_innate_skill_identities.sql");

        Assert.Contains("3000000000 + source_row.`sequence_number`", sql, StringComparison.Ordinal);
        Assert.Contains("'PetInnate','PetSkill','WikiVerified',0", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT IGNORE INTO `god2_game`.`pet_template_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("20 AS `required_level`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_pet_innate_skills_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("名稱與解鎖已實裝，效果公式待驗證", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualCompletionMigrationCalculatesTotalsAndKeepsRuntimeFailClosed()
    {
        var sql = Read("Database", "schema", "093_enable_manual_pet_growth_completion.sql");

        Assert.Contains("GENERATED ALWAYS AS", sql, StringComparison.Ordinal);
        Assert.Contains("`evidence_status`='UserConfirmed'", sql, StringComparison.Ordinal);
        Assert.Contains("`published_total`=`expected_total`", sql, StringComparison.Ordinal);
        Assert.Contains("`source_reference_zh_tw` IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_pet_growth_manual_fill_queue`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `必須符合的合計`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyGenericGrowthProfileIsRemovedAfterFormalAllocationsReplaceIt()
    {
        var sql = Read("Database", "schema", "096_remove_legacy_pet_growth_profiles.sql");

        Assert.Contains("DROP FOREIGN KEY IF EXISTS `fk_pet_templates_growth_profile`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS `ix_pet_templates_growth_profile`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `growth_profile_id`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2_game`.`pet_growth_profiles`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("pet_growth_grade_rules", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("pet_automatic_growth_allocations", sql, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "MariaDb")]
    public async Task FormalRepositoryLoadsAllTwelveRules()
    {
        var password = Environment.GetEnvironmentVariable("GOD2_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var options = new DatabaseOptions("127.0.0.1", 3306, "god2", "god2_server", password, 2, "ConfigValue");
        var catalog = await new MariaDbPetGrowthGradeCatalogRepository(options).LoadAsync(CancellationToken.None);

        Assert.Equal(12, catalog.RuleCount);
        Assert.Equal(5, catalog.Resolve(PetGrowthGrade.Top, 1).Value!.AutomaticPointsPerLevel);
        Assert.Equal(8, catalog.Resolve(PetGrowthGrade.Breakthrough, 20).Value!.AutomaticPointsPerLevel);
        Assert.Equal(10, catalog.Resolve(PetGrowthGrade.LateBreakthrough, 50).Value!.AutomaticPointsPerLevel);
        Assert.True(catalog.AllocationRuleCount > 0);
        var fox = catalog.ResolveAllocation(1, PetGrowthGrade.Normal, 1).Value!;
        Assert.Equal(2, fox.StrengthDelta);
        Assert.Equal(2, fox.SpeedDelta);
        Assert.Equal(new PetCoreStats(100, 52, 50, 102),
            new PetGrowthGradeEngine(catalog).CalculateCoreStats(1, PetGrowthGrade.Normal, 1).Value);
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
