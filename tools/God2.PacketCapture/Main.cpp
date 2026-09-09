#include "Core.h"
#include "AutomaticSemantic.h"
#include "EtwConsumer.h"
#include "EvidencePackage.h"
#include "Gui.h"
#include "Gameplay.h"
#include "GpuAcceleration.h"
#include "Rtx5070Validation.h"
#include "SemanticRecovery.h"
#include "SharedSemanticRing.h"
#include "Storage.h"
#include "Transport.h"
#include "UltimateRecovery.h"
#include "Version.h"

#include <shellapi.h>
#include <tlhelp32.h>
#include <winioctl.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cstring>
#include <fstream>
#include <iostream>
#include <iterator>
#include <memory>
#include <new>
#include <set>
#include <sstream>
#include <stdexcept>
#include <thread>
#include <type_traits>
#include <vector>
#include <ws2tcpip.h>

namespace god2 {
namespace {

constexpr int kExitUsage = 64;
constexpr int kExitUnavailable = 69;
constexpr int kExitSoftware = 70;
constexpr std::string_view kExactTargetClientSha256 =
    "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";

struct Arguments {
    std::vector<std::wstring> values;

    bool Has(std::wstring_view option) const {
        return std::find(values.begin(), values.end(), option) != values.end();
    }
    std::optional<std::wstring> Value(std::wstring_view option) const {
        for (std::size_t i = 0; i + 1 < values.size(); ++i) {
            if (values[i] == option) return values[i + 1];
        }
        return std::nullopt;
    }
};

enum class UltimateLiveElevationDecision { RunHere, Elevate, BlockNoUac };

constexpr UltimateLiveElevationDecision UltimateLiveElevationPolicy(
    bool administrator, bool no_uac_automation) noexcept {
    if (administrator) return UltimateLiveElevationDecision::RunHere;
    return no_uac_automation ? UltimateLiveElevationDecision::BlockNoUac :
                               UltimateLiveElevationDecision::Elevate;
}

bool IsContainedValidationPath(const fs::path& root, const fs::path& candidate);

struct LiveSecuritySelfTestResult {
    bool crafted_newer_ignored = false;
    bool reparse_rejected = false;
    bool zip_self_report_rejected = false;
    bool zip_tamper_rejected = false;
    bool file_replacement_blocked = false;
    bool handle_identity_revalidated = false;
};

LiveSecuritySelfTestResult RunLiveSecuritySelfTests(const fs::path& fixture_root);
bool DeepProbeCandidateMapValidatorSelfTest();
bool EnhancedStoppedStatusV2ValidatorSelfTest();

std::string UtcNowIso8601() {
    SYSTEMTIME value{};
    GetSystemTime(&value);
    char buffer[32]{};
    sprintf_s(buffer, "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ",
              value.wYear, value.wMonth, value.wDay, value.wHour,
              value.wMinute, value.wSecond, value.wMilliseconds);
    return buffer;
}

struct ContinuitySegment {
    std::uint32_t index = 0;
    fs::path path;
    std::uint64_t records = 0;
    std::uint64_t bytes = 0;
    std::uint32_t first_sequence = 0;
    std::uint32_t last_sequence = 0;
    std::string sha256;
};

bool VerifySegmentConcatenation(const std::vector<ContinuitySegment>& segments,
                                const fs::path& merged_path) {
    std::ifstream merged(merged_path, std::ios::binary);
    if (!merged) return false;
    std::vector<char> segment_buffer(64 * 1024);
    std::vector<char> merged_buffer(64 * 1024);
    for (const auto& segment : segments) {
        std::ifstream input(segment.path, std::ios::binary);
        if (!input) return false;
        while (input) {
            input.read(segment_buffer.data(),
                       static_cast<std::streamsize>(segment_buffer.size()));
            const auto count = input.gcount();
            if (count <= 0) break;
            merged.read(merged_buffer.data(), count);
            if (merged.gcount() != count ||
                std::memcmp(segment_buffer.data(), merged_buffer.data(),
                            static_cast<std::size_t>(count)) != 0) return false;
        }
    }
    return merged.peek() == std::ifstream::traits_type::eof();
}

int RunSemanticContinuityStress(const fs::path& output_root,
                                std::uint32_t event_count) {
    constexpr std::uint64_t kSegmentByteLimit = 16ULL * 1024ULL * 1024ULL;
    constexpr std::uint64_t kSegmentRecordLimit = 100000ULL;
    if (event_count < 2000001U || fs::exists(output_root)) return kExitUsage;
    std::error_code error;
    fs::create_directories(output_root / L"segments", error);
    if (error) return kExitSoftware;

    auto ring = std::make_unique<shared::SemanticSharedRing>();
    std::memset(ring.get(), 0, sizeof(*ring));
    ring->magic = shared::kSemanticRingMagic;
    ring->version = shared::kSemanticRingVersion;
    ring->header_bytes = static_cast<std::uint32_t>(
        offsetof(shared::SemanticSharedRing, slots));
    ring->slot_bytes = sizeof(shared::SemanticRingSlot);
    ring->capacity = shared::kSemanticPriorityCount *
                     shared::kSemanticLaneSlotCount;
    ring->domain_count = shared::kSemanticDomainCount;
    ring->lane_capacity = shared::kSemanticLaneSlotCount;
    ring->lane_count = shared::kSemanticPriorityCount;
    ring->producer_ready = 1;
    ring->consumer_ready = 1;
    ring->producer_process_id = GetCurrentProcessId();
    if (!shared::HasValidSemanticRingHeader(*ring)) return kExitSoftware;

    const auto merged_path = output_root / L"semantic-events-merged.jsonl";
    std::ofstream merged(merged_path, std::ios::binary | std::ios::trunc);
    if (!merged) return kExitSoftware;
    std::vector<ContinuitySegment> segments;
    std::ofstream segment_stream;
    ContinuitySegment current;
    std::atomic<bool> producer_done{false};
    std::atomic<bool> failed{false};
    std::atomic<std::uint64_t> producer_waits{0};
    std::atomic<std::uint64_t> write_failures{0};
    const auto deadline = std::chrono::steady_clock::now() +
                          std::chrono::minutes(5);

    const auto close_segment = [&]() {
        if (!segment_stream.is_open()) return true;
        segment_stream.flush();
        const bool stream_ok = static_cast<bool>(segment_stream);
        segment_stream.close();
        if (!stream_ok || current.records == 0) return false;
        current.sha256 = CalculateFileSha256(current.path).value_or("");
        error.clear();
        if (current.sha256.size() != 64U ||
            fs::file_size(current.path, error) != current.bytes || error) return false;
        segments.push_back(current);
        current = ContinuitySegment{};
        return true;
    };
    const auto open_segment = [&]() {
        current.index = static_cast<std::uint32_t>(segments.size());
        wchar_t name[64]{};
        swprintf_s(name, L"semantic-events-%06u.jsonl", current.index);
        current.path = output_root / L"segments" / name;
        segment_stream.open(current.path, std::ios::binary | std::ios::trunc);
        return static_cast<bool>(segment_stream);
    };

    std::thread consumer([&]() {
        std::uint32_t expected = 1;
        while (expected <= event_count && !failed.load()) {
            if (std::chrono::steady_clock::now() > deadline) {
                failed.store(true);
                break;
            }
            bool consumed = false;
            for (std::uint32_t priority = 0;
                 priority < 2U && !consumed; ++priority) {
                auto& lane = shared::SemanticLane(*ring, priority);
                const LONG next_lane_sequence =
                    InterlockedCompareExchange(&lane.read_sequence, 0, 0) + 1;
                auto& slot = shared::SemanticLaneSlot(
                    *ring, priority, next_lane_sequence - 1);
                if (InterlockedCompareExchange(&slot.state, 0, 0) != 2 ||
                    slot.sequence != expected) continue;
                MemoryBarrier();
                if (slot.priority != priority || slot.payload_bytes == 0 ||
                    slot.payload_bytes > shared::kSemanticPayloadBytes) {
                    failed.store(true);
                    break;
                }
                const std::string line(slot.payload, slot.payload + slot.payload_bytes);
                const std::string expected_line = "{\"Sequence\":" +
                    std::to_string(expected) + ",\"Priority\":" +
                    std::to_string(priority) + ",\"Domain\":" +
                    std::to_string(slot.domain) + "}\n";
                if (line != expected_line) {
                    failed.store(true);
                    break;
                }
                if (!segment_stream.is_open() && !open_segment()) {
                    failed.store(true);
                    break;
                }
                if (current.records >= kSegmentRecordLimit ||
                    current.bytes + line.size() > kSegmentByteLimit) {
                    if (!close_segment() || !open_segment()) {
                        failed.store(true);
                        break;
                    }
                }
                if (current.records == 0) current.first_sequence = expected;
                current.last_sequence = expected;
                ++current.records;
                current.bytes += line.size();
                segment_stream.write(line.data(),
                    static_cast<std::streamsize>(line.size()));
                merged.write(line.data(), static_cast<std::streamsize>(line.size()));
                if (!segment_stream || !merged) {
                    ++write_failures;
                    failed.store(true);
                    break;
                }
                InterlockedExchange(&slot.state, 0);
                InterlockedExchange(&lane.read_sequence, next_lane_sequence);
                InterlockedExchange(&ring->read_sequence,
                                    static_cast<LONG>(expected));
                const LONG lane_lag = InterlockedCompareExchange(
                    &lane.write_sequence, 0, 0) - next_lane_sequence;
                InterlockedExchange(&lane.consumer_lag, lane_lag);
                ++expected;
                consumed = true;
            }
            if (!consumed && !failed.load()) {
                if (producer_done.load() &&
                    InterlockedCompareExchange(&ring->read_sequence, 0, 0) >=
                        static_cast<LONG>(event_count)) break;
                SwitchToThread();
            }
        }
        if (!close_segment()) failed.store(true);
        merged.flush();
        if (!merged) failed.store(true);
        InterlockedExchange(&ring->consumer_closed, 1);
    });

    std::thread producer([&]() {
        for (std::uint32_t sequence = 1;
             sequence <= event_count && !failed.load(); ++sequence) {
            if (std::chrono::steady_clock::now() > deadline) {
                failed.store(true);
                break;
            }
            const std::uint32_t priority = (sequence - 1U) & 1U;
            const std::uint32_t domain = priority == 0U ? 0U : 4U;
            auto& lane = shared::SemanticLane(*ring, priority);
            while (InterlockedCompareExchange(&lane.write_sequence, 0, 0) -
                    InterlockedCompareExchange(&lane.read_sequence, 0, 0) >=
                    static_cast<LONG>(shared::kSemanticLaneSlotCount)) {
                ++producer_waits;
                if (failed.load() ||
                    std::chrono::steady_clock::now() > deadline) {
                    failed.store(true);
                    break;
                }
                SwitchToThread();
            }
            if (failed.load()) break;
            const LONG lane_sequence =
                InterlockedIncrement(&lane.write_sequence);
            auto& slot = shared::SemanticLaneSlot(
                *ring, priority, lane_sequence - 1);
            while (InterlockedCompareExchange(&slot.state, 1, 0) != 0) {
                ++producer_waits;
                if (failed.load() ||
                    std::chrono::steady_clock::now() > deadline) {
                    failed.store(true);
                    break;
                }
                SwitchToThread();
            }
            if (failed.load()) break;
            char line[128]{};
            const int written = sprintf_s(line,
                "{\"Sequence\":%u,\"Priority\":%u,\"Domain\":%u}\n",
                sequence, priority, domain);
            if (written <= 0 ||
                static_cast<std::size_t>(written) > shared::kSemanticPayloadBytes) {
                failed.store(true);
                InterlockedExchange(&slot.state, 0);
                break;
            }
            slot.sequence = sequence;
            slot.domain = domain;
            slot.priority = priority;
            slot.payload_bytes = static_cast<std::uint32_t>(written);
            slot.timestamp_unix_ms = sequence;
            std::memcpy(slot.payload, line, static_cast<std::size_t>(written));
            MemoryBarrier();
            InterlockedExchange(&slot.state, 2);
            InterlockedExchange(&ring->attempted_sequence,
                                static_cast<LONG>(sequence));
            InterlockedExchange(&ring->write_sequence,
                                static_cast<LONG>(sequence));
            InterlockedIncrement(&ring->domains[domain].accepted);
            const LONG lane_lag = lane_sequence - InterlockedCompareExchange(
                &lane.read_sequence, 0, 0);
            LONG observed = InterlockedCompareExchange(
                &lane.high_water_mark, 0, 0);
            while (lane_lag > observed && InterlockedCompareExchange(
                    &lane.high_water_mark, lane_lag, observed) != observed) {
                observed = InterlockedCompareExchange(
                    &lane.high_water_mark, 0, 0);
            }
        }
        InterlockedExchange(&ring->producer_closed, 1);
        producer_done.store(true);
    });

    producer.join();
    consumer.join();
    merged.close();
    const auto merged_sha = CalculateFileSha256(merged_path).value_or("");
    error.clear();
    const auto merged_bytes = fs::exists(merged_path) ?
        fs::file_size(merged_path, error) : 0ULL;
    std::uint64_t segment_records = 0;
    std::uint64_t segment_bytes = 0;
    bool segment_contract = !segments.empty();
    std::uint32_t next_sequence = 1;
    for (const auto& segment : segments) {
        segment_records += segment.records;
        segment_bytes += segment.bytes;
        segment_contract = segment_contract && segment.index < segments.size() &&
            segment.records > 0 && segment.records <= kSegmentRecordLimit &&
            segment.bytes <= kSegmentByteLimit && segment.sha256.size() == 64U &&
            segment.first_sequence == next_sequence &&
            segment.last_sequence == segment.first_sequence +
                static_cast<std::uint32_t>(segment.records) - 1U;
        next_sequence = segment.last_sequence + 1U;
    }
    const LONG p0_accepted = InterlockedCompareExchange(
        &ring->domains[0].accepted, 0, 0);
    const LONG p1_accepted = InterlockedCompareExchange(
        &ring->domains[4].accepted, 0, 0);
    const bool sequence_ordered =
        ring->read_sequence == static_cast<LONG>(event_count) &&
        ring->write_sequence == static_cast<LONG>(event_count) &&
        segment_records == event_count && next_sequence == event_count + 1U;
    const bool concatenation_verified =
        segment_bytes == merged_bytes && merged_sha.size() == 64U &&
        segment_contract && VerifySegmentConcatenation(segments, merged_path);
    const bool zero_loss = write_failures.load() == 0 &&
        ring->dropped_total == 0 && ring->sampled_total == 0 &&
        p0_accepted + p1_accepted == static_cast<LONG>(event_count) &&
        ring->lanes[0].dropped == 0 && ring->lanes[1].dropped == 0 &&
        ring->lanes[0].high_water_mark <=
            static_cast<LONG>(shared::kSemanticLaneSlotCount) &&
        ring->lanes[1].high_water_mark <=
            static_cast<LONG>(shared::kSemanticLaneSlotCount);
    const bool passed = !failed.load() && sequence_ordered &&
        concatenation_verified && zero_loss;

    std::ostringstream segment_json;
    segment_json << '[';
    for (std::size_t index = 0; index < segments.size(); ++index) {
        if (index != 0) segment_json << ',';
        const auto& segment = segments[index];
        segment_json << MakeJsonObject({
            {"Index", std::to_string(segment.index)},
            {"Path", "segments/" + WideToUtf8(segment.path.filename().wstring())},
            {"Records", std::to_string(segment.records)},
            {"Bytes", std::to_string(segment.bytes)},
            {"FirstSequence", std::to_string(segment.first_sequence)},
            {"LastSequence", std::to_string(segment.last_sequence)},
            {"SHA256", segment.sha256}
        }, {"Index", "Records", "Bytes", "FirstSequence", "LastSequence"});
    }
    segment_json << ']';
    const auto report = MakeJsonObject({
        {"SchemaVersion", "god2-semantic-continuity-stress-v1"},
        {"GeneratedAtUtc", UtcNowIso8601()},
        {"TransportMagic", "GSR4"}, {"TransportVersion", "4"},
        {"EventCount", std::to_string(event_count)},
        {"Attempted", std::to_string(ring->attempted_sequence)},
        {"Accepted", std::to_string(p0_accepted + p1_accepted)},
        {"P0Accepted", std::to_string(p0_accepted)},
        {"P1Accepted", std::to_string(p1_accepted)},
        {"P0Dropped", std::to_string(ring->lanes[0].dropped)},
        {"P1Dropped", std::to_string(ring->lanes[1].dropped)},
        {"WriteFailures", std::to_string(write_failures.load())},
        {"Sampled", std::to_string(ring->sampled_total)},
        {"FirstSequence", "1"}, {"LastSequence", std::to_string(event_count)},
        {"SequenceOrdered", sequence_ordered ? "true" : "false"},
        {"PendingAfterDrain", std::to_string(
            ring->write_sequence - ring->read_sequence)},
        {"P0HighWater", std::to_string(ring->lanes[0].high_water_mark)},
        {"P1HighWater", std::to_string(ring->lanes[1].high_water_mark)},
        {"ProducerBackpressureWaits", std::to_string(producer_waits.load())},
        {"SegmentByteLimit", std::to_string(kSegmentByteLimit)},
        {"SegmentRecordLimit", std::to_string(kSegmentRecordLimit)},
        {"SegmentCount", std::to_string(segments.size())},
        {"Segments", segment_json.str()},
        {"MergedPath", "semantic-events-merged.jsonl"},
        {"MergedBytes", std::to_string(merged_bytes)},
        {"MergedSHA256", merged_sha},
        {"ConcatenationVerified", concatenation_verified ? "true" : "false"},
        {"SemanticEvidenceIncomplete", passed ? "false" : "true"},
        {"Passed", passed ? "true" : "false"},
        {"Status", passed ? "PASS" : "FAILED"}
    }, {"TransportVersion", "EventCount", "Attempted", "Accepted", "P0Accepted",
        "P1Accepted", "P0Dropped", "P1Dropped", "WriteFailures", "Sampled",
        "FirstSequence", "LastSequence", "SequenceOrdered", "PendingAfterDrain",
        "P0HighWater", "P1HighWater", "ProducerBackpressureWaits", "SegmentByteLimit",
        "SegmentRecordLimit", "SegmentCount", "Segments", "MergedBytes",
        "ConcatenationVerified", "SemanticEvidenceIncomplete", "Passed"});
    if (!WriteUtf8FileAtomic(output_root / L"semantic-continuity-stress.json",
                             report + "\n")) return kExitSoftware;
    return passed ? 0 : kExitSoftware;
}

void Print(std::string_view value, bool error = false) {
    FILE* stream = error ? stderr : stdout;
    std::fwrite(value.data(), 1, value.size(), stream);
    std::fwrite("\n", 1, 1, stream);
}

std::string CompactUtcTimestamp() {
    SYSTEMTIME value{};
    GetSystemTime(&value);
    char buffer[32]{};
    sprintf_s(buffer, "%04u%02u%02uT%02u%02u%02uZ",
              value.wYear, value.wMonth, value.wDay,
              value.wHour, value.wMinute, value.wSecond);
    return buffer;
}

struct InjectedTransportEvidenceSummary {
    std::uint64_t records = 0;
    std::uint64_t client_to_server = 0;
    std::uint64_t server_to_client = 0;
    std::uint64_t send_calls = 0;
    std::uint64_t recv_calls = 0;
    std::uint64_t post_decrypt = 0;
    std::uint64_t pre_encrypt = 0;
    std::uint64_t handler_decoded = 0;
    std::uint64_t frame208_candidates = 0;
    std::uint64_t payload_bytes = 0;
};

std::uint64_t JsonMetric(const Fields& fields, std::string_view key) {
    const auto value = GetInt64(fields, key).value_or(0);
    return value > 0 ? static_cast<std::uint64_t>(value) : 0;
}

std::uint64_t JsonlLineCount(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) return 0;
    return static_cast<std::uint64_t>(std::count(std::istreambuf_iterator<char>(stream),
                                                 std::istreambuf_iterator<char>(), '\n'));
}

bool WriteAnalysisSummary(const fs::path& session_path, const AnalysisStatistics& statistics,
                          bool success, std::string_view analysis_run_id,
                          std::string_view mode, std::string* error) {
    std::uint64_t raw_etl_total_events = 0;
    std::uint64_t raw_etl_packet_events = 0;
    bool raw_etl_audit_failed = false;
    if (const auto raw_report = ReadUtf8File(session_path / L"reports" / L"raw-etl-inventory.json")) {
        Fields fields;
        std::string parse_error;
        if (ParseFlatJson(*raw_report, fields, &parse_error)) {
            const auto total = GetInt64(fields, "TotalEvents").value_or(0);
            const auto packets = GetInt64(fields, "NdisPacketEvents").value_or(0);
            raw_etl_total_events = total > 0 ? static_cast<std::uint64_t>(total) : 0;
            raw_etl_packet_events = packets > 0 ? static_cast<std::uint64_t>(packets) : 0;
            raw_etl_audit_failed = GetString(fields, "Status") == "Failed";
        }
    }
    const bool raw_only = statistics.frames == 0 && raw_etl_packet_events > 0;
    const bool warnings = success && (raw_only || raw_etl_audit_failed);
    std::uint64_t candidate_frames = 0;
    std::uint64_t semantic_candidates = 0;
    std::uint64_t high_confidence_candidates = 0;
    Fields candidate_fields;
    std::string candidate_parse_error;
    if (const auto report = ReadUtf8File(session_path / L"reports" / L"god2-frame-candidates.json");
        report && ParseFlatJson(*report, candidate_fields, &candidate_parse_error)) {
        candidate_frames = JsonMetric(candidate_fields, "CandidateFrames");
    }
    if (candidate_frames == 0)
        candidate_frames = JsonlLineCount(session_path / L"raw" / L"god2-frame-candidates.jsonl");
    candidate_fields.clear();
    candidate_parse_error.clear();
    if (const auto report = ReadUtf8File(session_path / L"reports" / L"god2-opcode-semantic-candidates.json");
        report && ParseFlatJson(*report, candidate_fields, &candidate_parse_error)) {
        semantic_candidates = JsonMetric(candidate_fields, "SemanticCandidateCount");
        high_confidence_candidates = JsonMetric(candidate_fields, "HighConfidenceSemanticCandidates");
    }
    if (semantic_candidates == 0)
        semantic_candidates = JsonlLineCount(session_path / L"raw" / L"god2-opcode-semantic-candidates.jsonl");
    if (high_confidence_candidates > semantic_candidates)
        high_confidence_candidates = semantic_candidates;
    const std::uint64_t verified = statistics.movement + statistics.mount + statistics.quest + statistics.skill +
        statistics.consumable + statistics.entity + statistics.character_lifecycle + statistics.party +
        statistics.economy + statistics.progression + statistics.formula;
    const std::string classification_status = raw_only ? "EvidenceBlockedRawOnly" :
        (verified > 0 && semantic_candidates > 0 ? "VerifiedAndCandidate" :
         verified > 0 ? "Verified" : semantic_candidates > 0 ? "CandidateOnly" : "UnknownOnly");
    Fields transport_fields;
    if (const auto report = ReadUtf8File(session_path / L"reports" / L"injected-transport-evidence.json"))
        ParseFlatJson(*report, transport_fields, nullptr);
    const auto transport_chunks = GetInt64(transport_fields, "SendCalls").value_or(0) +
        GetInt64(transport_fields, "RecvCalls").value_or(0);
    const auto decoded_messages = GetInt64(transport_fields, "PreEncryptRecords").value_or(0) +
        GetInt64(transport_fields, "PostDecryptRecords").value_or(0);
    const auto handler_observations = GetInt64(transport_fields, "HandlerDecodedRecords").value_or(0);
    const auto summary = MakeJsonObject({
        {"CompletedAtUtc", UtcNow()}, {"Status", success ?
            (warnings ? "CompletedWithWarnings" : "Completed") : "Failed"},
        {"AnalysisMode", std::string(mode)}, {"AnalysisRunId", std::string(analysis_run_id)},
        {"CaptureRecords", std::to_string(statistics.frames)},
        {"TransportChunks", std::to_string(transport_chunks)}, {"ProtocolFrames", "0"},
        {"DecodedMessages", std::to_string(decoded_messages)},
        {"HandlerObservations", std::to_string(handler_observations)},
        {"RawEtlTotalEvents", std::to_string(raw_etl_total_events)},
        {"RawEtlPacketEvents", std::to_string(raw_etl_packet_events)},
        {"VerifiedGameplayClassifications", std::to_string(verified)},
        {"CandidateFrames", std::to_string(candidate_frames)},
        {"OpcodeSemanticCandidates", std::to_string(semantic_candidates)},
        {"HighConfidenceOpcodeSemanticCandidates", std::to_string(high_confidence_candidates)},
        {"ClassificationStatus", classification_status},
        {"Movement", std::to_string(statistics.movement)},
        {"Mount", std::to_string(statistics.mount)}, {"Quest", std::to_string(statistics.quest)},
        {"Skill", std::to_string(statistics.skill)}, {"Consumable", std::to_string(statistics.consumable)},
        {"Entity", std::to_string(statistics.entity)}, {"CharacterLifecycle", std::to_string(statistics.character_lifecycle)},
        {"Party", std::to_string(statistics.party)}, {"Economy", std::to_string(statistics.economy)},
        {"Progression", std::to_string(statistics.progression)}, {"Formula", std::to_string(statistics.formula)},
        {"ProtocolDecoded", std::to_string(statistics.protocol_decoded)},
        {"Unknown", std::to_string(statistics.unknown)}
    }, {"CaptureRecords","TransportChunks","ProtocolFrames","DecodedMessages","HandlerObservations","RawEtlTotalEvents","RawEtlPacketEvents","VerifiedGameplayClassifications","CandidateFrames","OpcodeSemanticCandidates","HighConfidenceOpcodeSemanticCandidates","Movement","Mount","Quest","Skill","Consumable","Entity","CharacterLifecycle",
        "Party","Economy","Progression","Formula","ProtocolDecoded","Unknown"});
    if (!WriteUtf8FileAtomic(session_path / L"session-summary.json", summary + "\n")) {
        if (error) *error = "could not write session summary";
        return false;
    }
    return true;
}

bool WriteInjectedTransportEvidenceReport(const fs::path& session_path,
                                          const fs::path& injected_packets,
                                          std::string* error) {
    std::ifstream stream(injected_packets, std::ios::binary);
    if (!stream) {
        if (error) *error = "cannot open injected transport evidence";
        return false;
    }
    InjectedTransportEvidenceSummary summary;
    std::string line;
    while (std::getline(stream, line)) {
        if (line.empty()) continue;
        Fields fields;
        std::string parse_error;
        if (!ParseFlatJson(line, fields, &parse_error)) {
            if (error) *error = "cannot parse injected transport evidence: " + parse_error;
            return false;
        }
        ++summary.records;
        const auto direction = GetString(fields, "PacketDirection", GetString(fields, "direction"));
        if (direction == "ClientToServer") ++summary.client_to_server;
        else if (direction == "ServerToClient") ++summary.server_to_client;
        const auto api = GetString(fields, "Api");
        if (api == "send" || api == "WSASend") ++summary.send_calls;
        else if (api == "recv" || api == "WSARecv") ++summary.recv_calls;
        else if (api == "PostDecrypt") ++summary.post_decrypt;
        else if (api == "PreEncrypt") ++summary.pre_encrypt;
        else if (api == "HandlerDecoded") ++summary.handler_decoded;
        if (GetString(fields, "frame208") == "true") ++summary.frame208_candidates;
        summary.payload_bytes += GetString(fields, "PayloadHex").size() / 2;
    }
    std::uint64_t file_bytes = 0;
    std::error_code size_error;
    if (fs::exists(injected_packets, size_error)) file_bytes = fs::file_size(injected_packets, size_error);
    const auto report = MakeJsonObject({
        {"Status", summary.records == 0 ? "NotObserved" : "Observed"},
        {"EvidenceLevel", summary.records == 0 ? "NotObserved" : "Transmitted"},
        {"Reason", "x86 DLL enhanced capture produced transport plus guarded post-decrypt, pre-encrypt, and handler-level evidence"},
        {"Decision", "Plaintext records enter protocol handlers; unresolved meanings remain Candidate and are blocked from authoritative runtime mutation"},
        {"TargetExecutable", "God2_opt.exe"},
        {"TargetArchitecture", "x86"},
        {"CaptureSource", "OptInX86Dll"},
        {"Transport", "InjectedWinsock"},
        {"Records", std::to_string(summary.records)},
        {"ClientToServer", std::to_string(summary.client_to_server)},
        {"ServerToClient", std::to_string(summary.server_to_client)},
        {"SendCalls", std::to_string(summary.send_calls)},
        {"RecvCalls", std::to_string(summary.recv_calls)},
        {"PostDecryptRecords", std::to_string(summary.post_decrypt)},
        {"PreEncryptRecords", std::to_string(summary.pre_encrypt)},
        {"HandlerDecodedRecords", std::to_string(summary.handler_decoded)},
        {"Frame208Candidates", std::to_string(summary.frame208_candidates)},
        {"PayloadBytes", std::to_string(summary.payload_bytes)},
        {"InjectedPacketsPath", WideToUtf8(injected_packets.wstring())},
        {"InjectedPacketsBytes", std::to_string(file_bytes)}
    }, {"Records","ClientToServer","ServerToClient","SendCalls","RecvCalls","PostDecryptRecords","PreEncryptRecords","HandlerDecodedRecords","Frame208Candidates","PayloadBytes","InjectedPacketsBytes"});
    if (!WriteUtf8FileAtomic(session_path / L"reports" / L"injected-transport-evidence.json", report + "\n")) {
        if (error) *error = "could not write injected transport evidence report";
        return false;
    }
    return true;
}

std::wstring QuoteArgument(std::wstring_view value) {
    std::wstring result = L"\"";
    std::size_t slashes = 0;
    for (const wchar_t c : value) {
        if (c == L'\\') ++slashes;
        else if (c == L'"') {
            result.append(slashes * 2 + 1, L'\\');
            result.push_back(L'"');
            slashes = 0;
        } else {
            result.append(slashes, L'\\');
            result.push_back(c);
            slashes = 0;
        }
    }
    result.append(slashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}

bool IsLiteralIpAddress(std::wstring value) {
    if (value.empty()) return false;
    const auto scope = value.find(L'%');
    if (scope != std::wstring::npos) {
        if (scope == 0 || scope + 1 >= value.size() || value.find(L'%', scope + 1) != std::wstring::npos) return false;
        if (!std::all_of(value.begin() + static_cast<std::ptrdiff_t>(scope + 1), value.end(),
                         [](wchar_t c) { return c >= L'0' && c <= L'9'; })) return false;
        value.resize(scope);
    }
    IN_ADDR ipv4{};
    IN6_ADDR ipv6{};
    return InetPtonW(AF_INET, value.c_str(), &ipv4) == 1 || InetPtonW(AF_INET6, value.c_str(), &ipv6) == 1;
}

std::wstring ElevatedParameters(const Arguments& arguments) {
    std::wstring result;
    for (const auto& value : arguments.values) {
        if (value == L"--elevated") continue;
        if (!result.empty()) result.push_back(L' ');
        result += QuoteArgument(value);
    }
    result += L" --elevated";
    return result;
}

fs::path ActiveStatePath() {
    const auto root = LocalDataRoot();
    return root.empty() ? fs::path{} : root / L"active-capture.json";
}

fs::path CreateSessionPath() {
    const auto root = DefaultSessionsRoot();
    return root.empty() ? fs::path{} : root / Utf8ToWide("session-" + NewId());
}

std::optional<Fields> ActiveState(std::string* error) {
    const auto path = ActiveStatePath();
    if (path.empty()) {
        if (error) *error = "Windows LocalAppData could not be resolved";
        return std::nullopt;
    }
    const auto content = ReadUtf8File(path);
    if (!content) {
        if (error) *error = "no active capture state";
        return std::nullopt;
    }
    Fields fields;
    if (!ParseFlatJson(*content, fields, error)) return std::nullopt;
    return fields;
}

std::unique_ptr<ICaptureBackend> BackendByName(std::string_view name) {
    if (name == "PktMonCaptureBackend") return std::make_unique<PktMonCaptureBackend>();
    if (name == "EtwNetworkTraceBackend") return std::make_unique<EtwNetworkTraceBackend>();
    return nullptr;
}

bool IsInsideGameClient(const fs::path& target) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    bool inside = false;
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szExeFile, L"God2_opt.exe") != 0 && _wcsicmp(entry.szExeFile, L"GameClient.exe") != 0) continue;
            HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID);
            if (process == nullptr) continue;
            std::vector<wchar_t> buffer(32'768);
            DWORD length = static_cast<DWORD>(buffer.size());
            if (QueryFullProcessImageNameW(process, 0, buffer.data(), &length)) {
                const auto game_directory = fs::path(std::wstring(buffer.data(), length)).parent_path();
                const auto relative = target.lexically_normal().wstring();
                const auto prefix = game_directory.lexically_normal().wstring();
                if (relative.size() >= prefix.size() && _wcsnicmp(relative.c_str(), prefix.c_str(), prefix.size()) == 0) inside = true;
            }
            CloseHandle(process);
        } while (!inside && Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return inside;
}

