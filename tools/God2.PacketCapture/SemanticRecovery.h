#pragma once

#include "Core.h"

namespace god2 {

struct SemanticRecoveryInput {
    std::string session_id;
    std::string client_sha256;
    fs::path session_path;
    fs::path normalized_capture_records;
    fs::path raw_semantic_events;
    fs::path semantic_output_directory;
    fs::path sessions_root;
};

struct SemanticRecoveryResult {
    bool success = false;
    std::string status = "SemanticRuntimeEvidenceUnavailable";
    std::string error;
    std::uint64_t parser_read_events = 0;
    std::uint64_t serializer_write_events = 0;
    std::uint64_t handler_argument_events = 0;
    std::uint64_t object_resolution_events = 0;
    std::uint64_t state_mutation_events = 0;
    std::uint64_t ui_anchor_events = 0;
    std::uint64_t value_flow_edges = 0;
    std::uint64_t protocol_field_evidence = 0;
    std::uint64_t unknown_fields = 0;
    std::uint64_t candidate_fields = 0;
    std::uint64_t recovered_fields = 0;
    std::uint64_t verified_fields = 0;
    std::uint64_t verified_spec_packets = 0;
    std::uint64_t server_integration_ready = 0;
    std::uint64_t database_integration_ready = 0;
    std::uint64_t contradictions = 0;
    bool semantic_evidence_incomplete = false;
};

SemanticRecoveryResult AnalyzeSemanticRecovery(const SemanticRecoveryInput& input);
int RunSemanticRecoverySelfTest(const fs::path& report_path);

} // namespace god2
