#include "ActionGrouping.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <iomanip>
#include <limits>
#include <map>
#include <numeric>
#include <set>
#include <sstream>
#include <unordered_map>
#include <unordered_set>

namespace god2 {
namespace {

constexpr std::string_view kUnknownSemantic = "Unknown";

std::string StringArray(const std::vector<std::string>& values) {
    std::ostringstream output;
    output << '[';
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) output << ',';
        output << '"' << JsonEscape(values[index]) << '"';
    }
    output << ']';
    return output.str();
}

std::string UnsignedArray(const std::vector<std::uint64_t>& values) {
    std::ostringstream output;
    output << '[';
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) output << ',';
        output << values[index];
    }
    output << ']';
    return output.str();
}

std::string JsonNumber(double value) {
    if (!std::isfinite(value)) return "0";
    std::ostringstream output;
    output << std::fixed << std::setprecision(3) << value;
    std::string result = output.str();
    while (result.size() > 1 && result.back() == '0') result.pop_back();
    if (!result.empty() && result.back() == '.') result.pop_back();
    return result;
}

double Quantile(std::vector<double> values, double quantile) {
    if (values.empty()) return 0.0;
    std::sort(values.begin(), values.end());
    if (values.size() == 1) return values.front();
    const double position = quantile * static_cast<double>(values.size() - 1);
    const auto lower = static_cast<std::size_t>(std::floor(position));
    const auto upper = static_cast<std::size_t>(std::ceil(position));
    if (lower == upper) return values[lower];
    const double weight = position - static_cast<double>(lower);
    return values[lower] * (1.0 - weight) + values[upper] * weight;
}

double MedianAbsoluteDeviation(const std::vector<double>& values, double median) {
    std::vector<double> deviations;
    deviations.reserve(values.size());
    for (const auto value : values) deviations.push_back(std::abs(value - median));
    return Quantile(std::move(deviations), 0.5);
}

int CorrelationRank(std::string_view level) {
    if (level == "Exact") return 4;
    if (level == "Strong") return 3;
    if (level == "Candidate") return 2;
    return 1;
}

std::string StrongestCorrelation(std::string current, std::string_view candidate) {
    if (CorrelationRank(candidate) > CorrelationRank(current)) return std::string(candidate);
    return current;
}

bool IsBefore(const ActionGroupingRecord& left, const ActionGroupingRecord& right) {
    if (left.captured_qpc > 0 && right.captured_qpc > 0 && left.qpc_frequency > 0 &&
        left.qpc_frequency == right.qpc_frequency && left.captured_qpc != right.captured_qpc)
        return left.captured_qpc < right.captured_qpc;
    return left.capture_sequence < right.capture_sequence;
}

double ElapsedMilliseconds(const ActionGroupingRecord& start, const ActionGroupingRecord& end) {
    if (start.captured_qpc > 0 && end.captured_qpc >= start.captured_qpc && start.qpc_frequency > 0 &&
        start.qpc_frequency == end.qpc_frequency) {
        return static_cast<double>(end.captured_qpc - start.captured_qpc) * 1000.0 /
            static_cast<double>(start.qpc_frequency);
    }
    return end.captured_at_unix_ms >= start.captured_at_unix_ms ?
        static_cast<double>(end.captured_at_unix_ms - start.captured_at_unix_ms) : 0.0;
}

std::string EffectiveConnectionId(const ActionGroupingRecord& record) {
    if (!record.connection_id.empty()) return record.connection_id;
    if (record.capture_stage == "Transport" && record.socket != "Unknown" &&
        record.socket != "0xFFFFFFFF" && !record.socket.empty())
        return "process-" + std::to_string(record.process_id) + "-socket-" + record.socket;
    return {};
}

std::string HandlerOpcode(const ActionGroupingRecord& record) {
    if (!record.opcode.empty()) return record.opcode;
    if (record.payload_hex.size() < 2) return {};
    return "0x" + record.payload_hex.substr(0, 2);
}

struct LogicalMessage {
    std::string id;
    std::vector<const ActionGroupingRecord*> records;
    const ActionGroupingRecord* first = nullptr;
    const ActionGroupingRecord* last = nullptr;
    std::string direction;
    std::string connection_id;
    std::string correlation_level = "Uncorrelated";
    bool connection_ambiguous = false;
    bool has_transport = false;
    bool has_pre_encrypt = false;
    bool has_post_decrypt = false;
    bool has_handler = false;
    std::vector<const ActionGroupingRecord*> protocol_frames;
    std::vector<const ActionGroupingRecord*> handlers;
};

struct TriggerCandidate {
    const ActionGroupingRecord* record = nullptr;
    const LogicalMessage* logical = nullptr;
};

struct ResponseCandidate {
    const LogicalMessage* logical = nullptr;
    std::vector<std::string> opcodes;
    std::vector<std::uint64_t> lengths;
    std::vector<std::string> protocol_frame_ids;
    std::vector<std::string> handler_ids;
    std::vector<std::string> handler_opcodes;
    std::string structural_key;
    bool background = false;
};

struct OrphanBurst {
    std::string id;
    std::string context_invocation_id;
    std::string context_correlation_basis;
    std::int64_t process_id = 0;
    std::int64_t thread_id = 0;
    std::vector<const ActionGroupingRecord*> records;
};

struct ActionInstance {
    std::string id;
    std::string pattern_id;
    const TriggerCandidate* trigger = nullptr;
    std::vector<const ResponseCandidate*> responses;
    std::vector<const OrphanBurst*> orphan_bursts;
    std::vector<std::string> response_logical_ids;
    std::vector<std::string> response_protocol_ids;
    std::vector<std::string> response_opcodes;
    std::vector<std::uint64_t> response_lengths;
    std::vector<std::string> response_correlation_levels;
    std::vector<std::string> handler_ids;
    std::vector<std::string> handler_opcodes;
    std::vector<std::string> orphan_ids;
    std::vector<std::string> domain_correlation_ids;
    std::vector<std::string> correlation_reasons;
    std::string correlation_level = "Candidate";
    std::string semantic_status = std::string(kUnknownSemantic);
    std::string verified_registry_id;
    std::string verified_registry_evidence_sha256;
    std::string verified_gameplay_semantic;
    std::string structural_key;
    double duration_ms = 0.0;
    bool ambiguous = false;
};

