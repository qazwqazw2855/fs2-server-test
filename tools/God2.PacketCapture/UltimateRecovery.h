#pragma once

#include "Core.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace god2 {

// Ultimate recovery uses a deliberately separate authority model.  In
// particular, neither GPU nor ML output can acquire evidence authority.
enum class UltimateAuthority {
    Verified,
    Derived,
    Observed,
    Hypothesis,
    Unknown,
    UnknownServerOnly,
    Rejected
};

const char* ToString(UltimateAuthority value) noexcept;
std::optional<UltimateAuthority> ParseUltimateAuthority(std::string_view value) noexcept;

enum class UltimateSemanticEventType {
    PacketBoundary,
    ParserRead,
    SerializerWrite,
    HandlerInvocation,
    HandlerArgument,
    ObjectAllocated,
    ObjectDestroyed,
    ObjectResolved,
    ObjectLookup,
    RegistryLocated,
    RegistryEnumerated,
    ResourceRead,
    ResourceDecoded,
    ResourceDeserialized,
    StateMutation,
    TaintSeed,
    TaintPropagation,
    ValueFlow,
    FormulaOperand,
    FormulaResult,
    FunctionRoleCandidate,
    UIAnchor,
    SnapshotObject,
    SnapshotEdge,
    ProbeDiagnostic
};

constexpr std::size_t kUltimateSemanticEventTypeCount = 25;
const char* ToString(UltimateSemanticEventType value) noexcept;
std::optional<UltimateSemanticEventType> ParseUltimateSemanticEventType(
    std::string_view value) noexcept;

enum class UltimateSemanticDispatchSink {
    ProtocolRecovery,
    ObjectRecovery,
    RegistryRecovery,
    ResourceRecovery,
    MutationRecovery,
    ValueProvenanceRecovery,
    FormulaRecovery,
    CandidateLedger,
    ContentRecovery,
    SnapshotRecovery,
    DiagnosticLedger
};

enum class UltimateSemanticPromotionPolicy {
    StandardEvidenceGates,
    NoPromotion
};

struct UltimateSemanticEventCapability {
    UltimateSemanticEventType event_type =
        UltimateSemanticEventType::ProbeDiagnostic;
    UltimateSemanticDispatchSink sink =
        UltimateSemanticDispatchSink::DiagnosticLedger;
    UltimateSemanticPromotionPolicy promotion_policy =
        UltimateSemanticPromotionPolicy::NoPromotion;
    bool runtime_producer = false;
    bool fixture_producer = false;
    bool blocked_capability = false;
};

const UltimateSemanticEventCapability* GetUltimateSemanticEventCapability(
    UltimateSemanticEventType value) noexcept;

enum class UltimateProbeDomain {
    NetworkProbe,
    ParserProbe,
    SerializerProbe,
    HandlerProbe,
    ObjectProbe,
    AllocationProbe,
    VTableProbe,
    FactoryProbe,
    ManagerLookupProbe,
    RegistryProbe,
    ResourceDecodeProbe,
    MutationProbe,
    TaintSeedProbe,
    FormulaOperandProbe,
    QuestProbe,
    MapProbe,
    PortalProbe,
    NpcProbe,
    MonsterProbe,
    BattleProbe,
    InventoryProbe,
    ItemProbe,
    SkillProbe,
    PetMountProbe,
    SnapshotProbe
};

constexpr std::size_t kUltimateProbeDomainCount = 25;
const char* ToString(UltimateProbeDomain value) noexcept;

struct UltimateDeepRuntimeProducerDefinition {
    UltimateProbeDomain domain = UltimateProbeDomain::ObjectProbe;
    const char* producer_name = "";
    UltimateSemanticEventType primary_event_type =
        UltimateSemanticEventType::ProbeDiagnostic;
    const char* evidence_contract = "";
};

struct UltimateGameplayAdapterDefinition {
    UltimateProbeDomain domain = UltimateProbeDomain::QuestProbe;
    const char* adapter_name = "";
    const char* required_fundamental_evidence = "";
    bool preserves_base_runtime_observed_layers = true;
    bool preserves_unknown_server_authority = true;
};

constexpr std::size_t kUltimateFundamentalProducerCount = 11;
constexpr std::size_t kUltimateGameplayAdapterCount = 10;

