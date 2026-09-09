#include "Storage.h"
#include "Version.h"

#include <algorithm>
#include <fstream>
#include <sstream>

namespace god2 {
namespace {

constexpr int kSchemaVersion = 11;

bool SafeIdentifier(std::string_view value) {
    if (value.empty()) return false;
    return std::all_of(value.begin(), value.end(), [](unsigned char c) {
        return std::isalnum(c) || c == '_';
    });
}

std::string LastError(sqlite3* database) {
    return database == nullptr ? "database is not open" : sqlite3_errmsg(database);
}

std::string JsonFromFields(const Fields& fields) {
    std::vector<std::pair<std::string, std::string>> ordered(fields.begin(), fields.end());
    std::sort(ordered.begin(), ordered.end(), [](const auto& left, const auto& right) {
        return left.first < right.first;
    });
    return MakeJsonObject(ordered);
}

const char* kSchemaSql = R"SQL(
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
PRAGMA temp_store=MEMORY;

CREATE TABLE IF NOT EXISTS schema_migrations(
  version INTEGER PRIMARY KEY,
  applied_at_utc TEXT NOT NULL,
  description TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS sessions(
  session_id TEXT PRIMARY KEY,
  created_at_utc TEXT NOT NULL,
  schema_version INTEGER NOT NULL,
  tool_version TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS analysis_runs(
  analysis_run_id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  started_at_utc TEXT NOT NULL,
  completed_at_utc TEXT,
  status TEXT NOT NULL,
  reason TEXT NOT NULL,
  analyzer_version TEXT NOT NULL,
  schema_version INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS raw_frames(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL,
  frame_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL,
  direction TEXT,
  opcode TEXT,
  message_type TEXT,
  payload_hex TEXT,
  source_json TEXT NOT NULL DEFAULT '',
  original_json TEXT NOT NULL,
  jsonl_written INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_raw_frames_session_frame ON raw_frames(session_id, frame_id);

CREATE TABLE IF NOT EXISTS jsonl_records(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  record_id TEXT NOT NULL UNIQUE,
  session_id TEXT NOT NULL,
  analysis_run_id TEXT,
  relative_path TEXT NOT NULL,
  json TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_jsonl_records_path ON jsonl_records(session_id, relative_path, id);

CREATE TABLE IF NOT EXISTS movement_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, entity_id TEXT, character_id TEXT, map_id TEXT,
  start_x TEXT, start_y TEXT, start_z TEXT, end_x TEXT, end_y TEXT, end_z TEXT,
  direction_raw TEXT, direction_normalized TEXT, facing_raw TEXT, facing_normalized TEXT,
  movement_mode TEXT NOT NULL, movement_sequence TEXT, client_timestamp TEXT, server_timestamp TEXT,
  server_correction TEXT NOT NULL DEFAULT 'false', path_point_count TEXT, direction_source TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_movement_session_run ON movement_observations(session_id, analysis_run_id);
CREATE TABLE IF NOT EXISTS movement_direction_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, movement_observation_id TEXT, direction_raw TEXT,
  direction_normalized TEXT NOT NULL, direction_source TEXT NOT NULL, movement_mode TEXT NOT NULL,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS mount_profiles(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, mount_runtime_id TEXT, mount_template_id TEXT, mount_variant_id TEXT,
  mount_name TEXT, mount_level TEXT, mount_skill_ids TEXT, mount_buff_ids TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS mount_state_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, character_id TEXT, mount_runtime_id TEXT,
  mount_template_id TEXT, mount_variant_id TEXT, mount_name TEXT, mount_level TEXT, mount_state TEXT,
  is_mounted TEXT NOT NULL, mount_start_utc TEXT, dismount_utc TEXT, map_id TEXT, x TEXT, y TEXT, z TEXT,
  direction TEXT, movement_sequence TEXT, mount_movement_speed TEXT, character_stat_speed TEXT,
  normal_movement_speed TEXT, mount_buff_ids TEXT, mount_skill_ids TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS mounted_movement_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, character_id TEXT, mount_runtime_id TEXT, map_id TEXT,
  start_x TEXT, start_y TEXT, start_z TEXT, end_x TEXT, end_y TEXT, end_z TEXT,
  direction_raw TEXT, direction_normalized TEXT, movement_mode TEXT NOT NULL DEFAULT 'Mounted', movement_sequence TEXT, mount_movement_speed TEXT,
  character_stat_speed TEXT, normal_movement_speed TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS quest_profiles(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, quest_id TEXT NOT NULL, quest_type TEXT NOT NULL, quest_name TEXT,
  definition_status TEXT NOT NULL, required_level TEXT, maximum_level TEXT, required_class TEXT,
  required_quest_ids TEXT, required_item_ids TEXT, required_party_state TEXT, required_reputation TEXT,
  required_map TEXT, repeat_interval TEXT, daily_reset_rule TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS quest_stages(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, quest_id TEXT NOT NULL, quest_stage_id TEXT NOT NULL, stage_state TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS quest_objectives(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, quest_id TEXT NOT NULL, quest_stage_id TEXT, objective_id TEXT,
  objective_type TEXT NOT NULL, target_template_id TEXT, target_entity_id TEXT, target_npc_id TEXT,
  target_monster_id TEXT, target_item_id TEXT, target_map_id TEXT, target_x TEXT, target_y TEXT, target_z TEXT,
  required_count TEXT, current_count TEXT, previous_count TEXT, completed TEXT, optional TEXT,
  shared_with_party TEXT, time_limit_seconds TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS quest_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, quest_id TEXT, quest_stage_id TEXT,
  quest_correlation_id TEXT, quest_action_correlation_id TEXT, state TEXT, failure_reason_code TEXT,
  related_entity_type TEXT, related_entity_id TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS quest_progress_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, quest_id TEXT, quest_stage_id TEXT, objective_id TEXT,
  previous_count TEXT, current_count TEXT, required_count TEXT, completed TEXT,
  quest_correlation_id TEXT, quest_action_correlation_id TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS quest_reward_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, quest_id TEXT, experience_reward TEXT, currency_rewards TEXT,
  item_rewards TEXT, selectable_rewards TEXT, skill_rewards TEXT, pet_rewards TEXT, immortal_rewards TEXT,
  reputation_rewards TEXT, title_rewards TEXT, unlock_rewards TEXT, definition_status TEXT NOT NULL,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS quest_correlations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, quest_id TEXT, quest_correlation_id TEXT NOT NULL,
  quest_action_correlation_id TEXT NOT NULL, related_event_type TEXT NOT NULL,
  related_entity_type TEXT, related_entity_id TEXT, relationship TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS skill_profiles(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, skill_id TEXT, skill_level TEXT, skill_name TEXT,
  skill_source_type TEXT NOT NULL, element TEXT, activation_type TEXT, targeting_type TEXT,
  effect_types TEXT, control_types TEXT, duration_type TEXT, combat_role TEXT, mp_cost TEXT, hp_cost TEXT,
  other_resource_cost TEXT, cast_time TEXT, cooldown TEXT, range_value TEXT, area_radius TEXT,
  maximum_targets TEXT, hit_count TEXT, effect_ids TEXT, animation_ids TEXT, status_effect_ids TEXT,
  unknown_skill TEXT NOT NULL, candidate_facets TEXT, candidate_confidence TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS skill_facets(
  id INTEGER PRIMARY KEY AUTOINCREMENT, facet_id TEXT NOT NULL, facet_dimension TEXT NOT NULL,
  display_name TEXT NOT NULL, schema_version INTEGER NOT NULL,
  UNIQUE(facet_id, schema_version)
);
CREATE TABLE IF NOT EXISTS skill_profile_facets(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, skill_id TEXT, facet_id TEXT NOT NULL, facet_dimension TEXT NOT NULL,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS skill_cast_correlations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, skill_cast_correlation_id TEXT NOT NULL, event_type TEXT NOT NULL,
  skill_id TEXT, caster_entity_id TEXT, target_entity_ids TEXT, parent_correlation_id TEXT,
  failure_reason_code TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS skill_target_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, skill_cast_correlation_id TEXT NOT NULL, skill_id TEXT,
  target_entity_id TEXT, target_index TEXT, hit_result TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS skill_effect_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, skill_cast_correlation_id TEXT NOT NULL, skill_id TEXT,
  target_entity_id TEXT, effect_type TEXT, effect_id TEXT, amount TEXT, tick_index TEXT,
  duration_ms TEXT, status_effect_id TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS skill_control_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, skill_cast_correlation_id TEXT NOT NULL, skill_id TEXT,
  target_entity_id TEXT, control_type TEXT, duration_ms TEXT, resisted TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS consumable_profiles(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, item_id TEXT, item_name TEXT, consumable_type TEXT NOT NULL,
  is_item_skill TEXT NOT NULL, shared_cooldown_group TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS consumable_use_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, item_use_correlation_id TEXT NOT NULL,
  character_id TEXT, item_id TEXT, quantity_before TEXT, quantity_after TEXT, hp_before TEXT, hp_after TEXT,
  mp_before TEXT, mp_after TEXT, buff_ids TEXT, quest_id TEXT, failure_reason_code TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS backend_capabilities(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, observed_at_utc TEXT NOT NULL,
  capture_backend_name TEXT NOT NULL, capture_backend_version TEXT, available TEXT NOT NULL,
  supports_realtime_events TEXT NOT NULL, supports_packet_payload TEXT NOT NULL,
  supports_direction TEXT NOT NULL, supports_endpoint_filter TEXT NOT NULL,
  supports_process_filter TEXT NOT NULL, supports_drop_statistics TEXT NOT NULL,
  supports_pcapng_conversion TEXT NOT NULL, supports_ipv4 TEXT NOT NULL, supports_ipv6 TEXT NOT NULL,
  analysis_mode TEXT NOT NULL, blocked_capabilities TEXT, probe_evidence TEXT
);
CREATE TABLE IF NOT EXISTS os_compatibility_results(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, observed_at_utc TEXT NOT NULL,
  windows_edition TEXT, service_pack TEXT, build_number TEXT, architecture TEXT,
  installed_capture_backend TEXT, analysis_mode TEXT, captured_packet_count TEXT,
  captured_bytes TEXT, pcapng_validation TEXT, process_exit_code TEXT, test_status TEXT NOT NULL
);
)SQL";

const char* kSchema6Sql = R"SQL(
CREATE TABLE IF NOT EXISTS entity_profiles(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, entity_type TEXT NOT NULL, entity_id TEXT, template_id TEXT,
  entity_name TEXT, level TEXT, element TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS entity_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, entity_type TEXT NOT NULL, entity_id TEXT,
  template_id TEXT, map_id TEXT, x TEXT, y TEXT, z TEXT, level TEXT, hp TEXT, max_hp TEXT, mp TEXT,
  max_mp TEXT, strength TEXT, stamina TEXT, intelligence TEXT, character_stat_speed TEXT, element TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS character_lifecycle_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, account_id TEXT, character_id TEXT,
  character_name TEXT, slot_id TEXT, class_id TEXT, level TEXT, result TEXT, failure_reason_code TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS party_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, party_correlation_id TEXT NOT NULL,
  party_id TEXT, leader_entity_id TEXT, member_count TEXT, state TEXT, failure_reason_code TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS party_member_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, party_correlation_id TEXT NOT NULL, party_id TEXT,
  member_entity_id TEXT, member_name TEXT, member_level TEXT, member_role TEXT, online TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS economy_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, economy_correlation_id TEXT NOT NULL,
  character_id TEXT, shop_id TEXT, item_id TEXT, quantity TEXT, unit_price TEXT, total_price TEXT,
  currency_type TEXT, currency_before TEXT, currency_after TEXT, durability_before TEXT,
  durability_after TEXT, result TEXT, failure_reason_code TEXT,
  source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS progression_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, entity_type TEXT NOT NULL, entity_id TEXT,
  previous_level TEXT, current_level TEXT, previous_experience TEXT, current_experience TEXT,
  required_experience TEXT, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS formula_candidates(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, formula_type TEXT NOT NULL, candidate_expression TEXT,
  input_values TEXT, observed_result TEXT, sample_count TEXT NOT NULL DEFAULT '1', confidence TEXT,
  verification_status TEXT NOT NULL, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS gameplay_coverage(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  feature_group TEXT NOT NULL, feature_id TEXT NOT NULL, coverage_status TEXT NOT NULL,
  observed_count INTEGER NOT NULL DEFAULT 0, last_source_frame_ids TEXT, evidence_level TEXT NOT NULL,
  UNIQUE(session_id, analysis_run_id, feature_id)
);
CREATE TABLE IF NOT EXISTS formula_coverage(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  formula_type TEXT NOT NULL, coverage_status TEXT NOT NULL, observed_count INTEGER NOT NULL DEFAULT 0,
  last_source_frame_ids TEXT, evidence_level TEXT NOT NULL,
  UNIQUE(session_id, analysis_run_id, formula_type)
);
CREATE TABLE IF NOT EXISTS unknown_opcode_clusters(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  cluster_key TEXT NOT NULL, opcode TEXT, payload_prefix TEXT, payload_length INTEGER NOT NULL,
  observed_count INTEGER NOT NULL DEFAULT 0, sample_source_frame_ids TEXT, evidence_level TEXT NOT NULL,
  UNIQUE(session_id, analysis_run_id, cluster_key)
);
)SQL";

const char* kSchema7Sql = R"SQL(
CREATE TABLE IF NOT EXISTS protocol_handler_observations(
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, analysis_run_id TEXT NOT NULL,
  observed_at_utc TEXT NOT NULL, event_type TEXT NOT NULL, direction TEXT, opcode TEXT,
  capture_stage TEXT NOT NULL, transport TEXT, declared_frame_length TEXT, handler_record_length TEXT,
  attacker_position_candidate TEXT, target_position_candidate TEXT, effect_kind_candidate TEXT,
  effect_flags_candidate TEXT, observed_signed_delta_candidate TEXT, outcome_flags_candidate TEXT,
  battle_position_candidate TEXT, action_code_candidate TEXT, continuation_candidate TEXT,
  side_candidate TEXT, target_mask_0_candidate TEXT, target_mask_1_candidate TEXT,
  target_mask_2_candidate TEXT, battle_context_candidate TEXT, action_parameter_candidate TEXT,
  entity_or_item_id_candidate TEXT, slot_candidate TEXT, state_candidate TEXT,
  movement_argument_candidate TEXT, reserved_byte_candidate TEXT,
  character_create_marker_offset TEXT, character_create_marker_value TEXT,
  checksum_valid TEXT, decode_validation TEXT,
  semantic_status TEXT NOT NULL, source_frame_ids TEXT NOT NULL, evidence_level TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_protocol_handler_session_run
  ON protocol_handler_observations(session_id, analysis_run_id, capture_stage, opcode);
)SQL";

} // namespace

Database::~Database() {
    if (database_ != nullptr) sqlite3_close(database_);
}

bool Database::Open(const fs::path& path, std::string* error) {
    if (database_ != nullptr) sqlite3_close(database_);
    database_ = nullptr;
    const auto utf8 = WideToUtf8(path.wstring());
    const int rc = sqlite3_open_v2(utf8.c_str(), &database_,
                                   SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nullptr);
    if (rc != SQLITE_OK) {
        if (error) *error = LastError(database_);
        return false;
    }
    sqlite3_busy_timeout(database_, 15'000);
    return true;
}

bool Database::Execute(std::string_view sql, std::string* error) {
    if (database_ == nullptr) { if (error) *error = "database is not open"; return false; }
    char* message = nullptr;
    const std::string statement(sql);
    const int rc = sqlite3_exec(database_, statement.c_str(), nullptr, nullptr, &message);
    if (rc != SQLITE_OK) {
        if (error) *error = message == nullptr ? LastError(database_) : message;
        sqlite3_free(message);
        return false;
    }
    return true;
}

bool Database::Migrate(std::string* error) {
    if (!Execute(kSchemaSql, error)) return false;
    if (!Execute(kSchema6Sql, error)) return false;
    if (!Execute(kSchema7Sql, error)) return false;
    sqlite3_stmt* columns = nullptr;
    bool has_jsonl_written = false;
    bool has_source_json = false;
    if (sqlite3_prepare_v2(database_, "PRAGMA table_info(raw_frames);", -1, &columns, nullptr) != SQLITE_OK) {
        if (error) *error = LastError(database_);
        return false;
    }
    while (sqlite3_step(columns) == SQLITE_ROW) {
        const auto* name = sqlite3_column_text(columns, 1);
        if (name != nullptr && std::string_view(reinterpret_cast<const char*>(name)) == "jsonl_written") {
            has_jsonl_written = true;
        }
        if (name != nullptr && std::string_view(reinterpret_cast<const char*>(name)) == "source_json") has_source_json = true;
    }
    sqlite3_finalize(columns);
    if (!has_jsonl_written &&
        !Execute("ALTER TABLE raw_frames ADD COLUMN jsonl_written INTEGER NOT NULL DEFAULT 0;", error)) return false;
    if (!has_source_json &&
        !Execute("ALTER TABLE raw_frames ADD COLUMN source_json TEXT NOT NULL DEFAULT '';", error)) return false;
    if (!Execute("UPDATE raw_frames SET source_json=original_json WHERE source_json='';", error)) return false;
    sqlite3_stmt* mounted_columns = nullptr;
    bool has_movement_mode = false;
    if (sqlite3_prepare_v2(database_, "PRAGMA table_info(mounted_movement_observations);", -1,
                           &mounted_columns, nullptr) != SQLITE_OK) {
        if (error) *error = LastError(database_);
        return false;
    }
    while (sqlite3_step(mounted_columns) == SQLITE_ROW) {
        const auto* name = sqlite3_column_text(mounted_columns, 1);
        if (name != nullptr && std::string_view(reinterpret_cast<const char*>(name)) == "movement_mode") {
            has_movement_mode = true;
            break;
        }
    }
    sqlite3_finalize(mounted_columns);
    if (!has_movement_mode &&
        !Execute("ALTER TABLE mounted_movement_observations ADD COLUMN movement_mode TEXT NOT NULL DEFAULT 'Mounted';", error)) return false;

    // Schema 11 retains every schema-9/10 column. Package-side evidence
    // efficiency and correlation fields are additive normalized artifacts,
    // so existing session databases remain forward-compatible.
    const std::pair<const char*, const char*> protocol_columns[] = {
        {"entity_or_item_id_candidate", "TEXT"},
        {"slot_candidate", "TEXT"},
        {"state_candidate", "TEXT"},
        {"movement_argument_candidate", "TEXT"},
        {"reserved_byte_candidate", "TEXT"},
        {"character_create_marker_offset", "TEXT"},
        {"character_create_marker_value", "TEXT"},
        {"checksum_valid", "TEXT"},
        {"decode_validation", "TEXT"}
    };
    for (const auto& [column_name, column_type] : protocol_columns) {
        sqlite3_stmt* protocol_info = nullptr;
        if (sqlite3_prepare_v2(database_, "PRAGMA table_info(protocol_handler_observations);", -1,
                               &protocol_info, nullptr) != SQLITE_OK) {
            if (error) *error = LastError(database_);
            return false;
        }
        bool found = false;
        while (sqlite3_step(protocol_info) == SQLITE_ROW) {
            const auto* name = sqlite3_column_text(protocol_info, 1);
            if (name != nullptr && std::string_view(reinterpret_cast<const char*>(name)) == column_name) {
                found = true;
                break;
            }
        }
        sqlite3_finalize(protocol_info);
        if (!found && !Execute("ALTER TABLE protocol_handler_observations ADD COLUMN " +
                               std::string(column_name) + " " + column_type + ";", error)) return false;
    }
    return Execute("INSERT OR IGNORE INTO schema_migrations(version,applied_at_utc,description) VALUES(" +
                   std::to_string(kSchemaVersion) + "," + SqlQuote(UtcNow()) +
                   ",'schema-11 additive evidence efficiency contract; schema-9/10 columns retained');", error);
}

bool Database::Begin(std::string* error) { return Execute("BEGIN IMMEDIATE;", error); }
bool Database::Commit(std::string* error) { return Execute("COMMIT;", error); }
bool Database::Rollback() { return Execute("ROLLBACK;", nullptr); }

bool Database::Insert(std::string_view table, const Fields& values, std::string* error) {
    if (!SafeIdentifier(table) || values.empty()) {
        if (error) *error = "unsafe table name or empty insert";
        return false;
    }
    std::vector<std::pair<std::string, std::string>> sorted(values.begin(), values.end());
    std::sort(sorted.begin(), sorted.end());
    std::ostringstream columns;
    std::ostringstream bindings;
    bool first = true;
    for (const auto& [key, value] : sorted) {
        if (!SafeIdentifier(key)) { if (error) *error = "unsafe column name: " + key; return false; }
        if (!first) { columns << ','; bindings << ','; }
        first = false;
        columns << key;
        bindings << SqlQuote(value);
    }
    return Execute("INSERT INTO " + std::string(table) + "(" + columns.str() + ") VALUES(" +
                   bindings.str() + ");", error);
}

bool Database::ExportCsv(std::string_view table, const fs::path& path, std::string* error) {
    if (!SafeIdentifier(table)) { if (error) *error = "unsafe export table"; return false; }
    sqlite3_stmt* statement = nullptr;
    const std::string sql = "SELECT * FROM " + std::string(table) + ";";
    if (sqlite3_prepare_v2(database_, sql.c_str(), -1, &statement, nullptr) != SQLITE_OK) {
        if (error) *error = LastError(database_);
        return false;
    }
    std::error_code ec;
    fs::create_directories(path.parent_path(), ec);
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    if (!output) {
        sqlite3_finalize(statement);
        if (error) *error = "cannot create CSV: " + WideToUtf8(path.wstring());
        return false;
    }
    const int count = sqlite3_column_count(statement);
    for (int i = 0; i < count; ++i) {
        if (i != 0) output << ',';
        output << CsvEscape(sqlite3_column_name(statement, i));
    }
    output << "\r\n";
    while (sqlite3_step(statement) == SQLITE_ROW) {
        for (int i = 0; i < count; ++i) {
            if (i != 0) output << ',';
            const unsigned char* value = sqlite3_column_text(statement, i);
            output << CsvEscape(value == nullptr ? "" : reinterpret_cast<const char*>(value));
        }
        output << "\r\n";
    }
    const int rc = sqlite3_finalize(statement);
    if (rc != SQLITE_OK || !output.good()) {
        if (error) *error = "CSV export failed for " + std::string(table);
        return false;
    }
    return true;
}

std::int64_t Database::ScalarInt64(std::string_view sql, std::int64_t fallback) const {
    sqlite3_stmt* statement = nullptr;
    const std::string query(sql);
    if (database_ == nullptr || sqlite3_prepare_v2(database_, query.c_str(), -1, &statement, nullptr) != SQLITE_OK) return fallback;
    std::int64_t value = fallback;
    if (sqlite3_step(statement) == SQLITE_ROW) value = sqlite3_column_int64(statement, 0);
    sqlite3_finalize(statement);
    return value;
}

int Database::SchemaVersion() const {
    return static_cast<int>(ScalarInt64("SELECT COALESCE(MAX(version),0) FROM schema_migrations;", 0));
}

SessionStore::SessionStore(fs::path session_path)
    : session_path_(std::move(session_path)) {
}

bool SessionStore::Initialize(std::string* error) {
    if (!IsApprovedSessionPath(session_path_)) {
        if (error) *error = "session path must remain below %LOCALAPPDATA%\\God2Classic\\PacketCapture";
        return false;
    }
    std::error_code ec;
    for (const auto& directory : {L"raw", L"gameplay", L"exports", L"compatibility"}) {
        fs::create_directories(session_path_ / directory, ec);
        if (ec) { if (error) *error = ec.message(); return false; }
    }
    const auto manifest_path = session_path_ / L"session.json";
    std::string created_at_utc = UtcNow();
    Fields manifest_fields;
    if (const auto existing = ReadUtf8File(manifest_path)) {
        if (!ParseFlatJson(*existing, manifest_fields, error)) return false;
        session_id_ = GetString(manifest_fields, "SessionId");
        created_at_utc = GetString(manifest_fields, "CreatedAtUtc", created_at_utc);
    }
    if (session_id_.empty()) session_id_ = NewId();
    if (!database_.Open(session_path_ / L"session.sqlite3", error) || !database_.Migrate(error)) return false;
    manifest_fields["SessionId"] = session_id_;
    manifest_fields["CreatedAtUtc"] = created_at_utc;
    manifest_fields["SchemaVersion"] = std::to_string(kSchemaVersion);
    manifest_fields["ToolVersion"] = GOD2_TOOL_VERSION;
    manifest_fields["RawEvidenceRetention"] = "Preserve";
    std::vector<std::pair<std::string, std::string>> manifest_values(manifest_fields.begin(), manifest_fields.end());
    std::sort(manifest_values.begin(), manifest_values.end(), [](const auto& left, const auto& right) {
        return left.first < right.first;
    });
    const auto manifest = MakeJsonObject(manifest_values, {"SchemaVersion"});
    const auto manifest_temporary = manifest_path.parent_path() / (L"session.json.tmp-" + Utf8ToWide(NewId()));
    if (!WriteUtf8File(manifest_temporary, manifest + "\n") ||
        !MoveFileExW(manifest_temporary.c_str(), manifest_path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        std::error_code remove_error;
        fs::remove(manifest_temporary, remove_error);
        if (error) *error = "cannot atomically write session manifest";
        return false;
    }
    if (!database_.Execute("INSERT OR IGNORE INTO sessions(session_id,created_at_utc,schema_version,tool_version) VALUES(" +
                           SqlQuote(session_id_) + "," + SqlQuote(UtcNow()) + "," +
                            std::to_string(kSchemaVersion) + ",'" GOD2_TOOL_VERSION "');", error)) return false;
    if (!database_.Execute("UPDATE sessions SET schema_version=" + std::to_string(kSchemaVersion) +
                           ",tool_version='" GOD2_TOOL_VERSION "' WHERE session_id=" + SqlQuote(session_id_) + ";", error)) return false;
    if (!RebuildRawFramesJsonl(error)) return false;
    if (!ImportLegacyJsonlRecords(error) || !RebuildGameplayJsonl(error)) return false;
    for (const auto& relative : RequiredGameplayFiles()) {
        const auto path = session_path_ / relative;
        if (!fs::exists(path) && !WriteUtf8File(path, "")) { if (error) *error = "cannot initialize " + WideToUtf8(path.wstring()); return false; }
    }
    return true;
}

bool SessionStore::BeginAnalysis(std::string_view reason, std::string* error) {
    analysis_run_id_ = NewId();
    return database_.Insert("analysis_runs", {
        {"analysis_run_id", analysis_run_id_}, {"session_id", session_id_}, {"started_at_utc", UtcNow()},
        {"status", "Running"}, {"reason", std::string(reason)}, {"analyzer_version", GOD2_TOOL_VERSION},
        {"schema_version", std::to_string(kSchemaVersion)}
    }, error);
}

bool SessionStore::FinishAnalysis(std::string_view status, std::string* error) {
    if (analysis_run_id_.empty()) return true;
    if (!database_.Execute("UPDATE analysis_runs SET completed_at_utc=" + SqlQuote(UtcNow()) +
                           ",status=" + SqlQuote(status) + " WHERE analysis_run_id=" +
                           SqlQuote(analysis_run_id_) + ";", error)) return false;
    return RebuildRawFramesJsonl(error) && RebuildGameplayJsonl(error);
}

bool SessionStore::PersistRawFrame(const Frame& frame, std::string* error) {
    std::lock_guard lock(write_mutex_);
    const auto lookup = "SELECT jsonl_written FROM raw_frames WHERE session_id=" + SqlQuote(session_id_) +
                        " AND frame_id=" + SqlQuote(frame.frame_id) + ";";
    const auto existing_status = database_.ScalarInt64(lookup, -1);
    if (existing_status == 1) return true;
    if (existing_status == 0) return RebuildRawFramesJsonl(error);

    const Fields values = {
        {"session_id", session_id_}, {"frame_id", frame.frame_id}, {"observed_at_utc", frame.timestamp_utc},
        {"direction", frame.direction}, {"opcode", frame.opcode}, {"message_type", frame.message_type},
        {"payload_hex", frame.payload_hex}, {"source_json", frame.source_json.empty() ? frame.original_json : frame.source_json},
        {"original_json", frame.original_json}, {"jsonl_written", "0"}
    };
    if (!database_.Insert("raw_frames", values, error)) return false;
    std::uintmax_t previous_size = 0;
    if (!AppendJsonlLine(L"raw/frames.jsonl", frame.original_json, &previous_size, error)) return false;
    if (!database_.Execute("UPDATE raw_frames SET jsonl_written=1 WHERE session_id=" + SqlQuote(session_id_) +
                           " AND frame_id=" + SqlQuote(frame.frame_id) + ";", error)) {
        std::error_code resize_error;
        fs::resize_file(session_path_ / L"raw/frames.jsonl", previous_size, resize_error);
        return false;
    }
    return true;
}

bool SessionStore::WriteObservation(std::string_view table,
                                    const fs::path& jsonl_relative_path,
                                    const Fields& values,
                                    std::string* error) {
    Fields augmented = values;
    augmented["session_id"] = session_id_;
    augmented["analysis_run_id"] = analysis_run_id_;
    if (!augmented.contains("observed_at_utc")) augmented["observed_at_utc"] = UtcNow();
    const auto json = JsonFromFields(augmented);
    const auto relative_utf8 = WideToUtf8(jsonl_relative_path.generic_wstring());
    std::lock_guard lock(write_mutex_);
    if (!database_.Begin(error)) return false;
    if (!database_.Insert(table, augmented, error)) {
        database_.Rollback();
        return false;
    }
    if (!database_.Insert("jsonl_records", {
            {"record_id", NewId()}, {"session_id", session_id_}, {"analysis_run_id", analysis_run_id_},
            {"relative_path", relative_utf8}, {"json", json}
        }, error)) {
        database_.Rollback();
        return false;
    }
    if (!database_.Commit(error)) {
        database_.Rollback();
        return false;
    }
    std::uintmax_t previous_size = 0;
    if (!AppendJsonlLine(jsonl_relative_path, json, &previous_size, error)) {
        return RebuildJsonlFromLedger(jsonl_relative_path, error);
    }
    return true;
}

bool SessionStore::WriteJsonl(const fs::path& relative_path, std::string_view json, std::string* error) {
    std::uintmax_t ignored = 0;
    return AppendJsonlLine(relative_path, json, &ignored, error);
}

bool SessionStore::AppendJsonlLine(const fs::path& relative_path,
                                   std::string_view json,
                                   std::uintmax_t* previous_size,
                                   std::string* error) {
    std::string line(json);
    if (line.empty() || line.back() != '\n') line.push_back('\n');
    const auto path = session_path_ / relative_path;
    std::error_code size_error;
    const auto size = fs::exists(path, size_error) ? fs::file_size(path, size_error) : 0;
    if (size_error) {
        if (error) *error = "cannot inspect " + WideToUtf8(path.wstring()) + ": " + size_error.message();
        return false;
    }
    if (previous_size != nullptr) *previous_size = size;
    if (!WriteUtf8File(path, line, true)) {
        std::error_code resize_error;
        if (fs::exists(path, resize_error)) fs::resize_file(path, size, resize_error);
        if (error) *error = "cannot append " + WideToUtf8(path.wstring());
        return false;
    }
    return true;
}

bool SessionStore::RebuildRawFramesJsonl(std::string* error) {
    const auto target = session_path_ / L"raw/frames.jsonl";
    const auto temporary = target.parent_path() / (L"frames.jsonl.tmp-" + Utf8ToWide(NewId()));
    sqlite3_stmt* statement = nullptr;
    if (sqlite3_prepare_v2(database_.Handle(),
                           "SELECT original_json FROM raw_frames ORDER BY id;", -1, &statement, nullptr) != SQLITE_OK) {
        if (error) *error = sqlite3_errmsg(database_.Handle());
        return false;
    }
    std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
    if (!output) {
        sqlite3_finalize(statement);
        if (error) *error = "cannot create raw evidence rebuild file";
        return false;
    }
    int step = SQLITE_ROW;
    while ((step = sqlite3_step(statement)) == SQLITE_ROW) {
        const auto* value = sqlite3_column_text(statement, 0);
        if (value != nullptr) output << reinterpret_cast<const char*>(value);
        output << '\n';
    }
    const int finalize = sqlite3_finalize(statement);
    output.flush();
    const bool output_ok = output.good();
    output.close();
    if (step != SQLITE_DONE || finalize != SQLITE_OK || !output_ok) {
        std::error_code remove_error;
        fs::remove(temporary, remove_error);
        if (error) *error = "raw evidence rebuild failed";
        return false;
    }
    if (!MoveFileExW(temporary.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        const auto code = GetLastError();
        std::error_code remove_error;
        fs::remove(temporary, remove_error);
        if (error) *error = "cannot atomically replace raw/frames.jsonl: " + std::to_string(code);
        return false;
    }
    return database_.Execute("UPDATE raw_frames SET jsonl_written=1 WHERE jsonl_written<>1;", error);
}

bool SessionStore::ImportLegacyJsonlRecords(std::string* error) {
    for (const auto& relative : RequiredGameplayFiles()) {
        const auto relative_utf8 = WideToUtf8(relative.generic_wstring());
        const auto existing = database_.ScalarInt64(
            "SELECT COUNT(*) FROM jsonl_records WHERE session_id=" + SqlQuote(session_id_) +
            " AND relative_path=" + SqlQuote(relative_utf8) + ";");
        if (existing != 0) continue;
        std::ifstream input(session_path_ / relative, std::ios::binary);
        if (!input) continue;
        std::string line;
        std::size_t line_number = 0;
        if (!database_.Begin(error)) return false;
        bool imported = true;
        while (std::getline(input, line)) {
            if (line.empty()) continue;
            ++line_number;
            if (!database_.Insert("jsonl_records", {
                    {"record_id", "legacy:" + relative_utf8 + ":" + std::to_string(line_number) + ":" + NewId()},
                    {"session_id", session_id_}, {"analysis_run_id", ""},
                    {"relative_path", relative_utf8}, {"json", line}
                }, error)) {
                imported = false;
                break;
            }
        }
        if (!imported || !database_.Commit(error)) {
            database_.Rollback();
            return false;
        }
    }
    return true;
}

bool SessionStore::RebuildGameplayJsonl(std::string* error) {
    for (const auto& relative : RequiredGameplayFiles()) {
        const auto relative_utf8 = WideToUtf8(relative.generic_wstring());
        const auto count = database_.ScalarInt64(
            "SELECT COUNT(*) FROM jsonl_records WHERE session_id=" + SqlQuote(session_id_) +
            " AND relative_path=" + SqlQuote(relative_utf8) + ";");
        if (count != 0 && !RebuildJsonlFromLedger(relative, error)) return false;
    }
    return true;
}

bool SessionStore::RebuildJsonlFromLedger(const fs::path& relative_path, std::string* error) {
    const auto target = session_path_ / relative_path;
    const auto temporary = target.parent_path() /
        (target.filename().wstring() + L".tmp-" + Utf8ToWide(NewId()));
    const auto relative_utf8 = WideToUtf8(relative_path.generic_wstring());
    const auto sql = "SELECT json FROM jsonl_records WHERE session_id=" + SqlQuote(session_id_) +
                     " AND relative_path=" + SqlQuote(relative_utf8) + " ORDER BY id;";
    sqlite3_stmt* statement = nullptr;
    if (sqlite3_prepare_v2(database_.Handle(), sql.c_str(), -1, &statement, nullptr) != SQLITE_OK) {
        if (error) *error = sqlite3_errmsg(database_.Handle());
        return false;
    }
    std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
    if (!output) {
        sqlite3_finalize(statement);
        if (error) *error = "cannot create JSONL rebuild file: " + WideToUtf8(temporary.wstring());
        return false;
    }
    int step = SQLITE_ROW;
    while ((step = sqlite3_step(statement)) == SQLITE_ROW) {
        const auto* value = sqlite3_column_text(statement, 0);
        if (value != nullptr) output << reinterpret_cast<const char*>(value);
        output << '\n';
    }
    const int finalize = sqlite3_finalize(statement);
    output.flush();
    const bool output_ok = output.good();
    output.close();
    if (step != SQLITE_DONE || finalize != SQLITE_OK || !output_ok) {
        std::error_code remove_error;
        fs::remove(temporary, remove_error);
        if (error) *error = "JSONL rebuild failed for " + relative_utf8;
        return false;
    }
    if (!MoveFileExW(temporary.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        const auto code = GetLastError();
        std::error_code remove_error;
        fs::remove(temporary, remove_error);
        if (error) *error = "cannot atomically replace " + relative_utf8 + ": " + std::to_string(code);
        return false;
    }
    return true;
}

std::vector<std::pair<std::string, fs::path>> RequiredExports() {
    return {
        {"movement_observations", L"exports/movement-observations.csv"},
        {"mount_state_observations", L"exports/mount-observations.csv"},
        {"quest_profiles", L"exports/quest-profiles.csv"},
        {"quest_objectives", L"exports/quest-objectives.csv"},
        {"quest_progress_observations", L"exports/quest-progress.csv"},
        {"quest_reward_observations", L"exports/quest-rewards.csv"},
        {"skill_profiles", L"exports/skill-profiles.csv"},
        {"skill_profile_facets", L"exports/skill-facets.csv"},
        {"skill_cast_correlations", L"exports/skill-casts.csv"},
        {"skill_effect_observations", L"exports/skill-effects.csv"},
        {"consumable_use_observations", L"exports/consumable-effects.csv"},
        {"entity_observations", L"exports/entity-observations.csv"},
        {"character_lifecycle_observations", L"exports/character-lifecycle.csv"},
        {"party_observations", L"exports/party-lifecycle.csv"},
        {"party_member_observations", L"exports/party-members.csv"},
        {"economy_observations", L"exports/economy-observations.csv"},
        {"progression_observations", L"exports/progression-observations.csv"},
        {"formula_candidates", L"exports/formula-candidates.csv"},
        {"gameplay_coverage", L"exports/gameplay-coverage-matrix.csv"},
        {"formula_coverage", L"exports/formula-coverage-matrix.csv"},
        {"protocol_handler_observations", L"exports/protocol-handler-observations.csv"},
        {"unknown_opcode_clusters", L"exports/unknown-opcode-clusters.csv"}
    };
}

std::vector<fs::path> RequiredGameplayFiles() {
    return {
        L"gameplay/movement.jsonl", L"gameplay/movement-directions.jsonl", L"gameplay/mount.jsonl",
        L"gameplay/mounted-movement.jsonl", L"gameplay/quests.jsonl", L"gameplay/quest-progress.jsonl",
        L"gameplay/quest-rewards.jsonl", L"gameplay/skills.jsonl", L"gameplay/skill-casts.jsonl",
        L"gameplay/skill-effects.jsonl", L"gameplay/consumables.jsonl",
        L"gameplay/entities.jsonl", L"gameplay/character-lifecycle.jsonl", L"gameplay/party.jsonl",
        L"gameplay/economy.jsonl", L"gameplay/progression.jsonl", L"gameplay/formulas.jsonl",
        L"gameplay/protocol-handler-observations.jsonl",
        L"gameplay/protocol-semantic-candidates.jsonl"
    };
}

bool SessionStore::ExportAll(std::string* error) {
    for (const auto& [table, relative] : RequiredExports()) {
        if (!database_.ExportCsv(table, session_path_ / relative, error)) return false;
    }
    return true;
}

bool SessionStore::WriteCompatibilityReports(const BackendSelection& selection, std::string* error) {
    std::ostringstream capabilities;
    capabilities << "{\"SelectedBackend\":\"" << JsonEscape(selection.capabilities.capture_backend_name) << "\",\"Backends\":[";
    for (std::size_t i = 0; i < selection.all_capabilities.size(); ++i) {
        if (i != 0) capabilities << ',';
        capabilities << CapabilitiesJson(selection.all_capabilities[i]);
        const auto& c = selection.all_capabilities[i];
        if (!database_.Insert("backend_capabilities", {
            {"session_id", session_id_}, {"observed_at_utc", UtcNow()},
            {"capture_backend_name", c.capture_backend_name}, {"capture_backend_version", c.capture_backend_version},
            {"available", c.available ? "true" : "false"},
            {"supports_realtime_events", c.supports_realtime_events ? "true" : "false"},
            {"supports_packet_payload", c.supports_packet_payload ? "true" : "false"},
            {"supports_direction", c.supports_direction ? "true" : "false"},
            {"supports_endpoint_filter", c.supports_endpoint_filter ? "true" : "false"},
            {"supports_process_filter", c.supports_process_filter ? "true" : "false"},
            {"supports_drop_statistics", c.supports_drop_statistics ? "true" : "false"},
            {"supports_pcapng_conversion", c.supports_pcapng_conversion ? "true" : "false"},
            {"supports_ipv4", c.supports_ipv4 ? "true" : "false"},
            {"supports_ipv6", c.supports_ipv6 ? "true" : "false"},
            {"analysis_mode", ToString(c.analysis_mode)}, {"blocked_capabilities", JoinList(c.blocked_capabilities)},
            {"probe_evidence", c.probe_evidence}
        }, error)) return false;
    }
    capabilities << "]}\n";
    if (!WriteUtf8File(session_path_ / L"compatibility/capture-backend-capabilities.json", capabilities.str())) {
        if (error) *error = "cannot write backend capabilities report";
        return false;
    }

    const auto os = DetectOsVersion();
    const std::string service_pack = "SP" + std::to_string(os.service_pack_major);
    const auto compatibility = MakeJsonObject({
        {"WindowsEdition", os.edition}, {"ServicePack", service_pack}, {"BuildNumber", std::to_string(os.build)},
        {"Architecture", os.architecture}, {"InstalledCaptureBackend", selection.capabilities.capture_backend_name},
        {"AnalysisMode", ToString(selection.capabilities.analysis_mode)}, {"CapturedPacketCount", "0"},
        {"CapturedBytes", "0"}, {"PcapngValidation", "NOT_RUN"}, {"ProcessExitCode", "NOT_RUN"},
        {"TestStatus", "CURRENT_HOST_PROBE_ONLY"}
    });
    if (!WriteUtf8File(session_path_ / L"compatibility/windows-compatibility-report.json", compatibility + "\n")) {
        if (error) *error = "cannot write OS compatibility report";
        return false;
    }
    return database_.Insert("os_compatibility_results", {
        {"session_id", session_id_}, {"observed_at_utc", UtcNow()}, {"windows_edition", os.edition},
        {"service_pack", service_pack}, {"build_number", std::to_string(os.build)},
        {"architecture", os.architecture}, {"installed_capture_backend", selection.capabilities.capture_backend_name},
        {"analysis_mode", ToString(selection.capabilities.analysis_mode)}, {"captured_packet_count", "0"},
        {"captured_bytes", "0"}, {"pcapng_validation", "NOT_RUN"}, {"process_exit_code", "NOT_RUN"},
        {"test_status", "CURRENT_HOST_PROBE_ONLY"}
    }, error);
}

} // namespace god2
