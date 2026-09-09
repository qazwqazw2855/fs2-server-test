#include "EtwConsumer.h"

#include <evntrace.h>
#include <evntcons.h>

#include <array>
#include <algorithm>
#include <fstream>
#include <iomanip>
#include <sstream>

namespace god2 {
namespace {

struct ConsumerContext {
    std::ofstream output;
    bool write_jsonl = true;
    std::uint64_t event_index = 0;
    std::uint64_t pending_flush = 0;
    std::uint64_t total_events = 0;
    std::uint64_t ndis_packet_events = 0;
    std::uint64_t ndis_payload_bytes = 0;
};

constexpr GUID kNdisPacketCaptureProvider =
    {0x2ed6006e, 0x4729, 0x4609, {0xb4, 0x23, 0x3e, 0xe7, 0xbc, 0xd6, 0x78, 0xef}};

std::string Hex(const void* data, std::size_t size) {
    static const char alphabet[] = "0123456789ABCDEF";
    const auto* bytes = static_cast<const std::uint8_t*>(data);
    std::string result(size * 2, '0');
    for (std::size_t i = 0; i < size; ++i) {
        result[i * 2] = alphabet[bytes[i] >> 4];
        result[i * 2 + 1] = alphabet[bytes[i] & 0x0f];
    }
    return result;
}

std::string GuidText(const GUID& guid) {
    std::ostringstream output;
    output << std::hex << std::setfill('0') << std::setw(8) << guid.Data1 << '-' << std::setw(4) << guid.Data2
           << '-' << std::setw(4) << guid.Data3 << '-';
    for (int i = 0; i < 2; ++i) output << std::setw(2) << static_cast<unsigned>(guid.Data4[i]);
    output << '-';
    for (int i = 2; i < 8; ++i) output << std::setw(2) << static_cast<unsigned>(guid.Data4[i]);
    return output.str();
}

void WINAPI EventRecordCallback(PEVENT_RECORD record) {
    auto* context = static_cast<ConsumerContext*>(record->UserContext);
    if (context == nullptr) return;
    ++context->total_events;
    if (IsEqualGUID(record->EventHeader.ProviderId, kNdisPacketCaptureProvider)) {
        ++context->ndis_packet_events;
        context->ndis_payload_bytes += record->UserDataLength;
    }
    if (!context->write_jsonl || !context->output) return;
    ++context->event_index;
    const auto& header = record->EventHeader;
    const auto line = MakeJsonObject({
        {"SourceFrameId", "etw-" + std::to_string(context->event_index)},
        {"ObservedAtUtc", UtcNow()}, {"PacketDirection", "Unknown"}, {"MessageType", ""},
        {"Opcode", std::to_string(header.EventDescriptor.Opcode)},
        {"PayloadHex", Hex(record->UserData, record->UserDataLength)},
        {"EtwProviderId", GuidText(header.ProviderId)},
        {"EtwEventId", std::to_string(header.EventDescriptor.Id)},
        {"EtwVersion", std::to_string(header.EventDescriptor.Version)},
        {"EtwLevel", std::to_string(header.EventDescriptor.Level)},
        {"ProcessId", std::to_string(header.ProcessId)}, {"ThreadId", std::to_string(header.ThreadId)},
        {"EvidenceLevel", "Transmitted"}, {"CaptureSource", "ETWRealtimeEventRecord"}
    });
    context->output << line << '\n';
    if (++context->pending_flush >= 256) {
        context->output.flush();
        context->pending_flush = 0;
    }
}

std::wstring Quote(std::wstring_view value) {
    std::wstring result = L"\"";
    for (const wchar_t c : value) {
        if (c == L'"') result += L"\\\"";
        else result.push_back(c);
    }
    result.push_back(L'"');
    return result;
}

std::uint64_t FileTimeValue(const FILETIME& value) {
    return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
}

std::uint64_t ProcessCreationTime(HANDLE process) {
    FILETIME created{}, exited{}, kernel{}, user{};
    return GetProcessTimes(process, &created, &exited, &kernel, &user) ? FileTimeValue(created) : 0;
}

std::optional<fs::path> ProcessImagePath(HANDLE process) {
    std::vector<wchar_t> buffer(32'768);
    DWORD length = static_cast<DWORD>(buffer.size());
    if (!QueryFullProcessImageNameW(process, 0, buffer.data(), &length)) return std::nullopt;
    return fs::path(std::wstring(buffer.data(), length));
}

bool SamePath(const fs::path& left, const fs::path& right) {
    std::error_code left_error;
    std::error_code right_error;
    auto normalized_left = fs::weakly_canonical(left, left_error).wstring();
    auto normalized_right = fs::weakly_canonical(right, right_error).wstring();
    if (left_error || right_error) return false;
    std::transform(normalized_left.begin(), normalized_left.end(), normalized_left.begin(), ::towlower);
    std::transform(normalized_right.begin(), normalized_right.end(), normalized_right.begin(), ::towlower);
    return normalized_left == normalized_right;
}

void AppendWorkerLog(const fs::path& path, std::initializer_list<std::pair<std::string, std::string>> fields,
                     const std::set<std::string>& numeric = {}) noexcept {
    try {
        if (path.empty()) return;
        std::error_code ec;
        fs::create_directories(path.parent_path(), ec);
        std::ofstream output(path, std::ios::binary | std::ios::app);
        if (!output) return;
        output << MakeJsonObject(fields, numeric) << '\n';
        output.flush();
    } catch (...) {
    }
}

bool ReadWorkerStatus(const CaptureWorkerIdentity& identity, DWORD* worker_exit_code, std::string* error) {
    const auto content = ReadUtf8File(identity.status_path);
    if (!content) {
        if (error) *error = "capture worker exited without a completion status";
        return false;
    }
    Fields status;
    std::string parse_error;
    if (!ParseFlatJson(*content, status, &parse_error)) {
        if (error) *error = "capture worker status is invalid: " + parse_error;
        return false;
    }
    const auto status_pid = GetInt64(status, "ProcessId").value_or(0);
    const auto status_created = GetInt64(status, "CreationTime").value_or(0);
    const auto exit_code = GetInt64(status, "ExitCode").value_or(ERROR_GEN_FAILURE);
    if (GetString(status, "Nonce") != identity.nonce ||
        status_pid != static_cast<std::int64_t>(identity.process_id) ||
        status_created != static_cast<std::int64_t>(identity.creation_time)) {
        if (error) *error = "capture worker completion status does not match the recorded identity";
        return false;
    }
    if (worker_exit_code != nullptr) *worker_exit_code = static_cast<DWORD>(exit_code);
    return true;
}

bool ReadSuccessfulWorkerStatus(const CaptureWorkerIdentity& identity, std::string* error) {
    DWORD exit_code = ERROR_GEN_FAILURE;
    if (!ReadWorkerStatus(identity, &exit_code, error)) return false;
    if (exit_code != ERROR_SUCCESS) {
        if (error) *error = "capture worker exited with code " + std::to_string(exit_code);
        return false;
    }
    return true;
}

std::optional<CaptureWorkerIdentity> StartPrivateWorker(const fs::path& session_path,
                                                         std::string worker_type,
                                                         std::wstring arguments,
                                                         HANDLE cancel_event,
                                                         std::wstring_view cancel_event_name,
                                                         bool require_ready,
                                                         std::string* error) {
    const auto executable = ExecutablePath();
    if (executable.empty()) {
        if (error) *error = "capture worker executable path is unavailable";
        return std::nullopt;
    }
    CaptureWorkerIdentity identity;
    identity.image_path = executable;
    identity.worker_type = std::move(worker_type);
    identity.status_path = session_path / L"raw" /
        (L"capture-worker-status-" + Utf8ToWide(identity.worker_type) + L".json");
    identity.log_path = session_path / L"reports" /
        (L"capture-worker-" + Utf8ToWide(identity.worker_type) + L".jsonl");
    identity.nonce = NewId();
    identity.cancel_event_name = std::wstring(cancel_event_name);
    if (require_ready) {
        identity.ready_event_name = L"Local\\God2PacketCapture.EtwConsumerReady." +
            session_path.filename().wstring() + L"." + Utf8ToWide(identity.nonce);
    }
    std::error_code remove_error;
    fs::remove(identity.status_path, remove_error);
    fs::remove(identity.log_path, remove_error);
    arguments += L" --status " + Quote(identity.status_path.wstring()) + L" --nonce " + Quote(Utf8ToWide(identity.nonce));
    if (require_ready) {
        arguments += L" --ready-event " + Quote(identity.ready_event_name) +
            L" --cancel-event " + Quote(identity.cancel_event_name) +
            L" --worker-log " + Quote(identity.log_path.wstring()) +
            L" --session-id " + Quote(session_path.filename().wstring());
    }
    HANDLE ready_event = nullptr;
    if (require_ready) {
        ready_event = CreateEventW(nullptr, TRUE, FALSE, identity.ready_event_name.c_str());
        if (ready_event == nullptr) {
            if (error) *error = "failed to create ETW consumer READY event: " + std::to_string(GetLastError());
            return std::nullopt;
        }
    }
    std::wstring command = Quote(executable.wstring()) + L" " + arguments;
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), mutable_command.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP, nullptr, nullptr, &startup, &process)) {
        const DWORD create_error = GetLastError();
        if (ready_event != nullptr) CloseHandle(ready_event);
        if (error) *error = "failed to start capture worker: " + std::to_string(create_error);
        return std::nullopt;
    }
    identity.process_id = process.dwProcessId;
    identity.creation_time = ProcessCreationTime(process.hProcess);
    CloseHandle(process.hThread);
    if (identity.creation_time == 0) {
        TerminateProcess(process.hProcess, ERROR_INVALID_DATA);
        CloseHandle(process.hProcess);
        if (ready_event != nullptr) CloseHandle(ready_event);
        if (error) *error = "could not record capture worker creation time";
        return std::nullopt;
    }

