#pragma once

#include <windows.h>

#include <cstdint>

namespace god2 { namespace generic_probe {

constexpr std::uint32_t kPlanMagic = 0x31505047u; // GPP1
constexpr std::uint32_t kPlanVersion = 1u;
constexpr std::uint32_t kMaximumTargets = 3u;
constexpr std::uint32_t kMaximumStackWords = 8u;
constexpr std::uint32_t kMaximumConsumerSteps = 4u;
constexpr std::uint32_t kMaximumFramesPerThread = 8u;
constexpr std::uint32_t kMaximumCandidateIdBytes = 48u;
constexpr std::uint32_t kExpectedBytes = 8u;

enum CaptureMask : std::uint32_t {
    CaptureEcx = 1u << 0,
    CaptureEdx = 1u << 1,
    CaptureBoundedStack = 1u << 2
};

enum FollowMask : std::uint32_t {
    FollowReturn = 1u << 0,
    FollowCaller = 1u << 1,
    FollowConsumer = 1u << 2
};

enum class ObjectSource : std::uint32_t {
    None = 0,
    Ecx = 1,
    Edx = 2,
    Stack0 = 3
};

enum class EvidencePhase : std::uint32_t {
    Entry = 0,
    Return = 1,
    Caller = 2,
    Consumer = 3
};

enum class CommandAction : std::uint32_t {
    Apply = 1,
    Clear = 2,
    Query = 3,
    SelfTest = 4
};

enum class CommandStatus : std::uint32_t {
    NotRun = 0,
    Applied = 1,
    Cleared = 2,
    Queried = 3,
    SelfTestPassed = 4,
    InvalidCommand = 100,
    IdentityNotVerified = 101,
    InvalidPlan = 102,
    TargetBytesMismatch = 103,
    DebugRegisterConflict = 104,
    ThreadConfigurationFailed = 105,
    PlanAlreadyActive = 106,
    PendingEvidence = 107,
    PlatformUnavailable = 108
};

#pragma pack(push, 1)
struct GenericProbeTargetV1 {
    std::uint32_t target_id;
    std::uint32_t function_rva;
    std::uint32_t capture_mask;
    std::uint32_t follow_mask;
    std::uint32_t stack_words;
    std::uint32_t consumer_steps;
    std::uint32_t maximum_observations;
    std::uint32_t domain;
    std::uint32_t priority;
    std::uint32_t object_source;
    std::uint32_t expected_bytes_count;
    unsigned char expected_bytes[kExpectedBytes];
    char candidate_id[kMaximumCandidateIdBytes];
};

struct GenericProbePlanV1 {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t size;
    std::uint32_t generation;
    std::uint32_t target_count;
    std::uint32_t flags;
    std::uint32_t target_process_id;
    std::uint64_t target_process_creation_time;
    GenericProbeTargetV1 targets[kMaximumTargets];
};

struct GenericProbeStatusV1 {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t size;
    std::uint32_t command_status;
    std::uint32_t generation;
    std::uint32_t active;
    std::uint32_t target_count;
    std::uint32_t configured_thread_count;
    std::uint32_t thread_configuration_failures;
    std::uint32_t debug_register_conflicts;
    std::uint32_t attempted;
    std::uint32_t accepted;
    std::uint32_t drained;
    std::uint32_t pending;
    std::uint32_t sequence_gaps;
    std::uint32_t lane_dropped[4];
    std::uint32_t lane_pending[4];
    std::uint32_t active_invocations;
};

struct GenericProbeCommandV1 {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t size;
    std::uint32_t action;
    GenericProbePlanV1 plan;
    GenericProbeStatusV1 status;
};

struct GenericProbeEventV1 {
    std::uint64_t sequence;
    std::uint64_t invocation_id;
    std::uint64_t qpc;
    std::uint64_t packet_id;
    std::uint32_t generation;
    std::uint32_t phase;
    std::uint32_t consumer_step;
    std::uint32_t thread_id;
    std::uint32_t target_id;
    std::uint32_t domain;
    std::uint32_t priority;
    std::uint32_t function_rva;
    std::uint32_t instruction_rva;
    std::uint32_t return_rva;
    std::uint32_t caller_rva;
    std::uint32_t capture_mask;
    std::uint32_t ecx;
    std::uint32_t edx;
    std::uint32_t eax;
    std::uint32_t object_value;
    std::uint32_t stack_count;
    std::uint32_t stack_words[kMaximumStackWords];
    char candidate_id[kMaximumCandidateIdBytes];
};
#pragma pack(pop)

using PacketCorrelationFn = std::uint64_t (*)();

struct RuntimeConfig {
    std::uintptr_t image_base;
    std::uint32_t image_size;
    std::uint32_t process_id;
    std::uint64_t process_creation_time;
    bool exact_identity_verified;
    PacketCorrelationFn packet_correlation;
    HANDLE publish_signal;
};

bool Initialize(const RuntimeConfig& config) noexcept;
CommandStatus Execute(GenericProbeCommandV1* command) noexcept;
bool Drain(GenericProbeEventV1* event) noexcept;
void Shutdown() noexcept;
bool SelfTest() noexcept;

}} // namespace god2::generic_probe

static_assert(sizeof(god2::generic_probe::GenericProbeTargetV1) == 100u,
              "generic target wire ABI changed");
static_assert(sizeof(god2::generic_probe::GenericProbePlanV1) == 336u,
              "generic plan wire ABI changed");
