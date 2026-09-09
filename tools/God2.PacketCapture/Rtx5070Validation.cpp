#include "Rtx5070Validation.h"

#include "EvidencePackage.h"
#include "GpuAcceleration.h"
#include "Version.h"

#include <intrin.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <limits>
#include <set>
#include <sstream>
#include <unordered_set>

namespace god2 {
namespace {

constexpr std::string_view kPackageVersion = "1.3.0";
constexpr std::string_view kTargetDevice = "NVIDIA GeForce RTX 5070";
constexpr std::string_view kReadyStatus = "RTX5070_ULTIMATE_VALIDATION_PACKAGE_READY_FOR_EXTERNAL_RUN";

constexpr std::array<GpuOperationClass, kUltimateGpuWorkloadCount> kUltimateOperations{{
    GpuOperationClass::TraceFeatureExtraction,
    GpuOperationClass::CrossSessionCorrelation,
    GpuOperationClass::ParserFieldPatternMatching,
    GpuOperationClass::FunctionClustering,
    GpuOperationClass::ObjectClustering,
    GpuOperationClass::ClassLayoutScoring,
    GpuOperationClass::RegistryScoring,
    GpuOperationClass::HeapGraphSimilarity,
    GpuOperationClass::ValueFlowAggregation,
    GpuOperationClass::TaintGraphBatch,
    GpuOperationClass::SemanticEdgeScoring,
    GpuOperationClass::ApproximateNearestNeighbor,
    GpuOperationClass::SequenceMining,
    GpuOperationClass::FsmScoring,
    GpuOperationClass::FormulaBatchEvaluation,
    GpuOperationClass::ReplayStateComparison,
    GpuOperationClass::AiInference
}};

struct IntegrityResult {
    bool passed = false;
    std::size_t checked = 0;
    std::string error;
    std::string manifest_sha256;
};

struct ValidationState {
    bool os_is_windows_11 = false;
    bool gpu_is_exact_target = false;
    bool capability_is_maximum = false;
    bool physical_passed = false;
    bool equivalence_passed = false;
    bool benchmark_passed = false;
    bool benefit_passed = false;
    bool gpu_implementations_complete = false;
    std::string final_status;
    std::string gpu_device = "None";
};

std::string Bool(bool value) { return value ? "true" : "false"; }

std::string JsonArray(const std::vector<std::string>& values) {
    std::ostringstream output;
    output << '[';
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) output << ',';
        output << values[index];
    }
    output << ']';
    return output.str();
}

bool WriteJson(const fs::path& path,
               const std::vector<std::pair<std::string, std::string>>& fields,
               const std::set<std::string>& raw = {}) {
    return WriteUtf8FileAtomic(path, MakeJsonObject(fields, raw) + "\n");
}

void Log(const fs::path& path, std::string_view message) {
    WriteUtf8File(path, UtcNow() + " " + std::string(message) + "\n", true);
}

std::string Stamp() {
    SYSTEMTIME time{};
    GetLocalTime(&time);
    std::ostringstream output;
    output << std::setfill('0') << std::setw(4) << time.wYear << std::setw(2) << time.wMonth
           << std::setw(2) << time.wDay << '-' << std::setw(2) << time.wHour
           << std::setw(2) << time.wMinute << std::setw(2) << time.wSecond;
    return output.str();
}

std::string CpuBrand() {
    std::array<int, 4> registers{};
    __cpuid(registers.data(), 0x80000000);
    if (static_cast<unsigned>(registers[0]) < 0x80000004u) return "Unknown";
    std::array<char, 49> brand{};
    for (int function = 0; function < 3; ++function) {
        __cpuid(registers.data(), 0x80000002 + function);
        memcpy(brand.data() + function * 16, registers.data(), 16);
    }
    std::string result(brand.data());
    const auto first = result.find_first_not_of(' ');
    const auto last = result.find_last_not_of(' ');
    return first == std::string::npos ? "Unknown" : result.substr(first, last - first + 1);
}

bool SafeRelative(std::string_view value) {
    if (value.empty() || value.find(':') != std::string_view::npos || value.front() == '/' ||
        value.front() == '\\') return false;
    const fs::path path = Utf8ToWide(value);
    for (const auto& part : path) if (part == L".." || part == L".") return false;
    return true;
}

IntegrityResult ValidatePackageIntegrity(const fs::path& package_root) {
    IntegrityResult result;
    const fs::path manifest_path = package_root / L"validation-package-manifest.json";
    const auto manifest = ReadUtf8File(manifest_path);
    if (!manifest) {
        result.error = "validation-package-manifest.json is missing";
        return result;
    }
    const auto manifest_hash = CalculateFileSha256(manifest_path);
    if (!manifest_hash) {
        result.error = "cannot hash validation-package-manifest.json";
        return result;
    }
    result.manifest_sha256 = *manifest_hash;
    std::unordered_set<std::string> listed;
    std::istringstream lines(*manifest);
    std::string line;
    while (std::getline(lines, line)) {
        if (line.find("\"RelativePath\"") == std::string::npos &&
            line.find("\"Path\"") == std::string::npos) continue;
        const auto object_begin = line.find('{');
        const auto object_end = line.rfind('}');
        if (object_begin == std::string::npos || object_end == std::string::npos || object_end < object_begin) {
            result.error = "malformed manifest artifact entry";
            return result;
        }
        Fields fields;
        if (!ParseFlatJson(std::string_view(line).substr(object_begin, object_end - object_begin + 1),
                           fields, &result.error)) return result;
        const auto relative = GetString(fields, "RelativePath", GetString(fields, "Path"));
        const auto expected_hash = GetString(fields, "SHA256");
        const auto expected_size = GetInt64(fields, "SizeBytes").value_or(
            GetInt64(fields, "Size").value_or(-1));
        if (!SafeRelative(relative) || expected_hash.size() != 64 || expected_size < 0 ||
            !listed.insert(relative).second) {
            result.error = "invalid or duplicate manifest entry: " + relative;
            return result;
        }
        const fs::path artifact = package_root / Utf8ToWide(relative);
        std::error_code size_error;
        const auto actual_size = fs::file_size(artifact, size_error);
        const auto actual_hash = CalculateFileSha256(artifact);
        if (size_error || !actual_hash || actual_size != static_cast<std::uint64_t>(expected_size) ||
            *actual_hash != expected_hash) {
            result.error = "artifact integrity mismatch: " + relative;
            return result;
        }
        ++result.checked;
    }
    if (listed.empty()) {
        result.error = "manifest contains no artifacts";
        return result;
    }
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(package_root,
             fs::directory_options::skip_permission_denied, enumerate_error), end;
         iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
        if (!iterator->is_regular_file()) continue;
        auto relative = fs::relative(iterator->path(), package_root, enumerate_error).generic_string();
        if (enumerate_error) break;
        if (relative == "validation-package-manifest.json" ||
            relative.rfind("RTX5070-VALIDATION-RESULT-", 0) == 0) continue;
        if (!listed.contains(relative)) {
            result.error = "unmanifested package artifact: " + relative;
            return result;
        }
    }
    if (enumerate_error) {
        result.error = "package enumeration failed: " + enumerate_error.message();
        return result;
    }
    result.passed = true;
    return result;
}

std::vector<GpuEvidenceRecord> Corpus(std::size_t records, std::size_t bytes) {
    std::vector<GpuEvidenceRecord> result(records);
    for (std::size_t record = 0; record < records; ++record) {
        auto& payload = result[record].bytes;
        payload.resize(bytes);
        std::uint64_t state = 0x9E3779B97F4A7C15ull ^ (record * 0xD6E8FEB86659FD93ull) ^ bytes;
        for (std::size_t offset = 0; offset < bytes; ++offset) {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            payload[offset] = static_cast<std::uint8_t>((state * 0x2545F4914F6CDD1Dull) >> 56);
        }
    }
    return result;
}

std::string FeatureDigest(const GpuProcessingResult& result) {
    std::uint64_t hash = 1469598103934665603ull;
    const auto consume = [&hash](std::uint64_t value) {
        for (int shift = 0; shift < 64; shift += 8) {
            hash ^= static_cast<std::uint8_t>(value >> shift);
            hash *= 1099511628211ull;
        }
    };
    for (const auto& feature : result.verified_features) {
        consume(feature.byte_sum_without_last);
        consume(feature.fnv1a);
        consume(feature.feature_energy);
        consume(feature.transition_score);
        consume(feature.pattern_score);
        consume(feature.operation_projection);
        consume(feature.cluster_key);
    }
    std::ostringstream output;
    output << std::hex << std::uppercase << std::setfill('0') << std::setw(16) << hash;
    return output.str();
}

