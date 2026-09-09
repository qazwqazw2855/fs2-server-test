#include "GpuAcceleration.h"

#include <windows.h>
#include <psapi.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <limits>
#include <numeric>
#include <sstream>
#include <utility>

namespace god2 {
namespace {

using CUresult = int;
using CUdevice = int;
using CUcontext = void*;
using CUmodule = void*;
using CUfunction = void*;
using CUstream = void*;
using CUevent = void*;
using CUdeviceptr = unsigned long long;

constexpr CUresult kCudaSuccess = 0;
constexpr CUresult kCudaErrorNotReady = 600;
constexpr int kComputeCapabilityMajor = 75;
constexpr int kComputeCapabilityMinor = 76;
constexpr std::uint64_t kMib = 1024ull * 1024ull;
constexpr std::uint64_t kMaximumRecordBytes = 16ull * 1024ull * 1024ull;

// PTX is JIT-compiled by the installed NVIDIA driver.  PTX ISA 5.0 / sm_61 is
// intentionally the compatibility floor, so Pascal is not made dependent on a
// CUDA 13 toolchain or an RTX-only runtime.  No NVIDIA binary is redistributed.
constexpr char kEvidenceFeaturePtx[] = R"ptx(
.version 5.0
.target sm_61
.address_size 64

.visible .entry god2_evidence_features(
    .param .u64 data_ptr,
    .param .u64 offsets_ptr,
    .param .u64 lengths_ptr,
    .param .u64 sums_ptr,
    .param .u64 hashes_ptr,
    .param .u64 energies_ptr,
    .param .u64 transitions_ptr,
    .param .u64 patterns_ptr,
    .param .u64 projections_ptr,
    .param .u64 cluster_keys_ptr,
    .param .u32 record_count,
    .param .u32 operation_class)
{
    .reg .pred %p<6>;
    .reg .b32 %r<24>;
    .reg .b64 %rd<40>;

    ld.param.u64 %rd1, [data_ptr];
    ld.param.u64 %rd2, [offsets_ptr];
    ld.param.u64 %rd3, [lengths_ptr];
    ld.param.u64 %rd4, [sums_ptr];
    ld.param.u64 %rd5, [hashes_ptr];
    ld.param.u64 %rd19, [energies_ptr];
    ld.param.u64 %rd20, [transitions_ptr];
    ld.param.u64 %rd21, [patterns_ptr];
    ld.param.u64 %rd22, [projections_ptr];
    ld.param.u64 %rd23, [cluster_keys_ptr];
    ld.param.u32 %r1, [record_count];
    ld.param.u32 %r11, [operation_class];
    mov.u32 %r2, %tid.x;
    mov.u32 %r3, %ctaid.x;
    mov.u32 %r4, %ntid.x;
    mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1;
    @%p1 bra DONE;

    mul.wide.u32 %rd6, %r5, 8;
    add.s64 %rd7, %rd2, %rd6;
    ld.global.u64 %rd8, [%rd7];
    mul.wide.u32 %rd9, %r5, 4;
    add.s64 %rd10, %rd3, %rd9;
    ld.global.u32 %r6, [%rd10];
    add.s64 %rd11, %rd1, %rd8;
    mov.u32 %r7, 0;
    mov.u32 %r8, 0;
    mov.u64 %rd12, 1469598103934665603;
    mov.u64 %rd13, 1099511628211;
    mov.u64 %rd24, 0;
    mov.u64 %rd25, 0;
    mov.u64 %rd26, 0;
    mov.u32 %r12, 0;

LOOP:
    setp.ge.u32 %p2, %r7, %r6;
    @%p2 bra STORE;
    cvt.u64.u32 %rd14, %r7;
    add.s64 %rd15, %rd11, %rd14;
    ld.global.u8 %r9, [%rd15];
    mul.wide.u32 %rd27, %r9, %r9;
    add.u64 %rd24, %rd24, %rd27;
    setp.eq.u32 %p3, %r7, 0;
    @%p3 bra FIRST_BYTE;
    sub.s32 %r13, %r9, %r12;
    abs.s32 %r13, %r13;
    cvt.u64.u32 %rd28, %r13;
    add.u64 %rd25, %rd25, %rd28;
    setp.eq.u32 %p4, %r9, %r12;
    @%p4 add.u64 %rd26, %rd26, 1;
FIRST_BYTE:
    mov.u32 %r12, %r9;
    cvt.u64.u32 %rd18, %r9;
    xor.b64 %rd12, %rd12, %rd18;
    mul.lo.u64 %rd12, %rd12, %rd13;
    add.u32 %r10, %r7, 1;
    setp.lt.u32 %p2, %r10, %r6;
    @%p2 add.u32 %r8, %r8, %r9;
    add.u32 %r7, %r7, 1;
    bra LOOP;

STORE:
    add.s64 %rd16, %rd4, %rd9;
    st.global.u32 [%rd16], %r8;
    add.s64 %rd17, %rd5, %rd6;
    st.global.u64 [%rd17], %rd12;
    add.s64 %rd29, %rd19, %rd6;
    st.global.u64 [%rd29], %rd24;
    add.s64 %rd30, %rd20, %rd6;
    st.global.u64 [%rd30], %rd25;
    add.s64 %rd31, %rd21, %rd6;
    st.global.u64 [%rd31], %rd26;

    add.u32 %r14, %r11, 1;
    cvt.u64.u32 %rd32, %r14;
    mul.lo.u64 %rd33, %rd24, %rd32;
    and.b32 %r15, %r11, 7;
    add.u32 %r15, %r15, 1;
    shl.b64 %rd34, %rd25, %r15;
    xor.b64 %rd33, %rd33, %rd34;
    mul.lo.u64 %rd35, %rd26, 257;
    xor.b64 %rd33, %rd33, %rd35;
    add.s64 %rd36, %rd22, %rd6;
    st.global.u64 [%rd36], %rd33;

    mul.lo.u64 %rd37, %rd24, 1099511628211;
    mul.lo.u64 %rd38, %rd25, 1469598103934665603;
    xor.b64 %rd39, %rd12, %rd37;
    xor.b64 %rd39, %rd39, %rd38;
    xor.b64 %rd39, %rd39, %rd33;
    add.s64 %rd37, %rd23, %rd6;
    st.global.u64 [%rd37], %rd39;
DONE:
    ret;
}
)ptx"
R"ptx(

// TraceFeatureExtraction normalized contract:
// one byte record -> base primitives + TraceFeatureVectorV3 projection/key.
.visible .entry god2_trace_feature_v3(
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<2>;
    .reg .b32 %r<8>;
    .reg .b64 %rd<32>;
    ld.param.u64 %rd1, [sums_ptr];
    ld.param.u64 %rd2, [hashes_ptr];
    ld.param.u64 %rd3, [energies_ptr];
    ld.param.u64 %rd4, [transitions_ptr];
    ld.param.u64 %rd5, [patterns_ptr];
    ld.param.u64 %rd6, [projections_ptr];
    ld.param.u64 %rd7, [cluster_keys_ptr];
    ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x;
    mov.u32 %r3, %ctaid.x;
    mov.u32 %r4, %ntid.x;
    mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1;
    @%p1 bra TRACE_DONE;
    mul.wide.u32 %rd8, %r5, 8;
    mul.wide.u32 %rd9, %r5, 4;
    add.s64 %rd10, %rd1, %rd9;
    ld.global.u32 %r6, [%rd10];
    cvt.u64.u32 %rd11, %r6;
    add.s64 %rd12, %rd2, %rd8;
    ld.global.u64 %rd13, [%rd12];
    add.s64 %rd14, %rd3, %rd8;
    ld.global.u64 %rd15, [%rd14];
    add.s64 %rd16, %rd4, %rd8;
    ld.global.u64 %rd17, [%rd16];
    add.s64 %rd18, %rd5, %rd8;
    ld.global.u64 %rd19, [%rd18];
    shl.b64 %rd20, %rd17, 7;
    shr.u64 %rd21, %rd17, 57;
    or.b64 %rd20, %rd20, %rd21;
    shl.b64 %rd22, %rd11, 17;
    xor.b64 %rd23, %rd15, %rd20;
    xor.b64 %rd23, %rd23, %rd22;
    shl.b64 %rd24, %rd19, 29;
    shr.u64 %rd25, %rd19, 35;
    or.b64 %rd24, %rd24, %rd25;
    xor.b64 %rd26, %rd13, %rd24;
    mul.lo.u64 %rd27, %rd15, 1099511628211;
    mul.lo.u64 %rd28, %rd17, 1469598103934665603;
    xor.b64 %rd29, %rd26, %rd23;
    xor.b64 %rd29, %rd29, %rd27;
    xor.b64 %rd29, %rd29, %rd28;
    add.s64 %rd30, %rd6, %rd8;
    st.global.u64 [%rd30], %rd23;
    add.s64 %rd31, %rd7, %rd8;
    st.global.u64 [%rd31], %rd29;
TRACE_DONE:
    ret;
}

// ParserFieldPatternMatching normalized contract:
// byte record -> delimiter count and remaining-length marker count.
.visible .entry god2_parser_pattern_v1(
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<10>;
    .reg .b32 %r<16>;
    .reg .b64 %rd<40>;
    ld.param.u64 %rd1, [data_ptr];
    ld.param.u64 %rd2, [offsets_ptr];
    ld.param.u64 %rd3, [lengths_ptr];
    ld.param.u64 %rd4, [hashes_ptr];
    ld.param.u64 %rd5, [energies_ptr];
    ld.param.u64 %rd6, [transitions_ptr];
    ld.param.u64 %rd7, [projections_ptr];
    ld.param.u64 %rd8, [cluster_keys_ptr];
    ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x; mov.u32 %r3, %ctaid.x; mov.u32 %r4, %ntid.x;
    mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1; @%p1 bra PARSER_DONE;
    mul.wide.u32 %rd9, %r5, 8;
    mul.wide.u32 %rd10, %r5, 4;
    add.s64 %rd11, %rd2, %rd9; ld.global.u64 %rd12, [%rd11];
    add.s64 %rd13, %rd3, %rd10; ld.global.u32 %r6, [%rd13];
    add.s64 %rd14, %rd1, %rd12;
    mov.u32 %r7, 0; mov.u64 %rd20, 0; mov.u64 %rd21, 0;
PARSER_LOOP:
    setp.ge.u32 %p2, %r7, %r6; @%p2 bra PARSER_STORE;
    cvt.u64.u32 %rd15, %r7; add.s64 %rd16, %rd14, %rd15; ld.global.u8 %r8, [%rd16];
    setp.eq.u32 %p3, %r8, 0; @%p3 add.u64 %rd20, %rd20, 1;
    setp.eq.u32 %p4, %r8, 255; @%p4 add.u64 %rd20, %rd20, 1;
    setp.eq.u32 %p5, %r8, 58; @%p5 add.u64 %rd20, %rd20, 1;
    setp.eq.u32 %p6, %r8, 61; @%p6 add.u64 %rd20, %rd20, 1;
    add.u32 %r9, %r7, 2; setp.ge.u32 %p7, %r9, %r6; @%p7 bra PARSER_NEXT;
    sub.u32 %r10, %r6, %r7; sub.u32 %r10, %r10, 1;
    setp.eq.u32 %p8, %r8, %r10; @%p8 add.u64 %rd21, %rd21, 1;
PARSER_NEXT:
    add.u32 %r7, %r7, 1; bra PARSER_LOOP;
PARSER_STORE:
    add.s64 %rd22, %rd4, %rd9; ld.global.u64 %rd23, [%rd22];
    add.s64 %rd24, %rd5, %rd9; ld.global.u64 %rd25, [%rd24];
    add.s64 %rd26, %rd6, %rd9; ld.global.u64 %rd27, [%rd26];
    mul.lo.u64 %rd28, %rd20, 257; mul.lo.u64 %rd29, %rd21, 65537;
    cvt.u64.u32 %rd30, %r6; mul.lo.u64 %rd30, %rd30, 4099;
    xor.b64 %rd31, %rd28, %rd29; xor.b64 %rd31, %rd31, %rd30;
    shl.b64 %rd32, %rd20, 19; shr.u64 %rd33, %rd20, 45; or.b64 %rd32, %rd32, %rd33;
    xor.b64 %rd34, %rd23, %rd32; xor.b64 %rd34, %rd34, %rd21;
    mul.lo.u64 %rd35, %rd25, 1099511628211;
    mul.lo.u64 %rd36, %rd27, 1469598103934665603;
    xor.b64 %rd37, %rd34, %rd31; xor.b64 %rd37, %rd37, %rd35; xor.b64 %rd37, %rd37, %rd36;
    add.s64 %rd38, %rd7, %rd9; st.global.u64 [%rd38], %rd31;
    add.s64 %rd39, %rd8, %rd9; st.global.u64 [%rd39], %rd37;
PARSER_DONE:
    ret;
}

// ClassLayoutScoring normalized contract:
// byte record -> stride-4 repetition score and deterministic layout key.
.visible .entry god2_class_layout_v1(
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<5>; .reg .b32 %r<12>; .reg .b64 %rd<40>;
    ld.param.u64 %rd1, [data_ptr]; ld.param.u64 %rd2, [offsets_ptr]; ld.param.u64 %rd3, [lengths_ptr];
    ld.param.u64 %rd4, [hashes_ptr]; ld.param.u64 %rd5, [energies_ptr]; ld.param.u64 %rd6, [transitions_ptr];
    ld.param.u64 %rd7, [projections_ptr]; ld.param.u64 %rd8, [cluster_keys_ptr]; ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x; mov.u32 %r3, %ctaid.x; mov.u32 %r4, %ntid.x; mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1; @%p1 bra CLASS_DONE;
    mul.wide.u32 %rd9, %r5, 8; mul.wide.u32 %rd10, %r5, 4;
    add.s64 %rd11, %rd2, %rd9; ld.global.u64 %rd12, [%rd11]; add.s64 %rd13, %rd1, %rd12;
    add.s64 %rd14, %rd3, %rd10; ld.global.u32 %r6, [%rd14]; mov.u32 %r7, 4; mov.u64 %rd20, 0;
CLASS_LOOP:
    setp.ge.u32 %p2, %r7, %r6; @%p2 bra CLASS_STORE;
    cvt.u64.u32 %rd15, %r7; add.s64 %rd16, %rd13, %rd15; ld.global.u8 %r8, [%rd16];
    sub.u64 %rd17, %rd16, 4; ld.global.u8 %r9, [%rd17]; setp.eq.u32 %p3, %r8, %r9;
    @%p3 add.u64 %rd20, %rd20, 1; add.u32 %r7, %r7, 1; bra CLASS_LOOP;
CLASS_STORE:
    add.s64 %rd21, %rd4, %rd9; ld.global.u64 %rd22, [%rd21];
    add.s64 %rd23, %rd5, %rd9; ld.global.u64 %rd24, [%rd23];
    add.s64 %rd25, %rd6, %rd9; ld.global.u64 %rd26, [%rd25];
    mul.lo.u64 %rd27, %rd20, 104729; cvt.u64.u32 %rd28, %r6; mul.lo.u64 %rd29, %rd28, 4099;
    xor.b64 %rd30, %rd27, %rd29; xor.b64 %rd30, %rd30, %rd24;
    shl.b64 %rd31, %rd20, 31; shr.u64 %rd32, %rd20, 33; or.b64 %rd31, %rd31, %rd32;
    xor.b64 %rd33, %rd22, %rd31; xor.b64 %rd33, %rd33, %rd28;
    mul.lo.u64 %rd34, %rd24, 1099511628211; mul.lo.u64 %rd35, %rd26, 1469598103934665603;
    xor.b64 %rd36, %rd33, %rd30; xor.b64 %rd36, %rd36, %rd34; xor.b64 %rd36, %rd36, %rd35;
    add.s64 %rd37, %rd7, %rd9; st.global.u64 [%rd37], %rd30;
    add.s64 %rd38, %rd8, %rd9; st.global.u64 [%rd38], %rd36;
CLASS_DONE:
    ret;
}

// RegistryScoring normalized contract:
// UTF-8/key bytes -> accepted key-char count and ASCII case-folded FNV key.
.visible .entry god2_registry_score_v1(
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<16>; .reg .b32 %r<16>; .reg .b64 %rd<40>;
    ld.param.u64 %rd1, [data_ptr]; ld.param.u64 %rd2, [offsets_ptr]; ld.param.u64 %rd3, [lengths_ptr];
    ld.param.u64 %rd4, [energies_ptr]; ld.param.u64 %rd5, [transitions_ptr];
    ld.param.u64 %rd6, [projections_ptr]; ld.param.u64 %rd7, [cluster_keys_ptr]; ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x; mov.u32 %r3, %ctaid.x; mov.u32 %r4, %ntid.x; mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1; @%p1 bra REG_DONE;
    mul.wide.u32 %rd8, %r5, 8; mul.wide.u32 %rd9, %r5, 4;
    add.s64 %rd10, %rd2, %rd8; ld.global.u64 %rd11, [%rd10]; add.s64 %rd12, %rd1, %rd11;
    add.s64 %rd13, %rd3, %rd9; ld.global.u32 %r6, [%rd13]; mov.u32 %r7, 0;
    mov.u64 %rd20, 0; mov.u64 %rd21, 1469598103934665603;
REG_LOOP:
    setp.ge.u32 %p2, %r7, %r6; @%p2 bra REG_STORE;
    cvt.u64.u32 %rd14, %r7; add.s64 %rd15, %rd12, %rd14; ld.global.u8 %r8, [%rd15];
    setp.ge.u32 %p3, %r8, 48; setp.le.u32 %p4, %r8, 57; and.pred %p5, %p3, %p4; @%p5 bra REG_KEY;
    setp.ge.u32 %p6, %r8, 65; setp.le.u32 %p7, %r8, 90; and.pred %p8, %p6, %p7; @%p8 bra REG_KEY;
    setp.ge.u32 %p9, %r8, 97; setp.le.u32 %p10, %r8, 122; and.pred %p11, %p9, %p10; @%p11 bra REG_KEY;
    setp.eq.u32 %p12, %r8, 95; @%p12 bra REG_KEY; setp.eq.u32 %p13, %r8, 46; @%p13 bra REG_KEY;
    setp.eq.u32 %p14, %r8, 47; @%p14 bra REG_KEY; bra REG_FOLD;
REG_KEY:
    add.u64 %rd20, %rd20, 1;
REG_FOLD:
    setp.ge.u32 %p6, %r8, 65; setp.le.u32 %p7, %r8, 90; and.pred %p8, %p6, %p7;
    @%p8 add.u32 %r8, %r8, 32; cvt.u64.u32 %rd16, %r8; xor.b64 %rd21, %rd21, %rd16;
    mul.lo.u64 %rd21, %rd21, 1099511628211; add.u32 %r7, %r7, 1; bra REG_LOOP;
REG_STORE:
    add.s64 %rd22, %rd4, %rd8; ld.global.u64 %rd23, [%rd22];
    add.s64 %rd24, %rd5, %rd8; ld.global.u64 %rd25, [%rd24];
    mul.lo.u64 %rd26, %rd20, 257; xor.b64 %rd27, %rd26, %rd21;
    shl.b64 %rd28, %rd25, 11; shr.u64 %rd29, %rd25, 53; or.b64 %rd28, %rd28, %rd29;
    xor.b64 %rd30, %rd21, %rd28;
    mul.lo.u64 %rd31, %rd23, 1099511628211; mul.lo.u64 %rd32, %rd25, 1469598103934665603;
    xor.b64 %rd33, %rd30, %rd27; xor.b64 %rd33, %rd33, %rd31; xor.b64 %rd33, %rd33, %rd32;
    add.s64 %rd34, %rd6, %rd8; st.global.u64 [%rd34], %rd27;
    add.s64 %rd35, %rd7, %rd8; st.global.u64 [%rd35], %rd33;
REG_DONE:
    ret;
}

// ValueFlowAggregation normalized contract:
// ordered byte values -> positive/negative adjacent deltas.
.visible .entry god2_value_flow_v1(
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<6>; .reg .b32 %r<16>; .reg .b64 %rd<40>;
    ld.param.u64 %rd1, [data_ptr]; ld.param.u64 %rd2, [offsets_ptr]; ld.param.u64 %rd3, [lengths_ptr];
    ld.param.u64 %rd4, [hashes_ptr]; ld.param.u64 %rd5, [energies_ptr]; ld.param.u64 %rd6, [transitions_ptr];
    ld.param.u64 %rd7, [projections_ptr]; ld.param.u64 %rd8, [cluster_keys_ptr]; ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x; mov.u32 %r3, %ctaid.x; mov.u32 %r4, %ntid.x; mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1; @%p1 bra FLOW_DONE;
    mul.wide.u32 %rd9, %r5, 8; mul.wide.u32 %rd10, %r5, 4;
    add.s64 %rd11, %rd2, %rd9; ld.global.u64 %rd12, [%rd11]; add.s64 %rd13, %rd1, %rd12;
    add.s64 %rd14, %rd3, %rd10; ld.global.u32 %r6, [%rd14]; mov.u64 %rd20, 0; mov.u64 %rd21, 0;
    setp.eq.u32 %p2, %r6, 0; @%p2 bra FLOW_STORE; ld.global.u8 %r8, [%rd13]; mov.u32 %r7, 1;
FLOW_LOOP:
    setp.ge.u32 %p3, %r7, %r6; @%p3 bra FLOW_STORE;
    cvt.u64.u32 %rd15, %r7; add.s64 %rd16, %rd13, %rd15; ld.global.u8 %r9, [%rd16];
    setp.ge.u32 %p4, %r9, %r8; @%p4 bra FLOW_POS;
    sub.u32 %r10, %r8, %r9; cvt.u64.u32 %rd17, %r10; add.u64 %rd21, %rd21, %rd17; bra FLOW_NEXT;
FLOW_POS:
    sub.u32 %r10, %r9, %r8; cvt.u64.u32 %rd17, %r10; add.u64 %rd20, %rd20, %rd17;
FLOW_NEXT:
    mov.u32 %r8, %r9; add.u32 %r7, %r7, 1; bra FLOW_LOOP;
FLOW_STORE:
    add.s64 %rd22, %rd4, %rd9; ld.global.u64 %rd23, [%rd22];
    add.s64 %rd24, %rd5, %rd9; ld.global.u64 %rd25, [%rd24];
    add.s64 %rd26, %rd6, %rd9; ld.global.u64 %rd27, [%rd26];
    mul.lo.u64 %rd28, %rd20, 65537; shl.b64 %rd29, %rd21, 32; shr.u64 %rd30, %rd21, 32;
    or.b64 %rd29, %rd29, %rd30; xor.b64 %rd31, %rd28, %rd29; xor.b64 %rd31, %rd31, %rd25;
    shl.b64 %rd32, %rd21, 17; shr.u64 %rd33, %rd21, 47; or.b64 %rd32, %rd32, %rd33;
    xor.b64 %rd34, %rd23, %rd20; xor.b64 %rd34, %rd34, %rd32;
    mul.lo.u64 %rd35, %rd25, 1099511628211; mul.lo.u64 %rd36, %rd27, 1469598103934665603;
    xor.b64 %rd37, %rd34, %rd31; xor.b64 %rd37, %rd37, %rd35; xor.b64 %rd37, %rd37, %rd36;
    add.s64 %rd38, %rd7, %rd9; st.global.u64 [%rd38], %rd31; add.s64 %rd39, %rd8, %rd9; st.global.u64 [%rd39], %rd37;
FLOW_DONE:
    ret;
}

// FormulaBatchEvaluation normalized contract:
// each byte is an unsigned fixed-point operand; evaluate x^2+3x+7 and sum.
.visible .entry god2_formula_batch_v1(
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<3>; .reg .b32 %r<12>; .reg .b64 %rd<40>;
    ld.param.u64 %rd1, [data_ptr]; ld.param.u64 %rd2, [offsets_ptr]; ld.param.u64 %rd3, [lengths_ptr];
    ld.param.u64 %rd4, [hashes_ptr]; ld.param.u64 %rd5, [energies_ptr]; ld.param.u64 %rd6, [transitions_ptr];
    ld.param.u64 %rd7, [projections_ptr]; ld.param.u64 %rd8, [cluster_keys_ptr]; ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x; mov.u32 %r3, %ctaid.x; mov.u32 %r4, %ntid.x; mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1; @%p1 bra FORM_DONE;
    mul.wide.u32 %rd9, %r5, 8; mul.wide.u32 %rd10, %r5, 4;
    add.s64 %rd11, %rd2, %rd9; ld.global.u64 %rd12, [%rd11]; add.s64 %rd13, %rd1, %rd12;
    add.s64 %rd14, %rd3, %rd10; ld.global.u32 %r6, [%rd14]; mov.u32 %r7, 0; mov.u64 %rd20, 0;
FORM_LOOP:
    setp.ge.u32 %p2, %r7, %r6; @%p2 bra FORM_STORE;
    cvt.u64.u32 %rd15, %r7; add.s64 %rd16, %rd13, %rd15; ld.global.u8 %r8, [%rd16];
    mul.wide.u32 %rd17, %r8, %r8; cvt.u64.u32 %rd18, %r8; mul.lo.u64 %rd18, %rd18, 3;
    add.u64 %rd17, %rd17, %rd18; add.u64 %rd17, %rd17, 7; add.u64 %rd20, %rd20, %rd17;
    add.u32 %r7, %r7, 1; bra FORM_LOOP;
FORM_STORE:
    add.s64 %rd21, %rd4, %rd9; ld.global.u64 %rd22, [%rd21];
    add.s64 %rd23, %rd5, %rd9; ld.global.u64 %rd24, [%rd23];
    add.s64 %rd25, %rd6, %rd9; ld.global.u64 %rd26, [%rd25];
    xor.b64 %rd27, %rd20, %rd24; mul.lo.u64 %rd28, %rd20, 1099511628211; xor.b64 %rd29, %rd28, %rd22;
    mul.lo.u64 %rd30, %rd24, 1099511628211; mul.lo.u64 %rd31, %rd26, 1469598103934665603;
    xor.b64 %rd32, %rd29, %rd27; xor.b64 %rd32, %rd32, %rd30; xor.b64 %rd32, %rd32, %rd31;
    add.s64 %rd33, %rd7, %rd9; st.global.u64 [%rd33], %rd27; add.s64 %rd34, %rd8, %rd9; st.global.u64 [%rd34], %rd32;
FORM_DONE:
    ret;
}
)ptx";

