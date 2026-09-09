using God2.ClassicServer.Persistence;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class CanonicalRuntimeSqlTests
{
    [Fact]
    public void Character_creation_reads_only_canonical_named_columns()
    {
        var sql = MariaDbCharacterCreationAuthority.ResolveCommandText;

        Assert.Contains("`god2_game`.`character_creation_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_game`.`maps`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile.`evidence_status`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("profile.`evidence_reference`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("content_field_evidence", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'Derived'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Account_session_write_targets_canonical_player_schema()
    {
        var sql = MariaDbAccountRepository.ReplaceCurrentSessionCommandText;

        Assert.Contains("UPDATE `god2_player`.`accounts`", sql, StringComparison.Ordinal);
        Assert.Contains("`current_session_id`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `accounts`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Character_creation_inserts_exactly_the_four_canonical_life_skill_rows()
    {
        var sql = MariaDbCharacterRepository.CreateLifeSkillRowsCommandText;

        Assert.Contains("INSERT INTO `god2_player`.`character_life_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_game`.`life_skills`", sql, StringComparison.Ordinal);
        Assert.Contains("'ArmorForging', 'WeaponForging', 'PillAlchemy', 'MagicTreasureForging'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("god2`.`character_life_skills", sql, StringComparison.Ordinal);
    }
}
