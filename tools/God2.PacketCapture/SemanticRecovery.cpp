#include "SemanticRecovery.h"

#include "UltimateRecovery.h"

#include <algorithm>
#include <array>
#include <cctype>
#include <cstring>
#include <fstream>
#include <map>
#include <set>
#include <sstream>
#include <unordered_map>

namespace god2 {
namespace {

struct CaptureContext {
    std::string logical_message_id;
    std::string protocol_frame_id;
};

struct SemanticEvent {
    Fields fields;
    std::string original;
    std::string event_id;
    std::string session_id;
    std::string client_build_id;
    std::uint32_t process_id = 0;
    std::string event_type;
    std::string direction;
    std::string opcode;
    std::string semantic;
    std::string authority;
    std::string value_type;
    std::string endian;
    std::string hook_invocation_id;
    std::string value_token_id;
    std::string evidence_basis;
    std::int64_t offset = -1;
    std::int64_t width = 0;
    bool build_matches = false;
    bool exact_boundary = false;
    bool mutation_values_match = true;
};

struct FieldAggregate {
    std::string evidence_id;
    std::string direction;
    std::string opcode;
    std::string semantic;
    std::string authority;
    std::string value_type;
    std::string endian;
    std::int64_t offset = -1;
    std::int64_t width = 0;
    std::uint64_t observations = 0;
    std::uint64_t parser_reads = 0;
    std::uint64_t serializer_writes = 0;
    std::uint64_t handler_links = 0;
    std::uint64_t resolver_links = 0;
    std::uint64_t mutation_links = 0;
    std::uint64_t ui_links = 0;
    std::uint64_t runtime_source_writes = 0;
    std::uint64_t contradictions = 0;
    std::set<std::string> session_ids;
    std::set<std::string> event_ids;
    bool build_matches = true;
    bool exact_boundary = true;
    bool exact_build_causal = true;
    bool verified = false;
    std::string evidence_level = "Unknown";
};

std::string JsonArray(const std::vector<std::string>& values) {
    std::ostringstream output;
    output << '[';
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) output << ',';
        output << '"' << JsonEscape(values[index]) << '"';
    }
    output << ']';
    return output.str();
}

std::string FieldKey(const SemanticEvent& event) {
    return event.direction + "|" + event.opcode + "|" + std::to_string(event.offset) + "|" +
        std::to_string(event.width) + "|" + event.value_type + "|" + event.endian + "|" +
        event.semantic + "|" + event.authority;
}

std::string LocationKey(const SemanticEvent& event) {
    return event.direction + "|" + event.opcode + "|" + std::to_string(event.offset);
}

std::string ProtocolFieldId(const SemanticEvent& event) {
    return event.direction + ":" + event.opcode + ":+" + std::to_string(event.offset) + ":" +
        event.value_type + (event.width > 1 && event.endian == "Little" ? "le" : "");
}

bool IsStrictIdentityText(std::string_view value, std::size_t maximum) {
    if (value.empty() || value.size() > maximum) return false;
    return std::all_of(value.begin(), value.end(), [](unsigned char character) {
        return std::isalnum(character) != 0 || character == '-' || character == '_' ||
            character == '.' || character == ':';
    });
}

bool IsSha256Identity(std::string_view value) {
    return value.size() == 64 && std::all_of(value.begin(), value.end(), [](unsigned char character) {
        return std::isxdigit(character) != 0;
    });
}

std::string CausalScopeKey(const SemanticEvent& event) {
    return event.client_build_id + '|' + event.session_id + '|' +
        std::to_string(event.process_id);
}

std::string ScopedEventKey(const SemanticEvent& event, std::string_view event_id) {
    return CausalScopeKey(event) + "|event|" + std::string(event_id);
}

std::string ScopedTokenKey(const SemanticEvent& event, std::string_view token) {
    return CausalScopeKey(event) + "|token|" + std::string(token);
}

std::string StableEventIdentity(const SemanticEvent& event) {
    return ScopedEventKey(event, event.event_id);
}

std::unordered_map<std::string, CaptureContext> LoadCaptureContexts(const fs::path& path) {
    std::unordered_map<std::string, CaptureContext> contexts;
    std::ifstream input(path, std::ios::binary);
    std::string line;
    while (std::getline(input, line)) {
        Fields fields;
        if (line.empty() || !ParseFlatJson(line, fields, nullptr)) continue;
        const auto hook = GetString(fields, "HookInvocationId");
        if (hook.empty()) continue;
        contexts.emplace(hook, CaptureContext{
            GetString(fields, "LogicalMessageId"), GetString(fields, "ProtocolFrameId")});
    }
    return contexts;
}

std::string CompatibilityEventType(UltimateSemanticEventType value) {
    switch (value) {
    case UltimateSemanticEventType::ParserRead: return "ParserRead";
    case UltimateSemanticEventType::SerializerWrite: return "SerializerWrite";
    case UltimateSemanticEventType::HandlerInvocation:
    case UltimateSemanticEventType::HandlerArgument: return "HandlerInvocation";
    case UltimateSemanticEventType::ObjectResolved: return "ObjectResolution";
    case UltimateSemanticEventType::StateMutation: return "StateMutation";
    case UltimateSemanticEventType::UIAnchor: return "UIAnchor";
    case UltimateSemanticEventType::ValueFlow: return "ValueFlow";
    default: return {};
    }
}

std::vector<SemanticEvent> LoadSemanticEvents(const fs::path& path,
                                              std::string_view actual_client_sha256,
                                              std::string_view expected_session_id,
                                              std::uint64_t* invalid_count) {
    std::vector<SemanticEvent> events;
    std::set<std::string> stable_identities;
    std::ifstream input(path, std::ios::binary);
    std::string line;
    while (std::getline(input, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty()) continue;
        const auto decoded = ReadSemanticEvent(line);
        if (!decoded.success) {
            if (invalid_count != nullptr) ++*invalid_count;
            continue;
        }
        const auto compatibility_type = CompatibilityEventType(decoded.event.event_type);
        // v2 contains 25 valid event types. The compatibility analyzer consumes
        // only the legacy seven-domain subset; other valid types are handled by
        // UltimateRecovery and must not be mislabeled as corrupt input.
        if (compatibility_type.empty()) continue;

        SemanticEvent event;
        event.original = line;
        event.event_id = decoded.event.event_id;
        event.session_id = decoded.event.session_id;
        event.client_build_id = decoded.event.client_build_id;
        event.process_id = decoded.event.process_id;
        const bool strict_identity = IsStrictIdentityText(event.event_id, 160) &&
            IsStrictIdentityText(event.session_id, 128) &&
            IsSha256Identity(event.client_build_id) && event.process_id != 0 &&
            event.session_id != "LegacyV1SessionUnknown" &&
            event.client_build_id != "LegacyV1BuildUnknown" &&
            (expected_session_id.empty() || event.session_id == expected_session_id);
        if (!strict_identity || !stable_identities.insert(StableEventIdentity(event)).second) {
            if (invalid_count != nullptr) ++*invalid_count;
            continue;
        }
        Fields payload_fields;
        if (!ParseFlatJson(decoded.event.payload, payload_fields, nullptr)) {
            if (invalid_count != nullptr) ++*invalid_count;
            continue;
        }
        // ReadSemanticEvent intentionally exposes only the common v1/v2
        // companion surface.  The compatibility analyzer also needs these
        // legacy causal fields in order to re-evaluate old evidence without
        // silently dropping the source-object and before/after proof.  Keep
        // the allowlist local and exact: arbitrary v1 keys must not become
        // trusted analyzer input.
        if (decoded.read_from_v1 &&
            decoded.event.sensitive_mask_status == "NotSensitive") {
            Fields legacy_fields;
            if (!ParseFlatJson(line, legacy_fields, nullptr)) {
                if (invalid_count != nullptr) ++*invalid_count;
                continue;
            }
            constexpr std::array<std::string_view, 7> causal_keys = {
                "SourceObjectDomain", "ObjectDomain", "Property",
                "BeforeValue", "InputValue", "AfterValue", "Delta"
            };
            bool legacy_conflict = false;
            for (const auto key : causal_keys) {
                const auto source = legacy_fields.find(std::string(key));
                if (source == legacy_fields.end()) continue;
                const auto existing = payload_fields.find(source->first);
                if (existing != payload_fields.end() && existing->second != source->second) {
                    legacy_conflict = true;
                    break;
                }
                payload_fields.emplace(source->first, source->second);
            }
            if (legacy_conflict) {
                if (invalid_count != nullptr) ++*invalid_count;
                continue;
            }
        }
        for (const auto& [key, value] : decoded.event.evidence_binding)
            event.fields.emplace(key, value);
        bool companion_conflict = false;
        for (const auto& [key, value] : payload_fields) {
            const auto found = event.fields.find(key);
            if (found != event.fields.end() && found->second != value) {
                companion_conflict = true;
                break;
            }
            event.fields.emplace(key, value);
        }
        if (companion_conflict) {
            if (invalid_count != nullptr) ++*invalid_count;
            continue;
        }
        event.fields["SchemaId"] = "God2SemanticEvent";
        event.fields["SchemaVersion"] = std::to_string(decoded.event.schema_version);
        event.fields["SemanticEventId"] = decoded.event.event_id;
        event.fields["EventId"] = decoded.event.event_id;
        event.fields["EventType"] = compatibility_type;
        event.fields["ProcessId"] = std::to_string(decoded.event.process_id);
        event.fields["ThreadId"] = std::to_string(decoded.event.thread_id);
        event.fields["SessionId"] = decoded.event.session_id;
        event.fields["ClientBuildId"] = decoded.event.client_build_id;
        event.fields["SensitiveMaskStatus"] =
            decoded.event.sensitive_mask_status;
        if (!event.fields.contains("ObservedAtUnixMs"))
            event.fields["ObservedAtUnixMs"] = decoded.event.timestamp;
        if (!event.fields.contains("Module")) event.fields["Module"] = decoded.event.module_id;
        if (!event.fields.contains("Rva")) event.fields["Rva"] =
            !decoded.event.rva_expression.empty() ? decoded.event.rva_expression :
            std::to_string(decoded.event.rva);
        if (!event.fields.contains("AuthorityClassification"))
            event.fields["AuthorityClassification"] = ToString(decoded.event.authority_hint);
        if (!event.fields.contains("ValueTokenId"))
            event.fields["ValueTokenId"] = decoded.event.value_token;

        event.event_type = compatibility_type;
        event.direction = GetString(event.fields, "Direction", "Unknown");
        event.opcode = GetString(event.fields, "Opcode", "Unknown");
        event.semantic = GetString(event.fields, "FieldSemantic", "Unknown");
        event.authority = GetString(event.fields, "AuthorityClassification", "UnknownAuthority");
        event.value_type = GetString(event.fields, event.event_type == "SerializerWrite" ?
            "WriteType" : "ReadType", GetString(event.fields, "ArgumentType", "Unknown"));
        event.endian = GetString(event.fields, "Endian", "NotApplicable");
        event.hook_invocation_id = GetString(event.fields, "HookInvocationId");
        event.value_token_id = GetString(event.fields, "ValueTokenId",
            GetString(event.fields, "AssociatedValueTokenId"));
        event.evidence_basis = GetString(event.fields, "EvidenceBasis");
        event.offset = GetInt64(event.fields, "FrameOffset").value_or(
            GetInt64(event.fields, "DestinationOffset").value_or(-1));
        event.width = GetInt64(event.fields, "Width").value_or(
            event.event_type == "HandlerInvocation" ? 1 : 0);
        const auto expected_hash = GetString(event.fields, "ClientSha256Expected");
        const auto binding_status = GetString(event.fields, "BuildBindingStatus");
        const bool trusted_binding_status =
            binding_status == "ExactInstructionIdentityVerified" ||
            binding_status == "ExactCallTargetVerified";
        event.build_matches = !actual_client_sha256.empty() && !expected_hash.empty() &&
            IsSha256Identity(actual_client_sha256) &&
            _stricmp(expected_hash.c_str(), std::string(actual_client_sha256).c_str()) == 0 &&
            _stricmp(decoded.event.client_build_id.c_str(),
                     std::string(actual_client_sha256).c_str()) == 0 &&
            trusted_binding_status;
        event.exact_boundary = GetString(event.fields, "FrameOffsetStatus") == "VerifiedExactBoundary";
        if (event.event_type == "StateMutation") {
            const auto before = GetInt64(event.fields, "BeforeValue");
            const auto input_value = GetInt64(event.fields, "InputValue");
            const auto after = GetInt64(event.fields, "AfterValue");
            const auto delta = GetInt64(event.fields, "Delta");
            event.mutation_values_match = before && input_value && after && delta &&
                *after - *before == *delta && *input_value == *delta;
        }
        events.push_back(std::move(event));
    }
    return events;
}

