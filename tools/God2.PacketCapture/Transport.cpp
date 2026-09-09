#include "Transport.h"

#include <array>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <map>
#include <sstream>
#include <unordered_map>

namespace god2 {
namespace {

constexpr std::size_t kMaximumInjectedPayloadBytes = 4096;

std::uint16_t U16(const std::uint8_t* value, bool little) {
    return little ? static_cast<std::uint16_t>(value[0] | (value[1] << 8)) :
                    static_cast<std::uint16_t>((value[0] << 8) | value[1]);
}

std::uint32_t U32(const std::uint8_t* value, bool little) {
    if (little) {
        return static_cast<std::uint32_t>(value[0]) | (static_cast<std::uint32_t>(value[1]) << 8) |
               (static_cast<std::uint32_t>(value[2]) << 16) | (static_cast<std::uint32_t>(value[3]) << 24);
    }
    return (static_cast<std::uint32_t>(value[0]) << 24) | (static_cast<std::uint32_t>(value[1]) << 16) |
           (static_cast<std::uint32_t>(value[2]) << 8) | static_cast<std::uint32_t>(value[3]);
}

std::string Hex(const std::uint8_t* data, std::size_t size) {
    static const char alphabet[] = "0123456789ABCDEF";
    std::string result(size * 2, '0');
    for (std::size_t i = 0; i < size; ++i) {
        result[i * 2] = alphabet[data[i] >> 4];
        result[i * 2 + 1] = alphabet[data[i] & 0x0f];
    }
    return result;
}

int HexDigit(char value) {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
}

std::optional<std::vector<std::uint8_t>> ParseHexBytes(std::string_view hex) {
    if ((hex.size() % 2) != 0 || hex.size() / 2 > kMaximumInjectedPayloadBytes)
        return std::nullopt;
    std::vector<std::uint8_t> bytes;
    bytes.reserve(hex.size() / 2);
    for (std::size_t i = 0; i < hex.size(); i += 2) {
        const int high = HexDigit(hex[i]);
        const int low = HexDigit(hex[i + 1]);
        if (high < 0 || low < 0) return std::nullopt;
        bytes.push_back(static_cast<std::uint8_t>((high << 4) | low));
    }
    return bytes;
}

std::string Ipv4(const std::uint8_t* value) {
    return std::to_string(value[0]) + "." + std::to_string(value[1]) + "." +
           std::to_string(value[2]) + "." + std::to_string(value[3]);
}

std::string Ipv6(const std::uint8_t* value) {
    std::ostringstream output;
    output << std::hex;
    for (int i = 0; i < 8; ++i) {
        if (i != 0) output << ':';
        output << static_cast<unsigned>(value[i * 2] << 8 | value[i * 2 + 1]);
    }
    return output.str();
}

struct ParsedPacket {
    std::string source_address;
    std::string destination_address;
    std::uint16_t source_port = 0;
    std::uint16_t destination_port = 0;
    std::string transport;
    std::uint32_t tcp_sequence = 0;
    std::uint32_t tcp_acknowledgment = 0;
    std::uint8_t tcp_flags = 0;
    const std::uint8_t* payload = nullptr;
    std::size_t payload_size = 0;
};

struct SourceSpan {
    std::size_t size = 0;
    std::string source_frame_id;
    bool classification_counted = false;
};

struct PendingTcpSegment {
    std::vector<std::uint8_t> bytes;
    std::string source_frame_id;
};

struct TcpFlowState {
    bool initialized = false;
    std::uint32_t next_sequence = 0;
    std::map<std::uint32_t, PendingTcpSegment> pending;
    std::size_t pending_bytes = 0;
    std::vector<std::uint8_t> application_bytes;
    std::deque<SourceSpan> application_sources;
};

struct DecodedStreamMovement {
    VerifiedWorldMovementFrame movement;
    std::string wire_hex;
    std::vector<std::string> source_frame_ids;
    std::size_t newly_classified_packet_count = 0;
};

struct God2CandidateCluster {
    std::string direction;
    std::uint16_t length = 0;
    std::string opcode_u16_le;
    std::string opcode_u16_be;
    std::string opcode_byte;
    std::string prefix_hex;
    std::uint64_t observed_count = 0;
    std::string sample_source_frame_ids;
};

struct God2CandidateStream {
    std::vector<std::uint8_t> bytes;
    std::deque<SourceSpan> sources;
    std::uint64_t desync_since_frame = 0;
};

constexpr std::size_t kMaximumTcpFlows = 1024;
constexpr std::size_t kMaximumPendingBytesPerFlow = 256 * 1024;
constexpr std::size_t kMaximumCandidateFrameLength = 8192;
constexpr std::size_t kMaximumCandidateStreamBytes = 256 * 1024;
constexpr std::size_t kMaximumCandidateStreams = 2048;
constexpr std::size_t kMaximumCandidateClusters = 8192;

void ClearTcpFlow(TcpFlowState& flow) {
    flow = {};
}

void AppendApplicationBytes(TcpFlowState& flow,
                            const std::uint8_t* data,
                            std::size_t size,
                            const std::string& source_frame_id) {
    if (size == 0) return;
    flow.application_bytes.insert(flow.application_bytes.end(), data, data + size);
    if (!flow.application_sources.empty() &&
        flow.application_sources.back().source_frame_id == source_frame_id) {
        flow.application_sources.back().size += size;
    } else {
        flow.application_sources.push_back({size, source_frame_id, false});
    }
}

std::vector<std::string> SourcesForPrefix(TcpFlowState& flow,
                                          std::size_t size,
                                          std::size_t* newly_classified_packet_count) {
    std::vector<std::string> result;
    std::size_t remaining = size;
    std::string previous_source;
    for (auto& span : flow.application_sources) {
        if (remaining == 0) break;
        if (span.source_frame_id != previous_source) result.push_back(span.source_frame_id);
        previous_source = span.source_frame_id;
        if (!span.classification_counted) {
            span.classification_counted = true;
            if (newly_classified_packet_count != nullptr) ++*newly_classified_packet_count;
        }
        remaining -= std::min(remaining, span.size);
    }
    return result;
}

void DiscardApplicationPrefix(TcpFlowState& flow, std::size_t size) {
    size = std::min(size, flow.application_bytes.size());
    flow.application_bytes.erase(flow.application_bytes.begin(),
                                 flow.application_bytes.begin() + static_cast<std::ptrdiff_t>(size));
    std::size_t remaining = size;
    while (remaining != 0 && !flow.application_sources.empty()) {
        auto& span = flow.application_sources.front();
        if (remaining < span.size) {
            span.size -= remaining;
            remaining = 0;
        } else {
            remaining -= span.size;
            flow.application_sources.pop_front();
        }
    }
}

void AppendCandidateBytes(God2CandidateStream& stream,
                          const std::vector<std::uint8_t>& bytes,
                          const std::string& source_frame_id) {
    if (bytes.empty()) return;
    stream.bytes.insert(stream.bytes.end(), bytes.begin(), bytes.end());
    if (!stream.sources.empty() && stream.sources.back().source_frame_id == source_frame_id) {
        stream.sources.back().size += bytes.size();
    } else {
        stream.sources.push_back({bytes.size(), source_frame_id, false});
    }
    if (stream.bytes.size() > kMaximumCandidateStreamBytes) {
        const std::size_t drop = stream.bytes.size() - kMaximumCandidateStreamBytes;
        stream.bytes.erase(stream.bytes.begin(), stream.bytes.begin() + static_cast<std::ptrdiff_t>(drop));
        std::size_t remaining = drop;
        while (remaining != 0 && !stream.sources.empty()) {
            auto& span = stream.sources.front();
            if (remaining < span.size) {
                span.size -= remaining;
                remaining = 0;
            } else {
                remaining -= span.size;
                stream.sources.pop_front();
            }
        }
    }
}

std::vector<std::string> CandidateSourcesForPrefix(God2CandidateStream& stream, std::size_t size) {
    std::vector<std::string> result;
    std::size_t remaining = size;
    std::string previous_source;
    for (const auto& span : stream.sources) {
        if (remaining == 0) break;
        if (span.source_frame_id != previous_source) result.push_back(span.source_frame_id);
        previous_source = span.source_frame_id;
        remaining -= std::min(remaining, span.size);
    }
    return result;
}

void DiscardCandidatePrefix(God2CandidateStream& stream, std::size_t size) {
    size = std::min(size, stream.bytes.size());
    stream.bytes.erase(stream.bytes.begin(), stream.bytes.begin() + static_cast<std::ptrdiff_t>(size));
    std::size_t remaining = size;
    while (remaining != 0 && !stream.sources.empty()) {
        auto& span = stream.sources.front();
        if (remaining < span.size) {
            span.size -= remaining;
            remaining = 0;
        } else {
            remaining -= span.size;
            stream.sources.pop_front();
        }
    }
}

std::string JsonObjectFromCounts(const std::map<std::string, std::uint64_t>& counts) {
    std::string json = "{";
    bool first = true;
    for (const auto& [key, count] : counts) {
        if (!first) json += ",";
        first = false;
        json += "\"" + JsonEscape(key) + "\":" + std::to_string(count);
    }
    json += "}";
    return json;
}

void ExtractVerifiedMovements(TcpFlowState& flow, std::vector<DecodedStreamMovement>& decoded) {
    std::size_t scan = 0;
    while (scan + 2 <= flow.application_bytes.size()) {
        if (flow.application_bytes[scan] != 0x0A || flow.application_bytes[scan + 1] != 0x00) {
            ++scan;
            continue;
        }
        if (flow.application_bytes.size() - scan < 10) {
            DiscardApplicationPrefix(flow, scan);
            return;
        }
        VerifiedWorldMovementFrame movement;
        if (!TryDecodeVerifiedWorldMovement(flow.application_bytes.data() + scan, 10, movement)) {
            ++scan;
            continue;
        }
        DiscardApplicationPrefix(flow, scan);
        DecodedStreamMovement item;
        item.movement = std::move(movement);
        item.wire_hex = Hex(flow.application_bytes.data(), 10);
        item.source_frame_ids = SourcesForPrefix(flow, 10, &item.newly_classified_packet_count);
        decoded.push_back(std::move(item));
        DiscardApplicationPrefix(flow, 10);
        scan = 0;
    }
    const std::size_t keep = !flow.application_bytes.empty() && flow.application_bytes.back() == 0x0A ? 1 : 0;
    if (flow.application_bytes.size() > keep) {
        DiscardApplicationPrefix(flow, flow.application_bytes.size() - keep);
    }
}

void AppendContiguousSegment(TcpFlowState& flow,
                             const std::uint8_t* data,
                             std::size_t size,
                             const std::string& source_frame_id) {
    AppendApplicationBytes(flow, data, size, source_frame_id);
    flow.next_sequence += static_cast<std::uint32_t>(size);
}

void FlushPendingSegments(TcpFlowState& flow) {
    for (;;) {
        auto selected = flow.pending.end();
        for (auto it = flow.pending.begin(); it != flow.pending.end(); ++it) {
            const auto difference = static_cast<std::int32_t>(it->first - flow.next_sequence);
            if (difference <= 0) {
                const auto overlap = static_cast<std::uint32_t>(flow.next_sequence - it->first);
                if (overlap < it->second.bytes.size()) {
                    selected = it;
                    break;
                }
                flow.pending_bytes -= it->second.bytes.size();
                flow.pending.erase(it);
                selected = flow.pending.end();
                break;
            }
        }
        if (selected == flow.pending.end()) {
            if (!flow.pending.empty()) {
                const auto first = flow.pending.begin();
                if (static_cast<std::int32_t>(first->first - flow.next_sequence) != 0) return;
                selected = first;
            } else return;
        }
        const auto overlap = static_cast<std::size_t>(flow.next_sequence - selected->first);
        const auto size = selected->second.bytes.size();
        if (overlap < size) {
            AppendContiguousSegment(flow, selected->second.bytes.data() + overlap, size - overlap,
                                    selected->second.source_frame_id);
        }
        flow.pending_bytes -= size;
        flow.pending.erase(selected);
    }
}

void FeedTcpPayload(TcpFlowState& flow,
                    const ParsedPacket& packet,
                    const std::string& source_frame_id,
                    std::vector<DecodedStreamMovement>& decoded) {
    constexpr std::uint8_t kFin = 0x01;
    constexpr std::uint8_t kSyn = 0x02;
    constexpr std::uint8_t kRst = 0x04;
    if ((packet.tcp_flags & (kSyn | kRst)) != 0) ClearTcpFlow(flow);
    std::uint32_t sequence = packet.tcp_sequence + ((packet.tcp_flags & kSyn) != 0 ? 1u : 0u);
    if (!flow.initialized) {
        flow.initialized = true;
        flow.next_sequence = sequence;
    }
    const std::uint8_t* data = packet.payload;
    std::size_t size = packet.payload_size;
    const auto difference = static_cast<std::int32_t>(sequence - flow.next_sequence);
    if (difference < 0) {
        const auto overlap = static_cast<std::size_t>(flow.next_sequence - sequence);
        if (overlap >= size) return;
        data += overlap;
        size -= overlap;
        sequence = flow.next_sequence;
    }
    if (sequence == flow.next_sequence) {
        AppendContiguousSegment(flow, data, size, source_frame_id);
        FlushPendingSegments(flow);
        ExtractVerifiedMovements(flow, decoded);
    } else if (size != 0 && flow.pending_bytes + size <= kMaximumPendingBytesPerFlow) {
        const auto [it, inserted] = flow.pending.emplace(sequence, PendingTcpSegment{{data, data + size}, source_frame_id});
        if (inserted) flow.pending_bytes += it->second.bytes.size();
    } else if (flow.pending_bytes + size > kMaximumPendingBytesPerFlow) {
        ClearTcpFlow(flow);
    }
    if ((packet.tcp_flags & (kFin | kRst)) != 0) {
        ExtractVerifiedMovements(flow, decoded);
        ClearTcpFlow(flow);
    }
}

struct InterfaceDescription {
    std::uint16_t link_type = 0;
    long double seconds_per_tick = 0.000001L;
};

std::string PcapTimestamp(std::uint64_t ticks, long double seconds_per_tick) {
    const long double seconds = static_cast<long double>(ticks) * seconds_per_tick;
    if (seconds < 0 || seconds > 32'503'680'000.0L) return UtcNow();
    constexpr long double windows_epoch_100ns = 116'444'736'000'000'000.0L;
    const auto file_time_value = static_cast<std::uint64_t>(windows_epoch_100ns + seconds * 10'000'000.0L);
    FILETIME file_time{static_cast<DWORD>(file_time_value), static_cast<DWORD>(file_time_value >> 32)};
    SYSTEMTIME system_time{};
    if (!FileTimeToSystemTime(&file_time, &system_time)) return UtcNow();
    std::ostringstream output;
    output << std::setfill('0') << std::setw(4) << system_time.wYear << '-' << std::setw(2) << system_time.wMonth
           << '-' << std::setw(2) << system_time.wDay << 'T' << std::setw(2) << system_time.wHour << ':'
           << std::setw(2) << system_time.wMinute << ':' << std::setw(2) << system_time.wSecond << '.'
           << std::setw(3) << system_time.wMilliseconds << 'Z';
    return output.str();
}

bool ParseIp(const std::uint8_t* data, std::size_t size, ParsedPacket& packet) {
    if (size < 1) return false;
    const std::uint8_t version = data[0] >> 4;
    std::size_t offset = 0;
    std::uint8_t protocol = 0;
    std::size_t available = 0;
    if (version == 4) {
        if (size < 20) return false;
        const std::size_t header = static_cast<std::size_t>(data[0] & 0x0f) * 4;
        const std::size_t total = static_cast<std::size_t>(data[2] << 8 | data[3]);
        if (header < 20 || header > size) return false;
        offset = header;
        available = std::min(size, total) - std::min(header, std::min(size, total));
        protocol = data[9];
        packet.source_address = Ipv4(data + 12);
        packet.destination_address = Ipv4(data + 16);
    } else if (version == 6) {
        if (size < 40) return false;
        offset = 40;
        available = std::min<std::size_t>(size - 40, static_cast<std::size_t>(data[4] << 8 | data[5]));
        protocol = data[6];
        packet.source_address = Ipv6(data + 8);
        packet.destination_address = Ipv6(data + 24);
    } else return false;

    if (protocol == 6) {
        if (available < 20 || offset + 20 > size) return false;
        packet.source_port = static_cast<std::uint16_t>(data[offset] << 8 | data[offset + 1]);
        packet.destination_port = static_cast<std::uint16_t>(data[offset + 2] << 8 | data[offset + 3]);
        packet.tcp_sequence = U32(data + offset + 4, false);
        packet.tcp_acknowledgment = U32(data + offset + 8, false);
        packet.tcp_flags = data[offset + 13];
        const std::size_t header = static_cast<std::size_t>(data[offset + 12] >> 4) * 4;
        if (header < 20 || header > available) return false;
        packet.transport = "TCP";
        packet.payload = data + offset + header;
        packet.payload_size = available - header;
        return true;
    }
    if (protocol == 17) {
        if (available < 8 || offset + 8 > size) return false;
        packet.source_port = static_cast<std::uint16_t>(data[offset] << 8 | data[offset + 1]);
        packet.destination_port = static_cast<std::uint16_t>(data[offset + 2] << 8 | data[offset + 3]);
        const std::size_t udp_length = static_cast<std::size_t>(data[offset + 4] << 8 | data[offset + 5]);
        packet.transport = "UDP";
        packet.payload = data + offset + 8;
        packet.payload_size = std::min<std::size_t>(available - 8, udp_length >= 8 ? udp_length - 8 : 0);
        return true;
    }
    return false;
}

bool ParseLink(std::uint16_t link_type, const std::uint8_t* data, std::size_t size, ParsedPacket& packet) {
    if (link_type == 1) {
        if (size < 14) return false;
        std::size_t offset = 14;
        std::uint16_t ether_type = static_cast<std::uint16_t>(data[12] << 8 | data[13]);
        if ((ether_type == 0x8100 || ether_type == 0x88a8) && size >= 18) {
            ether_type = static_cast<std::uint16_t>(data[16] << 8 | data[17]);
            offset = 18;
        }
        if (ether_type != 0x0800 && ether_type != 0x86dd) return false;
        return ParseIp(data + offset, size - offset, packet);
    }
    if (link_type == 101 || link_type == 228 || link_type == 229) return ParseIp(data, size, packet);
    return false;
}

} // namespace

bool TryDecodeVerifiedWorldMovement(const std::uint8_t* data,
                                    std::size_t size,
                                    VerifiedWorldMovementFrame& movement) {
    movement = {};
    if (data == nullptr || size != 10 || data[0] != 10 || data[1] != 0) return false;
    // Build-locked transform from OfficialClientWorldProtocolFrames evidence
    // (EvidenceId World-ba1f987593024d32be06b5485b16c9ab-server-to-client).
    constexpr std::array<std::uint8_t, 8> key = {0xFD,0x9F,0xCA,0xC8,0x42,0xA8,0x69,0xFE};
    std::array<std::uint8_t, 10> decoded{};
    std::copy_n(data, 10, decoded.begin());
    int previous_plain = 0xB0;
    for (std::size_t i = 2; i < decoded.size(); ++i) {
        const int encrypted = (static_cast<int>(decoded[i]) + 3 - previous_plain) & 0xff;
        const int plain = key[i - 2] ^ encrypted;
        decoded[i] = static_cast<std::uint8_t>(plain);
        previous_plain = plain;
    }
    if (decoded[2] != 0x2E || decoded[8] != 0xFF) return false;
    int checksum = 0;
    for (std::size_t i = 0; i + 1 < decoded.size(); ++i) checksum = (checksum + decoded[i] + 0x3C) & 0xff;
    if (decoded.back() != static_cast<std::uint8_t>(checksum)) return false;
    const auto x = static_cast<std::uint16_t>(decoded[3] | decoded[4] << 8);
    const auto y = static_cast<std::uint16_t>(decoded[5] | decoded[6] << 8);
    if (x > 0x7fff || y > 0x7fff) return false;
    movement.x = x;
    movement.y = y;
    movement.sequence = decoded[7];
    movement.decoded_hex = Hex(decoded.data(), decoded.size());
    return true;
}

PcapngExtractionResult ExtractPcapngPayloads(const fs::path& pcapng,
                                             GameplayAnalysisEngine& engine,
                                             std::string* error,
                                             bool persist_raw) {
    PcapngExtractionResult result;
    std::ifstream input(pcapng, std::ios::binary);
    if (!input) {
        result.message = "cannot open PCAPNG";
        if (error) *error = result.message;
        return result;
    }
    bool little = true;
    std::vector<InterfaceDescription> interfaces;
    std::unordered_map<std::string, std::pair<std::uint16_t, std::uint16_t>> last_movement_positions;
    std::unordered_map<std::string, TcpFlowState> tcp_flows;
    std::uint64_t frame_index = 0;
    std::uint64_t decoded_index = 0;
    while (input) {
        std::array<std::uint8_t, 8> header{};
        input.read(reinterpret_cast<char*>(header.data()), 8);
        if (input.gcount() == 0) break;
        if (input.gcount() != 8) { result.message = "truncated block header"; break; }
        const std::uint32_t raw_type_le = U32(header.data(), true);
        if (raw_type_le == 0x0a0d0d0a) {
            std::array<std::uint8_t, 4> magic{};
            input.read(reinterpret_cast<char*>(magic.data()), 4);
            if (input.gcount() != 4) { result.message = "truncated section header"; break; }
            const auto magic_le = U32(magic.data(), true);
            if (magic_le == 0x1a2b3c4d) little = true;
            else if (magic_le == 0x4d3c2b1a) little = false;
            else { result.message = "invalid section byte-order magic"; break; }
            const std::uint32_t length = U32(header.data() + 4, little);
            if (length < 16 || length > 64 * 1024 * 1024) { result.message = "invalid section length"; break; }
            input.seekg(static_cast<std::streamoff>(length - 12), std::ios::cur);
            interfaces.clear();
            continue;
        }
        const std::uint32_t type = U32(header.data(), little);
        const std::uint32_t length = U32(header.data() + 4, little);
        if (length < 12 || length > 64 * 1024 * 1024) { result.message = "invalid block length"; break; }
        std::vector<std::uint8_t> body(length - 8);
        input.read(reinterpret_cast<char*>(body.data()), static_cast<std::streamsize>(body.size()));
        if (input.gcount() != static_cast<std::streamsize>(body.size())) { result.message = "truncated block"; break; }
        if (U32(body.data() + body.size() - 4, little) != length) { result.message = "block length trailer mismatch"; break; }

        if (type == 1 && body.size() >= 12) {
            InterfaceDescription interface;
            interface.link_type = U16(body.data(), little);
            std::size_t idb_option = 8;
            while (idb_option + 4 <= body.size() - 4) {
                const auto option_code = U16(body.data() + idb_option, little);
                const auto option_length = U16(body.data() + idb_option + 2, little);
                idb_option += 4;
                if (option_code == 0) break;
                if (idb_option + option_length > body.size() - 4) break;
                if (option_code == 9 && option_length >= 1) {
                    const auto resolution = body[idb_option];
                    interface.seconds_per_tick = (resolution & 0x80) != 0 ?
                        std::pow(2.0L, -static_cast<int>(resolution & 0x7f)) :
                        std::pow(10.0L, -static_cast<int>(resolution));
                }
                idb_option += (static_cast<std::size_t>(option_length) + 3) & ~std::size_t(3);
            }
            interfaces.push_back(interface);
            continue;
        }
        if (type != 6 || body.size() < 24) continue;
        const std::uint32_t interface_id = U32(body.data(), little);
        const std::uint32_t captured_length = U32(body.data() + 12, little);
        if (interface_id >= interfaces.size() || 20ull + captured_length + 4ull > body.size()) continue;
        std::string packet_direction = "Unknown";
        std::size_t option_offset = 20 + ((static_cast<std::size_t>(captured_length) + 3) & ~std::size_t(3));
        while (option_offset + 4 <= body.size() - 4) {
            const auto option_code = U16(body.data() + option_offset, little);
            const auto option_length = U16(body.data() + option_offset + 2, little);
            option_offset += 4;
            if (option_code == 0) break;
            if (option_offset + option_length > body.size() - 4) break;
            if (option_code == 2 && option_length >= 4) {
                const auto flags = U32(body.data() + option_offset, little);
                if ((flags & 0x3) == 1) packet_direction = "Inbound";
                else if ((flags & 0x3) == 2) packet_direction = "Outbound";
            }
            option_offset += (static_cast<std::size_t>(option_length) + 3) & ~std::size_t(3);
        }
        ParsedPacket packet;
        if (!ParseLink(interfaces[interface_id].link_type, body.data() + 20, captured_length, packet)) continue;
        ++result.packet_count;
        if (packet.payload_size == 0) continue;
        ++frame_index;
        ++result.unknown_protocol_packets;
        result.payload_bytes += packet.payload_size;
        Frame frame;
        frame.frame_id = "pcapng-" + WideToUtf8(pcapng.stem().wstring()) + "-" + std::to_string(frame_index);
        frame.source_frame_ids = frame.frame_id;
        const std::uint64_t timestamp = (static_cast<std::uint64_t>(U32(body.data() + 4, little)) << 32) |
                                        U32(body.data() + 8, little);
        frame.timestamp_utc = PcapTimestamp(timestamp, interfaces[interface_id].seconds_per_tick);
        frame.direction = packet_direction;
        frame.payload_hex = Hex(packet.payload, packet.payload_size);
        const std::string flow_key = packet.source_address + ":" + std::to_string(packet.source_port) + ">" +
                                     packet.destination_address + ":" + std::to_string(packet.destination_port);
        frame.fields = {
            {"SourceFrameId", frame.frame_id}, {"ObservedAtUtc", frame.timestamp_utc},
            {"PacketDirection", frame.direction}, {"MessageType", ""}, {"Opcode", ""},
            {"PayloadHex", frame.payload_hex}, {"Transport", packet.transport},
            {"SourceAddress", packet.source_address}, {"DestinationAddress", packet.destination_address},
            {"SourcePort", std::to_string(packet.source_port)}, {"DestinationPort", std::to_string(packet.destination_port)},
            {"EvidenceLevel", "Transmitted"}, {"CaptureTimestampRaw", std::to_string(timestamp)}
        };
        if (packet.transport == "TCP") {
            frame.fields["TcpSequence"] = std::to_string(packet.tcp_sequence);
            frame.fields["TcpAcknowledgment"] = std::to_string(packet.tcp_acknowledgment);
            frame.fields["TcpFlags"] = std::to_string(packet.tcp_flags);
        }
        std::vector<std::pair<std::string, std::string>> original_fields(frame.fields.begin(), frame.fields.end());
        std::sort(original_fields.begin(), original_fields.end());
        frame.original_json = MakeJsonObject(original_fields);
        frame.source_json = frame.original_json;
        std::string process_error;
        if (persist_raw && !engine.PersistRawEvidence(frame, &process_error)) {
            result.message = process_error;
            if (error) *error = process_error;
            return result;
        }

        std::vector<DecodedStreamMovement> movements;
        if (packet.transport == "TCP") {
            if (!tcp_flows.contains(flow_key) && tcp_flows.size() >= kMaximumTcpFlows) {
                tcp_flows.erase(tcp_flows.begin());
            }
            FeedTcpPayload(tcp_flows[flow_key], packet, frame.frame_id, movements);
        } else {
            VerifiedWorldMovementFrame movement;
            if (TryDecodeVerifiedWorldMovement(packet.payload, packet.payload_size, movement)) {
                movements.push_back({std::move(movement), frame.payload_hex, {frame.frame_id}, 1});
            }
        }

        for (const auto& decoded : movements) {
            Frame movement_frame;
            movement_frame.frame_id = "pcapng-" + WideToUtf8(pcapng.stem().wstring()) +
                                      "-movement-" + std::to_string(++decoded_index);
            movement_frame.source_frame_ids = JoinList(decoded.source_frame_ids);
            movement_frame.timestamp_utc = frame.timestamp_utc;
            movement_frame.direction = frame.direction;
            movement_frame.message_type = "MountedMovementUpdate";
            movement_frame.opcode = "0x2E";
            movement_frame.payload_hex = decoded.wire_hex;
            movement_frame.fields = frame.fields;
            movement_frame.fields["SourceFrameId"] = movement_frame.frame_id;
            movement_frame.fields["SourceFrameIds"] = movement_frame.source_frame_ids;
            movement_frame.fields["MessageType"] = movement_frame.message_type;
            movement_frame.fields["Opcode"] = movement_frame.opcode;
            movement_frame.fields["PayloadHex"] = movement_frame.payload_hex;
            movement_frame.fields["EndX"] = std::to_string(decoded.movement.x);
            movement_frame.fields["EndY"] = std::to_string(decoded.movement.y);
            movement_frame.fields["MovementSequence"] = std::to_string(decoded.movement.sequence);
            movement_frame.fields["MovementMode"] = "Mounted";
            movement_frame.fields["IsMounted"] = "true";
            movement_frame.fields["MountStateRaw"] = "0xFF";
            movement_frame.fields["DecodedFrameHex"] = decoded.movement.decoded_hex;
            movement_frame.fields["ProtocolEvidenceId"] = "World-ba1f987593024d32be06b5485b16c9ab-server-to-client";
            movement_frame.fields["EvidenceLevel"] = "Candidate";
            const auto previous = last_movement_positions.find(flow_key);
            if (previous != last_movement_positions.end()) {
                movement_frame.fields["StartX"] = std::to_string(previous->second.first);
                movement_frame.fields["StartY"] = std::to_string(previous->second.second);
            }
            if (previous == last_movement_positions.end() &&
                last_movement_positions.size() >= kMaximumTcpFlows) last_movement_positions.erase(last_movement_positions.begin());
            last_movement_positions[flow_key] = {decoded.movement.x, decoded.movement.y};
            std::vector<std::pair<std::string, std::string>> movement_fields(movement_frame.fields.begin(),
                                                                            movement_frame.fields.end());
            std::sort(movement_fields.begin(), movement_fields.end());
            movement_frame.original_json = MakeJsonObject(movement_fields);
            movement_frame.source_json = movement_frame.original_json;
            if (!engine.ProcessFrame(movement_frame, false, &process_error)) {
                result.message = process_error;
                if (error) *error = process_error;
                return result;
            }
            result.unknown_protocol_packets = decoded.newly_classified_packet_count <= result.unknown_protocol_packets ?
                result.unknown_protocol_packets - decoded.newly_classified_packet_count : 0;
        }
    }
    if (!result.message.empty()) {
        if (error) *error = result.message;
        return result;
    }
    result.success = true;
    result.message = "PCAPNG payload extraction completed";
    return result;
}

God2FrameCandidateAnalysisResult AnalyzeInjectedGod2FrameCandidates(const fs::path& session_path,
                                                                    const fs::path& injected_packets,
                                                                    std::string* error) {
    God2FrameCandidateAnalysisResult result;
    std::ifstream input(injected_packets, std::ios::binary);
    if (!input) {
        result.message = "cannot open injected packet evidence";
        if (error) *error = result.message;
        return result;
    }

    std::unordered_map<std::string, God2CandidateStream> streams;
    std::map<std::string, God2CandidateCluster> clusters;
    std::uint64_t candidate_id = 0;
    std::uint64_t frame208_event_hints = 0;
    const auto candidate_target = session_path / L"raw" / L"god2-frame-candidates.jsonl";
    const auto candidate_temporary = candidate_target.parent_path() /
        (L"god2-frame-candidates.jsonl.tmp-" + Utf8ToWide(NewId()));
    std::error_code directory_error;
    fs::create_directories(candidate_target.parent_path(), directory_error);
    if (directory_error) {
        result.message = "cannot create raw candidate directory: " + directory_error.message();
        if (error) *error = result.message;
        return result;
    }
    std::ofstream candidate_output(fs::path(Win32ExtendedPathForFileIo(candidate_temporary)),
                                   std::ios::binary | std::ios::trunc);
    if (!candidate_output) {
        result.message = "cannot create raw/god2-frame-candidates.jsonl";
        if (error) *error = result.message;
        return result;
    }
    const auto semantic_target = session_path / L"raw" / L"god2-opcode-semantic-candidates.jsonl";
    const auto semantic_temporary = semantic_target.parent_path() /
        (L"god2-opcode-semantic-candidates.jsonl.tmp-" + Utf8ToWide(NewId()));
    std::ofstream semantic_output(fs::path(Win32ExtendedPathForFileIo(semantic_temporary)),
                                  std::ios::binary | std::ios::trunc);
    if (!semantic_output) {
        candidate_output.close();
        DeleteFileW(Win32ExtendedPathForFileIo(candidate_temporary).c_str());
        result.message = "cannot create raw/god2-opcode-semantic-candidates.jsonl";
        if (error) *error = result.message;
        return result;
    }
    const auto gameplay_candidate_target = session_path / L"gameplay" / L"protocol-semantic-candidates.jsonl";
    const auto gameplay_candidate_temporary = gameplay_candidate_target.parent_path() /
        (L"protocol-semantic-candidates.jsonl.tmp-" + Utf8ToWide(NewId()));
    std::error_code gameplay_directory_error;
    fs::create_directories(gameplay_candidate_target.parent_path(), gameplay_directory_error);
    if (gameplay_directory_error) {
        candidate_output.close();
        semantic_output.close();
        DeleteFileW(Win32ExtendedPathForFileIo(candidate_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        result.message = "cannot create gameplay candidate directory: " + gameplay_directory_error.message();
        if (error) *error = result.message;
        return result;
    }
    std::ofstream gameplay_candidate_output(fs::path(Win32ExtendedPathForFileIo(gameplay_candidate_temporary)),
                                            std::ios::binary | std::ios::trunc);
    if (!gameplay_candidate_output) {
        candidate_output.close();
        semantic_output.close();
        DeleteFileW(Win32ExtendedPathForFileIo(candidate_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        result.message = "cannot create gameplay/protocol-semantic-candidates.jsonl";
        if (error) *error = result.message;
        return result;
    }

    auto fail = [&](std::string message) {
        candidate_output.close();
        semantic_output.close();
        gameplay_candidate_output.close();
        DeleteFileW(Win32ExtendedPathForFileIo(candidate_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = std::move(message);
        if (error) *error = result.message;
        return result;
    };

    auto emit_candidate = [&](God2CandidateStream& stream,
                              const std::string& stream_key,
                              const std::string& direction,
                              std::uint16_t length) -> bool {
        const auto source_ids = CandidateSourcesForPrefix(stream, length);
        const std::string source_ids_joined = JoinList(source_ids);
        const std::uint8_t* data = stream.bytes.data();
        const std::string payload_hex = Hex(data, length);
        const std::string prefix_hex = payload_hex.substr(0, std::min<std::size_t>(16, payload_hex.size()));
        // These are raw transport bytes and may still be encrypted. Do not
        // present byte offsets inside them as application opcodes.
        const std::string opcode_byte;
        const std::string opcode_u16_le;
        const std::string opcode_u16_be;
        const std::string candidate_frame_id = "god2-candidate-" + std::to_string(++candidate_id);
        const auto pending_after_frame = stream.bytes.size() - length;

        candidate_output << MakeJsonObject({
            {"CandidateFrameId", candidate_frame_id},
            {"SourceFrameIds", source_ids_joined},
            {"PacketDirection", direction},
            {"StreamKey", stream_key},
            {"CandidateLength", std::to_string(length)},
            {"CandidateOpcodeByte", opcode_byte},
            {"CandidateOpcodeU16Le", opcode_u16_le},
            {"CandidateOpcodeU16Be", opcode_u16_be},
            {"OpcodeStatus", "UnavailableEncryptedOrUnverifiedTransport"},
            {"PayloadPrefixHex", prefix_hex},
            {"PayloadHex", payload_hex},
            {"DesyncBytesBeforeFrame", std::to_string(stream.desync_since_frame)},
            {"PendingBytesAfterFrame", std::to_string(pending_after_frame)},
            {"EvidenceLevel", "Candidate"},
            {"Status", "Candidate"},
            {"Reason", "Little-endian 16-bit length prefix matched buffered x86 Winsock stream bytes"}
        }, {"CandidateLength","DesyncBytesBeforeFrame","PendingBytesAfterFrame"});
        candidate_output << '\n';
        if (!candidate_output) return false;

        const std::string cluster_key = direction + "|" + std::to_string(length) + "|" +
            opcode_u16_le + "|" + opcode_u16_be + "|" + opcode_byte + "|" + prefix_hex;
        auto cluster_it = clusters.find(cluster_key);
        if (cluster_it == clusters.end()) {
            if (clusters.size() >= kMaximumCandidateClusters) {
                ++result.evicted_candidate_clusters;
            } else {
                God2CandidateCluster cluster;
                cluster.direction = direction;
                cluster.length = length;
                cluster.opcode_u16_le = opcode_u16_le;
                cluster.opcode_u16_be = opcode_u16_be;
                cluster.opcode_byte = opcode_byte;
                cluster.prefix_hex = prefix_hex;
                cluster.sample_source_frame_ids = source_ids_joined;
                cluster_it = clusters.emplace(cluster_key, std::move(cluster)).first;
            }
        }
        if (cluster_it != clusters.end()) {
            ++cluster_it->second.observed_count;
        }

        ++result.candidate_frames;
        if (direction == "ClientToServer") ++result.client_to_server_frames;
        else if (direction == "ServerToClient") ++result.server_to_client_frames;
        if (length == 208) ++result.frame208_candidates;
        stream.desync_since_frame = 0;
        DiscardCandidatePrefix(stream, length);
        return true;
    };

    auto drain_stream = [&](God2CandidateStream& stream,
                            const std::string& stream_key,
                            const std::string& direction) -> bool {
        while (stream.bytes.size() >= 2) {
            const std::uint16_t length = U16(stream.bytes.data(), true);
            if (length < 2 || length > kMaximumCandidateFrameLength) {
                ++result.desync_bytes;
                ++result.invalid_length_count;
                ++stream.desync_since_frame;
                DiscardCandidatePrefix(stream, 1);
                continue;
            }
            if (stream.bytes.size() < length) break;
            if (!emit_candidate(stream, stream_key, direction, length)) return false;
        }
        return true;
    };

    std::string line;
    while (std::getline(input, line)) {
        if (line.empty()) continue;
        Fields fields;
        std::string parse_error;
        if (!ParseFlatJson(line, fields, &parse_error)) {
            return fail("cannot parse injected packet evidence: " + parse_error);
        }
        const auto api = GetString(fields, "Api");
        if (api != "send" && api != "WSASend" && api != "recv" && api != "WSARecv")
            continue;
        ++result.transport_records;
        const auto payload_hex = GetString(fields, "PayloadHex");
        auto payload_bytes = ParseHexBytes(payload_hex);
        if (!payload_bytes) {
            return fail("invalid injected packet PayloadHex");
        }
        result.payload_bytes += payload_bytes->size();
        if (GetString(fields, "frame208") == "true") ++frame208_event_hints;
        const auto direction = GetString(fields, "PacketDirection",
            GetString(fields, "direction", GetString(fields, "Direction", "Unknown")));
        const auto socket = GetString(fields, "Socket", "unknown-socket");
        const auto local = GetString(fields, "local", GetString(fields, "LocalEndpoint"));
        const auto remote = GetString(fields, "remote", GetString(fields, "RemoteEndpoint"));
        const auto source_frame_id = GetString(fields, "SourceFrameId",
            GetString(fields, "OriginalSourceRecordId",
                GetString(fields, "CaptureRecordId", "injected-transport-" + std::to_string(result.transport_records))));
        const std::string process_id = GetString(fields, "ProcessId", "unknown-process");
        const std::string stream_key = process_id + "|" + socket + "|" + direction + "|" + local + "|" + remote;
        if (!streams.contains(stream_key) && streams.size() >= kMaximumCandidateStreams) {
            const auto evicted = streams.begin();
            result.evicted_stream_bytes += evicted->second.bytes.size();
            streams.erase(evicted);
        }
        auto& stream = streams[stream_key];
        AppendCandidateBytes(stream, *payload_bytes, source_frame_id);
        if (!drain_stream(stream, stream_key, direction))
            return fail("cannot write raw/god2-frame-candidates.jsonl");
    }

    std::string incomplete_streams;
    for (const auto& [stream_key, stream] : streams) {
        result.pending_stream_bytes += stream.bytes.size();
        if (stream.bytes.empty()) continue;
        ++result.incomplete_stream_count;
        std::vector<std::string> source_ids;
        for (const auto& span : stream.sources) {
            if (source_ids.empty() || source_ids.back() != span.source_frame_id)
                source_ids.push_back(span.source_frame_id);
        }
        incomplete_streams += MakeJsonObject({
            {"StreamKey", stream_key}, {"SourceFrameIds", JoinList(source_ids)},
            {"RemainingPayloadHex", Hex(stream.bytes.data(), stream.bytes.size())},
            {"RemainingByteCount", std::to_string(stream.bytes.size())},
            {"DesyncBytesBeforeFragment", std::to_string(stream.desync_since_frame)},
            {"Reason", "Capture ended before a complete candidate length-prefixed frame was available"},
            {"EvidenceLevel", "Candidate"}
        }, {"RemainingByteCount","DesyncBytesBeforeFragment"}) + "\n";
    }
    if (!WriteUtf8FileAtomic(session_path / L"raw" / L"incomplete-stream-fragments.jsonl", incomplete_streams))
        return fail("cannot write raw/incomplete-stream-fragments.jsonl");

    std::string cluster_csv =
        "direction,frame_length,candidate_opcode_u16_le,candidate_opcode_u16_be,candidate_opcode_byte,"
        "payload_prefix_hex,observed_count,sample_source_frame_ids,evidence_level,status\n";
    std::vector<God2CandidateCluster> sorted_clusters;
    sorted_clusters.reserve(clusters.size());
    for (const auto& [_, cluster] : clusters) sorted_clusters.push_back(cluster);
    std::sort(sorted_clusters.begin(), sorted_clusters.end(),
              [](const God2CandidateCluster& left, const God2CandidateCluster& right) {
                  if (left.observed_count != right.observed_count) return left.observed_count > right.observed_count;
                  if (left.direction != right.direction) return left.direction < right.direction;
                  if (left.length != right.length) return left.length < right.length;
                  return left.prefix_hex < right.prefix_hex;
              });
    for (const auto& cluster : sorted_clusters) {
        cluster_csv += CsvEscape(cluster.direction) + "," + std::to_string(cluster.length) + "," +
            CsvEscape(cluster.opcode_u16_le) + "," + CsvEscape(cluster.opcode_u16_be) + "," +
            CsvEscape(cluster.opcode_byte) + "," + CsvEscape(cluster.prefix_hex) + "," +
            std::to_string(cluster.observed_count) + "," + CsvEscape(cluster.sample_source_frame_ids) +
            ",Candidate,Candidate\n";
    }
    result.cluster_count = clusters.size();

    std::map<std::string, std::uint64_t> semantic_counts;
    std::map<std::string, std::uint64_t> candidate_gameplay_group_counts;
    std::map<std::string, std::uint64_t> candidate_application_status_counts;
    std::string semantic_csv =
        "semantic_candidate,direction,frame_length,candidate_opcode_u16_le,candidate_opcode_u16_be,"
        "candidate_opcode_byte,payload_prefix_hex,observed_count,confidence_score,reason,"
        "sample_source_frame_ids,evidence_level,status\n";
    std::string gameplay_candidate_csv =
        "candidate_gameplay_group,semantic_candidate,direction,frame_length,candidate_opcode_u16_le,"
        "candidate_opcode_u16_be,candidate_opcode_byte,payload_prefix_hex,observed_count,confidence_score,"
        "application_status,promotion_blocker,sample_source_frame_ids,evidence_level,status\n";
    // Raw transport candidates can describe only a possible frame envelope.
    // They never emit opcode or gameplay semantic candidates; those require a
    // validated PreEncrypt/PostDecrypt plaintext frame.
    const std::string semantic_counts_json = JsonObjectFromCounts(semantic_counts);
    const std::string candidate_gameplay_group_counts_json = JsonObjectFromCounts(candidate_gameplay_group_counts);
    const std::string candidate_application_status_counts_json = JsonObjectFromCounts(candidate_application_status_counts);

    candidate_output.flush();
    const bool candidate_output_ok = candidate_output.good();
    candidate_output.close();
    if (!candidate_output_ok) {
        DeleteFileW(Win32ExtendedPathForFileIo(candidate_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = "cannot flush raw/god2-frame-candidates.jsonl";
        if (error) *error = result.message;
        return result;
    }
    if (!MoveFileExW(Win32ExtendedPathForFileIo(candidate_temporary).c_str(),
                     Win32ExtendedPathForFileIo(candidate_target).c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        const auto code = GetLastError();
        DeleteFileW(Win32ExtendedPathForFileIo(candidate_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = "cannot atomically replace raw/god2-frame-candidates.jsonl: " + std::to_string(code);
        if (error) *error = result.message;
        return result;
    }
    semantic_output.flush();
    const bool semantic_output_ok = semantic_output.good();
    semantic_output.close();
    if (!semantic_output_ok) {
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = "cannot flush raw/god2-opcode-semantic-candidates.jsonl";
        if (error) *error = result.message;
        return result;
    }
    if (!MoveFileExW(Win32ExtendedPathForFileIo(semantic_temporary).c_str(),
                     Win32ExtendedPathForFileIo(semantic_target).c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        const auto code = GetLastError();
        DeleteFileW(Win32ExtendedPathForFileIo(semantic_temporary).c_str());
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = "cannot atomically replace raw/god2-opcode-semantic-candidates.jsonl: " + std::to_string(code);
        if (error) *error = result.message;
        return result;
    }
    gameplay_candidate_output.flush();
    const bool gameplay_candidate_output_ok = gameplay_candidate_output.good();
    gameplay_candidate_output.close();
    if (!gameplay_candidate_output_ok) {
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = "cannot flush gameplay/protocol-semantic-candidates.jsonl";
        if (error) *error = result.message;
        return result;
    }
    if (!MoveFileExW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str(),
                     Win32ExtendedPathForFileIo(gameplay_candidate_target).c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        const auto code = GetLastError();
        DeleteFileW(Win32ExtendedPathForFileIo(gameplay_candidate_temporary).c_str());
        result.message = "cannot atomically replace gameplay/protocol-semantic-candidates.jsonl: " + std::to_string(code);
        if (error) *error = result.message;
        return result;
    }
    if (!WriteUtf8FileAtomic(session_path / L"exports" / L"god2-frame-candidates.csv", cluster_csv)) {
        result.message = "cannot write exports/god2-frame-candidates.csv";
        if (error) *error = result.message;
        return result;
    }
    if (!WriteUtf8FileAtomic(session_path / L"exports" / L"god2-opcode-semantic-candidates.csv", semantic_csv)) {
        result.message = "cannot write exports/god2-opcode-semantic-candidates.csv";
        if (error) *error = result.message;
        return result;
    }
    if (!WriteUtf8FileAtomic(session_path / L"exports" / L"gameplay-semantic-candidate-coverage.csv",
                             gameplay_candidate_csv)) {
        result.message = "cannot write exports/gameplay-semantic-candidate-coverage.csv";
        if (error) *error = result.message;
        return result;
    }
    const auto gameplay_candidate_report = MakeJsonObject({
        {"Status", result.semantic_candidate_count == 0 ? "EvidenceBlocked" : "Candidate"},
        {"EvidenceLevel", result.semantic_candidate_count == 0 ? "EvidenceBlocked" : "Candidate"},
        {"Reason", "Raw transport has no verified plaintext opcode; gameplay coverage remains empty"},
        {"Decision", "Use validated PreEncrypt/PostDecrypt frames before any gameplay classification"},
        {"TargetExecutable", "God2_opt.exe"},
        {"TargetArchitecture", "x86"},
        {"CandidateGameplayGroups", candidate_gameplay_group_counts_json},
        {"ApplicationStatuses", candidate_application_status_counts_json},
        {"SemanticCandidateCount", std::to_string(result.semantic_candidate_count)},
        {"HighConfidenceSemanticCandidates", std::to_string(result.high_confidence_semantic_candidates)},
        {"CandidateJsonlPath", WideToUtf8(gameplay_candidate_target.wstring())},
        {"CandidateCsvPath", WideToUtf8((session_path / L"exports" / L"gameplay-semantic-candidate-coverage.csv").wstring())}
    }, {"CandidateGameplayGroups","ApplicationStatuses","SemanticCandidateCount","HighConfidenceSemanticCandidates"});
    if (!WriteUtf8FileAtomic(session_path / L"reports" / L"gameplay-semantic-candidate-coverage.json",
                             gameplay_candidate_report + "\n")) {
        result.message = "cannot write reports/gameplay-semantic-candidate-coverage.json";
        if (error) *error = result.message;
        return result;
    }
    const auto semantic_report = MakeJsonObject({
        {"Status", result.semantic_candidate_count == 0 ? "EvidenceBlocked" : "Candidate"},
        {"EvidenceLevel", result.semantic_candidate_count == 0 ? "EvidenceBlocked" : "Candidate"},
        {"Reason", "Raw transport direction, length, frequency, and byte offsets do not establish an opcode semantic"},
        {"Decision", "No opcode or gameplay semantic candidate is emitted until plaintext frame validation succeeds"},
        {"TargetExecutable", "God2_opt.exe"},
        {"TargetArchitecture", "x86"},
        {"SemanticCandidateCount", std::to_string(result.semantic_candidate_count)},
        {"HighConfidenceSemanticCandidates", std::to_string(result.high_confidence_semantic_candidates)},
        {"SemanticCounts", semantic_counts_json},
        {"CandidateGameplayGroups", candidate_gameplay_group_counts_json},
        {"SemanticJsonlPath", WideToUtf8(semantic_target.wstring())},
        {"SemanticCsvPath", WideToUtf8((session_path / L"exports" / L"god2-opcode-semantic-candidates.csv").wstring())},
        {"CandidateCoverageCsvPath", WideToUtf8((session_path / L"exports" / L"gameplay-semantic-candidate-coverage.csv").wstring())}
    }, {"SemanticCandidateCount","HighConfidenceSemanticCandidates","SemanticCounts","CandidateGameplayGroups"});
    if (!WriteUtf8FileAtomic(session_path / L"reports" / L"god2-opcode-semantic-candidates.json",
                             semantic_report + "\n")) {
        result.message = "cannot write reports/god2-opcode-semantic-candidates.json";
        if (error) *error = result.message;
        return result;
    }
    const auto report = MakeJsonObject({
        {"Status", result.candidate_frames == 0 ? "EvidenceBlocked" : "Candidate"},
        {"EvidenceLevel", result.candidate_frames == 0 ? "EvidenceBlocked" : "Candidate"},
        {"Reason", "x86 Winsock payloads were reassembled by socket and direction using a candidate little-endian length prefix"},
        {"Decision", "Frame-envelope candidates are retained without interpreting encrypted bytes as opcodes or gameplay semantics"},
        {"TargetExecutable", "God2_opt.exe"},
        {"TargetArchitecture", "x86"},
        {"CaptureSource", "OptInX86Dll"},
        {"TransportRecords", std::to_string(result.transport_records)},
        {"PayloadBytes", std::to_string(result.payload_bytes)},
        {"CandidateFrames", std::to_string(result.candidate_frames)},
        {"ClientToServerFrames", std::to_string(result.client_to_server_frames)},
        {"ServerToClientFrames", std::to_string(result.server_to_client_frames)},
        {"Frame208Candidates", std::to_string(result.frame208_candidates)},
        {"Frame208EventHints", std::to_string(frame208_event_hints)},
        {"DesyncBytes", std::to_string(result.desync_bytes)},
        {"InvalidLengthCount", std::to_string(result.invalid_length_count)},
        {"PendingStreamBytes", std::to_string(result.pending_stream_bytes)},
        {"IncompleteStreamCount", std::to_string(result.incomplete_stream_count)},
        {"EvictedStreamBytes", std::to_string(result.evicted_stream_bytes)},
        {"EvictedCandidateClusters", std::to_string(result.evicted_candidate_clusters)},
        {"ClusterCount", std::to_string(result.cluster_count)},
        {"SemanticCandidateCount", std::to_string(result.semantic_candidate_count)},
        {"HighConfidenceSemanticCandidates", std::to_string(result.high_confidence_semantic_candidates)},
        {"CandidateGameplayGroups", candidate_gameplay_group_counts_json},
        {"CandidateApplicationStatuses", candidate_application_status_counts_json},
        {"LengthPrefixConfidence", result.candidate_frames != 0 && result.desync_bytes == 0 &&
            result.pending_stream_bytes == 0 && result.evicted_stream_bytes == 0 &&
            result.evicted_candidate_clusters == 0 ? "HighCandidate" : "Candidate"},
        {"CandidateJsonlPath", WideToUtf8(candidate_target.wstring())},
        {"CandidateCsvPath", WideToUtf8((session_path / L"exports" / L"god2-frame-candidates.csv").wstring())},
        {"SemanticCsvPath", WideToUtf8((session_path / L"exports" / L"god2-opcode-semantic-candidates.csv").wstring())},
        {"CandidateCoverageJsonlPath", WideToUtf8(gameplay_candidate_target.wstring())},
        {"CandidateCoverageCsvPath", WideToUtf8((session_path / L"exports" / L"gameplay-semantic-candidate-coverage.csv").wstring())}
    }, {"TransportRecords","PayloadBytes","CandidateFrames","ClientToServerFrames","ServerToClientFrames",
        "Frame208Candidates","Frame208EventHints","DesyncBytes","InvalidLengthCount","PendingStreamBytes","IncompleteStreamCount","EvictedStreamBytes",
        "EvictedCandidateClusters","ClusterCount","SemanticCandidateCount","HighConfidenceSemanticCandidates",
        "CandidateGameplayGroups","CandidateApplicationStatuses"});
    if (!WriteUtf8FileAtomic(session_path / L"reports" / L"god2-frame-candidates.json", report + "\n")) {
        result.message = "cannot write reports/god2-frame-candidates.json";
        if (error) *error = result.message;
        return result;
    }
    result.success = true;
    result.message = "God2 frame candidate analysis completed";
    return result;
}

} // namespace god2
