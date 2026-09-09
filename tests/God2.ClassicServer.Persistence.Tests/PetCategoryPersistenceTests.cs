namespace God2.ClassicServer.Persistence.Tests;

public sealed class PetCategoryPersistenceTests
{
    [Fact]
    public void MigrationPublishesReadableSequentialPetCategories()
    {
        var sql = Read("Database", "schema", "090_publish_battle_pet_categories.sql");

        Assert.Contains("`pet_categories`", sql, StringComparison.Ordinal);
        Assert.Contains("(1,'狐類','純種型','Pure'", sql, StringComparison.Ordinal);
        Assert.Contains("(64,'蝸牛類','補充族群','Mixed'", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_pet_categories_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `戰寵族群`", sql, StringComparison.Ordinal);
        Assert.Equal(
            64,
            System.Text.RegularExpressions.Regex.Matches(sql, "(?m)^\\s*\\([1-9][0-9]*,'").Count);
    }

    [Fact]
    public void MigrationSeparatesPlayerLevelOneFromWildMonsterLevel()
    {
        var sql = Read("Database", "schema", "090_publish_battle_pet_categories.sql");

        Assert.Contains("CHECK (`base_level`=1)", sql, StringComparison.Ordinal);
        Assert.Contains("`acquisition_origin`='LevelOnePet' AND `initial_level`=1", sql, StringComparison.Ordinal);
        Assert.Contains("`acquisition_origin`='CapturedWild'", sql, StringComparison.Ordinal);
        Assert.Contains("`initial_level`=`wild_encounter_level`", sql, StringComparison.Ordinal);
        Assert.Contains("`wild_source_monster_id`", sql, StringComparison.Ordinal);
        Assert.Contains("monster_row.`level` AS `野生遭遇等級`", sql, StringComparison.Ordinal);
        Assert.Contains("template_row.`base_level` AS `玩家初始等級`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationAllowsOnlyVerifiedWhiteNonQuestNonFormationMonsterCapture()
    {
        var sql = Read("Database", "schema", "090_publish_battle_pet_categories.sql");

        Assert.Contains("`name_color_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("`is_quest_monster`", sql, StringComparison.Ordinal);
        Assert.Contains("`is_formation_boss`", sql, StringComparison.Ordinal);
        Assert.Contains("`capture_eligibility`='Blocked'", sql, StringComparison.Ordinal);
        Assert.Contains("`capture_eligibility`='Capturable' AND `name_color_zh_tw`='白色'", sql, StringComparison.Ordinal);
        Assert.Contains("'黃字怪不可捕捉'", sql, StringComparison.Ordinal);
        Assert.Contains("'紅字怪不可捕捉'", sql, StringComparison.Ordinal);
        Assert.Contains("'任務怪不可捕捉'", sql, StringComparison.Ordinal);
        Assert.Contains("'陣法魔王不可捕捉'", sql, StringComparison.Ordinal);
        Assert.Contains("`capture_rule_source_article_sn`=921", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_monster_capture_eligibility_readable`", sql, StringComparison.Ordinal);
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
