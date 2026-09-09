#pragma once

#include "Core.h"

namespace god2 {

struct ActionGroupingRecord {
    std::string capture_record_id;
    std::string logical_message_id;
    std::string capture_stage;
    std::string direction;
    std::string api;
    std::string connection_id;
    std::string socket;
    std::string transport_chunk_id;
    std::string decoded_message_id;
    std::string handler_observation_id;
    std::string protocol_frame_id;
    std::string opcode;
    std::uint64_t frame_length = 0;
    std::string payload_hex;
    std::string captured_at_utc;
    std::int64_t captured_at_unix_ms = 0;
    std::uint64_t capture_sequence = 0;
    std::uint64_t stage_sequence = 0;
    std::int64_t captured_qpc = 0;
    std::int64_t qpc_frequency = 0;
    std::int64_t process_id = 0;
    std::int64_t thread_id = 0;
    std::string hook_invocation_id;
    std::string parent_invocation_id;
    std::string context_invocation_id;
    std::string context_correlation_basis;
    std::string correlation_level;
    std::map<std::string, std::string> verified_registry_fields;
    std::map<std::string, std::string> domain_correlation_ids;
};

struct ActionGroupingOpcodeWorkItem {
    std::string work_item_id;
    std::string direction;
    std::string opcode;
    std::uint64_t frame_length = 0;
};

struct GenericActionGroupingInput {
    std::string session_id;
    std::vector<ActionGroupingRecord> records;
    std::vector<ActionGroupingOpcodeWorkItem> opcode_work_items;
};

struct GenericActionGroupingResult {
    std::string action_instances_jsonl;
    std::string action_patterns_jsonl;
    std::string action_pattern_summary_json;
    std::string orphan_handler_bursts_jsonl;
    std::string outbound_batch_candidates_jsonl;
    std::map<std::string, std::vector<std::string>> related_action_pattern_ids_by_work_item;
    std::uint64_t action_trigger_candidate_count = 0;
    std::uint64_t action_instance_count = 0;
    std::uint64_t action_pattern_count = 0;
    std::uint64_t grouped_trigger_count = 0;
    std::uint64_t ungrouped_trigger_count = 0;
    std::uint64_t exact_action_count = 0;
    std::uint64_t strong_action_count = 0;
    std::uint64_t candidate_action_count = 0;
    std::uint64_t ambiguous_action_count = 0;
    std::uint64_t unknown_action_pattern_count = 0;
    std::uint64_t verified_gameplay_mapped_pattern_count = 0;
    std::uint64_t outbound_batch_candidate_count = 0;
    std::uint64_t outbound_batch_trigger_count = 0;
    std::uint64_t orphan_handler_burst_count = 0;
    std::uint64_t background_candidate_count = 0;
    double action_clustering_coverage_percent = 0.0;
    double session_latency_median_ms = 0.0;
    double session_latency_p95_ms = 0.0;
    double session_latency_limit_ms = 0.0;
};

GenericActionGroupingResult GroupGenericUnknownActions(const GenericActionGroupingInput& input);

} // namespace god2
