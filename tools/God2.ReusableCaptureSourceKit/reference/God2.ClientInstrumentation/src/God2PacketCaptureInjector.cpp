#include <windows.h>
#include <bcrypt.h>
#include <psapi.h>
#include <tlhelp32.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "psapi.lib")
#pragma comment(lib, "version.lib")

namespace {

// This helper is deliberately compiled as x86.  The remote thread return value is
// a 32-bit HMODULE and all pointer-sized arithmetic below must remain 32-bit.
static_assert(sizeof(void*) == 4, "God2PacketCaptureInjector must be built for x86");
static_assert(sizeof(SIZE_T) == 4, "x86 SIZE_T is required");
static_assert(sizeof(uintptr_t) == 4, "x86 uintptr_t is required");

constexpr wchar_t kExpectedTargetSha256[] =
    L"6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
constexpr wchar_t kExpectedTargetVersion[] = L"1.0.0.1";

std::wstring Arg(int argc, wchar_t** argv, const wchar_t* name) {
    for (int index = 1; index + 1 < argc; ++index)
        if (_wcsicmp(argv[index], name) == 0) return argv[index + 1];
    return {};
}

bool HasArg(int argc, wchar_t** argv, const wchar_t* name) {
    for (int index = 1; index < argc; ++index)
        if (_wcsicmp(argv[index], name) == 0) return true;
    return false;
}

struct InjectorResultRecord {
    const char* status{"REJECTED_ARGUMENTS"};
    DWORD code{ERROR_INVALID_PARAMETER};
    bool injectionAttempted{};
    bool moduleWasEverLoaded{};
    bool moduleLoadStateVerified{};
    bool moduleSnapshotVerified{};
    // Absence and strict-unload evidence are fail-closed.  They become true
    // only after a complete path/file-identity snapshot, two verified absence
    // snapshots, or a terminal target-process exit.
    bool moduleAbsent{};
    bool targetProcessExited{};
    bool targetIdentityVerified{};
    bool probeReady{};
    bool stopSucceeded{};
    bool unloadSafe{};
    bool moduleUnloaded{};
    bool moduleResidentInactive{};
    bool strictUnloadVerified{};
    bool cleanupRetriable{};
    const char* injectionMode{"None"};
    size_t suspendedThreadCount{};
    DWORD primaryThreadId{};
    bool extraReferenceRequested{};
    bool extraReferenceLoaded{};
    bool extraReferenceNegativeFirstFreeLibraryStillPresent{};
    bool extraReferenceNegativeNotClaimedUnloaded{};
    bool extraReferenceReleasedThenModuleAbsent{};
};

bool WriteResult(const std::wstring& path,
                 const InjectorResultRecord& result) {
    if (path.empty() || result.status == nullptr ||
        result.injectionMode == nullptr) return false;
    const auto jsonBool = [](bool value) noexcept {
        return value ? "true" : "false";
    };
    char content[4096]{};
    const int formatted = sprintf_s(content,
        "{\"schemaVersion\":2,\"status\":\"%s\",\"code\":%lu,"
        "\"injectionAttempted\":%s,\"moduleWasEverLoaded\":%s,"
        "\"moduleLoadStateVerified\":%s,\"moduleSnapshotVerified\":%s,"
        "\"moduleAbsent\":%s,\"targetProcessExited\":%s,"
        "\"targetIdentityVerified\":%s,\"probeReady\":%s,"
        "\"stopSucceeded\":%s,\"unloadSafe\":%s,"
        "\"moduleUnloaded\":%s,\"moduleResidentInactive\":%s,"
        "\"strictUnloadVerified\":%s,\"cleanupRetriable\":%s,"
        "\"injectionMode\":\"%s\",\"suspendedThreadCount\":%zu,"
        "\"primaryThreadId\":%lu,\"extraReferenceRequested\":%s,"
        "\"extraReferenceLoaded\":%s,"
        "\"extraReferenceNegativeFirstFreeLibraryStillPresent\":%s,"
        "\"extraReferenceNegativeNotClaimedUnloaded\":%s,"
        "\"extraReferenceReleasedThenModuleAbsent\":%s}\r\n",
        result.status, result.code,
        jsonBool(result.injectionAttempted),
        jsonBool(result.moduleWasEverLoaded),
        jsonBool(result.moduleLoadStateVerified),
        jsonBool(result.moduleSnapshotVerified), jsonBool(result.moduleAbsent),
        jsonBool(result.targetProcessExited),
        jsonBool(result.targetIdentityVerified), jsonBool(result.probeReady),
        jsonBool(result.stopSucceeded), jsonBool(result.unloadSafe),
        jsonBool(result.moduleUnloaded),
        jsonBool(result.moduleResidentInactive),
        jsonBool(result.strictUnloadVerified),
        jsonBool(result.cleanupRetriable), result.injectionMode,
        result.suspendedThreadCount, result.primaryThreadId,
        jsonBool(result.extraReferenceRequested),
        jsonBool(result.extraReferenceLoaded),
        jsonBool(result.extraReferenceNegativeFirstFreeLibraryStillPresent),
        jsonBool(result.extraReferenceNegativeNotClaimedUnloaded),
        jsonBool(result.extraReferenceReleasedThenModuleAbsent));
    if (formatted <= 0 || static_cast<size_t>(formatted) >= sizeof(content))
        return false;
    wchar_t temporary[MAX_PATH * 4]{};
    if (swprintf_s(temporary, L"%s.tmp-%lu-%lu", path.c_str(),
            GetCurrentProcessId(), GetCurrentThreadId()) <= 0) return false;
    HANDLE file = CreateFileW(temporary, GENERIC_WRITE, 0, nullptr,
                              CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const DWORD length = static_cast<DWORD>(formatted);
    const BOOL ok = WriteFile(file, content, length, &written, nullptr);
    const BOOL flushed = ok != FALSE ? FlushFileBuffers(file) : FALSE;
    CloseHandle(file);
    if (ok == FALSE || flushed == FALSE || written != length ||
        MoveFileExW(temporary, path.c_str(), MOVEFILE_REPLACE_EXISTING |
                    MOVEFILE_WRITE_THROUGH) == FALSE) {
        DeleteFileW(temporary);
        return false;
    }
    return true;
}

bool WriteResult(const std::wstring& path, const char* status,
                 DWORD value = 0, const char* = nullptr) {
    InjectorResultRecord result{};
    result.status = status;
    result.code = value;
    return WriteResult(path, result);
}

struct OwnedRemoteThreadSelfTestResult {
    DWORD initialTimeoutMs{};
    DWORD requestedDelayMs{};
    ULONGLONG elapsedMs{};
    bool initialTimeoutObserved{};
    bool threadSignaled{};
    bool handleRetainedUntilSignal{};
    bool delayedLoadCompleted{};
    bool moduleReferenceReleased{};
    bool waitFailedInjected{};
    bool waitFailedOwnerRetained{};
    bool getExitCodeFailureInjected{};
    bool getExitCodeOwnerRetained{};
    bool terminalBeforeHandleRelease{};
    bool exitCodeKnownAfterRetry{};
    bool moduleIdentityFixturePassed{};
    bool foreignOldBaseClassifiedAbsent{};
    bool expectedNewBaseClassifiedPresent{};
    bool enumerationFailureClassifiedUnknown{};
    bool baseMatchUnreadableClassifiedUnknown{};
    bool truncatedPathClassifiedUnknown{};
    bool baselineUnreadableClassifiedUnknown{};
    bool foreignBeforeRemoteActionRejected{};
    bool foreignRemoteActionCountZero{};
    DWORD foreignAuthorizationAttemptCount{};
    DWORD foreignAuthorizationRejectedCount{};
    DWORD foreignRemoteThreadCreateCount{};
    DWORD foreignRemoteThreadTerminalCount{};
    bool releaseCreateFailureRetainedToken{};
    bool releaseTokenConsumedOnce{};
    DWORD releaseThreadCreateCount{};
};

bool RunOwnedRemoteThreadSelfTest(OwnedRemoteThreadSelfTestResult* result);
bool RunInjectorOwnershipStateSelfTests(
    OwnedRemoteThreadSelfTestResult* result);

bool WriteOwnedRemoteThreadSelfTestReport(
    const std::wstring& path,
    const OwnedRemoteThreadSelfTestResult& result) {
    if (path.empty()) return false;
    const auto value = [](bool observed) noexcept {
        return observed ? "true" : "false";
    };
    char content[4096]{};
    const int formatted = sprintf_s(content,
        "{\"SchemaId\":\"God2OwnedRemoteThreadSelfTest\"," 
        "\"SchemaVersion\":1,\"InitialTimeoutMs\":%lu,"
        "\"RequestedDelayMs\":%lu,\"ElapsedMs\":%llu,"
        "\"InitialTimeoutObserved\":%s,\"ThreadSignaled\":%s,"
        "\"HandleRetainedUntilSignal\":%s,"
        "\"DelayedLoadCompleted\":%s,"
        "\"ModuleReferenceReleased\":%s,"
        "\"WaitFailedInjected\":%s,"
        "\"WaitFailedOwnerRetained\":%s,"
        "\"GetExitCodeFailureInjected\":%s,"
        "\"GetExitCodeOwnerRetained\":%s,"
        "\"TerminalBeforeHandleRelease\":%s,"
        "\"ExitCodeKnownAfterRetry\":%s,"
        "\"ModuleIdentityFixturePassed\":%s,"
        "\"ForeignOldBaseClassifiedAbsent\":%s,"
        "\"ExpectedNewBaseClassifiedPresent\":%s,"
        "\"EnumerationFailureClassifiedUnknown\":%s,"
        "\"BaseMatchUnreadableClassifiedUnknown\":%s,"
        "\"TruncatedPathClassifiedUnknown\":%s,"
        "\"BaselineUnreadableClassifiedUnknown\":%s,"
        "\"ForeignBeforeRemoteActionRejected\":%s,"
        "\"ForeignRemoteActionCountZero\":%s,"
        "\"ForeignAuthorizationAttemptCount\":%lu,"
        "\"ForeignAuthorizationRejectedCount\":%lu,"
        "\"ForeignRemoteThreadCreateCount\":%lu,"
        "\"ForeignRemoteThreadTerminalCount\":%lu,"
        "\"ReleaseCreateFailureRetainedToken\":%s,"
        "\"ReleaseTokenConsumedOnce\":%s,"
        "\"ReleaseThreadCreateCount\":%lu}\r\n",
        result.initialTimeoutMs, result.requestedDelayMs,
        static_cast<unsigned long long>(result.elapsedMs),
        value(result.initialTimeoutObserved), value(result.threadSignaled),
        value(result.handleRetainedUntilSignal),
        value(result.delayedLoadCompleted),
        value(result.moduleReferenceReleased),
        value(result.waitFailedInjected),
        value(result.waitFailedOwnerRetained),
        value(result.getExitCodeFailureInjected),
        value(result.getExitCodeOwnerRetained),
        value(result.terminalBeforeHandleRelease),
        value(result.exitCodeKnownAfterRetry),
        value(result.moduleIdentityFixturePassed),
        value(result.foreignOldBaseClassifiedAbsent),
        value(result.expectedNewBaseClassifiedPresent),
        value(result.enumerationFailureClassifiedUnknown),
        value(result.baseMatchUnreadableClassifiedUnknown),
        value(result.truncatedPathClassifiedUnknown),
        value(result.baselineUnreadableClassifiedUnknown),
        value(result.foreignBeforeRemoteActionRejected),
        value(result.foreignRemoteActionCountZero),
        result.foreignAuthorizationAttemptCount,
        result.foreignAuthorizationRejectedCount,
        result.foreignRemoteThreadCreateCount,
        result.foreignRemoteThreadTerminalCount,
        value(result.releaseCreateFailureRetainedToken),
        value(result.releaseTokenConsumedOnce),
        result.releaseThreadCreateCount);
    if (formatted <= 0 || static_cast<size_t>(formatted) >= sizeof(content))
        return false;
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const BOOL ok = WriteFile(file, content, static_cast<DWORD>(formatted),
        &written, nullptr);
    const BOOL flushed = ok != FALSE ? FlushFileBuffers(file) : FALSE;
    CloseHandle(file);
    return ok != FALSE && flushed != FALSE &&
        written == static_cast<DWORD>(formatted);
}

struct RestoreFaultLifecycleResult {
    DWORD processId{};
    DWORD moduleBase{};
    bool requested{};
    bool armExportResolved{};
    bool armCallCompleted{};
    bool armIdentityAuthorized{};
    bool armSucceeded{};
    bool firstStopCallCompleted{};
    bool firstStopIdentityAuthorized{};
    bool firstStopRejected{};
    bool firstCanUnloadCallCompleted{};
    bool firstCanUnloadIdentityAuthorized{};
    bool firstCanUnloadRejected{};
    bool firstSnapshotVerified{};
    bool firstSnapshotModulePresent{};
    bool firstSnapshotModuleIdentityMatched{};
    bool noFreeLibraryBeforeRetry{};
    DWORD freeLibraryCallCountAtEntry{};
    DWORD freeLibraryCallCountBeforeRetry{};
    DWORD freeLibraryCallCountDeltaBeforeRetry{};
    DWORD dllExportThreadCountAtEntry{};
    DWORD dllExportThreadCountAtExit{};
    DWORD dllExportThreadCountDelta{};
    DWORD dllExportIdentityRejectDelta{};
    DWORD waitReadyAuthorizationAttemptCount{};
    DWORD waitReadyAuthorizationRejectedCount{};
    DWORD waitReadyRemoteThreadCreateCount{};
    DWORD armAuthorizationAttemptCount{};
    DWORD armAuthorizationRejectedCount{};
    DWORD armRemoteThreadCreateCount{};
    DWORD stopAuthorizationAttemptCount{};
    DWORD stopAuthorizationRejectedCount{};
    DWORD stopRemoteThreadCreateCount{};
    DWORD canUnloadAuthorizationAttemptCount{};
    DWORD canUnloadAuthorizationRejectedCount{};
    DWORD canUnloadRemoteThreadCreateCount{};
    bool cleanupOwnerRetained{};
    bool retryStopCallCompleted{};
    bool retryStopIdentityAuthorized{};
    bool retryStopSucceeded{};
    bool retryCanUnloadCallCompleted{};
    bool retryCanUnloadIdentityAuthorized{};
    bool retryCanUnloadSucceeded{};
    bool freeLibraryThreadCreated{};
    bool freeLibraryThreadCompleted{};
    bool freeLibrarySucceeded{};
    DWORD freeLibraryCallCount{};
    DWORD freeLibraryCallCountAtExit{};
    bool releaseIdentityVerified{};
    bool releaseTokenConsumedOnce{};
    DWORD verifiedAbsentSnapshotCount{};
    bool moduleAbsent{};
    bool targetAliveAfter{};
    bool passed{};
};

bool WriteRestoreFaultLifecycleReport(
    const std::wstring& path,
    const RestoreFaultLifecycleResult& result) {
    if (path.empty()) return false;
    const auto value = [](bool observed) noexcept {
        return observed ? "true" : "false";
    };
    char content[4096]{};
    const int formatted = sprintf_s(content,
        "{\"SchemaId\":\"God2RestoreFaultLifecycle\"," 
        "\"SchemaVersion\":1,\"ProcessId\":%lu,\"ModuleBase\":%lu,"
        "\"Requested\":%s,\"ArmExportResolved\":%s,"
        "\"ArmCallCompleted\":%s,\"ArmIdentityAuthorized\":%s,"
        "\"ArmSucceeded\":%s,"
        "\"FirstStopCallCompleted\":%s,\"FirstStopRejected\":%s,"
        "\"FirstStopIdentityAuthorized\":%s,"
        "\"FirstCanUnloadCallCompleted\":%s,"
        "\"FirstCanUnloadIdentityAuthorized\":%s,"
        "\"FirstCanUnloadRejected\":%s,"
        "\"FirstSnapshotVerified\":%s,"
        "\"FirstSnapshotModulePresent\":%s,"
        "\"FirstSnapshotModuleIdentityMatched\":%s,"
        "\"NoFreeLibraryBeforeRetry\":%s,"
        "\"FreeLibraryCallCountAtEntry\":%lu,"
        "\"FreeLibraryCallCountBeforeRetry\":%lu,"
        "\"FreeLibraryCallCountDeltaBeforeRetry\":%lu,"
        "\"DllExportThreadCountAtEntry\":%lu,"
        "\"DllExportThreadCountAtExit\":%lu,"
        "\"DllExportThreadCountDelta\":%lu,"
        "\"DllExportIdentityRejectDelta\":%lu,"
        "\"WaitReadyAuthorizationAttemptCount\":%lu,"
        "\"WaitReadyAuthorizationRejectedCount\":%lu,"
        "\"WaitReadyRemoteThreadCreateCount\":%lu,"
        "\"ArmAuthorizationAttemptCount\":%lu,"
        "\"ArmAuthorizationRejectedCount\":%lu,"
        "\"ArmRemoteThreadCreateCount\":%lu,"
        "\"StopAuthorizationAttemptCount\":%lu,"
        "\"StopAuthorizationRejectedCount\":%lu,"
        "\"StopRemoteThreadCreateCount\":%lu,"
        "\"CanUnloadAuthorizationAttemptCount\":%lu,"
        "\"CanUnloadAuthorizationRejectedCount\":%lu,"
        "\"CanUnloadRemoteThreadCreateCount\":%lu,"
        "\"CleanupOwnerRetained\":%s,"
        "\"RetryStopCallCompleted\":%s,\"RetryStopSucceeded\":%s,"
        "\"RetryStopIdentityAuthorized\":%s,"
        "\"RetryCanUnloadCallCompleted\":%s,"
        "\"RetryCanUnloadIdentityAuthorized\":%s,"
        "\"RetryCanUnloadSucceeded\":%s,"
        "\"FreeLibraryThreadCreated\":%s,"
        "\"FreeLibraryThreadCompleted\":%s,"
        "\"FreeLibrarySucceeded\":%s,\"FreeLibraryCallCount\":%lu,"
        "\"FreeLibraryCallCountAtExit\":%lu,"
        "\"ReleaseIdentityVerified\":%s,"
        "\"ReleaseTokenConsumedOnce\":%s,"
        "\"VerifiedAbsentSnapshotCount\":%lu,\"ModuleAbsent\":%s,"
        "\"TargetAliveAfter\":%s,\"Passed\":%s}\n",
        result.processId, result.moduleBase, value(result.requested),
        value(result.armExportResolved), value(result.armCallCompleted),
        value(result.armIdentityAuthorized),
        value(result.armSucceeded), value(result.firstStopCallCompleted),
        value(result.firstStopRejected),
        value(result.firstStopIdentityAuthorized),
        value(result.firstCanUnloadCallCompleted),
        value(result.firstCanUnloadIdentityAuthorized),
        value(result.firstCanUnloadRejected),
        value(result.firstSnapshotVerified),
        value(result.firstSnapshotModulePresent),
        value(result.firstSnapshotModuleIdentityMatched),
        value(result.noFreeLibraryBeforeRetry),
        result.freeLibraryCallCountAtEntry,
        result.freeLibraryCallCountBeforeRetry,
        result.freeLibraryCallCountDeltaBeforeRetry,
        result.dllExportThreadCountAtEntry,
        result.dllExportThreadCountAtExit,
        result.dllExportThreadCountDelta,
        result.dllExportIdentityRejectDelta,
        result.waitReadyAuthorizationAttemptCount,
        result.waitReadyAuthorizationRejectedCount,
        result.waitReadyRemoteThreadCreateCount,
        result.armAuthorizationAttemptCount,
        result.armAuthorizationRejectedCount,
        result.armRemoteThreadCreateCount,
        result.stopAuthorizationAttemptCount,
        result.stopAuthorizationRejectedCount,
        result.stopRemoteThreadCreateCount,
        result.canUnloadAuthorizationAttemptCount,
        result.canUnloadAuthorizationRejectedCount,
        result.canUnloadRemoteThreadCreateCount,
        value(result.cleanupOwnerRetained),
        value(result.retryStopCallCompleted),
        value(result.retryStopSucceeded),
        value(result.retryStopIdentityAuthorized),
        value(result.retryCanUnloadCallCompleted),
        value(result.retryCanUnloadIdentityAuthorized),
        value(result.retryCanUnloadSucceeded),
        value(result.freeLibraryThreadCreated),
        value(result.freeLibraryThreadCompleted),
        value(result.freeLibrarySucceeded), result.freeLibraryCallCount,
        result.freeLibraryCallCountAtExit,
        value(result.releaseIdentityVerified),
        value(result.releaseTokenConsumedOnce),
        result.verifiedAbsentSnapshotCount, value(result.moduleAbsent),
        value(result.targetAliveAfter), value(result.passed));
    if (formatted <= 0 || static_cast<size_t>(formatted) >= sizeof(content))
        return false;
    wchar_t temporary[MAX_PATH * 4]{};
    if (swprintf_s(temporary, L"%s.tmp-%lu-%lu", path.c_str(),
            GetCurrentProcessId(), GetCurrentThreadId()) <= 0) return false;
    HANDLE file = CreateFileW(temporary, GENERIC_WRITE, 0, nullptr,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const DWORD length = static_cast<DWORD>(formatted);
    const BOOL wrote = WriteFile(file, content, length, &written, nullptr);
    const BOOL flushed = wrote != FALSE ? FlushFileBuffers(file) : FALSE;
    CloseHandle(file);
    if (wrote == FALSE || flushed == FALSE || written != length ||
        MoveFileExW(temporary, path.c_str(), MOVEFILE_REPLACE_EXISTING |
                    MOVEFILE_WRITE_THROUGH) == FALSE) {
        DeleteFileW(temporary);
        return false;
    }
    return true;
}

std::wstring FullPath(const std::wstring& path) {
    DWORD required = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
    if (required == 0) return {};
    std::wstring result(required, L'\0');
    DWORD written = GetFullPathNameW(path.c_str(), required, &result[0], nullptr);
    if (written == 0 || written >= required) return {};
    result.resize(written);
    return result;
}

bool IsExactTarget(HANDLE process, const std::wstring& expected) {
    const std::wstring expected_full = FullPath(expected);
    if (expected_full.empty() || _wcsicmp(expected_full.substr(expected_full.find_last_of(L"\\/") + 1).c_str(),
                                          L"God2_opt.exe") != 0) return false;
    std::wstring actual(32768, L'\0');
    DWORD length = static_cast<DWORD>(actual.size());
    if (!QueryFullProcessImageNameW(process, 0, &actual[0], &length)) return false;
    actual.resize(length);
    const std::wstring actual_full = FullPath(actual);
    return !actual_full.empty() && _wcsicmp(actual_full.c_str(), expected_full.c_str()) == 0;
}

bool IsX86(HANDLE process) {
    using IsWow64Process2Fn = BOOL(WINAPI*)(HANDLE, USHORT*, USHORT*);
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    if (kernel32 == nullptr) return false;
    auto function = reinterpret_cast<IsWow64Process2Fn>(
        GetProcAddress(kernel32, "IsWow64Process2"));
    if (function != nullptr) {
        USHORT process_machine = IMAGE_FILE_MACHINE_UNKNOWN;
        USHORT native_machine = IMAGE_FILE_MACHINE_UNKNOWN;
        return function(process, &process_machine, &native_machine) != FALSE &&
               (process_machine == IMAGE_FILE_MACHINE_I386 ||
                (process_machine == IMAGE_FILE_MACHINE_UNKNOWN && native_machine == IMAGE_FILE_MACHINE_I386));
    }
    BOOL wow64 = FALSE;
    if (IsWow64Process(process, &wow64) == FALSE) return false;
    if (wow64 != FALSE) return true;
    SYSTEM_INFO native{};
    GetNativeSystemInfo(&native);
    return native.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_INTEL;
}

bool Sha256File(const std::wstring& path, std::wstring* digest) {
    if (digest == nullptr) return false;
    constexpr DWORD kReadBufferBytes = 32u * 1024u;
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash_handle = nullptr;
    HANDLE file = INVALID_HANDLE_VALUE;
    UCHAR* read_buffer = nullptr;
    std::vector<UCHAR> hash_object;
    std::vector<UCHAR> hash_bytes;
    bool success = false;
    do {
        if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(
                &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0))) break;
        DWORD object_bytes = 0;
        DWORD hash_length = 0;
        DWORD property_bytes = 0;
        if (!BCRYPT_SUCCESS(BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
                reinterpret_cast<PUCHAR>(&object_bytes), sizeof(object_bytes), &property_bytes, 0)) ||
            property_bytes != sizeof(object_bytes) || object_bytes == 0) break;
        if (!BCRYPT_SUCCESS(BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH,
                reinterpret_cast<PUCHAR>(&hash_length), sizeof(hash_length), &property_bytes, 0)) ||
            property_bytes != sizeof(hash_length) || hash_length != 32) break;
        hash_object.resize(object_bytes);
        hash_bytes.resize(hash_length);
        if (!BCRYPT_SUCCESS(BCryptCreateHash(algorithm, &hash_handle,
                hash_object.data(), object_bytes, nullptr, 0, 0))) break;
        file = CreateFileW(path.c_str(), GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        if (file == INVALID_HANDLE_VALUE) break;
        read_buffer = static_cast<UCHAR*>(HeapAlloc(
            GetProcessHeap(), 0, kReadBufferBytes));
        if (read_buffer == nullptr) break;
        for (;;) {
            DWORD read = 0;
            if (!ReadFile(file, read_buffer, kReadBufferBytes, &read, nullptr)) break;
            if (read == 0) {
                if (!BCRYPT_SUCCESS(BCryptFinishHash(
                        hash_handle, hash_bytes.data(), hash_length, 0))) break;
                static constexpr wchar_t digits[] = L"0123456789ABCDEF";
                std::wstring value;
                value.reserve(hash_bytes.size() * 2);
                for (const UCHAR byte : hash_bytes) {
                    value.push_back(digits[byte >> 4]);
                    value.push_back(digits[byte & 0x0f]);
                }
                *digest = std::move(value);
                success = true;
                break;
            }
            if (!BCRYPT_SUCCESS(BCryptHashData(hash_handle, read_buffer, read, 0))) break;
        }
    } while (false);
    if (read_buffer != nullptr) HeapFree(GetProcessHeap(), 0, read_buffer);
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    if (hash_handle != nullptr) BCryptDestroyHash(hash_handle);
    if (algorithm != nullptr) BCryptCloseAlgorithmProvider(algorithm, 0);
    return success;
}