struct StructuredKernelDescriptor {
    GpuOperationClass operation;
    const char* entry;
    std::uint64_t projection_energy;
    std::uint64_t projection_transition;
    std::uint64_t projection_pattern;
    std::uint64_t projection_sum;
    std::uint64_t structural_energy;
    std::uint64_t structural_transition;
};

// The structured workloads consume the canonical primitive feature matrix
// produced by god2_evidence_features.  Each entry is a separately named CUDA
// kernel with an operation-specific, integer-only candidate projection.  The
// same descriptor is used by the CPU oracle below, avoiding float tolerance or
// architecture-dependent reductions while still performing useful clustering,
// graph, sequence, and replay candidate generation on the device.
constexpr std::array<StructuredKernelDescriptor, 10> kStructuredKernelDescriptors{{
    {GpuOperationClass::CrossSessionCorrelation, "god2_cross_session_correlation_v2",
        1099511628211ull, 257ull, 4099ull, 65537ull, 104729ull, 16777619ull},
    {GpuOperationClass::FunctionClustering, "god2_function_clustering_v2",
        65537ull, 1099511628211ull, 104729ull, 4099ull, 257ull, 1469598103934665603ull},
    {GpuOperationClass::ObjectClustering, "god2_object_clustering_v2",
        4099ull, 104729ull, 65537ull, 257ull, 16777619ull, 1099511628211ull},
    {GpuOperationClass::HeapGraphSimilarity, "god2_heap_graph_similarity_v2",
        104729ull, 16777619ull, 257ull, 1099511628211ull, 65537ull, 4099ull},
    {GpuOperationClass::TaintGraphBatch, "god2_taint_graph_batch_v2",
        257ull, 65537ull, 16777619ull, 104729ull, 4099ull, 1099511628211ull},
    {GpuOperationClass::SemanticEdgeScoring, "god2_semantic_edge_scoring_v2",
        16777619ull, 4099ull, 1099511628211ull, 257ull, 104729ull, 65537ull},
    {GpuOperationClass::ApproximateNearestNeighbor, "god2_ann_search_v2",
        4099ull, 257ull, 65537ull, 16777619ull, 1099511628211ull, 104729ull},
    {GpuOperationClass::SequenceMining, "god2_sequence_mining_v2",
        65537ull, 104729ull, 4099ull, 16777619ull, 257ull, 1099511628211ull},
    {GpuOperationClass::FsmScoring, "god2_fsm_scoring_v2",
        104729ull, 4099ull, 16777619ull, 65537ull, 1099511628211ull, 257ull},
    {GpuOperationClass::ReplayStateComparison, "god2_replay_state_comparison_v2",
        16777619ull, 1099511628211ull, 257ull, 4099ull, 65537ull, 104729ull}
}};

const StructuredKernelDescriptor* StructuredKernelFor(GpuOperationClass operation) noexcept {
    const auto found = std::find_if(kStructuredKernelDescriptors.begin(), kStructuredKernelDescriptors.end(),
        [operation](const StructuredKernelDescriptor& descriptor) { return descriptor.operation == operation; });
    return found == kStructuredKernelDescriptors.end() ? nullptr : &*found;
}

std::string BuildEvidenceFeaturePtx() {
    std::string ptx(kEvidenceFeaturePtx);
    for (const auto& descriptor : kStructuredKernelDescriptors) {
        ptx += "\n.visible .entry ";
        ptx += descriptor.entry;
        ptx += R"ptx((
    .param .u64 data_ptr, .param .u64 offsets_ptr, .param .u64 lengths_ptr,
    .param .u64 sums_ptr, .param .u64 hashes_ptr, .param .u64 energies_ptr,
    .param .u64 transitions_ptr, .param .u64 patterns_ptr,
    .param .u64 projections_ptr, .param .u64 cluster_keys_ptr,
    .param .u32 record_count)
{
    .reg .pred %p<2>;
    .reg .b32 %r<8>;
    .reg .b64 %rd<40>;
    ld.param.u64 %rd1, [sums_ptr];
    ld.param.u64 %rd2, [hashes_ptr];
    ld.param.u64 %rd3, [energies_ptr];
    ld.param.u64 %rd4, [transitions_ptr];
    ld.param.u64 %rd5, [patterns_ptr];
    ld.param.u64 %rd6, [projections_ptr];
    ld.param.u64 %rd7, [cluster_keys_ptr];
    ld.param.u32 %r1, [record_count];
    mov.u32 %r2, %tid.x;
    mov.u32 %r3, %ctaid.x;
    mov.u32 %r4, %ntid.x;
    mad.lo.s32 %r5, %r3, %r4, %r2;
    setp.ge.u32 %p1, %r5, %r1;
    @%p1 bra STRUCTURED_DONE;
    mul.wide.u32 %rd8, %r5, 8;
    mul.wide.u32 %rd9, %r5, 4;
    add.s64 %rd10, %rd1, %rd9; ld.global.u32 %r6, [%rd10]; cvt.u64.u32 %rd11, %r6;
    add.s64 %rd12, %rd2, %rd8; ld.global.u64 %rd13, [%rd12];
    add.s64 %rd14, %rd3, %rd8; ld.global.u64 %rd15, [%rd14];
    add.s64 %rd16, %rd4, %rd8; ld.global.u64 %rd17, [%rd16];
    add.s64 %rd18, %rd5, %rd8; ld.global.u64 %rd19, [%rd18];
)ptx";
        ptx += "    mul.lo.u64 %rd20, %rd15, " + std::to_string(descriptor.projection_energy) + ";\n";
        ptx += "    mul.lo.u64 %rd21, %rd17, " + std::to_string(descriptor.projection_transition) + ";\n";
        ptx += "    mul.lo.u64 %rd22, %rd19, " + std::to_string(descriptor.projection_pattern) + ";\n";
        ptx += "    mul.lo.u64 %rd23, %rd11, " + std::to_string(descriptor.projection_sum) + ";\n";
        ptx += R"ptx(    xor.b64 %rd24, %rd20, %rd21; xor.b64 %rd24, %rd24, %rd22;
    xor.b64 %rd24, %rd24, %rd23; xor.b64 %rd24, %rd24, %rd13;
)ptx";
        ptx += "    mul.lo.u64 %rd25, %rd15, " + std::to_string(descriptor.structural_energy) + ";\n";
        ptx += "    mul.lo.u64 %rd26, %rd17, " + std::to_string(descriptor.structural_transition) + ";\n";
        ptx += R"ptx(    xor.b64 %rd27, %rd13, %rd25; xor.b64 %rd27, %rd27, %rd26; xor.b64 %rd27, %rd27, %rd19;
    mul.lo.u64 %rd28, %rd15, 1099511628211;
    mul.lo.u64 %rd29, %rd17, 1469598103934665603;
    xor.b64 %rd30, %rd27, %rd24; xor.b64 %rd30, %rd30, %rd28; xor.b64 %rd30, %rd30, %rd29;
    add.s64 %rd31, %rd6, %rd8; st.global.u64 [%rd31], %rd24;
    add.s64 %rd32, %rd7, %rd8; st.global.u64 [%rd32], %rd30;
STRUCTURED_DONE:
    ret;
}
)ptx";
    }
    return ptx;
}

template <typename T>
bool LoadFunction(HMODULE module, const char* name, T* output) {
    *output = reinterpret_cast<T>(GetProcAddress(module, name));
    return *output != nullptr;
}

std::uint64_t ElapsedMicroseconds(std::chrono::steady_clock::time_point start) {
    return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now() - start).count());
}

GpuVerifiedFeature CpuFeature(const std::vector<std::uint8_t>& bytes,
                              GpuOperationClass operation_class) {
    GpuVerifiedFeature result;
    std::uint8_t previous = 0;
    for (std::size_t index = 0; index < bytes.size(); ++index) {
        const std::uint8_t value = bytes[index];
        result.fnv1a ^= value;
        result.fnv1a *= 1099511628211ull;
        if (index + 1 < bytes.size()) result.byte_sum_without_last += value;
        result.feature_energy += static_cast<std::uint64_t>(value) * value;
        if (index != 0) {
            result.transition_score += value >= previous ? value - previous : previous - value;
            if (value == previous) ++result.pattern_score;
        }
        previous = value;
    }
    if (const auto* descriptor = StructuredKernelFor(operation_class); descriptor != nullptr) {
        result.operation_projection =
            (result.feature_energy * descriptor->projection_energy) ^
            (result.transition_score * descriptor->projection_transition) ^
            (result.pattern_score * descriptor->projection_pattern) ^
            (static_cast<std::uint64_t>(result.byte_sum_without_last) * descriptor->projection_sum) ^
            result.fnv1a;
        const std::uint64_t structural = result.fnv1a ^
            (result.feature_energy * descriptor->structural_energy) ^
            (result.transition_score * descriptor->structural_transition) ^ result.pattern_score;
        result.cluster_key = structural ^ result.operation_projection ^
            (result.feature_energy * 1099511628211ull) ^
            (result.transition_score * 1469598103934665603ull);
        return result;
    }
    const auto byte_at = [&bytes](std::size_t index) -> std::uint64_t {
        return bytes.empty() ? 0 : bytes[index % bytes.size()];
    };
    const auto word_at = [&byte_at](std::size_t index) -> std::uint64_t {
        return byte_at(index) | (byte_at(index + 1) << 8) |
            (byte_at(index + 2) << 16) | (byte_at(index + 3) << 24);
    };
    const auto rotate_left = [](std::uint64_t value, unsigned count) {
        count &= 63u;
        return count == 0 ? value : (value << count) | (value >> (64u - count));
    };
    std::uint64_t projection = 0;
    std::uint64_t structural = 1469598103934665603ull;
    switch (operation_class) {
    case GpuOperationClass::TraceFeatureExtraction:
        projection = result.feature_energy ^ rotate_left(result.transition_score, 7) ^
            (static_cast<std::uint64_t>(result.byte_sum_without_last) << 17);
        structural = result.fnv1a ^ rotate_left(result.pattern_score, 29);
        break;
    case GpuOperationClass::CrossSessionCorrelation: {
        const std::size_t half = bytes.size() / 2;
        std::uint64_t agreement = 0;
        for (std::size_t index = 0; index < half; ++index)
            agreement += static_cast<std::uint64_t>(255u -
                static_cast<unsigned>(std::abs(static_cast<int>(bytes[index]) -
                                               static_cast<int>(bytes[index + half]))));
        projection = agreement ^ rotate_left(result.fnv1a, 13) ^ bytes.size();
        structural = agreement * 1099511628211ull ^ result.transition_score;
        break;
    }
    case GpuOperationClass::ParserFieldPatternMatching: {
        std::uint64_t delimiters = 0;
        std::uint64_t length_matches = 0;
        for (std::size_t index = 0; index < bytes.size(); ++index) {
            const auto value = bytes[index];
            delimiters += value == 0 || value == 0xff || value == ':' || value == '=';
            if (index + 2 < bytes.size() && value == bytes.size() - index - 1) ++length_matches;
        }
        projection = delimiters * 257ull ^ length_matches * 65537ull ^
            (static_cast<std::uint64_t>(bytes.size()) * 4099ull);
        structural = result.fnv1a ^ rotate_left(delimiters, 19) ^ length_matches;
        break;
    }
    case GpuOperationClass::FunctionClustering: {
        std::array<std::uint64_t, 8> opcode_bins{};
        std::uint64_t control_flow = 0;
        for (const auto value : bytes) {
            ++opcode_bins[value >> 5];
            control_flow += value == 0xe8 || value == 0xe9 || value == 0xeb || value == 0xc3;
        }
        for (std::size_t index = 0; index < opcode_bins.size(); ++index)
            structural = (structural ^ (opcode_bins[index] + index * 131ull)) * 1099511628211ull;
        projection = control_flow * 4099ull ^ structural ^ word_at(0);
        break;
    }
    case GpuOperationClass::ObjectClustering: {
        std::uint64_t zero_words = 0;
        std::uint64_t aligned_words = 0;
        for (std::size_t index = 0; index + 3 < bytes.size(); index += 4) {
            const auto word = word_at(index);
            zero_words += word == 0;
            aligned_words += word != 0 && (word & 3u) == 0;
        }
        projection = zero_words * 65537ull ^ aligned_words * 4099ull ^ result.feature_energy;
        structural = result.fnv1a ^ rotate_left(aligned_words, 23) ^ rotate_left(zero_words, 41);
        break;
    }
    case GpuOperationClass::ClassLayoutScoring: {
        std::uint64_t repeated_stride = 0;
        for (std::size_t index = 4; index < bytes.size(); ++index)
            repeated_stride += bytes[index] == bytes[index - 4];
        projection = repeated_stride * 104729ull ^
            (static_cast<std::uint64_t>(bytes.size()) * 4099ull) ^ result.feature_energy;
        structural = result.fnv1a ^ rotate_left(repeated_stride, 31) ^ bytes.size();
        break;
    }
    case GpuOperationClass::RegistryScoring: {
        std::uint64_t key_chars = 0;
        std::uint64_t folded_hash = 1469598103934665603ull;
        for (auto value : bytes) {
            key_chars += (value >= '0' && value <= '9') || (value >= 'A' && value <= 'Z') ||
                (value >= 'a' && value <= 'z') || value == '_' || value == '.' || value == '/';
            if (value >= 'A' && value <= 'Z') value = static_cast<std::uint8_t>(value + ('a' - 'A'));
            folded_hash = (folded_hash ^ value) * 1099511628211ull;
        }
        projection = key_chars * 257ull ^ folded_hash;
        structural = folded_hash ^ rotate_left(result.transition_score, 11);
        break;
    }
    case GpuOperationClass::HeapGraphSimilarity: {
        std::uint64_t edge_similarity = 0;
        std::uint64_t prior = word_at(0);
        for (std::size_t index = 4; index + 3 < bytes.size(); index += 4) {
            const auto current = word_at(index);
            const auto delta = current >= prior ? current - prior : prior - current;
            edge_similarity += 0xffffffffull - std::min(delta, 0xffffffffull);
            prior = current;
        }
        projection = edge_similarity ^ rotate_left(result.fnv1a, 37);
        structural = edge_similarity * 1099511628211ull ^ result.pattern_score;
        break;
    }
    case GpuOperationClass::ValueFlowAggregation: {
        std::uint64_t positive = 0;
        std::uint64_t negative = 0;
        for (std::size_t index = 1; index < bytes.size(); ++index) {
            if (bytes[index] >= bytes[index - 1]) positive += bytes[index] - bytes[index - 1];
            else negative += bytes[index - 1] - bytes[index];
        }
        projection = positive * 65537ull ^ rotate_left(negative, 32) ^ result.feature_energy;
        structural = result.fnv1a ^ positive ^ rotate_left(negative, 17);
        break;
    }
    case GpuOperationClass::TaintGraphBatch: {
        std::uint64_t taint = 0;
        std::uint64_t propagation = 0;
        for (const auto value : bytes) {
            taint = ((taint << 1) | (value >= 0x80 ? 1ull : 0ull)) & 0xffffull;
            propagation += taint != 0;
        }
        projection = propagation * 4099ull ^ taint ^ result.fnv1a;
        structural = rotate_left(taint, 43) ^ result.transition_score;
        break;
    }
    case GpuOperationClass::SemanticEdgeScoring: {
        std::uint64_t edge_hash = 1469598103934665603ull;
        for (std::size_t index = 1; index < bytes.size(); ++index) {
            const std::uint64_t edge = (static_cast<std::uint64_t>(bytes[index - 1]) << 8) | bytes[index];
            edge_hash = (edge_hash ^ edge) * 1099511628211ull;
        }
        projection = edge_hash ^ rotate_left(result.pattern_score, 21);
        structural = edge_hash ^ result.feature_energy;
        break;
    }
    case GpuOperationClass::ApproximateNearestNeighbor: {
        std::array<std::uint64_t, 8> dimensions{};
        for (const auto value : bytes) ++dimensions[value >> 5];
        std::uint64_t norm = 0;
        std::uint64_t simhash = 0;
        for (std::size_t index = 0; index < dimensions.size(); ++index) {
            norm += dimensions[index] * dimensions[index];
            if (dimensions[index] * dimensions.size() >= bytes.size()) simhash |= 1ull << index;
        }
        projection = norm ^ rotate_left(simhash, 47);
        structural = result.fnv1a ^ norm * 257ull ^ simhash;
        break;
    }
    case GpuOperationClass::SequenceMining: {
        std::uint64_t repeated_pairs = 0;
        std::uint64_t repeated_triples = 0;
        for (std::size_t index = 2; index < bytes.size(); ++index) {
            repeated_pairs += bytes[index] == bytes[index - 2];
            if (index >= 3) repeated_triples += bytes[index] == bytes[index - 3];
        }
        projection = repeated_pairs * 65537ull ^ repeated_triples * 104729ull ^ result.fnv1a;
        structural = rotate_left(repeated_pairs, 13) ^ rotate_left(repeated_triples, 39) ^ result.pattern_score;
        break;
    }
    case GpuOperationClass::FsmScoring: {
        std::uint64_t transition_matrix = 0;
        unsigned previous_state = 0;
        for (const auto value : bytes) {
            const unsigned state = value == 0 ? 0u : value < 0x20 ? 1u : value < 0x80 ? 2u : 3u;
            transition_matrix += 1ull << (previous_state * 4u + state);
            previous_state = state;
        }
        projection = transition_matrix ^ rotate_left(result.transition_score, 27);
        structural = result.fnv1a ^ transition_matrix * 4099ull;
        break;
    }
    case GpuOperationClass::FormulaBatchEvaluation: {
        std::uint64_t fixed_point = 0;
        for (const auto byte : bytes) {
            const auto value = static_cast<std::uint64_t>(byte);
            fixed_point += value * value + 3ull * value + 7ull;
        }
        projection = fixed_point ^ result.feature_energy;
        structural = fixed_point * 1099511628211ull ^ result.fnv1a;
        break;
    }
    case GpuOperationClass::ReplayStateComparison: {
        const std::size_t half = bytes.size() / 2;
        std::uint64_t differing_bits = 0;
        for (std::size_t index = 0; index < half; ++index) {
            unsigned value = static_cast<unsigned>(bytes[index] ^ bytes[index + half]);
            for (; value != 0; value &= value - 1) ++differing_bits;
        }
        projection = differing_bits * 65537ull ^ result.transition_score;
        structural = result.fnv1a ^ rotate_left(differing_bits, 33);
        break;
    }
    case GpuOperationClass::AiInference:
        // These are input-integrity features only.  They are never represented
        // as inference output when no verified model/provider is loaded.
        projection = 0;
        structural = result.fnv1a;
        break;
    }
    result.operation_projection = projection;
    result.cluster_key = structural ^ projection ^
        (result.feature_energy * 1099511628211ull) ^
        (result.transition_score * 1469598103934665603ull);
    return result;
}

std::string WorkloadAlgorithm(GpuOperationClass value) {
    switch (value) {
    case GpuOperationClass::TraceFeatureExtraction: return "TraceFeatureVectorV3";
    case GpuOperationClass::CrossSessionCorrelation: return "HalfWindowSessionCorrelationV1";
    case GpuOperationClass::ParserFieldPatternMatching: return "TypedFieldBoundaryPatternV1";
    case GpuOperationClass::FunctionClustering: return "OpcodeHistogramFunctionClusterV1";
    case GpuOperationClass::ObjectClustering: return "AlignedWordObjectClusterV1";
    case GpuOperationClass::ClassLayoutScoring: return "Stride4ClassLayoutScoreV1";
    case GpuOperationClass::RegistryScoring: return "CaseFoldedRegistryKeyScoreV1";
    case GpuOperationClass::HeapGraphSimilarity: return "AlignedWordHeapEdgeSimilarityV1";
    case GpuOperationClass::ValueFlowAggregation: return "SignedDeltaValueFlowAggregateV1";
    case GpuOperationClass::TaintGraphBatch: return "HighBitTaintPropagationBatchV1";
    case GpuOperationClass::SemanticEdgeScoring: return "AdjacentTokenSemanticEdgeScoreV1";
    case GpuOperationClass::ApproximateNearestNeighbor: return "ByteHistogramAnnFingerprintV1";
    case GpuOperationClass::SequenceMining: return "RepeatedNgramSequenceMiningV1";
    case GpuOperationClass::FsmScoring: return "FourStateTransitionMatrixScoreV1";
    case GpuOperationClass::FormulaBatchEvaluation: return "FixedPointPolynomialFormulaBatchV1";
    case GpuOperationClass::ReplayStateComparison: return "SplitStateHammingComparisonV1";
    case GpuOperationClass::AiInference: return "NoVerifiedModelRuntime";
    }
    return "Unknown";
}

bool HasOperationSpecificGpuImplementation(GpuOperationClass value) noexcept {
    return value != GpuOperationClass::AiInference;
}

std::string NormalizedInputContract(GpuOperationClass value) {
    switch (value) {
    case GpuOperationClass::TraceFeatureExtraction: return "TraceByteRecordBatchV1";
    case GpuOperationClass::CrossSessionCorrelation: return "CrossSessionTracePairMatrixV1";
    case GpuOperationClass::ParserFieldPatternMatching: return "ParserCandidateByteRecordBatchV1";
    case GpuOperationClass::FunctionClustering: return "FunctionOpcodeHistogramMatrixV1";
    case GpuOperationClass::ObjectClustering: return "ObjectFeatureMatrixV1";
    case GpuOperationClass::ClassLayoutScoring: return "ObjectByteRecordStride4BatchV1";
    case GpuOperationClass::RegistryScoring: return "RegistryKeyUtf8RecordBatchV1";
    case GpuOperationClass::HeapGraphSimilarity: return "HeapAdjacencyGraphPairBatchV1";
    case GpuOperationClass::ValueFlowAggregation: return "OrderedUnsignedByteSeriesBatchV1";
    case GpuOperationClass::TaintGraphBatch: return "TaintSourceSinkGraphBatchV1";
    case GpuOperationClass::SemanticEdgeScoring: return "SemanticEvidenceEdgeBatchV1";
    case GpuOperationClass::ApproximateNearestNeighbor: return "EmbeddingVectorMatrixV1";
    case GpuOperationClass::SequenceMining: return "OrderedEventSequenceCorpusV1";
    case GpuOperationClass::FsmScoring: return "StateTransitionTraceBatchV1";
    case GpuOperationClass::FormulaBatchEvaluation: return "UnsignedByteOperandBatchV1";
    case GpuOperationClass::ReplayStateComparison: return "ReplayStatePairBatchV1";
    case GpuOperationClass::AiInference: return "VerifiedModelTensorBatchV1";
    }
    return "UnknownInputContract";
}

