#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <tlhelp32.h>

#include <cstdlib>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#pragma comment(lib, "ws2_32.lib")

namespace {

LONG MemoryReadExceptionFilter(DWORD exceptionCode) noexcept
{
    switch (exceptionCode) {
    case EXCEPTION_ACCESS_VIOLATION:
    case EXCEPTION_IN_PAGE_ERROR:
    case EXCEPTION_DATATYPE_MISALIGNMENT:
        return EXCEPTION_EXECUTE_HANDLER;
    default:
        return EXCEPTION_CONTINUE_SEARCH;
    }
}

enum class CaseKind {
    Send,
    WSASend,
    Recv,
    WSARecv,
    WSARecvOverlapped
};

struct ServerContext {
    SOCKET listenSocket;
    CaseKind kind;
    const std::vector<unsigned char>* payload;
    DWORD sendDelayMs{};
};

enum class CompletionMechanism {
    CompletionRoutine,
    WsaGetOverlappedResult,
    GetQueuedCompletionStatus,
    GetQueuedCompletionStatusEx
};

volatile LONG g_completionRoutineCalled{};
volatile LONG g_completionRoutineError{};
volatile LONG g_completionRoutineBytes{};
volatile LONG g_completionRoutineFlags{};
PVOID volatile g_completionRoutineOverlapped{};
HANDLE g_completionRoutineEvent{};
volatile LONG g_blockingReturnedCount{};
HANDLE g_blockingAllReturnedEvent{};

void CALLBACK completionRoutineFixture(DWORD error, DWORD transferred,
                                       LPWSAOVERLAPPED overlapped, DWORD flags)
{
    InterlockedExchange(&g_completionRoutineError, static_cast<LONG>(error));
    InterlockedExchange(&g_completionRoutineBytes,
                        static_cast<LONG>(transferred));
    InterlockedExchange(&g_completionRoutineFlags, static_cast<LONG>(flags));
    InterlockedExchangePointer(&g_completionRoutineOverlapped, overlapped);
    InterlockedIncrement(&g_completionRoutineCalled);
    if (g_completionRoutineEvent != nullptr) SetEvent(g_completionRoutineEvent);
}

using SendPtr = int(WSAAPI*)(SOCKET, const char*, int, int);
using RecvPtr = int(WSAAPI*)(SOCKET, char*, int, int);
using WSASendPtr = int(WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, DWORD,
                                LPWSAOVERLAPPED,
                                LPWSAOVERLAPPED_COMPLETION_ROUTINE);
using WSARecvPtr = int(WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, LPDWORD,
                                LPWSAOVERLAPPED,
                                LPWSAOVERLAPPED_COMPLETION_ROUTINE);
using SemanticSelfTestPtr = DWORD(WINAPI*)(void*);

unsigned char seedForCase(CaseKind kind)
{
    switch (kind) {
    case CaseKind::Send:
        return 0x11;
    case CaseKind::WSASend:
        return 0x22;
    case CaseKind::Recv:
        return 0x33;
    case CaseKind::WSARecv:
        return 0x44;
    case CaseKind::WSARecvOverlapped:
        return 0x55;
    }
    return 0x7F;
}

const char* caseName(CaseKind kind)
{
    switch (kind) {
    case CaseKind::Send:
        return "send";
    case CaseKind::WSASend:
        return "WSASend";
    case CaseKind::Recv:
        return "recv";
    case CaseKind::WSARecv:
        return "WSARecv";
    case CaseKind::WSARecvOverlapped:
        return "WSARecvOverlapped";
    }
    return "unknown";
}

struct IdentityNegativeSnapshot {
    unsigned char hotpatch[4][7]{};
    DWORD iatSlotRva[3]{};
    ULONG_PTR iatValue[3]{};
    ULONG_PTR iatExpected[3]{};
    char iatOwner[3][16]{};
    bool hotpatchCaptured[4]{};
    bool iatCaptured[3]{};
    bool probeModuleAbsent{};
    bool probePathAbsent{};
};

std::string hexBytes(const unsigned char* bytes, std::size_t length)
{
    static constexpr char digits[] = "0123456789ABCDEF";
    std::string result(length * 2u, '0');
    for (std::size_t index = 0; index < length; ++index) {
        result[index * 2u] = digits[(bytes[index] >> 4u) & 0x0Fu];
        result[index * 2u + 1u] = digits[bytes[index] & 0x0Fu];
    }
    return result;
}

bool captureHotpatchWindow(const char* apiName, unsigned char (&window)[7])
{
    HMODULE ws2 = GetModuleHandleA("ws2_32.dll");
    const auto target = ws2 != nullptr ? reinterpret_cast<const unsigned char*>(
        GetProcAddress(ws2, apiName)) : nullptr;
    if (target == nullptr) return false;
    __try {
        std::memcpy(window, target - 5, sizeof(window));
        return true;
    } __except(MemoryReadExceptionFilter(GetExceptionCode())) {
        std::memset(window, 0, sizeof(window));
        return false;
    }
}

bool captureMainIatSlot(const char* apiName, const char* expectedOwner,
                        DWORD* slotRva, ULONG_PTR* value, char* owner,
                        std::size_t ownerCapacity)
{
    if (slotRva == nullptr || value == nullptr || owner == nullptr ||
        ownerCapacity == 0) return false;
    auto* base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    if (base == nullptr) return false;
    __try {
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0)
            return false;
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(
            base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
        const IMAGE_DATA_DIRECTORY& directory =
            nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (directory.VirtualAddress == 0 || directory.Size == 0) return false;
        auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(
            base + directory.VirtualAddress);
        DWORD matches{};
        DWORD ownerMatches{};
        DWORD matchedRva{};
        ULONG_PTR matchedValue{};
        const char* matchedOwner{};
        for (; descriptor->Name != 0; ++descriptor) {
            const char* descriptorOwner = reinterpret_cast<const char*>(
                base + descriptor->Name);
            auto* names = descriptor->OriginalFirstThunk != 0 ?
                reinterpret_cast<IMAGE_THUNK_DATA*>(
                    base + descriptor->OriginalFirstThunk) :
                reinterpret_cast<IMAGE_THUNK_DATA*>(
                    base + descriptor->FirstThunk);
            auto* slots = reinterpret_cast<IMAGE_THUNK_DATA*>(
                base + descriptor->FirstThunk);
            for (; names->u1.AddressOfData != 0; ++names, ++slots) {
                if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
                const auto* importName = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(
                    base + names->u1.AddressOfData);
                if (std::strcmp(reinterpret_cast<const char*>(importName->Name),
                                apiName) != 0)
                    continue;
                matchedRva = static_cast<DWORD>(
                    reinterpret_cast<unsigned char*>(&slots->u1.Function) - base);
                matchedValue = static_cast<ULONG_PTR>(slots->u1.Function);
                ++matches;
                if (_stricmp(descriptorOwner, expectedOwner) == 0) {
                    ++ownerMatches;
                    matchedOwner = descriptorOwner;
                }
            }
        }
        if (matches == 1u && ownerMatches == 1u && matchedOwner != nullptr) {
            *slotRva = matchedRva;
            *value = matchedValue;
            strncpy_s(owner, ownerCapacity, matchedOwner, _TRUNCATE);
            return true;
        }
    } __except(MemoryReadExceptionFilter(GetExceptionCode())) {
        return false;
    }
    return false;
}

bool isExactModulePathAbsent(const wchar_t* expectedPath)
{
    if (expectedPath == nullptr || expectedPath[0] == L'\0') return false;
    wchar_t fullExpected[MAX_PATH]{};
    if (GetFullPathNameW(expectedPath, static_cast<DWORD>(_countof(fullExpected)),
                         fullExpected, nullptr) == 0)
        return false;
    HANDLE snapshot = CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    bool absent = true;
    if (Module32FirstW(snapshot, &entry) != FALSE) {
        do {
            wchar_t fullObserved[MAX_PATH]{};
            if (GetFullPathNameW(entry.szExePath,
                    static_cast<DWORD>(_countof(fullObserved)), fullObserved,
                    nullptr) != 0 && _wcsicmp(fullObserved, fullExpected) == 0) {
                absent = false;
                break;
            }
        } while (Module32NextW(snapshot, &entry) != FALSE);
        if (absent && GetLastError() != ERROR_NO_MORE_FILES)
            absent = false;
    } else {
        absent = false;
    }
    CloseHandle(snapshot);
    return absent;
}

IdentityNegativeSnapshot captureIdentityNegativeSnapshot(
    const wchar_t* expectedProbePath)
{
    static const char* hotpatchApis[] = {
        "send", "WSASend", "recv", "WSARecv"
    };
    static const char* iatApis[] = {
        "WSAGetOverlappedResult", "GetQueuedCompletionStatus",
        "GetQueuedCompletionStatusEx"
    };
    static const char* iatOwners[] = {
        "WS2_32.dll", "KERNEL32.dll", "KERNEL32.dll"
    };
    IdentityNegativeSnapshot snapshot{};
    for (std::size_t index = 0; index < _countof(hotpatchApis); ++index)
        snapshot.hotpatchCaptured[index] = captureHotpatchWindow(
            hotpatchApis[index], snapshot.hotpatch[index]);
    for (std::size_t index = 0; index < _countof(iatApis); ++index)
        snapshot.iatCaptured[index] = captureMainIatSlot(
            iatApis[index], iatOwners[index], &snapshot.iatSlotRva[index],
            &snapshot.iatValue[index], snapshot.iatOwner[index],
            sizeof(snapshot.iatOwner[index]));
    HMODULE ws2 = GetModuleHandleA("ws2_32.dll");
    HMODULE kernel32 = GetModuleHandleA("kernel32.dll");
    snapshot.iatExpected[0] = reinterpret_cast<ULONG_PTR>(
        ws2 != nullptr ? GetProcAddress(ws2, iatApis[0]) : nullptr);
    snapshot.iatExpected[1] = reinterpret_cast<ULONG_PTR>(
        kernel32 != nullptr ? GetProcAddress(kernel32, iatApis[1]) : nullptr);
    snapshot.iatExpected[2] = reinterpret_cast<ULONG_PTR>(
        kernel32 != nullptr ? GetProcAddress(kernel32, iatApis[2]) : nullptr);
    snapshot.probeModuleAbsent =
        GetModuleHandleA("God2ClientTraceProbe.dll") == nullptr &&
        GetModuleHandleA("God2PacketCaptureProbe.dll") == nullptr;
    snapshot.probePathAbsent = isExactModulePathAbsent(expectedProbePath);
    return snapshot;
}

bool identityNegativeSnapshotsEqual(const IdentityNegativeSnapshot& left,
                                    const IdentityNegativeSnapshot& right)
{
    if (!left.probeModuleAbsent || !right.probeModuleAbsent ||
        !left.probePathAbsent || !right.probePathAbsent) return false;
    for (std::size_t index = 0; index < _countof(left.hotpatch); ++index) {
        if (!left.hotpatchCaptured[index] || !right.hotpatchCaptured[index] ||
            std::memcmp(left.hotpatch[index], right.hotpatch[index],
                        sizeof(left.hotpatch[index])) != 0)
            return false;
    }
    for (std::size_t index = 0; index < _countof(left.iatValue); ++index) {
        if (!left.iatCaptured[index] || !right.iatCaptured[index] ||
            left.iatSlotRva[index] != right.iatSlotRva[index] ||
            _stricmp(left.iatOwner[index], right.iatOwner[index]) != 0 ||
            left.iatValue[index] != right.iatValue[index] ||
            left.iatExpected[index] == 0 ||
            left.iatExpected[index] != right.iatExpected[index] ||
            left.iatValue[index] != left.iatExpected[index])
            return false;
    }
    return true;
}

bool writeIdentityNegativeSnapshot(const wchar_t* path,
                                   const IdentityNegativeSnapshot& before,
                                   const IdentityNegativeSnapshot& after,
                                   DWORD continuousSampleCount,
                                   bool transientMutationObserved)
{
    if (path == nullptr || path[0] == L'\0') return false;
    static const char* hotpatchApis[] = {
        "send", "WSASend", "recv", "WSARecv"
    };
    static const char* iatApis[] = {
        "WSAGetOverlappedResult", "GetQueuedCompletionStatus",
        "GetQueuedCompletionStatusEx"
    };
    static constexpr unsigned char canonicalHotpatch[7] = {
        0xCCu, 0xCCu, 0xCCu, 0xCCu, 0xCCu, 0x8Bu, 0xFFu
    };
    bool allUnchanged = before.probeModuleAbsent && after.probeModuleAbsent &&
        before.probePathAbsent && after.probePathAbsent &&
        continuousSampleCount > 0 && !transientMutationObserved;
    std::string hotpatchRows;
    for (std::size_t index = 0; index < _countof(hotpatchApis); ++index) {
        const bool unchanged = before.hotpatchCaptured[index] &&
            after.hotpatchCaptured[index] && std::memcmp(
                before.hotpatch[index], after.hotpatch[index],
                sizeof(before.hotpatch[index])) == 0;
        const bool canonical = before.hotpatchCaptured[index] &&
            std::memcmp(before.hotpatch[index], canonicalHotpatch,
                        sizeof(canonicalHotpatch)) == 0;
        allUnchanged = allUnchanged && unchanged && canonical;
        if (!hotpatchRows.empty()) hotpatchRows += ',';
        hotpatchRows += "{\"Api\":\"";
        hotpatchRows += hotpatchApis[index];
        hotpatchRows += "\",\"Before\":\"" + hexBytes(
            before.hotpatch[index], sizeof(before.hotpatch[index])) +
            "\",\"After\":\"" + hexBytes(
            after.hotpatch[index], sizeof(after.hotpatch[index])) +
            "\",\"Canonical\":" + (canonical ? "true" : "false") +
            ",\"Unchanged\":" + (unchanged ? "true" : "false") + "}";
    }
    std::string iatRows;
    for (std::size_t index = 0; index < _countof(iatApis); ++index) {
        const bool unchanged = before.iatCaptured[index] &&
            after.iatCaptured[index] &&
            before.iatSlotRva[index] == after.iatSlotRva[index] &&
            _stricmp(before.iatOwner[index], after.iatOwner[index]) == 0 &&
            before.iatValue[index] == after.iatValue[index];
        const bool canonical = before.iatExpected[index] != 0 &&
            before.iatExpected[index] == after.iatExpected[index] &&
            before.iatValue[index] == before.iatExpected[index];
        allUnchanged = allUnchanged && unchanged && canonical;
        char row[512]{};
        const int written = _snprintf_s(row, sizeof(row), _TRUNCATE,
            "{\"Api\":\"%s\",\"Owner\":\"%s\",\"SlotRva\":%lu,"
            "\"Expected\":\"0x%08lX\",\"Before\":\"0x%08lX\","
            "\"After\":\"0x%08lX\",\"Canonical\":%s,"
            "\"Unchanged\":%s}", iatApis[index], before.iatOwner[index],
            static_cast<unsigned long>(before.iatSlotRva[index]),
            static_cast<unsigned long>(before.iatExpected[index]),
            static_cast<unsigned long>(before.iatValue[index]),
            static_cast<unsigned long>(after.iatValue[index]),
            canonical ? "true" : "false",
            unchanged ? "true" : "false");
        if (written <= 0) return false;
        if (!iatRows.empty()) iatRows += ',';
        iatRows += row;
    }
    char prefix[512]{};
    const int prefixWritten = _snprintf_s(prefix, sizeof(prefix), _TRUNCATE,
        "{\"SchemaId\":\"God2WrongIdentityNoHookSnapshot\","
        "\"SchemaVersion\":1,\"ProcessId\":%lu,"
        "\"ContinuousSampleCount\":%lu,"
        "\"TransientMutationObserved\":%s,"
        "\"ProbeModuleAbsentBefore\":%s,\"ProbeModuleAbsentAfter\":%s,"
        "\"ProbePathAbsentBefore\":%s,\"ProbePathAbsentAfter\":%s,"
        "\"HotpatchRows\":[", static_cast<unsigned long>(GetCurrentProcessId()),
        static_cast<unsigned long>(continuousSampleCount),
        transientMutationObserved ? "true" : "false",
        before.probeModuleAbsent ? "true" : "false",
        after.probeModuleAbsent ? "true" : "false",
        before.probePathAbsent ? "true" : "false",
        after.probePathAbsent ? "true" : "false");
    if (prefixWritten <= 0) return false;
    std::string wire = prefix;
    wire += hotpatchRows + "],\"IatRows\":[" + iatRows +
        "],\"AllUnchanged\":" + (allUnchanged ? "true" : "false") + "}\n";
    wchar_t temporary[MAX_PATH]{};
    if (swprintf_s(temporary, L"%s.tmp-%lu", path,
                   static_cast<unsigned long>(GetCurrentProcessId())) <= 0)
        return false;
    HANDLE file = CreateFileW(temporary, GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH,
                              nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written{};
    const bool writeOk = WriteFile(file, wire.data(),
        static_cast<DWORD>(wire.size()), &written, nullptr) != FALSE &&
        written == wire.size() && FlushFileBuffers(file) != FALSE;
    CloseHandle(file);
    if (!writeOk || MoveFileExW(temporary, path,
            MOVEFILE_WRITE_THROUGH) == FALSE) {
        DeleteFileW(temporary);
        return false;
    }
    return allUnchanged;
}

bool runIdentityNegativeSnapshotIfRequested()
{
    wchar_t path[MAX_PATH]{};
    wchar_t expectedProbePath[MAX_PATH]{};
    const DWORD pathLength = GetEnvironmentVariableW(
        L"GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_PATH", path,
        static_cast<DWORD>(_countof(path)));
    if (pathLength == 0) return true;
    if (pathLength >= _countof(path)) return false;
    const DWORD probePathLength = GetEnvironmentVariableW(
        L"GOD2_TRACE_IDENTITY_NEGATIVE_PROBE_PATH", expectedProbePath,
        static_cast<DWORD>(_countof(expectedProbePath)));
    if (probePathLength == 0 || probePathLength >= _countof(expectedProbePath))
        return false;
    char requestName[256]{};
    char completeName[256]{};
    char baselineReadyName[256]{};
    if (GetEnvironmentVariableA(
            "GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_REQUEST_EVENT",
            requestName, static_cast<DWORD>(sizeof(requestName))) == 0 ||
        GetEnvironmentVariableA(
            "GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_COMPLETE_EVENT",
            completeName, static_cast<DWORD>(sizeof(completeName))) == 0 ||
        GetEnvironmentVariableA(
            "GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_BASELINE_READY_EVENT",
            baselineReadyName,
            static_cast<DWORD>(sizeof(baselineReadyName))) == 0)
        return false;
    HANDLE request = OpenEventA(SYNCHRONIZE, FALSE, requestName);
    HANDLE complete = OpenEventA(EVENT_MODIFY_STATE, FALSE, completeName);
    HANDLE baselineReady = OpenEventA(
        EVENT_MODIFY_STATE, FALSE, baselineReadyName);
    if (request == nullptr || complete == nullptr || baselineReady == nullptr) {
        if (request != nullptr) CloseHandle(request);
        if (complete != nullptr) CloseHandle(complete);
        if (baselineReady != nullptr) CloseHandle(baselineReady);
        return false;
    }
    const IdentityNegativeSnapshot before = captureIdentityNegativeSnapshot(
        expectedProbePath);
    const bool baselinePublished = SetEvent(baselineReady) != FALSE;
    DWORD continuousSampleCount{};
    bool transientMutationObserved = false;
    bool requested = false;
    const ULONGLONG deadline = GetTickCount64() + 30'000;
    while (baselinePublished && GetTickCount64() < deadline) {
        const DWORD wait = WaitForSingleObject(request, 1);
        if (wait == WAIT_OBJECT_0) {
            requested = true;
            break;
        }
        if (wait != WAIT_TIMEOUT) break;
        const IdentityNegativeSnapshot sample =
            captureIdentityNegativeSnapshot(expectedProbePath);
        ++continuousSampleCount;
        if (!identityNegativeSnapshotsEqual(before, sample))
            transientMutationObserved = true;
    }
    const IdentityNegativeSnapshot after = captureIdentityNegativeSnapshot(
        expectedProbePath);
    if (!identityNegativeSnapshotsEqual(before, after))
        transientMutationObserved = true;
    const bool written = requested && writeIdentityNegativeSnapshot(
        path, before, after, continuousSampleCount,
        transientMutationObserved);
    const bool signaled = SetEvent(complete) != FALSE;
    CloseHandle(complete);
    CloseHandle(request);
    CloseHandle(baselineReady);
    return written && signaled;
}

bool parseCase(int argc, char** argv, CaseKind& kind)
{
    const char* value = nullptr;
    char envCase[32]{};
    if (GetEnvironmentVariableA("GOD2_TRACE_SELFTEST_CASE", envCase, static_cast<DWORD>(sizeof(envCase))) > 0) {
        value = envCase;
    }
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--case") == 0 && i + 1 < argc) {
            value = argv[i + 1];
            break;
        }
        if (std::strncmp(argv[i], "--case=", 7) == 0) {
            value = argv[i] + 7;
            break;
        }
    }
    if (value == nullptr && argc > 1) {
        value = argv[1];
    }
    if (value == nullptr) {
        return false;
    }