const std::array<UltimateDeepRuntimeProducerDefinition,
                 kUltimateFundamentalProducerCount>&
GetUltimateDeepRuntimeProducerInventory() noexcept;
const std::array<UltimateGameplayAdapterDefinition,
                 kUltimateGameplayAdapterCount>&
GetUltimateGameplayAdapterInventory() noexcept;

struct UltimateDeepPromotionGates {
    bool exact_target_identity = false;
    bool executable_section = false;
    bool exact_candidate_bytes = false;
    bool calling_convention_verified = false;
    bool typed_runtime_evidence = false;
    bool repeated_causal_observation = false;
    bool contradictions_resolved = false;
    bool stable_object_or_context = false;
    bool verified_consumer_or_mutation = false;
    bool sensitive_mask_contract = false;
    bool argument_contract_verified = false;
    bool return_value_lifetime_verified = false;
    bool thread_context_verified = false;
    bool reentrancy_risk_verified = false;

    bool AllPassed() const noexcept;
};

enum class UltimateEventPriority : std::uint8_t {
    Authoritative = 0,
    ObjectEvidence = 1,
    SupportingTrace = 2,
    CandidateTelemetry = 3
};

struct God2SemanticEventV2 {
    std::uint32_t schema_version = 2;
    UltimateSemanticEventType event_type = UltimateSemanticEventType::ProbeDiagnostic;
    std::string event_id;
    std::uint64_t sequence = 0;
    std::string timestamp;
    std::uint32_t thread_id = 0;
    std::uint32_t process_id = 0;
    std::string session_id;
    std::string client_build_id;
    std::string module_id;
    std::uint64_t rva = 0;
    std::uint64_t callsite_rva = 0;
    // Preserves exact-build expressions used by transitional probe records
    // such as two verified callsites joined by "-or-".  Numeric fields remain
    // zero when the expression is not a single RVA.
    std::string rva_expression;
    std::string callsite_rva_expression;
    std::string parent_event_id;
    std::string context_id;
    std::string action_id;
    std::string object_token;
    std::string value_token;
    std::string source_token;
    UltimateAuthority authority_hint = UltimateAuthority::Unknown;
    std::string sensitive_mask_status = "NotSensitive";
    // Documented companion metadata emitted by the exact-build probe.  These
    // values are captured separately from Payload so promotion gates never
    // depend on caller-controlled magic booleans inside recovered content.
    std::map<std::string, std::string> evidence_binding;
    // Payload stores one of the closed UTF-8 JSON object profiles declared by
    // the canonical v2 contract.  The writer transports it as an object (not a
    // JSON-encoded string); unclassified input is replaced by fixed
    // EvidenceBlocked metadata and is never copied through as opaque content.
    std::string payload = "{}";
};

struct UltimateDeepRuntimeProducerInput {
    UltimateProbeDomain domain = UltimateProbeDomain::ObjectProbe;
    UltimateSemanticEventType event_type =
        UltimateSemanticEventType::ProbeDiagnostic;
    UltimateDeepPromotionGates gates;
    std::string session_id;
    std::string client_build_id;
    std::string candidate_id;
    std::string event_id;
    std::string timestamp;
    std::string source_token;
    std::string object_token;
    std::string value_token;
    std::string payload = "{}";
    std::uint64_t sequence = 0;
    std::uint64_t rva = 0;
    std::uint64_t callsite_rva = 0;
    std::uint32_t process_id = 0;
    std::uint32_t thread_id = 0;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    bool current_official_live = false;
    bool historical_official_live = false;
    bool fixture_only = false;
    bool sensitive_value_persisted = false;
};

struct UltimateDeepRuntimeProducerResult {
    bool producer_implemented = true;
    bool activation_allowed = false;
    bool event_produced = false;
    std::string status = "EVIDENCE_BLOCKED_CONTRACT_GATES_INCOMPLETE";
    std::optional<God2SemanticEventV2> event;
};

UltimateDeepRuntimeProducerResult ProduceUltimateDeepRuntimeEvent(
    const UltimateDeepRuntimeProducerInput& input) noexcept;

struct UltimateSemanticDispatchDecision {
    bool dispatchable = false;
    UltimateSemanticDispatchSink sink =
        UltimateSemanticDispatchSink::DiagnosticLedger;
    bool promotion_allowed = false;
};

UltimateSemanticDispatchDecision DispatchSemanticEvent(
    const God2SemanticEventV2& event) noexcept;

