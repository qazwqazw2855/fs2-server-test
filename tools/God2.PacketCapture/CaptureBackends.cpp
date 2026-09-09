#include "Core.h"

#include <filesystem>

namespace god2 {
namespace {

void AddBlockedCapabilities(BackendCapabilities& c) {
    const std::pair<const char*, bool> values[] = {
        {"SupportsRealtimeEvents", c.supports_realtime_events},
        {"SupportsPacketPayload", c.supports_packet_payload},
        {"SupportsDirection", c.supports_direction},
        {"SupportsEndpointFilter", c.supports_endpoint_filter},
        {"SupportsProcessFilter", c.supports_process_filter},
        {"SupportsDropStatistics", c.supports_drop_statistics},
        {"SupportsPcapngConversion", c.supports_pcapng_conversion},
        {"SupportsIpv4", c.supports_ipv4},
        {"SupportsIpv6", c.supports_ipv6}
    };
    c.blocked_capabilities.clear();
    for (const auto& [name, supported] : values) {
        if (!supported) c.blocked_capabilities.emplace_back(name);
    }
}

int CapabilityScore(const BackendCapabilities& c) {
    if (!c.available) return -1;
    return static_cast<int>(c.supports_packet_payload) * 8 +
           static_cast<int>(c.supports_direction) * 4 +
           static_cast<int>(c.supports_pcapng_conversion) * 4 +
           static_cast<int>(c.supports_realtime_events) * 2 +
           static_cast<int>(c.supports_endpoint_filter) +
           static_cast<int>(c.supports_drop_statistics);
}

bool WriteRawCapturePrivacyPolicy(const CaptureRequest& request,
                                  std::string_view backend,
                                  std::string_view phase,
                                  std::string* error) {
    std::error_code ec;
    fs::create_directories(request.session_path / L"compatibility", ec);
    if (ec) {
        if (error != nullptr) *error = "failed to create the compatibility report directory";
        return false;
    }
    const std::string report = MakeJsonObject({
        {"SchemaVersion", "1"},
        {"Status", "EvidenceBlockedPrivacyPolicy"},
        {"Phase", std::string(phase)},
        {"Backend", std::string(backend)},
        {"RawSystemCapture", "Disabled"},
        {"RawNetworkPayloadPersistence", "false"},
        {"PayloadBytesCaptured", "false"},
        {"PacketBytesWritten", "false"},
        {"NetshCaptureExecuted", "false"},
        {"PktMonCaptureExecuted", "false"},
        {"AuthenticationPlaintextPersistence", "Prohibited"},
        {"AllowedEvidenceChannel", "ExactBuildX86MaskedSemanticEvents"},
        {"Reason", "System-wide raw packet bytes cannot prove authentication-plaintext exclusion"}
    }, {"SchemaVersion", "RawNetworkPayloadPersistence", "PayloadBytesCaptured", "PacketBytesWritten",
        "NetshCaptureExecuted", "PktMonCaptureExecuted"});
    if (WriteUtf8FileAtomic(request.session_path / L"compatibility" /
                            L"raw-network-capture-policy.json", report + "\n")) return true;
    if (error != nullptr) *error = "failed to commit the raw-network privacy policy report";
    return false;
}

} // namespace

BackendCapabilities EtwNetworkTraceBackend::Probe() {
    BackendCapabilities capabilities;
    capabilities.capture_backend_name = Name();
    capabilities.analysis_mode = AnalysisMode::NearRealtime;
    const auto netsh = FindSystemExecutable(L"netsh.exe");
    capabilities.capture_backend_version = netsh ? FileVersion(*netsh) : "NotRequired";
    // This object is deliberately a privacy guard, not an available capture
    // backend. Production never starts a system-wide packet trace. Exact-build
    // x86 semantic events use the independently masked instrumentation channel.
    capabilities.available = false;
    capabilities.supports_realtime_events = false;
    capabilities.analysis_mode = AnalysisMode::PostCapture;
    capabilities.supports_packet_payload = false;
    capabilities.supports_direction = false;
    capabilities.supports_endpoint_filter = false;
    capabilities.supports_process_filter = false;
    capabilities.supports_drop_statistics = false;
    capabilities.supports_pcapng_conversion = false;
    capabilities.supports_ipv4 = false;
    capabilities.supports_ipv6 = false;
    capabilities.probe_evidence =
        "EvidenceBlockedPrivacyPolicy: netsh capture is never executed and no system-wide packet bytes are persisted; exact-build x86 masked semantic events remain available";
    AddBlockedCapabilities(capabilities);
    return capabilities;
}

CaptureResult EtwNetworkTraceBackend::Start(const CaptureRequest& request) {
    std::string error;
    if (!WriteRawCapturePrivacyPolicy(request, Name(), "Blocked", &error))
        return {false, ERROR_WRITE_FAULT, std::move(error)};
    return {false, ERROR_ACCESS_DISABLED_BY_POLICY,
            "EvidenceBlockedPrivacyPolicy: system-wide ETW packet capture is disabled; no raw network payload bytes are written"};
}

CaptureResult EtwNetworkTraceBackend::Stop(const CaptureRequest& request) {
    std::string error;
    if (!WriteRawCapturePrivacyPolicy(request, Name(), "NotStarted", &error))
        return {false, ERROR_WRITE_FAULT, std::move(error)};
    return {false, ERROR_ACCESS_DISABLED_BY_POLICY,
            "EvidenceBlockedPrivacyPolicy: no ETW raw backend was started; netsh was not executed"};
}

BackendCapabilities PktMonCaptureBackend::Probe() {
    BackendCapabilities capabilities;
    capabilities.capture_backend_name = Name();
    capabilities.analysis_mode = AnalysisMode::PostCapture;
    const auto os = DetectOsVersion();
    if (!BackendPolicyAllowsPktMon(os.major, os.minor)) {
        capabilities.probe_evidence = "NotProbedByPolicy: pktmon is never executed on Windows 7/8/8.1";
        AddBlockedCapabilities(capabilities);
        return capabilities;
    }

    const auto pktmon = FindSystemExecutable(L"pktmon.exe");
    if (!pktmon) {
        capabilities.probe_evidence = "pktmon.exe was not found by SearchPathW";
        AddBlockedCapabilities(capabilities);
        return capabilities;
    }
    capabilities.capture_backend_version = FileVersion(*pktmon);
    capabilities.available = false;
    capabilities.supports_realtime_events = false;
    capabilities.supports_packet_payload = false;
    capabilities.supports_direction = false;
    capabilities.supports_endpoint_filter = false;
    capabilities.supports_process_filter = false;
    capabilities.supports_drop_statistics = false;
    capabilities.supports_pcapng_conversion = false;
    capabilities.supports_ipv4 = false;
    capabilities.supports_ipv6 = false;
    capabilities.analysis_mode = AnalysisMode::PostCapture;
    capabilities.probe_evidence =
        "EvidenceBlockedPrivacyPolicy: pktmon capture and ETL conversion are never executed; full-packet persistence is prohibited";
    AddBlockedCapabilities(capabilities);
    return capabilities;
}

CaptureResult PktMonCaptureBackend::Start(const CaptureRequest& request) {
    started_by_this_instance_ = false;
    const auto os = DetectOsVersion();
    if (!BackendPolicyAllowsPktMon(os.major, os.minor)) {
        return {false, ERROR_OLD_WIN_VERSION, "pktmon is prohibited on Windows 7/8/8.1"};
    }
    std::string error;
    if (!WriteRawCapturePrivacyPolicy(request, Name(), "Blocked", &error))
        return {false, ERROR_WRITE_FAULT, std::move(error)};
    started_by_this_instance_ = false;
    return {false, ERROR_ACCESS_DISABLED_BY_POLICY,
            "EvidenceBlockedPrivacyPolicy: PktMon packet capture is disabled; no ETL or PCAPNG payload artifact is written"};
}

CaptureResult PktMonCaptureBackend::Stop(const CaptureRequest& request) {
    const auto os = DetectOsVersion();
    if (!BackendPolicyAllowsPktMon(os.major, os.minor)) {
        return {false, ERROR_OLD_WIN_VERSION, "pktmon is prohibited on Windows 7/8/8.1"};
    }
    std::string error;
    if (!WriteRawCapturePrivacyPolicy(request, Name(), "NotStarted", &error))
        return {false, ERROR_WRITE_FAULT, std::move(error)};
    started_by_this_instance_ = false;
    return {false, ERROR_ACCESS_DISABLED_BY_POLICY,
            "EvidenceBlockedPrivacyPolicy: no PktMon raw backend was started; pktmon was not executed"};
}

BackendSelection SelectCaptureBackend(bool require_endpoint_filter, bool require_process_filter) {
    BackendSelection selection;
    const auto os = DetectOsVersion();
    const auto eligible = [&](const BackendCapabilities& capabilities) {
        return capabilities.available &&
               (!require_endpoint_filter || capabilities.supports_endpoint_filter) &&
               (!require_process_filter || capabilities.supports_process_filter);
    };

    auto etw = std::make_unique<EtwNetworkTraceBackend>();
    auto etw_capabilities = etw->Probe();

    if (!BackendPolicyAllowsPktMon(os.major, os.minor)) {
        BackendCapabilities pktmon_not_probed;
        pktmon_not_probed.capture_backend_name = "PktMonCaptureBackend";
        pktmon_not_probed.probe_evidence = "NotProbedByPolicy: Windows 7/8/8.1 always selects ETW/netsh";
        pktmon_not_probed.blocked_capabilities = {
            "SupportsRealtimeEvents", "SupportsPacketPayload", "SupportsDirection", "SupportsEndpointFilter",
            "SupportsProcessFilter", "SupportsDropStatistics", "SupportsPcapngConversion", "SupportsIpv4", "SupportsIpv6"
        };
        selection.all_capabilities = {etw_capabilities, pktmon_not_probed};
        selection.capabilities = etw_capabilities;
        // The object is returned only to expose the explicit policy-blocked
        // Start result. Its capabilities remain unavailable and it is never
        // promoted to an active capture backend.
        selection.backend = std::move(etw);
        return selection;
    }

    auto pktmon = std::make_unique<PktMonCaptureBackend>();
    auto pktmon_capabilities = pktmon->Probe();
    selection.all_capabilities = {pktmon_capabilities, etw_capabilities};
    // Both implementations are privacy guards in production. Prefer the ETW
    // identity consistently so Stop never risks interacting with a global
    // PktMon capture owned by another program.
    if (eligible(etw_capabilities)) {
        selection.capabilities = etw_capabilities;
        selection.backend = std::move(etw);
    } else if (CapabilityScore(pktmon_capabilities) >= CapabilityScore(etw_capabilities) && eligible(pktmon_capabilities)) {
        selection.capabilities = pktmon_capabilities;
        selection.backend = std::move(pktmon);
    } else if (eligible(pktmon_capabilities)) {
        selection.capabilities = pktmon_capabilities;
        selection.backend = std::move(pktmon);
    } else {
        selection.capabilities = etw_capabilities;
        selection.backend = std::move(etw);
    }
    return selection;
}

} // namespace god2