std::string SelectionJson(const BackendSelection& selection) {
    std::ostringstream output;
    output << "{\"SelectedBackend\":\"" << JsonEscape(selection.capabilities.capture_backend_name)
           << "\",\"SelectedAnalysisMode\":\"" << ToString(selection.capabilities.analysis_mode)
           << "\",\"Backends\":[";
    for (std::size_t i = 0; i < selection.all_capabilities.size(); ++i) {
        if (i != 0) output << ',';
        output << CapabilitiesJson(selection.all_capabilities[i]);
    }
    output << "]}";
    return output.str();
}

void Usage() {
    Print(
        "God2 Semantic Recovery Engine " GOD2_TOOL_DISPLAY_VERSION "\n"
        "\n"
        "Launch God2SemanticRecoveryEngine.exe without arguments and use the GUI.\n"
        "Capture, passive semantic recognition, live analysis, world-entity observation,\n"
        "session finalization and evidence export are automated by the GUI.\n"
        "No scene selector or manual gameplay marker is required."
    );
}

int ProbeCommand() {
    auto selection = SelectCaptureBackend();
    Print(SelectionJson(selection));
    return selection.backend ? 0 : kExitUnavailable;
}

int CaptureStart(const Arguments& arguments) {
    if (!IsAdministrator()) {
        const int code = RelaunchElevated(ElevatedParameters(arguments));
        if (code == ERROR_CANCELLED) Print("UAC elevation was cancelled; no capture was started", true);
        return code;
    }
    std::string error;
    const auto active_state_path = ActiveStatePath();
    if (active_state_path.empty()) {
        Print("Windows LocalAppData could not be resolved; capture did not start", true);
        return ERROR_PATH_NOT_FOUND;
    }
    if (fs::exists(active_state_path)) {
        Print("A capture is already active. Stop it before starting another.", true);
        return ERROR_BUSY;
    }
    const fs::path session_path = CreateSessionPath();
    if (session_path.empty()) {
        Print("Windows LocalAppData could not be resolved; capture did not start", true);
        return ERROR_PATH_NOT_FOUND;
    }
    if (IsInsideGameClient(session_path)) {
        Print("Refusing to write capture data inside the GameClient directory", true);
        return ERROR_ACCESS_DENIED;
    }
    SessionStore store(session_path);
    if (!store.Initialize(&error)) { Print(error, true); return kExitSoftware; }
    CaptureRequest request;
    request.session_path = session_path;
    request.endpoint_filter = arguments.Value(L"--endpoint") ? WideToUtf8(*arguments.Value(L"--endpoint")) : "";
    if (!request.endpoint_filter.empty()) {
        if (!IsLiteralIpAddress(Utf8ToWide(request.endpoint_filter))) {
            Print("--endpoint must be a literal IPv4 or IPv6 address without extra options", true);
            return kExitUsage;
        }
    }
    if (const auto pid = arguments.Value(L"--pid")) {
        try {
            std::size_t consumed = 0;
            const auto parsed = std::stoul(*pid, &consumed);
            if (consumed != pid->size() || parsed == 0) throw std::invalid_argument("pid");
            request.process_id = static_cast<DWORD>(parsed);
        } catch (...) {
            Print("--pid must be an unsigned integer", true); return kExitUsage;
        }
    }
    auto selection = SelectCaptureBackend(!request.endpoint_filter.empty(), request.process_id != 0);
    if (!store.WriteCompatibilityReports(selection, &error)) { Print(error, true); return kExitSoftware; }
    if (!selection.backend) {
        Print("No capture backend passed all required capability probes" +
              std::string(request.endpoint_filter.empty() ? "" : "; endpoint filtering is required") +
              std::string(request.process_id == 0 ? "" : "; verified process filtering is required"), true);
        return kExitUnavailable;
    }
    const auto result = selection.backend->Start(request);
    if (!result.success) {
        Print("Capture start failed: " + result.message, true);
        return static_cast<int>(result.exit_code == 0 ? ERROR_GEN_FAILURE : result.exit_code);
    }
    std::optional<CaptureWorkerIdentity> capture_worker;
    HANDLE capture_cancel_event = nullptr;
    const std::wstring capture_cancel_event_name = L"Local\\God2PacketCapture.CliConsumerCancel." +
        Utf8ToWide(NewId());
    const bool worker_required =
        (selection.backend->Name() == "EtwNetworkTraceBackend" && selection.capabilities.supports_realtime_events) ||
        (selection.backend->Name() == "PktMonCaptureBackend" && selection.capabilities.supports_drop_statistics);
    if (selection.backend->Name() == "EtwNetworkTraceBackend" && worker_required) {
        capture_cancel_event = CreateEventW(nullptr, TRUE, FALSE, capture_cancel_event_name.c_str());
        if (capture_cancel_event == nullptr) {
            selection.backend->Stop(request);
            Print("ETW trace was rolled back because the consumer cancel event could not be created", true);
            return kExitSoftware;
        }
        capture_worker = StartEtwConsumerProcess(session_path, capture_cancel_event,
                                                 capture_cancel_event_name, &error);
        CloseHandle(capture_cancel_event);
        capture_cancel_event = nullptr;
        if (!capture_worker) {
            selection.backend->Stop(request);
            Print("ETW trace was rolled back because the near-realtime event consumer could not start: " + error, true);
            return kExitSoftware;
        }
    } else if (selection.backend->Name() == "PktMonCaptureBackend" && worker_required) {
        capture_worker = StartPktMonCounterProcess(session_path, &error);
        if (!capture_worker) {
            selection.backend->Stop(request);
            Print("PktMon capture was rolled back because the near-realtime counter consumer could not start: " + error, true);
            return kExitSoftware;
        }
    }
    if (worker_required && !capture_worker) {
        selection.backend->Stop(request);
        Print("Capture was rolled back because no supervised worker was created", true);
        return kExitSoftware;
    }
    const auto state = MakeJsonObject({
        {"SessionPath", WideToUtf8(session_path.wstring())}, {"SessionId", store.SessionId()},
        {"BackendName", selection.backend->Name()}, {"AnalysisMode", ToString(selection.capabilities.analysis_mode)},
        {"StartedAtUtc", UtcNow()}, {"ProcessId", std::to_string(request.process_id)},
        {"EndpointFilter", request.endpoint_filter},
        {"CaptureWorkerEnabled", capture_worker ? "true" : "false"},
        {"CaptureWorkerProcessId", std::to_string(capture_worker ? capture_worker->process_id : 0)},
        {"CaptureWorkerCreationTime", std::to_string(capture_worker ? capture_worker->creation_time : 0)},
        {"CaptureWorkerImagePath", capture_worker ? WideToUtf8(capture_worker->image_path.wstring()) : ""},
        {"CaptureWorkerNonce", capture_worker ? capture_worker->nonce : ""},
        {"CaptureWorkerStatusPath", capture_worker ? WideToUtf8(capture_worker->status_path.wstring()) : ""},
        {"CaptureWorkerType", capture_worker ? capture_worker->worker_type : ""}
    });
    if (!WriteUtf8FileAtomic(active_state_path, state + "\n")) {
        selection.backend->Stop(request);
        if (capture_worker) WaitForCaptureWorker(*capture_worker, 30'000, nullptr);
        Print("Capture was rolled back because active state could not be saved", true);
        return ERROR_WRITE_FAULT;
    }
    Print("CAPTURE STARTED");
    Print("Session=" + WideToUtf8(session_path.wstring()));
    Print("CaptureBackendName=" + selection.backend->Name());
    Print("AnalysisMode=" + ToString(selection.capabilities.analysis_mode));
    return 0;
}

int CaptureStop(const Arguments& arguments) {
    if (!IsAdministrator()) {
        const int code = RelaunchElevated(ElevatedParameters(arguments));
        if (code == ERROR_CANCELLED) Print("UAC elevation was cancelled; active capture state was preserved", true);
        return code;
    }
    std::string error;
    const auto state = ActiveState(&error);
    if (!state) { Print(error, true); return ERROR_NOT_FOUND; }
    const fs::path session_path = Utf8ToWide(GetString(*state, "SessionPath"));
    if (!IsApprovedSessionPath(session_path)) {
        Print("Active capture session path is outside the approved local data root; no command was executed", true);
        return ERROR_ACCESS_DENIED;
    }
    auto backend = BackendByName(GetString(*state, "BackendName"));
    if (!backend) { Print("Active capture references an unknown backend; state was preserved", true); return kExitSoftware; }
    CaptureRequest request;
    request.session_path = session_path;
    request.endpoint_filter = GetString(*state, "EndpointFilter");
    request.process_id = static_cast<DWORD>(GetInt64(*state, "ProcessId").value_or(0));
    const auto stop = backend->Stop(request);
    if (!stop.success) {
        Print("Capture stop failed; active state was preserved: " + stop.message, true);
        return static_cast<int>(stop.exit_code == 0 ? ERROR_GEN_FAILURE : stop.exit_code);
    }
    std::string consumer_warning;
    CaptureWorkerIdentity consumer_identity;
    consumer_identity.process_id = static_cast<DWORD>(GetInt64(*state, "CaptureWorkerProcessId").value_or(0));
    consumer_identity.creation_time = static_cast<std::uint64_t>(GetInt64(*state, "CaptureWorkerCreationTime").value_or(0));
    consumer_identity.image_path = ExecutablePath();
    consumer_identity.status_path = Utf8ToWide(GetString(*state, "CaptureWorkerStatusPath"));
    if (consumer_identity.status_path.empty())
        consumer_identity.status_path = session_path / L"raw" / L"capture-worker-status-etw-consumer.json";
    consumer_identity.nonce = GetString(*state, "CaptureWorkerNonce");
    consumer_identity.worker_type = GetString(*state, "CaptureWorkerType");
    const bool worker_expected = GetBool(*state, "CaptureWorkerEnabled").value_or(consumer_identity.process_id != 0);
    const bool consumer_ok = !worker_expected || WaitForCaptureWorker(consumer_identity, 30'000, &consumer_warning);
    std::error_code state_error;
    fs::remove(ActiveStatePath(), state_error);
    WriteUtf8File(session_path / L"capture-stopped-state.json", MakeJsonObject({
        {"StoppedAtUtc", UtcNow()}, {"BackendName", backend->Name()}, {"StopResult", stop.message},
        {"CaptureWorkerExit", consumer_ok ? "Clean" : consumer_warning}
    }) + "\n");

    SessionStore store(session_path);
    if (!store.Initialize(&error) || !store.BeginAnalysis("CaptureStop", &error)) {
        Print("Capture stopped, but analysis initialization failed: " + error, true);
        return kExitSoftware;
    }
    GameplayAnalysisEngine engine(store);
    PcapngExtractionResult extraction;
    std::vector<fs::path> pcapng_files;
    std::error_code enumerate_error;
    for (const auto& entry : fs::directory_iterator(session_path / L"raw", enumerate_error)) {
        if (entry.is_regular_file() && entry.path().extension() == L".pcapng") pcapng_files.push_back(entry.path());
    }
    std::sort(pcapng_files.begin(), pcapng_files.end());
    if (!pcapng_files.empty()) {
        extraction.success = true;
        for (const auto& pcapng : pcapng_files) {
            const auto part = ExtractPcapngPayloads(pcapng, engine, &error);
            if (!part.success) { extraction = part; break; }
            extraction.packet_count += part.packet_count;
            extraction.payload_bytes += part.payload_bytes;
            extraction.unknown_protocol_packets += part.unknown_protocol_packets;
        }
    } else {
        extraction.success = true;
        extraction.message = "No PCAPNG capability; raw ETL preserved for near-realtime segmented/offline analysis";
        const auto etw_events = session_path / L"raw" / L"etw-events.jsonl";
        if (fs::exists(etw_events)) {
            extraction.success = engine.AnalyzeJsonl(etw_events, true, &error);
            extraction.packet_count = engine.Statistics().frames;
            extraction.unknown_protocol_packets = engine.Statistics().unknown;
        }
    }
    const bool analysis_ok = extraction.success && store.FinishAnalysis(extraction.success ? "Completed" : "Failed", &error) &&
                             store.ExportAll(&error);
    if (!analysis_ok) {
        Print("Capture stopped and raw evidence was preserved, but analysis/export failed: " + error, true);
        return kExitSoftware;
    }

    std::uint64_t captured_bytes = 0;
    std::error_code file_error;
    for (const auto& entry : fs::directory_iterator(session_path / L"raw", file_error)) {
        if (entry.is_regular_file() && (entry.path().extension() == L".etl" || entry.path().extension() == L".pcapng")) {
            captured_bytes += entry.file_size(file_error);
        }
    }
    std::string pcap_validation = "NOT_AVAILABLE";
    if (!pcapng_files.empty()) {
        pcap_validation = "PASS";
        for (const auto& pcapng : pcapng_files) {
            std::string reason;
            if (!ValidatePcapng(pcapng, &reason)) { pcap_validation = "FAIL:" + reason; break; }
        }
    }
    if (const auto conversion_error = ReadUtf8File(session_path / L"compatibility" / L"pcapng-conversion-error.txt")) {
        pcap_validation = "FAIL:" + *conversion_error;
    }
    const auto os = DetectOsVersion();
    const auto report = MakeJsonObject({
        {"WindowsEdition", os.edition}, {"ServicePack", "SP" + std::to_string(os.service_pack_major)},
        {"BuildNumber", std::to_string(os.build)}, {"Architecture", os.architecture},
        {"InstalledCaptureBackend", backend->Name()}, {"AnalysisMode", GetString(*state, "AnalysisMode")},
        {"CapturedPacketCount", std::to_string(extraction.packet_count)},
        {"CapturedBytes", std::to_string(captured_bytes)}, {"PcapngValidation", pcap_validation},
        {"ProcessExitCode", consumer_ok ? "0" : std::to_string(kExitSoftware)},
        {"UnknownOpcodeCount", std::to_string(extraction.unknown_protocol_packets)},
        {"TestStatus", consumer_ok ? "CURRENT_HOST_CAPTURE_COMPLETED" : "CAPTURE_COMPLETED_WITH_WORKER_FAILURE"}
    });
    WriteUtf8File(session_path / L"compatibility/windows-compatibility-report.json", report + "\n");
    store.Db().Insert("os_compatibility_results", {
        {"session_id", store.SessionId()}, {"observed_at_utc", UtcNow()}, {"windows_edition", os.edition},
        {"service_pack", "SP" + std::to_string(os.service_pack_major)}, {"build_number", std::to_string(os.build)},
        {"architecture", os.architecture}, {"installed_capture_backend", backend->Name()},
        {"analysis_mode", GetString(*state, "AnalysisMode")}, {"captured_packet_count", std::to_string(extraction.packet_count)},
        {"captured_bytes", std::to_string(captured_bytes)}, {"pcapng_validation", pcap_validation},
        {"process_exit_code", consumer_ok ? "0" : std::to_string(kExitSoftware)},
        {"test_status", consumer_ok ? "CURRENT_HOST_CAPTURE_COMPLETED" : "CAPTURE_COMPLETED_WITH_WORKER_FAILURE"}
    });
    Print("CAPTURE STOPPED");
    Print("Session=" + WideToUtf8(session_path.wstring()));
    Print("CapturedPacketCount=" + std::to_string(extraction.packet_count));
    Print("CapturedBytes=" + std::to_string(captured_bytes));
    Print("UnknownOpcodeCount=" + std::to_string(extraction.unknown_protocol_packets));
    Print("PcapngValidation=" + pcap_validation);
    if (!consumer_warning.empty()) Print("ConsumerWarning=" + consumer_warning);
    return consumer_ok ? 0 : kExitSoftware;
}

int CaptureStatus() {
    std::string error;
    const auto state = ActiveState(&error);
    if (!state) {
        Print("{\"Active\":false}");
        return 0;
    }
    std::vector<std::pair<std::string, std::string>> fields(state->begin(), state->end());
    fields.emplace_back("Active", "true");
    Print(MakeJsonObject(fields, {"Active"}));
    return 0;
}

int MarkerCommand(const Arguments& arguments) {
    if (arguments.values.size() < 2) { Print("marker name is required", true); return kExitUsage; }
    const auto marker = WideToUtf8(arguments.values[1]);
    if (!IsCoverageMarker(marker)) { Print("unknown coverage marker: " + marker, true); return kExitUsage; }
    fs::path session_path;
    if (const auto explicit_session = arguments.Value(L"--session")) session_path = *explicit_session;
    else {
        std::string error;
        const auto state = ActiveState(&error);
        if (!state) { Print("marker requires an active capture or --session", true); return ERROR_NOT_FOUND; }
        session_path = Utf8ToWide(GetString(*state, "SessionPath"));
    }
    SessionStore store(session_path);
    std::string error;
    if (!store.Initialize(&error)) { Print(error, true); return kExitSoftware; }
    const auto json = MakeJsonObject({{"MarkerId", NewId()}, {"Marker", marker}, {"MarkedAtUtc", UtcNow()},
                                      {"Purpose", "LocalTimeCorrelationOnly"}, {"GameInputSynthesized", "false"}},
                                     {"GameInputSynthesized"});
    if (!store.WriteJsonl(L"gameplay/markers.jsonl", json, &error)) { Print(error, true); return kExitSoftware; }
    Print("MARKER RECORDED: " + marker);
    return 0;
}

int ListMarkers() {
    for (const auto& marker : CoverageMarkers()) Print(marker);
    return 0;
}

int AnalyzeCommand(const Arguments& arguments, bool reanalyze) {
    const auto session_value = arguments.Value(L"--session");
    if (!session_value) { Print("--session is required", true); return kExitUsage; }
    const fs::path session_path = *session_value;
    const fs::path input = arguments.Value(L"--input") ? fs::path(*arguments.Value(L"--input")) : fs::path{};
    if (!reanalyze && input.empty()) { Print("--input is required for ingest", true); return kExitUsage; }
    SessionStore store(session_path);
    std::string error;
    if (!store.Initialize(&error) || !store.BeginAnalysis(reanalyze ? "OfflineReanalysis" : "JsonlIngest", &error)) {
        Print(error, true); return kExitSoftware;
    }
    GameplayAnalysisEngine engine(store);
    bool ok = true;
    bool analyzed_retained_evidence = false;
    if (reanalyze) {
        const auto raw_etl = session_path / L"raw" / L"capture.etl";
        if (fs::exists(raw_etl)) {
            std::string audit_error;
            const auto audit = AuditEtwFile(raw_etl,
                session_path / L"reports" / L"raw-etl-inventory.json", &audit_error);
            analyzed_retained_evidence = audit.success;
            if (!audit.success) Print("ETL AUDIT WARNING: " + audit_error, true);
        }
        const auto injected_packets = session_path / L"raw" / L"injected-packets.jsonl";
        if (ok && fs::exists(injected_packets)) {
            ok = WriteInjectedTransportEvidenceReport(session_path, injected_packets, &error) &&
                AnalyzeInjectedGod2FrameCandidates(session_path, injected_packets, &error).success &&
                engine.AnalyzeJsonl(injected_packets, true, &error);
            analyzed_retained_evidence = ok;
        }
        std::vector<fs::path> pcapng_files;
        std::error_code enumerate_error;
        for (const auto& entry : fs::directory_iterator(session_path / L"raw", enumerate_error)) {
            if (entry.is_regular_file() && entry.path().extension() == L".pcapng") pcapng_files.push_back(entry.path());
        }
        if (enumerate_error) {
            error = "cannot enumerate retained raw evidence: " + enumerate_error.message();
            ok = false;
        } else if (!pcapng_files.empty()) {
            std::sort(pcapng_files.begin(), pcapng_files.end());
            for (const auto& pcapng : pcapng_files) {
                if (!ExtractPcapngPayloads(pcapng, engine, &error, false).success) {
                    ok = false;
                    break;
                }
            }
        }
        const auto canonical_frames = session_path / L"raw" / L"frames.jsonl";
        // frames.jsonl is a canonical derivative of injected-packets.jsonl for
        // enhanced sessions.  Replaying both would duplicate every observation
        // and inflate gameplay/formula evidence on each offline reanalysis.
        if (ok && !analyzed_retained_evidence && fs::exists(canonical_frames)) {
            ok = engine.AnalyzeJsonl(canonical_frames, false, &error);
            analyzed_retained_evidence = analyzed_retained_evidence || ok;
        } else if (ok && pcapng_files.empty() && !analyzed_retained_evidence) {
            error = "no retained raw evidence is available";
            ok = false;
        }
    } else {
        ok = engine.AnalyzeJsonl(input, true, &error);
    }
    store.FinishAnalysis(ok ? "Completed" : "Failed", nullptr);
    if (!ok || !store.ExportAll(&error) ||
        !WriteAnalysisSummary(session_path, engine.Statistics(), ok, store.AnalysisRunId(),
                              reanalyze ? "OfflineReanalysis" : "JsonlIngest", &error)) {
        Print(error, true);
        return kExitSoftware;
    }
    const auto& stats = engine.Statistics();
    Print(MakeJsonObject({
        {"AnalysisRunId", store.AnalysisRunId()}, {"CaptureRecordsAnalyzed", std::to_string(stats.frames)},
        {"Movement", std::to_string(stats.movement)}, {"Mount", std::to_string(stats.mount)},
        {"Quest", std::to_string(stats.quest)}, {"Skill", std::to_string(stats.skill)},
        {"Consumable", std::to_string(stats.consumable)}, {"Unknown", std::to_string(stats.unknown)}
    }, {"CaptureRecordsAnalyzed","Movement","Mount","Quest","Skill","Consumable","Unknown"}));
    return 0;
}

int ExportCommand(const Arguments& arguments) {
    const auto session_value = arguments.Value(L"--session");
    if (!session_value) { Print("--session is required", true); return kExitUsage; }
    SessionStore store(*session_value);
    std::string error;
    if (!store.Initialize(&error) || !store.ExportAll(&error)) { Print(error, true); return kExitSoftware; }
    Print("EXPORT COMPLETED");
    return 0;
}

std::uint64_t g_selftest_check_count = 0;

bool Check(bool condition, std::string_view name, std::vector<std::string>& failures) {
    ++g_selftest_check_count;
    if (!condition) failures.emplace_back(name);
    return condition;
}

std::string Event(std::initializer_list<std::pair<std::string, std::string>> fields) {
    return MakeJsonObject(std::vector<std::pair<std::string, std::string>>(fields));
}

int SelfTestCommand() {
    g_selftest_check_count = 0;
    const auto local_data_root = LocalDataRoot();
    if (local_data_root.empty()) { Print("Windows LocalAppData could not be resolved", true); return ERROR_PATH_NOT_FOUND; }
    const fs::path session_path = local_data_root / L"SelfTests" / L"繁體 中文 简体 中文 spaces" /
                                  Utf8ToWide("selftest-" + NewId());
    SessionStore store(session_path);
    std::string error;
    if (!store.Initialize(&error) || !store.BeginAnalysis("SelfTest", &error)) { Print(error, true); return kExitSoftware; }
    GameplayAnalysisEngine engine(store);
    std::vector<std::string> failures;

    Check(RunAutomaticSemanticRecognitionTests() == 0,
          "passive automatic semantic recognition rules", failures);

    Check(!BackendPolicyAllowsPktMon(6, 1), "Windows 7 policy prohibits pktmon", failures);
    Check(!BackendPolicyAllowsPktMon(6, 2), "Windows 8 policy prohibits pktmon", failures);
    Check(!BackendPolicyAllowsPktMon(6, 3), "Windows 8.1 policy prohibits pktmon", failures);
    Check(BackendPolicyAllowsPktMon(10, 0), "Windows 10 policy permits capability probe", failures);
    Check(IsTraceStopFinalizationProgress("Merging traces ... done\r\nGenerating data collection ..."),
          "netsh trace finalization progress is nonfatal with retained ETL", failures);
    Check(!IsTraceStopFinalizationProgress("Access is denied."),
          "netsh access denied is not finalization progress", failures);
    Check(IsLiteralIpAddress(L"127.0.0.1") && IsLiteralIpAddress(L"2001:db8::1") &&
          IsLiteralIpAddress(L"fe80::1%12") && !IsLiteralIpAddress(L"fe80::1%12 --capture") &&
          !IsLiteralIpAddress(L"999.1.1.1"), "literal endpoint validation", failures);
    std::array<wchar_t, MAX_PATH> system_directory{};
    const auto system_length = GetSystemDirectoryW(system_directory.data(), static_cast<UINT>(system_directory.size()));
    const auto system_netsh = FindSystemExecutable(L"netsh.exe");
    Check(system_length != 0 && system_length < system_directory.size() && system_netsh &&
          fs::weakly_canonical(system_netsh->parent_path()) ==
              fs::weakly_canonical(fs::path(std::wstring(system_directory.data(), system_length))),
          "system executables resolve only from System32", failures);
    const auto detected_os = DetectOsVersion();
    Check(detected_os.edition.find("Windows") != std::string::npos && detected_os.architecture == "x64",
          "Windows edition and native architecture detection", failures);
    Check(LocalDataRoot().is_absolute() &&
          fs::weakly_canonical(LocalDataRoot()) != fs::weakly_canonical(ExecutablePath().parent_path()),
          "capture data root is absolute and outside executable directory", failures);
    fs::path long_atomic_path = session_path / L"compatibility";
    for (int segment = 0; segment < 4; ++segment) {
        long_atomic_path /= Utf8ToWide("long-path-segment-" + std::string(48, static_cast<char>('a' + segment)));
    }
    long_atomic_path /= L"atomic-report.json";
    Check(WriteUtf8FileAtomic(long_atomic_path, "{\"Value\":\"first\"}\n") &&
          WriteUtf8FileAtomic(long_atomic_path, "{\"Value\":\"second\"}\n") &&
          ReadUtf8File(long_atomic_path).value_or("").find("\"second\"") != std::string::npos,
          "long path atomic replacement", failures);
    Fields strict_json;
    std::string strict_json_error;
    Check(ParseFlatJson("{\"Status\":\"PASS\",\"Count\":2,\"Nested\":{\"Ok\":true}}",
                        strict_json, &strict_json_error) &&
          GetString(strict_json, "Status") == "PASS" &&
          GetInt64(strict_json, "Count").value_or(-1) == 2,
          "strict flat JSON accepts a complete valid document", failures);
    Check(!ParseFlatJson("{\"Status\":\"PASS\",\"Status\":\"FAIL\"}", strict_json, nullptr),
          "strict flat JSON rejects duplicate keys", failures);
    Check(!ParseFlatJson("{\"Status\":\"PASS\"} trailing", strict_json, nullptr),
          "strict flat JSON rejects trailing content", failures);
    Check(!ParseFlatJson("{\"Status\":PASS}", strict_json, nullptr),
          "strict flat JSON rejects arbitrary unquoted tokens", failures);
    Check(!ParseFlatJson("{\"Nested\":{broken}}", strict_json, nullptr),
          "strict flat JSON rejects malformed nested values", failures);
    Check(!ParseFlatJson("{\"Value\":\"line\nfeed\"}", strict_json, nullptr),
          "strict flat JSON rejects unescaped control characters", failures);
    Check(!ParseFlatJson("{\"Status\":\"PASS\",}", strict_json, nullptr),
          "strict flat JSON rejects a root trailing comma", failures);
    Check(!ParseFlatJson("{\"Status\":\"PASS\"}\v", strict_json, nullptr),
          "strict flat JSON rejects non-JSON whitespace", failures);
    Check(UltimateLiveElevationPolicy(true, false) ==
              UltimateLiveElevationDecision::RunHere &&
          UltimateLiveElevationPolicy(false, false) ==
              UltimateLiveElevationDecision::Elevate &&
          UltimateLiveElevationPolicy(false, true) ==
              UltimateLiveElevationDecision::BlockNoUac,
          "Ultimate live elevation and no-UAC automation policy", failures);
    const auto live_security = RunLiveSecuritySelfTests(session_path / L"live-security-fixtures");
    Check(live_security.crafted_newer_ignored,
          "live validation ignores a crafted newer capture directory", failures);
    Check(live_security.reparse_rejected,
          "live validation rejects a crafted reparse path", failures);
    Check(live_security.zip_self_report_rejected,
          "live validation rejects ZIP self-report without matching inventory", failures);
    Check(live_security.zip_tamper_rejected,
          "live validation rejects recovery ZIP content tamper", failures);
    Check(live_security.file_replacement_blocked,
          "live validation pinned file blocks same-user replacement", failures);
    Check(live_security.handle_identity_revalidated,
          "live validation rechecks pinned handle identity after validation", failures);
    Check(DeepProbeCandidateMapValidatorSelfTest(),
          "versioned deep-probe candidate map remains blocked until typed causal verification",
          failures);
    Check(EnhancedStoppedStatusV2ValidatorSelfTest(),
          "live stopped authority requires the exact typed enhanced-status v2 contract",
          failures);
    CaptureWorkerIdentity mismatched_worker;
    mismatched_worker.process_id = GetCurrentProcessId();
    mismatched_worker.creation_time = 1;
    mismatched_worker.image_path = ExecutablePath();
    mismatched_worker.status_path = session_path / L"raw" / L"nonexistent-worker-status.json";
    mismatched_worker.nonce = NewId();
    std::string worker_identity_error;
    Check(!WaitForCaptureWorker(mismatched_worker, 0, &worker_identity_error) && !worker_identity_error.empty(),
          "mismatched worker identity is rejected without termination", failures);
    const std::array<std::uint8_t, 10> verified_movement = {0x0A,0x00,0x80,0xB8,0xD9,0xC1,0x4B,0xA6,0x94,0x88};
    VerifiedWorldMovementFrame decoded_movement;
    Check(TryDecodeVerifiedWorldMovement(verified_movement.data(), verified_movement.size(), decoded_movement) &&
          decoded_movement.x == 18 && decoded_movement.y == 12 && decoded_movement.sequence == 1 &&
          decoded_movement.decoded_hex == "0A002E12000C0001FF72",
          "verified 0x2E movement transport decoder", failures);

    const std::vector<std::tuple<double, double, std::string>> direction_cases = {
        {0,-1,"North"},{1,-1,"NorthEast"},{1,0,"East"},{1,1,"SouthEast"},{0,1,"South"},
        {-1,1,"SouthWest"},{-1,0,"West"},{-1,-1,"NorthWest"},{0,0,"Idle"}
    };
    int frame_id = 0;
    for (const auto& [dx, dy, expected] : direction_cases) {
        Frame frame;
        frame.fields = {{"StartX","10"},{"StartY","10"},{"EndX",std::to_string(10+dx)},
                        {"EndY",std::to_string(10+dy)}};
        const auto resolved = ResolveDirection(frame);
        Check(ToString(resolved.direction) == expected, "direction " + expected, failures);
        Check(resolved.evidence == EvidenceLevel::Derived, "coordinate direction remains Derived", failures);
        const auto json = Event({{"SourceFrameId","move-" + std::to_string(++frame_id)}, {"MessageType","MovementUpdate"},
                                 {"CharacterId","100"},{"StartX","10"},{"StartY","10"},
                                 {"EndX",std::to_string(10+dx)},{"EndY",std::to_string(10+dy)},
                                 {"DirectionRaw",std::to_string(frame_id-1)},{"MovementMode","Walking"}});
        Check(engine.ProcessJsonLine(json, true, &error), "movement ingestion", failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","teleport"},{"MessageType","Teleport"},{"CharacterId","100"},
                                        {"StartX","0"},{"StartY","0"},{"EndX","999"},{"EndY","999"}}), true, &error),
          "teleport ingestion", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","server-correction"},{"MessageType","ServerPositionCorrection"},
                                        {"CharacterId","100"},{"StartX","1"},{"StartY","1"},{"EndX","2"},{"EndY","2"}}), true, &error),
          "server correction ingestion", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","raw-direction"},{"MessageType","MovementStep"},
                                        {"CharacterId","100"},{"DirectionRaw","1"},
                                        {"DirectionEncoding","EightWay0NorthClockwiseV1"},
                                        {"DirectionMappingVerified","true"}}), true, &error),
          "verified raw direction ingestion", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","candidate-raw-direction"},{"MessageType","MovementStep"},
                                        {"CharacterId","100"},{"DirectionRaw","2"},
                                        {"DirectionEncoding","EightWay0NorthClockwiseV1"}}), true, &error),
          "unverified raw direction ingestion", failures);

    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","mount-start"},{"MessageType","MountStarted"},{"CharacterId","100"},
                                        {"MountRuntimeId","500"},{"MountTemplateId","50"},{"IsMounted","true"},
                                        {"CharacterStatSpeed","88"},{"MountMovementSpeed","12.5"},{"NormalMovementSpeed","5"}}), true, &error),
          "mount start", failures);
    int mounted = 0;
    for (const auto& [dx, dy, expected] : direction_cases) {
        if (expected == "Idle") continue;
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","mounted-" + std::to_string(++mounted)},
                                            {"MessageType","MountedMovementUpdate"},{"CharacterId","100"},
                                            {"MountRuntimeId","500"},{"StartX","10"},{"StartY","10"},
                                            {"EndX",std::to_string(10+dx)},{"EndY",std::to_string(10+dy)},
                                            {"MountMovementSpeed","12.5"},{"CharacterStatSpeed","88"},
                                            {"NormalMovementSpeed","5"}}), true, &error), "mounted direction", failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","mount-stop"},{"MessageType","Dismounted"},
                                        {"CharacterId","100"},{"MountRuntimeId","500"},{"IsMounted","false"}}), true, &error),
          "dismount", failures);

    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-detail"},{"MessageType","QuestDetailsResponse"},
                                        {"QuestId","Q1"},{"QuestType","MainQuest"},{"QuestName","測試主線"},
                                        {"DefinitionStatus","Transmitted"},{"RequiredLevel","5"}}), true, &error), "quest profile", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-accept-request"},{"MessageType","QuestAcceptRequest"},
                                        {"QuestId","Q1"}}), true, &error), "quest accept request", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-accept"},{"MessageType","QuestAcceptedNotification"},
                                        {"QuestId","Q1"},{"QuestStageId","S1"},{"RelatedEntityType","NPC"},
                                        {"RelatedEntityId","N10"},{"Relationship","QuestGiver"}}), true, &error), "quest accept", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-monster-spawn"},{"MessageType","MonsterSpawn"},
                                        {"QuestId","Q1"},{"QuestStageId","S1"},{"ObjectiveId","O1"},{"MonsterId","M1"}}), true, &error),
          "quest monster spawn correlation", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-battle-start"},{"MessageType","BattleStart"},
                                        {"QuestId","Q1"},{"QuestStageId","S1"},{"ObjectiveId","O1"},{"BattleId","B1"}}), true, &error),
          "quest battle correlation", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-monster-death"},{"MessageType","MonsterDeath"},
                                        {"QuestId","Q1"},{"QuestStageId","S1"},{"ObjectiveId","O1"},{"MonsterId","M1"}}), true, &error),
          "quest monster death correlation", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-item-drop"},{"MessageType","ItemDrop"},
                                        {"QuestId","Q1"},{"QuestStageId","S1"},{"ObjectiveId","O1"},{"ItemId","I1"}}), true, &error),
          "quest item drop correlation", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-progress"},{"MessageType","QuestObjectiveUpdated"},
                                        {"QuestId","Q1"},{"QuestStageId","S1"},{"ObjectiveId","O1"},
                                        {"ObjectiveType","KillMonster;CollectItem"},{"TargetMonsterId","M1"},{"TargetItemId","I1"},
                                        {"RequiredCount","3"},{"PreviousCount","1"},{"CurrentCount","2"},
                                        {"RelatedEntityType","Monster"},{"RelatedEntityId","M1"}}), true, &error), "quest progress", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-stage-two"},{"MessageType","QuestObjectiveAdded"},
                                        {"QuestId","Q1"},{"QuestStageId","S2"},{"ObjectiveId","O2"},
                                        {"ObjectiveType","VisitLocation"},{"TargetMapId","19"},
                                        {"TargetX","28"},{"TargetY","34"},{"RelatedEntityType","Map"},{"RelatedEntityId","19"}}), true, &error),
          "multi-stage quest", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-share"},{"MessageType","QuestShared"},
                                        {"QuestId","Q1"},{"RelatedEntityType","Party"},{"RelatedEntityId","P1"}}), true, &error),
          "quest share", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-abandon"},{"MessageType","QuestAbandonResult"},
                                        {"QuestId","Q2"},{"QuestState","Abandoned"}}), true, &error), "quest abandon", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-failed"},{"MessageType","QuestFailed"},
                                        {"QuestId","Q3"},{"FailureReasonCode","TIME_EXPIRED"}}), true, &error), "quest failed", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-turn-in"},{"MessageType","QuestTurnInResult"},
                                        {"QuestId","Q1"},{"QuestState","Completed"}}), true, &error), "quest turn-in", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","quest-reward"},{"MessageType","QuestRewardGranted"},
                                        {"QuestId","Q1"},{"ExperienceReward","100"},{"ItemRewards","I1:1"},
                                        {"DefinitionStatus","NotTransmitted"}}), true, &error), "quest reward", failures);

    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-profile"},{"MessageType","SkillAvailable"},
                                        {"SkillId","S100"},{"SkillLevel","1"},{"SkillName","烈焰風暴"},
                                        {"SkillSourceType","CharacterSkill"},
                                        {"FacetIds","Active;Magical;FireElement;AreaOfEffect;Damage;Debuff;DamageOverTime"},
                                        {"Element","FireElement"},{"ActivationType","Active"},
                                        {"TargetingType","AreaOfEffect"},{"EffectTypes","MagicalDamage;Debuff"},
                                        {"DurationType","DamageOverTime"},{"CombatRole","Offensive"}}), true, &error), "multi-facet skill", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-cast"},{"MessageType","SkillCastStart"},
                                        {"SkillId","S100"},{"CasterEntityId","100"},{"TargetEntityIds","200;201"}}), true, &error),
          "skill cast", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-hit"},{"MessageType","SkillHit"},{"SkillId","S100"},
                                        {"CasterEntityId","100"},{"TargetEntityIds","200;201"}}), true, &error), "multi-target skill", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-dot"},{"MessageType","SkillDamage"},{"SkillId","S100"},
                                        {"CasterEntityId","100"},{"TargetEntityId","200"},{"EffectType","DamageOverTime"},
                                        {"TickIndex","2"},{"Amount","35"}}), true, &error), "damage over time", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-hot"},{"MessageType","SkillHealing"},{"SkillId","S100"},
                                        {"CasterEntityId","100"},{"TargetEntityId","100"},{"EffectType","HealingOverTime"},
                                        {"TickIndex","2"},{"Amount","25"}}), true, &error), "healing over time", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-interrupted"},{"MessageType","SkillCastInterrupted"},
                                        {"SkillId","S100"},{"CasterEntityId","100"},{"FailureReasonCode","INTERRUPTED"}}), true, &error),
          "skill interrupted", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-cast-request-two"},{"MessageType","SkillCastRequest"},
                                        {"SkillId","S100"},{"CasterEntityId","100"},{"TargetEntityIds","202"}}), true, &error),
          "second skill cast request", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-cast-two"},{"MessageType","SkillCastStart"},
                                        {"SkillId","S100"},{"CasterEntityId","100"},{"TargetEntityIds","202"}}), true, &error),
          "second skill cast start", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-completed-two"},{"MessageType","SkillCastCompleted"},
                                        {"SkillId","S100"},{"CasterEntityId","100"},{"TargetEntityIds","202"}}), true, &error),
          "second skill cast completed", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-rejected"},{"MessageType","SkillCastRejected"},
                                        {"SkillId","S100"},{"CasterEntityId","100"},{"FailureReasonCode","MP_INSUFFICIENT"}}), true, &error),
          "skill failure", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-cooldown-start"},{"MessageType","SkillCooldownStarted"},
                                        {"SkillId","S100"},{"CasterEntityId","100"}}), true, &error), "skill cooldown start", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-cooldown-end"},{"MessageType","SkillCooldownEnded"},
                                        {"SkillId","S100"},{"CasterEntityId","100"}}), true, &error), "skill cooldown end", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","basic-attack-profile"},{"MessageType","SkillAvailable"},
                                        {"SkillId","BA1"},{"SkillName","普通攻擊"},{"SkillSourceType","CharacterSkill"},
                                        {"FacetIds","Active;PhysicalDamage;SingleTarget;BasicAttack"}}), true, &error),
          "basic attack classification", failures);
    const std::vector<std::pair<std::string,std::string>> source_profiles = {
        {"MON1","MonsterSkill"},{"BP1","BattlePetSkill"},{"PET1","PetSkill"},
        {"IMM1","ImmortalSkill"},{"MOUNT1","MountSkill"}
    };
    for (const auto& [id, source] : source_profiles) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","source-" + id},{"MessageType","SkillAvailable"},
                                            {"SkillId",id},{"SkillName",source},{"SkillSourceType",source},
                                            {"FacetIds",source + ";Active"}}), true, &error), "skill source " + source, failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","passive-trigger"},{"MessageType","SkillCastStart"},
                                        {"SkillId","PASS1"},{"CasterEntityId","100"},{"ActivationType","Triggered"}}), true, &error),
          "passive triggered skill", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-unknown"},{"MessageType","SkillAvailable"},
                                        {"UnknownSkill","true"},{"CandidateFacets","Active;FireElement"},
                                        {"CandidateConfidence","0.3"}}), true, &error), "unknown skill", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","skill-unknown-id"},{"MessageType","SkillAvailable"},
                                        {"SkillId","UNKNOWN-ID"},{"CandidateFacets","Active;FireElement"},
                                        {"CandidateConfidence","0.2"}}), true, &error), "unknown skill with identifier", failures);

    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","item-use"},{"MessageType","ItemUseRequest"},
                                        {"CharacterId","100"},{"ItemId","HP1"},{"ItemName","回復藥"},
                                        {"ConsumableType","HpMedicine"},{"HpBefore","10"},{"HpAfter","50"},
                                        {"IsItemSkill","false"}}), true, &error), "consumable separated", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","item-use-result"},{"MessageType","ItemUseResult"},
                                        {"CharacterId","100"},{"ItemId","HP1"}}), true, &error), "consumable result", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","item-use-two"},{"MessageType","ItemUseRequest"},
                                        {"CharacterId","100"},{"ItemId","HP1"},{"ConsumableType","HpMedicine"}}), true, &error),
          "second consumable use", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","item-use-result-two"},{"MessageType","ItemUseResult"},
                                        {"CharacterId","100"},{"ItemId","HP1"}}), true, &error), "second consumable result", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","npc-snapshot"},{"MessageType","NpcSnapshot"},
                                        {"EntityType","NPC"},{"EntityId","N10"},{"TemplateId","NT10"},
                                        {"EntityName","任務使者"},{"MapId","19"},{"X","28"},{"Y","34"},
                                        {"Level","12"},{"Hp","500"},{"MaxHp","500"},{"Mp","100"},{"MaxMp","100"},
                                        {"Strength","10"},{"Stamina","20"},{"Intelligence","30"},
                                        {"CharacterStatSpeed","15"},{"Element","WoodElement"}}), true, &error),
          "NPC entity observation", failures);
    const std::vector<std::string> entity_types = {
        "Monster", "Character", "BattlePet", "Pet", "Immortal", "Mount"
    };
    for (const auto& entity_type : entity_types) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","entity-" + entity_type},
                                            {"MessageType","EntitySnapshot"},{"EntityType",entity_type},
                                            {"EntityId","E-" + entity_type},{"TemplateId","T-" + entity_type},
                                            {"MapId","19"},{"X","30"},{"Y","40"},{"Level","9"},
                                            {"Hp","90"},{"Mp","45"},{"Strength","8"},{"Stamina","7"},
                                            {"Intelligence","6"},{"CharacterStatSpeed","5"},
                                            {"Element","FireElement"}}), true, &error),
              "entity type " + entity_type, failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","character-created"},{"MessageType","CharacterCreated"},
                                        {"AccountId","A1"},{"CharacterId","100"},{"CharacterName","測試角色"},
                                        {"SlotId","1"},{"ClassId","C1"},{"Level","1"},{"Result","Success"}}), true, &error),
          "character lifecycle", failures);
    for (const auto& event_type : {"CharacterSelected", "CharacterLoggedOut", "CharacterDeleted"}) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","lifecycle-" + std::string(event_type)},
                                            {"MessageType",event_type},{"AccountId","A1"},
                                            {"CharacterId","100"},{"Result","Success"}}), true, &error),
              std::string("character lifecycle ") + event_type, failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","party-create"},{"MessageType","PartyCreateRequest"},
                                        {"CharacterId","100"},{"PartyId","P1"}}), true, &error), "party create", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","party-member"},{"MessageType","PartyMemberAdded"},
                                        {"CharacterId","100"},{"PartyId","P1"},{"LeaderEntityId","100"},
                                        {"MemberEntityId","101"},{"MemberName","隊友"},{"MemberLevel","8"},
                                        {"MemberRole","Member"},{"Online","true"}}), true, &error), "party member", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","party-disband"},{"MessageType","PartyDisbanded"},
                                        {"CharacterId","100"},{"PartyId","P1"},{"PartyState","Disbanded"}}), true, &error),
          "party disband", failures);
    for (const auto& event_type : {"PartyInviteRequest", "PartyInviteAccepted", "PartyJoined",
                                  "PartyLeaderChanged", "PartyMemberRemoved", "PartyLeft"}) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","party-" + std::string(event_type)},
                                            {"MessageType",event_type},{"CharacterId","100"},
                                            {"PartyId","P2"},{"MemberEntityId","102"},
                                            {"PartyState","Observed"}}), true, &error),
              std::string("party lifecycle ") + event_type, failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","shop-purchase"},{"MessageType","ShopPurchaseRequest"},
                                        {"CharacterId","100"},{"ShopId","SHOP1"},{"ItemId","I1"},{"Quantity","2"},
                                        {"UnitPrice","25"},{"TotalPrice","50"},{"CurrencyType","Gold"},
                                        {"CurrencyBefore","100"}}), true, &error), "shop purchase request", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","shop-purchase-result"},{"MessageType","ShopPurchaseResult"},
                                        {"CharacterId","100"},{"ShopId","SHOP1"},{"ItemId","I1"},
                                        {"CurrencyAfter","50"},{"Result","Success"}}), true, &error), "shop purchase result", failures);
    for (const auto& event_type : {"ShopSellRequest", "ShopSellResult", "ItemRepairRequest", "ItemRepairResult"}) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","economy-" + std::string(event_type)},
                                            {"MessageType",event_type},{"CharacterId","100"},
                                            {"ShopId","SHOP1"},{"ItemId","I2"},{"Quantity","1"},
                                            {"CurrencyType","Gold"},{"DurabilityBefore","10"},
                                            {"DurabilityAfter","100"},{"Result","Success"}}), true, &error),
              std::string("economy lifecycle ") + event_type, failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","character-level"},{"MessageType","CharacterLevelChanged"},
                                        {"CharacterId","100"},{"PreviousLevel","1"},{"CurrentLevel","2"},
                                        {"PreviousExperience","90"},{"CurrentExperience","10"},
                                        {"RequiredExperience","100"},{"CandidateConfidence","0.4"}}), true, &error),
          "character progression and level formula sample", failures);
    for (const auto& [event_type, entity_id] : std::vector<std::pair<std::string,std::string>>{
            {"BattlePetLevelChanged","BP1"},{"PetLevelChanged","PET1"},{"ImmortalLevelChanged","IMM1"}}) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","progression-" + entity_id},
                                            {"MessageType",event_type},{"EntityId",entity_id},
                                            {"PreviousLevel","2"},{"CurrentLevel","3"},
                                            {"RequiredExperience","200"}}), true, &error),
              "progression " + event_type, failures);
    }
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","damage-formula"},{"MessageType","DamageCalculated"},
                                        {"FormulaType","Damage"},{"InputValues","Attack=50;Defense=20"},
                                        {"ObservedResult","32"},{"CandidateExpression","Attack-Defense+2"},
                                        {"CandidateConfidence","0.35"},{"EvidenceLevel","Candidate"}}), true, &error),
          "damage formula candidate", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","speed-order"},{"MessageType","SpeedOrderObserved"},
                                        {"FormulaType","SpeedOrder"},{"InputValues","A=15;B=12"},
                                        {"ObservedResult","AFirst"},{"EvidenceLevel","Derived"}}), true, &error),
          "speed order formula candidate", failures);
    for (const auto& [event_type, formula_type] : std::vector<std::pair<std::string,std::string>>{
            {"HitResult","Hit"},{"DodgeResult","Dodge"},{"CriticalResult","Critical"},
            {"ElementInteraction","Element"}}) {
        Check(engine.ProcessJsonLine(Event({{"SourceFrameId","formula-" + formula_type},
                                            {"MessageType",event_type},{"FormulaType",formula_type},
                                            {"InputValues","Sample=1"},{"ObservedResult","Observed"},
                                            {"EvidenceLevel","Candidate"}}), true, &error),
              "formula type " + formula_type, failures);
    }
    // Exact-build plaintext fixtures exercise the full bridge from injected
    // pre-encrypt/handler records into protocol evidence and formal gameplay
    // handlers. These are candidates until each semantic field is proven.
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","preencrypt-battle-command"},
            {"MessageType","BattleCommand"},{"PacketDirection","ClientToServer"},
            {"Opcode","0x35"},{"CaptureStage","PreEncrypt"},
            {"Transport","InjectedPreEncrypt"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","140035060B00004000000000000000100000001E"}}), true, &error),
          "pre-encrypt battle command handler", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","handler-battle-effect"},
            {"MessageType","SkillDamage"},{"PacketDirection","ServerToClient"},
            {"Opcode","0x83"},{"CaptureStage","HandlerDecoded"},
            {"Transport","InjectedHandlerDecoded"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","830101010000000800DBFF001C0000"}}), true, &error),
          "handler-level battle effect promotion", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","handler-battle-healing"},
            {"MessageType","SkillHealing"},{"PacketDirection","ServerToClient"},
            {"Opcode","0x83"},{"CaptureStage","HandlerDecoded"},
            {"Transport","InjectedHandlerDecoded"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","8301010100000008001900001C0000"}}), true, &error),
          "handler-level healing effect promotion", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","preencrypt-inventory-transfer"},
            {"MessageType","InventorySlotTransferCandidate"},{"PacketDirection","ClientToServer"},
            {"Opcode","0x28"},{"CaptureStage","PreEncrypt"},
            {"Transport","InjectedPreEncrypt"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","080028010305D3B0"}}), true, &error),
          "pre-encrypt inventory slot candidate", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","preencrypt-movement"},
            {"MessageType","MovementRequest"},{"PacketDirection","ClientToServer"},
            {"Opcode","0x2e"},{"CaptureStage","PreEncrypt"},
            {"Transport","InjectedPreEncrypt"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","0A002E3412785601026B"}}), true, &error),
          "pre-encrypt movement handler", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","preencrypt-character-create"},
            {"MessageType","CharacterCreateRequest"},{"PacketDirection","ClientToServer"},
            {"Opcode","0x17"},{"CaptureStage","PreEncrypt"},
            {"Transport","InjectedPreEncrypt"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","30001700000000000000000000000000000000000000000000000000000000000000000000000000000000040000004F"}}), true, &error),
          "pre-encrypt character create envelope", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","invalid-preencrypt-movement"},
            {"MessageType","MovementRequest"},{"PacketDirection","ClientToServer"},
            {"Opcode","0x2E"},{"CaptureStage","PreEncrypt"},
            {"Transport","InjectedPreEncrypt"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","0A002E3412785601026A"}}), true, &error),
          "invalid pre-encrypt movement is retained fail-closed", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","invalid-handler-battle-effect"},
            {"MessageType","SkillDamage"},{"PacketDirection","ServerToClient"},
            {"Opcode","0x83"},{"CaptureStage","HandlerDecoded"},
            {"Transport","InjectedHandlerDecoded"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","830101010000000800DBFF001C00"}}), true, &error),
          "invalid handler length is retained fail-closed", failures);
    Check(engine.ProcessJsonLine(Event({
            {"SourceFrameId","invalid-character-create-marker"},
            {"MessageType","CharacterCreateRequest"},{"PacketDirection","ClientToServer"},
            {"Opcode","0x17"},{"CaptureStage","PreEncrypt"},
            {"Transport","InjectedPreEncrypt"},{"EvidenceLevel","Candidate"},
            {"PayloadHex","300017000000000000000000000000000000000000000000000000000000000000000000000000000000000500000050"}}), true, &error),
          "invalid character marker is retained fail-closed", failures);
    const std::string stable_no_id_json =
        "{ \"MessageType\":\"MovementUpdate\", \"CharacterId\":\"stable-no-id\", \"StartX\":\"1\", \"StartY\":\"1\", \"EndX\":\"2\", \"EndY\":\"1\" }";
    Check(engine.ProcessJsonLine(stable_no_id_json, true, &error),
          "generated source frame id ingestion", failures);
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","unknown-opcode"},{"Opcode","0xBEEF"},
                                        {"PayloadHex","01020304"},{"EvidenceLevel","Transmitted"}}), true, &error),
          "unknown opcode retention", failures);

    const auto pcap_fixture = session_path / L"raw" / L"selftest-capture.pcapng";
    std::vector<std::uint8_t> pcap;
    auto u16 = [&](std::uint16_t value) { pcap.push_back(static_cast<std::uint8_t>(value)); pcap.push_back(static_cast<std::uint8_t>(value >> 8)); };
    auto u32 = [&](std::uint32_t value) { u16(static_cast<std::uint16_t>(value)); u16(static_cast<std::uint16_t>(value >> 16)); };
    u32(0x0A0D0D0A); u32(28); u32(0x1A2B3C4D); u16(1); u16(0); u32(0xFFFFFFFF); u32(0xFFFFFFFF); u32(28);
    u32(1); u32(20); u16(1); u16(0); u32(65535); u32(20);
    auto append_packet = [&](const std::vector<std::uint8_t>& payload, std::uint32_t sequence) {
        std::vector<std::uint8_t> packet(14 + 20 + 20 + payload.size(), 0);
        packet[12] = 0x08; packet[13] = 0x00;
        packet[14] = 0x45;
        const auto ip_length = static_cast<std::uint16_t>(20 + 20 + payload.size());
        packet[16] = static_cast<std::uint8_t>(ip_length >> 8);
        packet[17] = static_cast<std::uint8_t>(ip_length);
        packet[22] = 64; packet[23] = 6;
        packet[26] = 1; packet[27] = 2; packet[28] = 3; packet[29] = 4;
        packet[30] = 5; packet[31] = 6; packet[32] = 7; packet[33] = 8;
        packet[34] = 0x03; packet[35] = 0xE8; packet[36] = 0x07; packet[37] = 0xD0;
        packet[38] = static_cast<std::uint8_t>(sequence >> 24);
        packet[39] = static_cast<std::uint8_t>(sequence >> 16);
        packet[40] = static_cast<std::uint8_t>(sequence >> 8);
        packet[41] = static_cast<std::uint8_t>(sequence);
        packet[46] = 0x50; packet[47] = 0x10;
        std::copy(payload.begin(), payload.end(), packet.begin() + 54);
        const std::uint32_t padded = static_cast<std::uint32_t>((packet.size() + 3) & ~std::size_t(3));
        const std::uint32_t epb_length = 8 + 20 + padded + 4;
        u32(6); u32(epb_length); u32(0); u32(0); u32(0); u32(static_cast<std::uint32_t>(packet.size()));
        u32(static_cast<std::uint32_t>(packet.size()));
        pcap.insert(pcap.end(), packet.begin(), packet.end());
        while ((pcap.size() & 3) != 0) pcap.push_back(0);
        u32(epb_length);
    };
    append_packet(std::vector<std::uint8_t>(verified_movement.begin(), verified_movement.begin() + 4), 1000);
    std::vector<std::uint8_t> coalesced(verified_movement.begin() + 4, verified_movement.end());
    coalesced.insert(coalesced.end(), verified_movement.begin(), verified_movement.end());
    append_packet(coalesced, 1004);
    {
        std::ofstream fixture(pcap_fixture, std::ios::binary | std::ios::trunc);
        fixture.write(reinterpret_cast<const char*>(pcap.data()), static_cast<std::streamsize>(pcap.size()));
    }
    std::string pcap_reason;
    Check(ValidatePcapng(pcap_fixture, &pcap_reason), "synthetic PCAPNG validation", failures);
    const auto extracted = ExtractPcapngPayloads(pcap_fixture, engine, &error);
    Check(extracted.success && extracted.packet_count == 2 && extracted.payload_bytes == 20 &&
          extracted.unknown_protocol_packets == 0,
          "fragmented and coalesced TCP movement reassembly", failures);
    const auto frames_before_transport_replay = engine.Statistics().frames;
    const auto unknown_before_transport_replay = engine.Statistics().unknown;
    Check(engine.ProcessJsonLine(Event({{"SourceFrameId","transport-envelope-replay"},
                                        {"Transport","TCP"},{"PayloadHex","01020304"}}), false, &error) &&
          engine.Statistics().frames == frames_before_transport_replay &&
          engine.Statistics().unknown == unknown_before_transport_replay,
          "raw transport envelope is not double-counted during replay", failures);

    Check(store.FinishAnalysis("Completed", &error), "finish analysis", failures);
    Check(store.ExportAll(&error), "CSV export", failures);
    Check(store.Db().SchemaVersion() == 11, "schema migration version", failures);
    Check(QuestTypes().size() == 18, "quest type count", failures);
    Check(QuestObjectiveTypes().size() == 23, "quest objective count", failures);
    Check(SkillFacetCatalog().size() >= 110, "skill facet count", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profile_facets WHERE skill_id='S100';") == 7,
          "seven simultaneous facets", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profile_facets WHERE skill_id='';") == 0,
          "unknown skill facets not promoted", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profiles WHERE skill_id='UNKNOWN-ID' AND unknown_skill='true' AND evidence_level='Unknown';") == 1 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profile_facets WHERE skill_id='UNKNOWN-ID';") == 0,
          "identifier-only unknown skill remains Unknown", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM raw_frames WHERE source_json LIKE '{ \"MessageType\":\"MovementUpdate\"%' AND original_json LIKE '%\"SourceFrameId\"%';") == 1,
          "literal source JSON retained separately from canonical replay JSON", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM consumable_use_observations;") >= 1,
          "consumable observation exists", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT item_use_correlation_id) FROM consumable_use_observations WHERE event_type='ItemUseRequest' AND item_id='HP1';") == 2,
          "separate consumable uses receive separate correlations", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_cast_correlations WHERE skill_id='HP1';") == 0,
          "medicine is not a character skill", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM movement_observations WHERE event_type='Teleport' AND movement_mode='Walking';") == 0,
          "teleport not walking", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM movement_observations WHERE event_type='ServerPositionCorrection' AND movement_mode='Walking';") == 0,
          "server correction not walking", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM movement_direction_observations WHERE direction_raw='1' AND direction_normalized='NorthEast' AND evidence_level='Verified';") == 1,
          "raw direction preserved and verified mapping normalized", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM movement_direction_observations WHERE direction_raw='2' AND direction_normalized='East' AND evidence_level='Candidate';") == 1,
          "unverified raw mapping remains Candidate", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM mounted_movement_observations WHERE movement_mode='Mounted';") == 10,
          "mounted movement mode persisted explicitly", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM mounted_movement_observations WHERE mount_movement_speed='12.5' AND character_stat_speed='88';") == 8,
          "mount and stat speeds separated", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM mount_state_observations WHERE event_type='MountedMovementUpdate' AND is_mounted='true';") == 10,
          "all mounted movement observations remain mounted", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT skill_cast_correlation_id) FROM skill_cast_correlations WHERE event_type='SkillCastStart' AND skill_id='S100';") == 2,
          "separate skill casts receive separate correlations", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT quest_action_correlation_id) FROM quest_observations WHERE quest_id='Q1' AND event_type IN ('QuestAcceptRequest','QuestAcceptedNotification');") == 1,
          "quest request and notification share an action correlation", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM quest_correlations WHERE quest_id='Q1' AND related_event_type IN ('MonsterSpawn','BattleStart','MonsterDeath','ItemDrop');") == 4 &&
          store.Db().ScalarInt64("SELECT COUNT(DISTINCT quest_correlation_id) FROM quest_correlations WHERE quest_id='Q1' AND related_event_type IN ('MonsterSpawn','BattleStart','MonsterDeath','ItemDrop');") == 1 &&
          store.Db().ScalarInt64("SELECT COUNT(DISTINCT quest_action_correlation_id) FROM quest_correlations WHERE quest_id='Q1' AND related_event_type IN ('MonsterSpawn','BattleStart','MonsterDeath','ItemDrop');") == 1,
          "quest monster battle death and drop chain remains correlated", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT quest_stage_id) FROM quest_stages WHERE quest_id='Q1';") == 2,
          "multi-stage quest persisted", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM quest_objectives WHERE quest_id='Q1' AND objective_id='O1';") == 2,
          "multi-label quest objective persisted", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT skill_source_type) FROM skill_profiles WHERE skill_source_type IN ('CharacterSkill','MonsterSkill','BattlePetSkill','PetSkill','ImmortalSkill','MountSkill');") == 6,
          "character monster pet immortal mount skill sources separated", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profile_facets WHERE skill_id='BA1' AND facet_id='BasicAttack';") == 1 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profile_facets WHERE skill_id='S100' AND facet_id='BasicAttack';") == 0,
          "basic attack separated from active spell", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM entity_observations WHERE entity_type='NPC' AND entity_id='N10' AND strength='10' AND character_stat_speed='15';") == 1,
          "entity stats and speed persistence", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT entity_type) FROM entity_observations WHERE entity_type IN ('NPC','Monster','Character','BattlePet','Pet','Immortal','Mount');") == 7,
          "all required world entity types persist", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT event_type) FROM character_lifecycle_observations WHERE character_id='100';") >= 4,
          "character create select logout delete lifecycle persistence", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM party_observations WHERE party_id='P1';") == 3 &&
          store.Db().ScalarInt64("SELECT COUNT(DISTINCT event_type) FROM party_observations WHERE party_id='P2';") == 6 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM party_member_observations WHERE member_entity_id='101';") == 1,
          "party lifecycle persistence", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT event_type) FROM economy_observations WHERE shop_id='SHOP1';") == 6,
          "shop purchase sell and repair lifecycle persistence", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT entity_type) FROM progression_observations WHERE entity_type IN ('Character','BattlePet','Pet','Immortal');") == 4,
          "character pet and immortal progression persistence", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(DISTINCT formula_type) FROM formula_candidates WHERE formula_type IN ('Damage','Hit','Dodge','Critical','Element','SpeedOrder','LevelProgression');") == 7,
          "formula candidates preserve evidence status", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='preencrypt-battle-command' AND opcode='0x35' AND battle_position_candidate='6' AND action_code_candidate='11' AND action_parameter_candidate='16';") == 1,
          "pre-encrypt command fields persist in protocol handler evidence", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='handler-battle-effect' AND opcode='0x83' AND event_type='BattleEffectDeltaCandidate' AND attacker_position_candidate='1' AND target_position_candidate='1' AND observed_signed_delta_candidate='-37' AND semantic_status='SourcePositionAndDeltaVerified_TargetRequiresCommandMaskCorrelation';") == 1,
           "handler-level battle delta persists without authoritative promotion", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='preencrypt-inventory-transfer' AND opcode='0x28' AND entity_or_item_id_candidate='769' AND slot_candidate='5' AND state_candidate='211' AND checksum_valid='true';") == 1,
          "inventory slot candidate fields persist without runtime mutation", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM movement_observations WHERE source_frame_ids='preencrypt-movement' AND end_x='4660' AND end_y='22136';") == 1,
          "decoded movement coordinates reach formal movement handler", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='preencrypt-character-create' AND opcode='0x17' AND character_create_marker_offset='40' AND character_create_marker_value='4' AND semantic_status='StaticEnvelopeVerified_FieldSemanticsEvidenceBlocked_RuntimeMutationBlocked';") == 1,
          "character create static envelope persists with runtime mutation blocked", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='invalid-preencrypt-movement' AND event_type='DecodedFrameValidationFailed' AND semantic_status='EvidenceBlocked' AND decode_validation='LengthOrChecksumMismatch' AND evidence_level='Unknown';") == 1 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM movement_observations WHERE source_frame_ids='invalid-preencrypt-movement';") == 0,
          "invalid checksum cannot reach formal movement handler", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='invalid-handler-battle-effect' AND event_type='DecodedFrameValidationFailed' AND semantic_status='EvidenceBlocked' AND decode_validation='HandlerLengthMismatch' AND evidence_level='Unknown';") == 1 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_effect_observations WHERE source_frame_ids='invalid-handler-battle-effect';") == 0 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM formula_candidates WHERE source_frame_ids='invalid-handler-battle-effect';") == 0,
          "invalid handler length cannot reach skill or formula handlers", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM protocol_handler_observations WHERE source_frame_ids='invalid-character-create-marker' AND event_type='DecodedFrameValidationFailed' AND semantic_status='EvidenceBlocked_StaticMarkerMismatch' AND decode_validation='StaticMarkerMismatch' AND evidence_level='Unknown';") == 1 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM character_lifecycle_observations WHERE source_frame_ids='invalid-character-create-marker';") == 0,
          "invalid character marker cannot reach lifecycle handler", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM skill_effect_observations WHERE source_frame_ids IN ('handler-battle-effect','handler-battle-healing');") == 0,
          "handler-level battle deltas do not fabricate skill effects", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM formula_candidates WHERE source_frame_ids IN ('handler-battle-effect','handler-battle-healing');") == 0,
          "handler-level battle deltas do not fabricate damage or healing formulas", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM unknown_opcode_clusters WHERE opcode='0xBEEF' AND observed_count=1;") == 1,
          "unknown opcode clustering", failures);
    Check(store.Db().ScalarInt64("SELECT COUNT(*) FROM gameplay_coverage WHERE coverage_status<>'NotObserved';") >= 10 &&
          store.Db().ScalarInt64("SELECT COUNT(*) FROM formula_coverage WHERE coverage_status<>'NotObserved';") >= 3,
          "gameplay and formula coverage matrices", failures);

    Check(store.Db().Execute("UPDATE raw_frames SET jsonl_written=0;", &error), "mark raw JSONL for recovery", failures);
    Check(WriteUtf8File(session_path / L"raw/frames.jsonl", ""), "damage raw JSONL fixture", failures);
    Check(WriteUtf8File(session_path / L"gameplay/movement.jsonl", ""), "damage gameplay JSONL fixture", failures);
    SessionStore repaired(session_path);
    Check(repaired.Initialize(&error), "recover JSONL materializations from SQLite", failures);
    const auto repaired_raw = ReadUtf8File(session_path / L"raw/frames.jsonl");
    const auto repaired_movement = ReadUtf8File(session_path / L"gameplay/movement.jsonl");
    Check(repaired_raw && repaired_raw->find("stable-no-id") != std::string::npos &&
          repaired_movement && repaired_movement->find("MovementUpdate") != std::string::npos,
          "recovered JSONL contains canonical evidence", failures);

    SessionStore replay(session_path);
    Check(replay.Initialize(&error) && replay.BeginAnalysis("SelfTestReanalysis", &error), "begin reanalysis", failures);
    GameplayAnalysisEngine replay_engine(replay);
    Check(replay_engine.AnalyzeJsonl(session_path / L"raw" / L"frames.jsonl", false, &error), "old session reanalysis", failures);
    Check(replay.FinishAnalysis("Completed", &error), "finish reanalysis", failures);
    Check(replay.Db().ScalarInt64("SELECT COUNT(*) FROM analysis_runs;") == 2, "analysis history preserved", failures);
    Check(replay.Db().ScalarInt64("SELECT COUNT(DISTINCT source_frame_ids) FROM movement_observations WHERE character_id='stable-no-id';") == 1,
          "generated source frame id remains stable across reanalysis", failures);

    BoundedEventBuffer<std::uint64_t> buffer(1024);
    for (std::uint64_t i = 0; i < 1'000'000; ++i) buffer.Push(i);
    Check(buffer.Size() == 1024 && buffer.HighWatermark() == 1024, "million-event fixed memory bound", failures);

    std::string import_error;
    const auto imports = AuditImportedFunctions(ExecutablePath(), &import_error);
    const std::vector<std::string> forbidden = {
        "GetDpiForWindow", "SetThreadDescription", "GetSystemTimePreciseAsFileTime", "PathCch",
        "GetProcessInformation", "SetProcessInformation"
    };
    for (const auto& name : forbidden) {
        const bool direct = std::any_of(imports.begin(), imports.end(), [&](const std::string& value) {
            return value.find("!" + name) != std::string::npos;
        });
        Check(!direct, "Win7 import audit: " + name, failures);
    }
    for (const auto& path : RequiredGameplayFiles()) Check(fs::exists(session_path / path), "required JSONL " + WideToUtf8(path.wstring()), failures);
    for (const auto& [table, path] : RequiredExports()) Check(fs::exists(session_path / path), "required CSV " + table, failures);

    const std::string portable_session_path = "SelfTests/" +
        WideToUtf8(session_path.filename().wstring());
    Check(portable_session_path.find(':') == std::string::npos &&
          portable_session_path.find("C:\\") == std::string::npos &&
          portable_session_path.find("Users/") == std::string::npos &&
          portable_session_path.find("Users\\") == std::string::npos,
          "self-test report exposes only a portable session token", failures);
    const auto report = MakeJsonObject({
        {"Status", failures.empty() ? "PASS" : "FAIL"}, {"TestedAtUtc", UtcNow()},
        {"SessionPath", portable_session_path}, {"FailureCount", std::to_string(failures.size())},
        {"QuestTypeCount", std::to_string(QuestTypes().size())},
        {"QuestObjectiveTypeCount", std::to_string(QuestObjectiveTypes().size())},
        {"SkillFacetCount", std::to_string(SkillFacetCatalog().size())},
        {"BoundedBufferCapacity", std::to_string(buffer.Capacity())},
        {"BoundedBufferHighWatermark", std::to_string(buffer.HighWatermark())},
        {"CheckCount", std::to_string(g_selftest_check_count)},
        {"UnknownOpcodeCount", std::to_string(engine.Statistics().unknown)},
        {"Failures", JoinList(failures)}
    }, {"FailureCount","QuestTypeCount","QuestObjectiveTypeCount","SkillFacetCount",
        "BoundedBufferCapacity","BoundedBufferHighWatermark","CheckCount","UnknownOpcodeCount"});
    WriteUtf8File(session_path / L"compatibility/selftest-report.json", report + "\n");
    Print(report);
    return failures.empty() ? 0 : kExitSoftware;
}