void AddHistoricalSessions(const fs::path& root, const fs::path& current,
                           std::string_view client_sha256,
                           std::set<std::string>* observed_event_identities,
                           std::map<std::string, FieldAggregate>* aggregates) {
    if (root.empty() || !fs::is_directory(root) || observed_event_identities == nullptr) return;
    std::error_code error;
    for (fs::recursive_directory_iterator iterator(root, fs::directory_options::skip_permission_denied, error), end;
         iterator != end && !error; iterator.increment(error)) {
        if (!iterator->is_regular_file(error) || iterator->path().filename() != L"semantic-events.jsonl") continue;
        const auto path_text = iterator->path().wstring();
        if (path_text.find(L"evidence-package-staging") != std::wstring::npos ||
            path_text.find(L".candidate-reanalysis") != std::wstring::npos) continue;
        std::error_code equivalent_error;
        if (fs::equivalent(iterator->path(), current, equivalent_error) && !equivalent_error) continue;
        std::uint64_t ignored_invalid = 0;
        const auto events = LoadSemanticEvents(iterator->path(), client_sha256, {}, &ignored_invalid);
        std::set<std::string> seen;
        for (const auto& event : events) {
            if (event.event_type != "ParserRead" && event.event_type != "SerializerWrite") continue;
            if (!observed_event_identities->insert(StableEventIdentity(event)).second) continue;
            const auto key = FieldKey(event);
            const auto found = aggregates->find(key);
            if (found != aggregates->end() && event.build_matches && seen.insert(key).second)
                found->second.session_ids.insert(event.session_id);
        }
    }
}

std::string ProbePlanJson(const SemanticRecoveryInput& input, std::string_view status) {
    const auto point = [](std::initializer_list<std::pair<std::string, std::string>> fields) {
        return MakeJsonObject(std::vector<std::pair<std::string, std::string>>(fields));
    };
    const std::vector<std::string> points = {
        point({{"ProbeId","PacketDecode.FrameBoundary"},{"Category","ParserReader"},
            {"Module","God2_opt.exe"},{"Rva","0x00078D70"},{"ExpectedBytes","E8 rel32 at 0x00078A48"},
            {"InstructionSignature","CALL target RVA 0x00078D70; verified decoded frame base and exact length"},
            {"ExpectedCallTarget","God2_opt.exe+0x00078D70"},{"CallingConvention","x86 __thiscall"},
            {"ArgumentContract","decoder=this, frame=verified mutable frame base, length=int"},
            {"ReturnContract","application payload length = declared length - 3"},{"FieldType","FrameEnvelope"},
            {"ValueWidth","1/2"},{"Endian","Little"},{"BuildBindingStatus",std::string(status)},
            {"Status","Implemented"},{"EvidenceBasis","Exact call target plus runtime u16le length/checksum verification"}}),
        point({{"ProbeId","OutboundEnqueue.FrameBuilder"},{"Category","SerializerWriter"},
            {"Module","God2_opt.exe"},{"Rva","0x0007FC10"},{"ExpectedBytes","55 8B EC 56 57"},
            {"InstructionSignature","exact five-byte prologue and trampoline contract"},
            {"ExpectedCallTarget","God2_opt.exe+0x0007FC10"},{"CallingConvention","x86 __thiscall"},
            {"ArgumentContract","sender=this, opcode=u8, payload=bytes, length=int"},
            {"ReturnContract","int"},{"FieldType","FrameEnvelope"},{"ValueWidth","1/2"},
            {"Endian","Little"},{"BuildBindingStatus",std::string(status)},
            {"Status","Implemented"},{"EvidenceBasis","Exact prologue plus same-invocation PreEncrypt construction"}}),
        point({{"ProbeId","Battle.HandlerRecordLength"},{"Category","Handler"},
            {"Module","God2_opt.exe"},{"Rva","0x0007F940"},
            {"ExpectedBytes","E8 rel32 at 0x00147096 and 0x001470AE"},
            {"InstructionSignature","both dispatcher calls target 0x0007F940; current record in ESI"},
            {"ExpectedCallTarget","God2_opt.exe+0x0007F940"},{"CallingConvention","x86 __stdcall"},
            {"ArgumentContract","opcode=u8; ESI=current decoded record"},{"ReturnContract","record length int"},
            {"FieldType","HandlerOpcode"},{"ValueWidth","1"},{"Endian","NotApplicable"},
            {"BuildBindingStatus",std::string(status)},{"Status","Implemented"},
            {"EvidenceBasis","Two exact dispatch call targets and record[0] == opcode validation"}}),
        point({{"ProbeId","ObjectResolver.Unconfirmed"},{"Category","ObjectResolver"},
            {"Module","God2_opt.exe"},{"Rva","Unknown"},{"ExpectedBytes","Unknown"},
            {"InstructionSignature","Not proven"},{"ExpectedCallTarget","Unknown"},
            {"CallingConvention","Unknown"},{"ArgumentContract","Unknown"},{"ReturnContract","Unknown"},
            {"FieldType","Unknown"},{"ValueWidth","Unknown"},{"Endian","Unknown"},
            {"BuildBindingStatus","EvidenceBlocked"},{"Status","EvidenceBlocked"},
            {"EvidenceBasis","Packed on-disk image and current runtime traces do not prove a domain-safe resolver contract"}}),
        point({{"ProbeId","StateMutation.Unconfirmed"},{"Category","StateMutation"},
            {"Module","God2_opt.exe"},{"Rva","Unknown"},{"ExpectedBytes","Unknown"},
            {"InstructionSignature","Not proven"},{"ExpectedCallTarget","Unknown"},
            {"CallingConvention","Unknown"},{"ArgumentContract","Unknown"},{"ReturnContract","Unknown"},
            {"FieldType","Unknown"},{"ValueWidth","Unknown"},{"Endian","Unknown"},
            {"BuildBindingStatus","EvidenceBlocked"},{"Status","EvidenceBlocked"},
            {"EvidenceBasis","No verified setter or authoritative assignment choke point exists in current evidence"}}),
        point({{"ProbeId","UIAnchor.Unconfirmed"},{"Category","UIAnchor"},
            {"Module","God2_opt.exe"},{"Rva","Unknown"},{"ExpectedBytes","Unknown"},
            {"InstructionSignature","Not proven"},{"ExpectedCallTarget","Unknown"},
            {"CallingConvention","Unknown"},{"ArgumentContract","Unknown"},{"ReturnContract","Unknown"},
            {"FieldType","Unknown"},{"ValueWidth","Unknown"},{"Endian","Unknown"},
            {"BuildBindingStatus","EvidenceBlocked"},{"Status","EvidenceBlocked"},
            {"EvidenceBasis","Presentation binding cannot be assigned a semantic without a verified call/data contract"}})
    };
    std::ostringstream array;
    array << '[';
    for (std::size_t index = 0; index < points.size(); ++index) {
        if (index != 0) array << ',';
        array << points[index];
    }
    array << ']';
    return MakeJsonObject({
        {"SchemaVersion","1"},{"ClientBuildIdentity","God2_opt.exe x86 exact runtime build"},
        {"ClientSha256",input.client_sha256},{"GeneratedAt",UtcNow()},
        {"ProbeContractVersion","God2SemanticProbe/1"},{"ProbePoints",array.str()}
    }, {"SchemaVersion","ProbePoints"});
}

