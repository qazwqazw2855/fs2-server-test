#include "GenericProbePlan.h"

#include <tlhelp32.h>

#include <cctype>
#include <cstring>

namespace god2 { namespace generic_probe {
namespace {

static_assert(sizeof(void*) == 4, "Generic Probe Plan requires the x86 client ABI");

constexpr LONG kLaneCapacity = 2048;
constexpr DWORD kThreadStateCapacity = 256;
constexpr DWORD kConfiguredThreadCapacity = 1024;
constexpr DWORD kManagerPollMilliseconds = 100;
constexpr DWORD kMaximumObservationsPerTarget = 1'000'000;
constexpr DWORD kTrapFlag = 0x100u;
constexpr DWORD kResumeFlag = 0x10000u;

struct ProbeFrame {
    std::uint32_t target_index{};
    std::uint32_t return_address{};
    std::uint32_t object_value{};
    std::uint64_t invocation_id{};
    std::uint64_t packet_id{};
};

struct ThreadState {
    volatile LONG thread_id{};
    volatile LONG busy{};
    DWORD depth{};
    DWORD consumer_remaining{};
    DWORD consumer_step{};
    ProbeFrame frames[kMaximumFramesPerThread]{};
    ProbeFrame consumer{};
};

struct QueueSlot {
    volatile LONG turn{};
    GenericProbeEventV1 event{};
};

struct LaneQueue {
    QueueSlot slots[kLaneCapacity]{};
    volatile LONG write_position{};
    volatile LONG read_position{};
    volatile LONG pending{};
    volatile LONG dropped{};
};

RuntimeConfig g_config{};
GenericProbePlanV1 g_plan{};
LaneQueue g_lanes[4]{};
ThreadState g_thread_states[kThreadStateCapacity]{};
DWORD g_configured_threads[kConfiguredThreadCapacity]{};
CRITICAL_SECTION g_command_lock{};
volatile LONG g_initialized{};
volatile LONG g_active{};
volatile LONG g_handler_rundown{};
volatile LONG g_sequence{};
volatile LONG g_invocation_sequence{};
volatile LONG g_attempted{};
volatile LONG g_accepted{};
volatile LONG g_drained{};
volatile LONG g_sequence_gaps{};
volatile LONG g_last_drained_sequence{};
volatile LONG g_active_invocations{};
volatile LONG g_configured_thread_count{};
volatile LONG g_thread_configuration_failures{};
volatile LONG g_debug_register_conflicts{};
volatile LONG g_target_observations[kMaximumTargets]{};
PVOID g_veh{};
HANDLE g_manager_stop{};
HANDLE g_manager_thread{};
DWORD g_manager_thread_id{};

std::uint64_t qpcNow() noexcept
{
    LARGE_INTEGER value{};
    QueryPerformanceCounter(&value);
    return static_cast<std::uint64_t>(value.QuadPart);
}

bool inImage(std::uint32_t address) noexcept
{
    if (address < g_config.image_base) return false;
    const std::uintptr_t offset = address - g_config.image_base;
    return offset < g_config.image_size;
}

std::uint32_t imageRva(std::uint32_t address) noexcept
{
    return inImage(address) ?
        static_cast<std::uint32_t>(address - g_config.image_base) : 0u;
}

bool readableDword(std::uint32_t address, std::uint32_t* value) noexcept
{
    if (value == nullptr || address == 0) return false;
    __try {
        *value = *reinterpret_cast<const std::uint32_t*>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *value = 0;
        return false;
    }
}

bool executableAddress(std::uint32_t address) noexcept
{
    MEMORY_BASIC_INFORMATION memory{};
    if (address == 0 || VirtualQuery(reinterpret_cast<const void*>(address),
            &memory, sizeof(memory)) != sizeof(memory) ||
        memory.State != MEM_COMMIT || (memory.Protect & PAGE_GUARD) != 0 ||
        (memory.Protect & PAGE_NOACCESS) != 0) return false;
    const DWORD protection = memory.Protect & 0xFFu;
    return protection == PAGE_EXECUTE || protection == PAGE_EXECUTE_READ ||
        protection == PAGE_EXECUTE_READWRITE ||
        protection == PAGE_EXECUTE_WRITECOPY;
}

bool executableImageRva(std::uint32_t rva) noexcept
{
    if (rva >= g_config.image_size ||
        rva > MAXDWORD - static_cast<DWORD>(g_config.image_base)) return false;
    return executableAddress(static_cast<DWORD>(g_config.image_base) + rva);
}

bool safeCandidateId(const char value[kMaximumCandidateIdBytes]) noexcept
{
    if (value == nullptr || value[0] == '\0' ||
        std::memchr(value, '\0', kMaximumCandidateIdBytes) == nullptr) return false;
    for (DWORD index = 0; index < kMaximumCandidateIdBytes && value[index] != '\0';
         ++index) {
        const unsigned char ch = static_cast<unsigned char>(value[index]);
        if (!std::isalnum(ch) && ch != '_' && ch != '-' && ch != '.' &&
            ch != ':') return false;
    }
    return true;
}

bool validateTarget(const GenericProbeTargetV1& target) noexcept
{
    constexpr DWORD captureAllowed = CaptureEcx | CaptureEdx | CaptureBoundedStack;
    constexpr DWORD followAllowed = FollowReturn | FollowCaller | FollowConsumer;
    const DWORD expectedPriority = target.domain == 11u ? 0u :
        (target.domain >= 4u && target.domain <= 10u ? 1u :
        (target.domain >= 12u && target.domain <= 13u ? 2u : 3u));
    if (target.target_id == 0 || target.capture_mask == 0 ||
        (target.capture_mask & ~captureAllowed) != 0 ||
        (target.follow_mask & ~followAllowed) != 0 ||
        target.stack_words > kMaximumStackWords ||
        ((target.capture_mask & CaptureBoundedStack) == 0 &&
         target.stack_words != 0) ||
        target.consumer_steps > kMaximumConsumerSteps ||
        (((target.follow_mask & FollowConsumer) != 0) !=
         (target.consumer_steps != 0)) ||
        ((target.follow_mask & (FollowCaller | FollowConsumer)) != 0 &&
         (target.follow_mask & FollowReturn) == 0) ||
        target.maximum_observations == 0 ||
        target.maximum_observations > kMaximumObservationsPerTarget ||
        target.domain < 4u || target.domain > 24u ||
        target.priority != expectedPriority ||
        target.object_source > static_cast<DWORD>(ObjectSource::Stack0) ||
        (target.object_source == static_cast<DWORD>(ObjectSource::Stack0) &&
         ((target.capture_mask & CaptureBoundedStack) == 0 ||
          target.stack_words == 0)) ||
        target.expected_bytes_count != kExpectedBytes ||
        !safeCandidateId(target.candidate_id) ||
        !executableImageRva(target.function_rva)) return false;
    __try {
        return std::memcmp(reinterpret_cast<const void*>(
            g_config.image_base + target.function_rva), target.expected_bytes,
            kExpectedBytes) == 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

CommandStatus validatePlan(const GenericProbePlanV1& plan) noexcept
{
    if (!g_config.exact_identity_verified)
        return CommandStatus::IdentityNotVerified;
    if (plan.magic != kPlanMagic || plan.version != kPlanVersion ||
        plan.size != sizeof(plan) || plan.generation == 0 ||
        plan.target_count == 0 || plan.target_count > kMaximumTargets ||
        plan.flags != 0 || plan.target_process_id != g_config.process_id ||
        plan.target_process_creation_time != g_config.process_creation_time)
        return CommandStatus::InvalidPlan;
    for (DWORD index = 0; index < plan.target_count; ++index) {
        const auto& target = plan.targets[index];
        if (!validateTarget(target)) {
            if (target.expected_bytes_count == kExpectedBytes &&
                executableImageRva(target.function_rva))
                return CommandStatus::TargetBytesMismatch;
            return CommandStatus::InvalidPlan;
        }
        for (DWORD prior = 0; prior < index; ++prior) {
            if (target.target_id == plan.targets[prior].target_id ||
                target.function_rva == plan.targets[prior].function_rva)
                return CommandStatus::InvalidPlan;
        }
    }
    return CommandStatus::Applied;
}

ThreadState* stateForThread(DWORD threadId, bool create) noexcept
{
    DWORD start = threadId % kThreadStateCapacity;
    for (DWORD offset = 0; offset < kThreadStateCapacity; ++offset) {
        auto& state = g_thread_states[(start + offset) % kThreadStateCapacity];
        const LONG owner = InterlockedCompareExchange(&state.thread_id, 0, 0);
        if (owner == static_cast<LONG>(threadId)) return &state;
        if (create && owner == 0 &&
            InterlockedCompareExchange(&state.thread_id,
                static_cast<LONG>(threadId), 0) == 0) return &state;
    }
    return nullptr;
}

bool reserveObservation(DWORD targetIndex) noexcept
{
    volatile LONG* count = &g_target_observations[targetIndex];
    const LONG maximum = static_cast<LONG>(
        g_plan.targets[targetIndex].maximum_observations);
    LONG observed = InterlockedCompareExchange(count, 0, 0);
    while (observed < maximum) {
        const LONG previous = InterlockedCompareExchange(count, observed + 1,
                                                         observed);
        if (previous == observed) return true;
        observed = previous;
    }
    return false;
}

bool reserveLane(LaneQueue& lane) noexcept
{
    LONG pending = InterlockedCompareExchange(&lane.pending, 0, 0);
    while (pending < kLaneCapacity) {
        const LONG previous = InterlockedCompareExchange(
            &lane.pending, pending + 1, pending);
        if (previous == pending) return true;
        pending = previous;
    }
    return false;
}

bool enqueueEvent(GenericProbeEventV1& event) noexcept
{
    InterlockedIncrement(&g_attempted);
    event.sequence = static_cast<std::uint32_t>(InterlockedIncrement(&g_sequence));
    auto& lane = g_lanes[event.priority];
    if (!reserveLane(lane)) {
        InterlockedIncrement(&lane.dropped);
        InterlockedIncrement(&g_sequence_gaps);
        return false;
    }
    const LONG position = InterlockedIncrement(&lane.write_position) - 1;
    auto& slot = lane.slots[static_cast<DWORD>(position) % kLaneCapacity];
    while (InterlockedCompareExchange(&slot.turn, 0, 0) != position)
        YieldProcessor();
    slot.event = event;
    MemoryBarrier();
    InterlockedExchange(&slot.turn, position + 1);
    InterlockedIncrement(&g_accepted);
    if (g_config.publish_signal != nullptr) SetEvent(g_config.publish_signal);
    return true;
}

void captureStack(const CONTEXT& context, DWORD requested,
                  GenericProbeEventV1* event, bool skipReturnAddress) noexcept
{
    if (event == nullptr || requested == 0) return;
    const DWORD skip = skipReturnAddress ? sizeof(DWORD) : 0u;
    for (DWORD index = 0; index < requested && index < kMaximumStackWords;
         ++index) {
        std::uint32_t value{};
        if (!readableDword(context.Esp + skip + index * sizeof(DWORD), &value))
            break;
        event->stack_words[event->stack_count++] = value;
    }
}

void fillEvent(const CONTEXT& context, const ProbeFrame& frame,
               EvidencePhase phase, DWORD consumerStep,
               GenericProbeEventV1* event) noexcept
{
    const auto& target = g_plan.targets[frame.target_index];
    *event = GenericProbeEventV1{};
    event->invocation_id = frame.invocation_id;
    event->qpc = qpcNow();
    event->packet_id = frame.packet_id;
    event->generation = g_plan.generation;
    event->phase = static_cast<DWORD>(phase);
    event->consumer_step = consumerStep;
    event->thread_id = GetCurrentThreadId();
    event->target_id = target.target_id;
    event->domain = target.domain;
    event->priority = target.priority;
    event->function_rva = target.function_rva;
    event->instruction_rva = imageRva(context.Eip);
    event->return_rva = imageRva(frame.return_address);
    event->caller_rva = event->return_rva >= 5u ? event->return_rva - 5u : 0u;
    event->capture_mask = target.capture_mask;
    event->ecx = (target.capture_mask & CaptureEcx) != 0 ? context.Ecx : 0u;
    event->edx = (target.capture_mask & CaptureEdx) != 0 ? context.Edx : 0u;
    event->eax = context.Eax;
    event->object_value = frame.object_value;
    if ((target.capture_mask & CaptureBoundedStack) != 0)
        captureStack(context, target.stack_words, event,
                     phase == EvidencePhase::Entry);
    std::memcpy(event->candidate_id, target.candidate_id,
                kMaximumCandidateIdBytes);
}

DWORD objectValue(const GenericProbeTargetV1& target, const CONTEXT& context,
                  const GenericProbeEventV1& event) noexcept
{
    switch (static_cast<ObjectSource>(target.object_source)) {
    case ObjectSource::Ecx: return context.Ecx;
    case ObjectSource::Edx: return context.Edx;
    case ObjectSource::Stack0:
        return event.stack_count != 0 ? event.stack_words[0] : 0u;
    default: return 0u;
    }
}

void setReturnBreakpoint(CONTEXT* context, std::uint32_t address) noexcept
{
    context->Dr3 = address;
    context->Dr7 &= ~(3u << 6);
    context->Dr7 &= ~(0xFu << 28);
    if (address != 0) context->Dr7 |= 1u << 6;
}

LONG CALLBACK vectoredHandler(EXCEPTION_POINTERS* pointers) noexcept
{
    if (pointers == nullptr || pointers->ExceptionRecord == nullptr ||
        pointers->ContextRecord == nullptr ||
        pointers->ExceptionRecord->ExceptionCode != EXCEPTION_SINGLE_STEP ||
        InterlockedCompareExchange(&g_active, 0, 0) == 0)
        return EXCEPTION_CONTINUE_SEARCH;
    InterlockedIncrement(&g_handler_rundown);
    if (InterlockedCompareExchange(&g_active, 0, 0) == 0) {
        InterlockedDecrement(&g_handler_rundown);
        return EXCEPTION_CONTINUE_SEARCH;
    }
    CONTEXT* context = pointers->ContextRecord;
    const DWORD dr6 = context->Dr6;
    bool handled = false;
    auto* state = stateForThread(GetCurrentThreadId(), true);
    if (state != nullptr && InterlockedCompareExchange(&state->busy, 1, 0) == 0) {
        for (DWORD targetIndex = 0; targetIndex < g_plan.target_count;
             ++targetIndex) {
            if ((dr6 & (1u << targetIndex)) == 0) continue;
            handled = true;
            if (!reserveObservation(targetIndex) ||
                state->depth >= kMaximumFramesPerThread) continue;
            const auto& target = g_plan.targets[targetIndex];
            ProbeFrame frame{};
            frame.target_index = targetIndex;
            frame.invocation_id = static_cast<std::uint32_t>(
                InterlockedIncrement(&g_invocation_sequence));
            frame.packet_id = g_config.packet_correlation != nullptr ?
                g_config.packet_correlation() : 0u;
            readableDword(context->Esp, &frame.return_address);
            GenericProbeEventV1 event{};
            fillEvent(*context, frame, EvidencePhase::Entry, 0u, &event);
            frame.object_value = objectValue(target, *context, event);
            event.object_value = frame.object_value;
            enqueueEvent(event);
            state->frames[state->depth++] = frame;
            InterlockedIncrement(&g_active_invocations);
            if ((target.follow_mask & FollowReturn) != 0 &&
                executableAddress(frame.return_address))
                setReturnBreakpoint(context, frame.return_address);
            else {
                --state->depth;
                InterlockedDecrement(&g_active_invocations);
            }
        }
        if ((dr6 & (1u << 3)) != 0 && state->depth != 0) {
            handled = true;
            ProbeFrame frame = state->frames[state->depth - 1u];
            const auto& target = g_plan.targets[frame.target_index];
            if (context->Eip == frame.return_address) {
                GenericProbeEventV1 event{};
                fillEvent(*context, frame, EvidencePhase::Return, 0u, &event);
                enqueueEvent(event);
                if ((target.follow_mask & FollowCaller) != 0) {
                    fillEvent(*context, frame, EvidencePhase::Caller, 0u, &event);
                    enqueueEvent(event);
                }
                --state->depth;
                InterlockedDecrement(&g_active_invocations);
                if ((target.follow_mask & FollowConsumer) != 0 &&
                    target.consumer_steps != 0) {
                    state->consumer = frame;
                    state->consumer_remaining = target.consumer_steps;
                    state->consumer_step = 0;
                    context->EFlags |= kTrapFlag;
                    setReturnBreakpoint(context, 0u);
                } else {
                    const DWORD priorReturn = state->depth != 0 ?
                        state->frames[state->depth - 1u].return_address : 0u;
                    setReturnBreakpoint(context, priorReturn);
                }
            }
        } else if ((dr6 & (1u << 14)) != 0 &&
                   state->consumer_remaining != 0) {
            handled = true;
            GenericProbeEventV1 event{};
            ++state->consumer_step;
            fillEvent(*context, state->consumer, EvidencePhase::Consumer,
                      state->consumer_step, &event);
            enqueueEvent(event);
            if (--state->consumer_remaining == 0) {
                context->EFlags &= ~kTrapFlag;
                const DWORD priorReturn = state->depth != 0 ?
                    state->frames[state->depth - 1u].return_address : 0u;
                setReturnBreakpoint(context, priorReturn);
            } else {
                context->EFlags |= kTrapFlag;
            }
        }
        InterlockedExchange(&state->busy, 0);
    }
    if (handled) {
        context->Dr6 = 0;
        context->EFlags |= kResumeFlag;
    }
    InterlockedDecrement(&g_handler_rundown);
    return handled ? EXCEPTION_CONTINUE_EXECUTION : EXCEPTION_CONTINUE_SEARCH;
}

bool configuredThreadKnown(DWORD threadId) noexcept
{
    const LONG count = InterlockedCompareExchange(&g_configured_thread_count, 0, 0);
    for (LONG index = 0; index < count && index < kConfiguredThreadCapacity; ++index)
        if (g_configured_threads[index] == threadId) return true;
    return false;
}

void rememberConfiguredThread(DWORD threadId) noexcept
{
    if (configuredThreadKnown(threadId)) return;
    const LONG index = InterlockedIncrement(&g_configured_thread_count) - 1;
    if (index >= 0 && index < kConfiguredThreadCapacity)
        g_configured_threads[index] = threadId;
    else
        InterlockedIncrement(&g_thread_configuration_failures);
}

bool configureThread(DWORD threadId, bool* conflict) noexcept
{
    if (conflict != nullptr) *conflict = false;
    if (threadId == GetCurrentThreadId() || threadId == g_manager_thread_id ||
        configuredThreadKnown(threadId)) return true;
    HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT |
        THREAD_SET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, threadId);
    if (thread == nullptr) {
        if (GetLastError() != ERROR_INVALID_PARAMETER)
            InterlockedIncrement(&g_thread_configuration_failures);
        return false;
    }
    bool result = false;
    if (SuspendThread(thread) != MAXDWORD) {
        CONTEXT context{};
        context.ContextFlags = CONTEXT_DEBUG_REGISTERS;
        if (GetThreadContext(thread, &context) != FALSE) {
            if ((context.Dr7 & 0xFFu) != 0) {
                if (conflict != nullptr) *conflict = true;
                InterlockedIncrement(&g_debug_register_conflicts);
            } else {
                context.Dr0 = context.Dr1 = context.Dr2 = 0;
                context.Dr3 = 0;
                context.Dr7 &= ~0xFFFF00FFu;
                for (DWORD index = 0; index < g_plan.target_count; ++index) {
                    const DWORD address = static_cast<DWORD>(g_config.image_base) +
                        g_plan.targets[index].function_rva;
                    if (index == 0) context.Dr0 = address;
                    else if (index == 1) context.Dr1 = address;
                    else context.Dr2 = address;
                    context.Dr7 |= 1u << (index * 2u);
                }
                result = SetThreadContext(thread, &context) != FALSE;
                if (!result)
                    InterlockedIncrement(&g_thread_configuration_failures);
            }
        } else {
            InterlockedIncrement(&g_thread_configuration_failures);
        }
        ResumeThread(thread);
    } else {
        InterlockedIncrement(&g_thread_configuration_failures);
    }
    CloseHandle(thread);
    if (result) rememberConfiguredThread(threadId);
    return result;
}

void enumerateAndConfigureThreads(bool* conflict) noexcept
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) {
        InterlockedIncrement(&g_thread_configuration_failures);
        return;
    }
    THREADENTRY32 entry{};
    entry.dwSize = sizeof(entry);
    if (Thread32First(snapshot, &entry) != FALSE) {
        do {
            if (entry.th32OwnerProcessID != g_config.process_id) continue;
            bool localConflict = false;
            configureThread(entry.th32ThreadID, &localConflict);
            if (localConflict && conflict != nullptr) *conflict = true;
        } while (Thread32Next(snapshot, &entry) != FALSE);
    }
    CloseHandle(snapshot);
}

void clearThreadBreakpoints(DWORD threadId) noexcept
{
    if (threadId == GetCurrentThreadId() || !configuredThreadKnown(threadId))
        return;
    HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT |
        THREAD_SET_CONTEXT, FALSE, threadId);
    if (thread == nullptr) return;
    if (SuspendThread(thread) != MAXDWORD) {
        CONTEXT context{};
        context.ContextFlags = CONTEXT_DEBUG_REGISTERS;
        if (GetThreadContext(thread, &context) != FALSE) {
            for (DWORD index = 0; index < g_plan.target_count; ++index) {
                const DWORD expected = static_cast<DWORD>(g_config.image_base) +
                    g_plan.targets[index].function_rva;
                DWORD* value = index == 0 ? &context.Dr0 :
                    (index == 1 ? &context.Dr1 : &context.Dr2);
                if (*value == expected) {
                    *value = 0;
                    context.Dr7 &= ~(3u << (index * 2u));
                    context.Dr7 &= ~(0xFu << (16u + index * 4u));
                }
            }
            context.Dr3 = 0;
            context.Dr7 &= ~(3u << 6);
            context.Dr7 &= ~(0xFu << 28);
            SetThreadContext(thread, &context);
        }
        ResumeThread(thread);
    }
    CloseHandle(thread);
}

void clearAllThreadBreakpoints() noexcept
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return;
    THREADENTRY32 entry{};
    entry.dwSize = sizeof(entry);
    if (Thread32First(snapshot, &entry) != FALSE) {
        do {
            if (entry.th32OwnerProcessID == g_config.process_id)
                clearThreadBreakpoints(entry.th32ThreadID);
        } while (Thread32Next(snapshot, &entry) != FALSE);
    }
    CloseHandle(snapshot);
}