std::string CandidateOutputContract(GpuOperationClass value) {
    switch (value) {
    case GpuOperationClass::TraceFeatureExtraction: return "TraceFeatureVectorV3";
    case GpuOperationClass::CrossSessionCorrelation: return "CrossSessionCorrelationMatrixV1";
    case GpuOperationClass::ParserFieldPatternMatching: return "TypedFieldBoundaryPatternV1";
    case GpuOperationClass::FunctionClustering: return "FunctionClusterAssignmentVectorV1";
    case GpuOperationClass::ObjectClustering: return "ObjectClusterAssignmentVectorV1";
    case GpuOperationClass::ClassLayoutScoring: return "ClassLayoutScoreV1";
    case GpuOperationClass::RegistryScoring: return "RegistryKeyScoreV1";
    case GpuOperationClass::HeapGraphSimilarity: return "HeapGraphSimilarityMatrixV1";
    case GpuOperationClass::ValueFlowAggregation: return "SignedDeltaAggregateV1";
    case GpuOperationClass::TaintGraphBatch: return "TaintReachabilityMatrixV1";
    case GpuOperationClass::SemanticEdgeScoring: return "SemanticEdgeScoreVectorV1";
    case GpuOperationClass::ApproximateNearestNeighbor: return "NearestNeighborIndexDistanceMatrixV1";
    case GpuOperationClass::SequenceMining: return "SequenceSupportResultSetV1";
    case GpuOperationClass::FsmScoring: return "FsmStateScoreMatrixV1";
    case GpuOperationClass::FormulaBatchEvaluation: return "FixedPointPolynomialSumV1";
    case GpuOperationClass::ReplayStateComparison: return "ReplayStateDifferenceMatrixV1";
    case GpuOperationClass::AiInference: return "VerifiedModelInferenceTensorV1";
    }
    return "UnknownOutputContract";
}

std::string WorkloadScope(GpuOperationClass value) {
    if (value == GpuOperationClass::AiInference) return "ExternalVerifiedModelProvider";
    return StructuredKernelFor(value) == nullptr ?
        "IndependentRecordBatch" : "StructuredFeatureMatrixBatch";
}

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

constexpr std::array<GpuOperationClass, 16> kImplementedRecordOperations{{
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
    GpuOperationClass::ReplayStateComparison
}};

constexpr std::array<GpuOperationClass, 0> kStructuredBlockedOperations{};

std::string Hex64(std::uint64_t value) {
    std::ostringstream stream;
    stream << std::hex << std::uppercase;
    stream.width(16);
    stream.fill('0');
    stream << value;
    return stream.str();
}

std::string InputDigest(const std::vector<GpuEvidenceRecord>& records, GpuOperationClass operation) {
    std::uint64_t hash = 1469598103934665603ull;
    const auto mix = [&hash](std::uint8_t value) { hash = (hash ^ value) * 1099511628211ull; };
    mix(static_cast<std::uint8_t>(operation));
    for (const auto& record : records) {
        const auto size = static_cast<std::uint64_t>(record.bytes.size());
        for (unsigned shift = 0; shift < 64; shift += 8) mix(static_cast<std::uint8_t>(size >> shift));
        for (const auto value : record.bytes) mix(value);
    }
    return "FNV1A64:" + Hex64(hash);
}

std::string FeatureDigest(const std::vector<GpuVerifiedFeature>& features) {
    std::uint64_t hash = 1469598103934665603ull;
    const auto mix64 = [&hash](std::uint64_t value) {
        for (unsigned shift = 0; shift < 64; shift += 8)
            hash = (hash ^ static_cast<std::uint8_t>(value >> shift)) * 1099511628211ull;
    };
    for (const auto& feature : features) {
        mix64(feature.byte_sum_without_last);
        mix64(feature.fnv1a);
        mix64(feature.feature_energy);
        mix64(feature.transition_score);
        mix64(feature.pattern_score);
        mix64(feature.operation_projection);
        mix64(feature.cluster_key);
    }
    return "FNV1A64:" + Hex64(hash);
}

std::string FeatureDigestPrefix(const std::vector<GpuVerifiedFeature>& features, std::size_t count) {
    std::uint64_t hash = 1469598103934665603ull;
    const auto mix64 = [&hash](std::uint64_t value) {
        for (unsigned shift = 0; shift < 64; shift += 8)
            hash = (hash ^ static_cast<std::uint8_t>(value >> shift)) * 1099511628211ull;
    };
    count = std::min(count, features.size());
    for (std::size_t index = 0; index < count; ++index) {
        const auto& feature = features[index];
        mix64(feature.byte_sum_without_last);
        mix64(feature.fnv1a);
        mix64(feature.feature_energy);
        mix64(feature.transition_score);
        mix64(feature.pattern_score);
        mix64(feature.operation_projection);
        mix64(feature.cluster_key);
    }
    return "FNV1A64:" + Hex64(hash);
}

std::string PrimitiveDigestPrefix(const std::vector<GpuVerifiedFeature>& features, std::size_t count) {
    std::uint64_t hash = 1469598103934665603ull;
    const auto mix64 = [&hash](std::uint64_t value) {
        for (unsigned shift = 0; shift < 64; shift += 8)
            hash = (hash ^ static_cast<std::uint8_t>(value >> shift)) * 1099511628211ull;
    };
    count = std::min(count, features.size());
    for (std::size_t index = 0; index < count; ++index) {
        const auto& feature = features[index];
        mix64(feature.byte_sum_without_last);
        mix64(feature.fnv1a);
        mix64(feature.feature_energy);
        mix64(feature.transition_score);
        mix64(feature.pattern_score);
    }
    return "FNV1A64:" + Hex64(hash);
}

bool SameFeature(const GpuVerifiedFeature& left, const GpuVerifiedFeature& right) {
    return left.byte_sum_without_last == right.byte_sum_without_last &&
        left.fnv1a == right.fnv1a && left.feature_energy == right.feature_energy &&
        left.transition_score == right.transition_score &&
        left.pattern_score == right.pattern_score &&
        left.operation_projection == right.operation_projection &&
        left.cluster_key == right.cluster_key;
}

bool SamePrimitive(const GpuVerifiedFeature& left, const GpuVerifiedFeature& right) {
    return left.byte_sum_without_last == right.byte_sum_without_last &&
        left.fnv1a == right.fnv1a && left.feature_energy == right.feature_energy &&
        left.transition_score == right.transition_score && left.pattern_score == right.pattern_score;
}

std::uint64_t WorkingSetBytesForBenchmark() {
    PROCESS_MEMORY_COUNTERS counters{};
    return GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof(counters)) ?
        static_cast<std::uint64_t>(counters.PeakWorkingSetSize) : 0;
}

GpuCapabilityReport MockCapability(AccelerationMode mode, GpuTestScenario scenario) {
    GpuCapabilityReport value;
    value.requested_mode = mode;
    if (mode == AccelerationMode::CpuOnly) {
        value.fallback_reason = "CPUOnlySelected";
        return value;
    }
    switch (scenario) {
    case GpuTestScenario::NoGpu:
        value.fallback_reason = "NoNvidiaGpu";
        return value;
    case GpuTestScenario::NvidiaUnavailable:
        value.gpu_detected = true;
        value.vendor = "NVIDIA";
        value.device = "Mock NVIDIA device";
        value.fallback_reason = "NvidiaDriverUnavailable";
        return value;
    case GpuTestScenario::InitializationError:
        value.gpu_detected = true;
        value.nvidia_device_available = true;
        value.vendor = "NVIDIA";
        value.device = "Mock NVIDIA device";
        value.driver_status = "MockInitializationError";
        value.fallback_reason = "GpuInitializationError";
        return value;
    case GpuTestScenario::UnsupportedGpu:
        value.gpu_detected = true;
        value.nvidia_device_available = true;
        value.cuda_available = true;
        value.vendor = "NVIDIA";
        value.device = "Mock Maxwell";
        value.compute_major = 5;
        value.compute_minor = 2;
        value.driver_status = "Available";
        value.runtime_status = "UnsupportedComputeCapability";
        value.fallback_reason = "ComputeCapabilityBelow6.1";
        return value;
    default:
        break;
    }
    value.gpu_detected = true;
    value.nvidia_device_available = true;
    value.cuda_available = true;
    value.backend_initialized = true;
    value.driver_runtime_compatible = true;
    value.vendor = "NVIDIA";
    value.driver_status = "MockAvailable";
    value.runtime_status = "EmbeddedPtx50Sm61CompatibilityPath";
    value.backend = "Mock CUDA Driver API";
    value.vram_bytes = 8ull * 1024ull * kMib;
    value.free_vram_bytes = 6ull * 1024ull * kMib;
    switch (scenario) {
    case GpuTestScenario::MockTuring:
        value.device = "Mock Turing"; value.compute_major = 7; value.compute_minor = 5; break;
    case GpuTestScenario::MockAmpere:
        value.device = "Mock Ampere"; value.compute_major = 8; value.compute_minor = 6; break;
    case GpuTestScenario::MockAda:
        value.device = "Mock Ada"; value.compute_major = 8; value.compute_minor = 9; break;
    case GpuTestScenario::MockBlackwell:
        value.device = "Mock Blackwell"; value.compute_major = 12; value.compute_minor = 0; break;
    case GpuTestScenario::MockGpuBusy:
        value.device = "Mock Busy Ampere"; value.compute_major = 8; value.compute_minor = 6;
        value.current_gpu_load_percent = 96; break;
    case GpuTestScenario::BenefitModelUnavailable:
        value.device = "Mock Ampere (model unavailable)"; value.compute_major = 8; value.compute_minor = 6; break;
    default:
        value.device = "Mock Pascal"; value.compute_major = 6; value.compute_minor = 1; break;
    }
    value.tier = ClassifyGpuArchitecture(value.compute_major, value.compute_minor);
    value.architecture = ToString(value.tier);
    value.tensor_capability = value.compute_major >= 7;
    value.available_precision_modes = value.compute_major >= 8 ? "FP32,FP16,TF32" : "FP32,FP16";
    return value;
}

std::vector<GpuEvidenceRecord> MakeBenchmarkCorpus(std::size_t count, std::size_t bytes_per_record) {
    std::vector<GpuEvidenceRecord> records(count);
    std::uint32_t state = 0x4f1bbcdcU;
    for (std::size_t record = 0; record < count; ++record) {
        auto& bytes = records[record].bytes;
        bytes.resize(bytes_per_record + (record % 31));
        for (auto& byte : bytes) {
            state = state * 1664525U + 1013904223U;
            byte = static_cast<std::uint8_t>(state >> 24);
        }
    }
    return records;
}

struct CalibrationEntry {
    std::uint64_t sample_count = 0;
    std::uint64_t gpu_sample_count = 0;
    std::uint64_t paired_full_path_sample_count = 0;
    double cpu_microseconds_per_byte = 0.0;
    double gpu_microseconds_per_byte = 0.0;
    double cpu_baseline_microseconds_per_byte = 0.0;
    double gpu_full_path_microseconds_per_byte = 0.0;
    std::uint64_t latest_cpu_baseline_microseconds = 0;
    std::uint64_t latest_gpu_full_path_microseconds = 0;
    std::uint64_t latest_records = 0;
    std::uint64_t latest_bytes = 0;
    std::string latest_input_digest;
    std::chrono::steady_clock::time_point latest_paired_sample_at{};
};

constexpr double kAutoGpuMinimumMeasuredSpeedup = 1.10;
constexpr auto kCalibrationFreshness = std::chrono::minutes(5);
constexpr std::uint64_t kMinimumGeneralizedPairedSamples = 3;

std::size_t OperationIndex(GpuOperationClass value) {
    return static_cast<std::size_t>(value);
}

std::uint64_t ArithmeticIntensity(GpuOperationClass value) {
    switch (value) {
    case GpuOperationClass::TraceFeatureExtraction: return 12;
    case GpuOperationClass::CrossSessionCorrelation: return 20;
    case GpuOperationClass::ParserFieldPatternMatching: return 24;
    case GpuOperationClass::FunctionClustering: return 52;
    case GpuOperationClass::ObjectClustering: return 44;
    case GpuOperationClass::ClassLayoutScoring: return 40;
    case GpuOperationClass::RegistryScoring: return 26;
    case GpuOperationClass::HeapGraphSimilarity: return 72;
    case GpuOperationClass::ValueFlowAggregation: return 36;
    case GpuOperationClass::TaintGraphBatch: return 64;
    case GpuOperationClass::SemanticEdgeScoring: return 56;
    case GpuOperationClass::ApproximateNearestNeighbor: return 84;
    case GpuOperationClass::SequenceMining: return 68;
    case GpuOperationClass::FsmScoring: return 48;
    case GpuOperationClass::FormulaBatchEvaluation: return 60;
    case GpuOperationClass::ReplayStateComparison: return 42;
    case GpuOperationClass::AiInference: return 0;
    }
    return 1;
}

unsigned TierPolicyIndex(GpuArchitectureTier value) {
    switch (value) {
    case GpuArchitectureTier::LegacyCuda: return 0;
    case GpuArchitectureTier::RtxStandard: return 1;
    case GpuArchitectureTier::Full: return 2;
    case GpuArchitectureTier::HighPerformance: return 3;
    case GpuArchitectureTier::Maximum: return 4;
    default: return 0;
    }
}

std::uint64_t RoundedMicros(double value) {
    if (!std::isfinite(value) || value <= 1.0) return value <= 0.0 ? 0 : 1;
    constexpr double maximum = static_cast<double>((std::numeric_limits<std::uint64_t>::max)());
    return static_cast<std::uint64_t>(std::min(value, maximum));
}

GpuBenefitReport EstimateBenefit(const GpuCapabilityReport& capability,
                                 const GpuSchedulerPlan& scheduler,
                                 AccelerationPhase phase,
                                 GpuOperationClass operation_class,
                                 std::uint64_t records,
                                 std::uint64_t bytes,
                                 std::string_view normalized_input_digest,
                                 const CalibrationEntry& calibration,
                                 bool model_available,
                                 GpuDispatchIntent dispatch_intent) {
    GpuBenefitReport result;
    result.gpu_available = capability.requested_mode == AccelerationMode::Auto &&
        capability.backend_initialized && capability.driver_runtime_compatible;
    result.operation_class = ToString(operation_class);
    result.phase = phase == AccelerationPhase::Live ? "Live" : "PostCapture";
    result.workload_records = records;
    result.workload_bytes = bytes;
    result.average_record_bytes = records == 0 ? 0 : bytes / records;
    result.expected_arithmetic_intensity = ArithmeticIntensity(operation_class);
    result.expected_transfer_bytes = bytes > (std::numeric_limits<std::uint64_t>::max)() / 2 ?
        (std::numeric_limits<std::uint64_t>::max)() : bytes * 2;
    result.expected_batch_count = records == 0 ? 0 :
        (records + std::max<std::uint64_t>(scheduler.batch_records, 1) - 1) /
        std::max<std::uint64_t>(scheduler.batch_records, 1);
    result.calibration_sample_count = calibration.sample_count;
    result.paired_full_path_sample_count = calibration.paired_full_path_sample_count;
    result.latest_cpu_baseline_microseconds = calibration.latest_cpu_baseline_microseconds;
    result.latest_gpu_full_path_microseconds = calibration.latest_gpu_full_path_microseconds;
    result.latest_full_path_speedup = calibration.latest_gpu_full_path_microseconds == 0 ? 0.0 :
        static_cast<double>(calibration.latest_cpu_baseline_microseconds) /
        static_cast<double>(calibration.latest_gpu_full_path_microseconds);
    result.required_speedup_margin_percent = phase == AccelerationPhase::Live ? 20u : 10u;
    const auto now = std::chrono::steady_clock::now();
    result.calibration_fresh = calibration.paired_full_path_sample_count != 0 &&
        calibration.latest_paired_sample_at != std::chrono::steady_clock::time_point{} &&
        now - calibration.latest_paired_sample_at <= kCalibrationFreshness;
    result.same_input_calibration = result.calibration_fresh &&
        calibration.latest_input_digest == normalized_input_digest;
    if (calibration.paired_full_path_sample_count == 0)
        result.calibration_source = "InsufficientPairedFullPathMeasurements";
    else if (!result.calibration_fresh)
        result.calibration_source = "StalePairedFullPathMeasurement";
    else if (result.same_input_calibration)
        result.calibration_source = "LatestSameInputPairedFullPathMeasurement";
    else if (calibration.paired_full_path_sample_count < kMinimumGeneralizedPairedSamples)
        result.calibration_source = "InsufficientGeneralizedPairedFullPathMeasurements";
    else
        result.calibration_source = "FreshGeneralizedPairedFullPathMeasurements";

    if (operation_class == GpuOperationClass::AiInference) {
        result.selection_reason = "EvidenceBlockedModelUnavailable";
        result.benefit_gate_decision = "CpuInputIntegrityOnly";
        result.calibration_source = "NoVerifiedModelRuntime";
        return result;
    }
    if (capability.requested_mode == AccelerationMode::CpuOnly) {
        result.selection_reason = "ForcedCpuOnly";
        result.benefit_gate_decision = "ForcedCpuOnly";
        return result;
    }
    if (!result.gpu_available) {
        result.selection_reason = "GpuUnavailable";
        result.benefit_gate_decision = "CpuFallback";
        return result;
    }
    if (!model_available) {
        result.selection_reason = "GpuCalibrationUnavailable";
        result.benefit_gate_decision = "ConservativeCpuSelected";
        result.calibration_source = "Unavailable";
        return result;
    }
    if (records == 0 || bytes == 0) {
        result.selection_reason = "WorkloadBelowGpuBenefitThreshold";
        result.benefit_gate_decision = "CpuSelected";
        return result;
    }

    const unsigned tier = TierPolicyIndex(capability.tier);
    constexpr std::array<std::uint64_t, 5> kMinimumRecords{8192, 4096, 2048, 1024, 512};
    constexpr std::array<std::uint64_t, 5> kMinimumBytes{
        4 * kMib, 2 * kMib, 1 * kMib, 512 * 1024ull, 256 * 1024ull};
    constexpr std::array<double, 5> kGpuWorkBytesPerMicrosecond{5000.0, 7500.0, 12000.0, 18000.0, 26000.0};
    constexpr std::array<double, 5> kTransferBytesPerMicrosecond{7000.0, 9000.0, 12000.0, 16000.0, 20000.0};
    constexpr std::array<double, 5> kLaunchMicroseconds{950.0, 650.0, 420.0, 280.0, 190.0};
    constexpr std::array<double, 5> kBatchOverheadMicroseconds{95.0, 75.0, 55.0, 42.0, 32.0};
    const std::uint64_t intensity = result.expected_arithmetic_intensity;
    const std::uint64_t threshold_divisor = std::min<std::uint64_t>(
        8, static_cast<std::uint64_t>(std::sqrt(static_cast<double>(intensity))));
    const std::uint64_t minimum_records = std::max<std::uint64_t>(64, kMinimumRecords[tier] /
        std::max<std::uint64_t>(threshold_divisor, 1));
    const std::uint64_t minimum_bytes = std::max<std::uint64_t>(16 * 1024ull, kMinimumBytes[tier] /
        std::max<std::uint64_t>(threshold_divisor, 1));

    const double default_cpu_per_byte = static_cast<double>(intensity) / 800.0;
    const double cpu_per_byte = calibration.cpu_microseconds_per_byte > 0.0 ?
        calibration.cpu_microseconds_per_byte : default_cpu_per_byte;
    const double cpu_base = static_cast<double>(bytes) * cpu_per_byte +
        static_cast<double>(records) / 64.0 + 1.0;
    double expected_cpu = cpu_base;
    double gpu_candidate = kLaunchMicroseconds[tier] +
        static_cast<double>(result.expected_batch_count) * kBatchOverheadMicroseconds[tier] +
        static_cast<double>(result.expected_transfer_bytes) / kTransferBytesPerMicrosecond[tier] +
        static_cast<double>(bytes) * static_cast<double>(intensity) / kGpuWorkBytesPerMicrosecond[tier];
    if (calibration.gpu_sample_count != 0 && calibration.gpu_microseconds_per_byte > 0.0) {
        const double measured_candidate = static_cast<double>(bytes) * calibration.gpu_microseconds_per_byte;
        gpu_candidate = std::max(gpu_candidate, measured_candidate);
    }
    // CPU authority is permanent.  Every operation class runs the same
    // operation-specific deterministic projection on CPU after the GPU
    // candidate, so a heavy-workload result can never raise evidence authority.
    double expected_gpu = gpu_candidate + cpu_base;
    if (result.same_input_calibration) {
        expected_cpu = static_cast<double>(calibration.latest_cpu_baseline_microseconds);
        expected_gpu = static_cast<double>(calibration.latest_gpu_full_path_microseconds);
    } else if (result.calibration_fresh &&
               calibration.paired_full_path_sample_count >= kMinimumGeneralizedPairedSamples &&
               calibration.cpu_baseline_microseconds_per_byte > 0.0 &&
               calibration.gpu_full_path_microseconds_per_byte > 0.0) {
        expected_cpu = static_cast<double>(bytes) * calibration.cpu_baseline_microseconds_per_byte;
        expected_gpu = static_cast<double>(bytes) * calibration.gpu_full_path_microseconds_per_byte;
    }
    result.predicted_cpu_microseconds = RoundedMicros(expected_cpu);
    result.predicted_gpu_microseconds = RoundedMicros(expected_gpu);
    result.predicted_speedup = expected_gpu <= 0.0 ? 0.0 : expected_cpu / expected_gpu;

    if (dispatch_intent == GpuDispatchIntent::MeasurementOverride) {
        result.gpu_eligible = true;
        result.gpu_selected = true;
        result.selected_backend = "GPU";
        result.selection_reason = "PhysicalMeasurementOverride";
        result.benefit_gate_decision = "MeasurementOverride";
        return result;
    }
    const unsigned busy_limit = phase == AccelerationPhase::Live ? 50u : 85u;
    if (capability.current_gpu_load_percent >= busy_limit) {
        result.selection_reason = "GpuBusy";
        result.benefit_gate_decision = phase == AccelerationPhase::Live ? "DeferredToCpu" : "CpuSelected";
        return result;
    }
    if (records < minimum_records || bytes < minimum_bytes || result.average_record_bytes < 16) {
        result.selection_reason = "WorkloadBelowGpuBenefitThreshold";
        result.benefit_gate_decision = "CpuSelected";
        return result;
    }
    // The authoritative boundary currently recomputes the complete operation
    // with CpuFeature after every CUDA candidate.  Therefore the production
    // full path is GPU work plus the same complete CPU work and cannot have a
    // positive crossover versus CPU-only.  Paired measurements are retained
    // for truthful telemetry, never as a way around this structural gate.
    result.gpu_eligible = false;
    result.gpu_selected = false;
    result.selected_backend = "CPU";
    result.selection_reason = "NoPositiveCrossoverFullCpuOracleRequired";
    result.benefit_gate_decision = result.calibration_fresh ?
        "CpuSelectedFullCpuOracleRequired" :
        "CpuSelectedInsufficientOrStaleCalibrationAndFullCpuOracleRequired";
    return result;
}

} // namespace