std::wstring FileVersionText(const std::wstring& path) {
    DWORD ignored = 0;
    const DWORD size = GetFileVersionInfoSizeW(path.c_str(), &ignored);
    if (size == 0) return {};
    std::vector<BYTE> data(size);
    if (!GetFileVersionInfoW(path.c_str(), 0, size, data.data())) return {};
    VS_FIXEDFILEINFO* fixed = nullptr;
    UINT fixed_size = 0;
    if (!VerQueryValueW(data.data(), L"\\", reinterpret_cast<void**>(&fixed), &fixed_size) ||
        fixed == nullptr || fixed_size < sizeof(VS_FIXEDFILEINFO) || fixed->dwSignature != 0xFEEF04BD) return {};
    return std::to_wstring(HIWORD(fixed->dwFileVersionMS)) + L"." +
        std::to_wstring(LOWORD(fixed->dwFileVersionMS)) + L"." +
        std::to_wstring(HIWORD(fixed->dwFileVersionLS)) + L"." +
        std::to_wstring(LOWORD(fixed->dwFileVersionLS));
}

bool IsExactTargetBuild(HANDLE process, const std::wstring& expected) {
    if (!IsExactTarget(process, expected) || !IsX86(process)) return false;
    FILETIME created_before{}, exited{}, kernel{}, user{};
    if (!GetProcessTimes(process, &created_before, &exited, &kernel, &user)) return false;
    std::wstring actual(32768, L'\0');
    DWORD length = static_cast<DWORD>(actual.size());
    if (!QueryFullProcessImageNameW(process, 0, &actual[0], &length)) return false;
    actual.resize(length);
    const std::wstring actual_full = FullPath(actual);
    std::wstring digest;
    if (actual_full.empty() || !Sha256File(actual_full, &digest) ||
        _wcsicmp(digest.c_str(), kExpectedTargetSha256) != 0 ||
        FileVersionText(actual_full) != kExpectedTargetVersion) return false;
    FILETIME created_after{};
    if (!GetProcessTimes(process, &created_after, &exited, &kernel, &user) ||
        CompareFileTime(&created_before, &created_after) != 0) return false;
    std::wstring actual_after(32768, L'\0');
    length = static_cast<DWORD>(actual_after.size());
    if (!QueryFullProcessImageNameW(process, 0, &actual_after[0], &length)) return false;
    actual_after.resize(length);
    const std::wstring actual_after_full = FullPath(actual_after);
    return !actual_after_full.empty() && _wcsicmp(actual_full.c_str(), actual_after_full.c_str()) == 0;
}

bool IsX86PeFile(const std::wstring& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    IMAGE_DOS_HEADER dos{};
    DWORD read = 0;
    bool valid = ReadFile(file, &dos, sizeof(dos), &read, nullptr) != FALSE && read == sizeof(dos) &&
                 dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew > 0;
    if (valid) {
        LARGE_INTEGER offset{};
        offset.QuadPart = dos.e_lfanew;
        valid = SetFilePointerEx(file, offset, nullptr, FILE_BEGIN) != FALSE;
    }
    DWORD signature = 0;
    IMAGE_FILE_HEADER header{};
    if (valid) valid = ReadFile(file, &signature, sizeof(signature), &read, nullptr) != FALSE &&
                       read == sizeof(signature) && signature == IMAGE_NT_SIGNATURE;
    if (valid) valid = ReadFile(file, &header, sizeof(header), &read, nullptr) != FALSE &&
                       read == sizeof(header) && header.Machine == IMAGE_FILE_MACHINE_I386;
    CloseHandle(file);
    return valid;
}

