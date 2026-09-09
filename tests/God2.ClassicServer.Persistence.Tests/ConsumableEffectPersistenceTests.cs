namespace God2.ClassicServer.Persistence.Tests;

public sealed class ConsumableEffectPersistenceTests
{
    private static string MigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "086_publish_verified_consumable_effects.sql"));

    private static string ActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "087_activate_recovered_item_catalog.sql"));

    private static string FixedRecoveryActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "348_activate_fixed_value_consumable_recovery_items.sql"));

    private static string PercentRecoveryActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "349_activate_percent_recovery_consumable_items.sql"));

    private static string CleanseCandidateMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "350_register_cleanse_consumable_candidates_fail_closed.sql"));

    private static string TimedStatBuffCandidateMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "351_register_timed_stat_buff_consumable_candidates_fail_closed.sql"));

    private static string TimedStatBuffDurationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "352_add_item_effect_duration_for_timed_buff_candidates.sql"));

    private static string CharacterItemTimedEffectsMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "353_create_character_item_timed_effects.sql"));

    private static string TimedStatBuffActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "354_activate_timed_stat_buff_consumables.sql"));

    private static string CleanseActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "355_activate_cleanse_consumables.sql"));

    private static string ConstitutionTimedStatBuffActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "356_activate_constitution_timed_stat_buff_consumables.sql"));

    private static string BattleExperienceBonusActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "357_activate_battle_experience_bonus_consumables.sql"));

    private static string SpecialCategoryBattleExperienceBonusActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "358_enable_special_category_battle_experience_bonus_consumables.sql"));

    private static string ElementAttributeBonusActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "359_activate_element_attribute_bonus_consumables.sql"));

    private static string IdentificationMagnifierPercentRecoveryActivationMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "360_activate_identification_magnifier_percent_recovery_use.sql"));

    private static string FormalTextQualityAuditMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "361_publish_formal_text_quality_audit_views.sql"));

    private static string FormalSchemaTableInventoryAuditMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "362_publish_formal_schema_table_inventory_audit.sql"));

    private static string FirstWaveLegacyGod2TableCleanupMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "363_drop_empty_legacy_god2_tables_first_wave.sql"));

    private static string RefinedFormalSchemaTableInventoryAuditMigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "364_refine_formal_schema_table_inventory_cleanup_classification.sql"));

    [Fact]
    public void Migration_replaces_effects_and_accepts_only_exact_fixed_recovery_text()
    {
        var sql = MigrationSql();

        Assert.Contains("DELETE FROM `god2_game`.`item_effects`;", sql, StringComparison.Ordinal);
        Assert.Contains("'RestoreHp'", sql, StringComparison.Ordinal);
        Assert.Contains("'RestoreMp'", sql, StringComparison.Ordinal);
        Assert.Contains("^恢复生命", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("回复上限50000", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreHpAfterBattle", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_keeps_text_derived_restrictions_out_of_runtime_authority()
    {
        var sql = MigrationSql();

        Assert.Contains("`field_evidence_status` IN ('Verified','Recovered')", sql, StringComparison.Ordinal);
        Assert.Contains("`text_rule_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("使用旗標證據", sql, StringComparison.Ordinal);
        Assert.Contains("文字限制證據", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_publishes_traditional_chinese_readable_effect_views()
    {
        var sql = MigrationSql();

        Assert.Contains("`vw_item_effects_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `效果類型`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `效果數值`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `已實裝效果`", sql, StringComparison.Ordinal);
        Assert.Contains("'恢復生命'", sql, StringComparison.Ordinal);
        Assert.Contains("'恢復法力'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_and_repository_define_atomic_idempotent_item_use()
    {
        var migration = MigrationSql();
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbItemUseRepository.cs"));

        Assert.Contains("`item_use_idempotency`", migration, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.RepeatableRead", repository, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", repository, StringComparison.Ordinal);
        Assert.Contains("SET `quantity`=`quantity`-1", repository, StringComparison.Ordinal);
        Assert.Contains("SET `current_hp`=@currentHp,`current_mp`=@currentMp", repository, StringComparison.Ordinal);
        Assert.Contains("AdvanceInventoryVersionAsync", repository, StringComparison.Ordinal);
        Assert.Contains("InsertReplayAsync", repository, StringComparison.Ordinal);
        Assert.Contains("InsertAuditAsync", repository, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_maps_formal_traditional_item_and_effect_labels_to_runtime_enums()
    {
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));

        Assert.Contains("WHEN '消耗品' THEN 'Consumable'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '生命恢復' THEN 'RestoreHp'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '法力恢復' THEN 'RestoreMp'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '世界與戰鬥' THEN 'Both'", repository, StringComparison.Ordinal);
        Assert.Contains("\"UseItem\" => \"使用物品\"", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("娑堣", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("鎭", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("涓栫晫", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_recovery_activation_enables_only_exact_consumable_hp_mp_effects()
    {
        var sql = FixedRecoveryActivationMigrationSql();

        Assert.Contains("item_row.`item_category`='消耗品'", sql, StringComparison.Ordinal);
        Assert.Contains("effect_row.`effect_type` IN ('生命恢復','法力恢復')", sql, StringComparison.Ordinal);
        Assert.Contains("effect_row.`effect_text_zh_tw` REGEXP '^恢復(生命|法力) [0-9]+$'", sql, StringComparison.Ordinal);
        Assert.Contains("effect_row.`runtime_eligible`=1", sql, StringComparison.Ordinal);
        Assert.Contains("rule_row.`runtime_eligible`=1", sql, StringComparison.Ordinal);
        Assert.Contains("vw_fixed_recovery_consumables_runtime_readable", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("恢復生命與魔力", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("百分", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Percent_recovery_activation_publishes_only_use_recovery_percent_effects()
    {
        var sql = PercentRecoveryActivationMigrationSql();

        Assert.Contains("生命百分比恢復", sql, StringComparison.Ordinal);
        Assert.Contains("法力百分比恢復", sql, StringComparison.Ordinal);
        Assert.Contains("RestoreHpPercent", sql, StringComparison.Ordinal);
        Assert.Contains("RestoreMpPercent", sql, StringComparison.Ordinal);
        Assert.Contains("文字證據：百分比使用恢復", sql, StringComparison.Ordinal);
        Assert.Contains("vw_percent_recovery_consumables_runtime_readable", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("解除石化", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("每回合", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_maps_traditional_cleanse_item_effects_and_transaction_removes_battle_statuses_atomically()
    {
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));
        var itemUseRepository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbItemUseRepository.cs"));

        Assert.Contains("WHEN '解除中毒' THEN 'CleansePoison'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '解除睡眠' THEN 'CleanseSleep'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '解除神仙封' THEN 'CleanseSeal'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '解除石化' THEN 'CleansePetrify'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '解除混亂' THEN 'CleanseConfusion'", repository, StringComparison.Ordinal);
        Assert.Contains("LoadActiveBattleStatusCodesAsync", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("RemoveBattleStatusEffectsAsync", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("`god2`.`battle_status_removals`", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("ItemCleanse", itemUseRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("item.use.status_bridge_required", itemUseRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_maps_traditional_timed_stat_buff_item_effects_and_transaction_writes_character_effects_atomically()
    {
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbGameplayInventoryRepository.cs"));
        var itemUseRepository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbItemUseRepository.cs"));

        Assert.Contains("WHEN '提升腕力' THEN 'ApplyStrengthBuff'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '提升體力' THEN 'ApplyConstitutionBuff'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '提升智力' THEN 'ApplyIntelligenceBuff'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '提升速度' THEN 'ApplySpeedBuff'", repository, StringComparison.Ordinal);
        Assert.Contains("WHEN '戰鬥經驗加成' THEN 'ApplyBattleExperienceBuff'", repository, StringComparison.Ordinal);
        Assert.Contains("`duration_seconds` AS `DurationSeconds`", repository, StringComparison.Ordinal);
        Assert.Contains("InsertTimedBuffEffectsAsync", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("InsertExperienceBuffEffectsAsync", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("InsertElementBuffEffectsAsync", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("`god2_player`.`character_item_timed_effects`", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("`god2_player`.`character_item_experience_bonuses`", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("`god2_player`.`character_item_element_bonuses`", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("\"constitution\" => (\"提升體力\", \"體力\")", itemUseRepository, StringComparison.Ordinal);
        Assert.Contains("'Active',1", itemUseRepository, StringComparison.Ordinal);
        Assert.DoesNotContain("item.use.timed_buff_bridge_required", itemUseRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanse_candidate_migration_registers_readable_candidates_but_keeps_runtime_disabled()
    {
        var sql = CleanseCandidateMigrationSql();

        Assert.Contains("解除中毒", sql, StringComparison.Ordinal);
        Assert.Contains("解除睡眠", sql, StringComparison.Ordinal);
        Assert.Contains("解除神仙封", sql, StringComparison.Ordinal);
        Assert.Contains("解除石化", sql, StringComparison.Ordinal);
        Assert.Contains("解除混亂", sql, StringComparison.Ordinal);
        Assert.Contains("vw_cleanse_consumable_candidates_readable", sql, StringComparison.Ordinal);
        Assert.Contains("0 AS `runtime_eligible`", sql, StringComparison.Ordinal);
        Assert.Contains("0 AS `enabled`", sql, StringComparison.Ordinal);
        Assert.Contains("等待道具使用戰鬥狀態橋接", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Timed_stat_buff_candidate_migration_registers_readable_candidates_but_keeps_runtime_disabled()
    {
        var sql = TimedStatBuffCandidateMigrationSql();

        Assert.Contains("提升腕力", sql, StringComparison.Ordinal);
        Assert.Contains("提升智力", sql, StringComparison.Ordinal);
        Assert.Contains("提升速度", sql, StringComparison.Ordinal);
        Assert.Contains("vw_timed_stat_buff_consumable_candidates_readable", sql, StringComparison.Ordinal);
        Assert.Contains("0 AS `runtime_eligible`", sql, StringComparison.Ordinal);
        Assert.Contains("0 AS `enabled`", sql, StringComparison.Ordinal);
        Assert.Contains("等待道具使用限時狀態橋接", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Timed_stat_buff_duration_migration_materializes_duration_seconds_for_candidates()
    {
        var sql = TimedStatBuffDurationMigrationSql();

        Assert.Contains("`duration_seconds`", sql, StringComparison.Ordinal);
        Assert.Contains("持續時間秒數", sql, StringComparison.Ordinal);
        Assert.Contains("持續60分鐘", sql, StringComparison.Ordinal);
        Assert.Contains("3600", sql, StringComparison.Ordinal);
        Assert.Contains("持續90分鐘", sql, StringComparison.Ordinal);
        Assert.Contains("5400", sql, StringComparison.Ordinal);
        Assert.Contains("持續120分鐘", sql, StringComparison.Ordinal);
        Assert.Contains("7200", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Character_item_timed_effects_migration_creates_formal_character_level_buff_landing_table()
    {
        var sql = CharacterItemTimedEffectsMigrationSql();

        Assert.Contains("`god2_player`.`character_item_timed_effects`", sql, StringComparison.Ordinal);
        Assert.Contains("角色道具限時效果", sql, StringComparison.Ordinal);
        Assert.Contains("`source_item_id`", sql, StringComparison.Ordinal);
        Assert.Contains("`bonus_value`", sql, StringComparison.Ordinal);
        Assert.Contains("`duration_seconds`", sql, StringComparison.Ordinal);
        Assert.Contains("`expires_at_utc`", sql, StringComparison.Ordinal);
        Assert.Contains("PendingBridge", sql, StringComparison.Ordinal);
        Assert.Contains("vw_character_item_timed_effects_readable", sql, StringComparison.Ordinal);
        Assert.Contains("vw_character_item_timed_effects_active_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Timed_stat_buff_activation_enables_only_explicit_duration_buff_consumables()
    {
        var sql = TimedStatBuffActivationMigrationSql();

        Assert.Contains("提升腕力", sql, StringComparison.Ordinal);
        Assert.Contains("提升智力", sql, StringComparison.Ordinal);
        Assert.Contains("提升速度", sql, StringComparison.Ordinal);
        Assert.Contains("`duration_seconds` IN (3600,5400,7200)", sql, StringComparison.Ordinal);
        Assert.Contains("提升[0-9]+點(腕力|智力|速度)，持續(60|90|120)分鐘", sql, StringComparison.Ordinal);
        Assert.Contains("`runtime_eligible`=1", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=1", sql, StringComparison.Ordinal);
        Assert.Contains("vw_timed_stat_buff_consumables_runtime_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanse_activation_defines_statuses_and_enables_only_explicit_battle_cleanse_consumables()
    {
        var sql = CleanseActivationMigrationSql();

        Assert.Contains("`god2`.`status_effects`", sql, StringComparison.Ordinal);
        Assert.Contains("'poison','中毒'", sql, StringComparison.Ordinal);
        Assert.Contains("'sleep','睡眠'", sql, StringComparison.Ordinal);
        Assert.Contains("'seal','神仙封'", sql, StringComparison.Ordinal);
        Assert.Contains("'petrify','石化'", sql, StringComparison.Ordinal);
        Assert.Contains("'confusion','混亂'", sql, StringComparison.Ordinal);
        Assert.Contains("解除(石化|混亂|中毒|神仙封|封印|睡眠|狀態|異常|全部)", sql, StringComparison.Ordinal);
        Assert.Contains("`runtime_eligible`=1", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=1", sql, StringComparison.Ordinal);
        Assert.Contains("vw_cleanse_consumables_runtime_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Constitution_timed_stat_buff_activation_enables_only_explicit_duration_buff_consumables()
    {
        var sql = ConstitutionTimedStatBuffActivationMigrationSql();

        Assert.Contains("提升體力", sql, StringComparison.Ordinal);
        Assert.Contains("ApplyConstitutionBuff", sql, StringComparison.Ordinal);
        Assert.Contains("'腕力','體力','智力','速度'", sql, StringComparison.Ordinal);
        Assert.Contains("3005,3006,3007,3008,3018", sql, StringComparison.Ordinal);
        Assert.Contains("提升(60|80|100|120)點體力，持續60分鐘", sql, StringComparison.Ordinal);
        Assert.Contains("`duration_seconds`=VALUES(`duration_seconds`)", sql, StringComparison.Ordinal);
        Assert.Contains("3600", sql, StringComparison.Ordinal);
        Assert.Contains("vw_constitution_timed_stat_buff_consumables_runtime_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Battle_experience_bonus_activation_enables_only_explicit_duration_percent_consumables()
    {
        var sql = BattleExperienceBonusActivationMigrationSql();
        var specialSql = SpecialCategoryBattleExperienceBonusActivationMigrationSql();

        Assert.Contains("戰鬥經驗加成", sql, StringComparison.Ordinal);
        Assert.Contains("ApplyBattleExperienceBuff", sql, StringComparison.Ordinal);
        Assert.Contains("character_item_experience_bonuses", sql, StringComparison.Ordinal);
        Assert.Contains("5561,5570", sql, StringComparison.Ordinal);
        Assert.Contains("5661,5662,5663,5664,5665,5666,5667,5668,5669,5670", sql, StringComparison.Ordinal);
        Assert.Contains("6227,6228,6229,6230,6231,6232,6233,6234,6235,6236", sql, StringComparison.Ordinal);
        Assert.Contains("6247,6248,6249,6250,6251", sql, StringComparison.Ordinal);
        Assert.Contains("6399", sql, StringComparison.Ordinal);
        Assert.Contains("`bonus_percent` IN (20,30,40,50,60,70)", sql, StringComparison.Ordinal);
        Assert.Contains("`duration_seconds` IN (3600,7200,14400)", sql, StringComparison.Ordinal);
        Assert.Contains("vw_battle_experience_bonus_consumables_runtime_readable", sql, StringComparison.Ordinal);
        Assert.Contains("item_row.`item_category`='一般'", specialSql, StringComparison.Ordinal);
        Assert.Contains("item_row.`item_family`='特殊'", specialSql, StringComparison.Ordinal);
        Assert.Contains("戰鬥經驗加成道具", specialSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_attribute_bonus_activation_enables_only_explicit_three_hour_single_element_consumables()
    {
        var sql = ElementAttributeBonusActivationMigrationSql();

        Assert.Contains("提升金屬性", sql, StringComparison.Ordinal);
        Assert.Contains("提升木屬性", sql, StringComparison.Ordinal);
        Assert.Contains("提升水屬性", sql, StringComparison.Ordinal);
        Assert.Contains("提升火屬性", sql, StringComparison.Ordinal);
        Assert.Contains("提升土屬性", sql, StringComparison.Ordinal);
        Assert.Contains("ApplyMetalElementBuff", sql, StringComparison.Ordinal);
        Assert.Contains("character_item_element_bonuses", sql, StringComparison.Ordinal);
        Assert.Contains("BETWEEN 6368 AND 6387", sql, StringComparison.Ordinal);
        Assert.Contains("`bonus_value` IN (300,400,500,600)", sql, StringComparison.Ordinal);
        Assert.Contains("`duration_seconds`=10800", sql, StringComparison.Ordinal);
        Assert.Contains("後使用的會覆蓋前使用的效果", sql, StringComparison.Ordinal);
        Assert.Contains("vw_element_attribute_bonus_consumables_runtime_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Identification_magnifier_activation_enables_only_explicit_hp_mp_percent_use_effects()
    {
        var sql = IdentificationMagnifierPercentRecoveryActivationMigrationSql();

        Assert.Contains("client_item_id`=18048", sql, StringComparison.Ordinal);
        Assert.Contains("使用可增加生命50％魔力10％", sql, StringComparison.Ordinal);
        Assert.Contains("'生命百分比恢復' AS `effect_type`, 50", sql, StringComparison.Ordinal);
        Assert.Contains("'法力百分比恢復' AS `effect_type`, 10", sql, StringComparison.Ordinal);
        Assert.Contains("世界與戰鬥", sql, StringComparison.Ordinal);
        Assert.Contains("vw_identification_magnifier_percent_recovery_runtime_readable", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_text_quality_audit_view_covers_player_visible_and_server_written_fields()
    {
        var sql = FormalTextQualityAuditMigrationSql();

        Assert.Contains("vw_formal_text_quality_mojibake_audit_readable", sql, StringComparison.Ordinal);
        Assert.Contains("god2_game.item_registry", sql, StringComparison.Ordinal);
        Assert.Contains("description_zh_tw", sql, StringComparison.Ordinal);
        Assert.Contains("god2_game.item_effects", sql, StringComparison.Ordinal);
        Assert.Contains("effect_text_zh_tw", sql, StringComparison.Ordinal);
        Assert.Contains("god2.status_effects", sql, StringComparison.Ordinal);
        Assert.Contains("god2_player.inventory_audit_ledger", sql, StringComparison.Ordinal);
        Assert.Contains("需要清理", sql, StringComparison.Ordinal);
        Assert.Contains("通過", sql, StringComparison.Ordinal);
        Assert.Contains("鎻愬", sql, StringComparison.Ordinal);
        Assert.Contains("锛", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_schema_table_inventory_audit_classifies_runtime_content_and_cleanup_candidates()
    {
        var sql = FormalSchemaTableInventoryAuditMigrationSql();

        Assert.Contains("vw_formal_schema_table_inventory_audit_readable", sql, StringComparison.Ordinal);
        Assert.Contains("正式玩家資料", sql, StringComparison.Ordinal);
        Assert.Contains("正式靜態內容", sql, StringComparison.Ordinal);
        Assert.Contains("正式戰鬥Runtime", sql, StringComparison.Ordinal);
        Assert.Contains("舊資料鏡像或證據來源", sql, StringComparison.Ordinal);
        Assert.Contains("候選清理", sql, StringComparison.Ordinal);
        Assert.Contains("候選整併", sql, StringComparison.Ordinal);
        Assert.Contains("與 god2_game 正式內容高度重疊", sql, StringComparison.Ordinal);
        Assert.Contains("status_effects", sql, StringComparison.Ordinal);
        Assert.Contains("__schemaversion", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void First_wave_legacy_god2_table_cleanup_drops_only_guarded_empty_tables()
    {
        var sql = FirstWaveLegacyGod2TableCleanupMigrationSql();

        Assert.Contains("Legacy god2 table cleanup aborted", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*) FROM `god2`.`drop_table_items`", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*) FROM `god2`.`merchant_items`", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(*) FROM `god2`.`inventory_transaction_idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`drop_table_items`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`merchant_items`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`inventory_transaction_idempotency`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Refined_formal_schema_table_inventory_audit_separates_empty_referenced_tables_from_drop_candidates()
    {
        var sql = RefinedFormalSchemaTableInventoryAuditMigrationSql();

        Assert.Contains("vw_formal_schema_table_inventory_audit_readable", sql, StringComparison.Ordinal);
        Assert.Contains("延後清理：空表但仍有正式/稽核視圖引用", sql, StringComparison.Ordinal);
        Assert.Contains("延後清理：空表但仍有程式或工具引用", sql, StringComparison.Ordinal);
        Assert.Contains("先改寫或移除引用舊表的可讀/稽核視圖，再進行刪表", sql, StringComparison.Ordinal);
        Assert.Contains("先改寫舊工具/回收流程引用到 god2_game 正式表，再進行刪表", sql, StringComparison.Ordinal);
        Assert.Contains("content_runtime_releases", sql, StringComparison.Ordinal);
        Assert.Contains("spawns", sql, StringComparison.Ordinal);
        Assert.Contains("inventory_slots", sql, StringComparison.Ordinal);
        Assert.Contains("player_currency_balances", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Activation_migration_enables_only_complete_recovered_items_and_replaces_effects()
    {
        var sql = ActivationMigrationSql();

        Assert.Contains("`evidence_status` IN ('Verified','Recovered')", sql, StringComparison.Ordinal);
        Assert.Contains("`client_item_id` IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`name_zh_tw`<>''", sql, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM `god2_game`.`item_effects`;", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_item_catalog_health`", sql, StringComparison.Ordinal);
        Assert.Contains("未知堆疊上限，服務端暫按單件處理", sql, StringComparison.Ordinal);
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