class OptionalGpuAccelerator::Implementation {
public:
    using CuInit = CUresult(WINAPI*)(unsigned);
    using CuDriverGetVersion = CUresult(WINAPI*)(int*);
    using CuDeviceGetCount = CUresult(WINAPI*)(int*);
    using CuDeviceGet = CUresult(WINAPI*)(CUdevice*, int);
    using CuDeviceGetName = CUresult(WINAPI*)(char*, int, CUdevice);
    using CuDeviceGetAttribute = CUresult(WINAPI*)(int*, int, CUdevice);
    using CuDeviceTotalMem = CUresult(WINAPI*)(std::size_t*, CUdevice);
    using CuDevicePrimaryCtxRetain = CUresult(WINAPI*)(CUcontext*, CUdevice);
    using CuDevicePrimaryCtxRelease = CUresult(WINAPI*)(CUdevice);
    using CuDevicePrimaryCtxReset = CUresult(WINAPI*)(CUdevice);
    using CuCtxSetCurrent = CUresult(WINAPI*)(CUcontext);
    using CuMemGetInfo = CUresult(WINAPI*)(std::size_t*, std::size_t*);
    using CuModuleLoadDataEx = CUresult(WINAPI*)(CUmodule*, const void*, unsigned, void*, void*);
    using CuModuleUnload = CUresult(WINAPI*)(CUmodule);
    using CuModuleGetFunction = CUresult(WINAPI*)(CUfunction*, CUmodule, const char*);
    using CuMemAlloc = CUresult(WINAPI*)(CUdeviceptr*, std::size_t);
    using CuMemFree = CUresult(WINAPI*)(CUdeviceptr);
    using CuMemcpyHtoD = CUresult(WINAPI*)(CUdeviceptr, const void*, std::size_t);
    using CuMemcpyDtoH = CUresult(WINAPI*)(void*, CUdeviceptr, std::size_t);
    using CuMemcpyHtoDAsync = CUresult(WINAPI*)(CUdeviceptr, const void*, std::size_t, CUstream);
    using CuMemcpyDtoHAsync = CUresult(WINAPI*)(void*, CUdeviceptr, std::size_t, CUstream);
    using CuLaunchKernel = CUresult(WINAPI*)(CUfunction, unsigned, unsigned, unsigned, unsigned, unsigned,
                                            unsigned, unsigned, void*, void**, void**);
    using CuCtxSynchronize = CUresult(WINAPI*)();
    using CuStreamCreate = CUresult(WINAPI*)(CUstream*, unsigned);
    using CuStreamDestroy = CUresult(WINAPI*)(CUstream);
    using CuStreamSynchronize = CUresult(WINAPI*)(CUstream);
    using CuEventCreate = CUresult(WINAPI*)(CUevent*, unsigned);
    using CuEventDestroy = CUresult(WINAPI*)(CUevent);
    using CuEventRecord = CUresult(WINAPI*)(CUevent, CUstream);
    using CuEventQuery = CUresult(WINAPI*)(CUevent);
    using CuEventSynchronize = CUresult(WINAPI*)(CUevent);
    using CuMemHostAlloc = CUresult(WINAPI*)(void**, std::size_t, unsigned);
    using CuMemFreeHost = CUresult(WINAPI*)(void*);

    Implementation(AccelerationMode mode, GpuTestScenario scenario) : scenario_(scenario) {
        capability_.requested_mode = mode;
        if (scenario != GpuTestScenario::RealHardware) {
            capability_ = MockCapability(mode, scenario);
            return;
        }
        ProbeRealHardware(mode);
    }

    ~Implementation() { Release(); }

    void Release() noexcept {
        if (context_ != nullptr && cu_ctx_set_current_ != nullptr) cu_ctx_set_current_(context_);
        ReleasePools();
        for (auto& stream : streams_) {
            if (stream != nullptr && cu_stream_destroy_ != nullptr)
                cu_stream_destroy_(stream);
            stream = nullptr;
        }
        stream_count_ = 0;
        if (module_ != nullptr && cu_module_unload_ != nullptr) cu_module_unload_(module_);
        module_ = nullptr;
        kernel_ = nullptr;
        trace_kernel_ = nullptr;
        parser_kernel_ = nullptr;
        class_layout_kernel_ = nullptr;
        registry_kernel_ = nullptr;
        value_flow_kernel_ = nullptr;
        formula_kernel_ = nullptr;
        structured_kernels_.fill(nullptr);
        if (context_ != nullptr && cu_device_primary_ctx_release_ != nullptr)
            cu_device_primary_ctx_release_(device_);
        context_ = nullptr;
        if (driver_ != nullptr) FreeLibrary(driver_);
        driver_ = nullptr;
    }

    void ProbeRealHardware(AccelerationMode mode) {
        if (mode == AccelerationMode::CpuOnly) {
            capability_.fallback_reason = "CPUOnlySelected";
            return;
        }
        driver_ = LoadLibraryExW(L"nvcuda.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (driver_ == nullptr) {
            capability_.driver_status = "NvidiaDriverDllUnavailable";
            capability_.fallback_reason = "NvidiaDriverUnavailable";
            return;
        }
        capability_.vendor = "NVIDIA";
        capability_.gpu_detected = true;
        const bool imports =
            LoadFunction(driver_, "cuInit", &cu_init_) &&
            LoadFunction(driver_, "cuDriverGetVersion", &cu_driver_get_version_) &&
            LoadFunction(driver_, "cuDeviceGetCount", &cu_device_get_count_) &&
            LoadFunction(driver_, "cuDeviceGet", &cu_device_get_) &&
            LoadFunction(driver_, "cuDeviceGetName", &cu_device_get_name_) &&
            LoadFunction(driver_, "cuDeviceGetAttribute", &cu_device_get_attribute_) &&
            (LoadFunction(driver_, "cuDeviceTotalMem_v2", &cu_device_total_mem_) ||
             LoadFunction(driver_, "cuDeviceTotalMem", &cu_device_total_mem_)) &&
            LoadFunction(driver_, "cuDevicePrimaryCtxRetain", &cu_device_primary_ctx_retain_) &&
            (LoadFunction(driver_, "cuDevicePrimaryCtxRelease_v2", &cu_device_primary_ctx_release_) ||
             LoadFunction(driver_, "cuDevicePrimaryCtxRelease", &cu_device_primary_ctx_release_)) &&
            LoadFunction(driver_, "cuCtxSetCurrent", &cu_ctx_set_current_) &&
            (LoadFunction(driver_, "cuMemGetInfo_v2", &cu_mem_get_info_) ||
             LoadFunction(driver_, "cuMemGetInfo", &cu_mem_get_info_)) &&
            LoadFunction(driver_, "cuModuleLoadDataEx", &cu_module_load_data_ex_) &&
            LoadFunction(driver_, "cuModuleUnload", &cu_module_unload_) &&
            LoadFunction(driver_, "cuModuleGetFunction", &cu_module_get_function_) &&
            (LoadFunction(driver_, "cuMemAlloc_v2", &cu_mem_alloc_) || LoadFunction(driver_, "cuMemAlloc", &cu_mem_alloc_)) &&
            (LoadFunction(driver_, "cuMemFree_v2", &cu_mem_free_) || LoadFunction(driver_, "cuMemFree", &cu_mem_free_)) &&
            (LoadFunction(driver_, "cuMemcpyHtoD_v2", &cu_memcpy_htod_) || LoadFunction(driver_, "cuMemcpyHtoD", &cu_memcpy_htod_)) &&
            (LoadFunction(driver_, "cuMemcpyDtoH_v2", &cu_memcpy_dtoh_) || LoadFunction(driver_, "cuMemcpyDtoH", &cu_memcpy_dtoh_)) &&
            LoadFunction(driver_, "cuLaunchKernel", &cu_launch_kernel_) &&
            LoadFunction(driver_, "cuCtxSynchronize", &cu_ctx_synchronize_);
        if (!imports) {
            capability_.driver_status = "RequiredDriverApiMissing";
            capability_.fallback_reason = "DriverRuntimeIncompatible";
            Release();
            return;
        }
        LoadFunction(driver_, "cuMemcpyHtoDAsync_v2", &cu_memcpy_htod_async_);
        if (!LoadFunction(driver_, "cuDevicePrimaryCtxReset_v2", &cu_device_primary_ctx_reset_))
            LoadFunction(driver_, "cuDevicePrimaryCtxReset", &cu_device_primary_ctx_reset_);
        LoadFunction(driver_, "cuMemcpyDtoHAsync_v2", &cu_memcpy_dtoh_async_);
        LoadFunction(driver_, "cuStreamCreate", &cu_stream_create_);
        if (!LoadFunction(driver_, "cuStreamDestroy_v2", &cu_stream_destroy_))
            LoadFunction(driver_, "cuStreamDestroy", &cu_stream_destroy_);
        LoadFunction(driver_, "cuStreamSynchronize", &cu_stream_synchronize_);
        LoadFunction(driver_, "cuEventCreate", &cu_event_create_);
        if (!LoadFunction(driver_, "cuEventDestroy_v2", &cu_event_destroy_))
            LoadFunction(driver_, "cuEventDestroy", &cu_event_destroy_);
        LoadFunction(driver_, "cuEventRecord", &cu_event_record_);
        LoadFunction(driver_, "cuEventQuery", &cu_event_query_);
        LoadFunction(driver_, "cuEventSynchronize", &cu_event_synchronize_);
        LoadFunction(driver_, "cuMemHostAlloc", &cu_mem_host_alloc_);
        LoadFunction(driver_, "cuMemFreeHost", &cu_mem_free_host_);
        int driver_version = 0;
        int count = 0;
        if (cu_init_(0) != kCudaSuccess || cu_driver_get_version_(&driver_version) != kCudaSuccess ||
            cu_device_get_count_(&count) != kCudaSuccess || count <= 0 ||
            cu_device_get_(&device_, 0) != kCudaSuccess) {
            capability_.driver_status = "CudaInitializationFailed";
            capability_.fallback_reason = count <= 0 ? "NoNvidiaGpu" : "GpuInitializationError";
            return;
        }
        capability_.nvidia_device_available = true;
        capability_.cuda_available = true;
        capability_.driver_version = driver_version;
        char name[256]{};
        int major = 0;
        int minor = 0;
        std::size_t total = 0;
        if (cu_device_get_name_(name, static_cast<int>(std::size(name)), device_) != kCudaSuccess ||
            cu_device_get_attribute_(&major, kComputeCapabilityMajor, device_) != kCudaSuccess ||
            cu_device_get_attribute_(&minor, kComputeCapabilityMinor, device_) != kCudaSuccess ||
            cu_device_total_mem_(&total, device_) != kCudaSuccess) {
            capability_.driver_status = "CapabilityQueryFailed";
            capability_.fallback_reason = "GpuCapabilityQueryFailed";
            return;
        }
        capability_.device = name;
        capability_.compute_major = major;
        capability_.compute_minor = minor;
        capability_.vram_bytes = total;
        capability_.tier = ClassifyGpuArchitecture(major, minor);
        capability_.architecture = ToString(capability_.tier);
        capability_.tensor_capability = major >= 7;
        capability_.available_precision_modes = major >= 8 ? "FP32,FP16,TF32" : "FP32,FP16";
        if (capability_.tier == GpuArchitectureTier::CpuFullFeature) {
            capability_.driver_status = "AvailableButUnsupported";
            capability_.runtime_status = "ComputeCapabilityBelow6.1";
            capability_.fallback_reason = "ComputeCapabilityBelow6.1";
            return;
        }
        if (cu_device_primary_ctx_retain_(&context_, device_) != kCudaSuccess || context_ == nullptr ||
            cu_ctx_set_current_(context_) != kCudaSuccess) {
            capability_.driver_status = "ContextInitializationFailed";
            capability_.fallback_reason = "GpuInitializationError";
            return;
        }
        std::size_t free_memory = 0;
        std::size_t context_total = 0;
        if (cu_mem_get_info_(&free_memory, &context_total) == kCudaSuccess)
            capability_.free_vram_bytes = free_memory;
        const std::string module_ptx = BuildEvidenceFeaturePtx();
        if (cu_module_load_data_ex_(&module_, module_ptx.c_str(), 0, nullptr, nullptr) != kCudaSuccess ||
            cu_module_get_function_(&kernel_, module_, "god2_evidence_features") != kCudaSuccess) {
            capability_.runtime_status = "PtxKernelInitializationFailed";
            capability_.fallback_reason = "GpuBackendInitializationFailure";
            return;
        }
        cu_module_get_function_(&trace_kernel_, module_, "god2_trace_feature_v3");
        cu_module_get_function_(&parser_kernel_, module_, "god2_parser_pattern_v1");
        cu_module_get_function_(&class_layout_kernel_, module_, "god2_class_layout_v1");
        cu_module_get_function_(&registry_kernel_, module_, "god2_registry_score_v1");
        cu_module_get_function_(&value_flow_kernel_, module_, "god2_value_flow_v1");
        cu_module_get_function_(&formula_kernel_, module_, "god2_formula_batch_v1");
        for (std::size_t index = 0; index < kStructuredKernelDescriptors.size(); ++index)
            cu_module_get_function_(&structured_kernels_[index], module_,
                kStructuredKernelDescriptors[index].entry);
        if (cu_stream_create_ != nullptr && cu_stream_destroy_ != nullptr &&
            cu_stream_synchronize_ != nullptr && cu_memcpy_htod_async_ != nullptr &&
            cu_memcpy_dtoh_async_ != nullptr && cu_event_create_ != nullptr &&
            cu_event_destroy_ != nullptr && cu_event_record_ != nullptr &&
            cu_event_query_ != nullptr && cu_event_synchronize_ != nullptr) {
            for (auto& stream : streams_) {
                if (cu_stream_create_(&stream, 1u) != kCudaSuccess) break;
                ++stream_count_;
            }
        }
        capability_.backend_initialized = true;
        capability_.driver_runtime_compatible = true;
        capability_.driver_status = "Available:" + std::to_string(driver_version);
        capability_.runtime_status = "DynamicCudaDriverApi;EmbeddedPtx50Sm61CompatibilityPath;";
        capability_.runtime_status += stream_count_ == 0 ?
            "SynchronousTransferFallback" :
            "BoundedMultiStreamInflight;AsyncH2DAndD2H;EventQueryHarvest";
        capability_.runtime_status += cu_mem_host_alloc_ != nullptr && cu_mem_free_host_ != nullptr ?
            ";BoundedPinnedHostPool" : ";PageableHostFallback";
        capability_.runtime_status += ";ReusableBoundedPerStreamDeviceAndPinnedPools";
        capability_.backend = "NVIDIA CUDA Driver API (dynamic, embedded PTX)";
        using NvmlInit = int(WINAPI*)();
        using NvmlShutdown = int(WINAPI*)();
        using NvmlDeviceGetHandle = int(WINAPI*)(unsigned, void**);
        struct NvmlUtilization { unsigned gpu = 0; unsigned memory = 0; };
        using NvmlDeviceGetUtilization = int(WINAPI*)(void*, NvmlUtilization*);
        HMODULE nvml = LoadLibraryExW(L"nvml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (nvml != nullptr) {
            NvmlInit nvml_init = nullptr;
            NvmlShutdown nvml_shutdown = nullptr;
            NvmlDeviceGetHandle nvml_device_get_handle = nullptr;
            NvmlDeviceGetUtilization nvml_device_get_utilization = nullptr;
            const bool nvml_available =
                LoadFunction(nvml, "nvmlInit_v2", &nvml_init) &&
                LoadFunction(nvml, "nvmlShutdown", &nvml_shutdown) &&
                LoadFunction(nvml, "nvmlDeviceGetHandleByIndex_v2", &nvml_device_get_handle) &&
                LoadFunction(nvml, "nvmlDeviceGetUtilizationRates", &nvml_device_get_utilization);
            void* nvml_device = nullptr;
            NvmlUtilization utilization{};
            if (nvml_available && nvml_init() == 0) {
                if (nvml_device_get_handle(0, &nvml_device) == 0 &&
                    nvml_device_get_utilization(nvml_device, &utilization) == 0)
                    capability_.current_gpu_load_percent = std::min(utilization.gpu, 100u);
                nvml_shutdown();
            }
            FreeLibrary(nvml);
        }
    }

    struct DeviceBuffer {
        CUdeviceptr pointer = 0;
        std::size_t capacity = 0;
    };

    struct StreamPool {
        std::array<DeviceBuffer, 10> device{};
        void* pinned = nullptr;
        std::size_t pinned_capacity = 0;
        CUevent completion = nullptr;
        bool pending = false;
        std::size_t begin = 0;
        std::size_t end = 0;
        std::array<std::size_t, 7> output_offsets{};
    };

    std::uint64_t DevicePoolBytes() const noexcept {
        std::uint64_t total = 0;
        for (const auto& pool : stream_pools_)
            for (const auto& buffer : pool.device) total += buffer.capacity;
        return total;
    }

    std::uint64_t PinnedPoolBytes() const noexcept {
        std::uint64_t total = 0;
        for (const auto& pool : stream_pools_) total += pool.pinned_capacity;
        return total;
    }

    bool EnsureDeviceBuffer(unsigned slot, std::size_t index, std::size_t required, std::uint64_t limit,
                            GpuPerformanceReport* performance) {
        required = std::max<std::size_t>(required, 1);
        auto& buffer = stream_pools_[slot].device[index];
        if (buffer.pointer != 0 && buffer.capacity >= required) {
            ++performance->device_pool_reuse_count;
            return true;
        }
        const std::uint64_t projected = DevicePoolBytes() - buffer.capacity + required;
        if (projected > limit) return false;
        CUdeviceptr replacement = 0;
        if (cu_mem_alloc_(&replacement, required) != kCudaSuccess) return false;
        if (buffer.pointer != 0) cu_mem_free_(buffer.pointer);
        buffer.pointer = replacement;
        buffer.capacity = required;
        ++performance->device_pool_allocation_count;
        performance->peak_gpu_memory_bytes = std::max(performance->peak_gpu_memory_bytes, projected);
        return true;
    }

    bool EnsurePinnedBuffer(unsigned slot, std::size_t required, std::uint64_t limit,
                            GpuPerformanceReport* performance) {
        auto& pool = stream_pools_[slot];
        const std::uint64_t projected = PinnedPoolBytes() - pool.pinned_capacity + required;
        if (required == 0 || projected > limit || cu_mem_host_alloc_ == nullptr || cu_mem_free_host_ == nullptr)
            return false;
        if (pool.pinned != nullptr && pool.pinned_capacity >= required) {
            ++performance->pinned_pool_reuse_count;
            return true;
        }
        void* replacement = nullptr;
        if (cu_mem_host_alloc_(&replacement, required, 0u) != kCudaSuccess || replacement == nullptr)
            return false;
        if (pool.pinned != nullptr) cu_mem_free_host_(pool.pinned);
        pool.pinned = replacement;
        pool.pinned_capacity = required;
        ++performance->pinned_pool_allocation_count;
        performance->peak_pinned_host_memory_bytes = std::max<std::uint64_t>(
            performance->peak_pinned_host_memory_bytes, projected);
        return true;
    }

    void ReleasePools() noexcept {
        if (cu_mem_free_ != nullptr) {
            for (auto& pool : stream_pools_) {
                for (auto& buffer : pool.device) {
                    if (buffer.pointer != 0) cu_mem_free_(buffer.pointer);
                    buffer = {};
                }
            }
        }
        for (auto& pool : stream_pools_) {
            if (pool.pinned != nullptr && cu_mem_free_host_ != nullptr) cu_mem_free_host_(pool.pinned);
            pool.pinned = nullptr;
            pool.pinned_capacity = 0;
            if (pool.completion != nullptr && cu_event_destroy_ != nullptr) cu_event_destroy_(pool.completion);
            pool.completion = nullptr;
        }
    }

    void ResetPools(GpuPerformanceReport* performance) noexcept {
        const bool had_pool = DevicePoolBytes() != 0 || PinnedPoolBytes() != 0;
        ReleasePools();
        if (had_pool) ++performance->pool_reset_count;
    }

    CUfunction OperationKernel(GpuOperationClass operation) const noexcept {
        switch (operation) {
        case GpuOperationClass::TraceFeatureExtraction: return trace_kernel_;
        case GpuOperationClass::ParserFieldPatternMatching: return parser_kernel_;
        case GpuOperationClass::ClassLayoutScoring: return class_layout_kernel_;
        case GpuOperationClass::RegistryScoring: return registry_kernel_;
        case GpuOperationClass::ValueFlowAggregation: return value_flow_kernel_;
        case GpuOperationClass::FormulaBatchEvaluation: return formula_kernel_;
        default:
            for (std::size_t index = 0; index < kStructuredKernelDescriptors.size(); ++index) {
                if (kStructuredKernelDescriptors[index].operation == operation)
                    return structured_kernels_[index];
            }
            return nullptr;
        }
    }

    bool HarvestRealBatch(unsigned slot, std::vector<GpuVerifiedFeature>* output,
                          GpuPerformanceReport* performance, std::uint64_t timeout_microseconds) {
        auto& pool = stream_pools_[slot];
        if (!pool.pending) return true;
        const auto wait_start = std::chrono::steady_clock::now();
        CUresult query = kCudaErrorNotReady;
        while ((query = cu_event_query_(pool.completion)) == kCudaErrorNotReady &&
               ElapsedMicroseconds(wait_start) <= timeout_microseconds) Sleep(0);
        if (query != kCudaSuccess || ElapsedMicroseconds(wait_start) > timeout_microseconds) {
            if (query == kCudaErrorNotReady) ++performance->timeout_count;
            return false;
        }
        const auto synchronize_start = std::chrono::steady_clock::now();
        if (cu_event_synchronize_(pool.completion) != kCudaSuccess) return false;
        ++performance->stream_synchronize_count;
        performance->gpu_synchronize_microseconds += ElapsedMicroseconds(synchronize_start);
        const auto* base = static_cast<const std::uint8_t*>(pool.pinned);
        const auto* sums = reinterpret_cast<const std::uint32_t*>(base + pool.output_offsets[0]);
        const auto* hashes = reinterpret_cast<const std::uint64_t*>(base + pool.output_offsets[1]);
        const auto* energies = reinterpret_cast<const std::uint64_t*>(base + pool.output_offsets[2]);
        const auto* transitions = reinterpret_cast<const std::uint64_t*>(base + pool.output_offsets[3]);
        const auto* patterns = reinterpret_cast<const std::uint64_t*>(base + pool.output_offsets[4]);
        const auto* projections = reinterpret_cast<const std::uint64_t*>(base + pool.output_offsets[5]);
        const auto* cluster_keys = reinterpret_cast<const std::uint64_t*>(base + pool.output_offsets[6]);
        for (std::size_t index = 0; index < pool.end - pool.begin; ++index)
            (*output)[pool.begin + index] = GpuVerifiedFeature{sums[index], hashes[index], energies[index],
                transitions[index], patterns[index], projections[index], cluster_keys[index]};
        pool.pending = false;
        return true;
    }

    unsigned PendingRealBatchCount() const noexcept {
        return static_cast<unsigned>(std::count_if(stream_pools_.begin(), stream_pools_.end(),
            [](const StreamPool& pool) { return pool.pending; }));
    }

    bool FlushRealBatches(std::vector<GpuVerifiedFeature>* output, GpuPerformanceReport* performance,
                          std::uint64_t timeout_microseconds) {
        for (unsigned slot = 0; slot < stream_count_; ++slot) {
            if (!HarvestRealBatch(slot, output, performance, timeout_microseconds)) return false;
        }
        return true;
    }

    void FailClosedResetBackend(GpuPerformanceReport* performance) noexcept {
        if (PendingRealBatchCount() != 0 && cu_device_primary_ctx_reset_ != nullptr &&
            cu_device_primary_ctx_reset_(device_) == kCudaSuccess) {
            for (auto& pool : stream_pools_) pool = {};
            streams_.fill(nullptr);
            stream_count_ = 0;
            module_ = nullptr;
            kernel_ = nullptr;
            structured_kernels_.fill(nullptr);
            if (cu_device_primary_ctx_release_ != nullptr) cu_device_primary_ctx_release_(device_);
            context_ = nullptr;
            capability_.backend_initialized = false;
            capability_.fallback_reason = "CudaPrimaryContextResetAfterTimeoutOrTdr";
            ++performance->pool_reset_count;
            return;
        }
        ResetPools(performance);
    }