int SelfTestPayload(int argc, wchar_t** argv, const std::wstring& result_path) {
    const std::wstring dll = FullPath(Arg(argc, argv, L"--dll"));
    std::vector<wchar_t> injector_path(32768, L'\0');
    const DWORD injector_length = GetModuleFileNameW(
        nullptr, injector_path.data(), static_cast<DWORD>(injector_path.size()));
    if (dll.empty() || injector_length == 0 || injector_length >= injector_path.size()) {
        WriteResult(result_path, "SELF_TEST_INVALID_ARGUMENT", ERROR_INVALID_PARAMETER,
                    "requiresExplicitDll=true");
        return ERROR_INVALID_PARAMETER;
    }
    injector_path.resize(injector_length);
    if (!IsX86PeFile(injector_path.data()) || !IsX86PeFile(dll)) {
        WriteResult(result_path, "SELF_TEST_ARCHITECTURE_FAILED", ERROR_BAD_EXE_FORMAT,
                    "injectorAndDllMustBeX86=true");
        return ERROR_BAD_EXE_FORMAT;
    }

    // Map the exact extracted DLL without running DllMain.  This makes the
    // structural self-test executable and verifies the lifecycle handshakes plus semantic contract test that
    // the real monitor path resolves remotely; it deliberately does not claim
    // that a target process was injected.
    HMODULE probe = LoadLibraryExW(dll.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (probe == nullptr) {
        const DWORD failure = GetLastError();
        WriteResult(result_path, "SELF_TEST_DLL_MAP_FAILED", failure);
        return static_cast<int>(failure == ERROR_SUCCESS ? ERROR_DLL_INIT_FAILED : failure);
    }
    const bool has_ready = GetProcAddress(probe, "God2TraceProbeWaitReady") != nullptr;
    const bool has_stop = GetProcAddress(probe, "God2TraceProbeStop") != nullptr;
    const bool has_can_unload = GetProcAddress(probe, "God2TraceProbeCanUnload") != nullptr;
    const bool has_semantic_self_test = GetProcAddress(probe, "God2TraceProbeSemanticSelfTest") != nullptr;
    const bool has_shared_transport_self_test =
        GetProcAddress(probe, "God2TraceProbeSharedTransportSelfTest") != nullptr;
    const bool has_restore_fault_self_test =
        GetProcAddress(probe,
            "God2TraceProbeArmRestoreFaultSelfTest") != nullptr;
    FreeLibrary(probe);
    if (!has_ready || !has_stop || !has_can_unload || !has_semantic_self_test ||
        !has_shared_transport_self_test || !has_restore_fault_self_test) {
        WriteResult(result_path, "SELF_TEST_EXPORTS_FAILED", ERROR_PROC_NOT_FOUND,
                    "requiredExports=God2TraceProbeWaitReady,God2TraceProbeStop,God2TraceProbeCanUnload,God2TraceProbeSemanticSelfTest,God2TraceProbeSharedTransportSelfTest,God2TraceProbeArmRestoreFaultSelfTest");
        return ERROR_PROC_NOT_FOUND;
    }
    if (HasArg(argc, argv, L"--owned-remote-timeout-self-test")) {
        OwnedRemoteThreadSelfTestResult owned{};
        const std::wstring owned_report = Arg(
            argc, argv, L"--owned-remote-report");
        if (!RunOwnedRemoteThreadSelfTest(&owned) ||
            !WriteOwnedRemoteThreadSelfTestReport(owned_report, owned)) {
            WriteResult(result_path, "SELF_TEST_OWNED_REMOTE_THREAD_FAILED",
                        ERROR_TIMEOUT);
            return ERROR_TIMEOUT;
        }
    }
    InjectorResultRecord structural{};
    structural.status = "PASS_X86_PAYLOAD_STRUCTURE";
    structural.code = 0;
    // Mapping exports in this process is structural evidence only.  It is not
    // a target-process module snapshot and therefore cannot assert absence or
    // strict unload.
    return WriteResult(result_path, structural) ? 0 : ERROR_WRITE_FAULT;
}

DWORD RemoteKernel32Export(DWORD process_id, const char* export_name) {
    HMODULE local_kernel32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC local_export = local_kernel32 != nullptr ? GetProcAddress(local_kernel32, export_name) : nullptr;
    if (local_export == nullptr) return 0;

    // Forwarded Kernel32 exports can resolve into KernelBase.  Identify the
    // module that actually owns the function before calculating the RVA.
    HMODULE export_module = nullptr;
    if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                          GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                          reinterpret_cast<LPCWSTR>(local_export), &export_module) == FALSE ||
        export_module == nullptr) return 0;
    wchar_t module_path[MAX_PATH]{};
    if (GetModuleFileNameW(export_module, module_path, ARRAYSIZE(module_path)) == 0) return 0;
    const wchar_t* module_name = wcsrchr(module_path, L'\\');
    module_name = module_name != nullptr ? module_name + 1 : module_path;
    const uintptr_t export_rva = reinterpret_cast<uintptr_t>(local_export) -
        reinterpret_cast<uintptr_t>(export_module);

    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, process_id);
    if (snapshot == INVALID_HANDLE_VALUE) return 0;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    DWORD result = 0;
    if (Module32FirstW(snapshot, &entry)) {
        do {
            if (_wcsicmp(entry.szModule, module_name) != 0) continue;
            const uintptr_t remote_base = reinterpret_cast<uintptr_t>(entry.modBaseAddr);
            if (export_rva <= (std::numeric_limits<DWORD>::max)() - remote_base)
                result = static_cast<DWORD>(remote_base + export_rva);
            break;
        } while (Module32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return result;
}

class SuspendedPrimaryThread final {
public:
    explicit SuspendedPrimaryThread(DWORD process_id) : process_id_(process_id) {}
    ~SuspendedPrimaryThread() {
        // This object owns exactly one suspend count. Never abandon it after a
        // transient ResumeThread failure; keep retrying until the thread is
        // resumed or its signaled handle proves it has exited.
        DWORD ignored = ERROR_SUCCESS;
        while (thread_ != nullptr && !Resume(&ignored)) Sleep(100);
    }

    bool Suspend(DWORD* error) {
        DWORD last_failure = ERROR_NOT_FOUND;
        // Process discovery can race the first thread becoming visible in the
        // system thread snapshot. Retry briefly instead of treating that normal
        // startup window as a permanent injection failure.
        for (int attempt = 0; attempt < 40; ++attempt) {
            HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
            if (snapshot == INVALID_HANDLE_VALUE) {
                last_failure = GetLastError();
                Sleep(50);
                continue;
            }

            THREADENTRY32 entry{};
            entry.dwSize = sizeof(entry);
            uint64_t earliest_creation = (std::numeric_limits<uint64_t>::max)();
            HANDLE earliest_thread = nullptr;
            DWORD earliest_thread_id = 0;
            if (Thread32First(snapshot, &entry)) {
                do {
                    if (entry.th32OwnerProcessID != process_id_) continue;
                    HANDLE candidate = OpenThread(THREAD_SUSPEND_RESUME | THREAD_QUERY_INFORMATION,
                                                  FALSE, entry.th32ThreadID);
                    if (candidate == nullptr) continue; // It may have exited after the snapshot.
                    FILETIME creation{}, exit{}, kernel{}, user{};
                    if (GetThreadTimes(candidate, &creation, &exit, &kernel, &user) == FALSE) {
                        CloseHandle(candidate);
                        continue;
                    }
                    ULARGE_INTEGER value{};
                    value.LowPart = creation.dwLowDateTime;
                    value.HighPart = creation.dwHighDateTime;
                    if (value.QuadPart < earliest_creation) {
                        if (earliest_thread != nullptr) CloseHandle(earliest_thread);
                        earliest_creation = value.QuadPart;
                        earliest_thread = candidate;
                        earliest_thread_id = entry.th32ThreadID;
                    } else {
                        CloseHandle(candidate);
                    }
                } while (Thread32Next(snapshot, &entry));
            }
            CloseHandle(snapshot);
            if (earliest_thread == nullptr) {
                Sleep(50);
                continue;
            }
            if (SuspendThread(earliest_thread) == static_cast<DWORD>(-1)) {
                last_failure = GetLastError();
                CloseHandle(earliest_thread);
                Sleep(50);
                continue;
            }
            thread_ = earliest_thread;
            thread_id_ = earliest_thread_id;
            if (error != nullptr) *error = ERROR_SUCCESS;
            return true;
        }
        return Fail(last_failure, error);
    }

    bool Resume(DWORD* error = nullptr) noexcept {
        if (thread_ == nullptr) {
            if (error != nullptr) *error = ERROR_SUCCESS;
            return true;
        }
        const DWORD previous_count = ResumeThread(thread_);
        DWORD failure = previous_count == static_cast<DWORD>(-1) ? GetLastError() : ERROR_SUCCESS;
        if (failure != ERROR_SUCCESS) {
            DWORD exit_code = STILL_ACTIVE;
            if (GetExitCodeThread(thread_, &exit_code) != FALSE && exit_code != STILL_ACTIVE)
                failure = ERROR_SUCCESS;
        }
        // A failed ResumeThread is not proof that our suspend count was
        // released. Retain the handle and ownership so the monitor can retry;
        // closing it here could leave the target frozen with no cleanup owner.
        if (failure == ERROR_SUCCESS) {
            CloseHandle(thread_);
            thread_ = nullptr;
            thread_id_ = 0;
        }
        if (error != nullptr) *error = failure;
        return failure == ERROR_SUCCESS;
    }

    size_t Count() const noexcept { return thread_ != nullptr ? 1u : 0u; }
    DWORD ThreadId() const noexcept { return thread_id_; }

private:
    static bool Fail(DWORD failure, DWORD* error) {
        if (error != nullptr) *error = failure;
        return false;
    }

    DWORD process_id_{};
    DWORD thread_id_{};
    HANDLE thread_{};
};

struct InjectionResult {
    DWORD module{};
    DWORD error{ERROR_SUCCESS};
    bool loadStateVerified{};
};

struct OwnedRemoteWaitFaults {
    volatile LONG waitFailedRemaining{};
    volatile LONG getExitCodeFailedRemaining{};
};

struct OwnedRemoteActionResult {
    bool terminal{};
    bool processExited{};
    bool exitCodeKnown{};
    DWORD exitCode{};
    DWORD error{ERROR_SUCCESS};
    DWORD transientWaitFailures{};
    DWORD transientExitCodeFailures{};
};

bool ConsumeInjectedFailure(volatile LONG* remaining) noexcept {
    if (remaining == nullptr) return false;
    LONG observed = InterlockedCompareExchange(remaining, 0, 0);
    while (observed > 0) {
        const LONG prior = InterlockedCompareExchange(
            remaining, observed - 1, observed);
        if (prior == observed) return true;
        observed = prior;
    }
    return false;
}

OwnedRemoteActionResult WaitForOwnedRemoteThread(
    HANDLE process, HANDLE thread, DWORD initial_timeout_ms,
    OwnedRemoteWaitFaults* faults = nullptr) noexcept {
    OwnedRemoteActionResult result{};
    if (thread == nullptr) {
        result.error = ERROR_INVALID_HANDLE;
        return result;
    }
    DWORD timeout = initial_timeout_ms;
    for (;;) {
        DWORD wait = WAIT_FAILED;
        if (ConsumeInjectedFailure(faults != nullptr ?
                &faults->waitFailedRemaining : nullptr)) {
            SetLastError(ERROR_INVALID_HANDLE);
            ++result.transientWaitFailures;
        } else {
            wait = WaitForSingleObject(thread, timeout);
        }
        timeout = 1'000;
        if (wait == WAIT_OBJECT_0) {
            result.terminal = true;
            for (;;) {
                DWORD observed = 0;
                BOOL queried = FALSE;
                if (ConsumeInjectedFailure(faults != nullptr ?
                        &faults->getExitCodeFailedRemaining : nullptr)) {
                    SetLastError(ERROR_INVALID_DATA);
                    ++result.transientExitCodeFailures;
                } else {
                    queried = GetExitCodeThread(thread, &observed);
                }
                if (queried != FALSE) {
                    result.exitCodeKnown = true;
                    result.exitCode = observed;
                    result.error = ERROR_SUCCESS;
                    return result;
                }
                result.error = GetLastError();
                // The thread is already terminal. Retain its owned handle and
                // retry the outcome query; never repeat the remote action.
                if (process != nullptr &&
                    WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
                    result.processExited = true;
                    result.error = ERROR_PROCESS_ABORTED;
                    return result;
                }
                Sleep(1);
            }
        }
        if (process != nullptr &&
            WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
            result.terminal = true;
            result.processExited = true;
            result.error = ERROR_PROCESS_ABORTED;
            return result;
        }
        if (wait == WAIT_TIMEOUT) continue;
        // WAIT_FAILED (including an injected one) or another non-object result
        // is not terminal evidence. Retain the handle and retry in a bounded
        // slice while the target remains alive.
        result.error = GetLastError();
        Sleep(1);
    }
}

DWORD WINAPI DelayedLoadOwnedThread(void* parameter) {
    const DWORD delay = parameter != nullptr ?
        *static_cast<const DWORD*>(parameter) : 0u;
    Sleep(delay);
    return static_cast<DWORD>(reinterpret_cast<uintptr_t>(
        LoadLibraryW(L"winmm.dll")));
}

bool RunOwnedRemoteThreadSelfTest(OwnedRemoteThreadSelfTestResult* result) {
    if (result == nullptr) return false;
    *result = OwnedRemoteThreadSelfTestResult{};
    result->initialTimeoutMs = 50u;
    result->requestedDelayMs = 21'000u;
    DWORD delay = result->requestedDelayMs;
    HANDLE thread = CreateRemoteThread(GetCurrentProcess(), nullptr, 0,
        &DelayedLoadOwnedThread, &delay, 0, nullptr);
    if (thread == nullptr) return false;
    const ULONGLONG started = GetTickCount64();
    result->initialTimeoutObserved =
        WaitForSingleObject(thread, result->initialTimeoutMs) == WAIT_TIMEOUT;
    OwnedRemoteWaitFaults faults{};
    faults.waitFailedRemaining = 1;
    faults.getExitCodeFailedRemaining = 1;
    const OwnedRemoteActionResult action = WaitForOwnedRemoteThread(
        GetCurrentProcess(), thread, 0u, &faults);
    result->elapsedMs = GetTickCount64() - started;
    result->threadSignaled = action.terminal && !action.processExited;
    result->handleRetainedUntilSignal = result->initialTimeoutObserved &&
        result->threadSignaled && result->elapsedMs >= result->requestedDelayMs;
    result->waitFailedInjected = action.transientWaitFailures == 1u;
    result->waitFailedOwnerRetained = result->waitFailedInjected &&
        result->threadSignaled && result->handleRetainedUntilSignal;
    result->getExitCodeFailureInjected =
        action.transientExitCodeFailures == 1u;
    result->getExitCodeOwnerRetained =
        result->getExitCodeFailureInjected && action.terminal;
    result->terminalBeforeHandleRelease = action.terminal;
    result->exitCodeKnownAfterRetry = action.exitCodeKnown;
    result->delayedLoadCompleted = action.exitCodeKnown &&
        action.exitCode != 0 && action.error == ERROR_SUCCESS;
    result->moduleReferenceReleased = result->delayedLoadCompleted &&
        FreeLibrary(reinterpret_cast<HMODULE>(
            static_cast<uintptr_t>(action.exitCode))) != FALSE;
    CloseHandle(thread);
    const bool ownership_state_passed =
        RunInjectorOwnershipStateSelfTests(result);
    return result->initialTimeoutObserved && result->threadSignaled &&
        result->handleRetainedUntilSignal && result->delayedLoadCompleted &&
        result->moduleReferenceReleased && result->waitFailedInjected &&
        result->waitFailedOwnerRetained &&
        result->getExitCodeFailureInjected &&
        result->getExitCodeOwnerRetained &&
        result->terminalBeforeHandleRelease &&
        result->exitCodeKnownAfterRetry && ownership_state_passed;
}

InjectionResult Inject(HANDLE process, DWORD process_id, const std::wstring& dll,
                       SuspendedPrimaryThread* suspended, bool* resumed_before_completion) {
    if (resumed_before_completion != nullptr) *resumed_before_completion = false;
    if (dll.size() >= ((std::numeric_limits<SIZE_T>::max)() / sizeof(wchar_t)))
        return {0, ERROR_BUFFER_OVERFLOW};
    const SIZE_T bytes = static_cast<SIZE_T>((dll.size() + 1) * sizeof(wchar_t));
    void* remote = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (remote == nullptr) return {0, GetLastError()};
    SIZE_T written = 0;
    if (!WriteProcessMemory(process, remote, dll.c_str(), bytes, &written) || written != bytes) {
        const DWORD write_error = GetLastError();
        const DWORD error = write_error == ERROR_SUCCESS ? ERROR_WRITE_FAULT : write_error;
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return {0, error};
    }
    const DWORD load_library_address = RemoteKernel32Export(process_id, "LoadLibraryW");
    if (load_library_address == 0) {
        const DWORD error = GetLastError() == ERROR_SUCCESS ? ERROR_PROC_NOT_FOUND : GetLastError();
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return {0, error};
    }
    auto load_library = reinterpret_cast<LPTHREAD_START_ROUTINE>(
        static_cast<uintptr_t>(load_library_address));
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, load_library, remote, 0, nullptr);
    if (thread == nullptr) {
        const DWORD error = GetLastError();
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        return {0, error};
    }
    const DWORD initial_wait = WaitForSingleObject(thread, 3'000);
    if (initial_wait != WAIT_OBJECT_0 && suspended != nullptr) {
        // A thread caught while it owns the loader lock can prevent the remote
        // LoadLibraryW from completing.  Never leave the game frozen: release
        // our one suspend count, then retain the already-created remote thread
        // and path allocation until the action is terminal.
        DWORD resume_error = ERROR_SUCCESS;
        while (!suspended->Resume(&resume_error)) {
            if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) break;
            Sleep(100);
        }
        if (resume_error != ERROR_SUCCESS &&
            WaitForSingleObject(process, 0) != WAIT_OBJECT_0) {
            const OwnedRemoteActionResult terminal =
                WaitForOwnedRemoteThread(process, thread, 1'000);
            CloseHandle(thread);
            if (!terminal.processExited)
                VirtualFreeEx(process, remote, 0, MEM_RELEASE);
            if (terminal.processExited)
                return {0, ERROR_PROCESS_ABORTED, false};
            if (terminal.exitCodeKnown) {
                return {terminal.exitCode,
                    terminal.exitCode == 0 ? ERROR_DLL_INIT_FAILED :
                        resume_error, true};
            }
            return {0, resume_error, false};
        }
        if (resumed_before_completion != nullptr) *resumed_before_completion = true;
    }
    const OwnedRemoteActionResult action = WaitForOwnedRemoteThread(
        process, thread, initial_wait == WAIT_OBJECT_0 ? 0u : 1'000u);
    CloseHandle(thread);
    // The path is released only after terminal thread/process evidence.
    if (!action.processExited)
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    if (action.processExited)
        return {0, ERROR_PROCESS_ABORTED, false};
    if (!action.exitCodeKnown)
        return {0, action.error == ERROR_SUCCESS ? ERROR_GEN_FAILURE :
            action.error, false};
    const DWORD error = action.exitCode == 0 ? ERROR_DLL_INIT_FAILED :
        ERROR_SUCCESS;
    return {action.exitCode, error, true};
}

DWORD RemoteExport(DWORD remote_module, const std::wstring& dll, const char* name) {
    HMODULE local = LoadLibraryExW(dll.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (local == nullptr) return 0;
    FARPROC exported = GetProcAddress(local, name);
    if (exported == nullptr) {
        FreeLibrary(local);
        return 0;
    }
    const uintptr_t offset = reinterpret_cast<uintptr_t>(exported) - reinterpret_cast<uintptr_t>(local);
    FreeLibrary(local);
    if (offset > (std::numeric_limits<DWORD>::max)() - remote_module) return 0;
    return static_cast<DWORD>(remote_module + offset);
}

bool RemoteCall(HANDLE process, DWORD address, void* parameter, DWORD* result,
                DWORD* error = nullptr, DWORD timeout_ms = 20'000,
                bool* thread_created = nullptr,
                bool* thread_completed = nullptr) {
    if (error != nullptr) *error = ERROR_SUCCESS;
    if (thread_created != nullptr) *thread_created = false;
    if (thread_completed != nullptr) *thread_completed = false;
    if (address == 0) {
        if (error != nullptr) *error = ERROR_PROC_NOT_FOUND;
        return false;
    }
    HANDLE thread = CreateRemoteThread(process, nullptr, 0,
        reinterpret_cast<LPTHREAD_START_ROUTINE>(static_cast<uintptr_t>(address)), parameter, 0, nullptr);
    if (thread == nullptr) {
        if (error != nullptr) *error = GetLastError();
        return false;
    }
    if (thread_created != nullptr) *thread_created = true;
    const OwnedRemoteActionResult action = WaitForOwnedRemoteThread(
        process, thread, timeout_ms);
    if (thread_completed != nullptr) *thread_completed = action.terminal;
    const bool ok = action.terminal && !action.processExited &&
        action.exitCodeKnown;
    if (!ok && error != nullptr) *error = action.error;
    CloseHandle(thread);
    if (result != nullptr) *result = action.exitCode;
    return ok;
}

struct ProbeStopResult {
    bool stopped{};
    bool stopIdentityAuthorized{};
    bool unloadSafe{};
    bool canUnloadIdentityAuthorized{};
    bool unloaded{};
    bool moduleSnapshotVerified{};
    bool moduleAbsent{};
    bool freeLibraryThreadCreated{};
    bool freeLibraryThreadCompleted{};
    bool referenceReleased{};
    bool releaseIdentityVerified{};
    bool releaseTokenConsumed{};
    bool releaseOutcomeKnown{};
    DWORD error{ERROR_SUCCESS};
};

struct StableFileIdentity {
    DWORD volumeSerial{};
    DWORD fileIndexHigh{};
    DWORD fileIndexLow{};
    DWORD fileSizeHigh{};
    DWORD fileSizeLow{};
    bool valid{};
};

bool SameFileIdentity(const StableFileIdentity& left,
                      const StableFileIdentity& right) noexcept {
    return left.valid && right.valid &&
        left.volumeSerial == right.volumeSerial &&
        left.fileIndexHigh == right.fileIndexHigh &&
        left.fileIndexLow == right.fileIndexLow &&
        left.fileSizeHigh == right.fileSizeHigh &&
        left.fileSizeLow == right.fileSizeLow;
}

bool ReadStableFileIdentity(const std::wstring& path,
                            StableFileIdentity* identity) {
    if (identity == nullptr) return false;
    *identity = StableFileIdentity{};
    HANDLE file = CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    BY_HANDLE_FILE_INFORMATION information{};
    const BOOL queried = GetFileInformationByHandle(file, &information);
    CloseHandle(file);
    if (queried == FALSE ||
        (information.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        return false;
    identity->volumeSerial = information.dwVolumeSerialNumber;
    identity->fileIndexHigh = information.nFileIndexHigh;
    identity->fileIndexLow = information.nFileIndexLow;
    identity->fileSizeHigh = information.nFileSizeHigh;
    identity->fileSizeLow = information.nFileSizeLow;
    identity->valid = true;
    return true;
}

enum class RemoteModulePresence : uint32_t {
    Unknown = 0,
    Absent = 1,
    ExpectedPathPresent = 2,
};

struct RemoteModuleSnapshot {
    RemoteModulePresence presence{RemoteModulePresence::Unknown};
    bool enumerationComplete{};
    bool expectedIdentityVerified{};
    bool exactBase{};
    bool foreignAtExpectedBase{};
    DWORD observedBase{};
};

struct ModuleIdentityObservation {
    DWORD base{};
    std::wstring moduleName;
    std::wstring path;
    StableFileIdentity identity{};
    bool moduleNameVerified{};
    bool pathVerified{};
    bool identityVerified{};
};

RemoteModuleSnapshot ClassifyModuleObservations(
    DWORD expected_base, const std::wstring& expected_path,
    const StableFileIdentity& expected_identity,
    const std::vector<ModuleIdentityObservation>& observations,
    bool enumeration_complete) {
    RemoteModuleSnapshot result{};
    result.enumerationComplete = enumeration_complete;
    result.expectedIdentityVerified = expected_identity.valid;
    if (!enumeration_complete || expected_path.empty() ||
        !expected_identity.valid) return result;

    const size_t expected_separator = expected_path.find_last_of(L"\\/");
    const std::wstring expected_name = expected_separator ==
        std::wstring::npos ? expected_path :
        expected_path.substr(expected_separator + 1u);

    size_t exact_path_count = 0;
    for (const ModuleIdentityObservation& observation : observations) {
        const bool base_matches = expected_base != 0 &&
            observation.base == expected_base;
        if (!observation.pathVerified || observation.path.empty()) {
            // Without an authoritative full path this row could be the
            // expected module. A verified, distinct Toolhelp module name can
            // exclude an unrelated row without trusting its truncated path.
            const bool name_may_match = !observation.moduleNameVerified ||
                expected_name.empty() ||
                _wcsicmp(observation.moduleName.c_str(),
                         expected_name.c_str()) == 0;
            if (base_matches || name_may_match) return result;
            continue;
        }
        const bool path_matches = !observation.path.empty() &&
            _wcsicmp(observation.path.c_str(), expected_path.c_str()) == 0;
        if (base_matches && !path_matches) {
            if (!observation.identityVerified) return result;
            result.foreignAtExpectedBase = true;
        }
        if (!path_matches) continue;
        ++exact_path_count;
        if (!observation.identityVerified ||
            !SameFileIdentity(observation.identity, expected_identity)) {
            // A path match without a file-identity match is ambiguous (for
            // example, a path replaced during enumeration), never absence.
            return result;
        }
        result.presence = RemoteModulePresence::ExpectedPathPresent;
        result.observedBase = observation.base;
        result.exactBase = expected_base != 0 && base_matches;
    }
    if (exact_path_count > 1u) {
        result.presence = RemoteModulePresence::Unknown;
        result.observedBase = 0;
        result.exactBase = false;
        return result;
    }
    if (exact_path_count == 0u)
        result.presence = RemoteModulePresence::Absent;
    return result;
}

bool ReadAuthoritativeRemoteModulePath(
    HANDLE process, DWORD module_base, const MODULEENTRY32W& entry,
    std::wstring* path) {
    if (path == nullptr) return false;
    path->clear();
    std::vector<wchar_t> remote_path(32768, L'\0');
    const DWORD remote_length = process != nullptr ?
        GetModuleFileNameExW(process,
            reinterpret_cast<HMODULE>(
                static_cast<uintptr_t>(module_base)),
            remote_path.data(),
            static_cast<DWORD>(remote_path.size())) : 0u;
    if (remote_length > 0u &&
        remote_length < remote_path.size() - 1u) {
        *path = FullPath(std::wstring(
            remote_path.data(), remote_length));
        return !path->empty();
    }

    // Toolhelp's fixed MAX_PATH buffer is only authoritative when it is
    // definitely terminated before the truncation boundary.
    const size_t fallback_length = wcsnlen_s(
        entry.szExePath, ARRAYSIZE(entry.szExePath));
    if (fallback_length == 0u ||
        fallback_length >= ARRAYSIZE(entry.szExePath) - 1u)
        return false;
    *path = FullPath(std::wstring(entry.szExePath, fallback_length));
    return !path->empty();
}

RemoteModuleSnapshot SnapshotRemoteModule(
    DWORD process_id, DWORD remote_module,
    const std::wstring& expected_path) {
    const std::wstring expected_full = FullPath(expected_path);
    StableFileIdentity expected_identity{};
    if (expected_full.empty() ||
        !ReadStableFileIdentity(expected_full, &expected_identity))
        return {};

    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION |
        PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ,
        FALSE, process_id);
    if (process == nullptr) return {};
    HANDLE snapshot = CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, process_id);
    if (snapshot == INVALID_HANDLE_VALUE) {
        CloseHandle(process);
        return {};
    }
    std::vector<ModuleIdentityObservation> observations;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    SetLastError(ERROR_SUCCESS);
    BOOL current = Module32FirstW(snapshot, &entry);
    bool complete = current != FALSE;
    if (current == FALSE && GetLastError() == ERROR_NO_MORE_FILES)
        complete = true;
    while (current != FALSE) {
        ModuleIdentityObservation observation{};
        observation.base = static_cast<DWORD>(
            reinterpret_cast<uintptr_t>(entry.modBaseAddr));
        const size_t module_name_length = wcsnlen_s(
            entry.szModule, ARRAYSIZE(entry.szModule));
        observation.moduleNameVerified = module_name_length > 0u &&
            module_name_length < ARRAYSIZE(entry.szModule);
        if (observation.moduleNameVerified)
            observation.moduleName.assign(
                entry.szModule, module_name_length);
        observation.pathVerified = ReadAuthoritativeRemoteModulePath(
            process, observation.base, entry, &observation.path);
        observation.identityVerified = observation.pathVerified &&
            ReadStableFileIdentity(observation.path, &observation.identity);
        observations.push_back(observation);
        SetLastError(ERROR_SUCCESS);
        current = Module32NextW(snapshot, &entry);
        if (current == FALSE && GetLastError() != ERROR_NO_MORE_FILES)
            complete = false;
    }
    CloseHandle(snapshot);
    CloseHandle(process);
    return ClassifyModuleObservations(remote_module, expected_full,
        expected_identity, observations, complete);
}

bool IsRemoteModulePresent(DWORD process_id, DWORD remote_module,
                           const std::wstring& expected_path,
                           bool* snapshot_verified,
                           bool* exact_identity = nullptr) {
    const RemoteModuleSnapshot snapshot = SnapshotRemoteModule(
        process_id, remote_module, expected_path);
    const bool verified = snapshot.enumerationComplete &&
        snapshot.expectedIdentityVerified &&
        snapshot.presence != RemoteModulePresence::Unknown;
    if (snapshot_verified != nullptr) *snapshot_verified = verified;
    if (exact_identity != nullptr)
        *exact_identity = verified &&
            snapshot.presence == RemoteModulePresence::ExpectedPathPresent &&
            snapshot.exactBase;
    // Unknown is conservatively treated as present by this compatibility
    // wrapper. Callers that need the exact disposition use SnapshotRemoteModule.
    return snapshot.presence != RemoteModulePresence::Absent;
}

bool WaitForRemoteModuleAbsence(DWORD process_id, DWORD remote_module,
                                const std::wstring& expected_path,
                                DWORD timeout_ms, bool* snapshot_verified,
                                DWORD* verified_absent_snapshots = nullptr) {
    if (verified_absent_snapshots != nullptr)
        *verified_absent_snapshots = 0;
    const ULONGLONG deadline = GetTickCount64() + timeout_ms;
    bool verified_any = false;
    DWORD consecutive_absent = 0;
    for (;;) {
        bool verified = false;
        const bool present = IsRemoteModulePresent(
            process_id, remote_module, expected_path, &verified);
        verified_any = verified_any || verified;
        if (verified && !present) {
            ++consecutive_absent;
        } else {
            consecutive_absent = 0;
        }
        if (consecutive_absent >= 2u) {
            if (snapshot_verified != nullptr) *snapshot_verified = true;
            if (verified_absent_snapshots != nullptr)
                *verified_absent_snapshots = consecutive_absent;
            return true;
        }
        if (GetTickCount64() >= deadline) {
            if (snapshot_verified != nullptr) *snapshot_verified = verified_any;
            if (verified_absent_snapshots != nullptr)
                *verified_absent_snapshots = consecutive_absent;
            return false;
        }
        Sleep(25);
    }
}

struct FreeLibraryAttemptLedger {
    DWORD helperCreateCount{};
    DWORD terminalCount{};
    DWORD knownResultCount{};
    DWORD successfulResultCount{};
};

struct OwnedModuleActionLedger {
    DWORD authorizationAttemptCount{};
    DWORD authorizationRejectedCount{};
    DWORD remoteThreadCreateCount{};
    DWORD remoteThreadTerminalCount{};
    DWORD waitReadyAuthorizationAttemptCount{};
    DWORD waitReadyAuthorizationRejectedCount{};
    DWORD waitReadyRemoteThreadCreateCount{};
    DWORD armAuthorizationAttemptCount{};
    DWORD armAuthorizationRejectedCount{};
    DWORD armRemoteThreadCreateCount{};
    DWORD stopAuthorizationAttemptCount{};
    DWORD stopAuthorizationRejectedCount{};
    DWORD stopRemoteThreadCreateCount{};
    DWORD canUnloadAuthorizationAttemptCount{};
    DWORD canUnloadAuthorizationRejectedCount{};
    DWORD canUnloadRemoteThreadCreateCount{};
};

struct OwnedModuleReleaseState {
    DWORD processId{};
    DWORD moduleBase{};
    std::wstring expectedPath;
    StableFileIdentity expectedIdentity{};
    bool initialized{};
    bool tokenAvailable{};
    bool identityVerifiedAtIssue{};
    bool helperCreated{};
    bool helperTerminal{};
    bool exitCodeKnown{};
    bool releaseSucceeded{};
    bool processExited{};
};

bool SnapshotAuthorizesRemoteModuleAction(
    const RemoteModuleSnapshot& snapshot,
    DWORD expected_base) noexcept {
    return snapshot.enumerationComplete &&
        snapshot.expectedIdentityVerified &&
        snapshot.presence == RemoteModulePresence::ExpectedPathPresent &&
        snapshot.exactBase && snapshot.observedBase == expected_base;
}

OwnedModuleReleaseState MakeOwnedModuleReleaseState(
    DWORD process_id, DWORD module_base, const std::wstring& expected_path) {
    OwnedModuleReleaseState state{};
    state.processId = process_id;
    state.moduleBase = module_base;
    state.expectedPath = FullPath(expected_path);
    const bool identity_verified = !state.expectedPath.empty() &&
        ReadStableFileIdentity(state.expectedPath, &state.expectedIdentity);
    state.initialized = process_id != 0 && module_base != 0 &&
        !state.expectedPath.empty() && identity_verified;
    state.tokenAvailable = state.initialized;
    return state;
}

bool VerifyOwnedModuleForRemoteAction(
    const OwnedModuleReleaseState& owner,
    RemoteModuleSnapshot* observed = nullptr) {
    if (!owner.initialized || !owner.expectedIdentity.valid)
        return false;
    StableFileIdentity current_expected_identity{};
    if (!ReadStableFileIdentity(owner.expectedPath,
            &current_expected_identity) ||
        !SameFileIdentity(owner.expectedIdentity,
                          current_expected_identity))
        return false;
    const RemoteModuleSnapshot snapshot = SnapshotRemoteModule(
        owner.processId, owner.moduleBase, owner.expectedPath);
    if (observed != nullptr) *observed = snapshot;
    return SnapshotAuthorizesRemoteModuleAction(
        snapshot, owner.moduleBase);
}

bool CallOwnedModuleExport(
    HANDLE process, const OwnedModuleReleaseState& owner,
    const std::wstring& dll, const char* export_name, void* parameter,
    DWORD* result, DWORD* error, OwnedModuleActionLedger* ledger,
    bool* identity_authorized = nullptr,
    DWORD timeout_ms = 20'000) {
    DWORD* action_attempt_count = nullptr;
    DWORD* action_rejected_count = nullptr;
    DWORD* action_thread_create_count = nullptr;
    if (ledger != nullptr && export_name != nullptr) {
        if (strcmp(export_name, "God2TraceProbeWaitReady") == 0) {
            action_attempt_count = &ledger->waitReadyAuthorizationAttemptCount;
            action_rejected_count = &ledger->waitReadyAuthorizationRejectedCount;
            action_thread_create_count = &ledger->waitReadyRemoteThreadCreateCount;
        } else if (strcmp(export_name,
                       "God2TraceProbeArmRestoreFaultSelfTest") == 0) {
            action_attempt_count = &ledger->armAuthorizationAttemptCount;
            action_rejected_count = &ledger->armAuthorizationRejectedCount;
            action_thread_create_count = &ledger->armRemoteThreadCreateCount;
        } else if (strcmp(export_name, "God2TraceProbeStop") == 0) {
            action_attempt_count = &ledger->stopAuthorizationAttemptCount;
            action_rejected_count = &ledger->stopAuthorizationRejectedCount;
            action_thread_create_count = &ledger->stopRemoteThreadCreateCount;
        } else if (strcmp(export_name, "God2TraceProbeCanUnload") == 0) {
            action_attempt_count = &ledger->canUnloadAuthorizationAttemptCount;
            action_rejected_count = &ledger->canUnloadAuthorizationRejectedCount;
            action_thread_create_count = &ledger->canUnloadRemoteThreadCreateCount;
        }
    }
    if (identity_authorized != nullptr) *identity_authorized = false;
    if (error != nullptr) *error = ERROR_SUCCESS;
    if (result != nullptr) *result = 0;
    if (ledger != nullptr) ++ledger->authorizationAttemptCount;
    if (action_attempt_count != nullptr) ++*action_attempt_count;
    const DWORD address = RemoteExport(
        owner.moduleBase, dll, export_name);
    if (address == 0) {
        if (error != nullptr) *error = ERROR_PROC_NOT_FOUND;
        return false;
    }
    // Resolve the local RVA first, then verify the live target identity at the
    // last possible point before RemoteCall creates its remote thread.
    if (!VerifyOwnedModuleForRemoteAction(owner)) {
        if (ledger != nullptr) ++ledger->authorizationRejectedCount;
        if (action_rejected_count != nullptr) ++*action_rejected_count;
        if (error != nullptr) *error = ERROR_INVALID_ADDRESS;
        return false;
    }
    if (identity_authorized != nullptr) *identity_authorized = true;
    bool thread_created = false;
    bool thread_terminal = false;
    const bool completed = RemoteCall(process, address, parameter, result,
        error, timeout_ms, &thread_created, &thread_terminal);
    if (ledger != nullptr) {
        if (thread_created) ++ledger->remoteThreadCreateCount;
        if (thread_terminal) ++ledger->remoteThreadTerminalCount;
    }
    if (thread_created && action_thread_create_count != nullptr)
        ++*action_thread_create_count;
    return completed;
}

bool IssueOwnedFreeLibrary(
    HANDLE process, OwnedModuleReleaseState* owner,
    FreeLibraryAttemptLedger* ledger, ProbeStopResult* outcome,
    volatile LONG* injected_create_failures = nullptr) {
    if (owner == nullptr || ledger == nullptr || outcome == nullptr ||
        !owner->initialized) {
        if (outcome != nullptr) outcome->error = ERROR_INVALID_PARAMETER;
        return false;
    }
    outcome->freeLibraryThreadCreated = owner->helperCreated;
    outcome->freeLibraryThreadCompleted = owner->helperTerminal;
    outcome->referenceReleased = owner->releaseSucceeded;
    outcome->releaseIdentityVerified = owner->identityVerifiedAtIssue;
    outcome->releaseTokenConsumed = !owner->tokenAvailable;
    outcome->releaseOutcomeKnown = owner->exitCodeKnown;
    if (!owner->tokenAvailable) {
        outcome->error = owner->releaseSucceeded ? ERROR_SUCCESS : ERROR_BUSY;
        return owner->releaseSucceeded;
    }

    const RemoteModuleSnapshot snapshot = SnapshotRemoteModule(
        owner->processId, owner->moduleBase, owner->expectedPath);
    StableFileIdentity current_expected_identity{};
    const bool expected_identity_stable = ReadStableFileIdentity(
        owner->expectedPath, &current_expected_identity) &&
        SameFileIdentity(owner->expectedIdentity,
                         current_expected_identity);
    if (!snapshot.enumerationComplete ||
        !snapshot.expectedIdentityVerified ||
        !expected_identity_stable ||
        snapshot.presence == RemoteModulePresence::Unknown) {
        outcome->error = ERROR_RETRY;
        return false;
    }
    if (snapshot.presence == RemoteModulePresence::Absent) {
        // The exact owned instance is already absent.  Extinguish the token
        // without issuing a decrement and report snapshot-derived completion.
        owner->tokenAvailable = false;
        outcome->releaseTokenConsumed = true;
        outcome->moduleSnapshotVerified = true;
        outcome->moduleAbsent = true;
        outcome->unloaded = true;
        outcome->error = ERROR_SUCCESS;
        return true;
    }
    if (!snapshot.exactBase || snapshot.observedBase != owner->moduleBase) {
        // The expected file at a different base is a different lifetime. Do
        // not decrement it using the stale owned base.
        outcome->error = ERROR_INVALID_ADDRESS;
        return false;
    }
    owner->identityVerifiedAtIssue = true;
    outcome->releaseIdentityVerified = true;
    const DWORD free_library = RemoteKernel32Export(
        owner->processId, "FreeLibrary");
    if (free_library == 0) {
        outcome->error = GetLastError() == ERROR_SUCCESS ?
            ERROR_PROC_NOT_FOUND : GetLastError();
        return false;
    }
    if (ConsumeInjectedFailure(injected_create_failures)) {
        outcome->error = ERROR_NOT_ENOUGH_MEMORY;
        return false;
    }
    HANDLE thread = CreateRemoteThread(process, nullptr, 0,
        reinterpret_cast<LPTHREAD_START_ROUTINE>(
            static_cast<uintptr_t>(free_library)),
        reinterpret_cast<void*>(
            static_cast<uintptr_t>(owner->moduleBase)), 0, nullptr);
    if (thread == nullptr) {
        outcome->error = GetLastError();
        return false;
    }

    // Creation is the irreversible ownership transition.  Consume the token
    // before waiting; an unknown outcome can only be reconciled by snapshots,
    // never by issuing another FreeLibrary.
    owner->tokenAvailable = false;
    owner->helperCreated = true;
    ++ledger->helperCreateCount;
    outcome->freeLibraryThreadCreated = true;
    outcome->releaseTokenConsumed = true;
    const OwnedRemoteActionResult action = WaitForOwnedRemoteThread(
        process, thread, 20'000);
    CloseHandle(thread);
    owner->helperTerminal = action.terminal;
    owner->processExited = action.processExited;
    owner->exitCodeKnown = action.exitCodeKnown;
    owner->releaseSucceeded = action.exitCodeKnown && action.exitCode != 0;
    if (action.terminal) ++ledger->terminalCount;
    if (action.exitCodeKnown) ++ledger->knownResultCount;
    if (owner->releaseSucceeded) ++ledger->successfulResultCount;
    outcome->freeLibraryThreadCompleted = action.terminal;
    outcome->referenceReleased = owner->releaseSucceeded;
    outcome->releaseOutcomeKnown = action.exitCodeKnown;
    if (action.processExited) {
        outcome->moduleAbsent = true;
        outcome->unloaded = true;
        outcome->error = ERROR_SUCCESS;
        return true;
    }
    outcome->error = owner->releaseSucceeded ? ERROR_SUCCESS :
        (action.error == ERROR_SUCCESS ? ERROR_BUSY : action.error);
    return owner->releaseSucceeded;
}

bool RunInjectorOwnershipStateSelfTests(
    OwnedRemoteThreadSelfTestResult* result) {
    if (result == nullptr) return false;
    const std::wstring expected_path = L"C:\\fixture\\probe.dll";
    StableFileIdentity expected_identity{};
    expected_identity.volumeSerial = 0x1234u;
    expected_identity.fileIndexHigh = 0x5678u;
    expected_identity.fileIndexLow = 0x9abcu;
    expected_identity.fileSizeLow = 0x4000u;
    expected_identity.valid = true;

    ModuleIdentityObservation foreign{};
    foreign.base = 0x10000000u;
    foreign.path = L"C:\\fixture\\foreign.dll";
    foreign.identity = expected_identity;
    ++foreign.identity.fileIndexLow;
    foreign.identityVerified = true;
    foreign.pathVerified = true;
    const RemoteModuleSnapshot foreign_old_base =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {foreign}, true);
    result->foreignOldBaseClassifiedAbsent =
        foreign_old_base.presence == RemoteModulePresence::Absent &&
        foreign_old_base.foreignAtExpectedBase &&
        !foreign_old_base.exactBase;
    // This synthetic classification is only the pure policy half.  The
    // actual common export-action wrapper is exercised below against a live
    // foreign module base and its thread-creation ledger is the authority for
    // the no-remote-action claim.

    ModuleIdentityObservation exact{};
    exact.base = 0x10000000u;
    exact.path = expected_path;
    exact.identity = expected_identity;
    exact.pathVerified = true;
    exact.identityVerified = true;
    const RemoteModuleSnapshot exact_present =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {exact}, true);
    const bool exact_present_passed = exact_present.presence ==
            RemoteModulePresence::ExpectedPathPresent &&
        exact_present.exactBase &&
        exact_present.observedBase == exact.base;

    ModuleIdentityObservation relocated{};
    relocated.base = 0x20000000u;
    relocated.path = expected_path;
    relocated.identity = expected_identity;
    relocated.pathVerified = true;
    relocated.identityVerified = true;
    const RemoteModuleSnapshot expected_new_base =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {foreign, relocated}, true);
    result->expectedNewBaseClassifiedPresent =
        expected_new_base.presence ==
            RemoteModulePresence::ExpectedPathPresent &&
        expected_new_base.observedBase == relocated.base &&
        !expected_new_base.exactBase &&
        expected_new_base.foreignAtExpectedBase;

    const RemoteModuleSnapshot enumeration_failure =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {}, false);
    result->enumerationFailureClassifiedUnknown =
        enumeration_failure.presence == RemoteModulePresence::Unknown &&
        !enumeration_failure.enumerationComplete;
    ModuleIdentityObservation unreadable_base{};
    unreadable_base.base = 0x10000000u;
    const RemoteModuleSnapshot unreadable_base_result =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {unreadable_base}, true);
    result->baseMatchUnreadableClassifiedUnknown =
        unreadable_base_result.presence == RemoteModulePresence::Unknown;

    ModuleIdentityObservation truncated_base{};
    truncated_base.base = 0x10000000u;
    truncated_base.path.assign(MAX_PATH - 1u, L'x');
    truncated_base.identity = expected_identity;
    truncated_base.identityVerified = true;
    truncated_base.pathVerified = false;
    const RemoteModuleSnapshot truncated_base_result =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {truncated_base}, true);
    result->truncatedPathClassifiedUnknown =
        truncated_base_result.presence == RemoteModulePresence::Unknown;

    ModuleIdentityObservation unreadable_baseline{};
    unreadable_baseline.base = 0x30000000u;
    const RemoteModuleSnapshot unreadable_baseline_result =
        ClassifyModuleObservations(0u, expected_path,
            expected_identity, {unreadable_baseline}, true);
    result->baselineUnreadableClassifiedUnknown =
        unreadable_baseline_result.presence == RemoteModulePresence::Unknown;
    ModuleIdentityObservation replaced = exact;
    ++replaced.identity.fileIndexLow;
    const RemoteModuleSnapshot identity_mismatch =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {replaced}, true);
    const RemoteModuleSnapshot exact_absent =
        ClassifyModuleObservations(0x10000000u, expected_path,
            expected_identity, {}, true);
    const bool fail_closed_cases_passed =
        identity_mismatch.presence == RemoteModulePresence::Unknown &&
        exact_absent.presence == RemoteModulePresence::Absent;
    HMODULE module = LoadLibraryW(L"version.dll");
    if (module == nullptr) return false;
    wchar_t module_path[MAX_PATH * 4]{};
    const DWORD path_length = GetModuleFileNameW(
        module, module_path, ARRAYSIZE(module_path));
    if (path_length == 0 || path_length >= ARRAYSIZE(module_path)) {
        FreeLibrary(module);
        return false;
    }
    OwnedModuleReleaseState owner = MakeOwnedModuleReleaseState(
        GetCurrentProcessId(), static_cast<DWORD>(
            reinterpret_cast<uintptr_t>(module)), module_path);
    OwnedModuleReleaseState foreign_owner = owner;
    HMODULE foreign_module = GetModuleHandleW(L"kernel32.dll");
    OwnedModuleActionLedger foreign_action_ledger{};
    bool foreign_identity_authorized = false;
    DWORD foreign_result = 0;
    DWORD foreign_error = ERROR_SUCCESS;
    bool foreign_call_completed = false;
    if (owner.initialized && foreign_module != nullptr &&
        foreign_module != module) {
        foreign_owner.moduleBase = static_cast<DWORD>(
            reinterpret_cast<uintptr_t>(foreign_module));
        foreign_call_completed = CallOwnedModuleExport(
            GetCurrentProcess(), foreign_owner, module_path,
            "GetFileVersionInfoSizeW", nullptr, &foreign_result,
            &foreign_error, &foreign_action_ledger,
            &foreign_identity_authorized);
    }
    result->foreignAuthorizationAttemptCount =
        foreign_action_ledger.authorizationAttemptCount;
    result->foreignAuthorizationRejectedCount =
        foreign_action_ledger.authorizationRejectedCount;
    result->foreignRemoteThreadCreateCount =
        foreign_action_ledger.remoteThreadCreateCount;
    result->foreignRemoteThreadTerminalCount =
        foreign_action_ledger.remoteThreadTerminalCount;
    result->foreignBeforeRemoteActionRejected =
        !foreign_call_completed && !foreign_identity_authorized &&
        foreign_error == ERROR_INVALID_ADDRESS &&
        foreign_action_ledger.authorizationAttemptCount == 1u &&
        foreign_action_ledger.authorizationRejectedCount == 1u;
    result->foreignRemoteActionCountZero =
        foreign_action_ledger.remoteThreadCreateCount == 0u &&
        foreign_action_ledger.remoteThreadTerminalCount == 0u;
    result->moduleIdentityFixturePassed =
        exact_present_passed && fail_closed_cases_passed &&
        result->foreignOldBaseClassifiedAbsent &&
        result->foreignBeforeRemoteActionRejected &&
        result->foreignRemoteActionCountZero &&
        result->expectedNewBaseClassifiedPresent &&
        result->enumerationFailureClassifiedUnknown &&
        result->baseMatchUnreadableClassifiedUnknown &&
        result->truncatedPathClassifiedUnknown &&
        result->baselineUnreadableClassifiedUnknown;
    FreeLibraryAttemptLedger ledger{};
    volatile LONG create_failure = 1;
    ProbeStopResult injected{};
    const bool first = IssueOwnedFreeLibrary(GetCurrentProcess(), &owner,
        &ledger, &injected, &create_failure);
    result->releaseCreateFailureRetainedToken = !first &&
        owner.tokenAvailable && !owner.helperCreated &&
        ledger.helperCreateCount == 0u;

    ProbeStopResult released{};
    const bool second = IssueOwnedFreeLibrary(GetCurrentProcess(), &owner,
        &ledger, &released);
    ProbeStopResult duplicate{};
    (void)IssueOwnedFreeLibrary(GetCurrentProcess(), &owner,
        &ledger, &duplicate);
    result->releaseThreadCreateCount = ledger.helperCreateCount;
    result->releaseTokenConsumedOnce = second &&
        released.releaseIdentityVerified &&
        released.releaseTokenConsumed && !owner.tokenAvailable &&
        owner.helperCreated && owner.helperTerminal &&
        owner.exitCodeKnown && owner.releaseSucceeded &&
        ledger.helperCreateCount == 1u &&
        duplicate.releaseTokenConsumed &&
        ledger.helperCreateCount == result->releaseThreadCreateCount;
    return result->moduleIdentityFixturePassed &&
        result->releaseCreateFailureRetainedToken &&
        result->releaseTokenConsumedOnce;
}