DWORD WINAPI managerMain(void*) noexcept
{
    g_manager_thread_id = GetCurrentThreadId();
    while (WaitForSingleObject(g_manager_stop, kManagerPollMilliseconds) ==
           WAIT_TIMEOUT) {
        if (InterlockedCompareExchange(&g_active, 0, 0) == 0) continue;
        bool conflict = false;
        const LONG failuresBefore = InterlockedCompareExchange(
            &g_thread_configuration_failures, 0, 0);
        enumerateAndConfigureThreads(&conflict);
        const bool configurationFailed = InterlockedCompareExchange(
            &g_thread_configuration_failures, 0, 0) != failuresBefore;
        if (conflict || configurationFailed) {
            InterlockedExchange(&g_active, 0);
            clearAllThreadBreakpoints();
            SetEvent(g_manager_stop);
        }
    }
    return 0;
}

void resetPlanCounters() noexcept
{
    InterlockedExchange(&g_attempted, 0);
    InterlockedExchange(&g_accepted, 0);
    InterlockedExchange(&g_drained, 0);
    InterlockedExchange(&g_sequence, 0);
    InterlockedExchange(&g_invocation_sequence, 0);
    InterlockedExchange(&g_sequence_gaps, 0);
    InterlockedExchange(&g_last_drained_sequence, 0);
    InterlockedExchange(&g_active_invocations, 0);
    InterlockedExchange(&g_configured_thread_count, 0);
    InterlockedExchange(&g_thread_configuration_failures, 0);
    InterlockedExchange(&g_debug_register_conflicts, 0);
    std::memset(g_configured_threads, 0, sizeof(g_configured_threads));
    std::memset(g_thread_states, 0, sizeof(g_thread_states));
    std::memset(const_cast<LONG*>(g_target_observations), 0,
                sizeof(g_target_observations));
    for (DWORD laneIndex = 0; laneIndex < 4; ++laneIndex) {
        auto& lane = g_lanes[laneIndex];
        InterlockedExchange(&lane.write_position, 0);
        InterlockedExchange(&lane.read_position, 0);
        InterlockedExchange(&lane.pending, 0);
        InterlockedExchange(&lane.dropped, 0);
        for (LONG slot = 0; slot < kLaneCapacity; ++slot)
            InterlockedExchange(&lane.slots[slot].turn, slot);
    }
}

