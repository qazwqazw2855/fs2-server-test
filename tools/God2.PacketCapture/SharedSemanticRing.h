#pragma once

#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <type_traits>

namespace god2 { namespace shared {

constexpr std::uint32_t kSemanticRingMagic = 0x34525347u; // GSR4
constexpr std::uint32_t kSemanticRingVersion = 4;
constexpr std::uint32_t kSemanticDomainCount = 25;
constexpr std::uint32_t kSemanticLaneSlotCount = 64;
constexpr std::uint32_t kSemanticPayloadBytes = 8192;

enum class SemanticPriority : std::uint32_t {
    Authoritative = 0,
    ObjectEvidence = 1,
    SupportingTrace = 2,
    CandidateTelemetry = 3
};

constexpr std::uint32_t kSemanticPriorityCount = 4;

enum class SemanticDropReason : std::uint32_t {
    None = 0,
    RingFull = 1,
    PayloadTooLarge = 2,
    SlotBusy = 3,
    ProducerStopped = 4,
    PriorityBackpressure = 5,
    BatchValidationFailed = 6,
    TransportWriteFailure = 7,
    ProcessSemanticCap = 8,
    LockContention = 9,
    SamplingPolicy = 10,
    ConsumerUnavailable = 11
};

struct SemanticDomainCounters {
    volatile LONG accepted;
    volatile LONG dropped;
    volatile LONG first_dropped_sequence;
    volatile LONG last_dropped_sequence;
    volatile LONG last_drop_reason;
    volatile LONG high_water_mark;
    volatile LONG consumer_lag;
    volatile LONG write_failures;
};

struct SemanticRingSlot {
    // 0 = free, 1 = producer-owned, 2 = ready for the x64 consumer.
    volatile LONG state;
    std::uint32_t sequence;
    std::uint32_t domain;
    std::uint32_t priority;
    std::uint32_t payload_bytes;
    std::uint64_t timestamp_unix_ms;
    char payload[kSemanticPayloadBytes];
};

struct SemanticPriorityLane {
    volatile LONG write_sequence;
    volatile LONG read_sequence;
    volatile LONG dropped;
    volatile LONG sampled;
    volatile LONG first_dropped_sequence;
    volatile LONG last_dropped_sequence;
    volatile LONG last_drop_reason;
    volatile LONG high_water_mark;
    volatile LONG consumer_lag;
};

struct SemanticSharedRing {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t header_bytes;
    std::uint32_t slot_bytes;
    std::uint32_t capacity;
    std::uint32_t domain_count;
    std::uint32_t lane_capacity;
    std::uint32_t lane_count;
    volatile LONG write_sequence;
    volatile LONG read_sequence;
    volatile LONG attempted_sequence;
    volatile LONG dropped_total;
    volatile LONG sampled_total;
    volatile LONG high_water_mark;
    volatile LONG consumer_lag;
    volatile LONG producer_ready;
    volatile LONG producer_closed;
    volatile LONG consumer_ready;
    volatile LONG consumer_closed;
    volatile LONG consumer_failure;
    std::uint32_t producer_process_id;
    std::uint32_t reserved;
    SemanticPriorityLane lanes[kSemanticPriorityCount];
    SemanticDomainCounters domains[kSemanticDomainCount];
    SemanticRingSlot slots[kSemanticPriorityCount][kSemanticLaneSlotCount];
};

static_assert(std::is_standard_layout<SemanticSharedRing>::value, "shared ring must be standard layout");
static_assert(sizeof(SemanticDomainCounters) == 32u,
              "x86/x64 semantic domain counter wire ABI mismatch");
static_assert(sizeof(SemanticRingSlot) == 8224u,
              "x86/x64 semantic slot wire ABI mismatch");
static_assert(sizeof(SemanticPriorityLane) == 36u,
              "x86/x64 semantic priority lane wire ABI mismatch");
static_assert(offsetof(SemanticSharedRing, slots) == 1032u,
              "x86/x64 semantic header wire ABI mismatch");
static_assert(sizeof(SemanticSharedRing) == 2106376u,
              "x86/x64 semantic ring wire ABI mismatch");
static_assert(sizeof(SemanticRingSlot) > kSemanticPayloadBytes);
static_assert(sizeof(SemanticSharedRing) < 3u * 1024u * 1024u);

inline bool HasValidSemanticRingHeader(const SemanticSharedRing& ring) noexcept {
    return ring.magic == kSemanticRingMagic && ring.version == kSemanticRingVersion &&
        ring.header_bytes == offsetof(SemanticSharedRing, slots) &&
        ring.slot_bytes == sizeof(SemanticRingSlot) &&
        ring.capacity == kSemanticPriorityCount * kSemanticLaneSlotCount &&
        ring.domain_count == kSemanticDomainCount &&
        ring.lane_capacity == kSemanticLaneSlotCount &&
        ring.lane_count == kSemanticPriorityCount;
}

inline bool IsValidSemanticPriority(std::uint32_t value) noexcept {
    return value < kSemanticPriorityCount;
}

inline SemanticPriorityLane& SemanticLane(SemanticSharedRing& ring,
                                          std::uint32_t priority) noexcept {
    return ring.lanes[priority];
}

inline SemanticRingSlot& SemanticLaneSlot(SemanticSharedRing& ring,
                                          std::uint32_t priority,
                                          LONG lane_sequence) noexcept {
    return ring.slots[priority][static_cast<std::uint32_t>(lane_sequence) %
        kSemanticLaneSlotCount];
}

}} // namespace god2::shared
