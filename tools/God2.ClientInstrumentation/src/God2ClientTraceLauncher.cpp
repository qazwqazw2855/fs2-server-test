#include <windows.h>
#include <tlhelp32.h>

#include <cstdint>
#include <cstdio>
#include <limits>
#include <string>
#include <vector>

#include "GenericProbePlan.h"

namespace {

static_assert(sizeof(void*) == 4, "God2ClientTraceLauncher must be built for x86");
static_assert(sizeof(SIZE_T) == 4, "x86 SIZE_T is required");
static_assert(sizeof(uintptr_t) == 4, "x86 uintptr_t is required");

std::wstring argValue(int argc, wchar_t** argv, const wchar_t* name)
{
    for (int i = 1; i + 1 < argc; ++i) {
        if (_wcsicmp(argv[i], name) == 0) {
            return argv[i + 1];
        }
    }
    return {};
}

DWORD argDword(int argc, wchar_t** argv, const wchar_t* name)
{
    const auto value = argValue(argc, argv, name);
    if (value.empty()) {
        return 0;
    }
    return wcstoul(value.c_str(), nullptr, 0);
}

bool hasArg(int argc, wchar_t** argv, const wchar_t* name)
{
    for (int i = 1; i < argc; ++i) {
        if (_wcsicmp(argv[i], name) == 0) {
            return true;
        }
    }
    return false;
}

bool moduleStillLoaded(DWORD pid, DWORD module, std::wstring* modulePath = nullptr)
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return false;
    }

    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    const auto wanted = reinterpret_cast<BYTE*>(static_cast<uintptr_t>(module));
    bool found = false;
    if (Module32FirstW(snapshot, &entry) != FALSE) {
        do {
            if (entry.modBaseAddr == wanted) {
                found = true;
                if (modulePath != nullptr) {
                    *modulePath = entry.szExePath;
                }
                break;
            }
        } while (Module32NextW(snapshot, &entry) != FALSE);
    }

    CloseHandle(snapshot);
    return found;
}

DWORD remoteKernel32Export(DWORD pid, const char* exportName)
{
    HMODULE localKernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC localExport = localKernel32 != nullptr ? GetProcAddress(localKernel32, exportName) : nullptr;
    if (localExport == nullptr) {
        return 0;
    }
    const DWORD sameBitnessFallback = static_cast<DWORD>(reinterpret_cast<uintptr_t>(localExport));
    HMODULE ownerModule = nullptr;
    if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCWSTR>(localExport), &ownerModule) == FALSE || ownerModule == nullptr) {
        return sameBitnessFallback;
    }
    wchar_t ownerPath[MAX_PATH]{};
    if (GetModuleFileNameW(ownerModule, ownerPath, ARRAYSIZE(ownerPath)) == 0) {
        return sameBitnessFallback;
    }
    const wchar_t* ownerName = wcsrchr(ownerPath, L'\\');
    ownerName = ownerName != nullptr ? ownerName + 1 : ownerPath;
    const uintptr_t rva = reinterpret_cast<uintptr_t>(localExport) -
        reinterpret_cast<uintptr_t>(ownerModule);
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snapshot == INVALID_HANDLE_VALUE) {
        return sameBitnessFallback;
    }
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    DWORD result = 0;
    if (Module32FirstW(snapshot, &entry) != FALSE) {
        do {
            if (_wcsicmp(entry.szModule, ownerName) != 0) {
                continue;
            }
            const uintptr_t base = reinterpret_cast<uintptr_t>(entry.modBaseAddr);
            if (rva <= (std::numeric_limits<DWORD>::max)() - base) {
                result = static_cast<DWORD>(base + rva);
            }
            break;
        } while (Module32NextW(snapshot, &entry) != FALSE);
    }
    CloseHandle(snapshot);
    // Before the primary thread of a CREATE_SUSPENDED process runs, Toolhelp can
    // omit forwarded system modules from its loader list.  This launcher and its
    // target are both statically asserted x86; system DLL image sections use the
    // same per-boot ASLR mapping across same-bitness processes, so the local
    // resolved address is the required early-start fallback.
    if (result == 0) {
        result = sameBitnessFallback;
    }
    return result;
}

