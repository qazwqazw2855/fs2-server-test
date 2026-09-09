using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed record CanonicalCatalogRow(IReadOnlyDictionary<string, object?> Fields);

public abstract class MariaDbNamedCatalogRepository(DatabaseOptions options) : MariaDbRuntimeRepository(options)
{
    protected async Task<IReadOnlyList<CanonicalCatalogRow>> QueryAsync(
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = commandText;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        var rows = new List<CanonicalCatalogRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fields = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                fields.Add(reader.GetName(index), reader.IsDBNull(index) ? null : reader.GetValue(index));
            }
            rows.Add(new CanonicalCatalogRow(fields));
        }
        return rows;
    }
}

public sealed class MariaDbItemCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `item_id`,`code`,`name_zh_tw`,`item_category`,`maximum_stack`,`weight`,`buy_price`,`sell_price`,`updated_at_utc` FROM `god2_game`.`vw_all_item_definitions` WHERE `enabled`=1 ORDER BY `item_id`;", token);
}

public sealed class MariaDbEquipmentCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        """
        SELECT `item_id`,`equipment_type`,
               CASE `equipment_slot`
                   WHEN '防具' THEN 'Armor'
                   WHEN '飾品' THEN 'Accessory'
                   WHEN '頭盔' THEN 'Helmet'
                   WHEN '手套' THEN 'Gloves'
                   WHEN '鞋子' THEN 'Shoes'
                   ELSE `equipment_slot`
               END AS `equipment_slot`,
               `required_strength`,`required_intelligence`,`physical_attack_bonus`,`physical_defense_bonus`,`magic_attack_bonus`,`magic_defense_bonus`
        FROM `god2_game`.`equipment`
        WHERE `enabled`=1
        ORDER BY `item_id`;
        """, token);
}

public sealed class MariaDbWeaponCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`weapon_type_zh_tw`,`attack_range`,`physical_attack_bonus`,`magic_attack_bonus`,`base_durability`,`maximum_enhancement`,`socket_count`,`class_restriction_zh_tw` FROM `god2_game`.`weapons` WHERE `enabled`=1 ORDER BY `item_id`;", token);
}

public sealed class MariaDbMagicTreasureCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `item_id`,`client_item_id`,`code`,`name_zh_tw`,`treasure_type_zh_tw`,`battle_usable`,`effect_description_zh_tw`,`base_durability`,`maximum_enhancement`,`socket_count`,`class_restriction_zh_tw` FROM `god2_game`.`magic_treasures` WHERE `enabled`=1 ORDER BY `item_id`;", token);
}

public sealed class MariaDbEquipmentInstanceRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadByCharacterAsync(long characterId, CancellationToken token) => QueryAsync(
        "SELECT `inventory_id`,`character_id`,`item_id`,`catalog_type`,`enhancement_level`,`refinement_level`,`current_durability`,`maximum_durability`,`socket_count`,`metal_bonus`,`wood_bonus`,`water_bonus`,`fire_bonus`,`earth_bonus`,`magic_treasure_experience`,`bound` FROM `god2_player`.`equipment_instances` WHERE `character_id`=@characterId AND `enabled`=1 ORDER BY `inventory_id`;",
        token,
        ("@characterId", characterId));
}

public sealed class MariaDbMonsterCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `monster_id`,`code`,`name_zh_tw`,`level`,`max_hp`,`max_mp`,`strength`,`constitution`,`intelligence`,`speed`,`metal`,`wood`,`water`,`fire`,`earth`,`physical_attack`,`physical_defense`,`magic_attack`,`magic_defense`,`updated_at_utc` FROM `god2_game`.`monsters` WHERE `enabled`=1 ORDER BY `monster_id`;", token);
}

public sealed class MariaDbMonsterSkillRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `monster_skill_id`,`monster_id`,`skill_id`,`trigger_type`,`trigger_value`,`use_probability`,`cooldown_rounds`,`priority` FROM `god2_game`.`monster_skills` WHERE `enabled`=1 ORDER BY `monster_skill_id`;", token);
}

public sealed class MariaDbMonsterSpawnRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `spawn_id`,`monster_id`,`map_id`,`position_x`,`position_y`,`respawn_seconds_min`,`respawn_seconds_max`,`spawn_count` FROM `god2_game`.`monster_spawns` WHERE `enabled`=1 ORDER BY `spawn_id`;", token);
}

public sealed class MariaDbMonsterDropRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `drop_id`,`monster_id`,`item_id`,`minimum_quantity`,`maximum_quantity`,`drop_rate`,`is_guaranteed` FROM `god2_game`.`monster_drops` WHERE `enabled`=1 ORDER BY `drop_id`;", token);
}

public sealed class MariaDbMonsterAiRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT rule_row.`ai_rule_id`,rule_row.`ai_profile_id`,rule_row.`priority`,rule_row.`minimum_round`,rule_row.`maximum_round`,rule_row.`self_hp_percent_min`,rule_row.`self_hp_percent_max`,rule_row.`required_status_effect_id`,rule_row.`skill_id`,rule_row.`target_selector`,rule_row.`use_probability`,rule_row.`fallback_action` FROM `god2_game`.`monster_ai_rules` rule_row JOIN `god2_game`.`monster_ai_profiles` profile_row ON profile_row.`ai_profile_id`=rule_row.`ai_profile_id` WHERE rule_row.`enabled`=1 AND profile_row.`enabled`=1 ORDER BY rule_row.`ai_profile_id`,rule_row.`priority`;", token);

    public async Task<IReadOnlyDictionary<int, MonsterAiDefinition>> LoadDefinitionsByMonsterAsync(CancellationToken token)
    {
        var rows = await QueryAsync(
            """
            SELECT monster_row.`monster_id`, profile_row.`ai_profile_id`, profile_row.`admin_note` AS `profile_note`,
                   rule_row.`priority`, rule_row.`skill_id`, rule_row.`target_selector`,
                   rule_row.`cooldown_rounds`, rule_row.`fallback_action`, rule_row.`admin_note` AS `rule_note`
            FROM `god2_game`.`monsters` monster_row
            JOIN `god2_game`.`monster_ai_profiles` profile_row
              ON profile_row.`ai_profile_id`=monster_row.`ai_profile_id` AND profile_row.`enabled`=1
            JOIN `god2_game`.`monster_ai_rules` rule_row
              ON rule_row.`ai_profile_id`=profile_row.`ai_profile_id` AND rule_row.`enabled`=1
            WHERE monster_row.`enabled`=1
            ORDER BY monster_row.`monster_id`, rule_row.`priority`;
            """,
            token);
        return BuildDefinitionsByMonster(rows);
    }

    internal static IReadOnlyDictionary<int, MonsterAiDefinition> BuildDefinitionsByMonster(IReadOnlyList<CanonicalCatalogRow> rows)
    {
        var definitions = new Dictionary<int, MonsterAiDefinition>();
        foreach (var group in rows.GroupBy(row => Int32(row, "monster_id")).OrderBy(group => group.Key))
        {
            var skills = group
                .Where(row => Int32OrNull(row, "skill_id") is not null)
                .Select(row => new MonsterAiSkill(
                    Int32(row, "skill_id"),
                    Action(row),
                    0,
                    Math.Max(0, Int32OrNull(row, "cooldown_rounds") ?? 0),
                    Math.Max(0, Int32OrNull(row, "priority") ?? 0)))
                .OrderByDescending(skill => skill.Priority)
                .ToArray();
            var confidence = group.Any(row => Text(row, "profile_note").Contains("ServiceDesigned", StringComparison.OrdinalIgnoreCase) ||
                                              Text(row, "rule_note").Contains("ServiceDesigned", StringComparison.OrdinalIgnoreCase))
                ? "ServiceDesignedReplaceablePolicy"
                : "EvidenceBackedPolicy";
            definitions[group.Key] = new MonsterAiDefinition(
                group.Key,
                TargetPolicy(group.First()),
                skills,
                2_000,
                500,
                1_000,
                null,
                confidence);
        }

        return definitions;
    }

    private static MonsterAiActionType Action(CanonicalCatalogRow row) =>
        Text(row, "fallback_action") switch
        {
            "HealAlly" => MonsterAiActionType.HealAlly,
            "BuffAlly" => MonsterAiActionType.BuffAlly,
            "DebuffEnemy" => MonsterAiActionType.DebuffEnemy,
            "Flee" => MonsterAiActionType.Flee,
            "Defend" => MonsterAiActionType.Defend,
            _ => MonsterAiActionType.UseSkill
        };

    private static MonsterTargetPolicy TargetPolicy(CanonicalCatalogRow row) =>
        Text(row, "target_selector").Contains("HighestThreat", StringComparison.OrdinalIgnoreCase)
            ? MonsterTargetPolicy.HighestThreat
            : MonsterTargetPolicy.LowestHp;

    private static int Int32(CanonicalCatalogRow row, string name) =>
        Convert.ToInt32(row.Fields[name], System.Globalization.CultureInfo.InvariantCulture);

    private static int? Int32OrNull(CanonicalCatalogRow row, string name) =>
        row.Fields.TryGetValue(name, out var value) && value is not null
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static string Text(CanonicalCatalogRow row, string name) =>
        row.Fields.TryGetValue(name, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
}