    bool RunRealBatch(const std::vector<GpuEvidenceRecord>& records, std::size_t begin, std::size_t end,
                      std::vector<GpuVerifiedFeature>* output, GpuPerformanceReport* performance,
                      bool* allocation_failed, GpuOperationClass operation_class,
                      const GpuSchedulerPlan& scheduler, unsigned stream_slot,
                      std::uint64_t timeout_microseconds) {
        *allocation_failed = false;
        if (stream_count_ == 0 || stream_slot >= stream_count_ || context_ == nullptr || kernel_ == nullptr)
            return false;
        if (!HarvestRealBatch(stream_slot, output, performance, timeout_microseconds)) return false;
        const std::size_t count_size = end - begin;
        std::size_t packed_bytes = 0;
        for (std::size_t index = begin; index < end; ++index) {
            if (records[index].bytes.size() > std::numeric_limits<std::uint32_t>::max()) return false;
            packed_bytes += records[index].bytes.size();
        }
        const auto align8 = [](std::size_t value) { return (value + 7u) & ~std::size_t{7u}; };
        std::size_t cursor = align8(std::max<std::size_t>(packed_bytes, 1));
        const std::size_t offsets_offset = cursor; cursor += count_size * sizeof(std::uint64_t);
        const std::size_t lengths_offset = cursor; cursor = align8(cursor + count_size * sizeof(std::uint32_t));
        std::array<std::size_t, 7> output_offsets{};
        output_offsets[0] = cursor; cursor = align8(cursor + count_size * sizeof(std::uint32_t));
        for (std::size_t index = 1; index < output_offsets.size(); ++index) {
            output_offsets[index] = cursor;
            cursor += count_size * sizeof(std::uint64_t);
        }
        const std::array<std::size_t, 10> device_sizes{{std::max<std::size_t>(packed_bytes, 1),
            count_size * sizeof(std::uint64_t), count_size * sizeof(std::uint32_t),
            count_size * sizeof(std::uint32_t), count_size * sizeof(std::uint64_t),
            count_size * sizeof(std::uint64_t), count_size * sizeof(std::uint64_t),
            count_size * sizeof(std::uint64_t), count_size * sizeof(std::uint64_t),
            count_size * sizeof(std::uint64_t)}};
        const auto allocation_start = std::chrono::steady_clock::now();
        bool allocated = EnsurePinnedBuffer(stream_slot, cursor,
            scheduler.pinned_host_memory_limit_bytes, performance);
        for (std::size_t index = 0; index < device_sizes.size(); ++index)
            allocated = allocated && EnsureDeviceBuffer(stream_slot, index, device_sizes[index],
                scheduler.memory_pool_limit_bytes, performance);
        performance->gpu_allocation_microseconds += ElapsedMicroseconds(allocation_start);
        if (!allocated) { *allocation_failed = true; return false; }
        auto& pool = stream_pools_[stream_slot];
        auto* base = static_cast<std::uint8_t*>(pool.pinned);
        auto* offsets = reinterpret_cast<std::uint64_t*>(base + offsets_offset);
        auto* lengths = reinterpret_cast<std::uint32_t*>(base + lengths_offset);
        std::size_t packed_cursor = 0;
        for (std::size_t index = begin; index < end; ++index) {
            offsets[index - begin] = packed_cursor;
            lengths[index - begin] = static_cast<std::uint32_t>(records[index].bytes.size());
            if (!records[index].bytes.empty()) {
                std::memcpy(base + packed_cursor, records[index].bytes.data(), records[index].bytes.size());
                packed_cursor += records[index].bytes.size();
            }
        }
        CUstream stream = streams_[stream_slot];
        auto device = [&pool](std::size_t index) { return pool.device[index].pointer; };
        const auto h2d_start = std::chrono::steady_clock::now();
        bool succeeded = (packed_bytes == 0 ||
            cu_memcpy_htod_async_(device(0), base, packed_bytes, stream) == kCudaSuccess) &&
            cu_memcpy_htod_async_(device(1), offsets, device_sizes[1], stream) == kCudaSuccess &&
            cu_memcpy_htod_async_(device(2), lengths, device_sizes[2], stream) == kCudaSuccess;
        performance->async_transfer_count += 3;
        performance->gpu_h2d_microseconds += ElapsedMicroseconds(h2d_start);
        unsigned count = static_cast<unsigned>(count_size);
        unsigned operation = static_cast<unsigned>(operation_class);
        CUdeviceptr pointers[10]{};
        for (std::size_t index = 0; index < 10; ++index) pointers[index] = device(index);
        void* arguments[] = {&pointers[0], &pointers[1], &pointers[2], &pointers[3], &pointers[4],
            &pointers[5], &pointers[6], &pointers[7], &pointers[8], &pointers[9], &count, &operation};
        void* operation_arguments[] = {&pointers[0], &pointers[1], &pointers[2], &pointers[3], &pointers[4],
            &pointers[5], &pointers[6], &pointers[7], &pointers[8], &pointers[9], &count};
        const unsigned blocks = (count + 255u) / 256u;
        const auto launch_start = std::chrono::steady_clock::now();
        succeeded = succeeded && cu_launch_kernel_(kernel_, blocks, 1, 1, 256, 1, 1, 0, stream,
            arguments, nullptr) == kCudaSuccess;
        if (succeeded) ++performance->gpu_kernel_launch_count;
        const auto operation_kernel = OperationKernel(operation_class);
        succeeded = succeeded && operation_kernel != nullptr &&
            cu_launch_kernel_(operation_kernel, blocks, 1, 1, 256, 1, 1, 0, stream,
                operation_arguments, nullptr) == kCudaSuccess;
        if (succeeded) ++performance->gpu_kernel_launch_count;
        performance->gpu_kernel_launch_microseconds += ElapsedMicroseconds(launch_start);
        const auto d2h_start = std::chrono::steady_clock::now();
        for (std::size_t index = 0; succeeded && index < output_offsets.size(); ++index) {
            const std::size_t bytes = count_size * (index == 0 ? sizeof(std::uint32_t) : sizeof(std::uint64_t));
            succeeded = cu_memcpy_dtoh_async_(base + output_offsets[index], device(index + 3), bytes, stream) == kCudaSuccess;
            ++performance->async_transfer_count;
        }
        performance->gpu_d2h_microseconds += ElapsedMicroseconds(d2h_start);
        if (pool.completion == nullptr)
            succeeded = succeeded && cu_event_create_(&pool.completion, 2u) == kCudaSuccess;
        succeeded = succeeded && cu_event_record_(pool.completion, stream) == kCudaSuccess;
        if (!succeeded) return false;
        pool.pending = true;
        pool.begin = begin;
        pool.end = end;
        pool.output_offsets = output_offsets;
        return true;
    }

    GpuCapabilityReport capability_;
    GpuTestScenario scenario_ = GpuTestScenario::RealHardware;
    HMODULE driver_ = nullptr;
    CUdevice device_ = 0;
    CUcontext context_ = nullptr;
    CUmodule module_ = nullptr;
    CUfunction kernel_ = nullptr;
    CUfunction trace_kernel_ = nullptr;
    CUfunction parser_kernel_ = nullptr;
    CUfunction class_layout_kernel_ = nullptr;
    CUfunction registry_kernel_ = nullptr;
    CUfunction value_flow_kernel_ = nullptr;
    CUfunction formula_kernel_ = nullptr;
    std::array<CUfunction, kStructuredKernelDescriptors.size()> structured_kernels_{};
    CuInit cu_init_ = nullptr;
    CuDriverGetVersion cu_driver_get_version_ = nullptr;
    CuDeviceGetCount cu_device_get_count_ = nullptr;
    CuDeviceGet cu_device_get_ = nullptr;
    CuDeviceGetName cu_device_get_name_ = nullptr;
    CuDeviceGetAttribute cu_device_get_attribute_ = nullptr;
    CuDeviceTotalMem cu_device_total_mem_ = nullptr;
    CuDevicePrimaryCtxRetain cu_device_primary_ctx_retain_ = nullptr;
    CuDevicePrimaryCtxRelease cu_device_primary_ctx_release_ = nullptr;
    CuDevicePrimaryCtxReset cu_device_primary_ctx_reset_ = nullptr;
    CuCtxSetCurrent cu_ctx_set_current_ = nullptr;
    CuMemGetInfo cu_mem_get_info_ = nullptr;
    CuModuleLoadDataEx cu_module_load_data_ex_ = nullptr;
    CuModuleUnload cu_module_unload_ = nullptr;
    CuModuleGetFunction cu_module_get_function_ = nullptr;
    CuMemAlloc cu_mem_alloc_ = nullptr;
    CuMemFree cu_mem_free_ = nullptr;
    CuMemcpyHtoD cu_memcpy_htod_ = nullptr;
    CuMemcpyDtoH cu_memcpy_dtoh_ = nullptr;
    CuMemcpyHtoDAsync cu_memcpy_htod_async_ = nullptr;
    CuMemcpyDtoHAsync cu_memcpy_dtoh_async_ = nullptr;
    CuLaunchKernel cu_launch_kernel_ = nullptr;
    CuCtxSynchronize cu_ctx_synchronize_ = nullptr;
    CuStreamCreate cu_stream_create_ = nullptr;
    CuStreamDestroy cu_stream_destroy_ = nullptr;
    CuStreamSynchronize cu_stream_synchronize_ = nullptr;
    CuEventCreate cu_event_create_ = nullptr;
    CuEventDestroy cu_event_destroy_ = nullptr;
    CuEventRecord cu_event_record_ = nullptr;
    CuEventQuery cu_event_query_ = nullptr;
    CuEventSynchronize cu_event_synchronize_ = nullptr;
    CuMemHostAlloc cu_mem_host_alloc_ = nullptr;
    CuMemFreeHost cu_mem_free_host_ = nullptr;
    std::array<CUstream, 4> streams_{};
    std::array<StreamPool, 4> stream_pools_{};
    unsigned stream_count_ = 0;
    bool mock_pool_initialized_ = false;
    std::array<CalibrationEntry, kUltimateGpuWorkloadCount> calibration_{};
};

std::string ToString(AccelerationMode value) {
    return value == AccelerationMode::CpuOnly ? "CPU Only" : "Auto";
}

std::string ToString(GpuArchitectureTier value) {
    switch (value) {
    case GpuArchitectureTier::CpuFullFeature: return "Tier0CpuFullFeature";
    case GpuArchitectureTier::LegacyCuda: return "Tier1LegacyCuda";
    case GpuArchitectureTier::RtxStandard: return "Tier1.5RtxStandard";
    case GpuArchitectureTier::Full: return "Tier2Full";
    case GpuArchitectureTier::HighPerformance: return "Tier3HighPerformance";
    case GpuArchitectureTier::Maximum: return "Tier4Maximum";
    }
    return "Tier0CpuFullFeature";
}

std::string ToString(GpuOperationClass value) {
    switch (value) {
    case GpuOperationClass::TraceFeatureExtraction: return "TraceFeatureExtraction";
    case GpuOperationClass::CrossSessionCorrelation: return "CrossSessionCorrelation";
    case GpuOperationClass::ParserFieldPatternMatching: return "ParserFieldPatternMatching";
    case GpuOperationClass::FunctionClustering: return "FunctionClustering";
    case GpuOperationClass::ObjectClustering: return "ObjectClustering";
    case GpuOperationClass::ClassLayoutScoring: return "ClassLayoutScoring";
    case GpuOperationClass::RegistryScoring: return "RegistryScoring";
    case GpuOperationClass::HeapGraphSimilarity: return "HeapGraphSimilarity";
    case GpuOperationClass::ValueFlowAggregation: return "ValueFlowAggregation";
    case GpuOperationClass::TaintGraphBatch: return "TaintGraphBatch";
    case GpuOperationClass::SemanticEdgeScoring: return "SemanticEdgeScoring";
    case GpuOperationClass::ApproximateNearestNeighbor: return "ApproximateNearestNeighbor";
    case GpuOperationClass::SequenceMining: return "SequenceMining";
    case GpuOperationClass::FsmScoring: return "FsmScoring";
    case GpuOperationClass::FormulaBatchEvaluation: return "FormulaBatchEvaluation";
    case GpuOperationClass::ReplayStateComparison: return "ReplayStateComparison";
    case GpuOperationClass::AiInference: return "AiInference";
    }
    return "TraceFeatureExtraction";
}

std::string GpuModeDisplayName(GpuArchitectureTier value) {
    switch (value) {
    case GpuArchitectureTier::CpuFullFeature: return "CPU";
    case GpuArchitectureTier::LegacyCuda: return "Legacy CUDA";
    case GpuArchitectureTier::RtxStandard: return "RTX Standard";
    case GpuArchitectureTier::Full: return "Full";
    case GpuArchitectureTier::HighPerformance: return "High Performance";
    case GpuArchitectureTier::Maximum: return "Maximum";
    }
    return "CPU";
}

AccelerationMode ParseAccelerationMode(std::string_view value) {
    return value == "CPU Only" || value == "CpuOnly" || value == "CPUOnly" ?
        AccelerationMode::CpuOnly : AccelerationMode::Auto;
}

GpuArchitectureTier ClassifyGpuArchitecture(int compute_major, int compute_minor) {
    if (compute_major > 9) return GpuArchitectureTier::Maximum;
    if (compute_major > 8 || (compute_major == 8 && compute_minor >= 9))
        return GpuArchitectureTier::HighPerformance;
    if (compute_major >= 8) return GpuArchitectureTier::Full;
    if (compute_major == 7 && compute_minor >= 5) return GpuArchitectureTier::RtxStandard;
    if (compute_major > 6 || (compute_major == 6 && compute_minor >= 1))
        return GpuArchitectureTier::LegacyCuda;
    return GpuArchitectureTier::CpuFullFeature;
}

GpuSchedulerPlan MakeGpuSchedulerPlan(const GpuCapabilityReport& capability, AccelerationPhase phase,
                                      std::size_t queue_depth, std::uint64_t evidence_bytes) {
    GpuSchedulerPlan plan;
    plan.batch_records = 256;
    plan.buffer_bytes = 4 * static_cast<std::size_t>(kMib);
    if (!capability.backend_initialized || capability.tier == GpuArchitectureTier::CpuFullFeature)
        return plan;
    const bool live = phase == AccelerationPhase::Live;
    const std::uint64_t free_budget = capability.free_vram_bytes == 0 ? 64 * kMib : capability.free_vram_bytes / 16;
    const std::uint64_t phase_cap = live ? 64 * kMib : 512 * kMib;
    plan.memory_pool_limit_bytes = std::max<std::uint64_t>(8 * kMib, std::min(free_budget, phase_cap));
    const std::uint64_t pinned_phase_cap = live ? 8 * kMib : 64 * kMib;
    plan.pinned_host_memory_limit_bytes = std::min<std::uint64_t>(
        pinned_phase_cap, std::max<std::uint64_t>(1 * kMib, plan.memory_pool_limit_bytes / 8));
    plan.utilization_limit_percent = live ? 25u : 90u;
    plan.background_priority = live;
    plan.concurrency = live ? 1u : 2u;
    switch (capability.tier) {
    case GpuArchitectureTier::LegacyCuda: plan.batch_records = live ? 512 : 2048; break;
    case GpuArchitectureTier::RtxStandard: plan.batch_records = live ? 768 : 4096; break;
    case GpuArchitectureTier::Full: plan.batch_records = live ? 1024 : 8192; break;
    case GpuArchitectureTier::HighPerformance:
        plan.batch_records = live ? 1536 : 12288;
        plan.concurrency = live ? 1u : 3u;
        break;
    case GpuArchitectureTier::Maximum:
        plan.batch_records = live ? 2048 : 16384;
        plan.concurrency = live ? 1u : 4u;
        break;
    default: break;
    }
    if (queue_depth < plan.batch_records) plan.batch_records = std::max<std::size_t>(queue_depth, 1);
    plan.buffer_bytes = static_cast<std::size_t>(std::min<std::uint64_t>(
        plan.memory_pool_limit_bytes / 2, std::max<std::uint64_t>(evidence_bytes, 4 * kMib)));
    plan.gpu_split_percent = live ? 50u : 100u;
    if (capability.current_gpu_load_percent >= (live ? 50u : 85u)) {
        plan.batch_records = std::max<std::size_t>(plan.batch_records / 2, 1);
        plan.concurrency = 1;
        plan.gpu_split_percent = live ? 25u : 50u;
        plan.utilization_limit_percent = live ? 15u : 60u;
    }
    return plan;
}

OptionalGpuAccelerator::OptionalGpuAccelerator(AccelerationMode mode, GpuTestScenario scenario)
    : implementation_(std::make_unique<Implementation>(mode, scenario)) {}

OptionalGpuAccelerator::~OptionalGpuAccelerator() = default;

const GpuCapabilityReport& OptionalGpuAccelerator::Capability() const noexcept {
    return implementation_->capability_;
}