DWORD remoteExportAddress(DWORD remoteModule, const std::wstring& modulePath, const char* exportName)
{
    HMODULE local = LoadLibraryExW(modulePath.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (local == nullptr) {
        std::fwprintf(stderr, L"LoadLibraryExW local probe failed: %lu\n", GetLastError());
        return 0;
    }

    FARPROC localProc = GetProcAddress(local, exportName);
    if (localProc == nullptr) {
        std::fprintf(stderr, "GetProcAddress %s failed: %lu\n", exportName, GetLastError());
        FreeLibrary(local);
        return 0;
    }

    const auto offset = reinterpret_cast<uintptr_t>(localProc) - reinterpret_cast<uintptr_t>(local);
    FreeLibrary(local);
    return static_cast<DWORD>(static_cast<uintptr_t>(remoteModule) + offset);
}

std::wstring quote(const std::wstring& value)
{
    return L"\"" + value + L"\"";
}

bool appendLaunchIdentityConfig(const std::wstring& dllPath, HANDLE process,
                                DWORD processId, bool selfTestIdentity,
                                bool officialIdentity)
{
    constexpr wchar_t testDllName[] = L"God2ClientTraceProbeTest.dll";
    constexpr wchar_t officialDllName[] = L"God2ClientTraceProbe.dll";
    const auto slash = dllPath.find_last_of(L"\\/");
    const std::wstring fileName = slash == std::wstring::npos ? dllPath : dllPath.substr(slash + 1);
    const bool expectedDll =
        selfTestIdentity != officialIdentity &&
        ((selfTestIdentity && _wcsicmp(fileName.c_str(), testDllName) == 0) ||
         (officialIdentity && _wcsicmp(fileName.c_str(), officialDllName) == 0));
    if (!expectedDll) {
        return false;
    }
    FILETIME creation{}, exit{}, kernel{}, user{};
    if (GetProcessTimes(process, &creation, &exit, &kernel, &user) == FALSE) {
        return false;
    }
    ULARGE_INTEGER creationValue{};
    creationValue.LowPart = creation.dwLowDateTime;
    creationValue.HighPart = creation.dwHighDateTime;
    const std::wstring directory = slash == std::wstring::npos ? L"" : dllPath.substr(0, slash + 1);
    const std::wstring configPath = directory + L"God2ClientTraceProbe.attach.env";
    FILE* config{};
    if (_wfopen_s(&config, configPath.c_str(), L"ab") != 0 || config == nullptr) {
        return false;
    }
    const int written = std::fprintf(config,
        "clientProcessId=%lu\nclientProcessCreationTime=%llu\n",
        static_cast<unsigned long>(processId),
        static_cast<unsigned long long>(creationValue.QuadPart));
    const bool flushed = written > 0 && std::fflush(config) == 0;
    std::fclose(config);
    return flushed;
}

std::wstring buildEnvironment(const std::wstring& traceDir, const std::wstring& generalLog, const std::wstring& metadata)
{
    LPWCH current = GetEnvironmentStringsW();
    std::vector<std::wstring> entries;
    for (LPCWSTR cursor = current; cursor != nullptr && *cursor != L'\0'; cursor += wcslen(cursor) + 1) {
        if (_wcsnicmp(cursor, L"GOD2_CLIENT_TRACE_DIR=", 22) != 0 &&
            _wcsnicmp(cursor, L"GOD2_CLIENT_TRACE_GENERAL_LOG=", 30) != 0 &&
            _wcsnicmp(cursor, L"GOD2_CLIENT_TRACE_METADATA=", 27) != 0) {
            entries.emplace_back(cursor);
        }
    }
    if (current != nullptr) {
        FreeEnvironmentStringsW(current);
    }
    entries.emplace_back(L"GOD2_CLIENT_TRACE_DIR=" + traceDir);
    entries.emplace_back(L"GOD2_CLIENT_TRACE_GENERAL_LOG=" + generalLog);
    entries.emplace_back(L"GOD2_CLIENT_TRACE_METADATA=" + metadata);

    std::wstring block;
    for (const auto& entry : entries) {
        block.append(entry);
        block.push_back(L'\0');
    }
    block.push_back(L'\0');
    return block;
}

DWORD injectDll(HANDLE process, DWORD pid, const std::wstring& dllPath)
{
    if (dllPath.size() >= ((std::numeric_limits<SIZE_T>::max)() / sizeof(wchar_t))) {
        SetLastError(ERROR_BUFFER_OVERFLOW);
        return 0;
    }
    const SIZE_T bytes = static_cast<SIZE_T>((dllPath.size() + 1) * sizeof(wchar_t));
    void* remote = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (remote == nullptr) {
        std::fwprintf(stderr, L"VirtualAllocEx failed: %lu\n", GetLastError());
        return 0;
    }
    SIZE_T written = 0;
    if (WriteProcessMemory(process, remote, dllPath.c_str(), bytes, &written) == FALSE || written != bytes) {
        std::fwprintf(stderr, L"WriteProcessMemory failed: %lu\n", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return 0;
    }

    const DWORD loadLibraryAddress = remoteKernel32Export(pid, "LoadLibraryW");
    auto* loadLibrary = reinterpret_cast<LPTHREAD_START_ROUTINE>(
        static_cast<uintptr_t>(loadLibraryAddress));
    if (loadLibrary == nullptr) {
        std::fwprintf(stderr, L"Could not resolve remote LoadLibraryW.\n");
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return 0;
    }
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, loadLibrary, remote, 0, nullptr);
    if (thread == nullptr) {
        std::fwprintf(stderr, L"CreateRemoteThread LoadLibraryW failed: %lu\n", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return 0;
    }

    const DWORD wait = WaitForSingleObject(thread, 15000);
    DWORD module{};
    if (wait == WAIT_OBJECT_0) {
        GetExitCodeThread(thread, &module);
    } else {
        std::fwprintf(stderr, L"LoadLibraryW remote thread timed out or failed. wait=%lu lastError=%lu\n",
                      wait, GetLastError());
    }
    CloseHandle(thread);
    // The remote thread can still be using the string after a timeout.
    if (wait == WAIT_OBJECT_0) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    }
    if (module == 0) {
        std::fwprintf(stderr, L"LoadLibraryW returned null. lastError=%lu\n", GetLastError());
    }
    return module;
}

bool runRemoteThread(HANDLE process, LPTHREAD_START_ROUTINE start, void* parameter, DWORD timeoutMs, DWORD* exitCode)
{
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, start, parameter, 0, nullptr);
    if (thread == nullptr) {
        std::fwprintf(stderr, L"CreateRemoteThread failed: %lu\n", GetLastError());
        return false;
    }

    const DWORD wait = WaitForSingleObject(thread, timeoutMs);
    if (wait != WAIT_OBJECT_0) {
        std::fwprintf(stderr, L"Remote thread did not finish. wait=%lu lastError=%lu\n", wait, GetLastError());
        CloseHandle(thread);
        return false;
    }

    DWORD result{};
    if (GetExitCodeThread(thread, &result) == FALSE) {
        std::fwprintf(stderr, L"GetExitCodeThread failed: %lu\n", GetLastError());
        CloseHandle(thread);
        return false;
    }

    CloseHandle(thread);
    if (exitCode != nullptr) {
        *exitCode = result;
    }
    return true;
}