public sealed class MariaDbNpcCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `npc_id`,`code`,`name_zh_tw`,`npc_type`,`default_dialog_id`,`merchant_id` FROM `god2_game`.`npcs` WHERE `enabled`=1 ORDER BY `npc_id`;", token);
}

public sealed class MariaDbDialogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `dialog_id`,`code`,`npc_id`,`title_zh_tw`,`body_zh_tw`,`dialog_type`,`next_dialog_id` FROM `god2_game`.`npc_dialogs` WHERE `enabled`=1 ORDER BY `npc_id`,`dialog_id`;", token);
}

public sealed class MariaDbMerchantCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT inventory_row.`merchant_inventory_id`,inventory_row.`merchant_id`,inventory_row.`item_id`,inventory_row.`selling_price`,inventory_row.`purchasing_price`,inventory_row.`quantity_limit`,inventory_row.`pack_count` FROM `god2_game`.`merchant_inventory` inventory_row JOIN `god2_game`.`merchants` merchant_row ON merchant_row.`merchant_id`=inventory_row.`merchant_id` WHERE inventory_row.`enabled`=1 AND merchant_row.`enabled`=1 ORDER BY inventory_row.`merchant_id`,inventory_row.`display_order`;", token);
}

public sealed class MariaDbSkillCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `skill_id`,`code`,`name_zh_tw`,`skill_family`,`skill_category`,`target_type`,`target_scope_zh_tw`,`attack_range`,`element`,`mp_cost`,`cooldown_rounds`,`consumes_turn` FROM `god2_game`.`skills` WHERE `enabled`=1 ORDER BY `skill_id`;", token);
}

public sealed class MariaDbStatusEffectRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `status_effect_id`,`name_zh_tw`,`effect_type`,`element`,`duration_rounds`,`maximum_stacks`,`stack_mode`,`apply_phase`,`tick_phase`,`expire_phase` FROM `god2_game`.`status_effects` WHERE `enabled`=1 ORDER BY `status_effect_id`;", token);
}

public sealed class MariaDbQuestCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `quest_id`,`code`,`name_zh_tw`,`quest_type`,`start_npc_id`,`end_npc_id`,`required_level`,`maximum_level`,`repeatable` FROM `god2_game`.`quests` WHERE `enabled`=1 ORDER BY `quest_id`;", token);
}