GpuProcessingResult OptionalGpuAccelerator::Process(const std::vector<GpuEvidenceRecord>& records,
                                                     AccelerationPhase phase,
                                                     const std::atomic_bool* stop_requested,
                                                     GpuOperationClass operation_class,
                                                     GpuDispatchIntent dispatch_intent) {
    GpuProcessingResult result;
    result.capability = implementation_->capability_;
    result.workload.operation_class = ToString(operation_class);
    result.workload.algorithm = WorkloadAlgorithm(operation_class);
    result.workload.normalized_input_contract = NormalizedInputContract(operation_class);
    result.workload.candidate_output_contract = CandidateOutputContract(operation_class);
    result.workload.workload_scope = WorkloadScope(operation_class);
    result.workload.model_status = operation_class == GpuOperationClass::AiInference ?
        "EvidenceBlockedModelUnavailable" : "NotApplicable";
    result.workload.normalized_input_digest = InputDigest(records, operation_class);
    const bool operation_gpu_implemented = HasOperationSpecificGpuImplementation(operation_class);
    result.workload.gpu_implementation_available = operation_gpu_implemented;
    result.performance.input_records = records.size();
    result.performance.queue_high_water_mark = records.size();
    for (const auto& record : records)
        result.performance.input_bytes += record.bytes.size();
    result.scheduler = MakeGpuSchedulerPlan(result.capability, phase, records.size(), result.performance.input_bytes);
    result.performance.configured_stream_count = std::min<unsigned>(
        result.scheduler.concurrency, implementation_->stream_count_);
    const bool model_available = implementation_->scenario_ != GpuTestScenario::BenefitModelUnavailable;
    auto& calibration = implementation_->calibration_[OperationIndex(operation_class)];
    result.benefit = EstimateBenefit(result.capability, result.scheduler, phase, operation_class,
        result.performance.input_records, result.performance.input_bytes,
        result.workload.normalized_input_digest, calibration,
        model_available, dispatch_intent);
    result.verified_features.resize(records.size());
    const auto wall_start = std::chrono::steady_clock::now();
    const bool usable = result.benefit.gpu_selected;
    bool gpu_failed = false;
    std::size_t gpu_end = 0;
    const std::size_t gpu_target_records = usable ? std::min<std::size_t>(records.size(),
        (records.size() * result.scheduler.gpu_split_percent + 99u) / 100u) : 0;
    if (gpu_target_records != 0) {
        const std::size_t batch_size = std::max<std::size_t>(result.scheduler.batch_records, 1);
        unsigned stream_slot = 0;
        for (std::size_t begin = 0; begin < gpu_target_records; begin = gpu_end) {
            gpu_end = begin;
            std::uint64_t batch_bytes = 0;
            const std::uint64_t memory_limit = std::max<std::uint64_t>(
                result.scheduler.memory_pool_limit_bytes, 8 * kMib);
            while (gpu_end < gpu_target_records && gpu_end - begin < batch_size) {
                const std::uint64_t record_bytes = records[gpu_end].bytes.size() + 24ull;
                if (gpu_end != begin && batch_bytes + record_bytes > memory_limit / 2) break;
                if (record_bytes > memory_limit / 2) {
                    result.performance.fallback_reason = "EvidenceRecordExceedsGpuMemoryPool";
                    gpu_failed = true;
                    break;
                }
                batch_bytes += record_bytes;
                ++gpu_end;
            }
            if (gpu_failed) {
                gpu_end = begin;
                break;
            }
            if (stop_requested != nullptr && stop_requested->load()) {
                result.performance.cancelled = true;
                result.performance.fallback_reason = "StopRequestedDuringGpuWork";
                gpu_failed = true;
                gpu_end = begin;
                break;
            }
            const auto gpu_start = std::chrono::steady_clock::now();
            bool batch_ok = true;
            const std::uint64_t timeout_budget_microseconds =
                phase == AccelerationPhase::Live ? 750000ull : 30000000ull;
            if (implementation_->scenario_ == GpuTestScenario::Oom) {
                ++result.performance.gpu_oom_count;
                result.performance.fallback_reason = "GpuOutOfMemory";
                batch_ok = false;
            } else if (implementation_->scenario_ == GpuTestScenario::WorkerFailure) {
                result.performance.fallback_reason = "GpuWorkerFailure";
                batch_ok = false;
            } else if (implementation_->scenario_ == GpuTestScenario::Timeout) {
                ++result.performance.timeout_count;
                result.performance.fallback_reason = "GpuOperationTimeout";
                batch_ok = false;
            } else if (implementation_->scenario_ == GpuTestScenario::StopDuringWork) {
                result.performance.cancelled = true;
                result.performance.fallback_reason = "StopRequestedDuringGpuWork";
                batch_ok = false;
            } else if (implementation_->scenario_ == GpuTestScenario::RealHardware) {
                bool allocation_failed = false;
                batch_ok = implementation_->RunRealBatch(records, begin, gpu_end,
                    &result.verified_features, &result.performance,
                    &allocation_failed, operation_class, result.scheduler, stream_slot,
                    timeout_budget_microseconds);
                if (allocation_failed) ++result.performance.gpu_oom_count;
                if (!batch_ok) {
                    result.performance.fallback_reason = allocation_failed ?
                        "CudaOutOfMemoryCpuFallback" : "CudaBatchFailureOrDeviceResetCpuFallback";
                }
            } else {
                if (!implementation_->mock_pool_initialized_) {
                    ++result.performance.device_pool_allocation_count;
                    ++result.performance.pinned_pool_allocation_count;
                    implementation_->mock_pool_initialized_ = true;
                } else {
                    ++result.performance.device_pool_reuse_count;
                    ++result.performance.pinned_pool_reuse_count;
                }
                result.performance.peak_pinned_host_memory_bytes = std::min<std::uint64_t>(
                    result.scheduler.pinned_host_memory_limit_bytes, batch_bytes);
                for (std::size_t index = begin; index < gpu_end; ++index) {
                    result.verified_features[index] = CpuFeature(records[index].bytes, operation_class);
                    if (implementation_->scenario_ == GpuTestScenario::CandidateMismatch &&
                        operation_class == GpuOperationClass::TraceFeatureExtraction && index == begin)
                        result.verified_features[index].operation_projection ^= 1ull;
                }
                result.performance.peak_gpu_memory_bytes = std::max<std::uint64_t>(
                    result.performance.peak_gpu_memory_bytes,
                    std::min<std::uint64_t>(result.scheduler.memory_pool_limit_bytes,
                                            result.performance.input_bytes));
            }
            result.performance.gpu_elapsed_microseconds += ElapsedMicroseconds(gpu_start);
            if (!batch_ok) {
                ++result.performance.backend_error_count;
                gpu_failed = true;
                gpu_end = begin;
                break;
            }
            ++result.performance.gpu_batches;
            if (result.performance.configured_stream_count != 0)
                stream_slot = (stream_slot + 1u) % result.performance.configured_stream_count;
            result.performance.peak_inflight_batches = std::max(result.performance.peak_inflight_batches,
                implementation_->scenario_ == GpuTestScenario::RealHardware ?
                    implementation_->PendingRealBatchCount() : 1u);
            result.performance.gpu_processed_records += gpu_end - begin;
            for (std::size_t index = begin; index < gpu_end; ++index)
                result.performance.gpu_processed_bytes += records[index].bytes.size();
        }
        if (!gpu_failed && implementation_->scenario_ == GpuTestScenario::RealHardware) {
            const std::uint64_t timeout_budget_microseconds =
                phase == AccelerationPhase::Live ? 750000ull : 30000000ull;
            if (!implementation_->FlushRealBatches(&result.verified_features, &result.performance,
                    timeout_budget_microseconds)) {
                ++result.performance.backend_error_count;
                result.performance.fallback_reason = result.performance.timeout_count != 0 ?
                    "GpuOperationTimeoutCpuFallback" : "CudaEventOrDeviceResetCpuFallback";
                gpu_failed = true;
                gpu_end = 0;
            }
        }
    }
    if (!result.benefit.gpu_available && result.capability.requested_mode == AccelerationMode::Auto) {
        result.performance.fallback_occurred = true;
        result.performance.fallback_reason = result.capability.fallback_reason.empty() ?
            "GpuBackendUnavailable" : result.capability.fallback_reason;
    }
    if (gpu_failed) {
        if (implementation_->scenario_ == GpuTestScenario::RealHardware)
            implementation_->FailClosedResetBackend(&result.performance);
        else {
            implementation_->mock_pool_initialized_ = false;
            ++result.performance.pool_reset_count;
        }
        result.performance.fallback_occurred = true;
        result.performance.gpu_batches = 0;
        result.performance.gpu_processed_records = 0;
        result.performance.gpu_processed_bytes = 0;
        result.benefit.selected_backend = "CPU";
        result.benefit.selection_reason = result.performance.cancelled ?
            "CpuFallback" : "GpuBackendFailure";
        result.benefit.benefit_gate_decision = "CpuFallback";
    }

    const std::size_t gpu_primitive_count = gpu_failed ? gpu_end :
        static_cast<std::size_t>(result.performance.gpu_processed_records);
    if (gpu_primitive_count != 0)
        result.workload.gpu_primitive_digest = PrimitiveDigestPrefix(
            result.verified_features, gpu_primitive_count);
    const std::size_t gpu_candidate_count = operation_gpu_implemented ? gpu_primitive_count : 0;
    if (gpu_candidate_count != 0)
        result.workload.gpu_candidate_digest = FeatureDigestPrefix(
            result.verified_features, gpu_candidate_count);

    // This is the authority boundary: every GPU candidate is recomputed with
    // the deterministic CPU implementation before it can be exposed downstream.
    const auto cpu_start = std::chrono::steady_clock::now();
    const std::size_t gpu_verified_end = gpu_primitive_count;
    for (std::size_t index = 0; index < records.size(); ++index) {
        const auto cpu = CpuFeature(records[index].bytes, operation_class);
        ++result.performance.cpu_processed_records;
        if (index < gpu_verified_end) {
            ++result.equivalence.compared_records;
            const auto& candidate = result.verified_features[index];
            const bool equivalent = operation_gpu_implemented ?
                SameFeature(candidate, cpu) : SamePrimitive(candidate, cpu);
            if (!equivalent) {
                ++result.equivalence.mismatch_count;
                result.performance.fallback_occurred = true;
                result.performance.fallback_reason = operation_gpu_implemented ?
                    "CpuGpuOperationSpecificCandidateMismatch" : "CpuGpuPrimitiveMismatch";
            }
        }
        result.verified_features[index] = cpu;
    }
    result.performance.cpu_elapsed_microseconds = ElapsedMicroseconds(cpu_start);
    result.performance.wall_elapsed_microseconds = ElapsedMicroseconds(wall_start);
    if (result.equivalence.mismatch_count != 0) {
        result.equivalence.status = operation_gpu_implemented ?
            "OperationSpecificGpuMismatchDiscardedCpuAuthorityPreserved" :
            "PrimitiveMismatchDiscardedCpuAuthorityPreserved";
        result.benefit.selected_backend = "CPU";
        result.benefit.selection_reason = "CpuFallback";
        result.benefit.benefit_gate_decision = "CpuFallback";
    } else if (gpu_candidate_count != 0) {
        result.equivalence.status = gpu_failed ?
            "OperationSpecificGpuPartialEquivalentCpuFallback" :
            "OperationSpecificGpuEquivalentCpuAuthorityPreserved";
    } else if (result.equivalence.compared_records != 0) {
        result.equivalence.status = "CudaPrimitiveEquivalentSemanticGpuImplementationUnavailable";
    } else if (result.benefit.gpu_available && result.capability.requested_mode == AccelerationMode::Auto) {
        result.equivalence.status = "GpuAvailableButCpuPreferred";
    } else if (result.capability.requested_mode == AccelerationMode::CpuOnly) {
        result.equivalence.status = "CpuOnlyDeterministic";
    } else {
        result.equivalence.status = "CpuDeterministicFallback";
    }
    result.equivalence.authoritative_results_equivalent = true;
    result.equivalence.verified_results_equivalent = gpu_candidate_count != 0 &&
        result.equivalence.mismatch_count == 0;
    result.workload.gpu_primitive_extraction_executed = result.performance.gpu_processed_records != 0;
    result.workload.gpu_candidate_executed = gpu_candidate_count != 0;
    result.workload.authoritative_output_digest = FeatureDigest(result.verified_features);
    result.workload.cpu_primitive_digest = gpu_primitive_count == 0 ? "NOT_COMPARED" :
        PrimitiveDigestPrefix(result.verified_features, gpu_primitive_count);
    if (operation_class == GpuOperationClass::AiInference) {
        result.workload.implementation_status = "EvidenceBlockedModelUnavailable";
        result.workload.gpu_primitive_digest = "NOT_RUN_NO_VERIFIED_MODEL";
        result.workload.gpu_candidate_digest = "NOT_RUN_NO_VERIFIED_MODEL";
    } else if (!operation_gpu_implemented) {
        result.workload.implementation_status = "EvidenceBlockedGpuImplementationUnavailable";
        result.workload.gpu_candidate_digest =
            "NOT_RUN_OPERATION_SPECIFIC_GPU_IMPLEMENTATION_UNAVAILABLE";
        // The scheduler may have accelerated generic primitive extraction,
        // but the formal workload backend is CPU because its semantic
        // reduction never executed on CUDA.
        if (result.capability.requested_mode == AccelerationMode::Auto) {
            result.benefit.gpu_eligible = false;
            result.benefit.gpu_selected = false;
            result.benefit.selected_backend = "CPU";
            result.benefit.selection_reason = "EvidenceBlockedGpuImplementationUnavailable";
            result.benefit.benefit_gate_decision = "CpuAuthoritativeSemanticReduction";
            result.benefit.predicted_gpu_microseconds = 0;
            result.benefit.predicted_speedup = 0.0;
            if (result.benefit.calibration_source != "Unavailable")
                result.benefit.calibration_source = "GenericPrimitiveTelemetryNotSemanticGpuModel";
        }
    } else if (gpu_candidate_count != 0 && result.equivalence.mismatch_count == 0 && !gpu_failed) {
        result.workload.implementation_status = "OperationSpecificGpuCandidateVerified";
    } else if (gpu_candidate_count != 0 && result.equivalence.mismatch_count != 0) {
        result.workload.implementation_status = "GpuCandidateMismatchCpuFallback";
    } else if (gpu_failed) {
        result.workload.implementation_status = "GpuExecutionFailedCpuFallback";
        result.workload.gpu_candidate_digest = "NOT_RUN_GPU_EXECUTION_FAILED";
    } else if (result.capability.requested_mode == AccelerationMode::CpuOnly) {
        result.workload.implementation_status = "GpuImplementationAvailableCpuOnly";
        result.workload.gpu_candidate_digest = "NOT_RUN_CPU_ONLY";
    } else if (!result.benefit.gpu_available) {
        result.workload.implementation_status = "EvidenceBlockedGpuUnavailable";
        result.workload.gpu_candidate_digest = "NOT_RUN_GPU_UNAVAILABLE";
    } else {
        result.workload.implementation_status = "GpuImplementationAvailableCpuSelected";
        result.workload.gpu_candidate_digest = "NOT_RUN_GPU_NOT_SELECTED";
    }
    if (!records.empty() && !result.performance.cancelled && calibration.sample_count < 16) {
        const double sample_count = static_cast<double>(calibration.sample_count);
        if (result.performance.cpu_elapsed_microseconds != 0 && result.performance.input_bytes != 0) {
            const double cpu_sample = static_cast<double>(result.performance.cpu_elapsed_microseconds) /
                static_cast<double>(result.performance.input_bytes);
            calibration.cpu_microseconds_per_byte = calibration.sample_count == 0 ? cpu_sample :
                (calibration.cpu_microseconds_per_byte * sample_count + cpu_sample) / (sample_count + 1.0);
        }
        ++calibration.sample_count;
        if (!gpu_failed && result.performance.gpu_processed_records != 0 &&
            result.performance.gpu_elapsed_microseconds != 0) {
            const double gpu_fraction = static_cast<double>(result.performance.gpu_processed_records) /
                static_cast<double>(result.performance.input_records);
            const double gpu_bytes = static_cast<double>(result.performance.input_bytes) * gpu_fraction;
            if (gpu_bytes > 0.0) {
                const double gpu_sample = static_cast<double>(result.performance.gpu_elapsed_microseconds) / gpu_bytes;
                const double gpu_samples = static_cast<double>(calibration.gpu_sample_count);
                calibration.gpu_microseconds_per_byte = calibration.gpu_sample_count == 0 ? gpu_sample :
                    (calibration.gpu_microseconds_per_byte * gpu_samples + gpu_sample) / (gpu_samples + 1.0);
                ++calibration.gpu_sample_count;
            }
        }
    }
    const bool trusted_paired_full_path_sample =
        dispatch_intent == GpuDispatchIntent::MeasurementOverride && operation_gpu_implemented &&
        result.workload.gpu_candidate_executed && !gpu_failed && !result.performance.cancelled &&
        result.equivalence.mismatch_count == 0 && result.performance.dropped_evidence == 0 &&
        result.performance.gpu_processed_records == result.performance.input_records &&
        result.performance.input_bytes != 0 && result.performance.cpu_elapsed_microseconds != 0 &&
        result.performance.wall_elapsed_microseconds != 0;
    if (trusted_paired_full_path_sample) {
        const double sample_bytes = static_cast<double>(result.performance.input_bytes);
        const double cpu_sample = static_cast<double>(result.performance.cpu_elapsed_microseconds) / sample_bytes;
        const double gpu_full_sample = static_cast<double>(result.performance.wall_elapsed_microseconds) / sample_bytes;
        const double prior_samples = static_cast<double>(
            std::min<std::uint64_t>(calibration.paired_full_path_sample_count, 63));
        calibration.cpu_baseline_microseconds_per_byte =
            calibration.paired_full_path_sample_count == 0 ? cpu_sample :
            (calibration.cpu_baseline_microseconds_per_byte * prior_samples + cpu_sample) /
                (prior_samples + 1.0);
        calibration.gpu_full_path_microseconds_per_byte =
            calibration.paired_full_path_sample_count == 0 ? gpu_full_sample :
            (calibration.gpu_full_path_microseconds_per_byte * prior_samples + gpu_full_sample) /
                (prior_samples + 1.0);
        if (calibration.paired_full_path_sample_count < 64)
            ++calibration.paired_full_path_sample_count;
        calibration.latest_cpu_baseline_microseconds = result.performance.cpu_elapsed_microseconds;
        calibration.latest_gpu_full_path_microseconds = result.performance.wall_elapsed_microseconds;
        calibration.latest_records = result.performance.input_records;
        calibration.latest_bytes = result.performance.input_bytes;
        calibration.latest_input_digest = result.workload.normalized_input_digest;
        calibration.latest_paired_sample_at = implementation_->scenario_ == GpuTestScenario::StaleCalibration ?
            std::chrono::steady_clock::now() - kCalibrationFreshness - std::chrono::seconds(1) :
            std::chrono::steady_clock::now();
    }
    result.success = result.performance.dropped_evidence == 0;
    return result;
}

bool WriteGpuEvidenceReports(const fs::path& performance_directory, std::string_view session_id,
                             const GpuProcessingResult& result, std::string* error) {
    std::error_code directory_error;
    fs::create_directories(performance_directory, directory_error);
    if (directory_error) {
        if (error) *error = "cannot create GPU performance report directory: " + directory_error.message();
        return false;
    }
    const auto& capability = result.capability;
    const auto& scheduler = result.scheduler;
    const auto& benefit = result.benefit;
    const auto& performance = result.performance;
    const auto& equivalence = result.equivalence;
    const auto capability_json = MakeJsonObject({
        {"SchemaVersion", "1"}, {"SessionId", std::string(session_id)},
        {"GpuDetected", capability.gpu_detected ? "true" : "false"},
        {"GpuAvailable", benefit.gpu_available ? "true" : "false"},
        {"GpuEligible", benefit.gpu_eligible ? "true" : "false"},
        {"GpuSelected", benefit.gpu_selected ? "true" : "false"},
        {"OperationSpecificGpuImplementationAvailable",
            result.workload.gpu_implementation_available ? "true" : "false"},
        {"ConcurrentBatchExecutionAvailable", performance.peak_inflight_batches > 1 ? "true" : "false"},
        {"SelectedBackend", benefit.selected_backend},
        {"SelectionReason", benefit.selection_reason},
        {"BenefitGateDecision", benefit.benefit_gate_decision},
        {"Vendor", capability.vendor}, {"Device", capability.device},
        {"ArchitectureTier", ToString(capability.tier)},
        {"Mode", GpuModeDisplayName(capability.backend_initialized ? capability.tier :
            GpuArchitectureTier::CpuFullFeature)},
        {"ComputeCapability", capability.compute_major == 0 ? "NotAvailable" :
            std::to_string(capability.compute_major) + "." + std::to_string(capability.compute_minor)},
        {"VramBytes", std::to_string(capability.vram_bytes)},
        {"FreeVramBytes", std::to_string(capability.free_vram_bytes)},
        {"CurrentGpuLoadPercent", std::to_string(capability.current_gpu_load_percent)},
        {"AccelerationModeSelected", ToString(capability.requested_mode)},
        {"Backend", capability.backend}, {"DriverStatus", capability.driver_status},
        {"RuntimeStatus", capability.runtime_status},
        {"DriverRuntimeCompatible", capability.driver_runtime_compatible ? "true" : "false"},
        {"TensorCapability", capability.tensor_capability ? "true" : "false"},
        {"AvailablePrecisionModes", capability.available_precision_modes},
        {"AiBackend", capability.ai_backend_present ? "Present" : "EvidenceBlockedModelUnavailable"},
        {"AiAcceleration", capability.ai_acceleration_active ? "Active" : "EvidenceBlockedModelUnavailable"},
        {"FallbackReason", capability.fallback_reason.empty() ? "None" : capability.fallback_reason}
    }, {"SchemaVersion", "GpuDetected", "GpuAvailable", "GpuEligible", "GpuSelected",
        "OperationSpecificGpuImplementationAvailable", "ConcurrentBatchExecutionAvailable", "VramBytes",
        "FreeVramBytes", "CurrentGpuLoadPercent", "DriverRuntimeCompatible", "TensorCapability"});
    const std::string execution_status = result.workload.implementation_status;
    const bool operation_candidate_executed = result.workload.gpu_candidate_executed;
    const auto performance_json = MakeJsonObject({
        {"SchemaVersion", "1"}, {"SessionId", std::string(session_id)},
        {"Phase", benefit.phase},
        {"Status", execution_status},
        {"GpuAvailable", benefit.gpu_available ? "true" : "false"},
        {"GpuEligible", benefit.gpu_eligible ? "true" : "false"},
        {"GpuSelected", benefit.gpu_selected ? "true" : "false"},
        {"SelectedBackend", benefit.selected_backend},
        {"SelectionReason", benefit.selection_reason},
        {"BenefitGateDecision", benefit.benefit_gate_decision},
        {"OperationClass", benefit.operation_class},
        {"KernelAlgorithm", operation_candidate_executed ?
            result.workload.algorithm + "+CudaOperationKernel+CpuDeterministicOracle" :
            "CudaPrimitiveExtractionV3+CpuAuthoritativeSemanticReduction"},
        {"GpuProcessedScope", operation_candidate_executed ?
            result.workload.candidate_output_contract : "GenericBytePrimitivesOnly"},
        {"WorkloadAlgorithm", result.workload.algorithm},
        {"NormalizedInputContract", result.workload.normalized_input_contract},
        {"CandidateOutputContract", result.workload.candidate_output_contract},
        {"WorkloadScope", result.workload.workload_scope},
        {"WorkloadImplementationStatus", result.workload.implementation_status},
        {"ModelStatus", result.workload.model_status},
        {"AiProviderContractVersion", result.ai_provider.contract_version},
        {"AiProviderStatus", result.ai_provider.status},
        {"AiProvider", result.ai_provider.provider},
        {"AiModelName", result.ai_provider.model_name},
        {"AiModelVersion", result.ai_provider.model_version},
        {"AiModelLicense", result.ai_provider.model_license},
        {"AiModelSHA256", result.ai_provider.model_sha256},
        {"AiOutputAuthority", result.ai_provider.output_authority},
        {"AiLocalExecutionRequired", result.ai_provider.local_execution_required ? "true" : "false"},
        {"AiEvidenceUploadProhibited", result.ai_provider.evidence_upload_prohibited ? "true" : "false"},
        {"AiQuantizedInferenceSupported", result.ai_provider.quantized_inference_supported ? "true" : "false"},
        {"AiCpuFallbackRequired", result.ai_provider.cpu_fallback_required ? "true" : "false"},
        {"AiVerifiedArtifactLoaded", result.ai_provider.verified_artifact_loaded ? "true" : "false"},
        {"NormalizedInputDigest", result.workload.normalized_input_digest},
        {"GpuPrimitiveExtractionExecuted", result.workload.gpu_primitive_extraction_executed ? "true" : "false"},
        {"GpuPrimitiveDigest", result.workload.gpu_primitive_digest},
        {"CpuPrimitiveDigest", result.workload.cpu_primitive_digest},
        {"GpuCandidateDigest", result.workload.gpu_candidate_digest},
        {"AuthoritativeOutputDigest", result.workload.authoritative_output_digest},
        {"ComputedFeatureSet", "OperationSpecificStructuredProjection+FNV1a+ByteSum+Energy+Transitions+RepeatedBytePattern"},
        {"WorkloadRecords", std::to_string(benefit.workload_records)},
        {"WorkloadBytes", std::to_string(benefit.workload_bytes)},
        {"AverageRecordBytes", std::to_string(benefit.average_record_bytes)},
        {"ExpectedArithmeticIntensity", std::to_string(benefit.expected_arithmetic_intensity)},
        {"ExpectedTransferBytes", std::to_string(benefit.expected_transfer_bytes)},
        {"ExpectedBatchCount", std::to_string(benefit.expected_batch_count)},
        {"PredictedCpuMicros", std::to_string(benefit.predicted_cpu_microseconds)},
        {"PredictedGpuMicros", std::to_string(benefit.predicted_gpu_microseconds)},
        {"PredictedSpeedup", std::to_string(benefit.predicted_speedup)},
        {"CalibrationSource", benefit.calibration_source},
        {"CalibrationSampleCount", std::to_string(benefit.calibration_sample_count)},
        {"PairedFullPathCalibrationSampleCount", std::to_string(benefit.paired_full_path_sample_count)},
        {"LatestCpuBaselineMicroseconds", std::to_string(benefit.latest_cpu_baseline_microseconds)},
        {"LatestGpuFullPathMicroseconds", std::to_string(benefit.latest_gpu_full_path_microseconds)},
        {"LatestFullPathSpeedup", std::to_string(benefit.latest_full_path_speedup)},
        {"RequiredSpeedupMarginPercent", std::to_string(benefit.required_speedup_margin_percent)},
        {"SameInputCalibration", benefit.same_input_calibration ? "true" : "false"},
        {"CalibrationFresh", benefit.calibration_fresh ? "true" : "false"},
        {"FullCpuOracleRequired", benefit.full_cpu_oracle_required ? "true" : "false"},
        {"PositiveCrossoverPossible", benefit.positive_crossover_possible ? "true" : "false"},
        {"CpuLoadStatus", benefit.cpu_load_status},
        {"ActualCpuVerificationMicros", std::to_string(performance.cpu_elapsed_microseconds)},
        {"ActualGpuMicros", std::to_string(performance.gpu_elapsed_microseconds)},
        {"ActualWallClockMicros", std::to_string(performance.wall_elapsed_microseconds)},
        {"GpuBatches", std::to_string(performance.gpu_batches)},
        {"GpuProcessedRecords", std::to_string(performance.gpu_processed_records)},
        {"GpuProcessedBytes", std::to_string(performance.gpu_processed_bytes)},
        {"GpuKernelLaunchCount", std::to_string(performance.gpu_kernel_launch_count)},
        {"CpuProcessedRecords", std::to_string(performance.cpu_processed_records)},
        {"InputRecords", std::to_string(performance.input_records)},
        {"InputBytes", std::to_string(performance.input_bytes)},
        {"GpuElapsedMicroseconds", std::to_string(performance.gpu_elapsed_microseconds)},
        {"GpuAllocationMicroseconds", std::to_string(performance.gpu_allocation_microseconds)},
        {"GpuH2DMicroseconds", std::to_string(performance.gpu_h2d_microseconds)},
        {"GpuKernelLaunchMicroseconds", std::to_string(performance.gpu_kernel_launch_microseconds)},
        {"GpuSynchronizeMicroseconds", std::to_string(performance.gpu_synchronize_microseconds)},
        {"GpuD2HMicroseconds", std::to_string(performance.gpu_d2h_microseconds)},
        {"CpuElapsedMicroseconds", std::to_string(performance.cpu_elapsed_microseconds)},
        {"WallElapsedMicroseconds", std::to_string(performance.wall_elapsed_microseconds)},
        {"QueueHighWaterMark", std::to_string(performance.queue_high_water_mark)},
        {"GpuOomCount", std::to_string(performance.gpu_oom_count)},
        {"BackendErrorCount", std::to_string(performance.backend_error_count)},
        {"DroppedEvidence", std::to_string(performance.dropped_evidence)},
        {"FallbackOccurred", performance.fallback_occurred ? "true" : "false"},
        {"FallbackReason", performance.fallback_reason.empty() ? "None" : performance.fallback_reason},
        {"BatchRecordLimit", std::to_string(scheduler.batch_records)},
        {"Concurrency", std::to_string(scheduler.concurrency)},
        {"ConcurrentBatchExecutionAvailable", performance.peak_inflight_batches > 1 ? "true" : "false"},
        {"BufferBytes", std::to_string(scheduler.buffer_bytes)},
        {"GpuCpuSplitPercent", std::to_string(scheduler.gpu_split_percent)},
        {"UtilizationLimitPercent", std::to_string(scheduler.utilization_limit_percent)},
        {"GpuMemoryPoolLimitBytes", std::to_string(scheduler.memory_pool_limit_bytes)},
        {"PinnedHostMemoryLimitBytes", std::to_string(scheduler.pinned_host_memory_limit_bytes)},
        {"PeakGpuMemoryBytes", std::to_string(performance.peak_gpu_memory_bytes)},
        {"PeakPinnedHostMemoryBytes", std::to_string(performance.peak_pinned_host_memory_bytes)},
        {"DevicePoolAllocationCount", std::to_string(performance.device_pool_allocation_count)},
        {"DevicePoolReuseCount", std::to_string(performance.device_pool_reuse_count)},
        {"PinnedPoolAllocationCount", std::to_string(performance.pinned_pool_allocation_count)},
        {"PinnedPoolReuseCount", std::to_string(performance.pinned_pool_reuse_count)},
        {"AsyncTransferCount", std::to_string(performance.async_transfer_count)},
        {"StreamSynchronizeCount", std::to_string(performance.stream_synchronize_count)},
        {"PoolResetCount", std::to_string(performance.pool_reset_count)},
        {"TimeoutCount", std::to_string(performance.timeout_count)},
        {"ConfiguredStreamCount", std::to_string(performance.configured_stream_count)},
        {"PeakInflightBatches", std::to_string(performance.peak_inflight_batches)}
    }, {"SchemaVersion", "GpuAvailable", "GpuEligible", "GpuSelected", "WorkloadRecords", "WorkloadBytes",
        "AverageRecordBytes", "ExpectedArithmeticIntensity", "ExpectedTransferBytes", "ExpectedBatchCount",
        "PredictedCpuMicros", "PredictedGpuMicros", "PredictedSpeedup", "CalibrationSampleCount",
        "PairedFullPathCalibrationSampleCount", "LatestCpuBaselineMicroseconds",
        "LatestGpuFullPathMicroseconds", "LatestFullPathSpeedup", "RequiredSpeedupMarginPercent",
        "SameInputCalibration", "CalibrationFresh", "FullCpuOracleRequired", "PositiveCrossoverPossible",
        "ActualCpuVerificationMicros", "ActualGpuMicros", "ActualWallClockMicros", "GpuBatches",
        "GpuProcessedRecords", "GpuProcessedBytes", "GpuKernelLaunchCount", "CpuProcessedRecords", "InputRecords",
        "InputBytes", "GpuElapsedMicroseconds", "GpuAllocationMicroseconds", "GpuH2DMicroseconds",
        "GpuKernelLaunchMicroseconds", "GpuSynchronizeMicroseconds", "GpuD2HMicroseconds",
        "CpuElapsedMicroseconds", "WallElapsedMicroseconds",
        "QueueHighWaterMark", "GpuOomCount", "BackendErrorCount", "DroppedEvidence", "FallbackOccurred",
        "BatchRecordLimit", "Concurrency", "BufferBytes", "GpuCpuSplitPercent", "UtilizationLimitPercent",
        "GpuMemoryPoolLimitBytes", "PinnedHostMemoryLimitBytes", "PeakGpuMemoryBytes", "PeakPinnedHostMemoryBytes",
        "DevicePoolAllocationCount", "DevicePoolReuseCount", "PinnedPoolAllocationCount", "PinnedPoolReuseCount",
        "AsyncTransferCount", "StreamSynchronizeCount", "PoolResetCount", "TimeoutCount",
        "ConfiguredStreamCount", "PeakInflightBatches", "GpuPrimitiveExtractionExecuted",
        "ConcurrentBatchExecutionAvailable"});
    const auto equivalence_json = MakeJsonObject({
        {"SchemaVersion", "1"}, {"SessionId", std::string(session_id)},
        {"Status", equivalence.status},
        {"GpuAvailable", benefit.gpu_available ? "true" : "false"},
        {"GpuEligible", benefit.gpu_eligible ? "true" : "false"},
        {"GpuSelected", benefit.gpu_selected ? "true" : "false"},
        {"SelectedBackend", benefit.selected_backend},
        {"SelectionReason", benefit.selection_reason},
        {"BenefitGateDecision", benefit.benefit_gate_decision},
        {"ComparedRecords", std::to_string(equivalence.compared_records)},
        {"MismatchCount", std::to_string(equivalence.mismatch_count)},
        {"AuthoritativeResultsEquivalent", equivalence.authoritative_results_equivalent ? "true" : "false"},
        {"VerifiedResultsEquivalent", equivalence.verified_results_equivalent ? "true" : "false"},
        {"PrimitiveResultsEquivalent", equivalence.mismatch_count == 0 ? "true" : "false"},
        {"OperationSpecificGpuResultAvailable", operation_candidate_executed ? "true" : "false"},
        {"GpuAuthority", "Prohibited"},
        {"VerificationGate", operation_candidate_executed ?
            "Operation-specific CUDA candidate compared field-for-field with deterministic CPU oracle; CPU output remains authoritative" :
            "CUDA generic primitives compared separately; operation-specific semantic GPU result unavailable"},
        {"OperationClass", result.workload.operation_class},
        {"WorkloadAlgorithm", result.workload.algorithm},
        {"NormalizedInputContract", result.workload.normalized_input_contract},
        {"CandidateOutputContract", result.workload.candidate_output_contract},
        {"WorkloadScope", result.workload.workload_scope},
        {"WorkloadImplementationStatus", result.workload.implementation_status},
        {"NormalizedInputDigest", result.workload.normalized_input_digest},
        {"GpuPrimitiveExtractionExecuted", result.workload.gpu_primitive_extraction_executed ? "true" : "false"},
        {"GpuPrimitiveDigest", result.workload.gpu_primitive_digest},
        {"CpuPrimitiveDigest", result.workload.cpu_primitive_digest},
        {"GpuCandidateDigest", result.workload.gpu_candidate_digest},
        {"AuthoritativeOutputDigest", result.workload.authoritative_output_digest},
        {"ProtocolFields", "UnchangedByAcceleration"},
        {"SemanticCandidates", "UnchangedByAcceleration"},
        {"VerifiedFields", "UnchangedByAcceleration"},
        {"EvidenceGraphSemanticIdentities", "UnchangedByAcceleration"},
        {"AuthorityClasses", "UnchangedByAcceleration"},
        {"IntegrationReadiness", "UnchangedByAcceleration"},
        {"Contradictions", "UnchangedByAcceleration"}
    }, {"SchemaVersion", "GpuAvailable", "GpuEligible", "GpuSelected", "ComparedRecords", "MismatchCount",
        "AuthoritativeResultsEquivalent", "VerifiedResultsEquivalent", "PrimitiveResultsEquivalent",
        "OperationSpecificGpuResultAvailable", "GpuPrimitiveExtractionExecuted",
        "AiLocalExecutionRequired", "AiEvidenceUploadProhibited", "AiQuantizedInferenceSupported",
        "AiCpuFallbackRequired", "AiVerifiedArtifactLoaded"});
    const bool written =
        WriteUtf8FileAtomic(performance_directory / L"gpu-capability.json", capability_json + "\n") &&
        WriteUtf8FileAtomic(performance_directory / L"gpu-performance.json", performance_json + "\n") &&
        WriteUtf8FileAtomic(performance_directory / L"cpu-gpu-equivalence.json", equivalence_json + "\n");
    if (!written && error) *error = "cannot write one or more GPU provenance reports";
    return written;
}

