#pragma once

#include "Core.h"

namespace god2 {

struct CaptureWorkerIdentity {
    DWORD process_id = 0;
    std::uint64_t creation_time = 0;
    fs::path image_path;
    fs::path status_path;
    fs::path log_path;
    std::string nonce;
    std::wstring ready_event_name;
    std::wstring cancel_event_name;
    std::string worker_type;
    bool ready_confirmed = false;
};

struct EtwConsumerOptions {
    std::wstring logger_name;
    fs::path output_jsonl;
    fs::path worker_log_path;
    std::wstring ready_event_name;
    std::wstring cancel_event_name;
    std::string session_id;
};

struct CaptureWorkerRuntimeStatus {
    bool identity_valid = false;
    bool running = false;
    DWORD exit_code = STILL_ACTIVE;
    DWORD win32_error = ERROR_SUCCESS;
};

struct EtwFileAuditResult {
    bool success = false;
    DWORD win32_error = ERROR_SUCCESS;
    std::uint64_t total_events = 0;
    std::uint64_t ndis_packet_events = 0;
    std::uint64_t ndis_payload_bytes = 0;
    std::uint64_t events_lost = 0;
    std::uint64_t buffers_lost = 0;
};

bool IsRecoverableEtwOpenTraceError(DWORD error);
bool IsEtwConsumerStartupExitContractValid(bool ready_signaled, DWORD exit_code);
int EtwConsumerProcessTraceExitCode(bool stop_requested, ULONG process_trace_result);
int ConsumeEtwRealtime(const EtwConsumerOptions& options);
EtwFileAuditResult AuditEtwFile(const fs::path& etl_path,
                                const fs::path& report_path,
                                std::string* error = nullptr);
int RunPktMonCounterWorker(const fs::path& output_jsonl);
bool WriteCaptureWorkerStatus(const fs::path& status_path, std::string_view nonce, int exit_code);
std::optional<CaptureWorkerIdentity> StartEtwConsumerProcess(const fs::path& session_path,
                                                              HANDLE cancel_event,
                                                              std::wstring_view cancel_event_name,
                                                              std::string* error = nullptr);
std::optional<CaptureWorkerIdentity> StartPktMonCounterProcess(const fs::path& session_path,
                                                               std::string* error = nullptr);
bool WaitForCaptureWorker(const CaptureWorkerIdentity& identity,
                          DWORD timeout_ms,
                          std::string* error = nullptr);
CaptureWorkerRuntimeStatus QueryCaptureWorkerRuntime(const CaptureWorkerIdentity& identity);

} // namespace god2