    if (!require_ready) {
        if (WaitForSingleObject(process.hProcess, 250) != WAIT_OBJECT_0) {
            CloseHandle(process.hProcess);
            return identity;
        }
        DWORD exit_code = ERROR_GEN_FAILURE;
        GetExitCodeProcess(process.hProcess, &exit_code);
        CloseHandle(process.hProcess);
        if (error) *error = "capture worker exited during startup with code " + std::to_string(exit_code);
        return std::nullopt;
    }

    HANDLE waits[3] = {ready_event, process.hProcess, cancel_event};
    const DWORD wait_count = cancel_event != nullptr ? 3u : 2u;
    const DWORD wait = WaitForMultipleObjects(wait_count, waits, FALSE, 15'000);
    if (wait == WAIT_OBJECT_0) {
        identity.ready_confirmed = true;
        // READY means the callback and trace handle are configured and ProcessTrace
        // is about to run.  A worker that immediately disappears still violates
        // the long-running consumer contract.
        if (WaitForSingleObject(process.hProcess, 250) == WAIT_TIMEOUT) {
            CloseHandle(process.hProcess);
            CloseHandle(ready_event);
            return identity;
        }
        DWORD exit_code = ERROR_GEN_FAILURE;
        GetExitCodeProcess(process.hProcess, &exit_code);
        if (error) *error = "ETW consumer exited immediately after READY with code " + std::to_string(exit_code);
    } else if (wait == WAIT_OBJECT_0 + 1) {
        DWORD exit_code = ERROR_GEN_FAILURE;
        GetExitCodeProcess(process.hProcess, &exit_code);
        if (error) {
            *error = exit_code == ERROR_SUCCESS ?
                "ETW consumer violated the startup contract: exited before READY with code 0" :
                "ETW consumer exited before READY with code " + std::to_string(exit_code);
        }
    } else if (cancel_event != nullptr && wait == WAIT_OBJECT_0 + 2) {
        if (WaitForSingleObject(process.hProcess, 5'000) == WAIT_TIMEOUT) {
            TerminateProcess(process.hProcess, ERROR_CANCELLED);
            WaitForSingleObject(process.hProcess, 5'000);
        }
        if (error) *error = "ETW consumer startup was cancelled";
    } else {
        if (WaitForSingleObject(process.hProcess, 1'000) == WAIT_TIMEOUT) {
            TerminateProcess(process.hProcess, ERROR_TIMEOUT);
            WaitForSingleObject(process.hProcess, 5'000);
        }
        const DWORD wait_error = wait == WAIT_FAILED ? GetLastError() : ERROR_TIMEOUT;
        if (error) *error = "ETW consumer READY wait failed: " + std::to_string(wait_error);
    }
    CloseHandle(process.hProcess);
    CloseHandle(ready_event);
    return std::nullopt;
}

} // namespace

bool IsRecoverableEtwOpenTraceError(DWORD error) {
    return error == ERROR_WMI_INSTANCE_NOT_FOUND || error == ERROR_FILE_NOT_FOUND || error == ERROR_NOT_READY;
}

bool IsEtwConsumerStartupExitContractValid(bool ready_signaled, DWORD exit_code) {
    return ready_signaled || exit_code != ERROR_SUCCESS;
}

int EtwConsumerProcessTraceExitCode(bool stop_requested, ULONG process_trace_result) {
    if (stop_requested && (process_trace_result == ERROR_SUCCESS || process_trace_result == ERROR_CANCELLED ||
                           process_trace_result == ERROR_WMI_INSTANCE_NOT_FOUND)) {
        return ERROR_SUCCESS;
    }
    if (process_trace_result == ERROR_SUCCESS) return ERROR_OPERATION_ABORTED;
    return static_cast<int>(process_trace_result);
}

EtwFileAuditResult AuditEtwFile(const fs::path& etl_path, const fs::path& report_path,
                                std::string* error) {
    EtwFileAuditResult audit;
    std::error_code file_error;
    if (!fs::is_regular_file(etl_path, file_error) || file_error) {
        audit.win32_error = ERROR_FILE_NOT_FOUND;
        if (error != nullptr) *error = "ETL file is unavailable for offline audit";
        return audit;
    }

    std::wstring mutable_path = etl_path.wstring();
    ConsumerContext context;
    context.write_jsonl = false;
    EVENT_TRACE_LOGFILEW logfile{};
    logfile.LogFileName = mutable_path.data();
    logfile.ProcessTraceMode = PROCESS_TRACE_MODE_EVENT_RECORD;
    logfile.EventRecordCallback = EventRecordCallback;
    logfile.Context = &context;
    SetLastError(ERROR_SUCCESS);
    TRACEHANDLE trace = OpenTraceW(&logfile);
    if (trace == INVALID_PROCESSTRACE_HANDLE) {
        audit.win32_error = GetLastError();
        if (audit.win32_error == ERROR_SUCCESS) audit.win32_error = ERROR_OPEN_FAILED;
    } else {
        const ULONG process_result = ProcessTrace(&trace, 1, nullptr, nullptr);
        CloseTrace(trace);
        audit.win32_error = process_result;
        audit.success = process_result == ERROR_SUCCESS;
        audit.total_events = context.total_events;
        audit.ndis_packet_events = context.ndis_packet_events;
        audit.ndis_payload_bytes = context.ndis_payload_bytes;
        audit.events_lost = logfile.LogfileHeader.EventsLost;
        audit.buffers_lost = logfile.LogfileHeader.BuffersLost;
    }

    const auto report = MakeJsonObject({
        {"Status", audit.success ? (audit.ndis_packet_events > 0 ? "Observed" : "NoNdisPacketEvents") : "Failed"},
        {"EvidenceLevel", "RawUnattributed"},
        {"Reason", audit.success ?
            "Offline ETL inventory completed after the capture backend finalized the trace" :
            "Offline ETL inventory could not be completed"},
        {"Decision", "Raw NDIS packet events are counted but are not promoted to God2 frames without verified process attribution"},
        {"TargetExecutable", "God2_opt.exe"},
        {"TargetArchitecture", "x86"},
        {"RawEtlPath", WideToUtf8(etl_path.wstring())},
        {"TotalEvents", std::to_string(audit.total_events)},
        {"NdisPacketEvents", std::to_string(audit.ndis_packet_events)},
        {"NdisPayloadBytes", std::to_string(audit.ndis_payload_bytes)},
        {"EventsLost", std::to_string(audit.events_lost)},
        {"BuffersLost", std::to_string(audit.buffers_lost)},
        {"Win32Error", std::to_string(audit.win32_error)},
        {"ProcessAttribution", "UnavailableAtNdisLayer"}
    }, {"TotalEvents", "NdisPacketEvents", "NdisPayloadBytes", "EventsLost", "BuffersLost", "Win32Error"});
    if (!report_path.empty() && !WriteUtf8FileAtomic(report_path, report + "\n")) {
        audit.success = false;
        audit.win32_error = ERROR_WRITE_FAULT;
        if (error != nullptr) *error = "could not write offline ETL inventory report";
        return audit;
    }
    if (!audit.success && error != nullptr) {
        *error = "offline ETL inventory failed with code " + std::to_string(audit.win32_error);
    }
    return audit;
}

int ConsumeEtwRealtime(const EtwConsumerOptions& options) {
    AppendWorkerLog(options.worker_log_path, {
        {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"},
        {"SessionId", options.session_id}, {"ProcessId", std::to_string(GetCurrentProcessId())},
        {"CommandLineMode", "--internal-worker etw-consume"}, {"Stage", "ValidateArguments"},
        {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "NotCalled"},
        {"CapturedWin32Error", "0"}, {"ReadySignaled", "false"},
        {"StopRequested", "false"}, {"ProcessTraceResult", "NotCalled"},
        {"ExitReason", "Starting"}, {"ExitCode", "0"}
    }, {"ProcessId", "CapturedWin32Error", "ExitCode"});
    if (options.logger_name.empty() || options.output_jsonl.empty() || options.ready_event_name.empty() ||
        options.cancel_event_name.empty() || options.worker_log_path.empty()) {
        AppendWorkerLog(options.worker_log_path, {
            {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
            {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "ValidateArguments"},
            {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "NotCalled"},
            {"CapturedWin32Error", std::to_string(ERROR_INVALID_PARAMETER)}, {"ReadySignaled", "false"},
            {"StopRequested", "false"}, {"ProcessTraceResult", "NotCalled"},
            {"ExitReason", "InvalidArguments"}, {"ExitCode", std::to_string(ERROR_INVALID_PARAMETER)}
        }, {"ProcessId", "CapturedWin32Error", "ExitCode"});
        return ERROR_INVALID_PARAMETER;
    }

    HANDLE ready_event = OpenEventW(EVENT_MODIFY_STATE, FALSE, options.ready_event_name.c_str());
    if (ready_event == nullptr) {
        const DWORD open_error = GetLastError();
        AppendWorkerLog(options.worker_log_path, {
            {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
            {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "OpenReadyEvent"},
            {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "NotCalled"},
            {"CapturedWin32Error", std::to_string(open_error)}, {"ReadySignaled", "false"},
            {"StopRequested", "false"}, {"ProcessTraceResult", "NotCalled"},
            {"ExitReason", "ReadyEventOpenFailed"}, {"ExitCode", std::to_string(open_error)}
        }, {"ProcessId", "CapturedWin32Error", "ExitCode"});
        return static_cast<int>(open_error);
    }
    HANDLE cancel_event = OpenEventW(SYNCHRONIZE, FALSE, options.cancel_event_name.c_str());
    if (cancel_event == nullptr) {
        const DWORD open_error = GetLastError();
        CloseHandle(ready_event);
        AppendWorkerLog(options.worker_log_path, {
            {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
            {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "OpenCancelEvent"},
            {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "NotCalled"},
            {"CapturedWin32Error", std::to_string(open_error)}, {"ReadySignaled", "false"},
            {"StopRequested", "false"}, {"ProcessTraceResult", "NotCalled"},
            {"ExitReason", "CancelEventOpenFailed"}, {"ExitCode", std::to_string(open_error)}
        }, {"ProcessId", "CapturedWin32Error", "ExitCode"});
        return static_cast<int>(open_error);
    }

    std::error_code ec;
    fs::create_directories(options.output_jsonl.parent_path(), ec);
    ConsumerContext context;
    context.output.open(options.output_jsonl, std::ios::binary | std::ios::app);
    if (!context.output) {
        CloseHandle(cancel_event);
        CloseHandle(ready_event);
        AppendWorkerLog(options.worker_log_path, {
            {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
            {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "OpenOutput"},
            {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "NotCalled"},
            {"CapturedWin32Error", std::to_string(ERROR_OPEN_FAILED)}, {"ReadySignaled", "false"},
            {"StopRequested", "false"}, {"ProcessTraceResult", "NotCalled"},
            {"ExitReason", "OutputOpenFailed"}, {"ExitCode", std::to_string(ERROR_OPEN_FAILED)}
        }, {"ProcessId", "CapturedWin32Error", "ExitCode"});
        return ERROR_OPEN_FAILED;
    }

    std::wstring mutable_logger(options.logger_name);
    EVENT_TRACE_LOGFILEW logfile{};
    logfile.LoggerName = mutable_logger.data();
    logfile.ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD;
    logfile.EventRecordCallback = EventRecordCallback;
    logfile.Context = &context;
    TRACEHANDLE trace = INVALID_PROCESSTRACE_HANDLE;
    DWORD trace_error = ERROR_SUCCESS;
    const ULONGLONG retry_deadline = GetTickCount64() + 10'000;
    unsigned retry_count = 0;
    do {
        if (WaitForSingleObject(cancel_event, 0) == WAIT_OBJECT_0) {
            context.output.flush();
            CloseHandle(cancel_event);
            CloseHandle(ready_event);
            AppendWorkerLog(options.worker_log_path, {
                {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
                {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "OpenTraceRetry"},
                {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "Cancelled"},
                {"CapturedWin32Error", std::to_string(ERROR_CANCELLED)}, {"ReadySignaled", "false"},
                {"StopRequested", "true"}, {"ProcessTraceResult", "NotCalled"},
                {"ExitReason", "CancelledBeforeReady"}, {"ExitCode", std::to_string(ERROR_CANCELLED)}
            }, {"ProcessId", "CapturedWin32Error", "ExitCode"});
            return ERROR_CANCELLED;
        }
        SetLastError(ERROR_SUCCESS);
        trace = OpenTraceW(&logfile);
        if (trace != INVALID_PROCESSTRACE_HANDLE) break;
        trace_error = GetLastError();
        ++retry_count;
        AppendWorkerLog(options.worker_log_path, {
            {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
            {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "OpenTraceRetry"},
            {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "INVALID_PROCESSTRACE_HANDLE"},
            {"CapturedWin32Error", std::to_string(trace_error)}, {"RetryCount", std::to_string(retry_count)},
            {"ReadySignaled", "false"}, {"StopRequested", "false"},
            {"ProcessTraceResult", "NotCalled"}, {"ExitReason", "RetryingRecoverableOpenTrace"},
            {"ExitCode", std::to_string(trace_error)}
        }, {"ProcessId", "CapturedWin32Error", "RetryCount", "ExitCode"});
        if (!IsRecoverableEtwOpenTraceError(trace_error) || GetTickCount64() >= retry_deadline) break;
        if (WaitForSingleObject(cancel_event, 200) == WAIT_OBJECT_0) continue;
    } while (GetTickCount64() < retry_deadline);
    if (trace == INVALID_PROCESSTRACE_HANDLE) {
        context.output.flush();
        CloseHandle(cancel_event);
        CloseHandle(ready_event);
        AppendWorkerLog(options.worker_log_path, {
            {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
            {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "OpenTraceFailed"},
            {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "INVALID_PROCESSTRACE_HANDLE"},
            {"CapturedWin32Error", std::to_string(trace_error)}, {"RetryCount", std::to_string(retry_count)},
            {"ReadySignaled", "false"}, {"StopRequested", "false"},
            {"ProcessTraceResult", "NotCalled"}, {"ExitReason", "OpenTraceFailed"},
            {"ExitCode", std::to_string(trace_error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : trace_error)}
        }, {"ProcessId", "CapturedWin32Error", "RetryCount", "ExitCode"});
        return static_cast<int>(trace_error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : trace_error);
    }

    const BOOL ready_signaled = SetEvent(ready_event);
    const DWORD ready_error = ready_signaled ? ERROR_SUCCESS : GetLastError();
    AppendWorkerLog(options.worker_log_path, {
        {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
        {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "ConsumerReady"},
        {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "Success"},
        {"CapturedWin32Error", std::to_string(ready_error)}, {"RetryCount", std::to_string(retry_count)},
        {"ReadySignaled", ready_signaled ? "true" : "false"}, {"StopRequested", "false"},
        {"ProcessTraceResult", "Pending"}, {"ExitReason", ready_signaled ? "EnteringProcessTrace" : "ReadySignalFailed"},
        {"ExitCode", std::to_string(ready_error)}
    }, {"ProcessId", "CapturedWin32Error", "RetryCount", "ExitCode"});
    if (!ready_signaled) {
        CloseTrace(trace);
        context.output.flush();
        CloseHandle(cancel_event);
        CloseHandle(ready_event);
        return static_cast<int>(ready_error);
    }
    const ULONG result = ProcessTrace(&trace, 1, nullptr, nullptr);
    CloseTrace(trace);
    context.output.flush();
    const bool stop_requested = WaitForSingleObject(cancel_event, 0) == WAIT_OBJECT_0;
    CloseHandle(cancel_event);
    CloseHandle(ready_event);
    const int exit_code = EtwConsumerProcessTraceExitCode(stop_requested, result);
    AppendWorkerLog(options.worker_log_path, {
        {"ObservedAtUtc", UtcNow()}, {"WorkerType", "EtwConsumer"}, {"SessionId", options.session_id},
        {"ProcessId", std::to_string(GetCurrentProcessId())}, {"Stage", "ProcessTraceCompleted"},
        {"ETWSessionName", WideToUtf8(options.logger_name)}, {"OpenTraceResult", "Success"},
        {"CapturedWin32Error", "0"}, {"RetryCount", std::to_string(retry_count)},
        {"ReadySignaled", "true"}, {"StopRequested", stop_requested ? "true" : "false"},
        {"ProcessTraceResult", std::to_string(result)},
        {"ExitReason", stop_requested ? "CancelledAfterReady" : "TraceSessionEnded"},
        {"ExitCode", std::to_string(exit_code)}
    }, {"ProcessId", "CapturedWin32Error", "RetryCount", "ProcessTraceResult", "ExitCode"});
    return exit_code;
}

int RunPktMonCounterWorker(const fs::path& output_jsonl) {
    const auto pktmon = FindSystemExecutable(L"pktmon.exe");
    if (!pktmon) return ERROR_FILE_NOT_FOUND;
    std::error_code directory_error;
    fs::create_directories(output_jsonl.parent_path(), directory_error);
    if (directory_error) return static_cast<int>(directory_error.value());
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    HANDLE output = CreateFileW(output_jsonl.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                &security, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (output == INVALID_HANDLE_VALUE) {
        const DWORD output_error = GetLastError();
        return static_cast<int>(output_error);
    }
    SetFilePointer(output, 0, nullptr, FILE_END);
    HANDLE job = CreateJobObjectW(nullptr, nullptr);
    if (job == nullptr) {
        const auto code = GetLastError();
        CloseHandle(output);
        return static_cast<int>(code);
    }
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, sizeof(limits))) {
        const auto code = GetLastError();
        CloseHandle(job);
        CloseHandle(output);
        return static_cast<int>(code);
    }
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    startup.hStdOutput = output;
    startup.hStdError = output;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    PROCESS_INFORMATION process{};
    std::wstring command = Quote(pktmon->wstring()) + L" counters --live --refresh-rate 1 --json";
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');
    const BOOL created = CreateProcessW(pktmon->c_str(), mutable_command.data(), nullptr, nullptr, TRUE,
                                        CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP,
                                        nullptr, nullptr, &startup, &process);
    const DWORD create_error = created ? ERROR_SUCCESS : GetLastError();
    CloseHandle(output);
    if (!created) {
        CloseHandle(job);
        return static_cast<int>(create_error);
    }
    if (!AssignProcessToJobObject(job, process.hProcess)) {
        const auto code = GetLastError();
        TerminateProcess(process.hProcess, code);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        CloseHandle(job);
        return static_cast<int>(code);
    }
    ResumeThread(process.hThread);
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exit_code = ERROR_GEN_FAILURE;
    GetExitCodeProcess(process.hProcess, &exit_code);
    CloseHandle(process.hProcess);
    CloseHandle(job);
    return static_cast<int>(exit_code);
}

bool WriteCaptureWorkerStatus(const fs::path& status_path, std::string_view nonce, int exit_code) {
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user)) return false;
    const auto status = MakeJsonObject({
        {"Nonce", std::string(nonce)}, {"ProcessId", std::to_string(GetCurrentProcessId())},
        {"CreationTime", std::to_string(FileTimeValue(created))}, {"ExitCode", std::to_string(exit_code)},
        {"CompletedAtUtc", UtcNow()}
    }, {"ProcessId", "CreationTime", "ExitCode"});
    return WriteUtf8FileAtomic(status_path, status + "\n");
}

std::optional<CaptureWorkerIdentity> StartEtwConsumerProcess(const fs::path& session_path,
                                                              HANDLE cancel_event,
                                                              std::wstring_view cancel_event_name,
                                                              std::string* error) {
    if (cancel_event == nullptr || cancel_event_name.empty()) {
        if (error) *error = "ETW consumer requires a controller cancellation event";
        return std::nullopt;
    }
    return StartPrivateWorker(session_path, "etw-consumer",
        L"--internal-worker etw-consume --logger God2PacketCapture --output " +
        Quote((session_path / L"raw" / L"etw-events.jsonl").wstring()), cancel_event,
        cancel_event_name, true, error);
}

std::optional<CaptureWorkerIdentity> StartPktMonCounterProcess(const fs::path& session_path, std::string* error) {
    if (!FindSystemExecutable(L"pktmon.exe")) {
        if (error) *error = "pktmon.exe is unavailable for live counters";
        return std::nullopt;
    }
    return StartPrivateWorker(session_path, "pktmon-counter",
        L"--internal-worker pktmon-counter-worker --output " + Quote((session_path / L"raw" / L"pktmon-counters.jsonl").wstring()),
        nullptr, {}, false, error);
}

bool WaitForCaptureWorker(const CaptureWorkerIdentity& identity, DWORD timeout_ms, std::string* error) {
    if (identity.process_id == 0 || identity.creation_time == 0 || identity.nonce.empty() ||
        identity.image_path.empty() || identity.status_path.empty()) {
        if (error) *error = "capture worker identity is incomplete";
        return false;
    }
    HANDLE process = OpenProcess(SYNCHRONIZE | PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION,
                                 FALSE, identity.process_id);
    if (process == nullptr) return ReadSuccessfulWorkerStatus(identity, error);
    const auto actual_creation = ProcessCreationTime(process);
    const auto actual_image = ProcessImagePath(process);
    if (actual_creation != identity.creation_time || !actual_image || !SamePath(*actual_image, identity.image_path)) {
        CloseHandle(process);
        if (error) *error = "capture worker PID no longer identifies the recorded process";
        return false;
    }
    const DWORD wait = WaitForSingleObject(process, timeout_ms);
    if (wait == WAIT_TIMEOUT) {
        TerminateProcess(process, ERROR_TIMEOUT);
        WaitForSingleObject(process, 5'000);
        CloseHandle(process);
        if (error) *error = "capture worker required bounded termination after capture stop";
        return false;
    }
    if (wait != WAIT_OBJECT_0) {
        const DWORD code = wait == WAIT_FAILED ? GetLastError() : ERROR_GEN_FAILURE;
        CloseHandle(process);
        if (error) *error = "capture worker wait failed: " + std::to_string(code);
        return false;
    }
    DWORD exit_code = ERROR_GEN_FAILURE;
    GetExitCodeProcess(process, &exit_code);
    CloseHandle(process);
    if (exit_code != 0) {
        if (error) *error = "capture worker exited with code " + std::to_string(exit_code);
        return false;
    }
    return ReadSuccessfulWorkerStatus(identity, error);
}

CaptureWorkerRuntimeStatus QueryCaptureWorkerRuntime(const CaptureWorkerIdentity& identity) {
    CaptureWorkerRuntimeStatus status;
    if (identity.process_id == 0 || identity.creation_time == 0 || identity.image_path.empty() ||
        identity.status_path.empty() || identity.nonce.empty()) {
        status.win32_error = ERROR_INVALID_DATA;
        return status;
    }
    HANDLE process = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, identity.process_id);
    if (process == nullptr) {
        const DWORD open_error = GetLastError();
        DWORD recorded_exit = ERROR_GEN_FAILURE;
        std::string ignored;
        if (ReadWorkerStatus(identity, &recorded_exit, &ignored)) {
            status.identity_valid = true;
            status.running = false;
            status.exit_code = recorded_exit;
        } else {
            status.win32_error = open_error;
        }
        return status;
    }
    const auto actual_creation = ProcessCreationTime(process);
    const auto actual_image = ProcessImagePath(process);
    if (actual_creation != identity.creation_time || !actual_image || !SamePath(*actual_image, identity.image_path)) {
        CloseHandle(process);
        status.win32_error = ERROR_INVALID_DATA;
        return status;
    }
    status.identity_valid = true;
    const DWORD wait = WaitForSingleObject(process, 0);
    if (wait == WAIT_TIMEOUT) {
        status.running = true;
        status.exit_code = STILL_ACTIVE;
    } else if (wait == WAIT_OBJECT_0) {
        status.running = false;
        if (!GetExitCodeProcess(process, &status.exit_code)) {
            status.identity_valid = false;
            status.win32_error = GetLastError();
        }
    } else {
        status.identity_valid = false;
        status.win32_error = wait == WAIT_FAILED ? GetLastError() : ERROR_GEN_FAILURE;
    }
    CloseHandle(process);
    return status;
}

} // namespace god2