    if (_stricmp(value, "send") == 0) {
        kind = CaseKind::Send;
        return true;
    }
    if (_stricmp(value, "WSASend") == 0) {
        kind = CaseKind::WSASend;
        return true;
    }
    if (_stricmp(value, "recv") == 0) {
        kind = CaseKind::Recv;
        return true;
    }
    if (_stricmp(value, "WSARecv") == 0) {
        kind = CaseKind::WSARecv;
        return true;
    }
    if (_stricmp(value, "WSARecvOverlapped") == 0) {
        kind = CaseKind::WSARecvOverlapped;
        return true;
    }
    return false;
}

DWORD environmentDelay(const char* name)
{
    char value[32]{};
    if (GetEnvironmentVariableA(name, value, static_cast<DWORD>(sizeof(value))) == 0) {
        return 0;
    }
    char* end{};
    const unsigned long parsed = std::strtoul(value, &end, 10);
    return end != value && *end == '\0' ? static_cast<DWORD>(parsed) : 0;
}

std::vector<unsigned char> makePayload(CaseKind kind)
{
    const int length = (kind == CaseKind::Send || kind == CaseKind::WSASend) ? 208 : 64;
    std::vector<unsigned char> payload(static_cast<size_t>(length));
    const unsigned char seed = seedForCase(kind);
    payload[0] = static_cast<unsigned char>(length & 0xFF);
    payload[1] = static_cast<unsigned char>((length >> 8) & 0xFF);
    payload[2] = 0x42u; // non-authentication protocol fixture
    for (int i = 3; i + 1 < length; ++i) {
        payload[static_cast<size_t>(i)] = static_cast<unsigned char>((seed + i * 31) & 0xFF);
    }
    unsigned char checksum{};
    for (int i = 0; i + 1 < length; ++i) {
        checksum = static_cast<unsigned char>(checksum +
            static_cast<unsigned char>(payload[static_cast<size_t>(i)] + 0x3Cu));
    }
    payload.back() = checksum;
    return payload;
}