ProbeStopResult StopProbe(HANDLE process, DWORD process_id, DWORD remote_module,
                          const std::wstring& dll,
                          OwnedModuleReleaseState* release_owner,
                          FreeLibraryAttemptLedger* release_ledger,
                          OwnedModuleActionLedger* action_ledger) {
    ProbeStopResult outcome;
    StableFileIdentity current_expected_identity{};
    if (release_owner == nullptr || !release_owner->initialized ||
        !ReadStableFileIdentity(release_owner->expectedPath,
                                &current_expected_identity) ||
        !SameFileIdentity(release_owner->expectedIdentity,
                          current_expected_identity)) {
        outcome.error = ERROR_INVALID_DATA;
        return outcome;
    }
    const RemoteModuleSnapshot entry_snapshot = SnapshotRemoteModule(
        process_id, remote_module, dll);
    if (!entry_snapshot.enumerationComplete ||
        !entry_snapshot.expectedIdentityVerified ||
        entry_snapshot.presence == RemoteModulePresence::Unknown) {
        outcome.error = ERROR_RETRY;
        return outcome;
    }
    if (entry_snapshot.presence == RemoteModulePresence::Absent) {
        if (release_owner != nullptr) release_owner->tokenAvailable = false;
        outcome.moduleSnapshotVerified = true;
        outcome.moduleAbsent = true;
        outcome.unloaded = true;
        outcome.error = ERROR_SUCCESS;
        return outcome;
    }
    if (!entry_snapshot.exactBase ||
        entry_snapshot.observedBase != remote_module) {
        outcome.error = ERROR_INVALID_ADDRESS;
        return outcome;
    }
    DWORD stop_result = 0;
    if (!CallOwnedModuleExport(process, *release_owner, dll,
            "God2TraceProbeStop", nullptr, &stop_result, &outcome.error,
            action_ledger, &outcome.stopIdentityAuthorized) ||
        stop_result == 0) {
        if (outcome.error == ERROR_SUCCESS) outcome.error = ERROR_BUSY;
        return outcome;
    }
    outcome.stopped = true;

    DWORD can_unload = 0;
    DWORD unload_check_error = ERROR_SUCCESS;
    if (!CallOwnedModuleExport(process, *release_owner, dll,
            "God2TraceProbeCanUnload", nullptr, &can_unload,
            &unload_check_error, action_ledger,
            &outcome.canUnloadIdentityAuthorized)) {
        // A stopped resident probe is safer than unloading when safety cannot be proven.
        outcome.error = unload_check_error;
        return outcome;
    }
    outcome.unloadSafe = can_unload != 0;
    if (!outcome.unloadSafe) return outcome;

    if (release_owner == nullptr || release_ledger == nullptr) {
        outcome.error = ERROR_INVALID_PARAMETER;
        return outcome;
    }
    const bool release_completed = IssueOwnedFreeLibrary(
        process, release_owner, release_ledger, &outcome);
    if (outcome.unloaded || WaitForSingleObject(process, 0) == WAIT_OBJECT_0)
        return outcome;
    if (!release_completed && !outcome.freeLibraryThreadCreated)
        return outcome;
    outcome.moduleAbsent = WaitForRemoteModuleAbsence(process_id,
        remote_module, dll, 5'000, &outcome.moduleSnapshotVerified);
    outcome.unloaded = outcome.moduleSnapshotVerified && outcome.moduleAbsent;
    if (!outcome.unloaded) outcome.error = ERROR_BUSY;
    return outcome;
}

ProbeStopResult StopProbeUntilOwnedCleanupTerminal(
    HANDLE process, DWORD process_id, DWORD remote_module,
    const std::wstring& dll, OwnedModuleReleaseState* release_owner,
    FreeLibraryAttemptLedger* release_ledger,
    OwnedModuleActionLedger* action_ledger,
    bool knownRetainedReference = false) {
    ProbeStopResult outcome;
    for (;;) {
        outcome = StopProbe(process, process_id, remote_module, dll,
            release_owner, release_ledger, action_ledger);
        if (outcome.unloaded) return outcome;
        if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
            outcome.moduleAbsent = true;
            outcome.error = ERROR_SUCCESS;
            return outcome;
        }
        if (outcome.referenceReleased ||
            (outcome.freeLibraryThreadCreated &&
             !outcome.freeLibraryThreadCompleted)) {
            // FreeLibrary may legitimately leave a known extra self-test
            // reference. Return only to the code that owns and will release
            // that reference. Otherwise never issue a second FreeLibrary and
            // never abandon cleanup: keep proving absence from complete module
            // snapshots until the module disappears or the target exits.
            if (knownRetainedReference && outcome.referenceReleased)
                return outcome;
            for (;;) {
                bool snapshot_verified = false;
                if (WaitForRemoteModuleAbsence(process_id, remote_module, dll,
                        1'000, &snapshot_verified) && snapshot_verified) {
                    outcome.moduleSnapshotVerified = true;
                    outcome.moduleAbsent = true;
                    outcome.unloaded = true;
                    outcome.error = ERROR_SUCCESS;
                    return outcome;
                }
                if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
                    outcome.moduleAbsent = true;
                    outcome.error = ERROR_SUCCESS;
                    return outcome;
                }
                Sleep(100);
            }
        }
        // Retain process/module ownership and retry safely. A retriable cleanup
        // record is never followed by helper exit, handle close, or staging
        // abandonment while the target and module can still be resident.
        Sleep(100);
    }
}