struct ActionPattern {
    std::string id;
    std::string name;
    std::string key;
    std::vector<ActionInstance*> instances;
    std::vector<std::string> related_work_item_ids;
    std::string semantic_status = std::string(kUnknownSemantic);
    std::string verified_gameplay_semantic;
    std::string verified_registry_id;
    std::string verified_registry_evidence_sha256;
};

std::vector<std::string> UniqueStrings(std::vector<std::string> values) {
    std::vector<std::string> result;
    std::unordered_set<std::string> seen;
    result.reserve(values.size());
    for (auto& value : values) {
        if (!value.empty() && seen.insert(value).second) result.push_back(std::move(value));
    }
    return result;
}

std::string Join(const std::vector<std::string>& values, std::string_view separator) {
    std::ostringstream output;
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) output << separator;
        output << values[index];
    }
    return output.str();
}

std::string JoinLengths(const std::vector<std::uint64_t>& values) {
    std::ostringstream output;
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) output << ',';
        output << values[index];
    }
    return output.str();
}

std::string WorkItemKey(std::string_view direction, std::string_view opcode, std::uint64_t frame_length) {
    return std::string(direction) + "|" + std::string(opcode) + "|" + std::to_string(frame_length);
}

const TriggerCandidate* LatestPriorTrigger(const std::vector<TriggerCandidate>& triggers,
                                           const LogicalMessage& response) {
    const TriggerCandidate* latest = nullptr;
    if (response.first == nullptr || response.connection_id.empty()) return nullptr;
    for (const auto& trigger : triggers) {
        if (trigger.record == nullptr || trigger.logical == nullptr ||
            trigger.logical->connection_id != response.connection_id ||
            !IsBefore(*trigger.record, *response.first)) continue;
        if (latest == nullptr || IsBefore(*latest->record, *trigger.record)) latest = &trigger;
    }
    return latest;
}

const TriggerCandidate* NextTriggerForConnection(const std::vector<TriggerCandidate>& triggers,
                                                 const TriggerCandidate& current) {
    const TriggerCandidate* next = nullptr;
    if (current.record == nullptr || current.logical == nullptr || current.logical->connection_id.empty()) return nullptr;
    for (const auto& candidate : triggers) {
        if (candidate.record == nullptr || candidate.logical == nullptr ||
            candidate.logical->connection_id != current.logical->connection_id ||
            !IsBefore(*current.record, *candidate.record)) continue;
        if (next == nullptr || IsBefore(*candidate.record, *next->record)) next = &candidate;
    }
    return next;
}

std::vector<std::string> SharedDomainCorrelations(const LogicalMessage& trigger,
                                                  const std::vector<const ResponseCandidate*>& responses) {
    std::map<std::string, std::set<std::string>> trigger_values;
    for (const auto* record : trigger.records) {
        for (const auto& [name, value] : record->domain_correlation_ids)
            if (!value.empty()) trigger_values[name].insert(value);
    }
    std::vector<std::string> shared;
    for (const auto* response : responses) {
        for (const auto* record : response->logical->records) {
            for (const auto& [name, value] : record->domain_correlation_ids) {
                const auto found = trigger_values.find(name);
                if (!value.empty() && found != trigger_values.end() && found->second.contains(value))
                    shared.push_back(name + "=" + value);
            }
        }
    }
    return UniqueStrings(std::move(shared));
}

void ApplyVerifiedRegistryMapping(const ActionGroupingRecord& trigger, ActionInstance* instance) {
    const auto status = trigger.verified_registry_fields.find("ProtocolRegistryVerificationStatus");
    const auto source = trigger.verified_registry_fields.find("VerifiedProtocolRegistrySource");
    const auto evidence_sha256 = trigger.verified_registry_fields.find("VerifiedProtocolRegistryEvidenceSHA256");
    const auto registry = trigger.verified_registry_fields.find("VerifiedProtocolRegistryId");
    const auto semantic = trigger.verified_registry_fields.find("VerifiedGameplaySemantic");
    if (status == trigger.verified_registry_fields.end() || status->second != "Verified" ||
        source == trigger.verified_registry_fields.end() || source->second != "RepositoryVerifiedProtocolRegistry" ||
        evidence_sha256 == trigger.verified_registry_fields.end() || evidence_sha256->second.size() != 64 ||
        !std::all_of(evidence_sha256->second.begin(), evidence_sha256->second.end(),
            [](unsigned char value) { return std::isxdigit(value) != 0; }) ||
        registry == trigger.verified_registry_fields.end() || registry->second.empty() ||
        semantic == trigger.verified_registry_fields.end() || semantic->second.empty()) return;
    instance->semantic_status = "MappedToVerifiedGameplay";
    instance->verified_registry_id = registry->second;
    instance->verified_registry_evidence_sha256 = evidence_sha256->second;
    instance->verified_gameplay_semantic = semantic->second;
    instance->correlation_reasons.push_back(
        "Trigger semantic was explicitly mapped by a Verified Protocol Registry entry; response association keeps its independent evidence level");
}

} // namespace

