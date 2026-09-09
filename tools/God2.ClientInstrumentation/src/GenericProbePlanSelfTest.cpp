#include "GenericProbePlan.h"

#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

HANDLE g_start{};
volatile LONG g_fixture_result{};

__declspec(noinline) std::uint32_t __stdcall genericProbeFixture(
    std::uint32_t value)
{
    return value + 7u;
}

DWORD WINAPI fixtureThread(void*)
{
    if (WaitForSingleObject(g_start, 5000) != WAIT_OBJECT_0) return 1;
    InterlockedExchange(&g_fixture_result,
        static_cast<LONG>(genericProbeFixture(35u)));
    return 0;
}

std::uint64_t packetCorrelation()
{
    return 42u;
}

std::uint64_t processCreationTime()
{
    FILETIME creation{}, exit{}, kernel{}, user{};
    if (GetProcessTimes(GetCurrentProcess(), &creation, &exit, &kernel,
                        &user) == FALSE) return 0;
    return (static_cast<std::uint64_t>(creation.dwHighDateTime) << 32) |
        creation.dwLowDateTime;
}

DWORD imageSize()
{
    const auto* base = reinterpret_cast<const unsigned char*>(
        GetModuleHandleW(nullptr));
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(
        base + dos->e_lfanew);
    return nt->OptionalHeader.SizeOfImage;
}

} // namespace

int main()
{
    using namespace god2::generic_probe;
    static_assert(sizeof(void*) == 4, "self-test must execute the x86 ABI");
    g_start = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE signal = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    HANDLE worker = CreateThread(nullptr, 0, fixtureThread, nullptr, 0, nullptr);
    if (g_start == nullptr || signal == nullptr || worker == nullptr) return 10;
    const auto imageBase = reinterpret_cast<std::uintptr_t>(
        GetModuleHandleW(nullptr));
    if (!Initialize(RuntimeConfig{imageBase, imageSize(), GetCurrentProcessId(),
            processCreationTime(), true, &packetCorrelation, signal})) return 11;

    GenericProbeCommandV1 command{};
    command.magic = kPlanMagic;
    command.version = kPlanVersion;
    command.size = sizeof(command);
    command.action = static_cast<DWORD>(CommandAction::Apply);
    command.plan.magic = kPlanMagic;
    command.plan.version = kPlanVersion;
    command.plan.size = sizeof(command.plan);
    command.plan.generation = 7;
    command.plan.target_count = 1;
    command.plan.target_process_id = GetCurrentProcessId();
    command.plan.target_process_creation_time = processCreationTime();
    auto& target = command.plan.targets[0];
    target.target_id = 17;
    target.function_rva = static_cast<DWORD>(
        reinterpret_cast<std::uintptr_t>(&genericProbeFixture) - imageBase);
    target.capture_mask = CaptureEcx | CaptureEdx | CaptureBoundedStack;
    target.follow_mask = FollowReturn | FollowCaller | FollowConsumer;
    target.stack_words = 2;
    target.consumer_steps = 2;
    target.maximum_observations = 4;
    target.domain = 11;
    target.priority = 0;
    target.object_source = static_cast<DWORD>(ObjectSource::Stack0);
    target.expected_bytes_count = kExpectedBytes;
    std::memcpy(target.expected_bytes,
        reinterpret_cast<const void*>(&genericProbeFixture), kExpectedBytes);
    strcpy_s(target.candidate_id, "runtime-self-test");
    if (Execute(&command) != CommandStatus::Applied ||
        command.status.configured_thread_count == 0) return 12;
    SetEvent(g_start);
    if (WaitForSingleObject(worker, 5000) != WAIT_OBJECT_0 ||
        InterlockedCompareExchange(&g_fixture_result, 0, 0) != 42) return 13;

    DWORD entryCount = 0;
    DWORD returnCount = 0;
    DWORD callerCount = 0;
    DWORD consumerCount = 0;
    GenericProbeEventV1 event{};
    const ULONGLONG deadline = GetTickCount64() + 5000;
    do {
        while (Drain(&event)) {
            if (event.packet_id != 42u || event.generation != 7u ||
                event.target_id != 17u || event.domain != 11u ||
                event.priority != 0u) return 14;
            if (event.phase == static_cast<DWORD>(EvidencePhase::Entry))
                ++entryCount;
            else if (event.phase == static_cast<DWORD>(EvidencePhase::Return))
                ++returnCount;
            else if (event.phase == static_cast<DWORD>(EvidencePhase::Caller))
                ++callerCount;
            else if (event.phase == static_cast<DWORD>(EvidencePhase::Consumer))
                ++consumerCount;
        }
        if (entryCount == 1 && returnCount == 1 && callerCount == 1 &&
            consumerCount == 2) break;
        WaitForSingleObject(signal, 10);
    } while (GetTickCount64() < deadline);
    command.action = static_cast<DWORD>(CommandAction::Query);
    if (Execute(&command) != CommandStatus::Queried || entryCount != 1 ||
        returnCount != 1 || callerCount != 1 || consumerCount != 2 ||
        command.status.pending != 0 || command.status.sequence_gaps != 0 ||
        command.status.lane_dropped[0] != 0 ||
        command.status.active_invocations != 0) return 15;
    command.action = static_cast<DWORD>(CommandAction::Clear);
    if (Execute(&command) != CommandStatus::Cleared || command.status.active != 0)
        return 16;
    Shutdown();
    CloseHandle(worker);
    CloseHandle(signal);
    CloseHandle(g_start);
    std::puts("Generic Probe Plan runtime self-test PASS events=5 gaps=0 p0Loss=0 pending=0");
    return 0;
}