public sealed class MariaDbMapCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `map_id`,`client_build_id`,`client_map_id`,`client_area_id`,`code`,`name_zh_tw`,`resource_identity`,`width`,`height`,`minimum_x`,`maximum_x`,`minimum_y`,`maximum_y` FROM `god2_game`.`maps` WHERE `enabled`=1 ORDER BY `map_id`;", token);
}

public sealed class MariaDbPortalCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `portal_id`,`source_map_id`,`source_x`,`source_y`,`destination_map_id`,`destination_x`,`destination_y`,`required_level` FROM `god2_game`.`portals` WHERE `enabled`=1 ORDER BY `portal_id`;", token);
}

public sealed class MariaDbPetCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `pet_template_id`,`code`,`name_zh_tw`,`pet_family`,`pet_category_id`,`wild_source_monster_id`,`element_zh_tw`,`base_level`,`base_max_hp`,`base_max_mp`,`base_max_lifespan`,`base_strength`,`base_constitution`,`base_intelligence`,`base_speed`,`base_metal`,`base_wood`,`base_water`,`base_fire`,`base_earth`,`maximum_skill_slots`,`level_20_skill_name_zh_tw`,`level_40_skill_name_zh_tw`,`level_60_skill_name_zh_tw`,`capture_rule_zh_tw` FROM `god2_game`.`pet_templates` WHERE `enabled`=1 ORDER BY `pet_template_id`;", token);
}

public sealed class MariaDbImmortalCatalogRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT template_row.`immortal_template_id`,template_row.`code`,template_row.`name_zh_tw`,template_row.`profession_code`,template_row.`is_special`,template_row.`initial_rank_id`,stats_row.`level` AS `initial_level`,stats_row.`maximum_hp` AS `base_max_hp`,stats_row.`maximum_mp` AS `base_max_mp`,stats_row.`strength` AS `base_strength`,stats_row.`constitution` AS `base_constitution`,stats_row.`intelligence` AS `base_intelligence`,stats_row.`speed` AS `base_speed`,stats_row.`metal` AS `base_metal`,stats_row.`wood` AS `base_wood`,stats_row.`water` AS `base_water`,stats_row.`fire` AS `base_fire`,stats_row.`earth` AS `base_earth` FROM `god2_game`.`immortal_templates` template_row JOIN `god2_game`.`immortal_base_stats` stats_row ON stats_row.`immortal_template_id`=template_row.`immortal_template_id` WHERE template_row.`enabled`=1 AND stats_row.`enabled`=1 ORDER BY template_row.`immortal_template_id`;", token);
}

public sealed class MariaDbFormationRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `formation_id`,`name_zh_tw`,`description_zh_tw` FROM `god2_game`.`formations` WHERE `enabled`=1 ORDER BY `formation_id`;", token);
}

public abstract class MariaDbOwnedCharacterRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    protected Task<IReadOnlyList<CanonicalCatalogRow>> QueryOwnerAsync(string sql, long characterId, CancellationToken token) =>
        QueryAsync(sql, token, ("@characterId", characterId));
}

public sealed class MariaDbCharacterPetRepository(DatabaseOptions options) : MariaDbOwnedCharacterRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadAsync(long characterId, CancellationToken token) => QueryOwnerAsync(
        "SELECT `pet_instance_id`,`owner_character_id`,`pet_template_id`,`name`,`initial_level`,`acquisition_origin`,`wild_source_monster_id`,`wild_encounter_level`,`level`,`experience`,`current_hp`,`max_hp`,`current_mp`,`max_mp`,`current_lifespan`,`max_lifespan`,`strength_base`,`strength_bonus`,`constitution_base`,`constitution_bonus`,`intelligence_base`,`intelligence_bonus`,`speed_base`,`speed_bonus`,`metal_base`,`metal_bonus`,`wood_base`,`wood_bonus`,`water_base`,`water_bonus`,`fire_base`,`fire_bonus`,`earth_base`,`earth_bonus`,`physical_attack_base`,`physical_attack_bonus`,`physical_defense_base`,`physical_defense_bonus`,`magic_attack_base`,`magic_attack_bonus`,`magic_defense_base`,`magic_defense_bonus`,`initial_growth_quality`,`growth_grade`,`growth_grade_status`,`growth_grade_confirmed_level`,`last_automatic_growth_total`,`rebirth_count`,`remaining_stat_points`,`fusion_expires_at_utc`,`is_deployed`,`is_active` FROM `god2_player`.`character_pets` WHERE `owner_character_id`=@characterId AND `enabled`=1 ORDER BY `pet_instance_id`;", characterId, token);
}

