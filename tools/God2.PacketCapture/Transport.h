#pragma once

#include "Gameplay.h"

namespace god2 {

struct PcapngExtractionResult {
    bool success = false;
    std::uint64_t packet_count = 0;
    std::uint64_t payload_bytes = 0;
    std::uint64_t unknown_protocol_packets = 0;
    std::string message;
};

struct VerifiedWorldMovementFrame {
    std::uint16_t x = 0;
    std::uint16_t y = 0;
    std::uint8_t sequence = 0;
    std::string decoded_hex;
};

bool TryDecodeVerifiedWorldMovement(const std::uint8_t* data,
                                    std::size_t size,
                                    VerifiedWorldMovementFrame& movement);

PcapngExtractionResult ExtractPcapngPayloads(const fs::path& pcapng,
                                             GameplayAnalysisEngine& engine,
                                             std::string* error = nullptr,
                                             bool persist_raw = true);

struct God2FrameCandidateAnalysisResult {
    bool success = false;
    std::uint64_t transport_records = 0;
    std::uint64_t payload_bytes = 0;
    std::uint64_t candidate_frames = 0;
    std::uint64_t client_to_server_frames = 0;
    std::uint64_t server_to_client_frames = 0;
    std::uint64_t frame208_candidates = 0;
    std::uint64_t desync_bytes = 0;
    std::uint64_t invalid_length_count = 0;
    std::uint64_t pending_stream_bytes = 0;
    std::uint64_t incomplete_stream_count = 0;
    std::uint64_t evicted_stream_bytes = 0;
    std::uint64_t evicted_candidate_clusters = 0;
    std::uint64_t cluster_count = 0;
    std::uint64_t semantic_candidate_count = 0;
    std::uint64_t high_confidence_semantic_candidates = 0;
    std::string message;
};

God2FrameCandidateAnalysisResult AnalyzeInjectedGod2FrameCandidates(const fs::path& session_path,
                                                                    const fs::path& injected_packets,
                                                                    std::string* error = nullptr);

} // namespace god2