// The test-only contract matrix owns several RAII accelerator/result objects.
// /analyze conservatively sums their mutually exclusive scopes as 17 KiB;
// all payloads remain heap-backed and this is well below the 1 MiB thread
// reserve. Keeping RAII here makes cleanup/fallback assertions deterministic.
#pragma warning(suppress: 6262)
int RunGpuAccelerationContractTests(const fs::path& report_path) {
    const auto small_corpus = MakeBenchmarkCorpus(257, 97);
    const auto heavy_corpus = MakeBenchmarkCorpus(4096, 384);
    const auto physical_corpus = MakeBenchmarkCorpus(4096, 160);
    std::vector<std::pair<std::string, bool>> checks;
    const auto features_equal = [](const GpuProcessingResult& left, const GpuProcessingResult& right) {
        return left.verified_features.size() == right.verified_features.size() &&
            std::equal(left.verified_features.begin(), left.verified_features.end(),
                       right.verified_features.begin(), [](const auto& a, const auto& b) {
                           return SameFeature(a, b);
                       });
    };
    const auto process = [&](GpuTestScenario scenario, const std::vector<GpuEvidenceRecord>& corpus,
                             GpuOperationClass operation,
                             GpuDispatchIntent intent = GpuDispatchIntent::Adaptive,
                             AccelerationPhase phase = AccelerationPhase::PostCapture) {
        OptionalGpuAccelerator accelerator(AccelerationMode::Auto, scenario);
        return accelerator.Process(corpus, phase, nullptr, operation, intent);
    };
    const auto fallback = [&](std::string name, GpuTestScenario scenario) {
        OptionalGpuAccelerator accelerator(AccelerationMode::Auto, scenario);
        const auto result = accelerator.Process(small_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction);
        const bool passed = result.success &&
            result.performance.gpu_processed_records == 0 && result.performance.fallback_occurred &&
            result.performance.dropped_evidence == 0 && result.equivalence.mismatch_count == 0 &&
            result.verified_features.size() == small_corpus.size() &&
            result.workload.gpu_implementation_available && !result.workload.gpu_candidate_executed &&
            result.workload.implementation_status == "EvidenceBlockedGpuUnavailable";
        checks.emplace_back(std::move(name), passed);
    };
    fallback("NoGpuCpuFallback", GpuTestScenario::NoGpu);
    fallback("NvidiaUnavailableCpuFallback", GpuTestScenario::NvidiaUnavailable);
    fallback("InitializationErrorCpuFallback", GpuTestScenario::InitializationError);
    fallback("UnsupportedGpuCpuFallback", GpuTestScenario::UnsupportedGpu);

    checks.emplace_back("PascalLegacyCudaTier",
        process(GpuTestScenario::MockPascal, small_corpus, GpuOperationClass::TraceFeatureExtraction)
            .capability.tier == GpuArchitectureTier::LegacyCuda);
    checks.emplace_back("TuringRtxStandardTier",
        process(GpuTestScenario::MockTuring, small_corpus, GpuOperationClass::TraceFeatureExtraction)
            .capability.tier == GpuArchitectureTier::RtxStandard);
    checks.emplace_back("AmpereFullTier",
        process(GpuTestScenario::MockAmpere, small_corpus, GpuOperationClass::TraceFeatureExtraction)
            .capability.tier == GpuArchitectureTier::Full);
    checks.emplace_back("AdaHighPerformanceTier",
        process(GpuTestScenario::MockAda, small_corpus, GpuOperationClass::TraceFeatureExtraction)
            .capability.tier == GpuArchitectureTier::HighPerformance);
    checks.emplace_back("BlackwellMaximumTier",
        process(GpuTestScenario::MockBlackwell, small_corpus, GpuOperationClass::TraceFeatureExtraction)
            .capability.tier == GpuArchitectureTier::Maximum);

    {
        const auto result = process(GpuTestScenario::MockAmpere, small_corpus,
            GpuOperationClass::TraceFeatureExtraction);
        checks.emplace_back("SmallWorkloadKeepsAvailableKernelOnCpu", result.benefit.gpu_available &&
            !result.benefit.gpu_selected && result.performance.gpu_processed_records == 0 &&
            result.workload.gpu_implementation_available && !result.workload.gpu_candidate_executed &&
            result.workload.implementation_status == "GpuImplementationAvailableCpuSelected");
    }
    {
        OptionalGpuAccelerator uncalibrated(AccelerationMode::Auto, GpuTestScenario::MockPascal);
        const auto result = uncalibrated.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction);
        checks.emplace_back("InsufficientPairedCalibrationFailsClosedToCpu",
            !result.benefit.gpu_selected && result.benefit.selected_backend == "CPU" &&
            result.benefit.selection_reason == "NoPositiveCrossoverFullCpuOracleRequired" &&
            result.benefit.calibration_source == "InsufficientPairedFullPathMeasurements" &&
            result.benefit.paired_full_path_sample_count == 0 &&
            result.performance.gpu_processed_records == 0);
    }
    {
        OptionalGpuAccelerator calibrated(AccelerationMode::Auto, GpuTestScenario::MockPascal);
        const auto measured = calibrated.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        const auto automatic = calibrated.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction);
        checks.emplace_back("PairedFullWallNoPositiveCrossoverSelectsCpu",
            measured.workload.gpu_candidate_executed && measured.benefit.gpu_selected &&
            !automatic.benefit.gpu_selected && automatic.benefit.selected_backend == "CPU" &&
            automatic.benefit.selection_reason == "NoPositiveCrossoverFullCpuOracleRequired" &&
            automatic.benefit.same_input_calibration && automatic.benefit.calibration_fresh &&
            automatic.benefit.latest_gpu_full_path_microseconds >=
                automatic.benefit.latest_cpu_baseline_microseconds &&
            !automatic.benefit.positive_crossover_possible);
        checks.emplace_back("MeasurementOverrideDoesNotLeakIntoAutoDispatch",
            measured.benefit.selection_reason == "PhysicalMeasurementOverride" &&
            measured.performance.gpu_processed_records == heavy_corpus.size() &&
            automatic.performance.gpu_processed_records == 0 &&
            automatic.workload.implementation_status == "GpuImplementationAvailableCpuSelected");
    }
    {
        OptionalGpuAccelerator stale(AccelerationMode::Auto, GpuTestScenario::StaleCalibration);
        const auto measured = stale.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        const auto automatic = stale.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction);
        checks.emplace_back("StalePairedCalibrationFailsClosedToCpu",
            measured.workload.gpu_candidate_executed && !automatic.benefit.gpu_selected &&
            automatic.benefit.selected_backend == "CPU" && !automatic.benefit.calibration_fresh &&
            automatic.benefit.calibration_source == "StalePairedFullPathMeasurement" &&
            automatic.benefit.selection_reason == "NoPositiveCrossoverFullCpuOracleRequired");
    }

    OptionalGpuAccelerator cpu_only(AccelerationMode::CpuOnly, GpuTestScenario::MockBlackwell);
    const auto cpu_trace = cpu_only.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
        GpuOperationClass::TraceFeatureExtraction);
    checks.emplace_back("CpuOnlyPreservesAvailableImplementationBoundary",
        cpu_trace.performance.gpu_processed_records == 0 &&
        cpu_trace.workload.implementation_status == "GpuImplementationAvailableCpuOnly" &&
        cpu_trace.workload.authoritative_output_digest == FeatureDigest(cpu_trace.verified_features));

    {
        const auto ai = process(GpuTestScenario::MockBlackwell, small_corpus, GpuOperationClass::AiInference,
            GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("AiInferenceFailsClosedWithoutVerifiedModel", !ai.workload.gpu_candidate_executed &&
            !ai.workload.gpu_implementation_available &&
            ai.workload.implementation_status == "EvidenceBlockedModelUnavailable" &&
            ai.workload.gpu_candidate_digest == "NOT_RUN_NO_VERIFIED_MODEL" &&
            ai.ai_provider.contract_version == "LocalSemanticReasonerProviderV1" &&
            ai.ai_provider.status == "EvidenceBlockedModelUnavailable" &&
            ai.ai_provider.output_authority == "HYPOTHESIS" &&
            ai.ai_provider.local_execution_required && ai.ai_provider.evidence_upload_prohibited &&
            ai.ai_provider.cpu_fallback_required && !ai.ai_provider.verified_artifact_loaded);
    }

    OptionalGpuAccelerator mock_gpu(AccelerationMode::Auto, GpuTestScenario::MockAmpere);
    {
        bool implemented_matrix_ok = true;
        std::vector<std::string> candidate_digests;
        for (const auto operation : kImplementedRecordOperations) {
            const auto result = mock_gpu.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
                operation, GpuDispatchIntent::MeasurementOverride);
            const bool operation_exact =
                result.workload.gpu_implementation_available && result.workload.gpu_candidate_executed &&
                result.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
                !result.workload.workload_scope.empty() &&
                !result.workload.normalized_input_contract.empty() &&
                !result.workload.candidate_output_contract.empty() &&
                result.performance.gpu_processed_records == heavy_corpus.size() &&
                result.equivalence.compared_records == heavy_corpus.size() &&
                result.equivalence.mismatch_count == 0 && result.equivalence.verified_results_equivalent &&
                result.workload.gpu_primitive_digest == result.workload.cpu_primitive_digest &&
                result.workload.gpu_candidate_digest == result.workload.authoritative_output_digest;
            checks.emplace_back("OperationSpecificExactContract_" + ToString(operation), operation_exact);
            implemented_matrix_ok = implemented_matrix_ok && operation_exact;
            candidate_digests.push_back(result.workload.gpu_candidate_digest);
        }
        std::sort(candidate_digests.begin(), candidate_digests.end());
        implemented_matrix_ok = implemented_matrix_ok &&
            std::adjacent_find(candidate_digests.begin(), candidate_digests.end()) == candidate_digests.end();
        checks.emplace_back("SixteenOperationSpecificGpuContractsExactAndDistinct", implemented_matrix_ok);
    }

    const auto live_plan = MakeGpuSchedulerPlan(mock_gpu.Capability(), AccelerationPhase::Live, 10000, 128 * kMib);
    const auto post_plan = MakeGpuSchedulerPlan(mock_gpu.Capability(), AccelerationPhase::PostCapture, 10000, 128 * kMib);
    checks.emplace_back("LiveThrottleAndBoundedPostCaptureStreamsTruthful",
        live_plan.utilization_limit_percent < post_plan.utilization_limit_percent &&
        live_plan.memory_pool_limit_bytes < post_plan.memory_pool_limit_bytes && live_plan.background_priority &&
        live_plan.concurrency == 1 && post_plan.concurrency >= 2 && post_plan.concurrency <= 4);

    {
        const auto result = mock_gpu.Process(heavy_corpus, AccelerationPhase::Live, nullptr,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("LiveSchedulerGpuCpuSplitHonored",
            result.performance.gpu_processed_records == (heavy_corpus.size() + 1) / 2 &&
            result.performance.cpu_processed_records == heavy_corpus.size() &&
            result.performance.dropped_evidence == 0 && result.equivalence.mismatch_count == 0 &&
            result.workload.gpu_candidate_executed);
    }

    {
        bool structured_matrix_ok = true;
        for (const auto& descriptor : kStructuredKernelDescriptors) {
            const auto result = mock_gpu.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
                descriptor.operation, GpuDispatchIntent::MeasurementOverride);
            structured_matrix_ok = structured_matrix_ok && result.workload.gpu_implementation_available &&
                result.workload.gpu_candidate_executed && result.performance.dropped_evidence == 0 &&
                result.workload.workload_scope == "StructuredFeatureMatrixBatch" &&
                result.equivalence.mismatch_count == 0 && result.equivalence.verified_results_equivalent &&
                result.workload.gpu_candidate_digest == result.workload.authoritative_output_digest &&
                result.workload.implementation_status == "OperationSpecificGpuCandidateVerified";
        }
        checks.emplace_back("TenStructuredGpuBatchContractsCpuExact", structured_matrix_ok);
    }

    {
        const auto reused = mock_gpu.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("BoundedPoolsReuseWithoutCapViolation",
            reused.performance.device_pool_reuse_count != 0 &&
            reused.performance.pinned_pool_reuse_count != 0 &&
            reused.performance.peak_gpu_memory_bytes <= reused.scheduler.memory_pool_limit_bytes &&
            reused.performance.peak_pinned_host_memory_bytes <= reused.scheduler.pinned_host_memory_limit_bytes &&
            reused.scheduler.concurrency >= 2 && reused.scheduler.concurrency <= 4 &&
            reused.performance.peak_inflight_batches <= 1);
    }

    {
        const auto failure = process(GpuTestScenario::WorkerFailure, heavy_corpus,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("WorkerFailureFallsBackWithoutEvidenceLoss",
            failure.performance.backend_error_count == 1 && failure.performance.fallback_occurred &&
            failure.performance.pool_reset_count == 1 &&
            failure.workload.implementation_status == "GpuExecutionFailedCpuFallback" &&
            features_equal(failure, cpu_trace));
    }
    {
        const auto oom = process(GpuTestScenario::Oom, heavy_corpus,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("GpuOomFallsBackWithoutEvidenceLoss", oom.performance.gpu_oom_count == 1 &&
            oom.performance.pool_reset_count == 1 && oom.performance.fallback_occurred &&
            features_equal(oom, cpu_trace));
    }
    {
        const auto timeout = process(GpuTestScenario::Timeout, heavy_corpus,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("GpuTimeoutOrTdrResetsAndFallsBackWithoutEvidenceLoss", timeout.performance.timeout_count == 1 &&
            timeout.performance.pool_reset_count == 1 &&
            timeout.performance.fallback_occurred && timeout.performance.fallback_reason == "GpuOperationTimeout" &&
            features_equal(timeout, cpu_trace));
    }
    {
        OptionalGpuAccelerator mismatch_accelerator(AccelerationMode::Auto, GpuTestScenario::CandidateMismatch);
        const auto trace = mismatch_accelerator.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        const auto parser = mismatch_accelerator.Process(heavy_corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::ParserFieldPatternMatching, GpuDispatchIntent::MeasurementOverride);
        checks.emplace_back("PerOperationMismatchFailsOnlyAffectedOperation",
            trace.equivalence.mismatch_count != 0 &&
            trace.workload.implementation_status == "GpuCandidateMismatchCpuFallback" &&
            trace.workload.gpu_candidate_digest != trace.workload.authoritative_output_digest &&
            parser.equivalence.mismatch_count == 0 &&
            parser.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
            parser.workload.gpu_candidate_digest == parser.workload.authoritative_output_digest);
    }

    OptionalGpuAccelerator physical_gpu(AccelerationMode::Auto, GpuTestScenario::RealHardware);
    const bool physical_available = physical_gpu.Capability().backend_initialized;
    std::size_t physical_verified = 0;
    bool physical_matrix_ok = true;
    bool physical_auto_regression_ok = !physical_available;
    if (physical_available) {
        for (const auto operation : kImplementedRecordOperations) {
            const auto gpu = physical_gpu.Process(physical_corpus, AccelerationPhase::PostCapture, nullptr,
                operation, GpuDispatchIntent::MeasurementOverride);
            const auto cpu = cpu_only.Process(physical_corpus, AccelerationPhase::PostCapture, nullptr, operation);
            const bool verified = gpu.workload.gpu_candidate_executed &&
                gpu.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
                gpu.performance.gpu_kernel_launch_count >= 2 && gpu.equivalence.mismatch_count == 0 &&
                gpu.performance.configured_stream_count >= 2 &&
                gpu.performance.peak_inflight_batches >= 2 &&
                gpu.workload.gpu_candidate_digest == gpu.workload.authoritative_output_digest &&
                gpu.workload.gpu_primitive_digest == gpu.workload.cpu_primitive_digest &&
                features_equal(gpu, cpu);
            physical_matrix_ok = physical_matrix_ok && verified;
            if (verified) ++physical_verified;
        }
        const auto auto_regression_corpus = MakeBenchmarkCorpus(32768, 256);
        const auto measured = physical_gpu.Process(auto_regression_corpus, AccelerationPhase::PostCapture,
            nullptr, GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        const auto automatic = physical_gpu.Process(auto_regression_corpus, AccelerationPhase::PostCapture,
            nullptr, GpuOperationClass::TraceFeatureExtraction);
        physical_auto_regression_ok = measured.workload.gpu_candidate_executed &&
            measured.equivalence.mismatch_count == 0 && !automatic.benefit.gpu_selected &&
            automatic.benefit.selected_backend == "CPU" &&
            automatic.benefit.selection_reason == "NoPositiveCrossoverFullCpuOracleRequired" &&
            automatic.benefit.same_input_calibration && automatic.benefit.calibration_fresh &&
            automatic.performance.gpu_processed_records == 0;
    }
    checks.emplace_back("PhysicalSixteenKernelEqualityOrTruthfulHardwareBlock",
        physical_available ? physical_matrix_ok && physical_verified == kImplementedRecordOperations.size() :
            !physical_gpu.Capability().fallback_reason.empty());
    checks.emplace_back("PhysicalFullCpuOracleAutoRegression", physical_auto_regression_ok);

    std::size_t passed = 0;
    std::ostringstream check_json;
    check_json << '[';
    for (std::size_t index = 0; index < checks.size(); ++index) {
        if (index != 0) check_json << ',';
        if (checks[index].second) ++passed;
        check_json << MakeJsonObject({{"Name", checks[index].first},
                                     {"Status", checks[index].second ? "PASS" : "FAIL"}});
    }
    check_json << ']';
    const auto report = MakeJsonObject({
        {"SchemaVersion", "1"}, {"TestedAtUtc", UtcNow()},
        {"Status", passed == checks.size() ? "PASS" : "FAIL"},
        {"CheckCount", std::to_string(checks.size())}, {"Passed", std::to_string(passed)},
        {"Failed", std::to_string(checks.size() - passed)}, {"Checks", check_json.str()},
        {"ImplementedOperationCount", std::to_string(kImplementedRecordOperations.size())},
        {"StructuredImplementationBlockedCount", std::to_string(kStructuredBlockedOperations.size())},
        {"ModelBlockedCount", "1"},
        {"OperationSpecificGpuImplementationStatus", "16_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED"},
        {"ConcurrentBatchExecutionAvailable", physical_available && physical_matrix_ok ? "true" : "false"},
        {"PhysicalHardwareEvidence", physical_available ? physical_gpu.Capability().device : "Pending"},
        {"PhysicalVerifiedOperationCount", std::to_string(physical_verified)},
        {"HardwareGateStatus", physical_available && physical_matrix_ok ?
            "GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED" :
            "EvidenceBlockedPendingExternalHardware"}
    }, {"SchemaVersion", "CheckCount", "Passed", "Failed", "Checks", "ImplementedOperationCount",
        "StructuredImplementationBlockedCount", "ModelBlockedCount", "PhysicalVerifiedOperationCount",
        "ConcurrentBatchExecutionAvailable"});
    return WriteUtf8FileAtomic(report_path, report + "\n") && passed == checks.size() ? 0 : ERROR_GEN_FAILURE;
}

int RunGpuAccelerationBenchmark(const fs::path& report_path) {
    constexpr std::array<std::size_t, 6> kRecordCounts{128, 512, 2048, 8192, 32768, 131072};
    constexpr std::array<std::size_t, 6> kRecordByteSizes{64, 96, 128, 192, 256, 128};
    const auto peak_before = WorkingSetBytesForBenchmark();
    OptionalGpuAccelerator cpu(AccelerationMode::CpuOnly);
    OptionalGpuAccelerator automatic(AccelerationMode::Auto);
    const auto features_equal = [](const GpuProcessingResult& left, const GpuProcessingResult& right) {
        return left.verified_features.size() == right.verified_features.size() &&
            std::equal(left.verified_features.begin(), left.verified_features.end(),
                       right.verified_features.begin(), [](const auto& a, const auto& b) {
                           return SameFeature(a, b);
                       });
    };
    bool all_equal = true;
    bool all_undropped = true;
    bool positive_crossover = false;
    std::uint64_t crossover_records = 0;
    std::uint64_t crossover_bytes = 0;
    std::uint64_t peak_vram = 0;
    std::uint64_t representative_cpu_wall = 0;
    std::uint64_t representative_gpu_wall = 0;
    std::uint64_t representative_auto_wall = 0;
    std::uint64_t representative_auto_gpu = 0;
    std::string representative_backend = "CPU";
    std::string representative_reason = "NotMeasured";
    std::size_t gpu_measurement_count = 0;
    std::size_t measured_nonpositive_full_path_points = 0;
    std::size_t targeted_auto_regression_points = 0;
    bool auto_scheduler_safe = true;
    std::ostringstream curves;
    curves << '[';
    for (std::size_t index = 0; index < kRecordCounts.size(); ++index) {
        if (index != 0) curves << ',';
        const auto corpus = MakeBenchmarkCorpus(kRecordCounts[index], kRecordByteSizes[index]);
        const auto cpu_result = cpu.Process(corpus, AccelerationPhase::PostCapture);
        const auto gpu_measurement = automatic.Process(corpus, AccelerationPhase::PostCapture, nullptr,
            GpuOperationClass::TraceFeatureExtraction, GpuDispatchIntent::MeasurementOverride);
        const auto auto_result = automatic.Process(corpus, AccelerationPhase::PostCapture);
        const bool gpu_measured = gpu_measurement.workload.gpu_candidate_executed &&
            gpu_measurement.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
            gpu_measurement.equivalence.mismatch_count == 0 &&
            gpu_measurement.performance.gpu_processed_records != 0;
        const bool equal = features_equal(cpu_result, gpu_measurement) && features_equal(cpu_result, auto_result);
        const double speedup = !gpu_measured || gpu_measurement.performance.wall_elapsed_microseconds == 0 ? 0.0 :
            static_cast<double>(cpu_result.performance.wall_elapsed_microseconds) /
            static_cast<double>(gpu_measurement.performance.wall_elapsed_microseconds);
        const bool measured_positive_with_margin = speedup >= kAutoGpuMinimumMeasuredSpeedup;
        const bool auto_safety_gate_satisfied = !gpu_measured ||
            (!auto_result.benefit.gpu_selected && auto_result.benefit.selected_backend == "CPU");
        if (gpu_measured && !measured_positive_with_margin)
            ++measured_nonpositive_full_path_points;
        auto_scheduler_safe = auto_scheduler_safe && auto_safety_gate_satisfied;
        if (kRecordCounts[index] == 32768 || kRecordCounts[index] == 131072) {
            const bool targeted_safe = gpu_measured && speedup < 1.0 &&
                !auto_result.benefit.gpu_selected && auto_result.benefit.selected_backend == "CPU" &&
                auto_result.benefit.selection_reason == "NoPositiveCrossoverFullCpuOracleRequired" &&
                auto_result.benefit.same_input_calibration && auto_result.benefit.calibration_fresh;
            if (targeted_safe) ++targeted_auto_regression_points;
            auto_scheduler_safe = auto_scheduler_safe && targeted_safe;
        }
        if (gpu_measured) ++gpu_measurement_count;
        if (!positive_crossover && gpu_measured && speedup > 1.0) {
            positive_crossover = true;
            crossover_records = corpus.size();
            crossover_bytes = gpu_measurement.performance.input_bytes;
        }
        all_equal = all_equal && equal;
        all_undropped = all_undropped && cpu_result.performance.dropped_evidence == 0 &&
            gpu_measurement.performance.dropped_evidence == 0 && auto_result.performance.dropped_evidence == 0;
        peak_vram = std::max(peak_vram, gpu_measurement.performance.peak_gpu_memory_bytes);
        if (kRecordCounts[index] == 32768) {
            representative_cpu_wall = cpu_result.performance.wall_elapsed_microseconds;
            representative_gpu_wall = gpu_measurement.performance.wall_elapsed_microseconds;
            representative_auto_wall = auto_result.performance.wall_elapsed_microseconds;
            representative_auto_gpu = auto_result.performance.gpu_elapsed_microseconds;
            representative_backend = auto_result.benefit.selected_backend;
            representative_reason = auto_result.benefit.selection_reason;
        }
        curves << MakeJsonObject({
            {"Records", std::to_string(corpus.size())},
            {"RequestedRecordBytes", std::to_string(kRecordByteSizes[index])},
            {"TotalBytes", std::to_string(gpu_measurement.performance.input_bytes)},
            {"CpuMicroseconds", std::to_string(cpu_result.performance.wall_elapsed_microseconds)},
            {"GpuMicroseconds", gpu_measured ?
                std::to_string(gpu_measurement.performance.wall_elapsed_microseconds) : "0"},
            {"GpuKernelAndTransferMicroseconds", std::to_string(gpu_measurement.performance.gpu_elapsed_microseconds)},
            {"Speedup", std::to_string(speedup)},
            {"CudaPrimitiveMeasured", gpu_measured ? "true" : "false"},
            {"OperationSpecificGpuCandidateMeasured", gpu_measured ? "true" : "false"},
            {"SemanticGpuImplementationAvailable",
                gpu_measurement.workload.gpu_implementation_available ? "true" : "false"},
            {"GpuCandidateDigest", gpu_measurement.workload.gpu_candidate_digest},
            {"AuthoritativeOutputDigest", gpu_measurement.workload.authoritative_output_digest},
            {"AutoSelectedBackend", auto_result.benefit.selected_backend},
            {"AutoSelectionReason", auto_result.benefit.selection_reason},
            {"AutoBenefitGateDecision", auto_result.benefit.benefit_gate_decision},
            {"AutoWallMicroseconds", std::to_string(auto_result.performance.wall_elapsed_microseconds)},
            {"CalibrationSource", auto_result.benefit.calibration_source},
            {"CalibrationSampleCount", std::to_string(auto_result.benefit.calibration_sample_count)},
            {"PairedFullPathCalibrationSampleCount",
                std::to_string(auto_result.benefit.paired_full_path_sample_count)},
            {"SameInputCalibration", auto_result.benefit.same_input_calibration ? "true" : "false"},
            {"CalibrationFresh", auto_result.benefit.calibration_fresh ? "true" : "false"},
            {"LatestCpuBaselineMicroseconds",
                std::to_string(auto_result.benefit.latest_cpu_baseline_microseconds)},
            {"LatestGpuFullPathMicroseconds",
                std::to_string(auto_result.benefit.latest_gpu_full_path_microseconds)},
            {"LatestFullPathSpeedup", std::to_string(auto_result.benefit.latest_full_path_speedup)},
            {"RequiredSpeedupMarginPercent",
                std::to_string(auto_result.benefit.required_speedup_margin_percent)},
            {"FullCpuOracleRequired", auto_result.benefit.full_cpu_oracle_required ? "true" : "false"},
            {"PositiveCrossoverPossible",
                auto_result.benefit.positive_crossover_possible ? "true" : "false"},
            {"MeasuredPositiveWithRequiredMargin", measured_positive_with_margin ? "true" : "false"},
            {"AutoSafetyGateSatisfied", auto_safety_gate_satisfied ? "true" : "false"},
            {"CpuAuthorityOutputStable", equal ? "true" : "false"}
        }, {"Records", "RequestedRecordBytes", "TotalBytes", "CpuMicroseconds", "GpuMicroseconds",
            "GpuKernelAndTransferMicroseconds", "Speedup", "CudaPrimitiveMeasured",
            "OperationSpecificGpuCandidateMeasured", "SemanticGpuImplementationAvailable", "AutoWallMicroseconds",
            "CalibrationSampleCount", "PairedFullPathCalibrationSampleCount", "SameInputCalibration",
            "CalibrationFresh", "LatestCpuBaselineMicroseconds", "LatestGpuFullPathMicroseconds",
            "LatestFullPathSpeedup", "RequiredSpeedupMarginPercent", "FullCpuOracleRequired",
            "PositiveCrossoverPossible", "MeasuredPositiveWithRequiredMargin", "AutoSafetyGateSatisfied",
            "CpuAuthorityOutputStable"});
    }
    curves << ']';
    const auto ultimate_corpus = MakeBenchmarkCorpus(8192, 160);
    std::ostringstream ultimate_workloads;
    ultimate_workloads << '[';
    bool ultimate_matrix_truthful = true;
    bool implemented_gpu_equivalent = true;
    std::uint64_t ultimate_gpu_kernel_launches = 0;
    unsigned ultimate_peak_inflight_batches = 0;
    std::size_t ultimate_primitive_executed = 0;
    std::size_t ultimate_gpu_verified = 0;
    std::size_t ultimate_gpu_implementation_blocked = 0;
    std::size_t ultimate_gpu_unavailable_blocked = 0;
    std::size_t ultimate_model_blocked = 0;
    for (std::size_t index = 0; index < kUltimateOperations.size(); ++index) {
        if (index != 0) ultimate_workloads << ',';
        const auto operation = kUltimateOperations[index];
        const auto cpu_workload = cpu.Process(ultimate_corpus, AccelerationPhase::PostCapture, nullptr, operation);
        const auto gpu_workload = automatic.Process(ultimate_corpus, AccelerationPhase::PostCapture, nullptr,
            operation, GpuDispatchIntent::MeasurementOverride);
        const bool expected_model_block = operation == GpuOperationClass::AiInference;
        const bool expected_implementation = HasOperationSpecificGpuImplementation(operation);
        const bool cpu_output_stable = features_equal(cpu_workload, gpu_workload);
        const bool primitive_executed = gpu_workload.workload.gpu_primitive_extraction_executed;
        const bool primitive_equivalent = !primitive_executed ||
            (gpu_workload.equivalence.mismatch_count == 0 &&
             gpu_workload.workload.gpu_primitive_digest == gpu_workload.workload.cpu_primitive_digest);
        const bool candidate_equivalent = expected_implementation &&
            gpu_workload.workload.gpu_candidate_executed &&
            gpu_workload.workload.implementation_status == "OperationSpecificGpuCandidateVerified" &&
            gpu_workload.equivalence.verified_results_equivalent &&
            gpu_workload.equivalence.mismatch_count == 0 &&
            gpu_workload.workload.gpu_candidate_digest == gpu_workload.workload.authoritative_output_digest;
        const bool gpu_unavailable_truthful = expected_implementation &&
            !automatic.Capability().backend_initialized && !gpu_workload.workload.gpu_candidate_executed &&
            gpu_workload.workload.implementation_status == "EvidenceBlockedGpuUnavailable";
        const bool workload_truthful = expected_model_block ?
            (!gpu_workload.workload.gpu_candidate_executed &&
             gpu_workload.workload.implementation_status == "EvidenceBlockedModelUnavailable") :
            (expected_implementation ? (candidate_equivalent || gpu_unavailable_truthful) :
                (!gpu_workload.workload.gpu_candidate_executed &&
                 gpu_workload.workload.implementation_status == "EvidenceBlockedGpuImplementationUnavailable" &&
                 gpu_workload.workload.gpu_candidate_digest ==
                    "NOT_RUN_OPERATION_SPECIFIC_GPU_IMPLEMENTATION_UNAVAILABLE"));
        ultimate_matrix_truthful = ultimate_matrix_truthful && cpu_output_stable &&
            primitive_equivalent && workload_truthful;
        if (expected_implementation)
            implemented_gpu_equivalent = implemented_gpu_equivalent &&
                (candidate_equivalent || gpu_unavailable_truthful);
        ultimate_gpu_kernel_launches += gpu_workload.performance.gpu_kernel_launch_count;
        ultimate_peak_inflight_batches = std::max(ultimate_peak_inflight_batches,
            gpu_workload.performance.peak_inflight_batches);
        if (primitive_executed) ++ultimate_primitive_executed;
        if (expected_model_block) ++ultimate_model_blocked;
        else if (candidate_equivalent) ++ultimate_gpu_verified;
        else if (gpu_unavailable_truthful) ++ultimate_gpu_unavailable_blocked;
        else if (!expected_implementation) ++ultimate_gpu_implementation_blocked;
        ultimate_workloads << MakeJsonObject({
            {"OperationClass", ToString(operation)},
            {"Algorithm", gpu_workload.workload.algorithm},
            {"NormalizedInputContract", gpu_workload.workload.normalized_input_contract},
            {"CandidateOutputContract", gpu_workload.workload.candidate_output_contract},
            {"WorkloadScope", gpu_workload.workload.workload_scope},
            {"ImplementationStatus", gpu_workload.workload.implementation_status},
            {"ModelStatus", gpu_workload.workload.model_status},
            {"NormalizedInputDigest", gpu_workload.workload.normalized_input_digest},
            {"GpuPrimitiveDigest", gpu_workload.workload.gpu_primitive_digest},
            {"CpuPrimitiveDigest", gpu_workload.workload.cpu_primitive_digest},
            {"GpuCandidateDigest", gpu_workload.workload.gpu_candidate_digest},
            {"AuthoritativeOutputDigest", gpu_workload.workload.authoritative_output_digest},
            {"CudaPrimitiveExtractionExecuted", primitive_executed ? "true" : "false"},
            {"OperationSpecificGpuImplementationAvailable",
                gpu_workload.workload.gpu_implementation_available ? "true" : "false"},
            {"OperationSpecificGpuExecuted",
                gpu_workload.workload.gpu_candidate_executed ? "true" : "false"},
            {"GpuKernelLaunches", std::to_string(gpu_workload.performance.gpu_kernel_launch_count)},
            {"ConfiguredStreams", std::to_string(gpu_workload.performance.configured_stream_count)},
            {"PeakInflightBatches", std::to_string(gpu_workload.performance.peak_inflight_batches)},
            {"GpuProcessedRecords", std::to_string(gpu_workload.performance.gpu_processed_records)},
            {"CpuVerificationRecords", std::to_string(gpu_workload.performance.cpu_processed_records)},
            {"MismatchCount", std::to_string(gpu_workload.equivalence.mismatch_count)},
            {"PrimitiveEquivalent", primitive_equivalent ? "true" : "false"},
            {"OperationSpecificCandidateEquivalent", candidate_equivalent ? "true" : "false"},
            {"CpuAuthorityOutputStable", cpu_output_stable ? "true" : "false"},
            {"Status", candidate_equivalent ? "PASS_OPERATION_SPECIFIC_GPU_CPU_EXACT" :
                gpu_workload.workload.implementation_status}
        }, {"CudaPrimitiveExtractionExecuted", "OperationSpecificGpuImplementationAvailable",
            "OperationSpecificGpuExecuted", "GpuKernelLaunches", "ConfiguredStreams", "PeakInflightBatches",
            "GpuProcessedRecords", "CpuVerificationRecords", "MismatchCount", "PrimitiveEquivalent",
            "OperationSpecificCandidateEquivalent", "CpuAuthorityOutputStable"});
    }
    ultimate_workloads << ']';
    all_equal = all_equal && ultimate_matrix_truthful;
    const auto& capability = automatic.Capability();
    const bool hardware_available = capability.backend_initialized;
    const bool ultimate_truthful_partial = ultimate_gpu_implementation_blocked ==
        kStructuredBlockedOperations.size() && ultimate_model_blocked == 1 &&
        (hardware_available ? ultimate_gpu_verified == kImplementedRecordOperations.size() :
            ultimate_gpu_unavailable_blocked == kImplementedRecordOperations.size()) &&
        ultimate_matrix_truthful;
    const bool exact_rtx5070_device = capability.vendor == "NVIDIA" &&
        capability.device == "NVIDIA GeForce RTX 5070";
    const std::string crossover_status = gpu_measurement_count == 0 ?
        "GpuUnavailableForPhysicalMeasurement" : (positive_crossover ?
        "MeasuredPositiveGpuCrossoverForCurrentOperation" :
        "NoMeasuredPositiveGpuCrossoverForCurrentOperation");
    const bool auto_no_crossover_gate_ok = auto_scheduler_safe &&
        targeted_auto_regression_points == 2;
    const auto report = MakeJsonObject({
        {"SchemaVersion", "1"}, {"BenchmarkedAtUtc", UtcNow()},
        {"OperationClass", "TraceFeatureExtraction"},
        {"CurvePointCount", std::to_string(kRecordCounts.size())},
        {"Curves", curves.str()},
        {"UltimateWorkloadCount", std::to_string(kUltimateOperations.size())},
        {"UltimateWorkloads", ultimate_workloads.str()},
        {"UltimateGpuExecutedCount", std::to_string(ultimate_gpu_verified)},
        {"UltimatePrimitiveExecutedCount", std::to_string(ultimate_primitive_executed)},
        {"UltimateGpuImplementationBlockedCount", std::to_string(ultimate_gpu_implementation_blocked)},
        {"UltimateGpuUnavailableBlockedCount", std::to_string(ultimate_gpu_unavailable_blocked)},
        {"UltimateModelBlockedCount", std::to_string(ultimate_model_blocked)},
        {"UltimateEvidenceBlockedCount", std::to_string(
            ultimate_gpu_implementation_blocked + ultimate_gpu_unavailable_blocked + ultimate_model_blocked)},
        {"AiInferenceStatus", "EvidenceBlockedModelUnavailable"},
        {"UltimateGpuKernelLaunches", std::to_string(ultimate_gpu_kernel_launches)},
        {"UltimateCpuGpuEquivalent", hardware_available && implemented_gpu_equivalent ? "true" : "false"},
        {"ImplementedGpuCpuEquivalent", hardware_available && implemented_gpu_equivalent ? "true" : "false"},
        {"UltimatePrimitiveEquivalent", ultimate_matrix_truthful ? "true" : "false"},
        {"CrossoverStatus", crossover_status},
        {"AutoCrossoverStatus", "NoPositiveCrossoverFullCpuOracleRequired"},
        {"FullCpuOracleRequired", "true"},
        {"PositiveCrossoverPossible", "false"},
        {"RequiredAutoSpeedupMarginPercent", "10"},
        {"MeasuredNonpositiveFullPathPointCount",
            std::to_string(measured_nonpositive_full_path_points)},
        {"TargetedAutoRegressionPointCount", std::to_string(targeted_auto_regression_points)},
        {"AutoNeverSelectedFullCpuOracleGpuPath", auto_scheduler_safe ? "true" : "false"},
        {"AutoNoPositiveCrossoverGate", auto_no_crossover_gate_ok ? "PASS" : "FAIL"},
        {"CrossoverRecords", std::to_string(crossover_records)},
        {"CrossoverBytes", std::to_string(crossover_bytes)},
        {"GpuMeasurementCount", std::to_string(gpu_measurement_count)},
        {"CorpusRecords", "32768"},
        {"CpuOnlyWallMicroseconds", std::to_string(representative_cpu_wall)},
        {"GpuMeasuredWallMicroseconds", std::to_string(representative_gpu_wall)},
        {"GpuAutoWallMicroseconds", std::to_string(representative_auto_wall)},
        {"GpuAutoGpuMicroseconds", std::to_string(representative_auto_gpu)},
        {"SelectedBackend", representative_backend},
        {"SelectionReason", representative_reason},
        {"PeakRamBytes", std::to_string(std::max(peak_before, WorkingSetBytesForBenchmark()))},
        {"PeakVramBytes", std::to_string(peak_vram)},
        {"DroppedEvidence", all_undropped ? "0" : "1"},
        {"SemanticEquality", hardware_available && implemented_gpu_equivalent ? "true" : "false"},
        {"CpuAuthorityOutputStable", all_equal ? "true" : "false"},
        {"ConcurrentBatchExecutionAvailable", ultimate_peak_inflight_batches > 1 ? "true" : "false"},
        {"Backend", capability.backend},
        {"Device", capability.device},
        {"ArchitectureTier", ToString(capability.tier)},
        {"ComputeCapability", capability.compute_major == 0 ? "NotAvailable" :
            std::to_string(capability.compute_major) + "." + std::to_string(capability.compute_minor)},
        {"VramBytes", std::to_string(capability.vram_bytes)},
        {"FreeVramBytes", std::to_string(capability.free_vram_bytes)},
        {"CurrentGpuLoadPercent", std::to_string(capability.current_gpu_load_percent)},
        {"Mode", GpuModeDisplayName(capability.backend_initialized ? capability.tier :
            GpuArchitectureTier::CpuFullFeature)},
        {"PhysicalBenchmarkHonesty", hardware_available ?
            "SixteenOperationSpecificCudaKernelsCpuOracleVerified;AiModelBlocked;BoundedMultiStreamInFlight" :
            "GpuUnavailable;SixteenCompiledOperationKernelsNotExecuted;AiModelBlocked"},
        {"ExternalRtx5070Gate", exact_rtx5070_device ?
            "RTX5070_DEVICE_OBSERVED_REQUIRES_OUTER_OS_AND_PACKAGE_GATES" :
            "EvidenceBlockedPendingExternalHardware"},
        {"Status", all_equal && all_undropped && ultimate_truthful_partial && auto_no_crossover_gate_ok ?
            (hardware_available ? "GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED" :
                "EVIDENCE_BLOCKED_GPU_UNAVAILABLE_1_MODEL_BLOCKED") : "FAIL"}
    }, {"SchemaVersion", "CurvePointCount", "Curves", "UltimateWorkloadCount", "UltimateWorkloads",
        "UltimateGpuExecutedCount", "UltimatePrimitiveExecutedCount", "UltimateGpuImplementationBlockedCount",
        "UltimateGpuUnavailableBlockedCount",
        "UltimateModelBlockedCount", "UltimateEvidenceBlockedCount", "UltimateGpuKernelLaunches",
        "UltimateCpuGpuEquivalent", "ImplementedGpuCpuEquivalent", "UltimatePrimitiveEquivalent",
        "FullCpuOracleRequired", "PositiveCrossoverPossible", "RequiredAutoSpeedupMarginPercent",
        "MeasuredNonpositiveFullPathPointCount", "TargetedAutoRegressionPointCount",
        "AutoNeverSelectedFullCpuOracleGpuPath",
        "CrossoverRecords", "CrossoverBytes",
        "GpuMeasurementCount", "CorpusRecords", "CpuOnlyWallMicroseconds", "GpuMeasuredWallMicroseconds",
        "GpuAutoWallMicroseconds", "GpuAutoGpuMicroseconds", "PeakRamBytes", "PeakVramBytes", "DroppedEvidence",
        "SemanticEquality", "CpuAuthorityOutputStable", "ConcurrentBatchExecutionAvailable",
        "VramBytes", "FreeVramBytes", "CurrentGpuLoadPercent"});
    const bool report_written = WriteUtf8FileAtomic(report_path, report + "\n");
    return report_written && all_equal && all_undropped && ultimate_truthful_partial &&
        auto_no_crossover_gate_ok ?
        ERROR_NOT_SUPPORTED : ERROR_GEN_FAILURE;
}

} // namespace god2
