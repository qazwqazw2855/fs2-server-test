#pragma once

#include "Core.h"

#include <atomic>
#include <cstdint>
#include <string>
#include <vector>

namespace god2 {

enum class AccelerationMode {
    Auto,
    CpuOnly
};

enum class GpuArchitectureTier {
    CpuFullFeature,
    LegacyCuda,
    RtxStandard,
    Full,
    HighPerformance,
    Maximum
};

enum class AccelerationPhase {
    Live,
    PostCapture
};

enum class GpuOperationClass {
    TraceFeatureExtraction = 0,
    CrossSessionCorrelation,
    ParserFieldPatternMatching,
    FunctionClustering,
    ObjectClustering,
    ClassLayoutScoring,
    RegistryScoring,
    HeapGraphSimilarity,
    ValueFlowAggregation,
    TaintGraphBatch,
    SemanticEdgeScoring,
    ApproximateNearestNeighbor,
    SequenceMining,
    FsmScoring,
    FormulaBatchEvaluation,
    ReplayStateComparison,
    AiInference,

    // Source-compatible names used by the v1.2 scheduler.  They intentionally
    // resolve to a named v1.3 workload; no extra pseudo-workload is created.
    BulkChecksum = TraceFeatureExtraction,
    PatternCorrelation = CrossSessionCorrelation,
    SimilarityScoring = ObjectClustering,
    FeatureExtraction = TraceFeatureExtraction,
    Clustering = FunctionClustering,
    GraphBatchScoring = HeapGraphSimilarity,
    SemanticCandidateScoring = SemanticEdgeScoring,
    FutureAiInference = AiInference
};

constexpr std::size_t kUltimateGpuWorkloadCount = 17;

// MeasurementOverride is used only by the physical benchmark harness so it can
// measure the real GPU curve even when Auto correctly prefers CPU. It is never
// exposed as a player setting and never changes the Auto/CPU Only UI contract.
enum class GpuDispatchIntent {
    Adaptive,
    MeasurementOverride
};

// Test scenarios are implemented inside the same dispatch boundary as the real
// backend.  Production always uses RealHardware; the other values let a machine
// without an NVIDIA adapter exercise failure, cleanup, and tier contracts.
enum class GpuTestScenario {
    RealHardware,
    NoGpu,
    NvidiaUnavailable,
    InitializationError,
    Oom,
    WorkerFailure,
    CandidateMismatch,
    Timeout,
    StopDuringWork,
    UnsupportedGpu,
    MockPascal,
    MockTuring,
    MockAmpere,
    MockAda,
    MockBlackwell,
    MockGpuBusy,
    BenefitModelUnavailable,
    StaleCalibration
};

struct GpuCapabilityReport {
    AccelerationMode requested_mode = AccelerationMode::Auto;
    bool gpu_detected = false;
    bool nvidia_device_available = false;
    bool cuda_available = false;
    bool backend_initialized = false;
    bool driver_runtime_compatible = false;
    bool tensor_capability = false;
    bool ai_backend_present = false;
    bool ai_acceleration_active = false;
    int compute_major = 0;
    int compute_minor = 0;
    std::uint64_t vram_bytes = 0;
    std::uint64_t free_vram_bytes = 0;
    unsigned current_gpu_load_percent = 0;
    int driver_version = 0;
    GpuArchitectureTier tier = GpuArchitectureTier::CpuFullFeature;
    std::string vendor = "None";
    std::string device = "None";
    std::string backend = "CPU deterministic pipeline";
    std::string architecture = "CPU";
    std::string driver_status = "NotAvailable";
    std::string runtime_status = "NotRequired";
    std::string available_precision_modes = "FP32";
    std::string fallback_reason;
};

struct GpuSchedulerPlan {
    std::size_t batch_records = 0;
    std::size_t buffer_bytes = 0;
    unsigned concurrency = 1;
    unsigned gpu_split_percent = 0;
    unsigned utilization_limit_percent = 0;
    std::uint64_t memory_pool_limit_bytes = 0;
    std::uint64_t pinned_host_memory_limit_bytes = 0;
    bool background_priority = true;
};

struct GpuBenefitReport {
    bool gpu_available = false;
    bool gpu_eligible = false;
    bool gpu_selected = false;
    std::string selected_backend = "CPU";
    std::string selection_reason = "GpuUnavailable";
    std::string benefit_gate_decision = "CpuSelected";
    std::string operation_class = "TraceFeatureExtraction";
    std::string phase = "PostCapture";
    std::uint64_t workload_records = 0;
    std::uint64_t workload_bytes = 0;
    std::uint64_t average_record_bytes = 0;
    std::uint64_t expected_arithmetic_intensity = 1;
    std::uint64_t expected_transfer_bytes = 0;
    std::uint64_t expected_batch_count = 0;
    std::uint64_t predicted_cpu_microseconds = 0;
    std::uint64_t predicted_gpu_microseconds = 0;
    double predicted_speedup = 0.0;
    std::string calibration_source = "ConservativeDefault";
    std::uint64_t calibration_sample_count = 0;
    std::uint64_t paired_full_path_sample_count = 0;
    std::uint64_t latest_cpu_baseline_microseconds = 0;
    std::uint64_t latest_gpu_full_path_microseconds = 0;
    double latest_full_path_speedup = 0.0;
    unsigned required_speedup_margin_percent = 10;
    bool same_input_calibration = false;
    bool calibration_fresh = false;
    bool full_cpu_oracle_required = true;
    bool positive_crossover_possible = false;
    std::string cpu_load_status = "NotSampledNoSafeInterval";
};

struct GpuEvidenceRecord {
    std::vector<std::uint8_t> bytes;
};

struct GpuVerifiedFeature {
    std::uint32_t byte_sum_without_last = 0;
    std::uint64_t fnv1a = 1469598103934665603ull;
    std::uint64_t feature_energy = 0;
    std::uint64_t transition_score = 0;
    std::uint64_t pattern_score = 0;
    std::uint64_t operation_projection = 0;
    std::uint64_t cluster_key = 0;
};

struct GpuPerformanceReport {
    std::uint64_t input_records = 0;
    std::uint64_t input_bytes = 0;
    std::uint64_t gpu_batches = 0;
    std::uint64_t gpu_processed_records = 0;
    std::uint64_t gpu_processed_bytes = 0;
    std::uint64_t gpu_kernel_launch_count = 0;
    std::uint64_t cpu_processed_records = 0;
    std::uint64_t queue_high_water_mark = 0;
    std::uint64_t gpu_oom_count = 0;
    std::uint64_t backend_error_count = 0;
    std::uint64_t dropped_evidence = 0;
    std::uint64_t gpu_elapsed_microseconds = 0;
    std::uint64_t gpu_allocation_microseconds = 0;
    std::uint64_t gpu_h2d_microseconds = 0;
    std::uint64_t gpu_kernel_launch_microseconds = 0;
    std::uint64_t gpu_synchronize_microseconds = 0;
    std::uint64_t gpu_d2h_microseconds = 0;
    std::uint64_t cpu_elapsed_microseconds = 0;
    std::uint64_t wall_elapsed_microseconds = 0;
    std::uint64_t peak_gpu_memory_bytes = 0;
    std::uint64_t peak_pinned_host_memory_bytes = 0;
    std::uint64_t device_pool_allocation_count = 0;
    std::uint64_t device_pool_reuse_count = 0;
    std::uint64_t pinned_pool_allocation_count = 0;
    std::uint64_t pinned_pool_reuse_count = 0;
    std::uint64_t async_transfer_count = 0;
    std::uint64_t stream_synchronize_count = 0;
    std::uint64_t pool_reset_count = 0;
    std::uint64_t timeout_count = 0;
    unsigned configured_stream_count = 0;
    unsigned peak_inflight_batches = 0;
    bool fallback_occurred = false;
    bool cancelled = false;
    std::string fallback_reason;
};

struct GpuWorkloadEvidence {
    std::string operation_class;
    std::string algorithm;
    std::string normalized_input_contract;
    std::string candidate_output_contract;
    std::string workload_scope;
    std::string implementation_status = "CpuAuthoritative";
    std::string model_status = "NotApplicable";
    std::string normalized_input_digest;
    std::string gpu_primitive_digest;
    std::string cpu_primitive_digest;
    std::string gpu_candidate_digest;
    std::string authoritative_output_digest;
    bool gpu_primitive_extraction_executed = false;
    bool gpu_candidate_executed = false;
    bool gpu_implementation_available = false;
    bool cpu_authority_preserved = true;
};

struct GpuEquivalenceReport {
    std::string status = "NotRun";
    std::uint64_t compared_records = 0;
    std::uint64_t mismatch_count = 0;
    bool authoritative_results_equivalent = true;
    bool verified_results_equivalent = true;
    bool gpu_authority_prohibited = true;
};

struct AiModelProviderContract {
    std::string contract_version = "LocalSemanticReasonerProviderV1";
    std::string status = "EvidenceBlockedModelUnavailable";
    std::string provider = "None";
    std::string model_name = "None";
    std::string model_version = "None";
    std::string model_license = "None";
    std::string model_sha256 = "None";
    std::string output_authority = "HYPOTHESIS";
    bool local_execution_required = true;
    bool evidence_upload_prohibited = true;
    bool quantized_inference_supported = false;
    bool cpu_fallback_required = true;
    bool verified_artifact_loaded = false;
};

struct GpuProcessingResult {
    bool success = true;
    GpuCapabilityReport capability;
    GpuSchedulerPlan scheduler;
    GpuBenefitReport benefit;
    GpuPerformanceReport performance;
    GpuEquivalenceReport equivalence;
    GpuWorkloadEvidence workload;
    AiModelProviderContract ai_provider;
    std::vector<GpuVerifiedFeature> verified_features;
};

std::string ToString(AccelerationMode value);
std::string ToString(GpuArchitectureTier value);
std::string ToString(GpuOperationClass value);
std::string GpuModeDisplayName(GpuArchitectureTier value);
AccelerationMode ParseAccelerationMode(std::string_view value);
GpuArchitectureTier ClassifyGpuArchitecture(int compute_major, int compute_minor);
GpuSchedulerPlan MakeGpuSchedulerPlan(const GpuCapabilityReport& capability,
                                      AccelerationPhase phase,
                                      std::size_t queue_depth,
                                      std::uint64_t evidence_bytes);

class OptionalGpuAccelerator {
public:
    explicit OptionalGpuAccelerator(AccelerationMode mode = AccelerationMode::Auto,
                                    GpuTestScenario scenario = GpuTestScenario::RealHardware);
    ~OptionalGpuAccelerator();
    OptionalGpuAccelerator(const OptionalGpuAccelerator&) = delete;
    OptionalGpuAccelerator& operator=(const OptionalGpuAccelerator&) = delete;

    const GpuCapabilityReport& Capability() const noexcept;
    GpuProcessingResult Process(const std::vector<GpuEvidenceRecord>& records,
                                AccelerationPhase phase,
                                const std::atomic_bool* stop_requested = nullptr,
                                GpuOperationClass operation_class = GpuOperationClass::TraceFeatureExtraction,
                                GpuDispatchIntent dispatch_intent = GpuDispatchIntent::Adaptive);

private:
    class Implementation;
    std::unique_ptr<Implementation> implementation_;
};

bool WriteGpuEvidenceReports(const fs::path& performance_directory,
                             std::string_view session_id,
                             const GpuProcessingResult& result,
                             std::string* error = nullptr);
int RunGpuAccelerationContractTests(const fs::path& report_path);
int RunGpuAccelerationBenchmark(const fs::path& report_path);

} // namespace god2