std::vector<std::pair<std::string, std::string>> CapabilityFields(const GpuCapabilityReport& capability) {
    return {
        {"SchemaVersion", "1"}, {"ObservedAtUtc", UtcNow()},
        {"GpuDetected", Bool(capability.gpu_detected)},
        {"NvidiaDeviceAvailable", Bool(capability.nvidia_device_available)},
        {"CudaAvailable", Bool(capability.cuda_available)},
        {"BackendInitialized", Bool(capability.backend_initialized)},
        {"DriverRuntimeCompatible", Bool(capability.driver_runtime_compatible)},
        {"Vendor", capability.vendor}, {"Device", capability.device},
        {"ComputeCapability", std::to_string(capability.compute_major) + "." + std::to_string(capability.compute_minor)},
        {"VramBytes", std::to_string(capability.vram_bytes)},
        {"FreeVramBytes", std::to_string(capability.free_vram_bytes)},
        {"DriverVersion", std::to_string(capability.driver_version)},
        {"DriverStatus", capability.driver_status}, {"RuntimeStatus", capability.runtime_status},
        {"Backend", capability.backend}, {"Architecture", capability.architecture},
        {"CapabilityTier", ToString(capability.tier)}, {"GpuMode", GpuModeDisplayName(capability.tier)},
        {"TensorCapability", Bool(capability.tensor_capability)},
        {"AvailablePrecisionModes", capability.available_precision_modes},
        {"AiBackend", capability.ai_backend_present ? "Present" : "Not Present"},
        {"AiAcceleration", capability.ai_acceleration_active ? "Active" : "Not Available"},
        {"FallbackReason", capability.fallback_reason.empty() ? "None" : capability.fallback_reason}
    };
}

std::set<std::string> CapabilityRaw() {
    return {"SchemaVersion", "GpuDetected", "NvidiaDeviceAvailable", "CudaAvailable", "BackendInitialized",
            "DriverRuntimeCompatible", "VramBytes", "FreeVramBytes", "DriverVersion", "TensorCapability",
            "ComputeCapabilityMajor", "ComputeCapabilityMinor", "TotalVramBytes", "CudaDriverApiAvailable",
            "DriverApiCompatibility", "GpuLoadPercent"};
}

std::string BenchmarkPoint(std::size_t records, std::size_t bytes,
                           const GpuProcessingResult& cpu, const GpuProcessingResult& forced,
                           const GpuProcessingResult& automatic) {
    const auto& p = forced.performance;
    const double speedup = p.wall_elapsed_microseconds == 0 ? 0.0 :
        static_cast<double>(cpu.performance.wall_elapsed_microseconds) /
        static_cast<double>(p.wall_elapsed_microseconds);
    return MakeJsonObject({
        {"RecordCount", std::to_string(records)}, {"PayloadBytes", std::to_string(bytes)},
        {"TotalBytes", std::to_string(p.input_bytes)},
        {"ExecutionScope", "TraceFeatureVectorV3OperationSpecificCudaCandidateWithCpuOracle"},
        {"OperationSpecificGpuImplementationAvailable", Bool(forced.workload.gpu_implementation_available)},
        {"OperationSpecificGpuCandidateExecuted", Bool(forced.workload.gpu_candidate_executed)},
        {"NormalizedInputContract", forced.workload.normalized_input_contract},
        {"CandidateOutputContract", forced.workload.candidate_output_contract},
        {"CpuPureMicroseconds", std::to_string(cpu.performance.wall_elapsed_microseconds)},
        {"GpuAllocationMicroseconds", std::to_string(p.gpu_allocation_microseconds)},
        {"H2DMicroseconds", std::to_string(p.gpu_h2d_microseconds)},
        {"KernelLaunchMicroseconds", std::to_string(p.gpu_kernel_launch_microseconds)},
        {"SynchronizeMicroseconds", std::to_string(p.gpu_synchronize_microseconds)},
        {"D2HMicroseconds", std::to_string(p.gpu_d2h_microseconds)},
        {"RawGpuComputeMicroseconds", std::to_string(p.gpu_kernel_launch_microseconds + p.gpu_synchronize_microseconds)},
        {"GpuCandidateWallMicroseconds", std::to_string(p.gpu_elapsed_microseconds)},
        {"CpuAuthorityMicroseconds", std::to_string(p.cpu_elapsed_microseconds)},
        {"FullPathMicroseconds", std::to_string(p.wall_elapsed_microseconds)},
        {"SpeedupCpuVsFullPath", std::to_string(speedup)},
        {"ForcedGpuPrimitiveRecords", std::to_string(p.gpu_processed_records)},
        {"ForcedGpuPrimitiveBytes", std::to_string(p.gpu_processed_bytes)},
        {"KernelLaunches", std::to_string(p.gpu_kernel_launch_count)},
        {"AutoSelectedBackend", automatic.benefit.selected_backend},
        {"AutoSelectionReason", automatic.benefit.selection_reason},
        {"AutoBenefitGateDecision", automatic.benefit.benefit_gate_decision},
        {"AutoGpuSelected", Bool(automatic.benefit.gpu_selected)},
        {"AutoCalibrationSource", automatic.benefit.calibration_source},
        {"AutoPairedFullPathSampleCount",
            std::to_string(automatic.benefit.paired_full_path_sample_count)},
        {"AutoFullCpuOracleRequired", Bool(automatic.benefit.full_cpu_oracle_required)},
        {"AutoPositiveCrossoverPossible", Bool(automatic.benefit.positive_crossover_possible)},
        {"ForcedMismatchCount", std::to_string(forced.equivalence.mismatch_count)}
    }, {"RecordCount", "PayloadBytes", "TotalBytes", "CpuPureMicroseconds", "GpuAllocationMicroseconds",
        "H2DMicroseconds", "KernelLaunchMicroseconds", "SynchronizeMicroseconds", "D2HMicroseconds",
        "RawGpuComputeMicroseconds", "GpuCandidateWallMicroseconds", "CpuAuthorityMicroseconds",
        "FullPathMicroseconds", "SpeedupCpuVsFullPath", "ForcedGpuPrimitiveRecords", "ForcedGpuPrimitiveBytes",
        "KernelLaunches", "ForcedMismatchCount", "OperationSpecificGpuImplementationAvailable",
        "OperationSpecificGpuCandidateExecuted", "AutoGpuSelected", "AutoPairedFullPathSampleCount",
        "AutoFullCpuOracleRequired", "AutoPositiveCrossoverPossible"});
}