DWORD totalPending() noexcept
{
    DWORD result = 0;
    for (DWORD lane = 0; lane < 4; ++lane)
        result += static_cast<DWORD>(InterlockedCompareExchange(
            &g_lanes[lane].pending, 0, 0));
    return result;
}

void fillStatus(GenericProbeStatusV1* status, CommandStatus commandStatus) noexcept
{
    if (status == nullptr) return;
    *status = GenericProbeStatusV1{};
    status->magic = kPlanMagic;
    status->version = kPlanVersion;
    status->size = sizeof(*status);
    status->command_status = static_cast<DWORD>(commandStatus);
    status->generation = g_plan.generation;
    status->active = static_cast<DWORD>(
        InterlockedCompareExchange(&g_active, 0, 0) != 0);
    status->target_count = g_plan.target_count;
    status->configured_thread_count = static_cast<DWORD>(
        InterlockedCompareExchange(&g_configured_thread_count, 0, 0));
    status->thread_configuration_failures = static_cast<DWORD>(
        InterlockedCompareExchange(&g_thread_configuration_failures, 0, 0));
    status->debug_register_conflicts = static_cast<DWORD>(
        InterlockedCompareExchange(&g_debug_register_conflicts, 0, 0));
    status->attempted = static_cast<DWORD>(
        InterlockedCompareExchange(&g_attempted, 0, 0));
    status->accepted = static_cast<DWORD>(
        InterlockedCompareExchange(&g_accepted, 0, 0));
    status->drained = static_cast<DWORD>(
        InterlockedCompareExchange(&g_drained, 0, 0));
    status->pending = totalPending();
    status->sequence_gaps = static_cast<DWORD>(
        InterlockedCompareExchange(&g_sequence_gaps, 0, 0));
    for (DWORD lane = 0; lane < 4; ++lane) {
        status->lane_dropped[lane] = static_cast<DWORD>(
            InterlockedCompareExchange(&g_lanes[lane].dropped, 0, 0));
        status->lane_pending[lane] = static_cast<DWORD>(
            InterlockedCompareExchange(&g_lanes[lane].pending, 0, 0));
    }
    status->active_invocations = static_cast<DWORD>(
        InterlockedCompareExchange(&g_active_invocations, 0, 0));
}