bool sendExact(SOCKET socketValue, const unsigned char* buffer, int length)
{
    int offset = 0;
    while (offset < length) {
        const int sent = send(socketValue, reinterpret_cast<const char*>(buffer + offset), length - offset, 0);
        if (sent <= 0) {
            return false;
        }
        offset += sent;
    }
    return true;
}

bool recvExact(SOCKET socketValue, unsigned char* buffer, int length)
{
    int offset = 0;
    while (offset < length) {
        const int received = recv(socketValue, reinterpret_cast<char*>(buffer + offset), length - offset, 0);
        if (received <= 0) {
            return false;
        }
        offset += received;
    }
    return true;
}

DWORD WINAPI serverThread(void* parameter)
{
    auto* context = reinterpret_cast<ServerContext*>(parameter);
    SOCKET accepted = accept(context->listenSocket, nullptr, nullptr);
    if (accepted == INVALID_SOCKET) {
        return 10;
    }

    const auto& payload = *context->payload;
    if (context->kind == CaseKind::Send || context->kind == CaseKind::WSASend) {
        std::vector<unsigned char> received(payload.size());
        if (!recvExact(accepted, received.data(), static_cast<int>(received.size()))) {
            closesocket(accepted);
            return 11;
        }
        if (std::memcmp(received.data(), payload.data(), payload.size()) != 0) {
            closesocket(accepted);
            return 12;
        }
    } else {
        // The overlapped case must observe WSA_IO_PENDING rather than merely
        // using a non-null OVERLAPPED for an operation that already completed.
        Sleep(context->sendDelayMs != 0 ? context->sendDelayMs :
            (context->kind == CaseKind::WSARecvOverlapped ? 500 : 100));
        if (!sendExact(accepted, payload.data(), static_cast<int>(payload.size()))) {
            closesocket(accepted);
            return 13;
        }
        shutdown(accepted, SD_SEND);
    }

    closesocket(accepted);
    return 0;
}

bool setupLoopback(SOCKET& listener, sockaddr_in& address)
{
    listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == INVALID_SOCKET) {
        std::printf("listener socket failed %d\n", WSAGetLastError());
        return false;
    }

    address = {};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = 0;
    if (bind(listener, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0 || listen(listener, 1) != 0) {
        std::printf("bind/listen failed %d\n", WSAGetLastError());
        closesocket(listener);
        listener = INVALID_SOCKET;
        return false;
    }

    int addressLength = sizeof(address);
    if (getsockname(listener, reinterpret_cast<sockaddr*>(&address), &addressLength) != 0) {
        std::printf("getsockname failed %d\n", WSAGetLastError());
        closesocket(listener);
        listener = INVALID_SOCKET;
        return false;
    }
    return true;
}