bool detachDll(DWORD pid, DWORD module)
{
    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION, FALSE, pid);
    if (process == nullptr) {
        std::fwprintf(stderr, L"OpenProcess failed: %lu\n", GetLastError());
        return false;
    }

    std::wstring modulePath;
    if (!moduleStillLoaded(pid, module, &modulePath)) {
        std::wprintf(L"DETACH=ALREADY_UNLOADED\n");
        CloseHandle(process);
        return true;
    }

    const DWORD stopAddress = remoteExportAddress(module, modulePath, "God2TraceProbeStop");
    if (stopAddress != 0) {
        DWORD stopResult{};
        if (!runRemoteThread(process, reinterpret_cast<LPTHREAD_START_ROUTINE>(static_cast<uintptr_t>(stopAddress)), nullptr, 15000, &stopResult) || stopResult == 0) {
            std::fwprintf(stderr, L"God2TraceProbeStop failed or timed out. result=%lu\n", stopResult);
            CloseHandle(process);
            return false;
        }
        std::wprintf(L"STOP=PASS\n");
    }

    const DWORD canUnloadAddress = remoteExportAddress(module, modulePath, "God2TraceProbeCanUnload");
    DWORD canUnload{};
    if (canUnloadAddress == 0 ||
        !runRemoteThread(process,
                         reinterpret_cast<LPTHREAD_START_ROUTINE>(static_cast<uintptr_t>(canUnloadAddress)),
                         nullptr, 15000, &canUnload)) {
        std::fwprintf(stderr, L"Could not prove that unloading the probe is safe; leaving it inactive and resident.\n");
        CloseHandle(process);
        return true;
    }
    if (canUnload == 0) {
        std::wprintf(L"DETACH=PASS MODULE_RESIDENT_INACTIVE=1\n");
        CloseHandle(process);
        return true;
    }

    const DWORD freeLibraryAddress = remoteKernel32Export(pid, "FreeLibrary");
    auto* freeLibrary = reinterpret_cast<LPTHREAD_START_ROUTINE>(
        static_cast<uintptr_t>(freeLibraryAddress));
    if (freeLibrary == nullptr) {
        std::fwprintf(stderr, L"Could not resolve remote FreeLibrary.\n");
        CloseHandle(process);
        return false;
    }
    DWORD result{};
    if (!runRemoteThread(process, freeLibrary, reinterpret_cast<void*>(static_cast<uintptr_t>(module)), 15000, &result) || result == 0) {
        std::fwprintf(stderr, L"FreeLibrary failed or timed out. result=%lu\n", result);
        CloseHandle(process);
        return false;
    }

    for (int attempt = 0; attempt < 20; ++attempt) {
        if (!moduleStillLoaded(pid, module)) {
            CloseHandle(process);
            return true;
        }
        Sleep(100);
    }

    std::fwprintf(stderr, L"FreeLibrary returned success but module is still loaded.\n");
    CloseHandle(process);
    return false;
}