GenericActionGroupingResult GroupGenericUnknownActions(const GenericActionGroupingInput& input) {
    GenericActionGroupingResult result;

    std::map<std::string, LogicalMessage> logical_messages;
    for (const auto& record : input.records) {
        if (record.logical_message_id.empty()) continue;
        auto& logical = logical_messages[record.logical_message_id];
        logical.id = record.logical_message_id;
        logical.records.push_back(&record);
        if (logical.first == nullptr || IsBefore(record, *logical.first)) logical.first = &record;
        if (logical.last == nullptr || IsBefore(*logical.last, record)) logical.last = &record;
        if (logical.direction.empty() || record.capture_stage == "PreEncrypt" ||
            record.capture_stage == "PostDecrypt") logical.direction = record.direction;
        logical.correlation_level = StrongestCorrelation(logical.correlation_level, record.correlation_level);
        const auto connection_id = EffectiveConnectionId(record);
        if (!connection_id.empty()) {
            if (logical.connection_id.empty()) logical.connection_id = connection_id;
            else if (logical.connection_id != connection_id) logical.connection_ambiguous = true;
        }
        logical.has_transport = logical.has_transport || record.capture_stage == "Transport";
        logical.has_pre_encrypt = logical.has_pre_encrypt || record.capture_stage == "PreEncrypt";
        logical.has_post_decrypt = logical.has_post_decrypt || record.capture_stage == "PostDecrypt";
        logical.has_handler = logical.has_handler || record.capture_stage == "HandlerDecoded";
        if (!record.protocol_frame_id.empty()) logical.protocol_frames.push_back(&record);
        if (!record.handler_observation_id.empty()) logical.handlers.push_back(&record);
    }
    for (auto& [id, logical] : logical_messages) {
        static_cast<void>(id);
        std::sort(logical.records.begin(), logical.records.end(), [](const auto* left, const auto* right) {
            return IsBefore(*left, *right);
        });
        std::sort(logical.protocol_frames.begin(), logical.protocol_frames.end(), [](const auto* left, const auto* right) {
            return IsBefore(*left, *right);
        });
        std::sort(logical.handlers.begin(), logical.handlers.end(), [](const auto* left, const auto* right) {
            return IsBefore(*left, *right);
        });
        if (logical.connection_ambiguous) logical.connection_id.clear();
    }

    std::vector<TriggerCandidate> triggers;
    for (const auto& record : input.records) {
        if (record.capture_stage != "PreEncrypt" || record.direction != "ClientToServer" ||
            record.protocol_frame_id.empty() || record.opcode.empty() || record.frame_length == 0) continue;
        const auto logical = logical_messages.find(record.logical_message_id);
        if (logical == logical_messages.end()) continue;
        triggers.push_back({&record, &logical->second});
    }
    std::sort(triggers.begin(), triggers.end(), [](const auto& left, const auto& right) {
        return IsBefore(*left.record, *right.record);
    });
    result.action_trigger_candidate_count = triggers.size();

    std::vector<ResponseCandidate> responses;
    for (const auto& [id, logical] : logical_messages) {
        static_cast<void>(id);
        if (!logical.has_post_decrypt || logical.direction != "ServerToClient" || logical.first == nullptr) continue;
        ResponseCandidate response;
        response.logical = &logical;
        for (const auto* frame : logical.protocol_frames) {
            if (frame->capture_stage != "PostDecrypt" || frame->direction != "ServerToClient") continue;
            response.opcodes.push_back(frame->opcode);
            response.lengths.push_back(frame->frame_length);
            response.protocol_frame_ids.push_back(frame->protocol_frame_id);
        }
        if (response.protocol_frame_ids.empty()) continue;
        for (const auto* handler : logical.handlers) {
            response.handler_ids.push_back(handler->handler_observation_id);
            response.handler_opcodes.push_back(HandlerOpcode(*handler));
        }
        response.structural_key = logical.connection_id + "|" + Join(response.opcodes, ",") + "|" +
            JoinLengths(response.lengths);
        responses.push_back(std::move(response));
    }
    std::sort(responses.begin(), responses.end(), [](const auto& left, const auto& right) {
        return IsBefore(*left.logical->first, *right.logical->first);
    });

    struct BackgroundFamily {
        std::vector<ResponseCandidate*> members;
        std::vector<double> intervals;
        std::map<std::string, std::uint64_t> preceding_trigger_keys;
        double median_interval = 0.0;
        double interval_mad = 0.0;
        bool background = false;
    };
    std::map<std::string, BackgroundFamily> response_families;
    for (auto& response : responses) {
        auto& family = response_families[response.structural_key];
        family.members.push_back(&response);
        const auto* prior = LatestPriorTrigger(triggers, *response.logical);
        if (prior != nullptr)
            ++family.preceding_trigger_keys[prior->record->opcode + "|" +
                std::to_string(prior->record->frame_length)];
    }
    for (auto& [key, family] : response_families) {
        static_cast<void>(key);
        if (family.members.size() < 6) continue;
        for (std::size_t index = 1; index < family.members.size(); ++index) {
            family.intervals.push_back(ElapsedMilliseconds(
                *family.members[index - 1]->logical->first, *family.members[index]->logical->first));
        }
        family.median_interval = Quantile(family.intervals, 0.5);
        family.interval_mad = MedianAbsoluteDeviation(family.intervals, family.median_interval);
        const auto dominant = family.preceding_trigger_keys.empty() ? 0ull :
            std::max_element(family.preceding_trigger_keys.begin(), family.preceding_trigger_keys.end(),
                [](const auto& left, const auto& right) { return left.second < right.second; })->second;
        const double dominant_fraction = family.members.empty() ? 1.0 :
            static_cast<double>(dominant) / static_cast<double>(family.members.size());
        const double span = ElapsedMilliseconds(*family.members.front()->logical->first,
                                                *family.members.back()->logical->first);
        family.background = family.median_interval > 0.0 &&
            family.interval_mad / family.median_interval <= 0.15 &&
            span >= family.median_interval * 4.0 &&
            (family.preceding_trigger_keys.size() >= 3 || dominant_fraction < 0.60);
        if (!family.background) continue;
        ++result.background_candidate_count;
        for (auto* member : family.members) member->background = true;
    }

    std::vector<double> observed_latencies;
    for (const auto& response : responses) {
        if (response.background) continue;
        const auto* trigger = LatestPriorTrigger(triggers, *response.logical);
        if (trigger == nullptr) continue;
        observed_latencies.push_back(ElapsedMilliseconds(*trigger->record, *response.logical->first));
    }
    result.session_latency_median_ms = Quantile(observed_latencies, 0.5);
    result.session_latency_p95_ms = Quantile(observed_latencies, 0.95);
    const double latency_q1 = Quantile(observed_latencies, 0.25);
    const double latency_q3 = Quantile(observed_latencies, 0.75);
    result.session_latency_limit_ms = latency_q3 + 1.5 * std::max(0.0, latency_q3 - latency_q1);
    if (!observed_latencies.empty() && result.session_latency_limit_ms <= 0.0)
        result.session_latency_limit_ms = result.session_latency_p95_ms;

    std::map<std::string, OrphanBurst> orphan_bursts_by_key;
    for (const auto& [id, logical] : logical_messages) {
        static_cast<void>(id);
        if (!logical.has_handler || logical.has_post_decrypt) continue;
        for (const auto* handler : logical.handlers) {
            const std::string key = !handler->context_invocation_id.empty() ?
                std::to_string(handler->process_id) + "|" + std::to_string(handler->thread_id) + "|" +
                    handler->context_invocation_id + "|" + handler->context_correlation_basis :
                "unscoped|" + std::to_string(handler->process_id) + "|" +
                    std::to_string(handler->thread_id) + "|" + logical.id;
            auto& burst = orphan_bursts_by_key[key];
            burst.context_invocation_id = handler->context_invocation_id;
            burst.context_correlation_basis = handler->context_correlation_basis;
            burst.process_id = handler->process_id;
            burst.thread_id = handler->thread_id;
            burst.records.push_back(handler);
        }
    }
    std::vector<OrphanBurst> orphan_bursts;
    orphan_bursts.reserve(orphan_bursts_by_key.size());
    for (auto& [key, burst] : orphan_bursts_by_key) {
        static_cast<void>(key);
        std::sort(burst.records.begin(), burst.records.end(), [](const auto* left, const auto* right) {
            return IsBefore(*left, *right);
        });
        orphan_bursts.push_back(std::move(burst));
    }
    std::sort(orphan_bursts.begin(), orphan_bursts.end(), [](const auto& left, const auto& right) {
        return IsBefore(*left.records.front(), *right.records.front());
    });
    for (std::size_t index = 0; index < orphan_bursts.size(); ++index)
        orphan_bursts[index].id = "orphan-handler-burst-" + std::to_string(index + 1);
    result.orphan_handler_burst_count = orphan_bursts.size();

    std::vector<ActionInstance> instances;
    std::unordered_set<std::string> assigned_orphan_ids;
    for (const auto& trigger : triggers) {
        if (trigger.logical->connection_id.empty()) continue;
        const auto* next = NextTriggerForConnection(triggers, trigger);
        ActionInstance instance;
        instance.trigger = &trigger;
        for (const auto& response : responses) {
            if (response.background || response.logical->connection_id != trigger.logical->connection_id ||
                !IsBefore(*trigger.record, *response.logical->first)) continue;
            if (next != nullptr && !IsBefore(*response.logical->first, *next->record)) continue;
            const auto elapsed = ElapsedMilliseconds(*trigger.record, *response.logical->first);
            if (!observed_latencies.empty() && elapsed > result.session_latency_limit_ms) continue;
            instance.responses.push_back(&response);
        }
        for (const auto& burst : orphan_bursts) {
            if (assigned_orphan_ids.contains(burst.id) || burst.records.empty() ||
                burst.process_id != trigger.record->process_id || burst.thread_id != trigger.record->thread_id ||
                !IsBefore(*trigger.record, *burst.records.front())) continue;
            if (next != nullptr && !IsBefore(*burst.records.front(), *next->record)) continue;
            const auto elapsed = ElapsedMilliseconds(*trigger.record, *burst.records.front());
            if (!observed_latencies.empty() && elapsed > result.session_latency_limit_ms) continue;
            instance.orphan_bursts.push_back(&burst);
        }
        if (instance.responses.empty() && instance.orphan_bursts.empty()) continue;
        instance.id = "action-instance-" + std::to_string(instances.size() + 1);
        if (!instance.responses.empty())
            instance.correlation_reasons.push_back(
                "Same connection, post-trigger QPC/CaptureSequence ordering, and next-trigger boundary");
        for (const auto* response : instance.responses) {
            instance.response_logical_ids.push_back(response->logical->id);
            instance.response_protocol_ids.insert(instance.response_protocol_ids.end(),
                response->protocol_frame_ids.begin(), response->protocol_frame_ids.end());
            instance.response_opcodes.insert(instance.response_opcodes.end(),
                response->opcodes.begin(), response->opcodes.end());
            instance.response_lengths.insert(instance.response_lengths.end(),
                response->lengths.begin(), response->lengths.end());
            instance.response_correlation_levels.push_back(response->logical->correlation_level);
            instance.handler_ids.insert(instance.handler_ids.end(),
                response->handler_ids.begin(), response->handler_ids.end());
            instance.handler_opcodes.insert(instance.handler_opcodes.end(),
                response->handler_opcodes.begin(), response->handler_opcodes.end());
        }
        for (const auto* burst : instance.orphan_bursts) {
            instance.orphan_ids.push_back(burst->id);
            assigned_orphan_ids.insert(burst->id);
            for (const auto* handler : burst->records) {
                instance.handler_ids.push_back(handler->handler_observation_id);
                instance.handler_opcodes.push_back(HandlerOpcode(*handler));
            }
            instance.ambiguous = true;
            instance.correlation_reasons.push_back(
                "Orphan HandlerDecoded burst is retained as Candidate evidence without fabricating PostDecrypt");
        }
        instance.response_logical_ids = UniqueStrings(std::move(instance.response_logical_ids));
        instance.response_protocol_ids = UniqueStrings(std::move(instance.response_protocol_ids));
        instance.handler_ids = UniqueStrings(std::move(instance.handler_ids));
        instance.orphan_ids = UniqueStrings(std::move(instance.orphan_ids));
        instance.domain_correlation_ids = SharedDomainCorrelations(*trigger.logical, instance.responses);
        if (!instance.domain_correlation_ids.empty()) {
            instance.correlation_level = "Strong";
            instance.correlation_reasons.push_back(
                "Existing domain-specific correlation ID is shared by trigger and response evidence");
        }
        ApplyVerifiedRegistryMapping(*trigger.record, &instance);
        const ActionGroupingRecord* completed = trigger.record;
        for (const auto* response : instance.responses)
            if (response->logical->last != nullptr && IsBefore(*completed, *response->logical->last))
                completed = response->logical->last;
        for (const auto* burst : instance.orphan_bursts)
            if (!burst->records.empty() && IsBefore(*completed, *burst->records.back()))
                completed = burst->records.back();
        instance.duration_ms = ElapsedMilliseconds(*trigger.record, *completed);
        const std::string relationship_pattern = trigger.logical->correlation_level + ">" +
            Join(instance.response_correlation_levels, ",");
        instance.structural_key = trigger.record->opcode + "|" +
            std::to_string(trigger.record->frame_length) + "|" + Join(instance.response_opcodes, ",") + "|" +
            JoinLengths(instance.response_lengths) + "|" + Join(instance.handler_opcodes, ",") + "|" +
            relationship_pattern;
        instances.push_back(std::move(instance));
    }

    std::map<std::string, ActionPattern> patterns_by_key;
    for (auto& instance : instances) {
        auto& pattern = patterns_by_key[instance.structural_key];
        pattern.key = instance.structural_key;
        pattern.instances.push_back(&instance);
    }
    std::vector<ActionPattern*> ordered_patterns;
    ordered_patterns.reserve(patterns_by_key.size());
    for (auto& [key, pattern] : patterns_by_key) {
        static_cast<void>(key);
        ordered_patterns.push_back(&pattern);
    }
    std::sort(ordered_patterns.begin(), ordered_patterns.end(), [](const auto* left, const auto* right) {
        return IsBefore(*left->instances.front()->trigger->record,
                        *right->instances.front()->trigger->record);
    });

    std::map<std::string, std::string> work_item_ids;
    for (const auto& item : input.opcode_work_items)
        work_item_ids.emplace(WorkItemKey(item.direction, item.opcode, item.frame_length), item.work_item_id);
    for (std::size_t index = 0; index < ordered_patterns.size(); ++index) {
        auto& pattern = *ordered_patterns[index];
        pattern.id = "AP-" + [&] {
            std::ostringstream value;
            value << std::setfill('0') << std::setw(4) << index + 1;
            return value.str();
        }();
        pattern.name = "UnknownActionPattern-" + [&] {
            std::ostringstream value;
            value << std::setfill('0') << std::setw(4) << index + 1;
            return value.str();
        }();
        std::string mapped_semantic;
        std::string mapped_registry;
        std::string mapped_registry_evidence;
        bool all_mapped = !pattern.instances.empty();
        for (auto* instance : pattern.instances) {
            instance->pattern_id = pattern.id;
            if (instance->semantic_status != "MappedToVerifiedGameplay") all_mapped = false;
            if (mapped_semantic.empty()) mapped_semantic = instance->verified_gameplay_semantic;
            else if (mapped_semantic != instance->verified_gameplay_semantic) all_mapped = false;
            if (mapped_registry.empty()) mapped_registry = instance->verified_registry_id;
            else if (mapped_registry != instance->verified_registry_id) all_mapped = false;
            if (mapped_registry_evidence.empty())
                mapped_registry_evidence = instance->verified_registry_evidence_sha256;
            else if (mapped_registry_evidence != instance->verified_registry_evidence_sha256)
                all_mapped = false;
            const auto trigger_work_item = work_item_ids.find(WorkItemKey(
                "ClientToServer", instance->trigger->record->opcode, instance->trigger->record->frame_length));
            if (trigger_work_item != work_item_ids.end())
                pattern.related_work_item_ids.push_back(trigger_work_item->second);
            for (const auto* response : instance->responses) {
                for (std::size_t frame = 0; frame < response->opcodes.size(); ++frame) {
                    const auto response_work_item = work_item_ids.find(WorkItemKey(
                        "ServerToClient", response->opcodes[frame], response->lengths[frame]));
                    if (response_work_item != work_item_ids.end())
                        pattern.related_work_item_ids.push_back(response_work_item->second);
                }
            }
        }
        pattern.related_work_item_ids = UniqueStrings(std::move(pattern.related_work_item_ids));
        if (all_mapped && !mapped_semantic.empty() && !mapped_registry.empty()) {
            pattern.semantic_status = "MappedToVerifiedGameplay";
            pattern.verified_gameplay_semantic = mapped_semantic;
            pattern.verified_registry_id = mapped_registry;
            pattern.verified_registry_evidence_sha256 = mapped_registry_evidence;
            pattern.name = mapped_semantic;
            ++result.verified_gameplay_mapped_pattern_count;
        } else {
            ++result.unknown_action_pattern_count;
        }
        for (const auto& work_item_id : pattern.related_work_item_ids)
            result.related_action_pattern_ids_by_work_item[work_item_id].push_back(pattern.id);
    }

    std::ostringstream instance_output;
    for (const auto& instance : instances) {
        const auto& trigger = *instance.trigger->record;
        const ActionGroupingRecord* completed = &trigger;
        for (const auto* response : instance.responses)
            if (response->logical->last != nullptr && IsBefore(*completed, *response->logical->last))
                completed = response->logical->last;
        for (const auto* burst : instance.orphan_bursts)
            if (!burst->records.empty() && IsBefore(*completed, *burst->records.back())) completed = burst->records.back();
        const auto trigger_work_item = work_item_ids.find(
            WorkItemKey("ClientToServer", trigger.opcode, trigger.frame_length));
        instance_output << MakeJsonObject({
            {"SchemaId", "action-instance"}, {"SchemaVersion", "11"}, {"SessionId", input.session_id},
            {"ActionInstanceId", instance.id}, {"ActionPatternId", instance.pattern_id},
            {"TriggerCandidateStatus", "ActionTriggerCandidate"},
            {"TriggerLogicalMessageId", trigger.logical_message_id},
            {"TriggerCaptureRecordId", trigger.capture_record_id},
            {"TriggerProtocolFrameId", trigger.protocol_frame_id}, {"TriggerOpcode", trigger.opcode},
            {"TriggerLength", std::to_string(trigger.frame_length)},
            {"TriggerOpcodeWorkItemId", trigger_work_item == work_item_ids.end() ? "null" :
                "\"" + trigger_work_item->second + "\""},
            {"ResponseLogicalMessageIds", StringArray(instance.response_logical_ids)},
            {"ResponseProtocolFrameIds", StringArray(instance.response_protocol_ids)},
            {"ResponseOpcodeSequence", StringArray(instance.response_opcodes)},
            {"ResponseLengthPattern", UnsignedArray(instance.response_lengths)},
            {"HandlerObservationIds", StringArray(instance.handler_ids)},
            {"HandlerOpcodeSequence", StringArray(instance.handler_opcodes)},
            {"OrphanHandlerBurstIds", StringArray(instance.orphan_ids)},
            {"ConnectionId", instance.trigger->logical->connection_id},
            {"ProcessId", std::to_string(trigger.process_id)}, {"ThreadId", std::to_string(trigger.thread_id)},
            {"TriggerCaptureSequence", std::to_string(trigger.capture_sequence)},
            {"TriggerStageSequence", std::to_string(trigger.stage_sequence)},
            {"StartedAt", trigger.captured_at_utc}, {"CompletedAt", completed->captured_at_utc},
            {"DurationMs", JsonNumber(instance.duration_ms)},
            {"SessionAdaptiveLatencyLimitMs", JsonNumber(result.session_latency_limit_ms)},
            {"LatencyPolicy", "SessionAdaptiveDistributionWithNextTriggerBoundary"},
            {"CorrelationLevel", instance.correlation_level},
            {"AmbiguityStatus", instance.ambiguous ? "Ambiguous" : "Unambiguous"},
            {"CorrelationReasons", StringArray(instance.correlation_reasons)},
            {"ExistingDomainCorrelationIds", StringArray(instance.domain_correlation_ids)},
            {"SemanticStatus", instance.semantic_status},
            {"VerifiedProtocolRegistryId", instance.verified_registry_id.empty() ? "null" :
                "\"" + JsonEscape(instance.verified_registry_id) + "\""},
            {"VerifiedProtocolRegistryEvidenceSHA256", instance.verified_registry_evidence_sha256.empty() ? "null" :
                "\"" + JsonEscape(instance.verified_registry_evidence_sha256) + "\""},
            {"VerifiedGameplaySemantic", instance.verified_gameplay_semantic.empty() ? "null" :
                "\"" + JsonEscape(instance.verified_gameplay_semantic) + "\""},
            {"ProductionEligible", "false"}
        }, {"SchemaVersion", "TriggerLength", "TriggerOpcodeWorkItemId", "ResponseLogicalMessageIds",
            "ResponseProtocolFrameIds", "ResponseOpcodeSequence", "ResponseLengthPattern",
            "HandlerObservationIds", "HandlerOpcodeSequence", "OrphanHandlerBurstIds", "ProcessId",
            "ThreadId", "TriggerCaptureSequence", "TriggerStageSequence", "DurationMs",
            "SessionAdaptiveLatencyLimitMs", "CorrelationReasons", "ExistingDomainCorrelationIds",
            "VerifiedProtocolRegistryId", "VerifiedProtocolRegistryEvidenceSHA256",
            "VerifiedGameplaySemantic", "ProductionEligible"}) << '\n';
    }
    result.action_instances_jsonl = instance_output.str();

    std::ostringstream pattern_output;
    for (const auto* pattern : ordered_patterns) {
        std::vector<std::string> instance_ids;
        std::vector<double> latencies;
        for (const auto* instance : pattern->instances) {
            instance_ids.push_back(instance->id);
            latencies.push_back(instance->duration_ms);
        }
        const auto& sample = *pattern->instances.front();
        pattern_output << MakeJsonObject({
            {"SchemaId", "action-pattern"}, {"SchemaVersion", "11"}, {"SessionId", input.session_id},
            {"ActionPatternId", pattern->id}, {"PatternName", pattern->name},
            {"StructuralPatternKey", pattern->key},
            {"TriggerOpcode", sample.trigger->record->opcode},
            {"TriggerLength", std::to_string(sample.trigger->record->frame_length)},
            {"ResponseOpcodeSequence", StringArray(sample.response_opcodes)},
            {"ResponseLengthPattern", UnsignedArray(sample.response_lengths)},
            {"HandlerOpcodeSequence", StringArray(sample.handler_opcodes)},
            {"ActionInstanceIds", StringArray(instance_ids)},
            {"Occurrences", std::to_string(pattern->instances.size())},
            {"MedianLatencyMs", JsonNumber(Quantile(latencies, 0.5))},
            {"P95LatencyMs", JsonNumber(Quantile(latencies, 0.95))},
            {"RelatedOpcodeWorkItemIds", StringArray(pattern->related_work_item_ids)},
            {"SemanticStatus", pattern->semantic_status},
            {"VerifiedProtocolRegistryId", pattern->verified_registry_id.empty() ? "null" :
                "\"" + JsonEscape(pattern->verified_registry_id) + "\""},
            {"VerifiedProtocolRegistryEvidenceSHA256", pattern->verified_registry_evidence_sha256.empty() ? "null" :
                "\"" + JsonEscape(pattern->verified_registry_evidence_sha256) + "\""},
            {"VerifiedGameplaySemantic", pattern->verified_gameplay_semantic.empty() ? "null" :
                "\"" + JsonEscape(pattern->verified_gameplay_semantic) + "\""},
            {"ProductionEligible", "false"}
        }, {"SchemaVersion", "TriggerLength", "ResponseOpcodeSequence", "ResponseLengthPattern",
            "HandlerOpcodeSequence", "ActionInstanceIds", "Occurrences", "MedianLatencyMs", "P95LatencyMs",
            "RelatedOpcodeWorkItemIds", "VerifiedProtocolRegistryId", "VerifiedGameplaySemantic",
            "VerifiedProtocolRegistryEvidenceSHA256", "ProductionEligible"}) << '\n';
    }
    result.action_patterns_jsonl = pattern_output.str();

    std::ostringstream orphan_output;
    for (const auto& burst : orphan_bursts) {
        std::vector<std::string> capture_ids;
        std::vector<std::string> logical_ids;
        std::vector<std::string> handler_ids;
        std::vector<std::string> handler_opcodes;
        for (const auto* record : burst.records) {
            capture_ids.push_back(record->capture_record_id);
            logical_ids.push_back(record->logical_message_id);
            handler_ids.push_back(record->handler_observation_id);
            handler_opcodes.push_back(HandlerOpcode(*record));
        }
        logical_ids = UniqueStrings(std::move(logical_ids));
        orphan_output << MakeJsonObject({
            {"SchemaId", "orphan-handler-burst"}, {"SchemaVersion", "11"}, {"SessionId", input.session_id},
            {"OrphanHandlerBurstId", burst.id}, {"ContextInvocationId", burst.context_invocation_id},
            {"ContextCorrelationBasis", burst.context_correlation_basis},
            {"ProcessId", std::to_string(burst.process_id)}, {"ThreadId", std::to_string(burst.thread_id)},
            {"CaptureRecordIds", StringArray(capture_ids)}, {"LogicalMessageIds", StringArray(logical_ids)},
            {"HandlerObservationIds", StringArray(handler_ids)},
            {"HandlerOpcodeSequence", StringArray(handler_opcodes)},
            {"ParentPostDecryptRecordIds", "[]"}, {"CorrelationLevel", "Candidate"},
            {"CorrelationReasons", "[\"Shared existing ContextInvocationId/ContextCorrelationBasis; parent PostDecrypt was not observed\"]"},
            {"SyntheticPostDecryptCreated", "false"}, {"SemanticStatus", "Unknown"}
        }, {"SchemaVersion", "ProcessId", "ThreadId", "CaptureRecordIds", "LogicalMessageIds",
            "HandlerObservationIds", "HandlerOpcodeSequence", "ParentPostDecryptRecordIds",
            "CorrelationReasons", "SyntheticPostDecryptCreated"}) << '\n';
    }
    result.orphan_handler_bursts_jsonl = orphan_output.str();

    std::vector<const TriggerCandidate*> uncorrelated_triggers;
    for (const auto& trigger : triggers) {
        if (!trigger.logical->has_transport || trigger.logical->connection_id.empty() ||
            (trigger.logical->correlation_level != "Exact" && trigger.logical->correlation_level != "Strong"))
            uncorrelated_triggers.push_back(&trigger);
    }
    std::vector<std::vector<const TriggerCandidate*>> outbound_batches;
    for (const auto* trigger : uncorrelated_triggers) {
        bool extend = false;
        if (!outbound_batches.empty()) {
            const auto* previous = outbound_batches.back().back();
            extend = previous->record->process_id == trigger->record->process_id &&
                previous->record->thread_id == trigger->record->thread_id;
            if (extend) {
                for (const auto& record : input.records) {
                    if (record.capture_sequence <= previous->record->capture_sequence ||
                        record.capture_sequence >= trigger->record->capture_sequence) continue;
                    if (record.capture_stage == "Transport" && record.direction == "ClientToServer" &&
                        record.process_id == trigger->record->process_id && record.thread_id == trigger->record->thread_id) {
                        extend = false;
                        break;
                    }
                }
            }
        }
        if (!extend) outbound_batches.emplace_back();
        outbound_batches.back().push_back(trigger);
    }
    std::ostringstream outbound_output;
    for (std::size_t index = 0; index < outbound_batches.size(); ++index) {
        const auto& batch = outbound_batches[index];
        std::vector<std::string> logical_ids;
        std::vector<std::string> capture_ids;
        std::vector<std::string> frame_ids;
        std::vector<std::string> opcodes;
        std::vector<std::uint64_t> lengths;
        std::vector<std::string> following_transports;
        for (const auto* trigger : batch) {
            logical_ids.push_back(trigger->record->logical_message_id);
            capture_ids.push_back(trigger->record->capture_record_id);
            frame_ids.push_back(trigger->record->protocol_frame_id);
            opcodes.push_back(trigger->record->opcode);
            lengths.push_back(trigger->record->frame_length);
        }
        const auto last_sequence = batch.back()->record->capture_sequence;
        std::uint64_t boundary = std::numeric_limits<std::uint64_t>::max();
        for (const auto& record : input.records) {
            if (record.capture_sequence <= last_sequence || record.process_id != batch.back()->record->process_id ||
                record.thread_id != batch.back()->record->thread_id) continue;
            if (record.capture_stage == "PreEncrypt") {
                boundary = record.capture_sequence;
                break;
            }
        }
        for (const auto& record : input.records) {
            if (record.capture_sequence <= last_sequence || record.capture_sequence >= boundary ||
                record.process_id != batch.back()->record->process_id ||
                record.thread_id != batch.back()->record->thread_id ||
                record.capture_stage != "Transport" || record.direction != "ClientToServer" ||
                record.transport_chunk_id.empty()) continue;
            following_transports.push_back(record.transport_chunk_id);
        }
        following_transports = UniqueStrings(std::move(following_transports));
        outbound_output << MakeJsonObject({
            {"SchemaId", "outbound-batch-candidate"}, {"SchemaVersion", "11"}, {"SessionId", input.session_id},
            {"OutboundBatchCandidateId", "outbound-batch-candidate-" + std::to_string(index + 1)},
            {"TriggerLogicalMessageIds", StringArray(logical_ids)},
            {"TriggerCaptureRecordIds", StringArray(capture_ids)},
            {"TriggerProtocolFrameIds", StringArray(frame_ids)},
            {"TriggerOpcodeSequence", StringArray(opcodes)}, {"TriggerLengthPattern", UnsignedArray(lengths)},
            {"CandidateFollowingTransportChunkIds", StringArray(following_transports)},
            {"IndividualPreEncryptToTransportMappings", "[]"}, {"CorrelationLevel", "Candidate"},
            {"CorrelationReasons", "[\"Same-thread multi-pending outbound observations retained as a batch candidate; no individual Transport identity was asserted\"]"},
            {"ExplicitParentFabricated", "false"}, {"OriginalEvidencePreserved", "true"},
            {"SemanticStatus", "Unknown"}, {"ProductionEligible", "false"}
        }, {"SchemaVersion", "TriggerLogicalMessageIds", "TriggerCaptureRecordIds", "TriggerProtocolFrameIds",
            "TriggerOpcodeSequence", "TriggerLengthPattern", "CandidateFollowingTransportChunkIds",
            "IndividualPreEncryptToTransportMappings", "CorrelationReasons", "ExplicitParentFabricated",
            "OriginalEvidencePreserved", "ProductionEligible"}) << '\n';
        result.outbound_batch_trigger_count += batch.size();
    }
    result.outbound_batch_candidates_jsonl = outbound_output.str();
    result.outbound_batch_candidate_count = outbound_batches.size();

    result.action_instance_count = instances.size();
    result.grouped_trigger_count = instances.size();
    result.ungrouped_trigger_count = result.action_trigger_candidate_count - result.grouped_trigger_count;
    result.action_pattern_count = ordered_patterns.size();
    for (const auto& instance : instances) {
        if (instance.ambiguous) ++result.ambiguous_action_count;
        else if (instance.correlation_level == "Exact") ++result.exact_action_count;
        else if (instance.correlation_level == "Strong") ++result.strong_action_count;
        else ++result.candidate_action_count;
    }
    result.action_clustering_coverage_percent = result.action_trigger_candidate_count == 0 ? 0.0 :
        static_cast<double>(result.grouped_trigger_count) * 100.0 /
            static_cast<double>(result.action_trigger_candidate_count);

    std::ostringstream background_json;
    background_json << '[';
    bool first_background = true;
    for (const auto& [key, family] : response_families) {
        if (!family.background) continue;
        if (!first_background) background_json << ',';
        first_background = false;
        std::vector<std::string> logical_ids;
        for (const auto* member : family.members) logical_ids.push_back(member->logical->id);
        background_json << MakeJsonObject({
            {"BackgroundCandidateId", "background-candidate-" +
                std::to_string(static_cast<std::uint64_t>(std::count_if(
                    response_families.begin(), response_families.find(key),
                    [](const auto& item) { return item.second.background; })) + 1)},
            {"StructuralKey", key}, {"ObservationCount", std::to_string(family.members.size())},
            {"MedianIntervalMs", JsonNumber(family.median_interval)},
            {"IntervalMadMs", JsonNumber(family.interval_mad)},
            {"LogicalMessageIds", StringArray(logical_ids)}, {"SemanticStatus", "BackgroundCandidate"},
            {"GameplaySemantic", "Unknown"}
        }, {"ObservationCount", "MedianIntervalMs", "IntervalMadMs", "LogicalMessageIds"});
    }
    background_json << ']';

    std::vector<std::string> ungrouped_logical_ids;
    std::unordered_set<std::string> grouped_trigger_ids;
    for (const auto& instance : instances)
        grouped_trigger_ids.insert(instance.trigger->record->capture_record_id);
    for (const auto& trigger : triggers)
        if (!grouped_trigger_ids.contains(trigger.record->capture_record_id))
            ungrouped_logical_ids.push_back(trigger.record->logical_message_id);
    result.action_pattern_summary_json = MakeJsonObject({
        {"SchemaId", "action-pattern-summary"}, {"SchemaVersion", "11"}, {"SessionId", input.session_id},
        {"GroupingInputArtifacts", "[\"PrimarySession/evidence/logical-message-map.jsonl\",\"PrimarySession/mapping/transport-to-decrypted.jsonl\",\"PrimarySession/mapping/decrypted-to-handler.jsonl\"]"},
        {"ActionTriggerCandidateCount", std::to_string(result.action_trigger_candidate_count)},
        {"ActionInstanceCount", std::to_string(result.action_instance_count)},
        {"ActionPatternCount", std::to_string(result.action_pattern_count)},
        {"GroupedTriggerCount", std::to_string(result.grouped_trigger_count)},
        {"UngroupedTriggerCount", std::to_string(result.ungrouped_trigger_count)},
        {"ExactActionCount", std::to_string(result.exact_action_count)},
        {"StrongActionCount", std::to_string(result.strong_action_count)},
        {"CandidateActionCount", std::to_string(result.candidate_action_count)},
        {"AmbiguousActionCount", std::to_string(result.ambiguous_action_count)},
        {"UnknownActionPatternCount", std::to_string(result.unknown_action_pattern_count)},
        {"VerifiedGameplayMappedPatternCount", std::to_string(result.verified_gameplay_mapped_pattern_count)},
        {"OutboundBatchCandidateCount", std::to_string(result.outbound_batch_candidate_count)},
        {"OutboundBatchTriggerCount", std::to_string(result.outbound_batch_trigger_count)},
        {"OrphanHandlerBurstCount", std::to_string(result.orphan_handler_burst_count)},
        {"BackgroundCandidateCount", std::to_string(result.background_candidate_count)},
        {"ActionClusteringCoveragePercent", JsonNumber(result.action_clustering_coverage_percent)},
        {"ActionClusteringCoverageDenominator", "ActionTriggerCandidateCount"},
        {"ActionCountPartitionRule", "ExactActionCount + StrongActionCount + CandidateActionCount + AmbiguousActionCount = ActionInstanceCount; Ambiguous remains no stronger than Candidate evidence"},
        {"SessionLatencySampleCount", std::to_string(observed_latencies.size())},
        {"SessionLatencyMedianMs", JsonNumber(result.session_latency_median_ms)},
        {"SessionLatencyP95Ms", JsonNumber(result.session_latency_p95_ms)},
        {"SessionAdaptiveLatencyLimitMs", JsonNumber(result.session_latency_limit_ms)},
        {"LatencyPolicy", "SessionAdaptiveDistributionWithNextTriggerBoundary; timing never promotes Strong"},
        {"BackgroundCandidates", background_json.str()},
        {"UngroupedTriggerLogicalMessageIds", StringArray(UniqueStrings(std::move(ungrouped_logical_ids)))},
        {"SemanticPolicy", "Unknown unless an explicit Verified Protocol Registry mapping is present"},
        {"ManualMarkerDependency", "false"}
    }, {"SchemaVersion", "GroupingInputArtifacts", "ActionTriggerCandidateCount", "ActionInstanceCount",
        "ActionPatternCount", "GroupedTriggerCount", "UngroupedTriggerCount", "ExactActionCount",
        "StrongActionCount", "CandidateActionCount", "AmbiguousActionCount", "UnknownActionPatternCount",
        "VerifiedGameplayMappedPatternCount", "OutboundBatchCandidateCount", "OutboundBatchTriggerCount",
        "OrphanHandlerBurstCount", "BackgroundCandidateCount", "ActionClusteringCoveragePercent",
        "SessionLatencySampleCount", "SessionLatencyMedianMs", "SessionLatencyP95Ms",
        "SessionAdaptiveLatencyLimitMs", "BackgroundCandidates", "UngroupedTriggerLogicalMessageIds",
        "ManualMarkerDependency"}) + "\n";

    return result;
}

} // namespace god2