void stopManager() noexcept
{
    if (g_manager_stop != nullptr) SetEvent(g_manager_stop);
    if (g_manager_thread != nullptr &&
        GetCurrentThreadId() != g_manager_thread_id)
        WaitForSingleObject(g_manager_thread, 5000);
    if (g_manager_thread != nullptr) CloseHandle(g_manager_thread);
    if (g_manager_stop != nullptr) CloseHandle(g_manager_stop);
    g_manager_thread = nullptr;
    g_manager_stop = nullptr;
    g_manager_thread_id = 0;
}

void forceClear() noexcept
{
    InterlockedExchange(&g_active, 0);
    stopManager();
    clearAllThreadBreakpoints();
    const ULONGLONG deadline = GetTickCount64() + 5000;
    while (InterlockedCompareExchange(&g_handler_rundown, 0, 0) != 0 &&
           GetTickCount64() < deadline) SwitchToThread();
    InterlockedExchange(&g_active_invocations, 0);
    std::memset(g_thread_states, 0, sizeof(g_thread_states));
}

} // namespace

bool Initialize(const RuntimeConfig& config) noexcept
{
    if (InterlockedCompareExchange(&g_initialized, 1, 0) != 0) return true;
    if (config.image_base == 0 || config.image_size == 0 ||
        config.process_id == 0 || config.process_creation_time == 0) {
        InterlockedExchange(&g_initialized, 0);
        return false;
    }
    g_config = config;
    InitializeCriticalSection(&g_command_lock);
    resetPlanCounters();
    g_veh = AddVectoredExceptionHandler(1, &vectoredHandler);
    if (g_veh == nullptr) {
        DeleteCriticalSection(&g_command_lock);
        InterlockedExchange(&g_initialized, 0);
        return false;
    }
    return true;
}