ProbeStopResult RunRestoreFaultLifecycle(
    HANDLE process, DWORD process_id, DWORD remote_module,
    const std::wstring& dll, OwnedModuleReleaseState* release_owner,
    FreeLibraryAttemptLedger* release_ledger,
    OwnedModuleActionLedger* action_ledger,
    RestoreFaultLifecycleResult* evidence) {
    ProbeStopResult terminal{};
    if (process == nullptr || process == INVALID_HANDLE_VALUE ||
        process_id == 0u || remote_module == 0u || dll.empty() ||
        release_owner == nullptr || release_ledger == nullptr ||
        action_ledger == nullptr || evidence == nullptr) {
        terminal.error = ERROR_INVALID_PARAMETER;
        return terminal;
    }

    evidence->processId = process_id;
    evidence->moduleBase = remote_module;
    evidence->freeLibraryCallCountAtEntry = release_ledger != nullptr ?
        release_ledger->helperCreateCount : 0u;
    evidence->dllExportThreadCountAtEntry = action_ledger != nullptr ?
        action_ledger->remoteThreadCreateCount : 0u;
    const DWORD identity_reject_count_at_entry = action_ledger != nullptr ?
        action_ledger->authorizationRejectedCount : 0u;
    DWORD first_stop_result = 0;
    DWORD first_stop_error = ERROR_SUCCESS;
    evidence->firstStopCallCompleted =
        release_owner != nullptr && CallOwnedModuleExport(
            process, *release_owner, dll, "God2TraceProbeStop", nullptr,
            &first_stop_result, &first_stop_error, action_ledger,
            &evidence->firstStopIdentityAuthorized);
    evidence->firstStopRejected = evidence->firstStopCallCompleted &&
        first_stop_result == 0;

    DWORD first_can_unload_result = 0;
    DWORD first_can_unload_error = ERROR_SUCCESS;
    evidence->firstCanUnloadCallCompleted =
        release_owner != nullptr && CallOwnedModuleExport(
            process, *release_owner, dll, "God2TraceProbeCanUnload",
            nullptr, &first_can_unload_result, &first_can_unload_error,
            action_ledger, &evidence->firstCanUnloadIdentityAuthorized);
    evidence->firstCanUnloadRejected =
        evidence->firstCanUnloadCallCompleted &&
        first_can_unload_result == 0;

    bool first_snapshot_exact_identity = false;
    evidence->firstSnapshotModulePresent = IsRemoteModulePresent(
        process_id, remote_module, dll,
        &evidence->firstSnapshotVerified,
        &first_snapshot_exact_identity);
    evidence->firstSnapshotModuleIdentityMatched =
        evidence->firstSnapshotVerified &&
        evidence->firstSnapshotModulePresent &&
        first_snapshot_exact_identity;
    const DWORD release_count_before_retry = release_ledger != nullptr ?
        release_ledger->helperCreateCount : 0u;
    evidence->freeLibraryCallCountBeforeRetry = release_count_before_retry;
    evidence->freeLibraryCallCountDeltaBeforeRetry =
        release_count_before_retry - evidence->freeLibraryCallCountAtEntry;
    evidence->noFreeLibraryBeforeRetry = release_ledger != nullptr &&
        evidence->freeLibraryCallCountDeltaBeforeRetry == 0u;
    evidence->cleanupOwnerRetained = process != nullptr &&
        GetProcessId(process) == process_id &&
        WaitForSingleObject(process, 0) == WAIT_TIMEOUT &&
        evidence->firstSnapshotModuleIdentityMatched;

    DWORD retry_stop_result = 0;
    DWORD retry_stop_error = ERROR_SUCCESS;
    evidence->retryStopCallCompleted =
        release_owner != nullptr && CallOwnedModuleExport(
            process, *release_owner, dll, "God2TraceProbeStop", nullptr,
            &retry_stop_result, &retry_stop_error, action_ledger,
            &evidence->retryStopIdentityAuthorized);
    evidence->retryStopSucceeded = evidence->retryStopCallCompleted &&
        retry_stop_result != 0;

    DWORD retry_can_unload_result = 0;
    DWORD retry_can_unload_error = ERROR_SUCCESS;
    evidence->retryCanUnloadCallCompleted =
        release_owner != nullptr && CallOwnedModuleExport(
            process, *release_owner, dll, "God2TraceProbeCanUnload",
            nullptr, &retry_can_unload_result, &retry_can_unload_error,
            action_ledger, &evidence->retryCanUnloadIdentityAuthorized);
    evidence->retryCanUnloadSucceeded =
        evidence->retryCanUnloadCallCompleted &&
        retry_can_unload_result != 0;

    bool reference_released = false;
    bool release_issued_completion_unknown = false;
    if (evidence->retryStopSucceeded &&
        evidence->retryCanUnloadSucceeded) {
        ProbeStopResult release{};
        const bool free_call_completed = IssueOwnedFreeLibrary(
            process, release_owner, release_ledger, &release);
        evidence->freeLibraryThreadCreated =
            release.freeLibraryThreadCreated;
        evidence->freeLibraryThreadCompleted =
            release.freeLibraryThreadCompleted;
        evidence->freeLibraryCallCount = release_ledger != nullptr ?
            release_ledger->helperCreateCount : 0u;
        evidence->freeLibrarySucceeded = free_call_completed &&
            release.referenceReleased;
        evidence->releaseIdentityVerified =
            release.releaseIdentityVerified;
        evidence->releaseTokenConsumedOnce =
            release.releaseTokenConsumed && release_owner != nullptr &&
            !release_owner->tokenAvailable;
        reference_released = evidence->freeLibrarySucceeded;
        release_issued_completion_unknown =
            release.freeLibraryThreadCreated &&
            !release.releaseOutcomeKnown;
        terminal.error = evidence->freeLibrarySucceeded ?
            ERROR_SUCCESS : (release.error == ERROR_SUCCESS ?
                ERROR_BUSY : release.error);
    }

    if (reference_released || release_issued_completion_unknown) {
        // The reference decrement is a one-shot ownership transition. Never
        // issue a second FreeLibrary: retain the process owner and keep taking
        // complete snapshots until two consecutive observations prove absence
        // or the target itself exits.
        for (;;) {
            bool snapshot_verified = false;
            DWORD absent_snapshots = 0;
            if (WaitForRemoteModuleAbsence(
                    process_id, remote_module, dll, 1'000,
                    &snapshot_verified, &absent_snapshots) &&
                snapshot_verified && absent_snapshots >= 2u) {
                evidence->verifiedAbsentSnapshotCount = absent_snapshots;
                evidence->moduleAbsent = true;
                terminal.moduleSnapshotVerified = true;
                terminal.moduleAbsent = true;
                terminal.unloaded = true;
                terminal.referenceReleased = reference_released;
                terminal.freeLibraryThreadCreated =
                    evidence->freeLibraryThreadCreated;
                terminal.freeLibraryThreadCompleted =
                    evidence->freeLibraryThreadCompleted;
                terminal.error = ERROR_SUCCESS;
                break;
            }
            if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
                evidence->moduleAbsent = true;
                terminal.moduleAbsent = true;
                terminal.referenceReleased = reference_released;
                terminal.error = ERROR_SUCCESS;
                break;
            }
            Sleep(100);
        }
    } else {
        // No decrement was proven. Retry the ordinary staged cleanup while
        // counting every actually-created FreeLibrary helper. If any retry
        // releases the reference, transition permanently to snapshot-only
        // ownership and never issue a second decrement.
        bool release_issued_or_unknown = false;
        for (;;) {
            ProbeStopResult attempt = StopProbe(
                process, process_id, remote_module, dll,
                release_owner, release_ledger, action_ledger);
            evidence->freeLibraryCallCount = release_ledger != nullptr ?
                release_ledger->helperCreateCount : 0u;
            evidence->freeLibraryThreadCreated =
                evidence->freeLibraryThreadCreated ||
                attempt.freeLibraryThreadCreated;
            evidence->freeLibraryThreadCompleted =
                evidence->freeLibraryThreadCompleted ||
                attempt.freeLibraryThreadCompleted;
            evidence->freeLibrarySucceeded =
                evidence->freeLibrarySucceeded ||
                attempt.referenceReleased;
            evidence->releaseIdentityVerified =
                evidence->releaseIdentityVerified ||
                attempt.releaseIdentityVerified;
            evidence->releaseTokenConsumedOnce =
                evidence->releaseTokenConsumedOnce ||
                (attempt.releaseTokenConsumed &&
                 release_owner != nullptr &&
                 !release_owner->tokenAvailable);
            if (attempt.unloaded) {
                terminal = attempt;
                evidence->verifiedAbsentSnapshotCount = 2u;
                evidence->moduleAbsent = true;
                break;
            }
            if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
                terminal = attempt;
                terminal.moduleAbsent = true;
                terminal.error = ERROR_SUCCESS;
                evidence->moduleAbsent = true;
                break;
            }
            if (attempt.referenceReleased ||
                (attempt.freeLibraryThreadCreated &&
                 !attempt.freeLibraryThreadCompleted)) {
                terminal = attempt;
                release_issued_or_unknown = true;
                break;
            }
            Sleep(100);
        }
        while (release_issued_or_unknown && !evidence->moduleAbsent) {
            bool snapshot_verified = false;
            DWORD absent_snapshots = 0;
            if (WaitForRemoteModuleAbsence(
                    process_id, remote_module, dll, 1'000,
                    &snapshot_verified, &absent_snapshots) &&
                snapshot_verified && absent_snapshots >= 2u) {
                evidence->verifiedAbsentSnapshotCount = absent_snapshots;
                evidence->moduleAbsent = true;
                terminal.moduleSnapshotVerified = true;
                terminal.moduleAbsent = true;
                terminal.unloaded = true;
                terminal.error = ERROR_SUCCESS;
                break;
            }
            if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
                evidence->moduleAbsent = true;
                terminal.moduleAbsent = true;
                terminal.error = ERROR_SUCCESS;
                break;
            }
            Sleep(100);
        }
    }

    terminal.stopped = evidence->retryStopSucceeded || terminal.stopped;
    terminal.unloadSafe =
        evidence->retryCanUnloadSucceeded || terminal.unloadSafe;
    evidence->targetAliveAfter =
        WaitForSingleObject(process, 0) == WAIT_TIMEOUT;
    evidence->freeLibraryCallCountAtExit = release_ledger != nullptr ?
        release_ledger->helperCreateCount : 0u;
    evidence->freeLibraryCallCount =
        evidence->freeLibraryCallCountAtExit -
        evidence->freeLibraryCallCountAtEntry;
    evidence->dllExportThreadCountAtExit = action_ledger != nullptr ?
        action_ledger->remoteThreadCreateCount : 0u;
    evidence->dllExportThreadCountDelta =
        evidence->dllExportThreadCountAtExit -
        evidence->dllExportThreadCountAtEntry;
    evidence->dllExportIdentityRejectDelta = action_ledger != nullptr ?
        action_ledger->authorizationRejectedCount -
            identity_reject_count_at_entry : 0u;
    if (action_ledger != nullptr) {
        evidence->waitReadyAuthorizationAttemptCount =
            action_ledger->waitReadyAuthorizationAttemptCount;
        evidence->waitReadyAuthorizationRejectedCount =
            action_ledger->waitReadyAuthorizationRejectedCount;
        evidence->waitReadyRemoteThreadCreateCount =
            action_ledger->waitReadyRemoteThreadCreateCount;
        evidence->armAuthorizationAttemptCount =
            action_ledger->armAuthorizationAttemptCount;
        evidence->armAuthorizationRejectedCount =
            action_ledger->armAuthorizationRejectedCount;
        evidence->armRemoteThreadCreateCount =
            action_ledger->armRemoteThreadCreateCount;
        evidence->stopAuthorizationAttemptCount =
            action_ledger->stopAuthorizationAttemptCount;
        evidence->stopAuthorizationRejectedCount =
            action_ledger->stopAuthorizationRejectedCount;
        evidence->stopRemoteThreadCreateCount =
            action_ledger->stopRemoteThreadCreateCount;
        evidence->canUnloadAuthorizationAttemptCount =
            action_ledger->canUnloadAuthorizationAttemptCount;
        evidence->canUnloadAuthorizationRejectedCount =
            action_ledger->canUnloadAuthorizationRejectedCount;
        evidence->canUnloadRemoteThreadCreateCount =
            action_ledger->canUnloadRemoteThreadCreateCount;
    }
    evidence->passed = evidence->requested &&
        evidence->armExportResolved && evidence->armCallCompleted &&
        evidence->armIdentityAuthorized &&
        evidence->armSucceeded &&
        evidence->firstStopIdentityAuthorized &&
        evidence->firstStopCallCompleted && evidence->firstStopRejected &&
        evidence->firstCanUnloadIdentityAuthorized &&
        evidence->firstCanUnloadCallCompleted &&
        evidence->firstCanUnloadRejected &&
        evidence->firstSnapshotVerified &&
        evidence->firstSnapshotModulePresent &&
        evidence->firstSnapshotModuleIdentityMatched &&
        evidence->noFreeLibraryBeforeRetry &&
        evidence->cleanupOwnerRetained &&
        evidence->retryStopIdentityAuthorized &&
        evidence->retryStopCallCompleted &&
        evidence->retryStopSucceeded &&
        evidence->retryCanUnloadIdentityAuthorized &&
        evidence->retryCanUnloadCallCompleted &&
        evidence->retryCanUnloadSucceeded &&
        evidence->freeLibraryThreadCreated &&
        evidence->freeLibraryThreadCompleted &&
        evidence->freeLibrarySucceeded &&
        evidence->freeLibraryCallCount == 1u &&
        evidence->freeLibraryCallCountDeltaBeforeRetry == 0u &&
        evidence->dllExportThreadCountDelta == 4u &&
        evidence->dllExportIdentityRejectDelta == 0u &&
        evidence->waitReadyAuthorizationAttemptCount == 1u &&
        evidence->waitReadyAuthorizationRejectedCount == 0u &&
        evidence->waitReadyRemoteThreadCreateCount == 1u &&
        evidence->armAuthorizationAttemptCount >= 1u &&
        evidence->armAuthorizationRejectedCount == 0u &&
        evidence->armRemoteThreadCreateCount ==
            evidence->armAuthorizationAttemptCount &&
        evidence->stopAuthorizationAttemptCount == 2u &&
        evidence->stopAuthorizationRejectedCount == 0u &&
        evidence->stopRemoteThreadCreateCount == 2u &&
        evidence->canUnloadAuthorizationAttemptCount == 2u &&
        evidence->canUnloadAuthorizationRejectedCount == 0u &&
        evidence->canUnloadRemoteThreadCreateCount == 2u &&
        evidence->releaseIdentityVerified &&
        evidence->releaseTokenConsumedOnce &&
        evidence->verifiedAbsentSnapshotCount >= 2u &&
        evidence->moduleAbsent && evidence->targetAliveAfter;
    return terminal;
}