bool IsDatabaseEligibleAuthority(std::string_view authority) {
    return authority == "StaticClientContent" || authority == "PersistentServerState";
}

} // namespace

SemanticRecoveryResult AnalyzeSemanticRecovery(const SemanticRecoveryInput& input) {
    SemanticRecoveryResult result;
    std::error_code error;
    fs::create_directories(input.semantic_output_directory, error);
    if (error) {
        result.error = "cannot create semantic output directory: " + error.message();
        return result;
    }

    const auto contexts = LoadCaptureContexts(input.normalized_capture_records);
    std::uint64_t invalid_events = 0;
    const auto events = LoadSemanticEvents(input.raw_semantic_events, input.client_sha256,
                                           input.session_id, &invalid_events);
    const bool any_build_mismatch = std::any_of(events.begin(), events.end(), [](const SemanticEvent& event) {
        return !event.build_matches;
    });
    const std::string build_status = any_build_mismatch ? "EvidenceBlockedBuildMismatch" :
        events.empty() ? "SemanticRuntimeEvidenceUnavailable" : "ExactBuildVerified";

    std::map<std::string, FieldAggregate> aggregates;
    std::map<std::string, std::set<std::string>> location_signatures;
    std::unordered_map<std::string, std::uint64_t> handlers_by_source_event;
    std::unordered_map<std::string, std::uint64_t> resolvers_by_token;
    std::unordered_map<std::string, std::uint64_t> mutations_by_token;
    std::unordered_map<std::string, std::uint64_t> mutation_mismatches_by_token;
    std::unordered_map<std::string, std::uint64_t> ui_by_token;
    std::unordered_map<std::string, std::string> field_by_token;
    std::set<std::string> observed_event_identities;
    for (const auto& event : events) observed_event_identities.insert(StableEventIdentity(event));
    for (const auto& event : events) {
        if (!event.build_matches) continue;
        if (event.event_type == "HandlerInvocation" &&
            (!GetString(event.fields, "ExactParentSemanticEventId").empty() ||
             GetString(event.fields, "ContextCorrelationBasis").starts_with("Exact"))) {
            const auto source_event = GetString(event.fields, "ContextSemanticEventId",
                GetString(event.fields, "ExactParentSemanticEventId"));
            if (!source_event.empty() && observed_event_identities.contains(
                    ScopedEventKey(event, source_event)))
                ++handlers_by_source_event[ScopedEventKey(event, source_event)];
        }
        else if (event.event_type == "ObjectResolution")
            ++resolvers_by_token[ScopedTokenKey(event, event.value_token_id)];
        else if (event.event_type == "StateMutation") {
            if (event.mutation_values_match)
                ++mutations_by_token[ScopedTokenKey(event, event.value_token_id)];
            else ++mutation_mismatches_by_token[ScopedTokenKey(event, event.value_token_id)];
        }
        else if (event.event_type == "UIAnchor")
            ++ui_by_token[ScopedTokenKey(event, event.value_token_id)];
    }
    std::ostringstream normalized_events;
    std::ostringstream value_flow;
    for (const auto& event : events) {
        if (event.event_type == "ParserRead") ++result.parser_read_events;
        else if (event.event_type == "SerializerWrite") ++result.serializer_write_events;
        else if (event.event_type == "HandlerInvocation") ++result.handler_argument_events;
        else if (event.event_type == "ObjectResolution") ++result.object_resolution_events;
        else if (event.event_type == "StateMutation") ++result.state_mutation_events;
        else if (event.event_type == "UIAnchor") ++result.ui_anchor_events;

        const auto context = contexts.find(event.hook_invocation_id);
        const auto logical_id = context == contexts.end() ? "" : context->second.logical_message_id;
        const auto frame_id = context == contexts.end() ? GetString(event.fields, "ProtocolFrameId") :
            context->second.protocol_frame_id;
        normalized_events << MakeJsonObject({
            {"SchemaId","God2SemanticEvent"},{"SchemaVersion","1"},{"SessionId",event.session_id},
            {"ClientBuildId",event.client_build_id},
            {"SemanticEventId",event.event_id},{"EventType",event.event_type},
            {"ObservedAtUnixMs",std::to_string(GetInt64(event.fields,"ObservedAtUnixMs").value_or(0))},
            {"Qpc",std::to_string(GetInt64(event.fields,"Qpc").value_or(0))},
            {"ProcessId",std::to_string(GetInt64(event.fields,"ProcessId").value_or(0))},
            {"ThreadId",std::to_string(GetInt64(event.fields,"ThreadId").value_or(0))},
            {"Direction",event.direction},{"Opcode",event.opcode},{"LogicalMessageId",logical_id},
            {"ProtocolFrameId",frame_id},{"HookInvocationId",event.hook_invocation_id},
            {"ParentInvocationId",GetString(event.fields,"ParentInvocationId")},
            {"ContextInvocationId",GetString(event.fields,"ContextInvocationId")},
            {"ExactParentSemanticEventId",GetString(event.fields,"ExactParentSemanticEventId")},
            {"ProbeId",GetString(event.fields,"ProbeId")},{"ProbeCategory",GetString(event.fields,"ProbeCategory")},
            {"Module",GetString(event.fields,"Module")},{"Rva",GetString(event.fields,"Rva")},
            {"CallerRva",GetString(event.fields,"CallerRva")},{"EvidenceBasis",event.evidence_basis},
            {"FrameOffset",std::to_string(event.offset)},{"Width",std::to_string(event.width)},
            {"ValueType",event.value_type},{"Endian",event.endian},
            {"RawBytes",GetString(event.fields,"RawBytes")},
            {"Value",GetString(event.fields,"Value",GetString(event.fields,"ParsedValue",
                GetString(event.fields,"ArgumentValue")))},
            {"ValueTokenId",event.value_token_id},{"FieldSemantic",event.semantic},
            {"AuthorityClassification",event.authority},
            {"AnalyzerBuildBindingStatus",event.build_matches ?
                "ExactBuildVerified" : "EvidenceBlockedBuildMismatch"},
            {"AnalyzerEvidenceLevel",event.build_matches ? "Recovered" : "Unknown"}
        }, {"SchemaVersion","ObservedAtUnixMs","Qpc","ProcessId","ThreadId","FrameOffset","Width"}) << '\n';

        if (event.event_type != "ParserRead" && event.event_type != "SerializerWrite") continue;
        auto& aggregate = aggregates[FieldKey(event)];
        if (aggregate.observations == 0) {
            aggregate.evidence_id = ProtocolFieldId(event);
            aggregate.direction = event.direction;
            aggregate.opcode = event.opcode;
            aggregate.semantic = event.semantic;
            aggregate.authority = event.authority;
            aggregate.value_type = event.value_type;
            aggregate.endian = event.endian;
            aggregate.offset = event.offset;
            aggregate.width = event.width;
        }
        ++aggregate.observations;
        if (event.event_type == "ParserRead") ++aggregate.parser_reads;
        else ++aggregate.serializer_writes;
        aggregate.handler_links += handlers_by_source_event[ScopedEventKey(event, event.event_id)];
        aggregate.resolver_links += resolvers_by_token[ScopedTokenKey(event, event.value_token_id)];
        aggregate.mutation_links += mutations_by_token[ScopedTokenKey(event, event.value_token_id)];
        aggregate.contradictions += mutation_mismatches_by_token[
            ScopedTokenKey(event, event.value_token_id)];
        aggregate.ui_links += ui_by_token[ScopedTokenKey(event, event.value_token_id)];
        if (event.event_type == "SerializerWrite" &&
            !GetString(event.fields, "SourceObjectDomain").empty())
            ++aggregate.runtime_source_writes;
        aggregate.build_matches = aggregate.build_matches && event.build_matches;
        aggregate.exact_boundary = aggregate.exact_boundary && event.exact_boundary;
        aggregate.exact_build_causal = aggregate.exact_build_causal &&
            event.build_matches;
        aggregate.session_ids.insert(event.session_id);
        aggregate.event_ids.insert(event.event_id);
        if (!event.value_token_id.empty())
            field_by_token[ScopedTokenKey(event, event.value_token_id)] = aggregate.evidence_id;
        location_signatures[LocationKey(event)].insert(event.value_type + "|" + event.semantic + "|" + event.authority);

        ++result.value_flow_edges;
        value_flow << MakeJsonObject({
            {"SchemaVersion","1"},{"SessionId",event.session_id},
            {"ValueFlowEdgeId","VFE-" + std::to_string(result.value_flow_edges)},
            {"ProtocolFieldEvidenceId",aggregate.evidence_id},
            {"ProtocolFrameId",frame_id},{"LogicalMessageId",logical_id},
            {"ValueTokenId",event.value_token_id},{"SourceEventId",event.event_id},
            {"EdgeKind",event.event_type == "ParserRead" ? "WireFieldToParserBoundary" :
                "SerializerBoundaryToWireField"},{"CorrelationBasis","ExactSameSemanticEvent"},
            {"EvidenceLevel",event.build_matches ? "Recovered" : "Unknown"}
        }, {"SchemaVersion"}) << '\n';
    }

    for (const auto& event : events) {
        if (event.event_type != "ObjectResolution" && event.event_type != "StateMutation" &&
            event.event_type != "UIAnchor") continue;
        const auto field = field_by_token.find(ScopedTokenKey(event, event.value_token_id));
        if (field == field_by_token.end()) continue;
        ++result.value_flow_edges;
        value_flow << MakeJsonObject({
            {"SchemaVersion","1"},{"SessionId",event.session_id},
            {"ValueFlowEdgeId","VFE-" + std::to_string(result.value_flow_edges)},
            {"ProtocolFieldEvidenceId",field->second},{"ValueTokenId",event.value_token_id},
            {"SourceEventId",event.event_id},
            {"EdgeKind",event.event_type == "ObjectResolution" ? "ValueTokenToObjectResolver" :
                event.event_type == "StateMutation" ? "ValueTokenToStateMutation" : "ValueTokenToUIAnchor"},
            {"CorrelationBasis","ExactValueToken"},
            {"EvidenceLevel",event.build_matches ? "Recovered" : "Unknown"}
        }, {"SchemaVersion"}) << '\n';
    }

    for (auto& [key, aggregate] : aggregates) {
        static_cast<void>(key);
        SemanticEvent location;
        location.direction = aggregate.direction;
        location.opcode = aggregate.opcode;
        location.offset = aggregate.offset;
        const auto signatures = location_signatures.find(LocationKey(location));
        aggregate.contradictions += signatures != location_signatures.end() && signatures->second.size() > 1 ?
            signatures->second.size() - 1 : 0;
    }
    AddHistoricalSessions(input.sessions_root, input.raw_semantic_events, input.client_sha256,
                          &observed_event_identities, &aggregates);

    std::ostringstream graph;
    std::ostringstream ledger;
    std::map<std::string, std::vector<const FieldAggregate*>> verified_packets;
    for (auto& [key, aggregate] : aggregates) {
        static_cast<void>(key);
        const bool repeated = aggregate.observations >= 2;
        const bool protocol_control_static_exception = aggregate.authority == "ProtocolControl" &&
            aggregate.exact_build_causal;
        const bool semantic_causal = aggregate.authority == "ProtocolControl" ?
            (aggregate.parser_reads > 0 || aggregate.serializer_writes > 0) :
            aggregate.direction == "ClientToServer" ?
                (aggregate.serializer_writes > 0 && aggregate.runtime_source_writes > 0) :
                (aggregate.parser_reads > 0 && aggregate.handler_links > 0 &&
                 (aggregate.resolver_links > 0 || aggregate.mutation_links > 0));
        const bool independent = aggregate.session_ids.size() >= 2 || protocol_control_static_exception;
        aggregate.verified = aggregate.build_matches && aggregate.exact_boundary && repeated && semantic_causal &&
            aggregate.contradictions == 0 && independent && aggregate.semantic != "Unknown" &&
            aggregate.value_type != "Unknown";
        aggregate.evidence_level = aggregate.verified ? "Verified" :
            (aggregate.build_matches && aggregate.exact_boundary && aggregate.observations > 0 ? "Recovered" : "Unknown");
        ++result.protocol_field_evidence;
        result.contradictions += aggregate.contradictions;
        if (aggregate.evidence_level == "Verified") {
            ++result.verified_fields;
            verified_packets[aggregate.direction + "|" + aggregate.opcode].push_back(&aggregate);
        } else if (aggregate.evidence_level == "Recovered") ++result.recovered_fields;
        else ++result.unknown_fields;
        std::vector<std::string> sessions(aggregate.session_ids.begin(), aggregate.session_ids.end());
        std::vector<std::string> event_ids(aggregate.event_ids.begin(), aggregate.event_ids.end());
        graph << MakeJsonObject({
            {"SchemaVersion","1"},{"SessionId",input.session_id},
            {"EvidenceGraphId","SEG-" + std::to_string(result.protocol_field_evidence)},
            {"ProtocolFieldEvidenceId",aggregate.evidence_id},{"Direction",aggregate.direction},
            {"Opcode",aggregate.opcode},{"Offset",std::to_string(aggregate.offset)},
            {"Width",std::to_string(aggregate.width)},{"Type",aggregate.value_type},
            {"Endian",aggregate.endian},{"SemanticCandidate",aggregate.semantic},
            {"AuthorityClassification",aggregate.authority},
            {"FrameBoundaryEvidence","VerifiedExactRecordBoundary"},
            {"FieldBoundaryEvidence",aggregate.exact_boundary ? "Verified" : "EvidenceBlocked"},
            {"FieldTypeEvidence",aggregate.value_type == "Unknown" ? "EvidenceBlocked" : "RuntimeTypedBoundary"},
            {"EndianEvidence",aggregate.width <= 1 ? "NotApplicable" : aggregate.endian},
            {"ParserReadEvidence",std::to_string(aggregate.parser_reads)},
            {"SerializerWriteEvidence",std::to_string(aggregate.serializer_writes)},
            {"HandlerEvidence",std::to_string(aggregate.handler_links)},
            {"ObjectResolutionEvidence",std::to_string(aggregate.resolver_links)},
            {"MutationEvidence",std::to_string(aggregate.mutation_links)},
            {"UIAnchorEvidence",std::to_string(aggregate.ui_links)},
            {"ActionPatternEvidence","SupportingOnly"},
            {"CrossSessionEvidence",std::to_string(aggregate.session_ids.size())},
            {"PositiveObservationCount",std::to_string(aggregate.observations)},
            {"NegativeObservationCount","0"},{"ContradictionCount",std::to_string(aggregate.contradictions)},
            {"SourceSemanticEventIds",JsonArray(event_ids)},{"EvidenceLevel",aggregate.evidence_level},
            {"VerificationGate",aggregate.verified ? "Passed" : "NotPassed"},
            {"SingleSessionStaticExceptionApplied",aggregate.session_ids.size() < 2 &&
                protocol_control_static_exception ? "true" : "false"}
        }, {"SchemaVersion","Offset","Width","ParserReadEvidence","SerializerWriteEvidence",
            "HandlerEvidence","ObjectResolutionEvidence","MutationEvidence","UIAnchorEvidence",
            "CrossSessionEvidence","PositiveObservationCount","NegativeObservationCount",
            "ContradictionCount","SourceSemanticEventIds","SingleSessionStaticExceptionApplied"}) << '\n';
        ledger << MakeJsonObject({
            {"SchemaVersion","1"},{"ProtocolFieldEvidenceId",aggregate.evidence_id},
            {"ClientBuildIdentity",input.client_sha256},{"SessionIds",JsonArray(sessions)},
            {"SessionCount",std::to_string(aggregate.session_ids.size())},
            {"ObservationCount",std::to_string(aggregate.observations)},
            {"ContradictionCount",std::to_string(aggregate.contradictions)},
            {"EvidenceLevel",aggregate.evidence_level}
        }, {"SchemaVersion","SessionIds","SessionCount","ObservationCount","ContradictionCount"}) << '\n';
    }

    std::ostringstream packet_array;
    packet_array << '[';
    bool first_packet = true;
    for (const auto& [packet_key, fields] : verified_packets) {
        static_cast<void>(packet_key);
        if (!first_packet) packet_array << ',';
        first_packet = false;
        std::ostringstream field_array;
        field_array << '[';
        for (std::size_t index = 0; index < fields.size(); ++index) {
            if (index != 0) field_array << ',';
            const auto* field = fields[index];
            field_array << MakeJsonObject({
                {"Offset",std::to_string(field->offset)},{"Width",std::to_string(field->width)},
                {"Type",field->value_type},{"Endian",field->endian},{"Semantic",field->semantic},
                {"EvidenceLevel","Verified"},{"EvidenceGraphId",field->evidence_id},
                {"AuthorityClassification",field->authority}
            }, {"Offset","Width"});
        }
        field_array << ']';
        packet_array << MakeJsonObject({
            {"ProtocolSpecId","PS-" + fields.front()->direction + "-" + fields.front()->opcode},
            {"Direction",fields.front()->direction},{"Opcode",fields.front()->opcode},
            {"LengthContract","u16le exact frame length plus additive 0x3C checksum"},
            {"ClientBuildIdentity",input.client_sha256},{"Fields",field_array.str()}
        }, {"Fields"});
    }
    packet_array << ']';
    result.verified_spec_packets = verified_packets.size();
    result.server_integration_ready = verified_packets.size();

    std::ostringstream integration_array;
    integration_array << '[';
    bool first_integration = true;
    for (const auto& [packet_key, fields] : verified_packets) {
        static_cast<void>(packet_key);
        if (!first_integration) integration_array << ',';
        first_integration = false;
        const bool database_allowed = std::all_of(fields.begin(), fields.end(), [](const FieldAggregate* field) {
            return IsDatabaseEligibleAuthority(field->authority);
        });
        if (database_allowed) ++result.database_integration_ready;
        integration_array << MakeJsonObject({
            {"ProtocolSpecId","PS-" + fields.front()->direction + "-" + fields.front()->opcode},
            {"Direction",fields.front()->direction},{"Opcode",fields.front()->opcode},
            {"IntegrationDestination",fields.front()->direction == "ClientToServer" ?
                "ServerDecoder" : "ServerSerializer"},
            {"DatabaseMutationAllowed",database_allowed ? "true" : "false"},
            {"AuthorityClassification",fields.front()->authority},
            {"EvidenceLevel","Verified"}
        }, {"DatabaseMutationAllowed"});
    }
    integration_array << ']';

    const auto enhanced_log = ReadUtf8File(input.session_path / L"reports" / L"enhanced-capture.log");
    if (enhanced_log && (enhanced_log->find("semanticEvidenceIncomplete=1") != std::string::npos ||
        enhanced_log->find("semanticDroppedEvents=0") == std::string::npos &&
        enhanced_log->find("semanticDroppedEvents=") != std::string::npos))
        result.semantic_evidence_incomplete = true;
    if (invalid_events > 0) result.semantic_evidence_incomplete = true;

    result.status = events.empty() ? "SemanticRuntimeEvidenceUnavailable" :
        any_build_mismatch ? "EvidenceBlockedBuildMismatch" :
        result.semantic_evidence_incomplete ? "SemanticEvidenceIncomplete" : "SemanticRuntimeEvidenceAvailable";
    const auto summary = MakeJsonObject({
        {"SchemaVersion","1"},{"SessionId",input.session_id},{"SemanticProbeStatus",result.status},
        {"ParserReadEventCount",std::to_string(result.parser_read_events)},
        {"SerializerWriteEventCount",std::to_string(result.serializer_write_events)},
        {"HandlerArgumentEventCount",std::to_string(result.handler_argument_events)},
        {"ObjectResolutionEventCount",std::to_string(result.object_resolution_events)},
        {"StateMutationEventCount",std::to_string(result.state_mutation_events)},
        {"UIAnchorEventCount",std::to_string(result.ui_anchor_events)},
        {"ValueFlowEdgeCount",std::to_string(result.value_flow_edges)},
        {"ProtocolFieldEvidenceCount",std::to_string(result.protocol_field_evidence)},
        {"UnknownFieldSemanticCount",std::to_string(result.unknown_fields)},
        {"CandidateFieldSemanticCount",std::to_string(result.candidate_fields)},
        {"RecoveredFieldSemanticCount",std::to_string(result.recovered_fields)},
        {"VerifiedFieldSemanticCount",std::to_string(result.verified_fields)},
        {"VerifiedProtocolSpecPacketCount",std::to_string(result.verified_spec_packets)},
        {"VerifiedProtocolSpecFieldCount",std::to_string(result.verified_fields)},
        {"ServerIntegrationReadyCount",std::to_string(result.server_integration_ready)},
        {"DatabaseIntegrationReadyCount",std::to_string(result.database_integration_ready)},
        {"ContradictionCount",std::to_string(result.contradictions)},
        {"InvalidSemanticEventCount",std::to_string(invalid_events)},
        {"SemanticEvidenceIncomplete",result.semantic_evidence_incomplete ? "true" : "false"},
        {"OldEvidenceCompatibility",events.empty() ? "SemanticRuntimeEvidenceUnavailableNotFailure" : "NotApplicable"}
    }, {"SchemaVersion","ParserReadEventCount","SerializerWriteEventCount","HandlerArgumentEventCount",
        "ObjectResolutionEventCount","StateMutationEventCount","UIAnchorEventCount","ValueFlowEdgeCount",
        "ProtocolFieldEvidenceCount","UnknownFieldSemanticCount","CandidateFieldSemanticCount",
        "RecoveredFieldSemanticCount","VerifiedFieldSemanticCount","VerifiedProtocolSpecPacketCount",
        "VerifiedProtocolSpecFieldCount","ServerIntegrationReadyCount","DatabaseIntegrationReadyCount",
        "ContradictionCount","InvalidSemanticEventCount","SemanticEvidenceIncomplete"});

    const auto spec = MakeJsonObject({
        {"SchemaVersion","1"},{"ClientBuildIdentity",input.client_sha256},
        {"EvidencePolicy","Only fields passing structural, causal, repetition, contradiction and independence gates"},
        {"Packets",packet_array.str()}
    }, {"SchemaVersion","Packets"});
    const auto integration = MakeJsonObject({
        {"SchemaVersion","1"},{"ClientBuildIdentity",input.client_sha256},
        {"Policy","Only Verified facts; database writes limited to StaticClientContent or PersistentServerState"},
        {"Entries",integration_array.str()}
    }, {"SchemaVersion","Entries"});

    const std::vector<std::pair<fs::path, std::string>> outputs = {
        {input.semantic_output_directory / L"semantic-events.jsonl", normalized_events.str()},
        {input.semantic_output_directory / L"value-flow.jsonl", value_flow.str()},
        {input.semantic_output_directory / L"semantic-evidence-graph.jsonl", graph.str()},
        {input.semantic_output_directory / L"cross-session-ledger.jsonl", ledger.str()},
        {input.semantic_output_directory / L"verified-protocol-spec.json", spec + "\n"},
        {input.semantic_output_directory / L"verified-integration-manifest.json", integration + "\n"},
        {input.semantic_output_directory / L"semantic-summary.json", summary + "\n"},
        {input.semantic_output_directory / L"semantic-probe-plan.json", ProbePlanJson(input, build_status) + "\n"}
    };
    for (const auto& [path, content] : outputs) {
        if (!WriteUtf8FileAtomic(path, content)) {
            result.error = "cannot write semantic artifact: " + WideToUtf8(path.wstring());
            return result;
        }
    }
    result.success = true;
    return result;
}