int launch(int argc, wchar_t** argv)
{
    const auto exe = argValue(argc, argv, L"--exe");
    const auto cwd = argValue(argc, argv, L"--cwd");
    const auto dll = argValue(argc, argv, L"--dll");
    const auto traceDir = argValue(argc, argv, L"--trace-dir");
    const auto generalLog = argValue(argc, argv, L"--general-log");
    const auto metadata = argValue(argc, argv, L"--metadata");
    const DWORD postInjectBeforeResumeMs = argDword(argc, argv, L"--post-inject-before-resume-ms");
    if (exe.empty() || cwd.empty() || dll.empty() || traceDir.empty() || generalLog.empty() || metadata.empty()) {
        std::fwprintf(stderr, L"launch usage: --launch --exe <path> --cwd <path> --dll <path> --trace-dir <dir> --general-log <path> --metadata <path>\n");
        return 2;
    }

    auto commandLine = quote(exe);
    std::vector<wchar_t> commandLineBuffer(commandLine.begin(), commandLine.end());
    commandLineBuffer.push_back(L'\0');
    auto environment = buildEnvironment(traceDir, generalLog, metadata);
    std::vector<wchar_t> environmentBuffer(environment.begin(), environment.end());

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    const BOOL created = CreateProcessW(
        nullptr,
        commandLineBuffer.data(),
        nullptr,
        nullptr,
        FALSE,
        CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT,
        environmentBuffer.data(),
        cwd.c_str(),
        &startup,
        &process);
    if (created == FALSE) {
        std::fwprintf(stderr, L"CreateProcessW failed: %lu\n", GetLastError());
        return 3;
    }

    const bool selfTestIdentity = hasArg(argc, argv, L"--self-test-exact-identity");
    const bool officialIdentity = hasArg(argc, argv, L"--official-exact-identity");
    if ((selfTestIdentity || officialIdentity) &&
        !appendLaunchIdentityConfig(dll, process.hProcess, process.dwProcessId,
                                    selfTestIdentity, officialIdentity)) {
        std::fwprintf(stderr, L"Could not bind exact launch process identity.\n");
        TerminateProcess(process.hProcess, ERROR_INVALID_DATA);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 7;
    }

    const DWORD module = injectDll(process.hProcess, process.dwProcessId, dll);
    if (module == 0) {
        TerminateProcess(process.hProcess, 32);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 4;
    }

    const DWORD readyAddress = remoteExportAddress(module, dll, "God2TraceProbeWaitReady");
    DWORD readyResult{};
    if (readyAddress == 0 ||
        !runRemoteThread(process.hProcess,
                         reinterpret_cast<LPTHREAD_START_ROUTINE>(static_cast<uintptr_t>(readyAddress)),
                         nullptr, 25000, &readyResult) || readyResult == 0) {
        std::fwprintf(stderr, L"Injected probe did not report ready.\n");
        TerminateProcess(process.hProcess, 34);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 6;
    }

    std::wprintf(L"PID=%lu\n", process.dwProcessId);
    std::wprintf(L"MODULE=0x%08lX\n", module);
    std::fflush(stdout);

    if (postInjectBeforeResumeMs > 0) {
        Sleep(postInjectBeforeResumeMs);
    }

    if (ResumeThread(process.hThread) == static_cast<DWORD>(-1)) {
        std::fwprintf(stderr, L"ResumeThread failed: %lu\n", GetLastError());
        TerminateProcess(process.hProcess, 33);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 5;
    }

    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}

