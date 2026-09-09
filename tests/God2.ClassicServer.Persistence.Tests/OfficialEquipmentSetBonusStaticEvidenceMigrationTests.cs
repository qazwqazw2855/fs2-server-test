namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentSetBonusStaticEvidenceMigrationTests
{
    private const string ExactSourceSha256 = "5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858";

    [Fact]
    public void Migration_publishes_all_exact_display_values_but_keeps_activation_disabled()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "133_publish_verified_equipment_set_bonus_text_values.sql"));

        Assert.Contains("COUNT(*) FROM `tmp_equipment_set_expected_effect`)=48", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) FROM `tmp_equipment_set_bonus_group_value`)=149", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) FROM `tmp_equipment_set_bonus_seed`)=385", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT `set_id`) FROM `tmp_equipment_set_bonus_seed`)=48", sql, StringComparison.Ordinal);
        Assert.Contains("`required_pieces`=2)=352", sql, StringComparison.Ordinal);
        Assert.Contains("`required_pieces`=5)=33", sql, StringComparison.Ordinal);
        Assert.Contains(ExactSourceSha256, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BINARY source_row.`EffectsZhTw`=BINARY expected_row.`effect_text`", sql, StringComparison.Ordinal);
        Assert.Contains("'VerifiedOfficialClientText','EvidenceBlocked',0", sql, StringComparison.Ordinal);
        Assert.Contains("`SetBonusEvidenceStatus`='VerifiedStaticTextOnly'", sql, StringComparison.Ordinal);
        Assert.Contains("source_row.`ProductionBonusEnabled`=0", sql, StringComparison.Ordinal);
        Assert.Contains("`status_effect_id`,`enabled`,`admin_note`", sql, StringComparison.Ordinal);
        Assert.Contains("NULL,0,'Verified exact current-client display value only", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=0", sql, StringComparison.Ordinal);
        Assert.Contains("`runtime_eligible`=0", sql, StringComparison.Ordinal);
        Assert.Contains("`activation_evidence_status`='EvidenceBlocked'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`ProductionBonusEnabled`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`enabled`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_locks_every_supported_normalized_bonus_type_and_representative_combined_text()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "133_publish_verified_equipment_set_bonus_text_values.sql"));

        foreach (var type in new[]
                 {
                     "Strength", "Constitution", "Intelligence", "Speed",
                     "PhysicalAttack", "MagicAttack", "PhysicalDefense", "MagicDefense",
                     "MaximumHp", "MaximumMp"
                 })
        {
            Assert.Contains($"'{type}'", sql, StringComparison.Ordinal);
        }

        Assert.Contains("(26,5,'J','四維+200 雙抗雙攻各+150、 HP MP各1000')", sql, StringComparison.Ordinal);
        Assert.Contains("(35,2,'N','力速+200 雙攻各+150、 HP MP各500')", sql, StringComparison.Ordinal);
        Assert.Contains("(45,5,'S','四基+200 雙抗雙攻各+200 HP+2000')", sql, StringComparison.Ordinal);
        Assert.Contains("(48,2,'T','四基+150 物理防禦+150 魔法防禦+150 HP+1500')", sql, StringComparison.Ordinal);
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
