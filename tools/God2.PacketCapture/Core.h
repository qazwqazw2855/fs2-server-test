#pragma once

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#ifndef WINVER
#define WINVER 0x0601
#endif
#ifndef NTDDI_VERSION
#define NTDDI_VERSION 0x06010000
#endif

#include <windows.h>

#include <cstdint>
#include <filesystem>
#include <functional>
#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace god2 {

namespace fs = std::filesystem;
using Fields = std::unordered_map<std::string, std::string>;

enum class EvidenceLevel {
    Unknown,
    Candidate,
    Derived,
    Transmitted,
    Verified
};

enum class AnalysisMode {
    Realtime,
    NearRealtime,
    PostCapture
};

struct FeatureMetadata {
    std::string feature_id;
    std::string feature_version;
    std::string schema_version;
    std::string analyzer_version;
    std::string minimum_protocol_version;
    std::vector<std::string> supported_entity_types;
    std::vector<std::string> supported_message_types;
};

struct Frame {
    std::string frame_id;
    std::string source_frame_ids;
    std::string timestamp_utc;
    std::string direction;
    std::string opcode;
    std::string message_type;
    std::string payload_hex;
    std::string source_json;
    std::string original_json;
    Fields fields;
};

struct ProcessResult {
    DWORD exit_code = ERROR_GEN_FAILURE;
    bool started = false;
    std::string output;
};

struct BackendCapabilities {
    std::string capture_backend_name;
    std::string capture_backend_version;
    bool available = false;
    bool supports_realtime_events = false;
    bool supports_packet_payload = false;
    bool supports_direction = false;
    bool supports_endpoint_filter = false;
    bool supports_process_filter = false;
    bool supports_drop_statistics = false;
    bool supports_pcapng_conversion = false;
    bool supports_ipv4 = false;
    bool supports_ipv6 = false;
    AnalysisMode analysis_mode = AnalysisMode::PostCapture;
    std::vector<std::string> blocked_capabilities;
    std::string probe_evidence;
};

struct CaptureRequest {
    fs::path session_path;
    DWORD process_id = 0;
    std::string endpoint_filter;
};

struct CaptureResult {
    bool success = false;
    DWORD exit_code = ERROR_GEN_FAILURE;
    std::string message;
};

struct OsVersion {
    DWORD major = 0;
    DWORD minor = 0;
    DWORD build = 0;
    WORD service_pack_major = 0;
    WORD service_pack_minor = 0;
    std::string edition;
    std::string architecture;
};

std::wstring Utf8ToWide(std::string_view value);
std::string WideToUtf8(std::wstring_view value);
std::string JsonEscape(std::string_view value);
std::string CsvEscape(std::string_view value);
std::string SqlQuote(std::string_view value);
std::string UtcNow();
std::string NewId();
std::string ToString(EvidenceLevel value);
std::string ToString(AnalysisMode value);
std::optional<double> GetDouble(const Fields& fields, std::string_view key);
std::optional<std::int64_t> GetInt64(const Fields& fields, std::string_view key);
std::optional<bool> GetBool(const Fields& fields, std::string_view key);
std::string GetString(const Fields& fields, std::string_view key, std::string_view fallback = {});
std::vector<std::string> SplitList(std::string_view value);
std::string JoinList(const std::vector<std::string>& values);
bool ParseFlatJson(std::string_view json, Fields& output, std::string* error = nullptr);
std::string MakeJsonObject(const std::vector<std::pair<std::string, std::string>>& fields,
                           const std::set<std::string>& raw_json_keys = {});
bool WriteUtf8File(const fs::path& path, std::string_view value, bool append = false);
bool WriteUtf8FileAtomic(const fs::path& path, std::string_view value);
std::optional<std::string> ReadUtf8File(const fs::path& path);
std::wstring Win32ExtendedPathForFileIo(const fs::path& path);
bool IsAdministrator();
fs::path ExecutablePath();
fs::path LocalDataRoot();
fs::path DefaultSessionsRoot();
bool IsApprovedSessionPath(const fs::path& path);
OsVersion DetectOsVersion();
bool BackendPolicyAllowsPktMon(DWORD os_major, DWORD os_minor);
bool IsTraceStopFinalizationProgress(std::string_view output);
ProcessResult RunProcess(const fs::path& executable,
                         const std::wstring& arguments,
                         DWORD timeout_ms = 30'000,
                         bool hidden = true);
std::optional<fs::path> FindSystemExecutable(std::wstring_view file_name);
int RelaunchElevated(const std::wstring& parameters);
std::string FileVersion(const fs::path& path);
std::vector<std::string> AuditImportedFunctions(const fs::path& image_path, std::string* error = nullptr);

class ICaptureBackend {
public:
    virtual ~ICaptureBackend() = default;
    virtual BackendCapabilities Probe() = 0;
    virtual CaptureResult Start(const CaptureRequest& request) = 0;
    virtual CaptureResult Stop(const CaptureRequest& request) = 0;
    virtual std::string Name() const = 0;
};

class EtwNetworkTraceBackend final : public ICaptureBackend {
public:
    BackendCapabilities Probe() override;
    CaptureResult Start(const CaptureRequest& request) override;
    CaptureResult Stop(const CaptureRequest& request) override;
    std::string Name() const override { return "EtwNetworkTraceBackend"; }
};

class PktMonCaptureBackend final : public ICaptureBackend {
public:
    BackendCapabilities Probe() override;
    CaptureResult Start(const CaptureRequest& request) override;
    CaptureResult Stop(const CaptureRequest& request) override;
    std::string Name() const override { return "PktMonCaptureBackend"; }

private:
    bool started_by_this_instance_ = false;
};

struct BackendSelection {
    std::unique_ptr<ICaptureBackend> backend;
    BackendCapabilities capabilities;
    std::vector<BackendCapabilities> all_capabilities;
};

BackendSelection SelectCaptureBackend(bool require_endpoint_filter = false,
                                      bool require_process_filter = false);
std::string CapabilitiesJson(const BackendCapabilities& capabilities);
bool ValidatePcapng(const fs::path& path, std::string* reason = nullptr);

} // namespace god2