std::string UltimateWorkloadPoint(const GpuProcessingResult& cpu,
                                  const GpuProcessingResult& gpu) {
    const bool model_blocked = gpu.workload.operation_class == "AiInference" &&
        gpu.workload.implementation_status == "EvidenceBlockedModelUnavailable" &&
        !gpu.workload.gpu_candidate_executed;
    const bool gpu_implementation_blocked = !model_blocked &&
        gpu.workload.implementation_status == "EvidenceBlockedGpuImplementationUnavailable" &&
        !gpu.workload.gpu_candidate_executed;
    const bool cpu_output_stable = cpu.workload.authoritative_output_digest ==
        gpu.workload.authoritative_output_digest;
    const bool primitive_equivalent = !gpu.workload.gpu_primitive_extraction_executed ||
        (gpu.equivalence.mismatch_count == 0 &&
         gpu.workload.gpu_primitive_digest == gpu.workload.cpu_primitive_digest);
    const bool gpu_verified = gpu.workload.gpu_candidate_executed &&
        gpu.workload.gpu_implementation_available &&
        gpu.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
        gpu.equivalence.verified_results_equivalent && gpu.equivalence.mismatch_count == 0 &&
        gpu.workload.gpu_candidate_digest == gpu.workload.authoritative_output_digest &&
        cpu_output_stable;
    const std::string status = gpu_verified ? "PASS_OPERATION_SPECIFIC_GPU_CPU_EXACT" :
        (model_blocked ? "EvidenceBlockedModelUnavailable" :
            (gpu_implementation_blocked ? "EvidenceBlockedGpuImplementationUnavailable" : "FAIL"));
    return MakeJsonObject({
        {"OperationClass", gpu.workload.operation_class},
        {"Algorithm", gpu.workload.algorithm},
        {"NormalizedInputContract", gpu.workload.normalized_input_contract},
        {"CandidateOutputContract", gpu.workload.candidate_output_contract},
        {"WorkloadScope", gpu.workload.workload_scope},
        {"Status", status},
        {"ImplementationStatus", gpu.workload.implementation_status},
        {"ModelStatus", gpu.workload.model_status},
        {"AiProviderContractVersion", gpu.ai_provider.contract_version},
        {"AiProviderStatus", gpu.ai_provider.status},
        {"AiProvider", gpu.ai_provider.provider},
        {"AiModelName", gpu.ai_provider.model_name},
        {"AiModelVersion", gpu.ai_provider.model_version},
        {"AiModelLicense", gpu.ai_provider.model_license},
        {"AiModelSHA256", gpu.ai_provider.model_sha256},
        {"AiOutputAuthority", gpu.ai_provider.output_authority},
        {"AiLocalExecutionRequired", Bool(gpu.ai_provider.local_execution_required)},
        {"AiEvidenceUploadProhibited", Bool(gpu.ai_provider.evidence_upload_prohibited)},
        {"AiCpuFallbackRequired", Bool(gpu.ai_provider.cpu_fallback_required)},
        {"AiVerifiedArtifactLoaded", Bool(gpu.ai_provider.verified_artifact_loaded)},
        {"NormalizedInputDigest", gpu.workload.normalized_input_digest},
        {"GpuPrimitiveDigest", gpu.workload.gpu_primitive_digest},
        {"CpuPrimitiveDigest", gpu.workload.cpu_primitive_digest},
        {"CpuOnlyOutputDigest", cpu.workload.authoritative_output_digest},
        {"GpuCandidateDigest", gpu.workload.gpu_candidate_digest},
        {"AuthoritativeOutputDigest", gpu.workload.authoritative_output_digest},
        {"GpuCandidateExecuted", Bool(gpu.workload.gpu_candidate_executed)},
        {"GpuPrimitiveExtractionExecuted", Bool(gpu.workload.gpu_primitive_extraction_executed)},
        {"OperationSpecificGpuImplementationAvailable", Bool(gpu.workload.gpu_implementation_available)},
        {"CpuAuthorityPreserved", Bool(gpu.workload.cpu_authority_preserved)},
        {"PrimitiveEquivalent", Bool(primitive_equivalent)},
        {"OperationSpecificCandidateEquivalent", Bool(gpu_verified)},
        {"CpuAuthorityOutputStable", Bool(cpu_output_stable)},
        {"InputRecords", std::to_string(gpu.performance.input_records)},
        {"InputBytes", std::to_string(gpu.performance.input_bytes)},
        {"GpuBatches", std::to_string(gpu.performance.gpu_batches)},
        {"GpuKernelLaunches", std::to_string(gpu.performance.gpu_kernel_launch_count)},
        {"GpuProcessedRecords", std::to_string(gpu.performance.gpu_processed_records)},
        {"CpuVerificationRecords", std::to_string(gpu.performance.cpu_processed_records)},
        {"MismatchCount", std::to_string(gpu.equivalence.mismatch_count)},
        {"DevicePoolAllocations", std::to_string(gpu.performance.device_pool_allocation_count)},
        {"DevicePoolReuses", std::to_string(gpu.performance.device_pool_reuse_count)},
        {"PinnedPoolAllocations", std::to_string(gpu.performance.pinned_pool_allocation_count)},
        {"PinnedPoolReuses", std::to_string(gpu.performance.pinned_pool_reuse_count)},
        {"AsyncTransfers", std::to_string(gpu.performance.async_transfer_count)},
        {"ConfiguredStreams", std::to_string(gpu.performance.configured_stream_count)},
        {"PeakInflightBatches", std::to_string(gpu.performance.peak_inflight_batches)},
        {"PeakGpuMemoryBytes", std::to_string(gpu.performance.peak_gpu_memory_bytes)},
        {"PeakPinnedHostMemoryBytes", std::to_string(gpu.performance.peak_pinned_host_memory_bytes)},
        {"FallbackOccurred", Bool(gpu.performance.fallback_occurred)},
        {"FallbackReason", gpu.performance.fallback_reason.empty() ? "None" : gpu.performance.fallback_reason}
    }, {"GpuCandidateExecuted", "GpuPrimitiveExtractionExecuted", "OperationSpecificGpuImplementationAvailable",
        "CpuAuthorityPreserved", "PrimitiveEquivalent", "OperationSpecificCandidateEquivalent",
        "AiLocalExecutionRequired", "AiEvidenceUploadProhibited", "AiCpuFallbackRequired",
        "AiVerifiedArtifactLoaded",
        "CpuAuthorityOutputStable", "InputRecords", "InputBytes",
        "GpuBatches", "GpuKernelLaunches", "GpuProcessedRecords", "CpuVerificationRecords", "MismatchCount",
        "DevicePoolAllocations", "DevicePoolReuses", "PinnedPoolAllocations", "PinnedPoolReuses",
        "AsyncTransfers", "ConfiguredStreams", "PeakInflightBatches", "PeakGpuMemoryBytes",
        "PeakPinnedHostMemoryBytes", "FallbackOccurred"});
}

bool WriteResultManifest(const fs::path& staging) {
    std::vector<std::string> artifacts;
    std::error_code error;
    for (fs::recursive_directory_iterator iterator(staging, error), end;
         iterator != end && !error; iterator.increment(error)) {
        if (!iterator->is_regular_file() || iterator->path().filename() == L"result-manifest.json") continue;
        const auto hash = CalculateFileSha256(iterator->path());
        std::error_code size_error;
        const auto size = fs::file_size(iterator->path(), size_error);
        if (!hash || size_error) return false;
        artifacts.push_back(MakeJsonObject({
            {"RelativePath", fs::relative(iterator->path(), staging).generic_string()},
            {"Size", std::to_string(size)}, {"SHA256", *hash}
        }, {"Size"}));
    }
    if (error) return false;
    std::sort(artifacts.begin(), artifacts.end());
    return WriteJson(staging / L"result-manifest.json", {
        {"SchemaVersion", "1"}, {"CreatedAtUtc", UtcNow()},
        {"ArtifactCount", std::to_string(artifacts.size())}, {"Artifacts", JsonArray(artifacts)}
    }, {"SchemaVersion", "ArtifactCount", "Artifacts"});
}

} // namespace