public sealed class MariaDbCharacterImmortalRepository(DatabaseOptions options) : MariaDbOwnedCharacterRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadAsync(long characterId, CancellationToken token) => QueryOwnerAsync(
        "SELECT `immortal_instance_id`,`owner_character_id`,`immortal_template_id`,`name`,`rank_id`,`rank_name_cache`,`level`,`experience`,`conversation_experience`,`current_hp`,`max_hp`,`current_mp`,`max_mp`,`strength_base`,`strength_bonus`,`constitution_base`,`constitution_bonus`,`intelligence_base`,`intelligence_bonus`,`speed_base`,`speed_bonus`,`metal_base`,`metal_bonus`,`wood_base`,`wood_bonus`,`water_base`,`water_bonus`,`fire_base`,`fire_bonus`,`earth_base`,`earth_bonus`,`physical_attack_base`,`physical_attack_bonus`,`physical_defense_base`,`physical_defense_bonus`,`magic_attack_base`,`magic_attack_bonus`,`magic_defense_base`,`magic_defense_bonus`,`is_active` FROM `god2_player`.`character_immortals` WHERE `owner_character_id`=@characterId AND `enabled`=1 ORDER BY `immortal_instance_id`;", characterId, token);
}

public sealed class MariaDbLifeSkillRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `life_skill_id`,`code`,`name_zh_tw`,`description_zh_tw`,`maximum_level` FROM `god2_game`.`life_skills` WHERE `enabled`=1 ORDER BY `life_skill_id`;", token);
}

public sealed class MariaDbCharacterLifeSkillRepository(DatabaseOptions options) : MariaDbOwnedCharacterRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadAsync(long characterId, CancellationToken token) => QueryOwnerAsync(
        "SELECT `character_id`,`life_skill_id`,`level`,`experience`,`proficiency`,`progress_value`,`is_unlocked`,`is_active`,`success_rate_bonus`,`quality_rate_bonus`,`critical_craft_rate_bonus`,`daily_use_count`,`daily_use_limit`,`last_used_at_utc` FROM `god2_player`.`character_life_skills` WHERE `character_id`=@characterId ORDER BY `life_skill_id`;", characterId, token);
}

public sealed class MariaDbCraftingRecipeRepository(DatabaseOptions options) : MariaDbNamedCatalogRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadEnabledAsync(CancellationToken token) => QueryAsync(
        "SELECT `recipe_id`,`name_zh_tw`,`recipe_category`,`life_skill_id`,`required_life_skill_level`,`required_character_level`,`base_success_rate`,`base_quality_rate`,`craft_duration_ms`,`currency_cost` FROM `god2_game`.`crafting_recipes` WHERE `enabled`=1 ORDER BY `recipe_id`;", token);
}

public sealed class MariaDbCharacterRecipeRepository(DatabaseOptions options) : MariaDbOwnedCharacterRepository(options)
{
    public Task<IReadOnlyList<CanonicalCatalogRow>> LoadAsync(long characterId, CancellationToken token) => QueryOwnerAsync(
        "SELECT `character_id`,`recipe_id`,`enabled` AS `is_unlocked`,`learned_at_utc`,`proficiency`,`craft_count`,`success_count`,`failure_count`,`highest_quality_tier` FROM `god2_player`.`character_learned_recipes` WHERE `character_id`=@characterId ORDER BY `recipe_id`;", characterId, token);
}
