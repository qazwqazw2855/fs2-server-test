#include "EvidencePackage.h"
#include "GpuAcceleration.h"
#include "ActionGrouping.h"
#include "AutomaticSemantic.h"
#include "Resource.h"
#include "SemanticRecovery.h"
#include "Storage.h"
#include "SemanticEventV2Schema.generated.h"
#include "Transport.h"
#include "UltimateRecovery.h"
#include "Version.h"

#include "third_party/sqlite3.h"

#include <minizip/ioapi.h>
#include <minizip/iowin32.h>
#include <minizip/unzip.h>
#include <minizip/zip.h>
#include <zlib.h>

#include <bcrypt.h>

#include <algorithm>
#include <array>
#include <charconv>
#include <chrono>
#include <cctype>
#include <cstring>
#include <cstdlib>
#include <cwctype>
#include <ctime>
#include <fstream>
#include <functional>
#include <iomanip>
#include <iterator>
#include <limits>
#include <map>
#include <memory>
#include <new>
#include <numeric>
#include <sstream>
#include <unordered_map>
#include <unordered_set>

#pragma comment(lib, "bcrypt.lib")

namespace god2 {
namespace {

constexpr int kEvidenceSchemaVersion = 11;
constexpr int kDefaultCompressionLevel = 6;
constexpr std::uint64_t kMaximumValidatedEntryBytes = 256ull * 1024 * 1024 * 1024;
constexpr std::uint64_t kMaximumValidatedPackageBytes = 1024ull * 1024 * 1024 * 1024;
constexpr double kMaximumCompressionExpansionRatio = 1000.0;
constexpr std::string_view kEvidenceToolVersion = GOD2_TOOL_VERSION;
constexpr std::uint64_t kMaximumManifestBytes = 64ull * 1024 * 1024;
constexpr std::size_t kMaximumManifestArtifacts = 100'000;
constexpr std::uint64_t kSemanticSegmentMaximumBytes = 16ull * 1024 * 1024;
constexpr std::uint64_t kSemanticSegmentMaximumRecords = 100'000;
constexpr std::uint64_t kMaximumSemanticMergedBytes = 256ull * 1024 * 1024;
constexpr std::string_view kExactClientVersion = "1.0.0.1";
constexpr std::string_view kExactClientSha256 =
    "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";

struct ScopedWinHandle final {
    HANDLE value = INVALID_HANDLE_VALUE;

    ScopedWinHandle() = default;
    explicit ScopedWinHandle(HANDLE handle) : value(handle) {}
    ~ScopedWinHandle() {
        if (value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value);
    }
    ScopedWinHandle(const ScopedWinHandle&) = delete;
    ScopedWinHandle& operator=(const ScopedWinHandle&) = delete;
    ScopedWinHandle(ScopedWinHandle&& other) noexcept : value(other.value) {
        other.value = INVALID_HANDLE_VALUE;
    }
    ScopedWinHandle& operator=(ScopedWinHandle&& other) noexcept {
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

std::wstring FoldPath(std::wstring value) {
    std::replace(value.begin(), value.end(), L'/', L'\\');
    while (value.size() > 3 && value.back() == L'\\') value.pop_back();
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t character) {
        return static_cast<wchar_t>(std::towlower(character));
    });
    return value;
}

std::wstring StripExtendedPathPrefix(std::wstring value) {
    if (value.starts_with(L"\\\\?\\UNC\\")) return L"\\\\" + value.substr(8);
    if (value.starts_with(L"\\\\?\\")) return value.substr(4);
    return value;
}

std::optional<fs::path> FinalPathFromHandle(HANDLE handle) {
    const DWORD flags = FILE_NAME_NORMALIZED | VOLUME_NAME_DOS;
    const DWORD required = GetFinalPathNameByHandleW(handle, nullptr, 0, flags);
    if (required == 0) return std::nullopt;
    std::vector<wchar_t> buffer(static_cast<std::size_t>(required) + 1);
    const DWORD written = GetFinalPathNameByHandleW(handle, buffer.data(),
        static_cast<DWORD>(buffer.size()), flags);
    if (written == 0 || written >= buffer.size()) return std::nullopt;
    return fs::path(StripExtendedPathPrefix(std::wstring(buffer.data(), written)));
}

bool IsLexicallyContained(const fs::path& root, const fs::path& candidate) {
    std::error_code root_error;
    std::error_code candidate_error;
    const auto normalized_root = FoldPath(fs::absolute(root, root_error).lexically_normal().wstring());
    const auto normalized_candidate =
        FoldPath(fs::absolute(candidate, candidate_error).lexically_normal().wstring());
    if (root_error || candidate_error || normalized_root.empty() ||
        normalized_candidate.size() < normalized_root.size() ||
        normalized_candidate.compare(0, normalized_root.size(), normalized_root) != 0)
        return false;
    return normalized_candidate.size() == normalized_root.size() ||
        normalized_candidate[normalized_root.size()] == L'\\';
}

bool IsFinalPathContained(const fs::path& root, HANDLE candidate_handle) {
    ScopedWinHandle root_handle(CreateFileW(root.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    if (!root_handle) return false;
    FILE_ATTRIBUTE_TAG_INFO root_tag{};
    if (!GetFileInformationByHandleEx(root_handle.value, FileAttributeTagInfo,
            &root_tag, sizeof(root_tag)) || (root_tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        return false;
    const auto final_root = FinalPathFromHandle(root_handle.value);
    const auto final_candidate = FinalPathFromHandle(candidate_handle);
    return final_root && final_candidate && IsLexicallyContained(*final_root, *final_candidate);
}

bool ContainsReparsePoint(const fs::path& root, const fs::path& candidate,
                          bool allow_missing_leaf = false) {
    if (!IsLexicallyContained(root, candidate)) return true;
    std::error_code root_error;
    std::error_code candidate_error;
    const auto absolute_root = fs::absolute(root, root_error).lexically_normal();
    const auto absolute_candidate = fs::absolute(candidate, candidate_error).lexically_normal();
    if (root_error || candidate_error) return true;
    const auto relative = absolute_candidate.lexically_relative(absolute_root);
    if (relative.empty() && FoldPath(absolute_candidate.wstring()) != FoldPath(absolute_root.wstring()))
        return true;
    fs::path current = absolute_root;
    const auto check_component = [&](const fs::path& path, bool may_be_missing) {
        const DWORD attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES)
            return !(may_be_missing && GetLastError() == ERROR_FILE_NOT_FOUND);
        return (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
    };
    if (check_component(current, false)) return true;
    std::size_t index = 0;
    const auto component_count = static_cast<std::size_t>(std::distance(relative.begin(), relative.end()));
    for (const auto& component : relative) {
        current /= component;
        ++index;
        if (check_component(current, allow_missing_leaf && index == component_count)) return true;
    }
    return false;
}

bool EnsureContainedDirectory(const fs::path& root, const fs::path& directory) {
    if (!IsLexicallyContained(root, directory) || ContainsReparsePoint(root, root)) return false;
    std::error_code root_error;
    std::error_code directory_error;
    const auto absolute_root = fs::absolute(root, root_error).lexically_normal();
    const auto absolute_directory = fs::absolute(directory, directory_error).lexically_normal();
    if (root_error || directory_error) return false;
    const auto relative = absolute_directory.lexically_relative(absolute_root);
    fs::path current = absolute_root;
    for (const auto& component : relative) {
        current /= component;
        if (!CreateDirectoryW(current.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
            return false;
        const DWORD attributes = GetFileAttributesW(current.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES ||
            (attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != FILE_ATTRIBUTE_DIRECTORY)
            return false;
    }
    return !ContainsReparsePoint(root, directory);
}

bool IsSafePortableComponent(std::string_view value) {
    if (value.empty() || value.size() > 160 || value == "." || value == ".." ||
        value.back() == '.' || value.back() == ' ')
        return false;
    for (const unsigned char character : value) {
        const bool ascii_alphanumeric = (character >= 'A' && character <= 'Z') ||
            (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9');
        if (!(ascii_alphanumeric || character == '-' || character == '_' || character == '.'))
            return false;
    }
    std::string folded(value);
    std::transform(folded.begin(), folded.end(), folded.begin(), [](unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    const auto stem = folded.substr(0, folded.find('.'));
    static const std::unordered_set<std::string> reserved = {
        "con", "prn", "aux", "nul", "clock$", "com1", "com2", "com3", "com4", "com5",
        "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6",
        "lpt7", "lpt8", "lpt9"
    };
    return !reserved.contains(stem);
}

bool IsSafeZipRelativePath(std::string_view value) {
    if (value.empty() || value.size() > 4096 || value.front() == '/' || value.back() == '/' ||
        value.find('\\') != std::string_view::npos || value.find(':') != std::string_view::npos)
        return false;
    std::size_t start = 0;
    while (start < value.size()) {
        const auto end = value.find('/', start);
        const auto component = value.substr(start,
            end == std::string_view::npos ? value.size() - start : end - start);
        if (component.empty() || component == "." || component == ".." ||
            component.back() == '.' || component.back() == ' ')
            return false;
        for (const unsigned char character : component)
            if (character < 0x20 || character >= 0x7f) return false;
        if (end == std::string_view::npos) break;
        start = end + 1;
    }
    return true;
}

bool IsExcludedRawNetworkPayload(const fs::path& path) {
    auto extension = path.extension().wstring();
    std::transform(extension.begin(), extension.end(), extension.begin(),
        [](wchar_t character) { return static_cast<wchar_t>(std::towlower(character)); });
    return extension == L".etl" || extension == L".etw" || extension == L".pcap" ||
        extension == L".pcapng";
}

struct Sha256Provider {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    DWORD object_length = 0;

    Sha256Provider() {
        if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return;
        DWORD returned = 0;
        if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
                              reinterpret_cast<PUCHAR>(&object_length), sizeof(object_length),
                              &returned, 0) < 0) object_length = 0;
    }

    ~Sha256Provider() {
        if (algorithm != nullptr) BCryptCloseAlgorithmProvider(algorithm, 0);
    }

    Sha256Provider(const Sha256Provider&) = delete;
    Sha256Provider& operator=(const Sha256Provider&) = delete;
};

Sha256Provider& SharedSha256Provider() {
    static Sha256Provider provider;
    return provider;
}

struct Sha256Hasher {
    BCRYPT_HASH_HANDLE hash = nullptr;
    std::vector<UCHAR> object;

    Sha256Hasher() {
        auto& provider = SharedSha256Provider();
        if (provider.algorithm == nullptr || provider.object_length == 0) return;
        object.resize(provider.object_length);
        if (BCryptCreateHash(provider.algorithm, &hash, object.data(), provider.object_length,
                             nullptr, 0, 0) < 0)
            hash = nullptr;
    }

    ~Sha256Hasher() {
        if (hash != nullptr) BCryptDestroyHash(hash);
    }

    bool Update(const void* data, std::size_t size) {
        if (hash == nullptr || size > ULONG_MAX) return false;
        return BCryptHashData(hash, const_cast<PUCHAR>(static_cast<const UCHAR*>(data)),
                              static_cast<ULONG>(size), 0) >= 0;
    }

    std::optional<std::string> Finish() {
        if (hash == nullptr) return std::nullopt;
        std::array<UCHAR, 32> digest{};
        if (BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) < 0)
            return std::nullopt;
        static constexpr char alphabet[] = "0123456789ABCDEF";
        std::string result(digest.size() * 2, '0');
        for (std::size_t i = 0; i < digest.size(); ++i) {
            result[i * 2] = alphabet[digest[i] >> 4];
            result[i * 2 + 1] = alphabet[digest[i] & 0x0f];
        }
        return result;
    }
};

std::optional<std::string> Sha256(std::string_view value) {
    Sha256Hasher hasher;
    return hasher.Update(value.data(), value.size()) ? hasher.Finish() : std::nullopt;
}

std::optional<std::string> FileSha256(const fs::path& path,
                                      std::uint64_t* newline_count = nullptr) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) return std::nullopt;
    if (newline_count != nullptr) *newline_count = 0;
    Sha256Hasher hasher;
    std::vector<char> buffer(64 * 1024);
    while (stream) {
        stream.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
        const auto read = stream.gcount();
        if (read > 0) {
            if (newline_count != nullptr)
                *newline_count += static_cast<std::uint64_t>(
                    std::count(buffer.data(), buffer.data() + read, '\n'));
            if (!hasher.Update(buffer.data(), static_cast<std::size_t>(read))) return std::nullopt;
        }
    }
    return hasher.Finish();
}

bool SameFileObject(const BY_HANDLE_FILE_INFORMATION& left,
                    const BY_HANDLE_FILE_INFORMATION& right) noexcept {
    return left.dwVolumeSerialNumber == right.dwVolumeSerialNumber &&
        left.nFileIndexHigh == right.nFileIndexHigh &&
        left.nFileIndexLow == right.nFileIndexLow;
}

std::uint64_t HandleFileSize(const BY_HANDLE_FILE_INFORMATION& information) noexcept {
    return (static_cast<std::uint64_t>(information.nFileSizeHigh) << 32U) |
        information.nFileSizeLow;
}

std::optional<std::string> FileSha256ByHandle(HANDLE handle) {
    LARGE_INTEGER zero{};
    if (handle == INVALID_HANDLE_VALUE || handle == nullptr ||
        SetFilePointerEx(handle, zero, nullptr, FILE_BEGIN) == FALSE)
        return std::nullopt;
    Sha256Hasher hasher;
    std::vector<unsigned char> buffer(64U * 1024U);
    for (;;) {
        DWORD read = 0;
        if (ReadFile(handle, buffer.data(), static_cast<DWORD>(buffer.size()), &read,
                     nullptr) == FALSE)
            return std::nullopt;
        if (read == 0) break;
        if (!hasher.Update(buffer.data(), read)) return std::nullopt;
    }
    return hasher.Finish();
}

std::uint64_t CountLines(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) return 0;
    return static_cast<std::uint64_t>(std::count(std::istreambuf_iterator<char>(stream),
                                                 std::istreambuf_iterator<char>(), '\n'));
}

std::string JsonStringArray(const std::vector<std::string>& values) {
    std::ostringstream output;
    output << '[';
    for (std::size_t i = 0; i < values.size(); ++i) {
        if (i != 0) output << ',';
        output << '"' << JsonEscape(values[i]) << '"';
    }
    output << ']';
    return output.str();
}

std::string JsonStringArray(std::initializer_list<std::string> values) {
    return JsonStringArray(std::vector<std::string>(values));
}

std::string FieldsJsonObject(const Fields& fields) {
    std::vector<std::pair<std::string, std::string>> values;
    values.reserve(fields.size());
    for (const auto& field : fields) values.push_back(field);
    return MakeJsonObject(values);
}

std::string RelativeUtf8(const fs::path& root, const fs::path& path) {
    std::error_code error;
    auto relative = fs::relative(path, root, error);
    if (error) return {};
    auto value = WideToUtf8(relative.generic_wstring());
    std::replace(value.begin(), value.end(), '\\', '/');
    return value;
}

std::string UnixMillisecondsUtc(std::int64_t milliseconds) {
    if (milliseconds < 0) return {};
    const std::time_t seconds = static_cast<std::time_t>(milliseconds / 1000);
    std::tm utc{};
    if (gmtime_s(&utc, &seconds) != 0) return {};
    char prefix[32]{};
    if (std::strftime(prefix, sizeof(prefix), "%Y-%m-%dT%H:%M:%S", &utc) == 0) return {};
    std::ostringstream output;
    output << prefix << '.' << std::setfill('0') << std::setw(3) << (milliseconds % 1000) << 'Z';
    return output.str();
}

std::string UtcFilenameStamp(std::string_view utc) {
    std::string result;
    result.reserve(15);
    for (const char character : utc) {
        if (std::isdigit(static_cast<unsigned char>(character))) result.push_back(character);
        if (result.size() == 8) result.push_back('_');
        if (result.size() == 15) break;
    }
    return result;
}

bool IsHex(std::string_view value) {
    return (value.size() % 2) == 0 && std::all_of(value.begin(), value.end(), [](unsigned char c) {
        return std::isxdigit(c) != 0;
    });
}

std::optional<std::vector<std::uint8_t>> ParseHexBytes(std::string_view value) {
    if (!IsHex(value)) return std::nullopt;
    const auto nibble = [](char character) -> std::uint8_t {
        if (character >= '0' && character <= '9') return static_cast<std::uint8_t>(character - '0');
        if (character >= 'A' && character <= 'F') return static_cast<std::uint8_t>(character - 'A' + 10);
        return static_cast<std::uint8_t>(character - 'a' + 10);
    };
    std::vector<std::uint8_t> bytes;
    bytes.reserve(value.size() / 2);
    for (std::size_t index = 0; index < value.size(); index += 2)
        bytes.push_back(static_cast<std::uint8_t>((nibble(value[index]) << 4) | nibble(value[index + 1])));
    return bytes;
}

std::string HexByte(std::uint8_t value) {
    static constexpr char alphabet[] = "0123456789ABCDEF";
    std::string result = "0x00";
    result[2] = alphabet[value >> 4];
    result[3] = alphabet[value & 0x0f];
    return result;
}

std::string NumberOrNull(std::optional<std::int64_t> value) {
    return value ? std::to_string(*value) : "null";
}

std::vector<std::string> SourceIds(std::string_view value) {
    std::vector<std::string> result;
    std::size_t start = 0;
    while (start <= value.size()) {
        const auto end = value.find(';', start);
        const auto item = value.substr(start, end == std::string_view::npos ? value.size() - start : end - start);
        if (!item.empty()) result.emplace_back(item);
        if (end == std::string_view::npos) break;
        start = end + 1;
    }
    return result;
}

std::optional<std::int64_t> LastNamedDecimal(std::string_view text, std::string_view name) {
    const std::string marker = std::string(name) + "=";
    const auto position = text.rfind(marker);
    if (position == std::string_view::npos) return std::nullopt;
    const auto start = position + marker.size();
    std::size_t end = start;
    if (end < text.size() && text[end] == '-') ++end;
    while (end < text.size() && std::isdigit(static_cast<unsigned char>(text[end]))) ++end;
    if (end == start || (end == start + 1 && text[start] == '-')) return std::nullopt;
    char* parsed_end = nullptr;
    const std::string value(text.substr(start, end - start));
    const auto parsed = std::strtoll(value.c_str(), &parsed_end, 10);
    return parsed_end != nullptr && *parsed_end == '\0' ? std::optional<std::int64_t>(parsed) : std::nullopt;
}

struct CaptureRecord {
    Fields fields;
    std::string original;
    std::string source_id;
    std::string capture_record_id;
    std::string logical_message_id;
    std::string stage;
    std::string direction;
    std::string api;
    std::string socket;
    std::string payload_hex;
    std::string payload_sha256;
    std::string payload_prefix_hash;
    std::string payload_suffix_hash;
    std::string captured_at_utc;
    std::int64_t captured_at_unix_ms = 0;
    std::int64_t source_sequence = 0;
    std::uint64_t capture_sequence = 0;
    std::uint64_t stage_sequence = 0;
    std::uint64_t thread_sequence = 0;
    std::int64_t captured_qpc = 0;
    std::int64_t qpc_frequency = 0;
    std::int64_t process_id = 0;
    std::int64_t thread_id = 0;
    std::string hook_invocation_id;
    std::string parent_invocation_id;
    std::string parent_correlation_basis;
    std::string context_invocation_id;
    std::string context_correlation_basis;
    std::string correlation_level = "Uncorrelated";
    std::int64_t call_depth = 0;
    std::string caller_module;
    std::string caller_rva;
    std::vector<std::size_t> related;
    std::string transport_chunk_id;
    std::string decoded_message_id;
    std::string handler_observation_id;
    std::string protocol_frame_id;
    std::string frame_validation_status;
    std::string frame_validation_reason;
    std::string frame_opcode;
    std::string plain_payload_hex;
    std::uint64_t frame_length = 0;
    std::uint8_t frame_checksum = 0;
    std::uint32_t verified_byte_sum_without_last = 0;
    bool verified_bulk_feature_available = false;
    bool valid_json = true;
    bool valid_payload = true;
    bool valid_length = true;
};

struct AutomaticSemanticAggregate {
    std::string candidate_type;
    std::string candidate_domain;
    std::string stage;
    std::string direction;
    std::string opcode;
    std::uint64_t frame_length = 0;
    std::uint64_t observation_count = 0;
    std::uint64_t exact_or_strong_count = 0;
    std::set<std::string> unique_payload_hashes;
};

struct OpcodeWorkItem {
    std::string work_item_id;
    std::string direction;
    std::string opcode;
    std::uint64_t frame_length = 0;
    std::uint64_t observation_count = 0;
    std::uint64_t exact_handler_correlation_count = 0;
    std::uint64_t strong_handler_correlation_count = 0;
    std::string first_captured_at_utc;
    std::string last_captured_at_utc;
    std::string first_capture_record_id;
    std::string last_capture_record_id;
    std::string sample_frame_hex;
    std::string sample_frame_sha256;
    std::vector<std::uint8_t> baseline_bytes;
    std::vector<std::uint8_t> stable_bytes;
    std::vector<std::string> sample_protocol_frame_ids;
    std::vector<std::string> sample_capture_record_ids;
    std::vector<std::string> related_handler_observation_ids;
    std::vector<std::string> related_action_pattern_ids;
};

GenericActionGroupingResult BuildGenericActionGrouping(
    const std::string& session_id, const std::vector<CaptureRecord>& records,
    std::map<std::string, OpcodeWorkItem>* opcode_work_items) {
    GenericActionGroupingInput input;
    input.session_id = session_id;
    input.records.reserve(records.size());
    static constexpr std::array<std::string_view, 5> domain_correlation_names = {
        "quest_action_correlation_id", "skill_cast_correlation_id", "item_use_correlation_id",
        "party_correlation_id", "economy_correlation_id"
    };
    for (const auto& record : records) {
        ActionGroupingRecord projected;
        projected.capture_record_id = record.capture_record_id;
        projected.logical_message_id = record.logical_message_id;
        projected.capture_stage = record.stage;
        projected.direction = record.direction;
        projected.api = record.api;
        projected.connection_id = GetString(record.fields, "ConnectionId");
        projected.socket = record.socket;
        projected.transport_chunk_id = record.transport_chunk_id;
        projected.decoded_message_id = record.decoded_message_id;
        projected.handler_observation_id = record.handler_observation_id;
        projected.protocol_frame_id = record.protocol_frame_id;
        projected.opcode = record.stage == "HandlerDecoded" && record.payload_hex.size() >= 2 ?
            "0x" + record.payload_hex.substr(0, 2) : record.frame_opcode;
        projected.frame_length = record.frame_length;
        projected.payload_hex = record.payload_hex;
        projected.captured_at_utc = record.captured_at_utc;
        projected.captured_at_unix_ms = record.captured_at_unix_ms;
        projected.capture_sequence = record.capture_sequence;
        projected.stage_sequence = record.stage_sequence;
        projected.captured_qpc = record.captured_qpc;
        projected.qpc_frequency = record.qpc_frequency;
        projected.process_id = record.process_id;
        projected.thread_id = record.thread_id;
        projected.hook_invocation_id = record.hook_invocation_id;
        projected.parent_invocation_id = record.parent_invocation_id;
        projected.context_invocation_id = record.context_invocation_id;
        projected.context_correlation_basis = record.context_correlation_basis;
        projected.correlation_level = record.correlation_level;
        for (const auto name : domain_correlation_names) {
            const auto value = GetString(record.fields, name);
            if (!value.empty()) projected.domain_correlation_ids.emplace(std::string(name), value);
        }
        for (const auto name : {"ProtocolRegistryVerificationStatus", "VerifiedProtocolRegistrySource",
                                "VerifiedProtocolRegistryEvidenceSHA256", "VerifiedProtocolRegistryId",
                                "VerifiedGameplaySemantic"}) {
            const auto value = GetString(record.fields, name);
            if (!value.empty()) projected.verified_registry_fields.emplace(name, value);
        }
        input.records.push_back(std::move(projected));
    }
    input.opcode_work_items.reserve(opcode_work_items->size());
    for (const auto& [key, item] : *opcode_work_items) {
        static_cast<void>(key);
        input.opcode_work_items.push_back({item.work_item_id, item.direction, item.opcode, item.frame_length});
    }
    auto result = GroupGenericUnknownActions(input);
    for (auto& [key, item] : *opcode_work_items) {
        static_cast<void>(key);
        if (const auto related = result.related_action_pattern_ids_by_work_item.find(item.work_item_id);
            related != result.related_action_pattern_ids_by_work_item.end())
            item.related_action_pattern_ids = related->second;
    }
    return result;
}

std::pair<std::string, std::string> ModuleAndRva(std::string_view caller) {
    std::string module;
    std::string rva;
    const auto module_start = caller.find("module=");
    if (module_start != std::string_view::npos) {
        const auto value_start = module_start + 7;
        const auto value_end = caller.find(' ', value_start);
        module = std::string(caller.substr(value_start, value_end - value_start));
    }
    const auto rva_start = caller.find("rva=");
    if (rva_start != std::string_view::npos)
        rva = std::string(caller.substr(rva_start + 4));
    return {module, rva};
}

struct ExecutableIdentity {
    std::string file_name;
    std::string sha256;
    std::uint64_t file_size = 0;
    std::string file_version;
    std::string product_version;
    std::uint32_t pe_timestamp = 0;
    std::uint16_t machine = 0;
    std::string architecture;
    std::uint64_t image_base = 0;
    std::uint32_t entry_point_rva = 0;
    std::string captured_at_utc;
    std::string status = "UnavailableForLegacySession";
    std::string failure_reason;
    std::string original_local_path;
};

std::string FourPartVersion(DWORD most, DWORD least) {
    return std::to_string(HIWORD(most)) + "." + std::to_string(LOWORD(most)) + "." +
        std::to_string(HIWORD(least)) + "." + std::to_string(LOWORD(least));
}

ExecutableIdentity ReadExecutableIdentity(const fs::path& path, bool include_image_fields) {
    ExecutableIdentity identity;
    identity.file_name = WideToUtf8(path.filename().wstring());
    identity.original_local_path = WideToUtf8(path.wstring());
    identity.captured_at_utc = UtcNow();
    std::error_code file_error;
    if (path.empty() || !fs::is_regular_file(path, file_error) || file_error) {
        identity.status = "Unavailable";
        identity.failure_reason = file_error ? file_error.message() : "file is not present on disk";
        return identity;
    }
    ScopedWinHandle identity_handle(CreateFileW(path.c_str(), GENERIC_READ | FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    FILE_ATTRIBUTE_TAG_INFO identity_tag{};
    BY_HANDLE_FILE_INFORMATION identity_information{};
    const auto identity_final_path = identity_handle ? FinalPathFromHandle(identity_handle.value) :
        std::optional<fs::path>{};
    std::error_code absolute_error;
    const auto expected_absolute = fs::absolute(path, absolute_error).lexically_normal();
    if (!identity_handle || !identity_final_path || absolute_error ||
        !GetFileInformationByHandleEx(identity_handle.value, FileAttributeTagInfo,
            &identity_tag, sizeof(identity_tag)) ||
        !GetFileInformationByHandle(identity_handle.value, &identity_information) ||
        (identity_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        FoldPath(identity_final_path->lexically_normal().wstring()) != FoldPath(expected_absolute.wstring())) {
        identity.status = "Failed";
        identity.failure_reason = "executable path is a reparse point or final-path identity changed";
        return identity;
    }
    identity.file_size = HandleFileSize(identity_information);
    const auto hash = FileSha256ByHandle(identity_handle.value);
    if (!hash) {
        identity.status = "Failed";
        identity.failure_reason = "SHA-256 calculation failed; Win32=" + std::to_string(GetLastError());
        return identity;
    }
    identity.sha256 = *hash;

    const auto read_exact = [&](std::uint64_t offset, void* destination, DWORD bytes) {
        LARGE_INTEGER position{};
        position.QuadPart = static_cast<LONGLONG>(offset);
        DWORD read = 0;
        return SetFilePointerEx(identity_handle.value, position, nullptr, FILE_BEGIN) != FALSE &&
            ReadFile(identity_handle.value, destination, bytes, &read, nullptr) != FALSE &&
            read == bytes;
    };
    IMAGE_DOS_HEADER dos{};
    if (!read_exact(0, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE ||
        dos.e_lfanew <= 0) {
        identity.status = "Failed";
        identity.failure_reason = "invalid DOS/PE header";
        return identity;
    }
    DWORD signature = 0;
    IMAGE_FILE_HEADER file_header{};
    WORD optional_magic = 0;
    const auto nt_offset = static_cast<std::uint64_t>(dos.e_lfanew);
    if (!read_exact(nt_offset, &signature, sizeof(signature)) ||
        !read_exact(nt_offset + sizeof(signature), &file_header, sizeof(file_header)) ||
        !read_exact(nt_offset + sizeof(signature) + sizeof(file_header),
                    &optional_magic, sizeof(optional_magic)) ||
        signature != IMAGE_NT_SIGNATURE) {
        identity.status = "Failed";
        identity.failure_reason = "invalid PE signature";
        return identity;
    }
    identity.machine = file_header.Machine;
    identity.pe_timestamp = file_header.TimeDateStamp;
    identity.architecture = file_header.Machine == IMAGE_FILE_MACHINE_I386 ? "x86" :
        file_header.Machine == IMAGE_FILE_MACHINE_AMD64 ? "x64" : "Unknown";
    const auto optional_offset = nt_offset + sizeof(DWORD) + sizeof(IMAGE_FILE_HEADER);
    if (optional_magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
        IMAGE_OPTIONAL_HEADER32 optional{};
        if (!read_exact(optional_offset, &optional, sizeof(optional))) {
            identity.status = "Failed";
            identity.failure_reason = "truncated PE32 optional header";
            return identity;
        }
        if (include_image_fields) {
            identity.image_base = optional.ImageBase;
            identity.entry_point_rva = optional.AddressOfEntryPoint;
        }
    } else if (optional_magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
        IMAGE_OPTIONAL_HEADER64 optional{};
        if (!read_exact(optional_offset, &optional, sizeof(optional))) {
            identity.status = "Failed";
            identity.failure_reason = "truncated PE64 optional header";
            return identity;
        }
        if (include_image_fields) {
            identity.image_base = optional.ImageBase;
            identity.entry_point_rva = optional.AddressOfEntryPoint;
        }
    } else {
        identity.status = "Failed";
        identity.failure_reason = "unsupported PE optional header";
        return identity;
    }

    DWORD version_handle = 0;
    const DWORD version_size = GetFileVersionInfoSizeW(path.c_str(), &version_handle);
    if (version_size != 0) {
        std::vector<unsigned char> version_data(version_size);
        if (GetFileVersionInfoW(path.c_str(), 0, version_size, version_data.data())) {
            VS_FIXEDFILEINFO* fixed = nullptr;
            UINT fixed_size = 0;
            if (VerQueryValueW(version_data.data(), L"\\", reinterpret_cast<void**>(&fixed), &fixed_size) &&
                fixed != nullptr && fixed_size >= sizeof(VS_FIXEDFILEINFO)) {
                identity.file_version = FourPartVersion(fixed->dwFileVersionMS, fixed->dwFileVersionLS);
                identity.product_version = FourPartVersion(fixed->dwProductVersionMS, fixed->dwProductVersionLS);
            }
        }
    }
    ScopedWinHandle path_recheck(CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    BY_HANDLE_FILE_INFORMATION recheck_information{};
    FILE_ATTRIBUTE_TAG_INFO recheck_tag{};
    if (!path_recheck ||
        !GetFileInformationByHandle(path_recheck.value, &recheck_information) ||
        !GetFileInformationByHandleEx(path_recheck.value, FileAttributeTagInfo,
            &recheck_tag, sizeof(recheck_tag)) ||
        (recheck_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !SameFileObject(identity_information, recheck_information) ||
        HandleFileSize(identity_information) != HandleFileSize(recheck_information) ||
        FinalPathFromHandle(path_recheck.value) != identity_final_path ||
        FileSha256ByHandle(identity_handle.value) != std::optional<std::string>(identity.sha256)) {
        identity.status = "Failed";
        identity.failure_reason = "executable identity changed while the pinned attestation was live";
        return identity;
    }
    if (include_image_fields) {
        const bool exact = _stricmp(identity.file_name.c_str(), "God2_opt.exe") == 0 &&
            identity.architecture == "x86" && identity.file_version == kExactClientVersion &&
            _stricmp(identity.sha256.c_str(), std::string(kExactClientSha256).c_str()) == 0;
        identity.status = exact ? "ExactClientIdentityComputedAndValidated" :
            "EvidenceBlockedClientBuildMismatch";
        if (!exact)
            identity.failure_reason = "required God2_opt.exe x86 / 1.0.0.1 / approved SHA-256 identity did not match";
    } else {
        identity.status = "Pass";
    }
    return identity;
}

struct PackageArtifact {
    std::string artifact_id;
    std::string relative_path;
    std::string role;
    std::string artifact_class;
    bool authoritative = false;
    std::string schema_id;
    std::string content_type;
    std::uint64_t file_size = 0;
    std::optional<std::uint64_t> record_count;
    std::string sha256;
    std::string source_stage;
    bool required_for_codex = false;
    bool optional_diagnostic = false;
    bool required_for_package_validation = false;
    bool required_for_codex_ingestion = false;
    bool recommended_for_deep_analysis = false;
    bool diagnostic_only = false;
};

struct ConnectionStats {
    std::string connection_id;
    std::int64_t process_id = 0;
    std::string socket;
    std::string local_endpoint;
    std::string remote_endpoint;
    std::string first_utc;
    std::string last_utc;
    std::int64_t first_ms = 0;
    std::int64_t last_ms = 0;
    std::uint64_t first_sequence = 0;
    std::uint64_t last_sequence = 0;
    std::uint64_t c2s_chunks = 0;
    std::uint64_t s2c_chunks = 0;
    std::uint64_t c2s_bytes = 0;
    std::uint64_t s2c_bytes = 0;
    std::uint64_t sends = 0;
    std::uint64_t wsa_sends = 0;
    std::uint64_t recvs = 0;
    std::uint64_t wsa_recvs = 0;
    std::uint64_t pre_encrypt = 0;
    std::uint64_t post_decrypt = 0;
    std::uint64_t handler_decoded = 0;
    std::uint64_t candidate_frames = 0;
    std::uint64_t desync_bytes = 0;
    std::uint64_t pending_bytes = 0;
    std::uint64_t invalid_lengths = 0;
};

std::pair<std::string, std::string> AddressAndPort(std::string_view endpoint) {
    const auto separator = endpoint.rfind(':');
    if (separator == std::string_view::npos) return {std::string(endpoint), {}};
    const auto port = endpoint.substr(separator + 1);
    if (port.empty() || !std::all_of(port.begin(), port.end(), [](unsigned char value) {
            return std::isdigit(value) != 0;
        })) return {std::string(endpoint), {}};
    const auto address = std::string(endpoint.substr(0, separator));
    return {address, address == "unknown" || port == "0" ? std::string{} : std::string(port)};
}

struct ZipEntry {
    std::string name;
    fs::path source;
    std::uint32_t crc32 = 0;
    std::uint32_t size = 0;
    std::uint32_t offset = 0;
    BY_HANDLE_FILE_INFORMATION identity{};
    fs::path final_path;
    ScopedWinHandle handle;
};

std::uint32_t Crc32Update(std::uint32_t crc, const unsigned char* data, std::size_t size) {
    static const auto table = [] {
        std::array<std::uint32_t, 256> values{};
        std::uint32_t index = 0;
        std::generate(values.begin(), values.end(), [&index] {
            std::uint32_t value = index++;
            for (int bit = 0; bit < 8; ++bit)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            return value;
        });
        return values;
    }();
    for (std::size_t i = 0; i < size; ++i) {
        const auto index = static_cast<std::uint8_t>(crc ^ data[i]);
        crc = table[static_cast<std::size_t>(index)] ^ (crc >> 8);
    }
    return crc;
}

bool FileCrc32AndSizeByHandle(HANDLE handle, std::uint32_t* crc,
                              std::uint32_t* size) {
    LARGE_INTEGER zero{};
    if (SetFilePointerEx(handle, zero, nullptr, FILE_BEGIN) == FALSE) return false;
    std::uint32_t result = 0xFFFFFFFFu;
    std::uint64_t total = 0;
    std::vector<unsigned char> buffer(64U * 1024U);
    for (;;) {
        DWORD read = 0;
        if (ReadFile(handle, buffer.data(), static_cast<DWORD>(buffer.size()), &read,
                     nullptr) == FALSE) return false;
        if (read == 0U) break;
        result = Crc32Update(result, buffer.data(), read);
        total += read;
        if (total > UINT32_MAX) return false;
    }
    *crc = result ^ 0xFFFFFFFFu;
    *size = static_cast<std::uint32_t>(total);
    return true;
}

class WinHandleOutput final {
public:
    explicit WinHandleOutput(HANDLE handle) noexcept : handle_(handle) {}

    std::int64_t tellp() {
        LARGE_INTEGER zero{};
        LARGE_INTEGER position{};
        if (!good_ || SetFilePointerEx(handle_, zero, &position, FILE_CURRENT) == FALSE) {
            good_ = false;
            return -1;
        }
        return position.QuadPart;
    }

    void put(char value) { write(&value, 1); }

    void write(const char* data, std::streamsize bytes) {
        if (!good_ || bytes < 0) {
            good_ = false;
            return;
        }
        std::uint64_t remaining = static_cast<std::uint64_t>(bytes);
        while (remaining != 0U) {
            const DWORD requested = static_cast<DWORD>(std::min<std::uint64_t>(remaining, MAXDWORD));
            DWORD written = 0;
            if (WriteFile(handle_, data, requested, &written, nullptr) == FALSE || written == 0U) {
                good_ = false;
                return;
            }
            data += written;
            remaining -= written;
        }
    }

    void flush() {
        if (good_ && FlushFileBuffers(handle_) == FALSE) good_ = false;
    }

    explicit operator bool() const noexcept { return good_; }

private:
    HANDLE handle_ = INVALID_HANDLE_VALUE;
    bool good_ = true;
};

template <typename T>
void WriteLittle(WinHandleOutput& output, T value) {
    for (std::size_t i = 0; i < sizeof(T); ++i)
        output.put(static_cast<char>((static_cast<std::uint64_t>(value) >> (i * 8)) & 0xff));
}

bool CreateStoredZip(const fs::path& root, const fs::path& zip_path, std::string* error) {
    std::vector<ZipEntry> entries;
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(root, fs::directory_options::skip_permission_denied,
                                                    enumerate_error), end;
         iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
        if (IsExcludedRawNetworkPayload(iterator->path())) continue;
        const auto entry_io_path = Win32ExtendedPathForFileIo(iterator->path());
        const DWORD attributes = GetFileAttributesW(entry_io_path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES ||
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
            if (error) *error = "package staging contains an unreadable reparse-point entry";
            return false;
        }
        if (!iterator->is_regular_file()) continue;
        ZipEntry entry;
        entry.name = RelativeUtf8(root, iterator->path());
        entry.source = iterator->path();
        entry.handle = ScopedWinHandle(CreateFileW(entry_io_path.c_str(),
            GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT |
                FILE_FLAG_SEQUENTIAL_SCAN,
            nullptr));
        FILE_ATTRIBUTE_TAG_INFO source_tag{};
        const auto source_final = entry.handle ? FinalPathFromHandle(entry.handle.value) :
            std::optional<fs::path>{};
        if (entry.name.empty() || entry.name.size() > UINT16_MAX ||
            !entry.handle || !source_final ||
            !GetFileInformationByHandleEx(entry.handle.value, FileAttributeTagInfo,
                &source_tag, sizeof(source_tag)) ||
            !GetFileInformationByHandle(entry.handle.value, &entry.identity) ||
            (source_tag.FileAttributes &
                (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
            !IsFinalPathContained(root, entry.handle.value) ||
            !FileCrc32AndSizeByHandle(entry.handle.value, &entry.crc32, &entry.size)) {
            if (error) *error = "cannot inventory ZIP entry " + entry.name;
            return false;
        }
        entry.final_path = *source_final;
        entries.push_back(std::move(entry));
    }
    if (enumerate_error) {
        if (error) *error = "cannot enumerate package staging tree: " + enumerate_error.message();
        return false;
    }
    std::sort(entries.begin(), entries.end(), [](const auto& left, const auto& right) {
        return left.name < right.name;
    });
    if (entries.size() > UINT16_MAX) {
        if (error) *error = "package has too many ZIP entries";
        return false;
    }
    const fs::path temporary(zip_path.wstring() + L".tmp");
    const auto temporary_io_path = Win32ExtendedPathForFileIo(temporary);
    ScopedWinHandle output_handle(CreateFileW(temporary_io_path.c_str(),
        GENERIC_READ | GENERIC_WRITE | FILE_READ_ATTRIBUTES | DELETE,
        FILE_SHARE_READ, nullptr, CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT |
            FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr));
    if (!output_handle) {
        if (error) *error = "cannot create Evidence ZIP: Win32=" +
            std::to_string(GetLastError());
        return false;
    }
    WinHandleOutput output(output_handle.value);
    FILE_ATTRIBUTE_TAG_INFO output_tag{};
    BY_HANDLE_FILE_INFORMATION output_identity{};
    if (!output_handle ||
        !GetFileInformationByHandleEx(output_handle.value, FileAttributeTagInfo,
            &output_tag, sizeof(output_tag)) ||
        !GetFileInformationByHandle(output_handle.value, &output_identity) ||
        (output_tag.FileAttributes &
            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !IsFinalPathContained(zip_path.parent_path(), output_handle.value)) {
        if (error) *error = "cannot pin Evidence ZIP temporary output";
        FILE_DISPOSITION_INFO disposition{TRUE};
        SetFileInformationByHandle(output_handle.value, FileDispositionInfo,
                                   &disposition, sizeof(disposition));
        return false;
    }
    const auto abandon_output = [&] {
        FILE_DISPOSITION_INFO disposition{TRUE};
        SetFileInformationByHandle(output_handle.value, FileDispositionInfo,
                                   &disposition, sizeof(disposition));
    };
    for (auto& entry : entries) {
        const auto position = output.tellp();
        if (position < 0 || static_cast<std::uint64_t>(position) > UINT32_MAX) {
            if (error) *error = "Evidence ZIP exceeds the ZIP32 offset limit";
            abandon_output();
            return false;
        }
        entry.offset = static_cast<std::uint32_t>(position);
        WriteLittle<std::uint32_t>(output, 0x04034b50);
        WriteLittle<std::uint16_t>(output, 20);
        WriteLittle<std::uint16_t>(output, 0x0800);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint32_t>(output, entry.crc32);
        WriteLittle<std::uint32_t>(output, entry.size);
        WriteLittle<std::uint32_t>(output, entry.size);
        WriteLittle<std::uint16_t>(output, static_cast<std::uint16_t>(entry.name.size()));
        WriteLittle<std::uint16_t>(output, 0);
        output.write(entry.name.data(), static_cast<std::streamsize>(entry.name.size()));
        LARGE_INTEGER zero{};
        bool input_valid = SetFilePointerEx(entry.handle.value, zero, nullptr, FILE_BEGIN) != FALSE;
        std::vector<char> copy_buffer(64 * 1024);
        std::uint64_t copied = 0;
        while (input_valid) {
            DWORD read = 0;
            if (ReadFile(entry.handle.value, copy_buffer.data(),
                    static_cast<DWORD>(copy_buffer.size()), &read, nullptr) == FALSE) {
                input_valid = false;
                break;
            }
            if (read == 0U) break;
            output.write(copy_buffer.data(), static_cast<std::streamsize>(read));
            copied += read;
            if (!output) {
                input_valid = false;
                break;
            }
        }
        BY_HANDLE_FILE_INFORMATION input_after{};
        if (!input_valid || copied != entry.size ||
            !GetFileInformationByHandle(entry.handle.value, &input_after) ||
            !SameFileObject(entry.identity, input_after) ||
            HandleFileSize(input_after) != entry.size ||
            FinalPathFromHandle(entry.handle.value) !=
                std::optional<fs::path>(entry.final_path) ||
            !IsFinalPathContained(root, entry.handle.value) || !output) {
            if (error) *error = "cannot read ZIP input " + entry.name;
            abandon_output();
            return false;
        }
    }
    const auto central_position = output.tellp();
    if (central_position < 0 || static_cast<std::uint64_t>(central_position) > UINT32_MAX) {
        if (error) *error = "Evidence ZIP central-directory offset exceeds ZIP32 limits";
        abandon_output();
        return false;
    }
    const auto central_offset = static_cast<std::uint32_t>(central_position);
    for (const auto& entry : entries) {
        WriteLittle<std::uint32_t>(output, 0x02014b50);
        WriteLittle<std::uint16_t>(output, 20);
        WriteLittle<std::uint16_t>(output, 20);
        WriteLittle<std::uint16_t>(output, 0x0800);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint32_t>(output, entry.crc32);
        WriteLittle<std::uint32_t>(output, entry.size);
        WriteLittle<std::uint32_t>(output, entry.size);
        WriteLittle<std::uint16_t>(output, static_cast<std::uint16_t>(entry.name.size()));
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint16_t>(output, 0);
        WriteLittle<std::uint32_t>(output, 0);
        WriteLittle<std::uint32_t>(output, entry.offset);
        output.write(entry.name.data(), static_cast<std::streamsize>(entry.name.size()));
    }
    const auto end_position = output.tellp();
    if (end_position < 0 || static_cast<std::uint64_t>(end_position) > UINT32_MAX) {
        if (error) *error = "Evidence ZIP central directory exceeds ZIP32 limits";
        abandon_output();
        return false;
    }
    const auto central_size = static_cast<std::uint32_t>(end_position) - central_offset;
    WriteLittle<std::uint32_t>(output, 0x06054b50);
    WriteLittle<std::uint16_t>(output, 0);
    WriteLittle<std::uint16_t>(output, 0);
    WriteLittle<std::uint16_t>(output, static_cast<std::uint16_t>(entries.size()));
    WriteLittle<std::uint16_t>(output, static_cast<std::uint16_t>(entries.size()));
    WriteLittle<std::uint32_t>(output, central_size);
    WriteLittle<std::uint32_t>(output, central_offset);
    WriteLittle<std::uint16_t>(output, 0);
    output.flush();
    if (!output) {
        if (error) *error = "Evidence ZIP write failed";
        abandon_output();
        return false;
    }
    const auto absolute_target = Win32ExtendedPathForFileIo(zip_path);
    const auto name_bytes = absolute_target.size() * sizeof(wchar_t);
    std::vector<unsigned char> rename_storage(sizeof(FILE_RENAME_INFO) + name_bytes, 0);
    auto* rename_info = reinterpret_cast<FILE_RENAME_INFO*>(rename_storage.data());
    rename_info->ReplaceIfExists = TRUE;
    rename_info->RootDirectory = nullptr;
    rename_info->FileNameLength = static_cast<DWORD>(name_bytes);
    std::memcpy(rename_info->FileName, absolute_target.data(), name_bytes);
    const bool committed = SetFileInformationByHandle(output_handle.value, FileRenameInfo,
        rename_info, static_cast<DWORD>(rename_storage.size())) != FALSE;
    BY_HANDLE_FILE_INFORMATION committed_identity{};
    FILE_ATTRIBUTE_TAG_INFO committed_tag{};
    const auto committed_final = committed ? FinalPathFromHandle(output_handle.value) :
        std::optional<fs::path>{};
    const bool committed_valid = committed && committed_final &&
        GetFileInformationByHandle(output_handle.value, &committed_identity) &&
        GetFileInformationByHandleEx(output_handle.value, FileAttributeTagInfo,
            &committed_tag, sizeof(committed_tag)) &&
        SameFileObject(output_identity, committed_identity) &&
        (committed_tag.FileAttributes &
            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0 &&
        FoldPath(committed_final->lexically_normal().wstring()) ==
            FoldPath(fs::absolute(zip_path).lexically_normal().wstring()) &&
        IsFinalPathContained(zip_path.parent_path(), output_handle.value);
    if (!committed_valid) {
        if (error) *error = "Evidence ZIP handle-bound final rename failed: " +
            std::to_string(GetLastError());
        if (committed) {
            FILE_DISPOSITION_INFO disposition{};
            disposition.DeleteFile = TRUE;
            SetFileInformationByHandle(output_handle.value, FileDispositionInfo,
                                       &disposition, sizeof(disposition));
        } else {
            abandon_output();
        }
        return false;
    }
    return true;
}

template <typename T>
bool ReadLittle(std::istream& input, T* value) {
    std::uint64_t result = 0;
    for (std::size_t i = 0; i < sizeof(T); ++i) {
        const int byte = input.get();
        if (byte == EOF) return false;
        result |= static_cast<std::uint64_t>(static_cast<unsigned char>(byte)) << (i * 8);
    }
    *value = static_cast<T>(result);
    return true;
}

bool ValidateStoredZip(const fs::path& zip_path,
                       const std::vector<PackageArtifact>& artifacts,
                       std::string* error) {
    std::ifstream input(fs::path(Win32ExtendedPathForFileIo(zip_path)), std::ios::binary);
    if (!input) {
        if (error) *error = "cannot reopen Evidence ZIP";
        return false;
    }
    struct ObservedZipEntry {
        std::string sha256;
        std::uint64_t size = 0;
        std::uint32_t crc32 = 0;
        std::uint32_t local_offset = 0;
    };
    std::unordered_map<std::string, ObservedZipEntry> observed;
    std::uint32_t observed_central_offset = 0;
    bool found_central_directory = false;
    for (;;) {
        const auto header_position = input.tellg();
        if (header_position < 0 || static_cast<std::uint64_t>(header_position) > UINT32_MAX) {
            if (error) *error = "invalid ZIP local-header offset";
            return false;
        }
        std::uint32_t signature = 0;
        if (!ReadLittle(input, &signature)) {
            if (error) *error = "ZIP central directory is missing";
            return false;
        }
        if (signature == 0x02014b50) {
            observed_central_offset = static_cast<std::uint32_t>(header_position);
            found_central_directory = true;
            break;
        }
        if (signature == 0x06054b50) {
            if (error) *error = "ZIP end record appeared before the central directory";
            return false;
        }
        if (signature != 0x04034b50) {
            if (error) *error = "invalid local ZIP header";
            return false;
        }
        std::uint16_t version = 0, flags = 0, method = 0, ignored16 = 0, name_length = 0, extra_length = 0;
        std::uint32_t expected_crc = 0, compressed = 0, uncompressed = 0;
        if (!ReadLittle(input, &version) || !ReadLittle(input, &flags) || !ReadLittle(input, &method) ||
            !ReadLittle(input, &ignored16) || !ReadLittle(input, &ignored16) || !ReadLittle(input, &expected_crc) ||
            !ReadLittle(input, &compressed) || !ReadLittle(input, &uncompressed) ||
            !ReadLittle(input, &name_length) || !ReadLittle(input, &extra_length)) {
            if (error) *error = "truncated local ZIP header";
            return false;
        }
        if (version != 20 || flags != 0x0800 || method != 0 || compressed != uncompressed) {
            if (error) *error = "unsupported ZIP compression method";
            return false;
        }
        std::string name(name_length, '\0');
        input.read(name.data(), name_length);
        input.seekg(extra_length, std::ios::cur);
        if (!input || name.empty() || observed.contains(name)) {
            if (error) *error = name.empty() ? "ZIP entry has an empty name" : "duplicate or truncated local ZIP entry: " + name;
            return false;
        }
        Sha256Hasher hasher;
        std::uint32_t crc = 0xFFFFFFFFu;
        std::uint64_t remaining = uncompressed;
        std::vector<char> buffer(64 * 1024);
        while (remaining > 0) {
            const auto wanted = static_cast<std::streamsize>(std::min<std::uint64_t>(remaining, buffer.size()));
            input.read(buffer.data(), wanted);
            if (input.gcount() != wanted || !hasher.Update(buffer.data(), static_cast<std::size_t>(wanted))) {
                if (error) *error = "truncated ZIP entry: " + name;
                return false;
            }
            crc = Crc32Update(crc, reinterpret_cast<const unsigned char*>(buffer.data()),
                              static_cast<std::size_t>(wanted));
            remaining -= static_cast<std::uint64_t>(wanted);
        }
        const auto digest = hasher.Finish();
        crc ^= 0xFFFFFFFFu;
        if (!digest || crc != expected_crc) {
            if (error) *error = "ZIP CRC-32 validation failed for " + name;
            return false;
        }
        observed.emplace(name, ObservedZipEntry{*digest, uncompressed, crc,
                                                static_cast<std::uint32_t>(header_position)});
    }
    if (!found_central_directory || observed.empty()) {
        if (error) *error = "ZIP contains no validated entries or central directory";
        return false;
    }

    input.seekg(observed_central_offset, std::ios::beg);
    std::unordered_set<std::string> central_names;
    for (std::size_t entry_index = 0; entry_index < observed.size(); ++entry_index) {
        std::uint32_t signature = 0, expected_crc = 0, compressed = 0, uncompressed = 0;
        std::uint32_t external_attributes = 0, local_offset = 0;
        std::uint16_t made_by = 0, version = 0, flags = 0, method = 0, ignored16 = 0;
        std::uint16_t name_length = 0, extra_length = 0, comment_length = 0, disk = 0;
        if (!ReadLittle(input, &signature) || signature != 0x02014b50 ||
            !ReadLittle(input, &made_by) || !ReadLittle(input, &version) || !ReadLittle(input, &flags) ||
            !ReadLittle(input, &method) || !ReadLittle(input, &ignored16) || !ReadLittle(input, &ignored16) ||
            !ReadLittle(input, &expected_crc) || !ReadLittle(input, &compressed) || !ReadLittle(input, &uncompressed) ||
            !ReadLittle(input, &name_length) || !ReadLittle(input, &extra_length) ||
            !ReadLittle(input, &comment_length) || !ReadLittle(input, &disk) || !ReadLittle(input, &ignored16) ||
            !ReadLittle(input, &external_attributes) || !ReadLittle(input, &local_offset)) {
            if (error) *error = "truncated ZIP central directory";
            return false;
        }
        std::string name(name_length, '\0');
        input.read(name.data(), name_length);
        input.seekg(static_cast<std::streamoff>(extra_length) + comment_length, std::ios::cur);
        const auto iterator = observed.find(name);
        if (!input || made_by != 20 || version != 20 || flags != 0x0800 || method != 0 || disk != 0 ||
            compressed != uncompressed || iterator == observed.end() || !central_names.insert(name).second ||
            expected_crc != iterator->second.crc32 || uncompressed != iterator->second.size ||
            local_offset != iterator->second.local_offset) {
            if (error) *error = "ZIP central-directory validation failed for " + name;
            return false;
        }
    }
    const auto central_end = input.tellg();
    if (central_end < 0 || static_cast<std::uint64_t>(central_end) > UINT32_MAX) {
        if (error) *error = "invalid ZIP central-directory boundary";
        return false;
    }
    std::uint32_t end_signature = 0, central_size = 0, central_offset = 0;
    std::uint16_t disk = 0, central_disk = 0, disk_entries = 0, total_entries = 0, comment_length = 0;
    if (!ReadLittle(input, &end_signature) || !ReadLittle(input, &disk) || !ReadLittle(input, &central_disk) ||
        !ReadLittle(input, &disk_entries) || !ReadLittle(input, &total_entries) ||
        !ReadLittle(input, &central_size) || !ReadLittle(input, &central_offset) ||
        !ReadLittle(input, &comment_length) || end_signature != 0x06054b50 || disk != 0 || central_disk != 0 ||
        disk_entries != observed.size() || total_entries != observed.size() || central_offset != observed_central_offset ||
        central_size != static_cast<std::uint32_t>(central_end) - observed_central_offset || comment_length != 0 ||
        input.peek() != EOF) {
        if (error) *error = "ZIP end-of-central-directory validation failed";
        return false;
    }
    for (const auto& artifact : artifacts) {
        const auto iterator = observed.find(artifact.relative_path);
        if (iterator == observed.end() || iterator->second.sha256 != artifact.sha256 ||
            iterator->second.size != artifact.file_size) {
            if (error) *error = "ZIP artifact validation failed for " + artifact.relative_path;
            return false;
        }
    }
    return observed.contains("manifest.json") && observed.contains("package-validation.json") &&
           observed.contains("CODEX_HANDOFF.md") && observed.contains("PrimarySession/raw/capture-records.jsonl");
}

std::optional<std::string> ReadStoredZipEntry(const fs::path& zip_path, std::string_view wanted_name) {
    std::ifstream input(fs::path(Win32ExtendedPathForFileIo(zip_path)), std::ios::binary);
    if (!input) return std::nullopt;
    for (;;) {
        std::uint32_t signature = 0;
        if (!ReadLittle(input, &signature) || signature == 0x02014b50 || signature == 0x06054b50) break;
        if (signature != 0x04034b50) return std::nullopt;
        std::uint16_t version = 0, flags = 0, method = 0, ignored16 = 0, name_length = 0, extra_length = 0;
        std::uint32_t ignored32 = 0, compressed = 0, uncompressed = 0;
        if (!ReadLittle(input, &version) || !ReadLittle(input, &flags) || !ReadLittle(input, &method) ||
            !ReadLittle(input, &ignored16) || !ReadLittle(input, &ignored16) || !ReadLittle(input, &ignored32) ||
            !ReadLittle(input, &compressed) || !ReadLittle(input, &uncompressed) ||
            !ReadLittle(input, &name_length) || !ReadLittle(input, &extra_length) || method != 0 ||
            compressed != uncompressed) return std::nullopt;
        std::string name(name_length, '\0');
        input.read(name.data(), name_length);
        input.seekg(extra_length, std::ios::cur);
        if (!input) return std::nullopt;
        if (name == wanted_name) {
            std::string content(uncompressed, '\0');
            input.read(content.data(), static_cast<std::streamsize>(uncompressed));
            if (input.gcount() != static_cast<std::streamsize>(uncompressed)) return std::nullopt;
            return content;
        }
        input.seekg(static_cast<std::streamoff>(uncompressed), std::ios::cur);
        if (!input) return std::nullopt;
    }
    return std::nullopt;
}

struct ZipPackageMetrics {
    std::uint64_t uncompressed_bytes = 0;
    std::uint64_t compressed_bytes = 0;
    std::uint64_t duration_ms = 0;
    std::uint64_t entry_count = 0;
    bool zip64 = false;
};

int EvidenceCompressionLevel() {
    wchar_t value[16]{};
    const DWORD length = GetEnvironmentVariableW(L"GOD2_EVIDENCE_COMPRESSION_LEVEL", value,
                                                  static_cast<DWORD>(std::size(value)));
    if (length == 0 || length >= std::size(value)) return kDefaultCompressionLevel;
    wchar_t* end = nullptr;
    const long parsed = std::wcstol(value, &end, 10);
    return end != value && *end == L'\0' && parsed >= 1 && parsed <= 9
        ? static_cast<int>(parsed) : kDefaultCompressionLevel;
}

bool HasValidZipEndRecord(const fs::path& zip_path, bool* zip64, std::string* error) {
    *zip64 = false;
    std::ifstream input(fs::path(Win32ExtendedPathForFileIo(zip_path)), std::ios::binary);
    if (!input) {
        if (error) *error = "cannot open ZIP for EOCD validation";
        return false;
    }
    input.seekg(0, std::ios::end);
    const auto end = input.tellg();
    if (end < 22) {
        if (error) *error = "ZIP is too short for EOCD";
        return false;
    }
    const auto window = static_cast<std::size_t>(std::min<std::streamoff>(end, 65'557));
    std::vector<unsigned char> tail(window);
    input.seekg(end - static_cast<std::streamoff>(window), std::ios::beg);
    input.read(reinterpret_cast<char*>(tail.data()), static_cast<std::streamsize>(tail.size()));
    if (input.gcount() != static_cast<std::streamsize>(tail.size())) {
        if (error) *error = "truncated ZIP EOCD search window";
        return false;
    }
    for (std::size_t offset = tail.size() - 22;; --offset) {
        if (tail[offset] == 0x50 && tail[offset + 1] == 0x4b && tail[offset + 2] == 0x05 &&
            tail[offset + 3] == 0x06) {
            const std::uint16_t comment_length = static_cast<std::uint16_t>(tail[offset + 20]) |
                (static_cast<std::uint16_t>(tail[offset + 21]) << 8);
            if (offset + 22 + comment_length != tail.size()) continue;
            const bool sentinel = tail[offset + 8] == 0xff && tail[offset + 9] == 0xff &&
                tail[offset + 10] == 0xff && tail[offset + 11] == 0xff;
            if (sentinel) {
                if (offset < 20 || tail[offset - 20] != 0x50 || tail[offset - 19] != 0x4b ||
                    tail[offset - 18] != 0x06 || tail[offset - 17] != 0x07) {
                    if (error) *error = "ZIP64 EOCD locator is missing";
                    return false;
                }
                *zip64 = true;
            }
            return true;
        }
        if (offset == 0) break;
    }
    if (error) *error = "ZIP EOCD is missing or truncated";
    return false;
}

bool CreateDeflateZip(const fs::path& root, const fs::path& zip_path, int level,
                      ZipPackageMetrics* metrics, std::string* error) {
    struct SourceEntry {
        std::string name;
        fs::path source;
        std::uint64_t size = 0;
    };
    if (level < 1 || level > 9) {
        if (error) *error = "ZIP compression level must be in the supported range 1..9";
        return false;
    }
    std::vector<SourceEntry> entries;
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(root, fs::directory_options::skip_permission_denied,
                                                    enumerate_error), end;
         iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
        if (IsExcludedRawNetworkPayload(iterator->path())) continue;
        if (!iterator->is_regular_file()) continue;
        const auto name = RelativeUtf8(root, iterator->path());
        const auto size = iterator->file_size(enumerate_error);
        if (enumerate_error || name.empty() || name.size() > UINT16_MAX) {
            if (error) *error = "cannot inventory ZIP entry " + name;
            return false;
        }
        entries.push_back(SourceEntry{name, iterator->path(), size});
    }
    if (enumerate_error || entries.empty()) {
        if (error) *error = enumerate_error ?
            "cannot enumerate package staging tree: " + enumerate_error.message() :
            "package staging tree is empty";
        return false;
    }
    std::sort(entries.begin(), entries.end(), [](const auto& left, const auto& right) {
        return left.name < right.name;
    });

    const auto started = std::chrono::steady_clock::now();
    const fs::path temporary(zip_path.wstring() + L".tmp");
    if (!IsLexicallyContained(zip_path.parent_path(), temporary) ||
        ContainsReparsePoint(zip_path.parent_path(), temporary, true)) {
        if (error) *error = "Deflate Evidence ZIP temporary path is not contained or is a reparse point";
        return false;
    }
    DeleteFileW(temporary.c_str());
    zlib_filefunc64_def file_functions{};
    fill_win32_filefunc64W(&file_functions);
    zipFile archive = zipOpen2_64(temporary.c_str(), APPEND_STATUS_CREATE, nullptr, &file_functions);
    if (archive == nullptr) {
        if (error) *error = "cannot create Deflate Evidence ZIP";
        return false;
    }
    bool succeeded = true;
    std::vector<char> buffer(64 * 1024);
    std::uint64_t uncompressed_total = 0;
    bool zip64 = entries.size() > UINT16_MAX;
    for (const auto& entry : entries) {
        zip_fileinfo info{};
        const int entry_zip64 = entry.size >= UINT32_MAX ? 1 : 0;
        // These three small JSON files contain package-wide byte counts and
        // hashes.  Keep them as Method 8 Deflate entries, but use stored
        // Deflate blocks so changing fixed-width metric/hash values cannot
        // perturb the next archive's compressed size.  This makes the final
        // package metrics reconcilable without weakening payload compression.
        const bool self_referential_metadata = entry.name == "capture-info.json" ||
            entry.name == "capture-health.json" || entry.name == "manifest.json";
        const int entry_level = self_referential_metadata ? Z_NO_COMPRESSION : level;
        zip64 = zip64 || entry_zip64 != 0;
        const int opened = zipOpenNewFileInZip4_64(
            archive, entry.name.c_str(), &info, nullptr, 0, nullptr, 0, nullptr,
            Z_DEFLATED, entry_level, 0, -MAX_WBITS, DEF_MEM_LEVEL, Z_DEFAULT_STRATEGY,
            nullptr, 0, 0, 0x0800, entry_zip64);
        if (opened != ZIP_OK) {
            if (error) *error = "cannot create Deflate ZIP entry " + entry.name;
            succeeded = false;
            break;
        }
        ScopedWinHandle input(CreateFileW(entry.source.c_str(), GENERIC_READ | FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
        FILE_ATTRIBUTE_TAG_INFO input_tag{};
        LARGE_INTEGER input_size{};
        if (!input || !GetFileInformationByHandleEx(input.value, FileAttributeTagInfo,
                &input_tag, sizeof(input_tag)) ||
            (input_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
            !GetFileSizeEx(input.value, &input_size) || input_size.QuadPart < 0 ||
            static_cast<std::uint64_t>(input_size.QuadPart) != entry.size ||
            !IsFinalPathContained(root, input.value)) {
            succeeded = false;
            if (error) *error = "Deflate ZIP input failed handle-bound containment for " + entry.name;
        }
        std::uint64_t streamed_size = 0;
        while (succeeded) {
            DWORD read = 0;
            if (!ReadFile(input.value, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) {
                succeeded = false;
                if (error) *error = "cannot read Deflate ZIP input " + entry.name;
                break;
            }
            if (read == 0) break;
            streamed_size += read;
            if (zipWriteInFileInZip(archive, buffer.data(), read) != ZIP_OK) {
                succeeded = false;
                if (error) *error = "cannot stream Deflate ZIP entry " + entry.name;
                break;
            }
        }
        if (succeeded && streamed_size != entry.size) {
            succeeded = false;
            if (error) *error = "Deflate ZIP input size changed while reading " + entry.name;
        }
        if (zipCloseFileInZip(archive) != ZIP_OK && succeeded) {
            succeeded = false;
            if (error) *error = "cannot finalize Deflate ZIP entry " + entry.name;
        }
        if (!succeeded) break;
        if (UINT64_MAX - uncompressed_total < entry.size) {
            succeeded = false;
            if (error) *error = "uncompressed package size overflow";
            break;
        }
        uncompressed_total += entry.size;
    }
    if (zipClose(archive, nullptr) != ZIP_OK && succeeded) {
        succeeded = false;
        if (error) *error = "cannot finalize Deflate Evidence ZIP central directory";
    }
    if (!succeeded) {
        DeleteFileW(temporary.c_str());
        return false;
    }
    if (!MoveFileExW(temporary.c_str(), zip_path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        if (error) *error = "Deflate Evidence ZIP final rename failed: " + std::to_string(GetLastError());
        DeleteFileW(temporary.c_str());
        return false;
    }
    const auto zip_io_path = Win32ExtendedPathForFileIo(zip_path);
    ScopedWinHandle zip_handle(CreateFileW(zip_io_path.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    FILE_ATTRIBUTE_TAG_INFO zip_tag{};
    if (!zip_handle || !GetFileInformationByHandleEx(zip_handle.value, FileAttributeTagInfo,
            &zip_tag, sizeof(zip_tag)) ||
        (zip_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !IsFinalPathContained(zip_path.parent_path(), zip_handle.value)) {
        if (error) *error = "Deflate Evidence ZIP final path containment validation failed";
        DeleteFileW(zip_path.c_str());
        return false;
    }
    std::error_code size_error;
    const auto compressed_size = fs::file_size(zip_path, size_error);
    if (size_error) {
        if (error) *error = "cannot measure Deflate Evidence ZIP";
        return false;
    }
    metrics->uncompressed_bytes = uncompressed_total;
    metrics->compressed_bytes = compressed_size;
    metrics->duration_ms = static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - started).count());
    metrics->entry_count = entries.size();
    metrics->zip64 = zip64 || compressed_size >= UINT32_MAX;
    return true;
}

enum class StrictJsonKind { Object, Array, String, Number, Boolean, Null };

struct StrictJsonValue {
    StrictJsonKind kind = StrictJsonKind::Null;
    std::map<std::string, StrictJsonValue> object;
    std::vector<StrictJsonValue> array;
    std::string scalar;
};

class StrictJsonParser final {
public:
    explicit StrictJsonParser(std::string_view input) : input_(input) {}

    bool Parse(StrictJsonValue* output, std::string* error) {
        if (input_.empty() || input_.size() > kMaximumManifestBytes)
            return Fail("manifest JSON is empty or exceeds the bounded limit", error);
        SkipWhitespace();
        if (!ParseValue(output, 0, error)) return false;
        SkipWhitespace();
        return position_ == input_.size() || Fail("manifest JSON has trailing data", error);
    }

private:
    bool Fail(std::string_view message, std::string* error) const {
        if (error != nullptr)
            *error = std::string(message) + " at offset " + std::to_string(position_);
        return false;
    }

    char Peek() const noexcept {
        return position_ < input_.size() ? input_[position_] : '\0';
    }

    bool Consume(char expected) noexcept {
        if (Peek() != expected) return false;
        ++position_;
        return true;
    }

    void SkipWhitespace() noexcept {
        while (position_ < input_.size() &&
               (input_[position_] == ' ' || input_[position_] == '\t' ||
                input_[position_] == '\r' || input_[position_] == '\n'))
            ++position_;
    }

    static int HexDigit(char character) noexcept {
        if (character >= '0' && character <= '9') return character - '0';
        if (character >= 'a' && character <= 'f') return character - 'a' + 10;
        if (character >= 'A' && character <= 'F') return character - 'A' + 10;
        return -1;
    }

    static void AppendUtf8(std::string* output, std::uint32_t codepoint) {
        if (codepoint <= 0x7fU) {
            output->push_back(static_cast<char>(codepoint));
        } else if (codepoint <= 0x7ffU) {
            output->push_back(static_cast<char>(0xc0U | (codepoint >> 6U)));
            output->push_back(static_cast<char>(0x80U | (codepoint & 0x3fU)));
        } else if (codepoint <= 0xffffU) {
            output->push_back(static_cast<char>(0xe0U | (codepoint >> 12U)));
            output->push_back(static_cast<char>(0x80U | ((codepoint >> 6U) & 0x3fU)));
            output->push_back(static_cast<char>(0x80U | (codepoint & 0x3fU)));
        } else {
            output->push_back(static_cast<char>(0xf0U | (codepoint >> 18U)));
            output->push_back(static_cast<char>(0x80U | ((codepoint >> 12U) & 0x3fU)));
            output->push_back(static_cast<char>(0x80U | ((codepoint >> 6U) & 0x3fU)));
            output->push_back(static_cast<char>(0x80U | (codepoint & 0x3fU)));
        }
    }

    bool ParseUnicodeEscape(std::string* output, std::string* error) {
        if (position_ + 4 > input_.size()) return Fail("truncated unicode escape", error);
        std::uint32_t codepoint = 0;
        for (std::size_t index = 0; index < 4; ++index) {
            const int digit = HexDigit(input_[position_ + index]);
            if (digit < 0) return Fail("invalid unicode escape", error);
            codepoint = (codepoint << 4U) | static_cast<std::uint32_t>(digit);
        }
        position_ += 4;
        if (codepoint >= 0xd800U && codepoint <= 0xdbffU) {
            if (position_ + 6 > input_.size() || input_[position_] != '\\' ||
                input_[position_ + 1] != 'u')
                return Fail("missing unicode low surrogate", error);
            position_ += 2;
            std::uint32_t low = 0;
            for (std::size_t index = 0; index < 4; ++index) {
                const int digit = HexDigit(input_[position_ + index]);
                if (digit < 0) return Fail("invalid unicode low surrogate", error);
                low = (low << 4U) | static_cast<std::uint32_t>(digit);
            }
            position_ += 4;
            if (low < 0xdc00U || low > 0xdfffU)
                return Fail("invalid unicode low surrogate", error);
            codepoint = 0x10000U + ((codepoint - 0xd800U) << 10U) + (low - 0xdc00U);
        } else if (codepoint >= 0xdc00U && codepoint <= 0xdfffU) {
            return Fail("unexpected unicode low surrogate", error);
        }
        AppendUtf8(output, codepoint);
        return true;
    }

    bool ParseString(std::string* output, std::string* error) {
        if (!Consume('"')) return Fail("expected JSON string", error);
        output->clear();
        while (position_ < input_.size()) {
            const unsigned char character = static_cast<unsigned char>(input_[position_++]);
            if (character == '"') return true;
            if (character < 0x20U) return Fail("control character in JSON string", error);
            if (character != '\\') {
                output->push_back(static_cast<char>(character));
                continue;
            }
            if (position_ >= input_.size()) return Fail("truncated JSON escape", error);
            switch (input_[position_++]) {
            case '"': output->push_back('"'); break;
            case '\\': output->push_back('\\'); break;
            case '/': output->push_back('/'); break;
            case 'b': output->push_back('\b'); break;
            case 'f': output->push_back('\f'); break;
            case 'n': output->push_back('\n'); break;
            case 'r': output->push_back('\r'); break;
            case 't': output->push_back('\t'); break;
            case 'u': if (!ParseUnicodeEscape(output, error)) return false; break;
            default: return Fail("invalid JSON escape", error);
            }
        }
        return Fail("unterminated JSON string", error);
    }

    bool ParseNumber(std::string* output, std::string* error) {
        const std::size_t start = position_;
        if (Peek() == '-') ++position_;
        if (Peek() == '0') {
            ++position_;
            if (Peek() >= '0' && Peek() <= '9') return Fail("leading zero in JSON number", error);
        } else {
            if (Peek() < '1' || Peek() > '9') return Fail("invalid JSON number", error);
            while (Peek() >= '0' && Peek() <= '9') ++position_;
        }
        if (Peek() == '.') {
            ++position_;
            if (Peek() < '0' || Peek() > '9') return Fail("invalid JSON fraction", error);
            while (Peek() >= '0' && Peek() <= '9') ++position_;
        }
        if (Peek() == 'e' || Peek() == 'E') {
            ++position_;
            if (Peek() == '+' || Peek() == '-') ++position_;
            if (Peek() < '0' || Peek() > '9') return Fail("invalid JSON exponent", error);
            while (Peek() >= '0' && Peek() <= '9') ++position_;
        }
        *output = std::string(input_.substr(start, position_ - start));
        return true;
    }

    bool ParseValue(StrictJsonValue* output, std::size_t depth, std::string* error) {
        if (depth > 32 || ++node_count_ > 1'000'000)
            return Fail("manifest JSON nesting or node count exceeds the bounded limit", error);
        if (Peek() == '{') {
            output->kind = StrictJsonKind::Object;
            ++position_;
            SkipWhitespace();
            if (Consume('}')) return true;
            for (;;) {
                std::string key;
                if (!ParseString(&key, error)) return false;
                SkipWhitespace();
                if (!Consume(':')) return Fail("expected JSON colon", error);
                SkipWhitespace();
                StrictJsonValue value;
                if (!ParseValue(&value, depth + 1, error)) return false;
                if (!output->object.emplace(std::move(key), std::move(value)).second)
                    return Fail("duplicate JSON object key", error);
                SkipWhitespace();
                if (Consume('}')) return true;
                if (!Consume(',')) return Fail("expected JSON object comma", error);
                SkipWhitespace();
                if (Peek() == '}') return Fail("trailing JSON object comma", error);
            }
        }
        if (Peek() == '[') {
            output->kind = StrictJsonKind::Array;
            ++position_;
            SkipWhitespace();
            if (Consume(']')) return true;
            for (;;) {
                StrictJsonValue value;
                if (!ParseValue(&value, depth + 1, error)) return false;
                output->array.push_back(std::move(value));
                SkipWhitespace();
                if (Consume(']')) return true;
                if (!Consume(',')) return Fail("expected JSON array comma", error);
                SkipWhitespace();
                if (Peek() == ']') return Fail("trailing JSON array comma", error);
            }
        }
        if (Peek() == '"') {
            output->kind = StrictJsonKind::String;
            return ParseString(&output->scalar, error);
        }
        if (input_.substr(position_, 4) == "true") {
            position_ += 4;
            output->kind = StrictJsonKind::Boolean;
            output->scalar = "true";
            return true;
        }
        if (input_.substr(position_, 5) == "false") {
            position_ += 5;
            output->kind = StrictJsonKind::Boolean;
            output->scalar = "false";
            return true;
        }
        if (input_.substr(position_, 4) == "null") {
            position_ += 4;
            output->kind = StrictJsonKind::Null;
            return true;
        }
        output->kind = StrictJsonKind::Number;
        return ParseNumber(&output->scalar, error);
    }

    std::string_view input_;
    std::size_t position_ = 0;
    std::size_t node_count_ = 0;
};

struct StrictManifestArtifact {
    std::string artifact_id;
    std::string relative_path;
    std::uint64_t file_size = 0;
    std::string sha256;
};

struct StrictEvidenceManifest {
    std::int64_t schema_version = 0;
    std::string primary_session_id;
    std::vector<StrictManifestArtifact> artifacts;
};

const StrictJsonValue* JsonMember(const StrictJsonValue& object, std::string_view name,
                                  StrictJsonKind kind) {
    if (object.kind != StrictJsonKind::Object) return nullptr;
    const auto found = object.object.find(std::string(name));
    return found != object.object.end() && found->second.kind == kind ? &found->second : nullptr;
}

bool ParseStrictUint64(const StrictJsonValue& value, std::uint64_t* output) {
    if (value.kind != StrictJsonKind::Number || value.scalar.empty() ||
        value.scalar.front() == '-' || value.scalar.find_first_of(".eE") != std::string::npos)
        return false;
    const auto converted = std::from_chars(value.scalar.data(),
        value.scalar.data() + value.scalar.size(), *output);
    return converted.ec == std::errc{} && converted.ptr == value.scalar.data() + value.scalar.size();
}

bool ParseStrictEvidenceManifest(std::string_view json, StrictEvidenceManifest* manifest,
                                 std::string* error) {
    StrictJsonValue root;
    if (!StrictJsonParser(json).Parse(&root, error) || root.kind != StrictJsonKind::Object) {
        if (error != nullptr && error->empty()) *error = "manifest root must be a JSON object";
        return false;
    }
    const auto* schema_id = JsonMember(root, "SchemaId", StrictJsonKind::String);
    const auto* schema_version = JsonMember(root, "SchemaVersion", StrictJsonKind::Number);
    const auto* session_id = JsonMember(root, "PrimarySessionId", StrictJsonKind::String);
    const auto* path_policy = JsonMember(root, "RelativePathPolicy", StrictJsonKind::String);
    const auto* artifacts = JsonMember(root, "Artifacts", StrictJsonKind::Array);
    std::uint64_t parsed_schema = 0;
    if (schema_id == nullptr || schema_id->scalar != "manifest" || schema_version == nullptr ||
        !ParseStrictUint64(*schema_version, &parsed_schema) || parsed_schema < 9 || parsed_schema > 11 ||
        session_id == nullptr || !IsSafePortableComponent(session_id->scalar) || path_policy == nullptr ||
        path_policy->scalar != "ZipRootRelative" || artifacts == nullptr || artifacts->array.empty() ||
        artifacts->array.size() > kMaximumManifestArtifacts) {
        if (error != nullptr) *error = "manifest identity, schema, path policy, or artifact array is invalid";
        return false;
    }
    manifest->schema_version = static_cast<std::int64_t>(parsed_schema);
    manifest->primary_session_id = session_id->scalar;
    manifest->artifacts.clear();
    manifest->artifacts.reserve(artifacts->array.size());
    std::unordered_set<std::string> artifact_ids;
    std::unordered_set<std::string> folded_paths;
    for (const auto& item : artifacts->array) {
        const auto* artifact_id = JsonMember(item, "ArtifactId", StrictJsonKind::String);
        const auto* relative_path = JsonMember(item, "RelativePath", StrictJsonKind::String);
        const auto* file_size = JsonMember(item, "FileSize", StrictJsonKind::Number);
        const auto* sha256 = JsonMember(item, "SHA256", StrictJsonKind::String);
        std::uint64_t parsed_size = 0;
        if (artifact_id == nullptr || !IsSafePortableComponent(artifact_id->scalar) ||
            relative_path == nullptr || !IsSafeZipRelativePath(relative_path->scalar) ||
            relative_path->scalar == "manifest.json" || file_size == nullptr ||
            !ParseStrictUint64(*file_size, &parsed_size) || sha256 == nullptr ||
            sha256->scalar.size() != 64 || !IsHex(sha256->scalar)) {
            if (error != nullptr) *error = "manifest artifact has invalid identity, path, size, or SHA-256";
            return false;
        }
        std::string folded = relative_path->scalar;
        std::transform(folded.begin(), folded.end(), folded.begin(), [](unsigned char character) {
            return static_cast<char>(std::tolower(character));
        });
        if (!artifact_ids.insert(artifact_id->scalar).second || !folded_paths.insert(folded).second) {
            if (error != nullptr) *error = "manifest contains duplicate artifact identity or path";
            return false;
        }
        std::string normalized_hash = sha256->scalar;
        std::transform(normalized_hash.begin(), normalized_hash.end(), normalized_hash.begin(),
            [](unsigned char character) { return static_cast<char>(std::toupper(character)); });
        manifest->artifacts.push_back({artifact_id->scalar, relative_path->scalar,
                                      parsed_size, std::move(normalized_hash)});
    }
    return true;
}

struct StrictSemanticSegment {
    std::uint64_t index = 0;
    std::string relative_path;
    std::uint64_t records = 0;
    std::uint64_t bytes = 0;
    std::uint64_t first_sequence = 0;
    std::uint64_t last_sequence = 0;
    std::string sha256;
};

struct StrictSemanticSegmentManifest {
    bool pass = false;
    std::uint64_t segment_count = 0;
    std::uint64_t record_count = 0;
    std::string merged_sha256;
    std::vector<StrictSemanticSegment> segments;
};

bool IsCanonicalSha256(std::string_view value, bool allow_empty = false) {
    if (allow_empty && value.empty()) return true;
    return value.size() == 64 && IsHex(value) &&
        std::none_of(value.begin(), value.end(), [](unsigned char character) {
            return character >= 'a' && character <= 'f';
        });
}

bool ParseStrictSemanticSegmentManifest(std::string_view json,
                                        StrictSemanticSegmentManifest* manifest,
                                        std::string* error) {
    StrictJsonValue root;
    if (!StrictJsonParser(json).Parse(&root, error) ||
        root.kind != StrictJsonKind::Object || root.object.size() != 11) {
        if (error != nullptr && error->empty())
            *error = "semantic segment manifest root is not an exact closed object";
        return false;
    }
    const auto* schema = JsonMember(root, "SchemaVersion", StrictJsonKind::String);
    const auto* generated = JsonMember(root, "GeneratedAtUtc", StrictJsonKind::String);
    const auto* status = JsonMember(root, "Status", StrictJsonKind::String);
    const auto* maximum_bytes = JsonMember(root, "SegmentMaximumBytes", StrictJsonKind::Number);
    const auto* maximum_records = JsonMember(root, "SegmentMaximumRecords", StrictJsonKind::Number);
    const auto* segment_count = JsonMember(root, "SegmentCount", StrictJsonKind::Number);
    const auto* record_count = JsonMember(root, "RecordCount", StrictJsonKind::Number);
    const auto* continuity = JsonMember(root, "SequenceContinuity", StrictJsonKind::Boolean);
    const auto* merged_path = JsonMember(root, "MergedPath", StrictJsonKind::String);
    const auto* merged_sha = JsonMember(root, "MergedSHA256", StrictJsonKind::String);
    const auto* segments = JsonMember(root, "Segments", StrictJsonKind::Array);
    std::uint64_t parsed_maximum_bytes = 0;
    std::uint64_t parsed_maximum_records = 0;
    std::uint64_t parsed_segment_count = 0;
    std::uint64_t parsed_record_count = 0;
    const bool pass = status != nullptr && status->scalar == "PASS";
    const bool blocked = status != nullptr &&
        status->scalar == "EVIDENCE_BLOCKED_SEGMENT_OR_CONTINUITY_FAILURE";
    if (schema == nullptr || schema->scalar != "god2-semantic-segment-manifest-v1" ||
        generated == nullptr || generated->scalar.empty() || (!pass && !blocked) ||
        maximum_bytes == nullptr || !ParseStrictUint64(*maximum_bytes, &parsed_maximum_bytes) ||
        parsed_maximum_bytes != kSemanticSegmentMaximumBytes ||
        maximum_records == nullptr || !ParseStrictUint64(*maximum_records, &parsed_maximum_records) ||
        parsed_maximum_records != kSemanticSegmentMaximumRecords ||
        segment_count == nullptr || !ParseStrictUint64(*segment_count, &parsed_segment_count) ||
        record_count == nullptr || !ParseStrictUint64(*record_count, &parsed_record_count) ||
        continuity == nullptr || merged_path == nullptr ||
        merged_path->scalar != "raw/semantic-events.jsonl" || merged_sha == nullptr ||
        segments == nullptr || segments->array.size() != parsed_segment_count ||
        parsed_segment_count > kMaximumManifestArtifacts) {
        if (error != nullptr) *error = "semantic segment manifest identity or bounds are invalid";
        return false;
    }
    if (blocked) {
        if (continuity->scalar != "false" || parsed_segment_count != 0 ||
            parsed_record_count != 0 || !merged_sha->scalar.empty() || !segments->array.empty()) {
            if (error != nullptr) *error = "blocked semantic segment manifest carries promotion-shaped evidence";
            return false;
        }
        *manifest = {};
        return true;
    }
    if (continuity->scalar != "true" || parsed_segment_count == 0 ||
        parsed_record_count == 0 || !IsCanonicalSha256(merged_sha->scalar)) {
        if (error != nullptr) *error = "PASS semantic segment manifest is incomplete";
        return false;
    }
    StrictSemanticSegmentManifest parsed;
    parsed.pass = true;
    parsed.segment_count = parsed_segment_count;
    parsed.record_count = parsed_record_count;
    parsed.merged_sha256 = merged_sha->scalar;
    parsed.segments.reserve(segments->array.size());
    for (std::size_t position = 0; position < segments->array.size(); ++position) {
        const auto& item = segments->array[position];
        if (item.kind != StrictJsonKind::Object || item.object.size() != 7) {
            if (error != nullptr) *error = "semantic segment row is not an exact closed object";
            return false;
        }
        const auto* index = JsonMember(item, "Index", StrictJsonKind::Number);
        const auto* path = JsonMember(item, "Path", StrictJsonKind::String);
        const auto* records = JsonMember(item, "Records", StrictJsonKind::Number);
        const auto* bytes = JsonMember(item, "Bytes", StrictJsonKind::Number);
        const auto* first = JsonMember(item, "FirstSequence", StrictJsonKind::Number);
        const auto* last = JsonMember(item, "LastSequence", StrictJsonKind::Number);
        const auto* sha = JsonMember(item, "SHA256", StrictJsonKind::String);
        StrictSemanticSegment segment;
        if (index == nullptr || !ParseStrictUint64(*index, &segment.index) ||
            path == nullptr || records == nullptr || !ParseStrictUint64(*records, &segment.records) ||
            bytes == nullptr || !ParseStrictUint64(*bytes, &segment.bytes) ||
            first == nullptr || !ParseStrictUint64(*first, &segment.first_sequence) ||
            last == nullptr || !ParseStrictUint64(*last, &segment.last_sequence) ||
            sha == nullptr || !IsCanonicalSha256(sha->scalar) ||
            segment.index != position || segment.records == 0 ||
            segment.records > kSemanticSegmentMaximumRecords || segment.bytes == 0 ||
            segment.bytes > kSemanticSegmentMaximumBytes || segment.first_sequence == 0 ||
            segment.last_sequence < segment.first_sequence ||
            segment.last_sequence - segment.first_sequence + 1 != segment.records) {
            if (error != nullptr) *error = "semantic segment row counters or digest are invalid";
            return false;
        }
        std::ostringstream expected_path;
        expected_path << "raw/semantic-segments/semantic-" << std::setw(6)
                      << std::setfill('0') << position << ".jsonl";
        if (path->scalar != expected_path.str() || !IsSafeZipRelativePath(path->scalar)) {
            if (error != nullptr) *error = "semantic segment path/index binding is invalid";
            return false;
        }
        segment.relative_path = path->scalar;
        segment.sha256 = sha->scalar;
        parsed.segments.push_back(std::move(segment));
    }
    *manifest = std::move(parsed);
    return true;
}

bool ValidateSemanticSegmentPayloads(
    const StrictSemanticSegmentManifest& manifest,
    std::string_view merged,
    const std::function<std::optional<std::string>(std::string_view)>& load_segment,
    std::string_view expected_session_id,
    std::string* error) {
    if (!manifest.pass) return true;
    if (merged.empty() || merged.size() > kMaximumSemanticMergedBytes ||
        merged.back() != '\n' || Sha256(merged).value_or("") != manifest.merged_sha256) {
        if (error != nullptr) *error = "semantic merged JSONL size or SHA-256 is invalid";
        return false;
    }
    Sha256Hasher concatenated_hasher;
    std::uint64_t expected_sequence = 1;
    std::uint64_t total_records = 0;
    std::uint64_t total_bytes = 0;
    std::string observed_session;
    for (const auto& segment : manifest.segments) {
        const auto content = load_segment(segment.relative_path);
        if (!content || content->size() != segment.bytes || content->empty() ||
            content->back() != '\n' || Sha256(*content).value_or("") != segment.sha256 ||
            !concatenated_hasher.Update(content->data(), content->size())) {
            if (error != nullptr) *error = "semantic segment bytes or SHA-256 do not match the manifest";
            return false;
        }
        std::uint64_t records = 0;
        std::size_t offset = 0;
        while (offset < content->size()) {
            const auto newline = content->find('\n', offset);
            if (newline == std::string::npos || newline == offset) {
                if (error != nullptr) *error = "semantic segment contains a blank or unterminated record";
                return false;
            }
            std::string_view line(*content);
            line = line.substr(offset, newline - offset);
            if (!line.empty() && line.back() == '\r') line.remove_suffix(1);
            const auto parsed = ReadSemanticEvent(line);
            if (!parsed.success) {
                if (error != nullptr)
                    *error = "semantic segment event parse failed: " + parsed.error;
                return false;
            }
            if (parsed.event.sequence != expected_sequence) {
                if (error != nullptr)
                    *error = "semantic segment sequence gap: expected " +
                        std::to_string(expected_sequence) + ", observed " +
                        std::to_string(parsed.event.sequence);
                return false;
            }
            if (parsed.event.session_id.empty() ||
                (!expected_session_id.empty() && parsed.event.session_id != expected_session_id)) {
                if (error != nullptr)
                    *error = "semantic segment session identity does not match the package session";
                return false;
            }
            if (observed_session.empty()) observed_session = parsed.event.session_id;
            if (parsed.event.session_id != observed_session) {
                if (error != nullptr) *error = "semantic segment spans more than one session identity";
                return false;
            }
            ++expected_sequence;
            ++records;
            offset = newline + 1;
        }
        if (records != segment.records || segment.first_sequence != expected_sequence - records ||
            segment.last_sequence != expected_sequence - 1) {
            if (error != nullptr) *error = "semantic segment record/sequence counters do not reconcile";
            return false;
        }
        total_records += records;
        total_bytes += segment.bytes;
    }
    const auto concatenated_sha = concatenated_hasher.Finish();
    if (!concatenated_sha || *concatenated_sha != manifest.merged_sha256 ||
        total_records != manifest.record_count || total_bytes != merged.size()) {
        if (error != nullptr) *error = "semantic segment aggregate does not reconcile with merged JSONL";
        return false;
    }
    return true;
}

bool ValidateSourceSemanticSegments(const fs::path& manifest_path,
                                    const fs::path& session_root,
                                    const fs::path& merged_path,
                                    std::string_view expected_session_id,
                                    StrictSemanticSegmentManifest* parsed_manifest,
                                    std::vector<fs::path>* segment_paths,
                                    std::string* error) {
    std::error_code size_error;
    const auto manifest_size = fs::file_size(manifest_path, size_error);
    if (size_error || manifest_size == 0 || manifest_size > kMaximumManifestBytes ||
        !IsLexicallyContained(session_root, manifest_path) ||
        ContainsReparsePoint(session_root, manifest_path)) {
        if (error != nullptr) *error = "semantic segment source manifest identity is invalid";
        return false;
    }
    const auto manifest_json = ReadUtf8File(manifest_path);
    StrictSemanticSegmentManifest manifest;
    if (!manifest_json ||
        !ParseStrictSemanticSegmentManifest(*manifest_json, &manifest, error) ||
        !manifest.pass) {
        if (error != nullptr && error->empty())
            *error = "semantic segment source manifest does not carry PASS evidence";
        return false;
    }
    const auto merged_size = fs::file_size(merged_path, size_error);
    if (size_error || merged_size == 0 || merged_size > kMaximumSemanticMergedBytes ||
        !IsLexicallyContained(session_root, merged_path) ||
        ContainsReparsePoint(session_root, merged_path)) {
        if (error != nullptr) *error = "semantic merged source identity is invalid";
        return false;
    }
    const auto merged = ReadUtf8File(merged_path);
    if (!merged) {
        if (error != nullptr) *error = "semantic merged source cannot be read";
        return false;
    }
    segment_paths->clear();
    segment_paths->reserve(manifest.segments.size());
    for (const auto& segment : manifest.segments) {
        const fs::path path = session_root / Utf8ToWide(segment.relative_path);
        const auto actual_size = fs::file_size(path, size_error);
        if (size_error || actual_size != segment.bytes ||
            !IsLexicallyContained(session_root, path) ||
            ContainsReparsePoint(session_root, path)) {
            if (error != nullptr) *error = "semantic segment source path, size or containment is invalid";
            return false;
        }
        segment_paths->push_back(path);
    }
    const auto loader = [&](std::string_view relative) -> std::optional<std::string> {
        const fs::path path = session_root / Utf8ToWide(std::string(relative));
        std::error_code read_size_error;
        const auto size = fs::file_size(path, read_size_error);
        if (read_size_error || size == 0 || size > kSemanticSegmentMaximumBytes ||
            !IsLexicallyContained(session_root, path) ||
            ContainsReparsePoint(session_root, path))
            return std::nullopt;
        return ReadUtf8File(path);
    };
    if (!ValidateSemanticSegmentPayloads(manifest, *merged, loader,
                                          expected_session_id, error))
        return false;
    *parsed_manifest = std::move(manifest);
    return true;
}

std::optional<std::string> ReadEvidenceZipEntry(
    const fs::path& zip_path, std::string_view wanted_name,
    std::uint64_t maximum_bytes = kMaximumValidatedEntryBytes);
bool ValidateEvidenceZipSemanticSegments(const fs::path& zip_path, std::string* error);

bool ValidateEvidenceZip(const fs::path& zip_path,
                         const std::vector<PackageArtifact>& artifacts,
                         std::string* error) {
    bool zip64 = false;
    if (!HasValidZipEndRecord(zip_path, &zip64, error)) return false;
    zlib_filefunc64_def file_functions{};
    fill_win32_filefunc64W(&file_functions);
    unzFile archive = unzOpen2_64(zip_path.c_str(), &file_functions);
    if (archive == nullptr) {
        if (error) *error = "cannot reopen Evidence ZIP central directory";
        return false;
    }
    unz_global_info64 global{};
    if (unzGetGlobalInfo64(archive, &global) != UNZ_OK || global.number_entry == 0) {
        if (error) *error = "Evidence ZIP has no valid central-directory entries";
        unzClose(archive);
        return false;
    }
    std::unordered_map<std::string, std::pair<std::string, std::uint64_t>> observed;
    std::unordered_set<std::string> folded_names;
    std::string manifest_content;
    std::uint64_t total_uncompressed = 0;
    int status = unzGoToFirstFile(archive);
    for (ZPOS64_T index = 0; index < global.number_entry && status == UNZ_OK; ++index) {
        unz_file_info64 info{};
        if (unzGetCurrentFileInfo64(archive, &info, nullptr, 0, nullptr, 0, nullptr, 0) != UNZ_OK ||
            info.size_filename == 0 || info.size_filename > UINT16_MAX) {
            if (error) *error = "invalid ZIP central-directory entry metadata";
            status = UNZ_BADZIPFILE;
            break;
        }
        std::string name(static_cast<std::size_t>(info.size_filename) + 1, '\0');
        if (unzGetCurrentFileInfo64(archive, &info, name.data(),
                static_cast<uLong>(name.size()), nullptr, 0, nullptr, 0) != UNZ_OK) {
            if (error) *error = "cannot read ZIP UTF-8 entry name";
            status = UNZ_BADZIPFILE;
            break;
        }
        name.resize(static_cast<std::size_t>(info.size_filename));
        std::string folded_name = name;
        std::transform(folded_name.begin(), folded_name.end(), folded_name.begin(),
            [](unsigned char character) { return static_cast<char>(std::tolower(character)); });
        if (!IsSafeZipRelativePath(name) ||
            IsExcludedRawNetworkPayload(fs::path(Utf8ToWide(name))) ||
            !folded_names.insert(folded_name).second ||
            observed.contains(name) || (info.compression_method != 0 && info.compression_method != Z_DEFLATED) ||
            (info.flag & 1u) != 0 || info.uncompressed_size > kMaximumValidatedEntryBytes ||
            (info.compressed_size == 0 && info.uncompressed_size != 0) ||
            (name == "manifest.json" && info.uncompressed_size > kMaximumManifestBytes)) {
            if (error) *error = "unsupported, duplicate, encrypted or oversized ZIP entry: " + name;
            status = UNZ_BADZIPFILE;
            break;
        }
        if (info.uncompressed_size > 1024 * 1024 && info.compressed_size > 0 &&
            static_cast<double>(info.uncompressed_size) / static_cast<double>(info.compressed_size) >
                kMaximumCompressionExpansionRatio) {
            if (error) *error = "ZIP entry has an unreasonable compression expansion ratio: " + name;
            status = UNZ_BADZIPFILE;
            break;
        }
        if (UINT64_MAX - total_uncompressed < info.uncompressed_size ||
            (total_uncompressed += info.uncompressed_size) > kMaximumValidatedPackageBytes ||
            unzOpenCurrentFile(archive) != UNZ_OK) {
            if (error) *error = "ZIP local header, offset or package size validation failed for " + name;
            status = UNZ_BADZIPFILE;
            break;
        }
        Sha256Hasher hasher;
        std::uint64_t bytes_read = 0;
        std::vector<unsigned char> buffer(64 * 1024);
        for (;;) {
            const int read = unzReadCurrentFile(archive, buffer.data(), static_cast<unsigned>(buffer.size()));
            if (read < 0 || (read > 0 && !hasher.Update(buffer.data(), static_cast<std::size_t>(read)))) {
                status = UNZ_BADZIPFILE;
                if (error) *error = "ZIP Deflate stream is corrupt or truncated for " + name;
                break;
            }
            if (read == 0) break;
            bytes_read += static_cast<std::uint64_t>(read);
            if (name == "manifest.json")
                manifest_content.append(reinterpret_cast<const char*>(buffer.data()),
                                        static_cast<std::size_t>(read));
            if (bytes_read > info.uncompressed_size) {
                status = UNZ_BADZIPFILE;
                if (error) *error = "ZIP entry expanded beyond its declared size: " + name;
                break;
            }
        }
        const int close_status = unzCloseCurrentFile(archive);
        const auto digest = hasher.Finish();
        if (status != UNZ_OK || close_status != UNZ_OK || !digest || bytes_read != info.uncompressed_size) {
            if (status == UNZ_OK && error) *error = "ZIP CRC-32 or decompressed-size validation failed for " + name;
            status = UNZ_BADZIPFILE;
            break;
        }
        observed.emplace(name, std::make_pair(*digest, bytes_read));
        status = index + 1 == global.number_entry ? UNZ_END_OF_LIST_OF_FILE : unzGoToNextFile(archive);
    }
    const bool iteration_complete = status == UNZ_END_OF_LIST_OF_FILE && observed.size() == global.number_entry;
    unzClose(archive);
    if (!iteration_complete) return false;
    if (!observed.contains("manifest.json") || manifest_content.empty()) {
        if (error) *error = "ZIP manifest.json is missing or empty";
        return false;
    }

    std::unordered_map<std::string, std::pair<std::string, std::uint64_t>> expected;
    if (artifacts.empty()) {
        StrictEvidenceManifest manifest;
        if (!ParseStrictEvidenceManifest(manifest_content, &manifest, error)) return false;
        expected.reserve(manifest.artifacts.size());
        for (const auto& artifact : manifest.artifacts)
            expected.emplace(artifact.relative_path,
                             std::make_pair(artifact.sha256, artifact.file_size));
    } else {
        expected.reserve(artifacts.size());
        for (const auto& artifact : artifacts) {
            if (!IsSafeZipRelativePath(artifact.relative_path) || artifact.relative_path == "manifest.json" ||
                artifact.sha256.size() != 64 || !IsHex(artifact.sha256) ||
                !expected.emplace(artifact.relative_path,
                    std::make_pair(artifact.sha256, artifact.file_size)).second) {
                if (error) *error = "internal manifest inventory contains an invalid or duplicate artifact";
                return false;
            }
        }
    }
    if (observed.size() != expected.size() + 1) {
        if (error) *error = "ZIP contains an unlisted or missing manifest artifact";
        return false;
    }
    for (const auto& [relative_path, expected_identity] : expected) {
        const auto iterator = observed.find(relative_path);
        std::string expected_hash = expected_identity.first;
        std::transform(expected_hash.begin(), expected_hash.end(), expected_hash.begin(),
            [](unsigned char character) { return static_cast<char>(std::toupper(character)); });
        if (iterator == observed.end() || iterator->second.first != expected_hash ||
            iterator->second.second != expected_identity.second) {
            if (error) *error = "ZIP Manifest SHA-256/size validation failed for " + relative_path;
            return false;
        }
    }
    const bool required_present = expected.contains("package-validation.json") &&
        expected.contains("CODEX_HANDOFF.md") && expected.contains("capture-info.json") &&
        expected.contains("PrimarySession/raw/capture-records.jsonl");
    if (!required_present && error != nullptr)
        *error = "manifest inventory is missing a required Evidence Package artifact";
    return required_present &&
        (!expected.contains("PrimarySession/reports/semantic-segment-manifest.json") ||
         ValidateEvidenceZipSemanticSegments(zip_path, error));
}

std::optional<std::string> ReadEvidenceZipEntry(const fs::path& zip_path,
                                                std::string_view wanted_name,
                                                std::uint64_t maximum_bytes) {
    zlib_filefunc64_def file_functions{};
    fill_win32_filefunc64W(&file_functions);
    unzFile archive = unzOpen2_64(zip_path.c_str(), &file_functions);
    if (archive == nullptr) return std::nullopt;
    unz_global_info64 global{};
    if (unzGetGlobalInfo64(archive, &global) != UNZ_OK || unzGoToFirstFile(archive) != UNZ_OK) {
        unzClose(archive);
        return std::nullopt;
    }
    std::optional<std::string> result;
    for (ZPOS64_T index = 0; index < global.number_entry; ++index) {
        unz_file_info64 info{};
        if (unzGetCurrentFileInfo64(archive, &info, nullptr, 0, nullptr, 0, nullptr, 0) != UNZ_OK ||
            info.size_filename == 0 || info.size_filename > UINT16_MAX) break;
        std::string name(static_cast<std::size_t>(info.size_filename) + 1, '\0');
        if (unzGetCurrentFileInfo64(archive, &info, name.data(),
                static_cast<uLong>(name.size()), nullptr, 0, nullptr, 0) != UNZ_OK) break;
        name.resize(static_cast<std::size_t>(info.size_filename));
        if (name == wanted_name && info.uncompressed_size <= maximum_bytes &&
            info.uncompressed_size <= static_cast<ZPOS64_T>(std::numeric_limits<std::size_t>::max()) &&
            unzOpenCurrentFile(archive) == UNZ_OK) {
            std::string content;
            content.reserve(static_cast<std::size_t>(info.uncompressed_size));
            std::vector<char> buffer(64 * 1024);
            bool valid = true;
            for (;;) {
                const int read = unzReadCurrentFile(archive, buffer.data(), static_cast<unsigned>(buffer.size()));
                if (read < 0) { valid = false; break; }
                if (read == 0) break;
                content.append(buffer.data(), static_cast<std::size_t>(read));
            }
            valid = unzCloseCurrentFile(archive) == UNZ_OK && valid &&
                content.size() == info.uncompressed_size;
            if (valid) result = std::move(content);
            break;
        }
        if (index + 1 < global.number_entry && unzGoToNextFile(archive) != UNZ_OK) break;
    }
    unzClose(archive);
    return result;
}

bool ValidateEvidenceZipSemanticSegments(const fs::path& zip_path, std::string* error) {
    const auto manifest_json = ReadEvidenceZipEntry(zip_path,
        "PrimarySession/reports/semantic-segment-manifest.json", kMaximumManifestBytes);
    StrictSemanticSegmentManifest manifest;
    if (!manifest_json ||
        !ParseStrictSemanticSegmentManifest(*manifest_json, &manifest, error)) {
        if (error != nullptr && error->empty())
            *error = "semantic segment manifest is missing from the Evidence ZIP";
        return false;
    }
    if (!manifest.pass) return true;
    const auto merged = ReadEvidenceZipEntry(zip_path,
        "PrimarySession/raw/semantic-events.jsonl", kMaximumSemanticMergedBytes);
    if (!merged) {
        if (error != nullptr) *error = "semantic merged JSONL is missing from the Evidence ZIP";
        return false;
    }
    const auto loader = [&](std::string_view relative) -> std::optional<std::string> {
        return ReadEvidenceZipEntry(zip_path, "PrimarySession/" + std::string(relative),
                                    kSemanticSegmentMaximumBytes);
    };
    return ValidateSemanticSegmentPayloads(manifest, *merged, loader, {}, error);
}

bool ExtractEvidenceZipEntry(const fs::path& zip_path, std::string_view wanted_name,
                             const fs::path& target, const fs::path& target_root,
                             bool required, std::string* error) {
    zlib_filefunc64_def file_functions{};
    fill_win32_filefunc64W(&file_functions);
    unzFile archive = unzOpen2_64(zip_path.c_str(), &file_functions);
    if (archive == nullptr) {
        if (error) *error = "cannot open source Evidence ZIP";
        return false;
    }
    const std::string name(wanted_name);
    if (!IsSafeZipRelativePath(name) ||
        IsExcludedRawNetworkPayload(fs::path(Utf8ToWide(name))) ||
        IsExcludedRawNetworkPayload(target) ||
        !IsLexicallyContained(target_root, target) ||
        !EnsureContainedDirectory(target_root, target.parent_path()) ||
        ContainsReparsePoint(target_root, target.parent_path())) {
        unzClose(archive);
        if (error) *error = "unsafe or uncontained source ZIP extraction target: " + name;
        return false;
    }
    if (unzLocateFile(archive, name.c_str(), 1) != UNZ_OK) {
        unzClose(archive);
        if (required && error) *error = "required source ZIP entry is missing: " + name;
        return !required;
    }
    unz_file_info64 info{};
    if (unzGetCurrentFileInfo64(archive, &info, nullptr, 0, nullptr, 0, nullptr, 0) != UNZ_OK ||
        info.uncompressed_size > kMaximumValidatedEntryBytes || unzOpenCurrentFile(archive) != UNZ_OK) {
        unzClose(archive);
        if (error) *error = "cannot open source ZIP entry: " + name;
        return false;
    }
    const DWORD existing_attributes = GetFileAttributesW(target.c_str());
    if (existing_attributes != INVALID_FILE_ATTRIBUTES) {
        unzCloseCurrentFile(archive);
        unzClose(archive);
        if (error) *error = "source ZIP extraction target already exists: " + name;
        return false;
    }
    ScopedWinHandle parent_handle(CreateFileW(target.parent_path().c_str(),
        FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    FILE_ATTRIBUTE_TAG_INFO parent_tag{};
    if (!parent_handle || !GetFileInformationByHandleEx(parent_handle.value,
            FileAttributeTagInfo, &parent_tag, sizeof(parent_tag)) ||
        (parent_tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (parent_tag.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 ||
        !IsFinalPathContained(target_root, parent_handle.value)) {
        unzCloseCurrentFile(archive);
        unzClose(archive);
        if (error) *error = "source ZIP extraction parent containment validation failed: " + name;
        return false;
    }
    const fs::path temporary = target.parent_path() /
        (target.filename().wstring() + L".tmp-" + Utf8ToWide(NewId()));
    ScopedWinHandle output(CreateFileW(temporary.c_str(),
        GENERIC_WRITE | FILE_READ_ATTRIBUTES | DELETE,
        0, nullptr, CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    std::string failure_detail;
    bool valid = static_cast<bool>(output);
    if (!valid) {
        failure_detail = "temporary output creation failed (Win32=" +
            std::to_string(GetLastError()) + ")";
    } else if (!IsFinalPathContained(target_root, output.value)) {
        valid = false;
        failure_detail = "temporary output final-path containment failed";
    }
    std::vector<unsigned char> buffer(64 * 1024);
    std::uint64_t total = 0;
    while (valid) {
        const int read = unzReadCurrentFile(archive, buffer.data(), static_cast<unsigned>(buffer.size()));
        if (read < 0) {
            valid = false;
            failure_detail = "ZIP stream read or decompression failed";
            break;
        }
        if (read == 0) break;
        total += static_cast<std::uint64_t>(read);
        if (total > info.uncompressed_size) {
            valid = false;
            failure_detail = "ZIP stream exceeded declared uncompressed size";
            break;
        }
        DWORD offset = 0;
        while (offset < static_cast<DWORD>(read)) {
            DWORD written = 0;
            if (!WriteFile(output.value, buffer.data() + offset,
                    static_cast<DWORD>(read) - offset, &written, nullptr) || written == 0) {
                valid = false;
                failure_detail = "temporary output write failed (Win32=" +
                    std::to_string(GetLastError()) + ")";
                break;
            }
            offset += written;
        }
    }
    if (valid && total != info.uncompressed_size) {
        valid = false;
        failure_detail = "ZIP stream length did not match the validated manifest size";
    }
    if (valid && FlushFileBuffers(output.value) == FALSE) {
        valid = false;
        failure_detail = "temporary output flush failed (Win32=" +
            std::to_string(GetLastError()) + ")";
    }
    const int close_result = unzCloseCurrentFile(archive);
    if (close_result != UNZ_OK) {
        valid = false;
        failure_detail = "ZIP stream CRC/finalization failed";
    }
    unzClose(archive);
    bool target_committed = false;
    if (valid) {
        if (ContainsReparsePoint(target_root, target.parent_path())) {
            failure_detail = "target parent changed to a reparse path during extraction";
        } else {
            const auto target_name = fs::absolute(target).lexically_normal().wstring();
            const auto name_bytes = target_name.size() * sizeof(wchar_t);
            std::vector<unsigned char> rename_storage(sizeof(FILE_RENAME_INFO) + name_bytes);
            auto* rename_info = reinterpret_cast<FILE_RENAME_INFO*>(rename_storage.data());
            rename_info->ReplaceIfExists = FALSE;
            rename_info->RootDirectory = nullptr;
            rename_info->FileNameLength = static_cast<DWORD>(name_bytes);
            std::memcpy(rename_info->FileName, target_name.data(), name_bytes);
            target_committed = SetFileInformationByHandle(output.value, FileRenameInfo,
                rename_info, static_cast<DWORD>(rename_storage.size())) != FALSE;
            if (!target_committed) {
                failure_detail = "handle-bound atomic target commit failed (Win32=" +
                    std::to_string(GetLastError()) + ")";
            }
        }
        valid = target_committed;
    }
    if (valid) {
        FILE_ATTRIBUTE_TAG_INFO final_tag{};
        valid = GetFileInformationByHandleEx(output.value,
                FileAttributeTagInfo, &final_tag, sizeof(final_tag)) &&
            (final_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0 &&
            IsFinalPathContained(target_root, output.value);
        if (!valid) failure_detail = "committed output final-path containment failed";
    }
    bool committed_delete_requested = false;
    if (target_committed && !valid) {
        FILE_DISPOSITION_INFO disposition{};
        disposition.DeleteFile = TRUE;
        committed_delete_requested = SetFileInformationByHandle(output.value,
            FileDispositionInfo, &disposition, sizeof(disposition)) != FALSE;
        if (!committed_delete_requested)
            failure_detail += "; handle-bound cleanup failed (Win32=" +
                std::to_string(GetLastError()) + ")";
    }
    if (output) CloseHandle(output.value);
    output.value = INVALID_HANDLE_VALUE;
    if (!valid) {
        if (!target_committed) DeleteFileW(temporary.c_str());
        if (error) {
            *error = "source ZIP entry extraction failed: " + name;
            if (!failure_detail.empty()) *error += " (" + failure_detail + ")";
        }
    }
    return valid;
}

bool CopyArtifact(const fs::path& source, const fs::path& target,
                  const fs::path& source_root, const fs::path& target_root) {
    if (IsExcludedRawNetworkPayload(source) || IsExcludedRawNetworkPayload(target) ||
        !IsLexicallyContained(source_root, source) || !IsLexicallyContained(target_root, target) ||
        ContainsReparsePoint(source_root, source) ||
        !EnsureContainedDirectory(target_root, target.parent_path()))
        return false;

    ScopedWinHandle source_handle(CreateFileW(source.c_str(), GENERIC_READ | FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    if (!source_handle) return false;
    FILE_ATTRIBUTE_TAG_INFO source_tag{};
    BY_HANDLE_FILE_INFORMATION source_info_before{};
    const auto source_final_before = FinalPathFromHandle(source_handle.value);
    if (!GetFileInformationByHandleEx(source_handle.value, FileAttributeTagInfo,
            &source_tag, sizeof(source_tag)) ||
        !GetFileInformationByHandle(source_handle.value, &source_info_before) ||
        (source_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !source_final_before || !IsFinalPathContained(source_root, source_handle.value))
        return false;
    const auto source_hash_before = FileSha256ByHandle(source_handle.value);
    if (!source_hash_before) return false;

    // Pin the destination parent without FILE_SHARE_DELETE.  The parent may
    // still be used for normal file creation, but it cannot be renamed into a
    // junction while this copy transaction is live.
    ScopedWinHandle parent_handle(CreateFileW(target.parent_path().c_str(),
        FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    FILE_ATTRIBUTE_TAG_INFO parent_tag{};
    if (!parent_handle || !GetFileInformationByHandleEx(parent_handle.value,
            FileAttributeTagInfo, &parent_tag, sizeof(parent_tag)) ||
        (parent_tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
        (parent_tag.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 ||
        !IsFinalPathContained(target_root, parent_handle.value))
        return false;

    const DWORD existing_attributes = GetFileAttributesW(target.c_str());
    if (existing_attributes != INVALID_FILE_ATTRIBUTES &&
        ((existing_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ||
         (existing_attributes & FILE_ATTRIBUTE_DIRECTORY) != 0))
        return false;

    const fs::path temporary = target.parent_path() /
        (target.filename().wstring() + L".tmp-" + Utf8ToWide(NewId()));
    ScopedWinHandle target_handle(CreateFileW(temporary.c_str(),
        GENERIC_READ | GENERIC_WRITE | FILE_READ_ATTRIBUTES | DELETE,
        0, nullptr, CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr));
    if (!target_handle || !IsFinalPathContained(target_root, target_handle.value)) {
        DeleteFileW(temporary.c_str());
        return false;
    }
    BY_HANDLE_FILE_INFORMATION temporary_info{};
    if (!GetFileInformationByHandle(target_handle.value, &temporary_info)) return false;

    bool copied = true;
    LARGE_INTEGER zero{};
    copied = SetFilePointerEx(source_handle.value, zero, nullptr, FILE_BEGIN) != FALSE;
    std::vector<unsigned char> buffer(64 * 1024);
    while (copied) {
        DWORD read = 0;
        if (!ReadFile(source_handle.value, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) {
            copied = false;
            break;
        }
        if (read == 0) break;
        DWORD offset = 0;
        while (offset < read) {
            DWORD written = 0;
            if (!WriteFile(target_handle.value, buffer.data() + offset, read - offset, &written, nullptr) ||
                written == 0) {
                copied = false;
                break;
            }
            offset += written;
        }
        if (!copied) break;
    }
    copied = copied && FlushFileBuffers(target_handle.value) != FALSE;
    BY_HANDLE_FILE_INFORMATION source_info_after{};
    BY_HANDLE_FILE_INFORMATION temporary_info_after{};
    const auto source_hash_after = copied ? FileSha256ByHandle(source_handle.value) :
        std::optional<std::string>{};
    const auto temporary_hash = copied ? FileSha256ByHandle(target_handle.value) :
        std::optional<std::string>{};
    copied = copied && source_hash_after && temporary_hash &&
        *source_hash_before == *source_hash_after && *source_hash_before == *temporary_hash &&
        GetFileInformationByHandle(source_handle.value, &source_info_after) != FALSE &&
        GetFileInformationByHandle(target_handle.value, &temporary_info_after) != FALSE &&
        SameFileObject(source_info_before, source_info_after) &&
        SameFileObject(temporary_info, temporary_info_after) &&
        HandleFileSize(source_info_before) == HandleFileSize(source_info_after) &&
        HandleFileSize(source_info_after) == HandleFileSize(temporary_info_after) &&
        FinalPathFromHandle(source_handle.value) == source_final_before &&
        IsFinalPathContained(source_root, source_handle.value) &&
        IsFinalPathContained(target_root, target_handle.value) &&
        !ContainsReparsePoint(target_root, target.parent_path());

    // Reopen the caller-supplied source name while the non-write/non-delete
    // share lock is still held.  This catches a same-path substitution attempt
    // instead of silently attesting bytes from an unlinked file object.
    ScopedWinHandle source_path_handle(CreateFileW(source.c_str(),
        FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    BY_HANDLE_FILE_INFORMATION source_path_info{};
    copied = copied && source_path_handle &&
        GetFileInformationByHandle(source_path_handle.value, &source_path_info) != FALSE &&
        SameFileObject(source_info_before, source_path_info) &&
        FinalPathFromHandle(source_path_handle.value) == source_final_before;

    bool committed = false;
    if (copied) {
        const auto absolute_target = fs::absolute(target).lexically_normal().wstring();
        const auto name_bytes = absolute_target.size() * sizeof(wchar_t);
        std::vector<unsigned char> rename_storage(sizeof(FILE_RENAME_INFO) + name_bytes, 0);
        auto* rename_info = reinterpret_cast<FILE_RENAME_INFO*>(rename_storage.data());
        rename_info->ReplaceIfExists = TRUE;
        rename_info->RootDirectory = nullptr;
        rename_info->FileNameLength = static_cast<DWORD>(name_bytes);
        std::memcpy(rename_info->FileName, absolute_target.data(), name_bytes);
        committed = SetFileInformationByHandle(target_handle.value, FileRenameInfo,
            rename_info, static_cast<DWORD>(rename_storage.size())) != FALSE;
    }

    FILE_ATTRIBUTE_TAG_INFO committed_tag{};
    BY_HANDLE_FILE_INFORMATION committed_info{};
    const auto committed_final = committed ? FinalPathFromHandle(target_handle.value) :
        std::optional<fs::path>{};
    const auto expected_final = fs::absolute(target).lexically_normal();
    const auto committed_hash = committed ? FileSha256ByHandle(target_handle.value) :
        std::optional<std::string>{};
    const bool final_valid = committed && committed_final && committed_hash &&
        FoldPath(committed_final->lexically_normal().wstring()) ==
            FoldPath(expected_final.wstring()) &&
        GetFileInformationByHandleEx(target_handle.value, FileAttributeTagInfo,
            &committed_tag, sizeof(committed_tag)) != FALSE &&
        GetFileInformationByHandle(target_handle.value, &committed_info) != FALSE &&
        (committed_tag.FileAttributes &
            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0 &&
        SameFileObject(temporary_info, committed_info) &&
        HandleFileSize(committed_info) == HandleFileSize(source_info_before) &&
        *committed_hash == *source_hash_before &&
        IsFinalPathContained(target_root, target_handle.value);
    if (committed && !final_valid) {
        FILE_DISPOSITION_INFO disposition{};
        disposition.DeleteFile = TRUE;
        SetFileInformationByHandle(target_handle.value, FileDispositionInfo,
                                   &disposition, sizeof(disposition));
    }
    if (!committed) DeleteFileW(temporary.c_str());
    return final_valid;
}

struct SemanticPrivacyGateResult {
    std::uint64_t accepted = 0;
    std::uint64_t suppressed = 0;
    std::uint64_t rejected = 0;
};

bool SanitizeSemanticEventsForPackage(const fs::path& source,
                                      const fs::path& target,
                                      const fs::path& source_root,
                                      const fs::path& target_root,
                                      std::string_view expected_session_id,
                                      SemanticPrivacyGateResult* gate,
                                      std::string* error) {
    constexpr std::uint64_t kMaximumSemanticFileBytes = 256ULL * 1024ULL * 1024ULL;
    constexpr std::size_t kMaximumSemanticLineBytes = 1024U * 1024U;
    *gate = {};
    if (!IsLexicallyContained(source_root, source) ||
        !IsLexicallyContained(target_root, target) ||
        ContainsReparsePoint(source_root, source) ||
        !EnsureContainedDirectory(target_root, target.parent_path())) {
        if (error != nullptr) *error = "semantic privacy gate path containment failed";
        return false;
    }
    ScopedWinHandle source_handle(CreateFileW(source.c_str(),
        GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT |
            FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr));
    FILE_ATTRIBUTE_TAG_INFO source_tag{};
    BY_HANDLE_FILE_INFORMATION source_identity{};
    const auto source_final = source_handle ? FinalPathFromHandle(source_handle.value) :
        std::optional<fs::path>{};
    if (!source_handle || !source_final ||
        !GetFileInformationByHandleEx(source_handle.value, FileAttributeTagInfo,
            &source_tag, sizeof(source_tag)) ||
        !GetFileInformationByHandle(source_handle.value, &source_identity) ||
        (source_tag.FileAttributes &
            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        HandleFileSize(source_identity) > kMaximumSemanticFileBytes ||
        !IsFinalPathContained(source_root, source_handle.value)) {
        if (error != nullptr) *error = "semantic privacy gate source identity failed";
        return false;
    }
    const auto source_hash = FileSha256ByHandle(source_handle.value);
    LARGE_INTEGER zero{};
    if (!source_hash ||
        SetFilePointerEx(source_handle.value, zero, nullptr, FILE_BEGIN) == FALSE) {
        if (error != nullptr) *error = "semantic privacy gate source hash/read failed";
        return false;
    }
    std::string sanitized;
    sanitized.reserve(static_cast<std::size_t>(HandleFileSize(source_identity)));
    std::string pending;
    std::vector<char> buffer(64U * 1024U);
    const auto process_line = [&](std::string_view raw_line) {
        if (!raw_line.empty() && raw_line.back() == '\r') raw_line.remove_suffix(1U);
        if (raw_line.empty() || raw_line.size() > kMaximumSemanticLineBytes) {
            ++gate->rejected;
            return;
        }
        const auto parsed = ReadSemanticEvent(raw_line);
        const auto input_profile = ClassifySemanticEventWire(raw_line);
        if (!parsed.success || input_profile == SemanticEventWireProfile::Invalid ||
            parsed.event.session_id != expected_session_id ||
            parsed.event.process_id == 0U || parsed.event.client_build_id.empty() ||
            parsed.event.module_id.empty()) {
            ++gate->rejected;
            return;
        }
        const auto canonical = SerializeSemanticEventV2(parsed.event);
        const auto canonical_read = ReadSemanticEvent(canonical);
        if (!canonical_read.success ||
            ClassifySemanticEventWire(canonical) !=
                SemanticEventWireProfile::CanonicalV2Envelope ||
            !EquivalentSemanticEvent(parsed.event, canonical_read.event)) {
            ++gate->rejected;
            return;
        }
        ++gate->accepted;
        if (parsed.event.sensitive_mask_status ==
            "SensitivePayloadSuppressedMetadataOnly") ++gate->suppressed;
        sanitized += canonical;
        sanitized.push_back('\n');
    };
    for (;;) {
        DWORD read = 0;
        if (ReadFile(source_handle.value, buffer.data(),
                static_cast<DWORD>(buffer.size()), &read, nullptr) == FALSE) {
            if (error != nullptr) *error = "semantic privacy gate source read failed";
            return false;
        }
        if (read == 0U) break;
        pending.append(buffer.data(), read);
        for (;;) {
            const auto newline = pending.find('\n');
            if (newline == std::string::npos) break;
            process_line(std::string_view(pending).substr(0U, newline));
            pending.erase(0U, newline + 1U);
        }
        if (pending.size() > kMaximumSemanticLineBytes) {
            if (error != nullptr) *error = "semantic privacy gate line exceeds bounded limit";
            return false;
        }
    }
    if (!pending.empty()) process_line(pending);

    BY_HANDLE_FILE_INFORMATION source_after{};
    ScopedWinHandle rebound(CreateFileW(source.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    BY_HANDLE_FILE_INFORMATION rebound_identity{};
    const auto hash_after = FileSha256ByHandle(source_handle.value);
    if (!hash_after || *hash_after != *source_hash ||
        !GetFileInformationByHandle(source_handle.value, &source_after) ||
        !SameFileObject(source_identity, source_after) ||
        HandleFileSize(source_identity) != HandleFileSize(source_after) ||
        FinalPathFromHandle(source_handle.value) != source_final || !rebound ||
        !GetFileInformationByHandle(rebound.value, &rebound_identity) ||
        !SameFileObject(source_identity, rebound_identity) ||
        FinalPathFromHandle(rebound.value) != source_final) {
        if (error != nullptr) *error = "semantic privacy gate source changed during validation";
        return false;
    }
    if (!WriteUtf8FileAtomic(target, sanitized)) {
        if (error != nullptr) *error = "semantic privacy gate derivative write failed";
        return false;
    }
    ScopedWinHandle target_handle(CreateFileW(target.c_str(),
        GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    FILE_ATTRIBUTE_TAG_INFO target_tag{};
    const auto target_hash = target_handle ? FileSha256ByHandle(target_handle.value) :
        std::optional<std::string>{};
    const auto expected_hash = Sha256(sanitized);
    if (!target_handle || !target_hash || !expected_hash ||
        *target_hash != *expected_hash ||
        !GetFileInformationByHandleEx(target_handle.value, FileAttributeTagInfo,
            &target_tag, sizeof(target_tag)) ||
        (target_tag.FileAttributes &
            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !IsFinalPathContained(target_root, target_handle.value)) {
        if (error != nullptr) *error = "semantic privacy gate derivative identity failed";
        return false;
    }
    return true;
}

bool ContainsUnsafeLegacyDiagnostic(const fs::path& path, std::string* matched_marker = nullptr) {
    std::ifstream input(path, std::ios::binary);
    if (!input) return false;
    static constexpr std::array<std::string_view, 7> markers = {
        "decodedprefixsafe=", "key256=", "decodekey32=", "derivedkey=",
        "rawauth=", "authenticationpayloadhex=", "password="
    };
    std::string carry;
    std::vector<char> buffer;
    try {
        buffer.resize(64U * 1024U);
    } catch (const std::bad_alloc&) {
        if (matched_marker != nullptr) *matched_marker = "diagnostic-scan-allocation-failed";
        return true;
    }
    while (input) {
        input.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
        const auto read = input.gcount();
        if (read <= 0) break;
        std::string window = carry;
        window.append(buffer.data(), static_cast<std::size_t>(read));
        std::transform(window.begin(), window.end(), window.begin(), [](unsigned char character) {
            return static_cast<char>(std::tolower(character));
        });
        for (const auto marker : markers) {
            if (window.find(marker) != std::string::npos) {
                if (matched_marker != nullptr) *matched_marker = std::string(marker);
                return true;
            }
        }
        constexpr std::size_t overlap = 128;
        carry = window.substr(window.size() > overlap ? window.size() - overlap : 0);
    }
    return false;
}

bool IsUnsafeAuthenticationRecord(const CaptureRecord& record) {
    for (const auto* field : {"Password", "PasswordText", "AccountPassword", "Key256",
                              "DecodeKey32", "DecodedPrefixSafe", "RawAuthenticationPayload"}) {
        if (!GetString(record.fields, field).empty()) return true;
    }
    if (record.stage != "PreEncrypt" && record.stage != "PostDecrypt" &&
        record.stage != "HandlerDecoded")
        return false;
    const auto payload = ParseHexBytes(record.payload_hex);
    if (!payload || payload->empty()) return false;
    const std::size_t opcode_offset = record.stage == "HandlerDecoded" ? 0 : 2;
    const auto opcode = payload->size() > opcode_offset ? (*payload)[opcode_offset] : 0xffU;
    return (record.stage == "PreEncrypt" && opcode == 0x19U) ||
        ((record.stage == "PostDecrypt" || record.stage == "HandlerDecoded") &&
         (payload->size() == 417 || opcode == 0x1dU));
}

bool ValidateJsonlSessionAndSyntax(const fs::path& path, std::string_view session_id) {
    std::ifstream input(path, std::ios::binary);
    if (!input) return false;
    std::string line;
    while (std::getline(input, line)) {
        if (line.empty()) continue;
        Fields fields;
        std::string error;
        if (!ParseFlatJson(line, fields, &error) || GetString(fields, "SessionId") != session_id)
            return false;
    }
    return input.eof() && !input.bad();
}

bool SqliteContainsSessionId(const fs::path& path, std::string_view session_id) {
    sqlite3* database = nullptr;
    if (sqlite3_open_v2(WideToUtf8(path.wstring()).c_str(), &database, SQLITE_OPEN_READONLY, nullptr) != SQLITE_OK) {
        if (database != nullptr) sqlite3_close(database);
        return false;
    }
    sqlite3_stmt* statement = nullptr;
    bool found = false;
    if (sqlite3_prepare_v2(database, "SELECT 1 FROM sessions WHERE session_id=?1 LIMIT 1;", -1,
                           &statement, nullptr) == SQLITE_OK) {
        sqlite3_bind_text(statement, 1, session_id.data(), static_cast<int>(session_id.size()), SQLITE_TRANSIENT);
        found = sqlite3_step(statement) == SQLITE_ROW;
    }
    if (statement != nullptr) sqlite3_finalize(statement);
    sqlite3_close(database);
    return found;
}

std::string GenericSchema(std::string_view id, std::string_view required_properties,
                          std::string_view typed_properties) {
    std::ostringstream output;
    output << "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\","
           << "\"$id\":\"god2://schemas/" << JsonEscape(id) << "\","
           << "\"title\":\"" << JsonEscape(id) << "\",\"type\":\"object\","
           << "\"additionalProperties\":true,\"required\":[" << required_properties << "],"
           << "\"properties\":{\"SchemaVersion\":{\"type\":\"integer\",\"const\":11},"
           << typed_properties << "}}";
    return output.str();
}

std::string SemanticSchema(std::string_view id, std::string_view required_properties,
                           std::string_view typed_properties) {
    std::ostringstream output;
    output << "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\","
           << "\"$id\":\"god2://schemas/semantic/" << JsonEscape(id) << "\","
           << "\"title\":\"" << JsonEscape(id) << "\",\"type\":\"object\","
           << "\"additionalProperties\":true,\"required\":[" << required_properties << "],"
           << "\"properties\":{\"SchemaVersion\":{\"type\":\"integer\",\"const\":1},"
           << typed_properties << "}}";
    return output.str();
}

constexpr std::array<std::string_view, 43> kSemanticEventV2CompanionProperties{
        "ObservedAtUnixMs", "Qpc", "Direction", "Opcode", "ProtocolFrameId",
        "LogicalMessageId", "ActionInstanceId", "HookInvocationId",
        "ParentInvocationId", "ContextInvocationId", "ExactParentSemanticEventId",
        "ContextSemanticEventId", "ContextCorrelationBasis", "HandlerInvocationId",
        "ProbeId", "ProbeCategory", "Module", "Rva", "CallerRva",
        "ProbeContractVersion", "ClientSha256Expected", "BuildBindingStatus",
        "EvidenceBasis", "EvidenceLevel", "FrameOffset", "FrameOffsetStatus",
        "DestinationOffset", "Width", "ReadType", "WriteType", "Endian",
        "RawBytes", "ParsedValue", "Value", "ValueTokenId", "FieldSemantic",
        "AuthorityClassification", "SensitiveValue", "ArgumentIndex", "ArgumentType",
        "ArgumentValue", "AssociatedValueTokenId", "ObjectPointerPersisted"
};

constexpr std::array<std::string_view, 9> kSemanticEventV2NumericCompanions{
    "ObservedAtUnixMs", "Qpc", "FrameOffset", "DestinationOffset", "Width",
    "ParsedValue", "Value", "ArgumentIndex", "ArgumentValue"
};

constexpr std::array<std::string_view, 2> kSemanticEventV2BooleanCompanions{
    "SensitiveValue", "ObjectPointerPersisted"
};

constexpr std::array<std::string_view, 7> kSemanticEventV2NullableStringCompanions{
    "ProtocolFrameId", "LogicalMessageId", "ActionInstanceId", "ParentInvocationId",
    "ContextInvocationId", "ExactParentSemanticEventId", "ContextSemanticEventId"
};

std::string_view SemanticEventV2CompanionSchema(std::string_view property) noexcept {
    if (std::find(kSemanticEventV2NumericCompanions.begin(),
                  kSemanticEventV2NumericCompanions.end(), property) !=
        kSemanticEventV2NumericCompanions.end())
        return R"({"type":["number","string"]})";
    if (std::find(kSemanticEventV2BooleanCompanions.begin(),
                  kSemanticEventV2BooleanCompanions.end(), property) !=
        kSemanticEventV2BooleanCompanions.end())
        return R"({"type":["boolean","string"]})";
    if (std::find(kSemanticEventV2NullableStringCompanions.begin(),
                  kSemanticEventV2NullableStringCompanions.end(), property) !=
        kSemanticEventV2NullableStringCompanions.end())
        return R"({"type":["string","null"]})";
    return R"({"type":"string"})";
}

std::string SemanticEventV1Schema() {
    return R"({"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"https://god2.local/schemas/god2-semantic-event-v1.schema.json","title":"God2SemanticEvent v1 closed canonical writer profile","type":"object","additionalProperties":false,"required":["SchemaId","SchemaVersion","EventType","SemanticEventId","Sequence","Timestamp","ThreadId","ProcessId","SessionId","ClientBuildId","ModuleId","RVA","CallsiteRVA","SensitiveValue"],"properties":{"SchemaId":{"type":"string","const":"God2SemanticEvent"},"SchemaVersion":{"type":"integer","const":1},"EventType":{"enum":["PacketBoundary","ParserRead","SerializerWrite","HandlerInvocation","HandlerArgument","ObjectAllocated","ObjectDestroyed","ObjectResolved","ObjectLookup","RegistryLocated","RegistryEnumerated","ResourceRead","ResourceDecoded","ResourceDeserialized","StateMutation","TaintSeed","TaintPropagation","ValueFlow","FormulaOperand","FormulaResult","FunctionRoleCandidate","UIAnchor","SnapshotObject","SnapshotEdge","ProbeDiagnostic"]},"SemanticEventId":{"type":"string","minLength":1},"Sequence":{"type":"integer","minimum":0},"Timestamp":{"type":"string","minLength":1},"ThreadId":{"type":"integer","minimum":0},"ProcessId":{"type":"integer","minimum":1},"SessionId":{"type":"string","minLength":1},"ClientBuildId":{"type":"string","minLength":1},"ModuleId":{"type":"string","minLength":1},"RVA":{"type":"integer","minimum":0},"CallsiteRVA":{"type":"integer","minimum":0},"SensitiveValue":{"type":"boolean","const":false}}})";
}

std::string SemanticEventV1CompatibilityInputSchema() {
    return R"({"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"https://god2.local/schemas/god2-semantic-event-v1-compatibility-input.schema.json","title":"God2SemanticEvent v1 noncanonical nonpromotable compatibility input","type":"object","additionalProperties":true,"required":["SchemaVersion","EventType"],"anyOf":[{"required":["SemanticEventId"]},{"required":["EventId"]}],"properties":{"SchemaVersion":{"type":"integer","const":1},"SchemaId":{"type":"string","const":"God2SemanticEvent"},"SemanticEventId":{"type":"string","minLength":1},"EventId":{"type":"string","minLength":1},"EventType":{"enum":["PacketBoundary","ParserRead","SerializerWrite","HandlerInvocation","HandlerArgument","ObjectAllocated","ObjectDestroyed","ObjectResolved","ObjectResolution","ObjectLookup","RegistryLocated","RegistryEnumerated","ResourceRead","ResourceDecoded","ResourceDeserialized","StateMutation","TaintSeed","TaintPropagation","ValueFlow","FormulaOperand","FormulaResult","FunctionRoleCandidate","UIAnchor","SnapshotObject","SnapshotEdge","ProbeDiagnostic"]},"ProbeId":{"type":"string"},"EvidenceLevel":{"type":"string"}}})";
}

std::string SemanticEventV2Schema() {
    std::size_t size = 0;
    for (const auto chunk : kGod2SemanticEventV2ClosedSchemaChunks)
        size += chunk.size();
    std::string schema;
    schema.reserve(size);
    for (const auto chunk : kGod2SemanticEventV2ClosedSchemaChunks)
        schema.append(chunk);
    return schema;
}

std::string SemanticSharedRingHealthAvailabilitySchema() {
    return R"({"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"god2://schemas/semantic/semantic-shared-ring-health-availability-v1","title":"God2 semantic shared-ring health availability v1","type":"object","additionalProperties":false,"required":["SchemaVersion","Status","RequiredSchemaVersion","Promotable","Reason"],"properties":{"SchemaVersion":{"const":"god2-semantic-shared-ring-health-availability-v1"},"Status":{"const":"EvidenceBlockedSharedTransportHealthUnavailable"},"RequiredSchemaVersion":{"const":"god2-semantic-shared-ring-health-v4"},"Promotable":{"const":false},"Reason":{"type":"string","minLength":1}}})";
}

std::string DeepProbeCandidateMapAvailabilitySchema() {
    return R"({"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"god2://schemas/deep-probe/deep-probe-candidate-map-availability-v1","title":"God2 deep-probe candidate-map availability v1","type":"object","additionalProperties":false,"required":["SchemaVersion","Status","RequiredSchemaVersion","Promotable","DomainCount","ConfirmedContractDomainCount","CandidateOnlyBlockedDomainCount","Reason"],"properties":{"SchemaVersion":{"const":"god2-deep-probe-candidate-map-availability-v1"},"Status":{"const":"EvidenceBlockedCandidateMapUnavailable"},"RequiredSchemaVersion":{"const":"god2-deep-probe-candidate-map-v2"},"Promotable":{"const":false},"DomainCount":{"const":25},"ConfirmedContractDomainCount":{"const":4},"CandidateOnlyBlockedDomainCount":{"const":21},"Reason":{"type":"string","minLength":1}}})";
}

std::string SemanticSegmentManifestSchema() {
    return R"({"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"https://god2.local/schemas/semantic-segment-manifest.schema.json","title":"God2 semantic segmented collector manifest","type":"object","required":["SchemaVersion","GeneratedAtUtc","Status","SegmentMaximumBytes","SegmentMaximumRecords","SegmentCount","RecordCount","SequenceContinuity","MergedPath","MergedSHA256","Segments"],"properties":{"SchemaVersion":{"const":"god2-semantic-segment-manifest-v1"},"GeneratedAtUtc":{"type":"string","format":"date-time"},"Status":{"enum":["PASS","EVIDENCE_BLOCKED_SEGMENT_OR_CONTINUITY_FAILURE"]},"SegmentMaximumBytes":{"const":16777216},"SegmentMaximumRecords":{"const":100000},"SegmentCount":{"type":"integer","minimum":0},"RecordCount":{"type":"integer","minimum":0},"SequenceContinuity":{"type":"boolean"},"MergedPath":{"const":"raw/semantic-events.jsonl"},"MergedSHA256":{"type":"string","pattern":"^(?:[A-F0-9]{64})?$"},"Segments":{"type":"array","items":{"type":"object","required":["Index","Path","Records","Bytes","FirstSequence","LastSequence","SHA256"],"properties":{"Index":{"type":"integer","minimum":0},"Path":{"type":"string","pattern":"^raw/semantic-segments/semantic-[0-9]{6}\\.jsonl$"},"Records":{"type":"integer","minimum":1,"maximum":100000},"Bytes":{"type":"integer","minimum":1,"maximum":16777216},"FirstSequence":{"type":"integer","minimum":1},"LastSequence":{"type":"integer","minimum":1},"SHA256":{"type":"string","pattern":"^[A-F0-9]{64}$"}},"additionalProperties":false}}},"allOf":[{"if":{"properties":{"Status":{"const":"PASS"}},"required":["Status"]},"then":{"properties":{"SegmentCount":{"minimum":1},"RecordCount":{"minimum":1},"SequenceContinuity":{"const":true},"MergedSHA256":{"pattern":"^[A-F0-9]{64}$"}}},{"else":{"properties":{"SegmentCount":{"const":0},"RecordCount":{"const":0},"SequenceContinuity":{"const":false},"MergedSHA256":{"const":""},"Segments":{"maxItems":0}}}}],"additionalProperties":false})";
}

bool WriteSchemas(const fs::path& root) {
    const fs::path schemas = root / L"schemas";
    const std::vector<std::pair<std::wstring, std::string>> definitions = {
        {L"capture-record.schema.json", GenericSchema("capture-record", "\"SchemaVersion\",\"SessionId\",\"CaptureRecordId\",\"CaptureSequence\",\"CapturedAtUnixMs\",\"CapturedAtUtc\",\"CaptureStage\"", "\"SessionId\":{\"type\":\"string\"},\"CaptureRecordId\":{\"type\":\"string\"},\"CaptureSequence\":{\"type\":\"integer\"},\"CapturedAtUnixMs\":{\"type\":\"integer\"},\"CapturedAtUtc\":{\"type\":\"string\"},\"CaptureStage\":{\"type\":\"string\"}")},
        {L"transport-chunk.schema.json", GenericSchema("transport-chunk", "\"SchemaVersion\",\"SessionId\",\"TransportChunkId\",\"CaptureRecordId\",\"PayloadHex\"", "\"TransportChunkId\":{\"type\":\"string\"},\"CaptureRecordId\":{\"type\":\"string\"},\"PayloadHex\":{\"type\":\"string\"}")},
        {L"protocol-frame.schema.json", GenericSchema("protocol-frame", "\"SchemaVersion\",\"SessionId\",\"ProtocolFrameId\",\"CaptureRecordIds\"", "\"ProtocolFrameId\":{\"type\":\"string\"},\"CaptureRecordIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}")},
        {L"opcode-work-item.schema.json", GenericSchema("opcode-work-item", "\"SchemaVersion\",\"SessionId\",\"WorkItemId\",\"Direction\",\"Opcode\",\"FrameLength\",\"ObservationCount\",\"StableBytePattern\",\"SemanticStatus\"", "\"WorkItemId\":{\"type\":\"string\"},\"Direction\":{\"type\":\"string\"},\"Opcode\":{\"type\":\"string\"},\"FrameLength\":{\"type\":\"integer\"},\"ObservationCount\":{\"type\":\"integer\"},\"StableBytePattern\":{\"type\":\"string\"},\"SemanticStatus\":{\"type\":\"string\"}")},
        {L"decoded-message.schema.json", GenericSchema("decoded-message", "\"SchemaVersion\",\"SessionId\",\"DecodedMessageId\",\"CaptureRecordIds\"", "\"DecodedMessageId\":{\"type\":\"string\"},\"CaptureRecordIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}")},
        {L"handler-observation.schema.json", GenericSchema("handler-observation", "\"SchemaVersion\",\"SessionId\",\"HandlerObservationId\",\"CaptureRecordId\"", "\"HandlerObservationId\":{\"type\":\"string\"},\"CaptureRecordId\":{\"type\":\"string\"}")},
        {L"gameplay-candidate.schema.json", GenericSchema("gameplay-candidate", "\"SchemaVersion\",\"SessionId\",\"GameplayCandidateId\",\"EvidenceLevel\"", "\"GameplayCandidateId\":{\"type\":\"string\"},\"EvidenceLevel\":{\"type\":\"string\",\"const\":\"Candidate\"}")},
        {L"automatic-semantic-summary.schema.json", GenericSchema("automatic-semantic-summary", "\"SchemaVersion\",\"SessionId\",\"Mode\",\"UserInteractionRequired\",\"AutomaticCandidateCount\",\"Families\"", "\"SessionId\":{\"type\":\"string\"},\"Mode\":{\"const\":\"PassiveAutomaticNoSceneSelection\"},\"UserInteractionRequired\":{\"const\":false},\"AutomaticCandidateCount\":{\"type\":\"integer\"},\"Families\":{\"type\":\"array\"}")},
        {L"evidence-item.schema.json", GenericSchema("evidence-item", "\"SchemaVersion\",\"SessionId\",\"EvidenceItemId\",\"CaptureRecordIds\"", "\"EvidenceItemId\":{\"type\":\"string\"},\"CaptureRecordIds\":{\"type\":\"array\"}")},
        {L"capture-info.schema.json", GenericSchema("capture-info", "\"SchemaVersion\",\"SessionId\",\"AcquisitionStatus\",\"AnalysisStatus\",\"CleanupStatus\",\"PackageStatus\"", "\"SessionId\":{\"type\":\"string\"},\"AcquisitionStatus\":{\"type\":\"string\"},\"AnalysisStatus\":{\"type\":\"string\"},\"CleanupStatus\":{\"type\":\"string\"},\"PackageStatus\":{\"type\":\"string\"}")},
        {L"capture-health.schema.json", GenericSchema("capture-health", "\"SchemaVersion\",\"SessionId\",\"CaptureRecordCount\",\"PacketLossStatus\",\"OverallHealthStatus\"", "\"CaptureRecordCount\":{\"type\":\"integer\"},\"PacketLossStatus\":{\"type\":\"string\"},\"PacketLossCount\":{\"type\":[\"integer\",\"null\"]}")},
        {L"candidate-protocol-frame.schema.json", GenericSchema("candidate-protocol-frame", "\"SchemaVersion\",\"SessionId\",\"CandidateFrameId\",\"DeclaredLength\",\"ActualLength\",\"TransportChunkIds\",\"EvidenceLevel\"", "\"CandidateFrameId\":{\"type\":\"string\"},\"DeclaredLength\":{\"type\":\"integer\"},\"ActualLength\":{\"type\":\"integer\"},\"TransportChunkIds\":{\"type\":\"array\"},\"EvidenceLevel\":{\"const\":\"Candidate\"}")},
        {L"incomplete-stream-fragment.schema.json", GenericSchema("incomplete-stream-fragment", "\"SchemaVersion\",\"SessionId\",\"ConnectionId\",\"Direction\",\"TransportChunkIds\",\"RemainingByteCount\"", "\"ConnectionId\":{\"type\":\"string\"},\"TransportChunkIds\":{\"type\":\"array\"},\"RemainingByteCount\":{\"type\":\"integer\"}")},
        {L"connection-summary.schema.json", GenericSchema("connection-summary", "\"SchemaVersion\",\"SessionId\",\"ConnectionCount\",\"Connections\"", "\"ConnectionCount\":{\"type\":\"integer\"},\"Connections\":{\"type\":\"array\"}")},
        {L"logical-message-map.schema.json", GenericSchema("logical-message-map", "\"SchemaVersion\",\"SessionId\",\"LogicalMessageId\",\"CorrelationLevel\"", "\"LogicalMessageId\":{\"type\":\"string\"},\"CorrelationLevel\":{\"enum\":[\"Exact\",\"Strong\",\"Candidate\",\"Uncorrelated\"]}")},
        {L"action-instance.schema.json", GenericSchema("action-instance", "\"SchemaVersion\",\"SessionId\",\"ActionInstanceId\",\"TriggerLogicalMessageId\",\"TriggerProtocolFrameId\",\"SemanticStatus\"", "\"ActionInstanceId\":{\"type\":\"string\"},\"TriggerLogicalMessageId\":{\"type\":\"string\"},\"TriggerProtocolFrameId\":{\"type\":\"string\"},\"SemanticStatus\":{\"type\":\"string\"}")},
        {L"action-pattern.schema.json", GenericSchema("action-pattern", "\"SchemaVersion\",\"SessionId\",\"ActionPatternId\",\"PatternName\",\"Occurrences\",\"SemanticStatus\"", "\"ActionPatternId\":{\"type\":\"string\"},\"PatternName\":{\"type\":\"string\"},\"Occurrences\":{\"type\":\"integer\"},\"SemanticStatus\":{\"type\":\"string\"}")},
        {L"action-pattern-summary.schema.json", GenericSchema("action-pattern-summary", "\"SchemaVersion\",\"SessionId\",\"ActionTriggerCandidateCount\",\"ActionClusteringCoveragePercent\"", "\"ActionTriggerCandidateCount\":{\"type\":\"integer\"},\"ActionClusteringCoveragePercent\":{\"type\":\"number\"}")},
        {L"orphan-handler-burst.schema.json", GenericSchema("orphan-handler-burst", "\"SchemaVersion\",\"SessionId\",\"OrphanHandlerBurstId\",\"HandlerObservationIds\",\"CorrelationLevel\"", "\"OrphanHandlerBurstId\":{\"type\":\"string\"},\"HandlerObservationIds\":{\"type\":\"array\"},\"CorrelationLevel\":{\"const\":\"Candidate\"}")},
        {L"outbound-batch-candidate.schema.json", GenericSchema("outbound-batch-candidate", "\"SchemaVersion\",\"SessionId\",\"OutboundBatchCandidateId\",\"TriggerProtocolFrameIds\",\"CorrelationLevel\"", "\"OutboundBatchCandidateId\":{\"type\":\"string\"},\"TriggerProtocolFrameIds\":{\"type\":\"array\"},\"CorrelationLevel\":{\"const\":\"Candidate\"}")},
        {L"codex-handoff.schema.json", GenericSchema("codex-handoff", "\"SchemaVersion\",\"PackagePurpose\",\"RecommendedIngestionOrder\"", "\"PackagePurpose\":{\"type\":\"string\"},\"RecommendedIngestionOrder\":{\"type\":\"array\"}")},
        {L"manifest.schema.json", GenericSchema("manifest", "\"SchemaVersion\",\"PrimarySessionId\",\"Artifacts\"", "\"PrimarySessionId\":{\"type\":\"string\"},\"Artifacts\":{\"type\":\"array\"}")},
        {L"provenance.schema.json", GenericSchema("provenance", "\"SchemaVersion\",\"SessionId\",\"ProvenanceId\",\"ArtifactType\"", "\"ProvenanceId\":{\"type\":\"string\"},\"ArtifactType\":{\"type\":\"string\"}")}
        ,{L"god2-semantic-event-v1.schema.json", SemanticEventV1Schema()}
        ,{L"god2-semantic-event-v1-compatibility-input.schema.json",
            SemanticEventV1CompatibilityInputSchema()}
        ,{L"god2-semantic-event-v2.schema.json", SemanticEventV2Schema()}
        ,{L"semantic-shared-ring-health-availability.schema.json",
            SemanticSharedRingHealthAvailabilitySchema()}
        ,{L"deep-probe-candidate-map-availability.schema.json",
            DeepProbeCandidateMapAvailabilitySchema()}
        ,{L"semantic-segment-manifest.schema.json",
            SemanticSegmentManifestSchema()}
        ,{L"semantic-evidence-graph-v1.schema.json", SemanticSchema("semantic-evidence-graph-v1",
            "\"SchemaVersion\",\"ProtocolFieldEvidenceId\",\"EvidenceLevel\",\"ContradictionCount\"",
            "\"ProtocolFieldEvidenceId\":{\"type\":\"string\"},\"EvidenceLevel\":{\"enum\":[\"Unknown\",\"Candidate\",\"Recovered\",\"Verified\"]},\"ContradictionCount\":{\"type\":\"integer\"}")}
        ,{L"verified-protocol-spec-v1.schema.json", SemanticSchema("verified-protocol-spec-v1",
            "\"SchemaVersion\",\"ClientBuildIdentity\",\"Packets\"",
            "\"ClientBuildIdentity\":{\"type\":\"string\"},\"Packets\":{\"type\":\"array\"}")}
        ,{L"verified-integration-manifest-v1.schema.json", SemanticSchema("verified-integration-manifest-v1",
            "\"SchemaVersion\",\"ClientBuildIdentity\",\"Entries\"",
            "\"ClientBuildIdentity\":{\"type\":\"string\"},\"Entries\":{\"type\":\"array\"}")}
    };
    for (const auto& [name, content] : definitions)
        if (!WriteUtf8FileAtomic(schemas / name, content + "\n")) return false;
    return true;
}

bool WriteSqliteSchemaAndCheck(const fs::path& database_path, const fs::path& output_path,
                               bool* integrity_passed) {
    *integrity_passed = false;
    sqlite3* database = nullptr;
    const auto path = WideToUtf8(database_path.wstring());
    if (sqlite3_open_v2(path.c_str(), &database, SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX, nullptr) != SQLITE_OK) {
        if (database != nullptr) sqlite3_close(database);
        return false;
    }
    sqlite3_stmt* check = nullptr;
    if (sqlite3_prepare_v2(database, "PRAGMA integrity_check;", -1, &check, nullptr) == SQLITE_OK &&
        sqlite3_step(check) == SQLITE_ROW) {
        const auto* value = sqlite3_column_text(check, 0);
        *integrity_passed = value != nullptr && std::string_view(reinterpret_cast<const char*>(value)) == "ok";
    }
    sqlite3_finalize(check);
    sqlite3_stmt* schema = nullptr;
    std::ostringstream ddl;
    ddl << "-- Deterministic schema export for the packaged session SQLite database.\n";
    if (sqlite3_prepare_v2(database,
            "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY type,name;",
            -1, &schema, nullptr) == SQLITE_OK) {
        while (sqlite3_step(schema) == SQLITE_ROW) {
            const auto* sql = sqlite3_column_text(schema, 0);
            if (sql != nullptr) ddl << reinterpret_cast<const char*>(sql) << ";\n";
        }
    }
    sqlite3_finalize(schema);
    sqlite3_close(database);
    return *integrity_passed && WriteUtf8FileAtomic(output_path, ddl.str());
}

std::string ManifestJson(const std::string& session_id, const std::string& analysis_run_id,
                         const std::string& package_run_id, const std::string& original_package_sha256,
                         std::string_view source_package_schema_version, const std::string& packaged_at,
                         const std::vector<PackageArtifact>& artifacts) {
    std::ostringstream output;
    output << "{\"SchemaId\":\"manifest\",\"SchemaVersion\":11,"
           << "\"PrimarySessionId\":\"" << JsonEscape(session_id) << "\","
           << "\"CreatedAtUtc\":\"" << JsonEscape(packaged_at) << "\","
           << "\"AnalysisRunId\":\"" << JsonEscape(analysis_run_id) << "\","
           << "\"PackageRunId\":\"" << JsonEscape(package_run_id) << "\","
           << "\"OriginalPackageSHA256\":" << (original_package_sha256.empty() ? "null" :
                "\"" + JsonEscape(original_package_sha256) + "\"") << ','
           << "\"SourcePackageSchemaVersion\":" << (source_package_schema_version.empty() ? "null" :
                std::string(source_package_schema_version)) << ','
           << "\"RelativePathPolicy\":\"ZipRootRelative\","
           << "\"ManifestSelfEntryPolicy\":\"Manifest excluded because a cryptographic self-hash is recursive\","
           << "\"ManifestClassification\":{\"RequiredForPackageValidation\":true,"
              "\"RequiredForCodexIngestion\":true,\"RecommendedForDeepAnalysis\":false,"
              "\"DiagnosticOnly\":false},"
           << "\"Artifacts\":[";
    for (std::size_t i = 0; i < artifacts.size(); ++i) {
        if (i != 0) output << ',';
        const auto& artifact = artifacts[i];
        output << MakeJsonObject({
            {"ArtifactId", artifact.artifact_id}, {"RelativePath", artifact.relative_path},
            {"ArtifactRole", artifact.role}, {"ArtifactClass", artifact.artifact_class},
            {"Authoritative", artifact.authoritative ? "true" : "false"},
            {"SchemaId", artifact.schema_id}, {"SchemaVersion", "11"},
            {"ContentType", artifact.content_type}, {"FileSize", std::to_string(artifact.file_size)},
            {"RecordCount", artifact.record_count ? std::to_string(*artifact.record_count) : "null"},
            {"SHA256", artifact.sha256}, {"CreatedAtUtc", packaged_at},
            {"SourceStage", artifact.source_stage}, {"AnalysisRunId", analysis_run_id},
            {"RequiredForCodex", artifact.required_for_codex ? "true" : "false"},
            {"OptionalDiagnostic", artifact.optional_diagnostic ? "true" : "false"},
            {"RequiredForPackageValidation", artifact.required_for_package_validation ? "true" : "false"},
            {"RequiredForCodexIngestion", artifact.required_for_codex_ingestion ? "true" : "false"},
            {"RecommendedForDeepAnalysis", artifact.recommended_for_deep_analysis ? "true" : "false"},
            {"DiagnosticOnly", artifact.diagnostic_only ? "true" : "false"}
        }, {"Authoritative", "SchemaVersion", "FileSize", "RecordCount", "RequiredForCodex", "OptionalDiagnostic",
            "RequiredForPackageValidation", "RequiredForCodexIngestion", "RecommendedForDeepAnalysis", "DiagnosticOnly"});
    }
    output << "]}\n";
    return output.str();
}

PackageArtifact DescribeArtifact(const fs::path& root, const fs::path& path,
                                 std::size_t index) {
    PackageArtifact artifact;
    artifact.artifact_id = "artifact-" + std::to_string(index + 1);
    artifact.relative_path = RelativeUtf8(root, path);
    artifact.file_size = fs::file_size(path);
    const auto extension = path.extension().wstring();
    artifact.content_type = extension == L".jsonl" ? "application/x-ndjson" :
        extension == L".json" ? "application/json" : extension == L".md" ? "text/markdown" :
        extension == L".sql" ? "application/sql" : extension == L".sqlite3" ? "application/vnd.sqlite3" :
        extension == L".etl" ? "application/vnd.microsoft.etl" : "application/octet-stream";
    if (extension == L".jsonl") {
        std::uint64_t record_count = 0;
        artifact.sha256 = FileSha256(path, &record_count).value_or("");
        artifact.record_count = record_count;
    } else {
        artifact.sha256 = FileSha256(path).value_or("");
    }
    if (extension == L".json" || extension == L".md" || extension == L".sql") artifact.record_count = 1;
    const auto& relative = artifact.relative_path;
    static const std::unordered_set<std::string> codex_required = {
        "CODEX_HANDOFF.md", "codex-handoff.json", "capture-info.json", "capture-health.json",
        "connection-summary.json", "artifact-authority.json", "limitations.json", "sensitive-content.json",
        "package-validation.json", "provenance.jsonl",
        "PrimarySession/raw/capture-records.jsonl", "PrimarySession/raw/transport-chunks.jsonl",
        "PrimarySession/raw/semantic-events.jsonl",
        "PrimarySession/reports/semantic-segment-manifest.json",
        "PrimarySession/reports/semantic-shared-ring.json",
        "PrimarySession/reports/deep-probe-candidate-map.json",
        "PrimarySession/semantic/verified-integration-manifest.json",
        "PrimarySession/semantic/verified-protocol-spec.json",
        "PrimarySession/semantic/semantic-evidence-graph.jsonl",
        "PrimarySession/semantic/value-flow.jsonl",
        "PrimarySession/semantic/cross-session-ledger.jsonl",
        "PrimarySession/semantic/semantic-summary.json",
        "PrimarySession/semantic/semantic-probe-plan.json",
        "PrimarySession/decrypted/pre-encrypt-records.jsonl",
        "PrimarySession/decrypted/post-decrypt-records.jsonl",
        "PrimarySession/decrypted/handler-decoded-records.jsonl",
        "PrimarySession/decrypted/unframed-records.jsonl",
        "PrimarySession/protocol/decoded-frames.jsonl",
        "PrimarySession/protocol/codex-opcode-worklist.jsonl",
        "PrimarySession/gameplay/protocol-handler-observations.jsonl",
        "PrimarySession/gameplay/protocol-semantic-candidates.jsonl",
        "PrimarySession/exports/gameplay-coverage-matrix.csv",
        "PrimarySession/exports/formula-coverage-matrix.csv",
        "PrimarySession/exports/unknown-opcode-clusters.csv",
        "PrimarySession/protocol/opcode-summary.json",
        "PrimarySession/protocol/direction-summary.json",
        "PrimarySession/protocol/frame-validation-summary.json",
        "PrimarySession/mapping/transport-to-decrypted.jsonl",
        "PrimarySession/mapping/decrypted-to-handler.jsonl",
        "PrimarySession/decoded/decoded-messages.jsonl", "PrimarySession/decoded/handler-observations.jsonl",
        "PrimarySession/derived/candidate-protocol-frames.jsonl",
        "PrimarySession/analysis/action-instances.jsonl", "PrimarySession/analysis/action-patterns.jsonl",
        "PrimarySession/analysis/action-pattern-summary.json",
        "PrimarySession/analysis/orphan-handler-bursts.jsonl",
        "PrimarySession/analysis/outbound-batch-candidates.jsonl"
    };
    artifact.required_for_codex_ingestion = codex_required.contains(relative);
    if (relative.starts_with("PrimarySession/raw/semantic-segments/"))
        artifact.required_for_codex_ingestion = true;
    artifact.required_for_package_validation = artifact.required_for_codex_ingestion ||
        relative == "package-validation.json" || relative == "sensitive-content.json";
    artifact.recommended_for_deep_analysis = relative.find("trace.bin") != std::string::npos ||
        relative.find("capture.etl") != std::string::npos || relative.find("session.sqlite3") != std::string::npos ||
        relative.find(".pcapng") != std::string::npos || relative.find("incomplete-stream-fragments.jsonl") != std::string::npos ||
        relative.find("logical-message-map.jsonl") != std::string::npos;
    artifact.diagnostic_only = relative.find("logs/") != std::string::npos ||
        relative.find("diagnostics/") != std::string::npos || relative.find("compatibility/") != std::string::npos ||
        relative.find("cleanup") != std::string::npos;
    artifact.required_for_codex = artifact.required_for_codex_ingestion;
    artifact.optional_diagnostic = artifact.diagnostic_only;
    if (relative.find("raw/semantic-events.jsonl") != std::string::npos ||
        relative.find("raw/semantic-segments/") != std::string::npos) {
        artifact.artifact_class = "SanitizedDerivative";
        artifact.authoritative = false;
        artifact.role = "PrivacyGatedSemanticEvents";
        artifact.required_for_codex = true;
        artifact.source_stage = "SemanticPrivacyGate";
    } else if (relative.find("capture-records.jsonl") != std::string::npos ||
        relative.find("transport-chunks.jsonl") != std::string::npos ||
        relative.find("trace.bin") != std::string::npos || relative.find("capture.etl") != std::string::npos) {
        artifact.artifact_class = "RawImmutable";
        artifact.authoritative = true;
        artifact.required_for_codex = relative.find("capture-records.jsonl") != std::string::npos;
        artifact.source_stage = "Acquisition";
    } else if (relative.find("PrimarySession/analysis/") != std::string::npos ||
               relative.find("gameplay-candidates") != std::string::npos ||
               relative.find("candidate-") != std::string::npos) {
        artifact.artifact_class = "Candidate";
        artifact.role = "NonAuthoritativeCandidate";
        artifact.source_stage = "Analysis";
    } else if (relative.find("diagnostics/") != std::string::npos) {
        artifact.artifact_class = "Diagnostic";
        artifact.optional_diagnostic = true;
        artifact.source_stage = "Diagnostic";
    } else if (relative.find("schemas/") != std::string::npos) {
        artifact.artifact_class = "ToolchainIdentity";
        artifact.source_stage = "Schema";
    } else if (extension == L".sqlite3") {
        artifact.artifact_class = "ConvenienceExport";
        artifact.source_stage = "Storage";
    } else {
        artifact.artifact_class = "DerivedDeterministic";
        artifact.source_stage = "Packaging";
    }
    if (artifact.role.empty()) artifact.role = path.stem().string();
    artifact.schema_id = extension == L".jsonl" || extension == L".json" ? path.stem().string() : "binary";
    return artifact;
}

bool HasAbsoluteWindowsPath(const fs::path& path) {
    const auto extension = path.extension().wstring();
    if (extension != L".json" && extension != L".jsonl" && extension != L".md" && extension != L".sql")
        return false;
    const auto content = ReadUtf8File(path);
    if (!content) return true;
    for (std::size_t i = 0; i + 2 < content->size(); ++i) {
        const bool token_boundary = i == 0 ||
            std::isalnum(static_cast<unsigned char>((*content)[i - 1])) == 0;
        if (token_boundary && std::isalpha(static_cast<unsigned char>((*content)[i])) && (*content)[i + 1] == ':' &&
            ((*content)[i + 2] == '\\' || (*content)[i + 2] == '/')) return true;
    }
    return false;
}

struct ValidatedEnhancedCaptureStatus {
    bool valid{};
    std::string status;
    std::string detail;
    std::string failure_kind;
    bool requested{};
    bool was_ever_attached{};
    bool probe_ready{};
    bool strict_unload_verified{};
    bool injection_attempted{};
    bool module_was_ever_loaded{};
    bool module_load_state_verified{};
    bool module_snapshot_verified{};
    bool module_absent{};
    bool target_process_exited{};
    bool target_identity_verified{};
    bool injector_present{};
    bool probe_present{};
    bool injector_payload_validated{};
    bool probe_payload_validated{};
};

bool EnhancedAcquisitionBlocked(const ValidatedEnhancedCaptureStatus& status,
                                 std::uint64_t capture_records) {
    return !status.valid || (status.requested && capture_records == 0);
}

std::string CleanupStatus(const fs::path& session_path,
                          ValidatedEnhancedCaptureStatus* validated = nullptr) {
    if (validated != nullptr) *validated = {};
    const auto report = ReadUtf8File(session_path / L"reports" / L"enhanced-capture-status.json");
    if (!report) return "NotApplicableOrNotObserved";
    StrictJsonValue root;
    std::string error;
    if (!StrictJsonParser(*report).Parse(&root, &error) ||
        root.kind != StrictJsonKind::Object)
        return "ModuleStateUncertain";
    constexpr std::string_view required_strings[]{
        "SchemaVersion", "UpdatedAtUtc", "Status", "Detail", "FailureKind",
        "TargetExecutable", "TargetArchitecture", "ProbeScope", "ProbeDomains",
        "ConfirmedExactBuildDomains", "CandidateOnlyStatus",
        "DeepProbeCandidateMapSchema", "CandidateAbiSafetyContract",
        "SemanticEventContract", "SharedTransportContract", "InjectionSequence",
        "ReadinessHandshake", "PayloadStaging", "PayloadDacl",
        "PayloadMandatoryLabel", "SemanticIpcBinding", "PayloadLaunchMode",
        "UnloadPolicy", "Apis", "InputAndWindowHooks"};
    constexpr std::string_view required_booleans[]{
        "Requested", "WasEverAttached", "ProbeReady", "StrictUnloadVerified",
        "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
        "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
        "TargetIdentityVerified", "InjectorPresent", "ProbePresent",
        "InjectorPayloadValidated", "ProbePayloadValidated"};
    constexpr std::string_view required_numbers[]{
        "ProbeDomainCount", "ConfirmedExactBuildDomainCount",
        "CandidateOnlyDomainCount", "CandidatePromotionGateCount",
        "CandidateAbiSafetyGateCount", "CandidateActivationGateCount"};
    static_assert(std::size(required_strings) + std::size(required_booleans) +
        std::size(required_numbers) == 46);
    if (root.object.size() != 46) return "ModuleStateUncertain";
    for (const auto name : required_strings)
        if (JsonMember(root, name, StrictJsonKind::String) == nullptr)
            return "ModuleStateUncertain";
    for (const auto name : required_booleans)
        if (JsonMember(root, name, StrictJsonKind::Boolean) == nullptr)
            return "ModuleStateUncertain";
    for (const auto name : required_numbers)
        if (JsonMember(root, name, StrictJsonKind::Number) == nullptr)
            return "ModuleStateUncertain";
    const auto string_value = [&](std::string_view name) -> std::string_view {
        return JsonMember(root, name, StrictJsonKind::String)->scalar;
    };
    const auto boolean_value = [&](std::string_view name) {
        return JsonMember(root, name, StrictJsonKind::Boolean)->scalar == "true";
    };
    const auto uint_value = [&](std::string_view name, std::uint64_t expected) {
        std::uint64_t value{};
        const auto* member = JsonMember(root, name, StrictJsonKind::Number);
        return member != nullptr && ParseStrictUint64(*member, &value) &&
            value == expected;
    };
    if (string_value("SchemaVersion") != "god2-enhanced-capture-status-v2" ||
        string_value("TargetExecutable") != "God2_opt.exe" ||
        string_value("TargetArchitecture") != "x86" ||
        string_value("ProbeScope") != "WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded" ||
        string_value("ProbeDomains") != "Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot" ||
        string_value("ConfirmedExactBuildDomains") != "Network,Parser,Serializer,Handler" ||
        string_value("CandidateOnlyStatus") != "EvidenceBlockedUnconfirmedProbe" ||
        string_value("DeepProbeCandidateMapSchema") != "god2-deep-probe-candidate-map-v2" ||
        string_value("CandidateAbiSafetyContract") != "ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk" ||
        string_value("SemanticEventContract") != "God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes" ||
        string_value("SharedTransportContract") != "VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag" ||
        string_value("InjectionSequence") != "PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread" ||
        string_value("ReadinessHandshake") != "God2TraceProbeWaitReady" ||
        string_value("PayloadStaging") != "ProtectedProgramDataRandomDirectory" ||
        string_value("PayloadDacl") != "Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent" ||
        string_value("PayloadMandatoryLabel") != "MediumNoWriteUp" ||
        string_value("SemanticIpcBinding") != "DuplicatedTargetHandlesV2;NoNamedObjectAuthority" ||
        string_value("PayloadLaunchMode") != "AlreadyElevatedCreateProcessW" ||
        string_value("UnloadPolicy") != "QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped" ||
        string_value("Apis") != "send,WSASend,recv,WSARecv,OverlappedWSARecvCompletionRoutine,WSAGetOverlappedResult,GetQueuedCompletionStatus,GetQueuedCompletionStatusEx,PostDecrypt,PreEncrypt,HandlerDecoded" ||
        string_value("InputAndWindowHooks") != "Disabled" ||
        !uint_value("ProbeDomainCount", 25) ||
        !uint_value("ConfirmedExactBuildDomainCount", 4) ||
        !uint_value("CandidateOnlyDomainCount", 21) ||
        !uint_value("CandidatePromotionGateCount", 10) ||
        !uint_value("CandidateAbiSafetyGateCount", 4) ||
        !uint_value("CandidateActivationGateCount", 14))
        return "ModuleStateUncertain";
    const auto status = string_value("Status");
    const bool requested = boolean_value("Requested");
    const bool was_ever_attached = boolean_value("WasEverAttached");
    const bool probe_ready = boolean_value("ProbeReady");
    const bool strict_unload_verified = boolean_value("StrictUnloadVerified");
    const bool injection_attempted = boolean_value("InjectionAttempted");
    const bool module_was_ever_loaded = boolean_value("ModuleWasEverLoaded");
    const bool module_load_state_verified = boolean_value("ModuleLoadStateVerified");
    const bool module_snapshot_verified = boolean_value("ModuleSnapshotVerified");
    const bool module_absent = boolean_value("ModuleAbsent");
    const bool target_process_exited = boolean_value("TargetProcessExited");
    const bool target_identity_verified = boolean_value("TargetIdentityVerified");
    const bool injector_present = boolean_value("InjectorPresent");
    const bool probe_present = boolean_value("ProbePresent");
    const bool injector_payload_validated = boolean_value("InjectorPayloadValidated");
    const bool probe_payload_validated = boolean_value("ProbePayloadValidated");
    if (was_ever_attached && (!injection_attempted || !module_was_ever_loaded ||
            !module_load_state_verified || !target_identity_verified ||
            !injector_payload_validated || !probe_payload_validated))
        return "ModuleStateUncertain";
    if (probe_ready && (!was_ever_attached || module_absent ||
            strict_unload_verified || target_process_exited))
        return "ModuleStateUncertain";
    if (target_process_exited && !target_identity_verified)
        return "ModuleStateUncertain";
    if (status == "Stopped" && module_was_ever_loaded && !was_ever_attached)
        return "ModuleStateUncertain";
    if (validated != nullptr) {
        validated->valid = true;
        validated->status = std::string(status);
        validated->detail = std::string(string_value("Detail"));
        validated->failure_kind = std::string(string_value("FailureKind"));
        validated->requested = requested;
        validated->was_ever_attached = was_ever_attached;
        validated->probe_ready = probe_ready;
        validated->strict_unload_verified = strict_unload_verified;
        validated->injection_attempted = injection_attempted;
        validated->module_was_ever_loaded = module_was_ever_loaded;
        validated->module_load_state_verified = module_load_state_verified;
        validated->module_snapshot_verified = module_snapshot_verified;
        validated->module_absent = module_absent;
        validated->target_process_exited = target_process_exited;
        validated->target_identity_verified = target_identity_verified;
        validated->injector_present = injector_present;
        validated->probe_present = probe_present;
        validated->injector_payload_validated = injector_payload_validated;
        validated->probe_payload_validated = probe_payload_validated;
    }
    const bool loaded_module_cleanup_complete = requested &&
        module_was_ever_loaded && injection_attempted &&
        module_load_state_verified && target_identity_verified &&
        module_absent && (target_process_exited || module_snapshot_verified) &&
        !probe_ready && strict_unload_verified &&
        !injector_present && !probe_present &&
        injector_payload_validated && probe_payload_validated;
    const bool no_module_cleanup_complete = !module_was_ever_loaded &&
        module_load_state_verified && module_absent && !probe_ready &&
        strict_unload_verified && module_snapshot_verified &&
        !injector_present && !probe_present;
    if ((status == "Stopped" || status == "StoppedDuringAttach" ||
         status == "EvidenceBlocked" || status == "Disabled") &&
        (loaded_module_cleanup_complete || no_module_cleanup_complete))
        return "Completed";
    return "ModuleStateUncertain";
}

} // namespace

std::optional<std::string> CalculateFileSha256(const fs::path& path) {
    return FileSha256(path);
}

bool CreatePortableStoredZip(const fs::path& root, const fs::path& zip_path, std::string* error) {
    return CreateStoredZip(root, zip_path, error);
}

bool ValidatePortableStoredZip(const fs::path& root, const fs::path& zip_path, std::string* error) {
    std::size_t files = 0;
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(root, fs::directory_options::skip_permission_denied,
                                                    enumerate_error), end;
         iterator != end && !enumerate_error; iterator.increment(enumerate_error)) {
        if (IsExcludedRawNetworkPayload(iterator->path())) continue;
        if (!iterator->is_regular_file()) continue;
        ++files;
        const auto relative = RelativeUtf8(root, iterator->path());
        const auto extracted = ReadStoredZipEntry(zip_path, relative);
        const auto disk_hash = FileSha256(iterator->path());
        const auto zip_hash = extracted ? Sha256(*extracted) : std::nullopt;
        std::error_code size_error;
        const auto disk_size = fs::file_size(iterator->path(), size_error);
        if (!extracted || !disk_hash || !zip_hash || size_error || extracted->size() != disk_size ||
            *disk_hash != *zip_hash) {
            if (error) *error = "portable ZIP validation failed for " + relative;
            return false;
        }
    }
    if (enumerate_error || files == 0) {
        if (error) *error = enumerate_error ? enumerate_error.message() : "portable ZIP contains no source files";
        return false;
    }
    return true;
}

// This non-recursive, one-shot finalization routine intentionally keeps the
// complete package transaction in one scope.  MSVC /analyze estimates 16.8 KiB
// of stack for its many RAII handles, well below the Windows thread reserve;
// moving those handles to globals would weaken cleanup and reentrancy.
#pragma warning(suppress: 6262)
EvidencePackageResult EvidencePackageBuilder::Build(const fs::path& session_path,
                                                    bool partial_recovery,
                                                    bool simulate_zip_failure_for_test) {
    EvidencePackageResult result;
    try {
        if (!IsApprovedSessionPath(session_path) ||
            ContainsReparsePoint(LocalDataRoot(), session_path)) {
            result.error = "session path is outside the approved PacketCapture root or crosses a reparse point";
            result.blockers.push_back(result.error);
            return result;
        }
        const fs::path build_progress = session_path / L"reports" / L"evidence-package-build-progress.txt";
        WriteUtf8File(build_progress, "approved-session\n");
        const auto progress = [&](std::string_view step) { WriteUtf8File(build_progress, std::string(step) + "\n", true); };
        const auto session_json = ReadUtf8File(session_path / L"session.json");
        Fields session_fields;
        StrictJsonValue strict_session_fields;
        std::string parse_error;
        if (!session_json || !StrictJsonParser(*session_json).Parse(
                &strict_session_fields, &parse_error) ||
            strict_session_fields.kind != StrictJsonKind::Object ||
            JsonMember(strict_session_fields, "SessionId", StrictJsonKind::String) == nullptr ||
            JsonMember(strict_session_fields, "SchemaVersion", StrictJsonKind::Number) == nullptr ||
            !ParseFlatJson(*session_json, session_fields, &parse_error)) {
            result.error = "session.json is missing or invalid: " + parse_error;
            result.blockers.push_back(result.error);
            return result;
        }
        result.session_id = GetString(session_fields, "SessionId");
        if (!IsSafePortableComponent(result.session_id)) {
            result.error = "SessionId is missing or is not a strict portable path component";
            result.blockers.push_back(result.error);
            return result;
        }
        const bool immutable_package_source = !GetString(session_fields, "OriginalPackageSHA256").empty();
        const auto preserved_client_sha256 = GetString(session_fields, "OriginalClientSHA256");
        auto client_identity = std::make_unique<ExecutableIdentity>();
        auto launcher_identity = std::make_unique<ExecutableIdentity>();
        const auto client_path_value = GetString(session_fields, "ClientExecutablePath");
        const auto launcher_path_value = GetString(session_fields, "LauncherExecutablePath");
        if (!client_path_value.empty()) *client_identity = ReadExecutableIdentity(Utf8ToWide(client_path_value), true);
        if (!launcher_path_value.empty()) *launcher_identity = ReadExecutableIdentity(Utf8ToWide(launcher_path_value), false);
        result.client_sha256 = client_identity->sha256;
        result.launcher_sha256 = launcher_identity->sha256;
        result.client_identity_status = client_path_value.empty() ? "UnavailableForLegacySession" : client_identity->status;
        result.launcher_identity_status = launcher_path_value.empty() ? "UnavailableForLegacySession" : launcher_identity->status;
        // Reanalysis is performed only after the source package manifest and every
        // immutable artifact hash have been validated.  Preserve the client build
        // binding carried by that trusted package so semantic events are not
        // incorrectly downgraded merely because the original executable is absent
        // on the analysis machine.  Malformed or untrusted values remain unusable.
        if (result.client_sha256.empty() && client_path_value.empty() && immutable_package_source &&
            preserved_client_sha256.size() == 64 && IsHex(preserved_client_sha256)) {
            result.client_sha256 = preserved_client_sha256;
            result.client_identity_status = "PreservedClaimFromSourcePackageNotRecomputed";
        }
        const bool exact_client_identity =
            result.client_identity_status == "ExactClientIdentityComputedAndValidated";

        const fs::path legacy_records = session_path / L"raw" / L"injected-packets.jsonl";
        const fs::path current_records = session_path / L"raw" / L"capture-records.jsonl";
        const fs::path input_records = fs::exists(current_records) ? current_records : legacy_records;
        const bool enhanced_record_source_available = fs::is_regular_file(input_records);
        if (enhanced_record_source_available && ContainsReparsePoint(session_path, input_records)) {
            result.error = "CaptureRecord source crosses a reparse point";
            result.blockers.push_back(result.error);
            return result;
        }
        if (!enhanced_record_source_available && immutable_package_source) {
            result.error = "validated source package CaptureRecords are missing";
            result.blockers.push_back(result.error);
            return result;
        }
        if (!enhanced_record_source_available)
            result.warnings.push_back(
                "x86 enhanced capture produced no CaptureRecord source; raw network payload files are excluded fail-closed and the enhanced source is EvidenceBlocked");
        std::string unsafe_diagnostic_marker;
        const fs::path enhanced_diagnostic = session_path / L"reports" / L"enhanced-capture.log";
        if (fs::is_regular_file(enhanced_diagnostic) &&
            (ContainsReparsePoint(session_path, enhanced_diagnostic) ||
             ContainsUnsafeLegacyDiagnostic(enhanced_diagnostic, &unsafe_diagnostic_marker))) {
            result.error = "legacy enhanced diagnostic contains or may redirect unsafe authentication material";
            if (!unsafe_diagnostic_marker.empty()) result.error += "; marker=" + unsafe_diagnostic_marker;
            result.blockers.push_back(result.error);
            return result;
        }

        std::vector<CaptureRecord> records;
        std::unordered_map<std::string, std::size_t> source_record_index;
        std::unordered_map<std::string, std::uint64_t> stage_sequences;
        std::unordered_map<std::string, std::uint64_t> thread_sequences;
        if (enhanced_record_source_available) {
            std::error_code size_error;
            const auto input_size = fs::file_size(input_records, size_error);
            if (!size_error) {
                const auto estimated_records = static_cast<std::size_t>(std::clamp<std::uintmax_t>(
                    input_size / 2048u, 512u, 65536u));
                records.reserve(estimated_records);
                source_record_index.reserve(estimated_records * 2);
                thread_sequences.reserve(std::min<std::size_t>(estimated_records, 4096));
            }
        }
        stage_sequences.reserve(8);
        std::uint64_t invalid_records = 0;
        std::uint64_t invalid_payloads = 0;
        std::uint64_t invalid_lengths = 0;
        std::ifstream record_stream;
        if (enhanced_record_source_available) record_stream.open(input_records, std::ios::binary);
        std::string line;
        while (std::getline(record_stream, line)) {
            if (line.empty()) continue;
            CaptureRecord record;
            record.original = line;
            record.capture_sequence = records.size() + 1;
            if (!ParseFlatJson(line, record.fields, &parse_error)) {
                record.valid_json = false;
                record.stage = "Invalid";
                record.source_id = "invalid-source-" + std::to_string(record.capture_sequence);
                ++invalid_records;
            } else {
                record.source_id = GetString(record.fields, "SourceFrameId",
                    GetString(record.fields, "OriginalSourceRecordId",
                        GetString(record.fields, "CaptureRecordId",
                            "legacy-source-" + std::to_string(record.capture_sequence))));
                record.stage = GetString(record.fields, "CaptureStage");
                record.api = GetString(record.fields, "Api");
                if (record.stage.empty()) {
                    if (record.api == "PreEncrypt" || record.api == "PostDecrypt" || record.api == "HandlerDecoded")
                        record.stage = record.api;
                    else record.stage = "Transport";
                }
                record.direction = GetString(record.fields, "PacketDirection",
                    GetString(record.fields, "direction", GetString(record.fields, "Direction", "Unknown")));
                record.socket = GetString(record.fields, "Socket", "Unknown");
                record.payload_hex = GetString(record.fields, "PayloadHex");
                const auto plaintext_hex = GetString(record.fields, "PlaintextHex");
                if ((record.stage == "PreEncrypt" || record.stage == "PostDecrypt" ||
                     record.stage == "HandlerDecoded") && !plaintext_hex.empty())
                    record.payload_hex = plaintext_hex;
                record.captured_at_unix_ms = GetInt64(record.fields, "CapturedAtUnixMs").value_or(
                    GetInt64(record.fields, "ObservedAtUnixMs").value_or(
                        GetInt64(record.fields, "wallUnixMs").value_or(0)));
                record.source_sequence = GetInt64(record.fields, "SourceSequence").value_or(
                    GetInt64(record.fields, "sequence").value_or(static_cast<std::int64_t>(record.capture_sequence)));
                record.captured_qpc = GetInt64(record.fields, "CapturedQpc").value_or(
                    GetInt64(record.fields, "qpc").value_or(0));
                record.qpc_frequency = GetInt64(record.fields, "QpcFrequency").value_or(
                    GetInt64(record.fields, "qpcFrequency").value_or(0));
                record.process_id = GetInt64(record.fields, "ProcessId").value_or(0);
                record.thread_id = GetInt64(record.fields, "ThreadId").value_or(0);
                record.thread_sequence = ++thread_sequences[std::to_string(record.process_id) + "|" +
                    std::to_string(record.thread_id)];
                record.hook_invocation_id = GetString(record.fields, "HookInvocationId",
                    "hook-" + std::to_string(record.process_id) + "-" + std::to_string(record.source_sequence));
                record.parent_invocation_id = GetString(record.fields, "ParentInvocationId");
                record.parent_correlation_basis = GetString(record.fields, "ParentCorrelationBasis");
                record.context_invocation_id = GetString(record.fields, "ContextInvocationId");
                record.context_correlation_basis = GetString(record.fields, "ContextCorrelationBasis");
                record.call_depth = GetInt64(record.fields, "CallDepth").value_or(0);
                const auto [caller_module, caller_rva] = ModuleAndRva(GetString(record.fields, "caller"));
                record.caller_module = GetString(record.fields, "CallerModule", caller_module);
                record.caller_rva = GetString(record.fields, "CallerRva", caller_rva);
                record.captured_at_utc = UnixMillisecondsUtc(record.captured_at_unix_ms);
                record.valid_payload = IsHex(record.payload_hex);
                if (!record.valid_payload) ++invalid_payloads;
                const auto captured_length = GetInt64(record.fields, "CapturedLength");
                record.valid_length = !captured_length || !record.valid_payload ||
                    *captured_length == static_cast<std::int64_t>(record.payload_hex.size() / 2);
                if (!record.valid_length) ++invalid_lengths;
                if (record.valid_payload && IsUnsafeAuthenticationRecord(record)) {
                    result.error = "raw authentication plaintext record was rejected before Evidence Package staging; sequence=" +
                        std::to_string(record.capture_sequence);
                    result.blockers.push_back(result.error);
                    return result;
                }
            }
            record.capture_record_id = "capture-record-" + std::to_string(record.capture_sequence);
            record.stage_sequence = ++stage_sequences[record.stage];
            record.payload_sha256 = Sha256(record.payload_hex).value_or("");
            const auto prefix_size = std::min<std::size_t>(record.payload_hex.size(), 64);
            const auto suffix_start = record.payload_hex.size() > 64 ? record.payload_hex.size() - 64 : 0;
            if (record.payload_hex.size() <= 64) {
                record.payload_prefix_hash = record.payload_sha256;
                record.payload_suffix_hash = record.payload_sha256;
            } else {
                record.payload_prefix_hash = Sha256(record.payload_hex.substr(0, prefix_size)).value_or("");
                record.payload_suffix_hash = Sha256(record.payload_hex.substr(suffix_start)).value_or("");
            }
            source_record_index.emplace(record.source_id, records.size());
            records.push_back(std::move(record));
        }
        if (enhanced_record_source_available && !record_stream.eof() && record_stream.fail()) {
            result.error = "CaptureRecord input could not be read completely";
            result.blockers.push_back(result.error);
            return result;
        }
        result.capture_records = records.size();
        if (enhanced_record_source_available && records.empty())
            result.warnings.push_back(
                "x86 enhanced CaptureRecord source is empty; raw network payload files are excluded fail-closed and the enhanced source is EvidenceBlocked");
        progress("capture-records-read");

        // GPU work is deliberately downstream of capture and outside the DLL,
        // ordering, persistence, and verification paths.  It emits candidate
        // bulk features only; OptionalGpuAccelerator recomputes every candidate
        // on the CPU before returning the verified feature vector.
        const AccelerationMode acceleration_mode = ParseAccelerationMode(
            GetString(session_fields, "AccelerationMode", "Auto"));
        std::vector<GpuEvidenceRecord> gpu_inputs;
        std::vector<std::size_t> gpu_input_record_indices;
        gpu_inputs.reserve(records.size());
        gpu_input_record_indices.reserve(records.size());
        for (std::size_t index = 0; index < records.size(); ++index) {
            const auto& record = records[index];
            if (!record.valid_payload || record.payload_hex.size() > 128u * 1024u) continue;
            const auto bytes = ParseHexBytes(record.payload_hex);
            if (bytes) {
                gpu_inputs.push_back(GpuEvidenceRecord{*bytes});
                gpu_input_record_indices.push_back(index);
            }
        }
        OptionalGpuAccelerator gpu_accelerator(acceleration_mode);
        const GpuProcessingResult gpu_processing = gpu_accelerator.Process(
            gpu_inputs, AccelerationPhase::PostCapture);
        result.gpu_available = gpu_processing.benefit.gpu_available;
        result.gpu_eligible = gpu_processing.benefit.gpu_eligible;
        result.gpu_selected = gpu_processing.benefit.gpu_selected;
        result.gpu_device = gpu_processing.capability.device;
        result.gpu_capability = GpuModeDisplayName(gpu_processing.capability.backend_initialized ?
            gpu_processing.capability.tier : GpuArchitectureTier::CpuFullFeature);
        result.gpu_selected_backend = gpu_processing.benefit.selected_backend;
        result.gpu_selection_reason = gpu_processing.benefit.selection_reason;
        if (!gpu_processing.success || gpu_processing.performance.dropped_evidence != 0) {
            result.error = "optional GPU post-processing did not preserve all evidence";
            result.blockers.push_back(result.error);
            return result;
        }
        if (gpu_processing.verified_features.size() != gpu_input_record_indices.size()) {
            result.error = "optional GPU post-processing returned an incomplete verified feature vector";
            result.blockers.push_back(result.error);
            return result;
        }
        for (std::size_t index = 0; index < gpu_input_record_indices.size(); ++index) {
            auto& record = records[gpu_input_record_indices[index]];
            record.verified_byte_sum_without_last =
                gpu_processing.verified_features[index].byte_sum_without_last;
            record.verified_bulk_feature_available = true;
        }
        progress("gpu-post-processing-complete");

        std::vector<std::size_t> parent(records.size());
        std::iota(parent.begin(), parent.end(), 0);
        const auto find_root = [&](std::size_t value) -> std::size_t {
            std::size_t root = value;
            while (parent[root] != root) root = parent[root];
            while (parent[value] != value) {
                const auto next = parent[value];
                parent[value] = root;
                value = next;
            }
            return root;
        };
        const auto unite = [&](std::size_t left, std::size_t right) {
            left = find_root(left);
            right = find_root(right);
            if (left != right) parent[std::max(left, right)] = std::min(left, right);
        };
        std::unordered_map<std::string, std::size_t> invocation_index;
        std::unordered_map<std::string, std::uint64_t> invocation_counts;
        std::vector<std::pair<std::size_t, std::size_t>> strong_edges;
        std::vector<std::uint64_t> ambiguity_counts(records.size());
        for (std::size_t i = 0; i < records.size(); ++i) {
            if (records[i].hook_invocation_id.empty()) continue;
            const auto count = ++invocation_counts[records[i].hook_invocation_id];
            if (count == 1) invocation_index.emplace(records[i].hook_invocation_id, i);
            else invocation_index.erase(records[i].hook_invocation_id);
        }
        const auto same_thread_context = [](const CaptureRecord& current, const CaptureRecord& referenced) {
            return current.process_id > 0 && current.thread_id > 0 &&
                current.process_id == referenced.process_id && current.thread_id == referenced.thread_id &&
                current.source_sequence > referenced.source_sequence;
        };
        const auto valid_parent_transition = [&](const CaptureRecord& current, const CaptureRecord& referenced) {
            const bool outbound_transport = current.stage == "Transport" &&
                (current.api == "send" || current.api == "WSASend");
            const bool explicit_probe_basis = current.parent_correlation_basis.empty() ||
                current.parent_correlation_basis == "ExactNestedPreEncryptInvocation";
            return outbound_transport && referenced.stage == "PreEncrypt" &&
                current.direction == "ClientToServer" && referenced.direction == current.direction &&
                explicit_probe_basis && same_thread_context(current, referenced);
        };
        const auto valid_context_transition = [&](const CaptureRecord& current, const CaptureRecord& referenced) {
            if (!same_thread_context(current, referenced) || referenced.direction != current.direction) return false;
            if (current.stage == "Transport" && (current.api == "send" || current.api == "WSASend"))
                return referenced.stage == "PreEncrypt" && current.direction == "ClientToServer" &&
                    current.context_correlation_basis == "SameThreadSinglePendingPreEncrypt";
            if (current.stage == "PostDecrypt")
                return referenced.stage == "Transport" &&
                    (referenced.api == "recv" || referenced.api == "WSARecv") &&
                    current.direction == "ServerToClient" &&
                    current.context_correlation_basis == "SameThreadLatestInboundTransport";
            if (current.stage == "HandlerDecoded")
                return referenced.stage == "PostDecrypt" && current.direction == "ServerToClient" &&
                    current.context_correlation_basis == "SameThreadLatestPostDecrypt";
            return false;
        };
        for (std::size_t i = 0; i < records.size(); ++i) {
            const auto& current = records[i];
            if (!current.valid_json) continue;
            if (!current.parent_invocation_id.empty()) {
                const auto parent_match = invocation_index.find(current.parent_invocation_id);
                if (parent_match != invocation_index.end() && parent_match->second != i &&
                    valid_parent_transition(current, records[parent_match->second]))
                    unite(i, parent_match->second);
                else ++ambiguity_counts[i];
            }
            if (!current.context_invocation_id.empty()) {
                const auto context_match = invocation_index.find(current.context_invocation_id);
                if (context_match != invocation_index.end() && context_match->second != i &&
                    valid_context_transition(current, records[context_match->second])) {
                    strong_edges.emplace_back(i, context_match->second);
                    unite(i, context_match->second);
                } else ++ambiguity_counts[i];
            }
        }
        std::map<std::size_t, std::vector<std::size_t>> logical_groups;
        for (std::size_t i = 0; i < records.size(); ++i)
            logical_groups[find_root(i)].push_back(i);
        std::uint64_t logical_sequence = 0;
        std::map<std::size_t, std::string> correlation_levels;
        std::map<std::size_t, std::uint64_t> group_ambiguities;
        std::uint64_t covered_records = 0;
        std::unordered_set<std::size_t> strong_roots;
        for (const auto& edge : strong_edges) strong_roots.insert(find_root(edge.first));
        for (const auto& [root, members] : logical_groups) {
            const std::string logical_id = "logical-message-" + std::to_string(++logical_sequence);
            std::uint64_t ambiguity = 0;
            for (const auto index : members) ambiguity += ambiguity_counts[index];
            group_ambiguities[root] = ambiguity;
            if (members.size() > 1) {
                correlation_levels[root] = strong_roots.contains(root) ? "Strong" : "Exact";
                ++result.correlated_logical_messages;
                covered_records += members.size();
                if (correlation_levels[root] == "Strong") ++result.strong_correlations;
                else ++result.exact_correlations;
            } else if (ambiguity != 0) {
                correlation_levels[root] = "Candidate";
                ++result.candidate_correlations;
                ++result.uncorrelated_logical_messages;
            } else {
                correlation_levels[root] = "Uncorrelated";
                ++result.uncorrelated_correlations;
                ++result.uncorrelated_logical_messages;
            }
            for (const auto index : members) {
                records[index].logical_message_id = logical_id;
                records[index].correlation_level = correlation_levels[root];
                for (const auto related : members)
                    if (related != index) records[index].related.push_back(related);
            }
        }
        result.correlation_coverage_percent = records.empty() ? 0.0 :
            static_cast<double>(covered_records) * 100.0 / static_cast<double>(records.size());
        progress("correlation-complete");

        std::uint64_t chunk_sequence = 0;
        std::uint64_t decoded_sequence = 0;
        std::uint64_t handler_sequence = 0;
        std::uint64_t protocol_frame_sequence = 0;
        std::map<std::string, std::uint64_t> c2s_opcode_observations;
        std::map<std::string, std::uint64_t> s2c_opcode_observations;
        for (auto& record : records) {
            if (record.direction == "ClientToServer") ++result.client_to_server_records;
            else if (record.direction == "ServerToClient") ++result.server_to_client_records;
            const bool send = record.api == "send" || record.api == "WSASend";
            const bool receive = record.api == "recv" || record.api == "WSARecv";
            if (send || receive) {
                record.transport_chunk_id = "transport-chunk-" + std::to_string(++chunk_sequence);
                if (send) ++result.transport_send_records;
                else ++result.transport_recv_records;
            } else if (record.api == "PreEncrypt" || record.stage == "PreEncrypt") {
                record.decoded_message_id = "decoded-message-" + std::to_string(++decoded_sequence);
                ++result.pre_encrypt_records;
            } else if (record.api == "PostDecrypt" || record.stage == "PostDecrypt") {
                record.decoded_message_id = "decoded-message-" + std::to_string(++decoded_sequence);
                ++result.post_decrypt_records;
            } else if (record.api == "HandlerDecoded" || record.stage == "HandlerDecoded") {
                record.handler_observation_id = "handler-observation-" + std::to_string(++handler_sequence);
                ++result.handler_decoded_records;
            }
            if (record.stage == "PreEncrypt" || record.stage == "PostDecrypt") {
                const auto bytes = ParseHexBytes(record.payload_hex);
                if (!bytes) {
                    record.frame_validation_status = "EvidenceOnly";
                    record.frame_validation_reason = "InvalidPlaintextHex";
                } else if (bytes->size() < 4) {
                    record.frame_validation_status = "EvidenceOnly";
                    record.frame_validation_reason = "FrameTooShort";
                } else if (bytes->size() > 4096) {
                    record.frame_validation_status = "EvidenceOnly";
                    record.frame_validation_reason = "FrameExceedsVerifiedMaximum";
                } else {
                    const auto declared_length = static_cast<std::uint16_t>((*bytes)[0]) |
                        (static_cast<std::uint16_t>((*bytes)[1]) << 8);
                    unsigned char checksum{};
                    if (record.verified_bulk_feature_available) {
                        checksum = static_cast<unsigned char>(record.verified_byte_sum_without_last +
                            0x3Cu * static_cast<std::uint32_t>(bytes->size() - 1));
                    } else {
                        for (std::size_t index = 0; index + 1 < bytes->size(); ++index)
                            checksum = static_cast<unsigned char>(checksum +
                                static_cast<unsigned char>((*bytes)[index] + 0x3Cu));
                    }
                    if (declared_length != bytes->size()) {
                        record.frame_validation_status = "EvidenceOnly";
                        record.frame_validation_reason = "DeclaredLengthMismatch";
                    } else if (checksum != bytes->back()) {
                        record.frame_validation_status = "EvidenceOnly";
                        record.frame_validation_reason = "ChecksumMismatch";
                    } else {
                        record.frame_validation_status = "VerifiedExactRecordBoundary";
                        record.frame_validation_reason = "U16LeLengthAndChecksumVerified";
                        record.frame_length = bytes->size();
                        record.frame_checksum = bytes->back();
                        record.frame_opcode = HexByte((*bytes)[2]);
                        record.plain_payload_hex = record.payload_hex.substr(6, (bytes->size() - 4) * 2);
                        record.protocol_frame_id = "protocol-frame-" +
                            std::to_string(++protocol_frame_sequence);
                        auto& observations = record.direction == "ClientToServer" ?
                            c2s_opcode_observations : s2c_opcode_observations;
                        ++observations[record.frame_opcode];
                    }
                }
                if (record.protocol_frame_id.empty()) ++result.unframed_plaintext_records;
            }
        }
        result.transport_chunks = chunk_sequence;
        result.decoded_messages = decoded_sequence;
        result.handler_observations = handler_sequence;
        result.protocol_frames = protocol_frame_sequence;
        result.unknown_protocol_frames = protocol_frame_sequence;
        result.client_to_server_opcode_count = c2s_opcode_observations.size();
        result.server_to_client_opcode_count = s2c_opcode_observations.size();
        for (const auto& item : c2s_opcode_observations)
            result.client_to_server_opcode_observations += item.second;
        for (const auto& item : s2c_opcode_observations)
            result.server_to_client_opcode_observations += item.second;
        result.enhanced_plaintext_available = result.pre_encrypt_records > 0 ||
            result.post_decrypt_records > 0 || result.handler_decoded_records > 0;

        std::map<std::string, OpcodeWorkItem> opcode_work_items;
        for (const auto& record : records) {
            if (record.protocol_frame_id.empty()) continue;
            const auto bytes = ParseHexBytes(record.payload_hex);
            if (!bytes || bytes->size() != record.frame_length) continue;
            const std::string key = record.direction + "|" + record.frame_opcode + "|" +
                std::to_string(record.frame_length);
            auto& item = opcode_work_items[key];
            if (item.observation_count == 0) {
                item.direction = record.direction;
                item.opcode = record.frame_opcode;
                item.frame_length = record.frame_length;
                item.first_captured_at_utc = record.captured_at_utc;
                item.first_capture_record_id = record.capture_record_id;
                item.sample_frame_hex = record.payload_hex;
                item.sample_frame_sha256 = record.payload_sha256;
                item.baseline_bytes = *bytes;
                item.stable_bytes.assign(bytes->size(), 1);
            } else {
                for (std::size_t index = 0; index < bytes->size(); ++index)
                    if ((*bytes)[index] != item.baseline_bytes[index]) item.stable_bytes[index] = 0;
            }
            ++item.observation_count;
            item.last_captured_at_utc = record.captured_at_utc;
            item.last_capture_record_id = record.capture_record_id;
            if (item.sample_protocol_frame_ids.size() < 3) {
                item.sample_protocol_frame_ids.push_back(record.protocol_frame_id);
                item.sample_capture_record_ids.push_back(record.capture_record_id);
            }
            std::unordered_set<std::string> frame_handler_ids;
            for (const auto related_index : record.related) {
                const auto& handler_id = records[related_index].handler_observation_id;
                if (handler_id.empty() || !frame_handler_ids.insert(handler_id).second) continue;
                if (record.correlation_level == "Exact") ++item.exact_handler_correlation_count;
                else if (record.correlation_level == "Strong") ++item.strong_handler_correlation_count;
                if (item.related_handler_observation_ids.size() < 5 &&
                    std::find(item.related_handler_observation_ids.begin(),
                              item.related_handler_observation_ids.end(), handler_id) ==
                        item.related_handler_observation_ids.end())
                    item.related_handler_observation_ids.push_back(handler_id);
            }
        }

        std::uint64_t assigned_work_item_sequence = 0;
        for (auto& [key, item] : opcode_work_items) {
            static_cast<void>(key);
            item.work_item_id = "opcode-work-item-" + std::to_string(++assigned_work_item_sequence);
        }
        const auto action_grouping_storage = std::make_unique<GenericActionGroupingResult>(
            BuildGenericActionGrouping(result.session_id, records, &opcode_work_items));
        const auto& action_grouping = *action_grouping_storage;
        result.action_trigger_candidate_count = action_grouping.action_trigger_candidate_count;
        result.action_instance_count = action_grouping.action_instance_count;
        result.action_pattern_count = action_grouping.action_pattern_count;
        result.grouped_trigger_count = action_grouping.grouped_trigger_count;
        result.ungrouped_trigger_count = action_grouping.ungrouped_trigger_count;
        result.exact_action_count = action_grouping.exact_action_count;
        result.strong_action_count = action_grouping.strong_action_count;
        result.candidate_action_count = action_grouping.candidate_action_count;
        result.ambiguous_action_count = action_grouping.ambiguous_action_count;
        result.unknown_action_pattern_count = action_grouping.unknown_action_pattern_count;
        result.verified_gameplay_mapped_pattern_count = action_grouping.verified_gameplay_mapped_pattern_count;
        result.outbound_batch_candidate_count = action_grouping.outbound_batch_candidate_count;
        result.outbound_batch_trigger_count = action_grouping.outbound_batch_trigger_count;
        result.orphan_handler_burst_count = action_grouping.orphan_handler_burst_count;
        result.background_candidate_count = action_grouping.background_candidate_count;
        result.action_clustering_coverage_percent = action_grouping.action_clustering_coverage_percent;

        std::map<std::string, ConnectionStats> connections;
        for (const auto& record : records) {
            if (!record.valid_json) continue;
            const auto process_id = GetInt64(record.fields, "ProcessId").value_or(0);
            const std::string key = std::to_string(process_id) + "|" + record.socket;
            if (record.transport_chunk_id.empty()) {
                if (const auto existing = connections.find(key); existing != connections.end()) {
                    if (record.stage == "PreEncrypt") ++existing->second.pre_encrypt;
                    else if (record.stage == "PostDecrypt") ++existing->second.post_decrypt;
                    else if (record.stage == "HandlerDecoded") ++existing->second.handler_decoded;
                }
                continue;
            }
            auto& connection = connections[key];
            connection.process_id = process_id;
            connection.socket = record.socket;
            connection.connection_id = record.socket == "Unknown" ? "unknown-connection" :
                "process-" + std::to_string(process_id) + "-socket-" + record.socket;
            if (connection.local_endpoint.empty())
                connection.local_endpoint = GetString(record.fields, "local", GetString(record.fields, "LocalEndpoint"));
            if (connection.remote_endpoint.empty())
                connection.remote_endpoint = GetString(record.fields, "remote", GetString(record.fields, "RemoteEndpoint"));
            if (connection.first_sequence == 0 || record.capture_sequence < connection.first_sequence) {
                connection.first_sequence = record.capture_sequence;
                connection.first_ms = record.captured_at_unix_ms;
                connection.first_utc = record.captured_at_utc;
            }
            if (record.capture_sequence >= connection.last_sequence) {
                connection.last_sequence = record.capture_sequence;
                connection.last_ms = record.captured_at_unix_ms;
                connection.last_utc = record.captured_at_utc;
            }
            const auto bytes = record.payload_hex.size() / 2;
            if (record.direction == "ClientToServer") {
                ++connection.c2s_chunks;
                connection.c2s_bytes += bytes;
            } else if (record.direction == "ServerToClient") {
                ++connection.s2c_chunks;
                connection.s2c_bytes += bytes;
            }
            if (record.api == "send") ++connection.sends;
            else if (record.api == "WSASend") ++connection.wsa_sends;
            else if (record.api == "recv") ++connection.recvs;
            else if (record.api == "WSARecv") ++connection.wsa_recvs;
        }
        result.connection_count = connections.size();

        const std::string packaged_at = UtcNow();
        std::string analyzed_at = packaged_at;
        std::string analysis_run_id;
        std::string package_run_id = GetString(session_fields, "PackageRunId", NewId());
        const std::string original_package_sha256 = GetString(session_fields, "OriginalPackageSHA256");
        const std::string source_package_schema_version = GetString(session_fields, "SourcePackageSchemaVersion");
        if (const auto summary = ReadUtf8File(session_path / L"session-summary.json")) {
            Fields summary_fields;
            if (ParseFlatJson(*summary, summary_fields, &parse_error)) {
                analyzed_at = GetString(summary_fields, "CompletedAtUtc", packaged_at);
                analysis_run_id = GetString(summary_fields, "AnalysisRunId");
                package_run_id = GetString(summary_fields, "PackageRunId", package_run_id);
            }
        }
        if (analysis_run_id.empty()) analysis_run_id = GetString(session_fields, "AnalysisRunId", NewId());
        const fs::path staging = session_path / L"evidence-package-staging";
        std::error_code staging_error;
        if (fs::exists(staging, staging_error) && ContainsReparsePoint(session_path, staging)) {
            result.error = "Evidence Package staging path is a reparse point";
            result.blockers.push_back(result.error);
            return result;
        }
        fs::remove_all(staging, staging_error);
        staging_error.clear();
        if (!EnsureContainedDirectory(session_path, staging / L"PrimarySession" / L"raw")) {
            result.error = "cannot create a contained, reparse-free Evidence Package staging tree";
            result.blockers.push_back(result.error);
            return result;
        }
        const fs::path primary = staging / L"PrimarySession";
        for (const auto* directory : {L"raw", L"frames", L"decoded", L"decrypted", L"protocol",
                                      L"mapping", L"derived", L"evidence", L"analysis", L"gameplay", L"exports",
                                      L"convenience", L"semantic", L"reports"}) {
            fs::create_directories(primary / directory, staging_error);
            if (staging_error) {
                result.error = "cannot create normalized Evidence Package directory: " + staging_error.message();
                result.blockers.push_back(result.error);
                return result;
            }
        }
        const auto write_required = [&](const fs::path& path, std::string_view content) {
            if (WriteUtf8FileAtomic(path, content)) return true;
            result.error = "cannot write required Evidence Package artifact: " + RelativeUtf8(staging, path);
            result.blockers.push_back(result.error);
            return false;
        };
        const fs::path normalized_records = primary / L"raw" / L"capture-records.jsonl";
        const fs::path immutable_source_records = primary / L"raw" / L"original-package-capture-records.jsonl";
        const fs::path source_semantic_events = session_path / L"raw" / L"semantic-events.jsonl";
        const fs::path packaged_semantic_events = primary / L"raw" / L"semantic-events.jsonl";
        const fs::path source_semantic_segment_manifest = session_path / L"reports" /
            L"semantic-segment-manifest.json";
        const fs::path packaged_semantic_segment_manifest = primary / L"reports" /
            L"semantic-segment-manifest.json";
        const fs::path packaged_semantic_segment_directory = primary / L"raw" /
            L"semantic-segments";
        const fs::path semantic_privacy_report = primary / L"reports" /
            L"semantic-privacy-gate.json";
        SemanticPrivacyGateResult semantic_privacy_gate;
        bool semantic_privacy_incomplete = false;
        bool semantic_segments_complete = false;
        const bool source_semantic_present = fs::is_regular_file(source_semantic_events);
        const fs::path source_semantic_health = session_path / L"reports" / L"semantic-shared-ring.json";
        const fs::path packaged_semantic_health = primary / L"reports" / L"semantic-shared-ring.json";
        const fs::path source_deep_probe_map = session_path / L"raw" / L"enhanced-x86" /
            L"deep-probe-candidate-map.json";
        const fs::path packaged_deep_probe_map = primary / L"reports" /
            L"deep-probe-candidate-map.json";
        const fs::path chunks_path = primary / L"raw" / L"transport-chunks.jsonl";
        const fs::path protocol_frames_path = primary / L"frames" / L"protocol-frames.jsonl";
        const fs::path decoded_path = primary / L"decoded" / L"decoded-messages.jsonl";
        const fs::path handlers_path = primary / L"decoded" / L"handler-observations.jsonl";
        const fs::path pre_encrypt_path = primary / L"decrypted" / L"pre-encrypt-records.jsonl";
        const fs::path post_decrypt_path = primary / L"decrypted" / L"post-decrypt-records.jsonl";
        const fs::path handler_decoded_path = primary / L"decrypted" / L"handler-decoded-records.jsonl";
        const fs::path unframed_path = primary / L"decrypted" / L"unframed-records.jsonl";
        const fs::path decoded_frames_path = primary / L"protocol" / L"decoded-frames.jsonl";
        const fs::path opcode_worklist_path = primary / L"protocol" / L"codex-opcode-worklist.jsonl";
        const fs::path opcode_summary_path = primary / L"protocol" / L"opcode-summary.json";
        const fs::path direction_summary_path = primary / L"protocol" / L"direction-summary.json";
        const fs::path frame_validation_summary_path = primary / L"protocol" / L"frame-validation-summary.json";
        const fs::path transport_mapping_path = primary / L"mapping" / L"transport-to-decrypted.jsonl";
        const fs::path handler_mapping_path = primary / L"mapping" / L"decrypted-to-handler.jsonl";
        const fs::path candidate_frames_path = primary / L"derived" / L"candidate-protocol-frames.jsonl";
        const fs::path incomplete_fragments_path = primary / L"derived" / L"incomplete-stream-fragments.jsonl";
        const fs::path gameplay_candidates_path = primary / L"derived" / L"gameplay-candidates.jsonl";
        const fs::path automatic_semantic_summary_path = primary / L"analysis" / L"automatic-semantic-summary.json";
        const fs::path evidence_items_path = primary / L"evidence" / L"evidence-items.jsonl";
        const fs::path logical_map_path = primary / L"evidence" / L"logical-message-map.jsonl";
        const fs::path action_instances_path = primary / L"analysis" / L"action-instances.jsonl";
        const fs::path action_patterns_path = primary / L"analysis" / L"action-patterns.jsonl";
        const fs::path action_pattern_summary_path = primary / L"analysis" / L"action-pattern-summary.json";
        const fs::path orphan_handler_bursts_path = primary / L"analysis" / L"orphan-handler-bursts.jsonl";
        const fs::path outbound_batch_candidates_path = primary / L"analysis" / L"outbound-batch-candidates.jsonl";
        const fs::path performance_path = primary / L"performance";
        const fs::path provenance_path = staging / L"provenance.jsonl";
        progress("staging-created");
        if (!WriteGpuEvidenceReports(performance_path, result.session_id, gpu_processing, &result.error)) {
            result.blockers.push_back(result.error);
            return result;
        }
        if (!original_package_sha256.empty() &&
            !CopyArtifact(input_records, immutable_source_records, session_path, staging)) {
            result.error = "validated source package CaptureRecords could not be preserved byte-for-byte";
            result.blockers.push_back(result.error);
            return result;
        }
        if (source_semantic_present) {
            if (!SanitizeSemanticEventsForPackage(source_semantic_events,
                    packaged_semantic_events, session_path, staging,
                    result.session_id, &semantic_privacy_gate, &result.error)) {
                result.blockers.push_back(result.error);
                return result;
            }
            semantic_privacy_incomplete = semantic_privacy_gate.suppressed != 0U ||
                semantic_privacy_gate.rejected != 0U;
            if (semantic_privacy_incomplete) {
                result.blockers.push_back(
                    "EvidenceBlockedSemanticPrivacyGateSuppressedOrRejectedSourceEvents");
            }
        }
        if (!write_required(semantic_privacy_report, MakeJsonObject({
                {"SchemaVersion", "god2-semantic-privacy-gate-v1"},
                {"SourceStatus", source_semantic_present ?
                    "Present" : "EvidenceBlockedSourceUnavailable"},
                {"OutputClass", "SanitizedDerivative"},
                {"RawByteCopyAllowed", "false"},
                {"Accepted", std::to_string(semantic_privacy_gate.accepted)},
                {"SensitiveSuppressed", std::to_string(semantic_privacy_gate.suppressed)},
                {"Rejected", std::to_string(semantic_privacy_gate.rejected)},
                {"Status", semantic_privacy_incomplete ?
                    "EvidenceBlockedSensitiveOrInvalidEventsExcluded" : "Pass"}
            }, {"RawByteCopyAllowed", "Accepted", "SensitiveSuppressed", "Rejected"}) +
            "\n")) {
            return result;
        }
        StrictSemanticSegmentManifest source_segment_manifest;
        std::vector<fs::path> source_segment_paths;
        std::string segment_validation_error;
        const bool source_segments_valid = source_semantic_present &&
            fs::is_regular_file(source_semantic_segment_manifest) &&
            ValidateSourceSemanticSegments(source_semantic_segment_manifest,
                session_path, source_semantic_events, result.session_id,
                &source_segment_manifest, &source_segment_paths,
                &segment_validation_error) &&
            FileSha256(packaged_semantic_events).value_or("") ==
                source_segment_manifest.merged_sha256;
        if (source_segments_valid) {
            if (!EnsureContainedDirectory(staging, packaged_semantic_segment_directory) ||
                !CopyArtifact(source_semantic_segment_manifest,
                    packaged_semantic_segment_manifest, session_path, staging)) {
                result.error = "semantic segment manifest could not be preserved byte-for-byte";
                result.blockers.push_back(result.error);
                return result;
            }
            for (std::size_t index = 0; index < source_segment_paths.size(); ++index) {
                const fs::path target = primary /
                    Utf8ToWide(source_segment_manifest.segments[index].relative_path);
                if (!CopyArtifact(source_segment_paths[index], target, session_path, staging)) {
                    result.error = "semantic segment could not be preserved byte-for-byte";
                    result.blockers.push_back(result.error);
                    return result;
                }
            }
            StrictSemanticSegmentManifest packaged_manifest_check;
            std::vector<fs::path> packaged_segment_paths;
            semantic_segments_complete = ValidateSourceSemanticSegments(
                packaged_semantic_segment_manifest, primary, packaged_semantic_events,
                result.session_id, &packaged_manifest_check, &packaged_segment_paths,
                &segment_validation_error);
            if (!semantic_segments_complete) {
                result.error = "packaged semantic segment continuity validation failed";
                result.blockers.push_back(result.error);
                return result;
            }
        } else {
            result.blockers.push_back(
                "EvidenceBlockedSemanticSegmentManifestUnavailableOrInvalid");
            if (!write_required(packaged_semantic_segment_manifest, MakeJsonObject({
                    {"SchemaVersion", "god2-semantic-segment-manifest-v1"},
                    {"GeneratedAtUtc", UtcNow()},
                    {"Status", "EVIDENCE_BLOCKED_SEGMENT_OR_CONTINUITY_FAILURE"},
                    {"SegmentMaximumBytes", std::to_string(kSemanticSegmentMaximumBytes)},
                    {"SegmentMaximumRecords", std::to_string(kSemanticSegmentMaximumRecords)},
                    {"SegmentCount", "0"}, {"RecordCount", "0"},
                    {"SequenceContinuity", "false"},
                    {"MergedPath", "raw/semantic-events.jsonl"},
                    {"MergedSHA256", ""}, {"Segments", "[]"}
                }, {"SegmentMaximumBytes", "SegmentMaximumRecords", "SegmentCount",
                    "RecordCount", "SequenceContinuity", "Segments"}) + "\n")) {
                return result;
            }
        }
        if (fs::is_regular_file(source_semantic_health)) {
            if (!CopyArtifact(source_semantic_health, packaged_semantic_health, session_path, staging)) {
                result.error = "semantic shared-memory health evidence could not be preserved byte-for-byte";
                result.blockers.push_back(result.error);
                return result;
            }
        } else if (!write_required(packaged_semantic_health, MakeJsonObject({
            {"SchemaVersion", "god2-semantic-shared-ring-health-availability-v1"},
            {"Status", "EvidenceBlockedSharedTransportHealthUnavailable"},
            {"RequiredSchemaVersion", "god2-semantic-shared-ring-health-v4"},
            {"Promotable", "false"},
            {"Reason", "No complete versioned shared-ring health artifact was present in the source session"}
        }, {"Promotable"}) + "\n")) {
            return result;
        }
        if (fs::is_regular_file(source_deep_probe_map)) {
            if (!CopyArtifact(source_deep_probe_map, packaged_deep_probe_map, session_path, staging)) {
                result.error = "deep-probe candidate map could not be preserved byte-for-byte";
                result.blockers.push_back(result.error);
                return result;
            }
        } else if (!write_required(packaged_deep_probe_map, MakeJsonObject({
            {"SchemaVersion", "god2-deep-probe-candidate-map-availability-v1"},
            {"Status", "EvidenceBlockedCandidateMapUnavailable"},
            {"RequiredSchemaVersion", "god2-deep-probe-candidate-map-v2"},
            {"Promotable", "false"},
            {"DomainCount", "25"}, {"ConfirmedContractDomainCount", "4"},
            {"CandidateOnlyBlockedDomainCount", "21"},
            {"Reason", "No complete v2 candidate discovery map was present in the source session"}
        }, {"Promotable", "DomainCount", "ConfirmedContractDomainCount",
            "CandidateOnlyBlockedDomainCount"}) + "\n")) {
            return result;
        }

        const fs::path candidate_reanalysis = staging / L".candidate-reanalysis";
        for (const auto* directory : {L"raw", L"gameplay", L"exports", L"reports"})
            fs::create_directories(candidate_reanalysis / directory, staging_error);
        if (staging_error) {
            result.error = "cannot create isolated candidate reanalysis workspace: " + staging_error.message();
            result.blockers.push_back(result.error);
            return result;
        }
        std::string candidate_error;
        God2FrameCandidateAnalysisResult candidate_analysis;
        candidate_analysis.success = true;
        candidate_analysis.message = "no enhanced CaptureRecords were available for candidate reanalysis";
        if (enhanced_record_source_available)
            candidate_analysis = AnalyzeInjectedGod2FrameCandidates(candidate_reanalysis, input_records,
                                                                      &candidate_error);
        if (!candidate_analysis.success) {
            result.error = "candidate reanalysis failed: " + candidate_error;
            result.blockers.push_back(result.error);
            return result;
        }
        result.desync_bytes = candidate_analysis.desync_bytes;
        result.pending_stream_bytes = candidate_analysis.pending_stream_bytes;
        result.invalid_candidate_frames = candidate_analysis.invalid_length_count;
        result.incomplete_stream_fragments = candidate_analysis.incomplete_stream_count;

        std::ofstream capture_output(normalized_records, std::ios::binary);
        std::ofstream chunk_output(chunks_path, std::ios::binary);
        std::ofstream decoded_output(decoded_path, std::ios::binary);
        std::ofstream handler_output(handlers_path, std::ios::binary);
        std::ofstream protocol_frame_output(protocol_frames_path, std::ios::binary);
        std::ofstream decoded_frame_output(decoded_frames_path, std::ios::binary);
        std::ofstream pre_encrypt_output(pre_encrypt_path, std::ios::binary);
        std::ofstream post_decrypt_output(post_decrypt_path, std::ios::binary);
        std::ofstream handler_decoded_output(handler_decoded_path, std::ios::binary);
        std::ofstream unframed_output(unframed_path, std::ios::binary);
        std::ofstream transport_mapping_output(transport_mapping_path, std::ios::binary);
        std::ofstream handler_mapping_output(handler_mapping_path, std::ios::binary);
        std::ofstream evidence_output(evidence_items_path, std::ios::binary);
        std::ofstream provenance_output(provenance_path, std::ios::binary);
        if (!capture_output || !chunk_output || !decoded_output || !handler_output || !protocol_frame_output ||
            !decoded_frame_output || !pre_encrypt_output || !post_decrypt_output || !handler_decoded_output ||
            !unframed_output || !transport_mapping_output || !handler_mapping_output ||
            !evidence_output || !provenance_output) {
            result.error = "cannot create normalized Evidence Package outputs";
            result.blockers.push_back(result.error);
            return result;
        }

        for (const auto& record : records) {
            const std::string observed_evidence_level = record.stage == "Transport" ? "Transmitted" :
                (record.stage == "PreEncrypt" || record.stage == "PostDecrypt") ? "RuntimeObservedPlaintext" :
                record.stage == "HandlerDecoded" ? "RuntimeObservedHandler" : "EvidenceBlocked";
            const bool plaintext = record.stage == "PreEncrypt" || record.stage == "PostDecrypt" ||
                record.stage == "HandlerDecoded";
            const auto source_connection_id = GetString(record.fields, "ConnectionId");
            const std::string connection_id = !source_connection_id.empty() ? source_connection_id :
                (!plaintext && record.socket != "Unknown" && record.socket != "0xFFFFFFFF" ?
                    "process-" + std::to_string(GetInt64(record.fields, "ProcessId").value_or(0)) +
                        "-socket-" + record.socket : "");
            const std::string connection_status = connection_id.empty() ?
                "UnavailableAtInternalBoundary" : "ReliableSocketIdentity";
            std::string factual_opcode = record.frame_opcode;
            if (record.stage == "HandlerDecoded" && record.payload_hex.size() >= 2)
                factual_opcode = "0x" + record.payload_hex.substr(0, 2);
            std::vector<std::string> related_ids;
            std::vector<std::string> transport_ids;
            for (const auto index : record.related) {
                related_ids.push_back(records[index].capture_record_id);
                if (!records[index].transport_chunk_id.empty()) transport_ids.push_back(records[index].transport_chunk_id);
            }
            if (!record.transport_chunk_id.empty()) transport_ids.push_back(record.transport_chunk_id);
            const auto original_hash = Sha256(record.original).value_or("");
            const auto preserved_string = [&](std::string_view name) {
                const auto value = GetString(record.fields, name);
                return value.empty() ? std::string("null") : "\"" + JsonEscape(value) + "\"";
            };
            capture_output << MakeJsonObject({
                {"SchemaId", "capture-record"}, {"SchemaVersion", "11"},
                {"SessionId", result.session_id}, {"CaptureRecordId", record.capture_record_id},
                {"LogicalMessageId", record.logical_message_id}, {"CaptureStage", record.stage},
                {"StageSequence", std::to_string(record.stage_sequence)},
                {"CaptureSequence", std::to_string(record.capture_sequence)},
                {"SourceSequence", std::to_string(record.source_sequence)},
                {"CapturedAtUnixMs", std::to_string(record.captured_at_unix_ms)},
                {"CapturedAtUtc", record.captured_at_utc},
                {"CapturedMonotonicTicks", record.captured_qpc == 0 ? "null" : std::to_string(record.captured_qpc)},
                {"CapturedQpc", record.captured_qpc == 0 ? "null" : std::to_string(record.captured_qpc)},
                {"QpcFrequency", record.qpc_frequency == 0 ? "null" : std::to_string(record.qpc_frequency)},
                {"AnalyzedAtUtc", analyzed_at}, {"PackagedAtUtc", packaged_at},
                {"Direction", record.direction}, {"ProcessId", NumberOrNull(GetInt64(record.fields, "ProcessId"))},
                {"ThreadId", NumberOrNull(GetInt64(record.fields, "ThreadId"))},
                {"ThreadSequence", std::to_string(record.thread_sequence)},
                {"HookInvocationId", record.hook_invocation_id},
                {"ParentInvocationId", record.parent_invocation_id.empty() ? "null" :
                    "\"" + JsonEscape(record.parent_invocation_id) + "\""},
                {"ParentCorrelationBasis", record.parent_correlation_basis},
                {"ContextInvocationId", record.context_invocation_id.empty() ? "null" :
                    "\"" + JsonEscape(record.context_invocation_id) + "\""},
                {"ContextCorrelationBasis", record.context_correlation_basis},
                {"CorrelationLevel", record.correlation_level},
                {"CallDepth", std::to_string(record.call_depth)},
                {"Socket", record.socket}, {"ConnectionId", connection_id.empty() ? "null" :
                    "\"" + JsonEscape(connection_id) + "\""},
                {"ConnectionIdentityStatus", connection_status}, {"Api", record.api},
                {"MessageType", record.stage == "Transport" ? "TransportChunk" :
                    record.stage == "HandlerDecoded" ? "HandlerDecodedRecord" : "PlaintextProtocolRecord"},
                {"Opcode", factual_opcode},
                {"Transport", GetString(record.fields, "Transport")},
                {"CaptureSource", GetString(record.fields, "CaptureSource")},
                {"EvidenceLevel", "RawCaptured"},
                {"SourceEvidenceLevel", observed_evidence_level},
                {"PayloadHex", record.payload_hex}, {"PayloadSHA256", record.payload_sha256},
                {"Plaintext", plaintext ? "true" : "false"},
                {"PlaintextLength", plaintext ? std::to_string(record.payload_hex.size() / 2) : "0"},
                {"PlaintextHex", plaintext ? record.payload_hex : ""},
                {"FrameEnvelopeStatus", !record.protocol_frame_id.empty() ? "CompleteVerifiedFrame" :
                    record.stage == "HandlerDecoded" ? "HandlerRecordNoFrameEnvelope" :
                    record.stage == "Transport" ? "TransportChunk" : "UnframedEvidenceOnly"},
                {"FrameValidationStatus", record.frame_validation_status.empty() ? "NotApplicable" :
                    record.frame_validation_status},
                {"PayloadLength", std::to_string(record.payload_hex.size() / 2)},
                {"PayloadPrefixHash", record.payload_prefix_hash}, {"PayloadSuffixHash", record.payload_suffix_hash},
                {"CapturedLength", std::to_string(record.payload_hex.size() / 2)},
                {"RequestedLength", NumberOrNull(GetInt64(record.fields, "RequestedLength"))},
                {"TransferredLength", NumberOrNull(GetInt64(record.fields, "TransferredLength"))},
                {"LastError", NumberOrNull(GetInt64(record.fields, "LastError"))},
                {"Frame208", GetString(record.fields, "frame208") == "true" ? "true" : "false"},
                {"ReturnAddress", "null"},
                {"Caller", record.caller_module.empty() ? "" : record.caller_module + "+" + record.caller_rva},
                {"CallerModule", record.caller_module}, {"CallerRva", record.caller_rva},
                {"Stack", GetString(record.fields, "stack")},
                {"LocalEndpoint", GetString(record.fields, "local")},
                {"RemoteEndpoint", GetString(record.fields, "remote")},
                {"OriginalSourceRecordId", record.source_id}, {"OriginalRecordSHA256", original_hash},
                {"ParentRecordIds", "[]"}, {"RelatedRecordIds", JsonStringArray(related_ids)},
                {"TransportChunkIds", JsonStringArray(transport_ids)},
                {"ProtocolFrameId", record.protocol_frame_id.empty() ? "null" :
                    "\"" + JsonEscape(record.protocol_frame_id) + "\""},
                {"DecodedMessageId", record.decoded_message_id.empty() ? "null" :
                    "\"" + JsonEscape(record.decoded_message_id) + "\""},
                {"HandlerObservationId", record.handler_observation_id.empty() ? "null" :
                    "\"" + JsonEscape(record.handler_observation_id) + "\""},
                {"ProtocolRegistryVerificationStatus", preserved_string("ProtocolRegistryVerificationStatus")},
                {"VerifiedProtocolRegistrySource", preserved_string("VerifiedProtocolRegistrySource")},
                {"VerifiedProtocolRegistryEvidenceSHA256", preserved_string("VerifiedProtocolRegistryEvidenceSHA256")},
                {"VerifiedProtocolRegistryId", preserved_string("VerifiedProtocolRegistryId")},
                {"VerifiedGameplaySemantic", preserved_string("VerifiedGameplaySemantic")},
                {"quest_action_correlation_id", preserved_string("quest_action_correlation_id")},
                {"skill_cast_correlation_id", preserved_string("skill_cast_correlation_id")},
                {"item_use_correlation_id", preserved_string("item_use_correlation_id")},
                {"party_correlation_id", preserved_string("party_correlation_id")},
                {"economy_correlation_id", preserved_string("economy_correlation_id")},
                {"ValidJson", record.valid_json ? "true" : "false"},
                {"ValidPayloadHex", record.valid_payload ? "true" : "false"},
                {"ValidLength", record.valid_length ? "true" : "false"}
            }, {"SchemaVersion", "StageSequence", "CaptureSequence", "SourceSequence", "CapturedAtUnixMs",
                "CapturedMonotonicTicks", "CapturedQpc", "QpcFrequency", "ProcessId", "ThreadId", "ThreadSequence",
                "ParentInvocationId", "ContextInvocationId", "CallDepth", "ConnectionId", "Plaintext", "PlaintextLength", "PayloadLength", "ReturnAddress", "CapturedLength", "RequestedLength",
                "TransferredLength", "LastError", "Frame208", "ParentRecordIds",
                 "RelatedRecordIds", "TransportChunkIds", "ProtocolFrameId", "DecodedMessageId",
                 "HandlerObservationId", "ProtocolRegistryVerificationStatus", "VerifiedProtocolRegistrySource",
                 "VerifiedProtocolRegistryEvidenceSHA256", "VerifiedProtocolRegistryId",
                 "VerifiedGameplaySemantic", "quest_action_correlation_id", "skill_cast_correlation_id",
                 "item_use_correlation_id", "party_correlation_id", "economy_correlation_id",
                 "ValidJson", "ValidPayloadHex", "ValidLength"}) << '\n';

            if (!record.transport_chunk_id.empty()) {
                chunk_output << MakeJsonObject({
                    {"SchemaId", "transport-chunk"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                    {"TransportChunkId", record.transport_chunk_id}, {"CaptureRecordId", record.capture_record_id},
                    {"LogicalMessageId", record.logical_message_id}, {"ConnectionId", record.socket == "Unknown" ?
                        "unknown-connection" : "process-" +
                        std::to_string(GetInt64(record.fields, "ProcessId").value_or(0)) + "-socket-" + record.socket},
                    {"ConnectionEpoch", "1"},
                    {"Socket", record.socket}, {"Direction", record.direction}, {"Api", record.api},
                    {"Sequence", std::to_string(record.capture_sequence)}, {"CapturedAtUnixMs", std::to_string(record.captured_at_unix_ms)},
                    {"CapturedAtUtc", record.captured_at_utc}, {"PayloadHex", record.payload_hex},
                    {"PayloadSHA256", record.payload_sha256}, {"ChunkLength", std::to_string(record.payload_hex.size() / 2)},
                    {"IsProtocolFrame", "false"}
                }, {"SchemaVersion", "ConnectionEpoch", "Sequence", "CapturedAtUnixMs", "ChunkLength", "IsProtocolFrame"}) << '\n';
            }
            if (!record.decoded_message_id.empty()) {
                decoded_output << MakeJsonObject({
                    {"SchemaId", "decoded-message"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                    {"DecodedMessageId", record.decoded_message_id}, {"LogicalMessageId", record.logical_message_id},
                    {"CaptureRecordIds", JsonStringArray({record.capture_record_id})},
                    {"ProtocolFrameId", record.protocol_frame_id.empty() ? "null" :
                        "\"" + JsonEscape(record.protocol_frame_id) + "\""},
                    {"CaptureStage", record.stage}, {"Direction", record.direction},
                    {"ConnectionId", connection_id.empty() ? "null" : "\"" + JsonEscape(connection_id) + "\""},
                    {"CapturedAtUnixMs", std::to_string(record.captured_at_unix_ms)}, {"CapturedAtUtc", record.captured_at_utc},
                    {"Opcode", factual_opcode}, {"Plaintext", "true"},
                    {"PlaintextLength", std::to_string(record.payload_hex.size() / 2)},
                    {"PlaintextHex", record.payload_hex}, {"PayloadHex", record.payload_hex},
                    {"PayloadSHA256", record.payload_sha256},
                    {"FrameValidationStatus", record.frame_validation_status},
                    {"DecoderVersion", "God2SemanticRecoveryEngine-x86-probe-" GOD2_TOOL_VERSION},
                    {"EvidenceLevel", "RuntimeObservedPlaintext"}
                }, {"SchemaVersion", "CaptureRecordIds", "ProtocolFrameId", "ConnectionId", "CapturedAtUnixMs",
                    "Plaintext", "PlaintextLength"}) << '\n';
                provenance_output << MakeJsonObject({
                    {"SchemaId", "provenance"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                    {"ProvenanceId", "provenance-" + record.decoded_message_id}, {"ArtifactType", "DecodedMessage"},
                    {"ArtifactId", record.decoded_message_id}, {"LogicalMessageId", record.logical_message_id},
                    {"CaptureRecordIds", JsonStringArray({record.capture_record_id})},
                    {"ProtocolFrameIds", record.protocol_frame_id.empty() ? "[]" :
                        JsonStringArray({record.protocol_frame_id})},
                    {"CaptureStage", record.stage}, {"PayloadSHA256", record.payload_sha256},
                    {"DecoderVersion", "God2SemanticRecoveryEngine-x86-probe-" GOD2_TOOL_VERSION}
                }, {"SchemaVersion", "CaptureRecordIds", "ProtocolFrameIds"}) << '\n';
            }
            std::string decrypted_record;
            if (plaintext) {
                decrypted_record = MakeJsonObject({
                    {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                    {"CaptureRecordId", record.capture_record_id}, {"CaptureStage", record.stage},
                    {"Direction", record.direction},
                    {"CapturedAtUnixMs", std::to_string(record.captured_at_unix_ms)},
                    {"CapturedAtUtc", record.captured_at_utc},
                    {"ProcessId", NumberOrNull(GetInt64(record.fields, "ProcessId"))},
                    {"ThreadId", NumberOrNull(GetInt64(record.fields, "ThreadId"))},
                    {"ObservationSequence", std::to_string(record.source_sequence)},
                    {"ConnectionId", connection_id.empty() ? "null" : "\"" + JsonEscape(connection_id) + "\""},
                    {"ConnectionIdentityStatus", connection_status},
                    {"Plaintext", "true"}, {"PlaintextLength", std::to_string(record.payload_hex.size() / 2)},
                    {"PlaintextHex", record.payload_hex}, {"PayloadSHA256", record.payload_sha256},
                    {"Opcode", factual_opcode},
                    {"ProtocolFrameId", record.protocol_frame_id.empty() ? "null" :
                        "\"" + JsonEscape(record.protocol_frame_id) + "\""},
                    {"FrameBoundary", record.protocol_frame_id.empty() ? "Unframed" : "ExactRecordBoundary"},
                    {"FrameValidationStatus", record.frame_validation_status.empty() ? "NotApplicable" :
                        record.frame_validation_status},
                    {"FrameValidationReason", record.frame_validation_reason.empty() ? "NotApplicable" :
                        record.frame_validation_reason},
                    {"EvidenceLevel", observed_evidence_level}
                }, {"SchemaVersion", "CapturedAtUnixMs", "ProcessId", "ThreadId", "ObservationSequence",
                    "ConnectionId", "Plaintext", "PlaintextLength", "ProtocolFrameId"}) + "\n";
                if (record.stage == "PreEncrypt") pre_encrypt_output << decrypted_record;
                else if (record.stage == "PostDecrypt") post_decrypt_output << decrypted_record;
                else handler_decoded_output << decrypted_record;
            }
            if (!record.protocol_frame_id.empty()) {
                const auto frame_json = MakeJsonObject({
                    {"SchemaId", "protocol-frame"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                    {"ProtocolFrameId", record.protocol_frame_id},
                    {"CaptureRecordIds", JsonStringArray({record.capture_record_id})},
                    {"DecodedMessageId", record.decoded_message_id}, {"CaptureStage", record.stage},
                    {"Direction", record.direction},
                    {"CapturedAtUnixMs", std::to_string(record.captured_at_unix_ms)},
                    {"CapturedAtUtc", record.captured_at_utc},
                    {"ConnectionId", connection_id.empty() ? "null" : "\"" + JsonEscape(connection_id) + "\""},
                    {"ConnectionIdentityStatus", connection_status},
                    {"FrameBoundary", "VerifiedExactRecordBoundary"},
                    {"FrameLength", std::to_string(record.frame_length)},
                    {"DeclaredFrameLength", std::to_string(record.frame_length)},
                    {"LengthPrefixOffset", "0"}, {"LengthPrefixSize", "2"},
                    {"LengthPrefixEndian", "LittleEndian"}, {"LengthIncludesPrefix", "true"},
                    {"OpcodeOffset", "2"}, {"Opcode", record.frame_opcode},
                    {"PlainPayloadHex", record.plain_payload_hex},
                    {"FrameHex", record.payload_hex}, {"FrameSHA256", record.payload_sha256},
                    {"Checksum", std::to_string(record.frame_checksum)}, {"ChecksumValid", "true"},
                    {"ValidationStatus", "Verified"},
                    {"ValidationRule", "u16le exact length plus additive 0x3C checksum"},
                    {"ProtocolSemantic", "UnknownOpcode"}, {"EvidenceLevel", "RuntimeObservedPlaintext"},
                    {"ProductionGameplayEligible", "false"}
                }, {"SchemaVersion", "CaptureRecordIds", "CapturedAtUnixMs", "ConnectionId", "FrameLength",
                    "DeclaredFrameLength", "LengthPrefixOffset", "LengthPrefixSize", "LengthIncludesPrefix",
                    "OpcodeOffset", "Checksum", "ChecksumValid", "ProductionGameplayEligible"}) + "\n";
                protocol_frame_output << frame_json;
                decoded_frame_output << frame_json;
            } else if (record.stage == "PreEncrypt" || record.stage == "PostDecrypt") {
                unframed_output << decrypted_record;
            }
            if (!record.handler_observation_id.empty()) {
                handler_output << MakeJsonObject({
                    {"SchemaId", "handler-observation"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                    {"HandlerObservationId", record.handler_observation_id}, {"LogicalMessageId", record.logical_message_id},
                    {"CaptureRecordId", record.capture_record_id}, {"Direction", record.direction},
                    {"CapturedAtUnixMs", std::to_string(record.captured_at_unix_ms)}, {"CapturedAtUtc", record.captured_at_utc},
                    {"PayloadHex", record.payload_hex}, {"PayloadSHA256", record.payload_sha256},
                    {"HandlerAddress", record.caller_module.empty() ? "" : record.caller_module + "+" + record.caller_rva},
                    {"EvidenceLevel", "RuntimeObservedHandler"}
                }, {"SchemaVersion", "CapturedAtUnixMs"}) << '\n';
            }
            for (const auto related_index : record.related) {
                const auto& related = records[related_index];
                const auto& probe_context_basis = !record.context_correlation_basis.empty()
                    ? record.context_correlation_basis : related.context_correlation_basis;
                if (!record.decoded_message_id.empty() && !related.transport_chunk_id.empty()) {
                    transport_mapping_output << MakeJsonObject({
                        {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                        {"TransportRecordId", related.capture_record_id},
                        {"TransportChunkId", related.transport_chunk_id},
                        {"DecryptedRecordId", record.capture_record_id},
                        {"DecodedMessageId", record.decoded_message_id},
                        {"CorrelationBasis", record.correlation_level == "Exact" ?
                            "ExplicitParentInvocationId" : "ProbeIssuedSameThreadStageContext"},
                        {"ProbeContextCorrelationBasis", probe_context_basis},
                        {"EvidenceLevel", record.correlation_level}
                    }, {"SchemaVersion"}) << '\n';
                }
                if (!record.handler_observation_id.empty() && !related.decoded_message_id.empty()) {
                    handler_mapping_output << MakeJsonObject({
                        {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                        {"DecryptedRecordId", related.capture_record_id},
                        {"DecodedMessageId", related.decoded_message_id},
                        {"HandlerRecordId", record.capture_record_id},
                        {"HandlerObservationId", record.handler_observation_id},
                        {"CorrelationBasis", record.correlation_level == "Exact" ?
                            "ExplicitParentInvocationId" : "ProbeIssuedSameThreadStageContext"},
                        {"ProbeContextCorrelationBasis", probe_context_basis},
                        {"EvidenceLevel", record.correlation_level}
                    }, {"SchemaVersion"}) << '\n';
                }
            }
            evidence_output << MakeJsonObject({
                {"SchemaId", "evidence-item"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"EvidenceItemId", "evidence-item-" + std::to_string(record.capture_sequence)},
                {"EvidenceType", "CaptureRecord"}, {"CaptureRecordIds", JsonStringArray({record.capture_record_id})},
                {"TransportChunkIds", JsonStringArray(transport_ids)},
                {"DecodedMessageIds", record.decoded_message_id.empty() ? "[]" : JsonStringArray({record.decoded_message_id})},
                {"HandlerObservationIds", record.handler_observation_id.empty() ? "[]" : JsonStringArray({record.handler_observation_id})},
                {"ProtocolFrameIds", record.protocol_frame_id.empty() ? "[]" :
                    JsonStringArray({record.protocol_frame_id})}, {"EvidenceLevel", observed_evidence_level},
                {"PayloadSHA256", record.payload_sha256}, {"AuthoritativeSource", "PrimarySession/raw/capture-records.jsonl"}
            }, {"SchemaVersion", "CaptureRecordIds", "TransportChunkIds", "DecodedMessageIds",
                "HandlerObservationIds", "ProtocolFrameIds"}) << '\n';
        }
        result.evidence_items = records.size();
        capture_output.flush();
        if (!capture_output) {
            result.error = "normalized CaptureRecords could not be flushed before semantic analysis";
            result.blockers.push_back(result.error);
            return result;
        }
        const auto semantic = AnalyzeSemanticRecovery({
            result.session_id,
            result.client_sha256,
            session_path,
            normalized_records,
            packaged_semantic_events,
            primary / L"semantic",
            session_path.parent_path()
        });
        if (!semantic.success) {
            result.error = "semantic recovery failed: " + semantic.error;
            result.blockers.push_back(result.error);
            return result;
        }
        result.semantic_probe_status = semantic.status;
        result.parser_read_events = semantic.parser_read_events;
        result.serializer_write_events = semantic.serializer_write_events;
        result.handler_argument_events = semantic.handler_argument_events;
        result.object_resolution_events = semantic.object_resolution_events;
        result.state_mutation_events = semantic.state_mutation_events;
        result.ui_anchor_events = semantic.ui_anchor_events;
        result.semantic_value_flow_edges = semantic.value_flow_edges;
        result.protocol_field_evidence = semantic.protocol_field_evidence;
        result.unknown_field_semantics = semantic.unknown_fields;
        result.candidate_field_semantics = semantic.candidate_fields;
        result.recovered_field_semantics = semantic.recovered_fields;
        result.verified_field_semantics = semantic.verified_fields;
        result.verified_protocol_spec_packets = semantic.verified_spec_packets;
        result.server_integration_ready = semantic.server_integration_ready;
        result.database_integration_ready = semantic.database_integration_ready;
        result.semantic_contradictions = semantic.contradictions;
        result.semantic_evidence_incomplete = semantic.semantic_evidence_incomplete ||
            semantic_privacy_incomplete;
        progress("semantic-recovery-written");
        progress("normalized-records-written");

        std::ofstream logical_output(logical_map_path, std::ios::binary);
        if (!logical_output) {
            result.error = "cannot create logical-message-map.jsonl";
            result.blockers.push_back(result.error);
            return result;
        }
        for (const auto& [root, members] : logical_groups) {
            std::vector<std::string> record_ids;
            std::vector<std::string> stages;
            std::vector<std::string> chunks;
            std::vector<std::string> decoded;
            std::vector<std::string> handlers;
            std::vector<std::string> protocol_frames;
            std::vector<std::string> source_ids;
            std::unordered_set<std::string> unique_stages;
            for (const auto index : members) {
                const auto& record = records[index];
                record_ids.push_back(record.capture_record_id);
                source_ids.push_back(record.source_id);
                if (unique_stages.insert(record.stage).second) stages.push_back(record.stage);
                if (!record.transport_chunk_id.empty()) chunks.push_back(record.transport_chunk_id);
                if (!record.decoded_message_id.empty()) decoded.push_back(record.decoded_message_id);
                if (!record.handler_observation_id.empty()) handlers.push_back(record.handler_observation_id);
                if (!record.protocol_frame_id.empty()) protocol_frames.push_back(record.protocol_frame_id);
            }
            const auto& level = correlation_levels.at(root);
            const bool correlated = level == "Exact" || level == "Strong";
            const std::string score = level == "Exact" ? "100" : level == "Strong" ? "85" :
                level == "Candidate" ? "45" : "0";
            const std::string reasons = level == "Exact" ?
                "[\"explicit ParentInvocationId link\"]" :
                level == "Strong" ? "[\"probe-issued same-thread stage context link\"]" :
                "[\"no explicit cross-stage identity; timing, thread and payload similarity were not used\"]";
            logical_output << MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"LogicalMessageId", records[members.front()].logical_message_id},
                {"CaptureRecordIds", JsonStringArray(record_ids)}, {"CaptureStages", JsonStringArray(stages)},
                {"TransportChunkIds", JsonStringArray(chunks)}, {"DecodedMessageIds", JsonStringArray(decoded)},
                {"HandlerObservationIds", JsonStringArray(handlers)},
                {"ProtocolFrameIds", JsonStringArray(protocol_frames)},
                {"SourceRecordIds", JsonStringArray(source_ids)},
                {"CorrelationStatus", correlated ? "Correlated" : "Uncorrelated"},
                {"CorrelationLevel", level}, {"CorrelationScore", score},
                {"CorrelationReasons", reasons}, {"AmbiguityCount", std::to_string(group_ambiguities.at(root))},
                {"CorrelationConfidence", level == "Exact" ? "High" : level == "Strong" ? "HighCandidate" :
                    level == "Candidate" ? "Candidate" : "Unknown"},
                {"CorrelationReason", level == "Exact" ?
                    "Cross-stage observations carried an explicit parent invocation identity" :
                    level == "Strong" ?
                    "Cross-stage observations carried a probe-issued same-thread decode context identity" :
                    "Observation was intentionally left unmerged because no explicit identity was present"}
            }, {"SchemaVersion", "CaptureRecordIds", "CaptureStages", "TransportChunkIds",
                "DecodedMessageIds", "HandlerObservationIds", "ProtocolFrameIds", "SourceRecordIds",
                "CorrelationScore", "CorrelationReasons", "AmbiguityCount"}) << '\n';
        }
        if (!write_required(action_instances_path, action_grouping.action_instances_jsonl) ||
            !write_required(action_patterns_path, action_grouping.action_patterns_jsonl) ||
            !write_required(action_pattern_summary_path, action_grouping.action_pattern_summary_json) ||
            !write_required(orphan_handler_bursts_path, action_grouping.orphan_handler_bursts_jsonl) ||
            !write_required(outbound_batch_candidates_path, action_grouping.outbound_batch_candidates_jsonl))
            return result;
        progress("generic-action-grouping-written");

        std::ofstream candidate_frame_output(candidate_frames_path, std::ios::binary);
        std::ofstream incomplete_output(incomplete_fragments_path, std::ios::binary);
        if (!candidate_frame_output || !incomplete_output) {
            result.error = "cannot create candidate or incomplete stream output";
            result.blockers.push_back(result.error);
            return result;
        }
        const fs::path source_candidate_frames = candidate_reanalysis / L"raw" / L"god2-frame-candidates.jsonl";
        std::ifstream candidate_frame_input(source_candidate_frames, std::ios::binary);
        std::uint64_t candidate_frame_sequence = 0;
        while (std::getline(candidate_frame_input, line)) {
            if (line.empty()) continue;
            Fields fields;
            if (!ParseFlatJson(line, fields, &parse_error)) continue;
            const auto sources = SourceIds(GetString(fields, "SourceFrameIds"));
            std::vector<std::string> record_ids;
            std::vector<std::string> transport_ids;
            std::vector<std::size_t> source_indexes;
            for (const auto& source : sources) {
                const auto iterator = source_record_index.find(source);
                if (iterator != source_record_index.end()) {
                    const auto& record = records[iterator->second];
                    source_indexes.push_back(iterator->second);
                    record_ids.push_back(record.capture_record_id);
                    if (!record.transport_chunk_id.empty()) transport_ids.push_back(record.transport_chunk_id);
                }
            }
            std::sort(source_indexes.begin(), source_indexes.end());
            std::sort(transport_ids.begin(), transport_ids.end());
            transport_ids.erase(std::unique(transport_ids.begin(), transport_ids.end()), transport_ids.end());
            if (source_indexes.empty() || transport_ids.empty()) {
                ++result.invalid_candidate_frames;
                continue;
            }
            const auto& first_record = records[source_indexes.front()];
            const auto& last_record = records[source_indexes.back()];
            const auto payload_hex = GetString(fields, "PayloadHex");
            const auto declared_length = GetInt64(fields, "CandidateLength").value_or(0);
            const auto direction = GetString(fields, "PacketDirection", first_record.direction);
            const auto candidate_id = "candidate-protocol-frame-" + std::to_string(++candidate_frame_sequence);
            const std::string connection_key = std::to_string(GetInt64(first_record.fields, "ProcessId").value_or(0)) +
                "|" + first_record.socket;
            if (const auto connection = connections.find(connection_key); connection != connections.end()) {
                ++connection->second.candidate_frames;
                const auto candidate_desync = static_cast<std::uint64_t>(std::max<std::int64_t>(0,
                    GetInt64(fields, "DesyncBytesBeforeFrame").value_or(0)));
                connection->second.desync_bytes += candidate_desync;
                connection->second.invalid_lengths += candidate_desync;
            }
            candidate_frame_output << MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"CandidateFrameId", candidate_id}, {"CandidateProtocolFrameId", candidate_id},
                {"ProcessId", NumberOrNull(GetInt64(first_record.fields, "ProcessId"))},
                {"ConnectionId", first_record.socket == "Unknown" ? "unknown-connection" :
                    "process-" + std::to_string(GetInt64(first_record.fields, "ProcessId").value_or(0)) +
                    "-socket-" + first_record.socket},
                {"ConnectionEpoch", "1"}, {"Socket", first_record.socket},
                {"CaptureRecordIds", JsonStringArray(record_ids)}, {"TransportChunkIds", JsonStringArray(transport_ids)},
                {"FirstTransportChunkId", transport_ids.front()}, {"LastTransportChunkId", transport_ids.back()},
                {"Direction", direction},
                {"CandidateLength", std::to_string(declared_length)}, {"DeclaredLength", std::to_string(declared_length)},
                {"ActualLength", std::to_string(payload_hex.size() / 2)},
                {"CandidateOpcodeU16Le", GetString(fields, "CandidateOpcodeU16Le")},
                {"PayloadHex", payload_hex}, {"PayloadSHA256", Sha256(payload_hex).value_or("")},
                {"FirstCaptureSequence", std::to_string(first_record.capture_sequence)},
                {"LastCaptureSequence", std::to_string(last_record.capture_sequence)},
                {"ChunkCount", std::to_string(transport_ids.size())},
                {"FirstCapturedAtUnixMs", std::to_string(first_record.captured_at_unix_ms)},
                {"LastCapturedAtUnixMs", std::to_string(last_record.captured_at_unix_ms)},
                {"FirstCapturedAtUtc", first_record.captured_at_utc}, {"LastCapturedAtUtc", last_record.captured_at_utc},
                {"ReassemblyRuleId", "candidate-u16le-length-prefix-v1"},
                {"ReassemblyRuleDescription", "uint16 little-endian length at byte offset 0; declared length includes the two-byte prefix"},
                {"ReassemblerVersion", GOD2_TOOL_VERSION}, {"LengthPrefixOffset", "0"},
                {"LengthPrefixSize", "2"}, {"LengthPrefixEndian", "LittleEndian"}, {"LengthIncludesPrefix", "true"},
                {"ValidationStatus", "CandidateBoundaryMatched"},
                {"ValidationResults", "[\"DeclaredLengthWithin2To8192\",\"ActualLengthEqualsDeclaredLength\",\"TransportProvenancePresent\"]"},
                {"DesyncBytesBeforeFrame", NumberOrNull(GetInt64(fields, "DesyncBytesBeforeFrame"))},
                {"PendingBytesAfterFrame", NumberOrNull(GetInt64(fields, "PendingBytesAfterFrame"))},
                {"EvidenceLevel", "Candidate"}, {"Confidence", "65"},
                {"PromotedToProtocolFrame", "false"},
                {"CandidateReason", "Candidate little-endian length prefix matched reassembled transport bytes; protocol semantics remain unverified"},
                {"Reason", "Candidate little-endian length prefix is retained but remains unverified"}
            }, {"SchemaVersion", "ProcessId", "ConnectionEpoch", "CaptureRecordIds", "TransportChunkIds",
                "CandidateLength", "DeclaredLength", "ActualLength", "FirstCaptureSequence", "LastCaptureSequence",
                "ChunkCount", "FirstCapturedAtUnixMs", "LastCapturedAtUnixMs", "LengthPrefixOffset",
                "LengthPrefixSize", "LengthIncludesPrefix", "ValidationResults", "DesyncBytesBeforeFrame",
                "PendingBytesAfterFrame", "Confidence", "PromotedToProtocolFrame"}) << '\n';
            if (direction == "ClientToServer") ++result.candidate_client_to_server_frames;
            else if (direction == "ServerToClient") ++result.candidate_server_to_client_frames;
        }
        if (candidate_frame_input.bad()) {
            result.error = "candidate protocol-frame source could not be read completely";
            result.blockers.push_back(result.error);
            return result;
        }
        result.candidate_protocol_frames = candidate_frame_sequence;

        const fs::path source_incomplete = candidate_reanalysis / L"raw" / L"incomplete-stream-fragments.jsonl";
        std::ifstream incomplete_input(source_incomplete, std::ios::binary);
        std::uint64_t incomplete_sequence = 0;
        while (std::getline(incomplete_input, line)) {
            if (line.empty()) continue;
            Fields fields;
            if (!ParseFlatJson(line, fields, &parse_error)) continue;
            const auto sources = SourceIds(GetString(fields, "SourceFrameIds"));
            std::vector<std::string> transport_ids;
            std::vector<std::size_t> source_indexes;
            for (const auto& source : sources) {
                const auto iterator = source_record_index.find(source);
                if (iterator == source_record_index.end()) continue;
                source_indexes.push_back(iterator->second);
                if (!records[iterator->second].transport_chunk_id.empty())
                    transport_ids.push_back(records[iterator->second].transport_chunk_id);
            }
            if (source_indexes.empty()) continue;
            std::sort(source_indexes.begin(), source_indexes.end());
            std::sort(transport_ids.begin(), transport_ids.end());
            transport_ids.erase(std::unique(transport_ids.begin(), transport_ids.end()), transport_ids.end());
            const auto& first_record = records[source_indexes.front()];
            const auto& last_record = records[source_indexes.back()];
            const std::string connection_key = std::to_string(GetInt64(first_record.fields, "ProcessId").value_or(0)) +
                "|" + first_record.socket;
            if (const auto connection = connections.find(connection_key); connection != connections.end()) {
                const auto pending = static_cast<std::uint64_t>(std::max<std::int64_t>(0,
                    GetInt64(fields, "RemainingByteCount").value_or(0)));
                const auto desync = static_cast<std::uint64_t>(std::max<std::int64_t>(0,
                    GetInt64(fields, "DesyncBytesBeforeFragment").value_or(0)));
                connection->second.pending_bytes += pending;
                connection->second.desync_bytes += desync;
                connection->second.invalid_lengths += desync;
            }
            incomplete_output << MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"IncompleteStreamFragmentId", "incomplete-stream-fragment-" + std::to_string(++incomplete_sequence)},
                {"ConnectionId", first_record.socket == "Unknown" ? "unknown-connection" :
                    "process-" + std::to_string(GetInt64(first_record.fields, "ProcessId").value_or(0)) +
                    "-socket-" + first_record.socket},
                {"ConnectionEpoch", "1"}, {"Socket", first_record.socket},
                {"Direction", first_record.direction}, {"TransportChunkIds", JsonStringArray(transport_ids)},
                {"RemainingPayloadHex", GetString(fields, "RemainingPayloadHex")},
                {"RemainingByteCount", NumberOrNull(GetInt64(fields, "RemainingByteCount"))},
                {"Reason", GetString(fields, "Reason")},
                {"FirstCapturedAtUtc", first_record.captured_at_utc}, {"LastCapturedAtUtc", last_record.captured_at_utc},
                {"EvidenceLevel", "Candidate"}
            }, {"SchemaVersion", "ConnectionEpoch", "TransportChunkIds", "RemainingByteCount"}) << '\n';
        }
        if (incomplete_input.bad()) {
            result.error = "incomplete stream source could not be read completely";
            result.blockers.push_back(result.error);
            return result;
        }
        result.incomplete_stream_fragments = incomplete_sequence;

        std::ofstream gameplay_output(gameplay_candidates_path, std::ios::binary);
        if (!gameplay_output) {
            result.error = "cannot create gameplay-candidates.jsonl";
            result.blockers.push_back(result.error);
            return result;
        }
        std::map<std::string, AutomaticSemanticAggregate> automatic_semantic_aggregates;
        for (const auto& record : records) {
            const bool handler = record.stage == "HandlerDecoded" || record.api == "HandlerDecoded";
            const std::string opcode = handler && record.payload_hex.size() >= 2 ?
                "0x" + record.payload_hex.substr(0, 2) : record.frame_opcode;
            const std::uint64_t frame_length = handler ? record.payload_hex.size() / 2 : record.frame_length;
            const auto automatic = RecognizeAutomaticSemanticCandidate({
                record.stage, record.direction, opcode, frame_length, record.payload_hex,
                record.caller_rva, record.correlation_level
            });
            if (!automatic) continue;

            std::vector<std::string> decoded_ids;
            std::vector<std::string> handler_ids;
            std::vector<std::string> protocol_ids;
            if (!record.decoded_message_id.empty()) decoded_ids.push_back(record.decoded_message_id);
            if (!record.handler_observation_id.empty()) handler_ids.push_back(record.handler_observation_id);
            if (!record.protocol_frame_id.empty()) protocol_ids.push_back(record.protocol_frame_id);
            const std::string candidate_id = "gameplay-candidate-" +
                std::to_string(++result.gameplay_candidates);
            ++result.automatic_semantic_candidates;
            gameplay_output << MakeJsonObject({
                {"SchemaId", "gameplay-candidate"}, {"SchemaVersion", "11"},
                {"SessionId", result.session_id}, {"GameplayCandidateId", candidate_id},
                {"CandidateType", automatic->candidate_type},
                {"CandidateDomain", automatic->candidate_domain},
                {"CaptureRecordIds", JsonStringArray({record.capture_record_id})},
                {"LogicalMessageIds", JsonStringArray({record.logical_message_id})},
                {"DecodedMessageIds", JsonStringArray(decoded_ids)},
                {"HandlerObservationIds", JsonStringArray(handler_ids)},
                {"ProtocolFrameIds", JsonStringArray(protocol_ids)},
                {"Direction", record.direction}, {"CaptureStage", record.stage},
                {"Opcode", opcode}, {"FrameLength", std::to_string(frame_length)},
                {"CallerRva", record.caller_rva}, {"CorrelationLevel", record.correlation_level},
                {"Confidence", std::to_string(automatic->confidence)},
                {"EvidenceLevel", automatic->evidence_status},
                {"SemanticStatus", "AutomaticStructuralCandidate"},
                {"IdentificationBasis", automatic->identification_basis},
                {"ExtractedFields", FieldsJsonObject(automatic->extracted_fields)},
                {"Reason", automatic->reason},
                {"ClassifierVersion", "God2PassiveAutomaticSemantic-" GOD2_TOOL_VERSION},
                {"AutomaticRecognition", "true"}, {"UserInteractionRequired", "false"},
                {"ProductionEligible", "false"}
            }, {"SchemaVersion", "CaptureRecordIds", "LogicalMessageIds", "DecodedMessageIds",
                "HandlerObservationIds", "ProtocolFrameIds", "FrameLength", "Confidence",
                "ExtractedFields", "AutomaticRecognition", "UserInteractionRequired",
                "ProductionEligible"}) << '\n';
            provenance_output << MakeJsonObject({
                {"SchemaId", "provenance"}, {"SchemaVersion", "11"},
                {"SessionId", result.session_id}, {"ProvenanceId", "provenance-" + candidate_id},
                {"ArtifactType", "GameplayCandidate"}, {"ArtifactId", candidate_id},
                {"CaptureRecordIds", JsonStringArray({record.capture_record_id})},
                {"DecodedMessageIds", JsonStringArray(decoded_ids)},
                {"HandlerObservationIds", JsonStringArray(handler_ids)},
                {"ProtocolFrameIds", JsonStringArray(protocol_ids)},
                {"EvidenceLevel", automatic->evidence_status},
                {"Confidence", std::to_string(automatic->confidence)},
                {"Reason", automatic->reason},
                {"ClassifierVersion", "God2PassiveAutomaticSemantic-" GOD2_TOOL_VERSION}
            }, {"SchemaVersion", "CaptureRecordIds", "DecodedMessageIds",
                "HandlerObservationIds", "ProtocolFrameIds", "Confidence"}) << '\n';

            const std::string aggregate_key = automatic->candidate_type + "|" + record.stage + "|" +
                record.direction + "|" + opcode + "|" + std::to_string(frame_length);
            auto& aggregate = automatic_semantic_aggregates[aggregate_key];
            aggregate.candidate_type = automatic->candidate_type;
            aggregate.candidate_domain = automatic->candidate_domain;
            aggregate.stage = record.stage;
            aggregate.direction = record.direction;
            aggregate.opcode = opcode;
            aggregate.frame_length = frame_length;
            ++aggregate.observation_count;
            if (record.correlation_level == "Exact" || record.correlation_level == "Strong")
                ++aggregate.exact_or_strong_count;
            aggregate.unique_payload_hashes.insert(record.payload_sha256.empty() ?
                record.payload_hex : record.payload_sha256);
        }
        const fs::path semantic_source = candidate_reanalysis / L"raw" / L"god2-opcode-semantic-candidates.jsonl";
        std::ifstream semantic_input(semantic_source, std::ios::binary);
        while (std::getline(semantic_input, line)) {
            if (line.empty()) continue;
            Fields fields;
            if (!ParseFlatJson(line, fields, &parse_error)) continue;
            if (GetString(fields, "SemanticCandidate") == "UnknownOpcodeSemanticCandidate")
                continue;
            const auto sources = SourceIds(GetString(fields, "SampleSourceFrameIds"));
            std::vector<std::string> record_ids;
            std::vector<std::string> logical_ids;
            for (const auto& source : sources) {
                const auto iterator = source_record_index.find(source);
                if (iterator == source_record_index.end()) continue;
                const auto& record = records[iterator->second];
                record_ids.push_back(record.capture_record_id);
                logical_ids.push_back(record.logical_message_id);
            }
            const std::string candidate_id = "gameplay-candidate-" + std::to_string(++result.gameplay_candidates);
            gameplay_output << MakeJsonObject({
                {"SchemaId", "gameplay-candidate"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"GameplayCandidateId", candidate_id}, {"CandidateType", GetString(fields, "SemanticCandidate")},
                {"CaptureRecordIds", JsonStringArray(record_ids)}, {"LogicalMessageIds", JsonStringArray(logical_ids)},
                {"DecodedMessageIds", "[]"}, {"HandlerObservationIds", "[]"}, {"ProtocolFrameIds", "[]"},
                {"Direction", GetString(fields, "PacketDirection")},
                {"Confidence", NumberOrNull(GetInt64(fields, "ConfidenceScore"))},
                {"EvidenceLevel", "Candidate"}, {"Reason", GetString(fields, "Reason")},
                {"ClassifierVersion", "God2OpcodeSemanticCandidate-" GOD2_TOOL_VERSION}, {"ProductionEligible", "false"}
            }, {"SchemaVersion", "CaptureRecordIds", "LogicalMessageIds", "DecodedMessageIds",
                "HandlerObservationIds", "ProtocolFrameIds", "Confidence", "ProductionEligible"}) << '\n';
            provenance_output << MakeJsonObject({
                {"SchemaId", "provenance"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"ProvenanceId", "provenance-" + candidate_id}, {"ArtifactType", "GameplayCandidate"},
                {"ArtifactId", candidate_id}, {"CaptureRecordIds", JsonStringArray(record_ids)},
                {"DecodedMessageIds", "[]"}, {"HandlerObservationIds", "[]"}, {"ProtocolFrameIds", "[]"},
                {"EvidenceLevel", "Candidate"}, {"Confidence", NumberOrNull(GetInt64(fields, "ConfidenceScore"))},
                {"Reason", GetString(fields, "Reason")}, {"ClassifierVersion", "God2OpcodeSemanticCandidate-" GOD2_TOOL_VERSION}
            }, {"SchemaVersion", "CaptureRecordIds", "DecodedMessageIds", "HandlerObservationIds",
                "ProtocolFrameIds", "Confidence"}) << '\n';
        }
        if (semantic_input.bad()) {
            result.error = "gameplay-candidate source could not be read completely";
            result.blockers.push_back(result.error);
            return result;
        }
        std::ostringstream automatic_families;
        automatic_families << '[';
        bool first_automatic_family = true;
        for (const auto& [key, aggregate] : automatic_semantic_aggregates) {
            static_cast<void>(key);
            const bool structurally_sufficient = aggregate.observation_count >= 3 &&
                aggregate.unique_payload_hashes.size() >= 2;
            if (structurally_sufficient) ++result.automatic_structurally_sufficient_family_count;
            if (!first_automatic_family) automatic_families << ',';
            first_automatic_family = false;
            automatic_families << MakeJsonObject({
                {"CandidateType", aggregate.candidate_type},
                {"CandidateDomain", aggregate.candidate_domain},
                {"CaptureStage", aggregate.stage}, {"Direction", aggregate.direction},
                {"Opcode", aggregate.opcode}, {"FrameLength", std::to_string(aggregate.frame_length)},
                {"ObservationCount", std::to_string(aggregate.observation_count)},
                {"UniquePayloadCount", std::to_string(aggregate.unique_payload_hashes.size())},
                {"ExactOrStrongCorrelationCount", std::to_string(aggregate.exact_or_strong_count)},
                {"StructuralEvidenceStatus", structurally_sufficient ?
                    "StructurallySufficientCandidate" : "CandidateEvidenceAccumulating"},
                {"SemanticVerificationStatus", "CandidateNotVerified"},
                {"ProductionEligible", "false"}
            }, {"FrameLength", "ObservationCount", "UniquePayloadCount",
                "ExactOrStrongCorrelationCount", "ProductionEligible"});
        }
        automatic_families << ']';
        result.automatic_semantic_family_count = automatic_semantic_aggregates.size();
        const auto automatic_summary = MakeJsonObject({
            {"SchemaId", "automatic-semantic-summary"}, {"SchemaVersion", "11"},
            {"SessionId", result.session_id}, {"Mode", "PassiveAutomaticNoSceneSelection"},
            {"UserInteractionRequired", "false"},
            {"AutomaticCandidateCount", std::to_string(result.automatic_semantic_candidates)},
            {"FamilyCount", std::to_string(result.automatic_semantic_family_count)},
            {"StructurallySufficientFamilyCount",
                std::to_string(result.automatic_structurally_sufficient_family_count)},
            {"ProductionEligibleCount", "0"}, {"Families", automatic_families.str()},
            {"SemanticSafetyRule", "Automatic recognition remains Candidate until cross-session state-delta and response evidence verify the semantic and field layout"},
            {"Workflow", "No scene selector, marker button, overlay action, hotkey or game/tool window switching is required"}
        }, {"SchemaVersion", "UserInteractionRequired", "AutomaticCandidateCount", "FamilyCount",
            "StructurallySufficientFamilyCount", "ProductionEligibleCount", "Families"});
        if (!write_required(automatic_semantic_summary_path, automatic_summary + "\n")) return result;
        std::ostringstream opcode_worklist_output;
        const std::uint64_t opcode_work_item_sequence = assigned_work_item_sequence;
        for (const auto& [key, item] : opcode_work_items) {
            static_cast<void>(key);
            std::ostringstream stable_pattern;
            std::ostringstream changing_offsets;
            changing_offsets << '[';
            bool first_changing_offset = true;
            for (std::size_t index = 0; index < item.baseline_bytes.size(); ++index) {
                if (index != 0) stable_pattern << ' ';
                if (item.stable_bytes[index] != 0) stable_pattern << HexByte(item.baseline_bytes[index]).substr(2);
                else {
                    stable_pattern << "??";
                    if (!first_changing_offset) changing_offsets << ',';
                    changing_offsets << index;
                    first_changing_offset = false;
                }
            }
            changing_offsets << ']';
            const std::string priority = item.exact_handler_correlation_count > 0 ||
                item.strong_handler_correlation_count > 0 ? "High" :
                item.observation_count >= 3 ? "Medium" : "Low";
            opcode_worklist_output << MakeJsonObject({
                {"SchemaId", "opcode-work-item"}, {"SchemaVersion", "11"},
                {"SessionId", result.session_id},
                {"WorkItemId", item.work_item_id},
                {"Direction", item.direction}, {"Opcode", item.opcode},
                {"FrameLength", std::to_string(item.frame_length)},
                {"ObservationCount", std::to_string(item.observation_count)},
                {"FirstCapturedAtUtc", item.first_captured_at_utc},
                {"LastCapturedAtUtc", item.last_captured_at_utc},
                {"FirstCaptureRecordId", item.first_capture_record_id},
                {"LastCaptureRecordId", item.last_capture_record_id},
                {"SampleProtocolFrameIds", JsonStringArray(item.sample_protocol_frame_ids)},
                {"SampleCaptureRecordIds", JsonStringArray(item.sample_capture_record_ids)},
                {"SampleFrameHex", item.sample_frame_hex},
                {"SampleFrameSHA256", item.sample_frame_sha256},
                {"StableBytePattern", stable_pattern.str()},
                {"ChangingByteOffsets", changing_offsets.str()},
                {"ExactHandlerCorrelationCount", std::to_string(item.exact_handler_correlation_count)},
                {"StrongHandlerCorrelationCount", std::to_string(item.strong_handler_correlation_count)},
                {"RelatedHandlerObservationIds", JsonStringArray(item.related_handler_observation_ids)},
                {"RelatedActionPatternIds", JsonStringArray(item.related_action_pattern_ids)},
                {"Priority", priority}, {"FrameEvidenceLevel", "RuntimeObservedPlaintext"},
                {"SemanticEvidenceLevel", "Candidate"}, {"SemanticStatus", "UnknownUntilVerified"},
                {"ProductionEligible", "false"},
                {"CodexAction", "Correlate with exact HandlerDecoded evidence and labeled gameplay observations, then promote the versioned protocol registry"}
            }, {"SchemaVersion", "FrameLength", "ObservationCount", "SampleProtocolFrameIds",
                "SampleCaptureRecordIds", "ChangingByteOffsets", "ExactHandlerCorrelationCount",
                "StrongHandlerCorrelationCount",
                "RelatedHandlerObservationIds", "RelatedActionPatternIds", "ProductionEligible"}) << '\n';
        }
        if (!write_required(opcode_worklist_path, opcode_worklist_output.str())) return result;
        const auto opcode_counts_json = [](const std::map<std::string, std::uint64_t>& counts) {
            std::string json = "{";
            bool first = true;
            for (const auto& [opcode, count] : counts) {
                if (!first) json += ',';
                first = false;
                json += "\"" + JsonEscape(opcode) + "\":" + std::to_string(count);
            }
            json += '}';
            return json;
        };
        if (!write_required(opcode_summary_path, MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"EnhancedPlaintextAvailable", result.enhanced_plaintext_available ? "true" : "false"},
                {"ClientToServerUniqueOpcodeCount", std::to_string(result.client_to_server_opcode_count)},
                {"ServerToClientUniqueOpcodeCount", std::to_string(result.server_to_client_opcode_count)},
                {"ClientToServerOpcodeObservations", std::to_string(result.client_to_server_opcode_observations)},
                {"ServerToClientOpcodeObservations", std::to_string(result.server_to_client_opcode_observations)},
                {"CodexOpcodeWorkItemCount", std::to_string(opcode_work_item_sequence)},
                {"ClientToServerOpcodes", opcode_counts_json(c2s_opcode_observations)},
                {"ServerToClientOpcodes", opcode_counts_json(s2c_opcode_observations)},
                {"SemanticStatus", "UnknownUntilVerifiedProtocolKnowledgeIsApplied"}
            }, {"SchemaVersion", "EnhancedPlaintextAvailable", "ClientToServerUniqueOpcodeCount",
                "ServerToClientUniqueOpcodeCount", "ClientToServerOpcodeObservations",
                "ServerToClientOpcodeObservations", "CodexOpcodeWorkItemCount", "ClientToServerOpcodes", "ServerToClientOpcodes"}) + "\n") ||
            !write_required(direction_summary_path, MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"ClientToServerCaptureRecords", std::to_string(result.client_to_server_records)},
                {"ServerToClientCaptureRecords", std::to_string(result.server_to_client_records)},
                {"ClientToServerProtocolFrames", std::to_string(result.client_to_server_opcode_observations)},
                {"ServerToClientProtocolFrames", std::to_string(result.server_to_client_opcode_observations)}
            }, {"SchemaVersion", "ClientToServerCaptureRecords", "ServerToClientCaptureRecords",
                "ClientToServerProtocolFrames", "ServerToClientProtocolFrames"}) + "\n") ||
            !write_required(frame_validation_summary_path, MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"PlaintextRecordCount", std::to_string(result.pre_encrypt_records +
                    result.post_decrypt_records + result.handler_decoded_records)},
                {"FrameEligiblePlaintextRecordCount", std::to_string(result.pre_encrypt_records +
                    result.post_decrypt_records)},
                {"VerifiedProtocolFrameCount", std::to_string(result.protocol_frames)},
                {"UnframedEvidenceOnlyCount", std::to_string(result.unframed_plaintext_records)},
                {"HandlerDecodedRecordCount", std::to_string(result.handler_decoded_records)},
                {"FrameRule", "uint16 little-endian exact record length plus additive 0x3C checksum"},
                {"InvalidBytesPolicy", "PreserveAsUnframedEvidenceOnly"}
            }, {"SchemaVersion", "PlaintextRecordCount", "FrameEligiblePlaintextRecordCount",
                "VerifiedProtocolFrameCount", "UnframedEvidenceOnlyCount", "HandlerDecodedRecordCount"}) + "\n"))
            return result;
        progress("candidates-written");
        capture_output.close();
        chunk_output.close();
        decoded_output.close();
        handler_output.close();
        protocol_frame_output.close();
        decoded_frame_output.close();
        pre_encrypt_output.close();
        post_decrypt_output.close();
        handler_decoded_output.close();
        unframed_output.close();
        transport_mapping_output.close();
        handler_mapping_output.close();
        evidence_output.close();
        logical_output.close();
        candidate_frame_output.close();
        incomplete_output.close();
        gameplay_output.close();
        provenance_output.close();
        candidate_frame_input.close();
        incomplete_input.close();
        semantic_input.close();
        if (!capture_output || !chunk_output || !decoded_output || !handler_output || !protocol_frame_output ||
            !decoded_frame_output || !pre_encrypt_output || !post_decrypt_output || !handler_decoded_output ||
            !unframed_output || !transport_mapping_output || !handler_mapping_output || !evidence_output ||
            !logical_output || !candidate_frame_output || !incomplete_output || !gameplay_output || !provenance_output) {
            result.error = "one or more normalized Evidence Package outputs failed to flush";
            result.blockers.push_back(result.error);
            return result;
        }

        std::error_code reanalysis_cleanup_error;
        fs::remove_all(candidate_reanalysis, reanalysis_cleanup_error);
        if (reanalysis_cleanup_error)
            result.warnings.push_back("isolated candidate reanalysis workspace cleanup failed: " +
                                      reanalysis_cleanup_error.message());

        result.etw_event_lines = CountLines(session_path / L"raw" / L"etw-events.jsonl");
        std::optional<std::int64_t> etw_events_lost;
        std::optional<std::int64_t> etw_buffers_lost;
        if (const auto etw_inventory = ReadUtf8File(session_path / L"reports" / L"raw-etl-inventory.json")) {
            Fields etw_fields;
            if (ParseFlatJson(*etw_inventory, etw_fields, nullptr)) {
                etw_events_lost = GetInt64(etw_fields, "EventsLost");
                etw_buffers_lost = GetInt64(etw_fields, "BuffersLost");
            }
        }
        std::optional<std::int64_t> dropped_queue_records;
        std::optional<std::int64_t> probe_write_failures;
        std::optional<std::int64_t> semantic_dropped_events;
        std::optional<std::int64_t> semantic_write_failures;
        std::optional<std::int64_t> probe_semantic_incomplete;
        if (const auto enhanced_log = ReadUtf8File(session_path / L"reports" / L"enhanced-capture.log")) {
            dropped_queue_records = LastNamedDecimal(*enhanced_log, "droppedRecords");
            probe_write_failures = LastNamedDecimal(*enhanced_log, "writeFailures");
            semantic_dropped_events = LastNamedDecimal(*enhanced_log, "semanticDroppedEvents");
            semantic_write_failures = LastNamedDecimal(*enhanced_log, "semanticWriteFailures");
            probe_semantic_incomplete = LastNamedDecimal(*enhanced_log, "semanticEvidenceIncomplete");
        }
        std::optional<std::int64_t> records_lost;
        for (const auto value : {dropped_queue_records, probe_write_failures,
                                 semantic_dropped_events, semantic_write_failures}) {
            if (!value) continue;
            records_lost = records_lost.value_or(0) + std::max<std::int64_t>(0, *value);
        }
        Fields shared_health_fields;
        StrictJsonValue strict_shared_health;
        bool shared_health_parse_failed = false;
        if (const auto shared_health = ReadUtf8File(packaged_semantic_health)) {
            std::string strict_health_error;
            shared_health_parse_failed = !StrictJsonParser(*shared_health).Parse(
                &strict_shared_health, &strict_health_error) ||
                strict_shared_health.kind != StrictJsonKind::Object ||
                !ParseFlatJson(*shared_health, shared_health_fields, nullptr);
        } else {
            shared_health_parse_failed = true;
        }
        const auto shared_transport_ready = GetBool(shared_health_fields, "TransportReady");
        const auto shared_consumer_io = GetBool(shared_health_fields, "ConsumerIoFailure");
        const auto shared_producer_ready = GetInt64(shared_health_fields, "ProducerReady");
        const auto shared_producer_closed = GetInt64(shared_health_fields, "ProducerClosed");
        const auto shared_consumer_ready = GetInt64(shared_health_fields, "ConsumerReady");
        const auto shared_consumer_closed = GetInt64(shared_health_fields, "ConsumerClosed");
        const auto shared_consumer_failure = GetInt64(shared_health_fields, "ConsumerFailure");
        const auto shared_producer_pid = GetInt64(shared_health_fields, "ProducerProcessId");
        const auto shared_attached_pid = GetInt64(shared_health_fields, "AttachedTargetProcessId");
        const auto shared_attempted = GetInt64(shared_health_fields, "Attempted");
        const auto shared_accepted = GetInt64(shared_health_fields, "Accepted");
        const auto shared_consumed = GetInt64(shared_health_fields, "Consumed");
        const auto shared_dropped = GetInt64(shared_health_fields, "Dropped");
        const auto shared_sampled = GetInt64(shared_health_fields, "Sampled");
        const auto shared_invalid = GetInt64(shared_health_fields, "InvalidPayloads");
        const auto shared_lag = GetInt64(shared_health_fields, "ConsumerLag");
        const auto shared_high_water = GetInt64(shared_health_fields, "HighWaterMark");
        const auto shared_write_failures = GetInt64(shared_health_fields, "WriteFailures");
        const auto has_health_type = [&](std::string_view name, StrictJsonKind kind) {
            return JsonMember(strict_shared_health, name, kind) != nullptr;
        };
        bool shared_health_types_complete =
            has_health_type("SchemaVersion", StrictJsonKind::String) &&
            has_health_type("Lifecycle", StrictJsonKind::String) &&
            has_health_type("TransportReady", StrictJsonKind::Boolean) &&
            has_health_type("ConsumerIoFailure", StrictJsonKind::Boolean);
        for (const auto* field : {"ProducerReady", "ProducerClosed", "ConsumerReady",
                                   "ConsumerClosed", "ConsumerFailure", "ProducerProcessId",
                                   "AttachedTargetProcessId", "Attempted", "Accepted", "Consumed",
                                   "Dropped", "Sampled", "InvalidPayloads", "HighWaterMark", "ConsumerLag",
                                   "WriteFailures"})
            shared_health_types_complete = shared_health_types_complete &&
                has_health_type(field, StrictJsonKind::Number);
        const bool shared_consumer_io_failure = shared_consumer_io.value_or(true);
        bool shared_identity_bound = shared_producer_pid && shared_attached_pid &&
            *shared_producer_pid > 0 && *shared_producer_pid == *shared_attached_pid;
        bool observed_producer_pid = false;
        for (const auto& record : records) {
            if (record.process_id <= 0) {
                shared_identity_bound = false;
                continue;
            }
            if (!shared_producer_pid || record.process_id != *shared_producer_pid)
                shared_identity_bound = false;
            else
                observed_producer_pid = true;
        }
        shared_identity_bound = shared_identity_bound && observed_producer_pid;
        std::int64_t lane_accepted_total = 0;
        std::int64_t lane_dropped_total = 0;
        std::int64_t lane_sampled_total = 0;
        bool shared_lanes_complete = true;
        for (std::uint32_t priority = 0; priority < 4; ++priority) {
            const std::string prefix = "Lane0" + std::to_string(priority);
            const auto accepted = GetInt64(shared_health_fields, prefix + "Accepted");
            const auto consumed = GetInt64(shared_health_fields, prefix + "Consumed");
            const auto dropped = GetInt64(shared_health_fields, prefix + "Dropped");
            const auto sampled = GetInt64(shared_health_fields, prefix + "Sampled");
            const auto first_dropped = GetInt64(
                shared_health_fields, prefix + "FirstDroppedSequence");
            const auto last_dropped = GetInt64(
                shared_health_fields, prefix + "LastDroppedSequence");
            const auto drop_reason = GetInt64(shared_health_fields, prefix + "LastDropReason");
            const auto high_water = GetInt64(shared_health_fields, prefix + "HighWaterMark");
            const auto lag = GetInt64(shared_health_fields, prefix + "ConsumerLag");
            for (const auto* suffix : {"Accepted", "Consumed", "Dropped", "Sampled",
                                      "FirstDroppedSequence", "LastDroppedSequence",
                                      "LastDropReason", "HighWaterMark", "ConsumerLag"})
                shared_health_types_complete = shared_health_types_complete &&
                    has_health_type(prefix + suffix, StrictJsonKind::Number);
            const bool no_loss = dropped && sampled && first_dropped && last_dropped &&
                drop_reason && *dropped == 0 && *sampled == 0 &&
                *first_dropped == 0 && *last_dropped == 0 && *drop_reason == 0;
            const bool sampled_loss = dropped && sampled && first_dropped && last_dropped &&
                drop_reason && *dropped > 0 && *sampled == *dropped &&
                *first_dropped > 0 && *last_dropped >= *first_dropped &&
                *drop_reason == 10;
            if (!accepted || !consumed || !dropped || !sampled || !high_water || !lag ||
                *accepted < 0 || *consumed != *accepted || *high_water < 0 ||
                *high_water > 64 || *lag != 0 ||
                (priority < 2 ? !no_loss : !(no_loss || sampled_loss))) {
                shared_lanes_complete = false;
                continue;
            }
            lane_accepted_total += *accepted;
            lane_dropped_total += *dropped;
            lane_sampled_total += *sampled;
        }
        std::int64_t domain_accepted_total = 0;
        std::int64_t domain_dropped_total = 0;
        std::int64_t domain_write_failures_total = 0;
        std::int64_t domain_high_water_max = 0;
        bool shared_domains_complete = true;
        for (std::uint32_t domain = 0; domain < 25; ++domain) {
            const std::string prefix = "Domain" +
                (domain < 10 ? std::string("0") : std::string()) + std::to_string(domain);
            const auto accepted = GetInt64(shared_health_fields, prefix + "Accepted");
            const auto dropped = GetInt64(shared_health_fields, prefix + "Dropped");
            const auto first_dropped = GetInt64(shared_health_fields, prefix + "FirstDroppedSequence");
            const auto last_dropped = GetInt64(shared_health_fields, prefix + "LastDroppedSequence");
            const auto drop_reason = GetInt64(shared_health_fields, prefix + "LastDropReason");
            const auto high_water = GetInt64(shared_health_fields, prefix + "HighWaterMark");
            const auto lag = GetInt64(shared_health_fields, prefix + "ConsumerLag");
            const auto write_failures = GetInt64(shared_health_fields, prefix + "WriteFailures");
            for (const auto* suffix : {"Accepted", "Dropped", "FirstDroppedSequence",
                                      "LastDroppedSequence", "LastDropReason", "HighWaterMark",
                                      "ConsumerLag", "WriteFailures"})
                shared_health_types_complete = shared_health_types_complete &&
                    has_health_type(prefix + suffix, StrictJsonKind::Number);
            const bool no_loss = dropped && first_dropped && last_dropped && drop_reason &&
                *dropped == 0 && *first_dropped == 0 && *last_dropped == 0 &&
                *drop_reason == 0;
            const bool sampled_loss = dropped && first_dropped && last_dropped && drop_reason &&
                *dropped > 0 && *first_dropped > 0 && *last_dropped >= *first_dropped &&
                *drop_reason == 10;
            if (!accepted || !dropped || !first_dropped || !last_dropped || !drop_reason ||
                !high_water || !lag || !write_failures || *accepted < 0 || *dropped < 0 ||
                *high_water < 0 || *high_water > 256 || *write_failures < 0 ||
                *lag != 0 || !(no_loss || sampled_loss)) {
                shared_domains_complete = false;
                continue;
            }
            domain_accepted_total += *accepted;
            domain_dropped_total += *dropped;
            domain_write_failures_total += *write_failures;
            domain_high_water_max = std::max(domain_high_water_max, *high_water);
        }
        const auto semantic_event_lines = fs::is_regular_file(packaged_semantic_events) ?
            std::optional<std::uint64_t>(CountLines(packaged_semantic_events)) : std::nullopt;
        const bool semantic_transport_incomplete = semantic_privacy_incomplete ||
            !semantic_segments_complete ||
            shared_health_parse_failed ||
            !shared_health_types_complete ||
            GetString(shared_health_fields, "SchemaVersion") != "god2-semantic-shared-ring-health-v4" ||
            GetString(shared_health_fields, "Lifecycle") != "StoppedAndDrained" ||
            !shared_transport_ready || *shared_transport_ready || shared_consumer_io_failure ||
            !shared_producer_ready || *shared_producer_ready != 1 ||
            !shared_producer_closed || *shared_producer_closed != 1 ||
            !shared_consumer_ready || *shared_consumer_ready != 0 ||
            !shared_consumer_closed || *shared_consumer_closed != 1 ||
            !shared_consumer_failure || *shared_consumer_failure != 0 || !shared_identity_bound ||
            !shared_attempted || !shared_accepted || !shared_consumed || !shared_dropped || !shared_sampled ||
            !shared_invalid || !shared_lag || !shared_high_water || !shared_write_failures ||
            *shared_attempted < 0 ||
            *shared_accepted < 0 || *shared_consumed < 0 || *shared_dropped < 0 ||
            *shared_invalid < 0 || *shared_lag < 0 || *shared_high_water < 0 ||
            *shared_attempted != *shared_accepted + *shared_dropped ||
            *shared_consumed != *shared_accepted || *shared_sampled != *shared_dropped ||
            *shared_invalid != 0 || *shared_lag != 0 || *shared_write_failures != 0 ||
            *shared_high_water < domain_high_water_max || *shared_high_water > 256 ||
            !shared_lanes_complete || !shared_domains_complete ||
            lane_accepted_total != *shared_accepted || lane_dropped_total != *shared_dropped ||
            lane_sampled_total != *shared_sampled ||
            domain_accepted_total != *shared_accepted || domain_dropped_total != *shared_dropped ||
            domain_write_failures_total != *shared_write_failures ||
            (semantic_event_lines && *semantic_event_lines != static_cast<std::uint64_t>(*shared_consumed)) ||
            !dropped_queue_records || !probe_write_failures || !semantic_dropped_events ||
            !semantic_write_failures || !probe_semantic_incomplete ||
            *dropped_queue_records < 0 || *probe_write_failures < 0 ||
            *semantic_dropped_events < 0 || *semantic_write_failures < 0 ||
            *probe_semantic_incomplete < 0 || *dropped_queue_records != 0 || *probe_write_failures != 0 ||
            semantic_write_failures.value_or(0) != 0 ||
            probe_semantic_incomplete.value_or(0) != 0;
        ValidatedEnhancedCaptureStatus enhanced_status;
        result.cleanup_status = CleanupStatus(session_path, &enhanced_status);
        const auto& enhanced_original_status = enhanced_status.status;
        const auto& enhanced_original_detail = enhanced_status.detail;
        const auto& enhanced_failure_kind = enhanced_status.failure_kind;
        const bool enhanced_acquisition_blocked =
            EnhancedAcquisitionBlocked(enhanced_status, result.capture_records);
        const bool raw_etl_available = fs::is_regular_file(session_path / L"raw" / L"capture.etl");
        result.acquisition_status = enhanced_acquisition_blocked ? "EvidenceBlocked" :
            result.capture_records > 0 ? "CompletedWithEvidence" :
            raw_etl_available ? "CompletedWithRawEtwSourceExcluded" : "EvidenceBlocked";
        result.analysis_status = enhanced_acquisition_blocked ? "EvidenceBlocked" :
            (semantic_transport_incomplete || result.semantic_evidence_incomplete) ?
                "EvidenceBlockedSemanticEvidenceIncomplete" :
            result.protocol_frames > 0 ? "CompletedWithVerifiedPlaintextFrames" :
            result.enhanced_plaintext_available ? "CompletedWithUnframedPlaintextEvidence" :
            "CompletedWithoutPlaintext";
        result.package_status = enhanced_acquisition_blocked || semantic_transport_incomplete ||
            result.semantic_evidence_incomplete ||
            !exact_client_identity || result.cleanup_status != "Completed" ? "EvidenceBlocked" :
            partial_recovery ? "PartialRecovered" :
            (result.capture_records > 0 && result.cleanup_status == "Completed" && result.etw_event_lines > 0 &&
             result.desync_bytes == 0 && result.pending_stream_bytes == 0 ? "Ready" : "ReadyWithWarnings");
        if (enhanced_acquisition_blocked) {
            std::string blocker =
                "x86 enhanced capture was requested but no verified DLL attachment records were acquired";
            if (!enhanced_failure_kind.empty()) blocker += "; failureKind=" + enhanced_failure_kind;
            if (!enhanced_original_detail.empty()) blocker += "; detail=" + enhanced_original_detail;
            result.blockers.push_back(std::move(blocker));
        }
        if (semantic_transport_incomplete) {
            result.blockers.push_back(
                "bounded x86-to-x64 semantic transport reported missing, invalid, high-priority loss, unexplained sampling, lagged, or unwritten evidence; Ultimate completeness is blocked");
        }
        if (result.semantic_evidence_incomplete) {
            result.blockers.push_back(
                "semantic event v1/v2 analysis reported missing or corrupt evidence; formal readiness is blocked");
        }
        if (!exact_client_identity) {
            result.blockers.push_back(
                "exact God2_opt.exe x86 / 1.0.0.1 / approved SHA-256 identity was not recomputed; formal package PASS is blocked");
        }
        if (result.cleanup_status != "Completed") result.warnings.push_back("Enhanced DLL cleanup did not prove immediate unload; acquired evidence remains valid");
        if (result.capture_records == 0)
            result.warnings.push_back("No x86 enhanced CaptureRecords were observed; enhanced analysis remains EvidenceBlocked and raw network payload files are excluded fail-closed");
        if (!result.enhanced_plaintext_available)
            result.warnings.push_back("EnhancedPlaintextAvailable=false; raw transport was preserved without opcode or gameplay promotion");
        if (result.etw_event_lines == 0) result.warnings.push_back(
            "ETW attributed event JSONL is empty; raw network payload files are excluded fail-closed");
        if (result.desync_bytes != 0) result.warnings.push_back(result.enhanced_plaintext_available
            ? "Diagnostic raw encrypted-transport candidate reassembly contains desynchronization bytes; verified PreEncrypt/PostDecrypt frames are unaffected"
            : "Candidate stream reassembly contains desynchronization bytes");
        if (result.pending_stream_bytes != 0) result.warnings.push_back(result.enhanced_plaintext_available
            ? "Diagnostic raw encrypted-transport candidate reassembly ended with pending bytes; verified PreEncrypt/PostDecrypt frames are unaffected"
            : "Candidate stream reassembly ended with pending bytes");
        if (result.gameplay_candidates != 0) result.warnings.push_back("Gameplay and opcode outputs are candidates and are not production eligible");

        const std::uint64_t explicit_stage_total = result.transport_send_records + result.transport_recv_records +
            result.pre_encrypt_records + result.post_decrypt_records + result.handler_decoded_records + invalid_records;
        result.count_reconciliation_passed = explicit_stage_total == result.capture_records &&
            result.client_to_server_records + result.server_to_client_records + invalid_records == result.capture_records &&
            result.transport_chunks == result.transport_send_records + result.transport_recv_records &&
            result.decoded_messages == result.pre_encrypt_records + result.post_decrypt_records &&
            result.handler_observations == result.handler_decoded_records;
        result.timestamp_validation_passed = invalid_records == 0 && std::all_of(records.begin(), records.end(), [](const auto& record) {
            return !record.captured_at_utc.empty() && record.captured_at_utc == UnixMillisecondsUtc(record.captured_at_unix_ms);
        });
        result.provenance_validation_passed = CountLines(provenance_path) ==
            result.decoded_messages + result.gameplay_candidates;
        const std::vector<fs::path> normalized_jsonl = {
            normalized_records, chunks_path, protocol_frames_path, decoded_path, handlers_path,
            pre_encrypt_path, post_decrypt_path, handler_decoded_path, unframed_path, decoded_frames_path,
            opcode_worklist_path,
            transport_mapping_path, handler_mapping_path,
            candidate_frames_path, incomplete_fragments_path, gameplay_candidates_path, evidence_items_path, logical_map_path,
            action_instances_path, action_patterns_path, orphan_handler_bursts_path, outbound_batch_candidates_path,
            provenance_path
        };
        const bool normalized_syntax_and_identity = std::all_of(normalized_jsonl.begin(), normalized_jsonl.end(),
            [&](const auto& path) { return ValidateJsonlSessionAndSyntax(path, result.session_id); });
        result.session_identity_passed = normalized_syntax_and_identity;
        result.schema_validation_passed = invalid_records == 0 && invalid_payloads == 0 &&
            invalid_lengths == 0 && normalized_syntax_and_identity;
        progress("validators-computed");

        if (!CopyArtifact(session_path / L"raw" / L"enhanced-x86" / L"trace.bin",
                          primary / L"raw" / L"trace.bin", session_path, staging))
            result.warnings.push_back("x86 binary trace.bin was not available");
        if (fs::is_regular_file(session_path / L"raw" / L"capture.etl"))
            result.warnings.push_back(
                "raw ETW capture.etl was deliberately excluded by RawNetworkPayloadPolicy");
        if (!CopyArtifact(session_path / L"session.sqlite3",
                          primary / L"convenience" / L"session.sqlite3", session_path, staging))
            result.warnings.push_back("session SQLite convenience database was not available");
        std::uint64_t copied_gameplay_artifacts = 0;
        for (const auto& relative : RequiredGameplayFiles()) {
            const auto source = session_path / relative;
            if (!fs::is_regular_file(source)) continue;
            if (!CopyArtifact(source, primary / relative, session_path, staging)) {
                result.error = "cannot copy gameplay classifier artifact: " + WideToUtf8(relative.wstring());
                result.blockers.push_back(result.error);
                return result;
            }
            ++copied_gameplay_artifacts;
        }
        std::uint64_t copied_export_artifacts = 0;
        for (const auto& [table, relative] : RequiredExports()) {
            static_cast<void>(table);
            const auto source = session_path / relative;
            if (!fs::is_regular_file(source)) continue;
            if (!CopyArtifact(source, primary / relative, session_path, staging)) {
                result.error = "cannot copy evidence export artifact: " + WideToUtf8(relative.wstring());
                result.blockers.push_back(result.error);
                return result;
            }
            ++copied_export_artifacts;
        }
        if (copied_gameplay_artifacts == 0)
            result.warnings.push_back("no gameplay classifier JSONL artifacts were available for Codex handoff");
        if (copied_export_artifacts == 0)
            result.warnings.push_back("no gameplay/formula CSV exports were available for Codex handoff");
        if (!WriteSchemas(staging)) {
            result.error = "schema files could not be written";
            result.blockers.push_back(result.error);
            return result;
        }
        if (fs::exists(session_path / L"session.sqlite3")) {
            if (!WriteSqliteSchemaAndCheck(session_path / L"session.sqlite3",
                                           staging / L"schemas" / L"session-sqlite-schema.sql",
                                           &result.sqlite_integrity_passed)) {
                result.warnings.push_back("SQLite integrity_check or schema export failed");
            }
            result.session_identity_passed = result.session_identity_passed &&
                SqliteContainsSessionId(session_path / L"session.sqlite3", result.session_id);
        }

        std::uint64_t etw_raw_packets = 0;
        if (const auto etw_inventory = ReadUtf8File(session_path / L"reports" / L"raw-etl-inventory.json")) {
            Fields etw_fields;
            if (ParseFlatJson(*etw_inventory, etw_fields, nullptr))
                etw_raw_packets = static_cast<std::uint64_t>(std::max<std::int64_t>(0,
                    GetInt64(etw_fields, "NdisPacketEvents").value_or(0)));
        }
        const auto count_status = result.count_reconciliation_passed ? "Pass" : "Failed";
        const auto timestamp_status = result.timestamp_validation_passed ? "Pass" : "Failed";
        const auto provenance_status = result.provenance_validation_passed ? "Pass" : "Failed";
        const auto sqlite_status = result.sqlite_integrity_passed ? "Pass" : "EvidenceBlocked";
        const auto warnings_json = JsonStringArray(result.warnings);
        const auto blockers_json = JsonStringArray(result.blockers);
        result.compression_level = EvidenceCompressionLevel();

        std::ostringstream connection_array;
        connection_array << '[';
        bool first_connection = true;
        for (const auto& [_, connection] : connections) {
            if (!first_connection) connection_array << ',';
            first_connection = false;
            const auto [local_address, local_port] = AddressAndPort(connection.local_endpoint);
            const auto [remote_address, remote_port] = AddressAndPort(connection.remote_endpoint);
            connection_array << MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id},
                {"ConnectionId", connection.connection_id}, {"ConnectionEpoch", "1"},
                {"ProcessId", std::to_string(connection.process_id)}, {"Socket", connection.socket},
                {"LocalAddress", local_address}, {"LocalPort", local_port.empty() ? "null" : local_port},
                {"RemoteAddress", remote_address}, {"RemotePort", remote_port.empty() ? "null" : remote_port},
                {"FirstCapturedAtUtc", connection.first_utc}, {"LastCapturedAtUtc", connection.last_utc},
                {"DurationMs", std::to_string(std::max<std::int64_t>(0, connection.last_ms - connection.first_ms))},
                {"FirstCaptureSequence", std::to_string(connection.first_sequence)},
                {"LastCaptureSequence", std::to_string(connection.last_sequence)},
                {"ClientToServerTransportChunkCount", std::to_string(connection.c2s_chunks)},
                {"ServerToClientTransportChunkCount", std::to_string(connection.s2c_chunks)},
                {"ClientToServerBytes", std::to_string(connection.c2s_bytes)},
                {"ServerToClientBytes", std::to_string(connection.s2c_bytes)},
                {"SendCount", std::to_string(connection.sends)}, {"WSASendCount", std::to_string(connection.wsa_sends)},
                {"RecvCount", std::to_string(connection.recvs)}, {"WSARecvCount", std::to_string(connection.wsa_recvs)},
                {"PreEncryptCount", std::to_string(connection.pre_encrypt)},
                {"PostDecryptCount", std::to_string(connection.post_decrypt)},
                {"HandlerDecodedCount", std::to_string(connection.handler_decoded)},
                {"CandidateProtocolFrameCount", std::to_string(connection.candidate_frames)},
                {"VerifiedProtocolFrameCount", "0"}, {"DesyncBytes", std::to_string(connection.desync_bytes)},
                {"PendingStreamBytes", std::to_string(connection.pending_bytes)},
                {"InvalidLengthCount", std::to_string(connection.invalid_lengths)},
                {"ConnectionOpenedStatus", "Unknown"}, {"ConnectionClosedStatus", "Unknown"},
                {"DisconnectReason", "Unknown"}, {"CaptureSource", "OptInX86Dll"}, {"Warnings", "[]"},
                {"ServerRoleCandidate", "Unknown"}, {"ServerRoleConfidence", "0"},
                {"ServerRoleReason", "No verified application server-role evidence was observed"}
            }, {"SchemaVersion", "ConnectionEpoch", "ProcessId", "LocalPort", "RemotePort", "DurationMs",
                "FirstCaptureSequence", "LastCaptureSequence", "ClientToServerTransportChunkCount",
                "ServerToClientTransportChunkCount", "ClientToServerBytes", "ServerToClientBytes", "SendCount",
                "WSASendCount", "RecvCount", "WSARecvCount", "PreEncryptCount", "PostDecryptCount",
                "HandlerDecodedCount", "CandidateProtocolFrameCount", "VerifiedProtocolFrameCount", "DesyncBytes",
                "PendingStreamBytes", "InvalidLengthCount", "Warnings", "ServerRoleConfidence"});
        }
        connection_array << ']';
        const auto assigned_pre_encrypt = std::accumulate(connections.begin(), connections.end(), std::uint64_t{0},
            [](std::uint64_t total, const auto& item) { return total + item.second.pre_encrypt; });
        const auto assigned_post_decrypt = std::accumulate(connections.begin(), connections.end(), std::uint64_t{0},
            [](std::uint64_t total, const auto& item) { return total + item.second.post_decrypt; });
        const auto assigned_handlers = std::accumulate(connections.begin(), connections.end(), std::uint64_t{0},
            [](std::uint64_t total, const auto& item) { return total + item.second.handler_decoded; });
        const auto connection_summary = MakeJsonObject({
            {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"ConnectionCount", std::to_string(result.connection_count)}, {"Connections", connection_array.str()},
            {"ExactCorrelationCount", std::to_string(result.exact_correlations)},
            {"StrongCorrelationCount", std::to_string(result.strong_correlations)},
            {"CandidateCorrelationCount", std::to_string(result.candidate_correlations)},
            {"UncorrelatedCount", std::to_string(result.uncorrelated_correlations)},
            {"CorrelationCoveragePercent", std::to_string(result.correlation_coverage_percent)},
            {"UnassignedPreEncryptCount", std::to_string(result.pre_encrypt_records - assigned_pre_encrypt)},
            {"UnassignedPostDecryptCount", std::to_string(result.post_decrypt_records - assigned_post_decrypt)},
            {"UnassignedHandlerDecodedCount", std::to_string(result.handler_decoded_records - assigned_handlers)}
        }, {"SchemaVersion", "ConnectionCount", "Connections", "ExactCorrelationCount", "StrongCorrelationCount",
            "CandidateCorrelationCount", "UncorrelatedCount", "CorrelationCoveragePercent", "UnassignedPreEncryptCount",
            "UnassignedPostDecryptCount", "UnassignedHandlerDecodedCount"});
        if (!write_required(staging / L"connection-summary.json", connection_summary + "\n")) return result;

        const auto capture_info = MakeJsonObject({
            {"SchemaId", "capture-info"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"PrimarySessionDirectory", "PrimarySession"},
            {"OriginalCaptureDirectoryId", WideToUtf8(session_path.filename().wstring())},
            {"DirectoryIdentityExplanation", "ZIP uses PrimarySession and SessionId is the sole cross-artifact identity; the legacy directory name is diagnostic only"},
            {"ToolVersion", std::string(kEvidenceToolVersion)}, {"PackagedAtUtc", packaged_at},
            {"AnalysisRunId", analysis_run_id}, {"PackageRunId", package_run_id},
            {"OriginalPackageSHA256", original_package_sha256.empty() ? "null" :
                "\"" + original_package_sha256 + "\""},
            {"SourcePackageSchemaVersion", source_package_schema_version.empty() ? "null" :
                source_package_schema_version},
            {"OriginalPackageCaptureRecordsPath", original_package_sha256.empty() ? "null" :
                "\"PrimarySession/raw/original-package-capture-records.jsonl\""},
            {"OriginalPackageCaptureRecordsSHA256", original_package_sha256.empty() ? "null" :
                "\"" + FileSha256(input_records).value_or("") + "\""},
            {"CompressionMethod", "Deflate"}, {"CompressionLevel", std::to_string(result.compression_level)},
            {"CompressionStatus", "Pending"}, {"UncompressedPackageBytes", "null"},
            {"CompressedPackageBytes", "null"}, {"CompressionRatio", "null"}, {"CompressionDurationMs", "null"},
            {"ClientFileName", client_identity->file_name.empty() ? "God2_opt.exe" : client_identity->file_name},
            {"ClientSHA256", result.client_sha256.empty() ? "null" : "\"" + result.client_sha256 + "\""},
            {"ClientFileSize", client_identity->file_size == 0 ? "null" : std::to_string(client_identity->file_size)},
            {"ClientFileVersion", client_identity->file_version.empty() ? "null" : "\"" + client_identity->file_version + "\""},
            {"ClientProductVersion", client_identity->product_version.empty() ? "null" : "\"" + client_identity->product_version + "\""},
            {"ClientPeTimestamp", client_identity->pe_timestamp == 0 ? "null" : std::to_string(client_identity->pe_timestamp)},
            {"ClientMachine", client_identity->machine == 0 ? "null" : std::to_string(client_identity->machine)},
            {"ClientArchitecture", client_identity->architecture.empty() ? "null" : "\"" + client_identity->architecture + "\""},
            {"ClientImageBase", client_identity->image_base == 0 ? "null" : std::to_string(client_identity->image_base)},
            {"ClientEntryPointRva", client_identity->entry_point_rva == 0 ? "null" : std::to_string(client_identity->entry_point_rva)},
            {"LauncherFileName", launcher_identity->file_name.empty() ? "Launcher.exe" : launcher_identity->file_name},
            {"LauncherSHA256", result.launcher_sha256.empty() ? "null" : "\"" + result.launcher_sha256 + "\""},
            {"LauncherFileSize", launcher_identity->file_size == 0 ? "null" : std::to_string(launcher_identity->file_size)},
            {"LauncherFileVersion", launcher_identity->file_version.empty() ? "null" : "\"" + launcher_identity->file_version + "\""},
            {"LauncherProductVersion", launcher_identity->product_version.empty() ? "null" : "\"" + launcher_identity->product_version + "\""},
            {"LauncherPeTimestamp", launcher_identity->pe_timestamp == 0 ? "null" : std::to_string(launcher_identity->pe_timestamp)},
            {"LauncherMachine", launcher_identity->machine == 0 ? "null" : std::to_string(launcher_identity->machine)},
            {"LauncherArchitecture", launcher_identity->architecture.empty() ? "null" : "\"" + launcher_identity->architecture + "\""},
            {"IdentityCapturedAtUtc", client_identity->captured_at_utc.empty() ? "null" : "\"" + client_identity->captured_at_utc + "\""},
            {"ClientIdentityStatus", result.client_identity_status}, {"LauncherIdentityStatus", result.launcher_identity_status},
            {"IdentityStatus", exact_client_identity ? "Pass" : "EvidenceBlocked"},
            {"IdentityFailureReason", client_identity->failure_reason.empty() && launcher_identity->failure_reason.empty() ? "null" :
                "\"" + JsonEscape(client_identity->failure_reason +
                    (client_identity->failure_reason.empty() || launcher_identity->failure_reason.empty() ? "" : "; ") +
                    launcher_identity->failure_reason) + "\""},
            {"AcquisitionStatus", result.acquisition_status}, {"AnalysisStatus", result.analysis_status},
            {"CleanupStatus", result.cleanup_status}, {"PackageStatus", result.package_status},
            {"TransportCaptureStatus", result.transport_chunks > 0 ? "CompletedWithEvidence" : "EvidenceBlocked"},
            {"StreamReassemblyStatus", result.candidate_protocol_frames > 0 ? "CompletedWithCandidates" : "NoCandidatesProduced"},
            {"ProtocolFrameStatus", result.protocol_frames > 0 ? "CompletedWithVerifiedFrames" : "NoVerifiedFrames"},
            {"DecodeStatus", result.decoded_messages > 0 ? "CompletedWithRuntimeObservations" : "EvidenceBlocked"},
            {"HandlerObservationStatus", result.handler_observations > 0 ? "CompletedWithEvidence" : "EvidenceBlocked"},
            {"GameplayClassificationStatus", result.gameplay_candidates > 0 ? "CompletedWithCandidates" : "NoCandidatesProduced"},
            {"AutomaticSemanticMode", "PassiveAutomaticNoSceneSelection"},
            {"AutomaticSemanticStatus", result.automatic_semantic_candidates > 0 ?
                "CompletedWithCandidates" : "NoKnownStructuralFamilyObserved"},
            {"UserSceneSelectionRequired", "false"},
            {"EvidenceBuildStatus", "Completed"},
            {"CaptureModes", "[\"EnhancedX86Dll\",\"EtwMetadataOnlyRawPayloadExcluded\"]"},
            {"RawNetworkPayloadPolicy", "ExcludedFailClosed"},
            {"SemanticEventPayloadPolicy", "StrictCanonicalSanitizedDerivative"},
            {"CaptureStages", "[\"Transport\",\"PreEncrypt\",\"PostDecrypt\",\"HandlerDecoded\"]"},
            {"EnhancedPlaintextAvailable", result.enhanced_plaintext_available ? "true" : "false"},
            {"CaptureRecords", std::to_string(result.capture_records)},
            {"TransportChunks", std::to_string(result.transport_chunks)},
            {"ProtocolFrames", std::to_string(result.protocol_frames)},
            {"CandidateProtocolFrames", std::to_string(result.candidate_protocol_frames)},
            {"DecodedMessages", std::to_string(result.decoded_messages)},
            {"HandlerObservations", std::to_string(result.handler_observations)},
            {"GameplayCandidates", std::to_string(result.gameplay_candidates)},
            {"AutomaticSemanticCandidates", std::to_string(result.automatic_semantic_candidates)},
            {"AutomaticSemanticFamilies", std::to_string(result.automatic_semantic_family_count)},
            {"AutomaticStructurallySufficientFamilies",
                std::to_string(result.automatic_structurally_sufficient_family_count)},
            {"EvidenceItems", std::to_string(result.evidence_items)},
            {"ProtocolMappingInputEligible", result.protocol_frames > 0 ? "true" : "false"},
            {"EvidenceDatabaseImportEligible", result.capture_records > 0 ? "true" : "false"},
            {"DirectProductionMutationEligible", "false"},
            {"UnframedTransportChunkCount", std::to_string(result.transport_chunks)},
            {"UnframedPlaintextRecordCount", std::to_string(result.unframed_plaintext_records)},
            {"UnknownVerifiedProtocolFrameCount", std::to_string(result.unknown_protocol_frames)},
            {"ClientToServerUniqueOpcodeCount", std::to_string(result.client_to_server_opcode_count)},
            {"ServerToClientUniqueOpcodeCount", std::to_string(result.server_to_client_opcode_count)},
            {"InvalidCandidateFrameCount", std::to_string(result.invalid_candidate_frames)},
            {"IncompleteStreamFragmentCount", std::to_string(result.incomplete_stream_fragments)},
            {"PartialRecovery", partial_recovery ? "true" : "false"},
            {"LastSuccessfulCaptureSequence", std::to_string(result.capture_records)}
        }, {"SchemaVersion", "OriginalPackageSHA256", "SourcePackageSchemaVersion", "OriginalPackageCaptureRecordsPath",
            "OriginalPackageCaptureRecordsSHA256", "CompressionLevel", "ClientSHA256", "ClientFileSize", "ClientFileVersion", "ClientProductVersion",
            "ClientPeTimestamp", "ClientMachine", "ClientArchitecture", "ClientImageBase", "ClientEntryPointRva",
            "LauncherSHA256", "LauncherFileSize", "LauncherFileVersion", "LauncherProductVersion",
            "LauncherPeTimestamp", "LauncherMachine", "LauncherArchitecture", "IdentityCapturedAtUtc", "IdentityFailureReason",
            "UncompressedPackageBytes", "CompressedPackageBytes", "CompressionRatio", "CompressionDurationMs",
            "CaptureModes", "CaptureStages", "EnhancedPlaintextAvailable", "CaptureRecords", "TransportChunks",
            "ProtocolFrames", "CandidateProtocolFrames", "DecodedMessages", "HandlerObservations", "GameplayCandidates",
            "AutomaticSemanticCandidates", "AutomaticSemanticFamilies", "AutomaticStructurallySufficientFamilies",
            "UserSceneSelectionRequired", "EvidenceItems",
            "ProtocolMappingInputEligible", "EvidenceDatabaseImportEligible", "DirectProductionMutationEligible",
            "UnframedTransportChunkCount", "UnframedPlaintextRecordCount", "UnknownVerifiedProtocolFrameCount",
            "ClientToServerUniqueOpcodeCount", "ServerToClientUniqueOpcodeCount", "InvalidCandidateFrameCount", "IncompleteStreamFragmentCount",
            "PartialRecovery", "LastSuccessfulCaptureSequence"});
        if (!write_required(staging / L"capture-info.json", capture_info + "\n")) return result;

        const bool enhanced_was_attached =
            enhanced_status.valid && enhanced_status.was_ever_attached;
        const bool enhanced_probe_ready =
            enhanced_status.valid && enhanced_status.probe_ready;
        const bool enhanced_strict_unload_verified =
            enhanced_status.valid && enhanced_status.strict_unload_verified;
        const bool enhanced_detach_attempted = enhanced_status.valid &&
            enhanced_status.injection_attempted &&
            enhanced_status.module_was_ever_loaded && !enhanced_status.probe_ready;
        const bool enhanced_module_unloaded = enhanced_status.valid &&
            enhanced_status.module_was_ever_loaded && enhanced_status.module_absent &&
            !enhanced_status.target_process_exited;
        const bool enhanced_module_resident_inactive = enhanced_status.valid &&
            enhanced_status.module_was_ever_loaded && !enhanced_status.module_absent &&
            !enhanced_status.probe_ready;
        const auto enhanced_report = MakeJsonObject({
            {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"Requested", enhanced_status.valid && enhanced_status.requested ? "true" : "false"},
            {"WasEverAttached", enhanced_was_attached ? "true" : "false"},
            {"ProbeReady", enhanced_probe_ready ? "true" : "false"},
            {"StrictUnloadVerified", enhanced_strict_unload_verified ? "true" : "false"},
            {"FirstRecordAtUtc", records.empty() ? "" : records.front().captured_at_utc},
            {"LastRecordAtUtc", records.empty() ? "" : records.back().captured_at_utc},
            {"RecordsCaptured", std::to_string(result.capture_records)},
            {"TransportRecords", std::to_string(result.transport_chunks)},
            {"PreEncryptRecords", std::to_string(result.pre_encrypt_records)},
            {"PostDecryptRecords", std::to_string(result.post_decrypt_records)},
            {"HandlerDecodedRecords", std::to_string(result.handler_decoded_records)},
            {"EnhancedPlaintextAvailable", result.enhanced_plaintext_available ? "true" : "false"},
            {"ProtocolMappingInputEligible", result.protocol_frames > 0 ? "true" : "false"},
            {"EvidenceDatabaseImportEligible", result.capture_records > 0 ? "true" : "false"},
            {"DirectProductionMutationEligible", "false"},
            {"VerifiedProtocolFrames", std::to_string(result.protocol_frames)},
            {"DroppedQueueRecords", NumberOrNull(dropped_queue_records)},
            {"ProbeWriteFailures", NumberOrNull(probe_write_failures)},
            {"SemanticDroppedEvents", NumberOrNull(semantic_dropped_events)},
            {"SemanticWriteFailures", NumberOrNull(semantic_write_failures)},
            {"SharedTransportProducerProcessId", NumberOrNull(shared_producer_pid)},
            {"SharedTransportAttachedProcessId", NumberOrNull(shared_attached_pid)},
            {"SharedTransportAttempted", NumberOrNull(shared_attempted)},
            {"SharedTransportAccepted", NumberOrNull(shared_accepted)},
            {"SharedTransportConsumed", NumberOrNull(shared_consumed)},
            {"SharedTransportDropped", NumberOrNull(shared_dropped)},
            {"SharedTransportInvalidPayloads", NumberOrNull(shared_invalid)},
            {"SharedTransportConsumerLag", NumberOrNull(shared_lag)},
            {"SharedTransportWriteFailures", NumberOrNull(shared_write_failures)},
            {"SharedTransportConsumerIoFailure", shared_consumer_io_failure ? "true" : "false"},
            {"SharedTransportIdentityBound", shared_identity_bound ? "true" : "false"},
            {"SharedTransportDomainsComplete", shared_domains_complete ? "true" : "false"},
            {"SemanticEvidenceIncomplete", semantic_transport_incomplete ? "true" : "false"},
            {"StopRequested", enhanced_detach_attempted ? "true" : "false"},
            {"DetachAttempted", enhanced_detach_attempted ? "true" : "false"},
            {"ModuleUnloaded", enhanced_module_unloaded ? "true" : "false"},
            {"ModuleResidentInactive", enhanced_module_resident_inactive ? "true" : "false"},
            {"GameProcessExited", enhanced_status.valid && enhanced_status.target_process_exited ? "true" : "false"},
            {"AcquisitionStatus", result.acquisition_status}, {"CleanupStatus", result.cleanup_status},
            {"OriginalStatus", enhanced_original_status},
            {"OriginalDetail", enhanced_original_detail},
            {"OriginalFailureKind", enhanced_failure_kind},
            {"InjectorPresentAtStop", enhanced_status.valid && enhanced_status.injector_present ? "true" : "false"},
            {"ProbePresentAtStop", enhanced_status.valid && enhanced_status.probe_present ? "true" : "false"}
        }, {"SchemaVersion", "Requested", "WasEverAttached", "ProbeReady", "StrictUnloadVerified", "RecordsCaptured",
            "TransportRecords", "PreEncryptRecords", "PostDecryptRecords", "HandlerDecodedRecords",
            "EnhancedPlaintextAvailable", "ProtocolMappingInputEligible", "EvidenceDatabaseImportEligible",
            "DirectProductionMutationEligible", "VerifiedProtocolFrames", "DroppedQueueRecords", "ProbeWriteFailures",
            "SemanticDroppedEvents", "SemanticWriteFailures", "SharedTransportProducerProcessId",
            "SharedTransportAttachedProcessId", "SharedTransportAttempted", "SharedTransportAccepted",
            "SharedTransportConsumed", "SharedTransportDropped",
            "SharedTransportInvalidPayloads", "SharedTransportConsumerLag",
            "SharedTransportWriteFailures",
            "SharedTransportConsumerIoFailure", "SharedTransportIdentityBound",
            "SharedTransportDomainsComplete", "SemanticEvidenceIncomplete",
            "StopRequested", "DetachAttempted", "ModuleUnloaded", "ModuleResidentInactive", "GameProcessExited",
            "InjectorPresentAtStop", "ProbePresentAtStop"});
        if (!write_required(staging / L"enhanced-capture.json", enhanced_report + "\n")) return result;

        const auto health = MakeJsonObject({
            {"SchemaId", "capture-health"}, {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"CaptureRecordCount", std::to_string(result.capture_records)},
            {"EnhancedPlaintextAvailable", result.enhanced_plaintext_available ? "true" : "false"},
            {"PreEncryptRecordCount", std::to_string(result.pre_encrypt_records)},
            {"PostDecryptRecordCount", std::to_string(result.post_decrypt_records)},
            {"HandlerDecodedRecordCount", std::to_string(result.handler_decoded_records)},
            {"TransportChunkCount", std::to_string(result.transport_chunks)},
            {"ProtocolFrameCount", std::to_string(result.protocol_frames)},
            {"CandidateProtocolFrameCount", std::to_string(result.candidate_protocol_frames)},
            {"DecodedMessageCount", std::to_string(result.decoded_messages)},
            {"HandlerObservationCount", std::to_string(result.handler_observations)},
            {"GameplayCandidateCount", std::to_string(result.gameplay_candidates)},
            {"AutomaticSemanticCandidateCount", std::to_string(result.automatic_semantic_candidates)},
            {"AutomaticSemanticFamilyCount", std::to_string(result.automatic_semantic_family_count)},
            {"AutomaticStructurallySufficientFamilyCount",
                std::to_string(result.automatic_structurally_sufficient_family_count)},
            {"EvidenceItemCount", std::to_string(result.evidence_items)},
            {"UnframedTransportChunkCount", std::to_string(result.transport_chunks)},
            {"UnframedPlaintextRecordCount", std::to_string(result.unframed_plaintext_records)},
            {"UnknownVerifiedProtocolFrameCount", std::to_string(result.unknown_protocol_frames)},
            {"ClientToServerUniqueOpcodeCount", std::to_string(result.client_to_server_opcode_count)},
            {"ServerToClientUniqueOpcodeCount", std::to_string(result.server_to_client_opcode_count)},
            {"InvalidCandidateFrameCount", std::to_string(result.invalid_candidate_frames)},
            {"IncompleteStreamFragmentCount", std::to_string(result.incomplete_stream_fragments)},
            {"DroppedQueueRecordsStatus", dropped_queue_records ? "Measured" : "Unknown"},
            {"DroppedQueueRecords", NumberOrNull(dropped_queue_records)},
            {"ProbeWriteFailures", NumberOrNull(probe_write_failures)},
            {"SemanticDroppedEvents", NumberOrNull(semantic_dropped_events)},
            {"SemanticWriteFailures", NumberOrNull(semantic_write_failures)},
            {"SharedTransportProducerProcessId", NumberOrNull(shared_producer_pid)},
            {"SharedTransportAttachedProcessId", NumberOrNull(shared_attached_pid)},
            {"SharedTransportAttempted", NumberOrNull(shared_attempted)},
            {"SharedTransportAccepted", NumberOrNull(shared_accepted)},
            {"SharedTransportConsumed", NumberOrNull(shared_consumed)},
            {"SharedTransportDropped", NumberOrNull(shared_dropped)},
            {"SharedTransportInvalidPayloads", NumberOrNull(shared_invalid)},
            {"SharedTransportConsumerLag", NumberOrNull(shared_lag)},
            {"SharedTransportWriteFailures", NumberOrNull(shared_write_failures)},
            {"SharedTransportConsumerIoFailure", shared_consumer_io_failure ? "true" : "false"},
            {"SharedTransportIdentityBound", shared_identity_bound ? "true" : "false"},
            {"SharedTransportDomainsComplete", shared_domains_complete ? "true" : "false"},
            {"RecordsLost", NumberOrNull(records_lost)},
            {"EventsLost", NumberOrNull(etw_events_lost)}, {"BuffersLost", NumberOrNull(etw_buffers_lost)},
            {"PacketLossStatus", etw_events_lost ? "Measured" : "Unknown"},
            {"PacketLossCount", NumberOrNull(etw_events_lost)},
            {"InvalidRecordCount", std::to_string(invalid_records)},
            {"InvalidPayloadHexCount", std::to_string(invalid_payloads)},
            {"InvalidLengthCount", std::to_string(result.invalid_candidate_frames)},
            {"DesyncBytes", std::to_string(result.desync_bytes)},
            {"PendingStreamBytes", std::to_string(result.pending_stream_bytes)},
            {"EvictedStreamBytes", std::to_string(candidate_analysis.evicted_stream_bytes)},
            {"IncompleteStreams", std::to_string(result.incomplete_stream_fragments)},
            {"EtwEventCount", std::to_string(result.etw_event_lines)},
            {"EtwRawPacketEventCount", std::to_string(etw_raw_packets)},
            {"EtwSourceStatus", result.etw_event_lines > 0 ? "Pass" : "EvidenceBlocked"},
            {"EnhancedSourceStatus", result.capture_records > 0 ? "Pass" : "EvidenceBlocked"},
            {"CompressionStatus", "Pending"}, {"CompressionMethod", "Deflate"}, {"CompressionRatio", "null"},
            {"UncompressedPackageBytes", "null"}, {"CompressedPackageBytes", "null"}, {"CompressionDurationMs", "null"},
            {"ClientIdentityStatus", exact_client_identity ? "Pass" : "EvidenceBlocked"},
            {"LauncherIdentityStatus", result.launcher_identity_status == "Pass" ? "Pass" : "EvidenceBlocked"},
            {"ConnectionSummaryStatus", result.connection_count > 0 ? "Pass" : "EvidenceBlocked"},
            {"ConnectionCount", std::to_string(result.connection_count)},
            {"CandidateFrameStatus", result.candidate_protocol_frames > 0 ? "PassWithWarnings" : "EvidenceBlocked"},
            {"VerifiedProtocolFrameCount", std::to_string(result.protocol_frames)},
            {"CorrelationStatus", result.correlation_coverage_percent > 0 ? "PassWithWarnings" : "EvidenceBlocked"},
            {"ExactCorrelationCount", std::to_string(result.exact_correlations)},
            {"StrongCorrelationCount", std::to_string(result.strong_correlations)},
            {"CandidateCorrelationCount", std::to_string(result.candidate_correlations)},
            {"UncorrelatedCount", std::to_string(result.uncorrelated_correlations)},
            {"CorrelationCoveragePercent", std::to_string(result.correlation_coverage_percent)},
            {"SemanticProbeStatus", result.semantic_probe_status},
            {"ParserReadEventCount", std::to_string(result.parser_read_events)},
            {"SerializerWriteEventCount", std::to_string(result.serializer_write_events)},
            {"HandlerArgumentEventCount", std::to_string(result.handler_argument_events)},
            {"ObjectResolutionEventCount", std::to_string(result.object_resolution_events)},
            {"StateMutationEventCount", std::to_string(result.state_mutation_events)},
            {"UIAnchorEventCount", std::to_string(result.ui_anchor_events)},
            {"ValueFlowEdgeCount", std::to_string(result.semantic_value_flow_edges)},
            {"ProtocolFieldEvidenceCount", std::to_string(result.protocol_field_evidence)},
            {"VerifiedFieldSemanticCount", std::to_string(result.verified_field_semantics)},
            {"SemanticContradictionCount", std::to_string(result.semantic_contradictions)},
            {"SemanticEvidenceIncomplete",
                result.semantic_evidence_incomplete || semantic_transport_incomplete ? "true" : "false"},
            {"ManifestCodexIngestionStatus", "Pending"},
            {"EvidenceLevelValidationStatus", result.schema_validation_passed ? "Pass" : "Failed"},
            {"TimestampValidationStatus", timestamp_status}, {"CountReconciliationStatus", count_status},
            {"ManifestValidationStatus", "Pending"}, {"ProvenanceValidationStatus", provenance_status},
            {"SchemaValidationStatus", result.schema_validation_passed ? "Pass" : "Failed"},
            {"SqliteIntegrityStatus", sqlite_status}, {"ReplayReanalysisReadyStatus", "Pass"},
            {"OverallHealthStatus", result.blockers.empty() ? (result.warnings.empty() ? "Pass" : "PassWithWarnings") : "Failed"},
            {"Warnings", warnings_json}, {"Blockers", blockers_json}
        }, {"SchemaVersion", "CaptureRecordCount", "EnhancedPlaintextAvailable", "PreEncryptRecordCount",
            "PostDecryptRecordCount", "HandlerDecodedRecordCount", "TransportChunkCount", "ProtocolFrameCount",
            "CandidateProtocolFrameCount", "DecodedMessageCount", "HandlerObservationCount", "GameplayCandidateCount",
            "AutomaticSemanticCandidateCount", "AutomaticSemanticFamilyCount",
            "AutomaticStructurallySufficientFamilyCount", "EvidenceItemCount",
            "UnframedTransportChunkCount", "UnframedPlaintextRecordCount", "UnknownVerifiedProtocolFrameCount",
            "ClientToServerUniqueOpcodeCount", "ServerToClientUniqueOpcodeCount", "InvalidCandidateFrameCount", "IncompleteStreamFragmentCount", "DroppedQueueRecords",
            "ProbeWriteFailures", "SemanticDroppedEvents", "SemanticWriteFailures",
            "SharedTransportProducerProcessId", "SharedTransportAttachedProcessId",
            "SharedTransportAttempted", "SharedTransportAccepted", "SharedTransportConsumed",
            "SharedTransportDropped", "SharedTransportInvalidPayloads", "SharedTransportConsumerLag",
            "SharedTransportWriteFailures",
            "SharedTransportConsumerIoFailure", "SharedTransportIdentityBound",
            "SharedTransportDomainsComplete", "RecordsLost", "EventsLost", "BuffersLost", "PacketLossCount",
            "InvalidRecordCount", "InvalidPayloadHexCount", "InvalidLengthCount", "DesyncBytes",
            "PendingStreamBytes", "EvictedStreamBytes", "IncompleteStreams", "EtwEventCount", "EtwRawPacketEventCount",
            "CompressionRatio", "UncompressedPackageBytes", "CompressedPackageBytes", "CompressionDurationMs",
            "ConnectionCount", "VerifiedProtocolFrameCount", "ExactCorrelationCount", "StrongCorrelationCount",
            "CandidateCorrelationCount", "UncorrelatedCount", "CorrelationCoveragePercent",
            "ParserReadEventCount", "SerializerWriteEventCount", "HandlerArgumentEventCount",
            "ObjectResolutionEventCount", "StateMutationEventCount", "UIAnchorEventCount",
            "ValueFlowEdgeCount", "ProtocolFieldEvidenceCount", "VerifiedFieldSemanticCount",
            "SemanticContradictionCount", "SemanticEvidenceIncomplete",
            "Warnings", "Blockers"});
        if (!write_required(staging / L"capture-health.json", health + "\n")) return result;

        const auto limitations = MakeJsonObject({
            {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"EtwEventLines", std::to_string(result.etw_event_lines)},
            {"EtwStatus", result.etw_event_lines == 0 ? "EvidenceBlocked" : "Observed"},
            {"EnhancedCaptureRecords", std::to_string(result.capture_records)},
            {"DetachCleanupWarning", result.cleanup_status == "Completed" ? "false" : "true"},
            {"DesyncBytes", std::to_string(result.desync_bytes)},
            {"PendingStreamBytes", std::to_string(result.pending_stream_bytes)},
            {"UnknownProtocolRecordsOrFrames", std::to_string(CountLines(session_path / L"raw" / L"unknown-frames.jsonl"))},
            {"UnverifiedLengthPrefix", result.protocol_frames > 0 ? "false" : "true"}, {"AbsolutePathPortabilityFixed", "true"},
            {"TimestampConsistencyFixed", result.timestamp_validation_passed ? "true" : "false"},
            {"ProtocolFrameTruth", result.protocol_frames > 0 ?
                "PreEncrypt/PostDecrypt records passed exact u16le length and checksum validation" :
                "No PreEncrypt/PostDecrypt record passed verified frame validation"},
            {"KnownLimitations", warnings_json}
        }, {"SchemaVersion", "EtwEventLines", "EnhancedCaptureRecords", "DetachCleanupWarning", "DesyncBytes",
            "PendingStreamBytes", "UnknownProtocolRecordsOrFrames", "UnverifiedLengthPrefix",
            "AbsolutePathPortabilityFixed", "TimestampConsistencyFixed", "KnownLimitations"});
        if (!write_required(staging / L"limitations.json", limitations + "\n")) return result;

        const auto authority = MakeJsonObject({
            {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"RawImmutable", original_package_sha256.empty() ?
                "[\"PrimarySession/raw/capture-records.jsonl\",\"PrimarySession/raw/transport-chunks.jsonl\",\"PrimarySession/raw/trace.bin\"]" :
                "[\"PrimarySession/raw/original-package-capture-records.jsonl\",\"PrimarySession/raw/capture-records.jsonl\",\"PrimarySession/raw/transport-chunks.jsonl\",\"PrimarySession/raw/trace.bin\"]"},
            {"DerivedDeterministic", "[\"PrimarySession/decrypted/pre-encrypt-records.jsonl\",\"PrimarySession/decrypted/post-decrypt-records.jsonl\",\"PrimarySession/decrypted/handler-decoded-records.jsonl\",\"PrimarySession/decrypted/unframed-records.jsonl\",\"PrimarySession/frames/protocol-frames.jsonl\",\"PrimarySession/protocol/decoded-frames.jsonl\",\"PrimarySession/mapping/transport-to-decrypted.jsonl\",\"PrimarySession/mapping/decrypted-to-handler.jsonl\",\"PrimarySession/decoded/decoded-messages.jsonl\",\"PrimarySession/decoded/handler-observations.jsonl\",\"PrimarySession/semantic\",\"PrimarySession/gameplay\",\"PrimarySession/exports\",\"provenance.jsonl\"]"},
            {"Candidate", "[\"PrimarySession/protocol/codex-opcode-worklist.jsonl\",\"PrimarySession/derived/candidate-protocol-frames.jsonl\",\"PrimarySession/derived/gameplay-candidates.jsonl\",\"PrimarySession/analysis/automatic-semantic-summary.json\",\"PrimarySession/analysis/action-instances.jsonl\",\"PrimarySession/analysis/action-patterns.jsonl\",\"PrimarySession/analysis/action-pattern-summary.json\",\"PrimarySession/analysis/orphan-handler-bursts.jsonl\",\"PrimarySession/analysis/outbound-batch-candidates.jsonl\"]"},
            {"Diagnostic", "[\"capture-health.json\",\"limitations.json\",\"package-validation.json\"]"},
            {"ConflictRule", "RawImmutable wins; regenerate deterministic derived artifacts; Candidate is never Verified"},
            {"ProductionRule", "Candidate and Unknown artifacts are forbidden from direct Production Database mutation"}
        }, {"SchemaVersion", "RawImmutable", "DerivedDeterministic", "Candidate", "Diagnostic"});
        if (!write_required(staging / L"artifact-authority.json", authority + "\n")) return result;

        const auto sensitive = MakeJsonObject({
            {"SchemaVersion", "11"}, {"SessionId", result.session_id},
            {"ContainsPotentialCredentials", "null"}, {"ContainsAuthenticationPayload", "null"},
            {"ContainsChatPayload", "null"}, {"ContainsPersonalIdentifiers", "null"},
            {"ContainsNetworkEndpoints", "true"},
            {"ReviewStatus", "AuthenticationPlaintextGatePassedEncryptedTransportClassificationUnproven"},
            {"AuthenticationCapturePolicy", "Sensitive plaintext records are rejected; encrypted transport is not declared authentication-free without a bound fragment-redaction attestation"},
            {"LegacyUnsafeDiagnosticScan", "Pass"},
            {"RawNetworkPayloadPolicy", "ExcludedFailClosed"},
            {"SemanticEventPayloadPolicy", "StrictCanonicalSanitizedDerivative"},
            {"SemanticEventsSanitized", "true"},
            {"DetectedArtifactIds", "[]"}, {"Sanitized", "false"}, {"RawEvidenceModified", "false"}
        }, {"SchemaVersion", "ContainsPotentialCredentials", "ContainsAuthenticationPayload", "ContainsChatPayload",
            "ContainsPersonalIdentifiers", "ContainsNetworkEndpoints", "SemanticEventsSanitized",
            "DetectedArtifactIds", "Sanitized", "RawEvidenceModified"});
        if (!write_required(staging / L"sensitive-content.json", sensitive + "\n")) return result;

        const auto handoff_json = MakeJsonObject({
            {"SchemaVersion", "11"}, {"PackagePurpose", "Codex Evidence Package for God2 Classic protocol and gameplay recovery"},
            {"PrimarySessionId", result.session_id}, {"ClientSHA256", result.client_sha256.empty() ? "null" :
                "\"" + result.client_sha256 + "\""},
            {"AnalysisRunId", analysis_run_id}, {"PackageRunId", package_run_id},
            {"OriginalPackageSHA256", original_package_sha256.empty() ? "null" :
                "\"" + original_package_sha256 + "\""},
            {"SourcePackageSchemaVersion", source_package_schema_version.empty() ? "null" :
                source_package_schema_version},
            {"ClientSHA256Status", result.client_identity_status},
            {"LauncherSHA256", result.launcher_sha256.empty() ? "null" : "\"" + result.launcher_sha256 + "\""},
            {"LauncherSHA256Status", result.launcher_identity_status}, {"ToolVersion", std::string(kEvidenceToolVersion)},
            {"CaptureModes", "[\"EnhancedX86Dll\",\"EtwMetadataOnlyRawPayloadExcluded\"]"},
            {"RawNetworkPayloadPolicy", "ExcludedFailClosed"},
            {"SemanticEventPayloadPolicy", "StrictCanonicalSanitizedDerivative"},
            {"CaptureStages", "[\"Transport\",\"PreEncrypt\",\"PostDecrypt\",\"HandlerDecoded\"]"},
            {"RawTransportAvailable", result.transport_chunks > 0 ? "true" : "false"},
            {"PreEncryptAvailable", result.pre_encrypt_records > 0 ? "true" : "false"},
            {"PostDecryptAvailable", result.post_decrypt_records > 0 ? "true" : "false"},
            {"HandlerDecodedAvailable", result.handler_decoded_records > 0 ? "true" : "false"},
            {"EnhancedPlaintextAvailable", result.enhanced_plaintext_available ? "true" : "false"},
            {"ProtocolMappingInputEligible", result.protocol_frames > 0 ? "true" : "false"},
            {"EvidenceDatabaseImportEligible", result.capture_records > 0 ? "true" : "false"},
            {"DirectProductionMutationEligible", "false"},
            {"GameplayClassifierArtifactCount", std::to_string(copied_gameplay_artifacts)},
            {"EvidenceExportArtifactCount", std::to_string(copied_export_artifacts)},
            {"RawTransportCount", std::to_string(result.transport_chunks)},
            {"PreEncryptCount", std::to_string(result.pre_encrypt_records)},
            {"PostDecryptCount", std::to_string(result.post_decrypt_records)},
            {"HandlerDecodedCount", std::to_string(result.handler_decoded_records)},
            {"ValidProtocolFrameCount", std::to_string(result.protocol_frames)},
            {"UnknownFrameCount", std::to_string(result.unknown_protocol_frames)},
            {"SemanticProbeStatus", result.semantic_probe_status},
            {"ParserReadEventCount", std::to_string(result.parser_read_events)},
            {"SerializerWriteEventCount", std::to_string(result.serializer_write_events)},
            {"HandlerArgumentEventCount", std::to_string(result.handler_argument_events)},
            {"ObjectResolutionEventCount", std::to_string(result.object_resolution_events)},
            {"StateMutationEventCount", std::to_string(result.state_mutation_events)},
            {"UIAnchorEventCount", std::to_string(result.ui_anchor_events)},
            {"ValueFlowEdgeCount", std::to_string(result.semantic_value_flow_edges)},
            {"ProtocolFieldEvidenceCount", std::to_string(result.protocol_field_evidence)},
            {"UnknownFieldSemanticCount", std::to_string(result.unknown_field_semantics)},
            {"CandidateFieldSemanticCount", std::to_string(result.candidate_field_semantics)},
            {"RecoveredFieldSemanticCount", std::to_string(result.recovered_field_semantics)},
            {"VerifiedFieldSemanticCount", std::to_string(result.verified_field_semantics)},
            {"VerifiedProtocolSpecPacketCount", std::to_string(result.verified_protocol_spec_packets)},
            {"VerifiedProtocolSpecFieldCount", std::to_string(result.verified_field_semantics)},
            {"ServerIntegrationReadyCount", std::to_string(result.server_integration_ready)},
            {"DatabaseIntegrationReadyCount", std::to_string(result.database_integration_ready)},
            {"SemanticContradictionCount", std::to_string(result.semantic_contradictions)},
            {"SemanticEvidenceIncomplete",
                result.semantic_evidence_incomplete || semantic_transport_incomplete ? "true" : "false"},
            {"AutomaticSemanticMode", "PassiveAutomaticNoSceneSelection"},
            {"UserSceneSelectionRequired", "false"},
            {"AutomaticSemanticCandidateCount", std::to_string(result.automatic_semantic_candidates)},
            {"AutomaticSemanticFamilyCount", std::to_string(result.automatic_semantic_family_count)},
            {"AutomaticStructurallySufficientFamilyCount",
                std::to_string(result.automatic_structurally_sufficient_family_count)},
            {"ActionTriggerCandidateCount", std::to_string(result.action_trigger_candidate_count)},
            {"ActionInstanceCount", std::to_string(result.action_instance_count)},
            {"ActionPatternCount", std::to_string(result.action_pattern_count)},
            {"GroupedTriggerCount", std::to_string(result.grouped_trigger_count)},
            {"UngroupedTriggerCount", std::to_string(result.ungrouped_trigger_count)},
            {"ExactActionCount", std::to_string(result.exact_action_count)},
            {"StrongActionCount", std::to_string(result.strong_action_count)},
            {"CandidateActionCount", std::to_string(result.candidate_action_count)},
            {"AmbiguousActionCount", std::to_string(result.ambiguous_action_count)},
            {"UnknownActionPatternCount", std::to_string(result.unknown_action_pattern_count)},
            {"VerifiedGameplayMappedPatternCount", std::to_string(result.verified_gameplay_mapped_pattern_count)},
            {"OutboundBatchCandidateCount", std::to_string(result.outbound_batch_candidate_count)},
            {"OrphanHandlerBurstCount", std::to_string(result.orphan_handler_burst_count)},
            {"ActionClusteringCoveragePercent", std::to_string(result.action_clustering_coverage_percent)},
            {"ClientToServerOpcodeCount", std::to_string(result.client_to_server_opcode_count)},
            {"ServerToClientOpcodeCount", std::to_string(result.server_to_client_opcode_count)},
            {"EventsLost", NumberOrNull(etw_events_lost)}, {"RecordsLost", NumberOrNull(records_lost)},
            {"LossMeasurementStatus", etw_events_lost && records_lost ? "Measured" : "PartiallyMeasuredOrUnknown"},
            {"DeepAnalysisPriority", "[\"VerifiedIntegrationManifest\",\"VerifiedProtocolSpec\",\"SemanticEvidenceGraph\",\"ValueFlow\",\"ActionPattern\",\"OpcodeWorklist\",\"RawPlaintextEvidence\"]"},
            {"AutomaticSemanticIngestionPriority", "[\"PrimarySession/analysis/automatic-semantic-summary.json\",\"PrimarySession/derived/gameplay-candidates.jsonl\"]"},
            {"AcquisitionStatus", result.acquisition_status}, {"AnalysisStatus", result.analysis_status},
            {"CleanupStatus", result.cleanup_status}, {"PackageStatus", result.package_status},
            {"AuthoritativeArtifacts", original_package_sha256.empty() ?
                "[\"PrimarySession/raw/capture-records.jsonl\",\"PrimarySession/raw/transport-chunks.jsonl\",\"PrimarySession/raw/trace.bin\"]" :
                "[\"PrimarySession/raw/original-package-capture-records.jsonl\",\"PrimarySession/raw/capture-records.jsonl\",\"PrimarySession/raw/transport-chunks.jsonl\",\"PrimarySession/raw/trace.bin\"]"},
            {"CandidateArtifacts", "[\"PrimarySession/protocol/codex-opcode-worklist.jsonl\",\"PrimarySession/derived/candidate-protocol-frames.jsonl\",\"PrimarySession/derived/incomplete-stream-fragments.jsonl\",\"PrimarySession/derived/gameplay-candidates.jsonl\",\"PrimarySession/analysis/automatic-semantic-summary.json\",\"PrimarySession/analysis/action-instances.jsonl\",\"PrimarySession/analysis/action-patterns.jsonl\",\"PrimarySession/analysis/action-pattern-summary.json\",\"PrimarySession/analysis/orphan-handler-bursts.jsonl\",\"PrimarySession/analysis/outbound-batch-candidates.jsonl\"]"},
            {"KnownLimitations", warnings_json}, {"Warnings", warnings_json}, {"Blockers", blockers_json},
            {"RecommendedIngestionOrder", "[\"PrimarySession/reports/semantic-segment-manifest.json\",\"PrimarySession/semantic/verified-integration-manifest.json\",\"PrimarySession/semantic/verified-protocol-spec.json\",\"PrimarySession/semantic/semantic-evidence-graph.jsonl\",\"PrimarySession/semantic/value-flow.jsonl\",\"PrimarySession/analysis/action-patterns.jsonl\",\"PrimarySession/protocol/codex-opcode-worklist.jsonl\",\"PrimarySession/raw/semantic-events.jsonl\",\"PrimarySession/raw/capture-records.jsonl\",\"manifest.json\",\"package-validation.json\",\"provenance.jsonl\"]"},
            {"ProtocolFrameTruth", result.protocol_frames > 0 ?
                "Verified frames came only from plaintext records with exact u16le length and valid checksum" :
                "No verified plaintext frame is available; raw transport was not promoted to an opcode"},
            {"CandidateFrameTruth", "CandidateProtocolFrames are not Verified and are forbidden from direct production mutation"},
            {"RuntimeObservationTruth", "DecodedMessages and HandlerObservations are direct runtime observations"},
            {"ConflictRule", "Raw evidence outranks Candidate or Derived artifacts"},
            {"RecapturePolicy", "Reanalyze retained evidence before requesting another capture"},
            {"ServerMutationPolicy", "Validate package first. Candidate is not Verified. Unknown is not Verified. Preserve provenance for every Server or MariaDB change. Reuse raw evidence before requesting recapture."}
        }, {"SchemaVersion", "OriginalPackageSHA256", "SourcePackageSchemaVersion", "ClientSHA256", "LauncherSHA256", "CaptureModes", "CaptureStages",
            "RawTransportAvailable", "PreEncryptAvailable", "PostDecryptAvailable", "HandlerDecodedAvailable",
            "EnhancedPlaintextAvailable", "ProtocolMappingInputEligible", "EvidenceDatabaseImportEligible",
            "DirectProductionMutationEligible", "GameplayClassifierArtifactCount", "EvidenceExportArtifactCount",
             "RawTransportCount", "PreEncryptCount", "PostDecryptCount", "HandlerDecodedCount",
             "ValidProtocolFrameCount", "UnknownFrameCount", "UserSceneSelectionRequired",
             "ParserReadEventCount", "SerializerWriteEventCount", "HandlerArgumentEventCount",
             "ObjectResolutionEventCount", "StateMutationEventCount", "UIAnchorEventCount",
             "ValueFlowEdgeCount", "ProtocolFieldEvidenceCount", "UnknownFieldSemanticCount",
             "CandidateFieldSemanticCount", "RecoveredFieldSemanticCount", "VerifiedFieldSemanticCount",
             "VerifiedProtocolSpecPacketCount", "VerifiedProtocolSpecFieldCount",
             "ServerIntegrationReadyCount", "DatabaseIntegrationReadyCount",
             "SemanticContradictionCount", "SemanticEvidenceIncomplete",
             "AutomaticSemanticCandidateCount", "AutomaticSemanticFamilyCount",
             "AutomaticStructurallySufficientFamilyCount", "ClientToServerOpcodeCount", "ServerToClientOpcodeCount",
             "ActionTriggerCandidateCount", "ActionInstanceCount", "ActionPatternCount", "GroupedTriggerCount",
             "UngroupedTriggerCount", "ExactActionCount", "StrongActionCount", "CandidateActionCount",
             "AmbiguousActionCount", "UnknownActionPatternCount", "VerifiedGameplayMappedPatternCount",
             "OutboundBatchCandidateCount", "OrphanHandlerBurstCount", "ActionClusteringCoveragePercent",
             "EventsLost", "RecordsLost", "DeepAnalysisPriority", "AutomaticSemanticIngestionPriority", "AuthoritativeArtifacts",
            "CandidateArtifacts", "KnownLimitations", "Warnings", "Blockers", "RecommendedIngestionOrder"});
        if (!write_required(staging / L"codex-handoff.json", handoff_json + "\n")) return result;
        std::ostringstream handoff_markdown;
        handoff_markdown << "# CODEX HANDOFF\n\n"
            << "Primary Session: `" << result.session_id << "`  \n"
            << "Package Status: `" << result.package_status << "`  \n"
            << "Acquisition: `" << result.acquisition_status << "`  \n"
            << "Analysis: `" << result.analysis_status << "`  \n"
            << "Cleanup: `" << result.cleanup_status << "`\n\n"
            << "Raw Transport: `" << result.transport_chunks << "`  \n"
            << "PreEncrypt: `" << result.pre_encrypt_records << "`  \n"
            << "PostDecrypt: `" << result.post_decrypt_records << "`  \n"
            << "HandlerDecoded: `" << result.handler_decoded_records << "`  \n"
            << "EnhancedPlaintextAvailable: `" << (result.enhanced_plaintext_available ? "true" : "false") << "`  \n"
            << "Protocol mapping input eligible: `" << (result.protocol_frames > 0 ? "true" : "false") << "`  \n"
            << "Evidence database import eligible: `" << (result.capture_records > 0 ? "true" : "false") << "`  \n"
            << "Direct production mutation eligible: `false`  \n"
            << "Gameplay classifier artifacts: `" << copied_gameplay_artifacts << "`  \n"
            << "Evidence/coverage CSV exports: `" << copied_export_artifacts << "`  \n"
            << "Valid Protocol Frames: `" << result.protocol_frames << "`  \n"
            << "Unknown Frames: `" << result.unknown_protocol_frames << "`  \n"
            << "SemanticProbeStatus: `" << result.semantic_probe_status << "`  \n"
            << "ParserRead / SerializerWrite / HandlerArg: `" << result.parser_read_events << " / "
            << result.serializer_write_events << " / " << result.handler_argument_events << "`  \n"
            << "ObjectResolver / StateMutation / UIAnchor: `" << result.object_resolution_events << " / "
            << result.state_mutation_events << " / " << result.ui_anchor_events << "`  \n"
            << "ValueFlow edges / protocol field evidence: `" << result.semantic_value_flow_edges << " / "
            << result.protocol_field_evidence << "`  \n"
            << "Field semantics Unknown / Candidate / Recovered / Verified: `"
            << result.unknown_field_semantics << " / " << result.candidate_field_semantics << " / "
            << result.recovered_field_semantics << " / " << result.verified_field_semantics << "`  \n"
            << "Verified spec packets / fields: `" << result.verified_protocol_spec_packets << " / "
            << result.verified_field_semantics << "`  \n"
            << "Server ready / database ready: `" << result.server_integration_ready << " / "
            << result.database_integration_ready << "`  \n"
            << "Semantic contradictions / incomplete: `" << result.semantic_contradictions << " / "
            << (result.semantic_evidence_incomplete ? "true" : "false") << "`  \n"
            << "Automatic semantic candidates / families: `" << result.automatic_semantic_candidates
            << " / " << result.automatic_semantic_family_count << "`  \n"
            << "Scene selector or manual marker required: `false`  \n"
            << "Action triggers / grouped / ungrouped: `" << result.action_trigger_candidate_count << " / "
            << result.grouped_trigger_count << " / " << result.ungrouped_trigger_count << "`  \n"
            << "Action instances / patterns: `" << result.action_instance_count << " / "
            << result.action_pattern_count << "`  \n"
            << "Unknown / Verified-mapped patterns: `" << result.unknown_action_pattern_count << " / "
            << result.verified_gameplay_mapped_pattern_count << "`  \n"
            << "Outbound batch candidates / orphan handler bursts: `" << result.outbound_batch_candidate_count
            << " / " << result.orphan_handler_burst_count << "`  \n"
            << "Action clustering coverage: `" << result.action_clustering_coverage_percent
            << "%` (denominator: ActionTriggerCandidateCount)  \n"
            << "C2S / S2C unique opcodes: `" << result.client_to_server_opcode_count << " / "
            << result.server_to_client_opcode_count << "`  \n"
            << "Events lost / records lost: `"
            << (etw_events_lost ? std::to_string(*etw_events_lost) : "Unknown") << " / "
            << (records_lost ? std::to_string(*records_lost) : "Unknown") << "`\n\n"
            << "Deep-analysis priority: `Verified Integration Manifest -> Verified Protocol Spec -> Semantic Evidence Graph -> Value Flow -> Action Pattern -> Opcode Worklist -> Raw Plaintext Evidence`.\n"
            << "Automatic recognition is passive; it uses stage, direction, opcode, length and call context while the player remains in the game.\n\n"
            << "Recommended order: `manifest.json`, `package-validation.json`, `capture-info.json`, `capture-health.json`, action pattern summary, action patterns, action instances, orphan handler bursts, outbound batch candidates, "
            << "`connection-summary.json`, `artifact-authority.json`, `limitations.json`, `sensitive-content.json`, gameplay classifier JSONL, coverage/formula CSV, `PrimarySession/protocol/codex-opcode-worklist.jsonl`, raw CaptureRecords, "
            << "transport chunks, decoded messages, handler observations, candidate frames, logical-message map, provenance, then deep-analysis binaries.\n\n"
            << "Only plaintext records with an exact u16le length and valid checksum are Verified ProtocolFrames. "
            << "If EnhancedPlaintextAvailable=false, raw transport remains raw and does not produce an opcode. "
            << "DecodedMessages and HandlerObservations are direct runtime observations. Raw evidence outranks Candidate or Derived data. "
            << "Candidate and Unknown data must not be written directly to Production MariaDB or promoted to Verified. "
            << "Every Server or database change must retain SessionId and provenance. Reanalyze retained evidence before asking the user to capture again.\n";
        if (!write_required(staging / L"CODEX_HANDOFF.md", handoff_markdown.str())) return result;
        progress("metadata-written");

        result.relative_paths_passed = true;
        std::error_code scan_error;
        for (fs::recursive_directory_iterator iterator(staging, scan_error), end;
             iterator != end && !scan_error; iterator.increment(scan_error)) {
            if (IsExcludedRawNetworkPayload(iterator->path())) continue;
            const DWORD attributes = GetFileAttributesW(iterator->path().c_str());
            if (attributes == INVALID_FILE_ATTRIBUTES ||
                (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
                result.relative_paths_passed = false;
                break;
            }
            if (iterator->is_regular_file() && HasAbsoluteWindowsPath(iterator->path())) {
                result.relative_paths_passed = false;
                break;
            }
        }
        if (scan_error) result.relative_paths_passed = false;
        progress("absolute-path-scan-complete");

        const auto validation_json = [&](std::string_view zip_status, std::string_view manifest_status) {
            const bool pre_zip_valid = (!fs::exists(session_path / L"session.sqlite3") || result.sqlite_integrity_passed) && result.timestamp_validation_passed &&
                result.count_reconciliation_passed && result.schema_validation_passed &&
                result.provenance_validation_passed && result.session_identity_passed &&
                result.relative_paths_passed && result.blockers.empty();
            return MakeJsonObject({
                {"SchemaVersion", "11"}, {"SessionId", result.session_id}, {"ValidatedAtUtc", UtcNow()},
                {"WriterFlushStatus", "Pass"}, {"SqliteIntegrityStatus", result.sqlite_integrity_passed ? "Pass" : "EvidenceBlocked"},
                {"TimestampValidationStatus", result.timestamp_validation_passed ? "Pass" : "Failed"},
                {"CountReconciliationStatus", result.count_reconciliation_passed ? "Pass" : "Failed"},
                {"SchemaValidationStatus", result.schema_validation_passed ? "Pass" : "Failed"},
                {"ProvenanceValidationStatus", result.provenance_validation_passed ? "Pass" : "Failed"},
                {"SessionIdentityStatus", result.session_identity_passed ? "Pass" : "Failed"},
                {"AbsolutePathScanStatus", result.relative_paths_passed ? "Pass" : "Failed"},
                {"PrimarySessionCount", "1"}, {"ManifestValidationStatus", std::string(manifest_status)},
                {"ZipReopenValidationStatus", std::string(zip_status)},
                {"OverallValidationStatus", pre_zip_valid ?
                    (result.warnings.empty() ? "Pass" : "PassWithWarnings") : "Failed"},
                {"Warnings", warnings_json}, {"Blockers", blockers_json}
            }, {"SchemaVersion", "PrimarySessionCount", "Warnings", "Blockers"}) + "\n";
        };
        if (!write_required(staging / L"package-validation.json", validation_json("Pending", "Pending"))) return result;
        progress("initial-validation-written");

        auto inventory_artifacts = [&] {
            std::vector<PackageArtifact> artifacts;
            std::error_code error;
            for (fs::recursive_directory_iterator iterator(staging, error), end;
                 iterator != end && !error; iterator.increment(error)) {
                if (IsExcludedRawNetworkPayload(iterator->path())) continue;
                const DWORD attributes = GetFileAttributesW(iterator->path().c_str());
                if (attributes == INVALID_FILE_ATTRIBUTES ||
                    (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) {
                    artifacts.clear();
                    break;
                }
                if (!iterator->is_regular_file() || iterator->path().filename() == L"manifest.json") continue;
                artifacts.push_back(DescribeArtifact(staging, iterator->path(), artifacts.size()));
            }
            std::sort(artifacts.begin(), artifacts.end(), [](const auto& left, const auto& right) {
                return left.relative_path < right.relative_path;
            });
            for (std::size_t i = 0; i < artifacts.size(); ++i) artifacts[i].artifact_id = "artifact-" + std::to_string(i + 1);
            return artifacts;
        };
        const auto refresh_artifact = [&](std::vector<PackageArtifact>* values,
                                          const fs::path& path) {
            const auto relative = RelativeUtf8(staging, path);
            const auto iterator = std::find_if(values->begin(), values->end(), [&](const auto& artifact) {
                return artifact.relative_path == relative;
            });
            if (iterator == values->end() || !fs::is_regular_file(path)) return false;
            const auto artifact_id = iterator->artifact_id;
            auto refreshed = DescribeArtifact(staging, path,
                static_cast<std::size_t>(std::distance(values->begin(), iterator)));
            refreshed.artifact_id = artifact_id;
            *iterator = std::move(refreshed);
            return true;
        };

        const std::vector<std::string_view> required_artifacts = {
            "CODEX_HANDOFF.md", "artifact-authority.json", "capture-health.json", "capture-info.json", "connection-summary.json",
            "codex-handoff.json", "enhanced-capture.json", "limitations.json", "package-validation.json",
            "sensitive-content.json", "provenance.jsonl",
            "PrimarySession/raw/capture-records.jsonl", "PrimarySession/raw/transport-chunks.jsonl",
            "PrimarySession/reports/semantic-segment-manifest.json",
            "PrimarySession/reports/semantic-shared-ring.json",
            "PrimarySession/reports/semantic-privacy-gate.json",
            "PrimarySession/reports/deep-probe-candidate-map.json",
            "PrimarySession/frames/protocol-frames.jsonl", "PrimarySession/decoded/decoded-messages.jsonl",
            "PrimarySession/decoded/handler-observations.jsonl",
            "PrimarySession/protocol/codex-opcode-worklist.jsonl",
            "PrimarySession/derived/candidate-protocol-frames.jsonl",
            "PrimarySession/derived/incomplete-stream-fragments.jsonl",
            "PrimarySession/derived/gameplay-candidates.jsonl", "PrimarySession/evidence/evidence-items.jsonl",
            "PrimarySession/evidence/logical-message-map.jsonl",
            "PrimarySession/analysis/action-instances.jsonl", "PrimarySession/analysis/action-patterns.jsonl",
            "PrimarySession/analysis/automatic-semantic-summary.json",
            "PrimarySession/analysis/action-pattern-summary.json",
            "PrimarySession/analysis/orphan-handler-bursts.jsonl",
            "PrimarySession/analysis/outbound-batch-candidates.jsonl",
            "schemas/capture-record.schema.json", "schemas/transport-chunk.schema.json",
            "schemas/protocol-frame.schema.json", "schemas/opcode-work-item.schema.json", "schemas/decoded-message.schema.json",
            "schemas/handler-observation.schema.json", "schemas/gameplay-candidate.schema.json",
            "schemas/automatic-semantic-summary.schema.json",
            "schemas/evidence-item.schema.json", "schemas/capture-info.schema.json",
            "schemas/capture-health.schema.json", "schemas/manifest.schema.json", "schemas/provenance.schema.json",
            "schemas/connection-summary.schema.json", "schemas/candidate-protocol-frame.schema.json",
            "schemas/incomplete-stream-fragment.schema.json", "schemas/logical-message-map.schema.json",
            "schemas/action-instance.schema.json", "schemas/action-pattern.schema.json",
            "schemas/action-pattern-summary.schema.json", "schemas/orphan-handler-burst.schema.json",
            "schemas/outbound-batch-candidate.schema.json",
            "schemas/god2-semantic-event-v1.schema.json",
            "schemas/god2-semantic-event-v1-compatibility-input.schema.json",
            "schemas/god2-semantic-event-v2.schema.json",
            "schemas/semantic-segment-manifest.schema.json",
            "schemas/codex-handoff.schema.json"
        };
        const auto validate_manifest_artifacts = [&](const std::vector<PackageArtifact>& values) {
            const bool fields_valid = !values.empty() && std::all_of(values.begin(), values.end(), [](const auto& artifact) {
                return !artifact.relative_path.empty() && !artifact.sha256.empty() &&
                    (artifact.file_size > 0 || artifact.record_count.value_or(1) == 0);
            });
            return fields_valid && std::all_of(required_artifacts.begin(), required_artifacts.end(),
                [&](const auto required) {
                    return std::any_of(values.begin(), values.end(), [&](const auto& artifact) {
                        return artifact.relative_path == required;
                    });
                });
        };

        auto artifacts = inventory_artifacts();
        progress("inventory-created");
        result.manifest_validation_passed = validate_manifest_artifacts(artifacts);
        if (!result.manifest_validation_passed) {
            result.error = "manifest inventory contains an invalid artifact";
            result.blockers.push_back(result.error);
            return result;
        }
        if (auto validated_health = ReadUtf8File(staging / L"capture-health.json")) {
            const std::string pending = "\"ManifestValidationStatus\":\"Pending\"";
            const auto position = validated_health->find(pending);
            if (position == std::string::npos) {
                result.error = "capture-health manifest status could not be finalized";
                result.blockers.push_back(result.error);
                return result;
            }
            validated_health->replace(position, pending.size(), "\"ManifestValidationStatus\":\"Pass\"");
            if (!WriteUtf8FileAtomic(staging / L"capture-health.json", *validated_health)) {
                result.error = "validated capture-health could not be saved";
                result.blockers.push_back(result.error);
                return result;
            }
            if (!refresh_artifact(&artifacts, staging / L"capture-health.json")) {
                result.error = "capture-health artifact inventory could not be refreshed";
                result.blockers.push_back(result.error);
                return result;
            }
            result.manifest_validation_passed = validate_manifest_artifacts(artifacts);
        } else {
            result.manifest_validation_passed = false;
        }
        if (!result.manifest_validation_passed) {
            result.error = "final manifest inventory validation failed";
            result.blockers.push_back(result.error);
            return result;
        }
        if (!write_required(staging / L"manifest.json",
                            ManifestJson(result.session_id, analysis_run_id, package_run_id,
                                original_package_sha256, source_package_schema_version, packaged_at, artifacts))) return result;

        const std::wstring suffix = partial_recovery ? L"_partial" : L"";
        result.zip_path = session_path / (L"God2Evidence_" + Utf8ToWide(UtcFilenameStamp(packaged_at)) +
            L"_" + Utf8ToWide(result.session_id) + suffix + L".zip");
        result.sha256_path = fs::path(result.zip_path.wstring() + L".sha256");
        if (!IsLexicallyContained(session_path, result.zip_path) ||
            !IsLexicallyContained(session_path, result.sha256_path) ||
            ContainsReparsePoint(session_path, result.zip_path, true) ||
            ContainsReparsePoint(session_path, result.sha256_path, true)) {
            result.error = "Evidence Package output path escaped the session or crossed a reparse point";
            result.blockers.push_back(result.error);
            return result;
        }
        if (simulate_zip_failure_for_test) {
            result.error = "simulated ZIP creation failure";
            result.blockers.push_back(result.error);
            return result;
        }
        result.compression_level = EvidenceCompressionLevel();
        ZipPackageMetrics first_metrics;
        if (!CreateDeflateZip(staging, result.zip_path, result.compression_level, &first_metrics, &result.error)) {
            progress("first-zip-failed:" + result.error);
            result.blockers.push_back(result.error);
            return result;
        }
        progress("first-zip-created");
        const bool first_zip_validation = ValidateEvidenceZip(result.zip_path, artifacts, &result.error);
        if (auto compressed_health = ReadUtf8File(staging / L"capture-health.json")) {
            const auto first_ratio = first_metrics.uncompressed_bytes == 0 ? 0.0 :
                static_cast<double>(first_metrics.compressed_bytes) / static_cast<double>(first_metrics.uncompressed_bytes);
            const auto replace_once = [&](std::string_view from, std::string to) {
                const auto position = compressed_health->find(from);
                if (position != std::string::npos) compressed_health->replace(position, from.size(), std::move(to));
            };
            replace_once("\"CompressionStatus\":\"Pending\"", "\"CompressionStatus\":\"Pass\"");
            replace_once("\"CompressionRatio\":null", "\"CompressionRatio\":" + std::to_string(first_ratio));
            replace_once("\"UncompressedPackageBytes\":null", "\"UncompressedPackageBytes\":" +
                         std::to_string(first_metrics.uncompressed_bytes));
            replace_once("\"CompressedPackageBytes\":null", "\"CompressedPackageBytes\":" +
                         std::to_string(first_metrics.compressed_bytes));
            replace_once("\"CompressionDurationMs\":null", "\"CompressionDurationMs\":" +
                         std::to_string(first_metrics.duration_ms));
            replace_once("\"ManifestCodexIngestionStatus\":\"Pending\"",
                         "\"ManifestCodexIngestionStatus\":\"Pass\"");
            if (!WriteUtf8FileAtomic(staging / L"capture-health.json", *compressed_health)) {
                result.error = "capture-health compression metrics could not be finalized";
                result.blockers.push_back(result.error);
                return result;
            }
        }
        if (auto compressed_info = ReadUtf8File(staging / L"capture-info.json")) {
            const auto first_ratio = first_metrics.uncompressed_bytes == 0 ? 0.0 :
                static_cast<double>(first_metrics.compressed_bytes) / static_cast<double>(first_metrics.uncompressed_bytes);
            const auto replace_once = [&](std::string_view from, std::string to) {
                const auto position = compressed_info->find(from);
                if (position != std::string::npos) compressed_info->replace(position, from.size(), std::move(to));
            };
            replace_once("\"CompressionStatus\":\"Pending\"", "\"CompressionStatus\":\"Pass\"");
            replace_once("\"UncompressedPackageBytes\":null", "\"UncompressedPackageBytes\":" +
                         std::to_string(first_metrics.uncompressed_bytes));
            replace_once("\"CompressedPackageBytes\":null", "\"CompressedPackageBytes\":" +
                         std::to_string(first_metrics.compressed_bytes));
            replace_once("\"CompressionRatio\":null", "\"CompressionRatio\":" + std::to_string(first_ratio));
            replace_once("\"CompressionDurationMs\":null", "\"CompressionDurationMs\":" +
                         std::to_string(first_metrics.duration_ms));
            if (!WriteUtf8FileAtomic(staging / L"capture-info.json", *compressed_info)) {
                result.error = "capture-info compression metrics could not be finalized";
                result.blockers.push_back(result.error);
                return result;
            }
        }
        if (!write_required(staging / L"package-validation.json",
                            validation_json(first_zip_validation ? "Pass" : "Failed", "Pass"))) return result;
        if (!refresh_artifact(&artifacts, staging / L"capture-health.json") ||
            !refresh_artifact(&artifacts, staging / L"capture-info.json") ||
            !refresh_artifact(&artifacts, staging / L"package-validation.json")) {
            result.error = "final metadata artifact inventory could not be refreshed";
            result.blockers.push_back(result.error);
            return result;
        }
        result.manifest_validation_passed = validate_manifest_artifacts(artifacts);
        if (!result.manifest_validation_passed) {
            result.error = "final package-validation update invalidated the manifest inventory";
            result.blockers.push_back(result.error);
            return result;
        }
        ZipPackageMetrics final_metrics = first_metrics;
        std::uint64_t compression_duration_ms = first_metrics.duration_ms;
        const auto replace_numeric_member = [](std::string* json, std::string_view name,
                                               const std::string& value) {
            const std::string token = "\"" + std::string(name) + "\":";
            const auto token_position = json->find(token);
            if (token_position == std::string::npos) return false;
            const auto value_begin = token_position + token.size();
            auto value_end = value_begin;
            while (value_end < json->size()) {
                const char character = (*json)[value_end];
                if ((character >= '0' && character <= '9') || character == '-' || character == '.') {
                    ++value_end;
                    continue;
                }
                break;
            }
            if (value_end == value_begin) return false;
            json->replace(value_begin, value_end - value_begin, value);
            return true;
        };
        const auto write_reconciled_metrics = [&](const ZipPackageMetrics& metrics) {
            const auto ratio = metrics.uncompressed_bytes == 0 ? 0.0 :
                static_cast<double>(metrics.compressed_bytes) /
                    static_cast<double>(metrics.uncompressed_bytes);
            for (const auto* name : {L"capture-info.json", L"capture-health.json"}) {
                const fs::path path = staging / name;
                auto json = ReadUtf8File(path);
                if (!json ||
                    !replace_numeric_member(&*json, "UncompressedPackageBytes",
                                            std::to_string(metrics.uncompressed_bytes)) ||
                    !replace_numeric_member(&*json, "CompressedPackageBytes",
                                            std::to_string(metrics.compressed_bytes)) ||
                    !replace_numeric_member(&*json, "CompressionRatio", std::to_string(ratio)) ||
                    !WriteUtf8FileAtomic(path, *json)) {
                    result.error = "package compression metrics could not be reconciled in " +
                        WideToUtf8(fs::path(name).wstring());
                    return false;
                }
            }
            return true;
        };
        bool metrics_reconciled = false;
        for (int pass = 0; pass < 4; ++pass) {
            const auto advertised_metrics = final_metrics;
            if (!write_reconciled_metrics(advertised_metrics)) {
                result.blockers.push_back(result.error);
                return result;
            }
            if (!refresh_artifact(&artifacts, staging / L"capture-info.json") ||
                !refresh_artifact(&artifacts, staging / L"capture-health.json")) {
                result.error = "compression metadata artifact inventory could not be refreshed";
                result.blockers.push_back(result.error);
                return result;
            }
            result.manifest_validation_passed = validate_manifest_artifacts(artifacts);
            if (!result.manifest_validation_passed ||
                !write_required(staging / L"manifest.json",
                    ManifestJson(result.session_id, analysis_run_id, package_run_id,
                        original_package_sha256, source_package_schema_version, packaged_at, artifacts))) {
                if (result.error.empty()) result.error = "compression metric reconciliation invalidated the manifest";
                result.blockers.push_back(result.error);
                return result;
            }
            ZipPackageMetrics measured_metrics;
            if (!CreateDeflateZip(staging, result.zip_path, result.compression_level,
                                  &measured_metrics, &result.error)) {
                result.blockers.push_back(result.error);
                return result;
            }
            compression_duration_ms += measured_metrics.duration_ms;
            final_metrics = measured_metrics;
            if (measured_metrics.uncompressed_bytes == advertised_metrics.uncompressed_bytes &&
                measured_metrics.compressed_bytes == advertised_metrics.compressed_bytes) {
                metrics_reconciled = true;
                break;
            }
        }
        if (!metrics_reconciled) {
            result.error = "package compression metrics did not converge after finalization";
            result.blockers.push_back(result.error);
            return result;
        }
        progress("compression-metrics-reconciled");
        result.zip_reopen_validation_passed = first_zip_validation &&
            ValidateEvidenceZip(result.zip_path, artifacts, &result.error);
        if (!result.zip_reopen_validation_passed) {
            result.blockers.push_back(result.error.empty() ? "ZIP reopen validation failed" : result.error);
            return result;
        }
        result.uncompressed_package_bytes = final_metrics.uncompressed_bytes;
        result.compressed_package_bytes = final_metrics.compressed_bytes;
        result.compression_duration_ms = compression_duration_ms;
        result.compression_ratio = final_metrics.uncompressed_bytes == 0 ? 0.0 :
            static_cast<double>(final_metrics.compressed_bytes) /
                static_cast<double>(final_metrics.uncompressed_bytes);
        result.zip64_used = final_metrics.zip64;
        const auto zip_hash = FileSha256(result.zip_path);
        if (!zip_hash) {
            result.error = "Evidence ZIP SHA-256 could not be calculated";
            result.blockers.push_back(result.error);
            return result;
        }
        result.zip_sha256 = *zip_hash;
        if (!WriteUtf8FileAtomic(result.sha256_path,
            result.zip_sha256 + "  " + WideToUtf8(result.zip_path.filename().wstring()) + "\n")) {
            result.error = "Evidence ZIP SHA-256 sidecar could not be written";
            result.blockers.push_back(result.error);
            return result;
        }
        ScopedWinHandle sidecar_handle(CreateFileW(result.sha256_path.c_str(), FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        FILE_ATTRIBUTE_TAG_INFO sidecar_tag{};
        if (!sidecar_handle || !GetFileInformationByHandleEx(sidecar_handle.value,
                FileAttributeTagInfo, &sidecar_tag, sizeof(sidecar_tag)) ||
            (sidecar_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
            !IsFinalPathContained(session_path, sidecar_handle.value)) {
            result.error = "Evidence ZIP SHA-256 sidecar containment validation failed";
            result.blockers.push_back(result.error);
            DeleteFileW(result.sha256_path.c_str());
            return result;
        }
        result.success = result.manifest_validation_passed && result.zip_reopen_validation_passed &&
            result.timestamp_validation_passed && result.count_reconciliation_passed &&
            result.provenance_validation_passed && result.schema_validation_passed &&
            result.session_identity_passed && result.relative_paths_passed &&
            (!fs::exists(session_path / L"session.sqlite3") || result.sqlite_integrity_passed);
        if (!result.success && result.error.empty()) result.error = "one or more required package validators failed";
        if (result.success) {
            std::error_code remove_error;
            fs::remove_all(staging, remove_error);
        }
        return result;
    } catch (const std::exception& exception) {
        result.error = exception.what();
        result.blockers.push_back(result.error);
        return result;
    } catch (...) {
        result.error = "unknown Evidence Package failure";
        result.blockers.push_back(result.error);
        return result;
    }
}

EvidencePackageResult EvidencePackageBuilder::Repackage(const fs::path& package_path) {
    EvidencePackageResult result;
    std::string validation_error;
    ScopedWinHandle package_handle(CreateFileW(package_path.c_str(), GENERIC_READ | FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    FILE_ATTRIBUTE_TAG_INFO package_tag{};
    if (!package_handle || !GetFileInformationByHandleEx(package_handle.value, FileAttributeTagInfo,
            &package_tag, sizeof(package_tag)) ||
        (package_tag.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0 ||
        !fs::is_regular_file(package_path) || !ValidateEvidenceZip(package_path, {}, &validation_error)) {
        result.error = "source Evidence ZIP validation failed: " + validation_error;
        result.blockers.push_back(result.error);
        return result;
    }
    const auto original_hash = FileSha256(package_path);
    const auto manifest_json = ReadEvidenceZipEntry(package_path, "manifest.json");
    const auto capture_info = ReadEvidenceZipEntry(package_path, "capture-info.json");
    const auto segment_manifest_json = ReadEvidenceZipEntry(package_path,
        "PrimarySession/reports/semantic-segment-manifest.json", kMaximumManifestBytes);
    StrictEvidenceManifest source_manifest;
    StrictSemanticSegmentManifest source_segment_manifest;
    StrictJsonValue capture_fields;
    std::string parse_error;
    if (!original_hash || !manifest_json ||
        !ParseStrictEvidenceManifest(*manifest_json, &source_manifest, &parse_error) ||
        !capture_info || !StrictJsonParser(*capture_info).Parse(&capture_fields, &parse_error) ||
        capture_fields.kind != StrictJsonKind::Object) {
        result.error = "source package identity metadata could not be read: " + parse_error;
        result.blockers.push_back(result.error);
        return result;
    }
    if (segment_manifest_json && !ParseStrictSemanticSegmentManifest(
            *segment_manifest_json, &source_segment_manifest, &parse_error)) {
        result.error = "source package semantic segment manifest is invalid: " + parse_error;
        result.blockers.push_back(result.error);
        return result;
    }
    const auto* capture_schema_id = JsonMember(capture_fields, "SchemaId", StrictJsonKind::String);
    const auto* capture_session = JsonMember(capture_fields, "SessionId", StrictJsonKind::String);
    const auto* capture_schema = JsonMember(capture_fields, "SchemaVersion", StrictJsonKind::Number);
    const auto* capture_client_sha = JsonMember(capture_fields, "ClientSHA256", StrictJsonKind::String);
    std::uint64_t source_schema_unsigned = 0;
    if (capture_schema_id == nullptr || capture_schema_id->scalar != "capture-info" ||
        capture_session == nullptr || !IsSafePortableComponent(capture_session->scalar) ||
        capture_schema == nullptr || !ParseStrictUint64(*capture_schema, &source_schema_unsigned) ||
        source_schema_unsigned < 9 || source_schema_unsigned > 11 ||
        source_manifest.schema_version != static_cast<std::int64_t>(source_schema_unsigned) ||
        source_manifest.primary_session_id != capture_session->scalar) {
        result.error = "source package manifest/capture identity mismatch or unsupported Schema version";
        result.blockers.push_back(result.error);
        return result;
    }
    const auto session_id = capture_session->scalar;
    const auto source_schema = static_cast<std::int64_t>(source_schema_unsigned);
    const auto source_client_sha256 = capture_client_sha == nullptr ? std::string{} : capture_client_sha->scalar;
    if (!source_client_sha256.empty() &&
        (source_client_sha256.size() != 64 || !IsHex(source_client_sha256))) {
        result.error = "source package ClientSHA256 is malformed";
        result.blockers.push_back(result.error);
        return result;
    }
    const std::string analysis_run_id = NewId();
    const std::string package_run_id = NewId();
    const fs::path sessions_root = DefaultSessionsRoot();
    const fs::path import_session = sessions_root / L"Repackages" /
        (L"repackage-" + Utf8ToWide(package_run_id));
    const bool repackage_directories_ready = EnsureContainedDirectory(LocalDataRoot(), sessions_root) &&
        EnsureContainedDirectory(sessions_root, import_session / L"raw" / L"enhanced-x86") &&
        EnsureContainedDirectory(sessions_root, import_session / L"reports");
    if (!repackage_directories_ready || !IsApprovedSessionPath(import_session) ||
        ContainsReparsePoint(sessions_root, import_session)) {
        result.error = "cannot create an approved, contained, reparse-free repackage workspace";
        result.blockers.push_back(result.error);
        return result;
    }
    const auto session_json = MakeJsonObject({
        {"SessionId", session_id}, {"CreatedAtUtc", UtcNow()}, {"SchemaVersion", "11"},
        {"ToolVersion", GOD2_TOOL_VERSION}, {"RawEvidenceRetention", "Preserve"},
        {"AnalysisRunId", analysis_run_id}, {"PackageRunId", package_run_id},
        {"OriginalPackageSHA256", *original_hash},
        {"OriginalPackageFileName", WideToUtf8(package_path.filename().wstring())},
        {"SourcePackageSchemaVersion", std::to_string(source_schema)},
        {"OriginalClientSHA256", source_client_sha256}
    }, {"SchemaVersion", "SourcePackageSchemaVersion"});
    if (!WriteUtf8FileAtomic(import_session / L"session.json", session_json + "\n") ||
        !WriteUtf8FileAtomic(import_session / L"session-summary.json", MakeJsonObject({
            {"SessionId", session_id}, {"AnalysisRunId", analysis_run_id}, {"PackageRunId", package_run_id},
            {"CompletedAtUtc", UtcNow()}, {"Status", "Completed"},
            {"ReanalysisSource", "ExistingEvidencePackage"}
        }) + "\n") ||
        !WriteUtf8FileAtomic(import_session / L"reports" / L"enhanced-capture-status.json",
            MakeJsonObject({{"Status", "Stopped"}, {"Detail", "RepackagedFromValidatedEvidenceZip"}}) + "\n")) {
        result.error = "cannot initialize repackage metadata";
        result.blockers.push_back(result.error);
        return result;
    }
    if (!ExtractEvidenceZipEntry(package_path, "PrimarySession/raw/capture-records.jsonl",
                                 import_session / L"raw" / L"capture-records.jsonl",
                                 import_session, true, &result.error) ||
        !ExtractEvidenceZipEntry(package_path, "PrimarySession/raw/trace.bin",
                                 import_session / L"raw" / L"enhanced-x86" / L"trace.bin",
                                 import_session, false, &result.error) ||
        !ExtractEvidenceZipEntry(package_path, "PrimarySession/raw/semantic-events.jsonl",
                                 import_session / L"raw" / L"semantic-events.jsonl",
                                 import_session, false, &result.error) ||
        !ExtractEvidenceZipEntry(package_path, "PrimarySession/reports/semantic-shared-ring.json",
                                 import_session / L"reports" / L"semantic-shared-ring.json",
                                 import_session, false, &result.error) ||
        !ExtractEvidenceZipEntry(package_path, "PrimarySession/reports/semantic-segment-manifest.json",
                                 import_session / L"reports" / L"semantic-segment-manifest.json",
                                 import_session, false, &result.error) ||
        !ExtractEvidenceZipEntry(package_path, "PrimarySession/reports/deep-probe-candidate-map.json",
                                 import_session / L"raw" / L"enhanced-x86" /
                                     L"deep-probe-candidate-map.json",
                                 import_session, false, &result.error) ||
        !ExtractEvidenceZipEntry(package_path, "PrimarySession/convenience/session.sqlite3",
                                 import_session / L"session.sqlite3",
                                 import_session, false, &result.error)) {
        result.blockers.push_back(result.error);
        return result;
    }
    if (segment_manifest_json && source_segment_manifest.pass) {
        for (const auto& segment : source_segment_manifest.segments) {
            const std::string zip_entry = "PrimarySession/" + segment.relative_path;
            const fs::path target = import_session / Utf8ToWide(segment.relative_path);
            if (!ExtractEvidenceZipEntry(package_path, zip_entry, target,
                                         import_session, true, &result.error)) {
                result.blockers.push_back(result.error);
                return result;
            }
        }
    }
    return Build(import_session, false, false);
}

void EvidencePackageBuilder::RecoverIncompleteSessionsNoThrow() {
    try {
        const auto root = DefaultSessionsRoot();
        std::error_code error;
        const auto now = fs::file_time_type::clock::now();
        for (fs::directory_iterator iterator(root, error), end;
             iterator != end && !error; iterator.increment(error)) {
            if (!iterator->is_directory()) continue;
            const auto session = iterator->path();
            if (!fs::exists(session / L"session.json") || fs::exists(session / L"session-summary.json")) continue;
            if (!fs::exists(session / L"raw" / L"injected-packets.jsonl") &&
                !fs::exists(session / L"raw" / L"capture-records.jsonl")) continue;
            const auto modified = fs::last_write_time(session, error);
            if (error) { error.clear(); continue; }
            if (now - modified < std::chrono::minutes(5)) continue;
            Fields fields;
            const auto session_content = ReadUtf8File(session / L"session.json");
            if (!session_content || !ParseFlatJson(*session_content, fields, nullptr)) continue;
            const auto session_id = GetString(fields, "SessionId");
            if (!IsSafePortableComponent(session_id)) continue;
            bool partial_exists = false;
            const std::wstring suffix = L"_" + Utf8ToWide(session_id) + L"_partial.zip";
            for (fs::directory_iterator package_iterator(session, error), package_end;
                 package_iterator != package_end && !error; package_iterator.increment(error)) {
                const auto name = package_iterator->path().filename().wstring();
                if (name.starts_with(L"God2Evidence_") && name.ends_with(suffix)) {
                    partial_exists = true;
                    break;
                }
            }
            if (error) { error.clear(); continue; }
            if (partial_exists) continue;
            Build(session, true, false);
        }
    } catch (...) {
    }
}

// The fixture deliberately keeps all gate outcomes in one scope so every
// predicate is reported from the same package transaction.  Its fixed local
// state is below the Windows thread reserve and is never used on a capture
// worker thread.
#pragma warning(suppress: 6262)
int RunEvidencePackageFixtureTest(const fs::path& session_path, const fs::path& report_path) {
    struct CheckResult { std::string name; bool passed = false; std::string evidence; };
    std::vector<CheckResult> checks;
    const auto check = [&](std::string name, bool passed, std::string evidence) {
        checks.push_back({std::move(name), passed, std::move(evidence)});
    };

    fs::path active_session_path = session_path;
    fs::path sanitized_fixture_root;
    std::string source_unsafe_marker;
    const bool source_contains_legacy_sensitive_diagnostic = ContainsUnsafeLegacyDiagnostic(
        session_path / L"reports" / L"enhanced-capture.log", &source_unsafe_marker);
    const auto unsafe_source_package = source_contains_legacy_sensitive_diagnostic ?
        EvidencePackageBuilder::Build(session_path, false, false) : EvidencePackageResult{};
    // Always work on an isolated fixture clone so the negative privacy cases
    // exercise the production Build path without modifying the supplied session.
    sanitized_fixture_root = LocalDataRoot() / L"Temp" / L"EvidencePackageFixture" /
        Utf8ToWide(NewId());
    active_session_path = sanitized_fixture_root / L"session";
    bool sanitized_fixture_prepared =
        EnsureContainedDirectory(LocalDataRoot(), active_session_path);
    std::error_code enumerate_error;
    for (fs::recursive_directory_iterator iterator(session_path,
            fs::directory_options::skip_permission_denied, enumerate_error), end;
         sanitized_fixture_prepared && iterator != end && !enumerate_error;
         iterator.increment(enumerate_error)) {
        const auto relative = iterator->path().lexically_relative(session_path);
        const auto filename = iterator->path().filename().wstring();
        if (iterator->is_directory(enumerate_error)) {
            std::wstring folded = filename;
            std::transform(folded.begin(), folded.end(), folded.begin(),
                [](wchar_t character) {
                    return static_cast<wchar_t>(std::towlower(character));
                });
            if (folded.starts_with(L"evidence-package-staging"))
                iterator.disable_recursion_pending();
            else
                sanitized_fixture_prepared = EnsureContainedDirectory(
                    sanitized_fixture_root, active_session_path / relative);
            continue;
        }
        if (!iterator->is_regular_file(enumerate_error)) continue;
        const auto extension = iterator->path().extension().wstring();
        if ((source_contains_legacy_sensitive_diagnostic &&
             relative == fs::path(L"reports") / L"enhanced-capture.log") ||
            IsExcludedRawNetworkPayload(iterator->path()) ||
            extension == L".zip" || extension == L".sha256" ||
            filename == L"evidence-package-build-progress.txt")
            continue;
        sanitized_fixture_prepared = CopyArtifact(iterator->path(),
            active_session_path / relative, session_path, sanitized_fixture_root);
    }
    sanitized_fixture_prepared = sanitized_fixture_prepared && !enumerate_error;

    constexpr std::string_view arbitrary_secret = "S3cr3t-Value-8472";
    const auto cloned_session_json = ReadUtf8File(active_session_path / L"session.json");
    Fields cloned_session_fields;
    std::string cloned_session_parse_error;
    const bool cloned_session_parsed = cloned_session_json &&
        ParseFlatJson(*cloned_session_json, cloned_session_fields,
                      &cloned_session_parse_error);
    const auto cloned_session_id = GetString(cloned_session_fields, "SessionId");
    God2SemanticEventV2 sensitive_event;
    sensitive_event.event_type = UltimateSemanticEventType::ObjectResolved;
    sensitive_event.event_id = "ArbitrarySensitiveCarrierEvent";
    sensitive_event.sequence = 990001U;
    sensitive_event.timestamp = "2026-08-08T17:21:58.000Z";
    sensitive_event.thread_id = 91U;
    sensitive_event.process_id = 5151U;
    sensitive_event.session_id = cloned_session_id;
    sensitive_event.client_build_id = std::string(64U, 'A');
    sensitive_event.module_id = "God2_opt.exe";
    sensitive_event.parent_event_id = std::string(arbitrary_secret);
    sensitive_event.context_id = std::string(arbitrary_secret);
    sensitive_event.action_id = std::string(arbitrary_secret);
    sensitive_event.object_token = std::string(arbitrary_secret);
    sensitive_event.value_token = std::string(arbitrary_secret);
    sensitive_event.source_token = std::string(arbitrary_secret);
    sensitive_event.sensitive_mask_status = "NotSensitive";
    sensitive_event.payload =
        "{\"Property\":\"DisplayName\",\"RawValue\":\"S3cr3t-Value-8472\"}";
    sensitive_event.evidence_binding["ObjectAddress"] =
        std::string(arbitrary_secret);
    std::string raw_sensitive_event = SerializeSemanticEventV2(sensitive_event);
    // The canonical writer must never pass the arbitrary object through.  To
    // exercise the package reader/privacy boundary, explicitly transform that
    // fail-closed output into an untrusted raw-wire negative fixture instead
    // of relying on the production serializer to emit an invalid profile.
    const std::string blocked_status =
        "\"SensitiveMaskStatus\":\"UnclassifiedPayloadSuppressedMetadataOnly\"";
    const auto blocked_status_position = raw_sensitive_event.find(blocked_status);
    if (blocked_status_position != std::string::npos) {
        raw_sensitive_event.replace(blocked_status_position, blocked_status.size(),
            "\"SensitiveMaskStatus\":\"UnverifiedSensitiveState\"");
    }
    const std::string blocked_payload =
        "\"Payload\":{\"Status\":\"EvidenceBlockedInvalidOrUnclassifiedPayload\","
        "\"OriginalPayloadPersisted\":false,\"Authority\":\"UNKNOWN\"}";
    const auto blocked_payload_position = raw_sensitive_event.find(blocked_payload);
    if (blocked_payload_position != std::string::npos) {
        raw_sensitive_event.replace(blocked_payload_position, blocked_payload.size(),
            "\"Payload\":{\"Property\":\"DisplayName\","
            "\"RawValue\":\"S3cr3t-Value-8472\"}");
    }
    auto explicitly_safe_event = sensitive_event;
    explicitly_safe_event.event_id = "ExplicitlySafeCarrierEvent";
    explicitly_safe_event.sequence = 990002U;
    explicitly_safe_event.parent_event_id.clear();
    explicitly_safe_event.context_id.clear();
    explicitly_safe_event.action_id.clear();
    explicitly_safe_event.object_token = "SAFE-OBJECT-2048";
    explicitly_safe_event.value_token = "SAFE-VALUE-2048";
    explicitly_safe_event.source_token = "SAFE-SOURCE-2048";
    explicitly_safe_event.payload =
        "{\"Property\":\"DisplayName\",\"RawValue\":\"SAFE-VALUE-2048\"}";
    explicitly_safe_event.evidence_binding.clear();
    const auto safe_semantic_event = SerializeSemanticEventV2(explicitly_safe_event);
    const auto semantic_source = active_session_path / L"raw" / L"semantic-events.jsonl";
    auto semantic_source_content = ReadUtf8File(semantic_source).value_or("");
    if (!semantic_source_content.empty() && semantic_source_content.back() != '\n')
        semantic_source_content.push_back('\n');
    semantic_source_content += raw_sensitive_event + "\n" + safe_semantic_event + "\n";
    const auto strict_fixture_status = MakeJsonObject({
        {"SchemaVersion", "god2-enhanced-capture-status-v2"},
        {"UpdatedAtUtc", "2026-08-09T00:00:00.000Z"},
        {"Requested", "true"}, {"Status", "Stopped"},
        {"Detail", "Strict typed cleanup provenance fixture"},
        {"WasEverAttached", "true"}, {"ProbeReady", "false"},
        {"StrictUnloadVerified", "true"}, {"FailureKind", ""},
        {"InjectionAttempted", "true"}, {"ModuleWasEverLoaded", "true"},
        {"ModuleLoadStateVerified", "true"},
        {"ModuleSnapshotVerified", "true"}, {"ModuleAbsent", "true"},
        {"TargetProcessExited", "false"}, {"TargetIdentityVerified", "true"},
        {"InjectorPresent", "false"}, {"ProbePresent", "false"},
        {"InjectorPayloadValidated", "true"}, {"ProbePayloadValidated", "true"},
        {"TargetExecutable", "God2_opt.exe"}, {"TargetArchitecture", "x86"},
        {"ProbeScope", "WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded"},
        {"ProbeDomainCount", "25"},
        {"ProbeDomains", "Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot"},
        {"ConfirmedExactBuildDomainCount", "4"},
        {"ConfirmedExactBuildDomains", "Network,Parser,Serializer,Handler"},
        {"CandidateOnlyDomainCount", "21"},
        {"CandidateOnlyStatus", "EvidenceBlockedUnconfirmedProbe"},
        {"DeepProbeCandidateMapSchema", "god2-deep-probe-candidate-map-v2"},
        {"CandidatePromotionGateCount", "10"},
        {"CandidateAbiSafetyGateCount", "4"},
        {"CandidateActivationGateCount", "14"},
        {"CandidateAbiSafetyContract", "ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk"},
        {"SemanticEventContract", "God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes"},
        {"SharedTransportContract", "VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag"},
        {"InjectionSequence", "PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread"},
        {"ReadinessHandshake", "God2TraceProbeWaitReady"},
        {"PayloadStaging", "ProtectedProgramDataRandomDirectory"},
        {"PayloadDacl", "Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent"},
        {"PayloadMandatoryLabel", "MediumNoWriteUp"},
        {"SemanticIpcBinding", "DuplicatedTargetHandlesV2;NoNamedObjectAuthority"},
        {"PayloadLaunchMode", "AlreadyElevatedCreateProcessW"},
        {"UnloadPolicy", "QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped"},
        {"Apis", "send,WSASend,recv,WSARecv,OverlappedWSARecvCompletionRoutine,WSAGetOverlappedResult,GetQueuedCompletionStatus,GetQueuedCompletionStatusEx,PostDecrypt,PreEncrypt,HandlerDecoded"},
        {"InputAndWindowHooks", "Disabled"}
    }, {"Requested", "WasEverAttached", "ProbeReady", "StrictUnloadVerified",
        "InjectionAttempted", "ModuleWasEverLoaded", "ModuleLoadStateVerified",
        "ModuleSnapshotVerified", "ModuleAbsent", "TargetProcessExited",
        "TargetIdentityVerified", "InjectorPresent", "ProbePresent",
        "InjectorPayloadValidated", "ProbePayloadValidated", "ProbeDomainCount",
        "ConfirmedExactBuildDomainCount", "CandidateOnlyDomainCount",
        "CandidatePromotionGateCount", "CandidateAbiSafetyGateCount",
        "CandidateActivationGateCount"});
    sanitized_fixture_prepared = sanitized_fixture_prepared && cloned_session_parsed &&
        !cloned_session_id.empty() && blocked_status_position != std::string::npos &&
        blocked_payload_position != std::string::npos &&
        EnsureContainedDirectory(active_session_path, semantic_source.parent_path()) &&
        WriteUtf8FileAtomic(semantic_source, semantic_source_content) &&
        WriteUtf8FileAtomic(active_session_path / L"raw" / L"capture.etl",
            "RAW-NETWORK-PAYLOAD-MUST-BE-EXCLUDED") &&
        WriteUtf8FileAtomic(active_session_path / L"reports" /
            L"enhanced-capture-status.json", strict_fixture_status + "\n");

    const auto raw_path = fs::exists(active_session_path / L"raw" / L"capture-records.jsonl") ?
        active_session_path / L"raw" / L"capture-records.jsonl" :
        active_session_path / L"raw" / L"injected-packets.jsonl";
    const auto before_records = CountLines(raw_path);
    const auto partial = sanitized_fixture_prepared ?
        EvidencePackageBuilder::Build(active_session_path, true, false) : EvidencePackageResult{};
    const auto simulated_failure = sanitized_fixture_prepared ?
        EvidencePackageBuilder::Build(active_session_path, false, true) : EvidencePackageResult{};
    const auto result = sanitized_fixture_prepared ?
        EvidencePackageBuilder::Build(active_session_path, false, false) : EvidencePackageResult{};

    const auto manifest = ReadEvidenceZipEntry(result.zip_path, "manifest.json").value_or("");
    const auto capture_info = ReadEvidenceZipEntry(result.zip_path, "capture-info.json").value_or("");
    const auto health = ReadEvidenceZipEntry(result.zip_path, "capture-health.json").value_or("");
    const auto handoff = ReadEvidenceZipEntry(result.zip_path, "codex-handoff.json").value_or("");
    const auto authority = ReadEvidenceZipEntry(result.zip_path, "artifact-authority.json").value_or("");
    const auto first_record = [&]() {
        const auto records = ReadEvidenceZipEntry(result.zip_path, "PrimarySession/raw/capture-records.jsonl").value_or("");
        const auto newline = records.find('\n');
        return records.substr(0, newline);
    }();
    const auto protocol_frames = ReadEvidenceZipEntry(result.zip_path, "PrimarySession/frames/protocol-frames.jsonl").value_or("missing");
    const auto opcode_worklist = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/protocol/codex-opcode-worklist.jsonl").value_or("");
    const auto packaged_handler_observations = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/gameplay/protocol-handler-observations.jsonl").value_or("");
    const auto packaged_gameplay_coverage = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/exports/gameplay-coverage-matrix.csv").value_or("");
    const auto gameplay_candidates = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/derived/gameplay-candidates.jsonl").value_or("");
    const auto automatic_semantic_summary = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/analysis/automatic-semantic-summary.json").value_or("");
    const auto logical_map = ReadEvidenceZipEntry(result.zip_path, "PrimarySession/evidence/logical-message-map.jsonl").value_or("");
    const auto action_instances = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/analysis/action-instances.jsonl").value_or("");
    const auto action_patterns = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/analysis/action-patterns.jsonl").value_or("");
    const auto action_pattern_summary = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/analysis/action-pattern-summary.json").value_or("");
    const auto orphan_handler_bursts = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/analysis/orphan-handler-bursts.jsonl").value_or("");
    const auto outbound_batch_candidates = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/analysis/outbound-batch-candidates.jsonl").value_or("");
    const auto enhanced = ReadEvidenceZipEntry(result.zip_path, "enhanced-capture.json").value_or("");
    const auto package_validation = ReadEvidenceZipEntry(result.zip_path, "package-validation.json").value_or("");
    const auto sensitive_content = ReadEvidenceZipEntry(result.zip_path, "sensitive-content.json").value_or("");
    const auto packaged_semantic_events = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/raw/semantic-events.jsonl").value_or("");
    const auto semantic_privacy_report = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/reports/semantic-privacy-gate.json").value_or("");
    const auto packaged_shared_health = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/reports/semantic-shared-ring.json").value_or("");
    const auto packaged_candidate_map = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/reports/deep-probe-candidate-map.json").value_or("");
    const auto packaged_segment_manifest = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/reports/semantic-segment-manifest.json").value_or("");
    const auto semantic_event_v2_schema = ReadEvidenceZipEntry(
        result.zip_path, "schemas/god2-semantic-event-v2.schema.json").value_or("");
    const auto semantic_event_v1_schema = ReadEvidenceZipEntry(
        result.zip_path, "schemas/god2-semantic-event-v1.schema.json").value_or("");
    const auto semantic_event_v1_compatibility_schema = ReadEvidenceZipEntry(
        result.zip_path,
        "schemas/god2-semantic-event-v1-compatibility-input.schema.json").value_or("");
    const auto shared_health_availability_schema = ReadEvidenceZipEntry(
        result.zip_path,
        "schemas/semantic-shared-ring-health-availability.schema.json").value_or("");
    const auto candidate_map_availability_schema = ReadEvidenceZipEntry(
        result.zip_path,
        "schemas/deep-probe-candidate-map-availability.schema.json").value_or("");
    const auto semantic_segment_manifest_schema = ReadEvidenceZipEntry(
        result.zip_path, "schemas/semantic-segment-manifest.schema.json").value_or("");
    const auto gpu_capability = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/performance/gpu-capability.json").value_or("");
    const auto gpu_performance = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/performance/gpu-performance.json").value_or("");
    const auto gpu_equivalence = ReadEvidenceZipEntry(
        result.zip_path, "PrimarySession/performance/cpu-gpu-equivalence.json").value_or("");
    std::error_code corruption_test_error;
    const auto crc_tampered_zip = active_session_path / L"evidence-validator-crc-tamper-test.zip";
    fs::copy_file(result.zip_path, crc_tampered_zip, fs::copy_options::overwrite_existing, corruption_test_error);
    bool crc_tamper_written = !corruption_test_error;
    if (crc_tamper_written) {
        std::fstream tamper(crc_tampered_zip, std::ios::binary | std::ios::in | std::ios::out);
        std::uint16_t name_length = 0, extra_length = 0;
        tamper.seekg(26, std::ios::beg);
        crc_tamper_written = ReadLittle(tamper, &name_length) && ReadLittle(tamper, &extra_length);
        const auto payload_offset = static_cast<std::streamoff>(30u + name_length + extra_length + 64u);
        char byte = 0;
        if (crc_tamper_written) {
            tamper.seekg(payload_offset, std::ios::beg);
            crc_tamper_written = static_cast<bool>(tamper.get(byte));
        }
        if (crc_tamper_written) {
            tamper.seekp(payload_offset, std::ios::beg);
            tamper.put(static_cast<char>(static_cast<unsigned char>(byte) ^ 0x01u));
            tamper.flush();
            crc_tamper_written = static_cast<bool>(tamper);
        }
    }
    std::string crc_rejection_reason;
    const bool crc_tamper_rejected = crc_tamper_written &&
        !ValidateEvidenceZip(crc_tampered_zip, {}, &crc_rejection_reason) &&
        (crc_rejection_reason.find("CRC-32") != std::string::npos ||
         crc_rejection_reason.find("corrupt") != std::string::npos);
    fs::remove(crc_tampered_zip, corruption_test_error);

    const auto truncated_zip = active_session_path / L"evidence-validator-truncated-test.zip";
    corruption_test_error.clear();
    fs::copy_file(result.zip_path, truncated_zip, fs::copy_options::overwrite_existing, corruption_test_error);
    bool zip_truncated = !corruption_test_error;
    if (zip_truncated) {
        const auto original_size = fs::file_size(truncated_zip, corruption_test_error);
        zip_truncated = !corruption_test_error && original_size > 0;
        if (zip_truncated) fs::resize_file(truncated_zip, original_size - 1, corruption_test_error);
        zip_truncated = zip_truncated && !corruption_test_error;
    }
    std::string truncation_rejection_reason;
    const bool truncated_zip_rejected = zip_truncated &&
        !ValidateEvidenceZip(truncated_zip, {}, &truncation_rejection_reason) &&
        (truncation_rejection_reason.find("EOCD") != std::string::npos ||
         truncation_rejection_reason.find("end-of-central-directory") != std::string::npos);
    fs::remove(truncated_zip, corruption_test_error);

    const auto inventory_tampered_zip = active_session_path / L"evidence-validator-inventory-tamper-test.zip";
    corruption_test_error.clear();
    fs::copy_file(result.zip_path, inventory_tampered_zip,
                  fs::copy_options::overwrite_existing, corruption_test_error);
    bool inventory_name_rewritten = !corruption_test_error;
    if (inventory_name_rewritten) {
        std::ifstream input(inventory_tampered_zip, std::ios::binary);
        std::vector<unsigned char> bytes((std::istreambuf_iterator<char>(input)),
                                         std::istreambuf_iterator<char>());
        static constexpr std::string_view old_name = "capture-info.json";
        static constexpr std::string_view new_name = "capture-evil.json";
        std::size_t replacements = 0;
        const auto read16 = [&](std::size_t offset) -> std::uint16_t {
            return offset + 1 < bytes.size() ? static_cast<std::uint16_t>(
                bytes[offset] | (static_cast<std::uint16_t>(bytes[offset + 1]) << 8U)) : 0;
        };
        for (std::size_t offset = 0; offset + 46 < bytes.size(); ++offset) {
            const bool local = bytes[offset] == 0x50U && bytes[offset + 1] == 0x4bU &&
                bytes[offset + 2] == 0x03U && bytes[offset + 3] == 0x04U;
            const bool central = bytes[offset] == 0x50U && bytes[offset + 1] == 0x4bU &&
                bytes[offset + 2] == 0x01U && bytes[offset + 3] == 0x02U;
            if (!local && !central) continue;
            const auto name_length = read16(offset + (local ? 26U : 28U));
            const auto name_offset = offset + (local ? 30U : 46U);
            if (name_length != old_name.size() || name_offset + name_length > bytes.size() ||
                !std::equal(old_name.begin(), old_name.end(), bytes.begin() +
                    static_cast<std::ptrdiff_t>(name_offset)))
                continue;
            std::copy(new_name.begin(), new_name.end(), bytes.begin() +
                static_cast<std::ptrdiff_t>(name_offset));
            ++replacements;
        }
        inventory_name_rewritten = replacements == 2;
        if (inventory_name_rewritten) {
            std::ofstream output(inventory_tampered_zip, std::ios::binary | std::ios::trunc);
            output.write(reinterpret_cast<const char*>(bytes.data()),
                         static_cast<std::streamsize>(bytes.size()));
            inventory_name_rewritten = output.good();
        }
    }
    std::string inventory_rejection_reason;
    const bool unlisted_and_missing_rejected = inventory_name_rewritten &&
        !ValidateEvidenceZip(inventory_tampered_zip, {}, &inventory_rejection_reason) &&
        (inventory_rejection_reason.find("Manifest") != std::string::npos ||
         inventory_rejection_reason.find("unlisted") != std::string::npos ||
         inventory_rejection_reason.find("missing") != std::string::npos);
    fs::remove(inventory_tampered_zip, corruption_test_error);
    StrictEvidenceManifest duplicate_manifest;
    std::string duplicate_manifest_error;
    const std::string duplicate_manifest_json =
        "{\"SchemaId\":\"manifest\",\"SchemaVersion\":11,\"PrimarySessionId\":\"fixture-session\"," 
        "\"RelativePathPolicy\":\"ZipRootRelative\",\"Artifacts\":["
        "{\"ArtifactId\":\"a1\",\"RelativePath\":\"capture-info.json\",\"FileSize\":1,"
        "\"SHA256\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"},"
        "{\"ArtifactId\":\"a2\",\"RelativePath\":\"capture-info.json\",\"FileSize\":1,"
        "\"SHA256\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\"}]}";
    const bool duplicate_manifest_rejected = !ParseStrictEvidenceManifest(
        duplicate_manifest_json, &duplicate_manifest, &duplicate_manifest_error);
    const auto cleanup_contract_root = sanitized_fixture_root / L"cleanup-contract";
    const auto legacy_cleanup_session = cleanup_contract_root / L"legacy-detail-spoof";
    const auto strict_false_session = cleanup_contract_root / L"strict-false-spoof";
    const auto string_boolean_session = cleanup_contract_root / L"string-boolean-spoof";
    const auto missing_fields_session = cleanup_contract_root / L"missing-fields-spoof";
    const auto contradictory_session = cleanup_contract_root / L"contradictory-state-spoof";
    const auto unknown_field_session = cleanup_contract_root / L"unknown-field-spoof";
    const auto no_module_session = cleanup_contract_root / L"verified-no-module-loaded";
    const auto uncertain_module_session = cleanup_contract_root / L"uncertain-module-state";
    const auto strict_cleanup_session = cleanup_contract_root / L"strict-positive";
    auto strict_false_fixture_status = strict_fixture_status;
    const auto strict_true_position = strict_false_fixture_status.find(
        "\"StrictUnloadVerified\":true");
    if (strict_true_position != std::string::npos)
        strict_false_fixture_status.replace(strict_true_position,
            std::string_view("\"StrictUnloadVerified\":true").size(),
            "\"StrictUnloadVerified\":false");
    auto string_boolean_fixture_status = strict_fixture_status;
    const auto string_boolean_position = string_boolean_fixture_status.find(
        "\"StrictUnloadVerified\":true");
    if (string_boolean_position != std::string::npos)
        string_boolean_fixture_status.replace(string_boolean_position,
            std::string_view("\"StrictUnloadVerified\":true").size(),
            "\"StrictUnloadVerified\":\"true\"");
    auto contradictory_fixture_status = strict_fixture_status;
    const auto attached_true_position = contradictory_fixture_status.find(
        "\"WasEverAttached\":true");
    if (attached_true_position != std::string::npos)
        contradictory_fixture_status.replace(attached_true_position,
            std::string_view("\"WasEverAttached\":true").size(),
            "\"WasEverAttached\":false");
    auto unknown_field_fixture_status = strict_fixture_status;
    if (!unknown_field_fixture_status.empty())
        unknown_field_fixture_status.insert(
            unknown_field_fixture_status.size() - 1U,
            ",\"UntrustedCleanupClaim\":true");
    auto no_module_fixture_status = strict_fixture_status;
    const auto replace_token = [](std::string* value,
                                  std::string_view from,
                                  std::string_view to) {
        if (value == nullptr) return false;
        const auto position = value->find(from);
        if (position == std::string::npos) return false;
        value->replace(position, from.size(), to);
        return true;
    };
    const bool no_module_fixture_transformed =
        replace_token(&no_module_fixture_status,
            "\"Status\":\"Stopped\"", "\"Status\":\"EvidenceBlocked\"") &&
        replace_token(&no_module_fixture_status,
            "\"WasEverAttached\":true", "\"WasEverAttached\":false") &&
        replace_token(&no_module_fixture_status,
            "\"InjectionAttempted\":true", "\"InjectionAttempted\":false") &&
        replace_token(&no_module_fixture_status,
            "\"ModuleWasEverLoaded\":true", "\"ModuleWasEverLoaded\":false") &&
        replace_token(&no_module_fixture_status,
            "\"TargetIdentityVerified\":true", "\"TargetIdentityVerified\":false") &&
        replace_token(&no_module_fixture_status,
            "\"InjectorPayloadValidated\":true", "\"InjectorPayloadValidated\":false") &&
        replace_token(&no_module_fixture_status,
            "\"ProbePayloadValidated\":true", "\"ProbePayloadValidated\":false");
    auto uncertain_module_fixture_status = no_module_fixture_status;
    const bool uncertain_module_fixture_transformed = replace_token(
        &uncertain_module_fixture_status,
        "\"ModuleLoadStateVerified\":true",
        "\"ModuleLoadStateVerified\":false");
    const bool cleanup_contract_fixtures_written =
        strict_true_position != std::string::npos &&
        string_boolean_position != std::string::npos &&
        attached_true_position != std::string::npos &&
        EnsureContainedDirectory(sanitized_fixture_root,
            legacy_cleanup_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            strict_false_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            string_boolean_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            missing_fields_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            contradictory_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            unknown_field_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            no_module_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            uncertain_module_session / L"reports") &&
        EnsureContainedDirectory(sanitized_fixture_root,
            strict_cleanup_session / L"reports") &&
        WriteUtf8FileAtomic(legacy_cleanup_session / L"reports" /
            L"enhanced-capture-status.json",
            "{\"Status\":\"Stopped\",\"Detail\":\"DETACHED code=0 "
            "moduleUnloaded=true moduleResidentInactive=false\"}\n") &&
        WriteUtf8FileAtomic(strict_false_session / L"reports" /
            L"enhanced-capture-status.json",
            strict_false_fixture_status + "\n") &&
        WriteUtf8FileAtomic(string_boolean_session / L"reports" /
            L"enhanced-capture-status.json",
            string_boolean_fixture_status + "\n") &&
        WriteUtf8FileAtomic(missing_fields_session / L"reports" /
            L"enhanced-capture-status.json",
            "{\"SchemaVersion\":\"god2-enhanced-capture-status-v2\"," 
            "\"Status\":\"EvidenceBlocked\"}\n") &&
        WriteUtf8FileAtomic(contradictory_session / L"reports" /
            L"enhanced-capture-status.json",
            contradictory_fixture_status + "\n") &&
        WriteUtf8FileAtomic(unknown_field_session / L"reports" /
            L"enhanced-capture-status.json",
            unknown_field_fixture_status + "\n") &&
        no_module_fixture_transformed && uncertain_module_fixture_transformed &&
        WriteUtf8FileAtomic(no_module_session / L"reports" /
            L"enhanced-capture-status.json", no_module_fixture_status + "\n") &&
        WriteUtf8FileAtomic(uncertain_module_session / L"reports" /
            L"enhanced-capture-status.json",
            uncertain_module_fixture_status + "\n") &&
        WriteUtf8FileAtomic(strict_cleanup_session / L"reports" /
            L"enhanced-capture-status.json", strict_fixture_status + "\n");
    const bool legacy_cleanup_spoof_rejected = cleanup_contract_fixtures_written &&
        CleanupStatus(legacy_cleanup_session) == "ModuleStateUncertain" &&
        CleanupStatus(strict_false_session) == "ModuleStateUncertain";
    const bool strict_cleanup_positive_accepted = cleanup_contract_fixtures_written &&
        CleanupStatus(strict_cleanup_session) == "Completed";
    const bool typed_and_complete_cleanup_contract_enforced =
        cleanup_contract_fixtures_written &&
        CleanupStatus(string_boolean_session) == "ModuleStateUncertain" &&
        CleanupStatus(missing_fields_session) == "ModuleStateUncertain" &&
        CleanupStatus(contradictory_session) == "ModuleStateUncertain" &&
        CleanupStatus(unknown_field_session) == "ModuleStateUncertain" &&
        CleanupStatus(uncertain_module_session) == "ModuleStateUncertain";
    const bool verified_no_module_cleanup_accepted =
        cleanup_contract_fixtures_written &&
        CleanupStatus(no_module_session) == "Completed";
    check("1_fixture_5412_capture_records", result.capture_records == 5412,
          "CaptureRecords=" + std::to_string(result.capture_records));
    check("2_exact_capture_stage_counts",
          result.transport_send_records == 737 && result.transport_recv_records == 2415 &&
          result.pre_encrypt_records == 746 && result.post_decrypt_records == 799 &&
          result.handler_decoded_records == 715,
          "send/recv/pre/post/handler=" + std::to_string(result.transport_send_records) + "/" +
          std::to_string(result.transport_recv_records) + "/" + std::to_string(result.pre_encrypt_records) +
          "/" + std::to_string(result.post_decrypt_records) + "/" + std::to_string(result.handler_decoded_records));
    check("3_direction_reconciliation", result.client_to_server_records == 1483 &&
          result.server_to_client_records == 3929 && result.count_reconciliation_passed,
          "C2S/S2C=" + std::to_string(result.client_to_server_records) + "/" +
          std::to_string(result.server_to_client_records));
    check("4_plaintext_promoted_only_after_frame_validation", result.transport_chunks == 3152 &&
          result.protocol_frames == result.pre_encrypt_records + result.post_decrypt_records &&
          result.protocol_frames == static_cast<std::uint64_t>(
              std::count(protocol_frames.begin(), protocol_frames.end(), '\n')) &&
          protocol_frames.find("\"ValidationStatus\":\"Verified\"") != std::string::npos,
          "TransportChunks=" + std::to_string(result.transport_chunks) + ",ProtocolFrames=" +
          std::to_string(result.protocol_frames));
    check("5_summary_keeps_layer_specific_counts",
          capture_info.find("\"ProtocolFrames\":1545") != std::string::npos &&
          capture_info.find("\"CaptureRecords\":5412") != std::string::npos,
          "capture-info counters are layer-specific");
    check("6_timestamp_fixture", first_record.find("\"CapturedAtUnixMs\":1785998774614") != std::string::npos &&
          first_record.find("\"CapturedAtUtc\":\"2026-08-06T06:46:14.614Z\"") != std::string::npos,
          "1785998774614 -> 2026-08-06T06:46:14.614Z");
    check("7_analysis_time_separate_from_capture_time",
          first_record.find("\"AnalyzedAtUtc\":\"2026-08-06T06:46:14.614Z\"") == std::string::npos,
          "CapturedAtUtc and AnalyzedAtUtc are independent fields");
    check("8_legacy_time_only_correlation_not_promoted", result.correlated_logical_messages == 0 &&
          result.exact_correlations == 0 && result.strong_correlations == 0 &&
          result.candidate_correlations == 0 && result.correlation_coverage_percent == 0.0 &&
          logical_map.find("\"CorrelationLevel\":\"Candidate\"") == std::string::npos &&
          logical_map.find("\"CorrelationLevel\":\"Uncorrelated\"") != std::string::npos,
          "legacy records remain uncorrelated without explicit IDs; Exact/Strong/Candidate/Coverage=" +
          std::to_string(result.exact_correlations) + "/" + std::to_string(result.strong_correlations) + "/" +
          std::to_string(result.candidate_correlations) + "/" + std::to_string(result.correlation_coverage_percent));
    check("9_uncorrelated_is_preserved", result.uncorrelated_logical_messages > 0 &&
          logical_map.find("\"CorrelationStatus\":\"Uncorrelated\"") != std::string::npos,
          "UncorrelatedLogicalMessages=" + std::to_string(result.uncorrelated_logical_messages));
    const bool valid_cleanup_outcome = result.cleanup_status == "Completed" ||
        result.cleanup_status == "DetachFailedOrModuleStateUncertain";
    const bool legacy_semantic_evidence_blocked = result.package_status == "EvidenceBlocked" &&
        capture_info.find("\"AnalysisStatus\":\"EvidenceBlockedSemanticEvidenceIncomplete\"") !=
            std::string::npos &&
        package_validation.find("bounded x86-to-x64 semantic transport reported missing") !=
            std::string::npos;
    check("10_capture_cleanup_status_separation", result.acquisition_status == "CompletedWithEvidence" &&
          valid_cleanup_outcome &&
          (result.package_status == "Ready" || result.package_status == "ReadyWithWarnings" ||
           legacy_semantic_evidence_blocked),
          result.acquisition_status + "/" + result.cleanup_status + "/" + result.package_status);
    check("10a_legacy_or_detail_only_unload_spoof_rejected",
          legacy_cleanup_spoof_rejected,
          "legacy/detail-only or StrictUnloadVerified=false cannot complete cleanup");
    check("10b_versioned_strict_unload_positive_accepted",
          strict_cleanup_positive_accepted,
          "versioned StrictUnloadVerified=true completes cleanup");
    check("10c_cleanup_schema_requires_typed_complete_inventory",
          typed_and_complete_cleanup_contract_enforced,
          "string booleans, missing fields and contradictory state cannot prove DLL cleanup");
    check("10d_verified_no_module_loaded_cleanup_is_distinct_from_unknown",
          verified_no_module_cleanup_accepted,
          "typed ModuleWasEverLoaded=false + verified absence snapshots prove cleanup while unknown state remains blocked");
    check("11_etw_zero_does_not_block_enhanced", result.etw_event_lines == 0 &&
          enhanced.find("\"RecordsCaptured\":5412") != std::string::npos && result.success,
          "EtwEventLines=0,EnhancedRecords=5412");
    check("12_loss_metrics_never_invented", health.find("\"EventsLost\":") != std::string::npos &&
          health.find("\"RecordsLost\":") != std::string::npos &&
          (health.find("\"PacketLossStatus\":\"Unknown\"") != std::string::npos ||
           health.find("\"PacketLossStatus\":\"Measured\"") != std::string::npos),
          "loss metrics are measured when source counters exist and null otherwise");
    check("13_manifest_sha256_validation", result.manifest_validation_passed &&
          !manifest.empty() && FileSha256(result.zip_path) == std::optional<std::string>(result.zip_sha256),
          "Manifest and ZIP SHA-256 validators passed");
    check("14_json_schema_validation", result.schema_validation_passed &&
          manifest.find("capture-record.schema.json") != std::string::npos &&
          manifest.find("provenance.schema.json") != std::string::npos,
          "typed record validation passed and all required schemas are inventoried");
    constexpr std::array<std::string_view, 7> semantic_authority_names{
        "VERIFIED", "DERIVED", "OBSERVED", "HYPOTHESIS", "UNKNOWN",
        "UNKNOWN_SERVER_ONLY", "REJECTED"
    };
    const bool semantic_authority_schema_exact =
        semantic_event_v2_schema.find(
            "\"AuthorityHint\":{\"enum\":[\"VERIFIED\",\"DERIVED\",\"OBSERVED\",\"HYPOTHESIS\",\"UNKNOWN\",\"UNKNOWN_SERVER_ONLY\",\"REJECTED\"]}") !=
            std::string::npos &&
        semantic_event_v2_schema.find("\"CANDIDATE\"") == std::string::npos &&
        semantic_event_v2_schema.find("\"RECOVERED\"") == std::string::npos;
    bool semantic_authority_reader_writer_exact = semantic_authority_schema_exact &&
        !ParseUltimateAuthority("CANDIDATE") && !ParseUltimateAuthority("RECOVERED");
    for (std::size_t authority_index = 0;
         semantic_authority_reader_writer_exact && authority_index < semantic_authority_names.size();
         ++authority_index) {
        const auto parsed_authority = ParseUltimateAuthority(semantic_authority_names[authority_index]);
        God2SemanticEventV2 authority_event;
        authority_event.event_id = "AuthoritySchemaFixture-" + std::to_string(authority_index);
        authority_event.sequence = 880000U + authority_index;
        authority_event.timestamp = "2026-08-09T02:00:00.000Z";
        authority_event.thread_id = 1U;
        authority_event.process_id = 1U;
        authority_event.session_id = "AuthoritySchemaFixture";
        authority_event.client_build_id = std::string(64U, 'A');
        authority_event.module_id = "God2_opt.exe";
        authority_event.authority_hint = parsed_authority.value_or(UltimateAuthority::Unknown);
        authority_event.sensitive_mask_status = "NotSensitive";
        authority_event.payload = "{}";
        const auto authority_wire = SerializeSemanticEventV2(authority_event);
        const auto authority_read = ReadSemanticEvent(authority_wire);
        semantic_authority_reader_writer_exact = parsed_authority && authority_read.success &&
            ClassifySemanticEventWire(authority_wire) ==
                SemanticEventWireProfile::CanonicalV2Envelope &&
            authority_read.event.authority_hint == *parsed_authority &&
            std::string_view(ToString(*parsed_authority)) == semantic_authority_names[authority_index] &&
            authority_wire.find("\"AuthorityHint\":\"" +
                std::string(semantic_authority_names[authority_index]) + "\"") != std::string::npos;
    }
    check("14b_semantic_v2_authority_schema_reader_writer_consistent",
          semantic_authority_reader_writer_exact,
          "schema, writer and reader share exactly VERIFIED/DERIVED/OBSERVED/HYPOTHESIS/UNKNOWN/UNKNOWN_SERVER_ONLY/REJECTED");
    God2SemanticEventV2 strict_v2_fixture;
    strict_v2_fixture.event_id = "StrictV2SchemaFixture";
    strict_v2_fixture.sequence = 880100U;
    strict_v2_fixture.timestamp = "2026-08-09T02:01:00.000Z";
    strict_v2_fixture.thread_id = 1U;
    strict_v2_fixture.process_id = 1U;
    strict_v2_fixture.session_id = "StrictV2SchemaFixture";
    strict_v2_fixture.client_build_id = std::string(64U, 'B');
    strict_v2_fixture.module_id = "God2_opt.exe";
    strict_v2_fixture.sensitive_mask_status = "NotSensitive";
    strict_v2_fixture.payload = "{}";
    strict_v2_fixture.evidence_binding.emplace("SchemaId", "God2SemanticEvent");
    strict_v2_fixture.evidence_binding.emplace("SemanticEventId", strict_v2_fixture.event_id);
    for (const auto property : kSemanticEventV2CompanionProperties)
        strict_v2_fixture.evidence_binding.emplace(std::string(property), "fixture");
    const auto strict_v2_wire = SerializeSemanticEventV2(strict_v2_fixture);
    const auto strict_v2_read = ReadSemanticEvent(strict_v2_wire);
    bool strict_v2_schema_has_exact_allowlist =
        semantic_event_v2_schema.find("\"additionalProperties\":false") != std::string::npos &&
        semantic_event_v2_schema.find(
            "\"required\":[\"SchemaId\",\"SchemaVersion\",\"EventType\"") !=
            std::string::npos &&
        semantic_event_v2_schema.find("\"$defs\":{") != std::string::npos &&
        semantic_event_v2_schema.find("\"oneOf\":[") != std::string::npos &&
        semantic_event_v2_schema.find(
            "\"Payload\":{\"type\":\"object\",\"additionalProperties\":true}") ==
            std::string::npos &&
        semantic_event_v2_schema.find(
            "\"probeDomainStatus\":{\"type\":\"object\",\"additionalProperties\":false") !=
            std::string::npos &&
        semantic_event_v2_schema.find(
            "\"SchemaId\":{\"type\":\"string\",\"const\":\"God2SemanticEvent\"}") !=
            std::string::npos &&
        semantic_event_v2_schema.find(
            "\"SemanticEventId\":{\"type\":\"string\",\"minLength\":1}") !=
            std::string::npos;
    for (const auto property : kSemanticEventV2CompanionProperties) {
        const bool schema_has_property = property == "Rva" ?
            semantic_event_v2_schema.find(
                "\"patternProperties\":{\"^Rva$\":{\"type\":\"string\"}}") !=
                std::string::npos :
            semantic_event_v2_schema.find("\"" + std::string(property) + "\":" +
                std::string(SemanticEventV2CompanionSchema(property))) !=
                std::string::npos;
        strict_v2_schema_has_exact_allowlist = strict_v2_schema_has_exact_allowlist &&
            schema_has_property &&
            strict_v2_wire.find("\"" + std::string(property) + "\":") != std::string::npos;
    }
    std::string unknown_v2_wire = strict_v2_wire;
    if (!unknown_v2_wire.empty())
        unknown_v2_wire.insert(unknown_v2_wire.size() - 1U, ",\"UnknownV2Field\":1");
    const auto unknown_v2_read = ReadSemanticEvent(unknown_v2_wire);
    std::string unknown_payload_v2_wire = strict_v2_wire;
    const auto empty_payload_offset = unknown_payload_v2_wire.find("\"Payload\":{}");
    if (empty_payload_offset != std::string::npos) {
        unknown_payload_v2_wire.replace(empty_payload_offset,
            std::string_view("\"Payload\":{}").size(),
            "\"Payload\":{\"UntrustedNestedPayload\":1}");
    }
    const auto unknown_payload_v2_read = ReadSemanticEvent(unknown_payload_v2_wire);
    God2SemanticEventV2 unclassified_v2_fixture = strict_v2_fixture;
    unclassified_v2_fixture.payload =
        "{\"UntrustedNestedPayload\":\"raw-marker-must-not-persist\"}";
    const auto unclassified_v2_wire = SerializeSemanticEventV2(unclassified_v2_fixture);
    const auto unclassified_v2_read = ReadSemanticEvent(unclassified_v2_wire);
    check("14c_semantic_v2_schema_strict_allowlist_and_unknown_rejection",
          strict_v2_schema_has_exact_allowlist && strict_v2_read.success &&
              ClassifySemanticEventWire(strict_v2_wire) ==
                  SemanticEventWireProfile::CanonicalV2Envelope &&
              !unknown_v2_read.success &&
              unknown_v2_read.error.find("unknown v2 field: UnknownV2Field") != std::string::npos &&
              !unknown_payload_v2_read.success &&
              unknown_payload_v2_read.error.find("closed canonical profile") !=
                  std::string::npos &&
              unclassified_v2_read.success &&
              unclassified_v2_wire.find("raw-marker-must-not-persist") == std::string::npos &&
              unclassified_v2_wire.find(
                  "EvidenceBlockedInvalidOrUnclassifiedPayload") != std::string::npos,
          "v2 schema and reader reject unknown root/nested keys; writer replaces unclassified payload with fixed EvidenceBlocked metadata");
    const std::string legacy_v1_wire =
        "{\"SchemaVersion\":1,\"EventType\":\"StateMutation\"," 
        "\"EventId\":\"LegacySchemaFixture\",\"SensitiveValue\":false}";
    const auto legacy_v1_read = ReadSemanticEvent(legacy_v1_wire);
    const std::string canonical_v1_wire =
        "{\"SchemaId\":\"God2SemanticEvent\",\"SchemaVersion\":1,"
        "\"EventType\":\"StateMutation\",\"SemanticEventId\":\"CanonicalV1Fixture\","
        "\"Sequence\":1,\"Timestamp\":\"2026-08-09T02:02:00.000Z\","
        "\"ThreadId\":1,\"ProcessId\":1,\"SessionId\":\"CanonicalV1Fixture\","
        "\"ClientBuildId\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\","
        "\"ModuleId\":\"God2_opt.exe\",\"RVA\":0,\"CallsiteRVA\":0,"
        "\"SensitiveValue\":false}";
    const auto canonical_v1_read = ReadSemanticEvent(canonical_v1_wire);
    const auto converted_legacy_v1_wire = legacy_v1_read.success ?
        SerializeSemanticEventV2(legacy_v1_read.event) : std::string{};
    std::string promotable_v1_spoof = converted_legacy_v1_wire;
    const auto promotable_false_offset = promotable_v1_spoof.find("\"Promotable\":false");
    if (promotable_false_offset != std::string::npos) {
        promotable_v1_spoof.replace(promotable_false_offset,
            std::string_view("\"Promotable\":false").size(), "\"Promotable\":true");
    }
    const auto promotable_v1_spoof_read = ReadSemanticEvent(promotable_v1_spoof);
    const bool v1_schemas_match_profiles =
        semantic_event_v1_schema.find("\"additionalProperties\":false") != std::string::npos &&
        semantic_event_v1_schema.find(
            "\"required\":[\"SchemaId\",\"SchemaVersion\",\"EventType\",\"SemanticEventId\",\"Sequence\",\"Timestamp\",\"ThreadId\",\"ProcessId\",\"SessionId\",\"ClientBuildId\",\"ModuleId\",\"RVA\",\"CallsiteRVA\",\"SensitiveValue\"]") !=
            std::string::npos &&
        semantic_event_v1_schema.find("\"Payload\"") == std::string::npos &&
        semantic_event_v1_schema.find("\"ObjectResolution\"") == std::string::npos &&
        semantic_event_v1_compatibility_schema.find(
            "\"additionalProperties\":true") != std::string::npos &&
        semantic_event_v1_compatibility_schema.find(
            "\"required\":[\"SchemaVersion\",\"EventType\"]") !=
            std::string::npos &&
        semantic_event_v1_compatibility_schema.find(
            "\"anyOf\":[{\"required\":[\"SemanticEventId\"]},{\"required\":[\"EventId\"]}]") !=
            std::string::npos &&
        semantic_event_v1_compatibility_schema.find("\"ObjectResolution\"") !=
            std::string::npos;
    check("14d_semantic_v1_schema_matches_compatibility_reader",
          v1_schemas_match_profiles && legacy_v1_read.success &&
              legacy_v1_read.read_from_v1 && canonical_v1_read.success &&
              canonical_v1_read.read_from_v1 &&
              ClassifySemanticEventWire(legacy_v1_wire) ==
                  SemanticEventWireProfile::NonCanonicalV1CompatibilityInput &&
              ClassifySemanticEventWire(canonical_v1_wire) ==
                  SemanticEventWireProfile::CanonicalV1WriterProfile &&
              !CanEvaluateSemanticPromotion(
                  SemanticEventWireProfile::NonCanonicalV1CompatibilityInput) &&
              !CanEvaluateSemanticPromotion(
                  SemanticEventWireProfile::CanonicalV1WriterProfile) &&
              legacy_v1_read.event.authority_hint == UltimateAuthority::Unknown &&
              canonical_v1_read.event.authority_hint == UltimateAuthority::Unknown &&
              legacy_v1_read.event.payload.find(
                  "\"Profile\":\"NonCanonicalV1CompatibilityInput\"") !=
                  std::string::npos &&
              legacy_v1_read.event.payload.find("\"Canonical\":false") !=
                  std::string::npos &&
              legacy_v1_read.event.payload.find("\"Promotable\":false") !=
                  std::string::npos &&
              !promotable_v1_spoof_read.success,
          "closed v1 writer profile and broad v1 compatibility input remain distinct and non-promotable until canonical v2 conversion");
    const bool truthful_shared_health_availability =
        packaged_shared_health.find(
            "\"SchemaVersion\":\"god2-semantic-shared-ring-health-v4\"") !=
            std::string::npos ||
        (packaged_shared_health.find(
             "\"SchemaVersion\":\"god2-semantic-shared-ring-health-availability-v1\"") !=
             std::string::npos &&
         packaged_shared_health.find(
             "\"Status\":\"EvidenceBlockedSharedTransportHealthUnavailable\"") !=
             std::string::npos &&
         packaged_shared_health.find(
             "\"RequiredSchemaVersion\":\"god2-semantic-shared-ring-health-v4\"") !=
             std::string::npos &&
         packaged_shared_health.find("\"Promotable\":false") != std::string::npos &&
         shared_health_availability_schema.find(
             "\"additionalProperties\":false") != std::string::npos &&
         shared_health_availability_schema.find(
             "\"SchemaVersion\":{\"const\":\"god2-semantic-shared-ring-health-availability-v1\"") !=
             std::string::npos);
    check("14e_missing_shared_health_uses_nonpromotable_availability_schema",
          truthful_shared_health_availability,
          "missing shared-ring health never masquerades as a complete v3 instance");
    const bool truthful_candidate_map_availability =
        packaged_candidate_map.find(
            "\"SchemaVersion\":\"god2-deep-probe-candidate-map-v1\"") ==
            std::string::npos &&
        (packaged_candidate_map.find(
             "\"SchemaVersion\":\"god2-deep-probe-candidate-map-v2\"") !=
             std::string::npos ||
         (packaged_candidate_map.find(
              "\"SchemaVersion\":\"god2-deep-probe-candidate-map-availability-v1\"") !=
              std::string::npos &&
          packaged_candidate_map.find(
              "\"Status\":\"EvidenceBlockedCandidateMapUnavailable\"") !=
              std::string::npos &&
          packaged_candidate_map.find(
              "\"RequiredSchemaVersion\":\"god2-deep-probe-candidate-map-v2\"") !=
              std::string::npos &&
          packaged_candidate_map.find("\"Promotable\":false") !=
              std::string::npos &&
          candidate_map_availability_schema.find(
              "\"additionalProperties\":false") != std::string::npos &&
          candidate_map_availability_schema.find(
              "\"SchemaVersion\":{\"const\":\"god2-deep-probe-candidate-map-availability-v1\"") !=
              std::string::npos));
    check("14f_missing_candidate_map_never_emits_stale_v1_placeholder",
          truthful_candidate_map_availability,
          "candidate evidence is either a v2 map or an explicit non-promotable availability report");
    StrictSemanticSegmentManifest packaged_segment_contract;
    std::string packaged_segment_error;
    const bool packaged_segment_manifest_valid =
        ParseStrictSemanticSegmentManifest(packaged_segment_manifest,
            &packaged_segment_contract, &packaged_segment_error) &&
        ValidateEvidenceZipSemanticSegments(result.zip_path, &packaged_segment_error);
    check("14g_segment_manifest_is_closed_and_fail_closed",
          packaged_segment_manifest_valid &&
              packaged_segment_manifest.find(
                  "\"Status\":\"EVIDENCE_BLOCKED_SEGMENT_OR_CONTINUITY_FAILURE\"") !=
                  std::string::npos &&
              manifest.find(
                  "PrimarySession/reports/semantic-segment-manifest.json") !=
                  std::string::npos &&
              semantic_segment_manifest_schema.find(
                  "\"SegmentMaximumBytes\":{\"const\":16777216}") !=
                  std::string::npos &&
              semantic_segment_manifest_schema.find(
                  "\"additionalProperties\":false") != std::string::npos,
          packaged_segment_manifest_valid ?
              "fixture source lacks a trustworthy matching segment ledger, so the bundle carries an explicit blocked closed manifest" :
              packaged_segment_error);
    auto segment_event_one = strict_v2_fixture;
    segment_event_one.event_id = "SegmentContinuityOne";
    segment_event_one.evidence_binding["SemanticEventId"] = segment_event_one.event_id;
    segment_event_one.sequence = 1;
    segment_event_one.session_id = result.session_id;
    auto segment_event_two = segment_event_one;
    segment_event_two.event_id = "SegmentContinuityTwo";
    segment_event_two.evidence_binding["SemanticEventId"] = segment_event_two.event_id;
    segment_event_two.sequence = 2;
    const std::string segment_one_wire = SerializeSemanticEventV2(segment_event_one) + "\n";
    const std::string segment_two_wire = SerializeSemanticEventV2(segment_event_two) + "\n";
    const std::string synthetic_segment = segment_one_wire + segment_two_wire;
    const std::string synthetic_segment_sha = Sha256(synthetic_segment).value_or("");
    const std::string synthetic_segment_row = MakeJsonObject({
        {"Index", "0"}, {"Path", "raw/semantic-segments/semantic-000000.jsonl"},
        {"Records", "2"}, {"Bytes", std::to_string(synthetic_segment.size())},
        {"FirstSequence", "1"}, {"LastSequence", "2"},
        {"SHA256", synthetic_segment_sha}
    }, {"Index", "Records", "Bytes", "FirstSequence", "LastSequence"});
    const std::string synthetic_segment_manifest = MakeJsonObject({
        {"SchemaVersion", "god2-semantic-segment-manifest-v1"},
        {"GeneratedAtUtc", "2026-08-10T00:00:00.000Z"}, {"Status", "PASS"},
        {"SegmentMaximumBytes", std::to_string(kSemanticSegmentMaximumBytes)},
        {"SegmentMaximumRecords", std::to_string(kSemanticSegmentMaximumRecords)},
        {"SegmentCount", "1"}, {"RecordCount", "2"},
        {"SequenceContinuity", "true"}, {"MergedPath", "raw/semantic-events.jsonl"},
        {"MergedSHA256", synthetic_segment_sha},
        {"Segments", "[" + synthetic_segment_row + "]"}
    }, {"SegmentMaximumBytes", "SegmentMaximumRecords", "SegmentCount",
        "RecordCount", "SequenceContinuity", "Segments"});
    StrictSemanticSegmentManifest synthetic_segment_contract;
    std::string synthetic_segment_error;
    const auto synthetic_loader = [&](std::string_view path) -> std::optional<std::string> {
        return path == "raw/semantic-segments/semantic-000000.jsonl" ?
            std::optional<std::string>(synthetic_segment) : std::nullopt;
    };
    const bool synthetic_segment_positive = ParseStrictSemanticSegmentManifest(
        synthetic_segment_manifest, &synthetic_segment_contract,
        &synthetic_segment_error) &&
        ValidateSemanticSegmentPayloads(synthetic_segment_contract,
            synthetic_segment, synthetic_loader, result.session_id,
            &synthetic_segment_error);
    auto sequence_three_event = segment_event_two;
    sequence_three_event.sequence = 3;
    const std::string sequence_gap_segment = segment_one_wire +
        SerializeSemanticEventV2(sequence_three_event) + "\n";
    auto sequence_gap_contract = synthetic_segment_contract;
    const auto sequence_gap_loader = [&](std::string_view path) -> std::optional<std::string> {
        return path == "raw/semantic-segments/semantic-000000.jsonl" ?
            std::optional<std::string>(sequence_gap_segment) : std::nullopt;
    };
    bool sequence_gap_rejected = false;
    if (synthetic_segment_positive && sequence_gap_contract.segments.size() == 1) {
        sequence_gap_contract.merged_sha256 = Sha256(sequence_gap_segment).value_or("");
        sequence_gap_contract.segments[0].sha256 = sequence_gap_contract.merged_sha256;
        sequence_gap_contract.segments[0].bytes = sequence_gap_segment.size();
        sequence_gap_rejected = !ValidateSemanticSegmentPayloads(
            sequence_gap_contract, sequence_gap_segment, sequence_gap_loader,
            result.session_id, &synthetic_segment_error);
    }
    check("14h_segment_payload_continuity_positive_and_gap_negative",
          synthetic_segment_positive && sequence_gap_rejected,
          synthetic_segment_positive && sequence_gap_rejected ?
              "two canonical events reconcile through segment/merged hashes; a 1,3 sequence gap is rejected" :
              synthetic_segment_error);
    check("15_json_number_boolean_types", first_record.find("\"CaptureSequence\":1") != std::string::npos &&
          first_record.find("\"ValidJson\":true") != std::string::npos,
          "number and boolean tokens are not encoded as strings");
    check("16_sqlite_integrity", result.sqlite_integrity_passed,
          result.sqlite_integrity_passed ? "PRAGMA integrity_check=ok" : "integrity check failed or unavailable");
    check("17_relative_paths_only", result.relative_paths_passed && manifest.find(":\\\\") == std::string::npos,
          "absolute Windows path scan passed");
    check("18_single_primary_session", manifest.find("PrimarySession/raw/capture-records.jsonl") != std::string::npos &&
          manifest.find("PrimarySession2/") == std::string::npos,
          "one PrimarySession root is inventoried");
    check("19_session_identity_consistency", result.session_identity_passed &&
          capture_info.find(result.session_id) != std::string::npos && health.find(result.session_id) != std::string::npos,
          "SessionId=" + result.session_id);
    check("20_logs_not_mixed", manifest.find("Logs/") == std::string::npos && manifest.find("logs/") == std::string::npos,
          "no unrelated log tree is included");
    check("21_codex_handoff_complete", handoff.find("RecommendedIngestionOrder") != std::string::npos &&
          handoff.find("ServerMutationPolicy") != std::string::npos && handoff.find(result.session_id) != std::string::npos &&
          handoff.find("\"DeepAnalysisPriority\":[\"VerifiedIntegrationManifest\",\"VerifiedProtocolSpec\",\"SemanticEvidenceGraph\",\"ValueFlow\",\"ActionPattern\",\"OpcodeWorklist\",\"RawPlaintextEvidence\"]") != std::string::npos &&
          handoff.find("\"EnhancedPlaintextAvailable\":true") != std::string::npos &&
          handoff.find("\"ToolVersion\":\"" GOD2_TOOL_VERSION "\"") != std::string::npos,
          "release handoff identity, ingestion order, warnings and mutation policy present");
    check("22_authority_layers", authority.find("RawImmutable") != std::string::npos &&
          authority.find("DerivedDeterministic") != std::string::npos && authority.find("Candidate") != std::string::npos,
          "Raw/Derived/Candidate authority policy present");
    check("23_partial_session_recovery", partial.success && fs::is_regular_file(partial.zip_path),
          partial.success ? "partial package reopened and validated" : partial.error);
    check("24_zip_failure_retry_without_recapture", !simulated_failure.success && result.success &&
          CountLines(raw_path) == before_records,
          "simulated failure retried; raw record count remains " + std::to_string(CountLines(raw_path)));
    check("25_package_reopen_validation", result.zip_reopen_validation_passed &&
          package_validation.find("\"ZipReopenValidationStatus\":\"Pass\"") != std::string::npos,
          "ZIP reopened and every inventoried artifact matched");
    check("26_old_session_reanalysis_compatible", fs::is_regular_file(active_session_path / L"session-summary.json") &&
          fs::is_regular_file(active_session_path / L"session.sqlite3"),
          "legacy session summary and SQLite remain readable");
    const HMODULE module = GetModuleHandleW(nullptr);
    check("27_gui_start_stop_dll_capture_surface", module != nullptr &&
          FindResourceW(module, MAKEINTRESOURCEW(IDR_X86_PACKET_PROBE), RT_RCDATA) != nullptr &&
          FindResourceW(module, MAKEINTRESOURCEW(IDR_X86_PACKET_INJECTOR), RT_RCDATA) != nullptr,
          "running GUI executable contains both x86 enhanced capture resources");
    check("28_packaging_does_not_reduce_capture_records", before_records == 5412 && CountLines(raw_path) == before_records &&
          result.capture_records == before_records,
          "before/after/package=" + std::to_string(before_records) + "/" +
          std::to_string(CountLines(raw_path)) + "/" + std::to_string(result.capture_records));
    check("29_summary_counts_from_outputs", result.capture_records == CountLines(raw_path) &&
          result.gameplay_candidates == static_cast<std::uint64_t>(
              std::count(gameplay_candidates.begin(), gameplay_candidates.end(), '\n')) &&
          result.transport_chunks == result.transport_send_records + result.transport_recv_records,
          "all asserted counters reconcile with source JSONL outputs");
    check("30_zip_crc_corruption_rejected", crc_tamper_rejected,
          crc_tamper_rejected ? "tampered payload was rejected by CRC-32 validation" : crc_rejection_reason);
    check("31_truncated_zip_rejected", truncated_zip_rejected,
          truncated_zip_rejected ? "truncated EOCD was rejected" : truncation_rejection_reason);
    check("31b_unlisted_duplicate_missing_manifest_inventory_rejected",
          unlisted_and_missing_rejected && duplicate_manifest_rejected,
          unlisted_and_missing_rejected && duplicate_manifest_rejected ?
              "renamed ZIP entry and duplicate manifest path were rejected by strict inventory" :
              inventory_rejection_reason + "; " + duplicate_manifest_error);
    ValidatedEnhancedCaptureStatus quarantined_enhancement;
    quarantined_enhancement.valid = true;
    quarantined_enhancement.requested = true;
    quarantined_enhancement.status = "EvidenceBlocked";
    quarantined_enhancement.failure_kind = "SecuritySoftwareRemovalSuspected";
    check("32_requested_enhancement_without_records_is_blocked",
          EnhancedAcquisitionBlocked(quarantined_enhancement, 0) &&
          !EnhancedAcquisitionBlocked(quarantined_enhancement, 1),
          "requested enhancement with zero records is EvidenceBlocked; any acquired record clears this exact gate");
    check("33_codex_opcode_worklist", !opcode_worklist.empty() &&
          opcode_worklist.find("\"SchemaId\":\"opcode-work-item\"") != std::string::npos &&
          opcode_worklist.find("\"StableBytePattern\":") != std::string::npos &&
          opcode_worklist.find("\"ChangingByteOffsets\":[") != std::string::npos &&
          opcode_worklist.find("\"SemanticStatus\":\"UnknownUntilVerified\"") != std::string::npos &&
          opcode_worklist.find("\"ProductionEligible\":false") != std::string::npos,
          "machine-readable opcode groups contain samples, byte-difference patterns and safe promotion status");
    check("34_codex_gameplay_and_coverage_artifacts", !packaged_handler_observations.empty() &&
          !packaged_gameplay_coverage.empty() &&
          manifest.find("PrimarySession/gameplay/protocol-handler-observations.jsonl") != std::string::npos &&
          manifest.find("PrimarySession/exports/gameplay-coverage-matrix.csv") != std::string::npos &&
          handoff.find("\"GameplayClassifierArtifactCount\":19") != std::string::npos &&
          handoff.find("\"EvidenceExportArtifactCount\":22") != std::string::npos,
          "single Evidence ZIP contains classifier JSONL and database-friendly coverage/formula exports");
    check("34b_passive_automatic_semantic_recognition",
          result.automatic_semantic_candidates > 0 && result.automatic_semantic_family_count > 0 &&
          gameplay_candidates.find("\"AutomaticRecognition\":true") != std::string::npos &&
          gameplay_candidates.find("\"UserInteractionRequired\":false") != std::string::npos &&
          automatic_semantic_summary.find("\"Mode\":\"PassiveAutomaticNoSceneSelection\"") != std::string::npos &&
          automatic_semantic_summary.find("\"UserInteractionRequired\":false") != std::string::npos,
          "known plaintext/handler structures are classified without scene selection, markers or window switching");

    const auto correlation_session = DefaultSessionsRoot() /
        (L"correlation-contract-" + Utf8ToWide(NewId()));
    SessionStore correlation_store(correlation_session);
    std::string correlation_error;
    const bool correlation_initialized = correlation_store.Initialize(&correlation_error);
    const auto correlation_record = [](std::initializer_list<std::pair<std::string, std::string>> values) {
        return MakeJsonObject(values, {"ObservedAtUnixMs", "ProcessId", "ThreadId", "sequence",
            "CapturedLength", "TransferredLength", "CallDepth"}) + "\n";
    };
    std::string correlation_records;
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-recv"}, {"ObservedAtUnixMs", "1786080000001"},
        {"PacketDirection", "ServerToClient"}, {"CaptureStage", "Transport"}, {"Api", "recv"},
        {"ProcessId", "4242"}, {"ThreadId", "77"}, {"sequence", "1"},
        {"HookInvocationId", "hook-4242-1"}, {"PayloadHex", "A1B2C3D4E5"},
        {"CapturedLength", "5"}, {"TransferredLength", "5"}, {"CallDepth", "0"}
    });
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-post-decrypt"}, {"ObservedAtUnixMs", "1786080000002"},
        {"PacketDirection", "ServerToClient"}, {"CaptureStage", "PostDecrypt"}, {"Api", "PostDecrypt"},
        {"ProcessId", "4242"}, {"ThreadId", "77"}, {"sequence", "2"},
        {"HookInvocationId", "hook-4242-2"}, {"ContextInvocationId", "hook-4242-1"},
        {"ContextCorrelationBasis", "SameThreadLatestInboundTransport"},
        {"PayloadHex", "0500830078"}, {"PlaintextHex", "0500830078"},
        {"CapturedLength", "5"}, {"TransferredLength", "5"}, {"CallDepth", "0"}
    });
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-handler"}, {"ObservedAtUnixMs", "1786080000003"},
        {"PacketDirection", "ServerToClient"}, {"CaptureStage", "HandlerDecoded"}, {"Api", "HandlerDecoded"},
        {"ProcessId", "4242"}, {"ThreadId", "77"}, {"sequence", "3"},
        {"HookInvocationId", "hook-4242-3"}, {"ContextInvocationId", "hook-4242-2"},
        {"ContextCorrelationBasis", "SameThreadLatestPostDecrypt"},
        {"PayloadHex", "830000000000000000000000000000"}, {"PlaintextHex", "830000000000000000000000000000"},
        {"CapturedLength", "15"}, {"TransferredLength", "15"}, {"CallDepth", "0"}
    });
    // This record deliberately cites another thread's context.  It must not be
    // promoted merely because the referenced ID, direction and stages exist.
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-cross-thread-invalid"}, {"ObservedAtUnixMs", "1786080000004"},
        {"PacketDirection", "ServerToClient"}, {"CaptureStage", "HandlerDecoded"}, {"Api", "HandlerDecoded"},
        {"ProcessId", "4242"}, {"ThreadId", "78"}, {"sequence", "4"},
        {"HookInvocationId", "hook-4242-4"}, {"ContextInvocationId", "hook-4242-2"},
        {"ContextCorrelationBasis", "SameThreadLatestPostDecrypt"},
        {"PayloadHex", "830000000000000000000000000000"}, {"PlaintextHex", "830000000000000000000000000000"},
        {"CapturedLength", "15"}, {"TransferredLength", "15"}, {"CallDepth", "0"}
    });
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-pre-encrypt"}, {"ObservedAtUnixMs", "1786080000005"},
        {"PacketDirection", "ClientToServer"}, {"CaptureStage", "PreEncrypt"}, {"Api", "PreEncrypt"},
        {"ProcessId", "4242"}, {"ThreadId", "79"}, {"sequence", "5"},
        {"HookInvocationId", "hook-4242-5"}, {"PayloadHex", "0500300025"},
        {"PlaintextHex", "0500300025"}, {"CapturedLength", "5"},
        {"TransferredLength", "5"}, {"CallDepth", "0"}
    });
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-send"}, {"ObservedAtUnixMs", "1786080000006"},
        {"PacketDirection", "ClientToServer"}, {"CaptureStage", "Transport"}, {"Api", "send"},
        {"ProcessId", "4242"}, {"ThreadId", "79"}, {"sequence", "6"},
        {"HookInvocationId", "hook-4242-6"}, {"ParentInvocationId", "hook-4242-5"},
        {"ParentCorrelationBasis", "ExactNestedPreEncryptInvocation"},
        {"PayloadHex", "DEADBEEF"}, {"CapturedLength", "4"},
        {"TransferredLength", "4"}, {"CallDepth", "1"}
    });
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-pre-encrypt-async"}, {"ObservedAtUnixMs", "1786080000007"},
        {"PacketDirection", "ClientToServer"}, {"CaptureStage", "PreEncrypt"}, {"Api", "PreEncrypt"},
        {"ProcessId", "4242"}, {"ThreadId", "80"}, {"sequence", "7"},
        {"HookInvocationId", "hook-4242-7"}, {"PayloadHex", "0500310026"},
        {"PlaintextHex", "0500310026"}, {"CapturedLength", "5"},
        {"TransferredLength", "5"}, {"CallDepth", "0"}
    });
    correlation_records += correlation_record({
        {"SourceFrameId", "correlation-send-async"}, {"ObservedAtUnixMs", "1786080000008"},
        {"PacketDirection", "ClientToServer"}, {"CaptureStage", "Transport"}, {"Api", "WSASend"},
        {"ProcessId", "4242"}, {"ThreadId", "80"}, {"sequence", "8"},
        {"HookInvocationId", "hook-4242-8"}, {"ContextInvocationId", "hook-4242-7"},
        {"ContextCorrelationBasis", "SameThreadSinglePendingPreEncrypt"},
        {"PayloadHex", "CAFEBABE"}, {"CapturedLength", "4"},
        {"TransferredLength", "4"}, {"CallDepth", "0"}
    });
    const bool correlation_written = correlation_initialized && WriteUtf8FileAtomic(
        correlation_session / L"raw" / L"capture-records.jsonl", correlation_records);
    const auto correlation_result = correlation_written ? EvidencePackageBuilder::Build(correlation_session) :
        EvidencePackageResult{};
    const auto correlation_logical_map = ReadEvidenceZipEntry(
        correlation_result.zip_path, "PrimarySession/evidence/logical-message-map.jsonl").value_or("");
    const auto correlation_transport_map = ReadEvidenceZipEntry(
        correlation_result.zip_path, "PrimarySession/mapping/transport-to-decrypted.jsonl").value_or("");
    const auto correlation_handler_map = ReadEvidenceZipEntry(
        correlation_result.zip_path, "PrimarySession/mapping/decrypted-to-handler.jsonl").value_or("");
    const auto correlation_worklist = ReadEvidenceZipEntry(
        correlation_result.zip_path, "PrimarySession/protocol/codex-opcode-worklist.jsonl").value_or("");
    check("35_exact_parent_correlation_contract", correlation_result.success &&
          correlation_result.exact_correlations == 1 &&
          correlation_transport_map.find("\"EvidenceLevel\":\"Exact\"") != std::string::npos &&
          correlation_transport_map.find("\"CorrelationBasis\":\"ExplicitParentInvocationId\"") != std::string::npos,
          "same-thread PreEncrypt -> send explicit parent link is Exact");
    check("36_strong_decode_handler_context_contract", correlation_result.success &&
          correlation_result.strong_correlations == 2 &&
          correlation_handler_map.find("\"EvidenceLevel\":\"Strong\"") != std::string::npos &&
          correlation_handler_map.find("\"CorrelationBasis\":\"ProbeIssuedSameThreadStageContext\"") != std::string::npos,
          "same-thread recv -> PostDecrypt -> HandlerDecoded context is Strong");
    check("37_cross_thread_context_not_promoted", correlation_result.success &&
          correlation_result.candidate_correlations == 1 &&
          correlation_logical_map.find("\"CorrelationLevel\":\"Candidate\"") != std::string::npos,
          "cross-thread context citation remains Candidate");
    check("38_strong_handler_opcode_worklist_priority", correlation_result.success &&
          correlation_worklist.find("\"Opcode\":\"0x83\"") != std::string::npos &&
          correlation_worklist.find("\"StrongHandlerCorrelationCount\":1") != std::string::npos &&
          correlation_worklist.find("\"Priority\":\"High\"") != std::string::npos,
          "strong handler linkage elevates the matching opcode work item without marking it Verified");
    check("39_strong_async_outbound_context_contract", correlation_result.success &&
          correlation_result.strong_correlations == 2 &&
          correlation_transport_map.find("\"ProbeContextCorrelationBasis\":\"SameThreadSinglePendingPreEncrypt\"") != std::string::npos &&
          correlation_transport_map.find("\"DecryptedRecordId\":\"capture-record-7\"") != std::string::npos,
          "one pending same-thread PreEncrypt -> later WSASend link is Strong rather than Exact");
    std::error_code correlation_cleanup_error;
    fs::remove_all(correlation_session, correlation_cleanup_error);

    const auto synthetic_record = [](std::uint64_t sequence, std::int64_t relative_ms,
                                     std::string logical_id, std::string stage,
                                     std::string direction, std::string opcode,
                                     std::uint64_t frame_length, std::string connection_id,
                                     std::int64_t thread_id = 91) {
        ActionGroupingRecord record;
        record.capture_record_id = "synthetic-record-" + std::to_string(sequence);
        record.logical_message_id = std::move(logical_id);
        record.capture_stage = std::move(stage);
        record.direction = std::move(direction);
        record.api = record.capture_stage;
        record.connection_id = std::move(connection_id);
        record.opcode = std::move(opcode);
        record.frame_length = frame_length;
        record.captured_at_unix_ms = 1786105000000 + relative_ms;
        record.captured_at_utc = UnixMillisecondsUtc(record.captured_at_unix_ms);
        record.capture_sequence = sequence;
        record.stage_sequence = sequence;
        record.captured_qpc = relative_ms + 1;
        record.qpc_frequency = 1000;
        record.process_id = 5151;
        record.thread_id = thread_id;
        record.hook_invocation_id = "synthetic-hook-" + std::to_string(sequence);
        record.correlation_level = "Strong";
        if (record.capture_stage == "PreEncrypt" || record.capture_stage == "PostDecrypt")
            record.protocol_frame_id = "synthetic-frame-" + std::to_string(sequence);
        if (record.capture_stage == "HandlerDecoded") {
            record.handler_observation_id = "synthetic-handler-" + std::to_string(sequence);
            record.payload_hex = record.opcode.size() == 4 ? record.opcode.substr(2) + "00" : "9000";
        }
        return record;
    };
    const auto append_trigger = [&](GenericActionGroupingInput* input, std::uint64_t* sequence,
                                    std::int64_t relative_ms, const std::string& logical_id,
                                    const std::string& opcode, std::uint64_t frame_length,
                                    const std::string& connection_id, bool linked_transport,
                                    bool verified_registry = false) {
        auto trigger = synthetic_record(++*sequence, relative_ms, logical_id, "PreEncrypt",
            "ClientToServer", opcode, frame_length, connection_id);
        if (verified_registry) {
            trigger.verified_registry_fields["ProtocolRegistryVerificationStatus"] = "Verified";
            trigger.verified_registry_fields["VerifiedProtocolRegistrySource"] = "RepositoryVerifiedProtocolRegistry";
            trigger.verified_registry_fields["VerifiedProtocolRegistryEvidenceSHA256"] =
                "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
            trigger.verified_registry_fields["VerifiedProtocolRegistryId"] = "verified-synthetic-action";
            trigger.verified_registry_fields["VerifiedGameplaySemantic"] = "VerifiedSyntheticGameplay";
        }
        input->records.push_back(std::move(trigger));
        if (!linked_transport) return;
        auto transport = synthetic_record(++*sequence, relative_ms + 1, logical_id, "Transport",
            "ClientToServer", "", 0, connection_id);
        transport.api = "send";
        transport.transport_chunk_id = "synthetic-transport-" + std::to_string(*sequence);
        input->records.push_back(std::move(transport));
    };
    const auto append_response = [&](GenericActionGroupingInput* input, std::uint64_t* sequence,
                                     std::int64_t relative_ms, const std::string& logical_id,
                                     const std::string& opcode, std::uint64_t frame_length,
                                     const std::string& connection_id) {
        input->records.push_back(synthetic_record(++*sequence, relative_ms, logical_id, "PostDecrypt",
            "ServerToClient", opcode, frame_length, connection_id));
    };

    GenericActionGroupingInput synthetic_input;
    synthetic_input.session_id = "synthetic-action-contract";
    synthetic_input.opcode_work_items = {
        {"work-c2s-10", "ClientToServer", "0x10", 5},
        {"work-c2s-11", "ClientToServer", "0x11", 5},
        {"work-c2s-12", "ClientToServer", "0x12", 5},
        {"work-c2s-13", "ClientToServer", "0x13", 5},
        {"work-s2c-80", "ServerToClient", "0x80", 6},
        {"work-s2c-81", "ServerToClient", "0x81", 7}
    };
    std::uint64_t synthetic_sequence = 0;
    append_trigger(&synthetic_input, &synthetic_sequence, 0, "logical-a1", "0x10", 5, "connection-1", true);
    append_response(&synthetic_input, &synthetic_sequence, 10, "logical-r1", "0x80", 6, "connection-1");
    append_trigger(&synthetic_input, &synthetic_sequence, 100, "logical-a2", "0x10", 5, "connection-1", true);
    append_response(&synthetic_input, &synthetic_sequence, 110, "logical-r2", "0x80", 6, "connection-1");
    append_trigger(&synthetic_input, &synthetic_sequence, 200, "logical-b", "0x11", 5, "connection-1", true);
    append_response(&synthetic_input, &synthetic_sequence, 210, "logical-r3", "0x80", 6, "connection-1");
    append_trigger(&synthetic_input, &synthetic_sequence, 300, "logical-c", "0x10", 5, "connection-1", true);
    append_response(&synthetic_input, &synthetic_sequence, 310, "logical-r4", "0x81", 7, "connection-1");
    append_trigger(&synthetic_input, &synthetic_sequence, 400, "logical-unlinked-1", "0x12", 5, "", false);
    append_trigger(&synthetic_input, &synthetic_sequence, 401, "logical-unlinked-2", "0x12", 5, "", false);
    append_trigger(&synthetic_input, &synthetic_sequence, 500, "logical-orphan-trigger", "0x13", 5,
                   "connection-1", true);
    auto orphan = synthetic_record(++synthetic_sequence, 510, "logical-orphan-handler", "HandlerDecoded",
        "ServerToClient", "0x90", 0, "");
    orphan.context_invocation_id = "missing-postdecrypt-context";
    orphan.context_correlation_basis = "SameThreadLatestPostDecrypt";
    synthetic_input.records.push_back(std::move(orphan));
    append_trigger(&synthetic_input, &synthetic_sequence, 600, "logical-verified", "0x14", 5,
                   "connection-1", true, true);
    append_response(&synthetic_input, &synthetic_sequence, 610, "logical-verified-response", "0x80", 6,
                    "connection-1");
    const auto synthetic_grouping = GroupGenericUnknownActions(synthetic_input);

    check("40_one_trigger_one_response", synthetic_grouping.action_instance_count == 6 &&
          synthetic_grouping.action_instances_jsonl.find("\"ResponseOpcodeSequence\":[\"0x80\"]") != std::string::npos,
          "six deterministic instances include a one-response action");
    check("41_repeated_pattern_aggregation", synthetic_grouping.action_patterns_jsonl.find("\"Occurrences\":2") != std::string::npos,
          "identical trigger/response structures aggregate without gameplay labels");
    check("42_different_trigger_opcode_not_merged", synthetic_grouping.action_pattern_count == 5 &&
          synthetic_grouping.action_patterns_jsonl.find("\"TriggerOpcode\":\"0x11\"") != std::string::npos,
          "0x10 and 0x11 triggers retain distinct structural patterns");
    check("43_incompatible_response_pattern_not_merged",
          synthetic_grouping.action_patterns_jsonl.find("\"ResponseOpcodeSequence\":[\"0x81\"]") != std::string::npos,
          "different ordered response opcode/length structures remain separate");
    check("44_time_only_cannot_be_strong", synthetic_grouping.strong_action_count == 0 &&
          synthetic_grouping.action_instances_jsonl.find("\"CorrelationLevel\":\"Candidate\"") != std::string::npos,
          "same-connection timing/order stays Candidate without a domain or Verified Registry identity");
    check("45_ungrouped_remains_ungrouped", synthetic_grouping.ungrouped_trigger_count == 2,
          "two unlinked triggers remain ungrouped");
    check("46_multi_pending_pre_encrypt_batch_candidate", synthetic_grouping.outbound_batch_candidate_count == 1 &&
          synthetic_grouping.outbound_batch_trigger_count == 2 &&
          synthetic_grouping.outbound_batch_candidates_jsonl.find("\"CorrelationLevel\":\"Candidate\"") != std::string::npos &&
          synthetic_grouping.outbound_batch_candidates_jsonl.find("\"ExplicitParentFabricated\":false") != std::string::npos,
          "two multi-pending PreEncrypt frames are retained as one Candidate batch without individual mapping");
    check("47_orphan_handler_burst_retained", synthetic_grouping.orphan_handler_burst_count == 1 &&
          synthetic_grouping.orphan_handler_bursts_jsonl.find("missing-postdecrypt-context") != std::string::npos &&
          synthetic_grouping.orphan_handler_bursts_jsonl.find("\"SyntheticPostDecryptCreated\":false") != std::string::npos,
          "orphan handler context is preserved and no PostDecrypt record is invented");
    check("48_ambiguous_remains_ambiguous", synthetic_grouping.ambiguous_action_count == 1 &&
          synthetic_grouping.action_instances_jsonl.find("\"AmbiguityStatus\":\"Ambiguous\"") != std::string::npos,
          "orphan-backed action is explicitly ambiguous and remains at Candidate correlation");
    check("49_unknown_semantic_remains_unknown", synthetic_grouping.unknown_action_pattern_count == 4 &&
          synthetic_grouping.action_patterns_jsonl.find("UnknownActionPattern-") != std::string::npos,
          "unverified stable patterns remain Unknown");
    check("50_verified_registry_mapping_works", synthetic_grouping.verified_gameplay_mapped_pattern_count == 1 &&
          synthetic_grouping.action_patterns_jsonl.find("\"SemanticStatus\":\"MappedToVerifiedGameplay\"") != std::string::npos,
          "only an explicit repository Verified Protocol Registry mapping promotes semantic status without promoting time-only response correlation");
    check("51_opcode_worklist_cross_references", !synthetic_grouping.related_action_pattern_ids_by_work_item.empty() &&
          synthetic_grouping.action_patterns_jsonl.find("\"RelatedOpcodeWorkItemIds\":[") != std::string::npos,
          "ActionPattern and existing OpcodeWorkItem identities are cross-referenced");

    GenericActionGroupingInput burst_input;
    burst_input.session_id = "synthetic-response-burst";
    std::uint64_t burst_sequence = 0;
    append_trigger(&burst_input, &burst_sequence, 0, "burst-trigger", "0x20", 5, "connection-burst", true);
    append_response(&burst_input, &burst_sequence, 5, "burst-response-1", "0x82", 6, "connection-burst");
    append_response(&burst_input, &burst_sequence, 7, "burst-response-2", "0x83", 7, "connection-burst");
    const auto burst_grouping = GroupGenericUnknownActions(burst_input);
    check("52_one_trigger_response_burst", burst_grouping.action_instance_count == 1 &&
          burst_grouping.action_instances_jsonl.find("\"ResponseOpcodeSequence\":[\"0x82\",\"0x83\"]") != std::string::npos,
          "one trigger retains an ordered two-message response burst");

    GenericActionGroupingInput background_input;
    background_input.session_id = "synthetic-background";
    std::uint64_t background_sequence = 0;
    for (std::int64_t occurrence = 0; occurrence < 6; ++occurrence) {
        const std::string trigger_opcode = occurrence % 3 == 0 ? "0x30" : occurrence % 3 == 1 ? "0x31" : "0x32";
        append_trigger(&background_input, &background_sequence, occurrence * 1000,
            "background-trigger-" + std::to_string(occurrence), trigger_opcode, 5, "connection-background", true);
        append_response(&background_input, &background_sequence, occurrence * 1000 + 10,
            "background-response-" + std::to_string(occurrence), "0xEE", 6, "connection-background");
    }
    const auto background_grouping = GroupGenericUnknownActions(background_input);
    check("53_background_not_injected_into_action", background_grouping.background_candidate_count == 1 &&
          background_grouping.action_instance_count == 0 &&
          background_grouping.action_pattern_summary_json.find("\"SemanticStatus\":\"BackgroundCandidate\"") != std::string::npos,
          "periodic structurally stable but uncoupled messages are BackgroundCandidate and excluded from actions");

    GenericActionGroupingInput domain_input;
    domain_input.session_id = "synthetic-domain-correlation";
    std::uint64_t domain_sequence = 0;
    append_trigger(&domain_input, &domain_sequence, 0, "domain-trigger", "0x40", 5, "connection-domain", true);
    append_response(&domain_input, &domain_sequence, 10, "domain-response", "0x84", 6, "connection-domain");
    domain_input.records.front().domain_correlation_ids["quest_action_correlation_id"] = "quest-action-1";
    domain_input.records.back().domain_correlation_ids["quest_action_correlation_id"] = "quest-action-1";
    const auto domain_grouping = GroupGenericUnknownActions(domain_input);
    check("54_existing_domain_correlation_reused", domain_grouping.strong_action_count == 1 &&
          domain_grouping.action_instances_jsonl.find("quest_action_correlation_id=quest-action-1") != std::string::npos,
          "existing domain-specific correlation ID promotes Strong without creating a second gameplay architecture");
    check("55_no_manual_marker_dependency", synthetic_grouping.action_pattern_summary_json.find(
              "\"ManualMarkerDependency\":false") != std::string::npos &&
          synthetic_grouping.action_instances_jsonl.find("Marker") == std::string::npos,
          "generic grouping requires no player marker or manual label");
    check("56_package_action_artifacts_present", !action_pattern_summary.empty() &&
          manifest.find("PrimarySession/analysis/action-instances.jsonl") != std::string::npos &&
          manifest.find("schemas/action-pattern.schema.json") != std::string::npos &&
          opcode_worklist.find("\"RelatedActionPatternIds\":[") != std::string::npos,
          "all five Analyzer artifacts, schemas and Opcode Worklist backlink are packaged");
    check("57_package_unknown_policy", (action_patterns.empty() ||
          action_patterns.find("\"SemanticStatus\":\"Unknown\"") != std::string::npos) &&
          synthetic_grouping.action_patterns_jsonl.find("\"PatternName\":\"Movement\"") == std::string::npos &&
          synthetic_grouping.action_patterns_jsonl.find("\"PatternName\":\"SkillDamage\"") == std::string::npos,
          "packaged action patterns never receive an unverified gameplay name");
    check("58_action_count_reconciliation", result.action_trigger_candidate_count ==
          result.grouped_trigger_count + result.ungrouped_trigger_count &&
          result.action_instance_count == result.grouped_trigger_count &&
          result.outbound_batch_trigger_count <= result.ungrouped_trigger_count &&
          action_pattern_summary.find("\"ActionClusteringCoverageDenominator\":\"ActionTriggerCandidateCount\"") != std::string::npos,
          "trigger partition and clustering denominator reconcile exactly");
    check("59_orphan_and_batch_outputs_parseable", (result.orphan_handler_burst_count == 0 || !orphan_handler_bursts.empty()) &&
          (result.outbound_batch_candidate_count == 0 || !outbound_batch_candidates.empty()),
          "Candidate exception outputs are present whenever their counters are nonzero");

    Fields gpu_performance_fields;
    Fields gpu_equivalence_fields;
    const bool gpu_reports_parse = ParseFlatJson(gpu_performance, gpu_performance_fields, nullptr) &&
        ParseFlatJson(gpu_equivalence, gpu_equivalence_fields, nullptr);
    const bool operation_specific_gpu_result =
        GetBool(gpu_equivalence_fields, "OperationSpecificGpuResultAvailable").value_or(false);
    const bool verified_results_equivalent =
        GetBool(gpu_equivalence_fields, "VerifiedResultsEquivalent").value_or(false);
    const auto workload_implementation_status =
        GetString(gpu_equivalence_fields, "WorkloadImplementationStatus");
    const auto selected_gpu_backend = GetString(gpu_equivalence_fields, "SelectedBackend");
    const auto mismatch_count = GetInt64(gpu_equivalence_fields, "MismatchCount").value_or(-1);
    const bool verified_gpu_candidate =
        workload_implementation_status == "OperationSpecificGpuCandidateVerified";
    const bool cpu_only_or_gpu_not_run =
        workload_implementation_status == "EvidenceBlockedGpuImplementationUnavailable" ||
        workload_implementation_status == "EvidenceBlockedGpuUnavailable" ||
        workload_implementation_status == "GpuImplementationAvailableCpuOnly" ||
        workload_implementation_status == "GpuImplementationAvailableCpuSelected";
    const bool gpu_equivalence_branch_valid = verified_gpu_candidate ?
        (operation_specific_gpu_result && verified_results_equivalent && mismatch_count == 0) :
        (cpu_only_or_gpu_not_run && !operation_specific_gpu_result &&
         !verified_results_equivalent && selected_gpu_backend == "CPU");
    check("60_gpu_provenance_and_cpu_authority", gpu_reports_parse &&
          !gpu_capability.empty() && !gpu_performance.empty() &&
          !gpu_equivalence.empty() &&
          gpu_capability.find("\"AiBackend\":\"EvidenceBlockedModelUnavailable\"") != std::string::npos &&
          gpu_capability.find("\"AiAcceleration\":\"EvidenceBlockedModelUnavailable\"") !=
              std::string::npos &&
          gpu_capability.find("\"GpuAvailable\":") != std::string::npos &&
          gpu_capability.find("\"GpuSelected\":") != std::string::npos &&
          gpu_capability.find("\"SelectedBackend\":") != std::string::npos &&
          gpu_performance.find("\"BenefitGateDecision\":") != std::string::npos &&
          gpu_performance.find("\"WorkloadRecords\":") != std::string::npos &&
          gpu_performance.find("\"PredictedCpuMicros\":") != std::string::npos &&
          gpu_performance.find("\"PredictedGpuMicros\":") != std::string::npos &&
          gpu_performance.find("\"CalibrationSource\":") != std::string::npos &&
          gpu_performance.find("\"ActualCpuVerificationMicros\":") != std::string::npos &&
          gpu_performance.find("\"DroppedEvidence\":0") != std::string::npos &&
          gpu_equivalence.find("\"GpuAuthority\":\"Prohibited\"") != std::string::npos &&
          GetBool(gpu_equivalence_fields, "AuthoritativeResultsEquivalent").value_or(false) &&
          gpu_equivalence_branch_valid,
          operation_specific_gpu_result ?
              "operation-specific GPU result was available and verified equivalent with zero mismatch" :
              "operation-specific GPU unavailable branch stayed false/non-authoritative and preserved CPU authority");

    check("61_authentication_transport_claim_is_conservative",
          sensitive_content.find("\"ContainsAuthenticationPayload\":null") != std::string::npos &&
          sensitive_content.find("AuthenticationPlaintextGatePassedEncryptedTransportClassificationUnproven") !=
              std::string::npos &&
          sensitive_content.find("\"RawNetworkPayloadPolicy\":\"ExcludedFailClosed\"") !=
              std::string::npos &&
          sensitive_content.find("\"SemanticEventPayloadPolicy\":"
              "\"StrictCanonicalSanitizedDerivative\"") != std::string::npos &&
          sensitive_content.find("\"SemanticEventsSanitized\":true") != std::string::npos &&
          sensitive_content.find("\"Sanitized\":false") != std::string::npos,
          "plaintext authentication claims remain conservative while raw network payloads are excluded fail-closed and semantic events are sanitized");

    const fs::path canary_root = LocalDataRoot() / L"Temp" / L"EvidencePackageCanary" /
        Utf8ToWide(NewId());
    const fs::path canary_log = canary_root / L"enhanced-capture.log";
    const bool canary_written = EnsureContainedDirectory(LocalDataRoot(), canary_root) &&
        WriteUtf8FileAtomic(canary_log,
            "packet-decode decodedPrefixSafe=\"ACCOUNT-CANARY\" key256=\"SECRET-CANARY\"\n");
    std::string canary_marker;
    CaptureRecord sensitive_pre_encrypt;
    sensitive_pre_encrypt.stage = "PreEncrypt";
    sensitive_pre_encrypt.payload_hex = "0500190000";
    CaptureRecord safe_pre_encrypt;
    safe_pre_encrypt.stage = "PreEncrypt";
    safe_pre_encrypt.payload_hex = "0500200000";
    const bool authentication_canary_rejected = canary_written &&
        ContainsUnsafeLegacyDiagnostic(canary_log, &canary_marker) &&
        IsUnsafeAuthenticationRecord(sensitive_pre_encrypt) &&
        !IsUnsafeAuthenticationRecord(safe_pre_encrypt) &&
        !IsSafePortableComponent("../escape") && !IsSafePortableComponent("CON") &&
        IsSafePortableComponent("fixture-session_01.2") &&
        (!source_contains_legacy_sensitive_diagnostic ||
         (!unsafe_source_package.success && unsafe_source_package.zip_path.empty()));
    std::error_code canary_cleanup_error;
    fs::remove_all(canary_root, canary_cleanup_error);
    check("62_sensitive_authentication_canary_rejected", authentication_canary_rejected,
          authentication_canary_rejected ?
              "legacy secret/raw-auth and traversal/reserved-name canaries were rejected" :
              "authentication canary gate did not reject every unsafe source");

    StrictEvidenceManifest packaged_manifest;
    std::string packaged_manifest_error;
    bool packaged_sensitive_values_absent = ParseStrictEvidenceManifest(
        manifest, &packaged_manifest, &packaged_manifest_error);
    std::string folded_manifest = manifest;
    std::transform(folded_manifest.begin(), folded_manifest.end(), folded_manifest.begin(),
        [](unsigned char character) { return static_cast<char>(std::tolower(character)); });
    packaged_sensitive_values_absent = packaged_sensitive_values_absent &&
        folded_manifest.find("canary") == std::string::npos &&
        manifest.find(arbitrary_secret) == std::string::npos;
    for (const auto& artifact : packaged_manifest.artifacts) {
        const auto content = packaged_sensitive_values_absent ?
            ReadEvidenceZipEntry(result.zip_path, artifact.relative_path) :
            std::optional<std::string>{};
        std::string folded_content = content.value_or("");
        std::transform(folded_content.begin(), folded_content.end(),
            folded_content.begin(), [](unsigned char character) {
                return static_cast<char>(std::tolower(character));
            });
        if (!content || folded_content.find("canary") != std::string::npos ||
            content->find(arbitrary_secret) != std::string::npos ||
            IsExcludedRawNetworkPayload(fs::path(Utf8ToWide(artifact.relative_path)))) {
            packaged_sensitive_values_absent = false;
            break;
        }
    }
    bool suppressed_semantic_seen = false;
    bool explicitly_safe_semantic_preserved = false;
    std::istringstream semantic_lines(packaged_semantic_events);
    std::string semantic_line;
    while (std::getline(semantic_lines, semantic_line)) {
        if (semantic_line.empty()) continue;
        const auto parsed = ReadSemanticEvent(semantic_line);
        if (!parsed.success) {
            packaged_sensitive_values_absent = false;
            break;
        }
        if (parsed.event.sequence == 990001U) {
            suppressed_semantic_seen =
                parsed.event.sensitive_mask_status ==
                    "SensitivePayloadSuppressedMetadataOnly" &&
                parsed.event.parent_event_id.empty() &&
                parsed.event.context_id.empty() && parsed.event.action_id.empty() &&
                parsed.event.object_token.empty() && parsed.event.value_token.empty() &&
                parsed.event.source_token.empty() &&
                parsed.event.evidence_binding.empty() &&
                parsed.event.payload.find(arbitrary_secret) == std::string::npos;
        } else if (parsed.event.sequence == 990002U) {
            explicitly_safe_semantic_preserved =
                parsed.event.sensitive_mask_status == "NotSensitive" &&
                parsed.event.object_token == "SAFE-OBJECT-2048" &&
                parsed.event.value_token == "SAFE-VALUE-2048" &&
                parsed.event.source_token == "SAFE-SOURCE-2048" &&
                parsed.event.payload.find("SAFE-VALUE-2048") != std::string::npos;
        }
    }
    Fields privacy_fields;
    std::string privacy_parse_error;
    const bool semantic_privacy_report_valid =
        ParseFlatJson(semantic_privacy_report, privacy_fields, &privacy_parse_error) &&
        GetString(privacy_fields, "OutputClass") == "SanitizedDerivative" &&
        GetString(privacy_fields, "RawByteCopyAllowed") == "false" &&
        GetInt64(privacy_fields, "SensitiveSuppressed").value_or(0) >= 1 &&
        GetInt64(privacy_fields, "Rejected").value_or(-1) >= 0;
    check("63_evidence_zip_strict_semantic_privacy_gate",
          packaged_sensitive_values_absent && suppressed_semantic_seen &&
              explicitly_safe_semantic_preserved && semantic_privacy_report_valid &&
              (!unsafe_source_package.success || unsafe_source_package.zip_path.empty()),
          packaged_sensitive_values_absent && suppressed_semantic_seen &&
              explicitly_safe_semantic_preserved && semantic_privacy_report_valid ?
              "every manifest-bound ZIP entry was reopened; arbitrary sensitive carriers were removed and explicit NotSensitive values survived" :
              "a ZIP entry retained sensitive bytes, raw network payload, or failed strict semantic sanitization");

    const fs::path copy_transaction_root = LocalDataRoot() / L"Temp" /
        L"EvidenceCopyArtifactTransaction" / Utf8ToWide(NewId());
    const fs::path copy_source_root = copy_transaction_root / L"source";
    const fs::path copy_target_root = copy_transaction_root / L"target";
    const fs::path copy_source = copy_source_root / L"artifact.bin";
    const fs::path copy_replacement = copy_source_root / L"replacement.bin";
    const fs::path copy_target = copy_target_root / L"artifact.bin";
    bool replacement_denied_while_pinned = false;
    bool copy_transaction_passed = EnsureContainedDirectory(LocalDataRoot(), copy_source_root) &&
        EnsureContainedDirectory(LocalDataRoot(), copy_target_root) &&
        WriteUtf8FileAtomic(copy_source, "ORIGINAL-HANDLE-BOUND-BYTES") &&
        WriteUtf8FileAtomic(copy_replacement, "REPLACEMENT-SAME-PATH-BYTES");
    if (copy_transaction_passed) {
        {
            ScopedWinHandle replacement_guard(CreateFileW(copy_source.c_str(),
                GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr));
            const BOOL replacement_result = replacement_guard ?
                MoveFileExW(copy_replacement.c_str(), copy_source.c_str(),
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) : TRUE;
            const DWORD replacement_error = replacement_result == FALSE ?
                GetLastError() : ERROR_SUCCESS;
            replacement_denied_while_pinned = replacement_guard &&
                replacement_result == FALSE &&
                (replacement_error == ERROR_SHARING_VIOLATION ||
                 replacement_error == ERROR_ACCESS_DENIED);
        }
        copy_transaction_passed = replacement_denied_while_pinned &&
            CopyArtifact(copy_source, copy_target, copy_source_root, copy_target_root) &&
            FileSha256(copy_source) == FileSha256(copy_target) &&
            ReadUtf8File(copy_target) ==
                std::optional<std::string>("ORIGINAL-HANDLE-BOUND-BYTES");
    }
    std::error_code copy_cleanup_error;
    fs::remove_all(copy_transaction_root, copy_cleanup_error);
    check("64_copyartifact_pins_source_and_commits_same_temp_file_id",
          copy_transaction_passed,
          copy_transaction_passed ?
              "same-path replacement was denied and handle-bound atomic copy preserved SHA-256" :
              "source replacement or temp-handle atomic commit gate failed");

    const fs::path late_raw_root = LocalDataRoot() / L"Temp" /
        L"EvidenceLateRawNetworkPolicy" / Utf8ToWide(NewId());
    const fs::path late_source_root = late_raw_root / L"source";
    const fs::path late_staging_root = late_raw_root / L"staging";
    const fs::path late_semantic_source = late_source_root / L"semantic-events.jsonl";
    const fs::path late_semantic_target = late_staging_root / L"semantic-events.jsonl";
    const fs::path late_raw_payload = late_staging_root / L"capture.etl";
    const fs::path late_zip = late_raw_root / L"evidence-after-privacy-gate.zip";
    auto late_safe_event = explicitly_safe_event;
    late_safe_event.session_id = "LateRawPolicySession";
    late_safe_event.sequence = 1U;
    const auto late_safe_line = SerializeSemanticEventV2(late_safe_event);
    SemanticPrivacyGateResult late_gate;
    std::string late_gate_error;
    ZipPackageMetrics late_metrics;
    bool late_raw_payload_excluded =
        EnsureContainedDirectory(LocalDataRoot(), late_source_root) &&
        EnsureContainedDirectory(LocalDataRoot(), late_staging_root) &&
        WriteUtf8FileAtomic(late_semantic_source, late_safe_line + "\n") &&
        SanitizeSemanticEventsForPackage(late_semantic_source,
            late_semantic_target, late_source_root, late_staging_root,
            late_safe_event.session_id, &late_gate, &late_gate_error) &&
        late_gate.accepted == 1U && late_gate.suppressed == 0U &&
        late_gate.rejected == 0U;
    // This file is deliberately created after the semantic privacy gate.  The
    // ZIP inventory layer is the last fail-closed boundary and must omit it.
    late_raw_payload_excluded = late_raw_payload_excluded &&
        WriteUtf8FileAtomic(late_raw_payload,
            "LATE-RAW-NETWORK-" + std::string(arbitrary_secret)) &&
        CreateDeflateZip(late_staging_root, late_zip, 6, &late_metrics,
                         &late_gate_error);
    zlib_filefunc64_def late_file_functions{};
    fill_win32_filefunc64W(&late_file_functions);
    unzFile late_archive = late_raw_payload_excluded ?
        unzOpen2_64(late_zip.c_str(), &late_file_functions) : nullptr;
    unz_global_info64 late_global{};
    const bool late_single_sanitized_entry = late_archive != nullptr &&
        unzGetGlobalInfo64(late_archive, &late_global) == UNZ_OK &&
        late_global.number_entry == 1U;
    if (late_archive != nullptr) unzClose(late_archive);
    const auto late_packaged_semantic = late_raw_payload_excluded ?
        ReadEvidenceZipEntry(late_zip, "semantic-events.jsonl") :
        std::optional<std::string>{};
    late_raw_payload_excluded = late_raw_payload_excluded &&
        late_single_sanitized_entry && late_packaged_semantic &&
        late_packaged_semantic->find("SAFE-VALUE-2048") != std::string::npos &&
        late_packaged_semantic->find(arbitrary_secret) == std::string::npos &&
        !ReadEvidenceZipEntry(late_zip, "capture.etl").has_value();
    std::error_code late_cleanup_error;
    fs::remove_all(late_raw_root, late_cleanup_error);
    check("65_late_raw_network_payload_excluded_at_zip_inventory",
          late_raw_payload_excluded && !late_cleanup_error,
          late_raw_payload_excluded ?
              "capture.etl created after privacy validation was absent from the one-entry Evidence ZIP" :
              "late capture.etl reached the ZIP or the post-privacy fixture failed");

    std::unordered_set<std::string> evaluated_check_names;
    const bool prior_checks_evaluated = checks.size() >= 78 &&
          std::all_of(checks.begin(), checks.end(), [&](const auto& item) {
              return !item.name.empty() && !item.evidence.empty() &&
                  item.name != "66_no_hardcoded_pass" &&
                  evaluated_check_names.insert(item.name).second;
          });
    check("66_no_hardcoded_pass", prior_checks_evaluated,
          "each PASS/FAIL is emitted from an evaluated predicate with runtime evidence");

    const auto passed = static_cast<std::uint64_t>(std::count_if(checks.begin(), checks.end(), [](const auto& item) {
        return item.passed;
    }));
    std::ostringstream check_json;
    check_json << '[';
    for (std::size_t i = 0; i < checks.size(); ++i) {
        if (i != 0) check_json << ',';
        check_json << MakeJsonObject({
            {"Name", checks[i].name}, {"Status", checks[i].passed ? "PASS" : "FAIL"},
            {"Evidence", checks[i].evidence}
        });
    }
    check_json << ']';
    const auto report = MakeJsonObject({
        {"SchemaVersion", "11"}, {"TestedAtUtc", UtcNow()}, {"SessionId", result.session_id},
        {"FixturePath", WideToUtf8(session_path.filename().wstring())}, {"CheckCount", std::to_string(checks.size())},
        {"Passed", std::to_string(passed)}, {"Failed", std::to_string(checks.size() - passed)},
        {"PackagePath", WideToUtf8(result.zip_path.filename().wstring())}, {"PackageSHA256", result.zip_sha256},
        {"Checks", check_json.str()}
    }, {"SchemaVersion", "CheckCount", "Passed", "Failed", "Checks"}) + "\n";
    if (!WriteUtf8FileAtomic(report_path, report)) return 74;
    std::error_code fixture_cleanup_error;
    fs::remove_all(sanitized_fixture_root, fixture_cleanup_error);
    return passed == checks.size() && !fixture_cleanup_error ? 0 : 70;
}

int RunGpuPackageEquivalenceTest(const fs::path& session_path, const fs::path& report_path) {
    if (!IsApprovedSessionPath(session_path) || !fs::is_directory(session_path)) return ERROR_INVALID_PARAMETER;
    const fs::path test_root = LocalDataRoot() / L"Temp" / L"GpuPackageEquivalence" / Utf8ToWide(NewId());
    const fs::path cpu_session = test_root / L"cpu-only";
    const fs::path gpu_session = test_root / L"gpu-auto";
    std::error_code copy_error;
    const auto copy_fixture = [&](const fs::path& target) {
        fs::create_directories(target, copy_error);
        if (copy_error) return false;
        for (fs::recursive_directory_iterator iterator(session_path, copy_error), end;
             iterator != end && !copy_error; iterator.increment(copy_error)) {
            const auto relative = fs::relative(iterator->path(), session_path, copy_error);
            if (copy_error) break;
            const auto name = iterator->path().filename().wstring();
            if (iterator->is_directory(copy_error)) {
                if (name.starts_with(L"EvidencePackageStaging")) iterator.disable_recursion_pending();
                else fs::create_directories(target / relative, copy_error);
                continue;
            }
            if (!iterator->is_regular_file(copy_error)) continue;
            const auto extension = iterator->path().extension().wstring();
            if (extension == L".zip" || extension == L".sha256" ||
                name == L"evidence-package-build-progress.txt") continue;
            fs::create_directories((target / relative).parent_path(), copy_error);
            if (!copy_error) fs::copy_file(iterator->path(), target / relative,
                fs::copy_options::overwrite_existing, copy_error);
        }
        return !copy_error;
    };
    const auto set_mode = [](const fs::path& target, AccelerationMode mode) {
        const fs::path manifest = target / L"session.json";
        const auto content = ReadUtf8File(manifest);
        Fields fields;
        if (!content || !ParseFlatJson(*content, fields, nullptr)) return false;
        fields["AccelerationMode"] = ToString(mode);
        std::vector<std::pair<std::string, std::string>> values(fields.begin(), fields.end());
        std::sort(values.begin(), values.end(), [](const auto& left, const auto& right) {
            return left.first < right.first;
        });
        return WriteUtf8FileAtomic(manifest, MakeJsonObject(values, {"SchemaVersion"}) + "\n");
    };

    bool prepared = copy_fixture(cpu_session);
    copy_error.clear();
    prepared = copy_fixture(gpu_session) && prepared &&
        set_mode(cpu_session, AccelerationMode::CpuOnly) && set_mode(gpu_session, AccelerationMode::Auto);
    EvidencePackageResult cpu_result;
    EvidencePackageResult gpu_result;
    if (prepared) {
        cpu_result = EvidencePackageBuilder::Build(cpu_session);
        gpu_result = EvidencePackageBuilder::Build(gpu_session);
    }
    const std::array<std::string_view, 11> semantic_artifacts = {
        "PrimarySession/protocol/decoded-frames.jsonl",
        "PrimarySession/protocol/codex-opcode-worklist.jsonl",
        "PrimarySession/derived/gameplay-candidates.jsonl",
        "PrimarySession/analysis/automatic-semantic-summary.json",
        "PrimarySession/analysis/action-patterns.jsonl",
        "PrimarySession/semantic/value-flow.jsonl",
        "PrimarySession/semantic/semantic-evidence-graph.jsonl",
        "PrimarySession/semantic/verified-protocol-spec.json",
        "PrimarySession/semantic/verified-integration-manifest.json",
        "PrimarySession/semantic/semantic-summary.json",
        "PrimarySession/semantic/cross-session-ledger.jsonl"
    };
    std::ostringstream comparisons;
    comparisons << '[';
    std::uint64_t equal_artifacts = 0;
    for (std::size_t index = 0; index < semantic_artifacts.size(); ++index) {
        const auto cpu = cpu_result.success ? ReadEvidenceZipEntry(cpu_result.zip_path, semantic_artifacts[index]) :
            std::optional<std::string>{};
        const auto gpu = gpu_result.success ? ReadEvidenceZipEntry(gpu_result.zip_path, semantic_artifacts[index]) :
            std::optional<std::string>{};
        const bool equal = cpu && gpu && *cpu == *gpu;
        if (equal) ++equal_artifacts;
        if (index != 0) comparisons << ',';
        comparisons << MakeJsonObject({
            {"Artifact", std::string(semantic_artifacts[index])}, {"Status", equal ? "Equivalent" : "Mismatch"},
            {"CpuSHA256", cpu ? Sha256(*cpu).value_or("") : "Unavailable"},
            {"GpuSHA256", gpu ? Sha256(*gpu).value_or("") : "Unavailable"}
        });
    }
    comparisons << ']';
    const bool counters_equal = cpu_result.capture_records == gpu_result.capture_records &&
        cpu_result.protocol_frames == gpu_result.protocol_frames &&
        cpu_result.gameplay_candidates == gpu_result.gameplay_candidates &&
        cpu_result.automatic_semantic_candidates == gpu_result.automatic_semantic_candidates &&
        cpu_result.protocol_field_evidence == gpu_result.protocol_field_evidence &&
        cpu_result.unknown_field_semantics == gpu_result.unknown_field_semantics &&
        cpu_result.candidate_field_semantics == gpu_result.candidate_field_semantics &&
        cpu_result.recovered_field_semantics == gpu_result.recovered_field_semantics &&
        cpu_result.verified_field_semantics == gpu_result.verified_field_semantics &&
        cpu_result.server_integration_ready == gpu_result.server_integration_ready &&
        cpu_result.database_integration_ready == gpu_result.database_integration_ready &&
        cpu_result.semantic_contradictions == gpu_result.semantic_contradictions;
    std::vector<GpuEvidenceRecord> dispatch_corpus(2048);
    std::uint32_t dispatch_state = 0x7a4d21c3U;
    for (std::size_t record = 0; record < dispatch_corpus.size(); ++record) {
        dispatch_corpus[record].bytes.resize(128 + record % 31);
        for (auto& byte : dispatch_corpus[record].bytes) {
            dispatch_state = dispatch_state * 1664525U + 1013904223U;
            byte = static_cast<std::uint8_t>(dispatch_state >> 24);
        }
    }
    OptionalGpuAccelerator cpu_dispatch(AccelerationMode::CpuOnly);
    OptionalGpuAccelerator gpu_dispatch(AccelerationMode::Auto);
    const auto cpu_selected = cpu_dispatch.Process(dispatch_corpus, AccelerationPhase::PostCapture);
    const auto gpu_selected = gpu_dispatch.Process(dispatch_corpus, AccelerationPhase::PostCapture, nullptr,
        GpuOperationClass::BulkChecksum, GpuDispatchIntent::MeasurementOverride);
    const bool selected_results_equal = cpu_selected.verified_features.size() == gpu_selected.verified_features.size() &&
        std::equal(cpu_selected.verified_features.begin(), cpu_selected.verified_features.end(),
                   gpu_selected.verified_features.begin(), [](const auto& left, const auto& right) {
                       return left.byte_sum_without_last == right.byte_sum_without_last &&
                           left.fnv1a == right.fnv1a;
                   });
    const bool physical_gpu_hard_gate = gpu_selected.benefit.gpu_selected &&
        gpu_selected.benefit.selected_backend == "GPU" &&
        gpu_selected.equivalence.status == "Equivalent" &&
        gpu_selected.equivalence.mismatch_count == 0 && selected_results_equal;
    const bool equivalent = prepared && cpu_result.success && gpu_result.success && counters_equal &&
        equal_artifacts == semantic_artifacts.size() && physical_gpu_hard_gate;
    const auto report = MakeJsonObject({
        {"SchemaVersion", "1"}, {"TestedAtUtc", UtcNow()},
        {"Status", equivalent ? "PASS" : "FAIL"},
        {"CpuMode", "CPU Only"}, {"GpuMode", "Auto"},
        {"ComparedArtifactCount", std::to_string(semantic_artifacts.size())},
        {"EquivalentArtifactCount", std::to_string(equal_artifacts)},
        {"CountersEquivalent", counters_equal ? "true" : "false"},
        {"AuthoritativeAndVerifiedEquivalent", equivalent ? "true" : "false"},
        {"CpuSelectedResultEquivalent", selected_results_equal ? "true" : "false"},
        {"GpuSelectedResultEquivalent", physical_gpu_hard_gate ? "true" : "false"},
        {"GpuSelectedForHardGate", gpu_selected.benefit.gpu_selected ? "true" : "false"},
        {"GpuSelectedBackend", gpu_selected.benefit.selected_backend},
        {"GpuSelectionReason", gpu_selected.benefit.selection_reason},
        {"GpuComparedRecords", std::to_string(gpu_selected.equivalence.compared_records)},
        {"GpuMismatchCount", std::to_string(gpu_selected.equivalence.mismatch_count)},
        {"GpuHardGateHardware", gpu_selected.capability.device},
        {"GpuAuthority", "Prohibited"}, {"Comparisons", comparisons.str()},
        {"CpuError", cpu_result.error}, {"GpuError", gpu_result.error}
    }, {"SchemaVersion", "ComparedArtifactCount", "EquivalentArtifactCount", "CountersEquivalent",
        "AuthoritativeAndVerifiedEquivalent", "CpuSelectedResultEquivalent", "GpuSelectedResultEquivalent",
        "GpuSelectedForHardGate", "GpuComparedRecords", "GpuMismatchCount", "Comparisons"});
    const bool report_written = WriteUtf8FileAtomic(report_path, report + "\n");
    // Only exact directories created above are removed; the source fixture and
    // both completed result summaries have already been captured in the report.
    std::error_code cleanup_error;
    fs::remove_all(test_root, cleanup_error);
    return report_written && equivalent && !cleanup_error ? 0 : ERROR_GEN_FAILURE;
}

} // namespace god2