int Monitor(int argc, wchar_t** argv) {
    const std::wstring pid_text = Arg(argc, argv, L"--pid");
    const std::wstring expected = Arg(argc, argv, L"--expected-exe");
    const std::wstring dll = FullPath(Arg(argc, argv, L"--dll"));
    const std::wstring stop_event_name = Arg(argc, argv, L"--stop-event");
    const std::wstring result_path = Arg(argc, argv, L"--result");
    const DWORD pid = wcstoul(pid_text.c_str(), nullptr, 10);
    InjectorResultRecord result{};
    const auto publishResult = [&](const char* status, DWORD code) {
        result.status = status;
        result.code = code;
        return WriteResult(result_path, result);
    };
    const auto normalizeProcessExitedResult = [&]() {
        result.probeReady = false;
        result.stopSucceeded = false;
        result.unloadSafe = false;
        result.moduleUnloaded = false;
        result.moduleSnapshotVerified = false;
        result.moduleAbsent = true;
        result.targetProcessExited = true;
        result.moduleResidentInactive = false;
        result.strictUnloadVerified = true;
        result.cleanupRetriable = false;
    };
    if (pid == 0 || expected.empty() || dll.empty() || stop_event_name.empty() || result_path.empty()) {
        publishResult("REJECTED_ARGUMENTS", ERROR_INVALID_PARAMETER);
        return ERROR_INVALID_PARAMETER;
    }
#if defined(GOD2_INJECTOR_ALLOW_SELF_TEST_TARGET)
    if (HasArg(argc, argv, L"--self-test-restore-fault") &&
        (HasArg(argc, argv, L"--self-test-extra-reference") ||
         Arg(argc, argv, L"--restore-fault-report").empty())) {
        publishResult("REJECTED_ARGUMENTS", ERROR_INVALID_PARAMETER);
        return ERROR_INVALID_PARAMETER;
    }
#endif
    if (!IsX86PeFile(dll)) {
        publishResult("REJECTED_DLL_ARCHITECTURE", ERROR_BAD_EXE_FORMAT);
        return ERROR_BAD_EXE_FORMAT;
    }
    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION |
        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | SYNCHRONIZE, FALSE, pid);
    if (process == nullptr) {
        const DWORD error = GetLastError();
        publishResult("OPEN_PROCESS_FAILED", error);
        return static_cast<int>(error);
    }
    // Establish module ownership evidence before *any* target identity exit.
    // Wrong-build rejection is still required to prove that this injector did
    // not leave (or inherit) the configured probe module in the target.
    bool baseline_snapshot_verified = false;
    DWORD baseline_absent_snapshots = 0;
    const bool baseline_absent = WaitForRemoteModuleAbsence(
        pid, 0, dll, 500, &baseline_snapshot_verified,
        &baseline_absent_snapshots);
    if (!baseline_absent || !baseline_snapshot_verified ||
        baseline_absent_snapshots < 2u) {
        const RemoteModuleSnapshot baseline = SnapshotRemoteModule(
            pid, 0, dll);
        result.moduleLoadStateVerified = baseline.enumerationComplete &&
            baseline.expectedIdentityVerified &&
            baseline.presence != RemoteModulePresence::Unknown;
        result.moduleSnapshotVerified = result.moduleLoadStateVerified;
        result.moduleAbsent = false;
        result.strictUnloadVerified = false;
        result.cleanupRetriable = false;
        const bool preloaded = result.moduleLoadStateVerified &&
            baseline.presence == RemoteModulePresence::ExpectedPathPresent;
        const DWORD failure = preloaded ? ERROR_ALREADY_EXISTS : ERROR_RETRY;
        CloseHandle(process);
        publishResult(preloaded ? "EVIDENCE_BLOCKED_PRELOADED_MODULE" :
                                  "EVIDENCE_BLOCKED_MODULE_SNAPSHOT_UNKNOWN",
                      failure);
        return static_cast<int>(failure);
    }
    result.moduleLoadStateVerified = true;
    result.moduleSnapshotVerified = true;
    result.moduleAbsent = true;
    result.strictUnloadVerified = true;
    result.cleanupRetriable = false;
    bool exact_target_build = IsExactTargetBuild(process, expected);