CommandStatus Execute(GenericProbeCommandV1* command) noexcept
{
    if (command == nullptr || command->magic != kPlanMagic ||
        command->version != kPlanVersion || command->size != sizeof(*command) ||
        InterlockedCompareExchange(&g_initialized, 0, 0) == 0) {
        if (command != nullptr)
            fillStatus(&command->status, CommandStatus::InvalidCommand);
        return CommandStatus::InvalidCommand;
    }
    EnterCriticalSection(&g_command_lock);
    CommandStatus result = CommandStatus::InvalidCommand;
    const auto action = static_cast<CommandAction>(command->action);
    if (action == CommandAction::Query) {
        result = CommandStatus::Queried;
    } else if (action == CommandAction::SelfTest) {
        result = SelfTest() ? CommandStatus::SelfTestPassed :
            CommandStatus::PlatformUnavailable;
    } else if (action == CommandAction::Apply) {
        if (InterlockedCompareExchange(&g_active, 0, 0) != 0) {
            result = CommandStatus::PlanAlreadyActive;
        } else if (totalPending() != 0 ||
                   InterlockedCompareExchange(&g_active_invocations, 0, 0) != 0) {
            result = CommandStatus::PendingEvidence;
        } else {
            result = validatePlan(command->plan);
            if (result == CommandStatus::Applied) {
                g_plan = command->plan;
                resetPlanCounters();
                InterlockedExchange(&g_active, 1);
                bool conflict = false;
                enumerateAndConfigureThreads(&conflict);
                if (conflict) {
                    forceClear();
                    result = CommandStatus::DebugRegisterConflict;
                } else if (InterlockedCompareExchange(
                               &g_thread_configuration_failures, 0, 0) != 0 ||
                           InterlockedCompareExchange(
                               &g_configured_thread_count, 0, 0) == 0) {
                    forceClear();
                    result = CommandStatus::ThreadConfigurationFailed;
                } else {
                    g_manager_stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
                    if (g_manager_stop != nullptr)
                        g_manager_thread = CreateThread(nullptr, 0, managerMain,
                            nullptr, 0, nullptr);
                    if (g_manager_stop == nullptr || g_manager_thread == nullptr) {
                        forceClear();
                        result = CommandStatus::PlatformUnavailable;
                    }
                }
            }
        }
    } else if (action == CommandAction::Clear) {
        forceClear();
        result = totalPending() != 0 ? CommandStatus::PendingEvidence :
            CommandStatus::Cleared;
    }
    fillStatus(&command->status, result);
    LeaveCriticalSection(&g_command_lock);
    return result;
}