struct SemanticEventReadResult {
    bool success = false;
    bool read_from_v1 = false;
    God2SemanticEventV2 event;
    UltimateSemanticDispatchDecision dispatch;
    std::string error;
};

enum class SemanticEventWireProfile : std::uint8_t {
    Invalid = 0,
    CanonicalV1WriterProfile,
    NonCanonicalV1CompatibilityInput,
    CanonicalV2Envelope
};

const char* ToString(SemanticEventWireProfile value) noexcept;
SemanticEventWireProfile ClassifySemanticEventWire(std::string_view wire);
bool CanEvaluateSemanticPromotion(SemanticEventWireProfile profile) noexcept;

std::string SerializeSemanticEventV2(const God2SemanticEventV2& event);
SemanticEventReadResult ReadSemanticEvent(std::string_view wire);
bool EquivalentSemanticEvent(const God2SemanticEventV2& left,
                             const God2SemanticEventV2& right) noexcept;
bool ValidateCanonicalSemanticPayload(const God2SemanticEventV2& event,
                                      std::string* profile_name = nullptr,
                                      std::string* error = nullptr);

struct UltimateRingRecord {
    UltimateProbeDomain domain = UltimateProbeDomain::NetworkProbe;
    UltimateEventPriority priority = UltimateEventPriority::CandidateTelemetry;
    God2SemanticEventV2 event;
};

struct UltimateDomainBackpressure {
    std::uint64_t published = 0;
    std::uint64_t consumed = 0;
    std::uint64_t dropped = 0;
    std::uint64_t write_failures = 0;
    std::uint64_t first_dropped_sequence = 0;
    std::uint64_t last_dropped_sequence = 0;
    std::uint64_t consumer_lag = 0;
    std::uint64_t queue_high_water = 0;
    bool semantic_evidence_incomplete = false;
    bool disabled = false;
    std::string last_drop_reason;
    std::string diagnostic;
};

// Fixed-capacity, sequence-preserving ring.  Priority is used only for
// admission/eviction; draining remains globally ordered by sequence so a
// consumer never observes a fabricated causal order.
class UltimatePriorityRing final {
public:
    explicit UltimatePriorityRing(std::size_t capacity);
    ~UltimatePriorityRing();
    UltimatePriorityRing(const UltimatePriorityRing&) = delete;
    UltimatePriorityRing& operator=(const UltimatePriorityRing&) = delete;

