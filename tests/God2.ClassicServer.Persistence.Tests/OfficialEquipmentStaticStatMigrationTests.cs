namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentStaticStatMigrationTests
{
    private const string ExactSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";

    [Fact]
    public void Migration_publishes_only_explicit_equ_stats_and_withdraws_implicit_zeroes()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "134_publish_verified_equipment_static_stats_and_withdraw_implicit_zeroes.sql"));

        Assert.Contains("COUNT(*) FROM `tmp_equipment_static_text`)=731", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) FROM `tmp_equipment_explicit_stat_seed`)=1577", sql, StringComparison.Ordinal);
        Assert.Contains("`stat_type`='PhysicalDefense')=731", sql, StringComparison.Ordinal);
        Assert.Contains("`stat_type`='MagicDefense')=729", sql, StringComparison.Ordinal);
        Assert.Contains("`stat_type`='Speed')=117", sql, StringComparison.Ordinal);
        Assert.Contains("(物攻|魔攻|HP|MP)[+＋]-?[0-9]+')=0", sql, StringComparison.Ordinal);
        Assert.Contains("(物防|魔防|速度)[+＋]0([^0-9]|$)')=0", sql, StringComparison.Ordinal);
        Assert.Contains(ExactSourceSha256, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'VerifiedOfficialClientText','EvidenceBlocked',0", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`physical_attack_bonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`magic_attack_bonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`hp_bonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`mp_bonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`physical_defense_bonus`=pivot_row.`physical_defense`", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`magic_defense_bonus`=pivot_row.`magic_defense`", sql, StringComparison.Ordinal);
        Assert.Contains("equipment_row.`speed_bonus`=pivot_row.`speed`", sql, StringComparison.Ordinal);
        Assert.Contains("profile_row.`PhysicalAttackBonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`runtime_eligible`=0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase2_mapper_preserves_absent_stats_as_null_instead_of_zero()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase2ClientExhaustionExtractor.cs"));

        Assert.Contains("[\"physicalAttackBonus\"] = StatOrNull(stats, \"物攻\")", source, StringComparison.Ordinal);
        Assert.Contains("[\"magicDefenseBonus\"] = StatOrNull(stats, \"魔防\")", source, StringComparison.Ordinal);
        Assert.Contains("[\"speedBonus\"] = StatOrNull(stats, \"速度\")", source, StringComparison.Ordinal);
        Assert.Contains("stats.TryGetValue(name, out var value) ? value : null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stats.GetValueOrDefault(\"物攻\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("stats.GetValueOrDefault(\"物防\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Weapon_migration_publishes_explicit_wpn_stats_and_withdraws_profile_zeroes()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "135_publish_verified_weapon_static_stats_and_withdraw_implicit_zeroes.sql"));

        Assert.Contains("COUNT(*) FROM `tmp_weapon_static_text`)=645", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) FROM `tmp_weapon_explicit_stat_seed`)=1541", sql, StringComparison.Ordinal);
        Assert.Contains("`stat_type`='PhysicalAttack')=645", sql, StringComparison.Ordinal);
        Assert.Contains("`stat_type`='MagicAttack')=645", sql, StringComparison.Ordinal);
        Assert.Contains("`stat_type`='Speed')=251", sql, StringComparison.Ordinal);
        Assert.Contains(ExactSourceSha256, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'VerifiedOfficialClientText','EvidenceBlocked',0", sql, StringComparison.Ordinal);
        Assert.Contains("w.`physical_attack_bonus`=p.`physical_attack`", sql, StringComparison.Ordinal);
        Assert.Contains("w.`magic_attack_bonus`=p.`magic_attack`", sql, StringComparison.Ordinal);
        Assert.Contains("r.`PhysicalDefenseBonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("r.`MagicDefenseBonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("r.`HpBonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("r.`MpBonus`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("r.`SpeedBonus`=p.`speed`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
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
