#include "UltimateRecovery.h"

#include "EvidencePackage.h"

#include <bcrypt.h>

#include <algorithm>
#include <array>
#include <charconv>
#include <cctype>
#include <cmath>
#include <cstring>
#include <cwctype>
#include <deque>
#include <fstream>
#include <iomanip>
#include <initializer_list>
#include <limits>
#include <mutex>
#include <new>
#include <numeric>
#include <set>
#include <sstream>
#include <stdexcept>
#include <type_traits>
#include <unordered_map>
#include <unordered_set>

#pragma comment(lib, "bcrypt.lib")

namespace god2 {
namespace {

constexpr std::string_view kUltimateManifestSchema = "god2-ultimate-package-manifest-v1";
constexpr std::string_view kUltimateRecoverySchema = "god2-ultimate-recovery-v1";
constexpr std::string_view kExpectedClientName = "God2_opt.exe";
constexpr std::string_view kExpectedClientArchitecture = "x86";
constexpr std::string_view kExpectedClientVersion = "1.0.0.1";
constexpr std::string_view kExpectedClientSha256 =
    "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
constexpr std::uint32_t kMaximumTaintHops = 64;

struct UltimateScopedHandle final {
    HANDLE value = INVALID_HANDLE_VALUE;
    UltimateScopedHandle() = default;
    explicit UltimateScopedHandle(HANDLE handle) noexcept : value(handle) {}
    ~UltimateScopedHandle() {
        if (value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value);
    }
    UltimateScopedHandle(const UltimateScopedHandle&) = delete;
    UltimateScopedHandle& operator=(const UltimateScopedHandle&) = delete;
    UltimateScopedHandle(UltimateScopedHandle&& other) noexcept : value(other.value) {
        other.value = INVALID_HANDLE_VALUE;
    }
    UltimateScopedHandle& operator=(UltimateScopedHandle&& other) noexcept {
        if (this != &other) {
            if (value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value);
            value = other.value;
            other.value = INVALID_HANDLE_VALUE;
        }
        return *this;
    }
    explicit operator bool() const noexcept {
        return value != INVALID_HANDLE_VALUE && value != nullptr;
    }
};

std::wstring FoldUltimatePath(std::wstring value) {
    std::replace(value.begin(), value.end(), L'/', L'\\');
    while (value.size() > 3U && value.back() == L'\\') value.pop_back();
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t character) {
        return static_cast<wchar_t>(std::towlower(character));
    });
    return value;
}

std::wstring StripUltimateExtendedPrefix(std::wstring value) {
    if (value.starts_with(L"\\\\?\\UNC\\")) return L"\\\\" + value.substr(8U);
    if (value.starts_with(L"\\\\?\\")) return value.substr(4U);
    return value;
}

std::optional<fs::path> UltimateFinalPath(HANDLE handle) {
    constexpr DWORD flags = FILE_NAME_NORMALIZED | VOLUME_NAME_DOS;
    const DWORD required = GetFinalPathNameByHandleW(handle, nullptr, 0, flags);
    if (required == 0U) return std::nullopt;
    std::vector<wchar_t> buffer(static_cast<std::size_t>(required) + 1U);
    const DWORD written = GetFinalPathNameByHandleW(handle, buffer.data(),
        static_cast<DWORD>(buffer.size()), flags);
    if (written == 0U || written >= buffer.size()) return std::nullopt;
    return fs::path(StripUltimateExtendedPrefix(std::wstring(buffer.data(), written)));
}

bool SameUltimateFileObject(const BY_HANDLE_FILE_INFORMATION& left,
                            const BY_HANDLE_FILE_INFORMATION& right) noexcept {
    return left.dwVolumeSerialNumber == right.dwVolumeSerialNumber &&
        left.nFileIndexHigh == right.nFileIndexHigh &&
        left.nFileIndexLow == right.nFileIndexLow;
}

std::uint64_t UltimateHandleSize(const BY_HANDLE_FILE_INFORMATION& information) noexcept {
    return (static_cast<std::uint64_t>(information.nFileSizeHigh) << 32U) |
        information.nFileSizeLow;
}

bool UltimatePathContained(const fs::path& root, const fs::path& candidate) {
    const auto folded_root = FoldUltimatePath(root.lexically_normal().wstring());
    const auto folded_candidate = FoldUltimatePath(candidate.lexically_normal().wstring());
    if (folded_root.empty() || folded_candidate.size() < folded_root.size() ||
        folded_candidate.compare(0U, folded_root.size(), folded_root) != 0)
        return false;
    return folded_candidate.size() == folded_root.size() ||
        folded_candidate[folded_root.size()] == L'\\';
}

std::optional<std::string> UltimateSha256ByHandle(HANDLE handle) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD object_bytes = 0;
    DWORD digest_bytes = 0;
    DWORD returned = 0;
    std::vector<unsigned char> object;
    std::array<unsigned char, 32> digest{};
    LARGE_INTEGER zero{};
    bool success = handle != INVALID_HANDLE_VALUE && handle != nullptr &&
        SetFilePointerEx(handle, zero, nullptr, FILE_BEGIN) != FALSE &&
        BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0 &&
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
            reinterpret_cast<PUCHAR>(&object_bytes), sizeof(object_bytes), &returned, 0) >= 0 &&
        BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH,
            reinterpret_cast<PUCHAR>(&digest_bytes), sizeof(digest_bytes), &returned, 0) >= 0 &&
        digest_bytes == digest.size();
    if (success) {
        object.resize(object_bytes);
        success = BCryptCreateHash(algorithm, &hash, object.data(), object_bytes,
                                   nullptr, 0, 0) >= 0;
    }
    std::vector<unsigned char> buffer(64U * 1024U);
    while (success) {
        DWORD read = 0;
        if (ReadFile(handle, buffer.data(), static_cast<DWORD>(buffer.size()), &read,
                     nullptr) == FALSE) {
            success = false;
            break;
        }
        if (read == 0U) break;
        success = BCryptHashData(hash, buffer.data(), read, 0) >= 0;
    }
    if (success) success = BCryptFinishHash(hash, digest.data(),
                                             static_cast<ULONG>(digest.size()), 0) >= 0;
    if (hash != nullptr) BCryptDestroyHash(hash);
    if (algorithm != nullptr) BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!success) return std::nullopt;
    static constexpr char alphabet[] = "0123456789ABCDEF";
    std::string result(digest.size() * 2U, '0');
    for (std::size_t index = 0; index < digest.size(); ++index) {
        result[index * 2U] = alphabet[digest[index] >> 4U];
        result[index * 2U + 1U] = alphabet[digest[index] & 0x0FU];
    }
    return result;
}

std::optional<std::string> UltimateSha256Bytes(std::string_view value) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD object_bytes = 0;
    DWORD digest_bytes = 0;
    DWORD returned = 0;
    std::vector<unsigned char> object;
    std::array<unsigned char, 32> digest{};
    bool success =
        BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM,
                                    nullptr, 0) >= 0 &&
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
            reinterpret_cast<PUCHAR>(&object_bytes), sizeof(object_bytes),
            &returned, 0) >= 0 &&
        BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH,
            reinterpret_cast<PUCHAR>(&digest_bytes), sizeof(digest_bytes),
            &returned, 0) >= 0 &&
        digest_bytes == digest.size() &&
        value.size() <= (std::numeric_limits<ULONG>::max)();
    if (success) {
        object.resize(object_bytes);
        success = BCryptCreateHash(algorithm, &hash, object.data(), object_bytes,
                                   nullptr, 0, 0) >= 0;
    }
    if (success && !value.empty()) {
        success = BCryptHashData(hash,
            reinterpret_cast<PUCHAR>(const_cast<char*>(value.data())),
            static_cast<ULONG>(value.size()), 0) >= 0;
    }
    if (success) success = BCryptFinishHash(hash, digest.data(),
        static_cast<ULONG>(digest.size()), 0) >= 0;
    if (hash != nullptr) BCryptDestroyHash(hash);
    if (algorithm != nullptr) BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!success) return std::nullopt;
    static constexpr char alphabet[] = "0123456789ABCDEF";
    std::string result(digest.size() * 2U, '0');
    for (std::size_t index = 0; index < digest.size(); ++index) {
        result[index * 2U] = alphabet[digest[index] >> 4U];
        result[index * 2U + 1U] = alphabet[digest[index] & 0x0FU];
    }
    return result;
}

bool ReadPinnedPeArchitecture(HANDLE handle, std::string* architecture) {
    const auto read_exact = [&](std::uint64_t offset, void* destination, DWORD bytes) {
        LARGE_INTEGER position{};
        position.QuadPart = static_cast<LONGLONG>(offset);
        DWORD read = 0;
        return SetFilePointerEx(handle, position, nullptr, FILE_BEGIN) != FALSE &&
            ReadFile(handle, destination, bytes, &read, nullptr) != FALSE && read == bytes;
    };
    IMAGE_DOS_HEADER dos{};
    DWORD signature = 0;
    IMAGE_FILE_HEADER header{};
    if (!read_exact(0U, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE ||
        dos.e_lfanew <= 0 ||
        !read_exact(static_cast<std::uint64_t>(dos.e_lfanew), &signature,
                    sizeof(signature)) || signature != IMAGE_NT_SIGNATURE ||
        !read_exact(static_cast<std::uint64_t>(dos.e_lfanew) + sizeof(signature),
                    &header, sizeof(header)))
        return false;
    *architecture = header.Machine == IMAGE_FILE_MACHINE_I386 ? "x86" :
        header.Machine == IMAGE_FILE_MACHINE_AMD64 ? "x64" : "Other";
    return true;
}

template <typename Enum, std::size_t Size>
const char* EnumName(Enum value, const std::array<const char*, Size>& names,
                     const char* fallback) noexcept {
    const auto index = static_cast<std::size_t>(value);
    return index < names.size() ? names[index] : fallback;
}

constexpr std::array<const char*, 7> kAuthorityNames = {
    "VERIFIED", "DERIVED", "OBSERVED", "HYPOTHESIS", "UNKNOWN",
    "UNKNOWN_SERVER_ONLY", "REJECTED"
};

constexpr std::array<const char*, kUltimateSemanticEventTypeCount> kEventTypeNames = {
    "PacketBoundary", "ParserRead", "SerializerWrite", "HandlerInvocation",
    "HandlerArgument", "ObjectAllocated", "ObjectDestroyed", "ObjectResolved",
    "ObjectLookup", "RegistryLocated", "RegistryEnumerated", "ResourceRead",
    "ResourceDecoded", "ResourceDeserialized", "StateMutation", "TaintSeed",
    "TaintPropagation", "ValueFlow", "FormulaOperand", "FormulaResult",
    "FunctionRoleCandidate", "UIAnchor", "SnapshotObject", "SnapshotEdge",
    "ProbeDiagnostic"
};

constexpr std::array<UltimateSemanticEventCapability,
                     kUltimateSemanticEventTypeCount> kSemanticCapabilities = {{
    {UltimateSemanticEventType::PacketBoundary, UltimateSemanticDispatchSink::ProtocolRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, false},
    {UltimateSemanticEventType::ParserRead, UltimateSemanticDispatchSink::ProtocolRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, false},
    {UltimateSemanticEventType::SerializerWrite, UltimateSemanticDispatchSink::ProtocolRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, false},
    {UltimateSemanticEventType::HandlerInvocation, UltimateSemanticDispatchSink::ProtocolRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, false},
    {UltimateSemanticEventType::HandlerArgument, UltimateSemanticDispatchSink::ProtocolRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, false},
    {UltimateSemanticEventType::ObjectAllocated, UltimateSemanticDispatchSink::ObjectRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ObjectDestroyed, UltimateSemanticDispatchSink::ObjectRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ObjectResolved, UltimateSemanticDispatchSink::ObjectRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ObjectLookup, UltimateSemanticDispatchSink::ObjectRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::RegistryLocated, UltimateSemanticDispatchSink::RegistryRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::RegistryEnumerated, UltimateSemanticDispatchSink::RegistryRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ResourceRead, UltimateSemanticDispatchSink::ResourceRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ResourceDecoded, UltimateSemanticDispatchSink::ResourceRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ResourceDeserialized, UltimateSemanticDispatchSink::ResourceRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::StateMutation, UltimateSemanticDispatchSink::MutationRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::TaintSeed, UltimateSemanticDispatchSink::ValueProvenanceRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::TaintPropagation, UltimateSemanticDispatchSink::ValueProvenanceRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ValueFlow, UltimateSemanticDispatchSink::ValueProvenanceRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::FormulaOperand, UltimateSemanticDispatchSink::FormulaRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::FormulaResult, UltimateSemanticDispatchSink::FormulaRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::FunctionRoleCandidate, UltimateSemanticDispatchSink::CandidateLedger,
     UltimateSemanticPromotionPolicy::NoPromotion, true, true, true},
    {UltimateSemanticEventType::UIAnchor, UltimateSemanticDispatchSink::ContentRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::SnapshotObject, UltimateSemanticDispatchSink::SnapshotRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::SnapshotEdge, UltimateSemanticDispatchSink::SnapshotRecovery,
     UltimateSemanticPromotionPolicy::StandardEvidenceGates, true, true, true},
    {UltimateSemanticEventType::ProbeDiagnostic, UltimateSemanticDispatchSink::DiagnosticLedger,
     UltimateSemanticPromotionPolicy::NoPromotion, true, true, true}
}};

static_assert(kSemanticCapabilities.size() == kUltimateSemanticEventTypeCount,
              "every semantic event type requires an explicit dispatch row");

constexpr std::array<const char*, kUltimateProbeDomainCount> kProbeDomainNames = {
    "NetworkProbe", "ParserProbe", "SerializerProbe", "HandlerProbe", "ObjectProbe",
    "AllocationProbe", "VTableProbe", "FactoryProbe", "ManagerLookupProbe",
    "RegistryProbe", "ResourceDecodeProbe", "MutationProbe", "TaintSeedProbe",
    "FormulaOperandProbe", "QuestProbe", "MapProbe", "PortalProbe", "NPCProbe",
    "MonsterProbe", "BattleProbe", "InventoryProbe", "ItemProbe", "SkillProbe",
    "Pet/MountProbe", "SnapshotProbe"
};

constexpr std::array<UltimateDeepRuntimeProducerDefinition,
                     kUltimateFundamentalProducerCount> kDeepRuntimeProducers = {{
    {UltimateProbeDomain::ObjectProbe, "ObjectResolverProducer",
     UltimateSemanticEventType::ObjectResolved, "StableObjectIdentity;AllocationOrLookupWitness"},
    {UltimateProbeDomain::AllocationProbe, "AllocationLifetimeProducer",
     UltimateSemanticEventType::ObjectAllocated, "Address;Size;Lifetime;Free;Reuse"},
    {UltimateProbeDomain::VTableProbe, "VTableClassFamilyProducer",
     UltimateSemanticEventType::ObjectResolved, "ExecutableTargets;InstanceAssociation;ClassFamily"},
    {UltimateProbeDomain::FactoryProbe, "FactoryOwnershipProducer",
     UltimateSemanticEventType::ObjectAllocated, "FactoryInput;ReturnedObject;Ownership"},
    {UltimateProbeDomain::ManagerLookupProbe, "ManagerLookupProducer",
     UltimateSemanticEventType::ObjectLookup, "StableIdToPointer;LookupConsumer"},
    {UltimateProbeDomain::RegistryProbe, "RegistryEnumerationProducer",
     UltimateSemanticEventType::RegistryEnumerated, "Container;Count;IdMapping;Ownership"},
    {UltimateProbeDomain::ResourceDecodeProbe, "ResourceDecodeChainProducer",
     UltimateSemanticEventType::ResourceDeserialized, "Read;Decrypt;Decompress;Decode;Deserialize"},
    {UltimateProbeDomain::MutationProbe, "StateMutationProducer",
     UltimateSemanticEventType::StateMutation, "Object;Before;Input;After;Writer;Consumer"},
    {UltimateProbeDomain::TaintSeedProbe, "BoundedValueProvenanceProducer",
     UltimateSemanticEventType::TaintSeed, "Seed;BoundedPropagation;Consumer"},
    {UltimateProbeDomain::FormulaOperandProbe, "FormulaObservationProducer",
     UltimateSemanticEventType::FormulaOperand, "Operands;CallOrder;Result;Mutation"},
    {UltimateProbeDomain::SnapshotProbe, "BoundedSnapshotProducer",
     UltimateSemanticEventType::SnapshotObject, "SafePoint;ObjectToken;VTable;Edges"}
}};

constexpr std::array<UltimateGameplayAdapterDefinition,
                     kUltimateGameplayAdapterCount> kGameplayAdapters = {{
    {UltimateProbeDomain::QuestProbe, "QuestRuntimeAdapter",
     "ObjectResolver;Registry;Mutation", true, true},
    {UltimateProbeDomain::MapProbe, "MapRuntimeAdapter",
     "Registry;ObjectResolver;PositionState", true, true},
    {UltimateProbeDomain::PortalProbe, "PortalRuntimeAdapter",
     "MapRegistry;SnapshotEdge;Mutation", true, true},
    {UltimateProbeDomain::NpcProbe, "NpcRuntimeAdapter",
     "Registry;ResourceDecode;ObjectResolver", true, true},
    {UltimateProbeDomain::MonsterProbe, "MonsterRuntimeAdapter",
     "Registry;ResourceDecode;ObjectResolver;Mutation", true, true},
    {UltimateProbeDomain::BattleProbe, "BattleRuntimeAdapter",
     "Handler;ObjectResolver;Mutation;Formula", true, true},
    {UltimateProbeDomain::InventoryProbe, "InventoryRuntimeAdapter",
     "ObjectResolver;Mutation;Registry", true, true},
    {UltimateProbeDomain::ItemProbe, "ItemRuntimeAdapter",
     "Registry;ResourceDecode;ObjectResolver", true, true},
    {UltimateProbeDomain::SkillProbe, "SkillRuntimeAdapter",
     "Registry;Formula;Mutation", true, true},
    {UltimateProbeDomain::PetMountProbe, "PetMountRuntimeAdapter",
     "Registry;ObjectResolver;Mutation", true, true}
}};

std::string LowerAscii(std::string_view value) {
    std::string result(value);
    std::transform(result.begin(), result.end(), result.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return result;
}

bool EqualAsciiInsensitive(std::string_view left, std::string_view right) {
    return LowerAscii(left) == LowerAscii(right);
}

std::string TrimAscii(std::string_view value) {
    std::size_t begin = 0;
    std::size_t end = value.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(value[begin])) != 0) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(value[end - 1])) != 0) --end;
    return std::string(value.substr(begin, end - begin));
}

bool IsHexDigit(char value) noexcept {
    return (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') ||
        (value >= 'A' && value <= 'F');
}

int HexValue(char value) noexcept {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
}

void AppendUtf8(std::string* output, std::uint32_t codepoint) {
    if (codepoint <= 0x7FU) {
        output->push_back(static_cast<char>(codepoint));
    } else if (codepoint <= 0x7FFU) {
        output->push_back(static_cast<char>(0xC0U | (codepoint >> 6U)));
        output->push_back(static_cast<char>(0x80U | (codepoint & 0x3FU)));
    } else if (codepoint <= 0xFFFFU) {
        output->push_back(static_cast<char>(0xE0U | (codepoint >> 12U)));
        output->push_back(static_cast<char>(0x80U | ((codepoint >> 6U) & 0x3FU)));
        output->push_back(static_cast<char>(0x80U | (codepoint & 0x3FU)));
    } else {
        output->push_back(static_cast<char>(0xF0U | (codepoint >> 18U)));
        output->push_back(static_cast<char>(0x80U | ((codepoint >> 12U) & 0x3FU)));
        output->push_back(static_cast<char>(0x80U | ((codepoint >> 6U) & 0x3FU)));
        output->push_back(static_cast<char>(0x80U | (codepoint & 0x3FU)));
    }
}

enum class JsonAtomKind {
    String,
    Number,
    Boolean,
    Null,
    Object,
    Array
};

struct JsonAtom {
    std::string value;
    JsonAtomKind kind = JsonAtomKind::Null;
};

using StrictJsonObject = std::map<std::string, JsonAtom>;

class StrictFlatJsonParser final {
public:
    explicit StrictFlatJsonParser(std::string_view input) : input_(input) {}

    bool Parse(StrictJsonObject* output, std::string* error) {
        if (input_.size() > 1024U * 1024U) return Fail("JSON input exceeds bounded limit", error);
        output->clear();
        SkipWhitespace();
        if (!ParseObject(output, 0U, error)) return false;
        SkipWhitespace();
        return position_ == input_.size() || Fail("trailing data", error);
    }

private:
    char Peek() const noexcept {
        return position_ < input_.size() ? input_[position_] : '\0';
    }

    void SkipWhitespace() noexcept {
        while (position_ < input_.size() &&
               std::isspace(static_cast<unsigned char>(input_[position_])) != 0) ++position_;
    }

    bool Consume(char expected) noexcept {
        if (position_ >= input_.size() || input_[position_] != expected) return false;
        ++position_;
        return true;
    }

    bool Fail(std::string_view message, std::string* error) const {
        if (error != nullptr)
            *error = std::string(message) + " at offset " + std::to_string(position_);
        return false;
    }

    bool ParseString(std::string* output, std::string* error) {
        if (!Consume('"')) return Fail("expected string", error);
        output->clear();
        while (position_ < input_.size()) {
            const unsigned char c = static_cast<unsigned char>(input_[position_++]);
            if (c == '"') return true;
            if (c < 0x20U) return Fail("control character in string", error);
            if (c != '\\') {
                output->push_back(static_cast<char>(c));
                continue;
            }
            if (position_ >= input_.size()) return Fail("truncated escape", error);
            const char escape = input_[position_++];
            switch (escape) {
            case '"': output->push_back('"'); break;
            case '\\': output->push_back('\\'); break;
            case '/': output->push_back('/'); break;
            case 'b': output->push_back('\b'); break;
            case 'f': output->push_back('\f'); break;
            case 'n': output->push_back('\n'); break;
            case 'r': output->push_back('\r'); break;
            case 't': output->push_back('\t'); break;
            case 'u': {
                if (position_ + 4U > input_.size()) return Fail("truncated unicode escape", error);
                std::uint32_t codepoint = 0;
                for (std::size_t index = 0; index < 4U; ++index) {
                    const int digit = HexValue(input_[position_ + index]);
                    if (digit < 0) return Fail("invalid unicode escape", error);
                    codepoint = (codepoint << 4U) | static_cast<std::uint32_t>(digit);
                }
                position_ += 4U;
                if (codepoint >= 0xD800U && codepoint <= 0xDBFFU) {
                    if (position_ + 6U > input_.size() || input_[position_] != '\\' ||
                        input_[position_ + 1U] != 'u')
                        return Fail("missing unicode low surrogate", error);
                    position_ += 2U;
                    std::uint32_t low = 0;
                    for (std::size_t index = 0; index < 4U; ++index) {
                        const int digit = HexValue(input_[position_ + index]);
                        if (digit < 0) return Fail("invalid unicode low surrogate", error);
                        low = (low << 4U) | static_cast<std::uint32_t>(digit);
                    }
                    position_ += 4U;
                    if (low < 0xDC00U || low > 0xDFFFU)
                        return Fail("invalid unicode low surrogate", error);
                    codepoint = 0x10000U + ((codepoint - 0xD800U) << 10U) + (low - 0xDC00U);
                } else if (codepoint >= 0xDC00U && codepoint <= 0xDFFFU) {
                    return Fail("unexpected unicode low surrogate", error);
                }
                AppendUtf8(output, codepoint);
                break;
            }
            default: return Fail("invalid string escape", error);
            }
        }
        return Fail("unterminated string", error);
    }

    bool ParseObject(StrictJsonObject* output, std::size_t depth, std::string* error) {
        if (depth > 32U) return Fail("JSON nesting exceeds bounded limit", error);
        if (!Consume('{')) return Fail("expected object", error);
        SkipWhitespace();
        if (Consume('}')) return true;
        while (position_ < input_.size()) {
            std::string key;
            if (!ParseString(&key, error)) return false;
            SkipWhitespace();
            if (!Consume(':')) return Fail("expected colon", error);
            SkipWhitespace();
            JsonAtom atom;
            if (!ParseValue(&atom, depth + 1U, error)) return false;
            if (!output->emplace(std::move(key), std::move(atom)).second)
                return Fail("duplicate key", error);
            SkipWhitespace();
            if (Consume('}')) return true;
            if (!Consume(',')) return Fail("expected comma", error);
            SkipWhitespace();
            if (Peek() == '}') return Fail("trailing comma", error);
        }
        return Fail("unterminated object", error);
    }

    bool ParseArray(std::size_t depth, std::string* error) {
        if (depth > 32U) return Fail("JSON nesting exceeds bounded limit", error);
        if (!Consume('[')) return Fail("expected array", error);
        SkipWhitespace();
        if (Consume(']')) return true;
        while (position_ < input_.size()) {
            JsonAtom ignored;
            if (!ParseValue(&ignored, depth + 1U, error)) return false;
            SkipWhitespace();
            if (Consume(']')) return true;
            if (!Consume(',')) return Fail("expected array comma", error);
            SkipWhitespace();
            if (Peek() == ']') return Fail("trailing array comma", error);
        }
        return Fail("unterminated array", error);
    }

    bool ParseNumber(std::string* output, std::string* error) {
        const std::size_t start = position_;
        if (Peek() == '-') ++position_;
        if (Peek() == '0') {
            ++position_;
            if (Peek() >= '0' && Peek() <= '9') return Fail("leading zero in number", error);
        } else {
            if (Peek() < '1' || Peek() > '9') return Fail("invalid number", error);
            while (Peek() >= '0' && Peek() <= '9') ++position_;
        }
        if (Peek() == '.') {
            ++position_;
            if (Peek() < '0' || Peek() > '9') return Fail("missing fraction", error);
            while (Peek() >= '0' && Peek() <= '9') ++position_;
        }
        if (Peek() == 'e' || Peek() == 'E') {
            ++position_;
            if (Peek() == '+' || Peek() == '-') ++position_;
            if (Peek() < '0' || Peek() > '9') return Fail("missing exponent", error);
            while (Peek() >= '0' && Peek() <= '9') ++position_;
        }
        *output = std::string(input_.substr(start, position_ - start));
        return true;
    }

    bool ParseValue(JsonAtom* atom, std::size_t depth, std::string* error) {
        if (depth > 32U) return Fail("JSON nesting exceeds bounded limit", error);
        const std::size_t start = position_;
        if (Peek() == '"') {
            atom->kind = JsonAtomKind::String;
            return ParseString(&atom->value, error);
        }
        if (Peek() == '{') {
            StrictJsonObject nested;
            if (!ParseObject(&nested, depth, error)) return false;
            atom->kind = JsonAtomKind::Object;
            atom->value = std::string(input_.substr(start, position_ - start));
            return true;
        }
        if (Peek() == '[') {
            if (!ParseArray(depth, error)) return false;
            atom->kind = JsonAtomKind::Array;
            atom->value = std::string(input_.substr(start, position_ - start));
            return true;
        }
        if (input_.substr(position_, 4U) == "true") {
            position_ += 4U;
            atom->kind = JsonAtomKind::Boolean;
            atom->value = "true";
            return true;
        }
        if (input_.substr(position_, 5U) == "false") {
            position_ += 5U;
            atom->kind = JsonAtomKind::Boolean;
            atom->value = "false";
            return true;
        }
        if (input_.substr(position_, 4U) == "null") {
            position_ += 4U;
            atom->kind = JsonAtomKind::Null;
            atom->value = "null";
            return true;
        }
        atom->kind = JsonAtomKind::Number;
        return ParseNumber(&atom->value, error);
    }

    std::string_view input_;
    std::size_t position_ = 0;
};

template <typename Integer>
bool ParseUnsigned(std::string_view text, Integer* value) noexcept {
    static_assert(std::is_unsigned_v<Integer>);
    if (text.empty() || text.front() == '-') return false;
    Integer parsed = 0;
    const auto result = std::from_chars(text.data(), text.data() + text.size(), parsed);
    if (result.ec != std::errc{} || result.ptr != text.data() + text.size()) return false;
    *value = parsed;
    return true;
}

bool ParseFlexibleUint64(std::string_view text, std::uint64_t* value) noexcept {
    if (text.size() > 2U && text[0] == '0' && (text[1] == 'x' || text[1] == 'X')) {
        std::uint64_t parsed = 0;
        const auto result = std::from_chars(text.data() + 2, text.data() + text.size(), parsed, 16);
        if (result.ec != std::errc{} || result.ptr != text.data() + text.size()) return false;
        *value = parsed;
        return true;
    }
    return ParseUnsigned(text, value);
}

const JsonAtom* FindAtom(const StrictJsonObject& object, std::string_view key) {
    const auto found = object.find(std::string(key));
    return found == object.end() ? nullptr : &found->second;
}

std::string AtomString(const StrictJsonObject& object, std::string_view key,
                       std::string_view fallback = {}) {
    const auto* atom = FindAtom(object, key);
    if (atom == nullptr || atom->kind == JsonAtomKind::Null) return std::string(fallback);
    return atom->value;
}

std::uint64_t AtomUint64(const StrictJsonObject& object, std::string_view key,
                         std::uint64_t fallback = 0) noexcept {
    const auto* atom = FindAtom(object, key);
    std::uint64_t value = fallback;
    return atom != nullptr && ParseFlexibleUint64(atom->value, &value) ? value : fallback;
}

std::string JsonQuoted(std::string_view value) {
    return "\"" + JsonEscape(value) + "\"";
}

std::string JsonStringArray(const std::vector<std::string>& values) {
    std::ostringstream output;
    output << '[';
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0U) output << ',';
        output << JsonQuoted(values[index]);
    }
    output << ']';
    return output.str();
}

std::string V1CompatibilityPayload(const StrictJsonObject& object) {
    constexpr std::array<std::string_view, 15> allowed = {
        "Direction", "Opcode", "FrameOffset", "DestinationOffset", "Width",
        "ReadType", "WriteType", "ArgumentType", "Endian", "FieldSemantic",
        "HookInvocationId", "ProtocolFrameId", "LogicalMessageId", "ProbeId",
        "EvidenceBasis"
    };
    std::ostringstream output;
    output << "{\"Profile\":\"NonCanonicalV1CompatibilityInput\"," 
              "\"Canonical\":false,\"Promotable\":false";
    for (const auto key : allowed) {
        const auto* atom = FindAtom(object, key);
        if (atom == nullptr || atom->kind == JsonAtomKind::Null) continue;
        output << ',' << JsonQuoted(key) << ':' << JsonQuoted(atom->value);
    }
    output << '}';
    return output.str();
}

bool IsStringOrNull(const JsonAtom* atom) noexcept {
    return atom != nullptr && (atom->kind == JsonAtomKind::String ||
                               atom->kind == JsonAtomKind::Null);
}

const std::set<std::string>& LegacyCompanionKeys() {
    static const std::set<std::string> keys = {
        "SchemaId", "SemanticEventId", "ObservedAtUnixMs", "Qpc", "Direction",
        "Opcode", "ProtocolFrameId", "LogicalMessageId", "ActionInstanceId",
        "HookInvocationId", "ParentInvocationId", "ContextInvocationId",
        "ExactParentSemanticEventId", "ContextSemanticEventId",
        "ContextCorrelationBasis", "HandlerInvocationId", "ProbeId", "ProbeCategory",
        "Module", "Rva", "CallerRva", "ProbeContractVersion", "ClientSha256Expected",
        "BuildBindingStatus", "EvidenceBasis", "EvidenceLevel", "FrameOffset",
        "FrameOffsetStatus", "DestinationOffset", "Width", "ReadType", "WriteType",
        "Endian", "RawBytes", "ParsedValue", "Value", "ValueTokenId",
        "FieldSemantic", "AuthorityClassification", "SensitiveValue", "ArgumentIndex",
        "ArgumentType", "ArgumentValue", "AssociatedValueTokenId",
        "ObjectPointerPersisted"
    };
    return keys;
}

bool IsStrictV2KeySet(const StrictJsonObject& object, std::string* error) {
    static const std::set<std::string> required = {
        "SchemaVersion", "EventType", "EventId", "Sequence", "Timestamp", "ThreadId",
        "ProcessId", "SessionId", "ClientBuildId", "ModuleId", "RVA", "CallsiteRVA",
        "ParentEventId", "ContextId", "ActionId", "ObjectToken", "ValueToken",
        "SourceToken", "AuthorityHint", "SensitiveMaskStatus", "Payload"
    };
    // These are the documented v1 companion fields emitted during the v2
    // transition.  They are accepted but never replace a required v2 field.
    const auto& legacy_companion = LegacyCompanionKeys();
    for (const auto& key : required) {
        if (object.find(key) == object.end()) {
            if (error != nullptr) *error = "missing v2 field: " + key;
            return false;
        }
    }
    for (const auto& pair : object) {
        if (required.find(pair.first) == required.end() &&
            legacy_companion.find(pair.first) == legacy_companion.end()) {
            if (error != nullptr) *error = "unknown v2 field: " + pair.first;
            return false;
        }
    }
    constexpr std::array<std::string_view, 4> numeric = {
        "SchemaVersion", "Sequence", "ThreadId", "ProcessId"
    };
    for (const auto key : numeric) {
        const auto* atom = FindAtom(object, key);
        if (atom == nullptr || atom->kind != JsonAtomKind::Number) {
            if (error != nullptr) *error = "numeric v2 field has wrong type: " + std::string(key);
            return false;
        }
    }
    constexpr std::array<std::string_view, 7> required_strings = {
        "EventType", "EventId", "SessionId", "ClientBuildId", "ModuleId",
        "AuthorityHint", "SensitiveMaskStatus"
    };
    for (const auto key : required_strings) {
        const auto* atom = FindAtom(object, key);
        if (atom == nullptr || atom->kind != JsonAtomKind::String) {
            if (error != nullptr) *error = "required v2 string has wrong type: " +
                std::string(key);
            return false;
        }
    }
    const auto* timestamp = FindAtom(object, "Timestamp");
    if (timestamp == nullptr || (timestamp->kind != JsonAtomKind::String &&
                                timestamp->kind != JsonAtomKind::Number)) {
        if (error != nullptr) *error = "Timestamp must be a string or number";
        return false;
    }
    constexpr std::array<std::string_view, 7> nullable_strings = {
        "ParentEventId", "ContextId", "ActionId", "ObjectToken", "ValueToken",
        "SourceToken", "RVA"
    };
    for (const auto key : nullable_strings) {
        const auto* atom = FindAtom(object, key);
        const bool rva_number = key == "RVA" && atom != nullptr &&
            atom->kind == JsonAtomKind::Number;
        if (!rva_number && !IsStringOrNull(atom)) {
            if (error != nullptr) *error = "nullable v2 field has wrong type: " +
                std::string(key);
            return false;
        }
    }
    const auto* callsite = FindAtom(object, "CallsiteRVA");
    if (callsite == nullptr || (callsite->kind != JsonAtomKind::String &&
                               callsite->kind != JsonAtomKind::Number &&
                               callsite->kind != JsonAtomKind::Null)) {
        if (error != nullptr) *error = "CallsiteRVA has wrong type";
        return false;
    }
    const auto* payload = FindAtom(object, "Payload");
    if (payload == nullptr || (payload->kind != JsonAtomKind::Object &&
                              payload->kind != JsonAtomKind::String)) {
        if (error != nullptr) *error = "Payload must be an object (or legacy encoded string)";
        return false;
    }
    const auto* schema_id = FindAtom(object, "SchemaId");
    if (schema_id != nullptr && (schema_id->kind != JsonAtomKind::String ||
        schema_id->value != "God2SemanticEvent")) {
        if (error != nullptr) *error = "invalid v2 SchemaId";
        return false;
    }
    const auto* semantic_id = FindAtom(object, "SemanticEventId");
    const auto* event_id = FindAtom(object, "EventId");
    if (semantic_id != nullptr && (semantic_id->kind != JsonAtomKind::String ||
        event_id == nullptr || semantic_id->value != event_id->value)) {
        if (error != nullptr) *error = "legacy SemanticEventId contradicts EventId";
        return false;
    }
    return true;
}

bool IsCanonicalV1WriterProfile(const StrictJsonObject& object) {
    static const std::set<std::string> exact_keys = {
        "SchemaId", "SchemaVersion", "EventType", "SemanticEventId", "Sequence",
        "Timestamp", "ThreadId", "ProcessId", "SessionId", "ClientBuildId",
        "ModuleId", "RVA", "CallsiteRVA", "SensitiveValue"
    };
    if (object.size() != exact_keys.size()) return false;
    for (const auto& [key, ignored] : object) {
        static_cast<void>(ignored);
        if (exact_keys.find(key) == exact_keys.end()) return false;
    }
    const auto* schema_id = FindAtom(object, "SchemaId");
    const auto* schema_version = FindAtom(object, "SchemaVersion");
    const auto* event_type = FindAtom(object, "EventType");
    const auto* event_id = FindAtom(object, "SemanticEventId");
    const auto* timestamp = FindAtom(object, "Timestamp");
    const auto* session_id = FindAtom(object, "SessionId");
    const auto* client_build_id = FindAtom(object, "ClientBuildId");
    const auto* module_id = FindAtom(object, "ModuleId");
    const auto* sensitive_value = FindAtom(object, "SensitiveValue");
    std::uint32_t version = 0;
    if (schema_id == nullptr || schema_id->kind != JsonAtomKind::String ||
        schema_id->value != "God2SemanticEvent" || schema_version == nullptr ||
        schema_version->kind != JsonAtomKind::Number ||
        !ParseUnsigned(schema_version->value, &version) || version != 1U ||
        event_type == nullptr || event_type->kind != JsonAtomKind::String ||
        !ParseUltimateSemanticEventType(event_type->value).has_value() ||
        event_id == nullptr || event_id->kind != JsonAtomKind::String ||
        event_id->value.empty() || timestamp == nullptr ||
        timestamp->kind != JsonAtomKind::String || timestamp->value.empty() ||
        session_id == nullptr || session_id->kind != JsonAtomKind::String ||
        session_id->value.empty() || client_build_id == nullptr ||
        client_build_id->kind != JsonAtomKind::String || client_build_id->value.empty() ||
        module_id == nullptr || module_id->kind != JsonAtomKind::String ||
        module_id->value.empty() || sensitive_value == nullptr ||
        sensitive_value->kind != JsonAtomKind::Boolean ||
        sensitive_value->value != "false") return false;

    const auto valid_uint64 = [&](std::string_view key) {
        const auto* atom = FindAtom(object, key);
        std::uint64_t value = 0;
        return atom != nullptr && atom->kind == JsonAtomKind::Number &&
            ParseUnsigned(atom->value, &value);
    };
    const auto valid_uint32 = [&](std::string_view key, bool nonzero) {
        const auto* atom = FindAtom(object, key);
        std::uint32_t value = 0;
        return atom != nullptr && atom->kind == JsonAtomKind::Number &&
            ParseUnsigned(atom->value, &value) && (!nonzero || value != 0U);
    };
    return valid_uint64("Sequence") && valid_uint32("ThreadId", false) &&
        valid_uint32("ProcessId", true) && valid_uint64("RVA") &&
        valid_uint64("CallsiteRVA");
}

bool IsCanonicalV2CompanionType(std::string_view key, const JsonAtom& atom) {
    static const std::set<std::string> numeric_or_string = {
        "ObservedAtUnixMs", "Qpc", "FrameOffset", "DestinationOffset", "Width",
        "ParsedValue", "Value", "ArgumentIndex", "ArgumentValue"
    };
    static const std::set<std::string> boolean_or_string = {
        "SensitiveValue", "ObjectPointerPersisted"
    };
    static const std::set<std::string> nullable_string = {
        "ProtocolFrameId", "LogicalMessageId", "ActionInstanceId",
        "ParentInvocationId", "ContextInvocationId", "ExactParentSemanticEventId",
        "ContextSemanticEventId"
    };
    static const std::set<std::string> required_string = {
        "Direction", "Opcode", "HookInvocationId", "ContextCorrelationBasis",
        "HandlerInvocationId", "ProbeId", "ProbeCategory", "Module", "Rva",
        "CallerRva", "ProbeContractVersion", "ClientSha256Expected",
        "BuildBindingStatus", "EvidenceBasis", "EvidenceLevel", "FrameOffsetStatus",
        "ReadType", "WriteType", "Endian", "RawBytes", "ValueTokenId",
        "FieldSemantic", "AuthorityClassification", "ArgumentType",
        "AssociatedValueTokenId"
    };
    if (numeric_or_string.contains(std::string(key)))
        return atom.kind == JsonAtomKind::Number || atom.kind == JsonAtomKind::String;
    if (boolean_or_string.contains(std::string(key)))
        return atom.kind == JsonAtomKind::Boolean || atom.kind == JsonAtomKind::String;
    if (nullable_string.contains(std::string(key)))
        return atom.kind == JsonAtomKind::String || atom.kind == JsonAtomKind::Null;
    return required_string.contains(std::string(key)) && atom.kind == JsonAtomKind::String;
}

bool IsCanonicalV2Envelope(const StrictJsonObject& object) {
    std::string ignored;
    if (!IsStrictV2KeySet(object, &ignored)) return false;
    const auto* schema_id = FindAtom(object, "SchemaId");
    const auto* payload = FindAtom(object, "Payload");
    if (schema_id == nullptr || schema_id->kind != JsonAtomKind::String ||
        schema_id->value != "God2SemanticEvent" || payload == nullptr ||
        payload->kind != JsonAtomKind::Object) return false;
    for (const auto& [key, atom] : object) {
        if (key == "SchemaId" || key == "SemanticEventId" ||
            LegacyCompanionKeys().find(key) == LegacyCompanionKeys().end()) continue;
        if (!IsCanonicalV2CompanionType(key, atom)) return false;
    }
    return true;
}

UltimateSemanticEventType MapV1EventType(std::string_view value, bool* valid) noexcept {
    if (value == "ObjectResolution") {
        *valid = true;
        return UltimateSemanticEventType::ObjectResolved;
    }
    const auto parsed = ParseUltimateSemanticEventType(value);
    *valid = parsed.has_value();
    return parsed.value_or(UltimateSemanticEventType::ProbeDiagnostic);
}

UltimateEvidenceRef EvidenceFromEvent(const God2SemanticEventV2& event) {
    UltimateEvidenceRef result;
    result.evidence_id = event.event_id.empty() ? "Sequence:" + std::to_string(event.sequence) : event.event_id;
    result.event_id = event.event_id;
    result.session_id = event.session_id;
    result.client_build_id = event.client_build_id;
    result.module_id = event.module_id;
    result.rva = event.rva;
    result.rva_expression = event.rva_expression;
    result.callsite_rva_expression = event.callsite_rva_expression;
    result.claimed_authority_hint = event.authority_hint;
    // A raw hint is never serialized as validated provenance authority.
    // VERIFIED requires aggregation gates and therefore remains UNKNOWN on an
    // individual evidence reference; the parent recovered record carries the
    // recomputed authority.
    if (event.authority_hint == UltimateAuthority::Rejected ||
        event.authority_hint == UltimateAuthority::UnknownServerOnly ||
        event.authority_hint == UltimateAuthority::Hypothesis ||
        event.authority_hint == UltimateAuthority::Observed ||
        event.authority_hint == UltimateAuthority::Derived) {
        result.authority = event.event_id.empty() ? UltimateAuthority::Unknown :
            event.authority_hint;
    }
    return result;
}

std::string BindingString(const God2SemanticEventV2& event, std::string_view key) {
    const auto found = event.evidence_binding.find(std::string(key));
    return found == event.evidence_binding.end() ? std::string{} : found->second;
}

bool IsExactProbeBinding(const God2SemanticEventV2& event) {
    if (!EqualAsciiInsensitive(event.client_build_id, kExpectedClientSha256) ||
        !EqualAsciiInsensitive(event.module_id, kExpectedClientName)) return false;
    const auto expected = BindingString(event, "ClientSha256Expected");
    const auto status = BindingString(event, "BuildBindingStatus");
    const auto contract = BindingString(event, "ProbeContractVersion");
    const auto basis = BindingString(event, "EvidenceBasis");
    const bool exact_status = status == "ExactInstructionIdentityVerified" ||
        status == "ExactCallTargetVerified" || status == "ExactBuildTargetVerified";
    return EqualAsciiInsensitive(expected, kExpectedClientSha256) && exact_status &&
        !contract.empty() && basis.find("ExactBuild") != std::string::npos &&
        (event.rva != 0U || !event.rva_expression.empty());
}

bool HasSchemaCausalBinding(const God2SemanticEventV2& event) {
    return !event.parent_event_id.empty() || !event.action_id.empty() ||
        !event.context_id.empty() || !BindingString(event, "ExactParentSemanticEventId").empty() ||
        !BindingString(event, "ContextSemanticEventId").empty() ||
        !BindingString(event, "ContextInvocationId").empty() ||
        !BindingString(event, "HookInvocationId").empty() ||
        !BindingString(event, "ProtocolFrameId").empty();
}

bool HasVerifiedSchemaConsumer(const God2SemanticEventV2& event) {
    if (!IsExactProbeBinding(event) || BindingString(event, "ProbeId").empty()) return false;
    switch (event.event_type) {
    case UltimateSemanticEventType::ParserRead:
    case UltimateSemanticEventType::SerializerWrite:
    case UltimateSemanticEventType::HandlerInvocation:
    case UltimateSemanticEventType::HandlerArgument:
    case UltimateSemanticEventType::ObjectResolved:
    case UltimateSemanticEventType::RegistryEnumerated:
    case UltimateSemanticEventType::ResourceDeserialized:
    case UltimateSemanticEventType::StateMutation:
        return true;
    default:
        return false;
    }
}

Fields PayloadFields(const God2SemanticEventV2& event) {
    Fields result;
    std::string ignored;
    if (!ParseFlatJson(event.payload, result, &ignored)) result.clear();
    return result;
}

std::uint64_t FieldUint64(const Fields& fields, std::string_view key,
                          std::uint64_t fallback = 0) noexcept {
    const auto found = fields.find(std::string(key));
    if (found == fields.end()) return fallback;
    std::uint64_t value = fallback;
    return ParseFlexibleUint64(found->second, &value) ? value : fallback;
}

bool FieldBool(const Fields& fields, std::string_view key, bool fallback = false) noexcept {
    const auto found = fields.find(std::string(key));
    if (found == fields.end()) return fallback;
    return EqualAsciiInsensitive(found->second, "true") || found->second == "1";
}

std::string FieldString(const Fields& fields, std::string_view key,
                        std::string_view fallback = {}) {
    const auto found = fields.find(std::string(key));
    return found == fields.end() ? std::string(fallback) : found->second;
}

bool HasSchemaTypedSource(const God2SemanticEventV2& event, const Fields& fields) {
    const auto width = FieldUint64(fields, "Width", FieldUint64(fields, "PacketWidth"));
    const auto value_type = FieldString(fields, "ValueType",
        FieldString(fields, "ReadType", FieldString(fields, "WriteType",
            FieldString(fields, "Type", BindingString(event, "ReadType")))));
    if ((event.event_type == UltimateSemanticEventType::ParserRead ||
         event.event_type == UltimateSemanticEventType::SerializerWrite) &&
        width != 0U && !value_type.empty() && value_type != "unknown" &&
        value_type != "NotApplicable") return true;
    if (event.event_type == UltimateSemanticEventType::StateMutation)
        return !value_type.empty() && !FieldString(fields, "BeforeValue").empty() &&
            !FieldString(fields, "AfterValue").empty();
    if (event.event_type == UltimateSemanticEventType::ObjectResolved)
        return !FieldString(fields, "Property").empty() &&
            (!FieldString(fields, "NormalizedValue").empty() ||
             !FieldString(fields, "RawValue").empty());
    return !BindingString(event, "ProbeCategory").empty() &&
        (!FieldString(fields, "SchemaCandidate").empty() ||
         !FieldString(fields, "EntityFamily").empty());
}

std::vector<std::string> SplitEvidenceList(std::string_view value) {
    std::vector<std::string> result;
    std::size_t begin = 0;
    while (begin <= value.size()) {
        const auto end = value.find_first_of(";,|", begin);
        const auto part = TrimAscii(value.substr(begin, end == std::string_view::npos ?
            value.size() - begin : end - begin));
        if (!part.empty()) result.push_back(part);
        if (end == std::string_view::npos) break;
        begin = end + 1U;
    }
    return result;
}

UltimatePromotionGate GateFromFields(const God2SemanticEventV2& event, const Fields& fields,
                                     std::uint64_t observations,
                                     std::uint64_t contradictions) {
    UltimatePromotionGate gate;
    gate.exact_build_binding = IsExactProbeBinding(event);
    gate.typed_source = HasSchemaTypedSource(event, fields);
    gate.stable_object_or_context = !event.object_token.empty() ||
        !event.context_id.empty() || !event.source_token.empty() || !event.value_token.empty() ||
        !BindingString(event, "ProtocolFrameId").empty() ||
        !BindingString(event, "HookInvocationId").empty();
    gate.repeated_observations = observations;
    gate.contradictions = contradictions;
    gate.causal_path = HasSchemaCausalBinding(event);
    gate.verified_consumer_or_mutation = HasVerifiedSchemaConsumer(event);
    gate.cross_session_required = FieldBool(fields, "CrossSessionRequired");
    gate.cross_session_consistent = FieldBool(fields, "CrossSessionConsistent");
    gate.replay_required = FieldBool(fields, "ReplayRequired");
    gate.replay_consistent = FieldBool(fields, "ReplayConsistent");
    return gate;
}

UltimateAuthority EvidenceAuthority(const God2SemanticEventV2& event, const Fields& fields,
                                    std::uint64_t observations = 1,
                                    std::uint64_t contradictions = 0) {
    if (event.authority_hint == UltimateAuthority::Rejected) return UltimateAuthority::Rejected;
    if (event.authority_hint == UltimateAuthority::UnknownServerOnly)
        return UltimateAuthority::UnknownServerOnly;
    if (event.authority_hint == UltimateAuthority::Hypothesis)
        return UltimateAuthority::Hypothesis;
    if (event.authority_hint == UltimateAuthority::Verified) {
        return CanPromoteVerified(GateFromFields(event, fields, observations, contradictions)) ?
            UltimateAuthority::Verified : UltimateAuthority::Unknown;
    }
    if (event.authority_hint == UltimateAuthority::Derived && !event.event_id.empty())
        return UltimateAuthority::Derived;
    if (event.authority_hint == UltimateAuthority::Observed && !event.event_id.empty())
        return UltimateAuthority::Observed;
    return UltimateAuthority::Unknown;
}

UltimateAuthority CombineAuthoritiesConservatively(
    const std::vector<UltimateAuthority>& authorities,
    std::uint64_t contradictions = 0U) noexcept {
    if (contradictions != 0U || std::find(authorities.begin(), authorities.end(),
        UltimateAuthority::Rejected) != authorities.end()) return UltimateAuthority::Rejected;
    if (authorities.empty()) return UltimateAuthority::Unknown;
    const bool has_server_only = std::find(authorities.begin(), authorities.end(),
        UltimateAuthority::UnknownServerOnly) != authorities.end();
    if (has_server_only) {
        return std::all_of(authorities.begin(), authorities.end(), [](UltimateAuthority value) {
            return value == UltimateAuthority::UnknownServerOnly;
        }) ? UltimateAuthority::UnknownServerOnly : UltimateAuthority::Unknown;
    }
    if (std::find(authorities.begin(), authorities.end(), UltimateAuthority::Unknown) !=
        authorities.end()) return UltimateAuthority::Unknown;
    const auto first = authorities.front();
    return std::all_of(authorities.begin(), authorities.end(), [&](UltimateAuthority value) {
        return value == first;
    }) ? first : UltimateAuthority::Unknown;
}

struct AuthorityObservation {
    const God2SemanticEventV2* event = nullptr;
    Fields fields;
};

struct AuthorityAccumulator {
    std::vector<AuthorityObservation> observations;

    void Add(const God2SemanticEventV2& event, const Fields& fields) {
        observations.push_back(AuthorityObservation{&event, fields});
    }

    std::uint64_t DistinctObservations() const {
        std::set<std::pair<std::string, std::string>> identities;
        for (const auto& observation : observations) {
            if (observation.event != nullptr && !observation.event->session_id.empty() &&
                !observation.event->event_id.empty()) {
                identities.emplace(observation.event->session_id, observation.event->event_id);
            }
        }
        return static_cast<std::uint64_t>(identities.size());
    }

    UltimateAuthority Resolve(std::uint64_t contradictions = 0U) const {
        std::vector<UltimateAuthority> authorities;
        authorities.reserve(observations.size());
        const auto repetition = DistinctObservations();
        for (const auto& observation : observations) {
            if (observation.event != nullptr)
                authorities.push_back(EvidenceAuthority(*observation.event,
                    observation.fields, repetition, contradictions));
        }
        return CombineAuthoritiesConservatively(authorities, contradictions);
    }
};

bool IsKnownSubsystem(std::string_view value) {
    static const std::set<std::string> known = {
        "Authentication", "Character", "WorldBootstrap", "Movement", "MapTransfer",
        "NPC", "Quest", "Inventory", "Equipment", "Merchant", "Trade", "Party",
        "Guild", "Chat", "Battle", "Combat", "Skill", "ItemUse", "Pet",
        "Companion", "Mount", "Crafting", "LifeSkill", "Mail", "System",
        "Heartbeat", "Logout"
    };
    return known.find(std::string(value)) != known.end();
}

bool IsSha256(std::string_view value) noexcept {
    return value.size() == 64U &&
        std::all_of(value.begin(), value.end(), [](char c) { return IsHexDigit(c); });
}

std::string SanitizeComponent(std::string_view value) {
    std::string result;
    result.reserve(std::min<std::size_t>(value.size(), 80U));
    for (const unsigned char c : value) {
        if (result.size() >= 80U) break;
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.') {
            result.push_back(static_cast<char>(c));
        }
    }
    return result;
}

std::string TimestampComponent() {
    std::string value = UtcNow();
    value.erase(std::remove_if(value.begin(), value.end(), [](char c) {
        return c == '-' || c == ':' || c == '.' || c == 'T' || c == 'Z';
    }), value.end());
    if (value.size() > 14U) value.resize(14U);
    return value;
}

} // namespace

const char* ToString(UltimateAuthority value) noexcept {
    return EnumName(value, kAuthorityNames, "UNKNOWN");
}

std::optional<UltimateAuthority> ParseUltimateAuthority(std::string_view value) noexcept {
    for (std::size_t index = 0; index < kAuthorityNames.size(); ++index) {
        if (EqualAsciiInsensitive(value, kAuthorityNames[index]))
            return static_cast<UltimateAuthority>(index);
    }
    return std::nullopt;
}

const char* ToString(UltimateSemanticEventType value) noexcept {
    return EnumName(value, kEventTypeNames, "ProbeDiagnostic");
}

std::optional<UltimateSemanticEventType> ParseUltimateSemanticEventType(
    std::string_view value) noexcept {
    for (std::size_t index = 0; index < kEventTypeNames.size(); ++index) {
        if (value == kEventTypeNames[index])
            return static_cast<UltimateSemanticEventType>(index);
    }
    return std::nullopt;
}

const UltimateSemanticEventCapability* GetUltimateSemanticEventCapability(
    UltimateSemanticEventType value) noexcept {
    const auto index = static_cast<std::size_t>(value);
    if (index >= kSemanticCapabilities.size() ||
        kSemanticCapabilities[index].event_type != value) return nullptr;
    return &kSemanticCapabilities[index];
}

UltimateSemanticDispatchDecision DispatchSemanticEvent(
    const God2SemanticEventV2& event) noexcept {
    UltimateSemanticDispatchDecision decision;
    const auto* capability = GetUltimateSemanticEventCapability(event.event_type);
    if (capability == nullptr) return decision;
    decision.dispatchable = true;
    decision.sink = capability->sink;
    decision.promotion_allowed = capability->promotion_policy ==
        UltimateSemanticPromotionPolicy::StandardEvidenceGates;
    return decision;
}

const char* ToString(UltimateProbeDomain value) noexcept {
    return EnumName(value, kProbeDomainNames, "UnknownProbe");
}

const std::array<UltimateDeepRuntimeProducerDefinition,
                 kUltimateFundamentalProducerCount>&
GetUltimateDeepRuntimeProducerInventory() noexcept {
    return kDeepRuntimeProducers;
}

const std::array<UltimateGameplayAdapterDefinition,
                 kUltimateGameplayAdapterCount>&
GetUltimateGameplayAdapterInventory() noexcept {
    return kGameplayAdapters;
}

bool UltimateDeepPromotionGates::AllPassed() const noexcept {
    return exact_target_identity && executable_section && exact_candidate_bytes &&
        calling_convention_verified && typed_runtime_evidence &&
        repeated_causal_observation && contradictions_resolved &&
        stable_object_or_context && verified_consumer_or_mutation &&
        sensitive_mask_contract && argument_contract_verified &&
        return_value_lifetime_verified && thread_context_verified &&
        reentrancy_risk_verified;
}

namespace {

enum class SemanticPayloadValueKind : std::uint8_t {
    String,
    Boolean,
    UnsignedInteger
};

struct SemanticPayloadField final {
    std::string_view name;
    SemanticPayloadValueKind kind;
};

bool IsUnsignedIntegerAtom(const JsonAtom& atom) noexcept {
    std::uint64_t ignored = 0;
    return atom.kind == JsonAtomKind::Number &&
        atom.value.find_first_of(".eE") == std::string::npos &&
        ParseUnsigned(atom.value, &ignored);
}

bool IsPayloadFieldKind(const JsonAtom& atom,
                        SemanticPayloadValueKind kind) noexcept {
    switch (kind) {
    case SemanticPayloadValueKind::String:
        return atom.kind == JsonAtomKind::String;
    case SemanticPayloadValueKind::Boolean:
        return atom.kind == JsonAtomKind::Boolean;
    case SemanticPayloadValueKind::UnsignedInteger:
        return IsUnsignedIntegerAtom(atom);
    }
    return false;
}

bool MatchExactPayload(const StrictJsonObject& object,
                       std::initializer_list<SemanticPayloadField> fields) {
    if (object.size() != fields.size()) return false;
    for (const auto& field : fields) {
        const auto* atom = FindAtom(object, field.name);
        if (atom == nullptr || !IsPayloadFieldKind(*atom, field.kind)) return false;
    }
    return true;
}

bool PayloadStringEquals(const StrictJsonObject& object,
                         std::string_view key,
                         std::string_view expected) {
    const auto* atom = FindAtom(object, key);
    return atom != nullptr && atom->kind == JsonAtomKind::String &&
        atom->value == expected;
}

bool PayloadBooleanEquals(const StrictJsonObject& object,
                          std::string_view key,
                          bool expected) {
    const auto* atom = FindAtom(object, key);
    return atom != nullptr && atom->kind == JsonAtomKind::Boolean &&
        atom->value == (expected ? "true" : "false");
}

std::optional<std::uint64_t> PayloadUnsignedInteger(
    const StrictJsonObject& object, std::string_view key) noexcept {
    const auto* atom = FindAtom(object, key);
    std::uint64_t value = 0;
    if (atom == nullptr || !IsUnsignedIntegerAtom(*atom) ||
        !ParseUnsigned(atom->value, &value)) return std::nullopt;
    return value;
}

bool SourceStartsWith(const God2SemanticEventV2& event,
                      std::string_view prefix) noexcept {
    return event.source_token.size() >= prefix.size() &&
        event.source_token.compare(0U, prefix.size(), prefix) == 0;
}

bool EventTypeIsOneOf(const God2SemanticEventV2& event,
                      std::initializer_list<UltimateSemanticEventType> types) noexcept {
    return std::find(types.begin(), types.end(), event.event_type) != types.end();
}

struct ProbePayloadContract final {
    std::string_view domain;
    std::string_view probe;
    std::string_view intended_event_type;
    std::string_view dispatch_capability;
};

constexpr std::array<ProbePayloadContract, kUltimateProbeDomainCount>
    kProbePayloadContracts = {{
        {"Network", "WinsockTransport", "PacketBoundary", "ProtocolBoundary"},
        {"Parser", "PacketDecode.FrameBoundary", "ParserRead", "TypedProtocolField"},
        {"Serializer", "OutboundEnqueue.FrameBuilder", "SerializerWrite", "TypedProtocolField"},
        {"Handler", "Battle.HandlerRecordLength", "HandlerArgument", "HandlerDispatch"},
        {"Object", "ObjectResolver", "ObjectResolved", "ObjectResolver"},
        {"Allocation", "AllocationProbe", "ObjectAllocated", "ObjectResolver"},
        {"VTable", "VTableProbe", "FunctionRoleCandidate", "CandidateDiscovery"},
        {"Factory", "FactoryProbe", "FunctionRoleCandidate", "CandidateDiscovery"},
        {"ManagerLookup", "ManagerLookupProbe", "ObjectLookup", "ObjectResolver"},
        {"Registry", "RegistryProbe", "RegistryLocated", "RegistryRecovery"},
        {"ResourceDecode", "ResourceDecodeProbe", "ResourceDecoded", "ResourceRecovery"},
        {"Mutation", "MutationProbe", "StateMutation", "MutationRecovery"},
        {"TaintSeed", "TaintSeedProbe", "TaintSeed", "ValueProvenance"},
        {"FormulaOperand", "FormulaOperandProbe", "FormulaOperand", "FormulaRecovery"},
        {"Quest", "QuestProbe", "StateMutation", "MutationRecovery"},
        {"Map", "MapProbe", "UIAnchor", "ContentRecovery"},
        {"Portal", "PortalProbe", "SnapshotEdge", "SnapshotRecovery"},
        {"NPC", "NPCProbe", "ObjectResolved", "ObjectResolver"},
        {"Monster", "MonsterProbe", "ObjectResolved", "ObjectResolver"},
        {"Battle", "BattleProbe", "StateMutation", "MutationRecovery"},
        {"Inventory", "InventoryProbe", "StateMutation", "MutationRecovery"},
        {"Item", "ItemProbe", "ObjectResolved", "ObjectResolver"},
        {"Skill", "SkillProbe", "FormulaOperand", "FormulaRecovery"},
        {"Pet/Mount", "PetMountProbe", "ObjectResolved", "ObjectResolver"},
        {"Snapshot", "SnapshotProbe", "SnapshotObject", "SnapshotRecovery"}
    }};

const ProbePayloadContract* ProbePayloadContractForSource(
    std::string_view source, std::size_t* index = nullptr) noexcept {
    for (std::size_t candidate = 0; candidate < kProbePayloadContracts.size();
         ++candidate) {
        if (kProbePayloadContracts[candidate].probe == source) {
            if (index != nullptr) *index = candidate;
            return &kProbePayloadContracts[candidate];
        }
    }
    return nullptr;
}

bool MatchV1CompatibilityPayload(const StrictJsonObject& object) {
    static const std::set<std::string> allowed = {
        "Profile", "Canonical", "Promotable", "Direction", "Opcode",
        "FrameOffset", "DestinationOffset", "Width", "ReadType", "WriteType",
        "ArgumentType", "Endian", "FieldSemantic", "HookInvocationId",
        "ProtocolFrameId", "LogicalMessageId", "ProbeId", "EvidenceBasis"
    };
    if (!PayloadStringEquals(object, "Profile",
            "NonCanonicalV1CompatibilityInput") ||
        !PayloadBooleanEquals(object, "Canonical", false) ||
        !PayloadBooleanEquals(object, "Promotable", false)) return false;
    for (const auto& [key, atom] : object) {
        if (!allowed.contains(key)) return false;
        if (key != "Profile" && key != "Canonical" && key != "Promotable" &&
            atom.kind != JsonAtomKind::String) return false;
    }
    return true;
}

bool MatchProbeDomainStatusPayload(const God2SemanticEventV2& event,
                                   const StrictJsonObject& object) {
    if (event.event_type != UltimateSemanticEventType::ProbeDiagnostic ||
        !MatchExactPayload(object, {
            {"Domain", SemanticPayloadValueKind::String},
            {"Probe", SemanticPayloadValueKind::String},
            {"Status", SemanticPayloadValueKind::String},
            {"Reason", SemanticPayloadValueKind::String},
            {"ContractStatus", SemanticPayloadValueKind::String},
            {"ActivationAllowedByContract", SemanticPayloadValueKind::Boolean},
            {"RuntimeDiagnosticObserved", SemanticPayloadValueKind::Boolean},
            {"RuntimeActivationObserved", SemanticPayloadValueKind::Boolean},
            {"TargetIdentityVerified", SemanticPayloadValueKind::Boolean},
            {"FaultIsolation", SemanticPayloadValueKind::String}
        })) return false;
    std::size_t index = 0;
    const auto* contract = ProbePayloadContractForSource(event.source_token, &index);
    if (contract == nullptr || !PayloadStringEquals(object, "Domain", contract->domain) ||
        !PayloadStringEquals(object, "Probe", contract->probe) ||
        !PayloadBooleanEquals(object, "RuntimeDiagnosticObserved", true) ||
        !PayloadStringEquals(object, "FaultIsolation", "PerDomain")) return false;
    const bool runtime_active = PayloadBooleanEquals(
        object, "RuntimeActivationObserved", true);
    if (index < 4U) {
        if (!PayloadStringEquals(object, "ContractStatus", "ConfirmedContract") ||
            !PayloadBooleanEquals(object, "ActivationAllowedByContract", true)) return false;
        if (runtime_active) {
            return PayloadStringEquals(object, "Status", "Active") &&
                PayloadStringEquals(object, "Reason", "VerifiedProbeInstalled") &&
                PayloadBooleanEquals(object, "TargetIdentityVerified", true);
        }
        return PayloadStringEquals(object, "Status", "CurrentTargetIdentityBlocked") &&
            PayloadStringEquals(object, "Reason",
                "ConfirmedContractNotActivatedForCurrentTargetIdentity");
    }
    return !runtime_active &&
        PayloadStringEquals(object, "ContractStatus", "CandidateOnlyBlockedContract") &&
        PayloadBooleanEquals(object, "ActivationAllowedByContract", false) &&
        PayloadStringEquals(object, "Status", "EvidenceBlockedUnconfirmedProbe") &&
        PayloadStringEquals(object, "Reason", "NoVerifiedTargetRva");
}

bool MatchCandidatePayload(const God2SemanticEventV2& event,
                           const StrictJsonObject& object) {
    if (event.event_type != UltimateSemanticEventType::FunctionRoleCandidate ||
        !MatchExactPayload(object, {
            {"Status", SemanticPayloadValueKind::String},
            {"Domain", SemanticPayloadValueKind::String},
            {"Probe", SemanticPayloadValueKind::String},
            {"CandidateCount", SemanticPayloadValueKind::UnsignedInteger},
            {"ActiveHook", SemanticPayloadValueKind::Boolean},
            {"CandidateEventType", SemanticPayloadValueKind::String},
            {"IntendedVerifiedEventType", SemanticPayloadValueKind::String},
            {"DispatchCapability", SemanticPayloadValueKind::String},
            {"VerificationNeed", SemanticPayloadValueKind::String}
        })) return false;
    std::size_t index = 0;
    const auto* contract = ProbePayloadContractForSource(event.source_token, &index);
    const auto candidate_count = PayloadUnsignedInteger(object, "CandidateCount");
    return contract != nullptr && index >= 4U && candidate_count.has_value() &&
        *candidate_count <= 4U &&
        PayloadStringEquals(object, "Status", "EvidenceBlockedUnconfirmedProbe") &&
        PayloadStringEquals(object, "Domain", contract->domain) &&
        PayloadStringEquals(object, "Probe", contract->probe) &&
        PayloadBooleanEquals(object, "ActiveHook", false) &&
        PayloadStringEquals(object, "CandidateEventType", "FunctionRoleCandidate") &&
        PayloadStringEquals(object, "IntendedVerifiedEventType",
                            contract->intended_event_type) &&
        PayloadStringEquals(object, "DispatchCapability",
                            contract->dispatch_capability);
}

bool ClassifyCanonicalSemanticPayloadImpl(const God2SemanticEventV2& event,
                                          std::string* profile_name,
                                          std::string* error) {
    StrictJsonObject object;
    std::string parse_error;
    const auto trimmed = TrimAscii(event.payload);
    if (trimmed.empty() || trimmed.front() != '{' ||
        !StrictFlatJsonParser(trimmed).Parse(&object, &parse_error)) {
        if (error != nullptr) *error = "invalid payload JSON: " + parse_error;
        return false;
    }

    std::size_t matches = 0;
    std::string matched_profile;
    const auto accept = [&](bool matched, std::string_view name) {
        if (!matched) return;
        ++matches;
        matched_profile.assign(name);
    };
    const bool source_empty = event.source_token.empty();
    std::size_t source_domain_index = 0;
    const auto* source_contract = ProbePayloadContractForSource(
        event.source_token, &source_domain_index);
    const bool fundamental_producer_source = source_contract != nullptr &&
        ((source_domain_index >= 4U && source_domain_index <= 13U) ||
         source_domain_index == 24U);

    accept(object.empty() && source_empty && EventTypeIsOneOf(event, {
        UltimateSemanticEventType::PacketBoundary,
        UltimateSemanticEventType::ParserRead,
        UltimateSemanticEventType::SnapshotObject,
        UltimateSemanticEventType::ProbeDiagnostic}), "UltimateEmptyFixture");

    accept(source_empty && MatchExactPayload(object, {
        {"FixtureOnly", SemanticPayloadValueKind::Boolean},
        {"RuntimeObservation", SemanticPayloadValueKind::Boolean}
    }) && PayloadBooleanEquals(object, "FixtureOnly", true) &&
        PayloadBooleanEquals(object, "RuntimeObservation", false),
        "UltimateTwentyFiveTypeFixture");

    accept(event.event_type == UltimateSemanticEventType::PacketBoundary && source_empty &&
        MatchExactPayload(object, {
            {"Direction", SemanticPayloadValueKind::String},
            {"Opcode", SemanticPayloadValueKind::String},
            {"Subsystem", SemanticPayloadValueKind::String},
            {"ActionFamily", SemanticPayloadValueKind::String},
            {"FrameLength", SemanticPayloadValueKind::String},
            {"FrameLengthRule", SemanticPayloadValueKind::String},
            {"EncryptionBoundary", SemanticPayloadValueKind::String}
        }), "UltimatePacketBoundaryRecovery");

    accept(event.event_type == UltimateSemanticEventType::ParserRead && source_empty &&
        MatchExactPayload(object, {
            {"Direction", SemanticPayloadValueKind::String},
            {"Opcode", SemanticPayloadValueKind::String},
            {"Subsystem", SemanticPayloadValueKind::String},
            {"PacketOffset", SemanticPayloadValueKind::String},
            {"Width", SemanticPayloadValueKind::String},
            {"ValueType", SemanticPayloadValueKind::String},
            {"Endian", SemanticPayloadValueKind::String},
            {"SemanticCandidate", SemanticPayloadValueKind::String},
            {"TypedSource", SemanticPayloadValueKind::String}
        }), "UltimateParserReadRecovery");

    accept(event.event_type == UltimateSemanticEventType::ParserRead &&
        SourceStartsWith(event, "PF-") && MatchExactPayload(object, {
            {"Direction", SemanticPayloadValueKind::String},
            {"Opcode", SemanticPayloadValueKind::String},
            {"Width", SemanticPayloadValueKind::UnsignedInteger},
            {"ValueType", SemanticPayloadValueKind::String},
            {"FrameOffset", SemanticPayloadValueKind::UnsignedInteger}
        }), "UltimateParserReadBoundField");

    accept(event.event_type == UltimateSemanticEventType::ParserRead &&
        SourceStartsWith(event, "PF-") && MatchExactPayload(object, {
            {"Direction", SemanticPayloadValueKind::String},
            {"Opcode", SemanticPayloadValueKind::String},
            {"Width", SemanticPayloadValueKind::UnsignedInteger},
            {"ValueType", SemanticPayloadValueKind::String}
        }), "UltimateParserReadBoundFieldNoOffset");

    accept(event.event_type == UltimateSemanticEventType::SerializerWrite && source_empty &&
        MatchExactPayload(object, {
            {"Direction", SemanticPayloadValueKind::String},
            {"Opcode", SemanticPayloadValueKind::String},
            {"Subsystem", SemanticPayloadValueKind::String},
            {"PacketOffset", SemanticPayloadValueKind::String},
            {"Width", SemanticPayloadValueKind::String},
            {"ValueType", SemanticPayloadValueKind::String},
            {"Endian", SemanticPayloadValueKind::String}
        }), "UltimateSerializerWriteRecovery");

    accept(event.event_type == UltimateSemanticEventType::ObjectAllocated &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"AllocationSize", SemanticPayloadValueKind::String},
            {"VTable", SemanticPayloadValueKind::String},
            {"ConstructorPath", SemanticPayloadValueKind::String},
            {"Factory", SemanticPayloadValueKind::String}
        }), "UltimateObjectAllocatedRecovery");

    accept(event.event_type == UltimateSemanticEventType::ObjectResolved &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"EntityFamily", SemanticPayloadValueKind::String},
            {"StableTemplateId", SemanticPayloadValueKind::String},
            {"Property", SemanticPayloadValueKind::String},
            {"RawValue", SemanticPayloadValueKind::String},
            {"NormalizedValue", SemanticPayloadValueKind::String},
            {"ValueLayer", SemanticPayloadValueKind::String}
        }), "UltimateObjectResolvedRecovery");

    accept(event.event_type == UltimateSemanticEventType::ObjectResolved &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"EntityFamily", SemanticPayloadValueKind::String},
            {"StableTemplateId", SemanticPayloadValueKind::String},
            {"Property", SemanticPayloadValueKind::String},
            {"RawValue", SemanticPayloadValueKind::String},
            {"NormalizedValue", SemanticPayloadValueKind::String},
            {"ValueLayer", SemanticPayloadValueKind::String},
            {"ExactBuildBinding", SemanticPayloadValueKind::String},
            {"TypedSource", SemanticPayloadValueKind::String},
            {"StableContext", SemanticPayloadValueKind::String},
            {"CausalPath", SemanticPayloadValueKind::String},
            {"VerifiedConsumerOrMutation", SemanticPayloadValueKind::String}
        }), "UltimateObjectResolvedVerifiedRecovery");

    accept(event.event_type == UltimateSemanticEventType::ObjectResolved &&
        event.source_token == "SAFE-SOURCE-2048" && MatchExactPayload(object, {
            {"Property", SemanticPayloadValueKind::String},
            {"RawValue", SemanticPayloadValueKind::String}
        }), "UltimateObjectResolvedSafeFixture");

    accept(event.event_type == UltimateSemanticEventType::RegistryEnumerated &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"RegistryName", SemanticPayloadValueKind::String},
            {"EntityFamily", SemanticPayloadValueKind::String},
            {"RecordCount", SemanticPayloadValueKind::String},
            {"ReadOnly", SemanticPayloadValueKind::String},
            {"LookupConsumer", SemanticPayloadValueKind::String}
        }), "UltimateRegistryEnumeratedRecovery");

    accept(event.event_type == UltimateSemanticEventType::RegistryEnumerated &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"RegistryName", SemanticPayloadValueKind::String},
            {"EntityFamily", SemanticPayloadValueKind::String},
            {"RecordCount", SemanticPayloadValueKind::UnsignedInteger},
            {"ReadOnly", SemanticPayloadValueKind::Boolean}
        }), "UltimateRegistryEnumeratedMeasuredFixture");

    accept(event.event_type == UltimateSemanticEventType::ResourceDeserialized &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"RelativePath", SemanticPayloadValueKind::String},
            {"FileSha256", SemanticPayloadValueKind::String},
            {"DecodedBufferSha256", SemanticPayloadValueKind::String},
            {"SourceOffset", SemanticPayloadValueKind::String},
            {"DecodeRVA", SemanticPayloadValueKind::String},
            {"DeserializeRVA", SemanticPayloadValueKind::String},
            {"Destination", SemanticPayloadValueKind::String},
            {"RecordCount", SemanticPayloadValueKind::String},
            {"Provenance", SemanticPayloadValueKind::String}
        }), "UltimateResourceDeserializedRecovery");

    accept(event.event_type == UltimateSemanticEventType::ResourceDeserialized &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"RelativePath", SemanticPayloadValueKind::String},
            {"EntityFamily", SemanticPayloadValueKind::String},
            {"RecordCount", SemanticPayloadValueKind::UnsignedInteger},
            {"SchemaCandidate", SemanticPayloadValueKind::String}
        }), "UltimateResourceDeserializedMeasuredFixture");

    accept(event.event_type == UltimateSemanticEventType::StateMutation &&
        event.source_token == "Source:77" && MatchExactPayload(object, {
            {"Before", SemanticPayloadValueKind::String},
            {"After", SemanticPayloadValueKind::String}
        }), "UltimateStateMutationRoundTripFixture");

    accept(event.event_type == UltimateSemanticEventType::StateMutation &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"ObjectIdentity", SemanticPayloadValueKind::String},
            {"PropertyCandidate", SemanticPayloadValueKind::String},
            {"Type", SemanticPayloadValueKind::String},
            {"BeforeValue", SemanticPayloadValueKind::String},
            {"InputValue", SemanticPayloadValueKind::String},
            {"AfterValue", SemanticPayloadValueKind::String},
            {"TriggerAction", SemanticPayloadValueKind::String},
            {"WriterRVA", SemanticPayloadValueKind::String}
        }), "UltimateStateMutationObservedFixture");

    accept(event.event_type == UltimateSemanticEventType::StateMutation &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"ObjectIdentity", SemanticPayloadValueKind::String},
            {"PropertyCandidate", SemanticPayloadValueKind::String},
            {"Type", SemanticPayloadValueKind::String},
            {"BeforeValue", SemanticPayloadValueKind::String},
            {"InputValue", SemanticPayloadValueKind::String},
            {"AfterValue", SemanticPayloadValueKind::String},
            {"TriggerAction", SemanticPayloadValueKind::String},
            {"WriterRVA", SemanticPayloadValueKind::String},
            {"Contradictions", SemanticPayloadValueKind::UnsignedInteger}
        }), "UltimateStateMutationContradictionFixture");

    accept(event.event_type == UltimateSemanticEventType::StateMutation &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"ObjectIdentity", SemanticPayloadValueKind::String},
            {"PropertyCandidate", SemanticPayloadValueKind::String},
            {"Type", SemanticPayloadValueKind::String},
            {"BeforeValue", SemanticPayloadValueKind::String},
            {"InputValue", SemanticPayloadValueKind::String},
            {"AfterValue", SemanticPayloadValueKind::String},
            {"TriggerAction", SemanticPayloadValueKind::String},
            {"WriterRVA", SemanticPayloadValueKind::String},
            {"MachineId", SemanticPayloadValueKind::String},
            {"PreviousState", SemanticPayloadValueKind::String},
            {"NextState", SemanticPayloadValueKind::String},
            {"ReplayConsistent", SemanticPayloadValueKind::String}
        }), "UltimateStateMutationQuestRecovery");

    accept(event.event_type == UltimateSemanticEventType::StateMutation &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"ObjectIdentity", SemanticPayloadValueKind::String},
            {"PropertyCandidate", SemanticPayloadValueKind::String},
            {"Type", SemanticPayloadValueKind::String},
            {"BeforeValue", SemanticPayloadValueKind::String},
            {"InputValue", SemanticPayloadValueKind::String},
            {"AfterValue", SemanticPayloadValueKind::String},
            {"TriggerPacket", SemanticPayloadValueKind::String},
            {"TriggerHandler", SemanticPayloadValueKind::String},
            {"WriterRVA", SemanticPayloadValueKind::String},
            {"Consumers", SemanticPayloadValueKind::String},
            {"MachineId", SemanticPayloadValueKind::String},
            {"PreviousState", SemanticPayloadValueKind::String},
            {"NextState", SemanticPayloadValueKind::String},
            {"ReplayConsistent", SemanticPayloadValueKind::String},
            {"ExactBuildBinding", SemanticPayloadValueKind::String},
            {"TypedSource", SemanticPayloadValueKind::String},
            {"StableContext", SemanticPayloadValueKind::String},
            {"CausalPath", SemanticPayloadValueKind::String},
            {"VerifiedConsumerOrMutation", SemanticPayloadValueKind::String}
        }), "UltimateStateMutationVerifiedRecovery");

    const bool value_flow_operation = MatchExactPayload(object, {
        {"Operation", SemanticPayloadValueKind::String},
        {"PropagationHops", SemanticPayloadValueKind::String}
    });
    accept(value_flow_operation &&
        ((event.event_type == UltimateSemanticEventType::TaintSeed &&
          event.source_token == "Packet:0x41:+4") ||
         (event.event_type == UltimateSemanticEventType::TaintPropagation &&
          event.source_token == "Value:MonsterId") ||
         (event.event_type == UltimateSemanticEventType::ValueFlow &&
          event.source_token == "Object:MonsterTemplate:4102")),
        "UltimateValueFlowRecovery");

    accept(event.event_type == UltimateSemanticEventType::FormulaOperand &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"FormulaId", SemanticPayloadValueKind::String},
            {"FormulaCategory", SemanticPayloadValueKind::String},
            {"Expression", SemanticPayloadValueKind::String},
            {"Operand", SemanticPayloadValueKind::String}
        }), "UltimateFormulaOperandRecovery");

    accept(event.event_type == UltimateSemanticEventType::FormulaResult &&
        (source_empty || fundamental_producer_source) &&
        MatchExactPayload(object, {
            {"FormulaId", SemanticPayloadValueKind::String},
            {"FormulaCategory", SemanticPayloadValueKind::String},
            {"Expression", SemanticPayloadValueKind::String},
            {"Result", SemanticPayloadValueKind::String}
        }), "UltimateFormulaResultRecovery");

    accept(event.event_type == UltimateSemanticEventType::FormulaResult && source_empty &&
        MatchExactPayload(object, {
            {"FormulaId", SemanticPayloadValueKind::String},
            {"Expression", SemanticPayloadValueKind::String},
            {"Result", SemanticPayloadValueKind::String},
            {"Contradictions", SemanticPayloadValueKind::String}
        }), "UltimateFormulaCounterexampleFixture");

    accept(event.event_type == UltimateSemanticEventType::ProbeDiagnostic && source_empty &&
        MatchExactPayload(object, {
            {"Status", SemanticPayloadValueKind::String},
            {"Domain", SemanticPayloadValueKind::String}
        }) && PayloadStringEquals(object, "Status", "ProbeExceptionIsolated"),
        "UltimateProbeIsolationDiagnostic");

    accept(MatchExactPayload(object, {
        {"Status", SemanticPayloadValueKind::String},
        {"OriginalPayloadPersisted", SemanticPayloadValueKind::Boolean},
        {"Authority", SemanticPayloadValueKind::String}
    }) && PayloadStringEquals(object, "Status",
            "EvidenceBlockedSensitivePayloadSuppressed") &&
        PayloadBooleanEquals(object, "OriginalPayloadPersisted", false) &&
        PayloadStringEquals(object, "Authority", "UNKNOWN") &&
        event.sensitive_mask_status == "SensitivePayloadSuppressedMetadataOnly" &&
        source_empty, "EvidenceBlockedSensitivePayload");

    accept(MatchExactPayload(object, {
        {"Status", SemanticPayloadValueKind::String},
        {"OriginalPayloadPersisted", SemanticPayloadValueKind::Boolean},
        {"Authority", SemanticPayloadValueKind::String}
    }) && PayloadStringEquals(object, "Status",
            "EvidenceBlockedInvalidOrUnclassifiedPayload") &&
        PayloadBooleanEquals(object, "OriginalPayloadPersisted", false) &&
        PayloadStringEquals(object, "Authority", "UNKNOWN") &&
        event.sensitive_mask_status == "UnclassifiedPayloadSuppressedMetadataOnly",
        "EvidenceBlockedUnclassifiedPayload");

    accept(event.authority_hint == UltimateAuthority::Unknown &&
        MatchV1CompatibilityPayload(object),
        "CanonicalizedNonPromotableV1Compatibility");

    accept(event.source_token == "NativeSemanticTypeFixture" &&
        MatchExactPayload(object, {
            {"FixtureOnly", SemanticPayloadValueKind::Boolean},
            {"RuntimeObservation", SemanticPayloadValueKind::Boolean},
            {"DefinitionIndex", SemanticPayloadValueKind::UnsignedInteger},
            {"ExpectedEventType", SemanticPayloadValueKind::String}
        }) && PayloadBooleanEquals(object, "FixtureOnly", true) &&
        PayloadBooleanEquals(object, "RuntimeObservation", false) &&
        PayloadUnsignedInteger(object, "DefinitionIndex") ==
            std::optional<std::uint64_t>(static_cast<std::uint64_t>(event.event_type)) &&
        PayloadStringEquals(object, "ExpectedEventType", ToString(event.event_type)),
        "NativeSemanticTypeFixture");

    accept(event.event_type == UltimateSemanticEventType::ProbeDiagnostic &&
        event.source_token == "HookThreadSchemaBatchPrivateFixture" &&
        MatchExactPayload(object, {
            {"FixtureOnly", SemanticPayloadValueKind::Boolean},
            {"Row", SemanticPayloadValueKind::UnsignedInteger}
        }) && PayloadBooleanEquals(object, "FixtureOnly", true) &&
        PayloadUnsignedInteger(object, "Row").has_value() &&
        (*PayloadUnsignedInteger(object, "Row") == 1U ||
         *PayloadUnsignedInteger(object, "Row") == 2U),
        "HookThreadSchemaBatchPrivateFixture");

    accept(event.event_type == UltimateSemanticEventType::ProbeDiagnostic &&
        event.source_token == "PerDomainFaultIsolation" &&
        MatchExactPayload(object, {
            {"FixtureOnly", SemanticPayloadValueKind::Boolean},
            {"FaultPriorityPressure", SemanticPayloadValueKind::Boolean}
        }) && PayloadBooleanEquals(object, "FixtureOnly", true) &&
        PayloadBooleanEquals(object, "FaultPriorityPressure", true),
        "FaultPriorityPressureFixture");

    accept(event.event_type == UltimateSemanticEventType::ProbeDiagnostic &&
        event.source_token == "PerDomainFaultIsolation" &&
        MatchExactPayload(object, {
            {"FixtureOnly", SemanticPayloadValueKind::Boolean},
            {"ReservePressure", SemanticPayloadValueKind::Boolean},
            {"RuntimeObservation", SemanticPayloadValueKind::Boolean}
        }) && PayloadBooleanEquals(object, "FixtureOnly", true) &&
        PayloadBooleanEquals(object, "ReservePressure", true) &&
        PayloadBooleanEquals(object, "RuntimeObservation", false),
        "FaultReservePressureFixture");

    const auto fault_domain = PayloadUnsignedInteger(object, "DomainIndex");
    const auto other_enabled = PayloadUnsignedInteger(object, "OtherDomainEnabledCount");
    accept(event.event_type == UltimateSemanticEventType::ProbeDiagnostic &&
        event.source_token == "PerDomainFaultIsolation" &&
        MatchExactPayload(object, {
            {"Status", SemanticPayloadValueKind::String},
            {"DomainIndex", SemanticPayloadValueKind::UnsignedInteger},
            {"FaultCode", SemanticPayloadValueKind::String},
            {"AffectedDomainDisabled", SemanticPayloadValueKind::Boolean},
            {"OtherDomainsRemainEnabled", SemanticPayloadValueKind::Boolean},
            {"OtherDomainEnabledCount", SemanticPayloadValueKind::UnsignedInteger},
            {"OtherDomainEnabledMask", SemanticPayloadValueKind::String},
            {"EvidenceIncomplete", SemanticPayloadValueKind::Boolean},
            {"SensitivePayloadPersisted", SemanticPayloadValueKind::Boolean}
        }) && fault_domain.has_value() && *fault_domain < kUltimateProbeDomainCount &&
        other_enabled.has_value() && *other_enabled < kUltimateProbeDomainCount &&
        PayloadStringEquals(object, "Status", "ProbeDomainDisabledAfterIsolatedFault") &&
        PayloadBooleanEquals(object, "AffectedDomainDisabled", true) &&
        PayloadBooleanEquals(object, "OtherDomainsRemainEnabled", *other_enabled != 0U) &&
        PayloadBooleanEquals(object, "EvidenceIncomplete", *other_enabled == 0U) &&
        PayloadBooleanEquals(object, "SensitivePayloadPersisted", false),
        "PerDomainFaultIsolationDiagnostic");

    accept(MatchProbeDomainStatusPayload(event, object), "ProbeDomainStatus");
    accept(MatchCandidatePayload(event, object), "FunctionRoleCandidateStatus");

    accept(event.event_type == UltimateSemanticEventType::ProbeDiagnostic &&
        event.source_token == "WinsockTransportChunk" &&
        MatchExactPayload(object, {
            {"Status", SemanticPayloadValueKind::String},
            {"Direction", SemanticPayloadValueKind::String},
            {"Api", SemanticPayloadValueKind::UnsignedInteger},
            {"RequestedLength", SemanticPayloadValueKind::UnsignedInteger},
            {"TransferredLength", SemanticPayloadValueKind::UnsignedInteger},
            {"CapturedLength", SemanticPayloadValueKind::UnsignedInteger},
            {"SensitivePayloadSuppressed", SemanticPayloadValueKind::Boolean},
            {"SemanticPayloadContainsNetworkBytes", SemanticPayloadValueKind::Boolean}
        }) && PayloadStringEquals(object, "Status", "TransportChunkNotBoundary") &&
        PayloadBooleanEquals(object, "SemanticPayloadContainsNetworkBytes", false),
        "WinsockTransportChunkDiagnostic");

    accept(event.event_type == UltimateSemanticEventType::PacketBoundary &&
        (event.source_token == "PacketDecode.FrameBoundary" ||
         event.source_token == "OutboundEnqueue.FrameBuilder") &&
        MatchExactPayload(object, {
            {"Status", SemanticPayloadValueKind::String},
            {"Direction", SemanticPayloadValueKind::String},
            {"Api", SemanticPayloadValueKind::UnsignedInteger},
            {"FrameLength", SemanticPayloadValueKind::UnsignedInteger},
            {"Opcode", SemanticPayloadValueKind::UnsignedInteger},
            {"ChecksumVerified", SemanticPayloadValueKind::Boolean},
            {"ExactTargetIdentity", SemanticPayloadValueKind::Boolean},
            {"SemanticPayloadContainsNetworkBytes", SemanticPayloadValueKind::Boolean}
        }) && PayloadStringEquals(object, "Status", "VerifiedPlaintextFrameBoundary") &&
        PayloadBooleanEquals(object, "ChecksumVerified", true) &&
        PayloadBooleanEquals(object, "ExactTargetIdentity", true) &&
        PayloadBooleanEquals(object, "SemanticPayloadContainsNetworkBytes", false),
        "VerifiedPlaintextFrameBoundary");

    accept((event.event_type == UltimateSemanticEventType::ParserRead ||
            event.event_type == UltimateSemanticEventType::SerializerWrite) &&
        SourceStartsWith(event, "PF-") && MatchExactPayload(object, {
            {"Direction", SemanticPayloadValueKind::String},
            {"Opcode", SemanticPayloadValueKind::String},
            {"FrameOffset", SemanticPayloadValueKind::UnsignedInteger},
            {"Width", SemanticPayloadValueKind::UnsignedInteger},
            {"ValueType", SemanticPayloadValueKind::String},
            {"RawBytes", SemanticPayloadValueKind::String},
            {"ParsedValue", SemanticPayloadValueKind::UnsignedInteger},
            {"FieldSemantic", SemanticPayloadValueKind::String}
        }), "VerifiedTypedProtocolField");

    accept(event.event_type == UltimateSemanticEventType::HandlerInvocation &&
        (source_empty || SourceStartsWith(event, "PF-")) &&
        MatchExactPayload(object, {
            {"ArgumentIndex", SemanticPayloadValueKind::UnsignedInteger},
            {"ArgumentType", SemanticPayloadValueKind::String},
            {"ArgumentValue", SemanticPayloadValueKind::UnsignedInteger}
        }), "VerifiedHandlerInvocation");

    accept(event.event_type == UltimateSemanticEventType::HandlerArgument &&
        event.source_token == "Battle.HandlerRecordLength" &&
        MatchExactPayload(object, {
            {"Status", SemanticPayloadValueKind::String},
            {"ArgumentIndex", SemanticPayloadValueKind::UnsignedInteger},
            {"ArgumentType", SemanticPayloadValueKind::String},
            {"ArgumentValue", SemanticPayloadValueKind::UnsignedInteger},
            {"ObjectPointerPersisted", SemanticPayloadValueKind::Boolean},
            {"SensitiveValue", SemanticPayloadValueKind::Boolean}
        }) && PayloadStringEquals(object, "Status", "VerifiedHandlerArgument") &&
        PayloadBooleanEquals(object, "ObjectPointerPersisted", false) &&
        PayloadBooleanEquals(object, "SensitiveValue", false),
        "VerifiedHandlerArgument");

    accept(event.event_type == UltimateSemanticEventType::PacketBoundary &&
        event.source_token == "SharedPriorityReserveFill" &&
        MatchExactPayload(object, {
            {"FixtureOnly", SemanticPayloadValueKind::Boolean},
            {"RuntimeObservation", SemanticPayloadValueKind::Boolean},
            {"Phase", SemanticPayloadValueKind::String},
            {"Index", SemanticPayloadValueKind::UnsignedInteger},
            {"PriorityExpected", SemanticPayloadValueKind::UnsignedInteger},
            {"ProcessId", SemanticPayloadValueKind::UnsignedInteger}
        }) && PayloadBooleanEquals(object, "FixtureOnly", true) &&
        PayloadBooleanEquals(object, "RuntimeObservation", false) &&
        PayloadStringEquals(object, "Phase", "ReserveFill") &&
        PayloadUnsignedInteger(object, "PriorityExpected") ==
            std::optional<std::uint64_t>(0U) &&
        PayloadUnsignedInteger(object, "ProcessId").value_or(0U) != 0U,
        "SharedPriorityReserveFillFixture");

    if (matches != 1U) {
        if (error != nullptr) {
            *error = matches == 0U ? "payload does not match a closed canonical profile" :
                "payload ambiguously matches multiple canonical profiles";
        }
        return false;
    }
    if (profile_name != nullptr) *profile_name = std::move(matched_profile);
    return true;
}

bool ContainsSensitiveSemanticMarker(std::string_view value) {
    const auto folded = LowerAscii(value);
    static constexpr std::array<std::string_view, 11> markers = {
        "password", "passwd", "account", "credential", "authentication",
        "rawauth", "authpayload", "loginsecret", "decodedprefixsafe",
        "key256", "decodekey32"
    };
    if (std::any_of(markers.begin(), markers.end(), [&](std::string_view marker) {
            return folded.find(marker) != std::string::npos;
        })) return true;
    return folded.find("\"auth\"") != std::string::npos ||
        folded.find("\"authcategory\"") != std::string::npos;
}

bool SensitiveSemanticPayloadMustBeSuppressed(const God2SemanticEventV2& event) {
    const auto status = LowerAscii(TrimAscii(event.sensitive_mask_status));
    if (status != "notsensitive") return true;
    if (ContainsSensitiveSemanticMarker(event.payload) ||
        ContainsSensitiveSemanticMarker(event.object_token) ||
        ContainsSensitiveSemanticMarker(event.value_token) ||
        ContainsSensitiveSemanticMarker(event.source_token))
        return true;
    for (const auto& [key, value] : event.evidence_binding) {
        if (ContainsSensitiveSemanticMarker(key) || ContainsSensitiveSemanticMarker(value))
            return true;
    }
    return false;
}

void SuppressSensitiveSemanticPayload(God2SemanticEventV2* event) {
    if (!SensitiveSemanticPayloadMustBeSuppressed(*event)) return;
    event->payload =
        "{\"Status\":\"EvidenceBlockedSensitivePayloadSuppressed\"," 
        "\"OriginalPayloadPersisted\":false,\"Authority\":\"UNKNOWN\"}";
    event->sensitive_mask_status = "SensitivePayloadSuppressedMetadataOnly";
    event->authority_hint = UltimateAuthority::Unknown;
    // These fields are intentionally not pseudonymized from attacker- or
    // probe-controlled input: every one is a free-form persistence channel
    // and several feed object/value-flow identifiers.  Structural identity
    // (event/session/process/build/module/RVA) remains available as metadata.
    event->event_id = "SensitiveEvent-" + std::to_string(event->sequence);
    event->timestamp = "SensitiveTimestampSuppressed";
    event->rva_expression.clear();
    event->callsite_rva_expression.clear();
    event->parent_event_id.clear();
    event->context_id.clear();
    event->action_id.clear();
    event->object_token.clear();
    event->value_token.clear();
    event->source_token.clear();
    event->evidence_binding.clear();
}

void SuppressUnclassifiedSemanticPayload(God2SemanticEventV2* event) {
    event->payload =
        "{\"Status\":\"EvidenceBlockedInvalidOrUnclassifiedPayload\"," 
        "\"OriginalPayloadPersisted\":false,\"Authority\":\"UNKNOWN\"}";
    event->sensitive_mask_status = "UnclassifiedPayloadSuppressedMetadataOnly";
    event->authority_hint = UltimateAuthority::Unknown;
}

std::string PayloadObjectForWire(std::string_view payload) {
    const auto trimmed = TrimAscii(payload);
    StrictJsonObject ignored;
    std::string error;
    if (!trimmed.empty() && trimmed.front() == '{' &&
        StrictFlatJsonParser(trimmed).Parse(&ignored, &error)) return trimmed;
    return "{\"Status\":\"EvidenceBlockedInvalidOrUnclassifiedPayload\"," 
        "\"OriginalPayloadPersisted\":false,\"Authority\":\"UNKNOWN\"}";
}

std::string RvaForWire(std::uint64_t value, std::string_view expression) {
    return expression.empty() ? std::to_string(value) : JsonQuoted(expression);
}

} // namespace

bool ValidateCanonicalSemanticPayload(const God2SemanticEventV2& event,
                                      std::string* profile_name,
                                      std::string* error) {
    return ClassifyCanonicalSemanticPayloadImpl(event, profile_name, error);
}

UltimateDeepRuntimeProducerResult ProduceUltimateDeepRuntimeEvent(
    const UltimateDeepRuntimeProducerInput& input) noexcept {
    UltimateDeepRuntimeProducerResult result;
    try {
        const auto producer = std::find_if(kDeepRuntimeProducers.begin(),
            kDeepRuntimeProducers.end(), [&](const auto& row) {
                return row.domain == input.domain;
            });
        if (producer == kDeepRuntimeProducers.end()) {
            result.producer_implemented = false;
            result.status = "EVIDENCE_BLOCKED_NO_FUNDAMENTAL_PRODUCER";
            return result;
        }

        const auto event_allowed = [&] {
            switch (input.domain) {
            case UltimateProbeDomain::ObjectProbe:
                return input.event_type == UltimateSemanticEventType::ObjectResolved ||
                    input.event_type == UltimateSemanticEventType::ObjectDestroyed;
            case UltimateProbeDomain::AllocationProbe:
                return input.event_type == UltimateSemanticEventType::ObjectAllocated ||
                    input.event_type == UltimateSemanticEventType::ObjectDestroyed;
            case UltimateProbeDomain::VTableProbe:
                return input.event_type == UltimateSemanticEventType::ObjectResolved;
            case UltimateProbeDomain::FactoryProbe:
                return input.event_type == UltimateSemanticEventType::ObjectAllocated;
            case UltimateProbeDomain::ManagerLookupProbe:
                return input.event_type == UltimateSemanticEventType::ObjectLookup;
            case UltimateProbeDomain::RegistryProbe:
                return input.event_type == UltimateSemanticEventType::RegistryLocated ||
                    input.event_type == UltimateSemanticEventType::RegistryEnumerated;
            case UltimateProbeDomain::ResourceDecodeProbe:
                return input.event_type == UltimateSemanticEventType::ResourceRead ||
                    input.event_type == UltimateSemanticEventType::ResourceDecoded ||
                    input.event_type == UltimateSemanticEventType::ResourceDeserialized;
            case UltimateProbeDomain::MutationProbe:
                return input.event_type == UltimateSemanticEventType::StateMutation;
            case UltimateProbeDomain::TaintSeedProbe:
                return input.event_type == UltimateSemanticEventType::TaintSeed ||
                    input.event_type == UltimateSemanticEventType::TaintPropagation ||
                    input.event_type == UltimateSemanticEventType::ValueFlow;
            case UltimateProbeDomain::FormulaOperandProbe:
                return input.event_type == UltimateSemanticEventType::FormulaOperand ||
                    input.event_type == UltimateSemanticEventType::FormulaResult;
            case UltimateProbeDomain::SnapshotProbe:
                return input.event_type == UltimateSemanticEventType::SnapshotObject ||
                    input.event_type == UltimateSemanticEventType::SnapshotEdge;
            default:
                return false;
            }
        }();
        if (!event_allowed) {
            result.status = "EVIDENCE_BLOCKED_EVENT_DOMAIN_CONTRACT_MISMATCH";
            return result;
        }
        if (input.fixture_only || input.historical_official_live ||
            !input.current_official_live) {
            result.status = "EVIDENCE_BLOCKED_CURRENT_OFFICIAL_SESSION_REQUIRED";
            return result;
        }
        if (input.client_build_id != kExpectedClientSha256) {
            result.status = "EVIDENCE_BLOCKED_EXACT_TARGET_IDENTITY_MISMATCH";
            return result;
        }
        if (!input.gates.AllPassed()) {
            result.status = "EVIDENCE_BLOCKED_CONTRACT_GATES_INCOMPLETE";
            return result;
        }
        if (input.authority != UltimateAuthority::Verified) {
            result.status = "EVIDENCE_BLOCKED_VERIFIED_AUTHORITY_REQUIRED";
            return result;
        }
        if (input.sensitive_value_persisted) {
            result.status = "EVIDENCE_BLOCKED_SENSITIVE_VALUE";
            return result;
        }
        if (input.session_id.empty() || input.candidate_id.empty() ||
            input.event_id.empty() || input.timestamp.empty() ||
            input.source_token.empty() || input.sequence == 0U ||
            input.process_id == 0U || input.thread_id == 0U || input.rva == 0U) {
            result.status = "EVIDENCE_BLOCKED_RUNTIME_BINDING_INCOMPLETE";
            return result;
        }
        if (SanitizeComponent(input.candidate_id) != input.candidate_id ||
            SanitizeComponent(input.source_token) != input.source_token) {
            result.status = "EVIDENCE_BLOCKED_UNSAFE_RUNTIME_IDENTIFIER";
            return result;
        }
        const auto pointer_shaped = [](std::string_view value) {
            return value.find("0x") != std::string_view::npos ||
                value.find("0X") != std::string_view::npos;
        };
        if (pointer_shaped(input.object_token) || pointer_shaped(input.value_token)) {
            result.status = "EVIDENCE_BLOCKED_RAW_POINTER_TOKEN";
            return result;
        }

        God2SemanticEventV2 event;
        event.event_type = input.event_type;
        event.event_id = input.event_id;
        event.sequence = input.sequence;
        event.timestamp = input.timestamp;
        event.thread_id = input.thread_id;
        event.process_id = input.process_id;
        event.session_id = input.session_id;
        event.client_build_id = input.client_build_id;
        event.module_id = "God2_opt.exe";
        event.rva = input.rva;
        event.callsite_rva = input.callsite_rva;
        event.object_token = input.object_token;
        event.value_token = input.value_token;
        event.source_token = input.source_token;
        event.authority_hint = input.authority;
        event.sensitive_mask_status = "NotSensitive";
        event.payload = input.payload;
        std::string payload_error;
        if (!ValidateCanonicalSemanticPayload(event, nullptr, &payload_error)) {
            result.status = "EVIDENCE_BLOCKED_CANONICAL_PAYLOAD_REQUIRED";
            return result;
        }
        const auto dispatch = DispatchSemanticEvent(event);
        if (!dispatch.dispatchable || !dispatch.promotion_allowed) {
            result.status = "EVIDENCE_BLOCKED_EVENT_PROMOTION_POLICY";
            return result;
        }
        result.activation_allowed = true;
        result.event_produced = true;
        result.status = "CURRENT_OFFICIAL_DEEP_RUNTIME_EVENT_PRODUCED";
        result.event = std::move(event);
        return result;
    } catch (...) {
        result.activation_allowed = false;
        result.event_produced = false;
        result.event.reset();
        result.status = "EVIDENCE_BLOCKED_PRODUCER_EXCEPTION_ISOLATED";
        return result;
    }
}

std::string SerializeSemanticEventV2(const God2SemanticEventV2& event) {
    God2SemanticEventV2 safe_event = event;
    SuppressSensitiveSemanticPayload(&safe_event);
    std::string payload_error;
    if (!ValidateCanonicalSemanticPayload(safe_event, nullptr, &payload_error))
        SuppressUnclassifiedSemanticPayload(&safe_event);
    std::ostringstream output;
    output << '{'
           << "\"SchemaId\":\"God2SemanticEvent\","
           << "\"SchemaVersion\":2,"
           << "\"EventType\":" << JsonQuoted(ToString(safe_event.event_type)) << ','
           << "\"EventId\":" << JsonQuoted(safe_event.event_id) << ','
           << "\"Sequence\":" << safe_event.sequence << ','
           << "\"Timestamp\":" << JsonQuoted(safe_event.timestamp) << ','
           << "\"ThreadId\":" << safe_event.thread_id << ','
           << "\"ProcessId\":" << safe_event.process_id << ','
           << "\"SessionId\":" << JsonQuoted(safe_event.session_id) << ','
           << "\"ClientBuildId\":" << JsonQuoted(safe_event.client_build_id) << ','
           << "\"ModuleId\":" << JsonQuoted(safe_event.module_id) << ','
           << "\"RVA\":" << RvaForWire(safe_event.rva, safe_event.rva_expression) << ','
           << "\"CallsiteRVA\":" << RvaForWire(safe_event.callsite_rva,
                                                     safe_event.callsite_rva_expression) << ','
           << "\"ParentEventId\":" << JsonQuoted(safe_event.parent_event_id) << ','
           << "\"ContextId\":" << JsonQuoted(safe_event.context_id) << ','
           << "\"ActionId\":" << JsonQuoted(safe_event.action_id) << ','
           << "\"ObjectToken\":" << JsonQuoted(safe_event.object_token) << ','
           << "\"ValueToken\":" << JsonQuoted(safe_event.value_token) << ','
           << "\"SourceToken\":" << JsonQuoted(safe_event.source_token) << ','
           << "\"AuthorityHint\":" << JsonQuoted(ToString(safe_event.authority_hint)) << ','
           << "\"SensitiveMaskStatus\":" << JsonQuoted(safe_event.sensitive_mask_status) << ','
           << "\"Payload\":" << PayloadObjectForWire(safe_event.payload);
    for (const auto& [key, value] : safe_event.evidence_binding) {
        if (key == "SchemaId" ||
            LegacyCompanionKeys().find(key) == LegacyCompanionKeys().end()) continue;
        output << ',' << JsonQuoted(key) << ':' << JsonQuoted(value);
    }
    output << '}';
    return output.str();
}

SemanticEventReadResult ReadSemanticEvent(std::string_view wire) {
    SemanticEventReadResult result;
    StrictJsonObject object;
    if (!StrictFlatJsonParser(wire).Parse(&object, &result.error)) return result;
    const auto* schema_atom = FindAtom(object, "SchemaVersion");
    if (schema_atom == nullptr) {
        result.error = "missing SchemaVersion";
        return result;
    }
    std::uint32_t version = 0;
    if (!ParseUnsigned(schema_atom->value, &version)) {
        result.error = "invalid SchemaVersion";
        return result;
    }
    if (version == 2U) {
        if (!IsStrictV2KeySet(object, &result.error)) return result;
        auto event_type = ParseUltimateSemanticEventType(AtomString(object, "EventType"));
        auto authority = ParseUltimateAuthority(AtomString(object, "AuthorityHint"));
        if (!event_type || !authority) {
            result.error = !event_type ? "unknown v2 EventType" : "unknown v2 AuthorityHint";
            return result;
        }
        std::uint64_t sequence = 0;
        std::uint64_t rva = 0;
        std::uint64_t callsite_rva = 0;
        std::uint32_t thread_id = 0;
        std::uint32_t process_id = 0;
        if (!ParseUnsigned(AtomString(object, "Sequence"), &sequence) ||
            !ParseUnsigned(AtomString(object, "ThreadId"), &thread_id) ||
            !ParseUnsigned(AtomString(object, "ProcessId"), &process_id)) {
            result.error = "invalid v2 numeric field";
            return result;
        }
        if (process_id == 0U) {
            result.error = "v2 ProcessId must be non-zero";
            return result;
        }
        rva = AtomUint64(object, "RVA");
        callsite_rva = AtomUint64(object, "CallsiteRVA");
        auto& event = result.event;
        event.schema_version = 2;
        event.event_type = *event_type;
        event.event_id = AtomString(object, "EventId");
        event.sequence = sequence;
        event.timestamp = AtomString(object, "Timestamp");
        event.thread_id = thread_id;
        event.process_id = process_id;
        event.session_id = AtomString(object, "SessionId");
        event.client_build_id = AtomString(object, "ClientBuildId");
        event.module_id = AtomString(object, "ModuleId");
        event.rva = rva;
        event.callsite_rva = callsite_rva;
        const auto* rva_atom = FindAtom(object, "RVA");
        const auto* callsite_atom = FindAtom(object, "CallsiteRVA");
        if (rva_atom != nullptr && rva_atom->kind == JsonAtomKind::String)
            event.rva_expression = rva_atom->value;
        if (callsite_atom != nullptr && callsite_atom->kind == JsonAtomKind::String)
            event.callsite_rva_expression = callsite_atom->value;
        event.parent_event_id = AtomString(object, "ParentEventId");
        event.context_id = AtomString(object, "ContextId");
        event.action_id = AtomString(object, "ActionId");
        event.object_token = AtomString(object, "ObjectToken");
        event.value_token = AtomString(object, "ValueToken");
        event.source_token = AtomString(object, "SourceToken");
        event.authority_hint = *authority;
        event.sensitive_mask_status = AtomString(object, "SensitiveMaskStatus");
        event.payload = AtomString(object, "Payload");
        for (const auto& [key, atom] : object) {
            if (LegacyCompanionKeys().find(key) != LegacyCompanionKeys().end() &&
                atom.kind != JsonAtomKind::Null) {
                event.evidence_binding.emplace(key, atom.value);
            }
        }
        if (event.event_id.empty() || event.timestamp.empty() || event.session_id.empty() ||
            event.client_build_id.empty() || event.module_id.empty() ||
            event.sensitive_mask_status.empty()) {
            result.error = "required v2 identity field is empty";
            return result;
        }
        SuppressSensitiveSemanticPayload(&event);
        if (!ValidateCanonicalSemanticPayload(event, nullptr, &result.error)) return result;
        result.dispatch = DispatchSemanticEvent(event);
        if (!result.dispatch.dispatchable) {
            result.error = "v2 EventType has no dispatch capability";
            return result;
        }
        result.success = true;
        return result;
    }
    if (version != 1U) {
        result.error = "unsupported SemanticEvent version";
        return result;
    }
    if (schema_atom->kind != JsonAtomKind::Number) {
        result.error = "v1 SchemaVersion must be a number";
        return result;
    }
    const auto* v1_schema_id = FindAtom(object, "SchemaId");
    if (v1_schema_id != nullptr && (v1_schema_id->kind != JsonAtomKind::String ||
        v1_schema_id->value != "God2SemanticEvent")) {
        result.error = "unsupported v1 SchemaId";
        return result;
    }
    const auto* v1_event_type = FindAtom(object, "EventType");
    const auto* v1_semantic_id = FindAtom(object, "SemanticEventId");
    const auto* v1_event_id = FindAtom(object, "EventId");
    if (v1_event_type == nullptr || v1_event_type->kind != JsonAtomKind::String) {
        result.error = "v1 EventType must be a string";
        return result;
    }
    if ((v1_semantic_id != nullptr &&
         (v1_semantic_id->kind != JsonAtomKind::String || v1_semantic_id->value.empty())) ||
        (v1_event_id != nullptr &&
         (v1_event_id->kind != JsonAtomKind::String || v1_event_id->value.empty()))) {
        result.error = "v1 event identity must be a non-empty string";
        return result;
    }
    if (v1_semantic_id != nullptr && v1_event_id != nullptr &&
        v1_semantic_id->value != v1_event_id->value) {
        result.error = "v1 SemanticEventId contradicts EventId";
        return result;
    }
    bool valid_type = false;
    const auto event_type = MapV1EventType(AtomString(object, "EventType"), &valid_type);
    const auto event_id = AtomString(object, "SemanticEventId", AtomString(object, "EventId"));
    if (!valid_type || event_id.empty()) {
        result.error = !valid_type ? "unknown v1 EventType" : "missing v1 event identity";
        return result;
    }
    auto& event = result.event;
    event.schema_version = 2;
    event.event_type = event_type;
    event.event_id = event_id;
    event.sequence = AtomUint64(object, "Sequence", AtomUint64(object, "Qpc"));
    event.timestamp = AtomString(object, "Timestamp", AtomString(object, "ObservedAtUnixMs"));
    event.thread_id = static_cast<std::uint32_t>(AtomUint64(object, "ThreadId"));
    event.process_id = static_cast<std::uint32_t>(AtomUint64(object, "ProcessId"));
    event.session_id = AtomString(object, "SessionId", "LegacyV1SessionUnknown");
    event.client_build_id = AtomString(object, "ClientBuildId",
        AtomString(object, "ClientSha256Expected", "LegacyV1BuildUnknown"));
    event.module_id = AtomString(object, "ModuleId", AtomString(object, "Module", "UnknownModule"));
    event.rva = AtomUint64(object, "RVA", AtomUint64(object, "Rva"));
    event.callsite_rva = AtomUint64(object, "CallsiteRVA", AtomUint64(object, "CallerRva"));
    const auto* v1_rva = FindAtom(object, "RVA");
    if (v1_rva == nullptr) v1_rva = FindAtom(object, "Rva");
    const auto* v1_callsite = FindAtom(object, "CallsiteRVA");
    if (v1_callsite == nullptr) v1_callsite = FindAtom(object, "CallerRva");
    if (v1_rva != nullptr && v1_rva->kind == JsonAtomKind::String)
        event.rva_expression = v1_rva->value;
    if (v1_callsite != nullptr && v1_callsite->kind == JsonAtomKind::String)
        event.callsite_rva_expression = v1_callsite->value;
    event.parent_event_id = AtomString(object, "ExactParentSemanticEventId",
        AtomString(object, "ParentEventId"));
    event.context_id = AtomString(object, "ContextInvocationId",
        AtomString(object, "HookInvocationId"));
    event.action_id = AtomString(object, "ActionInstanceId");
    event.object_token = AtomString(object, "ObjectToken");
    event.value_token = AtomString(object, "ValueTokenId",
        AtomString(object, "AssociatedValueTokenId"));
    event.source_token = AtomString(object, "SourceTokenId");
    event.authority_hint = UltimateAuthority::Unknown;
    const auto legacy_sensitive_value =
        LowerAscii(TrimAscii(AtomString(object, "SensitiveValue")));
    event.sensitive_mask_status = legacy_sensitive_value == "false" ?
        "NotSensitive" : "LegacySensitiveValueUnverifiedOrSensitive";
    event.payload = V1CompatibilityPayload(object);
    for (const auto& [key, atom] : object) {
        if (LegacyCompanionKeys().find(key) != LegacyCompanionKeys().end() &&
            atom.kind != JsonAtomKind::Null) {
            event.evidence_binding.emplace(key, atom.value);
        }
    }
    SuppressSensitiveSemanticPayload(&event);
    if (!ValidateCanonicalSemanticPayload(event, nullptr, &result.error)) return result;
    result.dispatch = DispatchSemanticEvent(event);
    if (!result.dispatch.dispatchable) {
        result.error = "v1 EventType has no dispatch capability";
        return result;
    }
    result.success = true;
    result.read_from_v1 = true;
    return result;
}

const char* ToString(SemanticEventWireProfile value) noexcept {
    switch (value) {
    case SemanticEventWireProfile::CanonicalV1WriterProfile:
        return "CanonicalV1WriterProfile";
    case SemanticEventWireProfile::NonCanonicalV1CompatibilityInput:
        return "NonCanonicalV1CompatibilityInput";
    case SemanticEventWireProfile::CanonicalV2Envelope:
        return "CanonicalV2Envelope";
    case SemanticEventWireProfile::Invalid:
        break;
    }
    return "Invalid";
}

SemanticEventWireProfile ClassifySemanticEventWire(std::string_view wire) {
    StrictJsonObject object;
    std::string ignored;
    if (!StrictFlatJsonParser(wire).Parse(&object, &ignored))
        return SemanticEventWireProfile::Invalid;
    const auto read = ReadSemanticEvent(wire);
    if (!read.success) return SemanticEventWireProfile::Invalid;
    const auto* schema_version = FindAtom(object, "SchemaVersion");
    std::uint32_t version = 0;
    if (schema_version == nullptr ||
        !ParseUnsigned(schema_version->value, &version))
        return SemanticEventWireProfile::Invalid;
    if (version == 1U) {
        return IsCanonicalV1WriterProfile(object) ?
            SemanticEventWireProfile::CanonicalV1WriterProfile :
            SemanticEventWireProfile::NonCanonicalV1CompatibilityInput;
    }
    if (version == 2U && IsCanonicalV2Envelope(object))
        return SemanticEventWireProfile::CanonicalV2Envelope;
    return SemanticEventWireProfile::Invalid;
}

bool CanEvaluateSemanticPromotion(SemanticEventWireProfile profile) noexcept {
    return profile == SemanticEventWireProfile::CanonicalV2Envelope;
}

bool EquivalentSemanticEvent(const God2SemanticEventV2& left,
                             const God2SemanticEventV2& right) noexcept {
    const auto bindings_equal = [&]() noexcept {
        const auto left_schema = left.evidence_binding.find("SchemaId");
        const auto right_schema = right.evidence_binding.find("SchemaId");
        const auto left_count = left.evidence_binding.size() -
            (left_schema == left.evidence_binding.end() ? 0U : 1U);
        const auto right_count = right.evidence_binding.size() -
            (right_schema == right.evidence_binding.end() ? 0U : 1U);
        if (left_count != right_count) return false;
        for (const auto& [key, value] : left.evidence_binding) {
            if (key == "SchemaId") continue;
            const auto found = right.evidence_binding.find(key);
            if (found == right.evidence_binding.end() || found->second != value)
                return false;
        }
        return true;
    };
    return left.schema_version == right.schema_version && left.event_type == right.event_type &&
        left.event_id == right.event_id && left.sequence == right.sequence &&
        left.timestamp == right.timestamp && left.thread_id == right.thread_id &&
        left.process_id == right.process_id && left.session_id == right.session_id &&
        left.client_build_id == right.client_build_id && left.module_id == right.module_id &&
        left.rva == right.rva && left.callsite_rva == right.callsite_rva &&
        left.rva_expression == right.rva_expression &&
        left.callsite_rva_expression == right.callsite_rva_expression &&
        left.parent_event_id == right.parent_event_id && left.context_id == right.context_id &&
        left.action_id == right.action_id && left.object_token == right.object_token &&
        left.value_token == right.value_token && left.source_token == right.source_token &&
        left.authority_hint == right.authority_hint &&
        left.sensitive_mask_status == right.sensitive_mask_status && left.payload == right.payload &&
        bindings_equal();
}

bool CanPromoteVerified(const UltimatePromotionGate& gate) noexcept {
    return gate.exact_build_binding && gate.typed_source && gate.stable_object_or_context &&
        gate.repeated_observations >= 2U && gate.contradictions == 0U && gate.causal_path &&
        gate.verified_consumer_or_mutation &&
        (!gate.cross_session_required || gate.cross_session_consistent) &&
        (!gate.replay_required || gate.replay_consistent);
}

bool CanActivateDeepProbeCandidate(
    const UltimateDeepProbeCandidate& candidate,
    const UltimateDeepProbeVerification& verification) noexcept {
    const auto valid_signature = [](std::string_view value) noexcept {
        if (value.size() != 23U) return false;
        for (std::size_t index = 0; index < value.size(); ++index) {
            if ((index + 1U) % 3U == 0U) {
                if (value[index] != ' ') return false;
            } else if (std::isxdigit(static_cast<unsigned char>(value[index])) == 0) {
                return false;
            }
        }
        return true;
    };
    const auto domain_index = static_cast<std::size_t>(candidate.domain);
    return domain_index >= static_cast<std::size_t>(UltimateProbeDomain::ObjectProbe) &&
        domain_index < kUltimateProbeDomainCount && !candidate.module_section.empty() &&
        candidate.callsite_rva != 0U && candidate.target_rva != 0U &&
        candidate.callsite_rva != candidate.target_rva &&
        valid_signature(candidate.signature) &&
        candidate.signature_mask == "FF FF FF FF FF FF FF FF" &&
        candidate.discovery_provenance.find("BoundedExecutableDirectCall") !=
            std::string::npos && candidate.calling_convention_state != "UNKNOWN" &&
        !candidate.calling_convention_state.empty() &&
        (candidate.authority == UltimateAuthority::Unknown ||
         candidate.authority == UltimateAuthority::Hypothesis) &&
        verification.exact_target_identity && verification.executable_section &&
        verification.exact_candidate_bytes && verification.calling_convention_verified &&
        verification.typed_runtime_evidence &&
        verification.repeated_causal_observations >= 2U &&
        verification.contradictions == 0U &&
        verification.stable_object_or_context &&
        verification.verified_consumer_or_mutation &&
        verification.sensitive_mask_contract &&
        verification.argument_contract_verified &&
        verification.return_value_lifetime_verified &&
        verification.thread_context_verified &&
        verification.reentrancy_risk_verified;
}

struct UltimatePriorityRing::Impl {
    explicit Impl(std::size_t requested_capacity) : capacity(std::max<std::size_t>(1U, requested_capacity)) {}

    void Drop(UltimateProbeDomain domain, std::uint64_t sequence, std::string_view reason) noexcept {
        auto& current = status[static_cast<std::size_t>(domain)];
        ++current.dropped;
        if (current.first_dropped_sequence == 0U) current.first_dropped_sequence = sequence;
        current.last_dropped_sequence = sequence;
        current.last_drop_reason = std::string(reason);
        current.semantic_evidence_incomplete = true;
        incomplete = true;
    }

    void RefreshLag() noexcept {
        std::array<std::uint64_t, kUltimateProbeDomainCount> counts{};
        for (const auto& record : records) ++counts[static_cast<std::size_t>(record.domain)];
        for (std::size_t index = 0; index < counts.size(); ++index) {
            status[index].consumer_lag = counts[index];
            status[index].queue_high_water = std::max(status[index].queue_high_water, counts[index]);
        }
    }

    mutable std::mutex mutex;
    std::deque<UltimateRingRecord> records;
    std::array<UltimateDomainBackpressure, kUltimateProbeDomainCount> status{};
    std::size_t capacity = 1;
    std::uint64_t next_sequence = 1;
    bool stopped = false;
    bool incomplete = false;
};

UltimatePriorityRing::UltimatePriorityRing(std::size_t capacity) : impl_(std::make_unique<Impl>(capacity)) {}
UltimatePriorityRing::~UltimatePriorityRing() = default;

bool UltimatePriorityRing::Publish(UltimateProbeDomain domain, UltimateEventPriority priority,
                                   God2SemanticEventV2 event, std::string* reason) noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        auto& domain_status = impl_->status[static_cast<std::size_t>(domain)];
        if (event.sequence == 0U) event.sequence = impl_->next_sequence++;
        else impl_->next_sequence = std::max(impl_->next_sequence, event.sequence + 1U);
        if (impl_->stopped) {
            impl_->Drop(domain, event.sequence, "RingStopped");
            if (reason != nullptr) *reason = "RingStopped";
            return false;
        }
        if (domain_status.disabled) {
            impl_->Drop(domain, event.sequence, "ProbeDisabled");
            if (reason != nullptr) *reason = "ProbeDisabled";
            return false;
        }
        if (std::any_of(impl_->records.begin(), impl_->records.end(), [&](const auto& record) {
            return record.event.sequence == event.sequence;
        })) {
            impl_->Drop(domain, event.sequence, "DuplicateSequence");
            if (reason != nullptr) *reason = "DuplicateSequence";
            return false;
        }
        if (impl_->records.size() >= impl_->capacity) {
            auto eviction = impl_->records.end();
            for (auto iterator = impl_->records.begin(); iterator != impl_->records.end(); ++iterator) {
                if (static_cast<std::uint8_t>(iterator->priority) <=
                    static_cast<std::uint8_t>(priority)) continue;
                if (eviction == impl_->records.end() ||
                    static_cast<std::uint8_t>(iterator->priority) >
                        static_cast<std::uint8_t>(eviction->priority) ||
                    (iterator->priority == eviction->priority &&
                     iterator->event.sequence < eviction->event.sequence)) {
                    eviction = iterator;
                }
            }
            if (eviction == impl_->records.end()) {
                impl_->Drop(domain, event.sequence, "CapacityExhausted");
                if (reason != nullptr) *reason = "CapacityExhausted";
                return false;
            }
            impl_->Drop(eviction->domain, eviction->event.sequence, "EvictedByHigherPriority");
            impl_->records.erase(eviction);
        }
        impl_->records.push_back(UltimateRingRecord{domain, priority, std::move(event)});
        ++domain_status.published;
        impl_->RefreshLag();
        if (reason != nullptr) reason->clear();
        return true;
    } catch (...) {
        if (reason != nullptr) *reason = "RingInternalFailure";
        return false;
    }
}

std::vector<UltimateRingRecord> UltimatePriorityRing::DrainBatch(
    std::size_t maximum_records) noexcept {
    std::vector<UltimateRingRecord> output;
    try {
        std::lock_guard lock(impl_->mutex);
        const auto count = std::min(maximum_records, impl_->records.size());
        output.reserve(count);
        for (std::size_t index = 0; index < count; ++index) {
            const auto next = std::min_element(impl_->records.begin(), impl_->records.end(),
                [](const auto& left, const auto& right) {
                    return left.event.sequence < right.event.sequence;
                });
            ++impl_->status[static_cast<std::size_t>(next->domain)].consumed;
            output.push_back(std::move(*next));
            impl_->records.erase(next);
        }
        impl_->RefreshLag();
    } catch (...) {
        output.clear();
    }
    return output;
}

void UltimatePriorityRing::RecordWriteFailure(UltimateProbeDomain domain,
                                              std::string_view reason) noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        auto& status = impl_->status[static_cast<std::size_t>(domain)];
        ++status.write_failures;
        status.diagnostic = reason.empty() ? "WriteFailure" : std::string(reason);
        status.semantic_evidence_incomplete = true;
        impl_->incomplete = true;
    } catch (...) {
    }
}

bool UltimatePriorityRing::ExecuteIsolated(UltimateProbeDomain domain,
                                           const std::function<void()>& operation) noexcept {
    try {
        operation();
        return true;
    } catch (...) {
        try {
            std::lock_guard lock(impl_->mutex);
            auto& status = impl_->status[static_cast<std::size_t>(domain)];
            status.disabled = true;
            status.diagnostic = "ProbeExceptionIsolated";
            status.semantic_evidence_incomplete = true;
            impl_->incomplete = true;
            God2SemanticEventV2 diagnostic;
            diagnostic.event_type = UltimateSemanticEventType::ProbeDiagnostic;
            diagnostic.event_id = "ProbeDiagnostic-" + std::to_string(impl_->next_sequence);
            diagnostic.sequence = impl_->next_sequence++;
            diagnostic.timestamp = UtcNow();
            diagnostic.session_id = "Runtime";
            diagnostic.client_build_id = std::string(kExpectedClientSha256);
            diagnostic.module_id = "UltimatePriorityRing";
            diagnostic.authority_hint = UltimateAuthority::Observed;
            diagnostic.sensitive_mask_status = "DiagnosticSecretsSuppressed";
            diagnostic.payload = "{\"Status\":\"ProbeExceptionIsolated\",\"Domain\":\"" +
                JsonEscape(ToString(domain)) + "\"}";
            if (impl_->records.size() < impl_->capacity) {
                impl_->records.push_back(UltimateRingRecord{
                    domain, UltimateEventPriority::Authoritative, std::move(diagnostic)});
                ++status.published;
                impl_->RefreshLag();
            } else {
                impl_->Drop(domain, diagnostic.sequence, "DiagnosticQueueFull");
            }
        } catch (...) {
        }
        return false;
    }
}

UltimateDomainBackpressure UltimatePriorityRing::DomainStatus(
    UltimateProbeDomain domain) const noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        return impl_->status[static_cast<std::size_t>(domain)];
    } catch (...) {
        UltimateDomainBackpressure failed;
        failed.semantic_evidence_incomplete = true;
        failed.diagnostic = "RingStatusUnavailable";
        return failed;
    }
}

std::array<UltimateDomainBackpressure, kUltimateProbeDomainCount>
UltimatePriorityRing::AllDomainStatus() const noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        return impl_->status;
    } catch (...) {
        std::array<UltimateDomainBackpressure, kUltimateProbeDomainCount> failed{};
        for (auto& status : failed) {
            status.semantic_evidence_incomplete = true;
            status.diagnostic = "RingStatusUnavailable";
        }
        return failed;
    }
}

std::size_t UltimatePriorityRing::Size() const noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        return impl_->records.size();
    } catch (...) {
        return 0;
    }
}

std::size_t UltimatePriorityRing::Capacity() const noexcept {
    return impl_->capacity;
}

bool UltimatePriorityRing::SemanticEvidenceIncomplete() const noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        return impl_->incomplete;
    } catch (...) {
        return true;
    }
}

void UltimatePriorityRing::Stop() noexcept {
    try {
        std::lock_guard lock(impl_->mutex);
        impl_->stopped = true;
    } catch (...) {
    }
}

namespace {

using Matrix = std::vector<std::vector<double>>;

double Dot(const std::vector<double>& left, const std::vector<double>& right) noexcept {
    const auto count = std::min(left.size(), right.size());
    double result = 0.0;
    for (std::size_t index = 0; index < count; ++index) result += left[index] * right[index];
    return result;
}

double SquaredDistance(const std::vector<double>& left,
                       const std::vector<double>& right) noexcept {
    const auto count = std::min(left.size(), right.size());
    double result = 0.0;
    for (std::size_t index = 0; index < count; ++index) {
        const double delta = left[index] - right[index];
        result += delta * delta;
    }
    return result;
}

double Norm(const std::vector<double>& value) noexcept {
    return std::sqrt(std::max(0.0, Dot(value, value)));
}

double Cosine(const std::vector<double>& left, const std::vector<double>& right) noexcept {
    const double denominator = Norm(left) * Norm(right);
    if (denominator <= std::numeric_limits<double>::epsilon()) return 0.0;
    return std::clamp(Dot(left, right) / denominator, -1.0, 1.0);
}

Matrix Standardize(const std::vector<UltimateMlSample>& samples, std::size_t dimensions) {
    Matrix result(samples.size(), std::vector<double>(dimensions, 0.0));
    std::vector<double> means(dimensions, 0.0);
    for (const auto& sample : samples) {
        for (std::size_t dimension = 0; dimension < dimensions; ++dimension)
            means[dimension] += sample.features[dimension];
    }
    const double count = static_cast<double>(samples.size());
    for (double& mean : means) mean /= count;
    std::vector<double> deviations(dimensions, 0.0);
    for (const auto& sample : samples) {
        for (std::size_t dimension = 0; dimension < dimensions; ++dimension) {
            const double delta = sample.features[dimension] - means[dimension];
            deviations[dimension] += delta * delta;
        }
    }
    for (double& deviation : deviations)
        deviation = std::sqrt(deviation / count);
    for (std::size_t row = 0; row < samples.size(); ++row) {
        for (std::size_t dimension = 0; dimension < dimensions; ++dimension) {
            const double scale = deviations[dimension] <= std::numeric_limits<double>::epsilon() ?
                1.0 : deviations[dimension];
            result[row][dimension] = (samples[row].features[dimension] - means[dimension]) / scale;
        }
    }
    return result;
}

Matrix Covariance(const Matrix& standardized) {
    if (standardized.empty()) return {};
    const std::size_t dimensions = standardized.front().size();
    Matrix covariance(dimensions, std::vector<double>(dimensions, 0.0));
    const double divisor = standardized.size() > 1U ?
        static_cast<double>(standardized.size() - 1U) : 1.0;
    for (const auto& row : standardized) {
        for (std::size_t left = 0; left < dimensions; ++left) {
            for (std::size_t right = left; right < dimensions; ++right)
                covariance[left][right] += row[left] * row[right] / divisor;
        }
    }
    for (std::size_t left = 0; left < dimensions; ++left) {
        for (std::size_t right = left + 1U; right < dimensions; ++right)
            covariance[right][left] = covariance[left][right];
    }
    return covariance;
}

std::vector<double> Multiply(const Matrix& matrix, const std::vector<double>& vector) {
    std::vector<double> result(matrix.size(), 0.0);
    for (std::size_t row = 0; row < matrix.size(); ++row)
        result[row] = Dot(matrix[row], vector);
    return result;
}

Matrix PrincipalComponents(Matrix covariance, std::size_t requested) {
    Matrix components;
    if (covariance.empty()) return components;
    const auto dimensions = covariance.size();
    requested = std::min(requested, dimensions);
    for (std::size_t component = 0; component < requested; ++component) {
        std::vector<double> vector(dimensions, 0.0);
        for (std::size_t index = 0; index < dimensions; ++index)
            vector[index] = 1.0 + static_cast<double>((index + component) % 7U) / 7.0;
        for (std::size_t iteration = 0; iteration < 64U; ++iteration) {
            auto next = Multiply(covariance, vector);
            const double norm = Norm(next);
            if (norm <= 1e-12) break;
            for (double& value : next) value /= norm;
            vector = std::move(next);
        }
        const auto transformed = Multiply(covariance, vector);
        const double eigenvalue = Dot(vector, transformed);
        if (std::abs(eigenvalue) <= 1e-12) {
            vector.assign(dimensions, 0.0);
            vector[component % dimensions] = 1.0;
        }
        components.push_back(vector);
        for (std::size_t row = 0; row < dimensions; ++row) {
            for (std::size_t column = 0; column < dimensions; ++column)
                covariance[row][column] -= eigenvalue * vector[row] * vector[column];
        }
    }
    return components;
}

Matrix Embed(const Matrix& standardized) {
    if (standardized.empty()) return {};
    const auto component_count = std::min<std::size_t>(
        3U, std::min(standardized.size(), standardized.front().size()));
    const auto components = PrincipalComponents(Covariance(standardized), component_count);
    Matrix embeddings(standardized.size(), std::vector<double>(components.size(), 0.0));
    for (std::size_t row = 0; row < standardized.size(); ++row) {
        for (std::size_t component = 0; component < components.size(); ++component)
            embeddings[row][component] = Dot(standardized[row], components[component]);
    }
    return embeddings;
}

struct KMeansResult {
    std::vector<std::size_t> assignments;
    Matrix centroids;
};

KMeansResult RunKMeans(const Matrix& embeddings) {
    KMeansResult result;
    if (embeddings.empty()) return result;
    const std::size_t cluster_count = std::clamp<std::size_t>(
        static_cast<std::size_t>(std::sqrt(static_cast<double>(embeddings.size())) + 0.5),
        1U, std::min<std::size_t>(8U, embeddings.size()));
    result.centroids.push_back(embeddings.front());
    while (result.centroids.size() < cluster_count) {
        std::size_t farthest_index = 0;
        double farthest_distance = -1.0;
        for (std::size_t row = 0; row < embeddings.size(); ++row) {
            double nearest = std::numeric_limits<double>::max();
            for (const auto& centroid : result.centroids)
                nearest = std::min(nearest, SquaredDistance(embeddings[row], centroid));
            if (nearest > farthest_distance) {
                farthest_distance = nearest;
                farthest_index = row;
            }
        }
        result.centroids.push_back(embeddings[farthest_index]);
    }
    result.assignments.assign(embeddings.size(), 0U);
    for (std::size_t iteration = 0; iteration < 64U; ++iteration) {
        bool changed = false;
        for (std::size_t row = 0; row < embeddings.size(); ++row) {
            std::size_t best = 0;
            double distance = SquaredDistance(embeddings[row], result.centroids.front());
            for (std::size_t cluster = 1; cluster < result.centroids.size(); ++cluster) {
                const double candidate = SquaredDistance(embeddings[row], result.centroids[cluster]);
                if (candidate < distance) {
                    distance = candidate;
                    best = cluster;
                }
            }
            if (result.assignments[row] != best) {
                result.assignments[row] = best;
                changed = true;
            }
        }
        Matrix next(result.centroids.size(),
                    std::vector<double>(embeddings.front().size(), 0.0));
        std::vector<std::size_t> counts(result.centroids.size(), 0U);
        for (std::size_t row = 0; row < embeddings.size(); ++row) {
            const auto cluster = result.assignments[row];
            ++counts[cluster];
            for (std::size_t dimension = 0; dimension < embeddings[row].size(); ++dimension)
                next[cluster][dimension] += embeddings[row][dimension];
        }
        for (std::size_t cluster = 0; cluster < next.size(); ++cluster) {
            if (counts[cluster] == 0U) {
                next[cluster] = result.centroids[cluster];
                continue;
            }
            const double divisor = static_cast<double>(counts[cluster]);
            for (double& value : next[cluster]) value /= divisor;
        }
        result.centroids = std::move(next);
        if (!changed && iteration > 0U) break;
    }
    return result;
}

bool IsApprovedModelLicense(std::string_view license) {
    static const std::set<std::string> approved = {
        "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "CC-BY-4.0"
    };
    return approved.find(std::string(license)) != approved.end();
}

void ValidateModel(const std::optional<UltimateLocalModelArtifact>& model,
                   UltimateMlResult* result) {
    if (!model || model->path.empty() || model->name.empty() || model->version.empty() ||
        !IsApprovedModelLicense(model->license) || !IsSha256(model->sha256)) {
        result->diagnostics.push_back("LocalSemanticReasoner:EvidenceBlockedModelUnavailable");
        return;
    }
    std::error_code error;
    if (!fs::is_regular_file(model->path, error) || error) {
        result->diagnostics.push_back("LocalSemanticReasoner:EvidenceBlockedModelUnavailable");
        return;
    }
    const auto hash = CalculateFileSha256(model->path);
    if (!hash || !EqualAsciiInsensitive(*hash, model->sha256)) {
        result->diagnostics.push_back("LocalSemanticReasoner:EvidenceBlockedModelUnavailable");
        return;
    }
    // Hash/license checks validate metadata only.  This module has no model
    // provider load/inference contract, so it must never claim Available or
    // Active solely because arbitrary bytes match their declared digest.
    result->model_status = "MetadataValidatedRuntimeUnavailable";
    result->model_name = model->name;
    result->model_version = model->version;
    result->model_license = model->license;
    result->model_sha256 = *hash;
    result->diagnostics.push_back(
        "LocalSemanticReasoner:MetadataOnlyProviderRuntimeUnavailable");
}

std::vector<UltimateSequenceRanking> RankSequences(
    const std::vector<UltimateMlSequence>& sequences) {
    std::map<std::pair<std::string, std::string>, std::uint64_t> bigrams;
    std::uint64_t total = 0;
    for (const auto& sequence : sequences) {
        for (std::size_t index = 1; index < sequence.symbols.size(); ++index) {
            ++bigrams[{sequence.symbols[index - 1U], sequence.symbols[index]}];
            ++total;
        }
    }
    std::vector<UltimateSequenceRanking> result;
    result.reserve(sequences.size());
    for (const auto& sequence : sequences) {
        double novelty = 0.0;
        std::size_t transitions = 0;
        for (std::size_t index = 1; index < sequence.symbols.size(); ++index) {
            const auto count = bigrams[{sequence.symbols[index - 1U], sequence.symbols[index]}];
            const double probability = total == 0U ? 1.0 :
                static_cast<double>(count) / static_cast<double>(total);
            novelty += -std::log(std::max(probability, 1e-12));
            ++transitions;
        }
        if (transitions != 0U) novelty /= static_cast<double>(transitions);
        result.push_back(UltimateSequenceRanking{
            sequence.id, novelty, UltimateAuthority::Hypothesis});
    }
    std::stable_sort(result.begin(), result.end(), [](const auto& left, const auto& right) {
        if (left.novelty_score != right.novelty_score)
            return left.novelty_score > right.novelty_score;
        return left.sequence_id < right.sequence_id;
    });
    return result;
}

} // namespace

UltimateMlResult UltimateDeterministicMl::Analyze(
    const std::vector<UltimateMlSample>& samples,
    const std::vector<UltimateMlSequence>& sequences,
    const std::optional<UltimateLocalModelArtifact>& local_model) {
    UltimateMlResult result;
    ValidateModel(local_model, &result);
    result.sequence_rankings = RankSequences(sequences);
    if (samples.empty()) {
        result.diagnostics.push_back("DeterministicMl:EvidenceBlockedNoFeatureEvidence");
        return result;
    }
    std::size_t dimensions = samples.front().features.size();
    if (dimensions == 0U) {
        result.diagnostics.push_back("DeterministicMl:EvidenceBlockedEmptyFeatureVector");
        return result;
    }
    if (dimensions > 256U) {
        dimensions = 256U;
        result.diagnostics.push_back("DeterministicMl:FeatureDimensionsBoundedTo256");
    }
    for (const auto& sample : samples) {
        if (sample.id.empty() || sample.features.size() < dimensions ||
            !std::all_of(sample.features.begin(), sample.features.begin() +
                static_cast<std::ptrdiff_t>(dimensions), [](double value) {
                    return std::isfinite(value);
                })) {
            result.diagnostics.push_back("DeterministicMl:EvidenceBlockedInvalidFeatureSet");
            return result;
        }
    }
    const auto standardized = Standardize(samples, dimensions);
    const auto embeddings = Embed(standardized);
    const auto clusters = RunKMeans(embeddings);
    if (embeddings.size() != samples.size() || clusters.assignments.size() != samples.size()) {
        result.diagnostics.push_back("DeterministicMl:EvidenceBlockedTrainingFailure");
        return result;
    }
    result.predictions.resize(samples.size());
    std::vector<double> centroid_distances(samples.size(), 0.0);
    for (std::size_t row = 0; row < samples.size(); ++row) {
        auto& prediction = result.predictions[row];
        prediction.sample_id = samples[row].id;
        prediction.embedding = embeddings[row];
        prediction.cluster = clusters.assignments[row];
        prediction.authority = UltimateAuthority::Hypothesis;
        centroid_distances[row] = std::sqrt(SquaredDistance(
            embeddings[row], clusters.centroids[prediction.cluster]));
        if (samples.size() > 1U) {
            std::size_t nearest = row == 0U ? 1U : 0U;
            double similarity = Cosine(embeddings[row], embeddings[nearest]);
            for (std::size_t candidate = 0; candidate < samples.size(); ++candidate) {
                if (candidate == row) continue;
                const double current = Cosine(embeddings[row], embeddings[candidate]);
                if (current > similarity) {
                    nearest = candidate;
                    similarity = current;
                }
            }
            prediction.nearest_sample_id = samples[nearest].id;
            prediction.similarity = similarity;
        }
        std::map<std::string, double> label_scores;
        for (std::size_t seed = 0; seed < samples.size(); ++seed) {
            if (!samples[seed].verified_label) continue;
            const double weight = std::exp(-SquaredDistance(embeddings[row], embeddings[seed]));
            label_scores[*samples[seed].verified_label] += weight;
        }
        for (const auto& [label, score] : label_scores) {
            if (score > prediction.propagated_label_score ||
                (score == prediction.propagated_label_score && label < prediction.propagated_label)) {
                prediction.propagated_label = label;
                prediction.propagated_label_score = score;
            }
        }
    }
    const double mean_distance = std::accumulate(
        centroid_distances.begin(), centroid_distances.end(), 0.0) /
        static_cast<double>(centroid_distances.size());
    double variance = 0.0;
    for (const double value : centroid_distances) {
        const double delta = value - mean_distance;
        variance += delta * delta;
    }
    const double deviation = std::sqrt(variance / static_cast<double>(centroid_distances.size()));
    for (std::size_t index = 0; index < result.predictions.size(); ++index) {
        result.predictions[index].outlier_score = deviation <= 1e-12 ? 0.0 :
            (centroid_distances[index] - mean_distance) / deviation;
        // This is a permanent authority assignment, independent of model
        // availability or label quality.
        result.predictions[index].authority = UltimateAuthority::Hypothesis;
    }
    return result;
}

namespace {

bool LooksLikeBarePointer(std::string_view value) noexcept {
    if (value.size() < 3U || value[0] != '0' || (value[1] != 'x' && value[1] != 'X')) return false;
    return std::all_of(value.begin() + 2, value.end(), [](char c) { return IsHexDigit(c); });
}

std::string StableIdentity(const Fields& fields) {
    std::string family = FieldString(fields, "EntityFamily",
        FieldString(fields, "ObjectFamily", FieldString(fields, "Family")));
    std::string explicit_identity = FieldString(fields, "CanonicalIdentity");
    if (!explicit_identity.empty() && !LooksLikeBarePointer(explicit_identity) &&
        explicit_identity.find(':') != std::string::npos) return explicit_identity;
    const std::string template_id = FieldString(fields, "StableTemplateId",
        FieldString(fields, "TemplateId"));
    if (!family.empty() && !template_id.empty() && !LooksLikeBarePointer(template_id))
        return family + ':' + template_id;
    const std::string runtime_id = FieldString(fields, "RuntimeInstanceId");
    if (!family.empty() && !runtime_id.empty() && !LooksLikeBarePointer(runtime_id))
        return family + "Runtime:" + runtime_id;
    return {};
}

void MergeEvidence(std::vector<UltimateEvidenceRef>* target, const UltimateEvidenceRef& evidence) {
    if (std::none_of(target->begin(), target->end(), [&](const auto& current) {
        return current.evidence_id == evidence.evidence_id;
    })) target->push_back(evidence);
}

void RecoverObjects(const std::vector<God2SemanticEventV2>& events,
                    UltimateRecoveryResult* result) {
    std::map<std::string, UltimateObjectRecord> by_token;
    std::map<std::string, AuthorityAccumulator> authority_by_token;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::ObjectAllocated &&
            event.event_type != UltimateSemanticEventType::ObjectDestroyed &&
            event.event_type != UltimateSemanticEventType::ObjectResolved &&
            event.event_type != UltimateSemanticEventType::ObjectLookup &&
            event.event_type != UltimateSemanticEventType::SnapshotObject) continue;
        const auto fields = PayloadFields(event);
        const std::string token = event.object_token.empty() ?
            FieldString(fields, "ObjectToken") : event.object_token;
        if (token.empty()) {
            result->missing_evidence.push_back({
                "ObjectToken", event.event_id, "Stable object token and allocation lifetime",
                "Passive exact-build ObjectProbe observation", 0U, UltimateAuthority::Unknown});
            continue;
        }
        auto& object = by_token[token];
        object.object_token = token;
        MergeEvidence(&object.evidence, EvidenceFromEvent(event));
        authority_by_token[token].Add(event, fields);
        if (event.event_type == UltimateSemanticEventType::ObjectAllocated) {
            object.allocation_size = FieldUint64(fields, "AllocationSize", object.allocation_size);
            object.vtable = FieldString(fields, "VTable", object.vtable);
            object.constructor_path = FieldString(fields, "ConstructorPath", object.constructor_path);
            object.factory = FieldString(fields, "Factory", object.factory);
        } else if (event.event_type == UltimateSemanticEventType::ObjectDestroyed) {
            object.destructor_path = FieldString(fields, "DestructorPath", object.destructor_path);
        } else {
            object.family = FieldString(fields, "EntityFamily",
                FieldString(fields, "ObjectFamily", FieldString(fields, "Family", object.family)));
            object.stable_template_id = FieldString(fields, "StableTemplateId",
                FieldString(fields, "TemplateId", object.stable_template_id));
            object.runtime_instance_id = FieldString(fields, "RuntimeInstanceId",
                object.runtime_instance_id);
            object.manager_owner = FieldString(fields, "ManagerOwner", object.manager_owner);
            object.context = FieldString(fields, "Context", event.context_id);
            const auto identity = StableIdentity(fields);
            if (!identity.empty()) object.canonical_identity = identity;
            const auto property = FieldString(fields, "Property");
            if (!property.empty()) object.properties[property] = FieldString(fields, "NormalizedValue",
                FieldString(fields, "RawValue", FieldString(fields, "Value")));
        }
    }
    for (auto& [token, object] : by_token) {
        object.authority = authority_by_token[token].Resolve();
        if (object.canonical_identity.empty()) {
            object.authority = object.authority == UltimateAuthority::Rejected ?
                UltimateAuthority::Rejected : UltimateAuthority::Unknown;
            object.status = "EvidenceBlockedObjectIdentityUnavailable";
            result->missing_evidence.push_back({
                "ObjectClassCandidate", object.object_token,
                "Stable template/runtime identity plus causal consumer",
                "Passive exact-build resolver/registry correlation", 0U,
                UltimateAuthority::Unknown});
        } else {
            object.status = object.authority == UltimateAuthority::Verified ?
                "VerifiedStableIdentity" : "EvidenceCandidateStableIdentity";
        }
        result->objects.push_back(std::move(object));
    }
}

void RecoverRegistries(const std::vector<God2SemanticEventV2>& events,
                       UltimateRecoveryResult* result) {
    std::map<std::string, UltimateRegistryRecord> registries;
    std::map<std::string, AuthorityAccumulator> authorities;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::RegistryLocated &&
            event.event_type != UltimateSemanticEventType::RegistryEnumerated) continue;
        const auto fields = PayloadFields(event);
        const auto identity = FieldString(fields, "RegistryName",
            FieldString(fields, "CanonicalIdentity"));
        if (identity.empty()) {
            result->missing_evidence.push_back({"Registry", event.event_id,
                "Registry identity and read-only lookup consumer",
                "Passive manager lookup correlation", 0U, UltimateAuthority::Unknown});
            continue;
        }
        auto& registry = registries[identity];
        registry.canonical_identity = identity;
        registry.family = FieldString(fields, "EntityFamily", registry.family);
        registry.record_count = std::max(registry.record_count,
            FieldUint64(fields, "RecordCount"));
        registry.read_only_enumeration = registry.read_only_enumeration &&
            FieldBool(fields, "ReadOnly", true);
        registry.lookup_consumer = FieldString(fields, "LookupConsumer", registry.lookup_consumer);
        registry.destination = FieldString(fields, "Destination", registry.destination);
        MergeEvidence(&registry.evidence, EvidenceFromEvent(event));
        authorities[identity].Add(event, fields);
    }
    for (auto& [identity, registry] : registries) {
        registry.authority = authorities[identity].Resolve(
            registry.read_only_enumeration ? 0U : 1U);
        result->registries.push_back(std::move(registry));
    }
}

bool IsSafeResourcePath(std::string_view value) {
    if (value.empty() || value.front() == '/' || value.front() == '\\' ||
        value.find(':') != std::string_view::npos) return false;
    fs::path path(Utf8ToWide(value));
    if (path.is_absolute() || path.has_root_name() || path.has_root_directory()) return false;
    for (const auto& component : path) {
        if (component == L".." || component == L".") return false;
    }
    return true;
}

void RecoverResources(const std::vector<God2SemanticEventV2>& events,
                      UltimateRecoveryResult* result) {
    std::map<std::string, UltimateResourceRecord> resources;
    std::map<std::string, AuthorityAccumulator> authorities;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::ResourceRead &&
            event.event_type != UltimateSemanticEventType::ResourceDecoded &&
            event.event_type != UltimateSemanticEventType::ResourceDeserialized) continue;
        const auto fields = PayloadFields(event);
        const auto path = FieldString(fields, "RelativePath");
        if (!IsSafeResourcePath(path)) {
            result->missing_evidence.push_back({"Resource", event.event_id,
                "Privacy-safe relative resource path", "Observe decoded resource boundary",
                0U, UltimateAuthority::Unknown});
            continue;
        }
        auto& resource = resources[path];
        resource.relative_path = path;
        resource.file_sha256 = FieldString(fields, "FileSha256", resource.file_sha256);
        resource.source_offset = FieldUint64(fields, "SourceOffset", resource.source_offset);
        resource.decoded_buffer_sha256 = FieldString(fields, "DecodedBufferSha256",
            resource.decoded_buffer_sha256);
        resource.decode_rva = FieldUint64(fields, "DecodeRVA", resource.decode_rva);
        resource.deserialize_rva = FieldUint64(fields, "DeserializeRVA", resource.deserialize_rva);
        resource.destination = FieldString(fields, "Destination", resource.destination);
        resource.record_count = std::max(resource.record_count,
            FieldUint64(fields, "RecordCount"));
        resource.schema_candidate = FieldString(fields, "SchemaCandidate", resource.schema_candidate);
        resource.provenance = FieldString(fields, "Provenance", resource.provenance);
        auto evidence = EvidenceFromEvent(event);
        evidence.resource_offset = resource.source_offset;
        MergeEvidence(&resource.evidence, evidence);
        authorities[path].Add(event, fields);
    }
    for (auto& [path, resource] : resources) {
        resource.authority = authorities[path].Resolve();
        result->resources.push_back(std::move(resource));
    }
}

void RecoverMutations(const std::vector<God2SemanticEventV2>& events,
                      UltimateRecoveryResult* result) {
    std::map<std::string, UltimateMutationRecord> mutations;
    std::map<std::string, AuthorityAccumulator> authorities;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::StateMutation) continue;
        const auto fields = PayloadFields(event);
        const auto object_identity = FieldString(fields, "ObjectIdentity");
        const auto property = FieldString(fields, "PropertyCandidate");
        const auto key = object_identity + '|' + property + '|' + event.context_id;
        auto& mutation = mutations[key];
        mutation.object_identity = object_identity;
        mutation.property_candidate = property;
        mutation.value_type = FieldString(fields, "Type", mutation.value_type);
        const auto before = FieldString(fields, "BeforeValue");
        const auto input = FieldString(fields, "InputValue");
        const auto after = FieldString(fields, "AfterValue");
        if (!authorities[key].observations.empty() &&
            (!mutation.before_value.empty() && mutation.before_value != before ||
             !mutation.input_value.empty() && mutation.input_value != input ||
             !mutation.after_value.empty() && mutation.after_value != after))
            ++mutation.contradictions;
        mutation.before_value = before;
        mutation.input_value = input;
        mutation.after_value = after;
        mutation.trigger_packet = FieldString(fields, "TriggerPacket", mutation.trigger_packet);
        mutation.trigger_handler = FieldString(fields, "TriggerHandler", mutation.trigger_handler);
        mutation.trigger_action = FieldString(fields, "TriggerAction",
            event.action_id.empty() ? mutation.trigger_action : event.action_id);
        mutation.writer_rva = FieldUint64(fields, "WriterRVA", event.rva);
        mutation.consumers = SplitEvidenceList(FieldString(fields, "Consumers"));
        mutation.contradictions += FieldUint64(fields, "Contradictions");
        authorities[key].Add(event, fields);
        MergeEvidence(&mutation.evidence, EvidenceFromEvent(event));
        const bool complete = !object_identity.empty() && !property.empty() &&
            !before.empty() && !input.empty() && !after.empty() &&
            (!mutation.trigger_packet.empty() || !mutation.trigger_handler.empty() ||
             !mutation.trigger_action.empty()) && mutation.writer_rva != 0U;
        if (!complete) {
            mutation.status = "EvidenceBlockedMutationCausalityUnavailable";
        } else {
            mutation.status = "EvidenceCandidateBeforeInputAfter";
        }
    }
    for (auto& [key, mutation] : mutations) {
        mutation.observation_count = authorities[key].DistinctObservations();
        if (mutation.status.find("EvidenceBlocked") == std::string::npos) {
            mutation.authority = authorities[key].Resolve(mutation.contradictions);
            mutation.status = mutation.authority == UltimateAuthority::Verified ?
                "VerifiedBeforeInputAfterCausality" :
                (mutation.authority == UltimateAuthority::Rejected ?
                    "RejectedByContradiction" : "EvidenceCandidateBeforeInputAfter");
        } else if (mutation.contradictions != 0U) {
            mutation.authority = UltimateAuthority::Rejected;
        }
        if (mutation.status.find("EvidenceBlocked") != std::string::npos) {
            result->missing_evidence.push_back({"StateMutation",
                mutation.object_identity + ':' + mutation.property_candidate,
                "Before/Input/After plus causal packet, handler or action",
                "Passive exact writer/consumer correlation", 0U, UltimateAuthority::Unknown});
        }
        result->mutations.push_back(std::move(mutation));
    }
}

void RecoverValueFlow(const std::vector<God2SemanticEventV2>& events,
                      UltimateRecoveryResult* result) {
    std::map<std::string, UltimateValueFlowEdge> edges;
    std::map<std::string, AuthorityAccumulator> authorities;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::TaintSeed &&
            event.event_type != UltimateSemanticEventType::TaintPropagation &&
            event.event_type != UltimateSemanticEventType::ValueFlow) continue;
        const auto fields = PayloadFields(event);
        UltimateValueFlowEdge candidate;
        candidate.edge_kind = ToString(event.event_type);
        candidate.source_token = event.source_token.empty() ?
            FieldString(fields, "SourceToken") : event.source_token;
        candidate.target_token = event.value_token.empty() ?
            FieldString(fields, "TargetToken", event.object_token) : event.value_token;
        candidate.operation = FieldString(fields, "Operation");
        candidate.propagation_hops = static_cast<std::uint32_t>(std::min<std::uint64_t>(
            kMaximumTaintHops + 1U, FieldUint64(fields, "PropagationHops")));
        const auto key = candidate.edge_kind + '|' + candidate.source_token + '|' +
            candidate.target_token + '|' + candidate.operation;
        auto& edge = edges[key];
        if (edge.edge_kind.empty()) edge = candidate;
        edge.propagation_hops = std::max(edge.propagation_hops, candidate.propagation_hops);
        MergeEvidence(&edge.evidence, EvidenceFromEvent(event));
        authorities[key].Add(event, fields);
    }
    for (auto& [key, edge] : edges) {
        if (edge.source_token.empty() || edge.target_token.empty())
            edge.authority = UltimateAuthority::Unknown;
        else if (edge.propagation_hops > kMaximumTaintHops)
            edge.authority = UltimateAuthority::Rejected;
        else
            edge.authority = authorities[key].Resolve();
        result->value_flow.push_back(std::move(edge));
    }
}

std::string ProtocolKey(std::string_view direction, std::string_view opcode) {
    return std::string(direction) + '|' + std::string(opcode);
}

void RecoverProtocol(const std::vector<God2SemanticEventV2>& events,
                     UltimateRecoveryResult* result) {
    std::map<std::string, UltimatePacketSchema> packets;
    std::map<std::string, AuthorityAccumulator> packet_authorities;
    std::map<std::string, AuthorityAccumulator> field_authorities;
    for (const auto& event : events) {
        const auto fields = PayloadFields(event);
        const auto direction = FieldString(fields, "Direction", "Unknown");
        const auto opcode = FieldString(fields, "Opcode", "Unknown");
        if (event.event_type == UltimateSemanticEventType::PacketBoundary ||
            event.event_type == UltimateSemanticEventType::ParserRead ||
            event.event_type == UltimateSemanticEventType::SerializerWrite ||
            event.event_type == UltimateSemanticEventType::HandlerInvocation ||
            event.event_type == UltimateSemanticEventType::HandlerArgument) {
            auto& packet = packets[ProtocolKey(direction, opcode)];
            packet.direction = direction;
            packet.opcode = opcode;
            packet.action_family = FieldString(fields, "ActionFamily", packet.action_family);
            const auto subsystem = FieldString(fields, "Subsystem", packet.subsystem);
            packet.subsystem = IsKnownSubsystem(subsystem) ? subsystem : "Unknown";
            packet.frame_length_rule = FieldString(fields, "FrameLengthRule",
                packet.frame_length_rule);
            packet.checksum_rule = FieldString(fields, "ChecksumRule", packet.checksum_rule);
            packet.encryption_boundary = FieldString(fields, "EncryptionBoundary",
                packet.encryption_boundary);
            const auto frame_length = FieldUint64(fields, "FrameLength");
            const auto minimum = FieldUint64(fields, "MinLength", frame_length);
            const auto maximum = FieldUint64(fields, "MaxLength", frame_length);
            if (packet_authorities[ProtocolKey(direction, opcode)].observations.empty()) {
                packet.minimum_length = minimum;
                packet.maximum_length = maximum;
            } else {
                if (minimum != 0U)
                    packet.minimum_length = packet.minimum_length == 0U ? minimum :
                        std::min(packet.minimum_length, minimum);
                packet.maximum_length = std::max(packet.maximum_length, maximum);
            }
            if (event.event_type == UltimateSemanticEventType::ParserRead)
                packet.parser_rva = event.rva;
            if (event.event_type == UltimateSemanticEventType::SerializerWrite)
                packet.serializer_rva = event.rva;
            if (event.event_type == UltimateSemanticEventType::HandlerInvocation ||
                event.event_type == UltimateSemanticEventType::HandlerArgument)
                packet.handler_rva = event.rva;
            packet.enum_mappings = SplitEvidenceList(FieldString(fields, "EnumMappings"));
            packet.conditions = SplitEvidenceList(FieldString(fields, "Conditions"));
            packet.request_response_links = SplitEvidenceList(
                FieldString(fields, "RequestResponseLinks"));
            packet.required_protocol_state = FieldString(fields, "RequiredProtocolState",
                packet.required_protocol_state);
            packet.state_mutation_links = SplitEvidenceList(
                FieldString(fields, "StateMutationLinks"));
            packet_authorities[ProtocolKey(direction, opcode)].Add(event, fields);
            packet.contradictions += FieldUint64(fields, "Contradictions");
            MergeEvidence(&packet.evidence, EvidenceFromEvent(event));
            if (event.event_type == UltimateSemanticEventType::ParserRead ||
                event.event_type == UltimateSemanticEventType::SerializerWrite) {
                UltimateProtocolField field;
                field.packet_offset = FieldUint64(fields, "PacketOffset",
                    FieldUint64(fields, "FrameOffset"));
                field.width = static_cast<std::uint32_t>(FieldUint64(fields, "Width"));
                field.signed_value = FieldBool(fields, "Signed");
                field.value_type = FieldString(fields, "ValueType",
                    event.event_type == UltimateSemanticEventType::ParserRead ?
                        FieldString(fields, "ReadType", "unknown") :
                        FieldString(fields, "WriteType", "unknown"));
                field.endian = FieldString(fields, "Endian", "unknown");
                field.encoding = FieldString(fields, "Encoding");
                field.length_relationship = FieldString(fields, "LengthRelationship");
                field.optional_or_conditional = FieldBool(fields, "OptionalOrConditional");
                field.repeated_or_list = FieldBool(fields, "RepeatedOrList");
                field.enum_candidate = FieldString(fields, "EnumCandidate");
                field.semantic_candidate = FieldString(fields, "SemanticCandidate",
                    FieldString(fields, "FieldSemantic"));
                field.parser_rva = event.event_type == UltimateSemanticEventType::ParserRead ?
                    event.rva : 0U;
                field.serializer_rva = event.event_type == UltimateSemanticEventType::SerializerWrite ?
                    event.rva : 0U;
                field.handler_context = FieldString(fields, "HandlerContext", event.context_id);
                field.object_endpoint = FieldString(fields, "ObjectEndpoint", event.object_token);
                field.observation_count = 1U;
                field.contradictions = FieldUint64(fields, "Contradictions");
                field.evidence.push_back(EvidenceFromEvent(event));
                const auto field_key = ProtocolKey(direction, opcode) + '|' +
                    std::to_string(field.packet_offset) + '|' + std::to_string(field.width) +
                    '|' + field.value_type + '|' + field.endian;
                field_authorities[field_key].Add(event, fields);
                const auto duplicate = std::find_if(packet.typed_fields.begin(),
                    packet.typed_fields.end(), [&](const auto& current) {
                        return current.packet_offset == field.packet_offset &&
                            current.width == field.width && current.value_type == field.value_type &&
                            current.endian == field.endian;
                    });
                if (duplicate == packet.typed_fields.end()) {
                    packet.typed_fields.push_back(std::move(field));
                } else {
                    ++duplicate->observation_count;
                    duplicate->contradictions += field.contradictions;
                    MergeEvidence(&duplicate->evidence, field.evidence.front());
                    duplicate->parser_rva = std::max(duplicate->parser_rva, field.parser_rva);
                    duplicate->serializer_rva = std::max(duplicate->serializer_rva,
                                                         field.serializer_rva);
                }
            }
        }
    }
    for (auto& [key, packet] : packets) {
        packet.observation_count = packet_authorities[key].DistinctObservations();
        packet.authority = packet_authorities[key].Resolve(packet.contradictions);
        for (auto& field : packet.typed_fields) {
            const auto field_key = key + '|' + std::to_string(field.packet_offset) + '|' +
                std::to_string(field.width) + '|' + field.value_type + '|' + field.endian;
            field.observation_count = field_authorities[field_key].DistinctObservations();
            field.authority = field_authorities[field_key].Resolve(field.contradictions);
        }
        std::vector<UltimateAuthority> packet_and_fields = {packet.authority};
        for (const auto& field : packet.typed_fields)
            packet_and_fields.push_back(field.authority);
        packet.authority = CombineAuthoritiesConservatively(packet_and_fields,
                                                            packet.contradictions);
        if (packet.opcode == "Unknown" || packet.subsystem == "Unknown") {
            result->missing_evidence.push_back({"UnknownOpcode", packet.opcode,
                "Typed parser/serializer, handler, object or mutation correlation",
                "Passive exact-build correlation and deterministic replay", 0U,
                UltimateAuthority::Unknown});
        }
        result->packet_schemas.push_back(std::move(packet));
    }
}

void ApplyLayeredValue(UltimateContentEntity* entity, const Fields& fields) {
    const auto property = FieldString(fields, "Property");
    if (property.empty()) return;
    const auto raw = FieldString(fields, "RawValue", FieldString(fields, "Value"));
    const auto normalized = FieldString(fields, "NormalizedValue", raw);
    entity->raw_values[property] = raw;
    entity->normalized_values[property] = normalized;
    const auto layer = FieldString(fields, "ValueLayer");
    if (layer == "Template" || layer == "Base") entity->base_values[property] = normalized;
    else if (layer == "Runtime" || layer == "Scaled") entity->runtime_values[property] = normalized;
    else if (layer == "Current" || layer == "Observed") entity->current_values[property] = normalized;
}

void RecoverContent(const std::vector<God2SemanticEventV2>& events,
                    const std::vector<UltimateObjectRecord>& objects,
                    UltimateRecoveryResult* result) {
    std::map<std::string, UltimateContentEntity> entities;
    std::map<std::string, std::vector<UltimateAuthority>> inherited_authorities;
    std::map<std::string, AuthorityAccumulator> event_authorities;
    for (const auto& object : objects) {
        if (object.canonical_identity.empty()) continue;
        auto& entity = entities[object.canonical_identity];
        entity.canonical_identity = object.canonical_identity;
        entity.family = object.family;
        entity.exact_source = "ObjectResolver";
        if (!object.evidence.empty()) entity.client_build_id = object.evidence.front().client_build_id;
        entity.normalized_values.insert(object.properties.begin(), object.properties.end());
        entity.evidence = object.evidence;
        inherited_authorities[object.canonical_identity].push_back(object.authority);
        entity.status = object.status;
    }
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::ObjectResolved &&
            event.event_type != UltimateSemanticEventType::ResourceDeserialized &&
            event.event_type != UltimateSemanticEventType::RegistryEnumerated &&
            event.event_type != UltimateSemanticEventType::UIAnchor) continue;
        const auto fields = PayloadFields(event);
        auto identity = StableIdentity(fields);
        if (identity.empty() && event.event_type ==
                UltimateSemanticEventType::UIAnchor) {
            identity = FieldString(fields, "AnchorId",
                event.event_id.empty() ? std::string{} :
                    "UIAnchor:" + event.event_id);
        }
        if (identity.empty()) continue;
        auto& entity = entities[identity];
        entity.canonical_identity = identity;
        entity.family = FieldString(fields, "EntityFamily",
            FieldString(fields, "Family",
                event.event_type == UltimateSemanticEventType::UIAnchor ?
                    "UIAnchor" : entity.family));
        entity.exact_source = FieldString(fields, "ExactSource", ToString(event.event_type));
        entity.client_build_id = event.client_build_id;
        ApplyLayeredValue(&entity, fields);
        MergeEvidence(&entity.evidence, EvidenceFromEvent(event));
        event_authorities[identity].Add(event, fields);
        entity.contradictions += FieldUint64(fields, "Contradictions");
        if (entity.family == "MonsterDrop" || entity.family == "DropRelation") {
            entity.normalized_values.erase("AuthoritativeRate");
            entity.normalized_values.erase("Enabled");
            entity.normalized_values.erase("ServerDefaultRate");
            entity.status = "ObservedDropRelationRateEvidenceBlockedServerOnly";
        }
    }
    for (auto& [identity, entity] : entities) {
        std::vector<UltimateAuthority> authorities = inherited_authorities[identity];
        if (!event_authorities[identity].observations.empty())
            authorities.push_back(event_authorities[identity].Resolve(entity.contradictions));
        entity.authority = CombineAuthoritiesConservatively(authorities,
                                                             entity.contradictions);
        std::set<std::pair<std::string, std::string>> observations;
        for (const auto& evidence : entity.evidence) {
            if (!evidence.session_id.empty() && !evidence.event_id.empty())
                observations.emplace(evidence.session_id, evidence.event_id);
        }
        entity.observation_count = static_cast<std::uint64_t>(observations.size());
        if (entity.family != "MonsterDrop" && entity.family != "DropRelation") {
            entity.status = entity.authority == UltimateAuthority::Verified ?
                "VerifiedCanonicalEntity" :
                (entity.authority == UltimateAuthority::Rejected ?
                    "RejectedByContradiction" : "EvidenceCandidateCanonicalEntity");
        } else if (entity.authority == UltimateAuthority::Verified ||
                   entity.authority == UltimateAuthority::Derived) {
            // The relation itself was observed, but the server-owned rate is
            // explicitly unknown and may not inherit VERIFIED/DERIVED.
            entity.authority = UltimateAuthority::Observed;
        }
        result->content_entities.push_back(std::move(entity));
    }
}

void RecoverFormulas(const std::vector<God2SemanticEventV2>& events,
                     UltimateRecoveryResult* result) {
    std::map<std::string, UltimateFormulaRecord> formulas;
    std::map<std::string, AuthorityAccumulator> authorities;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::FormulaOperand &&
            event.event_type != UltimateSemanticEventType::FormulaResult) continue;
        const auto fields = PayloadFields(event);
        const auto id = FieldString(fields, "FormulaId", event.context_id);
        if (id.empty()) continue;
        auto& formula = formulas[id];
        formula.formula_id = id;
        formula.category = FieldString(fields, "FormulaCategory", formula.category);
        formula.expression = FieldString(fields, "Expression", formula.expression);
        const auto operands = SplitEvidenceList(FieldString(fields, "Operands",
            FieldString(fields, "Operand")));
        if (!operands.empty()) formula.operands = operands;
        formula.result = FieldString(fields, "Result", formula.result);
        authorities[id].Add(event, fields);
        formula.contradictions += FieldUint64(fields, "Contradictions");
        formula.replay_consistent = formula.replay_consistent || FieldBool(fields, "ReplayConsistent");
        formula.exact_recovered = formula.exact_recovered || FieldBool(fields, "ExactRecoveredFormula");
        MergeEvidence(&formula.evidence, EvidenceFromEvent(event));
    }
    for (auto& [id, formula] : formulas) {
        formula.observation_count = authorities[id].DistinctObservations();
        const auto evidence_authority = authorities[id].Resolve(formula.contradictions);
        if (evidence_authority == UltimateAuthority::Rejected) {
            formula.authority = UltimateAuthority::Rejected;
            formula.status = "RejectedByCounterexample";
        } else if (formula.exact_recovered && formula.replay_consistent &&
                   evidence_authority == UltimateAuthority::Verified) {
            formula.authority = UltimateAuthority::Verified;
            formula.status = "ExactRecoveredFormula";
        } else if (!formula.expression.empty()) {
            formula.authority = UltimateAuthority::Hypothesis;
            formula.status = "CandidateModel";
        }
        if (formula.authority != UltimateAuthority::Verified)
            result->missing_evidence.push_back({"Formula", formula.formula_id,
                "Exact recovered expression plus replay/oracle counterexamples",
                "Offline deterministic replay; do not probe official server", 1U,
                UltimateAuthority::Unknown});
        result->formulas.push_back(std::move(formula));
    }
}

void RecoverStateMachines(const std::vector<God2SemanticEventV2>& events,
                          UltimateRecoveryResult* result) {
    std::map<std::string, UltimateFsmRecord> machines;
    std::map<std::string, std::map<std::string, std::size_t>> transition_indices;
    std::map<std::string, AuthorityAccumulator> transition_authorities;
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::StateMutation &&
            event.event_type != UltimateSemanticEventType::PacketBoundary &&
            event.event_type != UltimateSemanticEventType::HandlerInvocation) continue;
        const auto fields = PayloadFields(event);
        const auto machine_id = FieldString(fields, "MachineId");
        const auto from = FieldString(fields, "PreviousState");
        const auto to = FieldString(fields, "NextState");
        if (machine_id.empty() || from.empty() || to.empty()) continue;
        auto& machine = machines[machine_id];
        machine.machine_id = machine_id;
        if (std::find(machine.states.begin(), machine.states.end(), from) == machine.states.end())
            machine.states.push_back(from);
        if (std::find(machine.states.begin(), machine.states.end(), to) == machine.states.end())
            machine.states.push_back(to);
        const auto key = from + "->" + to + '|' + FieldString(fields, "Guard");
        const auto authority_key = machine_id + '|' + key;
        transition_authorities[authority_key].Add(event, fields);
        auto found = transition_indices[machine_id].find(key);
        if (found == transition_indices[machine_id].end()) {
            UltimateFsmTransition transition;
            transition.from_state = from;
            transition.to_state = to;
            transition.guard = FieldString(fields, "Guard");
            transition.input = FieldString(fields, "Input");
            transition.output = FieldString(fields, "Output");
            transition.mutation = FieldString(fields, "Mutation");
            transition.observation_count = 1U;
            transition.replay_consistent = FieldBool(fields, "ReplayConsistent");
            transition.evidence.push_back(EvidenceFromEvent(event));
            transition_indices[machine_id][key] = machine.transitions.size();
            machine.transitions.push_back(std::move(transition));
        } else {
            auto& transition = machine.transitions[found->second];
            ++transition.observation_count;
            transition.replay_consistent = transition.replay_consistent ||
                FieldBool(fields, "ReplayConsistent");
            MergeEvidence(&transition.evidence, EvidenceFromEvent(event));
        }
        const auto invalid = FieldString(fields, "InvalidTransition");
        if (!invalid.empty()) machine.invalid_transitions.push_back(invalid);
        machine.recovery_or_rollback = FieldString(fields, "RecoveryOrRollback",
            machine.recovery_or_rollback);
    }
    for (auto& [id, machine] : machines) {
        std::vector<UltimateAuthority> machine_authorities;
        for (auto& [key, index] : transition_indices[id]) {
            auto& transition = machine.transitions[index];
            const auto authority_key = id + '|' + key;
            transition.observation_count =
                transition_authorities[authority_key].DistinctObservations();
            transition.authority = transition_authorities[authority_key].Resolve();
            machine_authorities.push_back(transition.authority);
        }
        machine.authority = CombineAuthoritiesConservatively(machine_authorities);
        result->state_machines.push_back(std::move(machine));
    }
}

void AddGraphNode(std::map<std::string, UltimateGraphNode>* nodes,
                  std::string id, std::string kind, UltimateAuthority authority) {
    if (id.empty()) return;
    const auto found = nodes->find(id);
    if (found == nodes->end()) {
        UltimateGraphNode node;
        node.id = id;
        node.kind = std::move(kind);
        node.authority = authority;
        nodes->emplace(std::move(id), std::move(node));
        return;
    }
    found->second.authority = CombineAuthoritiesConservatively(
        {found->second.authority, authority});
}

UltimateGraphEdge EvidenceGraphEdge(std::string from, std::string to,
                                    std::string relation,
                                    const std::vector<UltimateEvidenceRef>& evidence,
                                    UltimateAuthority authority,
                                    std::uint64_t observations = 1U,
                                    std::uint64_t contradictions = 0U) {
    UltimateGraphEdge edge;
    edge.from = std::move(from);
    edge.to = std::move(to);
    edge.relation = std::move(relation);
    edge.evidence = evidence;
    edge.client_build_id = evidence.empty() ? "" : evidence.front().client_build_id;
    edge.rva_or_resource_offset = evidence.empty() ? 0U : evidence.front().rva;
    edge.observation_count = observations;
    edge.contradictions = contradictions;
    edge.authority = authority;
    edge.status = evidence.empty() ? "EvidenceBlockedRelationEvidenceUnavailable" :
        ToString(authority);
    return edge;
}

void RecoverGraph(UltimateRecoveryResult* result) {
    std::map<std::string, UltimateGraphNode> nodes;
    for (const auto& packet : result->packet_schemas) {
        const auto packet_id = "Packet:" + packet.direction + ':' + packet.opcode;
        const auto opcode_id = "Opcode:" + packet.opcode;
        AddGraphNode(&nodes, packet_id, "Packet", packet.authority);
        AddGraphNode(&nodes, opcode_id, "Opcode", packet.authority);
        result->graph_edges.push_back(EvidenceGraphEdge(packet_id, opcode_id, "hasOpcode",
            packet.evidence, packet.authority, packet.observation_count, packet.contradictions));
        struct ConsumerNode {
            std::uint64_t rva;
            const char* relation;
            const char* kind;
        };
        const std::array<ConsumerNode, 3> consumers = {{
            {packet.parser_rva, "parsedBy", "Parser"},
            {packet.serializer_rva, "serializedBy", "Serializer"},
            {packet.handler_rva, "handledBy", "Handler"}
        }};
        for (const auto& consumer : consumers) {
            const auto rva = consumer.rva;
            if (rva == 0U) continue;
            const auto function_id = "FunctionRVA:" + std::to_string(rva);
            AddGraphNode(&nodes, function_id, consumer.kind, packet.authority);
            auto edge = EvidenceGraphEdge(packet_id, function_id, consumer.relation, packet.evidence,
                packet.authority, packet.observation_count, packet.contradictions);
            edge.rva_or_resource_offset = rva;
            result->graph_edges.push_back(std::move(edge));
        }
        for (const auto& field : packet.typed_fields) {
            const auto field_id = packet_id + ":Field:" + std::to_string(field.packet_offset);
            AddGraphNode(&nodes, field_id, "Field", field.authority);
            UltimateGraphEdge edge;
            edge.from = packet_id;
            edge.to = field_id;
            edge.relation = "contains";
            edge.evidence = field.evidence;
            edge.client_build_id = field.evidence.empty() ? "" :
                field.evidence.front().client_build_id;
            edge.rva_or_resource_offset = field.parser_rva != 0U ? field.parser_rva :
                field.serializer_rva;
            edge.observation_count = field.observation_count;
            edge.contradictions = field.contradictions;
            edge.authority = field.authority;
            edge.status = ToString(field.authority);
            result->graph_edges.push_back(std::move(edge));
            if (field.parser_rva != 0U) {
                result->graph_edges.push_back(EvidenceGraphEdge(
                    "FunctionRVA:" + std::to_string(field.parser_rva), field_id,
                    "reads", field.evidence, field.authority, field.observation_count,
                    field.contradictions));
            }
            if (field.serializer_rva != 0U) {
                result->graph_edges.push_back(EvidenceGraphEdge(
                    "FunctionRVA:" + std::to_string(field.serializer_rva), field_id,
                    "writes", field.evidence, field.authority, field.observation_count,
                    field.contradictions));
            }
        }
    }
    for (const auto& object : result->objects) {
        const auto token_id = "ObjectToken:" + object.object_token;
        const auto identity_id = object.canonical_identity.empty() ? token_id :
            object.canonical_identity;
        AddGraphNode(&nodes, object.canonical_identity.empty() ?
            token_id : object.canonical_identity,
            "Object", object.authority);
        if (!object.canonical_identity.empty() && !object.object_token.empty()) {
            AddGraphNode(&nodes, token_id, "ObjectToken", object.authority);
            result->graph_edges.push_back(EvidenceGraphEdge(token_id, identity_id,
                "resolvesTo", object.evidence, object.authority,
                static_cast<std::uint64_t>(object.evidence.size())));
        }
        if (!object.family.empty()) {
            const auto class_id = "Class:" + object.family;
            AddGraphNode(&nodes, class_id, "Class", object.authority);
            result->graph_edges.push_back(EvidenceGraphEdge(identity_id, class_id,
                "instanceOf", object.evidence, object.authority,
                static_cast<std::uint64_t>(object.evidence.size())));
        }
        for (const auto& [property, value] : object.properties) {
            const auto property_id = identity_id + ":Property:" + property;
            AddGraphNode(&nodes, property_id, "Property", object.authority);
            nodes[property_id].attributes["value"] = value;
            result->graph_edges.push_back(EvidenceGraphEdge(identity_id, property_id,
                "hasProperty", object.evidence, object.authority,
                static_cast<std::uint64_t>(object.evidence.size())));
        }
    }
    for (const auto& registry : result->registries) {
        AddGraphNode(&nodes, registry.canonical_identity, "Registry", registry.authority);
        if (!registry.lookup_consumer.empty()) {
            const auto consumer_id = "Consumer:" + registry.lookup_consumer;
            AddGraphNode(&nodes, consumer_id, "RegistryConsumer", registry.authority);
            result->graph_edges.push_back(EvidenceGraphEdge(registry.canonical_identity,
                consumer_id, "resolvedBy", registry.evidence, registry.authority,
                static_cast<std::uint64_t>(registry.evidence.size())));
        }
    }
    for (const auto& resource : result->resources) {
        AddGraphNode(&nodes, "Resource:" + resource.relative_path, "Resource", resource.authority);
        if (!resource.destination.empty()) {
            AddGraphNode(&nodes, resource.destination, "DecodeDestination", resource.authority);
            auto edge = EvidenceGraphEdge("Resource:" + resource.relative_path,
                resource.destination, "decodedInto", resource.evidence, resource.authority,
                static_cast<std::uint64_t>(resource.evidence.size()));
            edge.rva_or_resource_offset = resource.source_offset;
            result->graph_edges.push_back(std::move(edge));
        }
    }
    for (const auto& mutation : result->mutations) {
        const auto mutation_id = "Mutation:" + mutation.object_identity + ':' +
            mutation.property_candidate;
        AddGraphNode(&nodes, mutation_id, "Mutation", mutation.authority);
        AddGraphNode(&nodes, mutation.object_identity, "Object", mutation.authority);
        UltimateGraphEdge edge;
        edge.from = mutation_id;
        edge.to = mutation.object_identity;
        edge.relation = "mutates";
        edge.evidence = mutation.evidence;
        edge.client_build_id = mutation.evidence.empty() ? "" :
            mutation.evidence.front().client_build_id;
        edge.rva_or_resource_offset = mutation.writer_rva;
        edge.observation_count = mutation.observation_count;
        edge.contradictions = mutation.contradictions;
        edge.authority = mutation.authority;
        edge.status = mutation.status;
        result->graph_edges.push_back(std::move(edge));
    }
    for (const auto& value_flow : result->value_flow) {
        AddGraphNode(&nodes, value_flow.source_token, "Value", value_flow.authority);
        AddGraphNode(&nodes, value_flow.target_token, "Value", value_flow.authority);
        UltimateGraphEdge edge;
        edge.from = value_flow.source_token;
        edge.to = value_flow.target_token;
        edge.relation = "flowsTo";
        edge.evidence = value_flow.evidence;
        edge.client_build_id = value_flow.evidence.empty() ? "" :
            value_flow.evidence.front().client_build_id;
        edge.observation_count = 1U;
        edge.authority = value_flow.authority;
        edge.status = ToString(value_flow.authority);
        result->graph_edges.push_back(std::move(edge));
    }
    for (const auto& entity : result->content_entities)
        AddGraphNode(&nodes, entity.canonical_identity, entity.family, entity.authority);
    for (const auto& entity : result->content_entities) {
        if (!EqualAsciiInsensitive(entity.family, "Portal")) continue;
        const auto source = entity.normalized_values.find("SourceMap");
        const auto target = entity.normalized_values.find("TargetMap");
        if (source == entity.normalized_values.end() ||
            target == entity.normalized_values.end()) continue;
        AddGraphNode(&nodes, "Map:" + source->second, "Map", entity.authority);
        AddGraphNode(&nodes, "Map:" + target->second, "Map", entity.authority);
        UltimateGraphEdge edge;
        edge.from = "Map:" + source->second;
        edge.to = "Map:" + target->second;
        edge.relation = "portalsTo";
        edge.evidence = entity.evidence;
        edge.client_build_id = entity.client_build_id;
        edge.observation_count = entity.observation_count;
        edge.contradictions = entity.contradictions;
        edge.authority = entity.authority;
        edge.status = entity.status;
        result->graph_edges.push_back(std::move(edge));
    }
    for (const auto& formula : result->formulas) {
        AddGraphNode(&nodes, "Formula:" + formula.formula_id, "Formula", formula.authority);
        for (const auto& operand : formula.operands) {
            const auto operand_id = "Value:" + operand;
            AddGraphNode(&nodes, operand_id, "FormulaOperand", formula.authority);
            result->graph_edges.push_back(EvidenceGraphEdge("Formula:" + formula.formula_id,
                operand_id, "consumes", formula.evidence, formula.authority,
                formula.observation_count, formula.contradictions));
        }
    }
    for (const auto& machine : result->state_machines) {
        for (const auto& state : machine.states)
            AddGraphNode(&nodes, machine.machine_id + ":State:" + state, "State",
                         machine.authority);
        for (const auto& transition : machine.transitions) {
            const auto from_id = machine.machine_id + ":State:" + transition.from_state;
            const auto to_id = machine.machine_id + ":State:" + transition.to_state;
            const auto transition_id = machine.machine_id + ":Transition:" +
                transition.from_state + "->" + transition.to_state + ':' + transition.guard;
            AddGraphNode(&nodes, transition_id, "Transition", transition.authority);
            auto enter = EvidenceGraphEdge(from_id, transition_id, "entersTransition",
                transition.evidence, transition.authority, transition.observation_count);
            auto leave = EvidenceGraphEdge(transition_id, to_id, "leadsTo",
                transition.evidence, transition.authority, transition.observation_count);
            enter.status = leave.status = transition.replay_consistent ?
                "ReplayConsistent" : "EvidenceBlockedReplayUnavailable";
            result->graph_edges.push_back(std::move(enter));
            result->graph_edges.push_back(std::move(leave));
        }
    }
    constexpr std::array<std::string_view, 9> unavailable_relation_kinds = {
        "spawns", "drops", "startsQuest", "completesQuest", "requires", "rewards",
        "usesSkill", "belongsToProfile", "handledMutationLink"
    };
    for (const auto relation : unavailable_relation_kinds) {
        result->missing_evidence.push_back({"GraphRelation", std::string(relation),
            "Direct typed semantic evidence for this relation",
            "Collect passive exact-build correlated events; do not infer an edge", 3U,
            UltimateAuthority::Unknown});
    }
    for (auto& [id, node] : nodes) {
        (void)id;
        result->graph_nodes.push_back(std::move(node));
    }
}

struct AuthorityHistogram {
    std::uint64_t verified = 0;
    std::uint64_t derived = 0;
    std::uint64_t observed = 0;
    std::uint64_t hypothesis = 0;
    std::uint64_t unknown = 0;
    std::uint64_t unknown_server_only = 0;
    std::uint64_t rejected = 0;

    void Add(UltimateAuthority authority) noexcept {
        switch (authority) {
        case UltimateAuthority::Verified: ++verified; break;
        case UltimateAuthority::Derived: ++derived; break;
        case UltimateAuthority::Observed: ++observed; break;
        case UltimateAuthority::Hypothesis: ++hypothesis; break;
        case UltimateAuthority::Unknown: ++unknown; break;
        case UltimateAuthority::UnknownServerOnly: ++unknown_server_only; break;
        case UltimateAuthority::Rejected: ++rejected; break;
        }
    }

    std::uint64_t Total() const noexcept {
        return verified + derived + observed + hypothesis + unknown +
            unknown_server_only + rejected;
    }

    std::uint64_t Promotable() const noexcept { return verified + derived; }
};

AuthorityHistogram ContentAuthorityHistogram(const UltimateRecoveryResult& result,
                                             std::string_view family) {
    AuthorityHistogram histogram;
    for (const auto& entity : result.content_entities) {
        if (EqualAsciiInsensitive(entity.family, family)) histogram.Add(entity.authority);
    }
    return histogram;
}

void RecoverCoverage(const UltimateRecoveryInput& input, UltimateRecoveryResult* result) {
    AuthorityHistogram protocol;
    for (const auto& packet : result->packet_schemas) protocol.Add(packet.authority);
    AuthorityHistogram drop = ContentAuthorityHistogram(*result, "MonsterDrop");
    const auto drop_relation = ContentAuthorityHistogram(*result, "DropRelation");
    drop.verified += drop_relation.verified;
    drop.derived += drop_relation.derived;
    drop.observed += drop_relation.observed;
    drop.hypothesis += drop_relation.hypothesis;
    drop.unknown += drop_relation.unknown;
    drop.unknown_server_only += drop_relation.unknown_server_only;
    drop.rejected += drop_relation.rejected;
    AuthorityHistogram formulas;
    for (const auto& formula : result->formulas) formulas.Add(formula.authority);
    AuthorityHistogram machines;
    for (const auto& machine : result->state_machines) machines.Add(machine.authority);
    AuthorityHistogram objects;
    for (const auto& object : result->objects) {
        if (!object.canonical_identity.empty()) objects.Add(object.authority);
    }
    AuthorityHistogram mutations;
    for (const auto& mutation : result->mutations) mutations.Add(mutation.authority);
    const std::vector<std::pair<std::string, AuthorityHistogram>> recovered = {
        {"Protocol", protocol},
        {"Monster", ContentAuthorityHistogram(*result, "Monster")},
        {"NPC", ContentAuthorityHistogram(*result, "NPC")},
        {"Map", ContentAuthorityHistogram(*result, "Map")},
        {"Portal", ContentAuthorityHistogram(*result, "Portal")},
        {"Quest", ContentAuthorityHistogram(*result, "Quest")},
        {"Item", ContentAuthorityHistogram(*result, "Item")},
        {"Skill", ContentAuthorityHistogram(*result, "Skill")},
        {"DropRelation", drop},
        {"ExactRate", AuthorityHistogram{}},
        {"Formula", formulas},
        {"StateMachine", machines},
        {"ObjectResolver", objects},
        {"Mutation", mutations}
    };
    for (const auto& [domain, histogram] : recovered) {
        UltimateCoverageMetric metric;
        metric.domain = domain;
        metric.total_recovered = histogram.Total();
        metric.recovered = histogram.Promotable();
        metric.verified_recovered = histogram.verified;
        metric.derived_recovered = histogram.derived;
        metric.observed_recovered = histogram.observed;
        metric.hypothesis_recovered = histogram.hypothesis;
        metric.unknown_recovered = histogram.unknown;
        metric.unknown_server_only_recovered = histogram.unknown_server_only;
        metric.rejected_recovered = histogram.rejected;
        const auto denominator = input.declared_denominators.find(domain);
        const auto denominator_authority = input.declared_denominator_authorities.find(domain);
        if (denominator != input.declared_denominators.end() &&
            denominator_authority != input.declared_denominator_authorities.end() &&
            denominator_authority->second == UltimateAuthority::Verified) {
            metric.denominator = denominator->second;
            metric.denominator_authority = UltimateAuthority::Verified;
            metric.denominator_basis = "VerifiedProvenanceCatalogDenominator";
            if (denominator->second == 0U) {
                metric.percent.reset();
                metric.status = metric.recovered == 0U ?
                    "NotApplicableZeroOverZero" : "ContradictionRecoveredExceedsZeroDenominator";
            } else {
                metric.percent = 100.0 * static_cast<double>(metric.recovered) /
                    static_cast<double>(denominator->second);
                metric.status = metric.recovered > denominator->second ?
                    "ContradictionRecoveredExceedsDenominator" :
                    "MeasuredPromotableWithVerifiedDenominator";
            }
        } else {
            metric.denominator_basis = denominator == input.declared_denominators.end() ?
                "Unavailable" : "UnverifiedProvenance";
            metric.denominator_authority = denominator_authority ==
                input.declared_denominator_authorities.end() ? UltimateAuthority::Unknown :
                denominator_authority->second;
            metric.status = denominator == input.declared_denominators.end() ?
                "EvidenceBlockedDenominatorUnavailable" :
                "EvidenceBlockedDenominatorProvenanceUnverified";
            result->missing_evidence.push_back({"CoverageDenominator", domain,
                "Explicit VERIFIED-provenance catalog denominator",
                "Read-only registry/resource catalog enumeration", 2U,
                UltimateAuthority::Unknown});
        }
        result->coverage.push_back(std::move(metric));
    }
}

template <typename Operation>
void RunRecoveryComponent(std::string_view name, UltimateRecoveryResult* result,
                          Operation&& operation) noexcept {
    try {
        operation();
    } catch (...) {
        result->partial_recovery = true;
        result->semantic_evidence_incomplete = true;
        result->diagnostics.push_back(std::string(name) + ":ComponentFailureIsolated");
    }
}

bool IsPromotable(UltimateAuthority authority) noexcept {
    return authority == UltimateAuthority::Verified || authority == UltimateAuthority::Derived;
}

void DemotePromotable(UltimateAuthority* authority) noexcept {
    if (IsPromotable(*authority)) *authority = UltimateAuthority::Unknown;
}

void RecoverNonPromotableLedgers(
    const std::vector<God2SemanticEventV2>& events,
    UltimateRecoveryResult* result) {
    for (const auto& event : events) {
        const auto decision = DispatchSemanticEvent(event);
        if (!decision.dispatchable) {
            result->semantic_evidence_incomplete = true;
            result->diagnostics.push_back("Dispatch:UnrecognizedEventType");
            continue;
        }
        if (decision.sink == UltimateSemanticDispatchSink::CandidateLedger) {
            const auto fields = PayloadFields(event);
            result->missing_evidence.push_back({
                "FunctionRoleCandidate",
                FieldString(fields, "Domain", event.source_token),
                "All 10 promotion gates plus 4 ABI-safety gates",
                "Passive exact-build typed causal observations; candidate is never an active hook",
                14U, UltimateAuthority::Unknown});
            result->diagnostics.push_back("CandidateLedger:" + event.event_id +
                ":NoPromotion");
        } else if (decision.sink ==
                   UltimateSemanticDispatchSink::DiagnosticLedger) {
            result->diagnostics.push_back("DiagnosticLedger:" + event.event_id +
                ":NoPromotion");
        }
    }
}

void RecoverSnapshotEdges(const std::vector<God2SemanticEventV2>& events,
                          UltimateRecoveryResult* result) {
    for (const auto& event : events) {
        if (event.event_type != UltimateSemanticEventType::SnapshotEdge) continue;
        const auto fields = PayloadFields(event);
        const auto from = FieldString(fields, "FromToken",
            FieldString(fields, "SourceToken", event.source_token));
        const auto to = FieldString(fields, "ToToken",
            FieldString(fields, "TargetToken",
                !event.object_token.empty() ? event.object_token :
                    event.value_token));
        if (from.empty() || to.empty()) {
            result->missing_evidence.push_back({"SnapshotEdge", event.event_id,
                "Typed FromToken and ToToken", "Exact-build snapshot producer",
                1U, UltimateAuthority::Unknown});
            continue;
        }
        const auto evidence = EvidenceFromEvent(event);
        UltimateGraphNode from_node;
        from_node.id = from;
        from_node.kind = "SnapshotObject";
        from_node.authority = evidence.authority;
        UltimateGraphNode to_node;
        to_node.id = to;
        to_node.kind = "SnapshotObject";
        to_node.authority = evidence.authority;
        result->graph_nodes.push_back(std::move(from_node));
        result->graph_nodes.push_back(std::move(to_node));
        result->graph_edges.push_back(EvidenceGraphEdge(from, to,
            FieldString(fields, "Relation", "snapshotEdge"), {evidence},
            evidence.authority));
    }
}

void DemotePromotableRecovery(UltimateRecoveryResult* result) {
    for (auto& object : result->objects) DemotePromotable(&object.authority);
    for (auto& registry : result->registries) DemotePromotable(&registry.authority);
    for (auto& resource : result->resources) DemotePromotable(&resource.authority);
    for (auto& mutation : result->mutations) DemotePromotable(&mutation.authority);
    for (auto& edge : result->value_flow) DemotePromotable(&edge.authority);
    for (auto& packet : result->packet_schemas) {
        DemotePromotable(&packet.authority);
        for (auto& field : packet.typed_fields) DemotePromotable(&field.authority);
    }
    for (auto& entity : result->content_entities) DemotePromotable(&entity.authority);
    for (auto& formula : result->formulas) DemotePromotable(&formula.authority);
    for (auto& machine : result->state_machines) {
        DemotePromotable(&machine.authority);
        for (auto& transition : machine.transitions) DemotePromotable(&transition.authority);
    }
}

} // namespace

UltimateRecoveryResult UltimateRecoveryEngine::Recover(const UltimateRecoveryInput& input) noexcept {
    UltimateRecoveryResult result;
    result.session_id = input.session_id;
    result.semantic_evidence_incomplete = input.semantic_evidence_incomplete;
    result.partial_recovery = input.interrupted;
    if (input.semantic_events.empty()) {
        result.status = "EvidenceBlockedNoSemanticEvidence";
        result.ml = UltimateDeterministicMl::Analyze(input.ml_samples, input.ml_sequences,
                                                    input.local_model);
        RecoverCoverage(input, &result);
        result.missing_evidence.push_back({"SemanticEvent", input.session_id,
            "Exact-build typed semantic observations", "Passive authorized Client session",
            0U, UltimateAuthority::Unknown});
        return result;
    }
    std::set<std::string> builds;
    std::set<std::string> sessions;
    std::set<std::uint32_t> processes;
    bool identity_fields_complete = true;
    for (const auto& event : input.semantic_events) {
        if (event.client_build_id.empty() || event.session_id.empty() ||
            event.process_id == 0U || !EqualAsciiInsensitive(event.module_id,
                kExpectedClientName)) identity_fields_complete = false;
        if (!event.client_build_id.empty()) builds.insert(LowerAscii(event.client_build_id));
        if (!event.session_id.empty()) sessions.insert(event.session_id);
        if (event.process_id != 0U) processes.insert(event.process_id);
    }
    if (builds.size() == 1U) result.client_build_id = *builds.begin();
    const bool exact_build = builds.size() == 1U &&
        EqualAsciiInsensitive(*builds.begin(), kExpectedClientSha256);
    const bool session_process_consistent = sessions.size() == 1U && processes.size() == 1U &&
        !input.session_id.empty() && *sessions.begin() == input.session_id;
    result.exact_client_build_attested = identity_fields_complete && exact_build &&
        session_process_consistent;
    if (result.exact_client_build_attested) {
        result.client_build_attestation_status =
            "ExactRecoveryEventBuildSessionProcessAttested";
    } else if (!identity_fields_complete) {
        result.client_build_attestation_status =
            "EvidenceBlockedRecoveryBuildAttestationIncomplete";
    } else if (!exact_build) {
        result.client_build_attestation_status =
            "EvidenceBlockedRecoveryBuildMismatch";
    } else {
        result.client_build_attestation_status =
            "EvidenceBlockedRecoverySessionProcessMismatch";
    }
    if (!result.exact_client_build_attested) {
        result.semantic_evidence_incomplete = true;
        result.diagnostics.push_back("ClientBuildIdentity:" +
            result.client_build_attestation_status);
    }
    std::vector<God2SemanticEventV2> ordered = input.semantic_events;
    bool sensitive_payload_suppressed = false;
    for (auto& event : ordered) {
        sensitive_payload_suppressed =
            SensitiveSemanticPayloadMustBeSuppressed(event) || sensitive_payload_suppressed;
        SuppressSensitiveSemanticPayload(&event);
    }
    if (sensitive_payload_suppressed) {
        result.semantic_evidence_incomplete = true;
        result.diagnostics.push_back(
            "SensitiveSemanticPayload:MetadataOnlyOriginalValuesSuppressed");
    }
    std::stable_sort(ordered.begin(), ordered.end(), [](const auto& left, const auto& right) {
        return left.sequence < right.sequence;
    });
    RunRecoveryComponent("SemanticDispatchLedgers", &result, [&] {
        RecoverNonPromotableLedgers(ordered, &result);
    });
    RunRecoveryComponent("ObjectResolver", &result, [&] { RecoverObjects(ordered, &result); });
    RunRecoveryComponent("RegistryRecovery", &result, [&] { RecoverRegistries(ordered, &result); });
    RunRecoveryComponent("ResourceRecovery", &result, [&] { RecoverResources(ordered, &result); });
    RunRecoveryComponent("MutationRecovery", &result, [&] { RecoverMutations(ordered, &result); });
    RunRecoveryComponent("TaintValueFlow", &result, [&] { RecoverValueFlow(ordered, &result); });
    RunRecoveryComponent("ProtocolRecovery", &result, [&] { RecoverProtocol(ordered, &result); });
    RunRecoveryComponent("ContentRecovery", &result, [&] {
        RecoverContent(ordered, result.objects, &result);
    });
    RunRecoveryComponent("FormulaRecovery", &result, [&] { RecoverFormulas(ordered, &result); });
    RunRecoveryComponent("StateMachineRecovery", &result, [&] {
        RecoverStateMachines(ordered, &result);
    });
    if (!result.exact_client_build_attested) DemotePromotableRecovery(&result);
    RunRecoveryComponent("EvidenceGraph", &result, [&] { RecoverGraph(&result); });
    RunRecoveryComponent("SnapshotRecovery", &result, [&] {
        RecoverSnapshotEdges(ordered, &result);
    });
    RunRecoveryComponent("Coverage", &result, [&] { RecoverCoverage(input, &result); });
    RunRecoveryComponent("DeterministicMl", &result, [&] {
        result.ml = UltimateDeterministicMl::Analyze(input.ml_samples, input.ml_sequences,
                                                    input.local_model);
    });
    if (result.partial_recovery || result.semantic_evidence_incomplete)
        result.status = "PartialRecoverableEvidenceIncomplete";
    else
        result.status = "RecoveryCompletedWithEvidenceAuthoritiesPreserved";
    return result;
}

namespace {

std::string JsonStringMap(const std::map<std::string, std::string>& values) {
    std::ostringstream output;
    output << '{';
    bool first = true;
    for (const auto& [key, value] : values) {
        if (!first) output << ',';
        first = false;
        output << JsonQuoted(key) << ':' << JsonQuoted(value);
    }
    output << '}';
    return output.str();
}

std::string EvidenceRefsJson(const std::vector<UltimateEvidenceRef>& evidence) {
    std::ostringstream output;
    output << '[';
    for (std::size_t index = 0; index < evidence.size(); ++index) {
        if (index != 0U) output << ',';
        const auto& item = evidence[index];
        output << "{\"evidenceId\":" << JsonQuoted(item.evidence_id)
               << ",\"eventId\":" << JsonQuoted(item.event_id)
               << ",\"clientBuildId\":" << JsonQuoted(item.client_build_id)
               << ",\"moduleId\":" << JsonQuoted(item.module_id)
               << ",\"rva\":" << item.rva
               << ",\"rvaExpression\":" << JsonQuoted(item.rva_expression)
               << ",\"callsiteRvaExpression\":" <<
                    JsonQuoted(item.callsite_rva_expression)
               << ",\"resourceOffset\":" << item.resource_offset
               << ",\"validationStatus\":\"ValidatedOnlyAtParentRecord\""
               << ",\"claimedAuthorityHint\":" <<
                    JsonQuoted(ToString(item.claimed_authority_hint)) << '}';
    }
    output << ']';
    return output.str();
}

std::string ObjectJson(const UltimateObjectRecord& object) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Object\""
           << ",\"canonicalIdentity\":" << JsonQuoted(object.canonical_identity)
           << ",\"family\":" << JsonQuoted(object.family)
           << ",\"stableTemplateId\":" << JsonQuoted(object.stable_template_id)
           << ",\"runtimeInstanceId\":" << JsonQuoted(object.runtime_instance_id)
           << ",\"objectToken\":" << JsonQuoted(object.object_token)
           << ",\"allocationSize\":" << object.allocation_size
           << ",\"vtable\":" << JsonQuoted(object.vtable)
           << ",\"constructorPath\":" << JsonQuoted(object.constructor_path)
           << ",\"destructorPath\":" << JsonQuoted(object.destructor_path)
           << ",\"factory\":" << JsonQuoted(object.factory)
           << ",\"managerOwner\":" << JsonQuoted(object.manager_owner)
           << ",\"context\":" << JsonQuoted(object.context)
           << ",\"properties\":" << JsonStringMap(object.properties)
           << ",\"evidenceRefs\":" << EvidenceRefsJson(object.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(object.authority))
           << ",\"status\":" << JsonQuoted(object.status) << '}';
    return output.str();
}

std::string RegistryJson(const UltimateRegistryRecord& registry) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Registry\""
           << ",\"canonicalIdentity\":" << JsonQuoted(registry.canonical_identity)
           << ",\"family\":" << JsonQuoted(registry.family)
           << ",\"recordCount\":" << registry.record_count
           << ",\"readOnlyEnumeration\":" <<
                (registry.read_only_enumeration ? "true" : "false")
           << ",\"lookupConsumer\":" << JsonQuoted(registry.lookup_consumer)
           << ",\"destination\":" << JsonQuoted(registry.destination)
           << ",\"evidenceRefs\":" << EvidenceRefsJson(registry.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(registry.authority)) << '}';
    return output.str();
}

std::string ContentJson(const UltimateContentEntity& entity) {
    const auto delimiter = entity.canonical_identity.rfind(':');
    const std::string primary_identity = delimiter == std::string::npos ?
        entity.canonical_identity : entity.canonical_identity.substr(delimiter + 1U);
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Content\""
           << ",\"canonicalIdentity\":" << JsonQuoted(entity.canonical_identity)
           << ",\"id\":" << JsonQuoted(primary_identity)
           << ",\"family\":" << JsonQuoted(entity.family)
           << ",\"exactSource\":" << JsonQuoted(entity.exact_source)
           << ",\"clientBuildId\":" << JsonQuoted(entity.client_build_id)
           << ",\"rawValues\":" << JsonStringMap(entity.raw_values)
           << ",\"normalizedValues\":" << JsonStringMap(entity.normalized_values)
           << ",\"baseValues\":" << JsonStringMap(entity.base_values)
           << ",\"runtimeValues\":" << JsonStringMap(entity.runtime_values)
           << ",\"currentValues\":" << JsonStringMap(entity.current_values);
    if (entity.family == "MonsterDrop" || entity.family == "DropRelation") {
        auto observed = entity.normalized_values.find("Rate");
        if (observed == entity.normalized_values.end())
            observed = entity.normalized_values.find("ObservedRate");
        double observed_rate = 0.0;
        bool observed_rate_valid = false;
        if (observed != entity.normalized_values.end()) {
            std::istringstream parser(observed->second);
            parser >> observed_rate;
            observed_rate_valid = parser && parser.peek() == std::char_traits<char>::eof() &&
                std::isfinite(observed_rate);
        }
        output << ",\"authoritativeRate\":null"
               << ",\"authoritativeRateAuthority\":\"UNKNOWN_SERVER_ONLY\""
               << ",\"defaultDisabledRate\":0"
               << ",\"enabled\":false,\"isDropEnabled\":false"
               << ",\"observedClientRate\":";
        if (observed_rate_valid) output << std::setprecision(12) << observed_rate;
        else output << "null";
        output << ",\"observedClientRateAuthority\":" <<
            JsonQuoted(observed_rate_valid ? "OBSERVED" : "UNKNOWN");
    }
    output << ",\"evidenceRefs\":" << EvidenceRefsJson(entity.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(entity.authority))
           << ",\"observationCount\":" << entity.observation_count
           << ",\"contradictions\":" << entity.contradictions
           << ",\"status\":" << JsonQuoted(entity.status) << '}';
    return output.str();
}

std::string ProtocolFieldJson(const UltimateProtocolField& field) {
    std::ostringstream output;
    output << "{\"packetOffset\":" << field.packet_offset
           << ",\"width\":" << field.width
           << ",\"signed\":" << (field.signed_value ? "true" : "false")
           << ",\"valueType\":" << JsonQuoted(field.value_type)
           << ",\"endian\":" << JsonQuoted(field.endian)
           << ",\"encoding\":" << JsonQuoted(field.encoding)
           << ",\"lengthRelationship\":" << JsonQuoted(field.length_relationship)
           << ",\"optionalOrConditional\":" <<
                (field.optional_or_conditional ? "true" : "false")
           << ",\"repeatedOrList\":" << (field.repeated_or_list ? "true" : "false")
           << ",\"enumCandidate\":" << JsonQuoted(field.enum_candidate)
           << ",\"semanticCandidate\":" << JsonQuoted(field.semantic_candidate)
           << ",\"parserRva\":" << field.parser_rva
           << ",\"serializerRva\":" << field.serializer_rva
           << ",\"handlerContext\":" << JsonQuoted(field.handler_context)
           << ",\"objectEndpoint\":" << JsonQuoted(field.object_endpoint)
           << ",\"observationCount\":" << field.observation_count
           << ",\"contradictions\":" << field.contradictions
           << ",\"evidenceRefs\":" << EvidenceRefsJson(field.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(field.authority)) << '}';
    return output.str();
}

std::string PacketJson(const UltimatePacketSchema& packet) {
    std::ostringstream fields;
    fields << '[';
    for (std::size_t index = 0; index < packet.typed_fields.size(); ++index) {
        if (index != 0U) fields << ',';
        fields << ProtocolFieldJson(packet.typed_fields[index]);
    }
    fields << ']';
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"PacketSchema\""
           << ",\"opcode\":" << JsonQuoted(packet.opcode)
           << ",\"direction\":" << JsonQuoted(packet.direction)
           << ",\"actionFamily\":" << JsonQuoted(packet.action_family)
           << ",\"subsystem\":" << JsonQuoted(packet.subsystem)
           << ",\"frameLengthRule\":" << JsonQuoted(packet.frame_length_rule)
           << ",\"checksumRule\":" << JsonQuoted(packet.checksum_rule)
           << ",\"encryptionBoundary\":" << JsonQuoted(packet.encryption_boundary)
           << ",\"minimumLength\":" << packet.minimum_length
           << ",\"maximumLength\":" << packet.maximum_length
           << ",\"parserRva\":" << packet.parser_rva
           << ",\"serializerRva\":" << packet.serializer_rva
           << ",\"handlerRva\":" << packet.handler_rva
           << ",\"typedFields\":" << fields.str()
           << ",\"enumMappings\":" << JsonStringArray(packet.enum_mappings)
           << ",\"conditions\":" << JsonStringArray(packet.conditions)
           << ",\"requestResponseLinks\":" << JsonStringArray(packet.request_response_links)
           << ",\"requiredProtocolState\":" << JsonQuoted(packet.required_protocol_state)
           << ",\"stateMutationLinks\":" << JsonStringArray(packet.state_mutation_links)
           << ",\"evidenceRefs\":" << EvidenceRefsJson(packet.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(packet.authority))
           << ",\"observationCount\":" << packet.observation_count
           << ",\"contradictions\":" << packet.contradictions << '}';
    return output.str();
}

std::string JsonLines(const std::vector<std::string>& lines) {
    std::string output;
    for (const auto& line : lines) {
        output += line;
        output.push_back('\n');
    }
    return output;
}

std::string JoinJsonValues(const std::vector<std::string>& values) {
    std::string output;
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0U) output.push_back(',');
        output += values[index];
    }
    return output;
}

std::string MutationJson(const UltimateMutationRecord& mutation) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Mutation\""
           << ",\"objectIdentity\":" << JsonQuoted(mutation.object_identity)
           << ",\"propertyCandidate\":" << JsonQuoted(mutation.property_candidate)
           << ",\"type\":" << JsonQuoted(mutation.value_type)
           << ",\"beforeValue\":" << JsonQuoted(mutation.before_value)
           << ",\"inputValue\":" << JsonQuoted(mutation.input_value)
           << ",\"afterValue\":" << JsonQuoted(mutation.after_value)
           << ",\"triggerPacket\":" << JsonQuoted(mutation.trigger_packet)
           << ",\"triggerHandler\":" << JsonQuoted(mutation.trigger_handler)
           << ",\"triggerAction\":" << JsonQuoted(mutation.trigger_action)
           << ",\"writerRva\":" << mutation.writer_rva
           << ",\"consumers\":" << JsonStringArray(mutation.consumers)
           << ",\"observationCount\":" << mutation.observation_count
           << ",\"contradictions\":" << mutation.contradictions
           << ",\"evidenceRefs\":" << EvidenceRefsJson(mutation.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(mutation.authority))
           << ",\"status\":" << JsonQuoted(mutation.status) << '}';
    return output.str();
}

std::string ValueFlowJson(const UltimateValueFlowEdge& edge) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"ValueFlow\""
           << ",\"edgeKind\":" << JsonQuoted(edge.edge_kind)
           << ",\"sourceToken\":" << JsonQuoted(edge.source_token)
           << ",\"targetToken\":" << JsonQuoted(edge.target_token)
           << ",\"operation\":" << JsonQuoted(edge.operation)
           << ",\"propagationHops\":" << edge.propagation_hops
           << ",\"evidenceRefs\":" << EvidenceRefsJson(edge.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(edge.authority)) << '}';
    return output.str();
}

std::string ResourceJson(const UltimateResourceRecord& resource) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Resource\""
           << ",\"relativePath\":" << JsonQuoted(resource.relative_path)
           << ",\"fileSha256\":" << JsonQuoted(resource.file_sha256)
           << ",\"sourceOffset\":" << resource.source_offset
           << ",\"decodedBufferSha256\":" << JsonQuoted(resource.decoded_buffer_sha256)
           << ",\"decodeRva\":" << resource.decode_rva
           << ",\"deserializeRva\":" << resource.deserialize_rva
           << ",\"destination\":" << JsonQuoted(resource.destination)
           << ",\"recordCount\":" << resource.record_count
           << ",\"schemaCandidate\":" << JsonQuoted(resource.schema_candidate)
           << ",\"provenance\":" << JsonQuoted(resource.provenance)
           << ",\"evidenceRefs\":" << EvidenceRefsJson(resource.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(resource.authority)) << '}';
    return output.str();
}

std::string FormulaJson(const UltimateFormulaRecord& formula) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Formula\""
           << ",\"formulaId\":" << JsonQuoted(formula.formula_id)
           << ",\"category\":" << JsonQuoted(formula.category)
           << ",\"expression\":" << JsonQuoted(formula.expression)
           << ",\"operands\":" << JsonStringArray(formula.operands)
           << ",\"result\":" << JsonQuoted(formula.result)
           << ",\"observationCount\":" << formula.observation_count
           << ",\"contradictions\":" << formula.contradictions
           << ",\"replayConsistent\":" << (formula.replay_consistent ? "true" : "false")
           << ",\"exactRecovered\":" << (formula.exact_recovered ? "true" : "false")
           << ",\"evidenceRefs\":" << EvidenceRefsJson(formula.evidence)
           << ",\"authority\":" << JsonQuoted(ToString(formula.authority))
           << ",\"status\":" << JsonQuoted(formula.status) << '}';
    return output.str();
}

std::string FsmJson(const UltimateFsmRecord& machine) {
    std::ostringstream transitions;
    transitions << '[';
    for (std::size_t index = 0; index < machine.transitions.size(); ++index) {
        if (index != 0U) transitions << ',';
        const auto& transition = machine.transitions[index];
        transitions << "{\"from\":" << JsonQuoted(transition.from_state)
                    << ",\"to\":" << JsonQuoted(transition.to_state)
                    << ",\"guard\":" << JsonQuoted(transition.guard)
                    << ",\"input\":" << JsonQuoted(transition.input)
                    << ",\"output\":" << JsonQuoted(transition.output)
                    << ",\"mutation\":" << JsonQuoted(transition.mutation)
                    << ",\"observationCount\":" << transition.observation_count
                    << ",\"replayConsistent\":" <<
                         (transition.replay_consistent ? "true" : "false")
                    << ",\"evidenceRefs\":" << EvidenceRefsJson(transition.evidence)
                    << ",\"authority\":" << JsonQuoted(ToString(transition.authority)) << '}';
    }
    transitions << ']';
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"StateMachine\""
           << ",\"machineId\":" << JsonQuoted(machine.machine_id)
           << ",\"states\":" << JsonStringArray(machine.states)
           << ",\"transitions\":" << transitions.str()
           << ",\"invalidTransitions\":" << JsonStringArray(machine.invalid_transitions)
           << ",\"recoveryOrRollback\":" << JsonQuoted(machine.recovery_or_rollback)
           << ",\"authority\":" << JsonQuoted(ToString(machine.authority)) << '}';
    return output.str();
}

std::string GraphNodeJson(const UltimateGraphNode& node) {
    return "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
        ",\"recordType\":\"Node\",\"id\":" + JsonQuoted(node.id) +
        ",\"kind\":" + JsonQuoted(node.kind) + ",\"attributes\":" +
        JsonStringMap(node.attributes) + ",\"authority\":" +
        JsonQuoted(ToString(node.authority)) + '}';
}

std::string GraphEdgeJson(const UltimateGraphEdge& edge) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"Edge\",\"from\":" << JsonQuoted(edge.from)
           << ",\"to\":" << JsonQuoted(edge.to)
           << ",\"relation\":" << JsonQuoted(edge.relation)
           << ",\"evidenceRefs\":" << EvidenceRefsJson(edge.evidence)
           << ",\"clientBuildId\":" << JsonQuoted(edge.client_build_id)
           << ",\"rvaOrResourceOffset\":" << edge.rva_or_resource_offset
           << ",\"observationCount\":" << edge.observation_count
           << ",\"contradictions\":" << edge.contradictions
           << ",\"authority\":" << JsonQuoted(ToString(edge.authority))
           << ",\"status\":" << JsonQuoted(edge.status) << '}';
    return output.str();
}

std::string MlPredictionJson(const UltimateMlPrediction& prediction) {
    std::ostringstream embedding;
    embedding << '[';
    for (std::size_t index = 0; index < prediction.embedding.size(); ++index) {
        if (index != 0U) embedding << ',';
        embedding << std::setprecision(17) << prediction.embedding[index];
    }
    embedding << ']';
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"MlPrediction\""
           << ",\"sampleId\":" << JsonQuoted(prediction.sample_id)
           << ",\"embedding\":" << embedding.str()
           << ",\"cluster\":" << prediction.cluster
           << ",\"nearestSampleId\":" << JsonQuoted(prediction.nearest_sample_id)
           << ",\"similarity\":" << std::setprecision(17) << prediction.similarity
           << ",\"propagatedLabel\":" << JsonQuoted(prediction.propagated_label)
           << ",\"propagatedLabelScore\":" << prediction.propagated_label_score
           << ",\"outlierScore\":" << prediction.outlier_score
           << ",\"authority\":" << JsonQuoted(ToString(prediction.authority)) << '}';
    return output.str();
}

std::string MlSequenceJson(const UltimateSequenceRanking& ranking) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"recordType\":\"MlSequenceRanking\""
           << ",\"sequenceId\":" << JsonQuoted(ranking.sequence_id)
           << ",\"noveltyScore\":" << std::setprecision(17) << ranking.novelty_score
           << ",\"authority\":" << JsonQuoted(ToString(ranking.authority)) << '}';
    return output.str();
}

std::string CoverageJson(const UltimateRecoveryResult& recovery) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"metrics\":[";
    for (std::size_t index = 0; index < recovery.coverage.size(); ++index) {
        if (index != 0U) output << ',';
        const auto& metric = recovery.coverage[index];
        output << "{\"domain\":" << JsonQuoted(metric.domain)
               << ",\"totalRecovered\":" << metric.total_recovered
               << ",\"recovered\":" << metric.recovered
               << ",\"promotableRecovered\":" << metric.recovered
               << ",\"authorityNumerators\":{\"VERIFIED\":" <<
                    metric.verified_recovered
               << ",\"DERIVED\":" << metric.derived_recovered
               << ",\"OBSERVED\":" << metric.observed_recovered
               << ",\"HYPOTHESIS\":" << metric.hypothesis_recovered
               << ",\"UNKNOWN\":" << metric.unknown_recovered
               << ",\"UNKNOWN_SERVER_ONLY\":" <<
                    metric.unknown_server_only_recovered
               << ",\"REJECTED\":" << metric.rejected_recovered << '}'
               << ",\"denominator\":";
        if (metric.denominator) output << *metric.denominator;
        else output << "null";
        output << ",\"percent\":";
        if (metric.percent) output << std::setprecision(12) << *metric.percent;
        else output << "null";
        output << ",\"denominatorBasis\":" << JsonQuoted(metric.denominator_basis)
               << ",\"denominatorAuthority\":" <<
                    JsonQuoted(ToString(metric.denominator_authority))
               << ",\"status\":" << JsonQuoted(metric.status) << '}';
    }
    output << "]}";
    return output.str();
}

std::string MissingJson(const UltimateMissingEvidence& missing) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateRecoverySchema)
           << ",\"kind\":" << JsonQuoted(missing.kind)
           << ",\"identity\":" << JsonQuoted(missing.identity)
           << ",\"evidenceNeed\":" << JsonQuoted(missing.evidence_need)
           << ",\"safeNextAction\":" << JsonQuoted(missing.safe_next_action)
           << ",\"priority\":" << missing.priority
           << ",\"authority\":" << JsonQuoted(ToString(missing.authority)) << '}';
    return output.str();
}

std::string AuthorityRecords(const UltimateRecoveryResult& recovery,
                             UltimateAuthority authority) {
    std::vector<std::string> records;
    for (const auto& object : recovery.objects) {
        if (object.authority == authority) records.push_back(ObjectJson(object));
    }
    for (const auto& registry : recovery.registries) {
        if (registry.authority == authority) records.push_back(RegistryJson(registry));
    }
    for (const auto& resource : recovery.resources) {
        if (resource.authority == authority) records.push_back(ResourceJson(resource));
    }
    for (const auto& mutation : recovery.mutations) {
        if (mutation.authority == authority) records.push_back(MutationJson(mutation));
    }
    for (const auto& flow : recovery.value_flow) {
        if (flow.authority == authority) records.push_back(ValueFlowJson(flow));
    }
    for (const auto& entity : recovery.content_entities) {
        if (entity.authority == authority) records.push_back(ContentJson(entity));
    }
    for (const auto& packet : recovery.packet_schemas) {
        if (packet.authority == authority) records.push_back(PacketJson(packet));
    }
    for (const auto& formula : recovery.formulas) {
        if (formula.authority == authority) records.push_back(FormulaJson(formula));
    }
    for (const auto& machine : recovery.state_machines) {
        if (machine.authority == authority) records.push_back(FsmJson(machine));
    }
    for (const auto& node : recovery.graph_nodes) {
        if (node.authority == authority) records.push_back(GraphNodeJson(node));
    }
    for (const auto& edge : recovery.graph_edges) {
        if (edge.authority == authority) records.push_back(GraphEdgeJson(edge));
    }
    for (const auto& prediction : recovery.ml.predictions) {
        if (prediction.authority == authority) records.push_back(MlPredictionJson(prediction));
    }
    for (const auto& ranking : recovery.ml.sequence_rankings) {
        if (ranking.authority == authority) records.push_back(MlSequenceJson(ranking));
    }
    return JsonLines(records);
}

std::string FamilyRecords(const UltimateRecoveryResult& recovery,
                          const std::set<std::string>& families) {
    std::vector<std::string> records;
    for (const auto& entity : recovery.content_entities) {
        if (families.find(LowerAscii(entity.family)) != families.end())
            records.push_back(ContentJson(entity));
    }
    return JsonLines(records);
}

std::vector<UltimateAuthority> FamilyAuthorities(const UltimateRecoveryResult& recovery,
                                                 const std::set<std::string>& families) {
    std::vector<UltimateAuthority> authorities;
    for (const auto& entity : recovery.content_entities) {
        if (families.find(LowerAscii(entity.family)) != families.end())
            authorities.push_back(entity.authority);
    }
    return authorities;
}

UltimateAuthority DescriptorAuthority(const std::vector<UltimateAuthority>& authorities) {
    return CombineAuthoritiesConservatively(authorities);
}

template <typename Records>
std::vector<UltimateAuthority> RecordAuthorities(const Records& records) {
    std::vector<UltimateAuthority> authorities;
    authorities.reserve(records.size());
    for (const auto& record : records) authorities.push_back(record.authority);
    return authorities;
}

bool IsSafeArtifactRelativePath(const fs::path& relative) {
    if (relative.empty() || relative.is_absolute() || relative.has_root_name() ||
        relative.has_root_directory()) return false;
    for (const auto& component : relative) {
        if (component == L".." || component == L".") return false;
    }
    return true;
}

struct PinnedUltimateDirectory {
    fs::path expected_path;
    fs::path final_path;
    BY_HANDLE_FILE_INFORMATION identity{};
    UltimateScopedHandle handle;
};

struct PinnedUltimateFile {
    fs::path expected_path;
    fs::path final_path;
    BY_HANDLE_FILE_INFORMATION identity{};
    std::uint64_t size = 0;
    std::string sha256;
    UltimateScopedHandle handle;
};

std::optional<PinnedUltimateDirectory> PinUltimateDirectory(const fs::path& path) {
    std::error_code error;
    const auto absolute = fs::absolute(path, error).lexically_normal();
    if (error) return std::nullopt;
    PinnedUltimateDirectory pinned;
    pinned.expected_path = absolute;
    const auto absolute_io_path = Win32ExtendedPathForFileIo(absolute);
    pinned.handle = UltimateScopedHandle(CreateFileW(absolute_io_path.c_str(),
        FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    FILE_ATTRIBUTE_TAG_INFO tag{};
    const auto final = pinned.handle ? UltimateFinalPath(pinned.handle.value) :
        std::optional<fs::path>{};
    if (!pinned.handle || !final ||
        !GetFileInformationByHandleEx(pinned.handle.value, FileAttributeTagInfo,
            &tag, sizeof(tag)) ||
        !GetFileInformationByHandle(pinned.handle.value, &pinned.identity) ||
        (tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (tag.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 ||
        FoldUltimatePath(final->lexically_normal().wstring()) !=
            FoldUltimatePath(absolute.wstring()))
        return std::nullopt;
    pinned.final_path = final->lexically_normal();
    return pinned;
}

bool ValidatePinnedUltimateDirectory(const PinnedUltimateDirectory& pinned) {
    FILE_ATTRIBUTE_TAG_INFO tag{};
    BY_HANDLE_FILE_INFORMATION current{};
    const auto final = UltimateFinalPath(pinned.handle.value);
    const auto expected_io_path = Win32ExtendedPathForFileIo(pinned.expected_path);
    UltimateScopedHandle rebound(CreateFileW(expected_io_path.c_str(),
        FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    BY_HANDLE_FILE_INFORMATION rebound_identity{};
    return pinned.handle && final && *final == pinned.final_path &&
        GetFileInformationByHandleEx(pinned.handle.value, FileAttributeTagInfo,
            &tag, sizeof(tag)) != FALSE &&
        GetFileInformationByHandle(pinned.handle.value, &current) != FALSE &&
        (tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0 &&
        (tag.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 &&
        SameUltimateFileObject(pinned.identity, current) && rebound &&
        GetFileInformationByHandle(rebound.value, &rebound_identity) != FALSE &&
        SameUltimateFileObject(pinned.identity, rebound_identity) &&
        UltimateFinalPath(rebound.value) == std::optional<fs::path>(pinned.final_path);
}

bool UltimatePathIsReparseFree(const fs::path& root, const fs::path& candidate,
                               bool allow_missing_leaf = false) {
    std::error_code root_error;
    std::error_code candidate_error;
    const auto absolute_root = fs::absolute(root, root_error).lexically_normal();
    const auto absolute_candidate = fs::absolute(candidate, candidate_error).lexically_normal();
    if (root_error || candidate_error ||
        !UltimatePathContained(absolute_root, absolute_candidate)) return false;
    const auto relative = absolute_candidate.lexically_relative(absolute_root);
    fs::path current = absolute_root;
    const auto count = static_cast<std::size_t>(std::distance(relative.begin(), relative.end()));
    std::size_t index = 0;
    for (const auto& component : relative) {
        current /= component;
        ++index;
        const auto current_io_path = Win32ExtendedPathForFileIo(current);
        const DWORD attributes = GetFileAttributesW(current_io_path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES) {
            if (!(allow_missing_leaf && index == count &&
                  GetLastError() == ERROR_FILE_NOT_FOUND)) return false;
            continue;
        }
        if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return false;
    }
    return true;
}

std::optional<PinnedUltimateFile> PinUltimateFile(
    const PinnedUltimateDirectory& root, const fs::path& path) {
    if (!ValidatePinnedUltimateDirectory(root) ||
        !UltimatePathIsReparseFree(root.expected_path, path)) return std::nullopt;
    std::error_code error;
    const auto absolute = fs::absolute(path, error).lexically_normal();
    if (error || !UltimatePathContained(root.expected_path, absolute)) return std::nullopt;
    PinnedUltimateFile pinned;
    pinned.expected_path = absolute;
    const auto absolute_io_path = Win32ExtendedPathForFileIo(absolute);
    pinned.handle = UltimateScopedHandle(CreateFileW(absolute_io_path.c_str(),
        GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr));
    FILE_ATTRIBUTE_TAG_INFO tag{};
    const auto final = pinned.handle ? UltimateFinalPath(pinned.handle.value) :
        std::optional<fs::path>{};
    if (!pinned.handle || !final ||
        !GetFileInformationByHandleEx(pinned.handle.value, FileAttributeTagInfo,
            &tag, sizeof(tag)) ||
        !GetFileInformationByHandle(pinned.handle.value, &pinned.identity) ||
        (tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !UltimatePathContained(root.final_path, *final) ||
        FoldUltimatePath(final->lexically_normal().wstring()) !=
            FoldUltimatePath(absolute.wstring()))
        return std::nullopt;
    pinned.final_path = final->lexically_normal();
    pinned.size = UltimateHandleSize(pinned.identity);
    const auto hash = UltimateSha256ByHandle(pinned.handle.value);
    if (!hash) return std::nullopt;
    pinned.sha256 = *hash;
    return pinned;
}

bool ValidatePinnedUltimateFile(const PinnedUltimateDirectory& root,
                                const PinnedUltimateFile& pinned) {
    BY_HANDLE_FILE_INFORMATION current{};
    FILE_ATTRIBUTE_TAG_INFO tag{};
    const auto final = UltimateFinalPath(pinned.handle.value);
    const auto hash = UltimateSha256ByHandle(pinned.handle.value);
    const auto expected_io_path = Win32ExtendedPathForFileIo(pinned.expected_path);
    UltimateScopedHandle rebound(CreateFileW(expected_io_path.c_str(),
        FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    BY_HANDLE_FILE_INFORMATION rebound_identity{};
    return ValidatePinnedUltimateDirectory(root) && final && hash &&
        *final == pinned.final_path && *hash == pinned.sha256 &&
        GetFileInformationByHandle(pinned.handle.value, &current) != FALSE &&
        GetFileInformationByHandleEx(pinned.handle.value, FileAttributeTagInfo,
            &tag, sizeof(tag)) != FALSE &&
        SameUltimateFileObject(pinned.identity, current) &&
        UltimateHandleSize(current) == pinned.size &&
        (tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0 &&
        UltimatePathContained(root.final_path, *final) && rebound &&
        GetFileInformationByHandle(rebound.value, &rebound_identity) != FALSE &&
        SameUltimateFileObject(pinned.identity, rebound_identity) &&
        UltimateFinalPath(rebound.value) == std::optional<fs::path>(pinned.final_path);
}

bool WriteContainedUltimateFile(const PinnedUltimateDirectory& root,
                                const fs::path& target, std::string_view content,
                                PinnedUltimateFile* written) {
    if (!ValidatePinnedUltimateDirectory(root)) return false;
    std::error_code error;
    fs::create_directories(target.parent_path(), error);
    if (error || !UltimatePathIsReparseFree(root.expected_path, target.parent_path()))
        return false;
    auto parent = PinUltimateDirectory(target.parent_path());
    if (!parent || !UltimatePathContained(root.final_path, parent->final_path) ||
        !WriteUtf8FileAtomic(target, content)) return false;
    auto file = PinUltimateFile(root, target);
    if (!file || !ValidatePinnedUltimateDirectory(*parent) ||
        !ValidatePinnedUltimateFile(root, *file) ||
        !UltimatePathIsReparseFree(root.expected_path, target)) return false;
    *written = std::move(*file);
    return true;
}

struct BundleDraft {
    UltimateBundleFile metadata;
    std::string content;
};

void AddBundleDraft(std::vector<BundleDraft>* drafts, std::string relative_path,
                    std::string content, std::string provenance,
                    UltimateAuthority authority,
                    std::string schema_version = std::string(kUltimateRecoverySchema)) {
    BundleDraft draft;
    draft.metadata.relative_path = std::move(relative_path);
    draft.metadata.provenance = std::move(provenance);
    draft.metadata.authority = authority;
    draft.metadata.schema_version = std::move(schema_version);
    draft.content = std::move(content);
    drafts->push_back(std::move(draft));
}

std::string JsonObjectWithStatus(std::string_view status,
                                 UltimateAuthority authority = UltimateAuthority::Unknown) {
    return "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
        ",\"status\":" + JsonQuoted(status) + ",\"authority\":" +
        JsonQuoted(ToString(authority)) + '}';
}

std::vector<BundleDraft> BuildBundleDrafts(const UltimateRecoveryResult& recovery,
                                           std::string_view binding_status,
                                           bool exact_binding_validated,
                                           std::string_view computed_sha256,
                                           std::string_view computed_version,
                                           std::string_view computed_architecture) {
    std::vector<BundleDraft> drafts;
    AddBundleDraft(&drafts, "manifest/build-identity.json",
        "{\"schemaVersion\":\"god2-client-build-identity-v1\",\"executable\":" +
            JsonQuoted(kExpectedClientName) + ",\"architecture\":" +
            JsonQuoted(kExpectedClientArchitecture) + ",\"fileVersion\":" +
            JsonQuoted(kExpectedClientVersion) + ",\"sha256\":" +
            JsonQuoted(kExpectedClientSha256) + ",\"bindingStatus\":" +
            JsonQuoted(binding_status) + ",\"exactBindingValidated\":" +
            (exact_binding_validated ? "true" : "false") +
            ",\"recoveryAttestationStatus\":" +
            JsonQuoted(recovery.client_build_attestation_status) +
            ",\"computedSha256\":" +
            (computed_sha256.empty() ? "null" : JsonQuoted(computed_sha256)) +
            ",\"computedFileVersion\":" +
            (computed_version.empty() ? "null" : JsonQuoted(computed_version)) +
            ",\"computedArchitecture\":" +
            (computed_architecture.empty() ? "null" : JsonQuoted(computed_architecture)) + '}',
        "UltimateExactBuildContract", exact_binding_validated ?
            UltimateAuthority::Verified : UltimateAuthority::Unknown,
        "god2-client-build-identity-v1");
    AddBundleDraft(&drafts, "manifest/environment.json",
        "{\"schemaVersion\":\"god2-ultimate-environment-v1\",\"toolArchitecture\":\"x64\","
        "\"probeArchitecture\":\"x86\",\"evidenceUpload\":false,"
        "\"productionDatabaseMutation\":false}",
        "UltimateRecoveryEngine", UltimateAuthority::Verified,
        "god2-ultimate-environment-v1");

    const std::vector<std::pair<std::string, std::set<std::string>>> canonical_files = {
        {"canonical/monsters.jsonl", {"monster", "monstertemplate", "monsterruntime"}},
        {"canonical/monster-stats.jsonl", {"monsterstat"}},
        {"canonical/monster-skills.jsonl", {"monsterskill"}},
        {"canonical/monster-ai.jsonl", {"aiprofile", "monsterai"}},
        {"canonical/monster-spawns.jsonl", {"monsterspawn", "spawngroup"}},
        {"canonical/monster-drops.jsonl", {"monsterdrop", "droprelation", "droptable"}},
        {"canonical/npcs.jsonl", {"npc", "npctemplate"}},
        {"canonical/npc-spawns.jsonl", {"npcspawn"}},
        {"canonical/maps.jsonl", {"map", "maptemplate"}},
        {"canonical/map-regions.jsonl", {"mapregion"}},
        {"canonical/portals.jsonl", {"portal"}},
        {"canonical/quests.jsonl", {"quest", "questtemplate", "questruntime"}},
        {"canonical/quest-requirements.jsonl", {"questrequirement"}},
        {"canonical/quest-objectives.jsonl", {"questobjective"}},
        {"canonical/quest-rewards.jsonl", {"questreward"}},
        {"canonical/items.jsonl", {"item", "itemtemplate", "inventoryitem"}},
        {"canonical/equipment.jsonl", {"equipment"}},
        {"canonical/skills.jsonl", {"skill", "skilltemplate", "skillruntime"}},
        {"canonical/pets.jsonl", {"pet", "companion"}},
        {"canonical/mounts.jsonl", {"mount"}},
        {"canonical/merchants.jsonl", {"merchant", "shop"}},
        {"canonical/recipes.jsonl", {"recipe"}}
    };
    for (const auto& [path, families] : canonical_files) {
        const auto authorities = FamilyAuthorities(recovery, families);
        AddBundleDraft(&drafts, path, FamilyRecords(recovery, families),
                       "EvidenceDrivenCanonicalRecovery",
                       DescriptorAuthority(authorities));
    }

    std::vector<std::string> packet_lines;
    std::vector<UltimateAuthority> packet_authorities;
    for (const auto& packet : recovery.packet_schemas) {
        packet_lines.push_back(PacketJson(packet));
        packet_authorities.push_back(packet.authority);
        for (const auto& field : packet.typed_fields)
            packet_authorities.push_back(field.authority);
    }
    AddBundleDraft(&drafts, "protocol/packet-catalog.jsonl", JsonLines(packet_lines),
                   "TypedSemanticProtocolRecovery", DescriptorAuthority(packet_authorities));
    std::vector<std::string> parser_lines;
    std::vector<std::string> serializer_lines;
    std::vector<UltimateAuthority> parser_authorities;
    std::vector<UltimateAuthority> serializer_authorities;
    for (const auto& packet : recovery.packet_schemas) {
        if (packet.parser_rva != 0U) {
            parser_lines.push_back(PacketJson(packet));
            parser_authorities.push_back(packet.authority);
            for (const auto& field : packet.typed_fields)
                parser_authorities.push_back(field.authority);
        }
        if (packet.serializer_rva != 0U) {
            serializer_lines.push_back(PacketJson(packet));
            serializer_authorities.push_back(packet.authority);
            for (const auto& field : packet.typed_fields)
                serializer_authorities.push_back(field.authority);
        }
    }
    AddBundleDraft(&drafts, "protocol/parser-schemas.jsonl", JsonLines(parser_lines),
                   "TypedParserTrace", DescriptorAuthority(parser_authorities));
    AddBundleDraft(&drafts, "protocol/serializer-schemas.jsonl", JsonLines(serializer_lines),
                   "TypedSerializerTrace", DescriptorAuthority(serializer_authorities));
    AddBundleDraft(&drafts, "protocol/request-response.jsonl", {},
                   "EvidenceDrivenRequestResponseCorrelation", UltimateAuthority::Unknown);
    std::vector<std::string> fsm_lines;
    std::vector<UltimateAuthority> fsm_authorities;
    for (const auto& machine : recovery.state_machines) {
        fsm_lines.push_back(FsmJson(machine));
        fsm_authorities.push_back(machine.authority);
        for (const auto& transition : machine.transitions)
            fsm_authorities.push_back(transition.authority);
    }
    AddBundleDraft(&drafts, "protocol/protocol-state-machines.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"machines\":[" + JoinJsonValues(fsm_lines) + "]}",
                    "DeterministicStateMachineRecovery",
                    DescriptorAuthority(fsm_authorities));
    AddBundleDraft(&drafts, "protocol/language-neutral-idl.json",
                   JsonObjectWithStatus("EvidenceBlockedSchemaAuthorityInsufficient"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);

    std::vector<std::string> object_lines;
    for (const auto& object : recovery.objects) object_lines.push_back(ObjectJson(object));
    AddBundleDraft(&drafts, "runtime/object-layouts.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"objects\":[" + JoinJsonValues(object_lines) + "]}",
                    "ObjectResolverAndHeapEvidence",
                    DescriptorAuthority(RecordAuthorities(recovery.objects)));
    std::vector<std::string> registry_lines;
    for (const auto& registry : recovery.registries)
        registry_lines.push_back(RegistryJson(registry));
    AddBundleDraft(&drafts, "runtime/registries.jsonl", JsonLines(registry_lines),
                   "ReadOnlyRegistryRecovery",
                   DescriptorAuthority(RecordAuthorities(recovery.registries)));
    std::vector<std::string> mutation_lines;
    for (const auto& mutation : recovery.mutations) mutation_lines.push_back(MutationJson(mutation));
    AddBundleDraft(&drafts, "runtime/state-mutations.jsonl", JsonLines(mutation_lines),
                   "BeforeInputAfterCausalRecovery",
                   DescriptorAuthority(RecordAuthorities(recovery.mutations)));
    std::vector<std::string> formula_lines;
    for (const auto& formula : recovery.formulas) formula_lines.push_back(FormulaJson(formula));
    AddBundleDraft(&drafts, "runtime/formulas.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"formulas\":[" + JoinJsonValues(formula_lines) + "]}",
                    "FormulaEvidenceAndReplay",
                    DescriptorAuthority(RecordAuthorities(recovery.formulas)));
    AddBundleDraft(&drafts, "runtime/gameplay-rules.json",
                   JsonObjectWithStatus("EvidenceBlockedRulesRequireDeterministicVerification"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "runtime/runtime-state-machines.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"machines\":[" + JoinJsonValues(fsm_lines) + "]}",
                    "DeterministicStateMachineRecovery",
                    DescriptorAuthority(fsm_authorities));

    std::vector<std::string> ml_lines;
    std::vector<UltimateAuthority> ml_authorities;
    for (const auto& prediction : recovery.ml.predictions) {
        ml_lines.push_back(MlPredictionJson(prediction));
        ml_authorities.push_back(prediction.authority);
    }
    for (const auto& ranking : recovery.ml.sequence_rankings) {
        ml_lines.push_back(MlSequenceJson(ranking));
        ml_authorities.push_back(ranking.authority);
    }
    AddBundleDraft(&drafts, "runtime/ml-hypotheses.jsonl", JsonLines(ml_lines),
                   "DeterministicMetadataOnlyMl",
                   DescriptorAuthority(ml_authorities));

    std::vector<std::string> graph_lines;
    std::vector<UltimateAuthority> graph_authorities;
    for (const auto& node : recovery.graph_nodes) {
        graph_lines.push_back(GraphNodeJson(node));
        graph_authorities.push_back(node.authority);
    }
    for (const auto& edge : recovery.graph_edges) {
        graph_lines.push_back(GraphEdgeJson(edge));
        graph_authorities.push_back(edge.authority);
    }
    AddBundleDraft(&drafts, "graph/god2-knowledge-graph.jsonl", JsonLines(graph_lines),
                   "SemanticEvidenceGraph", DescriptorAuthority(graph_authorities));
    AddBundleDraft(&drafts, "graph/graph-index.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"nodeCount\":" + std::to_string(recovery.graph_nodes.size()) +
                       ",\"edgeCount\":" + std::to_string(recovery.graph_edges.size()) + '}',
                    "SemanticEvidenceGraph", DescriptorAuthority(graph_authorities));

    std::vector<std::string> flow_lines;
    for (const auto& flow : recovery.value_flow) flow_lines.push_back(ValueFlowJson(flow));
    AddBundleDraft(&drafts, "provenance/value-provenance.jsonl", JsonLines(flow_lines),
                   "BoundedValueProvenance",
                   DescriptorAuthority(RecordAuthorities(recovery.value_flow)));
    AddBundleDraft(&drafts, "provenance/taint.jsonl", JsonLines(flow_lines),
                   "BoundedSelectiveTaint",
                   DescriptorAuthority(RecordAuthorities(recovery.value_flow)));
    AddBundleDraft(&drafts, "provenance/value-flow.jsonl", JsonLines(flow_lines),
                   "BoundedValueFlow",
                   DescriptorAuthority(RecordAuthorities(recovery.value_flow)));
    std::vector<std::string> resource_lines;
    for (const auto& resource : recovery.resources) resource_lines.push_back(ResourceJson(resource));
    AddBundleDraft(&drafts, "provenance/resource-provenance.jsonl", JsonLines(resource_lines),
                   "ResourceDecodeBoundary",
                   DescriptorAuthority(RecordAuthorities(recovery.resources)));
    AddBundleDraft(&drafts, "provenance/evidence-ledger.jsonl", JsonLines(graph_lines),
                   "SemanticEvidenceLedger", DescriptorAuthority(graph_authorities));

    AddBundleDraft(&drafts, "replay/semantic-replay/replay-index.json",
                   JsonObjectWithStatus("EvidenceBlockedReplayFixtureUnavailable"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "replay/oracle-contract.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"comparison\":\"SemanticEquivalence\","
                       "\"status\":\"EvidenceBlockedOracleUnavailable\","
                       "\"authority\":\"UNKNOWN\"}",
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);

    const bool coverage_verified = !recovery.coverage.empty() &&
        std::all_of(recovery.coverage.begin(), recovery.coverage.end(), [](const auto& metric) {
            return metric.denominator_authority == UltimateAuthority::Verified &&
                metric.status.find("EvidenceBlocked") == std::string::npos &&
                metric.status.find("Contradiction") == std::string::npos;
        });
    AddBundleDraft(&drafts, "coverage/recovery-coverage.json", CoverageJson(recovery),
                   "ExplicitDenominatorCoverage", coverage_verified ?
                       UltimateAuthority::Derived : UltimateAuthority::Unknown);
    std::vector<std::string> missing_lines;
    for (const auto& missing : recovery.missing_evidence) missing_lines.push_back(MissingJson(missing));
    const auto missing_document = "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
        ",\"items\":[" + JoinJsonValues(missing_lines) + "]}";
    AddBundleDraft(&drafts, "coverage/missing-evidence-queue.json", missing_document,
                   "ActiveLearningEvidenceNeeds", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "coverage/contradictions.jsonl", {},
                   "DeterministicContradictionLedger", UltimateAuthority::Unknown);

    const std::array<std::pair<UltimateAuthority, const char*>, 7> authority_files = {{
        {UltimateAuthority::Verified, "authority/verified.jsonl"},
        {UltimateAuthority::Derived, "authority/derived.jsonl"},
        {UltimateAuthority::Observed, "authority/observed.jsonl"},
        {UltimateAuthority::Hypothesis, "authority/hypothesis.jsonl"},
        {UltimateAuthority::Unknown, "authority/unknown.jsonl"},
        {UltimateAuthority::UnknownServerOnly, "authority/unknown-server-only.jsonl"},
        {UltimateAuthority::Rejected, "authority/rejected.jsonl"}
    }};
    for (const auto& [authority, path] : authority_files)
        AddBundleDraft(&drafts, path, AuthorityRecords(recovery, authority),
                       "AuthorityPartition", authority);

    const bool any_verified = std::any_of(recovery.content_entities.begin(),
        recovery.content_entities.end(), [](const auto& entity) {
            return entity.authority == UltimateAuthority::Verified;
        }) || std::any_of(recovery.packet_schemas.begin(), recovery.packet_schemas.end(),
            [](const auto& packet) { return packet.authority == UltimateAuthority::Verified; });
    const auto ready_status = any_verified ?
        "EvidenceCandidateRequiresImporterValidation" : "EvidenceBlockedNoVerifiedIntegrationData";
    AddBundleDraft(&drafts, "integration/server-ready.json", JsonObjectWithStatus(ready_status),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "integration/database-ready.json", JsonObjectWithStatus(ready_status),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "integration/migration-ready.json",
                   JsonObjectWithStatus("EvidenceBlockedImporterStagingRequired"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "integration/replay-ready.json",
                   JsonObjectWithStatus("EvidenceBlockedReplayValidationRequired"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "integration/blocked-items.json", missing_document,
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);

    AddBundleDraft(&drafts, "codex/recovery-contract.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"authorityPromotion\":\"DeterministicHardGate\","
                       "\"productionMutation\":false}",
                   "UltimateRecoveryContract", UltimateAuthority::Verified);
    AddBundleDraft(&drafts, "codex/server-gap-plan.json",
                   JsonObjectWithStatus("EvidenceBlockedImporterComparisonRequired"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "codex/database-gap-plan.json",
                   JsonObjectWithStatus("EvidenceBlockedImporterComparisonRequired"),
                   "UltimateRecoveryEngine", UltimateAuthority::Unknown);
    AddBundleDraft(&drafts, "codex/validation-plan.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"order\":[\"Unit\",\"Integration\",\"GoldenReplay\","
                       "\"Headless\",\"GoldenRegression\",\"OfficialClientLast\"]}",
                   "UltimateRecoveryContract", UltimateAuthority::Verified);
    AddBundleDraft(&drafts, "codex/CODEX-APPLY-DIRECTIVE.md",
                   "# God2 Ultimate Recovery Apply Contract\n\nValidate manifest hashes, schemas, "
                   "Client build and authority before staging. Never mutate production from the "
                   "capture tool. UNKNOWN, HYPOTHESIS and UNKNOWN_SERVER_ONLY are not integration-ready.\n",
                   "UltimateRecoveryContract", UltimateAuthority::Verified,
                   "god2-ultimate-codex-directive-v1");
    AddBundleDraft(&drafts, "raw/README.json",
                   "{\"schemaVersion\":" + JsonQuoted(kUltimateRecoverySchema) +
                       ",\"status\":\"NoUnnecessaryRawEvidenceExported\"," 
                       "\"credentialsIncluded\":false,\"absolutePathsIncluded\":false,"
                       "\"credentialPersistenceStatus\":"
                       "\"VerifiedNoOriginalSensitiveSemanticPayloadPersisted\"}",
                   "SafetyPrivacyPolicy", UltimateAuthority::Verified);
    AddBundleDraft(&drafts, "manifest/integrity.json",
                   "{\"schemaVersion\":\"god2-ultimate-integrity-v1\","
                   "\"authority\":\"DERIVED\",\"status\":\"HashesRecordedInPackageManifest\"}",
                   "AuditedSha256", UltimateAuthority::Derived,
                   "god2-ultimate-integrity-v1");
    return drafts;
}

std::string ManifestJson(std::string_view package_id, std::string_view client_identity_status,
                         std::string_view recovery_attestation_status,
                         bool exact_binding_validated,
                         std::string_view computed_sha256,
                         std::string_view computed_version,
                         std::string_view computed_architecture,
                         const std::vector<UltimateBundleFile>& files) {
    std::ostringstream output;
    output << "{\"schemaVersion\":" << JsonQuoted(kUltimateManifestSchema)
           << ",\"packageId\":" << JsonQuoted(package_id)
           << ",\"clientBuild\":{\"executable\":" << JsonQuoted(kExpectedClientName)
           << ",\"architecture\":" << JsonQuoted(kExpectedClientArchitecture)
           << ",\"fileVersion\":" << JsonQuoted(kExpectedClientVersion)
           << ",\"sha256\":" << JsonQuoted(kExpectedClientSha256)
           << ",\"validationStatus\":" << JsonQuoted(client_identity_status)
           << ",\"exactBindingValidated\":" <<
                (exact_binding_validated ? "true" : "false")
           << ",\"recoveryAttestationStatus\":" <<
                JsonQuoted(recovery_attestation_status)
           << ",\"computedSha256\":" <<
                (computed_sha256.empty() ? "null" : JsonQuoted(computed_sha256))
           << ",\"computedFileVersion\":" <<
                (computed_version.empty() ? "null" : JsonQuoted(computed_version))
           << ",\"computedArchitecture\":" <<
                (computed_architecture.empty() ? "null" : JsonQuoted(computed_architecture))
           << '}';
    output << ",\"files\":[";
    for (std::size_t index = 0; index < files.size(); ++index) {
        if (index != 0U) output << ',';
        const auto& file = files[index];
        output << "{\"relativePath\":" << JsonQuoted(file.relative_path)
               << ",\"sizeBytes\":" << file.size_bytes
               << ",\"sha256\":" << JsonQuoted(file.sha256)
               << ",\"provenance\":" << JsonQuoted(file.provenance)
               << ",\"authority\":" << JsonQuoted(ToString(file.authority))
               << ",\"schemaVersion\":" << JsonQuoted(file.schema_version) << '}';
    }
    output << "]}";
    return output.str();
}

bool ComputeAndValidateClientIdentity(const fs::path& executable,
                                      UltimateBundleResult* result) {
    std::error_code error;
    const auto absolute = fs::absolute(executable, error).lexically_normal();
    if (error) {
        result->error = "EvidenceBlockedClientExecutableUnavailable";
        return false;
    }
    UltimateScopedHandle pinned(CreateFileW(absolute.c_str(),
        GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr));
    FILE_ATTRIBUTE_TAG_INFO tag{};
    BY_HANDLE_FILE_INFORMATION identity_before{};
    const auto final_before = pinned ? UltimateFinalPath(pinned.value) :
        std::optional<fs::path>{};
    if (!pinned || !final_before ||
        !GetFileInformationByHandleEx(pinned.value, FileAttributeTagInfo,
            &tag, sizeof(tag)) ||
        !GetFileInformationByHandle(pinned.value, &identity_before) ||
        (tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        FoldUltimatePath(final_before->lexically_normal().wstring()) !=
            FoldUltimatePath(absolute.wstring())) {
        result->error = "EvidenceBlockedClientExecutableIdentityUnsafe";
        return false;
    }
    const auto name = WideToUtf8(absolute.filename().wstring());
    const auto hash = UltimateSha256ByHandle(pinned.value);
    result->actual_client_sha256 = hash.value_or("");
    // GetFileVersionInfo is path-based on supported Windows versions.  The
    // pinned handle deliberately denies write/delete sharing for the whole
    // call, and the path is rebound to the same file-id immediately after it.
    result->actual_client_version = FileVersion(absolute);
    if (!ReadPinnedPeArchitecture(pinned.value, &result->actual_client_architecture)) {
        result->actual_client_architecture = "Unknown";
    }
    const auto hash_after = UltimateSha256ByHandle(pinned.value);
    BY_HANDLE_FILE_INFORMATION identity_after{};
    UltimateScopedHandle rebound(CreateFileW(absolute.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    BY_HANDLE_FILE_INFORMATION rebound_identity{};
    FILE_ATTRIBUTE_TAG_INFO rebound_tag{};
    const auto rebound_final = rebound ? UltimateFinalPath(rebound.value) :
        std::optional<fs::path>{};
    const bool lifetime_valid = hash && hash_after && *hash == *hash_after &&
        GetFileInformationByHandle(pinned.value, &identity_after) != FALSE &&
        SameUltimateFileObject(identity_before, identity_after) &&
        UltimateHandleSize(identity_before) == UltimateHandleSize(identity_after) &&
        UltimateFinalPath(pinned.value) == final_before && rebound && rebound_final &&
        GetFileInformationByHandle(rebound.value, &rebound_identity) != FALSE &&
        GetFileInformationByHandleEx(rebound.value, FileAttributeTagInfo,
            &rebound_tag, sizeof(rebound_tag)) != FALSE &&
        (rebound_tag.FileAttributes &
            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0 &&
        SameUltimateFileObject(identity_before, rebound_identity) &&
        UltimateHandleSize(identity_before) == UltimateHandleSize(rebound_identity) &&
        *rebound_final == *final_before;
    if (!lifetime_valid) {
        result->client_identity_status = "EvidenceBlockedClientIdentityChangedDuringValidation";
        result->error = result->client_identity_status;
        return false;
    }
    const bool matches = EqualAsciiInsensitive(name, kExpectedClientName) && hash &&
        EqualAsciiInsensitive(*hash, kExpectedClientSha256) &&
        result->actual_client_version == kExpectedClientVersion &&
        result->actual_client_architecture == kExpectedClientArchitecture;
    result->client_identity_status = matches ?
        "ExactClientIdentityComputedAndValidated" : "EvidenceBlockedClientBuildMismatch";
    if (!matches) result->error = "EvidenceBlockedClientBuildMismatch";
    return matches;
}

bool WriteBundleDrafts(const PinnedUltimateDirectory& root,
                       std::vector<BundleDraft>* drafts,
                       std::vector<UltimateBundleFile>* files, std::string* error) {
    std::set<std::string> unique;
    for (auto& draft : *drafts) {
        const auto folded_content = LowerAscii(draft.content);
        const bool forbidden_sensitive_material =
            folded_content.find("canary") != std::string::npos ||
            folded_content.find("\"password\":") != std::string::npos ||
            folded_content.find("password=") != std::string::npos ||
            folded_content.find("decodedprefixsafe") != std::string::npos ||
            folded_content.find("\"key256\":") != std::string::npos ||
            folded_content.find("key256=") != std::string::npos ||
            folded_content.find("rawauthenticationpayload") != std::string::npos;
        if (forbidden_sensitive_material) {
            *error = "sensitive semantic material rejected before bundle staging";
            return false;
        }
        fs::path relative(Utf8ToWide(draft.metadata.relative_path));
        if (!IsSafeArtifactRelativePath(relative) ||
            !unique.insert(draft.metadata.relative_path).second) {
            *error = "unsafe or duplicate bundle relative path";
            return false;
        }
        const auto target = root.expected_path / relative;
        PinnedUltimateFile written;
        if (!WriteContainedUltimateFile(root, target, draft.content, &written)) {
            *error = "failed to write bundle artifact: " + draft.metadata.relative_path;
            return false;
        }
        draft.metadata.size_bytes = written.size;
        draft.metadata.sha256 = written.sha256;
        files->push_back(draft.metadata);
    }
    std::sort(files->begin(), files->end(), [](const auto& left, const auto& right) {
        return left.relative_path < right.relative_path;
    });
    return true;
}

struct PinnedUltimateInventory {
    std::vector<PinnedUltimateDirectory> directories;
    std::vector<PinnedUltimateFile> files;
    std::set<std::string> relative_paths;
};

bool PinUltimateInventory(const PinnedUltimateDirectory& root,
                          const std::vector<UltimateBundleFile>& metadata,
                          PinnedUltimateInventory* inventory, std::string* error) {
    inventory->directories.clear();
    inventory->files.clear();
    inventory->relative_paths.clear();
    std::map<std::string, const UltimateBundleFile*> expected;
    for (const auto& file : metadata) expected.emplace(file.relative_path, &file);
    expected.emplace("manifest/package-manifest.json", nullptr);
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(root.expected_path,
            fs::directory_options::skip_permission_denied, enumerate_error), end;
         iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
        const auto relative_path = iterator->path().lexically_relative(root.expected_path);
        if (!IsSafeArtifactRelativePath(relative_path) ||
            !UltimatePathIsReparseFree(root.expected_path, iterator->path())) {
            if (error != nullptr) *error = "staging inventory contains an unsafe path";
            return false;
        }
        auto relative = WideToUtf8(relative_path.generic_wstring());
        std::replace(relative.begin(), relative.end(), '\\', '/');
        const DWORD attributes = GetFileAttributesW(iterator->path().c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES ||
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
            if (error != nullptr) *error = "staging inventory contains a reparse point";
            return false;
        }
        if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
            auto directory = PinUltimateDirectory(iterator->path());
            if (!directory || !UltimatePathContained(root.final_path,
                    directory->final_path)) {
                if (error != nullptr) *error = "staging subdirectory pin failed";
                return false;
            }
            inventory->directories.push_back(std::move(*directory));
            continue;
        }
        auto file = PinUltimateFile(root, iterator->path());
        const auto found = expected.find(relative);
        if (!file || found == expected.end() ||
            !inventory->relative_paths.insert(relative).second ||
            (found->second != nullptr &&
             (file->size != found->second->size_bytes ||
              !EqualAsciiInsensitive(file->sha256, found->second->sha256)))) {
            if (error != nullptr) *error = "staging file identity/manifest mismatch: " + relative;
            return false;
        }
        inventory->files.push_back(std::move(*file));
    }
    if (enumerate_error || inventory->relative_paths.size() != expected.size() ||
        !std::all_of(expected.begin(), expected.end(), [&](const auto& pair) {
            return inventory->relative_paths.contains(pair.first);
        })) {
        if (error != nullptr) *error = enumerate_error ? enumerate_error.message() :
            "staging inventory is not exact";
        return false;
    }
    return ValidatePinnedUltimateDirectory(root);
}

bool ValidatePinnedUltimateInventory(const PinnedUltimateDirectory& root,
                                     const PinnedUltimateInventory& inventory,
                                     std::string* error) {
    if (!ValidatePinnedUltimateDirectory(root) ||
        !std::all_of(inventory.directories.begin(), inventory.directories.end(),
            [](const auto& directory) {
                return ValidatePinnedUltimateDirectory(directory);
            }) ||
        !std::all_of(inventory.files.begin(), inventory.files.end(),
            [&](const auto& file) { return ValidatePinnedUltimateFile(root, file); })) {
        if (error != nullptr) *error = "pinned staging object changed during ZIP operation";
        return false;
    }
    std::set<std::string> observed;
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(root.expected_path,
            fs::directory_options::skip_permission_denied, enumerate_error), end;
         iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
        const DWORD attributes = GetFileAttributesW(iterator->path().c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES ||
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
            if (error != nullptr) *error = "staging tree changed into a reparse path";
            return false;
        }
        if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
        auto relative = WideToUtf8(
            iterator->path().lexically_relative(root.expected_path).generic_wstring());
        std::replace(relative.begin(), relative.end(), '\\', '/');
        observed.insert(std::move(relative));
    }
    if (enumerate_error || observed != inventory.relative_paths) {
        if (error != nullptr) *error = enumerate_error ? enumerate_error.message() :
            "staging inventory changed while ZIP was created";
        return false;
    }
    return true;
}

} // namespace

UltimateBundleResult WriteUltimateRecoveryBundle(const UltimateRecoveryResult& recovery,
                                                  const UltimateBundleOptions& options) noexcept {
    UltimateBundleResult result;
    try {
        const auto package_id = SanitizeComponent(options.package_id);
        if (package_id.empty() || package_id != options.package_id) {
            result.error = "invalid packageId; only ASCII letters, digits, dot, dash and underscore are allowed";
            return result;
        }
        if (options.output_directory.empty()) {
            result.error = "output directory is required";
            return result;
        }
        const bool client_identity_validated = !options.client_executable_path.empty() &&
            ComputeAndValidateClientIdentity(options.client_executable_path, &result);
        if (!options.client_executable_path.empty() && !client_identity_validated) {
            result.blockers.push_back(result.error);
            return result;
        }
        const bool exact_binding_validated = client_identity_validated &&
            recovery.exact_client_build_attested;
        const std::string binding_status = exact_binding_validated ?
            "ExactClientIdentityComputedAndValidated" :
            (client_identity_validated ? recovery.client_build_attestation_status :
                "EvidenceBlockedClientIdentityNotComputed");
        result.client_identity_status = binding_status;
        std::error_code directory_error;
        fs::create_directories(options.output_directory, directory_error);
        if (directory_error || !fs::is_directory(options.output_directory)) {
            result.error = "unable to create output directory";
            return result;
        }
        const std::string base_name = "God2UltimateRecovery_" + TimestampComponent() + '_' + package_id;
        // Staging is intentionally short and opaque.  The public ZIP keeps
        // the required descriptive name, while deep bundle subdirectories do
        // not consume the Windows path budget before audited hashing.
        const std::string staging_name = ".god2-ultimate-" + SanitizeComponent(NewId()) +
            ".staging";
        result.staging_directory = options.output_directory / Utf8ToWide(staging_name);
        result.zip_path = options.output_directory / Utf8ToWide(base_name + ".zip");
        if (fs::exists(result.staging_directory) || fs::exists(result.zip_path)) {
            result.error = "bundle target already exists; refusing to overwrite";
            return result;
        }
        if (!fs::create_directory(result.staging_directory, directory_error) || directory_error) {
            result.error = "unable to create bundle staging directory";
            return result;
        }
        const auto canonical_output = fs::weakly_canonical(options.output_directory, directory_error);
        if (directory_error) {
            result.error = "unable to canonicalize output directory";
            result.blockers.push_back("PartialStagingPreserved");
            return result;
        }
        const auto canonical_staging = fs::weakly_canonical(result.staging_directory, directory_error);
        if (directory_error || canonical_staging.parent_path() != canonical_output) {
            result.error = "staging path escaped output directory";
            result.blockers.push_back("PartialStagingPreserved");
            return result;
        }
        // Keep all subsequent Win32 I/O absolute so the shared extended-path
        // helper can safely cross MAX_PATH even when the caller supplied a
        // relative artifact root.
        result.staging_directory = canonical_staging;
        result.zip_path = canonical_output / Utf8ToWide(base_name + ".zip");
        auto output_pin = PinUltimateDirectory(canonical_output);
        auto staging_pin = PinUltimateDirectory(canonical_staging);
        if (!output_pin || !staging_pin ||
            !ValidatePinnedUltimateDirectory(*output_pin) ||
            !ValidatePinnedUltimateDirectory(*staging_pin) ||
            FoldUltimatePath(staging_pin->final_path.parent_path().wstring()) !=
                FoldUltimatePath(output_pin->final_path.wstring())) {
            result.error = "bundle staging directory identity/containment pin failed";
            result.blockers.push_back("PartialStagingPreserved");
            return result;
        }
        auto drafts = BuildBundleDrafts(recovery, binding_status, exact_binding_validated,
            result.actual_client_sha256, result.actual_client_version,
            result.actual_client_architecture);
        if (!WriteBundleDrafts(*staging_pin, &drafts, &result.files, &result.error)) {
            result.blockers.push_back("PartialStagingPreserved");
            return result;
        }
        result.manifest_path = result.staging_directory / L"manifest" / L"package-manifest.json";
        const auto manifest = ManifestJson(package_id, binding_status,
            recovery.client_build_attestation_status, exact_binding_validated,
            result.actual_client_sha256, result.actual_client_version,
            result.actual_client_architecture, result.files);
        PinnedUltimateFile manifest_file;
        if (!WriteContainedUltimateFile(*staging_pin, result.manifest_path,
                manifest, &manifest_file)) {
            result.error = "failed to write package manifest";
            result.blockers.push_back("PartialStagingPreserved");
            return result;
        }
        // Release the single manifest pin only after an exact, complete
        // inventory has pinned every staging directory and file.  Those
        // handles deny write/delete sharing for the whole ZIP transaction.
        PinnedUltimateInventory staging_inventory;
        if (!PinUltimateInventory(*staging_pin, result.files,
                &staging_inventory, &result.error)) {
            result.blockers.push_back("PartialStagingPreserved");
            return result;
        }
        if (options.create_zip) {
            std::string zip_error;
            if (!ValidatePinnedUltimateInventory(*staging_pin, staging_inventory, &zip_error) ||
                !CreatePortableStoredZip(result.staging_directory, result.zip_path, &zip_error) ||
                !ValidatePinnedUltimateInventory(*staging_pin, staging_inventory, &zip_error) ||
                !ValidatePortableStoredZip(result.staging_directory, result.zip_path, &zip_error) ||
                !ValidatePinnedUltimateInventory(*staging_pin, staging_inventory, &zip_error)) {
                result.error = "bundle ZIP validation failed: " + zip_error;
                result.blockers.push_back("PartialStagingPreserved");
                return result;
            }
            auto zip_file = PinUltimateFile(*output_pin, result.zip_path);
            if (!zip_file || !ValidatePinnedUltimateFile(*output_pin, *zip_file)) {
                result.error = "bundle ZIP SHA-256 failed";
                result.blockers.push_back("PartialStagingPreserved");
                return result;
            }
            result.zip_sha256 = zip_file->sha256;
        }
        if (!client_identity_validated)
            result.blockers.push_back("EvidenceBlockedClientIdentityNotComputed");
        else if (!recovery.exact_client_build_attested)
            result.blockers.push_back(recovery.client_build_attestation_status);
        if (recovery.partial_recovery || recovery.semantic_evidence_incomplete)
            result.blockers.push_back("SemanticEvidenceIncomplete");
        result.success = true;
    } catch (...) {
        result.error = "UltimateBundleWriterFailure";
        if (!result.staging_directory.empty()) result.blockers.push_back("PartialStagingPreserved");
    }
    return result;
}

namespace {

class SelfTestFailure final : public std::runtime_error {
public:
    explicit SelfTestFailure(const char* message) : std::runtime_error(message) {}
    explicit SelfTestFailure(const std::string& message) : std::runtime_error(message) {}
};

void Require(bool condition, const char* message) {
    if (!condition) throw SelfTestFailure(message);
}

God2SemanticEventV2 FixtureEvent(UltimateSemanticEventType type, std::uint64_t sequence,
                                 std::string payload,
                                 UltimateAuthority authority = UltimateAuthority::Observed) {
    God2SemanticEventV2 event;
    event.event_type = type;
    event.event_id = "SELFTEST-SE-" + std::to_string(sequence);
    event.sequence = sequence;
    event.timestamp = "2026-01-01T00:00:00.000Z";
    event.thread_id = 7U;
    event.process_id = 42U;
    event.session_id = "DeterministicSyntheticContractFixture";
    event.client_build_id = std::string(kExpectedClientSha256);
    event.module_id = "God2_opt.exe";
    event.rva = 0x78D70U + sequence;
    event.callsite_rva = 0x78A48U;
    event.parent_event_id = "SELFTEST-PARENT";
    event.context_id = "SELFTEST-CONTEXT";
    event.action_id = "SELFTEST-ACTION";
    event.authority_hint = authority;
    event.sensitive_mask_status = "NotSensitive";
    event.payload = std::move(payload);
    return event;
}

void ApplyExactFixtureBinding(God2SemanticEventV2* event,
                              std::string_view probe_category = "Mutation") {
    event->evidence_binding = {
        {"SchemaId", "God2SemanticEvent"},
        {"SemanticEventId", event->event_id},
        {"ProtocolFrameId", "SELFTEST-FRAME"},
        {"HookInvocationId", "SELFTEST-HOOK"},
        {"ProbeId", "UltimateRecovery.SelfTest"},
        {"ProbeCategory", std::string(probe_category)},
        {"ProbeContractVersion", "god2-semantic-probe-v2"},
        {"ClientSha256Expected", std::string(kExpectedClientSha256)},
        {"BuildBindingStatus", "ExactInstructionIdentityVerified"},
        {"EvidenceBasis", "ExactBuildDeterministicContractFixture"},
        {"EvidenceLevel", "Recovered"}
    };
}

UltimateRecoveryInput BuildRecoveryFixture() {
    UltimateRecoveryInput input;
    input.session_id = "DeterministicSyntheticContractFixture";
    std::uint64_t sequence = 1U;
    auto add = [&](UltimateSemanticEventType type, std::string payload,
                   UltimateAuthority authority = UltimateAuthority::Observed) ->
        God2SemanticEventV2& {
        input.semantic_events.push_back(FixtureEvent(type, sequence++, std::move(payload), authority));
        return input.semantic_events.back();
    };
    add(UltimateSemanticEventType::PacketBoundary,
        "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
        "\"Subsystem\":\"Battle\",\"ActionFamily\":\"BattleState\","
        "\"FrameLength\":\"12\",\"FrameLengthRule\":\"UInt16LEInclusive\","
        "\"EncryptionBoundary\":\"PostDecrypt\"}");
    add(UltimateSemanticEventType::ParserRead,
        "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
        "\"Subsystem\":\"Battle\",\"PacketOffset\":\"4\",\"Width\":\"4\","
        "\"ValueType\":\"uint32\",\"Endian\":\"Little\","
        "\"SemanticCandidate\":\"MonsterId\",\"TypedSource\":\"true\"}");
    add(UltimateSemanticEventType::ParserRead,
        "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
        "\"Subsystem\":\"Battle\",\"PacketOffset\":\"4\",\"Width\":\"4\","
        "\"ValueType\":\"uint32\",\"Endian\":\"Little\","
        "\"SemanticCandidate\":\"MonsterId\",\"TypedSource\":\"true\"}");
    add(UltimateSemanticEventType::SerializerWrite,
        "{\"Direction\":\"ClientToServer\",\"Opcode\":\"0x31\","
        "\"Subsystem\":\"Battle\",\"PacketOffset\":\"2\",\"Width\":\"2\","
        "\"ValueType\":\"uint16\",\"Endian\":\"Little\"}");
    auto& allocation = add(UltimateSemanticEventType::ObjectAllocated,
        "{\"AllocationSize\":\"128\",\"VTable\":\"God2_opt.exe+0x22000\","
        "\"ConstructorPath\":\"Ctor:MonsterRuntime\",\"Factory\":\"MonsterFactory\"}");
    allocation.object_token = "SELFTEST-OBJECT-4102";
    auto& base_object = add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"Monster\",\"StableTemplateId\":\"4102\","
        "\"Property\":\"BaseHP\",\"RawValue\":\"600\","
        "\"NormalizedValue\":\"600\",\"ValueLayer\":\"Base\","
        "\"ExactBuildBinding\":\"true\",\"TypedSource\":\"true\","
        "\"StableContext\":\"true\",\"CausalPath\":\"true\","
        "\"VerifiedConsumerOrMutation\":\"true\"}", UltimateAuthority::Observed);
    base_object.object_token = "SELFTEST-OBJECT-4102";
    auto& runtime_object = add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"Monster\",\"StableTemplateId\":\"4102\","
        "\"Property\":\"RuntimeMaxHP\",\"RawValue\":\"750\","
        "\"NormalizedValue\":\"750\",\"ValueLayer\":\"Runtime\"}");
    runtime_object.object_token = "SELFTEST-OBJECT-4102";
    add(UltimateSemanticEventType::RegistryEnumerated,
        "{\"RegistryName\":\"MonsterTemplateRegistry\",\"EntityFamily\":\"Monster\","
        "\"RecordCount\":\"1\",\"ReadOnly\":\"true\","
        "\"LookupConsumer\":\"BattleHandler\"}");
    add(UltimateSemanticEventType::ResourceDeserialized,
        "{\"RelativePath\":\"data/monster.dat\","
        "\"FileSha256\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\","
        "\"DecodedBufferSha256\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\","
        "\"SourceOffset\":\"64\",\"DecodeRVA\":\"0x1010\","
        "\"DeserializeRVA\":\"0x2020\",\"Destination\":\"MonsterTemplateRegistry\","
        "\"RecordCount\":\"1\",\"Provenance\":\"ClientDecodedBoundary\"}");
    for (int duplicate = 0; duplicate < 2; ++duplicate) {
        auto& mutation = add(UltimateSemanticEventType::StateMutation,
            "{\"ObjectIdentity\":\"MonsterRuntime:77\",\"PropertyCandidate\":\"HP\","
            "\"Type\":\"uint32\",\"BeforeValue\":\"600\","
            "\"InputValue\":\"-40\",\"AfterValue\":\"560\","
            "\"TriggerPacket\":\"ServerToClient:0x41\","
            "\"TriggerHandler\":\"BattleHandler\",\"WriterRVA\":\"0x3000\","
            "\"Consumers\":\"BattleUI;DeathCheck\",\"MachineId\":\"BattleFSM\","
            "\"PreviousState\":\"TurnActive\",\"NextState\":\"DamageApplied\","
            "\"ReplayConsistent\":\"true\",\"ExactBuildBinding\":\"true\","
            "\"TypedSource\":\"true\",\"StableContext\":\"true\","
            "\"CausalPath\":\"true\",\"VerifiedConsumerOrMutation\":\"true\"}",
            UltimateAuthority::Verified);
        mutation.object_token = "SELFTEST-OBJECT-77";
    }
    auto& seed = add(UltimateSemanticEventType::TaintSeed,
        "{\"Operation\":\"PacketRange\",\"PropagationHops\":\"0\"}");
    seed.source_token = "Packet:0x41:+4";
    seed.value_token = "Value:MonsterId";
    auto& propagation = add(UltimateSemanticEventType::TaintPropagation,
        "{\"Operation\":\"ManagerLookup\",\"PropagationHops\":\"1\"}");
    propagation.source_token = "Value:MonsterId";
    propagation.value_token = "Object:MonsterTemplate:4102";
    auto& flow = add(UltimateSemanticEventType::ValueFlow,
        "{\"Operation\":\"PropertyWrite\",\"PropagationHops\":\"2\"}");
    flow.source_token = "Object:MonsterTemplate:4102";
    flow.value_token = "MonsterRuntime:77.TemplateId";
    add(UltimateSemanticEventType::FormulaOperand,
        "{\"FormulaId\":\"DamageFormulaCandidate1\",\"FormulaCategory\":\"DamageFormula\","
        "\"Expression\":\"Attack-Defense\",\"Operand\":\"Attack;Defense\"}",
        UltimateAuthority::Hypothesis);
    add(UltimateSemanticEventType::FormulaResult,
        "{\"FormulaId\":\"DamageFormulaCandidate1\",\"FormulaCategory\":\"DamageFormula\","
        "\"Expression\":\"Attack-Defense\",\"Result\":\"40\"}",
        UltimateAuthority::Hypothesis);
    add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"MonsterDrop\",\"StableTemplateId\":\"4102-221\","
        "\"Property\":\"ObservedRate\",\"RawValue\":\"0.12\","
        "\"NormalizedValue\":\"0.12\",\"ValueLayer\":\"Observed\"}");
    add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"Quest\",\"StableTemplateId\":\"10521\","
        "\"Property\":\"ObjectiveType\",\"RawValue\":\"KillMonster\","
        "\"NormalizedValue\":\"KillMonster\",\"ValueLayer\":\"Base\"}");
    add(UltimateSemanticEventType::StateMutation,
        "{\"ObjectIdentity\":\"QuestRuntime:10521\","
        "\"PropertyCandidate\":\"QuestState\",\"Type\":\"enum\","
        "\"BeforeValue\":\"Accepted\",\"InputValue\":\"MonsterKilled\","
        "\"AfterValue\":\"ObjectiveComplete\",\"TriggerAction\":\"NormalGameplay\","
        "\"WriterRVA\":\"0x4000\",\"MachineId\":\"QuestFSM\","
        "\"PreviousState\":\"Accepted\",\"NextState\":\"ObjectiveComplete\","
        "\"ReplayConsistent\":\"true\"}");
    add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"Map\",\"StableTemplateId\":\"19\","
        "\"Property\":\"MapName\",\"RawValue\":\"TestMap\","
        "\"NormalizedValue\":\"TestMap\",\"ValueLayer\":\"Base\"}");
    add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"Portal\",\"StableTemplateId\":\"88\","
        "\"Property\":\"SourceMap\",\"RawValue\":\"19\","
        "\"NormalizedValue\":\"19\",\"ValueLayer\":\"Base\"}");
    add(UltimateSemanticEventType::ObjectResolved,
        "{\"EntityFamily\":\"Portal\",\"StableTemplateId\":\"88\","
        "\"Property\":\"TargetMap\",\"RawValue\":\"20\","
        "\"NormalizedValue\":\"20\",\"ValueLayer\":\"Base\"}");
    input.declared_denominators = {
        {"Protocol", 2U}, {"Monster", 1U}, {"NPC", 0U}, {"Map", 1U},
        {"Portal", 1U}, {"Quest", 1U}, {"Item", 0U}, {"Skill", 0U},
        {"DropRelation", 1U}, {"ExactRate", 1U}, {"Formula", 1U},
        {"StateMachine", 2U}, {"ObjectResolver", 5U}, {"Mutation", 2U}
    };
    for (const auto& [domain, ignored] : input.declared_denominators) {
        (void)ignored;
        input.declared_denominator_authorities[domain] = UltimateAuthority::Verified;
    }
    input.ml_samples = {
        {"Function:ParserA", {0.0, 0.1, 0.0, 1.0}, std::string("Parser")},
        {"Function:ParserB", {0.1, 0.0, 0.1, 0.9}, std::nullopt},
        {"Function:Registry", {4.0, 4.2, 3.9, 0.0}, std::string("Registry")},
        {"Function:Outlier", {20.0, -12.0, 17.0, 9.0}, std::nullopt}
    };
    input.ml_sequences = {
        {"Sequence:A", {"Login", "Character", "World"}},
        {"Sequence:B", {"Login", "Character", "Battle"}},
        {"Sequence:C", {"Unknown", "Rare", "Transition"}}
    };
    return input;
}

bool ContainsSimplifiedProductionCharacter(std::string_view value) {
    // Targeted release guard, not a language detector.  Raw evidence is not
    // passed through this production-text scanner.
    static constexpr std::array<std::string_view, 22> forbidden = {
        "简", "体", "后", "里", "发", "台", "网", "务", "数", "据", "库",
        "证", "验", "态", "图", "写", "读", "买", "卖", "宠", "务", "关"
    };
    return std::any_of(forbidden.begin(), forbidden.end(), [&](std::string_view token) {
        return value.find(token) != std::string_view::npos;
    });
}

bool ValidateContentReferences(const std::vector<UltimateContentEntity>& entities,
                               std::vector<std::string>* orphans) {
    std::set<std::string> identities;
    for (const auto& entity : entities) identities.insert(entity.canonical_identity);
    for (const auto& entity : entities) {
        for (const auto& [property, value] : entity.normalized_values) {
            if (property.size() < 8U || property.substr(property.size() - 8U) != "Identity")
                continue;
            if (!value.empty() && identities.find(value) == identities.end())
                orphans->push_back(entity.canonical_identity + ':' + property + "->" + value);
        }
    }
    return orphans->empty();
}

bool EquivalenceGate(const std::vector<UltimateMlPrediction>& cpu_reference,
                     const std::optional<std::vector<UltimateMlPrediction>>& accelerated) {
    if (!accelerated || accelerated->size() != cpu_reference.size()) return false;
    for (std::size_t index = 0; index < cpu_reference.size(); ++index) {
        if (cpu_reference[index].sample_id != (*accelerated)[index].sample_id ||
            cpu_reference[index].cluster != (*accelerated)[index].cluster ||
            cpu_reference[index].embedding.size() != (*accelerated)[index].embedding.size())
            return false;
        for (std::size_t dimension = 0;
             dimension < cpu_reference[index].embedding.size(); ++dimension) {
            if (std::abs(cpu_reference[index].embedding[dimension] -
                         (*accelerated)[index].embedding[dimension]) > 1e-10) return false;
        }
    }
    return true;
}

struct SelfTestRecord {
    std::string name;
    bool passed = false;
    std::string detail;
};

struct SemanticEventMatrixRow {
    std::size_t index = 0;
    std::string event_type;
    bool fixture_producer = false;
    bool runtime_producer = false;
    bool blocked_capability = false;
    bool reader_accepted = false;
    bool roundtrip_passed = false;
    bool corruption_rejected = false;
    bool version_rejected = false;
    bool v1_compatible = false;
    bool dispatch_passed = false;
    std::string dispatch_sink;
    std::string promotion_policy;
    bool no_promotion_enforced = false;
    bool native_wire_reader_accepted = false;
    bool native_wire_dispatch_passed = false;
};

struct SemanticEventMatrixReport {
    std::vector<SemanticEventMatrixRow> rows;
    bool native_wire_bound = false;
    bool native_wire_verified = false;
    std::string native_wire_input_sha256;
    std::string native_wire_event_type_digest;
    std::size_t native_wire_input_line_count = 0;
};

const char* SemanticDispatchSinkName(UltimateSemanticDispatchSink sink) noexcept {
    switch (sink) {
    case UltimateSemanticDispatchSink::ProtocolRecovery: return "ProtocolRecovery";
    case UltimateSemanticDispatchSink::ObjectRecovery: return "ObjectRecovery";
    case UltimateSemanticDispatchSink::RegistryRecovery: return "RegistryRecovery";
    case UltimateSemanticDispatchSink::ResourceRecovery: return "ResourceRecovery";
    case UltimateSemanticDispatchSink::MutationRecovery: return "MutationRecovery";
    case UltimateSemanticDispatchSink::ValueProvenanceRecovery: return "ValueProvenanceRecovery";
    case UltimateSemanticDispatchSink::FormulaRecovery: return "FormulaRecovery";
    case UltimateSemanticDispatchSink::CandidateLedger: return "CandidateLedger";
    case UltimateSemanticDispatchSink::ContentRecovery: return "ContentRecovery";
    case UltimateSemanticDispatchSink::SnapshotRecovery: return "SnapshotRecovery";
    case UltimateSemanticDispatchSink::DiagnosticLedger: return "DiagnosticLedger";
    }
    return "Unknown";
}

const char* SemanticPromotionPolicyName(
    UltimateSemanticPromotionPolicy policy) noexcept {
    return policy == UltimateSemanticPromotionPolicy::NoPromotion ?
        "NoPromotion" : "StandardEvidenceGates";
}

struct ImporterGateResult {
    bool attempted = false;
    bool passed = false;
    DWORD exit_code = ERROR_GEN_FAILURE;
    fs::path output_root;
    std::string status = "EvidenceBlockedImporterUnavailable";
    std::string output;
};

std::optional<fs::path> FindDotnetForImporter() {
    std::vector<wchar_t> buffer;
    try {
        buffer.resize(32'768U);
    } catch (const std::bad_alloc&) {
        return std::nullopt;
    }
    const DWORD length = SearchPathW(nullptr, L"dotnet.exe", nullptr,
                                     static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
    if (length != 0U && length < buffer.size()) return fs::path(buffer.data());
    const fs::path standard = L"C:\\Program Files\\dotnet\\dotnet.exe";
    std::error_code error;
    if (fs::is_regular_file(standard, error) && !error) return standard;
    return std::nullopt;
}

std::optional<fs::path> FindImporterRepositoryRoot(const fs::path& artifacts_path) {
    std::error_code error;
    auto current = fs::absolute(artifacts_path, error);
    if (error) return std::nullopt;
    if (!fs::is_directory(current, error)) current = current.parent_path();
    for (std::size_t depth = 0; depth < 16U && !current.empty(); ++depth) {
        const auto project = current / L"tools" / L"God2.RecoveredDatabaseImporter" /
            L"God2.RecoveredDatabaseImporter.csproj";
        if (fs::is_regular_file(project, error) && !error) return current;
        current = current.parent_path();
    }
    return std::nullopt;
}

std::wstring QuoteArgument(const fs::path& value) {
    std::wstring text = value.wstring();
    std::wstring escaped;
    escaped.reserve(text.size() + 2U);
    escaped.push_back(L'"');
    for (const wchar_t character : text) {
        if (character == L'"') escaped.push_back(L'\\');
        escaped.push_back(character);
    }
    escaped.push_back(L'"');
    return escaped;
}

ImporterGateResult RunRealImporterGate(const UltimateBundleResult& bundle,
                                       const fs::path& artifacts_path) {
    ImporterGateResult result;
    const auto dotnet = FindDotnetForImporter();
    const auto repository = FindImporterRepositoryRoot(artifacts_path);
    if (!dotnet || !repository || !bundle.success || bundle.zip_path.empty()) return result;
    const auto project = *repository / L"tools" / L"God2.RecoveredDatabaseImporter" /
        L"God2.RecoveredDatabaseImporter.csproj";
    result.output_root = fs::absolute(artifacts_path) /
        Utf8ToWide("importer-gate-" + SanitizeComponent(NewId()));
    const std::wstring arguments = L"run --project " + QuoteArgument(project) +
        L" --configuration Release --no-launch-profile -- --bundle " +
        QuoteArgument(bundle.zip_path) + L" --repository-root " + QuoteArgument(*repository) +
        L" --output-root " + QuoteArgument(result.output_root);
    result.attempted = true;
    const auto process = RunProcess(*dotnet, arguments, 180'000U, true);
    result.exit_code = process.exit_code;
    result.output = process.output;
    Fields summary_fields;
    std::size_t inspected = 0;
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(result.output_root,
            fs::directory_options::skip_permission_denied, enumerate_error), end;
         iterator != end && !enumerate_error && inspected < 512U;
         iterator.increment(enumerate_error), ++inspected) {
        if (!iterator->is_regular_file(enumerate_error) ||
            iterator->path().filename() != L"importer-summary.json") continue;
        const auto summary = ReadUtf8File(iterator->path());
        std::string parse_error;
        if (summary && ParseFlatJson(*summary, summary_fields, &parse_error)) {
            result.output = *summary;
            break;
        }
    }
    std::string compact_process_output;
    compact_process_output.reserve(process.output.size());
    for (const unsigned char character : process.output) {
        if (std::isspace(character) == 0)
            compact_process_output.push_back(static_cast<char>(character));
    }
    const bool stdout_contract =
        compact_process_output.find("RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS") !=
            std::string::npos &&
        compact_process_output.find("\"productionDatabaseConnectionAttempted\":false") !=
            std::string::npos &&
        compact_process_output.find("\"productionDatabaseMutationAttempted\":false") !=
            std::string::npos;
    const bool summary_contract =
        GetString(summary_fields, "status") == "RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS" &&
        GetBool(summary_fields, "productionDatabaseConnectionAttempted") == false &&
        GetBool(summary_fields, "productionDatabaseMutationAttempted") == false;
    const bool exact_identity = bundle.client_identity_status ==
        "ExactClientIdentityComputedAndValidated";
    const bool expected_identity_block = !exact_identity && process.started &&
        process.exit_code != 0U &&
        (process.output.find("build_identity.validation_status_not_trusted") !=
             std::string::npos ||
         process.output.find("Exact TARGET identity") != std::string::npos) &&
        !fs::exists(result.output_root / L"staging");
    result.passed = exact_identity ?
        (process.started && process.exit_code == 0U && (summary_contract || stdout_contract)) :
        expected_identity_block;
    result.status = result.passed ? (exact_identity ?
        "RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS" :
        "RECOVERY_BUNDLE_IMPORTER_EXPECTED_IDENTITY_BLOCK_PASS") :
        "EvidenceBlockedImporterDryRunFailed";
    return result;
}

template <typename Operation>
void RunSelfTest(std::vector<SelfTestRecord>* records, std::string name,
                 Operation&& operation) {
    SelfTestRecord record;
    record.name = std::move(name);
    try {
        operation();
        record.passed = true;
        record.detail = "ContractVerifiedWithDeterministicSyntheticFixture";
    } catch (const SelfTestFailure& failure) {
        record.detail = failure.what();
    } catch (...) {
        record.detail = "UnexpectedSelfTestFailure";
    }
    records->push_back(std::move(record));
}

std::string SelfTestReportJson(const std::vector<SelfTestRecord>& records,
                               const UltimateBundleResult& bundle,
                               const ImporterGateResult& importer,
                               const SemanticEventMatrixReport& matrix) {
    const auto passed = static_cast<std::size_t>(std::count_if(records.begin(), records.end(),
        [](const auto& record) { return record.passed; }));
    std::ostringstream output;
    output << "{\"schemaVersion\":\"god2-ultimate-selftest-report-v1\","
           << "\"evidenceSource\":\"DeterministicSyntheticContractFixture\","
           << "\"liveEvidenceClaimed\":false,\"total\":" << records.size()
           << ",\"passed\":" << passed
           << ",\"failed\":" << (records.size() - passed)
           << ",\"bundleZipSha256\":" << JsonQuoted(bundle.zip_sha256)
           << ",\"importerGateStatus\":" << JsonQuoted(importer.status)
           << ",\"importerExitCode\":" << importer.exit_code
           << ",\"semanticEventMatrix\":{\"schemaId\":"
           << JsonQuoted("God2SemanticEventMatrix")
           << ",\"schemaVersion\":2,\"rows\":[";
    for (std::size_t index = 0; index < matrix.rows.size(); ++index) {
        if (index != 0U) output << ',';
        const auto& row = matrix.rows[index];
        output << "{\"index\":" << row.index
               << ",\"eventType\":" << JsonQuoted(row.event_type)
               << ",\"fixtureProducer\":" << (row.fixture_producer ? "true" : "false")
               << ",\"producerImplemented\":" << (row.runtime_producer ? "true" : "false")
               << ",\"nativeSelfTestProducer\":true"
               << ",\"blockedCapability\":" << (row.blocked_capability ? "true" : "false")
               << ",\"readerAccepted\":" << (row.reader_accepted ? "true" : "false")
               << ",\"roundTripPassed\":" << (row.roundtrip_passed ? "true" : "false")
               << ",\"corruptionRejected\":" << (row.corruption_rejected ? "true" : "false")
               << ",\"versionRejected\":" << (row.version_rejected ? "true" : "false")
               << ",\"v1Compatible\":" << (row.v1_compatible ? "true" : "false")
               << ",\"dispatchPassed\":" << (row.dispatch_passed ? "true" : "false")
               << ",\"dispatchSink\":" << JsonQuoted(row.dispatch_sink)
               << ",\"promotionPolicy\":" << JsonQuoted(row.promotion_policy)
               << ",\"noPromotionEnforced\":"
               << (row.no_promotion_enforced ? "true" : "false")
               << ",\"nativeWireReaderAccepted\":"
               << (row.native_wire_reader_accepted ? "true" : "false")
               << ",\"nativeWireDispatchPassed\":"
               << (row.native_wire_dispatch_passed ? "true" : "false") << '}';
    }
    const auto count = [&](auto predicate) {
        return static_cast<std::size_t>(std::count_if(
            matrix.rows.begin(), matrix.rows.end(), predicate));
    };
    output << "],\"totals\":{\"fixtureProducerCount\":"
           << count([](const auto& row) { return row.fixture_producer; })
           << ",\"producerImplementedCount\":"
           << count([](const auto& row) { return row.runtime_producer; })
           << ",\"nativeSelfTestProducerCount\":" << matrix.rows.size()
           << ",\"blockedCapabilityCount\":"
           << count([](const auto& row) { return row.blocked_capability; })
           << ",\"readerAcceptedCount\":"
           << count([](const auto& row) { return row.reader_accepted; })
           << ",\"dispatchCount\":"
           << count([](const auto& row) { return row.dispatch_passed; })
           << ",\"roundTripCount\":"
           << count([](const auto& row) { return row.roundtrip_passed; })
           << ",\"corruptionRejectedCount\":"
           << count([](const auto& row) { return row.corruption_rejected; })
           << ",\"versionRejectedCount\":"
           << count([](const auto& row) { return row.version_rejected; })
           << ",\"v1CompatibleCount\":"
           << count([](const auto& row) { return row.v1_compatible; })
           << ",\"noPromotionEnforcedCount\":"
           << count([](const auto& row) { return row.no_promotion_enforced; })
           << ",\"nativeWireReaderAcceptedCount\":"
           << count([](const auto& row) { return row.native_wire_reader_accepted; })
           << ",\"nativeWireDispatchCount\":"
           << count([](const auto& row) { return row.native_wire_dispatch_passed; })
           << "},\"nativeWireBinding\":{\"bound\":"
           << (matrix.native_wire_bound ? "true" : "false")
           << ",\"verified\":"
           << (matrix.native_wire_verified ? "true" : "false")
           << ",\"inputSHA256\":"
           << JsonQuoted(matrix.native_wire_input_sha256)
           << ",\"eventTypeDigest\":"
           << JsonQuoted(matrix.native_wire_event_type_digest)
           << ",\"inputLineCount\":"
           << matrix.native_wire_input_line_count
           << "}},\"tests\":[";
    for (std::size_t index = 0; index < records.size(); ++index) {
        if (index != 0U) output << ',';
        output << "{\"name\":" << JsonQuoted(records[index].name)
               << ",\"status\":" << JsonQuoted(records[index].passed ? "PASS" : "FAIL")
               << ",\"detail\":" << JsonQuoted(records[index].detail) << '}';
    }
    output << "]}";
    return output.str();
}

struct NativeSemanticWireVerificationRow {
    std::size_t wire_line = 0;
    std::size_t definition_index = 0;
    std::string event_type;
    bool reader_accepted = false;
    bool source_token_bound = false;
    bool fixture_payload_bound = false;
    bool authority_fail_closed = false;
    bool dispatch_passed = false;
    bool round_trip_passed = false;
    bool corruption_rejected = false;
    bool version_rejected = false;
    bool promotion_policy_passed = false;
    std::string dispatch_sink;
    std::string promotion_policy;
};

struct NativeSemanticWireVerificationReport {
    fs::path input_path;
    std::string input_sha256;
    std::string event_type_digest;
    std::vector<NativeSemanticWireVerificationRow> rows;
    std::size_t input_line_count = 0;
    bool all_event_types_unique = false;
    bool passed = false;
    std::string error;
};

NativeSemanticWireVerificationReport VerifyNativeSemanticWire(
    const fs::path& input_path) {
    NativeSemanticWireVerificationReport report;
    // The report is portable evidence.  Bind content by SHA-256 and expose
    // only the stable artifact filename; never serialize a workstation path.
    report.input_path = input_path.filename();
    const auto content = ReadUtf8File(input_path);
    const auto input_hash = CalculateFileSha256(input_path);
    if (!content || !input_hash || content->empty() ||
        content->size() > 2U * 1024U * 1024U) {
        report.error = "NativeSemanticWireInputMissingInvalidOrOversized";
        return report;
    }
    report.input_sha256 = *input_hash;

    std::istringstream input(*content);
    std::string line;
    std::set<UltimateSemanticEventType> observed_types;
    std::vector<std::string> type_names;
    bool rows_valid = true;
    while (std::getline(input, line)) {
        ++report.input_line_count;
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line.size() > 64U * 1024U ||
            report.input_line_count > kUltimateSemanticEventTypeCount) {
            rows_valid = false;
            if (report.error.empty()) report.error =
                "NativeSemanticWireLineCountOrSizeInvalid";
            continue;
        }

        NativeSemanticWireVerificationRow row;
        row.wire_line = report.input_line_count;
        const auto read = ReadSemanticEvent(line);
        row.reader_accepted = read.success && !read.read_from_v1 &&
            read.event.schema_version == 2U;
        if (!row.reader_accepted) {
            rows_valid = false;
            if (report.error.empty()) report.error =
                "NativeSemanticWireReaderRejectedLine";
            report.rows.push_back(std::move(row));
            continue;
        }

        const auto* capability =
            GetUltimateSemanticEventCapability(read.event.event_type);
        row.event_type = ToString(read.event.event_type);
        const auto payload = PayloadFields(read.event);
        row.definition_index = static_cast<std::size_t>(
            FieldUint64(payload, "DefinitionIndex",
                        std::numeric_limits<std::uint64_t>::max()));
        row.source_token_bound =
            read.event.source_token == "NativeSemanticTypeFixture";
        row.fixture_payload_bound =
            row.definition_index < kUltimateSemanticEventTypeCount &&
            FieldBool(payload, "FixtureOnly") &&
            !FieldBool(payload, "RuntimeObservation", true) &&
            FieldString(payload, "ExpectedEventType") == row.event_type &&
            row.definition_index ==
                static_cast<std::size_t>(read.event.event_type);
        row.authority_fail_closed =
            read.event.authority_hint == UltimateAuthority::Unknown;
        row.dispatch_passed = capability != nullptr &&
            read.dispatch.dispatchable && read.dispatch.sink == capability->sink;
        if (capability != nullptr) {
            row.dispatch_sink = SemanticDispatchSinkName(capability->sink);
            row.promotion_policy =
                SemanticPromotionPolicyName(capability->promotion_policy);
            row.promotion_policy_passed =
                read.dispatch.promotion_allowed ==
                (capability->promotion_policy ==
                    UltimateSemanticPromotionPolicy::StandardEvidenceGates);
        }
        const auto canonical = SerializeSemanticEventV2(read.event);
        const auto round_trip = ReadSemanticEvent(canonical);
        row.round_trip_passed = round_trip.success &&
            EquivalentSemanticEvent(read.event, round_trip.event) &&
            round_trip.dispatch.dispatchable && capability != nullptr &&
            round_trip.dispatch.sink == capability->sink;

        const std::string expected_type =
            "\"EventType\":\"" + row.event_type + "\"";
        auto corrupt = line;
        const auto type_at = corrupt.find(expected_type);
        if (type_at != std::string::npos) {
            corrupt.replace(type_at, expected_type.size(),
                            "\"EventType\":\"CorruptType\"");
            row.corruption_rejected = !ReadSemanticEvent(corrupt).success;
        }
        auto unsupported = line;
        const auto version_at = unsupported.find("\"SchemaVersion\":2");
        if (version_at != std::string::npos) {
            unsupported.replace(version_at,
                std::strlen("\"SchemaVersion\":2"),
                "\"SchemaVersion\":3");
            row.version_rejected = !ReadSemanticEvent(unsupported).success;
        }

        const bool unique = observed_types.insert(read.event.event_type).second;
        type_names.push_back(row.event_type);
        const bool row_valid = unique && row.source_token_bound &&
            row.fixture_payload_bound && row.authority_fail_closed &&
            row.dispatch_passed && row.round_trip_passed &&
            row.corruption_rejected && row.version_rejected &&
            row.promotion_policy_passed;
        rows_valid = rows_valid && row_valid;
        if (!row_valid && report.error.empty()) report.error =
            "NativeSemanticWireRowContractFailed";
        report.rows.push_back(std::move(row));
    }

    std::sort(type_names.begin(), type_names.end());
    std::ostringstream digest_input;
    for (std::size_t index = 0; index < type_names.size(); ++index) {
        if (index != 0U) digest_input << '\n';
        digest_input << type_names[index];
    }
    report.event_type_digest =
        UltimateSha256Bytes(digest_input.str()).value_or("");
    report.all_event_types_unique =
        observed_types.size() == kUltimateSemanticEventTypeCount;
    report.passed = rows_valid &&
        report.input_line_count == kUltimateSemanticEventTypeCount &&
        report.rows.size() == kUltimateSemanticEventTypeCount &&
        report.all_event_types_unique &&
        report.event_type_digest.size() == 64U;
    if (report.passed) report.error.clear();
    else if (report.error.empty()) report.error =
        "NativeSemanticWireMatrixIncomplete";
    return report;
}

std::string NativeSemanticWireVerificationJson(
    const NativeSemanticWireVerificationReport& report) {
    const auto count = [&](auto predicate) {
        return static_cast<std::size_t>(std::count_if(
            report.rows.begin(), report.rows.end(), predicate));
    };
    std::ostringstream output;
    output << "{\"schemaId\":\"God2NativeSemanticWireVerification\"," 
           << "\"schemaVersion\":1,\"inputPath\":"
           << JsonQuoted(WideToUtf8(report.input_path.wstring()))
           << ",\"inputSHA256\":" << JsonQuoted(report.input_sha256)
           << ",\"eventTypeDigest\":" << JsonQuoted(report.event_type_digest)
           << ",\"inputLineCount\":" << report.input_line_count
           << ",\"allEventTypesUnique\":"
           << (report.all_event_types_unique ? "true" : "false")
           << ",\"passed\":" << (report.passed ? "true" : "false")
           << ",\"totals\":{\"readerAcceptedCount\":"
           << count([](const auto& row) { return row.reader_accepted; })
           << ",\"sourceTokenBoundCount\":"
           << count([](const auto& row) { return row.source_token_bound; })
           << ",\"fixturePayloadBoundCount\":"
           << count([](const auto& row) { return row.fixture_payload_bound; })
           << ",\"authorityFailClosedCount\":"
           << count([](const auto& row) { return row.authority_fail_closed; })
           << ",\"dispatchCount\":"
           << count([](const auto& row) { return row.dispatch_passed; })
           << ",\"roundTripCount\":"
           << count([](const auto& row) { return row.round_trip_passed; })
           << ",\"corruptionRejectedCount\":"
           << count([](const auto& row) { return row.corruption_rejected; })
           << ",\"versionRejectedCount\":"
           << count([](const auto& row) { return row.version_rejected; })
           << ",\"promotionPolicyCount\":"
           << count([](const auto& row) { return row.promotion_policy_passed; })
           << "},\"rows\":[";
    for (std::size_t index = 0; index < report.rows.size(); ++index) {
        if (index != 0U) output << ',';
        const auto& row = report.rows[index];
        output << "{\"wireLine\":" << row.wire_line
               << ",\"definitionIndex\":" << row.definition_index
               << ",\"eventType\":" << JsonQuoted(row.event_type)
               << ",\"readerAccepted\":" << (row.reader_accepted ? "true" : "false")
               << ",\"sourceTokenBound\":" << (row.source_token_bound ? "true" : "false")
               << ",\"fixturePayloadBound\":" << (row.fixture_payload_bound ? "true" : "false")
               << ",\"authorityFailClosed\":" << (row.authority_fail_closed ? "true" : "false")
               << ",\"dispatchPassed\":" << (row.dispatch_passed ? "true" : "false")
               << ",\"roundTripPassed\":" << (row.round_trip_passed ? "true" : "false")
               << ",\"corruptionRejected\":" << (row.corruption_rejected ? "true" : "false")
               << ",\"versionRejected\":" << (row.version_rejected ? "true" : "false")
               << ",\"promotionPolicyPassed\":" << (row.promotion_policy_passed ? "true" : "false")
               << ",\"dispatchSink\":" << JsonQuoted(row.dispatch_sink)
               << ",\"promotionPolicy\":" << JsonQuoted(row.promotion_policy)
               << '}';
    }
    output << "],\"error\":" << JsonQuoted(report.error) << '}';
    return output.str();
}

} // namespace

int VerifyNativeSemanticWireFixture(const fs::path& input_path,
                                    const fs::path& report_path) {
    const auto verification = VerifyNativeSemanticWire(input_path);
    std::error_code directory_error;
    if (!report_path.parent_path().empty())
        fs::create_directories(report_path.parent_path(), directory_error);
    const bool written = !directory_error && WriteUtf8FileAtomic(
        report_path, NativeSemanticWireVerificationJson(verification));
    return verification.passed && written ? 0 : 1;
}

int RunUltimateRecoverySelfTests(const fs::path& report_path,
                                const fs::path& artifacts_path,
                                std::optional<fs::path> native_semantic_wire_path) {
    std::vector<SelfTestRecord> tests;
    SemanticEventMatrixReport semantic_event_matrix;
    UltimateBundleResult bundle_fixture;
    ImporterGateResult importer_fixture;
    const auto fixture_input = BuildRecoveryFixture();
    const auto recovered = UltimateRecoveryEngine::Recover(fixture_input);

    RunSelfTest(&tests, "01 SemanticEvent v2 round-trip", [] {
        auto event = FixtureEvent(UltimateSemanticEventType::StateMutation, 77U,
            "{\"Before\":\"1\",\"After\":\"2\\n3\"}", UltimateAuthority::Derived);
        event.object_token = "Object:77";
        event.value_token = "Value:77";
        event.source_token = "Source:77";
        const auto v2_wire = SerializeSemanticEventV2(event);
        const auto read = ReadSemanticEvent(v2_wire);
        Require(read.success && !read.read_from_v1, "V2RoundTripReadFailed");
        Require(EquivalentSemanticEvent(event, read.event), "V2RoundTripMismatch");
        Require(v2_wire.starts_with(
                    "{\"SchemaId\":\"God2SemanticEvent\",\"SchemaVersion\":2,") &&
                v2_wire.find("\"SchemaId\":", 2U) == std::string::npos &&
                ClassifySemanticEventWire(v2_wire) ==
                    SemanticEventWireProfile::CanonicalV2Envelope &&
                CanEvaluateSemanticPromotion(
                    SemanticEventWireProfile::CanonicalV2Envelope),
                "CanonicalV2EnvelopeClassificationFailed");
        const std::string broad_v1 =
            "{\"SchemaId\":\"God2SemanticEvent\",\"SchemaVersion\":1,"
            "\"EventType\":\"ParserRead\",\"SemanticEventId\":\"legacy-1\","
            "\"Qpc\":12,\"ProcessId\":4,\"ThreadId\":5,"
            "\"Module\":\"God2_opt.exe\",\"Rva\":\"0x78D70\","
            "\"SensitiveValue\":false}";
        const auto v1 = ReadSemanticEvent(broad_v1);
        Require(v1.success && v1.read_from_v1 &&
                ClassifySemanticEventWire(broad_v1) ==
                    SemanticEventWireProfile::NonCanonicalV1CompatibilityInput &&
                !CanEvaluateSemanticPromotion(
                    SemanticEventWireProfile::NonCanonicalV1CompatibilityInput) &&
                v1.event.authority_hint == UltimateAuthority::Unknown,
                "V1CompatibilityReadFailed");
        Require(!ReadSemanticEvent(
                    "{\"SchemaVersion\":\"1\",\"EventType\":\"ParserRead\","
                    "\"EventId\":\"quoted-version\"}").success &&
                !ReadSemanticEvent(
                    "{\"SchemaVersion\":1,\"EventType\":\"ParserRead\","
                    "\"EventId\":7}").success &&
                !ReadSemanticEvent(
                    "{\"SchemaVersion\":1,\"EventType\":\"ParserRead\","
                    "\"SemanticEventId\":\"one\",\"EventId\":\"two\"}").success,
                "V1TypeOrDualIdentitySpoofAccepted");
        const std::string probe_diagnostic_wire =
            "{\"SchemaId\":\"God2SemanticEvent\",\"SchemaVersion\":2,"
            "\"EventType\":\"ProbeDiagnostic\",\"EventId\":\"PD-42-1\","
            "\"Sequence\":1,\"Timestamp\":1786200000000,\"ThreadId\":7,"
            "\"ProcessId\":42,\"SessionId\":\"live-pid-42\","
            "\"ClientBuildId\":\"6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B\","
            "\"ModuleId\":\"God2_opt.exe\",\"RVA\":null,\"CallsiteRVA\":null,"
            "\"ParentEventId\":null,\"ContextId\":null,\"ActionId\":null,"
            "\"ObjectToken\":null,\"ValueToken\":null,\"SourceToken\":\"ObjectResolver\","
            "\"AuthorityHint\":\"UNKNOWN\",\"SensitiveMaskStatus\":\"NotSensitive\","
            "\"Payload\":{\"Domain\":\"Object\",\"Probe\":\"ObjectResolver\","
            "\"Status\":\"EvidenceBlockedUnconfirmedProbe\","
            "\"Reason\":\"NoVerifiedTargetRva\","
            "\"ContractStatus\":\"CandidateOnlyBlockedContract\","
            "\"ActivationAllowedByContract\":false,"
            "\"RuntimeDiagnosticObserved\":true,"
            "\"RuntimeActivationObserved\":false,"
            "\"TargetIdentityVerified\":false,\"FaultIsolation\":\"PerDomain\"}}";
        const auto probe_diagnostic = ReadSemanticEvent(probe_diagnostic_wire);
        Require(probe_diagnostic.success &&
                probe_diagnostic.event.payload.find("EvidenceBlockedUnconfirmedProbe") !=
                    std::string::npos,
                "ActualProbeDiagnosticV2ShapeRejected");
        auto unknown_probe_payload = probe_diagnostic_wire;
        const auto probe_payload_end = unknown_probe_payload.rfind("}}");
        Require(probe_payload_end != std::string::npos, "ProbePayloadEndMissing");
        unknown_probe_payload.insert(probe_payload_end,
            ",\"UntrustedNestedPayload\":1");
        auto quoted_probe_boolean = probe_diagnostic_wire;
        const auto runtime_false = quoted_probe_boolean.find(
            "\"RuntimeActivationObserved\":false");
        Require(runtime_false != std::string::npos, "ProbeBooleanFixtureMissing");
        quoted_probe_boolean.replace(runtime_false,
            std::strlen("\"RuntimeActivationObserved\":false"),
            "\"RuntimeActivationObserved\":\"false\"");
        auto swapped_probe_source = probe_diagnostic_wire;
        const auto object_source = swapped_probe_source.find(
            "\"SourceToken\":\"ObjectResolver\"");
        Require(object_source != std::string::npos, "ProbeSourceFixtureMissing");
        swapped_probe_source.replace(object_source,
            std::strlen("\"SourceToken\":\"ObjectResolver\""),
            "\"SourceToken\":\"WinsockTransport\"");
        Require(!ReadSemanticEvent(unknown_probe_payload).success &&
                !ReadSemanticEvent(quoted_probe_boolean).success &&
                !ReadSemanticEvent(swapped_probe_source).success,
                "ClosedProbePayloadSpoofAccepted");
        auto unclassified_event = FixtureEvent(
            UltimateSemanticEventType::PacketBoundary, 78U,
            "{\"UntrustedNestedPayload\":\"raw-marker-must-not-persist\"}");
        const auto unclassified_wire = SerializeSemanticEventV2(unclassified_event);
        Require(unclassified_wire.find("raw-marker-must-not-persist") ==
                    std::string::npos &&
                unclassified_wire.find(
                    "EvidenceBlockedInvalidOrUnclassifiedPayload") !=
                    std::string::npos &&
                ReadSemanticEvent(unclassified_wire).success,
                "UnclassifiedPayloadWasPersistedOrRejectedAfterBlocking");
        const auto canonicalized_v1 = SerializeSemanticEventV2(v1.event);
        Require(canonicalized_v1.find(
                    "\"Profile\":\"NonCanonicalV1CompatibilityInput\"") !=
                    std::string::npos &&
                canonicalized_v1.find("\"Canonical\":false") !=
                    std::string::npos &&
                canonicalized_v1.find("\"Promotable\":false") !=
                    std::string::npos,
                "V1CompatibilityDiscriminatorMissing");
        auto promotable_v1_spoof = canonicalized_v1;
        const auto promotable_false = promotable_v1_spoof.find(
            "\"Promotable\":false");
        Require(promotable_false != std::string::npos,
                "V1PromotableFixtureMissing");
        promotable_v1_spoof.replace(promotable_false,
            std::strlen("\"Promotable\":false"), "\"Promotable\":true");
        Require(!ReadSemanticEvent(promotable_v1_spoof).success,
                "V1CompatibilityPromotionSpoofAccepted");
        const auto probe_field = ReadSemanticEvent(
            "{\"SchemaId\":\"God2SemanticEvent\",\"SchemaVersion\":2,"
            "\"SemanticEventId\":\"SE-42-9-1\",\"EventId\":\"SE-42-9-1\","
            "\"EventType\":\"ParserRead\",\"Sequence\":9,"
            "\"Timestamp\":1786200000001,\"ThreadId\":7,\"ProcessId\":42,"
            "\"SessionId\":\"live-pid-42\","
            "\"ClientBuildId\":\"6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B\","
            "\"ModuleId\":\"God2_opt.exe\",\"RVA\":\"0x00078D70\","
            "\"CallsiteRVA\":\"0x00078A48\",\"ParentEventId\":null,"
            "\"ContextId\":null,\"ActionId\":null,\"ObjectToken\":null,"
            "\"ValueToken\":\"VT-42-9-1\",\"SourceToken\":\"PF-42-9\","
            "\"AuthorityHint\":\"VERIFIED\",\"SensitiveMaskStatus\":\"NotSensitive\","
            "\"Payload\":{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
            "\"FrameOffset\":4,\"Width\":4,\"ValueType\":\"UInt32\","
            "\"RawBytes\":\"01000000\",\"ParsedValue\":1,"
            "\"FieldSemantic\":\"MonsterId\"},\"ObservedAtUnixMs\":1786200000001,"
            "\"Qpc\":99,\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
            "\"ProtocolFrameId\":\"PF-42-9\",\"LogicalMessageId\":null,"
            "\"ActionInstanceId\":null,\"HookInvocationId\":\"hook-42-9\","
            "\"ParentInvocationId\":null,\"ContextInvocationId\":null,"
            "\"ExactParentSemanticEventId\":null,\"ProbeId\":\"PacketDecode.FrameBoundary\","
            "\"ProbeCategory\":\"Parser\",\"Module\":\"God2_opt.exe\","
            "\"Rva\":\"0x00078D70\",\"CallerRva\":\"0x00078A4D\","
            "\"ProbeContractVersion\":\"1\","
            "\"ClientSha256Expected\":\"6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B\","
            "\"BuildBindingStatus\":\"ExactInstructionIdentityVerified\","
            "\"EvidenceBasis\":\"ExactBuild\",\"EvidenceLevel\":\"Recovered\","
            "\"FrameOffset\":4,\"FrameOffsetStatus\":\"VerifiedExactBoundary\","
            "\"DestinationOffset\":4,\"Width\":4,\"ReadType\":\"UInt32\","
            "\"WriteType\":\"NotApplicable\",\"Endian\":\"Little\","
            "\"RawBytes\":\"01000000\",\"ParsedValue\":1,\"Value\":1,"
            "\"ValueTokenId\":\"VT-42-9-1\",\"FieldSemantic\":\"MonsterId\","
            "\"AuthorityClassification\":\"ProtocolControl\",\"SensitiveValue\":false}");
        Require(probe_field.success && probe_field.event.rva == 0x78D70U &&
                probe_field.event.callsite_rva == 0x78A48U &&
                PayloadFields(probe_field.event).at("Direction") == "ServerToClient",
                "ActualProbeFieldV2ShapeRejected");
        UltimateRecoveryInput actual_probe_input;
        actual_probe_input.session_id = "live-pid-42";
        actual_probe_input.semantic_events.push_back(probe_field.event);
        const auto actual_probe_recovery = UltimateRecoveryEngine::Recover(actual_probe_input);
        Require(actual_probe_recovery.packet_schemas.size() == 1U &&
                actual_probe_recovery.packet_schemas.front().authority ==
                    UltimateAuthority::Unknown &&
                !actual_probe_recovery.packet_schemas.front().evidence.empty() &&
                actual_probe_recovery.packet_schemas.front().evidence.front().authority ==
                    UltimateAuthority::Unknown,
                "SingleActualProbeEventWasImproperlyPromoted");
        const auto canonical = SerializeSemanticEventV2(probe_field.event);
        Require(canonical.find("\"Payload\":{") != std::string::npos &&
                canonical.find("\"Payload\":\"") == std::string::npos &&
                ReadSemanticEvent(canonical).success,
                "CanonicalV2ObjectPayloadRoundTripFailed");
        const auto probe_handler = ReadSemanticEvent(
            "{\"SchemaId\":\"God2SemanticEvent\",\"SchemaVersion\":2,"
            "\"SemanticEventId\":\"SE-42-10-1\",\"EventId\":\"SE-42-10-1\","
            "\"EventType\":\"HandlerInvocation\",\"Sequence\":10,"
            "\"Timestamp\":1786200000002,\"ThreadId\":7,\"ProcessId\":42,"
            "\"SessionId\":\"live-pid-42\","
            "\"ClientBuildId\":\"6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B\","
            "\"ModuleId\":\"God2_opt.exe\",\"RVA\":\"0x0007F940\","
            "\"CallsiteRVA\":\"0x00147096-or-0x001470AE\","
            "\"ParentEventId\":null,\"ContextId\":null,\"ActionId\":null,"
            "\"ObjectToken\":null,\"ValueToken\":\"VT-42-10-1\","
            "\"SourceToken\":null,\"AuthorityHint\":\"VERIFIED\","
            "\"SensitiveMaskStatus\":\"NotSensitive\","
            "\"Payload\":{\"ArgumentIndex\":0,\"ArgumentType\":\"UInt8\","
            "\"ArgumentValue\":12},\"ObservedAtUnixMs\":1786200000002,"
            "\"Qpc\":100,\"Direction\":\"ServerToClient\",\"Opcode\":\"0x41\","
            "\"ProtocolFrameId\":null,\"LogicalMessageId\":null,"
            "\"ActionInstanceId\":null,\"HookInvocationId\":\"hook-42-10\","
            "\"ParentInvocationId\":null,\"ContextInvocationId\":null,"
            "\"ExactParentSemanticEventId\":null,\"ContextSemanticEventId\":null,"
            "\"ContextCorrelationBasis\":\"SameThreadLatestPostDecrypt\","
            "\"HandlerInvocationId\":\"HI-42-10\","
            "\"ProbeId\":\"Battle.HandlerRecordLength\",\"ProbeCategory\":\"Handler\","
            "\"Module\":\"God2_opt.exe\",\"Rva\":\"0x0007F940\","
            "\"CallerRva\":\"0x0014709B-or-0x001470B3\","
            "\"ProbeContractVersion\":\"1\","
            "\"ClientSha256Expected\":\"6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B\","
            "\"BuildBindingStatus\":\"ExactCallTargetVerified\","
            "\"EvidenceBasis\":\"ExactBuildBattleDispatcherRecordLengthArgument\","
            "\"EvidenceLevel\":\"Recovered\",\"ArgumentIndex\":0,"
            "\"ArgumentType\":\"UInt8\",\"ArgumentValue\":12,"
            "\"AssociatedValueTokenId\":\"VT-42-10-1\","
            "\"ObjectPointerPersisted\":false,\"SensitiveValue\":false}");
        Require(probe_handler.success && probe_handler.event.rva == 0x7F940U &&
                probe_handler.event.callsite_rva == 0U &&
                probe_handler.event.callsite_rva_expression ==
                    "0x00147096-or-0x001470AE" &&
                ReadSemanticEvent(SerializeSemanticEventV2(probe_handler.event)).success,
                "ActualProbeHandlerV2ShapeRejected");
    });
    RunSelfTest(&tests, "02 SemanticEvent corruption rejection", [] {
        const auto event = FixtureEvent(UltimateSemanticEventType::PacketBoundary, 1U, "{}");
        const auto valid = SerializeSemanticEventV2(event);
        Require(!ReadSemanticEvent(valid.substr(0, valid.size() - 1U)).success,
                "TruncationAccepted");
        auto duplicate = valid;
        duplicate.insert(1U, "\"SchemaVersion\":2,");
        Require(!ReadSemanticEvent(duplicate).success, "DuplicateKeyAccepted");
        auto unknown = valid;
        unknown.insert(1U, "\"Unexpected\":1,");
        Require(!ReadSemanticEvent(unknown).success, "UnknownV2FieldAccepted");
        auto unsupported = valid;
        const auto location = unsupported.find("\"SchemaVersion\":2");
        unsupported.replace(location, std::string("\"SchemaVersion\":2").size(),
                            "\"SchemaVersion\":3");
        Require(!ReadSemanticEvent(unsupported).success, "UnsupportedVersionAccepted");
    });
    RunSelfTest(&tests,
        "02b SemanticEvent 25-type roundtrip corruption version dispatch matrix",
        [&semantic_event_matrix, &artifacts_path] {
        std::size_t fixture_count = 0;
        std::size_t roundtrip_count = 0;
        std::size_t corruption_rejected = 0;
        std::size_t version_rejected = 0;
        std::size_t v1_compatible = 0;
        std::size_t dispatch_count = 0;
        std::size_t runtime_producers = 0;
        std::size_t blocked_capabilities = 0;
        std::size_t no_promotion_count = 0;
        std::ostringstream canonical_v1_lines;
        std::vector<std::string> canonical_v1_event_types;
        for (std::size_t index = 0;
             index < kUltimateSemanticEventTypeCount; ++index) {
            const auto type = static_cast<UltimateSemanticEventType>(index);
            const auto* capability = GetUltimateSemanticEventCapability(type);
            if (capability == nullptr) {
                Require(false, "SemanticCapabilityRowMissing");
                continue;
            }
            Require(capability->event_type == type && capability->fixture_producer,
                    "SemanticCapabilityRowMissing");
            ++fixture_count;
            runtime_producers += capability->runtime_producer ? 1U : 0U;
            blocked_capabilities += capability->blocked_capability ? 1U : 0U;

            auto event = FixtureEvent(type, 1000U + index,
                "{\"FixtureOnly\":true,\"RuntimeObservation\":false}");
            const auto wire = SerializeSemanticEventV2(event);
            const auto read = ReadSemanticEvent(wire);
            SemanticEventMatrixRow row;
            row.index = index;
            row.event_type = ToString(type);
            row.fixture_producer = capability->fixture_producer;
            row.runtime_producer = capability->runtime_producer;
            row.blocked_capability = capability->blocked_capability;
            row.reader_accepted = read.success;
            row.roundtrip_passed = read.success &&
                EquivalentSemanticEvent(event, read.event);
            row.dispatch_sink = SemanticDispatchSinkName(capability->sink);
            row.promotion_policy = SemanticPromotionPolicyName(
                capability->promotion_policy);
            Require(row.roundtrip_passed,
                    "SemanticTypeRoundTripFailed");
            ++roundtrip_count;
            row.dispatch_passed = read.dispatch.dispatchable &&
                read.dispatch.sink == capability->sink;
            Require(row.dispatch_passed,
                    "ReaderDispatchMatrixMismatch");
            ++dispatch_count;

            const std::string expected_type =
                "\"EventType\":\"" + std::string(ToString(type)) + "\"";
            auto corrupt = wire;
            const auto type_at = corrupt.find(expected_type);
            Require(type_at != std::string::npos, "SerializedEventTypeMissing");
            corrupt.replace(type_at, expected_type.size(),
                "\"EventType\":\"CorruptType\"");
            row.corruption_rejected = !ReadSemanticEvent(corrupt).success;
            if (row.corruption_rejected) ++corruption_rejected;

            auto unsupported = wire;
            const auto version_at = unsupported.find("\"SchemaVersion\":2");
            Require(version_at != std::string::npos,
                    "SerializedSchemaVersionMissing");
            unsupported.replace(version_at,
                std::strlen("\"SchemaVersion\":2"),
                "\"SchemaVersion\":3");
            row.version_rejected = !ReadSemanticEvent(unsupported).success;
            if (row.version_rejected) ++version_rejected;

            std::ostringstream v1;
            v1 << "{\"SchemaId\":\"God2SemanticEvent\","
               << "\"SchemaVersion\":1,\"EventType\":"
               << JsonQuoted(ToString(type))
               << ",\"SemanticEventId\":\"v1-type-" << index
               << "\",\"Sequence\":" << (2000U + index)
               << ",\"Timestamp\":\"2026-01-01T00:00:00.000Z\","
               << "\"ThreadId\":7,\"ProcessId\":42,"
               << "\"SessionId\":\"V1Matrix\",\"ClientBuildId\":"
               << JsonQuoted(kExpectedClientSha256)
               << ",\"ModuleId\":\"God2_opt.exe\",\"RVA\":0,"
               << "\"CallsiteRVA\":0,\"SensitiveValue\":false}";
            const auto v1_wire = v1.str();
            canonical_v1_lines << v1_wire << '\n';
            canonical_v1_event_types.emplace_back(ToString(type));
            const auto v1_read = ReadSemanticEvent(v1_wire);
            row.v1_compatible = v1_read.success && v1_read.read_from_v1 &&
                v1_read.event.event_type == type &&
                v1_read.dispatch.dispatchable &&
                v1_read.event.authority_hint == UltimateAuthority::Unknown &&
                ClassifySemanticEventWire(v1_wire) ==
                    SemanticEventWireProfile::CanonicalV1WriterProfile &&
                !CanEvaluateSemanticPromotion(
                    SemanticEventWireProfile::CanonicalV1WriterProfile);
            if (row.v1_compatible) {
                const auto canonical_v2 = SerializeSemanticEventV2(v1_read.event);
                const auto canonical_read = ReadSemanticEvent(canonical_v2);
                row.v1_compatible = canonical_read.success &&
                    ClassifySemanticEventWire(canonical_v2) ==
                        SemanticEventWireProfile::CanonicalV2Envelope &&
                    EquivalentSemanticEvent(v1_read.event, canonical_read.event) &&
                    canonical_read.event.authority_hint == UltimateAuthority::Unknown;
            }
            if (row.v1_compatible) ++v1_compatible;

            if (capability->promotion_policy ==
                UltimateSemanticPromotionPolicy::NoPromotion) {
                event.authority_hint = UltimateAuthority::Verified;
                const auto decision = DispatchSemanticEvent(event);
                Require(decision.dispatchable && !decision.promotion_allowed,
                        "NoPromotionEventCouldEnterPromotion");
                row.no_promotion_enforced = decision.dispatchable &&
                    !decision.promotion_allowed;
                ++no_promotion_count;
            }
            semantic_event_matrix.rows.push_back(std::move(row));
        }
        Require(fixture_count == 25U && roundtrip_count == 25U &&
                corruption_rejected == 25U && version_rejected == 25U &&
                v1_compatible == 25U && dispatch_count == 25U,
                "Semantic25TypeMatrixCountMismatch");
        Require(runtime_producers == 25U && blocked_capabilities == 20U &&
                 no_promotion_count == 2U,
                 "SemanticProducerCapabilityCountMismatch");

        std::sort(canonical_v1_event_types.begin(), canonical_v1_event_types.end());
        std::ostringstream event_type_digest_input;
        for (std::size_t index = 0; index < canonical_v1_event_types.size(); ++index) {
            if (index != 0U) event_type_digest_input << '\n';
            event_type_digest_input << canonical_v1_event_types[index];
        }
        const auto canonical_v1_content = canonical_v1_lines.str();
        const auto canonical_v1_sha = UltimateSha256Bytes(canonical_v1_content);
        const auto canonical_v1_event_type_digest =
            UltimateSha256Bytes(event_type_digest_input.str());
        const auto canonical_v1_path =
            artifacts_path / L"canonical-v1-semantic-wire.jsonl";
        const auto canonical_v1_binding_path =
            artifacts_path / L"canonical-v1-semantic-wire.binding.json";
        std::error_code artifact_directory_error;
        fs::create_directories(artifacts_path, artifact_directory_error);
        Require(!artifact_directory_error && canonical_v1_sha.has_value() &&
                canonical_v1_event_type_digest.has_value() &&
                *canonical_v1_event_type_digest ==
                    "A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C" &&
                WriteUtf8FileAtomic(canonical_v1_path, canonical_v1_content),
                "CanonicalV1ArtifactWriteOrDigestFailed");
        const auto canonical_v1_file_sha = CalculateFileSha256(canonical_v1_path);
        const std::string canonical_v1_binding =
            "{\"SchemaId\":\"God2SemanticEventProfileArtifactBinding\"," 
            "\"SchemaVersion\":1,\"EvidenceSource\":"
            "\"DeterministicSyntheticContractFixture\"," 
            "\"LiveEvidenceClaimed\":false,\"FixtureOnly\":true,"
            "\"Profile\":\"CanonicalV1WriterProfile\",\"Canonical\":true,"
            "\"Promotable\":false,\"CanonicalizeToV2BeforePromotion\":true,"
            "\"RelativePath\":\"canonical-v1-semantic-wire.jsonl\",\"SHA256\":" +
            JsonQuoted(canonical_v1_sha.value_or("")) + ",\"EventTypeDigest\":" +
            JsonQuoted(canonical_v1_event_type_digest.value_or("")) +
            ",\"LineCount\":25}";
        Require(canonical_v1_file_sha == canonical_v1_sha &&
                WriteUtf8FileAtomic(canonical_v1_binding_path,
                                    canonical_v1_binding + '\n') &&
                ReadUtf8File(canonical_v1_binding_path) ==
                    std::optional<std::string>(canonical_v1_binding + '\n'),
                "CanonicalV1ArtifactBindingFailed");

        const std::string compatibility_v1_content =
            "{\"SchemaVersion\":1,\"EventType\":\"ObjectResolution\"," 
            "\"EventId\":\"NonCanonicalV1CompatibilityFixture\"," 
            "\"FixtureOnly\":true,\"LiveEvidenceClaimed\":false,"
            "\"Profile\":\"NonCanonicalV1CompatibilityInput\"," 
            "\"Canonical\":false,\"Promotable\":false,"
            "\"WrongCausalFixture\":true,\"LegacyProducerExtension\":"
            "\"preserved-only-during-input-canonicalization\"}\n";
        const auto compatibility_v1_path =
            artifacts_path / L"noncanonical-v1-compatibility-input.json";
        const auto compatibility_v1_sha = UltimateSha256Bytes(compatibility_v1_content);
        const auto compatibility_v1_read = ReadSemanticEvent(
            std::string_view(compatibility_v1_content).substr(
                0U, compatibility_v1_content.size() - 1U));
        Require(compatibility_v1_sha.has_value() && compatibility_v1_read.success &&
                compatibility_v1_read.read_from_v1 &&
                compatibility_v1_read.event.authority_hint == UltimateAuthority::Unknown &&
                ClassifySemanticEventWire(
                    std::string_view(compatibility_v1_content).substr(
                        0U, compatibility_v1_content.size() - 1U)) ==
                    SemanticEventWireProfile::NonCanonicalV1CompatibilityInput &&
                !CanEvaluateSemanticPromotion(
                    SemanticEventWireProfile::NonCanonicalV1CompatibilityInput) &&
                WriteUtf8FileAtomic(compatibility_v1_path,
                                    compatibility_v1_content) &&
                CalculateFileSha256(compatibility_v1_path) == compatibility_v1_sha,
                "NonCanonicalV1CompatibilityArtifactContractFailed");

        const auto zero_pid = FixtureEvent(
            UltimateSemanticEventType::ProbeDiagnostic, 3000U, "{}");
        auto zero_pid_wire = SerializeSemanticEventV2(zero_pid);
        const auto pid_at = zero_pid_wire.find("\"ProcessId\":42");
        Require(pid_at != std::string::npos, "ProcessIdFixtureMissing");
        zero_pid_wire.replace(pid_at, std::strlen("\"ProcessId\":42"),
                              "\"ProcessId\":0");
        Require(!ReadSemanticEvent(zero_pid_wire).success,
                "ZeroProcessIdAcceptedByStrictV2Reader");
    });
    if (native_semantic_wire_path.has_value()) {
        RunSelfTest(&tests,
            "02c DLL native 25-wire cross-runtime reader dispatch binding",
            [&semantic_event_matrix, &native_semantic_wire_path] {
            const auto verification = VerifyNativeSemanticWire(
                *native_semantic_wire_path);
            Require(verification.passed && verification.rows.size() ==
                    kUltimateSemanticEventTypeCount,
                    "NativeSemanticWireVerificationFailed");
            semantic_event_matrix.native_wire_bound = true;
            semantic_event_matrix.native_wire_verified = verification.passed;
            semantic_event_matrix.native_wire_input_sha256 =
                verification.input_sha256;
            semantic_event_matrix.native_wire_event_type_digest =
                verification.event_type_digest;
            semantic_event_matrix.native_wire_input_line_count =
                verification.input_line_count;
            for (auto& matrix_row : semantic_event_matrix.rows) {
                const auto native_row = std::find_if(
                    verification.rows.begin(), verification.rows.end(),
                    [&](const auto& row) {
                        return row.event_type == matrix_row.event_type;
                    });
                Require(native_row != verification.rows.end(),
                        "NativeSemanticWireTypeMissingFromBinding");
                matrix_row.native_wire_reader_accepted =
                    native_row->reader_accepted;
                matrix_row.native_wire_dispatch_passed =
                    native_row->dispatch_passed;
                Require(matrix_row.native_wire_reader_accepted &&
                        matrix_row.native_wire_dispatch_passed,
                        "NativeSemanticWireReaderDispatchBindingFailed");
            }
        });
    }
    RunSelfTest(&tests, "03 Shared ring ordering", [] {
        UltimatePriorityRing ring(4U);
        ring.Publish(UltimateProbeDomain::NetworkProbe, UltimateEventPriority::Authoritative,
                     FixtureEvent(UltimateSemanticEventType::PacketBoundary, 3U, "{}"));
        ring.Publish(UltimateProbeDomain::NetworkProbe, UltimateEventPriority::Authoritative,
                     FixtureEvent(UltimateSemanticEventType::PacketBoundary, 1U, "{}"));
        ring.Publish(UltimateProbeDomain::NetworkProbe, UltimateEventPriority::Authoritative,
                     FixtureEvent(UltimateSemanticEventType::PacketBoundary, 2U, "{}"));
        const auto batch = ring.DrainBatch(4U);
        Require(batch.size() == 3U && batch[0].event.sequence == 1U &&
                batch[1].event.sequence == 2U && batch[2].event.sequence == 3U,
                "RingSequenceOrderMismatch");
    });
    RunSelfTest(&tests, "04 Ring overflow accounting", [] {
        UltimatePriorityRing ring(2U);
        ring.Publish(UltimateProbeDomain::SnapshotProbe, UltimateEventPriority::CandidateTelemetry,
                     FixtureEvent(UltimateSemanticEventType::SnapshotObject, 1U, "{}"));
        ring.Publish(UltimateProbeDomain::SnapshotProbe, UltimateEventPriority::CandidateTelemetry,
                     FixtureEvent(UltimateSemanticEventType::SnapshotObject, 2U, "{}"));
        Require(!ring.Publish(UltimateProbeDomain::SnapshotProbe,
            UltimateEventPriority::CandidateTelemetry,
            FixtureEvent(UltimateSemanticEventType::SnapshotObject, 3U, "{}")),
            "RingOverflowUnexpectedlyAccepted");
        const auto status = ring.DomainStatus(UltimateProbeDomain::SnapshotProbe);
        Require(status.dropped == 1U && status.first_dropped_sequence == 3U &&
                status.last_dropped_sequence == 3U &&
                status.last_drop_reason == "CapacityExhausted" &&
                status.semantic_evidence_incomplete, "RingDropLedgerIncomplete");
    });
    RunSelfTest(&tests, "05 Per-domain backpressure", [] {
        UltimatePriorityRing ring(1U);
        ring.Publish(UltimateProbeDomain::NetworkProbe, UltimateEventPriority::Authoritative,
                     FixtureEvent(UltimateSemanticEventType::PacketBoundary, 1U, "{}"));
        ring.Publish(UltimateProbeDomain::ParserProbe, UltimateEventPriority::Authoritative,
                     FixtureEvent(UltimateSemanticEventType::ParserRead, 2U, "{}"));
        const auto network = ring.DomainStatus(UltimateProbeDomain::NetworkProbe);
        const auto parser = ring.DomainStatus(UltimateProbeDomain::ParserProbe);
        Require(network.dropped == 0U && parser.dropped == 1U && parser.consumer_lag == 0U,
                "PerDomainBackpressureMixed");
    });
    RunSelfTest(&tests, "06 Probe exception isolation", [] {
        UltimatePriorityRing ring(4U);
        Require(!ring.ExecuteIsolated(UltimateProbeDomain::ObjectProbe, [] {
            throw std::runtime_error("suppressed diagnostic detail");
        }), "ProbeExceptionNotCaught");
        const auto object = ring.DomainStatus(UltimateProbeDomain::ObjectProbe);
        Require(object.disabled && object.diagnostic == "ProbeExceptionIsolated",
                "AffectedProbeNotDisabled");
        Require(ring.Publish(UltimateProbeDomain::NetworkProbe,
            UltimateEventPriority::Authoritative,
            FixtureEvent(UltimateSemanticEventType::PacketBoundary, 99U, "{}")),
            "UnaffectedCaptureDomainStopped");
    });
    RunSelfTest(&tests, "06b Deep-probe candidate promotion hard gate", [] {
        UltimateDeepProbeCandidate candidate;
        candidate.domain = UltimateProbeDomain::ObjectProbe;
        candidate.module_section = ".text";
        candidate.callsite_rva = 0x100U;
        candidate.target_rva = 0x180U;
        candidate.signature = "55 8B EC 83 EC 08 53 56";
        candidate.signature_mask = "FF FF FF FF FF FF FF FF";
        candidate.discovery_provenance =
            "VerifiedSeed:Parser;BoundedExecutableDirectCall";
        candidate.calling_convention_state = "thiscall";
        candidate.authority = UltimateAuthority::Unknown;
        UltimateDeepProbeVerification verified;
        verified.exact_target_identity = true;
        verified.executable_section = true;
        verified.exact_candidate_bytes = true;
        verified.calling_convention_verified = true;
        verified.typed_runtime_evidence = true;
        verified.repeated_causal_observations = 2U;
        verified.contradictions = 0U;
        verified.stable_object_or_context = true;
        verified.verified_consumer_or_mutation = true;
        verified.sensitive_mask_contract = true;
        verified.argument_contract_verified = true;
        verified.return_value_lifetime_verified = true;
        verified.thread_context_verified = true;
        verified.reentrancy_risk_verified = true;
        Require(CanActivateDeepProbeCandidate(candidate, verified),
                "FullyVerifiedDeepProbeCandidateRejected");
        const auto rejects = [&](auto mutation) {
            auto rejected = verified;
            mutation(rejected);
            return !CanActivateDeepProbeCandidate(candidate, rejected);
        };
        Require(rejects([](auto& gate) { gate.exact_target_identity = false; }) &&
                rejects([](auto& gate) { gate.executable_section = false; }) &&
                rejects([](auto& gate) { gate.exact_candidate_bytes = false; }) &&
                rejects([](auto& gate) { gate.calling_convention_verified = false; }) &&
                rejects([](auto& gate) { gate.typed_runtime_evidence = false; }) &&
                rejects([](auto& gate) { gate.repeated_causal_observations = 1U; }) &&
                rejects([](auto& gate) { gate.contradictions = 1U; }) &&
                rejects([](auto& gate) { gate.stable_object_or_context = false; }) &&
                rejects([](auto& gate) { gate.verified_consumer_or_mutation = false; }) &&
                rejects([](auto& gate) { gate.sensitive_mask_contract = false; }) &&
                rejects([](auto& gate) { gate.argument_contract_verified = false; }) &&
                rejects([](auto& gate) { gate.return_value_lifetime_verified = false; }) &&
                rejects([](auto& gate) { gate.thread_context_verified = false; }) &&
                rejects([](auto& gate) { gate.reentrancy_risk_verified = false; }),
                "IncompleteDeepProbeCandidateGateAccepted");
        candidate.calling_convention_state = "UNKNOWN";
        Require(!CanActivateDeepProbeCandidate(candidate, verified),
                "UnknownCallingConventionActivated");
        candidate.calling_convention_state = "thiscall";
        candidate.authority = UltimateAuthority::Verified;
        Require(!CanActivateDeepProbeCandidate(candidate, verified),
                "CandidateSelfAssertedVerifiedAuthority");
    });
    for (const auto& producer : GetUltimateDeepRuntimeProducerInventory()) {
        RunSelfTest(&tests,
            std::string("P0 producer implemented: ") + producer.producer_name,
            [producer] {
            Require(producer.producer_name != nullptr &&
                    producer.producer_name[0] != '\0' &&
                    producer.evidence_contract != nullptr &&
                    producer.evidence_contract[0] != '\0' &&
                    static_cast<std::size_t>(producer.domain) >= 4U,
                    "FundamentalProducerInventoryIncomplete");
            UltimateDeepRuntimeProducerInput blocked;
            blocked.domain = producer.domain;
            blocked.event_type = producer.primary_event_type;
            const auto result = ProduceUltimateDeepRuntimeEvent(blocked);
            Require(result.producer_implemented && !result.activation_allowed &&
                    !result.event_produced && !result.event.has_value(),
                    "DormantProducerDidNotFailClosed");
        });
    }
    for (const auto& adapter : GetUltimateGameplayAdapterInventory()) {
        RunSelfTest(&tests,
            std::string("P1 gameplay adapter implemented: ") + adapter.adapter_name,
            [adapter] {
            Require(adapter.adapter_name != nullptr && adapter.adapter_name[0] != '\0' &&
                    adapter.required_fundamental_evidence != nullptr &&
                    adapter.required_fundamental_evidence[0] != '\0' &&
                    adapter.preserves_base_runtime_observed_layers &&
                    adapter.preserves_unknown_server_authority &&
                    static_cast<std::size_t>(adapter.domain) >= 14U &&
                    static_cast<std::size_t>(adapter.domain) <= 23U,
                    "GameplayAdapterAuthorityContractIncomplete");
        });
    }
    const auto make_producer_input = [] {
        UltimateDeepRuntimeProducerInput input;
        input.domain = UltimateProbeDomain::ObjectProbe;
        input.event_type = UltimateSemanticEventType::ObjectResolved;
        input.gates = {true, true, true, true, true, true, true,
                       true, true, true, true, true, true, true};
        input.session_id = "SELFTEST-DEEP-PRODUCER";
        input.client_build_id = kExpectedClientSha256;
        input.candidate_id = "Object-00000001";
        input.event_id = "SELFTEST-DEEP-EVENT-1";
        input.timestamp = "2026-01-01T00:00:00.000Z";
        input.source_token = "ObjectResolver";
        input.object_token = "Session:SELFTEST:Object:1";
        input.value_token = "Session:SELFTEST:Value:1";
        input.payload =
            "{\"EntityFamily\":\"Monster\",\"StableTemplateId\":\"4102\"," 
            "\"Property\":\"Level\",\"RawValue\":\"12\"," 
            "\"NormalizedValue\":\"12\",\"ValueLayer\":\"Observed\"," 
            "\"ExactBuildBinding\":\"PASS\",\"TypedSource\":\"ObjectResolver\"," 
            "\"StableContext\":\"SessionObjectToken\"," 
            "\"CausalPath\":\"RegistryToObject\"," 
            "\"VerifiedConsumerOrMutation\":\"ObjectReader\"}";
        input.sequence = 1U;
        input.rva = 0x100U;
        input.callsite_rva = 0x80U;
        input.process_id = 42U;
        input.thread_id = 7U;
        input.authority = UltimateAuthority::Verified;
        input.current_official_live = true;
        return input;
    };
    RunSelfTest(&tests, "P0 producer all-gate activation", [make_producer_input] {
        const auto result = ProduceUltimateDeepRuntimeEvent(make_producer_input());
        Require(result.activation_allowed && result.event_produced &&
                result.event.has_value(), "AllGateProducerActivationFailed");
    });
    RunSelfTest(&tests, "P0 producer exact-build rejection", [make_producer_input] {
        auto input = make_producer_input(); input.client_build_id.assign(64U, '0');
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "MismatchedBuildProducedEvent");
    });
    RunSelfTest(&tests, "P0 producer fixture isolation", [make_producer_input] {
        auto input = make_producer_input(); input.fixture_only = true;
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "FixtureProducedOfficialEvent");
    });
    RunSelfTest(&tests, "P0 producer historical isolation", [make_producer_input] {
        auto input = make_producer_input(); input.historical_official_live = true;
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "HistoricalEvidenceProducedCurrentEvent");
    });
    RunSelfTest(&tests, "P0 producer authority rejection", [make_producer_input] {
        auto input = make_producer_input(); input.authority = UltimateAuthority::Observed;
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "ObservedAuthorityProducedVerifiedEvent");
    });
    RunSelfTest(&tests, "P0 producer sensitive-value rejection", [make_producer_input] {
        auto input = make_producer_input(); input.sensitive_value_persisted = true;
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "SensitiveValueProducedEvent");
    });
    RunSelfTest(&tests, "P0 producer raw-pointer rejection", [make_producer_input] {
        auto input = make_producer_input(); input.object_token = "0x12345678";
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "RawPointerTokenProducedEvent");
    });
    RunSelfTest(&tests, "P0 producer domain-event rejection", [make_producer_input] {
        auto input = make_producer_input();
        input.event_type = UltimateSemanticEventType::FormulaResult;
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "MismatchedDomainEventProduced");
    });
    RunSelfTest(&tests, "P0 producer canonical-payload rejection", [make_producer_input] {
        auto input = make_producer_input(); input.payload = "{\"Untrusted\":true}";
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "UnclassifiedPayloadProducedEvent");
    });
    RunSelfTest(&tests, "P0 producer runtime-binding rejection", [make_producer_input] {
        auto input = make_producer_input(); input.process_id = 0U;
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "IncompleteRuntimeBindingProducedEvent");
    });
    RunSelfTest(&tests, "P0 producer unsafe-identifier rejection", [make_producer_input] {
        auto input = make_producer_input(); input.candidate_id = "../candidate";
        Require(!ProduceUltimateDeepRuntimeEvent(input).event_produced,
                "UnsafeIdentifierProducedEvent");
    });
    RunSelfTest(&tests, "P0 producer verified-authority preservation", [make_producer_input] {
        const auto result = ProduceUltimateDeepRuntimeEvent(make_producer_input());
        Require(result.event.has_value() &&
                result.event->authority_hint == UltimateAuthority::Verified &&
                result.event->client_build_id == kExpectedClientSha256 &&
                result.event->sensitive_mask_status == "NotSensitive",
                "ProducerAuthorityBindingChanged");
    });
    RunSelfTest(&tests, "07 Object resolver contract", [&] {
        const auto found = std::find_if(recovered.objects.begin(), recovered.objects.end(),
            [](const auto& object) { return object.canonical_identity == "Monster:4102"; });
        Require(found != recovered.objects.end() && !LooksLikeBarePointer(found->canonical_identity),
                "StableObjectIdentityUnavailable");
    });
    RunSelfTest(&tests, "08 Allocation/vtable contract", [&] {
        const auto found = std::find_if(recovered.objects.begin(), recovered.objects.end(),
            [](const auto& object) { return object.object_token == "SELFTEST-OBJECT-4102"; });
        Require(found != recovered.objects.end() && found->allocation_size == 128U &&
                !found->vtable.empty() && !found->constructor_path.empty(),
                "AllocationVtableEvidenceMissing");
    });
    RunSelfTest(&tests, "09 Registry enumerator contract", [&] {
        Require(recovered.registries.size() == 1U &&
                recovered.registries.front().canonical_identity == "MonsterTemplateRegistry" &&
                recovered.registries.front().read_only_enumeration &&
                recovered.registries.front().record_count == 1U,
                "ReadOnlyRegistryContractMismatch");
    });
    RunSelfTest(&tests, "10 Resource decode provenance", [&] {
        Require(recovered.resources.size() == 1U &&
                recovered.resources.front().relative_path == "data/monster.dat" &&
                recovered.resources.front().decode_rva == 0x1010U &&
                recovered.resources.front().deserialize_rva == 0x2020U &&
                IsSha256(recovered.resources.front().file_sha256),
                "ResourceProvenanceIncomplete");
    });
    RunSelfTest(&tests, "11 State mutation before/input/after", [&] {
        const auto found = std::find_if(recovered.mutations.begin(), recovered.mutations.end(),
            [](const auto& mutation) { return mutation.property_candidate == "HP"; });
        Require(found != recovered.mutations.end() && found->before_value == "600" &&
                found->input_value == "-40" && found->after_value == "560" &&
                found->writer_rva == 0x3000U && found->observation_count == 2U,
                "MutationCausalWitnessIncomplete");
    });
    RunSelfTest(&tests, "12 Taint seed/propagation", [&] {
        Require(recovered.value_flow.size() == 3U &&
                std::all_of(recovered.value_flow.begin(), recovered.value_flow.end(),
                    [](const auto& edge) { return edge.propagation_hops <= kMaximumTaintHops; }),
                "BoundedTaintChainMismatch");
    });
    RunSelfTest(&tests, "13 Value provenance chain", [&] {
        std::set<std::string> sources;
        std::set<std::string> targets;
        for (const auto& edge : recovered.value_flow) {
            sources.insert(edge.source_token);
            targets.insert(edge.target_token);
        }
        Require(sources.find("Packet:0x41:+4") != sources.end() &&
                targets.find("MonsterRuntime:77.TemplateId") != targets.end(),
                "ValueProvenanceEndpointsMissing");
    });
    RunSelfTest(&tests, "14 Packet schema synthesis", [&] {
        const auto found = std::find_if(recovered.packet_schemas.begin(),
            recovered.packet_schemas.end(), [](const auto& packet) {
                return packet.direction == "ServerToClient" && packet.opcode == "0x41";
            });
        Require(found != recovered.packet_schemas.end() && !found->typed_fields.empty() &&
                found->typed_fields.front().packet_offset == 4U &&
                found->typed_fields.front().width == 4U &&
                found->typed_fields.front().value_type == "uint32",
                "TypedPacketSchemaMissing");
    });
    RunSelfTest(&tests, "15 Protocol classification", [&] {
        const auto found = std::find_if(recovered.packet_schemas.begin(),
            recovered.packet_schemas.end(), [](const auto& packet) {
                return packet.opcode == "0x41";
            });
        Require(found != recovered.packet_schemas.end() && found->subsystem == "Battle" &&
                found->action_family == "BattleState", "ProtocolSubsystemClassificationMissing");
    });
    RunSelfTest(&tests, "16 Content entity normalization", [&] {
        const auto found = std::find_if(recovered.content_entities.begin(),
            recovered.content_entities.end(), [](const auto& entity) {
                return entity.canonical_identity == "Monster:4102";
            });
        Require(found != recovered.content_entities.end() &&
                found->normalized_values.at("BaseHP") == "600",
                "CanonicalContentNormalizationMissing");
    });
    RunSelfTest(&tests, "17 Base/runtime stat separation", [&] {
        const auto found = std::find_if(recovered.content_entities.begin(),
            recovered.content_entities.end(), [](const auto& entity) {
                return entity.canonical_identity == "Monster:4102";
            });
        Require(found != recovered.content_entities.end() &&
                found->base_values.at("BaseHP") == "600" &&
                found->runtime_values.at("RuntimeMaxHP") == "750" &&
                found->base_values.find("RuntimeMaxHP") == found->base_values.end(),
                "BaseRuntimeValuesConflated");
    });
    RunSelfTest(&tests, "18 Drop relation/rate authority", [&] {
        const auto found = std::find_if(recovered.content_entities.begin(),
            recovered.content_entities.end(), [](const auto& entity) {
                return entity.family == "MonsterDrop";
            });
        const auto serialized = found == recovered.content_entities.end() ?
            std::string{} : ContentJson(*found);
        Require(found != recovered.content_entities.end() &&
                found->authority == UltimateAuthority::Observed &&
                found->normalized_values.find("AuthoritativeRate") ==
                    found->normalized_values.end() &&
                serialized.find("\"authoritativeRate\":null") != std::string::npos &&
                serialized.find("\"authoritativeRateAuthority\":\"UNKNOWN_SERVER_ONLY\"") !=
                    std::string::npos &&
                serialized.find("\"defaultDisabledRate\":0") != std::string::npos &&
                serialized.find("\"enabled\":false") != std::string::npos,
                "UnknownServerRateWasFabricated");
    });
    RunSelfTest(&tests, "19 Quest objective/state recovery", [&] {
        const bool quest = std::any_of(recovered.content_entities.begin(),
            recovered.content_entities.end(), [](const auto& entity) {
                return entity.family == "Quest" &&
                    entity.normalized_values.find("ObjectiveType") != entity.normalized_values.end();
            });
        const bool fsm = std::any_of(recovered.state_machines.begin(),
            recovered.state_machines.end(), [](const auto& machine) {
                return machine.machine_id == "QuestFSM" && !machine.transitions.empty();
            });
        Require(quest && fsm, "QuestObjectiveOrStateMachineMissing");
    });
    RunSelfTest(&tests, "20 Map/portal graph", [&] {
        Require(std::any_of(recovered.graph_edges.begin(), recovered.graph_edges.end(),
            [](const auto& edge) {
                return edge.relation == "portalsTo" && edge.from == "Map:19" &&
                    edge.to == "Map:20";
            }), "PortalWorldGraphEdgeMissing");
    });
    RunSelfTest(&tests, "21 Formula candidate rejection", [&] {
        Require(recovered.formulas.size() == 1U &&
                recovered.formulas.front().authority == UltimateAuthority::Hypothesis &&
                recovered.formulas.front().status == "CandidateModel",
                "RegressionCandidateImproperlyPromoted");
    });
    RunSelfTest(&tests, "22 FSM inference/replay", [&] {
        const auto found = std::find_if(recovered.state_machines.begin(),
            recovered.state_machines.end(), [](const auto& machine) {
                return machine.machine_id == "BattleFSM";
            });
        Require(found != recovered.state_machines.end() && found->states.size() == 2U &&
                found->transitions.size() == 1U &&
                found->transitions.front().observation_count == 2U &&
                found->transitions.front().replay_consistent,
                "FsmTransitionReplayContractMismatch");
    });
    RunSelfTest(&tests, "23 GPU function clustering equivalence gate", [&] {
        const auto cpu = UltimateDeterministicMl::Analyze(fixture_input.ml_samples, {}).predictions;
        Require(!EquivalenceGate(cpu, std::nullopt), "MissingGpuResultAcceptedAsEquivalent");
        Require(EquivalenceGate(cpu, cpu), "EqualAcceleratedResultRejected");
    });
    RunSelfTest(&tests, "24 GPU object clustering equivalence gate", [&] {
        const auto cpu = UltimateDeterministicMl::Analyze(fixture_input.ml_samples, {}).predictions;
        auto different = cpu;
        different.front().cluster += 1U;
        Require(!EquivalenceGate(cpu, different), "GpuClusterMismatchAccepted");
    });
    RunSelfTest(&tests, "25 GPU graph scoring equivalence gate", [&] {
        const auto cpu = UltimateDeterministicMl::Analyze(fixture_input.ml_samples, {}).predictions;
        auto different = cpu;
        different.front().embedding.front() += 1e-4;
        Require(!EquivalenceGate(cpu, different), "GpuGraphScoreMismatchAccepted");
    });
    RunSelfTest(&tests, "26 AI/ML hypothesis authority prohibition", [&] {
        Require(!recovered.ml.predictions.empty() &&
                std::all_of(recovered.ml.predictions.begin(), recovered.ml.predictions.end(),
                    [](const auto& prediction) {
                        return prediction.authority == UltimateAuthority::Hypothesis;
                    }) &&
                std::all_of(recovered.ml.sequence_rankings.begin(),
                    recovered.ml.sequence_rankings.end(), [](const auto& ranking) {
                        return ranking.authority == UltimateAuthority::Hypothesis;
                    }), "MlOutputAcquiredEvidenceAuthority");
    });
    RunSelfTest(&tests, "27 Model absent fallback", [&] {
        Require(recovered.ml.model_status == "EvidenceBlockedModelUnavailable" &&
                !recovered.ml.predictions.empty(), "ModelAbsentFallbackContractMismatch");
    });
    RunSelfTest(&tests, "28 Ultimate Bundle integrity", [&] {
        Require(!artifacts_path.empty(), "SelfTestArtifactsPathRequired");
        UltimateBundleOptions options;
        options.output_directory = artifacts_path;
        options.package_id = "SelfTest_" + SanitizeComponent(NewId());
        bundle_fixture = WriteUltimateRecoveryBundle(recovered, options);
        if (!bundle_fixture.success || bundle_fixture.zip_sha256.empty() ||
            bundle_fixture.files.size() < 50U ||
            !fs::is_regular_file(bundle_fixture.manifest_path) ||
            !fs::is_regular_file(bundle_fixture.zip_path)) {
            throw SelfTestFailure("UltimateBundleIntegrityFailed:" + bundle_fixture.error);
        }
        const auto manifest = ReadUtf8File(bundle_fixture.manifest_path);
        const auto build_identity = ReadUtf8File(bundle_fixture.staging_directory /
            L"manifest" / L"build-identity.json");
        Require(manifest && manifest->find("god2-ultimate-package-manifest-v1") !=
                    std::string::npos &&
                manifest->find("\"relativePath\":\"manifest/package-manifest.json\"") ==
                    std::string::npos &&
                manifest->find("\"validationStatus\":\"EvidenceBlockedClientIdentityNotComputed\"") !=
                    std::string::npos &&
                manifest->find("\"exactBindingValidated\":false") != std::string::npos &&
                manifest->find("\"computedSha256\":null") != std::string::npos &&
                build_identity &&
                build_identity->find("\"bindingStatus\":\"EvidenceBlockedClientIdentityNotComputed\"") !=
                    std::string::npos &&
                build_identity->find("\"exactBindingValidated\":false") != std::string::npos,
                "ManifestSchemaOrSelfExclusionMismatch");
    });
    RunSelfTest(&tests, "29 Importer staging dry-run contract", [&] {
        Require(bundle_fixture.success, "BundleFixtureUnavailable");
        importer_fixture = RunRealImporterGate(bundle_fixture, artifacts_path);
        if (!importer_fixture.passed)
            throw SelfTestFailure("ImporterDryRunGateFailed:" + importer_fixture.status);
    });
    RunSelfTest(&tests, "30 Migration generation validation", [&] {
        const auto migration = ReadUtf8File(bundle_fixture.staging_directory /
            L"integration" / L"migration-ready.json");
        Require(importer_fixture.passed && migration &&
                migration->find("EvidenceBlockedImporterStagingRequired") !=
                    std::string::npos &&
                migration->find("IntegrationReady") == std::string::npos,
                "UnvalidatedMigrationMarkedReady");
    });
    RunSelfTest(&tests, "31 Traditional Chinese production scan", [] {
        Require(ContainsSimplifiedProductionCharacter("简体数据库"),
                "SimplifiedFixtureNotDetected");
        Require(!ContainsSimplifiedProductionCharacter("繁體資料庫"),
                "TraditionalChineseIncorrectlyRejected");
    });
    RunSelfTest(&tests, "32 Broken FK/orphan validation", [] {
        UltimateContentEntity parent;
        parent.canonical_identity = "Map:19";
        UltimateContentEntity child;
        child.canonical_identity = "Portal:88";
        child.normalized_values["TargetIdentity"] = "Map:20";
        std::vector<std::string> orphans;
        Require(!ValidateContentReferences({parent, child}, &orphans) && orphans.size() == 1U,
                "BrokenReferenceNotDetected");
        UltimateContentEntity target;
        target.canonical_identity = "Map:20";
        orphans.clear();
        Require(ValidateContentReferences({parent, child, target}, &orphans),
                "ValidReferenceRejected");
    });
    RunSelfTest(&tests, "33 Replay/oracle counterexample", [] {
        UltimateRecoveryInput input;
        input.session_id = "CounterexampleFixture";
        input.semantic_events.push_back(FixtureEvent(UltimateSemanticEventType::FormulaResult,
            1U, "{\"FormulaId\":\"F1\",\"Expression\":\"x+1\","
                "\"Result\":\"9\",\"Contradictions\":\"1\"}",
            UltimateAuthority::Hypothesis));
        const auto result = UltimateRecoveryEngine::Recover(input);
        Require(result.formulas.size() == 1U &&
                result.formulas.front().authority == UltimateAuthority::Rejected &&
                result.formulas.front().status == "RejectedByCounterexample",
                "CounterexampleDidNotRejectFormula");
    });
    RunSelfTest(&tests, "34 Stop/detach/unload bounded-work contract", [] {
        UltimatePriorityRing ring(8U);
        ring.Publish(UltimateProbeDomain::NetworkProbe, UltimateEventPriority::Authoritative,
                     FixtureEvent(UltimateSemanticEventType::PacketBoundary, 1U, "{}"));
        ring.Stop();
        Require(!ring.Publish(UltimateProbeDomain::SnapshotProbe,
            UltimateEventPriority::CandidateTelemetry,
            FixtureEvent(UltimateSemanticEventType::SnapshotObject, 2U, "{}")),
            "StoppedRingAcceptedWork");
        const auto drained = ring.DrainBatch(8U);
        Require(drained.size() == 1U && ring.Size() == 0U,
                "BoundedWorkDidNotDrainAfterStop");
    });
    RunSelfTest(&tests, "35 Partial recovery after interrupted analysis", [&] {
        auto interrupted = fixture_input;
        interrupted.interrupted = true;
        interrupted.semantic_evidence_incomplete = true;
        const auto partial = UltimateRecoveryEngine::Recover(interrupted);
        Require(partial.success && partial.partial_recovery &&
                partial.semantic_evidence_incomplete &&
                partial.status == "PartialRecoverableEvidenceIncomplete" &&
                !partial.packet_schemas.empty(), "InterruptedAnalysisNotRecoverable");
    });
    RunSelfTest(&tests, "36 VERIFIED then contradiction is sticky REJECTED", [] {
        UltimateRecoveryInput input;
        input.session_id = "DeterministicSyntheticContractFixture";
        for (std::uint64_t sequence = 700U; sequence < 702U; ++sequence) {
            auto event = FixtureEvent(UltimateSemanticEventType::StateMutation, sequence,
                "{\"ObjectIdentity\":\"MonsterRuntime:9\","
                "\"PropertyCandidate\":\"HP\",\"Type\":\"uint32\","
                "\"BeforeValue\":\"10\",\"InputValue\":\"-1\","
                "\"AfterValue\":\"9\",\"TriggerAction\":\"Hit\","
                "\"WriterRVA\":\"0x3000\"}", UltimateAuthority::Verified);
            ApplyExactFixtureBinding(&event);
            input.semantic_events.push_back(std::move(event));
        }
        auto contradiction = FixtureEvent(UltimateSemanticEventType::StateMutation, 702U,
            "{\"ObjectIdentity\":\"MonsterRuntime:9\","
            "\"PropertyCandidate\":\"HP\",\"Type\":\"uint32\","
            "\"BeforeValue\":\"10\",\"InputValue\":\"-1\","
            "\"AfterValue\":\"9\",\"TriggerAction\":\"Hit\","
            "\"WriterRVA\":\"0x3000\",\"Contradictions\":1}",
            UltimateAuthority::Rejected);
        ApplyExactFixtureBinding(&contradiction);
        input.semantic_events.push_back(std::move(contradiction));
        const auto result = UltimateRecoveryEngine::Recover(input);
        Require(result.mutations.size() == 1U &&
                result.mutations.front().authority == UltimateAuthority::Rejected &&
                result.mutations.front().status == "RejectedByContradiction",
                "LaterContradictionWasMaskedByVerifiedEvidence");
    });
    RunSelfTest(&tests, "37 Payload RecordCount cannot fake repetition", [] {
        UltimateRecoveryInput input;
        input.session_id = "DeterministicSyntheticContractFixture";
        auto registry = FixtureEvent(UltimateSemanticEventType::RegistryEnumerated, 710U,
            "{\"RegistryName\":\"FakeRegistry\",\"EntityFamily\":\"Monster\","
            "\"RecordCount\":999,\"ReadOnly\":true}", UltimateAuthority::Verified);
        ApplyExactFixtureBinding(&registry, "Registry");
        input.semantic_events.push_back(registry);
        input.semantic_events.push_back(registry);
        auto resource = FixtureEvent(UltimateSemanticEventType::ResourceDeserialized, 711U,
            "{\"RelativePath\":\"data/fake.dat\",\"EntityFamily\":\"Monster\","
            "\"RecordCount\":999,\"SchemaCandidate\":\"MonsterV1\"}",
            UltimateAuthority::Verified);
        ApplyExactFixtureBinding(&resource, "ResourceDecode");
        input.semantic_events.push_back(std::move(resource));
        const auto result = UltimateRecoveryEngine::Recover(input);
        Require(result.registries.size() == 1U && result.resources.size() == 1U &&
                result.registries.front().record_count == 999U &&
                result.registries.front().authority == UltimateAuthority::Unknown &&
                result.resources.front().authority == UltimateAuthority::Unknown,
                "PayloadRecordCountPromotedSingleEvidence");
    });
    RunSelfTest(&tests, "38 UNKNOWN_SERVER_ONLY is incomparable", [] {
        Require(CombineAuthoritiesConservatively({UltimateAuthority::Verified,
                    UltimateAuthority::UnknownServerOnly}) == UltimateAuthority::Unknown &&
                CombineAuthoritiesConservatively({UltimateAuthority::UnknownServerOnly,
                    UltimateAuthority::UnknownServerOnly}) ==
                    UltimateAuthority::UnknownServerOnly,
                "UnknownServerOnlyWasOrdinallyRanked");
    });
    RunSelfTest(&tests, "39 Actual evidence binding gates promotion", [] {
        UltimateRecoveryInput one;
        one.session_id = "DeterministicSyntheticContractFixture";
        auto first = FixtureEvent(UltimateSemanticEventType::ParserRead, 720U,
            "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x77\","
            "\"Width\":4,\"ValueType\":\"UInt32\",\"FrameOffset\":4}",
            UltimateAuthority::Verified);
        first.source_token = "PF-720";
        ApplyExactFixtureBinding(&first, "ParserReader");
        one.semantic_events.push_back(first);
        const auto single = UltimateRecoveryEngine::Recover(one);
        auto second = FixtureEvent(UltimateSemanticEventType::ParserRead, 721U,
            first.payload, UltimateAuthority::Verified);
        second.source_token = "PF-721";
        ApplyExactFixtureBinding(&second, "ParserReader");
        one.semantic_events.push_back(std::move(second));
        const auto repeated = UltimateRecoveryEngine::Recover(one);
        Require(single.packet_schemas.size() == 1U && repeated.packet_schemas.size() == 1U &&
                single.packet_schemas.front().authority == UltimateAuthority::Unknown &&
                repeated.packet_schemas.front().authority == UltimateAuthority::Verified,
                "SchemaDefinedCompanionBindingGateMismatch");
    });
    RunSelfTest(&tests, "40 Mixed recovery build demotes promotable output", [] {
        UltimateRecoveryInput input;
        input.session_id = "DeterministicSyntheticContractFixture";
        for (std::uint64_t sequence = 730U; sequence < 732U; ++sequence) {
            auto event = FixtureEvent(UltimateSemanticEventType::ParserRead, sequence,
                "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x78\","
                "\"Width\":4,\"ValueType\":\"UInt32\",\"FrameOffset\":4}",
                UltimateAuthority::Verified);
            event.source_token = "PF-" + std::to_string(sequence);
            ApplyExactFixtureBinding(&event, "ParserReader");
            input.semantic_events.push_back(std::move(event));
        }
        auto wrong = FixtureEvent(UltimateSemanticEventType::ProbeDiagnostic, 732U, "{}",
                                  UltimateAuthority::Unknown);
        wrong.client_build_id = std::string(64U, '0');
        input.semantic_events.push_back(std::move(wrong));
        const auto result = UltimateRecoveryEngine::Recover(input);
        Require(!result.exact_client_build_attested &&
                result.client_build_attestation_status ==
                    "EvidenceBlockedRecoveryBuildMismatch" &&
                result.packet_schemas.front().authority == UltimateAuthority::Unknown,
                "MixedBuildRecoveryRetainedPromotableAuthority");
    });
    RunSelfTest(&tests, "41 Mixed descriptor cannot be promotable", [] {
        UltimateRecoveryResult mixed;
        UltimateObjectRecord verified;
        verified.object_token = "A";
        verified.authority = UltimateAuthority::Verified;
        UltimateObjectRecord observed;
        observed.object_token = "B";
        observed.authority = UltimateAuthority::Observed;
        mixed.objects = {verified, observed};
        const auto drafts = BuildBundleDrafts(mixed,
            "EvidenceBlockedClientIdentityNotComputed", false, {}, {}, {});
        const auto found = std::find_if(drafts.begin(), drafts.end(), [](const auto& draft) {
            return draft.metadata.relative_path == "runtime/object-layouts.json";
        });
        Require(found != drafts.end() &&
                found->metadata.authority == UltimateAuthority::Unknown,
                "MixedFileDescriptorMasqueradedAsPromotable");
    });
    RunSelfTest(&tests, "42 Authority partitions cover all recovery models", [&] {
        Require(bundle_fixture.success, "BundleFixtureUnavailable");
        std::string all;
        const std::array<const wchar_t*, 7> names = {
            L"verified.jsonl", L"derived.jsonl", L"observed.jsonl",
            L"hypothesis.jsonl", L"unknown.jsonl", L"unknown-server-only.jsonl",
            L"rejected.jsonl"
        };
        for (const auto name : names) {
            const auto content = ReadUtf8File(bundle_fixture.staging_directory /
                L"authority" / name);
            if (content) all += *content;
        }
        constexpr std::array<std::string_view, 9> types = {
            "Registry", "Resource", "Mutation", "ValueFlow", "StateMachine",
            "Node", "Edge", "MlPrediction", "MlSequenceRanking"
        };
        Require(std::all_of(types.begin(), types.end(), [&](std::string_view type) {
            return all.find("\"recordType\":\"" + std::string(type) + "\"") !=
                std::string::npos;
        }), "AuthorityPartitionsOmittedRecoveryModel");
    });
    RunSelfTest(&tests, "43 Model metadata cannot claim runtime availability", [&] {
        const auto model_path = artifacts_path / L"model-metadata-only.bin";
        Require(WriteUtf8FileAtomic(model_path, "random-not-a-provider-model"),
                "ModelFixtureWriteFailed");
        const auto hash = CalculateFileSha256(model_path);
        Require(hash.has_value(), "ModelFixtureHashFailed");
        UltimateLocalModelArtifact artifact;
        artifact.path = model_path;
        artifact.name = "RandomBytes";
        artifact.version = "1";
        artifact.license = "MIT";
        artifact.sha256 = *hash;
        const auto metadata_only = UltimateDeterministicMl::Analyze(
            fixture_input.ml_samples, fixture_input.ml_sequences, artifact);
        artifact.sha256 = "malformed";
        const auto malformed = UltimateDeterministicMl::Analyze(
            fixture_input.ml_samples, fixture_input.ml_sequences, artifact);
        Require(metadata_only.model_status == "MetadataValidatedRuntimeUnavailable" &&
                metadata_only.model_status.find("Available") == std::string::npos &&
                malformed.model_status == "EvidenceBlockedModelUnavailable",
                "MetadataOnlyModelClaimedActiveRuntime");
    });
    RunSelfTest(&tests, "44 Coverage uses promotable numerator and verified denominator", [] {
        UltimateRecoveryInput zero;
        zero.session_id = "CoverageZero";
        zero.declared_denominators["Protocol"] = 0U;
        zero.declared_denominator_authorities["Protocol"] = UltimateAuthority::Verified;
        const auto zero_result = UltimateRecoveryEngine::Recover(zero);
        const auto zero_metric = std::find_if(zero_result.coverage.begin(),
            zero_result.coverage.end(), [](const auto& metric) {
                return metric.domain == "Protocol";
            });
        zero.declared_denominators["Protocol"] = 1U;
        zero.declared_denominator_authorities["Protocol"] = UltimateAuthority::Observed;
        const auto unverified_result = UltimateRecoveryEngine::Recover(zero);
        const auto unverified_metric = std::find_if(unverified_result.coverage.begin(),
            unverified_result.coverage.end(), [](const auto& metric) {
                return metric.domain == "Protocol";
            });
        Require(zero_metric != zero_result.coverage.end() && !zero_metric->percent &&
                zero_metric->status == "NotApplicableZeroOverZero" &&
                unverified_metric != unverified_result.coverage.end() &&
                !unverified_metric->percent &&
                unverified_metric->status ==
                    "EvidenceBlockedDenominatorProvenanceUnverified",
                "CoverageZeroOrUnverifiedDenominatorOverclaimed");
    });
    RunSelfTest(&tests, "45 Evidence graph typed nodes and witnessed edges", [&] {
        const std::set<std::string> required_relations = {
            "hasOpcode", "parsedBy", "serializedBy", "reads", "writes",
            "decodedInto", "resolvesTo", "consumes", "entersTransition", "leadsTo"
        };
        const std::set<std::string> required_kinds = {
            "Parser", "Serializer", "Class", "Property", "Transition"
        };
        Require(std::all_of(required_relations.begin(), required_relations.end(),
                    [&](const auto& relation) {
                        return std::any_of(recovered.graph_edges.begin(),
                            recovered.graph_edges.end(), [&](const auto& edge) {
                                return edge.relation == relation && !edge.evidence.empty();
                            });
                    }) &&
                std::all_of(required_kinds.begin(), required_kinds.end(),
                    [&](const auto& kind) {
                        return std::any_of(recovered.graph_nodes.begin(),
                            recovered.graph_nodes.end(), [&](const auto& node) {
                                return node.kind == kind;
                            });
                    }), "FormalEvidenceGraphContractIncomplete");
    });
    RunSelfTest(&tests, "46 Raw VERIFIED hint is not evidence authority", [] {
        auto event = FixtureEvent(UltimateSemanticEventType::ParserRead, 740U,
            "{\"Direction\":\"ServerToClient\",\"Opcode\":\"0x79\","
            "\"Width\":4,\"ValueType\":\"UInt32\"}", UltimateAuthority::Verified);
        ApplyExactFixtureBinding(&event, "ParserReader");
        const auto evidence = EvidenceFromEvent(event);
        const auto json = EvidenceRefsJson({evidence});
        Require(evidence.authority == UltimateAuthority::Unknown &&
                evidence.claimed_authority_hint == UltimateAuthority::Verified &&
                json.find("\"authority\":\"VERIFIED\"") == std::string::npos &&
                json.find("\"claimedAuthorityHint\":\"VERIFIED\"") !=
                    std::string::npos,
                "RawVerifiedHintSerializedAsValidatedAuthority");
    });
    RunSelfTest(&tests, "47 Bundle MonsterDrop typed safety line", [&] {
        const auto drops = ReadUtf8File(bundle_fixture.staging_directory /
            L"canonical" / L"monster-drops.jsonl");
        Require(drops && drops->find("\"authoritativeRate\":null") != std::string::npos &&
                drops->find("\"authoritativeRateAuthority\":\"UNKNOWN_SERVER_ONLY\"") !=
                    std::string::npos &&
                drops->find("\"defaultDisabledRate\":0") != std::string::npos &&
                drops->find("\"enabled\":false") != std::string::npos &&
                drops->find("\"AuthoritativeRate\":\"UNKNOWN_SERVER_ONLY\"") ==
                    std::string::npos,
                "BundleMonsterDropSafetyWasNotTyped");
    });
    RunSelfTest(&tests, "48 Recovery build session process attestation", [&] {
        Require(recovered.exact_client_build_attested &&
                recovered.client_build_attestation_status ==
                    "ExactRecoveryEventBuildSessionProcessAttested" &&
                bundle_fixture.client_identity_status ==
                    "EvidenceBlockedClientIdentityNotComputed",
                "RecoveryAndDiskIdentityStatesWereConflated");
    });
    RunSelfTest(&tests, "49 SensitiveMaskStatus is an active persistence gate", [&] {
        constexpr std::string_view arbitrary_secret = "S3cr3t-Value-8472";
        UltimateRecoveryInput sensitive_input;
        sensitive_input.session_id = "SensitivePersistenceFixture";
        auto sensitive = FixtureEvent(UltimateSemanticEventType::ObjectResolved, 900U,
            "{\"Property\":\"DisplayName\",\"RawValue\":\"S3cr3t-Value-8472\"," 
            "\"NormalizedValue\":\"S3cr3t-Value-8472\",\"Value\":\"S3cr3t-Value-8472\"}",
            UltimateAuthority::Verified);
        sensitive.session_id = sensitive_input.session_id;
        sensitive.sensitive_mask_status = "UnverifiedSensitiveState";
        sensitive.parent_event_id = std::string(arbitrary_secret);
        sensitive.context_id = std::string(arbitrary_secret);
        sensitive.action_id = std::string(arbitrary_secret);
        sensitive.object_token = std::string(arbitrary_secret);
        sensitive.value_token = std::string(arbitrary_secret);
        sensitive.source_token = std::string(arbitrary_secret);
        ApplyExactFixtureBinding(&sensitive, "ObjectResolver");
        sensitive.evidence_binding["ObjectAddress"] = std::string(arbitrary_secret);
        const auto serialized = SerializeSemanticEventV2(sensitive);
        const auto reparsed = ReadSemanticEvent(serialized);

        auto explicitly_safe = sensitive;
        explicitly_safe.sequence = 901U;
        explicitly_safe.event_id = "ExplicitlySafeEvent";
        explicitly_safe.sensitive_mask_status = "NotSensitive";
        explicitly_safe.parent_event_id.clear();
        explicitly_safe.context_id.clear();
        explicitly_safe.action_id.clear();
        explicitly_safe.object_token = "SAFE-OBJECT-2048";
        explicitly_safe.value_token = "SAFE-VALUE-2048";
        explicitly_safe.source_token = "SAFE-SOURCE-2048";
        explicitly_safe.payload =
            "{\"Property\":\"DisplayName\",\"RawValue\":\"SAFE-VALUE-2048\"}";
        explicitly_safe.evidence_binding.clear();
        ApplyExactFixtureBinding(&explicitly_safe, "ObjectResolver");
        const auto safe_serialized = SerializeSemanticEventV2(explicitly_safe);
        const auto safe_reparsed = ReadSemanticEvent(safe_serialized);

        sensitive_input.semantic_events.push_back(sensitive);
        const auto sensitive_recovery = UltimateRecoveryEngine::Recover(sensitive_input);
        UltimateBundleOptions sensitive_options;
        sensitive_options.output_directory = artifacts_path;
        sensitive_options.package_id = "SensitivePersistenceFixture_" +
            SanitizeComponent(NewId());
        sensitive_options.create_zip = true;
        const auto sensitive_bundle = WriteUltimateRecoveryBundle(
            sensitive_recovery, sensitive_options);
        const auto zip_bytes = sensitive_bundle.success ?
            ReadUtf8File(sensitive_bundle.zip_path) : std::optional<std::string>{};
        const auto privacy_contract = sensitive_bundle.success ?
            ReadUtf8File(sensitive_bundle.staging_directory / L"raw" / L"README.json") :
            std::optional<std::string>{};
        bool every_staged_entry_is_secret_free = sensitive_bundle.success;
        std::error_code enumerate_error;
        for (fs::recursive_directory_iterator iterator(
                sensitive_bundle.staging_directory,
                fs::directory_options::skip_permission_denied, enumerate_error), end;
             every_staged_entry_is_secret_free && iterator != end && !enumerate_error;
             iterator.increment(enumerate_error)) {
            if (!iterator->is_regular_file(enumerate_error)) continue;
            const auto content = ReadUtf8File(iterator->path());
            if (!content || content->find(arbitrary_secret) != std::string::npos)
                every_staged_entry_is_secret_free = false;
        }
        every_staged_entry_is_secret_free = every_staged_entry_is_secret_free &&
            !enumerate_error && zip_bytes &&
            zip_bytes->find(arbitrary_secret) == std::string::npos;
        const std::string sensitive_persistence_error =
            "ArbitrarySensitiveSemanticTokenPersistedIntoUltimateEvidence:" +
            (sensitive_bundle.error.empty() ? "NoBundleWriterError" : sensitive_bundle.error);
        Require(reparsed.success &&
                reparsed.event.sensitive_mask_status ==
                    "SensitivePayloadSuppressedMetadataOnly" &&
                reparsed.event.parent_event_id.empty() &&
                reparsed.event.context_id.empty() &&
                reparsed.event.action_id.empty() &&
                reparsed.event.object_token.empty() &&
                reparsed.event.value_token.empty() &&
                reparsed.event.source_token.empty() &&
                reparsed.event.evidence_binding.empty() &&
                serialized.find(arbitrary_secret) == std::string::npos &&
                serialized.find("OriginalPayloadPersisted\":false") != std::string::npos &&
                safe_reparsed.success &&
                safe_reparsed.event.object_token == "SAFE-OBJECT-2048" &&
                safe_reparsed.event.value_token == "SAFE-VALUE-2048" &&
                safe_reparsed.event.source_token == "SAFE-SOURCE-2048" &&
                safe_reparsed.event.payload.find("SAFE-VALUE-2048") != std::string::npos &&
                sensitive_recovery.semantic_evidence_incomplete &&
                sensitive_recovery.content_entities.empty() &&
                sensitive_recovery.objects.empty() &&
                sensitive_recovery.value_flow.empty() &&
                sensitive_bundle.success && every_staged_entry_is_secret_free &&
                privacy_contract &&
                privacy_contract->find("\"credentialsIncluded\":false") !=
                    std::string::npos,
                sensitive_persistence_error.c_str());
    });
    RunSelfTest(&tests, "50 Pinned client identity rejects same-path replacement", [&] {
        const auto root = artifacts_path / L"ultimate-client-identity-replacement";
        const auto original = root / L"God2_opt.exe";
        const auto replacement = root / L"replacement.exe";
        std::error_code error;
        fs::create_directories(root, error);
        Require(!error && WriteUtf8FileAtomic(original, "ORIGINAL-PINNED-CLIENT") &&
                WriteUtf8FileAtomic(replacement, "REPLACEMENT-CLIENT"),
                "ClientReplacementFixtureWriteFailed");
        UltimateScopedHandle pinned(CreateFileW(original.c_str(),
            GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Require(static_cast<bool>(pinned), "ClientReplacementFixturePinFailed");
        const BOOL replaced = MoveFileExW(replacement.c_str(), original.c_str(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
        const DWORD replace_error = replaced == FALSE ? GetLastError() : ERROR_SUCCESS;
        Require(replaced == FALSE &&
                (replace_error == ERROR_SHARING_VIOLATION ||
                 replace_error == ERROR_ACCESS_DENIED) &&
                ReadUtf8File(original) ==
                    std::optional<std::string>("ORIGINAL-PINNED-CLIENT"),
                "PinnedClientPathWasReplaceable");
    });
    RunSelfTest(&tests, "51 Pinned staging rejects junction replacement", [&] {
        const auto root = artifacts_path / L"ultimate-staging-junction-replacement";
        const auto staging = root / L"staging";
        const auto outside = root / L"outside";
        const auto link = root / L"junction-attempt";
        std::error_code error;
        fs::create_directories(staging, error);
        fs::create_directories(outside, error);
        Require(!error, "StagingJunctionFixtureCreateFailed");
        auto pinned = PinUltimateDirectory(staging);
        Require(pinned.has_value(), "StagingJunctionFixturePinFailed");
        const BOOL renamed = MoveFileExW(staging.c_str(), (root / L"old-staging").c_str(), 0);
        const DWORD rename_error = renamed == FALSE ? GetLastError() : ERROR_SUCCESS;
        const DWORD symbolic_flags = SYMBOLIC_LINK_FLAG_DIRECTORY | 0x2U;
        const BOOL link_created = CreateSymbolicLinkW(link.c_str(), outside.c_str(),
                                                       symbolic_flags);
        const DWORD link_error = link_created == FALSE ? GetLastError() : ERROR_SUCCESS;
        const bool reparse_branch_safe = link_created != FALSE ?
            !UltimatePathIsReparseFree(root, link) :
            (link_error == ERROR_PRIVILEGE_NOT_HELD ||
             link_error == ERROR_INVALID_PARAMETER ||
             link_error == ERROR_ACCESS_DENIED ||
             link_error == ERROR_NOT_SUPPORTED);
        Require(renamed == FALSE &&
                (rename_error == ERROR_SHARING_VIOLATION ||
                 rename_error == ERROR_ACCESS_DENIED) &&
                ValidatePinnedUltimateDirectory(*pinned) && reparse_branch_safe,
                "PinnedStagingCouldBeReplacedByReparseDirectory");
    });

    const auto passed = static_cast<std::size_t>(std::count_if(tests.begin(), tests.end(),
        [](const auto& test) { return test.passed; }));
    bool report_written = false;
    if (!report_path.empty()) {
        std::error_code directory_error;
        if (!report_path.parent_path().empty())
            fs::create_directories(report_path.parent_path(), directory_error);
        if (!directory_error)
            report_written = WriteUtf8FileAtomic(report_path,
                SelfTestReportJson(tests, bundle_fixture, importer_fixture,
                                   semantic_event_matrix));
    }
    return tests.size() >= 35U && passed == tests.size() && report_written ? 0 : 1;
}

} // namespace god2