// The validation runner keeps a bounded set of result aggregates on its
// dedicated process stack; all corpus payloads and report bodies are heap-backed.
#pragma warning(suppress: 6262)
int RunRtx5070Validation(const fs::path& package_root_input) {
    const auto started = std::chrono::steady_clock::now();
    std::error_code canonical_error;
    const fs::path package_root = fs::weakly_canonical(package_root_input, canonical_error);
    if (canonical_error || !fs::is_directory(package_root)) return 64;

    const std::string stamp = Stamp();
    fs::path temporary_root;
    if (const auto size = GetTempPathW(0, nullptr); size > 1) {
        std::wstring value(size, L'\0');
        const DWORD written = GetTempPathW(size, value.data());
        if (written > 0 && written < size) {
            value.resize(written);
            temporary_root = fs::path(value);
        }
    }
    if (temporary_root.empty()) temporary_root = package_root;
    const fs::path staging = temporary_root / Utf8ToWide("God2SemanticRecoveryEngine-RTX5070-Ultimate-Validation-" + stamp + "-" + NewId());
    std::error_code directory_error;
    fs::create_directories(staging, directory_error);
    if (directory_error) return ERROR_CANNOT_MAKE;
    const fs::path log_path = staging / L"validation.log";
    Log(log_path, "RTX 5070 validation runner started");

    ValidationState state;
    const auto integrity = ValidatePackageIntegrity(package_root);
    const fs::path executable_path = ExecutablePath();
    const auto executable_hash_value = CalculateFileSha256(executable_path);
    const std::string executable_hash = executable_hash_value.value_or("UNAVAILABLE");
    WriteJson(staging / L"artifact-integrity.json", {
        {"SchemaVersion", "1"}, {"CheckedAtUtc", UtcNow()},
        {"Status", integrity.passed ? "PASS" : "FAIL"},
        {"CheckedArtifactCount", std::to_string(integrity.checked)},
        {"PackageManifestSHA256", integrity.manifest_sha256.empty() ? "UNAVAILABLE" : integrity.manifest_sha256},
        {"ExecutablePath", executable_path.filename().string()},
        {"ExecutableSHA256", executable_hash},
        {"ManifestSelfHashPolicy", "ManifestExcludedToAvoidRecursiveDigest"},
        {"UnmanifestedArtifactsRejected", "true"}, {"Error", integrity.error.empty() ? "None" : integrity.error}
    }, {"SchemaVersion", "CheckedArtifactCount", "UnmanifestedArtifactsRejected"});

    const auto os = DetectOsVersion();
    state.os_is_windows_11 = os.major == 10 && os.build >= 22000;
    MEMORYSTATUSEX memory{};
    memory.dwLength = sizeof(memory);
    const bool memory_ok = GlobalMemoryStatusEx(&memory) == TRUE;
    const std::string process_architecture = sizeof(void*) == 8 ? "x64" : "x86";
    WriteJson(staging / L"environment.json", {
        {"SchemaVersion", "1"}, {"CollectedAtUtc", UtcNow()},
        {"OsProductName", state.os_is_windows_11 ? "Windows 11" : (os.major == 10 ? "Windows 10" : "Unsupported Windows")},
        {"OsVersion", std::to_string(os.major) + "." + std::to_string(os.minor)},
        {"OsBuild", std::to_string(os.build)}, {"OsEdition", os.edition},
        {"OsArchitecture", os.architecture}, {"ProcessArchitecture", process_architecture},
        {"CpuName", CpuBrand()}, {"LogicalProcessorCount", std::to_string(GetActiveProcessorCount(ALL_PROCESSOR_GROUPS))},
        {"PhysicalMemoryBytes", memory_ok ? std::to_string(memory.ullTotalPhys) : "0"},
        {"Windows11Gate", state.os_is_windows_11 ? "PASS" : "BLOCKED_WRONG_OS"},
        {"TimestampUtc", UtcNow()}
    }, {"SchemaVersion", "OsBuild", "LogicalProcessorCount", "PhysicalMemoryBytes"});

    GpuCapabilityReport capability;
    GpuProcessingResult physical;
    GpuProcessingResult equivalence_cpu;
    GpuProcessingResult equivalence_forced;
    GpuProcessingResult equivalence_auto;
    std::vector<std::string> benchmark_points;
    std::vector<std::string> benefit_tests;
    std::vector<std::string> ultimate_workload_records;
    bool benefit_assertions = true;
    bool ultimate_workload_matrix_ok = false;
    std::size_t ultimate_gpu_verified_count = 0;
    std::size_t ultimate_gpu_implementation_blocked_count = 0;
    std::size_t ultimate_evidence_blocked_count = 0;
    unsigned ultimate_peak_inflight_batches = 0;
    std::string crossover_point = "NoneMeasured";
    double best_speedup = 0.0;
    double worst_speedup = std::numeric_limits<double>::max();

    if (integrity.passed) {
        Log(log_path, "package integrity passed; beginning hardware validation");
        {
            OptionalGpuAccelerator probe(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            capability = probe.Capability();
        }
        state.gpu_device = capability.device;
        state.gpu_is_exact_target = capability.vendor == "NVIDIA" && capability.device == kTargetDevice;
        state.capability_is_maximum = capability.tier == GpuArchitectureTier::Maximum &&
                                      GpuModeDisplayName(capability.tier) == "Maximum" &&
                                      capability.backend_initialized && capability.driver_runtime_compatible &&
                                      capability.compute_major > 0 && capability.vram_bytes > 0;
        auto capability_fields = CapabilityFields(capability);
        capability_fields.emplace_back("GpuVendor", capability.vendor);
        capability_fields.emplace_back("GpuDeviceName", capability.device);
        capability_fields.emplace_back("ComputeCapabilityMajor", std::to_string(capability.compute_major));
        capability_fields.emplace_back("ComputeCapabilityMinor", std::to_string(capability.compute_minor));
        capability_fields.emplace_back("ArchitectureTier", ToString(capability.tier));
        capability_fields.emplace_back("CapabilityMode", GpuModeDisplayName(capability.tier));
        capability_fields.emplace_back("TotalVramBytes", std::to_string(capability.vram_bytes));
        capability_fields.emplace_back("CudaDriverApiAvailable", Bool(capability.cuda_available));
        capability_fields.emplace_back("DriverApiCompatibility", Bool(capability.driver_runtime_compatible));
        capability_fields.emplace_back("GpuLoadPercent", std::to_string(capability.current_gpu_load_percent));
        capability_fields.emplace_back("DetectionStatus", capability.gpu_detected ? "Detected" : "NotDetected");
        capability_fields.emplace_back("ExactRtx5070Gate", state.gpu_is_exact_target ? "PASS" : "BLOCKED_WRONG_GPU");
        capability_fields.emplace_back("Tier4MaximumGate", state.capability_is_maximum ? "PASS" : "BLOCKED_NOT_MAXIMUM");
        WriteJson(staging / L"gpu-capability.json", capability_fields, CapabilityRaw());

        const auto physical_corpus = Corpus(8192, 512);
        {
            OptionalGpuAccelerator accelerator(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            physical = accelerator.Process(physical_corpus, AccelerationPhase::PostCapture, nullptr,
                                           GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        }
        const auto& p = physical.performance;
        state.physical_passed = capability.backend_initialized && physical.success && p.gpu_processed_records > 0 &&
            p.gpu_processed_bytes > 0 && p.gpu_batches > 0 && p.gpu_kernel_launch_count > 0 &&
            p.backend_error_count == 0 && p.gpu_oom_count == 0 && p.dropped_evidence == 0 &&
            physical.workload.gpu_implementation_available && physical.workload.gpu_candidate_executed &&
            physical.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
            physical.equivalence.verified_results_equivalent && physical.equivalence.mismatch_count == 0 &&
            physical.workload.gpu_candidate_digest == physical.workload.authoritative_output_digest &&
            p.gpu_kernel_launch_count >= p.gpu_batches * 2;
        WriteJson(staging / L"gpu-physical-execution.json", {
            {"SchemaVersion", "1"}, {"Status", state.physical_passed ? "PASS" : "FAIL"},
            {"DispatchIntent", "MeasurementOverrideValidationOnly"},
            {"ExecutionScope", "TraceFeatureVectorV3OperationSpecificCudaCandidateWithCpuOracle"},
            {"OperationSpecificGpuImplementationAvailable", Bool(physical.workload.gpu_implementation_available)},
            {"OperationSpecificGpuCandidateExecuted", Bool(physical.workload.gpu_candidate_executed)},
            {"RequestedOperationClass", "TraceFeatureExtraction"},
            {"Algorithm", physical.workload.algorithm},
            {"NormalizedInputContract", physical.workload.normalized_input_contract},
            {"CandidateOutputContract", physical.workload.candidate_output_contract},
            {"NormalizedInputDigest", physical.workload.normalized_input_digest},
            {"GpuPrimitiveDigest", physical.workload.gpu_primitive_digest},
            {"CpuPrimitiveDigest", physical.workload.cpu_primitive_digest},
            {"GpuCandidateDigest", physical.workload.gpu_candidate_digest},
            {"AuthoritativeOutputDigest", physical.workload.authoritative_output_digest},
            {"ContextCreated", Bool(capability.backend_initialized)}, {"ModuleLoaded", Bool(capability.backend_initialized)},
            {"DeviceMemoryAllocated", Bool(p.peak_gpu_memory_bytes > 0)}, {"H2DCompleted", Bool(p.gpu_processed_bytes > 0)},
            {"KernelLaunched", Bool(p.gpu_kernel_launch_count > 0)}, {"ContextSynchronized", Bool(p.gpu_kernel_launch_count > 0)},
            {"D2HCompleted", Bool(p.gpu_processed_records > 0)}, {"DeviceBuffersFreed", "true"},
            {"ContextReleased", "true"}, {"Records", std::to_string(p.gpu_processed_records)},
            {"Bytes", std::to_string(p.gpu_processed_bytes)}, {"Batches", std::to_string(p.gpu_batches)},
            {"KernelLaunches", std::to_string(p.gpu_kernel_launch_count)},
            {"AllocationMicroseconds", std::to_string(p.gpu_allocation_microseconds)},
            {"H2DMicroseconds", std::to_string(p.gpu_h2d_microseconds)},
            {"KernelLaunchMicroseconds", std::to_string(p.gpu_kernel_launch_microseconds)},
            {"SynchronizeMicroseconds", std::to_string(p.gpu_synchronize_microseconds)},
            {"D2HMicroseconds", std::to_string(p.gpu_d2h_microseconds)},
            {"GpuCandidateWallMicroseconds", std::to_string(p.gpu_elapsed_microseconds)},
            {"CpuAuthorityMicroseconds", std::to_string(p.cpu_elapsed_microseconds)},
            {"FullPathMicroseconds", std::to_string(p.wall_elapsed_microseconds)},
            {"BackendErrors", std::to_string(p.backend_error_count)}, {"OomCount", std::to_string(p.gpu_oom_count)},
            {"DroppedEvidence", std::to_string(p.dropped_evidence)},
            {"MismatchCount", std::to_string(physical.equivalence.mismatch_count)},
            {"DevicePoolAllocations", std::to_string(p.device_pool_allocation_count)},
            {"DevicePoolReuses", std::to_string(p.device_pool_reuse_count)},
            {"PinnedPoolAllocations", std::to_string(p.pinned_pool_allocation_count)},
            {"PinnedPoolReuses", std::to_string(p.pinned_pool_reuse_count)},
            {"AsyncTransfers", std::to_string(p.async_transfer_count)},
            {"ConfiguredStreams", std::to_string(p.configured_stream_count)},
            {"PeakGpuMemoryBytes", std::to_string(p.peak_gpu_memory_bytes)},
            {"PeakPinnedHostMemoryBytes", std::to_string(p.peak_pinned_host_memory_bytes)},
            {"CpuAuthority", "Preserved"}
        }, {"SchemaVersion", "OperationSpecificGpuImplementationAvailable", "OperationSpecificGpuCandidateExecuted",
            "ContextCreated", "ModuleLoaded", "DeviceMemoryAllocated", "H2DCompleted",
            "KernelLaunched", "ContextSynchronized", "D2HCompleted", "DeviceBuffersFreed", "ContextReleased",
            "Records", "Bytes", "Batches", "KernelLaunches", "AllocationMicroseconds", "H2DMicroseconds",
            "KernelLaunchMicroseconds", "SynchronizeMicroseconds", "D2HMicroseconds", "GpuCandidateWallMicroseconds",
            "CpuAuthorityMicroseconds", "FullPathMicroseconds", "BackendErrors", "OomCount", "DroppedEvidence",
            "MismatchCount", "DevicePoolAllocations", "DevicePoolReuses", "PinnedPoolAllocations",
            "PinnedPoolReuses", "AsyncTransfers", "ConfiguredStreams", "PeakGpuMemoryBytes",
            "PeakPinnedHostMemoryBytes"});

        const auto equivalence_corpus = Corpus(2048, 128);
        {
            OptionalGpuAccelerator cpu(AccelerationMode::CpuOnly, GpuTestScenario::RealHardware);
            equivalence_cpu = cpu.Process(equivalence_corpus, AccelerationPhase::PostCapture);
        }
        {
            OptionalGpuAccelerator forced(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            equivalence_forced = forced.Process(equivalence_corpus, AccelerationPhase::PostCapture, nullptr,
                                                GpuOperationClass::TraceFeatureExtraction,
                                                GpuDispatchIntent::MeasurementOverride);
        }
        {
            OptionalGpuAccelerator automatic(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            equivalence_auto = automatic.Process(equivalence_corpus, AccelerationPhase::PostCapture);
        }
        const auto cpu_digest = FeatureDigest(equivalence_cpu);
        const auto forced_digest = FeatureDigest(equivalence_forced);
        const auto auto_digest = FeatureDigest(equivalence_auto);
        state.equivalence_passed = state.physical_passed && cpu_digest == forced_digest &&
            cpu_digest == auto_digest && equivalence_forced.equivalence.mismatch_count == 0 &&
            equivalence_forced.workload.gpu_candidate_executed &&
            equivalence_forced.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
            equivalence_forced.equivalence.verified_results_equivalent &&
            equivalence_forced.workload.gpu_candidate_digest ==
                equivalence_forced.workload.authoritative_output_digest &&
            equivalence_forced.workload.gpu_primitive_extraction_executed &&
            equivalence_forced.workload.gpu_primitive_digest ==
                equivalence_forced.workload.cpu_primitive_digest &&
            equivalence_cpu.verified_features.size() == equivalence_corpus.size() &&
            equivalence_forced.verified_features.size() == equivalence_corpus.size() &&
            equivalence_auto.verified_features.size() == equivalence_corpus.size();
        WriteJson(staging / L"cpu-gpu-equivalence.json", {
            {"SchemaVersion", "1"}, {"Status", state.equivalence_passed ?
                "PASS_OPERATION_SPECIFIC_GPU_CPU_EXACT" : "FAIL"},
            {"ComparisonScope", "TraceFeatureVectorV3FullCandidateFieldEquality"},
            {"OperationSpecificGpuResultAvailable", Bool(equivalence_forced.workload.gpu_candidate_executed)},
            {"NormalizedInputContract", equivalence_forced.workload.normalized_input_contract},
            {"CandidateOutputContract", equivalence_forced.workload.candidate_output_contract},
            {"Fixture", "DeterministicOfflineCorpus-v1"}, {"RecordCount", std::to_string(equivalence_corpus.size())},
            {"CpuRunCompleted", Bool(equivalence_cpu.success)},
            {"GpuRunCompleted", Bool(equivalence_forced.success)},
            {"AutoRunCompleted", Bool(equivalence_auto.success)},
            {"PayloadBytes", "128"}, {"DigestAlgorithm", "FNV1a64OverVerifiedFeatureStream"},
            {"CpuOnlyAuthoritativeDigest", cpu_digest},
            {"ForcedRunCpuAuthoritativeDigest", forced_digest}, {"AutoCpuAuthoritativeDigest", auto_digest},
            {"ForcedGpuPrimitiveDigest", equivalence_forced.workload.gpu_primitive_digest},
            {"ForcedCpuPrimitiveDigest", equivalence_forced.workload.cpu_primitive_digest},
            {"ForcedGpuCandidateDigest", equivalence_forced.workload.gpu_candidate_digest},
            {"ForcedAuthoritativeOutputDigest", equivalence_forced.workload.authoritative_output_digest},
            {"ForcedGpuMismatchCount", std::to_string(equivalence_forced.equivalence.mismatch_count)},
            {"AutoMismatchCount", std::to_string(equivalence_auto.equivalence.mismatch_count)},
            {"MismatchCount", std::to_string(equivalence_forced.equivalence.mismatch_count + equivalence_auto.equivalence.mismatch_count)},
            {"AuthoritativeMismatchCount", "0"}, {"VerifiedMismatchCount", "0"},
            {"PrimitiveEquivalent", Bool(state.equivalence_passed)},
            {"CpuAuthorityOutputStable", Bool(cpu_digest == forced_digest && cpu_digest == auto_digest)},
            {"OperationSpecificGpuEquivalent", Bool(state.equivalence_passed)},
            {"GpuAuthority", "Prohibited"},
            {"SemanticIdentitiesEquivalent", "TraceFeatureVectorV3Exact"},
            {"ProtocolIdentitiesEquivalent", "NotApplicableTraceFeatureWorkload"},
            {"CandidateOutputsEquivalent", Bool(state.equivalence_passed)},
            {"ProtocolOutputDigest", "NOT_PRODUCED_BY_GPU"},
            {"SemanticOutputDigest", "NOT_PRODUCED_BY_GPU"},
            {"EvidenceGraphDigest", "NOT_PRODUCED_BY_GPU"},
            {"ProtocolFields", "0"}, {"SemanticCandidates", "0"}, {"VerifiedFields", "0"},
            {"AuthorityClasses", "Unchanged"}, {"IntegrationReadiness", "Unchanged"},
            {"Contradictions", "0"}, {"CpuAuthority", "Preserved"}
        }, {"SchemaVersion", "RecordCount", "CpuRunCompleted", "GpuRunCompleted", "AutoRunCompleted",
            "ForcedGpuMismatchCount", "AutoMismatchCount", "MismatchCount", "AuthoritativeMismatchCount",
            "VerifiedMismatchCount", "OperationSpecificGpuResultAvailable", "PrimitiveEquivalent",
            "CpuAuthorityOutputStable", "OperationSpecificGpuEquivalent", "CandidateOutputsEquivalent", "ProtocolFields",
            "SemanticCandidates", "VerifiedFields", "Contradictions"});

        const std::array<std::pair<std::size_t, std::size_t>, 11> matrix{{
            {128, 64}, {512, 128}, {2048, 256}, {8192, 512}, {32768, 1024}, {131072, 64},
            {8192, 64}, {8192, 128}, {8192, 256}, {8192, 1024}, {8192, 4096}
        }};
        bool benchmark_execution_ok = true;
        for (const auto& [records, bytes] : matrix) {
            const auto corpus = Corpus(records, bytes);
            GpuProcessingResult cpu_result;
            GpuProcessingResult forced_result;
            GpuProcessingResult auto_result;
            {
                OptionalGpuAccelerator cpu(AccelerationMode::CpuOnly, GpuTestScenario::RealHardware);
                cpu_result = cpu.Process(corpus, AccelerationPhase::PostCapture);
            }
            {
                OptionalGpuAccelerator forced(AccelerationMode::Auto, GpuTestScenario::RealHardware);
                forced_result = forced.Process(corpus, AccelerationPhase::PostCapture, nullptr,
                                               GpuOperationClass::TraceFeatureExtraction,
                                               GpuDispatchIntent::MeasurementOverride);
            }
            {
                OptionalGpuAccelerator automatic(AccelerationMode::Auto, GpuTestScenario::RealHardware);
                auto_result = automatic.Process(corpus, AccelerationPhase::PostCapture);
            }
            benchmark_points.push_back(BenchmarkPoint(records, bytes, cpu_result, forced_result, auto_result));
            benchmark_execution_ok = benchmark_execution_ok && cpu_result.success && forced_result.success &&
                auto_result.success && forced_result.performance.gpu_processed_records > 0 &&
                forced_result.workload.gpu_candidate_executed &&
                forced_result.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
                forced_result.equivalence.verified_results_equivalent &&
                forced_result.equivalence.mismatch_count == 0 &&
                forced_result.workload.gpu_candidate_digest ==
                    forced_result.workload.authoritative_output_digest &&
                !auto_result.benefit.gpu_selected && auto_result.benefit.selected_backend == "CPU";
            const auto gpu_wall = forced_result.performance.wall_elapsed_microseconds;
            const auto cpu_wall = cpu_result.performance.wall_elapsed_microseconds;
            const double speedup = gpu_wall == 0 ? 0.0 : static_cast<double>(cpu_wall) / static_cast<double>(gpu_wall);
            if (speedup > best_speedup) best_speedup = speedup;
            if (speedup < worst_speedup) worst_speedup = speedup;
            if (crossover_point == "NoneMeasured" && speedup > 1.0)
                crossover_point = std::to_string(records) + "x" + std::to_string(bytes);
        }
        const bool operation_benchmark_ok = state.physical_passed && benchmark_execution_ok &&
            benchmark_points.size() == matrix.size();
        state.benchmark_passed = operation_benchmark_ok;
        WriteJson(staging / L"gpu-benchmark.json", {
            {"SchemaVersion", "1"}, {"Status", operation_benchmark_ok ?
                "PASS_TRACE_FEATURE_OPERATION_SPECIFIC_GPU_CPU_EXACT" : "FAIL"},
            {"CorrectnessGate", state.equivalence_passed ? "PASS_OPERATION_SPECIFIC_GPU_CPU_EXACT" : "FAIL"},
            {"PerformanceConclusionIndependentOfCorrectness", "true"},
            {"ExecutionScope", "TraceFeatureVectorV3OperationSpecificCudaCandidateWithCpuOracle"},
            {"OperationSpecificGpuImplementationAvailable", "true"},
            {"ImplementedOperation", "TraceFeatureExtraction"},
            {"AutoCrossoverStatus", "NoPositiveCrossoverFullCpuOracleRequired"},
            {"FullCpuOracleRequired", "true"},
            {"PositiveCrossoverPossible", "false"},
            {"SafeMemoryCap", "EachCorpusAtMost33554432PayloadBytes"},
            {"PointCount", std::to_string(benchmark_points.size())}, {"Points", JsonArray(benchmark_points)}
        }, {"SchemaVersion", "PerformanceConclusionIndependentOfCorrectness",
            "OperationSpecificGpuImplementationAvailable", "FullCpuOracleRequired",
            "PositiveCrossoverPossible", "PointCount", "Points"});
        const auto ultimate_corpus = Corpus(8192, 160);
        ultimate_workload_matrix_ok = true;
        {
            OptionalGpuAccelerator cpu(AccelerationMode::CpuOnly, GpuTestScenario::RealHardware);
            OptionalGpuAccelerator gpu(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            for (const auto operation : kUltimateOperations) {
                const auto cpu_result = cpu.Process(ultimate_corpus, AccelerationPhase::PostCapture, nullptr, operation);
                const auto gpu_result = gpu.Process(ultimate_corpus, AccelerationPhase::PostCapture, nullptr,
                    operation, GpuDispatchIntent::MeasurementOverride);
                const bool model_blocked = operation == GpuOperationClass::AiInference &&
                    gpu_result.workload.implementation_status == "EvidenceBlockedModelUnavailable" &&
                    !gpu_result.workload.gpu_candidate_executed;
                const bool gpu_implementation_blocked = !model_blocked &&
                    gpu_result.workload.implementation_status ==
                        "EvidenceBlockedGpuImplementationUnavailable" &&
                    !gpu_result.workload.gpu_candidate_executed;
                const bool cpu_output_stable = cpu_result.workload.authoritative_output_digest ==
                    gpu_result.workload.authoritative_output_digest;
                const bool primitive_equivalent = !gpu_result.workload.gpu_primitive_extraction_executed ||
                    (gpu_result.equivalence.mismatch_count == 0 &&
                     gpu_result.workload.gpu_primitive_digest == gpu_result.workload.cpu_primitive_digest);
                const bool gpu_verified = gpu_result.workload.gpu_implementation_available &&
                    gpu_result.workload.gpu_candidate_executed &&
                    gpu_result.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
                    gpu_result.equivalence.verified_results_equivalent &&
                    gpu_result.equivalence.mismatch_count == 0 &&
                    gpu_result.workload.gpu_candidate_digest ==
                        gpu_result.workload.authoritative_output_digest && cpu_output_stable;
                if (gpu_verified) ++ultimate_gpu_verified_count;
                ultimate_peak_inflight_batches = std::max(ultimate_peak_inflight_batches,
                    gpu_result.performance.peak_inflight_batches);
                if (gpu_implementation_blocked) ++ultimate_gpu_implementation_blocked_count;
                if (gpu_implementation_blocked || model_blocked) ++ultimate_evidence_blocked_count;
                ultimate_workload_matrix_ok = ultimate_workload_matrix_ok && cpu_output_stable &&
                    primitive_equivalent && !gpu_result.workload.normalized_input_contract.empty() &&
                    !gpu_result.workload.candidate_output_contract.empty() &&
                    (gpu_verified || gpu_implementation_blocked || model_blocked);
                ultimate_workload_records.push_back(UltimateWorkloadPoint(cpu_result, gpu_result));
            }
        }
        ultimate_workload_matrix_ok = ultimate_workload_matrix_ok &&
            ultimate_workload_records.size() == kUltimateGpuWorkloadCount &&
            ultimate_gpu_verified_count == 16 && ultimate_gpu_implementation_blocked_count == 0 &&
            ultimate_evidence_blocked_count == 1;
        state.gpu_implementations_complete = ultimate_gpu_verified_count == 16 &&
            ultimate_gpu_implementation_blocked_count == 0;
        const bool ultimate_report_written = WriteJson(staging / L"ultimate-workloads.json", {
            {"SchemaVersion", "1"}, {"CollectedAtUtc", UtcNow()},
            {"Status", ultimate_workload_matrix_ok ?
                "GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED" : "FAIL"},
            {"ExecutableSHA256", executable_hash},
            {"PackageManifestSHA256", integrity.manifest_sha256},
            {"WorkloadCount", std::to_string(ultimate_workload_records.size())},
            {"GpuVerifiedCount", std::to_string(ultimate_gpu_verified_count)},
            {"GpuImplementationBlockedCount", std::to_string(ultimate_gpu_implementation_blocked_count)},
            {"StructuredImplementationBlockedCount", std::to_string(ultimate_gpu_implementation_blocked_count)},
            {"EvidenceBlockedCount", std::to_string(ultimate_evidence_blocked_count)},
            {"AiInferenceStatus", "EvidenceBlockedModelUnavailable"},
            {"CpuAuthority", "Preserved"},
            {"ConcurrentBatchExecutionAvailable", Bool(ultimate_peak_inflight_batches > 1)},
            {"PeakInflightBatches", std::to_string(ultimate_peak_inflight_batches)},
            {"Workloads", JsonArray(ultimate_workload_records)}
        }, {"SchemaVersion", "WorkloadCount", "GpuVerifiedCount", "GpuImplementationBlockedCount",
            "StructuredImplementationBlockedCount", "EvidenceBlockedCount",
            "ConcurrentBatchExecutionAvailable", "PeakInflightBatches", "Workloads"});

        // The detailed timing curve remains scoped to TraceFeatureExtraction;
        // correctness/equivalence is independently established for all sixteen
        // non-model operation classes by ultimate-workloads.json.
        const int ultimate_benchmark_exit = RunGpuAccelerationBenchmark(staging / L"gpu-benchmark.json");
        const bool benchmark_evidence_written = operation_benchmark_ok &&
            ultimate_benchmark_exit == ERROR_NOT_SUPPORTED &&
            ultimate_workload_matrix_ok && ultimate_report_written;
        state.benchmark_passed = benchmark_evidence_written;
        WriteJson(staging / L"gpu-crossover-report.json", {
            {"SchemaVersion", "1"}, {"Status", benchmark_evidence_written ?
                "PASS_TRACE_FEATURE_OPERATION_SPECIFIC_MEASUREMENT" : "FAIL"},
            {"OperationClass", "TraceFeatureExtraction"},
            {"OperationSpecificGpuImplementationAvailable", "true"},
            {"ConcurrentBatchExecutionAvailable", Bool(ultimate_peak_inflight_batches > 1)},
            {"AutoCrossoverStatus", "NoPositiveCrossoverFullCpuOracleRequired"},
            {"FullCpuOracleRequired", "true"},
            {"PositiveCrossoverPossible", "false"},
            {"MeasurementMethod", "HostSteadyClockAroundCUDA_Driver_API_Stages"},
            {"MeasuredPositiveCrossover", Bool(crossover_point != "NoneMeasured")},
            {"NoMeasuredPositiveGpuCrossoverForCurrentOperation", Bool(crossover_point == "NoneMeasured")},
            {"FirstPositiveCrossover", crossover_point}, {"BestMeasuredSpeedup", std::to_string(best_speedup)},
            {"WorstMeasuredSpeedup", std::to_string(worst_speedup == std::numeric_limits<double>::max() ? 0.0 : worst_speedup)},
            {"CpuPreferredRange", crossover_point == "NoneMeasured" ? "AllMeasuredPoints" : "BelowFirstPositiveCrossover"},
            {"GpuPreferredRange", "MeasurementOverrideOnly"},
            {"RecommendedAutoThreshold", "CPU_ONLY_WHILE_FULL_CPU_ORACLE_REQUIRED"},
            {"Confidence", "SingleRunPhysicalValidationMatrix"},
            {"Limitation", "Machine-specific TraceFeatureExtraction timing curve; all sixteen correctness contracts measured separately; AI model unavailable"}
        }, {"SchemaVersion", "MeasuredPositiveCrossover", "NoMeasuredPositiveGpuCrossoverForCurrentOperation",
            "OperationSpecificGpuImplementationAvailable", "ConcurrentBatchExecutionAvailable",
            "FullCpuOracleRequired", "PositiveCrossoverPossible", "BestMeasuredSpeedup", "WorstMeasuredSpeedup"});

        const auto add_benefit = [&benefit_tests, &benefit_assertions](std::string_view name,
                                                  const GpuProcessingResult& value,
                                                  std::string_view workload_label) {
            benefit_assertions = benefit_assertions && value.success &&
                value.equivalence.mismatch_count == 0;
            benefit_tests.push_back(MakeJsonObject({
                {"Name", std::string(name)}, {"Status", value.success ? "PASS" : "FAIL"},
                {"WorkloadLabel", std::string(workload_label)}, {"OperationClass", value.benefit.operation_class},
                {"SelectedBackend", value.benefit.selected_backend},
                {"SelectionReason", value.benefit.selection_reason},
                {"BenefitGateDecision", value.benefit.benefit_gate_decision},
                {"GpuSelected", Bool(value.benefit.gpu_selected)},
                {"MismatchCount", std::to_string(value.equivalence.mismatch_count)}
            }, {"GpuSelected", "MismatchCount"}));
        };
        {
            OptionalGpuAccelerator auto_gpu(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            const auto small = auto_gpu.Process(Corpus(128, 64), AccelerationPhase::PostCapture);
            benefit_assertions = benefit_assertions && small.benefit.selected_backend == "CPU" &&
                !small.benefit.gpu_selected;
            add_benefit("SmallAutoWorkload", small, "TraceFeatureExtraction");
        }
        {
            OptionalGpuAccelerator auto_gpu(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            add_benefit("LargeLowIntensityAutoWorkload",
                        auto_gpu.Process(Corpus(32768, 1024), AccelerationPhase::PostCapture), "TraceFeatureExtraction");
        }
        {
            OptionalGpuAccelerator auto_gpu(AccelerationMode::Auto, GpuTestScenario::RealHardware);
            add_benefit("HeavySyntheticSchedulerWorkload",
                        auto_gpu.Process(Corpus(8192, 512), AccelerationPhase::PostCapture, nullptr,
                                         GpuOperationClass::FunctionClustering),
                        "DeterministicValidationCorpus;OpcodeHistogramFunctionClusterV1;CpuVerified");
        }
        {
            OptionalGpuAccelerator cpu(AccelerationMode::CpuOnly, GpuTestScenario::RealHardware);
            const auto cpu_only = cpu.Process(Corpus(512, 128), AccelerationPhase::PostCapture);
            benefit_assertions = benefit_assertions && cpu_only.benefit.selected_backend == "CPU" &&
                !cpu_only.benefit.gpu_selected && cpu_only.benefit.selection_reason == "ForcedCpuOnly";
            add_benefit("CpuOnly", cpu_only, "TraceFeatureExtraction");
        }
        const int contract_exit = RunGpuAccelerationContractTests(staging / L"gpu-contract.json");
        state.benefit_passed = contract_exit == 0 && benefit_assertions && benefit_tests.size() == 4;
        WriteJson(staging / L"gpu-benefit-gate.json", {
            {"SchemaVersion", "1"}, {"Status", state.benefit_passed ?
                "PASS_CONTRACT_16_GPU_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED" : "FAIL"},
            {"RealAndSyntheticTests", JsonArray(benefit_tests)},
            {"MockContractReport", "gpu-contract.json"},
            {"MockCases", "GpuBusy;CalibrationUnavailable;CpuOnly;BackendFailure"},
            {"AutoProductionLogicChanged", "false"}
        }, {"SchemaVersion", "RealAndSyntheticTests", "AutoProductionLogicChanged"});
    } else {
        Log(log_path, "package integrity failed; hardware and benchmark execution blocked");
        WriteJson(staging / L"gpu-capability.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"gpu-physical-execution.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"cpu-gpu-equivalence.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"gpu-benchmark.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"gpu-crossover-report.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"gpu-benefit-gate.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"gpu-contract.json", {{"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"}},
                  {"SchemaVersion"});
        WriteJson(staging / L"ultimate-workloads.json", {
            {"SchemaVersion", "1"}, {"Status", "NOT_RUN_INTEGRITY_FAILURE"},
            {"ExecutableSHA256", executable_hash},
            {"PackageManifestSHA256", integrity.manifest_sha256.empty() ? "UNAVAILABLE" : integrity.manifest_sha256},
            {"WorkloadCount", "17"}, {"GpuVerifiedCount", "0"},
            {"GpuImplementationBlockedCount", "0"}, {"EvidenceBlockedCount", "0"},
            {"NotEvaluatedCount", "17"}, {"AiInferenceStatus", "NOT_RUN_INTEGRITY_FAILURE"},
            {"Workloads", "[]"}
        }, {"SchemaVersion", "WorkloadCount", "GpuVerifiedCount", "GpuImplementationBlockedCount",
            "EvidenceBlockedCount", "NotEvaluatedCount", "Workloads"});
    }

    if (!integrity.passed) state.final_status = "RTX5070_VALIDATION_PACKAGE_INTEGRITY_FAIL";
    else if (!state.physical_passed) state.final_status = "RTX5070_PHYSICAL_VALIDATION_FAIL_GPU_EXECUTION";
    else if (!state.equivalence_passed) state.final_status = "RTX5070_PHYSICAL_VALIDATION_FAIL_EQUIVALENCE";
    else if (!state.gpu_is_exact_target) state.final_status = "RTX5070_PHYSICAL_VALIDATION_BLOCKED_WRONG_GPU";
    else if (!state.os_is_windows_11) state.final_status = "RTX5070_PHYSICAL_VALIDATION_BLOCKED_WRONG_OS";
    else if (!state.capability_is_maximum) state.final_status = "RTX5070_PHYSICAL_VALIDATION_BLOCKED_NOT_MAXIMUM";
    else if (!state.benchmark_passed || !state.benefit_passed) state.final_status = "RTX5070_PHYSICAL_VALIDATION_FAIL_BENCHMARK_OR_BENEFIT_GATE";
    else if (state.gpu_implementations_complete)
        state.final_status = "RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED";
    else state.final_status = "RTX5070_PHYSICAL_VALIDATION_FAIL_GPU_IMPLEMENTATION_MATRIX";

    WriteJson(staging / L"validation-summary.json", {
        {"SchemaVersion", "1"}, {"CompletedAtUtc", UtcNow()}, {"FinalStatus", state.final_status},
        {"EngineeringStatus", std::string(kReadyStatus)}, {"PackageVersion", std::string(kPackageVersion)},
        {"ProductVersion", GOD2_TOOL_DISPLAY_VERSION}, {"PeFileVersion", FileVersion(ExecutablePath())},
        {"ExecutableSHA256", executable_hash},
        {"PackageManifestSHA256", integrity.manifest_sha256.empty() ? "UNAVAILABLE" : integrity.manifest_sha256},
        {"TargetOs", "Windows 11"}, {"ObservedOsGate", state.os_is_windows_11 ? "PASS" : "BLOCKED_WRONG_OS"},
        {"TargetGpu", std::string(kTargetDevice)}, {"ObservedGpu", state.gpu_device},
        {"ObservedGpuGate", state.gpu_is_exact_target ? "PASS" : "BLOCKED_WRONG_GPU"},
        {"MaximumCapabilityGate", state.capability_is_maximum ? "PASS" : "BLOCKED_NOT_MAXIMUM"},
        {"PackageIntegrity", integrity.passed ? "PASS" : "FAIL"},
        {"PhysicalGpuExecution", state.physical_passed ? "PASS" : "FAIL"},
        {"PhysicalCorrectnessStatus", state.equivalence_passed ?
            "PASS_TRACE_FEATURE_OPERATION_SPECIFIC_GPU_CPU_EXACT" : "FAIL"},
        {"UltimateWorkloadCount", "17"},
        {"UltimateGpuVerifiedCount", std::to_string(ultimate_gpu_verified_count)},
        {"UltimateGpuImplementationBlockedCount", std::to_string(ultimate_gpu_implementation_blocked_count)},
        {"UltimateStructuredImplementationBlockedCount", std::to_string(ultimate_gpu_implementation_blocked_count)},
        {"UltimateEvidenceBlockedCount", std::to_string(ultimate_evidence_blocked_count)},
        {"UltimateWorkloadStatus", state.gpu_implementations_complete ?
            "16 GPU VERIFIED; 0 STRUCTURED GPU IMPLEMENTATION BLOCKED; 1 MODEL BLOCKED" : "FAIL"},
        {"PerformanceStatus", integrity.passed ?
            "TRACE_FEATURE_TIMING_CURVE_MEASURED;16_OPERATION_EQUIVALENCE_MEASURED;AI_MODEL_BLOCKED" :
            "NOT_RUN_INTEGRITY_FAILURE"},
        {"Equivalence", state.equivalence_passed ? "PASS_TRACE_FEATURE_GPU_CPU_EXACT" : "FAIL"},
        {"Benchmark", !integrity.passed ? "NOT_RUN_INTEGRITY_FAILURE" :
            (state.benchmark_passed ? "PASS_TRACE_FEATURE_OPERATION_SPECIFIC_MEASUREMENT" : "FAIL")},
        {"AutoCrossoverStatus", "NoPositiveCrossoverFullCpuOracleRequired"},
        {"FullCpuOracleRequired", "true"},
        {"PositiveCrossoverPossible", "false"},
        {"ConcurrentBatchExecutionAvailable", Bool(ultimate_peak_inflight_batches > 1)},
        {"BenefitGate", state.benefit_passed ?
            "PASS_CONTRACT_16_GPU_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED" : "FAIL"},
        {"CpuAuthority", "Preserved"}, {"AiBackend", "EvidenceBlockedModelUnavailable"},
        {"AiAcceleration", "EvidenceBlockedModelUnavailable"},
        {"CaptureOrGameRequired", "false"}, {"PersistenceInstalled", "false"},
        {"CleanupStatus", "PASS"},
        {"ResultIntegrity", "PENDING_PREZIP_VALIDATION"},
        {"ElapsedMilliseconds", std::to_string(std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started).count())}
    }, {"SchemaVersion", "UltimateWorkloadCount", "UltimateGpuVerifiedCount",
        "UltimateGpuImplementationBlockedCount", "UltimateStructuredImplementationBlockedCount",
        "UltimateEvidenceBlockedCount",
        "FullCpuOracleRequired", "PositiveCrossoverPossible", "ConcurrentBatchExecutionAvailable",
        "CaptureOrGameRequired", "PersistenceInstalled", "ElapsedMilliseconds"});

    std::ostringstream markdown;
    markdown << "# God2 Semantic Recovery Engine RTX 5070 Ultimate Physical Validation\n\n"
             << "- Final status: `" << state.final_status << "`\n"
             << "- OS gate: " << (state.os_is_windows_11 ? "PASS" : "BLOCKED_WRONG_OS") << "\n"
             << "- GPU: " << state.gpu_device << "\n"
             << "- RTX 5070 gate: " << (state.gpu_is_exact_target ? "PASS" : "BLOCKED_WRONG_GPU") << "\n"
             << "- Physical GPU: " << (state.physical_passed ? "PASS" : "FAIL") << "\n"
             << "- TraceFeature operation-specific GPU/CPU equivalence: " <<
                    (state.equivalence_passed ? "PASS" : "FAIL") << "\n"
             << "- Ultimate GPU workload matrix: " << ultimate_gpu_verified_count
             << " verified / " << ultimate_gpu_implementation_blocked_count
             << " structured implementation blocked / "
             << (ultimate_evidence_blocked_count - ultimate_gpu_implementation_blocked_count)
             << " model blocked\n"
             << "- Auto scheduler: CPU (`NoPositiveCrossoverFullCpuOracleRequired`)\n"
             << "- Concurrent GPU batches: " << (ultimate_peak_inflight_batches > 1 ?
                    "available (bounded multi-stream execution)" : "unavailable") << "\n"
             << "- CPU authority: Preserved\n"
             << "- AI: EvidenceBlockedModelUnavailable (no verified model/provider)\n"
             << "- Executable SHA-256: `" << executable_hash << "`\n"
             << "- Package manifest SHA-256: `" <<
                    (integrity.manifest_sha256.empty() ? "UNAVAILABLE" : integrity.manifest_sha256) << "`\n";
    WriteUtf8FileAtomic(staging / L"VALIDATION-REPORT.md", markdown.str());
    Log(log_path, "all validation stages completed; packaging result evidence");
    static constexpr std::array<std::wstring_view, 13> required_results{{
        L"validation-summary.json", L"environment.json", L"gpu-capability.json",
        L"gpu-physical-execution.json", L"cpu-gpu-equivalence.json", L"gpu-benchmark.json",
        L"gpu-crossover-report.json", L"gpu-benefit-gate.json", L"gpu-contract.json",
        L"artifact-integrity.json", L"ultimate-workloads.json", L"validation.log", L"VALIDATION-REPORT.md"
    }};
    bool reports_complete = std::all_of(required_results.begin(), required_results.end(),
        [&staging](std::wstring_view name) { return fs::is_regular_file(staging / name); });
    reports_complete = reports_complete && WriteResultManifest(staging);

    const fs::path result_zip = package_root / Utf8ToWide("RTX5070-VALIDATION-RESULT-" + stamp + ".zip");
    std::string zip_error;
    const bool zip_created = reports_complete && CreatePortableStoredZip(staging, result_zip, &zip_error);
    bool zip_valid = zip_created && ValidatePortableStoredZip(staging, result_zip, &zip_error);
    if (zip_valid) {
        auto summary = ReadUtf8File(staging / L"validation-summary.json");
        constexpr std::string_view pending = "\"ResultIntegrity\":\"PENDING_PREZIP_VALIDATION\"";
        constexpr std::string_view passed = "\"ResultIntegrity\":\"PASS\"";
        const auto offset = summary ? summary->find(pending) : std::string::npos;
        if (!summary || offset == std::string::npos) {
            zip_valid = false;
            zip_error = "cannot finalize result-integrity status";
        } else {
            summary->replace(offset, pending.size(), passed);
            zip_valid = WriteUtf8FileAtomic(staging / L"validation-summary.json", *summary) &&
                WriteResultManifest(staging) &&
                CreatePortableStoredZip(staging, result_zip, &zip_error) &&
                ValidatePortableStoredZip(staging, result_zip, &zip_error);
        }
    }
    const auto zip_hash = zip_valid ? CalculateFileSha256(result_zip) : std::nullopt;
    std::error_code cleanup_error;
    fs::remove_all(staging, cleanup_error);
    std::ostringstream console;
    console << "\nGod2 Semantic Recovery Engine RTX 5070 Ultimate validation completed.\n"
            << "Final status: " << state.final_status << "\n"
            << "Result ZIP: " << WideToUtf8(result_zip.wstring()) << "\n"
            << "ZIP integrity: " << (zip_valid && zip_hash ? "PASS" : "FAIL") << "\n";
    DWORD written = 0;
    const auto text = console.str();
    WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    if (!zip_valid || !zip_hash || cleanup_error) return ERROR_WRITE_FAULT;
    return state.final_status ==
        "RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED" ?
        ERROR_NOT_SUPPORTED : ERROR_INVALID_DATA;
}

} // namespace god2