    bool Publish(UltimateProbeDomain domain, UltimateEventPriority priority,
                 God2SemanticEventV2 event, std::string* reason = nullptr) noexcept;
    std::vector<UltimateRingRecord> DrainBatch(std::size_t maximum_records) noexcept;
    void RecordWriteFailure(UltimateProbeDomain domain,
                            std::string_view reason) noexcept;
    bool ExecuteIsolated(UltimateProbeDomain domain,
                         const std::function<void()>& operation) noexcept;
    UltimateDomainBackpressure DomainStatus(UltimateProbeDomain domain) const noexcept;
    std::array<UltimateDomainBackpressure, kUltimateProbeDomainCount> AllDomainStatus() const noexcept;
    std::size_t Size() const noexcept;
    std::size_t Capacity() const noexcept;
    bool SemanticEvidenceIncomplete() const noexcept;
    void Stop() noexcept;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

struct UltimateEvidenceRef {
    std::string evidence_id;
    std::string event_id;
    std::string session_id;
    std::string client_build_id;
    std::string module_id;
    std::uint64_t rva = 0;
    std::string rva_expression;
    std::string callsite_rva_expression;
    std::uint64_t resource_offset = 0;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    UltimateAuthority claimed_authority_hint = UltimateAuthority::Unknown;
};

struct UltimatePromotionGate {
    bool exact_build_binding = false;
    bool typed_source = false;
    bool stable_object_or_context = false;
    std::uint64_t repeated_observations = 0;
    std::uint64_t contradictions = 0;
    bool causal_path = false;
    bool verified_consumer_or_mutation = false;
    bool cross_session_required = false;
    bool cross_session_consistent = false;
    bool replay_required = false;
    bool replay_consistent = false;
};

bool CanPromoteVerified(const UltimatePromotionGate& gate) noexcept;

// A discovery hit is never a hook authorization.  The x86 probe may emit a
// bounded executable-code candidate, but this contract keeps the domain
// EvidenceBlocked until identity, ABI and repeated typed causal evidence all
// independently pass.
struct UltimateDeepProbeCandidate {
    UltimateProbeDomain domain = UltimateProbeDomain::ObjectProbe;
    std::string module_section;
    std::uint64_t callsite_rva = 0;
    std::uint64_t target_rva = 0;
    std::string signature;
    std::string signature_mask;
    std::string discovery_provenance;
    std::string calling_convention_state = "UNKNOWN";
    std::string risk = "High";
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateDeepProbeVerification {
    bool exact_target_identity = false;
    bool executable_section = false;
    bool exact_candidate_bytes = false;
    bool calling_convention_verified = false;
    bool typed_runtime_evidence = false;
    std::uint64_t repeated_causal_observations = 0;
    std::uint64_t contradictions = 0;
    bool stable_object_or_context = false;
    bool verified_consumer_or_mutation = false;
    bool sensitive_mask_contract = false;
    bool argument_contract_verified = false;
    bool return_value_lifetime_verified = false;
    bool thread_context_verified = false;
    bool reentrancy_risk_verified = false;
};

bool CanActivateDeepProbeCandidate(
    const UltimateDeepProbeCandidate& candidate,
    const UltimateDeepProbeVerification& verification) noexcept;

struct UltimateObjectRecord {
    std::string canonical_identity;
    std::string family;
    std::string stable_template_id;
    std::string runtime_instance_id;
    std::string object_token;
    std::uint64_t allocation_size = 0;
    std::string vtable;
    std::string constructor_path;
    std::string destructor_path;
    std::string factory;
    std::string manager_owner;
    std::string context;
    std::map<std::string, std::string> properties;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::string status = "EvidenceBlockedObjectIdentityUnavailable";
};

struct UltimateRegistryRecord {
    std::string canonical_identity;
    std::string family;
    std::uint64_t record_count = 0;
    bool read_only_enumeration = true;
    std::string lookup_consumer;
    std::string destination;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateResourceRecord {
    std::string relative_path;
    std::string file_sha256;
    std::uint64_t source_offset = 0;
    std::string decoded_buffer_sha256;
    std::uint64_t decode_rva = 0;
    std::uint64_t deserialize_rva = 0;
    std::string destination;
    std::uint64_t record_count = 0;
    std::string schema_candidate;
    std::string provenance;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateMutationRecord {
    std::string object_identity;
    std::string property_candidate;
    std::string value_type;
    std::string before_value;
    std::string input_value;
    std::string after_value;
    std::string trigger_packet;
    std::string trigger_handler;
    std::string trigger_action;
    std::uint64_t writer_rva = 0;
    std::vector<std::string> consumers;
    std::uint64_t observation_count = 0;
    std::uint64_t contradictions = 0;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::string status = "EvidenceBlockedMutationCausalityUnavailable";
};

struct UltimateValueFlowEdge {
    std::string edge_kind;
    std::string source_token;
    std::string target_token;
    std::string operation;
    std::uint32_t propagation_hops = 0;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateProtocolField {
    std::uint64_t packet_offset = 0;
    std::uint32_t width = 0;
    bool signed_value = false;
    std::string value_type = "unknown";
    std::string endian = "unknown";
    std::string encoding;
    std::string length_relationship;
    bool optional_or_conditional = false;
    bool repeated_or_list = false;
    std::string enum_candidate;
    std::string semantic_candidate;
    std::uint64_t parser_rva = 0;
    std::uint64_t serializer_rva = 0;
    std::string handler_context;
    std::string object_endpoint;
    std::uint64_t observation_count = 0;
    std::uint64_t contradictions = 0;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimatePacketSchema {
    std::string opcode;
    std::string direction = "Unknown";
    std::string action_family = "Unknown";
    std::string subsystem = "Unknown";
    std::string frame_length_rule = "Unknown";
    std::string checksum_rule = "Unknown";
    std::string encryption_boundary = "Unknown";
    std::uint64_t minimum_length = 0;
    std::uint64_t maximum_length = 0;
    std::uint64_t parser_rva = 0;
    std::uint64_t serializer_rva = 0;
    std::uint64_t handler_rva = 0;
    std::vector<UltimateProtocolField> typed_fields;
    std::vector<std::string> enum_mappings;
    std::vector<std::string> conditions;
    std::vector<std::string> request_response_links;
    std::string required_protocol_state;
    std::vector<std::string> state_mutation_links;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::uint64_t observation_count = 0;
    std::uint64_t contradictions = 0;
};

struct UltimateContentEntity {
    std::string canonical_identity;
    std::string family;
    std::string exact_source;
    std::string client_build_id;
    std::map<std::string, std::string> raw_values;
    std::map<std::string, std::string> normalized_values;
    std::map<std::string, std::string> base_values;
    std::map<std::string, std::string> runtime_values;
    std::map<std::string, std::string> current_values;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::uint64_t observation_count = 0;
    std::uint64_t contradictions = 0;
    std::string status = "EvidenceBlockedCanonicalIdentityUnavailable";
};

struct UltimateFormulaRecord {
    std::string formula_id;
    std::string category;
    std::string expression;
    std::vector<std::string> operands;
    std::string result;
    std::uint64_t observation_count = 0;
    std::uint64_t contradictions = 0;
    bool replay_consistent = false;
    bool exact_recovered = false;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::string status = "CandidateModel";
};

struct UltimateFsmTransition {
    std::string from_state;
    std::string to_state;
    std::string guard;
    std::string input;
    std::string output;
    std::string mutation;
    std::uint64_t observation_count = 0;
    bool replay_consistent = false;
    std::vector<UltimateEvidenceRef> evidence;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateFsmRecord {
    std::string machine_id;
    std::vector<std::string> states;
    std::vector<UltimateFsmTransition> transitions;
    std::vector<std::string> invalid_transitions;
    std::string recovery_or_rollback;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateGraphNode {
    std::string id;
    std::string kind;
    std::map<std::string, std::string> attributes;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateGraphEdge {
    std::string from;
    std::string to;
    std::string relation;
    std::vector<UltimateEvidenceRef> evidence;
    std::string client_build_id;
    std::uint64_t rva_or_resource_offset = 0;
    std::uint64_t observation_count = 0;
    std::uint64_t contradictions = 0;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::string status;
};

struct UltimateCoverageMetric {
    std::string domain;
    std::uint64_t total_recovered = 0;
    std::uint64_t recovered = 0;
    std::uint64_t verified_recovered = 0;
    std::uint64_t derived_recovered = 0;
    std::uint64_t observed_recovered = 0;
    std::uint64_t hypothesis_recovered = 0;
    std::uint64_t unknown_recovered = 0;
    std::uint64_t unknown_server_only_recovered = 0;
    std::uint64_t rejected_recovered = 0;
    std::optional<std::uint64_t> denominator;
    std::optional<double> percent;
    std::string denominator_basis;
    UltimateAuthority denominator_authority = UltimateAuthority::Unknown;
    std::string status = "EvidenceBlockedDenominatorUnavailable";
};

struct UltimateMissingEvidence {
    std::string kind;
    std::string identity;
    std::string evidence_need;
    std::string safe_next_action;
    std::uint32_t priority = 0;
    UltimateAuthority authority = UltimateAuthority::Unknown;
};

struct UltimateMlSample {
    std::string id;
    std::vector<double> features;
    std::optional<std::string> verified_label;
};

struct UltimateMlSequence {
    std::string id;
    std::vector<std::string> symbols;
};

struct UltimateLocalModelArtifact {
    fs::path path;
    std::string name;
    std::string version;
    std::string license;
    std::string sha256;
};

struct UltimateMlPrediction {
    std::string sample_id;
    std::vector<double> embedding;
    std::size_t cluster = 0;
    std::string nearest_sample_id;
    double similarity = 0.0;
    std::string propagated_label;
    double propagated_label_score = 0.0;
    double outlier_score = 0.0;
    UltimateAuthority authority = UltimateAuthority::Hypothesis;
};

struct UltimateSequenceRanking {
    std::string sequence_id;
    double novelty_score = 0.0;
    UltimateAuthority authority = UltimateAuthority::Hypothesis;
};

struct UltimateMlResult {
    std::string model_status = "EvidenceBlockedModelUnavailable";
    std::string model_name;
    std::string model_version;
    std::string model_license;
    std::string model_sha256;
    std::vector<UltimateMlPrediction> predictions;
    std::vector<UltimateSequenceRanking> sequence_rankings;
    std::vector<std::string> diagnostics;
};

class UltimateDeterministicMl final {
public:
    static UltimateMlResult Analyze(
        const std::vector<UltimateMlSample>& samples,
        const std::vector<UltimateMlSequence>& sequences,
        const std::optional<UltimateLocalModelArtifact>& local_model = std::nullopt);
};

struct UltimateRecoveryInput {
    std::string session_id;
    std::vector<God2SemanticEventV2> semantic_events;
    std::map<std::string, std::uint64_t> declared_denominators;
    // A denominator is usable only when its catalog provenance is VERIFIED.
    std::map<std::string, UltimateAuthority> declared_denominator_authorities;
    std::vector<UltimateMlSample> ml_samples;
    std::vector<UltimateMlSequence> ml_sequences;
    std::optional<UltimateLocalModelArtifact> local_model;
    bool semantic_evidence_incomplete = false;
    bool interrupted = false;
};

struct UltimateRecoveryResult {
    bool success = true;
    std::string schema_version = "god2-ultimate-recovery-v1";
    std::string session_id;
    std::string client_build_id;
    bool exact_client_build_attested = false;
    std::string client_build_attestation_status =
        "EvidenceBlockedRecoveryBuildAttestationUnavailable";
    std::string status = "EvidenceBlockedNoSemanticEvidence";
    bool semantic_evidence_incomplete = false;
    bool partial_recovery = false;
    std::vector<UltimateObjectRecord> objects;
    std::vector<UltimateRegistryRecord> registries;
    std::vector<UltimateResourceRecord> resources;
    std::vector<UltimateMutationRecord> mutations;
    std::vector<UltimateValueFlowEdge> value_flow;
    std::vector<UltimatePacketSchema> packet_schemas;
    std::vector<UltimateContentEntity> content_entities;
    std::vector<UltimateFormulaRecord> formulas;
    std::vector<UltimateFsmRecord> state_machines;
    std::vector<UltimateGraphNode> graph_nodes;
    std::vector<UltimateGraphEdge> graph_edges;
    std::vector<UltimateCoverageMetric> coverage;
    std::vector<UltimateMissingEvidence> missing_evidence;
    UltimateMlResult ml;
    std::vector<std::string> diagnostics;
};

class UltimateRecoveryEngine final {
public:
    static UltimateRecoveryResult Recover(const UltimateRecoveryInput& input) noexcept;
};

struct UltimateBundleOptions {
    fs::path output_directory;
    std::string package_id;
    // When present, the writer computes SHA-256, PE architecture and version
    // from this exact executable before adding manifest.clientBuild.  A caller
    // cannot assert identity by merely supplying the expected hash string.
    fs::path client_executable_path;
    bool create_zip = true;
};

struct UltimateBundleFile {
    std::string relative_path;
    std::uint64_t size_bytes = 0;
    std::string sha256;
    std::string provenance;
    UltimateAuthority authority = UltimateAuthority::Unknown;
    std::string schema_version;
};

struct UltimateBundleResult {
    bool success = false;
    fs::path staging_directory;
    fs::path manifest_path;
    fs::path zip_path;
    std::string zip_sha256;
    std::string client_identity_status = "EvidenceBlockedClientIdentityNotComputed";
    std::string actual_client_sha256;
    std::string actual_client_version;
    std::string actual_client_architecture;
    std::vector<UltimateBundleFile> files;
    std::vector<std::string> blockers;
    std::string error;
};

UltimateBundleResult WriteUltimateRecoveryBundle(const UltimateRecoveryResult& recovery,
                                                  const UltimateBundleOptions& options) noexcept;

// Runs only deterministic contract fixtures.  The report explicitly labels
// them as synthetic self-test evidence and never represents them as live
// Client observations.  A successful run contains at least 35 PASS results.
int RunUltimateRecoverySelfTests(const fs::path& report_path,
                                const fs::path& artifacts_path,
                                std::optional<fs::path> native_semantic_wire_path =
                                    std::nullopt);

// Consumes the exact JSONL bytes emitted by the x86 probe's 25-type producer
// fixture through the production v2 reader and dispatch table.  The versioned
// report binds the input SHA-256 and contains one measured row per event type.
int VerifyNativeSemanticWireFixture(const fs::path& input_path,
                                    const fs::path& report_path);

} // namespace god2