bool IsContainedValidationPath(const fs::path& root, const fs::path& candidate) {
    if (root.empty() || candidate.empty() || !root.is_absolute() || !candidate.is_absolute()) return false;
    std::error_code root_error;
    std::error_code candidate_error;
    auto canonical_root = fs::weakly_canonical(root, root_error).wstring();
    auto canonical_candidate = fs::weakly_canonical(candidate, candidate_error).wstring();
    if (root_error || candidate_error) return false;
    std::transform(canonical_root.begin(), canonical_root.end(), canonical_root.begin(), ::towlower);
    std::transform(canonical_candidate.begin(), canonical_candidate.end(), canonical_candidate.begin(), ::towlower);
    return canonical_candidate.size() >= canonical_root.size() &&
        canonical_candidate.compare(0, canonical_root.size(), canonical_root) == 0 &&
        (canonical_candidate.size() == canonical_root.size() ||
         canonical_candidate[canonical_root.size()] == L'\\' ||
         canonical_candidate[canonical_root.size()] == L'/');
}

std::wstring NormalizeValidationFinalPath(std::wstring path) {
    constexpr std::wstring_view unc_prefix = L"\\\\?\\UNC\\";
    constexpr std::wstring_view local_prefix = L"\\\\?\\";
    if (path.starts_with(unc_prefix)) path = L"\\\\" + path.substr(unc_prefix.size());
    else if (path.starts_with(local_prefix)) path.erase(0, local_prefix.size());
    while (path.size() > 3 && (path.back() == L'\\' || path.back() == L'/')) path.pop_back();
    return path;
}