bool runCompletionMechanism(CompletionMechanism mechanism)
{
    SOCKET listener = INVALID_SOCKET;
    sockaddr_in address{};
    if (!setupLoopback(listener, address)) return false;
    std::vector<unsigned char> payload = makePayload(
        CaseKind::WSARecvOverlapped);
    ServerContext server{listener, CaseKind::WSARecvOverlapped, &payload, 500};
    HANDLE serverHandle = CreateThread(nullptr, 0, serverThread, &server, 0,
                                       nullptr);
    if (serverHandle == nullptr) {
        closesocket(listener);
        return false;
    }
    SOCKET client = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    bool passed = client != INVALID_SOCKET &&
        connect(client, reinterpret_cast<sockaddr*>(&address),
                sizeof(address)) == 0;
    HANDLE completionPort = nullptr;
    HANDLE overlappedEvent = nullptr;
    std::vector<unsigned char> received(payload.size());
    WSAOVERLAPPED overlapped{};
    if (passed && (mechanism == CompletionMechanism::GetQueuedCompletionStatus ||
                   mechanism == CompletionMechanism::GetQueuedCompletionStatusEx)) {
        completionPort = CreateIoCompletionPort(
            reinterpret_cast<HANDLE>(client), nullptr, 0x43325047u, 0);
        passed = completionPort != nullptr;
    }
    if (passed && mechanism == CompletionMechanism::WsaGetOverlappedResult) {
        overlappedEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        overlapped.hEvent = overlappedEvent;
        passed = overlappedEvent != nullptr;
    }
    if (passed && mechanism == CompletionMechanism::CompletionRoutine) {
        g_completionRoutineEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        InterlockedExchange(&g_completionRoutineCalled, 0);
        InterlockedExchange(&g_completionRoutineError, ERROR_IO_PENDING);
        InterlockedExchange(&g_completionRoutineBytes, 0);
        InterlockedExchange(&g_completionRoutineFlags, -1);
        InterlockedExchangePointer(&g_completionRoutineOverlapped, nullptr);
        passed = g_completionRoutineEvent != nullptr;
    }
    WSABUF buffer{};
    buffer.buf = reinterpret_cast<char*>(received.data());
    buffer.len = static_cast<ULONG>(received.size());
    DWORD flags{};
    DWORD submittedBytes{};
    int submitResult = SOCKET_ERROR;
    int submitError = WSAEINVAL;
    if (passed) {
        submitResult = WSARecv(client, &buffer, 1u, &submittedBytes, &flags,
            &overlapped,
            mechanism == CompletionMechanism::CompletionRoutine ?
                &completionRoutineFixture : nullptr);
        submitError = submitResult == SOCKET_ERROR ? WSAGetLastError() :
            ERROR_SUCCESS;
        passed = submitResult == SOCKET_ERROR && submitError == WSA_IO_PENDING;
    }
    DWORD completedBytes{};
    if (passed) {
        if (mechanism == CompletionMechanism::CompletionRoutine) {
            const ULONGLONG deadline = GetTickCount64() + 5'000;
            while (InterlockedCompareExchange(
                       &g_completionRoutineCalled, 0, 0) == 0 &&
                   GetTickCount64() < deadline) {
                SleepEx(25, TRUE);
            }
            completedBytes = static_cast<DWORD>(InterlockedCompareExchange(
                &g_completionRoutineBytes, 0, 0));
            passed = InterlockedCompareExchange(
                         &g_completionRoutineCalled, 0, 0) == 1 &&
                InterlockedCompareExchange(
                    &g_completionRoutineError, 0, 0) == ERROR_SUCCESS &&
                InterlockedCompareExchange(
                    &g_completionRoutineFlags, 0, 0) == 0 &&
                InterlockedCompareExchangePointer(
                    &g_completionRoutineOverlapped, nullptr, nullptr) ==
                    &overlapped;
            // Keep this thread alertable after the first completion and demand
            // a bounded quiet period.  This catches a delayed duplicate APC
            // that an exactly-once forwarder must never queue.
            const ULONGLONG quietDeadline = GetTickCount64() + 500;
            while (GetTickCount64() < quietDeadline) SleepEx(25, TRUE);
            passed = passed && InterlockedCompareExchange(
                &g_completionRoutineCalled, 0, 0) == 1;
        } else if (mechanism == CompletionMechanism::WsaGetOverlappedResult) {
            DWORD resultFlags{};
            passed = WSAGetOverlappedResult(client, &overlapped,
                &completedBytes, TRUE, &resultFlags) != FALSE;
        } else if (mechanism == CompletionMechanism::GetQueuedCompletionStatus) {
            ULONG_PTR completionKey{};
            LPOVERLAPPED completedOverlapped{};
            passed = completionPort != nullptr && GetQueuedCompletionStatus(
                completionPort, &completedBytes, &completionKey,
                &completedOverlapped, 5'000) != FALSE &&
                completionKey == 0x43325047u &&
                completedOverlapped == &overlapped;
        } else {
            OVERLAPPED_ENTRY entries[2]{};
            ULONG removed{};
            passed = completionPort != nullptr && GetQueuedCompletionStatusEx(
                completionPort, entries,
                static_cast<ULONG>(_countof(entries)), &removed, 5'000, FALSE) !=
                FALSE && removed == 1u &&
                entries[0].lpOverlapped == &overlapped &&
                entries[0].lpCompletionKey == 0x43325047u;
            if (passed) completedBytes = entries[0].dwNumberOfBytesTransferred;
        }
    }
    passed = passed && completedBytes == payload.size() &&
        std::memcmp(received.data(), payload.data(), payload.size()) == 0;
    if (g_completionRoutineEvent != nullptr) {
        CloseHandle(g_completionRoutineEvent);
        g_completionRoutineEvent = nullptr;
    }
    if (overlappedEvent != nullptr) CloseHandle(overlappedEvent);
    if (completionPort != nullptr) CloseHandle(completionPort);
    if (client != INVALID_SOCKET) closesocket(client);
    closesocket(listener);
    const DWORD serverWait = WaitForSingleObject(serverHandle, 5'000);
    DWORD serverExit = ERROR_GEN_FAILURE;
    GetExitCodeThread(serverHandle, &serverExit);
    CloseHandle(serverHandle);
    return passed && serverWait == WAIT_OBJECT_0 && serverExit == 0;
}

struct PendingAtStopContext {
    HANDLE ready{};
    volatile LONG passed{};
};

DWORD WINAPI pendingAtStopThread(void* parameter)
{
    auto& pendingContext = *static_cast<PendingAtStopContext*>(parameter);
    SOCKET listener = INVALID_SOCKET;
    sockaddr_in address{};
    if (!setupLoopback(listener, address)) return 1u;
    std::vector<unsigned char> payload = makePayload(
        CaseKind::WSARecvOverlapped);
    // Keep the request pending longer than the probe's first bounded 5-second
    // Stop drain, while remaining inside the injector's retry window.
    ServerContext server{listener, CaseKind::WSARecvOverlapped, &payload, 10'000};
    HANDLE serverHandle = CreateThread(nullptr, 0, serverThread, &server, 0,
                                       nullptr);
    SOCKET client = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    HANDLE completionPort = client != INVALID_SOCKET ? CreateIoCompletionPort(
        reinterpret_cast<HANDLE>(client), nullptr, 0x53544F50u, 0) : nullptr;
    bool passed = serverHandle != nullptr && client != INVALID_SOCKET &&
        completionPort != nullptr && connect(client,
            reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0;
    std::vector<unsigned char> received(payload.size());
    WSABUF buffer{static_cast<ULONG>(received.size()),
                  reinterpret_cast<char*>(received.data())};
    WSAOVERLAPPED overlapped{};
    DWORD flags{};
    DWORD immediate{};
    if (passed) {
        const int result = WSARecv(client, &buffer, 1u, &immediate, &flags,
                                   &overlapped, nullptr);
        passed = result == SOCKET_ERROR && WSAGetLastError() == WSA_IO_PENDING;
    }
    if (pendingContext.ready != nullptr) SetEvent(pendingContext.ready);
    DWORD completed{};
    ULONG_PTR completionKey{};
    LPOVERLAPPED completedOverlapped{};
    if (passed) {
        passed = GetQueuedCompletionStatus(completionPort, &completed,
            &completionKey, &completedOverlapped, 12'000) != FALSE &&
            completedOverlapped == &overlapped &&
            completionKey == 0x53544F50u && completed == payload.size() &&
            std::memcmp(received.data(), payload.data(), payload.size()) == 0;
    }
    if (completionPort != nullptr) CloseHandle(completionPort);
    if (client != INVALID_SOCKET) closesocket(client);
    closesocket(listener);
    if (serverHandle != nullptr) {
        WaitForSingleObject(serverHandle, 5'000);
        CloseHandle(serverHandle);
    }
    InterlockedExchange(&pendingContext.passed, passed ? 1 : 0);
    return passed ? 0u : 1u;
}

enum class BlockingShutdownKind : unsigned char {
    GetQueuedCompletionStatus,
    GetQueuedCompletionStatusEx,
    Recv,
    WsaGetOverlappedResult
};

struct BlockingShutdownContext {
    BlockingShutdownKind kind{};
    HANDLE entered{};
    HANDLE releaseEvent{};
    HANDLE completionPort{};
    SOCKET client{INVALID_SOCKET};
    SOCKET peer{INVALID_SOCKET};
    WSAOVERLAPPED overlapped{};
    OVERLAPPED completionToken{};
    OVERLAPPED reuseToken{};
    HANDLE overlappedEvent{};
    WSABUF buffer{};
    unsigned char storage[64]{};
    LONG observedResult{};
    DWORD observedTransferred{};
    DWORD observedFlags{};
    DWORD observedRemoved{};
    ULONG_PTR observedCompletionKey{};
    ULONG_PTR inputOverlapped{};
    ULONG_PTR outputOverlapped{};
    ULONG_PTR inputResource{};
    ULONG_PTR baselineResource{};
    DWORD entryLastError{};
    DWORD baselineLastError{};
    DWORD observedLastError{};
    bool observedPayloadMatched{};
    volatile LONG passed{};
    volatile LONG reusable{};
};

constexpr ULONG_PTR kNaturalCompletionKey =
    static_cast<ULONG_PTR>(0x47324E52u); // "G2NR"
constexpr DWORD kNaturalCompletionBytes = 7u;
constexpr char kNaturalRecvByte = 'R';
constexpr char kNaturalWsaByte = 'W';

const char* blockingShutdownKindName(BlockingShutdownKind kind)
{
    switch (kind) {
    case BlockingShutdownKind::GetQueuedCompletionStatus:
        return "GetQueuedCompletionStatus";
    case BlockingShutdownKind::GetQueuedCompletionStatusEx:
        return "GetQueuedCompletionStatusEx";
    case BlockingShutdownKind::Recv:
        return "recv";
    case BlockingShutdownKind::WsaGetOverlappedResult:
        return "WSAGetOverlappedResult";
    }
    return "Unknown";
}

bool createConnectedSocketPair(SOCKET* client, SOCKET* peer)
{
    if (client == nullptr || peer == nullptr) return false;
    *client = INVALID_SOCKET;
    *peer = INVALID_SOCKET;
    SOCKET listener = INVALID_SOCKET;
    sockaddr_in address{};
    if (!setupLoopback(listener, address)) return false;
    *client = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (*client != INVALID_SOCKET && connect(*client,
            reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0)
        *peer = accept(listener, nullptr, nullptr);
    closesocket(listener);
    if (*client == INVALID_SOCKET || *peer == INVALID_SOCKET) {
        if (*client != INVALID_SOCKET) closesocket(*client);
        if (*peer != INVALID_SOCKET) closesocket(*peer);
        *client = INVALID_SOCKET;
        *peer = INVALID_SOCKET;
        return false;
    }
    return true;
}

DWORD WINAPI blockingShutdownThread(void* parameter)
{
    auto& context = *static_cast<BlockingShutdownContext*>(parameter);
    if (context.entered != nullptr) SetEvent(context.entered);
    bool passed = false;
    if (context.kind == BlockingShutdownKind::GetQueuedCompletionStatus) {
        DWORD transferred{};
        ULONG_PTR completionKey{};
        LPOVERLAPPED overlapped{};
        SetLastError(context.entryLastError);
        const BOOL result = GetQueuedCompletionStatus(context.completionPort,
            &transferred, &completionKey, &overlapped, INFINITE);
        context.observedResult = result != FALSE ? 1 : 0;
        context.observedTransferred = transferred;
        context.observedCompletionKey = completionKey;
        context.outputOverlapped = reinterpret_cast<ULONG_PTR>(overlapped);
        context.observedLastError = GetLastError();
        context.observedPayloadMatched = overlapped == &context.completionToken;
        passed = result != FALSE && transferred == kNaturalCompletionBytes &&
            completionKey == kNaturalCompletionKey &&
            overlapped == &context.completionToken;
    } else if (context.kind ==
               BlockingShutdownKind::GetQueuedCompletionStatusEx) {
        OVERLAPPED_ENTRY entries[2]{};
        ULONG removed{};
        SetLastError(context.entryLastError);
        const BOOL result = GetQueuedCompletionStatusEx(
            context.completionPort, entries,
            static_cast<ULONG>(_countof(entries)), &removed, INFINITE, FALSE);
        context.observedResult = result != FALSE ? 1 : 0;
        context.observedRemoved = removed;
        context.observedTransferred = removed == 1u ?
            entries[0].dwNumberOfBytesTransferred : 0u;
        context.observedCompletionKey = removed == 1u ?
            entries[0].lpCompletionKey : 0u;
        context.outputOverlapped = removed == 1u ?
            reinterpret_cast<ULONG_PTR>(entries[0].lpOverlapped) : 0u;
        context.observedLastError = GetLastError();
        context.observedPayloadMatched = removed == 1u &&
            entries[0].lpOverlapped == &context.completionToken;
        passed = result != FALSE && removed == 1u &&
            entries[0].dwNumberOfBytesTransferred == kNaturalCompletionBytes &&
            entries[0].lpCompletionKey == kNaturalCompletionKey &&
            entries[0].lpOverlapped == &context.completionToken;
    } else if (context.kind == BlockingShutdownKind::Recv) {
        char value{};
        WSASetLastError(static_cast<int>(context.entryLastError));
        const int result = recv(context.client, &value, 1, 0);
        context.observedResult = result;
        context.observedTransferred = result > 0 ?
            static_cast<DWORD>(result) : 0u;
        context.observedLastError = static_cast<DWORD>(WSAGetLastError());
        context.observedPayloadMatched = value == kNaturalRecvByte;
        passed = result == 1 && value == kNaturalRecvByte;
    } else {
        DWORD transferred{};
        DWORD flags{};
        context.inputOverlapped = reinterpret_cast<ULONG_PTR>(
            &context.overlapped);
        WSASetLastError(static_cast<int>(context.entryLastError));
        const BOOL result = WSAGetOverlappedResult(context.client,
            &context.overlapped, &transferred, TRUE, &flags);
        context.observedResult = result != FALSE ? 1 : 0;
        context.observedTransferred = transferred;
        context.observedFlags = flags;
        context.observedLastError = static_cast<DWORD>(WSAGetLastError());
        context.observedPayloadMatched = context.storage[0] ==
            static_cast<unsigned char>(kNaturalWsaByte);
        passed = result != FALSE && transferred == 1u &&
            flags == 0u && context.observedPayloadMatched;
    }
    InterlockedExchange(&context.passed, passed ? 1 : 0);
    if (InterlockedIncrement(&g_blockingReturnedCount) == 4 &&
        g_blockingAllReturnedEvent != nullptr)
        SetEvent(g_blockingAllReturnedEvent);
    return passed ? 0u : 1u;
}

DWORD WINAPI naturallyReleaseBlockingCall(void* parameter)
{
    auto& context = *static_cast<BlockingShutdownContext*>(parameter);
    // The external controller releases these calls only after it has verified
    // that FreeLibrary completed and the exact probe module is absent.  A
    // fixed delay would allow Stop to wait for natural return and create a
    // false-positive unload result.
    if (context.releaseEvent == nullptr ||
        WaitForSingleObject(context.releaseEvent, 120'000) != WAIT_OBJECT_0)
        return 1u;
    bool released = false;
    if (context.kind == BlockingShutdownKind::GetQueuedCompletionStatus ||
        context.kind == BlockingShutdownKind::GetQueuedCompletionStatusEx) {
        released = PostQueuedCompletionStatus(context.completionPort,
            kNaturalCompletionBytes, kNaturalCompletionKey,
            &context.completionToken) != FALSE;
    } else if (context.kind == BlockingShutdownKind::Recv) {
        released = send(context.peer, &kNaturalRecvByte, 1, 0) == 1;
    } else {
        released = send(context.peer, &kNaturalWsaByte, 1, 0) == 1;
    }
    return released ? 0u : 1u;
}

bool verifyBlockingCallResourceStillUsable(BlockingShutdownContext* context)
{
    if (context == nullptr) return false;
    context->baselineResource = context->kind ==
            BlockingShutdownKind::GetQueuedCompletionStatus ||
            context->kind == BlockingShutdownKind::GetQueuedCompletionStatusEx ?
        reinterpret_cast<ULONG_PTR>(context->completionPort) :
        static_cast<ULONG_PTR>(context->client);
    if (context->kind == BlockingShutdownKind::GetQueuedCompletionStatus) {
        if (PostQueuedCompletionStatus(context->completionPort, 3u,
                kNaturalCompletionKey, &context->reuseToken) == FALSE)
            return false;
        DWORD bytes{};
        ULONG_PTR key{};
        LPOVERLAPPED overlapped{};
        SetLastError(context->entryLastError);
        const BOOL result = GetQueuedCompletionStatus(context->completionPort,
            &bytes, &key, &overlapped, 1'000);
        context->baselineLastError = GetLastError();
        return result != FALSE && bytes == 3u &&
               key == kNaturalCompletionKey &&
               overlapped == &context->reuseToken;
    }
    if (context->kind == BlockingShutdownKind::GetQueuedCompletionStatusEx) {
        if (PostQueuedCompletionStatus(context->completionPort, 3u,
                kNaturalCompletionKey, &context->reuseToken) == FALSE)
            return false;
        OVERLAPPED_ENTRY entry{};
        ULONG removed{};
        SetLastError(context->entryLastError);
        const BOOL result = GetQueuedCompletionStatusEx(context->completionPort,
            &entry, 1u, &removed, 1'000, FALSE);
        context->baselineLastError = GetLastError();
        return result != FALSE && removed == 1u &&
               entry.dwNumberOfBytesTransferred == 3u &&
               entry.lpCompletionKey == kNaturalCompletionKey &&
               entry.lpOverlapped == &context->reuseToken;
    }
    constexpr char reuseByte = 'U';
    if (context->kind == BlockingShutdownKind::Recv) {
        char observed{};
        if (send(context->peer, &reuseByte, 1, 0) != 1) return false;
        WSASetLastError(static_cast<int>(context->entryLastError));
        const int result = recv(context->client, &observed, 1, 0);
        context->baselineLastError = static_cast<DWORD>(WSAGetLastError());
        return result == 1 && observed == reuseByte;
    }
    HANDLE event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (event == nullptr) return false;
    OVERLAPPED overlapped{};
    overlapped.hEvent = event;
    char observed{};
    WSABUF buffer{1u, &observed};
    DWORD flags{};
    DWORD immediate{};
    const int pendingResult = WSARecv(context->client, &buffer, 1u,
        &immediate, &flags, &overlapped, nullptr);
    const bool pending = pendingResult == SOCKET_ERROR &&
        WSAGetLastError() == WSA_IO_PENDING;
    const bool sent = pending && send(context->peer, &reuseByte, 1, 0) == 1;
    DWORD transferred{};
    flags = 0;
    WSASetLastError(static_cast<int>(context->entryLastError));
    const BOOL result = sent ? WSAGetOverlappedResult(context->client,
        &overlapped, &transferred, TRUE, &flags) : FALSE;
    context->baselineLastError = static_cast<DWORD>(WSAGetLastError());
    CloseHandle(event);
    return result != FALSE && transferred == 1u && flags == 0u &&
        observed == reuseByte;
}

bool initializeBlockingShutdownContext(BlockingShutdownContext* context,
                                       BlockingShutdownKind kind,
                                       HANDLE releaseEvent)
{
    if (context == nullptr) return false;
    if (releaseEvent == nullptr) return false;
    *context = BlockingShutdownContext{};
    context->kind = kind;
    context->entryLastError = 0x51A10001u +
        static_cast<DWORD>(kind);
    context->releaseEvent = releaseEvent;
    context->entered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (context->entered == nullptr) return false;
    if (kind == BlockingShutdownKind::GetQueuedCompletionStatus ||
        kind == BlockingShutdownKind::GetQueuedCompletionStatusEx) {
        context->completionPort = CreateIoCompletionPort(
            INVALID_HANDLE_VALUE, nullptr, 0, 1);
        context->inputResource = reinterpret_cast<ULONG_PTR>(
            context->completionPort);
        return context->completionPort != nullptr;
    }
    if (!createConnectedSocketPair(&context->client, &context->peer))
        return false;
    context->inputResource = static_cast<ULONG_PTR>(context->client);
    if (kind == BlockingShutdownKind::WsaGetOverlappedResult) {
        context->overlappedEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (context->overlappedEvent == nullptr) return false;
        context->overlapped.hEvent = context->overlappedEvent;
        context->buffer.buf = reinterpret_cast<char*>(context->storage);
        context->buffer.len = static_cast<ULONG>(sizeof(context->storage));
        DWORD flags{};
        DWORD immediate{};
        const int result = WSARecv(context->client, &context->buffer, 1u,
            &immediate, &flags, &context->overlapped, nullptr);
        return result == SOCKET_ERROR && WSAGetLastError() == WSA_IO_PENDING;
    }
    return true;
}

void closeBlockingShutdownContext(BlockingShutdownContext* context)
{
    if (context == nullptr) return;
    if (context->completionPort != nullptr)
        CloseHandle(context->completionPort);
    if (context->overlappedEvent != nullptr)
        CloseHandle(context->overlappedEvent);
    if (context->client != INVALID_SOCKET) closesocket(context->client);
    if (context->peer != INVALID_SOCKET) closesocket(context->peer);
    if (context->entered != nullptr) CloseHandle(context->entered);
    *context = BlockingShutdownContext{};
}

} // namespace

int main(int argc, char** argv)
{
    CaseKind kind{};
    if (!parseCase(argc, argv, kind)) {
        std::printf("usage: God2TraceSelfTestClient.exe --case send|WSASend|recv|WSARecv|WSARecvOverlapped\n");
        return 1;
    }

    if (!runIdentityNegativeSnapshotIfRequested()) {
        std::printf("wrong-identity no-hook snapshot failed\n");
        return 19;
    }
    const DWORD startDelay = environmentDelay("GOD2_TRACE_SELFTEST_START_DELAY_MS");
    if (startDelay > 0) {
        Sleep(startDelay);
    }

    HMODULE probe = GetModuleHandleA("God2ClientTraceProbeTest.dll");
    if (probe == nullptr) probe = GetModuleHandleA("God2ClientTraceProbe.dll");
    if (probe == nullptr) probe = GetModuleHandleA("God2PacketCaptureProbe.dll");
    auto waitReady = probe != nullptr ? reinterpret_cast<SemanticSelfTestPtr>(
        GetProcAddress(probe, "God2TraceProbeWaitReady")) : nullptr;
    if (waitReady == nullptr || waitReady(nullptr) != 1u) {
        std::printf("probe readiness handshake failed\n");
        return 14;
    }
    auto privacyIntegrationSelfTest = probe != nullptr ?
        reinterpret_cast<SemanticSelfTestPtr>(GetProcAddress(
            probe, "God2TraceProbePrivacyIntegrationSelfTest")) : nullptr;
    if (privacyIntegrationSelfTest == nullptr ||
        privacyIntegrationSelfTest(nullptr) != 1u) {
        std::printf("sensitive outbound live integration self-test failed\n");
        return 14;
    }
    auto semanticSelfTest = probe != nullptr ? reinterpret_cast<SemanticSelfTestPtr>(
        GetProcAddress(probe, "God2TraceProbeSemanticSelfTest")) : nullptr;
    if (semanticSelfTest == nullptr || semanticSelfTest(nullptr) != 1u) {
        std::printf("semantic probe native contract self-test failed\n");
        return 14;
    }
    char sharedFixtureRequested[8]{};
    const bool runSharedFixture = GetEnvironmentVariableA(
        "GOD2_TRACE_SHARED_RING_SELFTEST", sharedFixtureRequested,
        static_cast<DWORD>(sizeof(sharedFixtureRequested))) != 0 &&
        std::strcmp(sharedFixtureRequested, "1") == 0;
    if (runSharedFixture) {
        auto sharedTransportSelfTest = probe != nullptr ?
            reinterpret_cast<SemanticSelfTestPtr>(GetProcAddress(
                probe, "God2TraceProbeSharedTransportSelfTest")) : nullptr;
        if (sharedTransportSelfTest == nullptr) {
            std::printf("shared semantic transport producer export is missing\n");
            return 15;
        }
        const DWORD sharedTransportResult = sharedTransportSelfTest(nullptr);
        if (sharedTransportResult == MAXDWORD || sharedTransportResult == 0) {
            std::printf("shared semantic transport producer self-test failed\n");
            return 15;
        }
        char completionEventName[256]{};
        if (GetEnvironmentVariableA("GOD2_TRACE_SHARED_RING_COMPLETE_EVENT",
                completionEventName,
                static_cast<DWORD>(sizeof(completionEventName))) == 0) {
            std::printf("shared semantic transport completion event is missing\n");
            return 15;
        }
        HANDLE completionEvent = OpenEventA(EVENT_MODIFY_STATE, FALSE,
                                             completionEventName);
        if (completionEvent == nullptr || SetEvent(completionEvent) == FALSE) {
            if (completionEvent != nullptr) CloseHandle(completionEvent);
            std::printf("shared semantic transport completion signal failed\n");
            return 15;
        }
        CloseHandle(completionEvent);

        // Keep the deterministic producer phase quiescent until the external
        // consumer has copied and retired every exact wire slot.  This avoids
        // racing the fixture parser with the later runtime completion probes.
        char drainedEventName[256]{};
        if (GetEnvironmentVariableA("GOD2_TRACE_SHARED_RING_DRAINED_EVENT",
                drainedEventName,
                static_cast<DWORD>(sizeof(drainedEventName))) == 0) {
            std::printf("shared semantic transport drained event is missing\n");
            return 15;
        }
        HANDLE drainedEvent = OpenEventA(SYNCHRONIZE, FALSE,
                                         drainedEventName);
        if (drainedEvent == nullptr ||
            WaitForSingleObject(drainedEvent, 15'000) != WAIT_OBJECT_0) {
            if (drainedEvent != nullptr) CloseHandle(drainedEvent);
            std::printf("shared semantic transport consumer drain timed out\n");
            return 15;
        }
        CloseHandle(drainedEvent);
    }

    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        std::printf("WSAStartup failed\n");
        return 2;
    }

    SOCKET listener = INVALID_SOCKET;
    sockaddr_in address{};
    if (!setupLoopback(listener, address)) {
        WSACleanup();
        return 3;
    }

    std::vector<unsigned char> payload = makePayload(kind);
    ServerContext context{ listener, kind, &payload };
    HANDLE thread = CreateThread(nullptr, 0, serverThread, &context, 0, nullptr);
    if (thread == nullptr) {
        closesocket(listener);
        WSACleanup();
        return 4;
    }

    SOCKET client = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (client == INVALID_SOCKET) {
        closesocket(listener);
        WSACleanup();
        return 5;
    }
    if (connect(client, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0) {
        std::printf("connect failed %d\n", WSAGetLastError());
        closesocket(client);
        closesocket(listener);
        WSACleanup();
        return 6;
    }

    HMODULE ws2 = GetModuleHandleA("ws2_32.dll");
    if (ws2 == nullptr) {
        std::printf("ws2_32.dll module lookup failed\n");
        closesocket(client);
        closesocket(listener);
        WSACleanup();
        return 7;
    }
    auto dynamicSend = reinterpret_cast<SendPtr>(GetProcAddress(ws2, "send"));
    auto dynamicRecv = reinterpret_cast<RecvPtr>(GetProcAddress(ws2, "recv"));
    auto dynamicWSASend = reinterpret_cast<WSASendPtr>(
        GetProcAddress(ws2, "WSASend"));
    auto dynamicWSARecv = reinterpret_cast<WSARecvPtr>(
        GetProcAddress(ws2, "WSARecv"));
    if (dynamicSend == nullptr || dynamicRecv == nullptr ||
        dynamicWSASend == nullptr || dynamicWSARecv == nullptr) {
        std::printf("dynamic winsock lookup failed\n");
        closesocket(client);
        closesocket(listener);
        WSACleanup();
        return 7;
    }

    int apiResult = SOCKET_ERROR;
    DWORD bytesTransferred = 0;
    if (kind == CaseKind::Send) {
        apiResult = dynamicSend(client, reinterpret_cast<const char*>(payload.data()), static_cast<int>(payload.size()), 0);
        if (apiResult != SOCKET_ERROR) {
            bytesTransferred = static_cast<DWORD>(apiResult);
        }
        shutdown(client, SD_SEND);
    } else if (kind == CaseKind::WSASend) {
        WSABUF buffers[2]{};
        buffers[0].buf = reinterpret_cast<char*>(payload.data());
        buffers[0].len = 97;
        buffers[1].buf = reinterpret_cast<char*>(payload.data() + buffers[0].len);
        buffers[1].len = static_cast<ULONG>(payload.size() - buffers[0].len);
        DWORD sent{};
        apiResult = dynamicWSASend(
            client, buffers, 2, &sent, 0, nullptr, nullptr);
        bytesTransferred = sent;
        shutdown(client, SD_SEND);
    } else if (kind == CaseKind::Recv) {
        std::vector<unsigned char> received(payload.size());
        apiResult = dynamicRecv(client, reinterpret_cast<char*>(received.data()), static_cast<int>(received.size()), MSG_WAITALL);
        if (apiResult != SOCKET_ERROR) {
            bytesTransferred = static_cast<DWORD>(apiResult);
        }
        if (bytesTransferred != payload.size() || std::memcmp(received.data(), payload.data(), payload.size()) != 0) {
            std::printf("recv payload mismatch result=%d bytes=%lu\n", apiResult, bytesTransferred);
            closesocket(client);
            closesocket(listener);
            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
            WSACleanup();
            return 8;
        }
    } else if (kind == CaseKind::WSARecv) {
        std::vector<unsigned char> first(31);
        std::vector<unsigned char> second(payload.size() - first.size());
        WSABUF buffers[2]{};
        buffers[0].buf = reinterpret_cast<char*>(first.data());
        buffers[0].len = static_cast<ULONG>(first.size());
        buffers[1].buf = reinterpret_cast<char*>(second.data());
        buffers[1].len = static_cast<ULONG>(second.size());
        DWORD flags = MSG_WAITALL;
        DWORD received{};
        apiResult = dynamicWSARecv(
            client, buffers, 2, &received, &flags, nullptr, nullptr);
        bytesTransferred = received;
        std::vector<unsigned char> combined;
        combined.insert(combined.end(), first.begin(), first.end());
        combined.insert(combined.end(), second.begin(), second.end());
        if (apiResult != 0 || bytesTransferred != payload.size() || std::memcmp(combined.data(), payload.data(), payload.size()) != 0) {
            std::printf("WSARecv payload mismatch result=%d lastError=%d bytes=%lu\n", apiResult, WSAGetLastError(), bytesTransferred);
            closesocket(client);
            closesocket(listener);
            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
            WSACleanup();
            return 9;
        }
    } else if (kind == CaseKind::WSARecvOverlapped) {
        HANDLE completionPort = CreateIoCompletionPort(reinterpret_cast<HANDLE>(client), nullptr, 0x47524432u, 0);
        if (completionPort == nullptr) {
            std::printf("CreateIoCompletionPort failed lastError=%lu\n", GetLastError());
            closesocket(client);
            closesocket(listener);
            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
            WSACleanup();
            return 12;
        }

        std::vector<unsigned char> first(31);
        std::vector<unsigned char> second(payload.size() - first.size());
        WSABUF buffers[2]{};
        buffers[0].buf = reinterpret_cast<char*>(first.data());
        buffers[0].len = static_cast<ULONG>(first.size());
        buffers[1].buf = reinterpret_cast<char*>(second.data());
        buffers[1].len = static_cast<ULONG>(second.size());
        WSAOVERLAPPED overlapped{};
        DWORD flags{};
        DWORD received{};
        apiResult = dynamicWSARecv(
            client, buffers, 2, &received, &flags, &overlapped, nullptr);
        const int submitError = apiResult == SOCKET_ERROR ? WSAGetLastError() : ERROR_SUCCESS;
        if (apiResult != SOCKET_ERROR || submitError != WSA_IO_PENDING) {
            std::printf("overlapped WSARecv did not enter pending state result=%d lastError=%d\n",
                apiResult, submitError);
            CloseHandle(completionPort);
            closesocket(client);
            closesocket(listener);
            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
            WSACleanup();
            return 13;
        }

        ULONG_PTR completionKey{};
        LPOVERLAPPED completedOverlapped{};
        DWORD completed{};
        const BOOL completionResult = GetQueuedCompletionStatus(
            completionPort, &completed, &completionKey, &completedOverlapped, 5000);
        const DWORD completionError = GetLastError();
        CloseHandle(completionPort);
        bytesTransferred = completed;
        apiResult = completionResult != FALSE ? 0 : SOCKET_ERROR;
        std::vector<unsigned char> combined;
        combined.insert(combined.end(), first.begin(), first.end());
        combined.insert(combined.end(), second.begin(), second.end());
        if (completionResult == FALSE || completedOverlapped != &overlapped ||
            completionKey != 0x47524432u || bytesTransferred != payload.size() ||
            std::memcmp(combined.data(), payload.data(), payload.size()) != 0) {
            std::printf("overlapped WSARecv completion mismatch result=%d lastError=%lu bytes=%lu key=%llu\n",
                completionResult != FALSE ? 1 : 0, completionError, bytesTransferred,
                static_cast<unsigned long long>(completionKey));
            closesocket(client);
            closesocket(listener);
            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
            WSACleanup();
            return 14;
        }
    }

    const DWORD lastError = WSAGetLastError();
    closesocket(client);
    closesocket(listener);
    const DWORD wait = WaitForSingleObject(thread, 5000);
    DWORD serverExit{};
    GetExitCodeThread(thread, &serverExit);
    CloseHandle(thread);

    bool completionRoutinePassed = false;
    bool wsaGetOverlappedResultPassed = false;
    bool getQueuedCompletionStatusPassed = false;
    bool getQueuedCompletionStatusExPassed = false;
    const bool completionMatrixExecuted = runSharedFixture ||
        kind == CaseKind::WSARecvOverlapped;
    PendingAtStopContext pendingAtStop{};
    HANDLE pendingAtStopHandle = nullptr;
    BlockingShutdownContext blockingShutdown[4]{};
    HANDLE blockingShutdownThreads[4]{};
    HANDLE blockingNaturalReleaseThreads[4]{};
    HANDLE blockingReleaseEvent = nullptr;
    HANDLE blockingAllReturnedEvent = nullptr;
    bool blockingShutdownReady = !runSharedFixture;
    if (completionMatrixExecuted) {
        completionRoutinePassed = runCompletionMechanism(
            CompletionMechanism::CompletionRoutine);
        wsaGetOverlappedResultPassed = runCompletionMechanism(
            CompletionMechanism::WsaGetOverlappedResult);
        getQueuedCompletionStatusPassed = runCompletionMechanism(
            CompletionMechanism::GetQueuedCompletionStatus);
        getQueuedCompletionStatusExPassed = runCompletionMechanism(
            CompletionMechanism::GetQueuedCompletionStatusEx);
        if (runSharedFixture) {
            char releaseEventName[256]{};
            char allReturnedEventName[256]{};
            const bool releaseNamesPresent =
                GetEnvironmentVariableA(
                    "GOD2_TRACE_BLOCKING_RELEASE_EVENT", releaseEventName,
                    static_cast<DWORD>(sizeof(releaseEventName))) != 0 &&
                GetEnvironmentVariableA(
                    "GOD2_TRACE_BLOCKING_ALL_RETURNED_EVENT",
                    allReturnedEventName,
                    static_cast<DWORD>(sizeof(allReturnedEventName))) != 0;
            if (releaseNamesPresent) {
                blockingReleaseEvent = OpenEventA(
                    SYNCHRONIZE, FALSE, releaseEventName);
                blockingAllReturnedEvent = OpenEventA(
                    EVENT_MODIFY_STATE, FALSE, allReturnedEventName);
            }
            g_blockingAllReturnedEvent = blockingAllReturnedEvent;
            InterlockedExchange(&g_blockingReturnedCount, 0);
            pendingAtStop.ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            pendingAtStopHandle = pendingAtStop.ready != nullptr ?
                CreateThread(nullptr, 0, pendingAtStopThread, &pendingAtStop, 0,
                             nullptr) : nullptr;
            if (pendingAtStopHandle == nullptr ||
                WaitForSingleObject(pendingAtStop.ready, 5'000) != WAIT_OBJECT_0) {
                completionRoutinePassed = false;
            }
            const BlockingShutdownKind kinds[] = {
                BlockingShutdownKind::GetQueuedCompletionStatus,
                BlockingShutdownKind::GetQueuedCompletionStatusEx,
                BlockingShutdownKind::Recv,
                BlockingShutdownKind::WsaGetOverlappedResult
            };
            blockingShutdownReady = blockingReleaseEvent != nullptr &&
                blockingAllReturnedEvent != nullptr;
            for (DWORD index = 0; index < _countof(kinds); ++index) {
                if (!initializeBlockingShutdownContext(
                        &blockingShutdown[index], kinds[index],
                        blockingReleaseEvent)) {
                    blockingShutdownReady = false;
                    break;
                }
                blockingShutdownThreads[index] = CreateThread(
                    nullptr, 0, blockingShutdownThread,
                    &blockingShutdown[index], 0, nullptr);
                if (blockingShutdownThreads[index] == nullptr ||
                    WaitForSingleObject(blockingShutdown[index].entered,
                        5'000) != WAIT_OBJECT_0) {
                    blockingShutdownReady = false;
                    break;
                }
                blockingNaturalReleaseThreads[index] = CreateThread(
                    nullptr, 0, naturallyReleaseBlockingCall,
                    &blockingShutdown[index], 0, nullptr);
                if (blockingNaturalReleaseThreads[index] == nullptr) {
                    blockingShutdownReady = false;
                    break;
                }
            }
            // entered is signaled immediately before the hooked API. Give all
            // four wrappers a bounded scheduling window to register their
            // original blocking call before the out-of-ring Stop marker.
            if (blockingShutdownReady) Sleep(250);
        }
    }

    if (runSharedFixture && completionRoutinePassed &&
        wsaGetOverlappedResultPassed && getQueuedCompletionStatusPassed &&
        getQueuedCompletionStatusExPassed && pendingAtStopHandle != nullptr &&
        blockingShutdownReady) {
        char readyEventName[256]{};
        if (GetEnvironmentVariableA(
                "GOD2_TRACE_RUNTIME_FIXTURES_READY_FOR_STOP_EVENT",
                readyEventName, static_cast<DWORD>(sizeof(readyEventName))) == 0) {
            std::printf("runtime fixture ready-for-stop event is missing\n");
            return 17;
        }
        HANDLE readyEvent = OpenEventA(EVENT_MODIFY_STATE, FALSE,
                                       readyEventName);
        if (readyEvent == nullptr || SetEvent(readyEvent) == FALSE) {
            if (readyEvent != nullptr) CloseHandle(readyEvent);
            std::printf("runtime fixture ready-for-stop signal failed\n");
            return 17;
        }
        CloseHandle(readyEvent);
    }

    std::printf("SelfTestCase=%s\n", caseName(kind));
    std::printf("ApiCallResult=%d\n", apiResult);
    std::printf("BytesTransferred=%lu\n", bytesTransferred);
    std::printf("ServerExit=%lu\n", serverExit);
    std::printf("CompletionRoutineRuntime=%s\n",
        !completionMatrixExecuted ? "NOT_RUN" :
            (completionRoutinePassed ? "PASS" : "FAIL"));
    std::printf("WSAGetOverlappedResultRuntime=%s\n",
        !completionMatrixExecuted ? "NOT_RUN" :
            (wsaGetOverlappedResultPassed ? "PASS" : "FAIL"));
    std::printf("GetQueuedCompletionStatusRuntime=%s\n",
        !completionMatrixExecuted ? "NOT_RUN" :
            (getQueuedCompletionStatusPassed ? "PASS" : "FAIL"));
    std::printf("GetQueuedCompletionStatusExRuntime=%s\n",
        !completionMatrixExecuted ? "NOT_RUN" :
            (getQueuedCompletionStatusExPassed ? "PASS" : "FAIL"));
    std::printf("BlockingApiNaturalReturnFixture=%s\n",
        !runSharedFixture ? "NOT_RUN" :
            (blockingShutdownReady ? "ENTERED" : "FAIL"));
    if (wait != WAIT_OBJECT_0 || serverExit != 0) {
        return 10;
    }
    if ((kind == CaseKind::Send && bytesTransferred != payload.size()) ||
        (kind == CaseKind::WSASend && (apiResult != 0 || bytesTransferred != payload.size()))) {
        std::printf("send api failed lastError=%lu\n", lastError);
        return 11;
    }
    if (completionMatrixExecuted &&
        (!completionRoutinePassed || !wsaGetOverlappedResultPassed ||
         !getQueuedCompletionStatusPassed ||
         !getQueuedCompletionStatusExPassed)) {
        std::printf("completion observation matrix failed\n");
        return 16;
    }

    std::printf("SELFTEST %s PASS\n", caseName(kind));
    const DWORD holdDelay = environmentDelay("GOD2_TRACE_SELFTEST_HOLD_MS");
    if (holdDelay > 0) {
        Sleep(holdDelay);
    }
    if (pendingAtStopHandle != nullptr) {
        const DWORD pendingWait = WaitForSingleObject(pendingAtStopHandle, 5'000);
        DWORD pendingExit = ERROR_GEN_FAILURE;
        GetExitCodeThread(pendingAtStopHandle, &pendingExit);
        CloseHandle(pendingAtStopHandle);
        if (pendingAtStop.ready != nullptr) CloseHandle(pendingAtStop.ready);
        if (pendingWait != WAIT_OBJECT_0 || pendingExit != 0 ||
            InterlockedCompareExchange(&pendingAtStop.passed, 0, 0) != 1) {
            WSACleanup();
            return 17;
        }
    } else if (pendingAtStop.ready != nullptr) {
        CloseHandle(pendingAtStop.ready);
    }
    bool blockingShutdownPassed = blockingShutdownReady;
    bool naturalReleaseObserved[4]{};
    for (DWORD index = 0; index < _countof(blockingShutdownThreads); ++index) {
        if (blockingNaturalReleaseThreads[index] != nullptr) {
            const DWORD releaseWait = WaitForSingleObject(
                blockingNaturalReleaseThreads[index], 5'000);
            DWORD releaseExit = ERROR_GEN_FAILURE;
            GetExitCodeThread(blockingNaturalReleaseThreads[index],
                              &releaseExit);
            CloseHandle(blockingNaturalReleaseThreads[index]);
            naturalReleaseObserved[index] = releaseWait == WAIT_OBJECT_0 &&
                releaseExit == 0;
            blockingShutdownPassed = blockingShutdownPassed &&
                naturalReleaseObserved[index];
        } else if (runSharedFixture) {
            blockingShutdownPassed = false;
        }
        if (blockingShutdownThreads[index] != nullptr) {
            const DWORD waitResult = WaitForSingleObject(
                blockingShutdownThreads[index], 5'000);
            DWORD exitCode = ERROR_GEN_FAILURE;
            GetExitCodeThread(blockingShutdownThreads[index], &exitCode);
            CloseHandle(blockingShutdownThreads[index]);
            blockingShutdownPassed = blockingShutdownPassed &&
                waitResult == WAIT_OBJECT_0 && exitCode == 0 &&
                InterlockedCompareExchange(
                    &blockingShutdown[index].passed, 0, 0) == 1;
            const bool reusable = waitResult == WAIT_OBJECT_0 &&
                verifyBlockingCallResourceStillUsable(
                    &blockingShutdown[index]);
            InterlockedExchange(&blockingShutdown[index].reusable,
                                reusable ? 1 : 0);
            const bool lastErrorPreserved = reusable &&
                blockingShutdown[index].observedLastError ==
                    blockingShutdown[index].baselineLastError;
            blockingShutdownPassed = blockingShutdownPassed && reusable &&
                lastErrorPreserved;
            const bool resultPreserved = waitResult == WAIT_OBJECT_0 &&
                exitCode == 0 && InterlockedCompareExchange(
                    &blockingShutdown[index].passed, 0, 0) == 1 &&
                lastErrorPreserved;
            std::printf(
                "God2BlockingPostUnload={\"SchemaId\":\"God2BlockingPostUnloadResult\"," 
                "\"SchemaVersion\":1,\"Index\":%lu,\"Api\":\"%s\"," 
                "\"ReleaseBarrierObserved\":%s,\"Returned\":%s," 
                "\"ResultPreserved\":%s,\"ResourceReusable\":%s,"
                "\"ObservedResult\":%ld,\"ObservedTransferred\":%lu,"
                "\"ObservedFlags\":%lu,\"ObservedRemoved\":%lu,"
                "\"ObservedCompletionKey\":\"0x%08lX\","
                "\"InputOverlapped\":\"0x%08lX\","
                "\"OutputOverlapped\":\"0x%08lX\","
                "\"InputResource\":\"0x%08lX\","
                "\"BaselineResource\":\"0x%08lX\","
                "\"EntryLastError\":%lu,\"BaselineLastError\":%lu,"
                "\"ObservedLastError\":%lu,"
                "\"PayloadMatched\":%s}\n",
                static_cast<unsigned long>(index),
                blockingShutdownKindName(blockingShutdown[index].kind),
                naturalReleaseObserved[index] ? "true" : "false",
                waitResult == WAIT_OBJECT_0 ? "true" : "false",
                resultPreserved ? "true" : "false",
                reusable ? "true" : "false",
                static_cast<long>(blockingShutdown[index].observedResult),
                static_cast<unsigned long>(
                    blockingShutdown[index].observedTransferred),
                static_cast<unsigned long>(blockingShutdown[index].observedFlags),
                static_cast<unsigned long>(blockingShutdown[index].observedRemoved),
                static_cast<unsigned long>(
                    blockingShutdown[index].observedCompletionKey),
                static_cast<unsigned long>(blockingShutdown[index].inputOverlapped),
                static_cast<unsigned long>(blockingShutdown[index].outputOverlapped),
                static_cast<unsigned long>(blockingShutdown[index].inputResource),
                static_cast<unsigned long>(blockingShutdown[index].baselineResource),
                static_cast<unsigned long>(
                    blockingShutdown[index].entryLastError),
                static_cast<unsigned long>(
                    blockingShutdown[index].baselineLastError),
                static_cast<unsigned long>(
                    blockingShutdown[index].observedLastError),
                blockingShutdown[index].observedPayloadMatched ?
                    "true" : "false");
        } else if (runSharedFixture) {
            blockingShutdownPassed = false;
        }
        closeBlockingShutdownContext(&blockingShutdown[index]);
    }
    if (runSharedFixture && !blockingShutdownPassed) {
        g_blockingAllReturnedEvent = nullptr;
        if (blockingAllReturnedEvent != nullptr)
            CloseHandle(blockingAllReturnedEvent);
        if (blockingReleaseEvent != nullptr) CloseHandle(blockingReleaseEvent);
        WSACleanup();
        return 18;
    }
    g_blockingAllReturnedEvent = nullptr;
    if (blockingAllReturnedEvent != nullptr)
        CloseHandle(blockingAllReturnedEvent);
    if (blockingReleaseEvent != nullptr) CloseHandle(blockingReleaseEvent);
    WSACleanup();
    return 0;
}
