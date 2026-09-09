#include "AutomaticSemantic.h"

namespace god2 {
namespace {

int HexValue(char value) {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
}

std::optional<std::vector<std::uint8_t>> ParseBytes(std::string_view hex) {
    if (hex.size() % 2 != 0) return std::nullopt;
    std::vector<std::uint8_t> bytes;
    bytes.reserve(hex.size() / 2);
    for (std::size_t index = 0; index < hex.size(); index += 2) {
        const auto high = HexValue(hex[index]);
        const auto low = HexValue(hex[index + 1]);
        if (high < 0 || low < 0) return std::nullopt;
        bytes.push_back(static_cast<std::uint8_t>((high << 4) | low));
    }
    return bytes;
}

AutomaticSemanticCandidate Candidate(std::string type, std::string domain,
                                     std::uint32_t confidence, std::string reason) {
    return {
        std::move(type),
        std::move(domain),
        "PassiveBuildBoundStageDirectionOpcodeLengthAndCallContext",
        std::move(reason),
        "Candidate",
        confidence,
        {}
    };
}

bool Is(const AutomaticSemanticInput& input, std::string_view stage, std::string_view direction,
        std::string_view opcode, std::uint64_t length) {
    return input.capture_stage == stage && input.direction == direction &&
        input.opcode == opcode && input.frame_length == length;
}

} // namespace

std::optional<AutomaticSemanticCandidate> RecognizeAutomaticSemanticCandidate(
    const AutomaticSemanticInput& input) {
    if (Is(input, "PreEncrypt", "ClientToServer", "0x17", 48)) {
        auto candidate = Candidate(
            "CharacterCreateRequestCandidate", "CharacterLifecycle", 82,
            "Exact-build sender path and validated 48-byte plaintext envelope identify the create-request family; individual create settings remain field candidates.");
        candidate.extracted_fields["NameFieldOffsetCandidate"] = "3";
        candidate.extracted_fields["NameFieldLengthCandidate"] = "29";
        candidate.extracted_fields["OpaqueSettingsOffset"] = "32";
        candidate.extracted_fields["OpaqueSettingsLength"] = "15";
        return candidate;
    }
    if (Is(input, "PreEncrypt", "ClientToServer", "0x18", 5)) {
        auto candidate = Candidate(
            "CharacterLifecycleSlotActionCandidate", "CharacterLifecycle", 64,
            "Validated five-byte plaintext action carries one slot/index candidate; create/delete/select operation is not promoted without response and state-delta proof.");
        candidate.extracted_fields["SlotOrIndexOffsetCandidate"] = "3";
        return candidate;
    }
    if (Is(input, "PostDecrypt", "ServerToClient", "0x18", 6)) {
        return Candidate(
            "CharacterLifecycleResultCandidate", "CharacterLifecycle", 58,
            "Validated six-byte response is temporally and structurally eligible for automatic lifecycle correlation; result-code semantics remain unknown.");
    }
    if (Is(input, "PreEncrypt", "ClientToServer", "0x25", 8)) {
        auto candidate = Candidate(
            "ItemRequestCandidate", "InventoryItemRequest", 92,
            "Exact-build senders verify item_id:u16le and slot:u8; the final byte and operation/result semantics remain opaque.");
        candidate.extracted_fields["ClientItemIdOffset"] = "3";
        candidate.extracted_fields["SlotIndexOffset"] = "5";
        candidate.extracted_fields["RawTailOffset"] = "6";
        return candidate;
    }
    if (Is(input, "PreEncrypt", "ClientToServer", "0x28", 8)) {
        auto candidate = Candidate(
            "InventoryActivationCandidate", "InventoryEquipment", 92,
            "Exact-build senders verify item_id:u16le, slot:u8 and a one-valued caller field; activation result semantics remain blocked.");
        candidate.extracted_fields["ClientItemIdOffset"] = "3";
        candidate.extracted_fields["SlotIndexOffset"] = "5";
        candidate.extracted_fields["QuantityOrModeOffsetCandidate"] = "6";
        return candidate;
    }
    if (Is(input, "PreEncrypt", "ClientToServer", "0x2E", 10)) {
        auto candidate = Candidate(
            "MovementRequestCandidate", "Movement", 90,
            "Exact-build sender RVA family and validated ten-byte plaintext layout identify two little-endian coordinates without requiring a marker.");
        candidate.extracted_fields["EndXOffset"] = "3";
        candidate.extracted_fields["EndYOffset"] = "5";
        candidate.extracted_fields["MovementArgumentOffsetCandidate"] = "7";
        return candidate;
    }
    if (Is(input, "PreEncrypt", "ClientToServer", "0x35", 20)) {
        auto candidate = Candidate(
            "BattleCommandCandidate", "Combat", 86,
            "Exact-build battle sender and validated twenty-byte plaintext command layout permit passive battle-command recognition.");
        candidate.extracted_fields["BattlePositionOffsetCandidate"] = "3";
        candidate.extracted_fields["ActionCodeOffsetCandidate"] = "4";
        candidate.extracted_fields["SideOffsetCandidate"] = "5";
        candidate.extracted_fields["TargetMaskOffsetCandidate"] = "7";
        candidate.extracted_fields["ActionParameterOffsetCandidate"] = "15";
        return candidate;
    }
    if (Is(input, "PostDecrypt", "ServerToClient", "0xE6", 53) ||
        Is(input, "PostDecrypt", "ServerToClient", "0xE6", 95)) {
        return Candidate(
            "BattleResultCandidate", "Combat", 70,
            "Existing current-build evidence maps the validated S2C 0xE6 family to a battle-result candidate; result fields and formula inputs remain gated.");
    }
    if (Is(input, "HandlerDecoded", "ServerToClient", "0x83", 15)) {
        const auto bytes = ParseBytes(input.payload_hex);
        if (!bytes || bytes->size() != 15) return std::nullopt;
        const auto raw_delta = static_cast<std::uint16_t>((*bytes)[9]) |
            static_cast<std::uint16_t>(static_cast<std::uint16_t>((*bytes)[10]) << 8);
        const auto delta = static_cast<std::int16_t>(raw_delta);
        const auto outcome_flags = static_cast<std::uint32_t>((*bytes)[11]) |
            (static_cast<std::uint32_t>((*bytes)[12]) << 8) |
            (static_cast<std::uint32_t>((*bytes)[13]) << 16) |
            (static_cast<std::uint32_t>((*bytes)[14]) << 24);
        const auto outcome_low_bits = outcome_flags & 0x0Fu;
        const bool terminal = outcome_low_bits == 0x0Bu;
        auto candidate = Candidate(
            terminal ? "BattleTargetTerminalDelta" : "BattleEffectDeltaCandidate",
            "Combat", terminal ? 92u : 84u,
            "Handler 0x83 exact length exposes the source battle position, signed effect delta and outcome bits. Controlled multi-actor capture proved that the target must be correlated from the preceding command target mask; this record alone cannot identify monster HP.");
        candidate.extracted_fields["AttackerPositionCandidate"] = std::to_string((*bytes)[1]);
        candidate.extracted_fields["SourceBattlePosition"] = std::to_string((*bytes)[2]);
        candidate.extracted_fields["TargetPosition"] = "RequiresCommandTargetMaskCorrelation";
        candidate.extracted_fields["EffectKindCandidate"] = std::to_string((*bytes)[3]);
        candidate.extracted_fields["ObservedSignedDelta"] = std::to_string(delta);
        candidate.extracted_fields["DeltaDirection"] =
            delta < 0 ? "Negative" : delta > 0 ? "Positive" : "Zero";
        candidate.extracted_fields["OutcomeFlags"] = std::to_string(outcome_flags);
        candidate.extracted_fields["OutcomeLowBits"] = std::to_string(outcome_low_bits);
        candidate.extracted_fields["TerminalOutcomeObserved"] = terminal ? "true" : "false";
        candidate.extracted_fields["MonsterHpChainContributionEligible"] = "false";
        candidate.extracted_fields["MonsterHpCorrelationRequirement"] =
            "SourceBattlePositionPlusPrecedingCommandTargetMask";
        candidate.extracted_fields["HitCandidate"] = "Unknown";
        candidate.extracted_fields["CriticalCandidate"] = "Unknown";
        candidate.extracted_fields["ElementCandidate"] = "Unknown";
        return candidate;
    }
    return std::nullopt;
}

int RunAutomaticSemanticRecognitionTests() {
    int failures = 0;
    const auto expect = [&](bool condition) { if (!condition) ++failures; };

    auto battle = RecognizeAutomaticSemanticCandidate(
        {"PreEncrypt", "ClientToServer", "0x35", 20, std::string(40, '0'), "0x00000000", "Strong"});
    expect(battle && battle->candidate_type == "BattleCommandCandidate" && battle->confidence == 86);

    auto create = RecognizeAutomaticSemanticCandidate(
        {"PreEncrypt", "ClientToServer", "0x17", 48, std::string(96, '0'), "0x00000000", "Strong"});
    expect(create && create->candidate_type == "CharacterCreateRequestCandidate" &&
           create->extracted_fields["NameFieldOffsetCandidate"] == "3");

    auto item_request = RecognizeAutomaticSemanticCandidate(
        {"PreEncrypt", "ClientToServer", "0x25", 8, "0800251D0C07FF00", "0x000AEFA8", "Strong"});
    expect(item_request && item_request->candidate_type == "ItemRequestCandidate" &&
           item_request->confidence == 92 &&
           item_request->extracted_fields["ClientItemIdOffset"] == "3" &&
           item_request->extracted_fields["SlotIndexOffset"] == "5" &&
           item_request->extracted_fields["RawTailOffset"] == "6");

    auto inventory_activation = RecognizeAutomaticSemanticCandidate(
        {"PreEncrypt", "ClientToServer", "0x28", 8, "0800280619230100", "0x000AEE7D", "Strong"});
    expect(inventory_activation &&
           inventory_activation->candidate_type == "InventoryActivationCandidate" &&
           inventory_activation->confidence == 92 &&
           inventory_activation->extracted_fields["ClientItemIdOffset"] == "3" &&
           inventory_activation->extracted_fields["SlotIndexOffset"] == "5" &&
           inventory_activation->extracted_fields["QuantityOrModeOffsetCandidate"] == "6");

    std::string damage_hex(30, '0');
    damage_hex.replace(0, 2, "83");
    damage_hex.replace(4, 2, "12");
    damage_hex.replace(18, 4, "F6FF");
    damage_hex.replace(22, 8, "0B1C0000");
    auto damage = RecognizeAutomaticSemanticCandidate(
        {"HandlerDecoded", "ServerToClient", "0x83", 15, damage_hex, "0x0014709B", "Strong"});
    expect(damage && damage->candidate_type == "BattleTargetTerminalDelta" &&
           damage->confidence == 92 &&
           damage->extracted_fields["SourceBattlePosition"] == "18" &&
           damage->extracted_fields["TargetPosition"] == "RequiresCommandTargetMaskCorrelation" &&
           damage->extracted_fields["ObservedSignedDelta"] == "-10" &&
           damage->extracted_fields["DeltaDirection"] == "Negative" &&
           damage->extracted_fields["OutcomeLowBits"] == "11" &&
           damage->extracted_fields["MonsterHpChainContributionEligible"] == "false" &&
           damage->extracted_fields["CriticalCandidate"] == "Unknown");

    auto unknown = RecognizeAutomaticSemanticCandidate(
        {"PreEncrypt", "ClientToServer", "0xFF", 8, std::string(16, '0'), "", "Uncorrelated"});
    expect(!unknown);
    return failures;
}

} // namespace god2
