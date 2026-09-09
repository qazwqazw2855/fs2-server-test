#include "Core.h"

#include <shellapi.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <winver.h>
#include <objbase.h>

#include <algorithm>
#include <array>
#include <charconv>
#include <chrono>
#include <cmath>
#include <cctype>
#include <cwctype>
#include <fstream>
#include <iomanip>
#include <limits>
#include <sstream>

namespace god2 {
namespace {

std::string Trim(std::string_view value) {
    const auto first = value.find_first_not_of(" \t\r\n");
    if (first == std::string_view::npos) return {};
    const auto last = value.find_last_not_of(" \t\r\n");
    return std::string(value.substr(first, last - first + 1));
}

std::wstring BuildWin32ExtendedPath(const fs::path& path) {
    std::error_code ec;
    fs::path absolute = path.is_absolute() ? path : fs::absolute(path, ec);
    if (ec) absolute = path;
    std::wstring value = absolute.wstring();
    std::replace(value.begin(), value.end(), L'/', L'\\');
    if (value.rfind(LR"(\\?\)", 0) == 0 || value.rfind(LR"(\\.\)", 0) == 0) return value;
    if (value.rfind(LR"(\\)", 0) == 0) return LR"(\\?\UNC\)" + value.substr(2);
    return LR"(\\?\)" + value;
}

bool DirectoryExistsForFileIo(const fs::path& directory) {
    const DWORD attributes = GetFileAttributesW(BuildWin32ExtendedPath(directory).c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

bool EnsureDirectoriesForFileIo(const fs::path& directory) {
    if (directory.empty()) return true;
    if (DirectoryExistsForFileIo(directory)) return true;
    std::error_code ec;
    fs::path absolute = directory.is_absolute() ? directory : fs::absolute(directory, ec);
    if (ec) absolute = directory;
    fs::path current;
    for (const auto& part : absolute) {
        current /= part;
        if (current.empty() || current == current.root_path() || current.filename().empty()) continue;
        const auto current_text = current.wstring();
        if (current_text.size() == 2 && current_text[1] == L':') continue;
        const std::wstring current_path = BuildWin32ExtendedPath(current);
        const DWORD attributes = GetFileAttributesW(current_path.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES) {
            if ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0) return false;
            continue;
        }
        if (!CreateDirectoryW(current_path.c_str(), nullptr)) {
            const DWORD code = GetLastError();
            if (code != ERROR_ALREADY_EXISTS) return false;
            const DWORD retry_attributes = GetFileAttributesW(current_path.c_str());
            if (retry_attributes == INVALID_FILE_ATTRIBUTES ||
                (retry_attributes & FILE_ATTRIBUTE_DIRECTORY) == 0) return false;
        }
    }
    return DirectoryExistsForFileIo(directory);
}

std::string WindowsProductName(DWORD product) {
    switch (product) {
    case PRODUCT_ULTIMATE: return "Ultimate";
    case PRODUCT_HOME_BASIC: return "Home Basic";
    case PRODUCT_HOME_PREMIUM: return "Home Premium";
    case PRODUCT_ENTERPRISE: return "Enterprise";
    case PRODUCT_BUSINESS: return "Business";
    case PRODUCT_STARTER: return "Starter";
    case PRODUCT_PROFESSIONAL: return "Professional";
    case PRODUCT_PROFESSIONAL_N: return "Professional N";
    case PRODUCT_CORE: return "Home";
    case PRODUCT_CORE_N: return "Home N";
    case PRODUCT_EDUCATION: return "Education";
    case PRODUCT_EDUCATION_N: return "Education N";
    case PRODUCT_STANDARD_SERVER: return "Standard Server";
    case PRODUCT_STANDARD_SERVER_CORE: return "Standard Server Core";
    case PRODUCT_DATACENTER_SERVER: return "Datacenter Server";
    case PRODUCT_DATACENTER_SERVER_CORE: return "Datacenter Server Core";
    case PRODUCT_ENTERPRISE_SERVER: return "Enterprise Server";
    case PRODUCT_ENTERPRISE_SERVER_CORE: return "Enterprise Server Core";
    case PRODUCT_WEB_SERVER: return "Web Server";
    default: return "Product " + std::to_string(product);
    }
}

std::string WindowsReleaseName(DWORD major, DWORD minor, DWORD build, BYTE product_type) {
    const bool workstation = product_type == VER_NT_WORKSTATION;
    if (major == 6 && minor == 1) return workstation ? "Windows 7" : "Windows Server 2008 R2";
    if (major == 6 && minor == 2) return workstation ? "Windows 8" : "Windows Server 2012";
    if (major == 6 && minor == 3) return workstation ? "Windows 8.1" : "Windows Server 2012 R2";
    if (major == 10 && workstation) return build >= 22'000 ? "Windows 11" : "Windows 10";
    if (major == 10) return "Windows Server";
    return "Windows " + std::to_string(major) + "." + std::to_string(minor);
}

std::string UnescapeJsonString(std::string_view value, bool* ok) {
    std::string result;
    result.reserve(value.size());
    *ok = true;
    for (std::size_t i = 0; i < value.size(); ++i) {
        const char c = value[i];
        if (c != '\\') {
            result.push_back(c);
            continue;
        }
        if (++i >= value.size()) {
            *ok = false;
            return {};
        }
        switch (value[i]) {
        case '"': result.push_back('"'); break;
        case '\\': result.push_back('\\'); break;
        case '/': result.push_back('/'); break;
        case 'b': result.push_back('\b'); break;
        case 'f': result.push_back('\f'); break;
        case 'n': result.push_back('\n'); break;
        case 'r': result.push_back('\r'); break;
        case 't': result.push_back('\t'); break;
        case 'u': {
            if (i + 4 >= value.size()) {
                *ok = false;
                return {};
            }
            unsigned code = 0;
            for (int j = 0; j < 4; ++j) {
                const char h = value[++i];
                code <<= 4;
                if (h >= '0' && h <= '9') code += static_cast<unsigned>(h - '0');
                else if (h >= 'a' && h <= 'f') code += static_cast<unsigned>(h - 'a' + 10);
                else if (h >= 'A' && h <= 'F') code += static_cast<unsigned>(h - 'A' + 10);
                else { *ok = false; return {}; }
            }
            if (code <= 0x7f) result.push_back(static_cast<char>(code));
            else if (code <= 0x7ff) {
                result.push_back(static_cast<char>(0xc0 | (code >> 6)));
                result.push_back(static_cast<char>(0x80 | (code & 0x3f)));
            } else {
                result.push_back(static_cast<char>(0xe0 | (code >> 12)));
                result.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3f)));
                result.push_back(static_cast<char>(0x80 | (code & 0x3f)));
            }
            break;
        }
        default: *ok = false; return {};
        }
    }
    return result;
}

