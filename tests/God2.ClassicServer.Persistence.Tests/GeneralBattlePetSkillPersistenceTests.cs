namespace God2.ClassicServer.Persistence.Tests;

public sealed class GeneralBattlePetSkillPersistenceTests
{
    [Fact]
    public void GeneratedMigrationContainsOnlyGeneralPetsWithFullSkillNames()
    {
        var sql = Read("Database", "schema", "091_publish_general_battle_pet_skills.sql");

        Assert.Contains("一般戰寵", sql, StringComparison.Ordinal);
        Assert.Contains("獸之精  刀一  破星斬。瞬斬", sql, StringComparison.Ordinal);
        Assert.Contains("獸之神  金一-天雷一", sql, StringComparison.Ordinal);
        Assert.Contains("獸之氣  毒一-毒擊", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("九黎半島", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("太極境", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("'刀１'", sql, StringComparison.Ordinal);
        Assert.Equal(
            315,
            System.Text.RegularExpressions.Regex.Matches(sql, "(?m)^\\s*\\([1-9][0-9]*,'").Count);
    }

    [Fact]
    public void PetTableExposesLevelOneAndThreeReadableSkillColumns()
    {
        var sql = Read("Database", "schema", "090_publish_battle_pet_categories.sql");

        Assert.Contains("`level_20_skill_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`level_40_skill_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`level_60_skill_name_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `20級技能`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `玩家初始等級`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `捕捉轉換規則`", sql, StringComparison.Ordinal);
        Assert.Contains("保留野生遭遇等級", sql, StringComparison.Ordinal);
        Assert.Contains("不沿用野怪戰鬥能力值", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void FourthSkillSlotRequiresAConsumedLearningItemSource()
    {
        var sql = Read("Database", "schema", "090_publish_battle_pet_categories.sql");

        Assert.Contains("`slot_index` BETWEEN 1 AND 4", sql, StringComparison.Ordinal);
        Assert.Contains("`slot_index`=4 AND `unlock_source`='FedItem'", sql, StringComparison.Ordinal);
        Assert.Contains("`learned_from_item_id` IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_character_pet_skills_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("'餵食道具習得'", sql, StringComparison.Ordinal);
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
