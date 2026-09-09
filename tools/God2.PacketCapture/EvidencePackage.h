#pragma once

#include "Core.h"

namespace god2 {

struct EvidencePackageResult {
    bool success = false;
    fs::path zip_path;
    fs::path sha256_path;
    std::string zip_sha256;
    std::string session_id;
    std::string acquisition_status;
    std::string analysis_status;
    std::string cleanup_status;
    std::string package_status;
    bool gpu_available = false;
    bool gpu_eligible = false;
    bool gpu_selected = false;
    std::string gpu_device = "None";
    std::string gpu_capability = "CPU";
    std::string gpu_selected_backend = "CPU";
    std::string gpu_selection_reason = "GpuUnavailable";
    std::uint64_t capture_records = 0;
    std::uint64_t transport_chunks = 0;
    std::uint64_t protocol_frames = 0;
    std::uint64_t candidate_protocol_frames = 0;
    std::uint64_t decoded_messages = 0;
    std::uint64_t handler_observations = 0;
    std::uint64_t gameplay_candidates = 0;
    std::uint64_t automatic_semantic_candidates = 0;
    std::uint64_t automatic_semantic_family_count = 0;
    std::uint64_t automatic_structurally_sufficient_family_count = 0;
    std::uint64_t evidence_items = 0;
    std::uint64_t client_to_server_records = 0;
    std::uint64_t server_to_client_records = 0;
    std::uint64_t transport_send_records = 0;
    std::uint64_t transport_recv_records = 0;
    std::uint64_t pre_encrypt_records = 0;
    std::uint64_t post_decrypt_records = 0;
    std::uint64_t handler_decoded_records = 0;
    std::uint64_t unframed_plaintext_records = 0;
    std::uint64_t unknown_protocol_frames = 0;
    std::uint64_t client_to_server_opcode_count = 0;
    std::uint64_t server_to_client_opcode_count = 0;
    std::uint64_t client_to_server_opcode_observations = 0;
    std::uint64_t server_to_client_opcode_observations = 0;
    bool enhanced_plaintext_available = false;
    std::uint64_t correlated_logical_messages = 0;
    std::uint64_t uncorrelated_logical_messages = 0;
    std::uint64_t desync_bytes = 0;
    std::uint64_t pending_stream_bytes = 0;
    std::uint64_t etw_event_lines = 0;
    std::uint64_t uncompressed_package_bytes = 0;
    std::uint64_t compressed_package_bytes = 0;
    std::uint64_t compression_duration_ms = 0;
    double compression_ratio = 0.0;
    int compression_level = 6;
    std::string compression_method = "Deflate";
    bool zip64_used = false;
    std::uint64_t connection_count = 0;
    std::uint64_t candidate_client_to_server_frames = 0;
    std::uint64_t candidate_server_to_client_frames = 0;
    std::uint64_t invalid_candidate_frames = 0;
    std::uint64_t incomplete_stream_fragments = 0;
    std::uint64_t exact_correlations = 0;
    std::uint64_t strong_correlations = 0;
    std::uint64_t candidate_correlations = 0;
    std::uint64_t uncorrelated_correlations = 0;
    std::string semantic_probe_status = "SemanticRuntimeEvidenceUnavailable";
    std::uint64_t parser_read_events = 0;
    std::uint64_t serializer_write_events = 0;
    std::uint64_t handler_argument_events = 0;
    std::uint64_t object_resolution_events = 0;
    std::uint64_t state_mutation_events = 0;
    std::uint64_t ui_anchor_events = 0;
    std::uint64_t semantic_value_flow_edges = 0;
    std::uint64_t protocol_field_evidence = 0;
    std::uint64_t unknown_field_semantics = 0;
    std::uint64_t candidate_field_semantics = 0;
    std::uint64_t recovered_field_semantics = 0;
    std::uint64_t verified_field_semantics = 0;
    std::uint64_t verified_protocol_spec_packets = 0;
    std::uint64_t server_integration_ready = 0;
    std::uint64_t database_integration_ready = 0;
    std::uint64_t semantic_contradictions = 0;
    bool semantic_evidence_incomplete = false;
    double correlation_coverage_percent = 0.0;
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
    std::string client_sha256;
    std::string launcher_sha256;
    std::string client_identity_status = "Unknown";
    std::string launcher_identity_status = "Unknown";
    bool timestamp_validation_passed = false;
    bool count_reconciliation_passed = false;
    bool manifest_validation_passed = false;
    bool provenance_validation_passed = false;
    bool schema_validation_passed = false;
    bool sqlite_integrity_passed = false;
    bool session_identity_passed = false;
    bool relative_paths_passed = false;
    bool zip_reopen_validation_passed = false;
    std::vector<std::string> warnings;
    std::vector<std::string> blockers;
    std::string error;
};

class EvidencePackageBuilder {
public:
    static EvidencePackageResult Build(const fs::path& session_path,
                                       bool partial_recovery = false,
                                       bool simulate_zip_failure_for_test = false);
    static EvidencePackageResult Repackage(const fs::path& package_path);
    static void RecoverIncompleteSessionsNoThrow();
};

int RunEvidencePackageFixtureTest(const fs::path& session_path, const fs::path& report_path);
int RunGpuPackageEquivalenceTest(const fs::path& session_path, const fs::path& report_path);

// Shared by the validation-only portable runner.  These wrappers expose the
// same audited SHA-256 and deterministic stored-ZIP primitives used by the
// evidence package without exposing its internal manifest model.
std::optional<std::string> CalculateFileSha256(const fs::path& path);
bool CreatePortableStoredZip(const fs::path& root, const fs::path& zip_path,
                             std::string* error = nullptr);
bool ValidatePortableStoredZip(const fs::path& root, const fs::path& zip_path,
                               std::string* error = nullptr);

} // namespace god2