std::wstring QuoteWindowsArgument(const std::wstring& argument) {
    if (argument.find_first_of(L" \t\"") == std::wstring::npos) return argument;
    std::wstring result = L"\"";
    std::size_t slashes = 0;
    for (const wchar_t c : argument) {
        if (c == L'\\') {
            ++slashes;
        } else if (c == L'"') {
            result.append(slashes * 2 + 1, L'\\');
            result.push_back(L'"');
            slashes = 0;
        } else {
            result.append(slashes, L'\\');
            result.push_back(c);
            slashes = 0;
        }
    }
    result.append(slashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}

bool ContainsInsensitive(std::string_view haystack, std::string_view needle) {
    std::string h(haystack);
    std::string n(needle);
    std::transform(h.begin(), h.end(), h.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    std::transform(n.begin(), n.end(), n.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return h.find(n) != std::string::npos;
}

std::uint32_t ReadU32(const std::vector<std::uint8_t>& bytes, std::size_t offset) {
    if (offset + 4 > bytes.size()) return 0;
    return static_cast<std::uint32_t>(bytes[offset]) |
           (static_cast<std::uint32_t>(bytes[offset + 1]) << 8) |
           (static_cast<std::uint32_t>(bytes[offset + 2]) << 16) |
           (static_cast<std::uint32_t>(bytes[offset + 3]) << 24);
}

std::uint16_t ReadU16(const std::vector<std::uint8_t>& bytes, std::size_t offset) {
    if (offset + 2 > bytes.size()) return 0;
    return static_cast<std::uint16_t>(bytes[offset]) |
           static_cast<std::uint16_t>(bytes[offset + 1] << 8);
}

std::optional<std::size_t> RvaToOffset(const std::vector<std::uint8_t>& bytes,
                                       std::uint32_t rva,
                                       std::size_t section_table,
                                       std::uint16_t section_count) {
    for (std::uint16_t i = 0; i < section_count; ++i) {
        const std::size_t s = section_table + static_cast<std::size_t>(i) * 40;
        if (s + 40 > bytes.size()) return std::nullopt;
        const auto virtual_size = ReadU32(bytes, s + 8);
        const auto virtual_address = ReadU32(bytes, s + 12);
        const auto raw_size = ReadU32(bytes, s + 16);
        const auto raw_pointer = ReadU32(bytes, s + 20);
        const auto span = std::max(virtual_size, raw_size);
        if (rva >= virtual_address && rva < virtual_address + span) {
            return static_cast<std::size_t>(raw_pointer) + (rva - virtual_address);
        }
    }
    return std::nullopt;
}

} // namespace

std::wstring Win32ExtendedPathForFileIo(const fs::path& path) {
    return BuildWin32ExtendedPath(path);
}

std::wstring Utf8ToWide(std::string_view value) {
    if (value.empty()) return {};
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
                                         static_cast<int>(value.size()), nullptr, 0);
    if (size <= 0) return {};
    std::wstring result(static_cast<std::size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
                        result.data(), size);
    return result;
}

std::string WideToUtf8(std::wstring_view value) {
    if (value.empty()) return {};
    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
                                         static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (size <= 0) return {};
    std::string result(static_cast<std::size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
                        result.data(), size, nullptr, nullptr);
    return result;
}

std::string JsonEscape(std::string_view value) {
    std::ostringstream output;
    for (const unsigned char c : value) {
        switch (c) {
        case '"': output << "\\\""; break;
        case '\\': output << "\\\\"; break;
        case '\b': output << "\\b"; break;
        case '\f': output << "\\f"; break;
        case '\n': output << "\\n"; break;
        case '\r': output << "\\r"; break;
        case '\t': output << "\\t"; break;
        default:
            if (c < 0x20) {
                output << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(c);
            } else {
                output << static_cast<char>(c);
            }
        }
    }
    return output.str();
}

std::string CsvEscape(std::string_view value) {
    if (value.find_first_of(",\"\r\n") == std::string_view::npos) return std::string(value);
    std::string result = "\"";
    for (const char c : value) {
        if (c == '"') result += "\"\"";
        else result.push_back(c);
    }
    result.push_back('"');
    return result;
}

std::string SqlQuote(std::string_view value) {
    std::string result = "'";
    for (const char c : value) {
        if (c == '\'') result += "''";
        else result.push_back(c);
    }
    result.push_back('\'');
    return result;
}

std::string UtcNow() {
    FILETIME ft{};
    using PreciseFn = VOID(WINAPI*)(LPFILETIME);
    const HMODULE kernel = GetModuleHandleW(L"kernel32.dll");
    const auto precise = kernel == nullptr ? nullptr :
        reinterpret_cast<PreciseFn>(GetProcAddress(kernel, "GetSystemTimePreciseAsFileTime"));
    if (precise != nullptr) precise(&ft);
    else GetSystemTimeAsFileTime(&ft);

    SYSTEMTIME st{};
    FileTimeToSystemTime(&ft, &st);
    std::ostringstream output;
    output << std::setfill('0') << std::setw(4) << st.wYear << '-' << std::setw(2) << st.wMonth << '-'
           << std::setw(2) << st.wDay << 'T' << std::setw(2) << st.wHour << ':' << std::setw(2)
           << st.wMinute << ':' << std::setw(2) << st.wSecond << '.' << std::setw(3)
           << st.wMilliseconds << 'Z';
    return output.str();
}

std::string NewId() {
    const auto fallback = [] {
        return std::to_string(GetTickCount64()) + "-" + std::to_string(GetCurrentProcessId());
    };
    GUID guid{};
    if (CoCreateGuid(&guid) != S_OK) {
        return fallback();
    }
    std::array<wchar_t, 64> text{};
    if (StringFromGUID2(guid, text.data(), static_cast<int>(text.size())) <= 1) return fallback();
    std::wstring value(text.data());
    value.erase(std::remove(value.begin(), value.end(), L'{'), value.end());
    value.erase(std::remove(value.begin(), value.end(), L'}'), value.end());
    return WideToUtf8(value);
}

std::string ToString(EvidenceLevel value) {
    switch (value) {
    case EvidenceLevel::Candidate: return "Candidate";
    case EvidenceLevel::Derived: return "Derived";
    case EvidenceLevel::Transmitted: return "Transmitted";
    case EvidenceLevel::Verified: return "Verified";
    default: return "Unknown";
    }
}

std::string ToString(AnalysisMode value) {
    switch (value) {
    case AnalysisMode::Realtime: return "Realtime";
    case AnalysisMode::NearRealtime: return "NearRealtime";
    default: return "PostCapture";
    }
}

std::optional<double> GetDouble(const Fields& fields, std::string_view key) {
    const auto it = fields.find(std::string(key));
    if (it == fields.end()) return std::nullopt;
    char* end = nullptr;
    const double value = std::strtod(it->second.c_str(), &end);
    if (end == it->second.c_str() || *end != '\0' || !std::isfinite(value)) return std::nullopt;
    return value;
}

std::optional<std::int64_t> GetInt64(const Fields& fields, std::string_view key) {
    const auto it = fields.find(std::string(key));
    if (it == fields.end()) return std::nullopt;
    std::int64_t value = 0;
    const auto result = std::from_chars(it->second.data(), it->second.data() + it->second.size(), value);
    if (result.ec != std::errc{} || result.ptr != it->second.data() + it->second.size()) return std::nullopt;
    return value;
}

std::optional<bool> GetBool(const Fields& fields, std::string_view key) {
    const auto value = GetString(fields, key);
    if (value == "true" || value == "1" || value == "True") return true;
    if (value == "false" || value == "0" || value == "False") return false;
    return std::nullopt;
}

std::string GetString(const Fields& fields, std::string_view key, std::string_view fallback) {
    const auto it = fields.find(std::string(key));
    return it == fields.end() ? std::string(fallback) : it->second;
}

std::vector<std::string> SplitList(std::string_view value) {
    std::vector<std::string> result;
    std::size_t start = 0;
    while (start <= value.size()) {
        const auto end = value.find_first_of(",;|", start);
        const auto part = Trim(value.substr(start, end == std::string_view::npos ? value.size() - start : end - start));
        if (!part.empty()) result.push_back(part);
        if (end == std::string_view::npos) break;
        start = end + 1;
    }
    return result;
}

std::string JoinList(const std::vector<std::string>& values) {
    std::string result;
    for (const auto& value : values) {
        if (!result.empty()) result.push_back(';');
        result += value;
    }
    return result;
}

bool ParseFlatJson(std::string_view json, Fields& output, std::string* error) {
    output.clear();
    std::size_t i = 0;
    auto fail = [&](std::string_view message) {
        if (error != nullptr) *error = std::string(message) + " at offset " + std::to_string(i);
        return false;
    };
    const auto skip_at = [&](std::size_t& cursor) {
        // RFC 8259 permits exactly SP, HTAB, LF and CR as JSON whitespace.
        // Locale-dependent isspace() would also accept form-feed/vertical-tab.
        while (cursor < json.size() &&
               (json[cursor] == ' ' || json[cursor] == '\t' ||
                json[cursor] == '\n' || json[cursor] == '\r')) ++cursor;
    };
    auto skip = [&]() { skip_at(i); };
    const auto parse_string = [&](std::size_t& cursor, std::string* decoded) {
        if (cursor >= json.size() || json[cursor] != '"') return false;
        const std::size_t content_start = ++cursor;
        bool escaped = false;
        while (cursor < json.size()) {
            const unsigned char c = static_cast<unsigned char>(json[cursor]);
            if (!escaped && c == '"') break;
            if (!escaped && c < 0x20) return false;
            if (!escaped && c == '\\') escaped = true;
            else escaped = false;
            ++cursor;
        }
        if (cursor >= json.size()) return false;
        bool ok = false;
        const auto value = UnescapeJsonString(
            json.substr(content_start, cursor - content_start), &ok);
        if (!ok) return false;
        ++cursor;
        if (decoded != nullptr) *decoded = value;
        return true;
    };
    std::string parse_detail;
    const auto consume_value = [&](auto&& self, std::size_t& cursor, unsigned depth) -> bool {
        if (depth > 64) {
            parse_detail = "JSON nesting limit exceeded";
            return false;
        }
        skip_at(cursor);
        if (cursor >= json.size()) return false;
        if (json[cursor] == '"') return parse_string(cursor, nullptr);
        if (json[cursor] == '{') {
            ++cursor;
            skip_at(cursor);
            std::set<std::string> keys;
            if (cursor < json.size() && json[cursor] == '}') { ++cursor; return true; }
            for (;;) {
                std::string key;
                if (!parse_string(cursor, &key) || !keys.insert(key).second) {
                    parse_detail = "invalid or duplicate nested object key";
                    return false;
                }
                skip_at(cursor);
                if (cursor >= json.size() || json[cursor++] != ':') return false;
                if (!self(self, cursor, depth + 1)) return false;
                skip_at(cursor);
                if (cursor < json.size() && json[cursor] == ',') { ++cursor; skip_at(cursor); continue; }
                if (cursor < json.size() && json[cursor] == '}') { ++cursor; return true; }
                return false;
            }
        }
        if (json[cursor] == '[') {
            ++cursor;
            skip_at(cursor);
            if (cursor < json.size() && json[cursor] == ']') { ++cursor; return true; }
            for (;;) {
                if (!self(self, cursor, depth + 1)) return false;
                skip_at(cursor);
                if (cursor < json.size() && json[cursor] == ',') { ++cursor; skip_at(cursor); continue; }
                if (cursor < json.size() && json[cursor] == ']') { ++cursor; return true; }
                return false;
            }
        }
        for (const std::string_view literal : {std::string_view("true"),
                                               std::string_view("false"),
                                               std::string_view("null")}) {
            if (json.substr(cursor, literal.size()) == literal) {
                cursor += literal.size();
                return true;
            }
        }
        std::size_t number = cursor;
        if (json[number] == '-') ++number;
        if (number >= json.size()) return false;
        if (json[number] == '0') {
            ++number;
            if (number < json.size() && std::isdigit(static_cast<unsigned char>(json[number])))
                return false;
        } else if (json[number] >= '1' && json[number] <= '9') {
            do { ++number; } while (number < json.size() &&
                std::isdigit(static_cast<unsigned char>(json[number])));
        } else {
            return false;
        }
        if (number < json.size() && json[number] == '.') {
            ++number;
            const std::size_t fraction = number;
            while (number < json.size() &&
                   std::isdigit(static_cast<unsigned char>(json[number]))) ++number;
            if (number == fraction) return false;
        }
        if (number < json.size() && (json[number] == 'e' || json[number] == 'E')) {
            ++number;
            if (number < json.size() && (json[number] == '+' || json[number] == '-')) ++number;
            const std::size_t exponent = number;
            while (number < json.size() &&
                   std::isdigit(static_cast<unsigned char>(json[number]))) ++number;
            if (number == exponent) return false;
        }
        cursor = number;
        return true;
    };
    skip();
    if (i >= json.size() || json[i++] != '{') return fail("expected object");
    while (true) {
        skip();
        if (i < json.size() && json[i] == '}') {
            ++i;
            skip();
            return i == json.size() ? true : fail("trailing content");
        }
        std::string key;
        if (!parse_string(i, &key)) return fail("invalid key");
        skip();
        if (i >= json.size() || json[i++] != ':') return fail("expected colon");
        skip();
        if (i >= json.size()) return fail("missing value");
        std::string value;
        if (json[i] == '"') {
            if (!parse_string(i, &value)) return fail("invalid string value");
        } else {
            const std::size_t value_start = i;
            if (!consume_value(consume_value, i, 0))
                return fail(parse_detail.empty() ? "invalid value" : parse_detail);
            value = Trim(json.substr(value_start, i - value_start));
            if (value == "null") value.clear();
        }
        if (!output.emplace(key, value).second) return fail("duplicate key");
        skip();
        if (i < json.size() && json[i] == ',') {
            ++i;
            std::size_t next = i;
            skip_at(next);
            if (next >= json.size() || json[next] == '}')
                return fail("trailing comma");
            continue;
        }
        if (i < json.size() && json[i] == '}') {
            ++i;
            skip();
            return i == json.size() ? true : fail("trailing content");
        }
        return fail("expected comma or object end");
    }
}

std::string MakeJsonObject(const std::vector<std::pair<std::string, std::string>>& fields,
                           const std::set<std::string>& raw_json_keys) {
    std::ostringstream output;
    output << '{';
    bool first = true;
    for (const auto& [key, value] : fields) {
        if (!first) output << ',';
        first = false;
        output << '"' << JsonEscape(key) << "\":";
        if (raw_json_keys.contains(key)) output << (value.empty() ? "null" : value);
        else output << '"' << JsonEscape(value) << '"';
    }
    output << '}';
    return output.str();
}

bool WriteUtf8File(const fs::path& path, std::string_view value, bool append) {
    if (path.empty() || path.parent_path().empty()) return false;
    if (!EnsureDirectoriesForFileIo(path.parent_path())) return false;
    const DWORD disposition = append ? OPEN_ALWAYS : CREATE_ALWAYS;
    const std::wstring file_path = Win32ExtendedPathForFileIo(path);
    HANDLE file = CreateFileW(file_path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                              disposition, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    bool ok = true;
    if (append) {
        LARGE_INTEGER zero{};
        ok = SetFilePointerEx(file, zero, nullptr, FILE_END) == TRUE;
    }
    std::size_t written_total = 0;
    while (ok && written_total < value.size()) {
        const DWORD chunk = static_cast<DWORD>(std::min<std::size_t>(
            value.size() - written_total, 1024u * 1024u));
        DWORD written = 0;
        if (!WriteFile(file, value.data() + written_total, chunk, &written, nullptr) ||
            written != chunk) {
            ok = false;
            break;
        }
        written_total += written;
    }
    CloseHandle(file);
    return ok;
}

bool WriteUtf8FileAtomic(const fs::path& path, std::string_view value) {
    if (path.empty() || path.parent_path().empty()) return false;
    const auto temporary = path.parent_path() / (path.filename().wstring() + L".tmp-" + Utf8ToWide(NewId()));
    if (!WriteUtf8File(temporary, value)) return false;
    const std::wstring temporary_path = Win32ExtendedPathForFileIo(temporary);
    const std::wstring target_path = Win32ExtendedPathForFileIo(path);
    if (MoveFileExW(temporary_path.c_str(), target_path.c_str(),
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return true;
    DeleteFileW(temporary_path.c_str());
    return false;
}

std::optional<std::string> ReadUtf8File(const fs::path& path) {
    const std::wstring file_path = Win32ExtendedPathForFileIo(path);
    HANDLE file = CreateFileW(file_path.c_str(), GENERIC_READ,
                              FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return std::nullopt;
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart < 0 ||
        static_cast<unsigned long long>(size.QuadPart) >
            static_cast<unsigned long long>((std::numeric_limits<std::size_t>::max)())) {
        CloseHandle(file);
        return std::nullopt;
    }
    std::string value;
    value.resize(static_cast<std::size_t>(size.QuadPart));
    std::size_t read_total = 0;
    bool ok = true;
    while (read_total < value.size()) {
        const DWORD chunk = static_cast<DWORD>(std::min<std::size_t>(
            value.size() - read_total, 1024u * 1024u));
        DWORD read = 0;
        if (!ReadFile(file, value.data() + read_total, chunk, &read, nullptr)) {
            ok = false;
            break;
        }
        if (read == 0) break;
        read_total += read;
    }
    CloseHandle(file);
    if (!ok) return std::nullopt;
    value.resize(read_total);
    return value;
}

bool IsAdministrator() {
    BOOL member = FALSE;
    SID_IDENTIFIER_AUTHORITY authority = SECURITY_NT_AUTHORITY;
    PSID administrators = nullptr;
    if (AllocateAndInitializeSid(&authority, 2, SECURITY_BUILTIN_DOMAIN_RID,
                                 DOMAIN_ALIAS_RID_ADMINS, 0, 0, 0, 0, 0, 0,
                                 &administrators)) {
        CheckTokenMembership(nullptr, administrators, &member);
        FreeSid(administrators);
    }
    return member == TRUE;
}

fs::path ExecutablePath() {
    std::vector<wchar_t> buffer(32'768);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    return length == 0 ? fs::path{} : fs::path(std::wstring(buffer.data(), length));
}

fs::path LocalDataRoot() {
    const auto validated_root = [](const fs::path& base) -> fs::path {
        if (!base.is_absolute()) return {};
        const auto product = base / L"God2Classic";
        const auto capture = product / L"PacketCapture";
        for (const auto& path : {product, capture}) {
            const DWORD attributes = GetFileAttributesW(path.c_str());
            if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return {};
        }
        std::error_code executable_error;
        std::error_code base_error;
        const auto executable_directory = fs::weakly_canonical(ExecutablePath().parent_path(), executable_error);
        const auto canonical_base = fs::weakly_canonical(base, base_error);
        if (!executable_error && !base_error && executable_directory == canonical_base) return {};
        return capture;
    };
    std::array<wchar_t, MAX_PATH> known_folder{};
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA | CSIDL_FLAG_CREATE, nullptr,
                                   SHGFP_TYPE_CURRENT, known_folder.data()))) {
        const auto root = validated_root(fs::path(known_folder.data()));
        if (!root.empty()) return root;
    }
    std::vector<wchar_t> buffer(32'768);
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length != 0 && length < buffer.size()) {
        const auto root = validated_root(fs::path(std::wstring(buffer.data(), length)));
        if (!root.empty()) return root;
    }
    return {};
}

fs::path DefaultSessionsRoot() {
    const auto root = LocalDataRoot();
    return root.empty() ? fs::path{} : root / L"Sessions";
}

bool IsApprovedSessionPath(const fs::path& path) {
    const auto local_data_root = LocalDataRoot();
    if (local_data_root.empty() || path.empty() || !path.is_absolute()) return false;
    std::error_code ec;
    auto root = fs::weakly_canonical(local_data_root, ec).wstring();
    if (ec) return false;
    auto target = fs::weakly_canonical(path, ec).wstring();
    if (ec) return false;
    std::transform(root.begin(), root.end(), root.begin(), ::towlower);
    std::transform(target.begin(), target.end(), target.begin(), ::towlower);
    if (target.size() < root.size() || target.compare(0, root.size(), root) != 0) return false;
    return target.size() == root.size() || target[root.size()] == L'\\' || target[root.size()] == L'/';
}

OsVersion DetectOsVersion() {
    OsVersion result;
    BYTE product_type = VER_NT_WORKSTATION;
    using RtlGetVersionFn = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);
    const HMODULE ntdll = LoadLibraryW(L"ntdll.dll");
    if (ntdll != nullptr) {
        const auto rtl_get_version = reinterpret_cast<RtlGetVersionFn>(GetProcAddress(ntdll, "RtlGetVersion"));
        if (rtl_get_version != nullptr) {
            RTL_OSVERSIONINFOEXW info{};
            info.dwOSVersionInfoSize = sizeof(info);
            if (rtl_get_version(reinterpret_cast<PRTL_OSVERSIONINFOW>(&info)) == 0) {
                result.major = info.dwMajorVersion;
                result.minor = info.dwMinorVersion;
                result.build = info.dwBuildNumber;
                result.service_pack_major = info.wServicePackMajor;
                result.service_pack_minor = info.wServicePackMinor;
                product_type = info.wProductType;
            }
        }
        FreeLibrary(ntdll);
    }
    SYSTEM_INFO system{};
    GetNativeSystemInfo(&system);
    result.architecture = system.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_AMD64 ? "x64" :
                          system.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_ARM64 ? "arm64" : "unsupported";
    DWORD product = PRODUCT_UNDEFINED;
    const bool product_available = GetProductInfo(result.major, result.minor,
                                                  result.service_pack_major, result.service_pack_minor,
                                                  &product) == TRUE;
    result.edition = WindowsReleaseName(result.major, result.minor, result.build, product_type) +
                     (product_available ? " " + WindowsProductName(product) : " Unknown Edition");
    return result;
}

bool BackendPolicyAllowsPktMon(DWORD os_major, DWORD) {
    return os_major >= 10;
}

bool IsTraceStopFinalizationProgress(std::string_view output) {
    std::string normalized(output);
    std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return normalized.find("merging traces") != std::string::npos ||
           normalized.find("generating data collection") != std::string::npos ||
           normalized.find("generating report") != std::string::npos ||
           normalized.find("correlating events") != std::string::npos ||
           normalized.find("trace file location") != std::string::npos;
}

ProcessResult RunProcess(const fs::path& executable,
                         const std::wstring& arguments,
                         DWORD timeout_ms,
                         bool hidden) {
    ProcessResult result;
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    HANDLE read_pipe = nullptr;
    HANDLE write_pipe = nullptr;
    if (!CreatePipe(&read_pipe, &write_pipe, &security, 0)) return result;
    SetHandleInformation(read_pipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES | (hidden ? STARTF_USESHOWWINDOW : 0);
    startup.wShowWindow = hidden ? SW_HIDE : SW_SHOW;
    startup.hStdOutput = write_pipe;
    startup.hStdError = write_pipe;
    startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);

    PROCESS_INFORMATION process{};
    std::wstring command = QuoteWindowsArgument(executable.wstring());
    if (!arguments.empty()) command += L" " + arguments;
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');
    const BOOL created = CreateProcessW(executable.c_str(), mutable_command.data(), nullptr, nullptr, TRUE,
                                        CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process);
    const DWORD create_error = created ? ERROR_SUCCESS : GetLastError();
    CloseHandle(write_pipe);
    if (!created) {
        CloseHandle(read_pipe);
        result.exit_code = create_error;
        return result;
    }
    std::array<char, 4096> buffer{};
    constexpr std::size_t kMaximumCapturedOutput = 1024 * 1024;
    constexpr std::string_view kTruncatedMarker = "\n[process output truncated]\n";
    bool output_truncated = false;
    const auto append_output = [&](const char* data, std::size_t size) {
        const auto content_limit = kMaximumCapturedOutput - kTruncatedMarker.size();
        const auto remaining = result.output.size() < content_limit ? content_limit - result.output.size() : 0;
        result.output.append(data, std::min(size, remaining));
        if (size > remaining) output_truncated = true;
    };
    result.started = true;
    const ULONGLONG started_at = GetTickCount64();
    bool timed_out = false;
    while (true) {
        DWORD available = 0;
        while (PeekNamedPipe(read_pipe, nullptr, 0, nullptr, &available, nullptr) && available > 0) {
            DWORD read = 0;
            const DWORD requested = std::min<DWORD>(available, static_cast<DWORD>(buffer.size()));
            if (!ReadFile(read_pipe, buffer.data(), requested, &read, nullptr) || read == 0) break;
            append_output(buffer.data(), read);
            available -= read;
        }
        if (WaitForSingleObject(process.hProcess, 10) == WAIT_OBJECT_0) break;
        if (GetTickCount64() - started_at >= timeout_ms) {
            TerminateProcess(process.hProcess, ERROR_TIMEOUT);
            WaitForSingleObject(process.hProcess, 5'000);
            timed_out = true;
            break;
        }
    }
    DWORD read = 0;
    while (ReadFile(read_pipe, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr) && read > 0) {
        append_output(buffer.data(), read);
    }
    if (output_truncated) result.output.append(kTruncatedMarker);
    if (timed_out) result.exit_code = ERROR_TIMEOUT;
    else GetExitCodeProcess(process.hProcess, &result.exit_code);
    CloseHandle(read_pipe);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return result;
}

std::optional<fs::path> FindSystemExecutable(std::wstring_view file_name) {
    if (file_name.empty() || file_name.find(L'\\') != std::wstring_view::npos ||
        file_name.find(L'/') != std::wstring_view::npos) return std::nullopt;
    std::vector<wchar_t> buffer(32'768);
    const UINT length = GetSystemDirectoryW(buffer.data(), static_cast<UINT>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return std::nullopt;
    const fs::path candidate = fs::path(std::wstring(buffer.data(), length)) / std::wstring(file_name);
    const DWORD attributes = GetFileAttributesW(candidate.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) return std::nullopt;
    return candidate;
}

int RelaunchElevated(const std::wstring& parameters) {
    SHELLEXECUTEINFOW execute{};
    execute.cbSize = sizeof(execute);
    execute.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
    execute.lpVerb = L"runas";
    const auto executable = ExecutablePath().wstring();
    execute.lpFile = executable.c_str();
    execute.lpParameters = parameters.c_str();
    execute.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&execute)) {
        const DWORD execute_error = GetLastError();
        return static_cast<int>(execute_error);
    }
    if (execute.hProcess == nullptr) return static_cast<int>(ERROR_INVALID_HANDLE);
    const DWORD wait_result = WaitForSingleObject(execute.hProcess, INFINITE);
    if (wait_result != WAIT_OBJECT_0) {
        const DWORD wait_error = wait_result == WAIT_FAILED ? GetLastError() : ERROR_GEN_FAILURE;
        CloseHandle(execute.hProcess);
        return static_cast<int>(wait_error);
    }
    DWORD code = ERROR_GEN_FAILURE;
    if (!GetExitCodeProcess(execute.hProcess, &code)) code = GetLastError();
    CloseHandle(execute.hProcess);
    return static_cast<int>(code);
}

std::string FileVersion(const fs::path& path) {
    DWORD ignored = 0;
    const DWORD size = GetFileVersionInfoSizeW(path.c_str(), &ignored);
    if (size == 0) return {};
    std::vector<std::uint8_t> buffer(size);
    if (!GetFileVersionInfoW(path.c_str(), 0, size, buffer.data())) return {};
    VS_FIXEDFILEINFO* info = nullptr;
    UINT length = 0;
    if (!VerQueryValueW(buffer.data(), L"\\", reinterpret_cast<void**>(&info), &length) || info == nullptr) return {};
    std::ostringstream output;
    output << HIWORD(info->dwFileVersionMS) << '.' << LOWORD(info->dwFileVersionMS) << '.'
           << HIWORD(info->dwFileVersionLS) << '.' << LOWORD(info->dwFileVersionLS);
    return output.str();
}

std::vector<std::string> AuditImportedFunctions(const fs::path& image_path, std::string* error) {
    std::ifstream file(image_path, std::ios::binary);
    if (!file) { if (error) *error = "image cannot be opened"; return {}; }
    std::vector<std::uint8_t> bytes((std::istreambuf_iterator<char>(file)), {});
    if (bytes.size() < 0x100 || ReadU16(bytes, 0) != 0x5a4d) {
        if (error) *error = "invalid DOS header";
        return {};
    }
    const std::size_t pe = ReadU32(bytes, 0x3c);
    if (pe + 0x108 > bytes.size() || ReadU32(bytes, pe) != 0x00004550) {
        if (error) *error = "invalid PE header";
        return {};
    }
    const std::uint16_t sections = ReadU16(bytes, pe + 6);
    const std::uint16_t optional_size = ReadU16(bytes, pe + 20);
    const std::size_t optional = pe + 24;
    const bool pe64 = ReadU16(bytes, optional) == 0x20b;
    const std::size_t data_directory = optional + (pe64 ? 112 : 96);
    const std::uint32_t import_rva = ReadU32(bytes, data_directory + 8);
    const std::size_t section_table = optional + optional_size;
    const auto import_offset = RvaToOffset(bytes, import_rva, section_table, sections);
    if (!import_offset) { if (error) *error = "import directory missing"; return {}; }

    std::vector<std::string> imports;
    for (std::size_t descriptor = *import_offset; descriptor + 20 <= bytes.size(); descriptor += 20) {
        const auto original_first_thunk = ReadU32(bytes, descriptor);
        const auto name_rva = ReadU32(bytes, descriptor + 12);
        const auto first_thunk = ReadU32(bytes, descriptor + 16);
        if (name_rva == 0 && first_thunk == 0) break;
        const auto name_offset = RvaToOffset(bytes, name_rva, section_table, sections);
        std::string module;
        if (name_offset) {
            for (std::size_t p = *name_offset; p < bytes.size() && bytes[p] != 0; ++p) module.push_back(static_cast<char>(bytes[p]));
        }
        const auto thunk_offset = RvaToOffset(bytes, original_first_thunk != 0 ? original_first_thunk : first_thunk,
                                              section_table, sections);
        if (!thunk_offset) continue;
        const std::size_t stride = pe64 ? 8 : 4;
        for (std::size_t p = *thunk_offset; p + stride <= bytes.size(); p += stride) {
            std::uint64_t thunk = ReadU32(bytes, p);
            if (pe64) thunk |= static_cast<std::uint64_t>(ReadU32(bytes, p + 4)) << 32;
            if (thunk == 0) break;
            const std::uint64_t ordinal_mask = pe64 ? 0x8000000000000000ull : 0x80000000ull;
            if ((thunk & ordinal_mask) != 0) continue;
            const auto function_offset = RvaToOffset(bytes, static_cast<std::uint32_t>(thunk), section_table, sections);
            if (!function_offset || *function_offset + 2 >= bytes.size()) continue;
            std::string name;
            for (std::size_t q = *function_offset + 2; q < bytes.size() && bytes[q] != 0; ++q) name.push_back(static_cast<char>(bytes[q]));
            imports.push_back(module + "!" + name);
        }
    }
    std::sort(imports.begin(), imports.end());
    return imports;
}

std::string CapabilitiesJson(const BackendCapabilities& c) {
    std::ostringstream blocked;
    blocked << '[';
    for (std::size_t i = 0; i < c.blocked_capabilities.size(); ++i) {
        if (i != 0) blocked << ',';
        blocked << '"' << JsonEscape(c.blocked_capabilities[i]) << '"';
    }
    blocked << ']';
    return MakeJsonObject({
        {"CaptureBackendName", c.capture_backend_name},
        {"CaptureBackendVersion", c.capture_backend_version},
        {"Available", c.available ? "true" : "false"},
        {"SupportsRealtimeEvents", c.supports_realtime_events ? "true" : "false"},
        {"SupportsPacketPayload", c.supports_packet_payload ? "true" : "false"},
        {"SupportsDirection", c.supports_direction ? "true" : "false"},
        {"SupportsEndpointFilter", c.supports_endpoint_filter ? "true" : "false"},
        {"SupportsProcessFilter", c.supports_process_filter ? "true" : "false"},
        {"SupportsDropStatistics", c.supports_drop_statistics ? "true" : "false"},
        {"SupportsPcapngConversion", c.supports_pcapng_conversion ? "true" : "false"},
        {"SupportsIpv4", c.supports_ipv4 ? "true" : "false"},
        {"SupportsIpv6", c.supports_ipv6 ? "true" : "false"},
        {"AnalysisMode", ToString(c.analysis_mode)},
        {"BackendCapabilityBlocked", blocked.str()},
        {"ProbeEvidence", c.probe_evidence}
    }, {"Available", "SupportsRealtimeEvents", "SupportsPacketPayload", "SupportsDirection",
        "SupportsEndpointFilter", "SupportsProcessFilter", "SupportsDropStatistics",
        "SupportsPcapngConversion", "SupportsIpv4", "SupportsIpv6", "BackendCapabilityBlocked"});
}

bool ValidatePcapng(const fs::path& path, std::string* reason) {
    std::ifstream input(path, std::ios::binary);
    std::array<std::uint8_t, 12> header{};
    input.read(reinterpret_cast<char*>(header.data()), static_cast<std::streamsize>(header.size()));
    if (input.gcount() != static_cast<std::streamsize>(header.size())) {
        if (reason) *reason = "file is shorter than a section header";
        return false;
    }
    const std::uint32_t block = static_cast<std::uint32_t>(header[0]) |
        (static_cast<std::uint32_t>(header[1]) << 8) | (static_cast<std::uint32_t>(header[2]) << 16) |
        (static_cast<std::uint32_t>(header[3]) << 24);
    const std::uint32_t magic = static_cast<std::uint32_t>(header[8]) |
        (static_cast<std::uint32_t>(header[9]) << 8) | (static_cast<std::uint32_t>(header[10]) << 16) |
        (static_cast<std::uint32_t>(header[11]) << 24);
    if (block != 0x0a0d0d0a || (magic != 0x1a2b3c4d && magic != 0x4d3c2b1a)) {
        if (reason) *reason = "invalid pcapng section header or byte-order magic";
        return false;
    }
    if (reason) *reason = "valid section header";
    return true;
}

} // namespace god2
