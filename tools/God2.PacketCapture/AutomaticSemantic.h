#pragma once

#include "Core.h"

namespace god2 {

struct AutomaticSemanticInput {
    std::string capture_stage;
    std::string direction;
    std::string opcode;
    std::uint64_t frame_length = 0;
    std::string payload_hex;
    std::string caller_rva;
    std::string correlation_level;
};

struct AutomaticSemanticCandidate {
    std::string candidate_type;
    std::string candidate_domain;
    std::string identification_basis;
    std::string reason;
    std::string evidence_status = "Candidate";
    std::uint32_t confidence = 0;
    Fields extracted_fields;
};

std::optional<AutomaticSemanticCandidate> RecognizeAutomaticSemanticCandidate(
    const AutomaticSemanticInput& input);

int RunAutomaticSemanticRecognitionTests();

} // namespace god2