bool Drain(GenericProbeEventV1* event) noexcept
{
    if (event == nullptr || InterlockedCompareExchange(&g_initialized, 0, 0) == 0)
        return false;
    LONG selectedLane = -1;
    std::uint64_t selectedSequence = ~std::uint64_t{0};
    for (DWORD laneIndex = 0; laneIndex < 4; ++laneIndex) {
        auto& lane = g_lanes[laneIndex];
        if (InterlockedCompareExchange(&lane.pending, 0, 0) == 0) continue;
        const LONG position = InterlockedCompareExchange(&lane.read_position, 0, 0);
        auto& slot = lane.slots[static_cast<DWORD>(position) % kLaneCapacity];
        if (InterlockedCompareExchange(&slot.turn, 0, 0) != position + 1)
            return false;
        if (slot.event.sequence < selectedSequence) {
            selectedSequence = slot.event.sequence;
            selectedLane = static_cast<LONG>(laneIndex);
        }
    }
    if (selectedLane < 0) return false;
    auto& lane = g_lanes[selectedLane];
    const LONG position = InterlockedCompareExchange(&lane.read_position, 0, 0);
    auto& slot = lane.slots[static_cast<DWORD>(position) % kLaneCapacity];
    *event = slot.event;
    MemoryBarrier();
    InterlockedExchange(&slot.turn, position + kLaneCapacity);
    InterlockedIncrement(&lane.read_position);
    InterlockedDecrement(&lane.pending);
    InterlockedIncrement(&g_drained);
    InterlockedExchange(&g_last_drained_sequence,
                        static_cast<LONG>(event->sequence));
    return true;
}

void Shutdown() noexcept
{
    if (InterlockedCompareExchange(&g_initialized, 0, 0) == 0) return;
    EnterCriticalSection(&g_command_lock);
    forceClear();
    if (g_veh != nullptr) RemoveVectoredExceptionHandler(g_veh);
    g_veh = nullptr;
    LeaveCriticalSection(&g_command_lock);
    DeleteCriticalSection(&g_command_lock);
    InterlockedExchange(&g_initialized, 0);
}

bool SelfTest() noexcept
{
    return sizeof(GenericProbeTargetV1) == 100u &&
        sizeof(GenericProbePlanV1) == 336u && kMaximumTargets == 3u &&
        kMaximumStackWords == 8u && kMaximumConsumerSteps == 4u &&
        kLaneCapacity >= 1024;
}

}} // namespace god2::generic_probe