int RunSemanticRecoverySelfTest(const fs::path& report_path) {
    struct Check { std::string name; bool passed; std::string evidence; };
    std::vector<Check> checks;
    const auto check = [&](std::string name, bool passed, std::string evidence) {
        checks.push_back({std::move(name), passed, std::move(evidence)});
    };
    const std::string hash(64, 'A');
    const fs::path root = report_path.parent_path() / L"semantic-selftest-work";
    std::error_code error;
    fs::remove_all(root, error);
    fs::create_directories(root / L"current" / L"raw", error);
    fs::create_directories(root / L"current" / L"semantic", error);
    fs::create_directories(root / L"history" / L"raw", error);
    if (error) return 73;
    const fs::path capture_path = root / L"current" / L"raw" / L"capture-records.jsonl";
    const fs::path event_path = root / L"current" / L"raw" / L"semantic-events.jsonl";
    WriteUtf8FileAtomic(capture_path, "");

    const auto field_event = [&](std::string id, std::string type, std::string direction,
                                 std::string opcode, int offset, std::string semantic,
                                 std::string authority, std::string token,
                                 std::string source_domain = {}) {
        return MakeJsonObject({
            {"SchemaId","God2SemanticEvent"},{"SchemaVersion","1"},{"SemanticEventId",id},
            {"SessionId","semantic-selftest"},{"ClientBuildId",hash},
            {"EventType",type},{"ObservedAtUnixMs","1786105000000"},{"Qpc","100"},
            {"ProcessId","42"},{"ThreadId","7"},{"Direction",direction},{"Opcode",opcode},
            {"ProtocolFrameId","PF-" + id},{"HookInvocationId","hook-" + id},
            {"ProbeId",type == "ParserRead" ? "PacketDecode.FrameBoundary" : "OutboundEnqueue.FrameBuilder"},
            {"ProbeCategory",type == "ParserRead" ? "ParserReader" : "SerializerWriter"},
            {"Module","God2_opt.exe"},{"Rva",type == "ParserRead" ? "0x00078D70" : "0x0007FC10"},
            {"CallerRva","SyntheticExactCallsite"},{"ClientSha256Expected",hash},
            {"BuildBindingStatus","ExactInstructionIdentityVerified"},
            {"EvidenceBasis","ExactBuildSyntheticCausalBoundary"},{"EvidenceLevel","Recovered"},
            {"FrameOffset",std::to_string(offset)},{"DestinationOffset",std::to_string(offset)},
            {"FrameOffsetStatus","VerifiedExactBoundary"},{"Width","4"},
            {"ReadType",type == "ParserRead" ? "Int32" : "NotApplicable"},
            {"WriteType",type == "SerializerWrite" ? "Int32" : "NotApplicable"},
            {"Endian","Little"},{"RawBytes","25000000"},{"ParsedValue","37"},{"Value","37"},
            {"ValueTokenId",token},{"FieldSemantic",semantic},
            {"AuthorityClassification",authority},{"SourceObjectDomain",source_domain},
            {"SensitiveValue","false"}
        }, {"SchemaVersion","ObservedAtUnixMs","Qpc","ProcessId","ThreadId","FrameOffset",
            "DestinationOffset","Width","ParsedValue","Value","SensitiveValue"}) + "\n";
    };
    const auto handler_event = [&](std::string id, std::string source_event, std::string token,
                                   std::string opcode) {
        return MakeJsonObject({
            {"SchemaId","God2SemanticEvent"},{"SchemaVersion","1"},{"SemanticEventId",id},
            {"SessionId","semantic-selftest"},{"ClientBuildId",hash},
            {"EventType","HandlerInvocation"},{"ObservedAtUnixMs","1786105000001"},
            {"Qpc","101"},{"ProcessId","42"},{"ThreadId","7"},
            {"Direction","ServerToClient"},{"Opcode",opcode},{"HookInvocationId","hook-" + id},
            {"ContextSemanticEventId",source_event},{"ExactParentSemanticEventId",source_event},
            {"ContextCorrelationBasis","ExactNestedInvocation"},{"ProbeId","Synthetic.Handler"},
            {"ClientSha256Expected",hash},{"BuildBindingStatus","ExactInstructionIdentityVerified"},
            {"EvidenceBasis","ExactBuildSyntheticHandlerArgument"},{"ArgumentType","Int32"},
            {"AssociatedValueTokenId",token},{"SensitiveValue","false"}
        }, {"SchemaVersion","ObservedAtUnixMs","Qpc","ProcessId","ThreadId",
            "SensitiveValue"}) + "\n";
    };
    const auto mutation_event = [&](std::string id, std::string token, std::string opcode, bool match) {
        return MakeJsonObject({
            {"SchemaId","God2SemanticEvent"},{"SchemaVersion","1"},{"SemanticEventId",id},
            {"SessionId","semantic-selftest"},{"ClientBuildId",hash},
            {"EventType","StateMutation"},{"ObservedAtUnixMs","1786105000002"},
            {"Qpc","102"},{"ProcessId","42"},{"ThreadId","7"},
            {"Direction","ServerToClient"},{"Opcode",opcode},{"HookInvocationId","hook-" + id},
            {"ProbeId","Synthetic.StateMutation"},{"ClientSha256Expected",hash},
            {"BuildBindingStatus","ExactInstructionIdentityVerified"},
            {"EvidenceBasis","ExactBuildSyntheticStateMutation"},{"ValueTokenId",token},
            {"ObjectDomain","Character"},{"Property","HP"},{"BeforeValue","100"},
            {"InputValue","-37"},{"AfterValue",match ? "63" : "64"},{"Delta","-37"},
            {"AuthorityClassification","EphemeralRuntimeState"},{"SensitiveValue","false"}
        }, {"SchemaVersion","ObservedAtUnixMs","Qpc","ProcessId","ThreadId","BeforeValue",
            "InputValue","AfterValue","Delta","SensitiveValue"}) + "\n";
    };
    const auto ui_event = [&](std::string id, std::string token) {
        return MakeJsonObject({
            {"SchemaId","God2SemanticEvent"},{"SchemaVersion","1"},{"SemanticEventId",id},
            {"SessionId","semantic-selftest"},{"ClientBuildId",hash},
            {"EventType","UIAnchor"},{"ObservedAtUnixMs","1786105000003"},{"Qpc","103"},
            {"ProcessId","42"},{"ThreadId","7"},{"Direction","ServerToClient"},{"Opcode","0x30"},
            {"ProbeId","Synthetic.UI"},{"ClientSha256Expected",hash},
            {"BuildBindingStatus","ExactInstructionIdentityVerified"},{"EvidenceBasis","SyntheticUIOnly"},
            {"ValueTokenId",token},{"AuthorityClassification","PresentationOnly"},
            {"SensitiveValue","false"}
        }, {"SchemaVersion","ObservedAtUnixMs","Qpc","ProcessId","ThreadId",
            "SensitiveValue"}) + "\n";
    };

    God2SemanticEventV2 probe_v2;
    probe_v2.event_type = UltimateSemanticEventType::ParserRead;
    probe_v2.event_id = "V2-PROBE-PARSER-1";
    probe_v2.sequence = 1;
    probe_v2.timestamp = "1786105000004";
    probe_v2.thread_id = 7;
    probe_v2.process_id = 42;
    probe_v2.session_id = "semantic-v2-fixture";
    probe_v2.client_build_id = hash;
    probe_v2.module_id = "God2_opt.exe";
    probe_v2.rva = 0x00078D70;
    probe_v2.callsite_rva = 0x00078A48;
    probe_v2.value_token = "VT-V2-1";
    probe_v2.source_token = "PF-V2-1";
    probe_v2.authority_hint = UltimateAuthority::Verified;
    probe_v2.payload =
        "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
        "\"FrameOffset\":4,\"Width\":4,\"ValueType\":\"UInt32\","
        "\"RawBytes\":\"25000000\",\"ParsedValue\":37,"
        "\"FieldSemantic\":\"CharacterId\"}";
    probe_v2.evidence_binding = {
        {"SchemaId", "God2SemanticEvent"}, {"SemanticEventId", probe_v2.event_id},
        {"ObservedAtUnixMs", probe_v2.timestamp}, {"Qpc", "104"},
        {"Direction", "ServerToClient"}, {"Opcode", "0x41"},
        {"ProtocolFrameId", "PF-V2-1"}, {"HookInvocationId", "hook-v2-1"},
        {"ProbeId", "PacketDecode.FrameBoundary"}, {"ProbeCategory", "ParserReader"},
        {"Module", "God2_opt.exe"}, {"Rva", "0x00078D70"},
        {"CallerRva", "0x00078A4D"}, {"ClientSha256Expected", hash},
        {"BuildBindingStatus", "ExactInstructionIdentityVerified"},
        {"EvidenceBasis", "ExactBuildPacketDecodeFieldRead"},
        {"FrameOffset", "4"}, {"DestinationOffset", "4"}, {"Width", "4"},
        {"ReadType", "UInt32"}, {"WriteType", "NotApplicable"},
        {"Endian", "Little"}, {"RawBytes", "25000000"},
        {"ParsedValue", "37"}, {"ValueTokenId", "VT-V2-1"},
        {"FieldSemantic", "CharacterId"}, {"AuthorityClassification", "ProtocolControl"}
    };
    auto diagnostic_v2 = probe_v2;
    diagnostic_v2.event_type = UltimateSemanticEventType::ProbeDiagnostic;
    diagnostic_v2.event_id = "V2-DIAGNOSTIC-1";
    diagnostic_v2.sequence = 2;
    diagnostic_v2.timestamp = "1786105000005";
    diagnostic_v2.rva = 0;
    diagnostic_v2.callsite_rva = 0;
    diagnostic_v2.value_token.clear();
    diagnostic_v2.source_token.clear();
    diagnostic_v2.authority_hint = UltimateAuthority::Unknown;
    diagnostic_v2.payload = "{}";
    diagnostic_v2.evidence_binding.clear();
    const fs::path v2_fixture_path = root / L"current" / L"raw" / L"semantic-v2-probe-fixture.jsonl";
    WriteUtf8FileAtomic(v2_fixture_path, SerializeSemanticEventV2(probe_v2) + "\n" +
        SerializeSemanticEventV2(diagnostic_v2) + "\n");
    std::uint64_t v2_invalid = 0;
    const auto v2_loaded = LoadSemanticEvents(v2_fixture_path, hash,
        "semantic-v2-fixture", &v2_invalid);
    check("actual probe-shaped v2 compatibility adapter",
          v2_invalid == 0 && v2_loaded.size() == 1 &&
          v2_loaded.front().event_type == "ParserRead" &&
          v2_loaded.front().build_matches && v2_loaded.front().offset == 4 &&
          v2_loaded.front().semantic == "CharacterId",
          "valid v2 ParserRead is adapted; valid ProbeDiagnostic is ignored without corruption");

    auto untrusted_binding = probe_v2;
    untrusted_binding.event_id = "V2-PROBE-PARSER-UNTRUSTED";
    untrusted_binding.evidence_binding["SemanticEventId"] = untrusted_binding.event_id;
    untrusted_binding.evidence_binding["BuildBindingStatus"] =
        "NotExactInstructionIdentityVerified";
    const fs::path untrusted_fixture_path =
        root / L"current" / L"raw" / L"semantic-v2-untrusted-binding.jsonl";
    WriteUtf8FileAtomic(untrusted_fixture_path,
        SerializeSemanticEventV2(untrusted_binding) + "\n");
    std::uint64_t untrusted_invalid = 0;
    const auto untrusted_loaded =
        LoadSemanticEvents(untrusted_fixture_path, hash,
            "semantic-v2-fixture", &untrusted_invalid);
    check("v2 build binding status exact allowlist",
          untrusted_invalid == 0 && untrusted_loaded.size() == 1 &&
          !untrusted_loaded.front().build_matches,
          "substring status cannot satisfy exact build binding");

    const auto replace_all = [](std::string value, std::string_view from,
                                std::string_view to) {
        std::size_t position = 0;
        while ((position = value.find(from, position)) != std::string::npos) {
            value.replace(position, from.size(), to);
            position += to.size();
        }
        return value;
    };

    const fs::path duplicate_identity_path = root / L"current" / L"raw" /
        L"semantic-duplicate-identity.jsonl";
    const std::string duplicate_identity_event = field_event(
        "DUPLICATE-STABLE-ID", "ParserRead", "ServerToClient", "0x7D", 4,
        "DuplicateMustNotCountTwice", "ProtocolControl", "VT-DUPLICATE");
    WriteUtf8FileAtomic(duplicate_identity_path,
        duplicate_identity_event + duplicate_identity_event);
    std::uint64_t duplicate_invalid = 0;
    const auto duplicate_loaded = LoadSemanticEvents(duplicate_identity_path, hash,
        "semantic-selftest", &duplicate_invalid);
    check("stable event identity duplicate rejected",
        duplicate_loaded.size() == 1 && duplicate_invalid == 1,
        "the same build/session/process/EventId is accepted once only");
    const fs::path zero_process_path = root / L"current" / L"raw" /
        L"semantic-zero-process.jsonl";
    WriteUtf8FileAtomic(zero_process_path, replace_all(duplicate_identity_event,
        "\"ProcessId\":42", "\"ProcessId\":0"));
    std::uint64_t zero_process_invalid = 0;
    const auto zero_process_loaded = LoadSemanticEvents(zero_process_path, hash,
        "semantic-selftest", &zero_process_invalid);
    check("strict process identity required", zero_process_loaded.empty() &&
        zero_process_invalid == 1, "ProcessId zero cannot become semantic evidence");

    const fs::path v1_causal_compatibility_path = root / L"current" / L"raw" /
        L"semantic-v1-causal-compatibility.jsonl";
    WriteUtf8FileAtomic(v1_causal_compatibility_path,
        mutation_event("V1-SEVEN-CAUSAL-KEYS", "VT-V1-SEVEN", "0x7E", true));
    std::uint64_t v1_causal_invalid = 0;
    const auto v1_causal_loaded = LoadSemanticEvents(v1_causal_compatibility_path, hash,
        "semantic-selftest", &v1_causal_invalid);
    const fs::path v1_missing_status_path = root / L"current" / L"raw" /
        L"semantic-v1-missing-sensitive-status.jsonl";
    auto v1_missing_status_event = mutation_event(
        "V1-MISSING-SENSITIVE-STATUS", "S3cr3t-Value-8472", "0x7F", true);
    v1_missing_status_event = replace_all(std::move(v1_missing_status_event),
        ",\"SensitiveValue\":false", "");
    v1_missing_status_event = replace_all(std::move(v1_missing_status_event),
        "\"Property\":\"HP\"", "\"Property\":\"S3cr3t-Value-8472\"");
    WriteUtf8FileAtomic(v1_missing_status_path, v1_missing_status_event);
    std::uint64_t v1_missing_status_invalid = 0;
    const auto v1_missing_status_loaded = LoadSemanticEvents(
        v1_missing_status_path, hash, "semantic-selftest",
        &v1_missing_status_invalid);
    const bool v1_missing_status_suppressed =
        v1_missing_status_invalid == 0 && v1_missing_status_loaded.size() == 1 &&
        v1_missing_status_loaded.front().value_token_id.empty() &&
        GetString(v1_missing_status_loaded.front().fields, "Property").empty() &&
        GetString(v1_missing_status_loaded.front().fields, "BeforeValue").empty() &&
        GetString(v1_missing_status_loaded.front().fields,
            "SensitiveMaskStatus") ==
                "SensitivePayloadSuppressedMetadataOnly";
    check("v1 seven causal keys compatibility retained",
        v1_causal_invalid == 0 && v1_causal_loaded.size() == 1 &&
        GetString(v1_causal_loaded.front().fields, "SourceObjectDomain").empty() &&
        GetString(v1_causal_loaded.front().fields, "ObjectDomain") == "Character" &&
        GetString(v1_causal_loaded.front().fields, "Property") == "HP" &&
        GetString(v1_causal_loaded.front().fields, "BeforeValue") == "100" &&
        GetString(v1_causal_loaded.front().fields, "InputValue") == "-37" &&
        GetString(v1_causal_loaded.front().fields, "AfterValue") == "63" &&
        GetString(v1_causal_loaded.front().fields, "Delta") == "-37" &&
        v1_missing_status_suppressed,
        "explicit-safe legacy causal keys survive while missing-status token/property carriers remain metadata-only");

    std::string events;
    events += field_event("C10-1","SerializerWrite","ClientToServer","0x10",0,"FrameLength","ProtocolControl","VT-C10-1");
    events += field_event("C10-2","SerializerWrite","ClientToServer","0x10",0,"FrameLength","ProtocolControl","VT-C10-2");
    events += field_event("S20-1","ParserRead","ServerToClient","0x20",2,"Opcode","ProtocolControl","VT-S20-1");
    events += field_event("S20-2","ParserRead","ServerToClient","0x20",2,"Opcode","ProtocolControl","VT-S20-2");
    events += field_event("ONE-1","ParserRead","ServerToClient","0x30",8,"OneObservation","UnknownAuthority","VT-ONE");
    events += ui_event("UI-ONE","VT-ONE");
    for (int occurrence = 1; occurrence <= 2; ++occurrence) {
        events += field_event("CON-A" + std::to_string(occurrence),"ParserRead","ServerToClient","0x31",10,
            "ConflictA","ProtocolControl","VT-CON-A" + std::to_string(occurrence));
        events += field_event("CON-B" + std::to_string(occurrence),"ParserRead","ServerToClient","0x31",10,
            "ConflictB","ProtocolControl","VT-CON-B" + std::to_string(occurrence));
        events += field_event("P40-" + std::to_string(occurrence),"SerializerWrite","ClientToServer","0x40",4,
            "CharacterExperience","PersistentServerState","VT-P40-" + std::to_string(occurrence),"Character");
        events += field_event("E41-" + std::to_string(occurrence),"SerializerWrite","ClientToServer","0x41",4,
            "BattleTurnValue","EphemeralRuntimeState","VT-E41-" + std::to_string(occurrence),"BattleState");
        events += field_event("U42-" + std::to_string(occurrence),"SerializerWrite","ClientToServer","0x42",4,
            "UnprovenAuthorityValue","UnknownAuthority","VT-U42-" + std::to_string(occurrence),"Unknown");
        const std::string hp_event = "HP50-" + std::to_string(occurrence);
        const std::string hp_token = "VT-HP50-" + std::to_string(occurrence);
        events += field_event(hp_event,"ParserRead","ServerToClient","0x50",12,
            "HitPointDelta","EphemeralRuntimeState",hp_token);
        events += handler_event("H-" + hp_event,hp_event,hp_token,"0x50");
        events += mutation_event("M-" + hp_event,hp_token,"0x50",true);
        const std::string bad_event = "BAD51-" + std::to_string(occurrence);
        const std::string bad_token = "VT-BAD51-" + std::to_string(occurrence);
        events += field_event(bad_event,"ParserRead","ServerToClient","0x51",12,
            "HitPointDelta","EphemeralRuntimeState",bad_token);
        events += handler_event("H-" + bad_event,bad_event,bad_token,"0x51");
        events += mutation_event("M-" + bad_event,bad_token,"0x51",false);
    }
    if (!WriteUtf8FileAtomic(event_path, events)) return 74;
    std::string historical;
    historical += field_event("HIST-P40","SerializerWrite","ClientToServer","0x40",4,
        "CharacterExperience","PersistentServerState","VT-HIST-P40","Character");
    historical += field_event("HIST-E41","SerializerWrite","ClientToServer","0x41",4,
        "BattleTurnValue","EphemeralRuntimeState","VT-HIST-E41","BattleState");
    historical += field_event("HIST-U42","SerializerWrite","ClientToServer","0x42",4,
        "UnprovenAuthorityValue","UnknownAuthority","VT-HIST-U42","Unknown");
    historical += field_event("HIST-HP50","ParserRead","ServerToClient","0x50",12,
        "HitPointDelta","EphemeralRuntimeState","VT-HIST-HP50");
    historical += field_event("HIST-BAD51","ParserRead","ServerToClient","0x51",12,
        "HitPointDelta","EphemeralRuntimeState","VT-HIST-BAD51");
    historical = replace_all(std::move(historical),
        "\"SessionId\":\"semantic-selftest\"",
        "\"SessionId\":\"semantic-history\"");
    WriteUtf8FileAtomic(root / L"history" / L"raw" / L"semantic-events.jsonl", historical);
    fs::create_directories(root / L"fake-newer-copy" / L"raw", error);
    WriteUtf8FileAtomic(root / L"fake-newer-copy" / L"raw" / L"semantic-events.jsonl",
        historical);

    const auto analyzed = AnalyzeSemanticRecovery({"semantic-selftest",hash,root / L"current",capture_path,
        event_path,root / L"current" / L"semantic",root});
    const auto graph = ReadUtf8File(root / L"current" / L"semantic" / L"semantic-evidence-graph.jsonl").value_or("");
    const auto flow = ReadUtf8File(root / L"current" / L"semantic" / L"value-flow.jsonl").value_or("");
    const auto ledger = ReadUtf8File(root / L"current" / L"semantic" / L"cross-session-ledger.jsonl").value_or("");
    const auto spec = ReadUtf8File(root / L"current" / L"semantic" / L"verified-protocol-spec.json").value_or("");
    const auto manifest = ReadUtf8File(root / L"current" / L"semantic" / L"verified-integration-manifest.json").value_or("");
    check("1_typed_field_reconstruction", analyzed.success && analyzed.protocol_field_evidence >= 10,
        "ProtocolFieldEvidence=" + std::to_string(analyzed.protocol_field_evidence));
    check("2_little_endian_preserved", graph.find("\"Endian\":\"Little\"") != std::string::npos,
        "semantic graph retains typed little-endian evidence");
    check("3_parser_field_correlation", analyzed.parser_read_events >= 9 &&
        flow.find("WireFieldToParserBoundary") != std::string::npos, "ParserRead value flow emitted");
    check("4_serializer_field_correlation", analyzed.serializer_write_events >= 8 &&
        flow.find("SerializerBoundaryToWireField") != std::string::npos, "SerializerWrite value flow emitted");
    check("5_handler_argument_trace", analyzed.handler_argument_events == 4,
        "HandlerArgumentEvents=" + std::to_string(analyzed.handler_argument_events));
    check("6_field_to_mutation", analyzed.state_mutation_events == 4 &&
        flow.find("ValueTokenToStateMutation") != std::string::npos, "exact ValueToken mutation edges emitted");
    check("7_exact_state_delta_match", spec.find("\"Opcode\":\"0x50\"") != std::string::npos,
        "matching Before/Input/After/Delta causal chain reached Verified");
    check("8_state_delta_mismatch_blocks", spec.find("\"Opcode\":\"0x51\"") == std::string::npos &&
        analyzed.contradictions > 0, "mismatched mutation chain is absent from verified spec");
    check("9_ui_only_cannot_verify", analyzed.ui_anchor_events == 1 &&
        spec.find("OneObservation") == std::string::npos, "UI-only support did not pass gate");
    check("10_single_observation_cannot_verify", spec.find("\"Opcode\":\"0x30\"") == std::string::npos,
        "single observation remains below Verified");
    check("11_contradictions_block_verified", spec.find("\"Opcode\":\"0x31\"") == std::string::npos,
        "conflicting location semantics remain outside verified spec");
    check("12_cross_session_ledger", ledger.find("\"SessionCount\":2") != std::string::npos,
        "embedded same-build session identities are recorded once despite a copied JSONL folder");
    check("13_protocol_control_static_gate", spec.find("\"Opcode\":\"0x10\"") != std::string::npos &&
        spec.find("\"Opcode\":\"0x20\"") != std::string::npos,
        "exact-build structural protocol controls passed the explicit exception");
    check("14_persistent_state_db_eligible", manifest.find("PS-ClientToServer-0x40") != std::string::npos &&
        manifest.find("PS-ClientToServer-0x40\",\"Direction\":\"ClientToServer\",\"Opcode\":\"0x40\",\"IntegrationDestination\":\"ServerDecoder\",\"DatabaseMutationAllowed\":true") != std::string::npos,
        "Verified PersistentServerState is database eligible");
    check("15_ephemeral_runtime_db_blocked", manifest.find("PS-ClientToServer-0x41") != std::string::npos &&
        manifest.find("PS-ClientToServer-0x41\",\"Direction\":\"ClientToServer\",\"Opcode\":\"0x41\",\"IntegrationDestination\":\"ServerDecoder\",\"DatabaseMutationAllowed\":false") != std::string::npos,
        "EphemeralRuntimeState is explicitly database blocked");
    check("16_unknown_authority_db_blocked", manifest.find("PS-ClientToServer-0x42") != std::string::npos &&
        manifest.find("PS-ClientToServer-0x42\",\"Direction\":\"ClientToServer\",\"Opcode\":\"0x42\",\"IntegrationDestination\":\"ServerDecoder\",\"DatabaseMutationAllowed\":false") != std::string::npos,
        "UnknownAuthority is explicitly database blocked");
    check("17_candidate_never_in_verified_spec", spec.find("OneObservation") == std::string::npos,
        "single-observation candidate is not present");
    check("18_recovered_never_in_verified_spec", spec.find("ConflictA") == std::string::npos &&
        spec.find("ConflictB") == std::string::npos, "Recovered contradictory fields are not present");
    check("19_verified_manifest_only_verified", manifest.find("\"EvidenceLevel\":\"Recovered\"") == std::string::npos &&
        manifest.find("\"EvidenceLevel\":\"Candidate\"") == std::string::npos,
        "integration manifest contains Verified facts only");

    const fs::path wrong_causal_root = root / L"wrong-causal";
    fs::create_directories(wrong_causal_root / L"current" / L"raw", error);
    fs::create_directories(wrong_causal_root / L"history" / L"raw", error);
    std::string wrong_causal_events;
    for (int occurrence = 1; occurrence <= 2; ++occurrence) {
        const std::string source = "W52-" + std::to_string(occurrence);
        const std::string token = "VT-W52-" + std::to_string(occurrence);
        wrong_causal_events += field_event(source, "ParserRead", "ServerToClient", "0x52", 16,
            "WrongBuildCausalMustNotPromote", "EphemeralRuntimeState", token);
        wrong_causal_events += handler_event("H-" + source, source, token, "0x52");
        wrong_causal_events += replace_all(
            mutation_event("WRONG-BUILD-M-" + source, token, "0x52", true),
            std::string(64, 'A'), std::string(64, 'B'));
    }
    const fs::path wrong_causal_event_path = wrong_causal_root / L"current" / L"raw" /
        L"semantic-events.jsonl";
    WriteUtf8FileAtomic(wrong_causal_event_path, wrong_causal_events);
    std::string wrong_causal_history = field_event("W52-HISTORY", "ParserRead",
        "ServerToClient", "0x52", 16, "WrongBuildCausalMustNotPromote",
        "EphemeralRuntimeState", "VT-W52-HISTORY");
    wrong_causal_history = replace_all(std::move(wrong_causal_history),
        "\"SessionId\":\"semantic-selftest\"",
        "\"SessionId\":\"wrong-causal-history\"");
    WriteUtf8FileAtomic(wrong_causal_root / L"history" / L"raw" /
        L"semantic-events.jsonl", wrong_causal_history);
    WriteUtf8FileAtomic(wrong_causal_root / L"current" / L"raw" /
        L"capture-records.jsonl", "");
    const auto wrong_causal = AnalyzeSemanticRecovery({"semantic-selftest", hash,
        wrong_causal_root / L"current", wrong_causal_root / L"current" / L"raw" /
            L"capture-records.jsonl", wrong_causal_event_path,
        wrong_causal_root / L"current" / L"semantic", wrong_causal_root});
    const auto wrong_causal_spec = ReadUtf8File(wrong_causal_root / L"current" /
        L"semantic" / L"verified-protocol-spec.json").value_or("");
    const auto wrong_causal_flow = ReadUtf8File(wrong_causal_root / L"current" /
        L"semantic" / L"value-flow.jsonl").value_or("");
    check("wrong-build causal values cannot supplement exact-build fields",
        wrong_causal.success && wrong_causal.status == "EvidenceBlockedBuildMismatch" &&
        wrong_causal_spec.find("\"Opcode\":\"0x52\"") == std::string::npos &&
        wrong_causal_flow.find("ValueTokenToStateMutation") == std::string::npos,
        "wrong ClientBuildId cannot supply Before/Input/After/Delta or a causal edge");

    const auto mismatch = AnalyzeSemanticRecovery({"semantic-selftest",std::string(64,'B'),root / L"current",
        capture_path,event_path,root / L"mismatch",root});
    check("20_build_mismatch_separation", mismatch.success && mismatch.status == "EvidenceBlockedBuildMismatch" &&
        mismatch.verified_fields == 0, "wrong Client SHA cannot share a layout or promote fields");
    const auto old = AnalyzeSemanticRecovery({"old-evidence",hash,root / L"old",capture_path,
        root / L"old" / L"raw" / L"semantic-events.jsonl",root / L"old" / L"semantic",{}});
    const auto old_events = ReadUtf8File(root / L"old" / L"semantic" / L"semantic-events.jsonl").value_or("missing");
    const auto old_spec = ReadUtf8File(root / L"old" / L"semantic" / L"verified-protocol-spec.json").value_or("");
    check("21_old_evidence_reanalysis_valid", old.success && old.status == "SemanticRuntimeEvidenceUnavailable",
        "old ZIP/session without semantic runtime evidence is not a failure");
    check("22_no_fake_events_for_old_evidence", old_events.empty() && old_spec.find("\"Packets\":[]") != std::string::npos,
        "offline reanalysis created no synthetic semantic observations");
    check("23_all_semantic_artifacts_written", fs::is_regular_file(root / L"current" / L"semantic" / L"semantic-probe-plan.json") &&
        fs::is_regular_file(root / L"current" / L"semantic" / L"semantic-summary.json") &&
        fs::is_regular_file(root / L"current" / L"semantic" / L"verified-integration-manifest.json"),
        "probe plan, summary, graph, spec and manifest exist");

    const auto passed = static_cast<std::uint64_t>(std::count_if(checks.begin(), checks.end(),
        [](const Check& value) { return value.passed; }));
    std::ostringstream items;
    items << '[';
    for (std::size_t index = 0; index < checks.size(); ++index) {
        if (index != 0) items << ',';
        items << MakeJsonObject({{"Name",checks[index].name},{"Status",checks[index].passed ? "PASS" : "FAIL"},
            {"Evidence",checks[index].evidence}});
    }
    items << ']';
    const auto report = MakeJsonObject({
        {"SchemaVersion","1"},{"TestedAtUtc",UtcNow()},{"CheckCount",std::to_string(checks.size())},
        {"Passed",std::to_string(passed)},{"Failed",std::to_string(checks.size() - passed)},
        {"Checks",items.str()}
    }, {"SchemaVersion","CheckCount","Passed","Failed","Checks"});
    const bool written = WriteUtf8FileAtomic(report_path, report + "\n");
    fs::remove_all(root, error);
    if (!written) return 74;
    return passed == checks.size() ? 0 : 70;
}

} // namespace god2