#if defined(GOD2_INJECTOR_ALLOW_SELF_TEST_TARGET)
    // This branch exists only in the separately named test binary produced by
    // Build-Instrumentation.ps1. The production injector embedded in the GUI
    // is compiled without this symbol and has no target-identity bypass.
    if (!exact_target_build && HasArg(argc, argv, L"--self-test-target"))
        exact_target_build = IsExactTarget(process, expected) && IsX86(process);
#endif
    if (!exact_target_build) {
        CloseHandle(process);
        publishResult("EVIDENCE_BLOCKED_BUILD_MISMATCH", ERROR_BAD_EXE_FORMAT);
        return ERROR_BAD_EXE_FORMAT;
    }
    result.targetIdentityVerified = true;
    HANDLE stop_event = OpenEventW(SYNCHRONIZE, FALSE, stop_event_name.c_str());
    if (stop_event == nullptr) {
        const DWORD error = GetLastError();
        CloseHandle(process);
        publishResult("OPEN_STOP_EVENT_FAILED", error);
        return static_cast<int>(error);
    }
    SuspendedPrimaryThread suspended(pid);
    DWORD suspend_error = ERROR_SUCCESS;
    if (!suspended.Suspend(&suspend_error)) {
        CloseHandle(stop_event);
        CloseHandle(process);
        publishResult("SUSPEND_FAILED", suspend_error);
        return suspend_error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : static_cast<int>(suspend_error);
    }
    const size_t suspended_thread_count = suspended.Count();
    const DWORD suspended_thread_id = suspended.ThreadId();
    bool resumed_before_completion = false;
    result.injectionAttempted = true;
    result.moduleLoadStateVerified = false;
    result.moduleSnapshotVerified = false;
    result.moduleAbsent = false;
    result.strictUnloadVerified = false;
    result.cleanupRetriable = true;
    result.suspendedThreadCount = suspended_thread_count;
    result.primaryThreadId = suspended_thread_id;
    const InjectionResult injected = Inject(process, pid, dll, &suspended, &resumed_before_completion);
    FreeLibraryAttemptLedger release_ledger{};
    OwnedModuleActionLedger action_ledger{};
    OwnedModuleReleaseState main_release = MakeOwnedModuleReleaseState(
        pid, injected.module, dll);
    result.moduleLoadStateVerified = injected.loadStateVerified;
    result.moduleWasEverLoaded = injected.module != 0;
    result.moduleAbsent = false;
    result.injectionMode = resumed_before_completion ?
        "PauseResumeThenRemoteThreadComplete" :
        "PausePrimaryRemoteThreadResume";
    if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
        if (injected.loadStateVerified && injected.module != 0) {
            normalizeProcessExitedResult();
            publishResult("DETACHED_PROCESS_EXITED", 0);
        } else if (injected.loadStateVerified) {
            result.targetProcessExited = true;
            result.moduleAbsent = true;
            result.strictUnloadVerified = true;
            result.cleanupRetriable = false;
            publishResult("INJECTION_FAILED", ERROR_PROCESS_ABORTED);
        } else {
            // Target exit proves no resident cleanup remains, but it cannot
            // retroactively prove whether a loader thread completed. Preserve
            // the unknown load provenance and fail closed instead of claiming
            // DETACHED_PROCESS_EXITED for a known-loaded module.
            result.targetProcessExited = true;
            result.moduleAbsent = true;
            result.strictUnloadVerified = false;
            result.cleanupRetriable = false;
            publishResult("INJECTION_FAILED", ERROR_PROCESS_ABORTED);
        }
        CloseHandle(stop_event);
        CloseHandle(process);
        return 0;
    }
    const auto applyCleanup = [&](const ProbeStopResult& cleanup) {
        result.stopSucceeded = cleanup.stopped;
        result.unloadSafe = cleanup.unloadSafe;
        result.moduleUnloaded = cleanup.unloaded;
        result.moduleSnapshotVerified = cleanup.moduleSnapshotVerified;
        result.moduleAbsent = cleanup.moduleAbsent;
        result.targetProcessExited =
            WaitForSingleObject(process, 0) == WAIT_OBJECT_0;
        if (result.targetProcessExited) result.moduleAbsent = true;
        result.moduleResidentInactive = result.moduleWasEverLoaded &&
            cleanup.stopped && !cleanup.unloaded && !result.targetProcessExited;
        result.strictUnloadVerified = !result.moduleWasEverLoaded ?
            result.moduleLoadStateVerified :
            (result.targetProcessExited ||
             (cleanup.unloaded && cleanup.moduleSnapshotVerified &&
              cleanup.moduleAbsent));
        result.cleanupRetriable = !result.strictUnloadVerified;
    };
    DWORD resume_error = ERROR_SUCCESS;
    bool resumed = suspended.Resume(&resume_error);
    while (!resumed && WaitForSingleObject(process, 0) != WAIT_OBJECT_0) {
        Sleep(100);
        resumed = suspended.Resume(&resume_error);
    }
    if (injected.module == 0) {
        if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
            if (injected.loadStateVerified) {
                result.targetProcessExited = true;
                result.moduleAbsent = true;
                result.strictUnloadVerified = true;
                result.cleanupRetriable = false;
                publishResult("INJECTION_FAILED", ERROR_PROCESS_ABORTED);
            } else {
                result.targetProcessExited = true;
                result.moduleAbsent = true;
                result.strictUnloadVerified = false;
                result.cleanupRetriable = false;
                publishResult("INJECTION_FAILED", ERROR_PROCESS_ABORTED);
            }
            CloseHandle(stop_event);
            CloseHandle(process);
            return 0;
        }
        bool no_load_snapshot_verified = false;
        DWORD no_load_absent_snapshots = 0;
        const bool no_load_absent = injected.loadStateVerified &&
            WaitForRemoteModuleAbsence(pid, 0, dll, 5'000,
                &no_load_snapshot_verified,
                &no_load_absent_snapshots) &&
            no_load_snapshot_verified &&
            no_load_absent_snapshots >= 2u;
        result.moduleSnapshotVerified = no_load_snapshot_verified;
        result.moduleAbsent = no_load_absent;
        result.strictUnloadVerified = no_load_absent;
        result.cleanupRetriable = !no_load_absent;
        CloseHandle(stop_event);
        CloseHandle(process);
        publishResult(no_load_absent ? "INJECTION_FAILED" :
            "EVIDENCE_BLOCKED_LOAD_RESULT_RECONCILIATION",
            injected.error == ERROR_SUCCESS ? ERROR_RETRY : injected.error);
        return injected.error == ERROR_SUCCESS ? ERROR_DLL_INIT_FAILED : static_cast<int>(injected.error);
    }
    if (!resumed) {
        const ProbeStopResult cleanup = StopProbeUntilOwnedCleanupTerminal(
            process, pid, injected.module, dll, &main_release,
            &release_ledger, &action_ledger);
        applyCleanup(cleanup);
        if (result.targetProcessExited) {
            normalizeProcessExitedResult();
            publishResult("DETACHED_PROCESS_EXITED", 0);
            CloseHandle(stop_event);
            CloseHandle(process);
            return 0;
        }
        CloseHandle(stop_event);
        CloseHandle(process);
        const DWORD failure = resume_error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : resume_error;
        publishResult("RESUME_FAILED", failure);
        return static_cast<int>(failure);
    }
    // Never create a readiness remote call while the primary thread is still
    // paused: initialization may legitimately need a lock owned by that thread,
    // and timing out then creating a second call would abandon the first. One
    // owned call is issued only after the suspend count is proven released.
    DWORD ready_result = 0;
    DWORD ready_error = ERROR_SUCCESS;
    bool ready_identity_authorized = false;
    const bool probe_ready = CallOwnedModuleExport(
        process, main_release, dll, "God2TraceProbeWaitReady", nullptr,
        &ready_result, &ready_error, &action_ledger,
        &ready_identity_authorized, 20'000) &&
        ready_identity_authorized && ready_result != 0;
    if (!probe_ready) {
        const ProbeStopResult cleanup = StopProbeUntilOwnedCleanupTerminal(
            process, pid, injected.module, dll, &main_release,
            &release_ledger, &action_ledger);
        applyCleanup(cleanup);
        if (result.targetProcessExited) {
            normalizeProcessExitedResult();
            publishResult("DETACHED_PROCESS_EXITED", 0);
            CloseHandle(stop_event);
            CloseHandle(process);
            return 0;
        }
        CloseHandle(stop_event);
        CloseHandle(process);
        const DWORD failure = ready_error == ERROR_SUCCESS ? ERROR_DLL_INIT_FAILED : ready_error;
        publishResult("PROBE_INIT_FAILED", failure);
        return static_cast<int>(failure);
    }
    result.probeReady = true;
    const DWORD module = injected.module;
    RestoreFaultLifecycleResult restore_fault_lifecycle{};
    std::wstring restore_fault_report_path;
    bool restore_fault_requested = false;
#if defined(GOD2_INJECTOR_ALLOW_SELF_TEST_TARGET)
    const bool extra_reference_requested =
        HasArg(argc, argv, L"--self-test-extra-reference");
    result.extraReferenceRequested = extra_reference_requested;
    bool extra_reference_loaded = false;
    OwnedModuleReleaseState extra_release{};
    if (extra_reference_requested) {
        const InjectionResult extra_reference = Inject(
            process, pid, dll, nullptr, nullptr);
        extra_reference_loaded = extra_reference.module == module &&
            extra_reference.error == ERROR_SUCCESS;
        result.extraReferenceLoaded = extra_reference_loaded;
        if (extra_reference_loaded)
            extra_release = MakeOwnedModuleReleaseState(pid, module, dll);
        if (!extra_reference_loaded) {
            const ProbeStopResult cleanup = StopProbeUntilOwnedCleanupTerminal(
                process, pid, injected.module, dll, &main_release,
                &release_ledger, &action_ledger);
            applyCleanup(cleanup);
            CloseHandle(stop_event);
            CloseHandle(process);
            publishResult("EXTRA_REFERENCE_FIXTURE_FAILED",
                extra_reference.error == ERROR_SUCCESS ? ERROR_BUSY :
                    extra_reference.error);
            return extra_reference.error == ERROR_SUCCESS ? ERROR_BUSY :
                static_cast<int>(extra_reference.error);
        }
    }
    restore_fault_requested =
        HasArg(argc, argv, L"--self-test-restore-fault");
    restore_fault_report_path = Arg(
        argc, argv, L"--restore-fault-report");
    if (restore_fault_requested) {
        restore_fault_lifecycle.requested = true;
        restore_fault_lifecycle.armExportResolved =
            RemoteExport(module, dll,
                "God2TraceProbeArmRestoreFaultSelfTest") != 0;
        DWORD arm_result = 0;
        DWORD arm_error = ERROR_SUCCESS;
        for (DWORD attempt = 0; attempt < 200u &&
             !restore_fault_lifecycle.armSucceeded; ++attempt) {
            bool arm_identity_authorized = false;
            const bool completed = CallOwnedModuleExport(
                process, main_release, dll,
                "God2TraceProbeArmRestoreFaultSelfTest", nullptr,
                &arm_result, &arm_error, &action_ledger,
                &arm_identity_authorized);
            restore_fault_lifecycle.armIdentityAuthorized =
                restore_fault_lifecycle.armIdentityAuthorized ||
                arm_identity_authorized;
            restore_fault_lifecycle.armCallCompleted =
                restore_fault_lifecycle.armCallCompleted || completed;
            restore_fault_lifecycle.armSucceeded = completed &&
                arm_result != 0;
            if (!restore_fault_lifecycle.armSucceeded) {
                if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0)
                    break;
                Sleep(25);
            }
        }
        if (!restore_fault_lifecycle.armSucceeded) {
            const ProbeStopResult cleanup =
                StopProbeUntilOwnedCleanupTerminal(
                    process, pid, module, dll, &main_release,
                    &release_ledger, &action_ledger);
            applyCleanup(cleanup);
            restore_fault_lifecycle.processId = pid;
            restore_fault_lifecycle.moduleBase = module;
            restore_fault_lifecycle.moduleAbsent =
                cleanup.moduleSnapshotVerified && cleanup.moduleAbsent;
            restore_fault_lifecycle.targetAliveAfter =
                WaitForSingleObject(process, 0) == WAIT_TIMEOUT;
            WriteRestoreFaultLifecycleReport(
                restore_fault_report_path, restore_fault_lifecycle);
            result.probeReady = false;
            CloseHandle(stop_event);
            CloseHandle(process);
            const DWORD failure = arm_error == ERROR_SUCCESS ?
                ERROR_INVALID_STATE : arm_error;
            publishResult("PROBE_INIT_FAILED", failure);
            return static_cast<int>(failure);
        }
    }