std::optional<std::wstring> ValidationFinalPath(HANDLE handle) {
    if (handle == nullptr || handle == INVALID_HANDLE_VALUE) return std::nullopt;
    std::vector<wchar_t> buffer(512);
    for (;;) {
        const DWORD length = GetFinalPathNameByHandleW(handle, buffer.data(),
            static_cast<DWORD>(buffer.size()), FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (length == 0) return std::nullopt;
        if (length < buffer.size())
            return NormalizeValidationFinalPath(std::wstring(buffer.data(), length));
        buffer.resize(static_cast<std::size_t>(length) + 1U);
    }
}

bool SameFileIdentity(const BY_HANDLE_FILE_INFORMATION& left,
                      const BY_HANDLE_FILE_INFORMATION& right) noexcept {
    return left.dwVolumeSerialNumber == right.dwVolumeSerialNumber &&
        left.nFileIndexHigh == right.nFileIndexHigh &&
        left.nFileIndexLow == right.nFileIndexLow &&
        left.dwFileAttributes == right.dwFileAttributes;
}

class PinnedValidationPath {
public:
    PinnedValidationPath() = default;
    PinnedValidationPath(const PinnedValidationPath&) = delete;
    PinnedValidationPath& operator=(const PinnedValidationPath&) = delete;
    PinnedValidationPath(PinnedValidationPath&& other) noexcept { MoveFrom(other); }
    PinnedValidationPath& operator=(PinnedValidationPath&& other) noexcept {
        if (this != &other) {
            Close();
            MoveFrom(other);
        }
        return *this;
    }
    ~PinnedValidationPath() { Close(); }

    bool Open(const fs::path& containment_root, const fs::path& path,
              bool directory, std::string* error) {
        Close();
        std::error_code canonical_error;
        const auto absolute_root = fs::weakly_canonical(containment_root, canonical_error);
        const auto absolute_path = fs::absolute(path, canonical_error).lexically_normal();
        std::wstring folded_root = absolute_root.wstring();
        std::wstring folded_path = absolute_path.wstring();
        std::transform(folded_root.begin(), folded_root.end(), folded_root.begin(), ::towlower);
        std::transform(folded_path.begin(), folded_path.end(), folded_path.begin(), ::towlower);
        const bool lexically_contained = folded_path.size() >= folded_root.size() &&
            folded_path.compare(0, folded_root.size(), folded_root) == 0 &&
            (folded_path.size() == folded_root.size() ||
             folded_path[folded_root.size()] == L'\\' ||
             folded_path[folded_root.size()] == L'/');
        if (canonical_error || !lexically_contained) {
            if (error != nullptr) *error = "validation path escaped its containment root";
            return false;
        }
        const DWORD flags = FILE_FLAG_OPEN_REPARSE_POINT |
            (directory ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL);
        handle_ = CreateFileW(absolute_path.c_str(), GENERIC_READ, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, flags, nullptr);
        if (handle_ == INVALID_HANDLE_VALUE) {
            handle_ = nullptr;
            if (error != nullptr) *error = "validation path could not be pinned: Win32 " +
                std::to_string(GetLastError());
            return false;
        }
        const auto observed_final = ValidationFinalPath(handle_);
        BY_HANDLE_FILE_INFORMATION observed{};
        const bool information_read = GetFileInformationByHandle(handle_, &observed) != FALSE;
        const bool type_matches = directory ?
            (information_read && (observed.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) :
            (information_read && (observed.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0);
        if (!observed_final || !information_read || !type_matches ||
            (observed.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
            _wcsicmp(observed_final->c_str(), absolute_path.wstring().c_str()) != 0) {
            if (error != nullptr) *error = "validation path is reparse-backed or has a different final path";
            Close();
            return false;
        }
        path_ = absolute_path;
        final_path_ = *observed_final;
        identity_ = observed;
        directory_ = directory;
        if (!directory_) {
            hash_ = CalculateFileSha256(path_).value_or("");
            size_ = (static_cast<std::uint64_t>(identity_.nFileSizeHigh) << 32U) |
                identity_.nFileSizeLow;
            if (hash_.empty()) {
                if (error != nullptr) *error = "validation file could not be hashed while pinned";
                Close();
                return false;
            }
        }
        return true;
    }

    bool Revalidate(std::string* error) const {
        if (handle_ == nullptr) {
            if (error != nullptr) *error = "validation handle is not open";
            return false;
        }
        BY_HANDLE_FILE_INFORMATION observed{};
        const auto observed_final = ValidationFinalPath(handle_);
        const bool identity_matches = GetFileInformationByHandle(handle_, &observed) != FALSE &&
            SameFileIdentity(identity_, observed);
        const bool final_matches = observed_final &&
            _wcsicmp(observed_final->c_str(), final_path_.c_str()) == 0;
        const bool content_matches = directory_ ||
            CalculateFileSha256(path_).value_or("") == hash_;
        if (!identity_matches || !final_matches || !content_matches) {
            if (error != nullptr) *error = "pinned validation identity changed during validation";
            return false;
        }
        return true;
    }

    const fs::path& Path() const noexcept { return path_; }
    const std::string& Hash() const noexcept { return hash_; }
    std::uint64_t Size() const noexcept { return size_; }
    HANDLE Handle() const noexcept { return handle_; }

private:
    void Close() noexcept {
        if (handle_ != nullptr) CloseHandle(handle_);
        handle_ = nullptr;
        path_.clear();
        final_path_.clear();
        hash_.clear();
        identity_ = {};
        size_ = 0;
        directory_ = false;
    }
    void MoveFrom(PinnedValidationPath& other) noexcept {
        handle_ = other.handle_;
        other.handle_ = nullptr;
        path_ = std::move(other.path_);
        final_path_ = std::move(other.final_path_);
        hash_ = std::move(other.hash_);
        identity_ = other.identity_;
        size_ = other.size_;
        directory_ = other.directory_;
        other.identity_ = {};
        other.size_ = 0;
        other.directory_ = false;
    }
    HANDLE handle_ = nullptr;
    fs::path path_;
    std::wstring final_path_;
    std::string hash_;
    BY_HANDLE_FILE_INFORMATION identity_{};
    std::uint64_t size_ = 0;
    bool directory_ = false;
};

bool IsSafeZipInventoryPath(std::string_view value) {
    if (value.empty() || value.size() > 4096 || value.front() == '/' ||
        value.find('\\') != std::string_view::npos ||
        value.find(':') != std::string_view::npos ||
        value.find('\0') != std::string_view::npos) return false;
    std::size_t start = 0;
    while (start < value.size()) {
        const auto separator = value.find('/', start);
        const auto component = value.substr(start, separator == std::string_view::npos ?
            value.size() - start : separator - start);
        if (component.empty() || component == "." || component == "..") return false;
        if (separator == std::string_view::npos) break;
        start = separator + 1U;
    }
    return true;
}

template <typename T>
bool ReadValidationZipLittle(std::istream& input, T* value) {
    static_assert(std::is_unsigned_v<T>);
    std::uint64_t result = 0;
    for (std::size_t index = 0; index < sizeof(T); ++index) {
        const int byte = input.get();
        if (byte == EOF) return false;
        result |= static_cast<std::uint64_t>(static_cast<unsigned char>(byte)) << (index * 8U);
    }
    *value = static_cast<T>(result);
    return true;
}

std::uint32_t ValidationCrc32Update(std::uint32_t crc, const unsigned char* data,
                                    std::size_t size) {
    for (std::size_t index = 0; index < size; ++index) {
        crc ^= data[index];
        for (int bit = 0; bit < 8; ++bit)
            crc = (crc >> 1U) ^ (0xEDB88320U & (0U - (crc & 1U)));
    }
    return crc;
}

struct StrictZipEntryIdentity {
    std::uint32_t crc32 = 0;
    std::uint32_t size = 0;
    std::uint32_t local_offset = 0;
};

bool ReadStrictStoredZipInventory(const fs::path& zip_path,
                                  std::map<std::string, StrictZipEntryIdentity>* entries,
                                  std::string* error) {
    if (entries == nullptr) return false;
    entries->clear();
    std::ifstream input(zip_path, std::ios::binary);
    if (!input) {
        if (error != nullptr) *error = "cannot open recovery ZIP inventory";
        return false;
    }
    std::vector<unsigned char> buffer;
    try {
        buffer.resize(64U * 1024U);
    } catch (const std::bad_alloc&) {
        if (error != nullptr) *error = "cannot allocate bounded recovery ZIP validation buffer";
        return false;
    }
    std::uint32_t central_offset = 0;
    for (;;) {
        const auto local_position = input.tellg();
        if (local_position < 0 || static_cast<std::uint64_t>(local_position) > UINT32_MAX) return false;
        std::uint32_t signature = 0;
        if (!ReadValidationZipLittle(input, &signature)) return false;
        if (signature == 0x02014B50U) {
            central_offset = static_cast<std::uint32_t>(local_position);
            input.seekg(local_position);
            break;
        }
        std::uint16_t version = 0, flags = 0, method = 0, ignored16 = 0;
        std::uint16_t name_length = 0, extra_length = 0;
        std::uint32_t expected_crc = 0, compressed = 0, uncompressed = 0;
        if (signature != 0x04034B50U ||
            !ReadValidationZipLittle(input, &version) ||
            !ReadValidationZipLittle(input, &flags) ||
            !ReadValidationZipLittle(input, &method) ||
            !ReadValidationZipLittle(input, &ignored16) ||
            !ReadValidationZipLittle(input, &ignored16) ||
            !ReadValidationZipLittle(input, &expected_crc) ||
            !ReadValidationZipLittle(input, &compressed) ||
            !ReadValidationZipLittle(input, &uncompressed) ||
            !ReadValidationZipLittle(input, &name_length) ||
            !ReadValidationZipLittle(input, &extra_length) ||
            version != 20 || flags != 0x0800 || method != 0 || compressed != uncompressed ||
            name_length == 0) {
            if (error != nullptr) *error = "invalid recovery ZIP local entry";
            return false;
        }
        std::string name(name_length, '\0');
        input.read(name.data(), static_cast<std::streamsize>(name.size()));
        input.seekg(extra_length, std::ios::cur);
        if (!input || !IsSafeZipInventoryPath(name) || entries->contains(name)) {
            if (error != nullptr) *error = "unsafe or duplicate recovery ZIP entry";
            return false;
        }
        std::uint32_t remaining = uncompressed;
        std::uint32_t crc = 0xFFFFFFFFU;
        while (remaining != 0) {
            const auto wanted = static_cast<std::streamsize>(
                std::min<std::uint32_t>(remaining, static_cast<std::uint32_t>(buffer.size())));
            input.read(reinterpret_cast<char*>(buffer.data()), wanted);
            if (input.gcount() != wanted) return false;
            crc = ValidationCrc32Update(crc, buffer.data(), static_cast<std::size_t>(wanted));
            remaining -= static_cast<std::uint32_t>(wanted);
        }
        crc ^= 0xFFFFFFFFU;
        if (crc != expected_crc) {
            if (error != nullptr) *error = "recovery ZIP CRC mismatch";
            return false;
        }
        entries->emplace(std::move(name), StrictZipEntryIdentity{
            expected_crc, uncompressed, static_cast<std::uint32_t>(local_position)});
    }
    const auto central_start = input.tellg();
    std::set<std::string> central_names;
    for (std::size_t index = 0; index < entries->size(); ++index) {
        std::uint32_t signature = 0, crc = 0, compressed = 0, uncompressed = 0;
        std::uint32_t external_attributes = 0, local_offset = 0;
        std::uint16_t version_made = 0, version_needed = 0, flags = 0, method = 0;
        std::uint16_t ignored16 = 0, name_length = 0, extra_length = 0, comment_length = 0;
        if (!ReadValidationZipLittle(input, &signature) || signature != 0x02014B50U ||
            !ReadValidationZipLittle(input, &version_made) ||
            !ReadValidationZipLittle(input, &version_needed) ||
            !ReadValidationZipLittle(input, &flags) ||
            !ReadValidationZipLittle(input, &method) ||
            !ReadValidationZipLittle(input, &ignored16) ||
            !ReadValidationZipLittle(input, &ignored16) ||
            !ReadValidationZipLittle(input, &crc) ||
            !ReadValidationZipLittle(input, &compressed) ||
            !ReadValidationZipLittle(input, &uncompressed) ||
            !ReadValidationZipLittle(input, &name_length) ||
            !ReadValidationZipLittle(input, &extra_length) ||
            !ReadValidationZipLittle(input, &comment_length) ||
            !ReadValidationZipLittle(input, &ignored16) ||
            !ReadValidationZipLittle(input, &ignored16) ||
            !ReadValidationZipLittle(input, &external_attributes) ||
            !ReadValidationZipLittle(input, &local_offset) ||
            version_needed != 20 || flags != 0x0800 || method != 0 || compressed != uncompressed) {
            if (error != nullptr) *error = "invalid recovery ZIP central entry";
            return false;
        }
        static_cast<void>(version_made);
        static_cast<void>(external_attributes);
        std::string name(name_length, '\0');
        input.read(name.data(), static_cast<std::streamsize>(name.size()));
        input.seekg(static_cast<std::streamoff>(extra_length) + comment_length, std::ios::cur);
        const auto found = entries->find(name);
        if (!input || found == entries->end() || !central_names.insert(name).second ||
            found->second.crc32 != crc || found->second.size != uncompressed ||
            found->second.local_offset != local_offset) {
            if (error != nullptr) *error = "recovery ZIP local/central inventory mismatch";
            return false;
        }
    }
    const auto central_end = input.tellg();
    std::uint32_t end_signature = 0, central_size = 0, recorded_offset = 0;
    std::uint16_t disk = 0, central_disk = 0, disk_entries = 0, total_entries = 0;
    std::uint16_t comment_length = 0;
    if (central_start < 0 || central_end < central_start ||
        !ReadValidationZipLittle(input, &end_signature) || end_signature != 0x06054B50U ||
        !ReadValidationZipLittle(input, &disk) ||
        !ReadValidationZipLittle(input, &central_disk) ||
        !ReadValidationZipLittle(input, &disk_entries) ||
        !ReadValidationZipLittle(input, &total_entries) ||
        !ReadValidationZipLittle(input, &central_size) ||
        !ReadValidationZipLittle(input, &recorded_offset) ||
        !ReadValidationZipLittle(input, &comment_length) || disk != 0 || central_disk != 0 ||
        disk_entries != entries->size() || total_entries != entries->size() ||
        recorded_offset != central_offset ||
        central_size != static_cast<std::uint64_t>(central_end - central_start) ||
        comment_length != 0 || input.peek() != EOF || central_names.size() != entries->size()) {
        if (error != nullptr) *error = "recovery ZIP EOCD/inventory is incomplete";
        return false;
    }
    return !entries->empty();
}

bool SplitStrictJsonObjectArray(std::string_view wire,
                                std::vector<Fields>* objects) {
    if (objects == nullptr) return false;
    objects->clear();
    const auto skip = [&](std::size_t* cursor) {
        while (*cursor < wire.size() &&
               (wire[*cursor] == ' ' || wire[*cursor] == '\t' ||
                wire[*cursor] == '\r' || wire[*cursor] == '\n')) ++*cursor;
    };
    std::size_t cursor = 0;
    skip(&cursor);
    if (cursor >= wire.size() || wire[cursor++] != '[') return false;
    skip(&cursor);
    if (cursor < wire.size() && wire[cursor] == ']') {
        ++cursor;
        skip(&cursor);
        return cursor == wire.size();
    }
    for (;;) {
        skip(&cursor);
        if (cursor >= wire.size() || wire[cursor] != '{') return false;
        const std::size_t start = cursor;
        std::vector<char> nesting;
        bool in_string = false;
        bool escaped = false;
        for (; cursor < wire.size(); ++cursor) {
            const char c = wire[cursor];
            if (in_string) {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') in_string = false;
                continue;
            }
            if (c == '"') {
                in_string = true;
                continue;
            }
            if (c == '{' || c == '[') nesting.push_back(c);
            else if (c == '}' || c == ']') {
                if (nesting.empty() ||
                    (c == '}' && nesting.back() != '{') ||
                    (c == ']' && nesting.back() != '[')) return false;
                nesting.pop_back();
                if (nesting.empty()) {
                    ++cursor;
                    break;
                }
            }
        }
        if (in_string || !nesting.empty()) return false;
        Fields object;
        if (!ParseFlatJson(wire.substr(start, cursor - start), object, nullptr)) return false;
        objects->push_back(std::move(object));
        skip(&cursor);
        if (cursor < wire.size() && wire[cursor] == ',') {
            ++cursor;
            std::size_t next = cursor;
            skip(&next);
            if (next >= wire.size() || wire[next] == ']') return false;
            continue;
        }
        if (cursor < wire.size() && wire[cursor] == ']') {
            ++cursor;
            skip(&cursor);
            return cursor == wire.size();
        }
        return false;
    }
}

bool IsHexSha256(std::string_view value) {
    return value.size() == 64 && std::all_of(value.begin(), value.end(), [](unsigned char character) {
        return std::isxdigit(character) != 0;
    });
}

bool IsSafeValidationRelativePath(const fs::path& relative) {
    if (relative.empty() || relative.is_absolute() || relative.has_root_name() ||
        relative.has_root_directory()) return false;
    for (const auto& component : relative) {
        if (component.empty() || component == L"." || component == L"..") return false;
    }
    return true;
}

struct PinnedTreeFile {
    std::string relative_path;
    std::uint64_t size = 0;
    std::string sha256;
};

bool PinValidationTree(const fs::path& tree_root,
                       std::vector<PinnedValidationPath>* pins,
                       std::map<std::string, PinnedTreeFile>* files,
                       std::string* error) {
    if (pins == nullptr || files == nullptr) return false;
    pins->clear();
    files->clear();
    PinnedValidationPath root_pin;
    if (!root_pin.Open(tree_root, tree_root, true, error)) return false;
    pins->push_back(std::move(root_pin));
    std::error_code iterator_error;
    for (fs::recursive_directory_iterator iterator(tree_root,
             fs::directory_options::none, iterator_error), end;
         iterator != end && !iterator_error; iterator.increment(iterator_error)) {
        const auto relative = iterator->path().lexically_relative(tree_root);
        if (!IsSafeValidationRelativePath(relative)) {
            if (error != nullptr) *error = "recovery staging entry escaped its lexical root";
            return false;
        }
        const std::string relative_text = WideToUtf8(relative.generic_wstring());
        const bool directory = iterator->is_directory(iterator_error);
        const bool regular = !iterator_error && iterator->is_regular_file(iterator_error);
        if (iterator_error || (!directory && !regular)) {
            if (error != nullptr) *error = "recovery staging contains a non-regular entry";
            return false;
        }
        PinnedValidationPath pin;
        if (!pin.Open(tree_root, iterator->path(), directory, error)) return false;
        if (regular) {
            PinnedTreeFile file{relative_text, pin.Size(), pin.Hash()};
            if (!IsSafeZipInventoryPath(relative_text) ||
                !files->emplace(relative_text, std::move(file)).second) {
                if (error != nullptr) *error = "recovery staging inventory is unsafe or duplicated";
                return false;
            }
        }
        pins->push_back(std::move(pin));
    }
    if (iterator_error || files->empty()) {
        if (error != nullptr) *error = iterator_error ? iterator_error.message() :
            "recovery staging inventory is empty";
        return false;
    }
    return true;
}

bool RevalidatePins(const std::vector<PinnedValidationPath>& pins, std::string* error) {
    return std::all_of(pins.begin(), pins.end(), [&](const auto& pin) {
        return pin.Revalidate(error);
    });
}

bool ValidateUltimateRecoveryZipBound(const fs::path& session,
                                      const Fields& package_result,
                                      fs::path* recovery_zip,
                                      std::string* recovery_sha,
                                      std::vector<PinnedValidationPath>* retained_pins,
                                      std::string* error) {
    if (recovery_zip == nullptr || recovery_sha == nullptr || retained_pins == nullptr) return false;
    const fs::path zip_relative = Utf8ToWide(GetString(package_result, "ZipPath"));
    const fs::path staging_relative = Utf8ToWide(GetString(package_result, "StagingPath"));
    const fs::path manifest_relative = Utf8ToWide(GetString(package_result, "ManifestPath"));
    if (!IsSafeValidationRelativePath(zip_relative) ||
        !IsSafeValidationRelativePath(staging_relative) ||
        !IsSafeValidationRelativePath(manifest_relative)) {
        if (error != nullptr) *error = "recovery package paths are not strict relative paths";
        return false;
    }
    const fs::path zip = session / zip_relative;
    const fs::path staging = session / staging_relative;
    const fs::path manifest = session / manifest_relative;
    std::error_code canonical_error;
    const auto canonical_session = fs::weakly_canonical(session, canonical_error);
    const auto canonical_zip = fs::weakly_canonical(zip, canonical_error);
    const auto canonical_staging = fs::weakly_canonical(staging, canonical_error);
    const auto canonical_manifest = fs::weakly_canonical(manifest, canonical_error);
    if (canonical_error || !IsContainedValidationPath(canonical_session, canonical_zip) ||
        !IsContainedValidationPath(canonical_session, canonical_staging) ||
        !IsContainedValidationPath(canonical_staging, canonical_manifest) ||
        canonical_manifest != canonical_staging / L"manifest" / L"package-manifest.json") {
        if (error != nullptr) *error = "recovery ZIP/staging/manifest containment failed";
        return false;
    }

    std::vector<PinnedValidationPath> staging_pins;
    std::map<std::string, PinnedTreeFile> staging_files;
    if (!PinValidationTree(staging, &staging_pins, &staging_files, error)) return false;
    PinnedValidationPath zip_pin;
    if (!zip_pin.Open(session, zip, false, error)) return false;
    const auto expected_zip_sha = GetString(package_result, "ZipSHA256");
    if (!IsHexSha256(expected_zip_sha) ||
        _stricmp(zip_pin.Hash().c_str(), expected_zip_sha.c_str()) != 0) {
        if (error != nullptr) *error = "recovery ZIP full SHA-256 differs from package result";
        return false;
    }
    const auto manifest_content = ReadUtf8File(manifest);
    Fields manifest_root;
    Fields client_build;
    std::vector<Fields> manifest_files;
    const std::string reported_package_id = GetString(package_result, "PackageId");
    const bool package_id_safe = !reported_package_id.empty() && reported_package_id.size() <= 160 &&
        std::all_of(reported_package_id.begin(), reported_package_id.end(),
            [](unsigned char character) {
                return std::isalnum(character) != 0 || character == '-' || character == '_' ||
                    character == '.';
            });
    if (!manifest_content || !package_id_safe ||
        !ParseFlatJson(*manifest_content, manifest_root, error) ||
        manifest_root.size() != 4 ||
        GetString(manifest_root, "schemaVersion") != "god2-ultimate-package-manifest-v1" ||
        GetString(manifest_root, "packageId") != reported_package_id ||
        !ParseFlatJson(GetString(manifest_root, "clientBuild"), client_build, error) ||
        client_build.size() != 10 ||
        GetString(client_build, "executable") != "God2_opt.exe" ||
        GetString(client_build, "architecture") != "x86" ||
        GetString(client_build, "fileVersion") != "1.0.0.1" ||
        GetString(client_build, "sha256") != kExactTargetClientSha256 ||
        GetString(client_build, "validationStatus") !=
            "ExactClientIdentityComputedAndValidated" ||
        GetBool(client_build, "exactBindingValidated") != true ||
        !SplitStrictJsonObjectArray(GetString(manifest_root, "files"), &manifest_files) ||
        manifest_files.empty()) {
        if (error != nullptr && error->empty()) *error = "recovery package manifest contract failed";
        return false;
    }
    std::set<std::string> manifest_paths;
    for (const auto& item : manifest_files) {
        const std::string path = GetString(item, "relativePath");
        const auto size = GetInt64(item, "sizeBytes");
        const std::string sha = GetString(item, "sha256");
        const auto disk = staging_files.find(path);
        if (item.size() != 6 || !IsSafeZipInventoryPath(path) || !size || *size < 0 ||
            !IsHexSha256(sha) || !manifest_paths.insert(path).second ||
            disk == staging_files.end() ||
            disk->second.size != static_cast<std::uint64_t>(*size) ||
            _stricmp(disk->second.sha256.c_str(), sha.c_str()) != 0 ||
            GetString(item, "provenance").empty() ||
            GetString(item, "authority").empty() ||
            GetString(item, "schemaVersion").empty()) {
            if (error != nullptr) *error = "recovery manifest file inventory/hash mismatch";
            return false;
        }
    }
    if (staging_files.size() != manifest_paths.size() + 1U ||
        !staging_files.contains("manifest/package-manifest.json")) {
        if (error != nullptr) *error = "recovery staging contains unlisted or missing files";
        return false;
    }
    std::map<std::string, StrictZipEntryIdentity> zip_entries;
    if (!ReadStrictStoredZipInventory(zip, &zip_entries, error) ||
        zip_entries.size() != staging_files.size()) return false;
    for (const auto& [path, disk] : staging_files) {
        const auto zip_entry = zip_entries.find(path);
        if (zip_entry == zip_entries.end() || zip_entry->second.size != disk.size) {
            if (error != nullptr) *error = "recovery ZIP has an extra/missing inventory entry";
            return false;
        }
    }
    std::string portable_error;
    if (!ValidatePortableStoredZip(staging, zip, &portable_error)) {
        if (error != nullptr) *error = "recovery ZIP full content validation failed: " + portable_error;
        return false;
    }
    const fs::path validated_zip_path = zip_pin.Path();
    staging_pins.push_back(std::move(zip_pin));
    if (!RevalidatePins(staging_pins, error)) return false;
    *recovery_zip = validated_zip_path;
    *recovery_sha = expected_zip_sha;
    for (auto& pin : staging_pins) retained_pins->push_back(std::move(pin));
    return true;
}

bool ValidateGuiLiveSessionBinding(const fs::path& captures_root,
                                   const GuiLiveSessionBinding& binding,
                                   fs::path* exact_session,
                                   std::string* error) {
    const bool nonce_valid = binding.challenge_nonce.size() >= 32 &&
        binding.challenge_nonce.size() <= 128 &&
        binding.challenge_nonce == binding.confirmation_nonce &&
        std::all_of(binding.challenge_nonce.begin(), binding.challenge_nonce.end(),
            [](unsigned char character) {
                return std::isalnum(character) != 0 || character == '-';
            });
    if (!nonce_valid || binding.session_directory_handle == nullptr ||
        binding.session_id.empty()) {
        if (error != nullptr) *error = "GUI did not confirm the in-memory live session challenge";
        return false;
    }
    std::error_code canonical_error;
    const auto canonical_root = fs::weakly_canonical(captures_root, canonical_error);
    const auto canonical_session = fs::weakly_canonical(binding.session_path, canonical_error);
    BY_HANDLE_FILE_INFORMATION observed{};
    const auto observed_final = ValidationFinalPath(binding.session_directory_handle);
    const bool identity_matches = GetFileInformationByHandle(
        binding.session_directory_handle, &observed) != FALSE &&
        observed.dwVolumeSerialNumber == binding.volume_serial_number &&
        observed.nFileIndexHigh == binding.file_index_high &&
        observed.nFileIndexLow == binding.file_index_low;
    if (canonical_error || canonical_session.parent_path() != canonical_root ||
        !IsContainedValidationPath(canonical_root, canonical_session) || !identity_matches ||
        !observed_final ||
        _wcsicmp(observed_final->c_str(), binding.final_directory_path.c_str()) != 0 ||
        _wcsicmp(observed_final->c_str(), canonical_session.wstring().c_str()) != 0 ||
        (observed.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (observed.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
        if (error != nullptr) *error = "GUI live session handle/final path identity changed";
        return false;
    }
    if (exact_session != nullptr) *exact_session = canonical_session;
    return true;
}

bool CreateSelfTestJunction(const fs::path& link, const fs::path& target) {
    std::error_code directory_error;
    fs::create_directories(link, directory_error);
    if (directory_error) return false;
    HANDLE handle = CreateFileW(link.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return false;
    const std::wstring substitute = L"\\??\\" + fs::absolute(target).wstring();
    const std::wstring print = fs::absolute(target).wstring();
    struct MountPointBuffer {
        DWORD tag;
        WORD data_length;
        WORD reserved;
        WORD substitute_offset;
        WORD substitute_length;
        WORD print_offset;
        WORD print_length;
        wchar_t path_buffer[2048];
    } buffer{};
    const std::size_t substitute_bytes = substitute.size() * sizeof(wchar_t);
    const std::size_t print_bytes = print.size() * sizeof(wchar_t);
    if (substitute_bytes + print_bytes + 2U * sizeof(wchar_t) > sizeof(buffer.path_buffer)) {
        CloseHandle(handle);
        return false;
    }
    buffer.tag = IO_REPARSE_TAG_MOUNT_POINT;
    buffer.substitute_offset = 0;
    buffer.substitute_length = static_cast<WORD>(substitute_bytes);
    buffer.print_offset = static_cast<WORD>(substitute_bytes + sizeof(wchar_t));
    buffer.print_length = static_cast<WORD>(print_bytes);
    std::memcpy(buffer.path_buffer, substitute.c_str(), substitute_bytes);
    std::memcpy(reinterpret_cast<unsigned char*>(buffer.path_buffer) + buffer.print_offset,
                print.c_str(), print_bytes);
    buffer.data_length = static_cast<WORD>(8U + substitute_bytes + sizeof(wchar_t) +
        print_bytes + sizeof(wchar_t));
    DWORD returned = 0;
    const DWORD input_size = static_cast<DWORD>(buffer.data_length + 8U);
    const bool created = DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, &buffer,
        input_size, nullptr, 0, &returned, nullptr) != FALSE;
    CloseHandle(handle);
    return created;
}

bool FlipFirstStoredZipPayloadByte(const fs::path& zip) {
    std::fstream stream(zip, std::ios::binary | std::ios::in | std::ios::out);
    std::uint32_t signature = 0;
    std::uint16_t ignored16 = 0, name_length = 0, extra_length = 0;
    std::uint32_t ignored32 = 0, compressed = 0;
    if (!ReadValidationZipLittle(stream, &signature) || signature != 0x04034B50U ||
        !ReadValidationZipLittle(stream, &ignored16) ||
        !ReadValidationZipLittle(stream, &ignored16) ||
        !ReadValidationZipLittle(stream, &ignored16) ||
        !ReadValidationZipLittle(stream, &ignored16) ||
        !ReadValidationZipLittle(stream, &ignored16) ||
        !ReadValidationZipLittle(stream, &ignored32) ||
        !ReadValidationZipLittle(stream, &compressed) ||
        !ReadValidationZipLittle(stream, &ignored32) ||
        !ReadValidationZipLittle(stream, &name_length) ||
        !ReadValidationZipLittle(stream, &extra_length) || compressed == 0) return false;
    stream.seekg(static_cast<std::streamoff>(name_length) + extra_length, std::ios::cur);
    const auto position = stream.tellg();
    char value = 0;
    stream.read(&value, 1);
    if (!stream || position < 0) return false;
    value ^= 0x5A;
    stream.seekp(position);
    stream.write(&value, 1);
    stream.flush();
    return stream.good();
}

LiveSecuritySelfTestResult RunLiveSecuritySelfTests(const fs::path& fixture_root) {
    LiveSecuritySelfTestResult result;
    std::error_code cleanup_error;
    fs::remove_all(fixture_root, cleanup_error);
    fs::create_directories(fixture_root / L"captures" / L"bound", cleanup_error);
    fs::create_directories(fixture_root / L"captures" / L"crafted-newer", cleanup_error);
    if (cleanup_error) return result;

    GuiLiveSessionBinding binding;
    binding.challenge_nonce = NewId() + NewId();
    binding.confirmation_nonce = binding.challenge_nonce;
    binding.session_id = "live-security-session";
    binding.session_path = fs::weakly_canonical(fixture_root / L"captures" / L"bound");
    binding.session_directory_handle = CreateFileW(binding.session_path.c_str(),
        FILE_READ_ATTRIBUTES | SYNCHRONIZE, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
        nullptr);
    BY_HANDLE_FILE_INFORMATION directory_identity{};
    const auto directory_final = ValidationFinalPath(binding.session_directory_handle);
    if (binding.session_directory_handle != INVALID_HANDLE_VALUE && directory_final &&
        GetFileInformationByHandle(binding.session_directory_handle, &directory_identity)) {
        binding.final_directory_path = *directory_final;
        binding.volume_serial_number = directory_identity.dwVolumeSerialNumber;
        binding.file_index_high = directory_identity.nFileIndexHigh;
        binding.file_index_low = directory_identity.nFileIndexLow;
        fs::last_write_time(fixture_root / L"captures" / L"crafted-newer",
            fs::file_time_type::clock::now(), cleanup_error);
        fs::path selected;
        std::string bind_error;
        result.crafted_newer_ignored = ValidateGuiLiveSessionBinding(
            fixture_root / L"captures", binding, &selected, &bind_error) &&
            selected == binding.session_path;
    }

    const fs::path junction_target = fixture_root / L"junction-target";
    const fs::path junction = fixture_root / L"crafted-reparse";
    fs::create_directories(junction_target, cleanup_error);
    const bool junction_created = CreateSelfTestJunction(junction, junction_target);
    PinnedValidationPath reparse_pin;
    std::string pin_error;
    result.reparse_rejected = junction_created &&
        !reparse_pin.Open(fixture_root, junction, true, &pin_error);

    const fs::path zip_source = fixture_root / L"zip-source";
    const fs::path zip = fixture_root / L"fixture.zip";
    fs::create_directories(zip_source, cleanup_error);
    WriteUtf8FileAtomic(zip_source / L"evidence.json", "{\"Status\":\"PASS\"}\n");
    std::string zip_error;
    const bool zip_created = CreatePortableStoredZip(zip_source, zip, &zip_error);
    std::map<std::string, StrictZipEntryIdentity> valid_inventory;
    const bool strict_zip_valid = zip_created &&
        ReadStrictStoredZipInventory(zip, &valid_inventory, &zip_error) &&
        valid_inventory.size() == 1 && valid_inventory.contains("evidence.json");
    const std::string self_reported_hash = CalculateFileSha256(zip).value_or("");
    WriteUtf8FileAtomic(zip_source / L"evidence.json", "{\"Status\":\"TAMPERED\"}\n");
    result.zip_self_report_rejected = strict_zip_valid && !self_reported_hash.empty() &&
        CalculateFileSha256(zip).value_or("") == self_reported_hash &&
        !ValidatePortableStoredZip(zip_source, zip, &zip_error);
    WriteUtf8FileAtomic(zip_source / L"evidence.json", "{\"Status\":\"PASS\"}\n");
    const fs::path tampered_zip = fixture_root / L"tampered.zip";
    fs::copy_file(zip, tampered_zip, fs::copy_options::overwrite_existing, cleanup_error);
    std::map<std::string, StrictZipEntryIdentity> tampered_inventory;
    result.zip_tamper_rejected = !cleanup_error && FlipFirstStoredZipPayloadByte(tampered_zip) &&
        !ReadStrictStoredZipInventory(tampered_zip, &tampered_inventory, &zip_error);

    const fs::path pinned_file = fixture_root / L"pinned.json";
    const fs::path replacement_file = fixture_root / L"replacement.json";
    WriteUtf8FileAtomic(pinned_file, "{\"Identity\":1}\n");
    WriteUtf8FileAtomic(replacement_file, "{\"Identity\":2}\n");
    PinnedValidationPath pinned;
    if (pinned.Open(fixture_root, pinned_file, false, &pin_error)) {
        SetLastError(ERROR_SUCCESS);
        const bool replaced = MoveFileExW(replacement_file.c_str(), pinned_file.c_str(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
        const DWORD replace_error = GetLastError();
        result.file_replacement_blocked = !replaced &&
            (replace_error == ERROR_SHARING_VIOLATION || replace_error == ERROR_ACCESS_DENIED);
        result.handle_identity_revalidated = pinned.Revalidate(&pin_error);
    }
    if (binding.session_directory_handle != nullptr &&
        binding.session_directory_handle != INVALID_HANDLE_VALUE)
        CloseHandle(binding.session_directory_handle);
    binding.session_directory_handle = nullptr;
    fs::remove_all(fixture_root, cleanup_error);
    return result;
}

bool ValidateDeepProbeCandidateMap(std::string_view wire) {
    Fields root;
    if (!ParseFlatJson(wire, root, nullptr)) return false;
    const std::string schema = GetString(root, "SchemaVersion");
    const bool legacy = schema == "god2-deep-probe-candidate-map-v1";
    const bool discovery_v2 = schema == "god2-deep-probe-candidate-map-v2";
    // v1 remains identifiable for compatibility diagnostics, but it has no
    // discovery bounds or 14-gate ABI contract and can never bind live
    // authority. Only v2 is promotable in this formal validation path.
    if (legacy || !discovery_v2 || root.size() != 12 ||
        GetString(root, "ClientSHA256") != kExactTargetClientSha256 ||
        GetString(root, "ClientSha256Expected") != kExactTargetClientSha256 ||
        GetString(root, "Architecture") != "x86" ||
        GetString(root, "DiscoveryPolicy") !=
            "ExecutableCodeAndExactSignaturesOnly;NoWritableMemoryScan;NoSensitiveValueLogging" ||
        GetString(root, "UnconfirmedProbePolicy") !=
            "EvidenceBlockedUnconfirmedProbe" ||
        GetInt64(root, "DomainCount") != 25) return false;
    if (discovery_v2) {
        if (GetString(root, "CandidateVerificationEngine") !=
                "BoundedExecutableCallGraphPlusExactCandidateBytesPlusPromotionHardGate" ||
            GetString(root, "CandidateSchemaVersion") !=
                "god2-deep-probe-candidate-v1" ||
            GetString(root, "TargetIdentityVerified") != "true") return false;
        Fields bounds;
        if (!ParseFlatJson(GetString(root, "EngineBounds"), bounds, nullptr) ||
            bounds.size() != 4 ||
            GetInt64(bounds, "ScanRadiusBytes") != 256 ||
            GetInt64(bounds, "MaximumCandidatesPerDomain") != 4 ||
            GetInt64(bounds, "SignatureBytes") != 8 ||
            GetString(bounds, "WritableMemoryScanned") != "false") return false;
    }
    std::vector<Fields> domains;
    if (!SplitStrictJsonObjectArray(GetString(root, "Domains"), &domains) ||
        domains.size() != 25) return false;
    const std::array<std::string_view, 25> expected_names = {
        "Network", "Parser", "Serializer", "Handler", "Object", "Allocation",
        "VTable", "Factory", "ManagerLookup", "Registry", "ResourceDecode",
        "Mutation", "TaintSeed", "FormulaOperand", "Quest", "Map", "Portal",
        "NPC", "Monster", "Battle", "Inventory", "Item", "Skill", "Pet/Mount",
        "Snapshot"
    };
    const std::array<std::string_view, 4> expected_probes = {
        "WinsockTransport", "PacketDecode.FrameBoundary",
        "OutboundEnqueue.FrameBuilder", "Battle.HandlerRecordLength"
    };
    const std::array<std::string_view, 21> expected_seeds = {
        "Parser", "Parser", "Parser", "Parser", "Parser", "Parser", "Parser",
        "Handler", "Parser", "Handler", "Parser", "Parser", "Parser", "Parser",
        "Handler", "Handler", "Serializer", "Parser", "Handler", "Handler", "Parser"
    };
    const std::array<std::string_view, 4> expected_candidates = {
        "[\"send\",\"WSASend\",\"recv\",\"WSARecv\"]",
        "[{\"callsiteRva\":\"0x00078A48\",\"targetRva\":\"0x00078D70\",\"verification\":\"ExactCallTarget\"}]",
        "[{\"targetRva\":\"0x0007FC10\",\"signature\":\"55 8B EC 56 57\",\"verification\":\"ExactInstructionBytes\"}]",
        "[{\"callsiteRvas\":[\"0x00147096\",\"0x001470AE\"],\"targetRva\":\"0x0007F940\",\"verification\":\"BothExactCallTargets\"}]"
    };
    std::set<std::string> unique;
    for (std::size_t index = 0; index < domains.size(); ++index) {
        const auto& domain = domains[index];
        const std::string name = GetString(domain, "Domain");
        if (name != expected_names[index] || !unique.insert(name).second) return false;
        if (index < 4) {
            if (domain.size() != 7 || GetString(domain, "Probe") != expected_probes[index] ||
                GetString(domain, "Status") != "Active" ||
                GetString(domain, "Authority") != "VERIFIED" ||
                GetString(domain, "Candidates") != expected_candidates[index] ||
                GetInt64(domain, "CandidateCount") != (index == 0 ? 4 : 1) ||
                GetString(domain, "VerificationStatus") != "PASS") return false;
        } else if (GetString(domain, "Status") !=
                       "EvidenceBlockedUnconfirmedProbe" ||
                   GetString(domain, "Authority") != "UNKNOWN" ||
                   GetString(domain, "Probe").empty()) {
            return false;
        } else {
            if (domain.size() != 11 ||
                GetString(domain, "InstallationPolicy") !=
                    "NeverInstallUntilAllVerificationGatesPass" ||
                GetString(domain, "SafeNextAction") !=
                    "CollectExactBuildRepeatedTypedRuntimeEvidenceAndVerifyCallingConvention") return false;
            Fields plan;
            if (!ParseFlatJson(GetString(domain, "DiscoveryPlan"), plan, nullptr) ||
                plan.size() != 4 ||
                GetString(plan, "SeedDomain") != expected_seeds[index - 4] ||
                GetString(plan, "Strategy") !=
                    "BoundedDirectCallTargetsFromVerifiedSeed" ||
                GetString(plan, "TypedEvidenceRequired").empty() ||
                GetString(plan, "CausalEvidenceRequired").empty()) return false;
            std::vector<Fields> discovered;
            if (!SplitStrictJsonObjectArray(GetString(domain, "Candidates"), &discovered) ||
                discovered.size() > 4 ||
                GetInt64(domain, "CandidateCount") !=
                    static_cast<std::int64_t>(discovered.size())) return false;
            std::set<std::string> target_rvas;
            for (const auto& candidate : discovered) {
                const std::string callsite = GetString(candidate, "CallsiteRva");
                const std::string target = GetString(candidate, "TargetRva");
                const auto valid_rva = [](std::string_view value) {
                    if (value.size() != 10 || value.substr(0, 2) != "0x") return false;
                    return std::all_of(value.begin() + 2, value.end(), [](unsigned char c) {
                        return std::isxdigit(c) != 0;
                    });
                };
                const auto valid_signature = [](std::string_view value) {
                    if (value.size() != 23) return false;
                    for (std::size_t position = 0; position < value.size(); ++position) {
                        if ((position + 1) % 3 == 0) {
                            if (value[position] != ' ') return false;
                        } else if (std::isxdigit(
                                static_cast<unsigned char>(value[position])) == 0) {
                            return false;
                        }
                    }
                    return true;
                };
                const std::string module_section =
                    GetString(candidate, "ModuleSection");
                const bool valid_section = !module_section.empty() &&
                    module_section.size() <= IMAGE_SIZEOF_SHORT_NAME &&
                    std::all_of(module_section.begin(), module_section.end(),
                        [](unsigned char c) {
                            return std::isalnum(c) != 0 || c == '.' || c == '_' ||
                                c == '$';
                        });
                if (candidate.size() != 14 || !valid_rva(callsite) ||
                    !valid_rva(target) || !target_rvas.insert(target).second ||
                    GetString(candidate, "DiscoveryProvenance") !=
                        "VerifiedSeed:" + std::string(expected_seeds[index - 4]) +
                            ";BoundedExecutableDirectCall" ||
                    !valid_signature(GetString(candidate, "Signature")) ||
                    GetString(candidate, "SignatureMask") !=
                        "FF FF FF FF FF FF FF FF" ||
                    !valid_section ||
                    GetString(candidate, "CallingConventionState") != "UNKNOWN" ||
                    GetString(candidate, "ArgumentContractState") != "UNKNOWN" ||
                    GetString(candidate, "ReturnValueLifetimeState") != "UNKNOWN" ||
                    GetString(candidate, "ThreadContextState") != "UNKNOWN" ||
                    GetString(candidate, "ReentrancyRiskState") != "UNKNOWN" ||
                    GetString(candidate, "Risk") != "High" ||
                    GetString(candidate, "VerificationState") != "CandidateOnly" ||
                    GetString(candidate, "RejectionReason") !=
                        "InsufficientTypedDomainCorrelationAndCausalEvidence") return false;
            }
            Fields contract;
            if (!ParseFlatJson(GetString(domain, "VerificationContract"),
                               contract, nullptr) || contract.size() != 17 ||
                GetString(contract, "ExactTargetIdentity") != "true" ||
                GetString(contract, "ExecutableSection") !=
                    (discovered.empty() ? "false" : "true") ||
                GetString(contract, "ExactCandidateBytes") !=
                    (discovered.empty() ? "false" : "true") ||
                GetString(contract, "CallingConventionVerified") != "false" ||
                GetString(contract, "TypedRuntimeEvidence") != "false" ||
                GetString(contract, "RepeatedCausalObservation") != "false" ||
                GetString(contract, "ContradictionsResolved") != "false" ||
                GetString(contract, "StableObjectOrContext") != "false" ||
                GetString(contract, "VerifiedConsumerOrMutation") != "false" ||
                GetString(contract, "SensitiveMaskContract") != "false" ||
                GetString(contract, "ArgumentContractVerified") != "false" ||
                GetString(contract, "ReturnValueLifetimeVerified") != "false" ||
                GetString(contract, "ThreadContextVerified") != "false" ||
                GetString(contract, "ReentrancyRiskVerified") != "false" ||
                GetInt64(contract, "PromotionGateCount") != 10 ||
                GetInt64(contract, "AbiSafetyGateCount") != 4 ||
                GetInt64(contract, "TotalGateCount") != 14 ||
                GetString(domain, "VerificationStatus") !=
                    (discovered.empty() ? "EvidenceBlockedNoExecutableCallCandidate" :
                     "EvidenceBlockedCandidateRequiresTypedRuntimeVerification")) return false;
        }
    }
    return true;
}

bool DeepProbeCandidateMapValidatorSelfTest() {
    const std::array<std::string_view, 25> names = {
        "Network", "Parser", "Serializer", "Handler", "Object", "Allocation",
        "VTable", "Factory", "ManagerLookup", "Registry", "ResourceDecode",
        "Mutation", "TaintSeed", "FormulaOperand", "Quest", "Map", "Portal",
        "NPC", "Monster", "Battle", "Inventory", "Item", "Skill", "Pet/Mount",
        "Snapshot"
    };
    const std::array<std::string_view, 25> probes = {
        "WinsockTransport", "PacketDecode.FrameBoundary",
        "OutboundEnqueue.FrameBuilder", "Battle.HandlerRecordLength",
        "ObjectResolver", "AllocationProbe", "VTableProbe", "FactoryProbe",
        "ManagerLookupProbe", "RegistryProbe", "ResourceDecodeProbe", "MutationProbe",
        "TaintSeedProbe", "FormulaOperandProbe", "QuestProbe", "MapProbe", "PortalProbe",
        "NPCProbe", "MonsterProbe", "BattleProbe", "InventoryProbe", "ItemProbe",
        "SkillProbe", "PetMountProbe", "SnapshotProbe"
    };
    const std::array<std::string_view, 21> seeds = {
        "Parser", "Parser", "Parser", "Parser", "Parser", "Parser", "Parser",
        "Handler", "Parser", "Handler", "Parser", "Parser", "Parser", "Parser",
        "Handler", "Handler", "Serializer", "Parser", "Handler", "Handler", "Parser"
    };
    const std::array<std::string_view, 4> active_candidates = {
        "[\"send\",\"WSASend\",\"recv\",\"WSARecv\"]",
        "[{\"callsiteRva\":\"0x00078A48\",\"targetRva\":\"0x00078D70\",\"verification\":\"ExactCallTarget\"}]",
        "[{\"targetRva\":\"0x0007FC10\",\"signature\":\"55 8B EC 56 57\",\"verification\":\"ExactInstructionBytes\"}]",
        "[{\"callsiteRvas\":[\"0x00147096\",\"0x001470AE\"],\"targetRva\":\"0x0007F940\",\"verification\":\"BothExactCallTargets\"}]"
    };
    std::ostringstream output;
    output << "{\"SchemaVersion\":\"god2-deep-probe-candidate-map-v2\"," 
           << "\"ClientSHA256\":\"" << kExactTargetClientSha256 << "\"," 
           << "\"ClientSha256Expected\":\"" << kExactTargetClientSha256 << "\"," 
           << "\"Architecture\":\"x86\"," 
           << "\"DiscoveryPolicy\":\"ExecutableCodeAndExactSignaturesOnly;NoWritableMemoryScan;NoSensitiveValueLogging\"," 
           << "\"CandidateVerificationEngine\":\"BoundedExecutableCallGraphPlusExactCandidateBytesPlusPromotionHardGate\"," 
           << "\"UnconfirmedProbePolicy\":\"EvidenceBlockedUnconfirmedProbe\"," 
           << "\"CandidateSchemaVersion\":\"god2-deep-probe-candidate-v1\"," 
           << "\"TargetIdentityVerified\":true," 
           << "\"EngineBounds\":{\"ScanRadiusBytes\":256,\"MaximumCandidatesPerDomain\":4,"
              "\"SignatureBytes\":8,\"WritableMemoryScanned\":false},"
           << "\"DomainCount\":25,\"Domains\":[";
    for (std::size_t index = 0; index < names.size(); ++index) {
        if (index != 0) output << ',';
        if (index < 4) {
            output << "{\"Domain\":\"" << names[index] << "\",\"Probe\":\""
                   << probes[index] << "\",\"Status\":\"Active\","
                   << "\"Authority\":\"VERIFIED\",\"Candidates\":"
                   << active_candidates[index] << ",\"CandidateCount\":"
                   << (index == 0 ? 4 : 1)
                   << ",\"VerificationStatus\":\"PASS\"}";
            continue;
        }
        const bool with_candidate = index == 4;
        output << "{\"Domain\":\"" << names[index] << "\",\"Probe\":\""
               << probes[index]
               << "\",\"Status\":\"EvidenceBlockedUnconfirmedProbe\","
                  "\"Authority\":\"UNKNOWN\",\"DiscoveryPlan\":{\"SeedDomain\":\""
               << seeds[index - 4]
               << "\",\"Strategy\":\"BoundedDirectCallTargetsFromVerifiedSeed\","
                  "\"TypedEvidenceRequired\":\"TypedEvidence\","
                  "\"CausalEvidenceRequired\":\"CausalEvidence\"},\"Candidates\":";
        if (with_candidate) {
            output << "[{\"CallsiteRva\":\"0x00000100\",\"TargetRva\":\"0x00000180\","
                      "\"DiscoveryProvenance\":\"VerifiedSeed:Parser;BoundedExecutableDirectCall\","
                      "\"Signature\":\"55 8B EC 83 EC 08 53 56\","
                      "\"SignatureMask\":\"FF FF FF FF FF FF FF FF\","
                      "\"ModuleSection\":\".text\",\"CallingConventionState\":\"UNKNOWN\","
                      "\"ArgumentContractState\":\"UNKNOWN\","
                      "\"ReturnValueLifetimeState\":\"UNKNOWN\","
                      "\"ThreadContextState\":\"UNKNOWN\","
                      "\"ReentrancyRiskState\":\"UNKNOWN\","
                      "\"Risk\":\"High\",\"VerificationState\":\"CandidateOnly\","
                      "\"RejectionReason\":\"InsufficientTypedDomainCorrelationAndCausalEvidence\"}]";
        } else {
            output << "[]";
        }
        output << ",\"CandidateCount\":" << (with_candidate ? 1 : 0)
               << ",\"VerificationContract\":{\"ExactTargetIdentity\":true,"
                  "\"ExecutableSection\":" << (with_candidate ? "true" : "false")
               << ",\"ExactCandidateBytes\":" << (with_candidate ? "true" : "false")
               << ",\"CallingConventionVerified\":false,\"TypedRuntimeEvidence\":false,"
                  "\"RepeatedCausalObservation\":false,\"ContradictionsResolved\":false,"
                  "\"StableObjectOrContext\":false,\"VerifiedConsumerOrMutation\":false,"
                  "\"SensitiveMaskContract\":false,\"ArgumentContractVerified\":false,"
                  "\"ReturnValueLifetimeVerified\":false,\"ThreadContextVerified\":false,"
                  "\"ReentrancyRiskVerified\":false,\"PromotionGateCount\":10,"
                  "\"AbiSafetyGateCount\":4,\"TotalGateCount\":14},"
                  "\"VerificationStatus\":\""
               << (with_candidate ?
                    "EvidenceBlockedCandidateRequiresTypedRuntimeVerification" :
                    "EvidenceBlockedNoExecutableCallCandidate")
               << "\",\"InstallationPolicy\":\"NeverInstallUntilAllVerificationGatesPass\","
                  "\"SafeNextAction\":\"CollectExactBuildRepeatedTypedRuntimeEvidenceAndVerifyCallingConvention\"}";
    }
    output << "]}";
    const std::string valid = output.str();
    if (!ValidateDeepProbeCandidateMap(valid)) return false;
    std::string legacy_bypass = valid;
    const std::string v2_schema = "god2-deep-probe-candidate-map-v2";
    const auto schema_at = legacy_bypass.find(v2_schema);
    if (schema_at == std::string::npos) return false;
    legacy_bypass.replace(schema_at, v2_schema.size(),
                          "god2-deep-probe-candidate-map-v1");
    if (ValidateDeepProbeCandidateMap(legacy_bypass)) return false;
    std::string unsafe_promotion = valid;
    const std::string blocked = "\"Status\":\"EvidenceBlockedUnconfirmedProbe\"";
    const auto blocked_at = unsafe_promotion.find(blocked);
    if (blocked_at == std::string::npos) return false;
    unsafe_promotion.replace(blocked_at, blocked.size(), "\"Status\":\"Active\"");
    if (ValidateDeepProbeCandidateMap(unsafe_promotion)) return false;
    std::string wrong_identity = valid;
    const auto identity_at = wrong_identity.find("\"TargetIdentityVerified\":true");
    if (identity_at == std::string::npos) return false;
    wrong_identity.replace(identity_at, std::strlen("\"TargetIdentityVerified\":true"),
                           "\"TargetIdentityVerified\":false");
    if (ValidateDeepProbeCandidateMap(wrong_identity)) return false;
    const std::array<std::pair<std::string_view, bool>, 14> verification_gates = {
        std::pair{"ExactTargetIdentity", true},
        std::pair{"ExecutableSection", true},
        std::pair{"ExactCandidateBytes", true},
        std::pair{"CallingConventionVerified", false},
        std::pair{"TypedRuntimeEvidence", false},
        std::pair{"RepeatedCausalObservation", false},
        std::pair{"ContradictionsResolved", false},
        std::pair{"StableObjectOrContext", false},
        std::pair{"VerifiedConsumerOrMutation", false},
        std::pair{"SensitiveMaskContract", false},
        std::pair{"ArgumentContractVerified", false},
        std::pair{"ReturnValueLifetimeVerified", false},
        std::pair{"ThreadContextVerified", false},
        std::pair{"ReentrancyRiskVerified", false}
    };
    for (const auto& [gate, original_value] : verification_gates) {
        std::string unsafe_gate = valid;
        const std::string original = "\"" + std::string(gate) + "\":" +
            (original_value ? "true" : "false");
        const auto gate_at = unsafe_gate.find(original);
        if (gate_at == std::string::npos) return false;
        unsafe_gate.replace(gate_at, original.size(),
            "\"" + std::string(gate) + "\":" +
                (original_value ? "false" : "true"));
        if (ValidateDeepProbeCandidateMap(unsafe_gate)) return false;
    }
    return true;
}

bool ValidateEnhancedStoppedStatusV2(std::string_view wire) {
    if (!wire.ends_with('\n')) return false;
    wire.remove_suffix(1);
    if (wire.empty() || wire.find('\r') != std::string_view::npos ||
        wire.find('\n') != std::string_view::npos ||
        wire.find('\0') != std::string_view::npos) return false;
    Fields fields;
    if (!ParseFlatJson(wire, fields, nullptr) || fields.size() != 46U) return false;
    constexpr std::array<std::string_view, 46> keys{
        "SchemaVersion", "UpdatedAtUtc", "Requested", "Status", "Detail",
        "WasEverAttached", "ProbeReady", "StrictUnloadVerified", "FailureKind",
        "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
        "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
        "TargetIdentityVerified", "InjectorPresent", "ProbePresent",
        "InjectorPayloadValidated", "ProbePayloadValidated", "TargetExecutable",
        "TargetArchitecture", "ProbeScope", "ProbeDomainCount", "ProbeDomains",
        "ConfirmedExactBuildDomainCount", "ConfirmedExactBuildDomains",
        "CandidateOnlyDomainCount", "CandidateOnlyStatus",
        "DeepProbeCandidateMapSchema", "CandidatePromotionGateCount",
        "CandidateAbiSafetyGateCount", "CandidateActivationGateCount",
        "CandidateAbiSafetyContract", "SemanticEventContract",
        "SharedTransportContract", "InjectionSequence", "ReadinessHandshake",
        "PayloadStaging", "PayloadDacl", "PayloadMandatoryLabel",
        "SemanticIpcBinding", "PayloadLaunchMode", "UnloadPolicy", "Apis",
        "InputAndWindowHooks"};
    for (const auto key : keys)
        if (!fields.contains(std::string(key))) return false;

    constexpr std::array<std::string_view, 15> boolean_keys{
        "Requested", "WasEverAttached", "ProbeReady", "StrictUnloadVerified",
        "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
        "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
        "TargetIdentityVerified", "InjectorPresent", "ProbePresent",
        "InjectorPayloadValidated", "ProbePayloadValidated"};
    for (const auto key : boolean_keys) {
        const std::string value = GetString(fields, key);
        if (value != "true" && value != "false") return false;
    }
    constexpr std::array<std::string_view, 6> numeric_keys{
        "ProbeDomainCount", "ConfirmedExactBuildDomainCount",
        "CandidateOnlyDomainCount", "CandidatePromotionGateCount",
        "CandidateAbiSafetyGateCount", "CandidateActivationGateCount"};
    for (const auto key : numeric_keys)
        if (!GetInt64(fields, key)) return false;

    std::vector<std::pair<std::string, std::string>> ordered;
    ordered.reserve(keys.size());
    for (const auto key : keys)
        ordered.emplace_back(key, GetString(fields, key));
    std::set<std::string> raw_keys;
    for (const auto key : boolean_keys) raw_keys.emplace(key);
    for (const auto key : numeric_keys) raw_keys.emplace(key);
    if (wire != MakeJsonObject(ordered, raw_keys)) return false;

    return GetString(fields, "SchemaVersion") == "god2-enhanced-capture-status-v2" &&
        !GetString(fields, "UpdatedAtUtc").empty() &&
        GetString(fields, "Requested") == "true" &&
        GetString(fields, "Status") == "Stopped" &&
        GetString(fields, "WasEverAttached") == "true" &&
        GetString(fields, "ProbeReady") == "false" &&
        GetString(fields, "StrictUnloadVerified") == "true" &&
        GetString(fields, "FailureKind").empty() &&
        GetString(fields, "InjectionAttempted") == "true" &&
        GetString(fields, "ModuleWasEverLoaded") == "true" &&
        GetString(fields, "ModuleLoadStateVerified") == "true" &&
        GetString(fields, "ModuleSnapshotVerified") == "true" &&
        GetString(fields, "ModuleAbsent") == "true" &&
        GetString(fields, "TargetProcessExited") == "false" &&
        GetString(fields, "TargetIdentityVerified") == "true" &&
        GetString(fields, "InjectorPresent") == "true" &&
        GetString(fields, "ProbePresent") == "true" &&
        GetString(fields, "InjectorPayloadValidated") == "true" &&
        GetString(fields, "ProbePayloadValidated") == "true" &&
        GetString(fields, "TargetExecutable") == "God2_opt.exe" &&
        GetString(fields, "TargetArchitecture") == "x86" &&
        GetString(fields, "ProbeScope") ==
            "WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded" &&
        GetInt64(fields, "ProbeDomainCount") == 25 &&
        GetString(fields, "ProbeDomains") ==
            "Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot" &&
        GetInt64(fields, "ConfirmedExactBuildDomainCount") == 4 &&
        GetString(fields, "ConfirmedExactBuildDomains") ==
            "Network,Parser,Serializer,Handler" &&
        GetInt64(fields, "CandidateOnlyDomainCount") == 21 &&
        GetString(fields, "CandidateOnlyStatus") ==
            "EvidenceBlockedUnconfirmedProbe" &&
        GetString(fields, "DeepProbeCandidateMapSchema") ==
            "god2-deep-probe-candidate-map-v2" &&
        GetInt64(fields, "CandidatePromotionGateCount") == 10 &&
        GetInt64(fields, "CandidateAbiSafetyGateCount") == 4 &&
        GetInt64(fields, "CandidateActivationGateCount") == 14 &&
        GetString(fields, "CandidateAbiSafetyContract") ==
            "ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk" &&
        GetString(fields, "SemanticEventContract") ==
            "God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes" &&
        GetString(fields, "SharedTransportContract") ==
            "VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag" &&
        GetString(fields, "InjectionSequence") ==
            "PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread" &&
        GetString(fields, "ReadinessHandshake") == "God2TraceProbeWaitReady" &&
        GetString(fields, "PayloadStaging") ==
            "ProtectedProgramDataRandomDirectory" &&
        GetString(fields, "PayloadDacl") ==
            "Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent" &&
        GetString(fields, "PayloadMandatoryLabel") == "MediumNoWriteUp" &&
        GetString(fields, "SemanticIpcBinding") ==
            "DuplicatedTargetHandlesV2;NoNamedObjectAuthority" &&
        GetString(fields, "PayloadLaunchMode") ==
            "AlreadyElevatedCreateProcessW" &&
        GetString(fields, "UnloadPolicy") ==
            "QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped" &&
        GetString(fields, "Apis") ==
            "send,WSASend,recv,WSARecv,OverlappedWSARecvCompletionRoutine,WSAGetOverlappedResult,GetQueuedCompletionStatus,GetQueuedCompletionStatusEx,PostDecrypt,PreEncrypt,HandlerDecoded" &&
        GetString(fields, "InputAndWindowHooks") == "Disabled";
}

std::string MakeEnhancedStoppedStatusV2Fixture() {
    return MakeJsonObject({
        {"SchemaVersion", "god2-enhanced-capture-status-v2"},
        {"UpdatedAtUtc", "2026-08-09T00:00:00.000Z"},
        {"Requested", "true"}, {"Status", "Stopped"},
        {"Detail", "typed-detail-is-informational-only"},
        {"WasEverAttached", "true"}, {"ProbeReady", "false"},
        {"StrictUnloadVerified", "true"}, {"FailureKind", ""},
        {"InjectionAttempted", "true"}, {"ModuleWasEverLoaded", "true"},
        {"ModuleLoadStateVerified", "true"},
        {"ModuleSnapshotVerified", "true"}, {"ModuleAbsent", "true"},
        {"TargetProcessExited", "false"}, {"TargetIdentityVerified", "true"},
        {"InjectorPresent", "true"}, {"ProbePresent", "true"},
        {"InjectorPayloadValidated", "true"}, {"ProbePayloadValidated", "true"},
        {"TargetExecutable", "God2_opt.exe"}, {"TargetArchitecture", "x86"},
        {"ProbeScope", "WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded"},
        {"ProbeDomainCount", "25"},
        {"ProbeDomains", "Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot"},
        {"ConfirmedExactBuildDomainCount", "4"},
        {"ConfirmedExactBuildDomains", "Network,Parser,Serializer,Handler"},
        {"CandidateOnlyDomainCount", "21"},
        {"CandidateOnlyStatus", "EvidenceBlockedUnconfirmedProbe"},
        {"DeepProbeCandidateMapSchema", "god2-deep-probe-candidate-map-v2"},
        {"CandidatePromotionGateCount", "10"},
        {"CandidateAbiSafetyGateCount", "4"},
        {"CandidateActivationGateCount", "14"},
        {"CandidateAbiSafetyContract", "ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk"},
        {"SemanticEventContract", "God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes"},
        {"SharedTransportContract", "VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag"},
        {"InjectionSequence", "PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread"},
        {"ReadinessHandshake", "God2TraceProbeWaitReady"},
        {"PayloadStaging", "ProtectedProgramDataRandomDirectory"},
        {"PayloadDacl", "Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent"},
        {"PayloadMandatoryLabel", "MediumNoWriteUp"},
        {"SemanticIpcBinding", "DuplicatedTargetHandlesV2;NoNamedObjectAuthority"},
        {"PayloadLaunchMode", "AlreadyElevatedCreateProcessW"},
        {"UnloadPolicy", "QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped"},
        {"Apis", "send,WSASend,recv,WSARecv,OverlappedWSARecvCompletionRoutine,WSAGetOverlappedResult,GetQueuedCompletionStatus,GetQueuedCompletionStatusEx,PostDecrypt,PreEncrypt,HandlerDecoded"},
        {"InputAndWindowHooks", "Disabled"}
    }, {"Requested", "WasEverAttached", "ProbeReady", "StrictUnloadVerified",
        "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
        "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
        "TargetIdentityVerified", "InjectorPresent", "ProbePresent",
        "InjectorPayloadValidated", "ProbePayloadValidated", "ProbeDomainCount",
        "ConfirmedExactBuildDomainCount", "CandidateOnlyDomainCount",
        "CandidatePromotionGateCount", "CandidateAbiSafetyGateCount",
        "CandidateActivationGateCount"}) + "\n";
}

bool EnhancedStoppedStatusV2ValidatorSelfTest() {
    const std::string valid = MakeEnhancedStoppedStatusV2Fixture();
    if (!ValidateEnhancedStoppedStatusV2(valid)) return false;
    const auto rejected = [&](std::string value) {
        return !ValidateEnhancedStoppedStatusV2(value);
    };
    std::string quoted = valid;
    auto at = quoted.find("\"ProbeReady\":false");
    if (at == std::string::npos) return false;
    quoted.replace(at, std::strlen("\"ProbeReady\":false"),
                   "\"ProbeReady\":\"false\"");
    std::string unknown = valid;
    unknown.insert(1, "\"Unknown\":false,");
    std::string duplicate = valid;
    duplicate.insert(1, "\"SchemaVersion\":\"god2-enhanced-capture-status-v2\",");
    std::string ready = valid;
    at = ready.find("\"ProbeReady\":false");
    if (at == std::string::npos) return false;
    ready.replace(at, std::strlen("\"ProbeReady\":false"),
                  "\"ProbeReady\":true");
    const std::string legacy =
        "{\"Requested\":true,\"Status\":\"Stopped\","
        "\"WasEverAttached\":true,\"ProbeReady\":true,"
        "\"FailureKind\":\"\",\"Detail\":\"DETACHED legacy spoof\"}\n";
    return rejected(quoted) && rejected(unknown) && rejected(duplicate) &&
        rejected(ready) && rejected(legacy);
}

int RunUltimateLiveValidation(const fs::path& package_root) {
    if (!IsAdministrator()) {
        Print("Ultimate live validation requires an elevated process", true);
        return ERROR_ELEVATION_REQUIRED;
    }
    std::error_code root_error;
    const auto root = fs::weakly_canonical(package_root, root_error);
    const auto executable = fs::weakly_canonical(ExecutablePath(), root_error);
    if (root_error || root.empty() || executable.empty() ||
        !IsContainedValidationPath(root, executable) || executable.parent_path() != root) {
        Print("live validation package root does not own the running executable", true);
        return kExitUsage;
    }
    const DWORD root_attributes = GetFileAttributesW(root.c_str());
    if (root_attributes == INVALID_FILE_ATTRIBUTES ||
        (root_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
        Print("live validation package root is unavailable or is a reparse point", true);
        return kExitUsage;
    }
    GuiLiveSessionBinding live_binding;
    live_binding.challenge_nonce = NewId() + NewId();
    struct LiveBindingHandleCloser {
        GuiLiveSessionBinding* binding = nullptr;
        ~LiveBindingHandleCloser() {
            if (binding != nullptr && binding->session_directory_handle != nullptr &&
                binding->session_directory_handle != INVALID_HANDLE_VALUE) {
                CloseHandle(binding->session_directory_handle);
                binding->session_directory_handle = nullptr;
            }
        }
    } live_binding_closer{&live_binding};
    const int gui_result = RunGuiApplication(GetModuleHandleW(nullptr), SW_SHOWNORMAL,
                                             &live_binding);
    fs::path session;
    std::string live_binding_error;
    const bool session_bound = ValidateGuiLiveSessionBinding(
        LocalDataRoot() / L"Captures", live_binding, &session, &live_binding_error);

    std::vector<PinnedValidationPath> validation_pins;
    const auto pin = [&](const fs::path& path, bool directory) {
        if (!session_bound) return false;
        PinnedValidationPath value;
        if (!value.Open(session, path, directory, &live_binding_error)) return false;
        validation_pins.push_back(std::move(value));
        return true;
    };
    const bool session_read_locked = pin(session, true);
    const bool session_manifest_locked = pin(session / L"session.json", false);
    const bool identity_locked = pin(session / L"reports" / L"client-build-identity.json", false);
    const bool health_locked = pin(session / L"reports" / L"semantic-shared-ring.json", false);
    const fs::path segment_manifest_path = session / L"reports" /
        L"semantic-segment-manifest.json";
    const bool segment_manifest_locked = pin(segment_manifest_path, false);
    const bool enhanced_locked = pin(session / L"reports" / L"enhanced-capture-status.json", false);
    const bool package_locked = pin(
        session / L"reports" / L"ultimate-recovery-package-result.json", false);
    const bool semantic_locked = pin(session / L"raw" / L"semantic-events.jsonl", false);
    const fs::path candidate_map_path = session / L"raw" / L"enhanced-x86" /
        L"deep-probe-candidate-map.json";
    const bool candidate_map_locked = pin(candidate_map_path, false);

    Fields session_manifest;
    Fields identity;
    Fields health;
    Fields segment_manifest;
    Fields enhanced;
    Fields package_result;
    const auto read_flat = [](const fs::path& path, Fields* fields) {
        const auto text = ReadUtf8File(path);
        return text && ParseFlatJson(*text, *fields, nullptr);
    };
    const bool session_manifest_read = session_manifest_locked && read_flat(
        session / L"session.json", &session_manifest);
    const bool identity_read = identity_locked && read_flat(
        session / L"reports" / L"client-build-identity.json", &identity);
    const bool health_read = health_locked && read_flat(
        session / L"reports" / L"semantic-shared-ring.json", &health);
    const bool segment_manifest_read = segment_manifest_locked && read_flat(
        segment_manifest_path, &segment_manifest);
    const auto enhanced_wire = enhanced_locked ? ReadUtf8File(
        session / L"reports" / L"enhanced-capture-status.json") :
        std::optional<std::string>{};
    const bool enhanced_read = enhanced_wire &&
        ParseFlatJson(*enhanced_wire, enhanced, nullptr);
    const bool package_read = package_locked && read_flat(
        session / L"reports" / L"ultimate-recovery-package-result.json", &package_result);
    const bool exact_session_manifest = session_manifest_read &&
        GetString(session_manifest, "SessionId") == live_binding.session_id;

    const std::uint64_t process_id = static_cast<std::uint64_t>(
        std::max<std::int64_t>(0, GetInt64(identity, "ProcessId").value_or(0)));
    const bool exact_identity = identity_read && GetString(identity, "Status") == "PASS" &&
        GetString(identity, "PathMatches") == "true" && GetString(identity, "Architecture") == "x86" &&
        GetString(identity, "FileVersion") == "1.0.0.1" &&
        GetString(identity, "SHA256") == kExactTargetClientSha256 && process_id != 0 &&
        GetInt64(identity, "ProcessCreationTime").value_or(0) > 0;
    const auto attached_process_id = GetInt64(health, "AttachedTargetProcessId");
    const auto producer_process_id = GetInt64(health, "ProducerProcessId");
    const auto attempted_count = GetInt64(health, "Attempted");
    const auto accepted_count = GetInt64(health, "Accepted");
    const auto dropped_count = GetInt64(health, "Dropped");
    const auto sampled_count = GetInt64(health, "Sampled");
    const auto consumed_count = GetInt64(health, "Consumed");
    const auto invalid_count = GetInt64(health, "InvalidPayloads");
    const auto high_water = GetInt64(health, "HighWaterMark");
    const auto consumer_lag = GetInt64(health, "ConsumerLag");
    const auto write_failures = GetInt64(health, "WriteFailures");
    bool transport_complete = health_read &&
        GetString(health, "SchemaVersion") == "god2-semantic-shared-ring-health-v4" &&
        !GetString(health, "UpdatedAtUtc").empty() &&
        GetString(health, "Lifecycle") == "StoppedAndDrained" &&
        GetBool(health, "TransportReady") == false &&
        GetBool(health, "ConsumerIoFailure") == false &&
        GetInt64(health, "ProducerReady") == 1 &&
        GetInt64(health, "ProducerClosed") == 1 &&
        GetInt64(health, "ConsumerReady") == 0 &&
        GetInt64(health, "ConsumerClosed") == 1 &&
        GetInt64(health, "ConsumerFailure") == 0 &&
        attached_process_id && producer_process_id &&
        *attached_process_id == static_cast<std::int64_t>(process_id) &&
        *producer_process_id == static_cast<std::int64_t>(process_id) &&
        attempted_count && accepted_count && dropped_count && sampled_count &&
        consumed_count && invalid_count && high_water && consumer_lag && write_failures &&
        *attempted_count > 0 && *accepted_count > 0 &&
        *dropped_count >= 0 && *sampled_count == *dropped_count &&
        *attempted_count == *accepted_count + *dropped_count &&
        *consumed_count == *accepted_count && *invalid_count == 0 &&
        *high_water >= 0 && *high_water <= 256 &&
        *consumer_lag == 0 && *write_failures == 0;
    std::int64_t lane_accepted_total = 0;
    std::int64_t lane_dropped_total = 0;
    std::int64_t lane_sampled_total = 0;
    for (std::uint32_t priority = 0; priority < shared::kSemanticPriorityCount;
         ++priority) {
        const std::string prefix = "Lane0" + std::to_string(priority);
        const auto lane_accepted = GetInt64(health, prefix + "Accepted");
        const auto lane_consumed = GetInt64(health, prefix + "Consumed");
        const auto lane_dropped = GetInt64(health, prefix + "Dropped");
        const auto lane_sampled = GetInt64(health, prefix + "Sampled");
        const auto first_dropped = GetInt64(health, prefix + "FirstDroppedSequence");
        const auto last_dropped = GetInt64(health, prefix + "LastDroppedSequence");
        const auto drop_reason = GetInt64(health, prefix + "LastDropReason");
        const auto lane_high_water = GetInt64(health, prefix + "HighWaterMark");
        const auto lane_lag = GetInt64(health, prefix + "ConsumerLag");
        const bool no_loss = lane_dropped && lane_sampled && first_dropped &&
            last_dropped && drop_reason && *lane_dropped == 0 &&
            *lane_sampled == 0 && *first_dropped == 0 && *last_dropped == 0 &&
            *drop_reason == 0;
        const bool sampled_loss = lane_dropped && lane_sampled && first_dropped &&
            last_dropped && drop_reason && *lane_dropped > 0 &&
            *lane_sampled == *lane_dropped && *first_dropped > 0 &&
            *last_dropped >= *first_dropped && *drop_reason == 10;
        if (!lane_accepted || !lane_consumed || !lane_dropped || !lane_sampled ||
            !lane_high_water || !lane_lag || *lane_accepted < 0 ||
            *lane_consumed != *lane_accepted || *lane_high_water < 0 ||
            *lane_high_water > 64 || *lane_lag != 0 ||
            (priority < 2 ? !no_loss : !(no_loss || sampled_loss))) {
            transport_complete = false;
            continue;
        }
        lane_accepted_total += *lane_accepted;
        lane_dropped_total += *lane_dropped;
        lane_sampled_total += *lane_sampled;
    }
    std::int64_t domain_accepted_total = 0;
    std::int64_t domain_dropped_total = 0;
    for (std::uint32_t domain = 0; domain < shared::kSemanticDomainCount; ++domain) {
        const std::string prefix = "Domain" +
            (domain < 10 ? std::string("0") : std::string()) + std::to_string(domain);
        const auto domain_accepted = GetInt64(health, prefix + "Accepted");
        const auto domain_dropped = GetInt64(health, prefix + "Dropped");
        const auto first_dropped = GetInt64(health, prefix + "FirstDroppedSequence");
        const auto last_dropped = GetInt64(health, prefix + "LastDroppedSequence");
        const auto drop_reason = GetInt64(health, prefix + "LastDropReason");
        const auto domain_high_water = GetInt64(health, prefix + "HighWaterMark");
        const auto domain_lag = GetInt64(health, prefix + "ConsumerLag");
        const auto domain_write_failures =
            GetInt64(health, prefix + "WriteFailures");
        const bool no_loss = domain_dropped && first_dropped && last_dropped &&
            drop_reason && *domain_dropped == 0 && *first_dropped == 0 &&
            *last_dropped == 0 && *drop_reason == 0;
        const bool sampled_loss = domain_dropped && first_dropped && last_dropped &&
            drop_reason && *domain_dropped > 0 && *first_dropped > 0 &&
            *last_dropped >= *first_dropped && *drop_reason == 10;
        if (!domain_accepted || !domain_dropped || !first_dropped || !last_dropped ||
            !drop_reason || !domain_high_water || !domain_lag || !domain_write_failures ||
            *domain_accepted < 0 || !(no_loss || sampled_loss) ||
            *domain_high_water < 0 || *domain_high_water > 256 || *domain_lag != 0 ||
            *domain_write_failures != 0) {
            transport_complete = false;
            continue;
        }
        domain_accepted_total += *domain_accepted;
        domain_dropped_total += *domain_dropped;
    }
    transport_complete = transport_complete && accepted_count && dropped_count &&
        sampled_count && domain_accepted_total == *accepted_count &&
        domain_dropped_total == *dropped_count &&
        lane_accepted_total == *accepted_count &&
        lane_dropped_total == *dropped_count &&
        lane_sampled_total == *sampled_count;
    const bool detach_complete = enhanced_read && enhanced_wire &&
        ValidateEnhancedStoppedStatusV2(*enhanced_wire);

    std::uint64_t semantic_count = 0;
    std::uint64_t rejected_semantic = 0;
    std::uint64_t diagnostics = 0;
    std::uint64_t parser_reads = 0;
    std::uint64_t serializer_writes = 0;
    std::uint64_t handler_invocations = 0;
    std::set<std::uint32_t> event_processes;
    std::set<std::string> event_sessions;
    std::set<std::string> event_builds;
    std::set<std::string> event_ids;
    std::set<std::string> diagnostic_domains;
    bool semantic_event_contract_complete = true;
    if (semantic_locked && exact_session_manifest) {
        std::ifstream input(session / L"raw" / L"semantic-events.jsonl", std::ios::binary);
        std::string line;
        while (std::getline(input, line)) {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (line.empty()) continue;
            const auto event = ReadSemanticEvent(line);
            if (!event.success || event.read_from_v1 || event.event.event_id.empty() ||
                event.event.sequence == 0 || event.event.thread_id == 0 ||
                event.event.process_id == 0 || event.event.timestamp.empty() ||
                event.event.module_id != "God2_opt.exe" ||
                event.event.sensitive_mask_status != "NotSensitive" ||
                !event_ids.insert(event.event.event_id).second) {
                ++rejected_semantic;
                continue;
            }
            ++semantic_count;
            event_processes.insert(event.event.process_id);
            event_sessions.insert(event.event.session_id);
            event_builds.insert(event.event.client_build_id);
            switch (event.event.event_type) {
            case UltimateSemanticEventType::ProbeDiagnostic: {
                Fields payload;
                const bool parsed = ParseFlatJson(event.event.payload, payload, nullptr);
                const std::string domain = GetString(payload, "Domain");
                const bool active = domain == "Network" || domain == "Parser" ||
                    domain == "Serializer" || domain == "Handler";
                const bool expected_status = active ?
                    GetString(payload, "Status") == "Active" &&
                        event.event.authority_hint == UltimateAuthority::Verified :
                    GetString(payload, "Status") == "EvidenceBlockedUnconfirmedProbe" &&
                        event.event.authority_hint == UltimateAuthority::Unknown;
                if (!parsed || domain.empty() || !expected_status ||
                    !diagnostic_domains.insert(domain).second) {
                    semantic_event_contract_complete = false;
                }
                ++diagnostics;
                break;
            }
            case UltimateSemanticEventType::ParserRead:
                semantic_event_contract_complete = semantic_event_contract_complete &&
                    event.event.authority_hint == UltimateAuthority::Verified;
                ++parser_reads;
                break;
            case UltimateSemanticEventType::SerializerWrite:
                semantic_event_contract_complete = semantic_event_contract_complete &&
                    event.event.authority_hint == UltimateAuthority::Verified;
                ++serializer_writes;
                break;
            case UltimateSemanticEventType::HandlerInvocation:
                semantic_event_contract_complete = semantic_event_contract_complete &&
                    event.event.authority_hint == UltimateAuthority::Verified;
                ++handler_invocations;
                break;
            default: break;
            }
        }
    }
    const std::string session_id = exact_session_manifest ? live_binding.session_id : "";
    const auto semantic_sha = semantic_locked ? CalculateFileSha256(
        session / L"raw" / L"semantic-events.jsonl").value_or("") : "";
    const bool segment_manifest_bound = segment_manifest_read &&
        GetString(segment_manifest, "SchemaVersion") ==
            "god2-semantic-segment-manifest-v1" &&
        GetString(segment_manifest, "Status") == "PASS" &&
        GetBool(segment_manifest, "SequenceContinuity") == true &&
        GetString(segment_manifest, "MergedPath") == "raw/semantic-events.jsonl" &&
        !semantic_sha.empty() && GetString(segment_manifest, "MergedSHA256") == semantic_sha &&
        GetInt64(segment_manifest, "SegmentMaximumBytes").value_or(0) > 0 &&
        GetInt64(segment_manifest, "SegmentMaximumRecords").value_or(0) > 0 &&
        GetInt64(segment_manifest, "SegmentCount").value_or(0) > 0 &&
        GetInt64(segment_manifest, "RecordCount").value_or(-1) ==
            static_cast<std::int64_t>(semantic_count);
    transport_complete = transport_complete && consumed_count &&
        semantic_count == static_cast<std::uint64_t>(*consumed_count) &&
        segment_manifest_bound;
    const std::set<std::string> expected_diagnostic_domains = {
        "Network", "Parser", "Serializer", "Handler", "Object", "Allocation", "VTable",
        "Factory", "ManagerLookup", "Registry", "ResourceDecode", "Mutation", "TaintSeed",
        "FormulaOperand", "Quest", "Map", "Portal", "NPC", "Monster", "Battle",
        "Inventory", "Item", "Skill", "Pet/Mount", "Snapshot"
    };
    const bool semantic_bound = semantic_count != 0 && rejected_semantic == 0 &&
        semantic_event_contract_complete && diagnostics == 25 &&
        diagnostic_domains == expected_diagnostic_domains &&
        parser_reads != 0 && serializer_writes != 0 && handler_invocations != 0 &&
        event_processes.size() == 1 && *event_processes.begin() == process_id &&
        event_sessions.size() == 1 && *event_sessions.begin() == session_id &&
        event_builds.size() == 1 && *event_builds.begin() == kExactTargetClientSha256;
    const auto candidate_map = candidate_map_locked ? ReadUtf8File(candidate_map_path) :
        std::optional<std::string>{};
    const bool candidate_map_bound = candidate_map_locked && candidate_map &&
        ValidateDeepProbeCandidateMap(*candidate_map);

    fs::path recovery_zip;
    std::string recovery_sha;
    bool recovery_package_bound = package_read &&
        GetString(package_result, "SchemaVersion") == "god2-ultimate-session-package-result-v1" &&
        GetString(package_result, "SessionId") == session_id &&
        GetString(package_result, "Status") == "PASS" &&
        GetString(package_result, "ArtifactWritten") == "true" &&
        GetString(package_result, "EvidenceComplete") == "true" &&
        GetString(package_result, "ReportWritten") == "true" &&
        GetString(package_result, "SemanticEvidenceIncomplete") == "false" &&
        GetString(package_result, "ClientIdentityStatus") ==
            "ExactClientIdentityComputedAndValidated";
    if (recovery_package_bound) {
        recovery_package_bound = ValidateUltimateRecoveryZipBound(session, package_result,
            &recovery_zip, &recovery_sha, &validation_pins, &live_binding_error);
    }
    fs::path revalidated_session;
    const bool session_identity_revalidated = session_read_locked && exact_session_manifest &&
        ValidateGuiLiveSessionBinding(LocalDataRoot() / L"Captures", live_binding,
            &revalidated_session, &live_binding_error) && revalidated_session == session &&
        RevalidatePins(validation_pins, &live_binding_error);
    const auto executable_sha = CalculateFileSha256(executable).value_or("");
    const bool engine_identity = FileVersion(executable) == "1.3.0.0" && !executable_sha.empty();
    const bool capture_core_pass = gui_result == 0 && session_bound &&
        session_identity_revalidated &&
        exact_identity && transport_complete && detach_complete && semantic_bound &&
        candidate_map_bound && recovery_package_bound && engine_identity;
    // Candidate discovery and four confirmed capture-core domains are not deep
    // runtime promotion evidence.  A future current-session promotion ledger
    // must bind all required domains before this runner may emit a deep PASS.
    constexpr std::uint64_t deep_runtime_required_domain_count = 25;
    const std::uint64_t deep_runtime_verified_domain_count = 0;
    const bool deep_runtime_pass = capture_core_pass &&
        deep_runtime_verified_domain_count == deep_runtime_required_domain_count;
    const bool pass = capture_core_pass && deep_runtime_pass;
    const std::string final_status = pass ?
        "ULTIMATE_LIVE_DEEP_RECOVERY_EVIDENCE_PASS" :
        (capture_core_pass ? "EVIDENCE_BLOCKED_EXTERNAL_DEEP_RUNTIME_GATE" :
            "EVIDENCE_BLOCKED_LIVE_VALIDATION_INCOMPLETE");

    const fs::path result_parent = root / L"ValidationResults";
    const std::string package_id = "UltimateLiveValidationResult_" + CompactUtcTimestamp() +
        "_" + NewId();
    const fs::path result_root = result_parent / Utf8ToWide(package_id);
    std::error_code directory_error;
    fs::create_directories(result_root / L"evidence", directory_error);
    if (directory_error || !IsContainedValidationPath(root, fs::absolute(result_root))) {
        Print("cannot create a safe live validation result directory", true);
        return kExitSoftware;
    }
    if (recovery_package_bound) {
        std::error_code copy_error;
        const fs::path copied_recovery = result_root / L"evidence" / recovery_zip.filename();
        fs::copy_file(recovery_zip, copied_recovery,
                      fs::copy_options::none, copy_error);
        if (copy_error || CalculateFileSha256(copied_recovery).value_or("") != recovery_sha) {
            Print("cannot preserve the exact recovery bundle in the live validation result", true);
            return kExitSoftware;
        }
    }
    if (candidate_map_bound) {
        std::error_code copy_error;
        const fs::path copied_map = result_root / L"evidence" / L"deep-probe-candidate-map.json";
        fs::copy_file(candidate_map_path, copied_map,
                      fs::copy_options::none, copy_error);
        const auto copied_map_content = ReadUtf8File(copied_map);
        if (copy_error || !copied_map_content || *copied_map_content != *candidate_map ||
            !ValidateDeepProbeCandidateMap(*copied_map_content)) {
            Print("cannot preserve the verified deep-probe candidate map", true);
            return kExitSoftware;
        }
    }
    if (segment_manifest_bound) {
        std::error_code copy_error;
        const fs::path copied_manifest = result_root / L"evidence" /
            L"semantic-segment-manifest.json";
        fs::copy_file(segment_manifest_path, copied_manifest,
                      fs::copy_options::none, copy_error);
        if (copy_error || CalculateFileSha256(copied_manifest).value_or("") !=
                CalculateFileSha256(segment_manifest_path).value_or("")) {
            Print("cannot preserve the bound semantic segment manifest", true);
            return kExitSoftware;
        }
    }
    const auto binding = MakeJsonObject({
        {"SchemaVersion", "god2-ultimate-live-evidence-binding-v2"},
        {"Status", pass ? "DEEP_RECOVERY_EVIDENCE_PASS" :
            (capture_core_pass ? "CAPTURE_CORE_EVIDENCE_PASS_DEEP_RUNTIME_BLOCKED" :
                "EvidenceBlocked")},
        {"ValidationScope", "ExactBuildCaptureCoreAndDeepRuntimeGate"},
        {"SessionId", session_id}, {"ProcessId", std::to_string(process_id)},
        {"ProcessCreationTime", GetString(identity, "ProcessCreationTime")},
        {"ClientArchitecture", GetString(identity, "Architecture")},
        {"ClientFileVersion", GetString(identity, "FileVersion")},
        {"ClientSHA256", GetString(identity, "SHA256")},
        {"SessionNonceBound", session_bound ? "true" : "false"},
        {"SessionIdentityRevalidated", session_identity_revalidated ? "true" : "false"},
        {"ExactIdentity", exact_identity ? "true" : "false"},
        {"DetachComplete", detach_complete ? "true" : "false"},
        {"SharedTransportComplete", transport_complete ? "true" : "false"},
        {"SegmentManifestBound", segment_manifest_bound ? "true" : "false"},
        {"SegmentCount", std::to_string(std::max<std::int64_t>(0,
            GetInt64(segment_manifest, "SegmentCount").value_or(0)))},
        {"SegmentRecordCount", std::to_string(std::max<std::int64_t>(0,
            GetInt64(segment_manifest, "RecordCount").value_or(0)))},
        {"SegmentMergedSHA256", GetString(segment_manifest, "MergedSHA256")},
        {"SemanticEventCount", std::to_string(semantic_count)},
        {"RejectedSemanticEventCount", std::to_string(rejected_semantic)},
        {"ProbeDiagnosticCount", std::to_string(diagnostics)},
        {"ParserReadCount", std::to_string(parser_reads)},
        {"SerializerWriteCount", std::to_string(serializer_writes)},
        {"HandlerInvocationCount", std::to_string(handler_invocations)},
        {"SemanticEvidenceBound", semantic_bound ? "true" : "false"},
        {"DeepProbeCandidateMapBound", candidate_map_bound ? "true" : "false"},
        {"RecoveryManifestInventoryBound", recovery_package_bound ? "true" : "false"},
        {"CaptureCoreEvidencePass", capture_core_pass ? "true" : "false"},
        {"DeepRuntimeVerifiedDomainCount",
            std::to_string(deep_runtime_verified_domain_count)},
        {"DeepRuntimeRequiredDomainCount",
            std::to_string(deep_runtime_required_domain_count)},
        {"DeepRuntimePromotionEligible", deep_runtime_pass ? "true" : "false"}
    }, {"ProcessId", "ProcessCreationTime", "SessionNonceBound",
        "SessionIdentityRevalidated", "ExactIdentity", "DetachComplete",
        "SharedTransportComplete", "SegmentManifestBound", "SegmentCount",
        "SegmentRecordCount", "SemanticEventCount", "RejectedSemanticEventCount",
        "ProbeDiagnosticCount", "ParserReadCount", "SerializerWriteCount",
        "HandlerInvocationCount", "SemanticEvidenceBound", "DeepProbeCandidateMapBound",
        "RecoveryManifestInventoryBound", "CaptureCoreEvidencePass",
        "DeepRuntimeVerifiedDomainCount", "DeepRuntimeRequiredDomainCount",
        "DeepRuntimePromotionEligible"});
    const auto summary = MakeJsonObject({
        {"SchemaVersion", "god2-ultimate-live-validation-result-v2"},
        {"ValidatedAtUtc", UtcNow()}, {"FinalStatus", final_status},
        {"ValidationScope", "ExactBuildCaptureCoreAndDeepRuntimeGate"},
        {"IntegrityStatus", "MANIFEST_VERIFIED_AFTER_SUMMARY_WRITE"},
        {"PackageIntegrity", "MANIFEST_VERIFIED_AFTER_SUMMARY_WRITE"},
        {"ResultIntegrity", "MANIFEST_VERIFIED_AFTER_SUMMARY_WRITE"},
        {"ExecutableSHA256", executable_sha}, {"ExecutableFileVersion", FileVersion(executable)},
        {"TargetClientSHA256", std::string(kExactTargetClientSha256)},
        {"ExactBuildIdentity", exact_identity ? "true" : "false"},
        {"ProbeMapStatus", candidate_map_bound ?
            "THREE_VERIFIED_CHOKE_POINTS_AND_21_BLOCKED_DOMAINS_BOUND" : "EvidenceBlocked"},
        {"SharedTransportStatus", transport_complete ? "PASS" : "EvidenceBlocked"},
        {"SegmentManifestStatus", segment_manifest_bound ? "PASS" : "EvidenceBlocked"},
        {"RecoveryBundleStatus", recovery_package_bound ? "PASS" : "EvidenceBlocked"},
        {"CaptureCoreStatus", capture_core_pass ? "PASS" : "EvidenceBlocked"},
        {"DeepRuntimeStatus", deep_runtime_pass ? "PASS" :
            "EVIDENCE_BLOCKED_EXTERNAL_DEEP_RUNTIME_GATE"},
        {"DeepRuntimeVerifiedDomainCount",
            std::to_string(deep_runtime_verified_domain_count)},
        {"DeepRuntimeRequiredDomainCount",
            std::to_string(deep_runtime_required_domain_count)},
        {"OperationSpecificGpuStatus", "EvidenceBlockedNotEvaluatedByLiveRunner"},
        {"GameStabilityStatus", "EvidenceBlockedNoBoundStabilityTelemetry"},
        {"OverheadStatus", "EvidenceBlockedNoBoundOverheadMeasurement"},
        {"UltimateCompletionEligible", "false"},
        {"RecoveryBundleSHA256", recovery_sha}, {"RecoveryPackageId", package_id},
        {"ProductionDatabaseConnectionAttempted", "false"},
        {"ProductionDatabaseMutationAttempted", "false"},
        {"GuiExitCode", std::to_string(gui_result)}
    }, {"ExactBuildIdentity", "DeepRuntimeVerifiedDomainCount",
        "DeepRuntimeRequiredDomainCount", "UltimateCompletionEligible",
        "ProductionDatabaseConnectionAttempted", "ProductionDatabaseMutationAttempted",
        "GuiExitCode"});
    if (!WriteUtf8FileAtomic(result_root / L"live-evidence-binding.json", binding + "\n") ||
        !WriteUtf8FileAtomic(result_root / L"validation-summary.json", summary + "\n")) {
        Print("cannot write live validation result evidence", true);
        return kExitSoftware;
    }

    struct ManifestEntry { std::string path; std::uint64_t size = 0; std::string sha; };
    std::vector<ManifestEntry> entries;
    std::error_code iterator_error;
    for (fs::recursive_directory_iterator iterator(result_root, iterator_error), end;
         !iterator_error && iterator != end; iterator.increment(iterator_error)) {
        if (!iterator->is_regular_file()) continue;
        const auto relative = fs::relative(iterator->path(), result_root, iterator_error);
        if (iterator_error) break;
        entries.push_back({WideToUtf8(relative.generic_wstring()),
            static_cast<std::uint64_t>(iterator->file_size()),
            CalculateFileSha256(iterator->path()).value_or("")});
    }
    std::sort(entries.begin(), entries.end(), [](const auto& left, const auto& right) {
        return left.path < right.path;
    });
    std::ostringstream files;
    files << '[';
    for (std::size_t index = 0; index < entries.size(); ++index) {
        if (index != 0) files << ',';
        files << MakeJsonObject({{"relativePath", entries[index].path},
            {"sizeBytes", std::to_string(entries[index].size)}, {"sha256", entries[index].sha}},
            {"sizeBytes"});
    }
    files << ']';
    const auto manifest = MakeJsonObject({
        {"schemaVersion", "god2-ultimate-live-result-manifest-v1"},
        {"packageId", package_id}, {"finalStatus", final_status},
        {"executableSHA256", executable_sha}, {"files", files.str()}
    }, {"files"});
    const fs::path manifest_path = result_root / L"validation-manifest.json";
    if (iterator_error || std::any_of(entries.begin(), entries.end(), [](const auto& entry) {
            return entry.path.empty() || entry.sha.empty();
        }) || !WriteUtf8FileAtomic(manifest_path, manifest + "\n")) {
        Print("cannot create complete live validation manifest", true);
        return kExitSoftware;
    }
    const fs::path zip_path = result_parent / (Utf8ToWide(package_id) + L".zip");
    std::string zip_error;
    if (!CreatePortableStoredZip(result_root, zip_path, &zip_error) ||
        !ValidatePortableStoredZip(result_root, zip_path, &zip_error)) {
        Print("cannot create or reopen the live validation result ZIP: " + zip_error, true);
        return kExitSoftware;
    }
    const auto zip_sha = CalculateFileSha256(zip_path).value_or("");
    const auto manifest_sha = CalculateFileSha256(manifest_path).value_or("");
    if (zip_sha.empty() || manifest_sha.empty()) {
        Print("cannot hash the committed live validation result", true);
        return kExitSoftware;
    }
    const auto attestation = MakeJsonObject({
        {"SchemaVersion", "god2-ultimate-live-result-attestation-v1"},
        {"PackageId", package_id}, {"FinalStatus", final_status},
        {"ResultZipFileName", WideToUtf8(zip_path.filename().wstring())},
        {"ResultZipSHA256", zip_sha}, {"ResultManifestSHA256", manifest_sha},
        {"ExecutableSHA256", executable_sha}
    });
    if (!WriteUtf8FileAtomic(
            result_parent / (Utf8ToWide(package_id) + L".attestation.json"),
            attestation + "\n")) {
        Print("cannot commit the live validation result attestation", true);
        return kExitSoftware;
    }
    Print(MakeJsonObject({{"FinalStatus", final_status},
        {"ResultZip", WideToUtf8(zip_path.wstring())}, {"ResultZipSHA256", zip_sha},
        {"RecoveryBundleSHA256", recovery_sha}}), !pass);
    return pass ? 0 : ERROR_INVALID_DATA;
}

int RunUltimateLiveValidationEntry(const fs::path& package_root,
                                   bool no_uac_automation,
                                   bool elevated_child) {
    const auto decision = UltimateLiveElevationPolicy(IsAdministrator(), no_uac_automation);
    if (decision == UltimateLiveElevationDecision::RunHere)
        return RunUltimateLiveValidation(package_root);
    if (decision == UltimateLiveElevationDecision::BlockNoUac || elevated_child) {
        Print(MakeJsonObject({
            {"FinalStatus", "EVIDENCE_BLOCKED_ADMINISTRATOR_REQUIRED"},
            {"ElevationAttempted", "false"},
            {"Reason", elevated_child ? "ElevatedChildIsNotAdministrator" :
                                         "NoUacAutomationMode"}
        }, {"ElevationAttempted"}), true);
        return ERROR_ELEVATION_REQUIRED;
    }

    std::error_code root_error;
    std::error_code executable_error;
    const auto root = fs::weakly_canonical(package_root, root_error);
    const auto executable = fs::weakly_canonical(ExecutablePath(), executable_error);
    const DWORD attributes = root.empty() ? INVALID_FILE_ATTRIBUTES :
        GetFileAttributesW(root.c_str());
    if (root_error || executable_error || root.empty() || executable.empty() ||
        attributes == INVALID_FILE_ATTRIBUTES ||
        (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        executable.parent_path() != root || !IsContainedValidationPath(root, executable)) {
        Print("live validation elevation rejected an unowned package root", true);
        return kExitUsage;
    }

    const std::wstring parameters =
        L"--internal-ultimate-live-validation --package-root " +
        QuoteArgument(root.wstring()) + L" --elevated-live-child";
    SHELLEXECUTEINFOW elevation{};
    elevation.cbSize = sizeof(elevation);
    elevation.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI | SEE_MASK_NOASYNC;
    elevation.lpVerb = L"runas";
    elevation.lpFile = executable.c_str();
    elevation.lpParameters = parameters.c_str();
    elevation.lpDirectory = root.c_str();
    elevation.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&elevation) || elevation.hProcess == nullptr) {
        const DWORD code = GetLastError();
        Print(MakeJsonObject({
            {"FinalStatus", "EVIDENCE_BLOCKED_ADMINISTRATOR_REQUIRED"},
            {"ElevationAttempted", "true"}, {"Win32Error", std::to_string(code)}
        }, {"ElevationAttempted", "Win32Error"}), true);
        if (elevation.hProcess != nullptr) CloseHandle(elevation.hProcess);
        return code == ERROR_SUCCESS ? ERROR_ELEVATION_REQUIRED : static_cast<int>(code);
    }
    const DWORD wait = WaitForSingleObject(elevation.hProcess, INFINITE);
    DWORD child_exit = ERROR_GEN_FAILURE;
    const bool exit_read = wait == WAIT_OBJECT_0 &&
        GetExitCodeProcess(elevation.hProcess, &child_exit) != FALSE;
    CloseHandle(elevation.hProcess);
    return exit_read ? static_cast<int>(child_exit) : kExitSoftware;
}

} // namespace
} // namespace god2

int god2::RunInternalCommandLine(int argc, wchar_t** argv) {
    god2::Arguments arguments;
    for (int i = 1; i < argc; ++i) arguments.values.emplace_back(argv[i]);
    if (arguments.values.empty()) return 64;
    const auto& command = arguments.values[0];
    if (command == L"--internal-selftest") {
        if (arguments.values.size() >= 2 && arguments.values[1] == L"gui-smoke") {
            const auto report = arguments.Value(L"--report");
            return report ? god2::RunGuiSmokeTest(*report) : 64;
        }
        return god2::SelfTestCommand();
    }
    if (command == L"--internal-reanalyze") return god2::AnalyzeCommand(arguments, true);
    if (command == L"--internal-package") {
        const auto session = arguments.Value(L"--session");
        if (!session) return 64;
        const auto result = god2::EvidencePackageBuilder::Build(*session,
            arguments.Has(L"--partial"), false);
        god2::Print(god2::MakeJsonObject({
            {"Status", result.success ? "Completed" : "Failed"},
            {"SessionId", result.session_id}, {"PackageStatus", result.package_status},
            {"ZipPath", god2::WideToUtf8(result.zip_path.wstring())},
            {"ZipSHA256", result.zip_sha256}, {"CaptureRecords", std::to_string(result.capture_records)},
            {"ProtocolFrames", std::to_string(result.protocol_frames)}, {"Error", result.error}
        }, {"CaptureRecords", "ProtocolFrames"}), !result.success);
        return result.success ? 0 : god2::kExitSoftware;
    }
    if (command == L"--internal-repackage-evidence") {
        const auto package = arguments.Value(L"--package");
        if (!package) return 64;
        const auto result = god2::EvidencePackageBuilder::Repackage(*package);
        god2::Print(god2::MakeJsonObject({
            {"Status", result.success ? "Completed" : "Failed"}, {"SessionId", result.session_id},
            {"PackageStatus", result.package_status}, {"ZipPath", god2::WideToUtf8(result.zip_path.wstring())},
            {"ZipSHA256", result.zip_sha256}, {"CaptureRecords", std::to_string(result.capture_records)},
            {"TransportChunks", std::to_string(result.transport_chunks)},
            {"CandidateProtocolFrames", std::to_string(result.candidate_protocol_frames)},
            {"ProtocolFrames", std::to_string(result.protocol_frames)},
            {"DecodedMessages", std::to_string(result.decoded_messages)},
            {"HandlerObservations", std::to_string(result.handler_observations)},
            {"CompressedPackageBytes", std::to_string(result.compressed_package_bytes)},
            {"UncompressedPackageBytes", std::to_string(result.uncompressed_package_bytes)},
            {"CompressionRatio", std::to_string(result.compression_ratio)},
            {"ConnectionCount", std::to_string(result.connection_count)}, {"Error", result.error}
        }, {"CaptureRecords", "TransportChunks", "CandidateProtocolFrames", "ProtocolFrames", "DecodedMessages",
            "HandlerObservations", "CompressedPackageBytes", "UncompressedPackageBytes", "CompressionRatio",
            "ConnectionCount"}), !result.success);
        return result.success ? 0 : god2::kExitSoftware;
    }
    if (command == L"--internal-package-fixture-test") {
        const auto session = arguments.Value(L"--session");
        const auto report = arguments.Value(L"--report");
        return session && report ? god2::RunEvidencePackageFixtureTest(*session, *report) : 64;
    }
    if (command == L"--internal-semantic-selftest") {
        const auto report = arguments.Value(L"--report");
        return report ? god2::RunSemanticRecoverySelfTest(*report) : 64;
    }
    if (command == L"--internal-semantic-continuity-stress") {
        const auto output = arguments.Value(L"--output");
        return output ? god2::RunSemanticContinuityStress(*output, 2000001U) : 64;
    }
    if (command == L"--internal-ultimate-selftest") {
        const auto report = arguments.Value(L"--report");
        const auto artifacts = arguments.Value(L"--artifacts");
        const auto native_semantic_wire =
            arguments.Value(L"--native-semantic-wire");
        return report && artifacts ? god2::RunUltimateRecoverySelfTests(
            *report, *artifacts, native_semantic_wire) : 64;
    }
    if (command == L"--internal-verify-native-semantic-wire") {
        const auto input = arguments.Value(L"--input");
        const auto report = arguments.Value(L"--report");
        return input && report ?
            god2::VerifyNativeSemanticWireFixture(*input, *report) : 64;
    }
    if (command == L"--internal-ultimate-live-validation") {
        const auto package_root = arguments.Value(L"--package-root");
        return package_root ? god2::RunUltimateLiveValidationEntry(
            *package_root, arguments.Has(L"--no-uac-automation"),
            arguments.Has(L"--elevated-live-child")) : 64;
    }
    if (command == L"--internal-headless-enhanced-capture") {
        const auto session = arguments.Value(L"--session");
        const auto session_id = arguments.Value(L"--session-id");
        const auto game = arguments.Value(L"--game");
        const auto pid_text = arguments.Value(L"--pid");
        const auto observe_text = arguments.Value(L"--observe-seconds");
        const auto stop_event = arguments.Value(L"--stop-event");
        if (!session || !session_id || !game || !pid_text || !observe_text ||
            !stop_event) return 64;
        wchar_t* pid_end = nullptr;
        wchar_t* observe_end = nullptr;
        const unsigned long pid = wcstoul(pid_text->c_str(), &pid_end, 10);
        const unsigned long observe = wcstoul(observe_text->c_str(), &observe_end, 10);
        constexpr std::wstring_view stop_prefix =
            L"Local\\God2SemanticRecovery.OfficialHeadlessStop.";
        const bool stop_event_valid = stop_event->size() == stop_prefix.size() + 32 &&
            stop_event->compare(0, stop_prefix.size(), stop_prefix) == 0 &&
            std::all_of(stop_event->begin() + stop_prefix.size(), stop_event->end(),
                [](wchar_t value) {
                    return (value >= L'0' && value <= L'9') ||
                           (value >= L'a' && value <= L'f') ||
                           (value >= L'A' && value <= L'F');
                });
        if (pid == 0 || pid > MAXDWORD || observe < 4 || observe > 600 ||
            pid_end == pid_text->c_str() || *pid_end != L'\0' ||
            observe_end == observe_text->c_str() || *observe_end != L'\0' ||
            !stop_event_valid) return 64;
        return god2::RunHeadlessEnhancedCapture(*session,
            god2::WideToUtf8(*session_id), *game,
            static_cast<DWORD>(pid), static_cast<DWORD>(observe), *stop_event);
    }
    if (command == L"--internal-gpu-selftest") {
        const auto report = arguments.Value(L"--report");
        return report ? god2::RunGpuAccelerationContractTests(*report) : 64;
    }
    if (command == L"--internal-gpu-benchmark") {
        const auto report = arguments.Value(L"--report");
        return report ? god2::RunGpuAccelerationBenchmark(*report) : 64;
    }
    if (command == L"--internal-rtx5070-validation") {
        const auto package_root = arguments.Value(L"--package-root");
        return package_root ? god2::RunRtx5070Validation(*package_root) : 64;
    }
    if (command == L"--internal-gpu-package-equivalence") {
        const auto session = arguments.Value(L"--session");
        const auto report = arguments.Value(L"--report");
        return session && report ? god2::RunGpuPackageEquivalenceTest(*session, *report) : 64;
    }
    if (command == L"--internal-etl-audit") {
        const auto input = arguments.Value(L"--input");
        const auto report = arguments.Value(L"--report");
        if (!input || !report) return 64;
        std::string error;
        const auto audit = god2::AuditEtwFile(*input, *report, &error);
        god2::Print(god2::MakeJsonObject({
            {"Status", audit.success ? "Completed" : "Failed"},
            {"TotalEvents", std::to_string(audit.total_events)},
            {"NdisPacketEvents", std::to_string(audit.ndis_packet_events)},
            {"NdisPayloadBytes", std::to_string(audit.ndis_payload_bytes)},
            {"EventsLost", std::to_string(audit.events_lost)},
            {"BuffersLost", std::to_string(audit.buffers_lost)},
            {"Win32Error", std::to_string(audit.win32_error)},
            {"Error", error}
        }, {"TotalEvents", "NdisPacketEvents", "NdisPayloadBytes", "EventsLost", "BuffersLost", "Win32Error"}),
            !audit.success);
        return audit.success ? 0 : god2::kExitSoftware;
    }
    if (command == L"--internal-worker" && arguments.values.size() >= 2 &&
        arguments.values[1] == L"elevated-capture") {
        return god2::RunElevatedCaptureWorker(arguments.values);
    }
    if (command == L"--internal-worker" && arguments.values.size() >= 2 &&
        arguments.values[1] == L"etw-consume") {
        const auto logger = arguments.Value(L"--logger");
        const auto output = arguments.Value(L"--output");
        const auto status = arguments.Value(L"--status");
        const auto nonce = arguments.Value(L"--nonce");
        const auto ready_event = arguments.Value(L"--ready-event");
        const auto cancel_event = arguments.Value(L"--cancel-event");
        const auto worker_log = arguments.Value(L"--worker-log");
        const auto session_id = arguments.Value(L"--session-id");
        if (!logger || !output || !status || !nonce || !ready_event || !cancel_event ||
            !worker_log || !session_id) return 64;
        god2::EtwConsumerOptions options;
        options.logger_name = *logger;
        options.output_jsonl = *output;
        options.worker_log_path = *worker_log;
        options.ready_event_name = *ready_event;
        options.cancel_event_name = *cancel_event;
        options.session_id = god2::WideToUtf8(*session_id);
        const int result = god2::ConsumeEtwRealtime(options);
        return god2::WriteCaptureWorkerStatus(*status, god2::WideToUtf8(*nonce), result) ? result : ERROR_WRITE_FAULT;
    }
    if (command == L"--internal-worker" && arguments.values.size() >= 2 &&
        arguments.values[1] == L"pktmon-counter-worker") {
        const auto output = arguments.Value(L"--output");
        const auto status = arguments.Value(L"--status");
        const auto nonce = arguments.Value(L"--nonce");
        if (!output || !status || !nonce) return 64;
        const int result = god2::RunPktMonCounterWorker(*output);
        return god2::WriteCaptureWorkerStatus(*status, god2::WideToUtf8(*nonce), result) ? result : ERROR_WRITE_FAULT;
    }
    return 64;
}