int attach(int argc, wchar_t** argv)
{
    const DWORD pid = argDword(argc, argv, L"--pid");
    const auto dll = argValue(argc, argv, L"--dll");
    if (pid == 0 || dll.empty()) {
        std::fwprintf(stderr, L"attach usage: --attach --pid <pid> --dll <path>\n");
        return 2;
    }

    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid);
    if (process == nullptr) {
        std::fwprintf(stderr, L"OpenProcess failed: %lu\n", GetLastError());
        return 3;
    }
    const DWORD module = injectDll(process, pid, dll);
    CloseHandle(process);
    if (module == 0) {
        return 4;
    }
    std::wprintf(L"PID=%lu\n", pid);
    std::wprintf(L"MODULE=0x%08lX\n", module);
    return 0;
}

int detach(int argc, wchar_t** argv)
{
    const DWORD pid = argDword(argc, argv, L"--pid");
    const DWORD module = argDword(argc, argv, L"--module");
    if (pid == 0 || module == 0) {
        std::fwprintf(stderr, L"detach usage: --detach --pid <pid> --module <module>\n");
        return 2;
    }

    if (!detachDll(pid, module)) {
        return 3;
    }
    std::wprintf(L"DETACH=PASS\n");
    return 0;
}

bool readGenericPlan(const std::wstring& path,
                     god2::generic_probe::GenericProbePlanV1* plan)
{
    if (plan == nullptr || path.empty()) return false;
    FILE* input{};
    if (_wfopen_s(&input, path.c_str(), L"rb") != 0 || input == nullptr)
        return false;
    const std::size_t read = std::fread(plan, 1, sizeof(*plan), input);
    const int trailing = std::fgetc(input);
    const bool closed = std::fclose(input) == 0;
    return read == sizeof(*plan) && trailing == EOF && closed;
}