#else
    bool extra_reference_requested = false;
    bool extra_reference_loaded = false;
    OwnedModuleReleaseState extra_release{};
#endif
    // Status codes are protocol values, never target addresses. Keeping the
    // remote module base out of the result also avoids conflating a successful
    // attach with an error/status code.
    publishResult("ATTACHED", 0);
    HANDLE waits[] = {stop_event, process};
    const DWORD wait = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
    if (wait == WAIT_OBJECT_0 + 1) {
        normalizeProcessExitedResult();
        publishResult("DETACHED_PROCESS_EXITED", 0);
        CloseHandle(stop_event);
        CloseHandle(process);
        return 0;
    }
    ProbeStopResult stopped{};
    bool restore_fault_report_written = true;
    if (restore_fault_requested) {
        stopped = RunRestoreFaultLifecycle(
            process, pid, module, dll, &main_release, &release_ledger,
            &action_ledger,
            &restore_fault_lifecycle);
        restore_fault_report_written = WriteRestoreFaultLifecycleReport(
            restore_fault_report_path, restore_fault_lifecycle);
    } else {
        stopped = StopProbeUntilOwnedCleanupTerminal(
            process, pid, module, dll, &main_release, &release_ledger,
            &action_ledger,
            extra_reference_requested && extra_reference_loaded);
    }
    if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) {
        normalizeProcessExitedResult();
        publishResult("DETACHED_PROCESS_EXITED", 0);
        CloseHandle(stop_event);
        CloseHandle(process);
        return 0;
    }
    bool extra_reference_first_free_still_present = false;
    bool extra_reference_not_claimed_unloaded = false;
    bool extra_reference_released_then_absent = false;
    bool extra_reference_release_identity_verified = false;
    bool extra_reference_create_failure_retained_token = false;
    bool extra_reference_release_token_consumed_once = false;
    bool extra_reference_duplicate_release_suppressed = false;
    if (extra_reference_requested && extra_reference_loaded &&
        stopped.referenceReleased) {
        extra_reference_first_free_still_present =
            stopped.moduleSnapshotVerified && !stopped.moduleAbsent;
        extra_reference_not_claimed_unloaded = !stopped.unloaded;
        const DWORD created_before_extra = release_ledger.helperCreateCount;
#if defined(GOD2_INJECTOR_ALLOW_SELF_TEST_TARGET)
        volatile LONG injected_create_failure = 1;
        ProbeStopResult injected_failure{};
        const bool injected_release = IssueOwnedFreeLibrary(
            process, &extra_release, &release_ledger, &injected_failure,
            &injected_create_failure);
        extra_reference_create_failure_retained_token =
            !injected_release && extra_release.tokenAvailable &&
            !extra_release.helperCreated &&
            release_ledger.helperCreateCount == created_before_extra;
#endif
        ProbeStopResult extra_release_result{};
        const bool released = IssueOwnedFreeLibrary(
            process, &extra_release, &release_ledger,
            &extra_release_result);
        extra_reference_release_identity_verified =
            extra_release_result.releaseIdentityVerified;
        const DWORD created_after_extra = release_ledger.helperCreateCount;
        ProbeStopResult duplicate_release{};
        (void)IssueOwnedFreeLibrary(process, &extra_release,
            &release_ledger, &duplicate_release);
        extra_reference_release_token_consumed_once = released &&
            !extra_release.tokenAvailable &&
            created_after_extra == created_before_extra + 1u;
        extra_reference_duplicate_release_suppressed =
            release_ledger.helperCreateCount == created_after_extra;
        bool release_snapshot_verified = false;
        const bool released_absent = released && WaitForRemoteModuleAbsence(
            pid, module, dll, 5'000, &release_snapshot_verified);
        extra_reference_released_then_absent = released_absent &&
            release_snapshot_verified;
        if (extra_reference_first_free_still_present &&
            extra_reference_not_claimed_unloaded &&
            extra_reference_released_then_absent) {
            stopped.moduleSnapshotVerified = true;
            stopped.moduleAbsent = true;
            stopped.unloaded = true;
            stopped.error = ERROR_SUCCESS;
        } else if (stopped.error == ERROR_SUCCESS) {
            stopped.error = extra_release_result.error == ERROR_SUCCESS ?
                ERROR_BUSY : extra_release_result.error;
        }
    }
    result.extraReferenceNegativeFirstFreeLibraryStillPresent =
        extra_reference_first_free_still_present;
    result.extraReferenceNegativeNotClaimedUnloaded =
        extra_reference_not_claimed_unloaded;
    result.extraReferenceReleasedThenModuleAbsent =
        extra_reference_released_then_absent;
    applyCleanup(stopped);
    result.probeReady = false;
    if (result.targetProcessExited) {
        normalizeProcessExitedResult();
        publishResult("DETACHED_PROCESS_EXITED", 0);
        CloseHandle(stop_event);
        CloseHandle(process);
        return 0;
    }
    const bool detached = stopped.stopped && stopped.unloadSafe &&
        stopped.unloaded && stopped.moduleSnapshotVerified &&
        stopped.moduleAbsent &&
        (!extra_reference_requested ||
         (extra_reference_first_free_still_present &&
          extra_reference_not_claimed_unloaded &&
          extra_reference_released_then_absent &&
          extra_reference_release_identity_verified &&
          extra_reference_create_failure_retained_token &&
          extra_reference_release_token_consumed_once &&
          extra_reference_duplicate_release_suppressed));
    publishResult(detached ? "DETACHED" :
                (stopped.stopped ? "EVIDENCE_BLOCKED_RESIDENT_INACTIVE" :
                                   "DETACH_DEFERRED"),
                detached ? 0 : (stopped.error == ERROR_SUCCESS ?
                    ERROR_BUSY : stopped.error));
    CloseHandle(stop_event);
    CloseHandle(process);
    if (detached && (!restore_fault_requested ||
        (restore_fault_lifecycle.passed &&
         restore_fault_report_written))) return 0;
    if (detached) return ERROR_INVALID_DATA;
    return static_cast<int>(stopped.error == ERROR_SUCCESS ?
        ERROR_BUSY : stopped.error);
}

} // namespace

int wmain(int argc, wchar_t** argv) {
    const std::wstring result = Arg(argc, argv, L"--result");
    if (HasArg(argc, argv, L"--self-test")) {
        return SelfTestPayload(argc, argv, result);
    }
    if (!HasArg(argc, argv, L"--monitor")) {
        WriteResult(result, "REJECTED_MODE", ERROR_INVALID_PARAMETER);
        return ERROR_INVALID_PARAMETER;
    }
    return Monitor(argc, argv);
}