int genericPlanCommand(int argc, wchar_t** argv)
{
    using namespace god2::generic_probe;
    const DWORD pid = argDword(argc, argv, L"--pid");
    const DWORD module = argDword(argc, argv, L"--module");
    CommandAction action{};
    if (hasArg(argc, argv, L"--generic-plan-apply"))
        action = CommandAction::Apply;
    else if (hasArg(argc, argv, L"--generic-plan-clear"))
        action = CommandAction::Clear;
    else if (hasArg(argc, argv, L"--generic-plan-query"))
        action = CommandAction::Query;
    else if (hasArg(argc, argv, L"--generic-plan-self-test"))
        action = CommandAction::SelfTest;
    if (pid == 0 || module == 0 || static_cast<DWORD>(action) == 0) {
        std::fwprintf(stderr, L"generic plan usage: --generic-plan-apply|clear|query|self-test --pid <pid> --module <module> [--plan <binary>]\n");
        return 2;
    }

    GenericProbeCommandV1 command{};
    command.magic = kPlanMagic;
    command.version = kPlanVersion;
    command.size = sizeof(command);
    command.action = static_cast<DWORD>(action);
    if (action == CommandAction::Apply) {
        const auto planPath = argValue(argc, argv, L"--plan");
        if (!readGenericPlan(planPath, &command.plan)) {
            std::fwprintf(stderr, L"Could not read an exact GenericProbePlanV1 binary: %s\n",
                          planPath.c_str());
            return 3;
        }
    }

    std::wstring modulePath;
    if (!moduleStillLoaded(pid, module, &modulePath)) {
        std::fwprintf(stderr, L"Probe module is not loaded at 0x%08lX.\n", module);
        return 4;
    }
    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid);
    if (process == nullptr) {
        std::fwprintf(stderr, L"OpenProcess failed: %lu\n", GetLastError());
        return 5;
    }
    void* remote = VirtualAllocEx(process, nullptr, sizeof(command),
                                  MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    SIZE_T transferred{};
    if (remote == nullptr || WriteProcessMemory(process, remote, &command,
            sizeof(command), &transferred) == FALSE ||
        transferred != sizeof(command)) {
        std::fwprintf(stderr, L"Could not publish GenericProbeCommandV1: %lu\n",
                      GetLastError());
        if (remote != nullptr) VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return 6;
    }
    const DWORD exportAddress = remoteExportAddress(
        module, modulePath, "God2TraceProbeGenericPlan");
    DWORD remoteResult{};
    const bool completed = exportAddress != 0 && runRemoteThread(process,
        reinterpret_cast<LPTHREAD_START_ROUTINE>(
            static_cast<uintptr_t>(exportAddress)), remote, 20000,
        &remoteResult);
    if (!completed || ReadProcessMemory(process, remote, &command,
            sizeof(command), &transferred) == FALSE ||
        transferred != sizeof(command)) {
        std::fwprintf(stderr, L"Generic probe command failed or timed out. result=%lu\n",
                      remoteResult);
        // Keep the buffer owned by the target on timeout because the remote
        // command thread may still be using it.
        if (completed) VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return 7;
    }
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    CloseHandle(process);
    const auto& status = command.status;
    std::wprintf(L"GENERIC_PROBE_STATUS=%lu\nGENERATION=%lu\nACTIVE=%lu\nTARGETS=%lu\nCONFIGURED_THREADS=%lu\nTHREAD_CONFIGURATION_FAILURES=%lu\nDEBUG_REGISTER_CONFLICTS=%lu\nATTEMPTED=%lu\nACCEPTED=%lu\nDRAINED=%lu\nPENDING=%lu\nSEQUENCE_GAPS=%lu\nACTIVE_INVOCATIONS=%lu\n",
        status.command_status, status.generation, status.active,
        status.target_count, status.configured_thread_count,
        status.thread_configuration_failures, status.debug_register_conflicts,
        status.attempted, status.accepted, status.drained, status.pending,
        status.sequence_gaps, status.active_invocations);
    for (DWORD lane = 0; lane < 4; ++lane)
        std::wprintf(L"LANE%lu_DROPPED=%lu\nLANE%lu_PENDING=%lu\n",
            lane, status.lane_dropped[lane], lane, status.lane_pending[lane]);
    const bool formalGate = status.lane_dropped[0] == 0 &&
        status.lane_dropped[1] == 0 && status.sequence_gaps == 0 &&
        status.pending == 0 && status.active_invocations == 0 &&
        status.thread_configuration_failures == 0 &&
        status.debug_register_conflicts == 0;
    std::wprintf(L"GENERIC_FORMAL_GATE=%s\n",
                 formalGate ? L"PASS" : L"INCOMPLETE");
    return remoteResult == status.command_status &&
        (remoteResult == static_cast<DWORD>(CommandStatus::Applied) ||
         remoteResult == static_cast<DWORD>(CommandStatus::Cleared) ||
         remoteResult == static_cast<DWORD>(CommandStatus::Queried) ||
         remoteResult == static_cast<DWORD>(CommandStatus::SelfTestPassed) ||
         remoteResult == static_cast<DWORD>(CommandStatus::PendingEvidence)) ? 0 : 8;
}

} // namespace

int wmain(int argc, wchar_t** argv)
{
    if (hasArg(argc, argv, L"--generic-plan-apply") ||
        hasArg(argc, argv, L"--generic-plan-clear") ||
        hasArg(argc, argv, L"--generic-plan-query") ||
        hasArg(argc, argv, L"--generic-plan-self-test")) {
        return genericPlanCommand(argc, argv);
    }
    if (hasArg(argc, argv, L"--launch")) {
        return launch(argc, argv);
    }
    if (hasArg(argc, argv, L"--attach")) {
        return attach(argc, argv);
    }
    if (hasArg(argc, argv, L"--detach")) {
        return detach(argc, argv);
    }

    std::fwprintf(stderr, L"usage: --launch | --attach | --detach | --generic-plan-apply | --generic-plan-clear | --generic-plan-query | --generic-plan-self-test\n");
    return 2;
}
