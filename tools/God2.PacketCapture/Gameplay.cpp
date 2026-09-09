#include "Gameplay.h"
#include "Version.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <fstream>
#include <sstream>

namespace god2 {
namespace {

constexpr std::size_t kMaximumDecodedEvidenceBytes = 4096;

const std::vector<std::string> kMovementEvents = {
    "MovementRequest", "MovementAccepted", "MovementRejected", "MovementStart", "MovementStep",
    "MovementUpdate", "MovementStop", "FacingChanged", "PathRequest", "PathAccepted", "PathRejected",
    "WaypointReached", "ServerPositionCorrection", "ClientPositionCorrection", "Teleport", "ForcedMovement",
    "KnockbackMovement", "BattlePositionChanged", "MapBoundaryMovement", "PortalApproach", "UnknownMovement"
};

const std::vector<std::string> kMountEvents = {
    "MountList", "MountAcquire", "MountRemove", "MountEquip", "MountUnequip", "MountRequest", "MountResult",
    "MountStarted", "DismountRequest", "DismountResult", "Dismounted", "MountStateSnapshot", "MountStateChanged",
    "MountedMovementStart", "MountedMovementUpdate", "MountedMovementStop", "MountAppearanceChanged",
    "MountLevelChanged", "MountExperienceChanged", "MountSkillTriggered", "MountBuffApplied", "MountBuffRemoved",
    "MountUnavailable"
};

const std::vector<std::string> kQuestEvents = {
    "QuestListRequest", "QuestListSnapshot", "QuestAvailable", "QuestUnavailable", "QuestDetailsRequest",
    "QuestDetailsResponse", "QuestAcceptRequest", "QuestAcceptValidation", "QuestAcceptResult",
    "QuestAcceptedNotification", "QuestProgressSnapshot", "QuestProgressUpdate", "QuestObjectiveAdded",
    "QuestObjectiveUpdated", "QuestObjectiveCompleted", "QuestObjectiveReset", "QuestTurnInRequest",
    "QuestTurnInValidation", "QuestTurnInResult", "QuestCompletedNotification", "QuestRewardPreview",
    "QuestRewardSelectionRequest", "QuestRewardSelectionResult", "QuestRewardGranted", "QuestAbandonRequest",
    "QuestAbandonResult", "QuestFailed", "QuestExpired", "QuestRestarted", "QuestShared", "QuestShareAccepted",
    "QuestShareRejected", "QuestTracked", "QuestUntracked", "QuestNpcMarkerChanged", "QuestMapMarkerChanged",
    "QuestStateRefresh"
};

const std::vector<std::string> kSkillEvents = {
    "SkillAvailable", "SkillSelected", "SkillTargetSelected", "SkillCastRequest", "SkillCastValidation",
    "SkillCastAccepted", "SkillCastRejected", "SkillCastStart", "SkillCastProgress", "SkillCastInterrupted",
    "SkillProjectileCreated", "SkillEffectStarted", "SkillHit", "SkillMiss", "SkillCritical", "SkillDamage",
    "SkillHealing", "SkillBuffApplied", "SkillDebuffApplied", "SkillControlApplied", "SkillEffectEnded",
    "SkillResourceConsumed", "SkillCooldownStarted", "SkillCooldownUpdated", "SkillCooldownEnded",
    "SkillCastCompleted"
};

const std::vector<std::string> kConsumableEvents = {
    "ItemUseRequest", "ItemUseValidation", "ItemUseResult", "ItemConsumed", "ItemEffectApplied",
    "ItemCooldownStarted", "InventoryQuantityChanged", "HpChanged", "MpChanged", "BuffApplied",
    "QuestProgressChanged"
};

const std::vector<std::string> kEntityEvents = {
    "NpcSpawn", "NpcSnapshot", "NpcDespawn", "MonsterSpawn", "MonsterSnapshot", "MonsterDespawn",
    "CharacterSnapshot", "BattlePetSnapshot", "PetSnapshot", "ImmortalSnapshot", "MountSnapshot",
    "EntitySpawn", "EntitySnapshot", "EntityStatsChanged", "EntityPositionChanged", "EntityDespawn"
};

const std::vector<std::string> kCharacterLifecycleEvents = {
    "CharacterListSnapshot", "CharacterCreateRequest", "CharacterCreateResult", "CharacterCreated",
    "CharacterSelectRequest", "CharacterSelectResult", "CharacterSelected", "CharacterLogoutRequest",
    "CharacterLogoutResult", "CharacterLoggedOut", "CharacterDeleteRequest", "CharacterDeleteResult",
    "CharacterDeleted"
};

const std::vector<std::string> kPartyEvents = {
    "PartyCreateRequest", "PartyCreateResult", "PartyCreated", "PartyInviteRequest", "PartyInviteReceived",
    "PartyInviteAccepted", "PartyInviteRejected", "PartyJoinRequest", "PartyJoined", "PartySnapshot",
    "PartyMemberAdded", "PartyMemberUpdated", "PartyMemberRemoved", "PartyLeaderChanged", "PartyLeaveRequest",
    "PartyLeft", "PartyDisbanded"
};

const std::vector<std::string> kEconomyEvents = {
    "ShopOpen", "ShopInventory", "ShopPurchaseRequest", "ShopPurchaseResult", "ShopSellRequest",
    "ShopSellResult", "ItemRepairRequest", "ItemRepairResult", "CurrencyChanged", "EconomySnapshot"
};

const std::vector<std::string> kProgressionEvents = {
    "CharacterExperienceChanged", "CharacterLevelChanged", "BattlePetExperienceChanged", "BattlePetLevelChanged",
    "PetExperienceChanged", "PetLevelChanged", "ImmortalExperienceChanged", "ImmortalLevelChanged"
};

const std::vector<std::string> kFormulaEvents = {
    "DamageCalculated", "HitResult", "DodgeResult", "CriticalResult", "ElementInteraction",
    "BattleTurnOrder", "SpeedOrderObserved", "FormulaSample"
};

const std::vector<std::string> kDecodedProtocolEvents = {
    "DecodedServerFrame", "DecodedClientFrame", "DecodedBattleRecord", "BattleCommand",
    "BattleBasicAttackCommand", "BattleDefendCommand", "BattleSkillCommand", "BattleEffectDeltaCandidate",
    "BattleRoundResult", "BattleStateSnapshot", "BattleSettlement",
    "MountEquipmentCandidate", "InventorySlotTransferCandidate",
    "ItemRequestCandidate", "InventoryActivationCandidate"
};

const std::vector<std::string> kFormulaTypes = {
    "Damage", "Healing", "Hit", "Dodge", "Critical", "Element", "SpeedOrder", "LevelProgression"
};

Fields Common(const Frame& frame, EvidenceLevel evidence) {
    return {
        {"observed_at_utc", frame.timestamp_utc},
        {"source_frame_ids", frame.source_frame_ids.empty() ? frame.frame_id : frame.source_frame_ids},
        {"evidence_level", ToString(evidence)}
    };
}

void Copy(Fields& destination, const Frame& frame, std::string_view output_key, std::string_view input_key) {
    destination[std::string(output_key)] = GetString(frame.fields, input_key);
}

EvidenceLevel FrameEvidence(const Frame& frame, EvidenceLevel fallback = EvidenceLevel::Transmitted) {
    const auto evidence = GetString(frame.fields, "EvidenceLevel");
    if (evidence == "Verified") return EvidenceLevel::Verified;
    if (evidence == "Transmitted") return EvidenceLevel::Transmitted;
    if (evidence == "Derived") return EvidenceLevel::Derived;
    if (evidence == "Candidate") return EvidenceLevel::Candidate;
    if (evidence == "Unknown") return EvidenceLevel::Unknown;
    return fallback;
}

bool ContainsEvent(const std::vector<std::string>& events, std::string_view value) {
    return std::find(events.begin(), events.end(), value) != events.end();
}

std::string EntityTypeForFrame(const Frame& frame) {
    const auto explicit_type = GetString(frame.fields, "EntityType");
    if (!explicit_type.empty()) return explicit_type;
    const auto& type = frame.message_type;
    if (type.rfind("Npc", 0) == 0) return "NPC";
    if (type.rfind("Monster", 0) == 0) return "Monster";
    if (type.rfind("BattlePet", 0) == 0) return "BattlePet";
    if (type.rfind("Pet", 0) == 0) return "Pet";
    if (type.rfind("Immortal", 0) == 0) return "Immortal";
    if (type.rfind("Mount", 0) == 0) return "Mount";
    if (type.rfind("Character", 0) == 0) return "Character";
    return "UnknownEntity";
}

std::string EntityIdForFrame(const Frame& frame, std::string_view type) {
    const auto generic = GetString(frame.fields, "EntityId");
    if (!generic.empty()) return generic;
    if (type == "NPC") return GetString(frame.fields, "NpcId");
    if (type == "Monster") return GetString(frame.fields, "MonsterId");
    if (type == "BattlePet") return GetString(frame.fields, "BattlePetId");
    if (type == "Pet") return GetString(frame.fields, "PetId");
    if (type == "Immortal") return GetString(frame.fields, "ImmortalId");
    if (type == "Mount") return GetString(frame.fields, "MountRuntimeId");
    if (type == "Character") return GetString(frame.fields, "CharacterId");
    return {};
}

std::string GameplayGroupForEvent(std::string_view event) {
    if (ContainsEvent(kMovementEvents, event)) return "Movement";
    if (ContainsEvent(kMountEvents, event)) return "Mount";
    if (ContainsEvent(kQuestEvents, event)) return "Quest";
    if (ContainsEvent(kSkillEvents, event)) return "Skill";
    if (ContainsEvent(kConsumableEvents, event)) return "Consumable";
    if (ContainsEvent(kEntityEvents, event)) return "Entity";
    if (ContainsEvent(kCharacterLifecycleEvents, event)) return "CharacterLifecycle";
    if (ContainsEvent(kPartyEvents, event)) return "Party";
    if (ContainsEvent(kEconomyEvents, event)) return "Economy";
    if (ContainsEvent(kProgressionEvents, event)) return "Progression";
    if (ContainsEvent(kFormulaEvents, event)) return "Formula";
    if (ContainsEvent(kDecodedProtocolEvents, event)) return "DecodedProtocol";
    return "Unknown";
}

int GameplayHexDigit(char value) {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
}

std::optional<std::vector<std::uint8_t>> GameplayParseHex(std::string_view value) {
    if (value.empty() || (value.size() % 2) != 0 ||
        value.size() / 2 > kMaximumDecodedEvidenceBytes) return std::nullopt;
    std::vector<std::uint8_t> bytes;
    bytes.reserve(value.size() / 2);
    for (std::size_t index = 0; index < value.size(); index += 2) {
        const int high = GameplayHexDigit(value[index]);
        const int low = GameplayHexDigit(value[index + 1]);
        if (high < 0 || low < 0) return std::nullopt;
        bytes.push_back(static_cast<std::uint8_t>((high << 4) | low));
    }
    return bytes;
}

std::string GameplayHexByte(std::uint8_t value) {
    static constexpr char digits[] = "0123456789ABCDEF";
    std::string result = "0x00";
    result[2] = digits[(value >> 4) & 0x0Fu];
    result[3] = digits[value & 0x0Fu];
    return result;
}

bool GameplayOpcodeEquals(std::string_view value, std::string_view canonical) {
    if (value.size() != canonical.size()) return false;
    return std::equal(value.begin(), value.end(), canonical.begin(),
        [](unsigned char left, unsigned char right) {
            return std::toupper(left) == std::toupper(right);
        });
}

void BlockDecodedSemanticPromotion(Frame& frame, std::string validation,
                                   std::string semantic_status = "EvidenceBlocked") {
    if (!frame.message_type.empty()) frame.fields["ClaimedMessageType"] = frame.message_type;
    frame.message_type = "DecodedFrameValidationFailed";
    frame.fields["MessageType"] = frame.message_type;
    frame.fields["EvidenceLevel"] = "Unknown";
    frame.fields["ProtocolSemanticStatus"] = std::move(semantic_status);
    frame.fields["DecodeValidation"] = std::move(validation);
}

std::uint16_t GameplayU16(const std::vector<std::uint8_t>& bytes, std::size_t offset) {
    return static_cast<std::uint16_t>(bytes[offset]) |
        static_cast<std::uint16_t>(static_cast<std::uint16_t>(bytes[offset + 1]) << 8);
}

std::uint32_t GameplayU32(const std::vector<std::uint8_t>& bytes, std::size_t offset) {
    return static_cast<std::uint32_t>(bytes[offset]) |
        (static_cast<std::uint32_t>(bytes[offset + 1]) << 8) |
        (static_cast<std::uint32_t>(bytes[offset + 2]) << 16) |
        (static_cast<std::uint32_t>(bytes[offset + 3]) << 24);
}

void NormalizeCapturedDecodedFrame(Frame& frame) {
    const auto stage = GetString(frame.fields, "CaptureStage");
    if (stage != "PostDecrypt" && stage != "PreEncrypt" && stage != "HandlerDecoded") return;
    const auto bytes = GameplayParseHex(frame.payload_hex);
    if (!bytes) {
        BlockDecodedSemanticPromotion(frame, "InvalidHex");
        return;
    }

    frame.fields["ProtocolSemanticStatus"] = stage == "PostDecrypt" ? "Decoded" : "Candidate";
    if (stage == "PostDecrypt" || stage == "PreEncrypt") {
        if (bytes->size() < 4) {
            BlockDecodedSemanticPromotion(frame, "FrameTooShort");
            return;
        }
        const auto declared = GameplayU16(*bytes, 0);
        frame.fields["DeclaredFrameLength"] = std::to_string(declared);
        const auto payload_opcode = GameplayHexByte((*bytes)[2]);
        frame.fields["ApplicationOpcode"] = payload_opcode;
        unsigned char checksum{};
        for (std::size_t index = 0; index + 1 < bytes->size(); ++index) {
            checksum = static_cast<unsigned char>(checksum +
                static_cast<unsigned char>((*bytes)[index] + 0x3Cu));
        }
        const bool valid = declared == bytes->size() && checksum == bytes->back();
        frame.fields["ChecksumValid"] = valid ? "true" : "false";
        frame.fields["DecodeValidation"] = valid ? "Valid" : "LengthOrChecksumMismatch";
        if (!valid) {
            BlockDecodedSemanticPromotion(frame, "LengthOrChecksumMismatch");
            return;
        }
        if (!frame.opcode.empty() && !GameplayOpcodeEquals(frame.opcode, payload_opcode)) {
            BlockDecodedSemanticPromotion(frame, "OpcodeMismatch");
            return;
        }
        frame.opcode = payload_opcode;
        frame.fields["Opcode"] = payload_opcode;
    } else {
        if (bytes->empty()) {
            BlockDecodedSemanticPromotion(frame, "HandlerRecordEmpty");
            return;
        }
        const auto payload_opcode = GameplayHexByte((*bytes)[0]);
        frame.fields["HandlerOpcode"] = payload_opcode;
        frame.fields["HandlerRecordLength"] = std::to_string(bytes->size());
        const bool handler_opcode_mismatch =
            !frame.opcode.empty() && !GameplayOpcodeEquals(frame.opcode, payload_opcode);
        const bool handler_length_mismatch = payload_opcode == "0x83" && bytes->size() != 15;
        if (handler_opcode_mismatch || handler_length_mismatch) {
            BlockDecodedSemanticPromotion(frame,
                handler_opcode_mismatch ? "HandlerOpcodeMismatch" : "HandlerLengthMismatch");
            return;
        }
        frame.opcode = payload_opcode;
        frame.fields["Opcode"] = payload_opcode;
        frame.fields["DecodeValidation"] = "ClientLengthFunctionBounded";
    }

    // Controlled live differential capture verified action codes 1, 2 and 3 as
    // basic attack, defend and skill. Target masks and action parameters remain
    // passive evidence until their meanings are independently reproduced.
    if (stage == "PreEncrypt" && frame.opcode == "0x35" && bytes->size() == 20) {
        const auto action = (*bytes)[4];
        switch (action & 0x7Fu) {
        case 1u: frame.message_type = "BattleBasicAttackCommand"; break;
        case 2u: frame.message_type = "BattleDefendCommand"; break;
        case 3u: frame.message_type = "BattleSkillCommand"; break;
        default: frame.message_type = "BattleCommand"; break;
        }
        frame.fields["MessageType"] = frame.message_type;
        frame.fields["BattlePositionCandidate"] = std::to_string((*bytes)[3]);
        frame.fields["ActionCodeCandidate"] = std::to_string(action & 0x7Fu);
        frame.fields["ContinuationCandidate"] = (action & 0x80u) != 0 ? "true" : "false";
        frame.fields["SideCandidate"] = std::to_string((*bytes)[5]);
        frame.fields["TargetMask0Candidate"] = std::to_string(GameplayU16(*bytes, 7));
        frame.fields["TargetMask1Candidate"] = std::to_string(GameplayU16(*bytes, 9));
        frame.fields["TargetMask2Candidate"] = std::to_string(GameplayU16(*bytes, 11));
        frame.fields["BattleContextCandidate"] = std::to_string(GameplayU16(*bytes, 13));
        frame.fields["ActionParameterCandidate"] = std::to_string(GameplayU32(*bytes, 15));
        frame.fields["ProtocolSemanticStatus"] = (action & 0x7Fu) >= 1u && (action & 0x7Fu) <= 3u
            ? "ControlledActionMeaningVerified_TargetAndParameterEvidenceBlocked"
            : "StaticFieldLayoutVerified_ActionMeaningCandidate";
    }

    // Exact-build senders pin opcodes 0x25/0x28 to a four-byte payload. Both
    // carry item_id:u16le and slot:u8. The exact 0x25 sender does not
    // consistently write the final byte, so preserve it as opaque RawTail.
    // 0x28 callers supply one in the final field, but the operation meaning is
    // still activation/equip/move candidate evidence rather than a runtime write.
    if (stage == "PreEncrypt" && bytes->size() == 8 &&
        (frame.opcode == "0x25" || frame.opcode == "0x28")) {
        frame.message_type = frame.opcode == "0x25" ?
            "ItemRequestCandidate" : "InventoryActivationCandidate";
        frame.fields["MessageType"] = frame.message_type;
        frame.fields["ClientItemId"] = std::to_string(GameplayU16(*bytes, 3));
        frame.fields["SlotIndex"] = std::to_string((*bytes)[5]);
        if (frame.opcode == "0x25") {
            frame.fields["RawTail"] = std::to_string((*bytes)[6]);
            frame.fields["ProtocolSemanticStatus"] =
                "ExactBuildStaticItemSlotVerified_RawTailOpaque_OperationMeaningBlocked";
        } else {
            frame.fields["QuantityOrModeCandidate"] = std::to_string((*bytes)[6]);
            frame.fields["ProtocolSemanticStatus"] =
                "ExactBuildStaticItemSlotVerified_ActivationMeaningCandidate_RuntimeMutationBlocked";
        }
    }

    // Exact-build sender RVA 0x0007FDD0 pins the 0x2E payload to two LE
    // coordinates, one explicit byte argument, and one uninitialised/reserved
    // padding byte. Never promote that final byte to movement state.
    if ((stage == "PreEncrypt" || stage == "PostDecrypt") &&
        frame.opcode == "0x2E" && bytes->size() == 10) {
        frame.message_type = stage == "PreEncrypt" ? "MovementRequest" : "MovementUpdate";
        frame.fields["MessageType"] = frame.message_type;
        frame.fields["EndX"] = std::to_string(GameplayU16(*bytes, 3));
        frame.fields["EndY"] = std::to_string(GameplayU16(*bytes, 5));
        frame.fields["MovementArgumentCandidate"] = std::to_string((*bytes)[7]);
        frame.fields["ReservedByteCandidate"] = std::to_string((*bytes)[8]);
        frame.fields["ProtocolSemanticStatus"] =
            "StaticCoordinateLayoutVerified_ArgumentMeaningCandidate";
    }

    // Exact-build sender RVA 0x0007D0D0 pins CharacterCreate to opcode 0x17,
    // a 44-byte payload, and marker value 4 at payload offset 40. Name/class/
    // gender/life-skill/appearance offsets remain evidence-blocked.
    if (stage == "PreEncrypt" && frame.opcode == "0x17" && bytes->size() == 48) {
        frame.message_type = "CharacterCreateRequest";
        frame.fields["MessageType"] = frame.message_type;
        frame.fields["CharacterCreateMarkerOffset"] = "40";
        frame.fields["CharacterCreateMarkerValue"] = std::to_string((*bytes)[43]);
        if ((*bytes)[43] != 4u) {
            BlockDecodedSemanticPromotion(frame, "StaticMarkerMismatch",
                                          "EvidenceBlocked_StaticMarkerMismatch");
            return;
        }
        frame.fields["ProtocolSemanticStatus"] =
            "StaticEnvelopeVerified_FieldSemanticsEvidenceBlocked_RuntimeMutationBlocked";
    }

    // The official Client length table fixes handler opcode 0x83 at 15 bytes.
    // Controlled live capture proves that its signed delta is not sufficient to
    // distinguish damage, healing, maximum-state synchronization, or another
    // battle field, so retain it as neutral protocol evidence.
    if (stage == "HandlerDecoded" && frame.opcode == "0x83" && bytes->size() == 15) {
        const auto delta = static_cast<std::int16_t>(GameplayU16(*bytes, 9));
        const auto outcome_flags = GameplayU32(*bytes, 11);
        const auto outcome_low_bits = outcome_flags & 0x0Fu;
        const bool terminal = outcome_low_bits == 0x0Bu;
        frame.message_type = terminal ? "BattleTargetTerminalDelta" : "BattleEffectDeltaCandidate";
        frame.fields["MessageType"] = frame.message_type;
        frame.fields["AttackerPositionCandidate"] = std::to_string((*bytes)[1]);
        frame.fields["SourceBattlePosition"] = std::to_string((*bytes)[2]);
        frame.fields["TargetPosition"] = "RequiresCommandTargetMaskCorrelation";
        frame.fields["TargetPositionCandidate"] = frame.fields["SourceBattlePosition"];
        frame.fields["EffectKindCandidate"] = std::to_string((*bytes)[3]);
        frame.fields["EffectFlagsCandidate"] = std::to_string(GameplayU32(*bytes, 4));
        frame.fields["ObservedSignedDelta"] = std::to_string(delta);
        frame.fields["ObservedSignedDeltaCandidate"] = frame.fields["ObservedSignedDelta"];
        frame.fields["OutcomeFlags"] = std::to_string(outcome_flags);
        frame.fields["OutcomeFlagsCandidate"] = frame.fields["OutcomeFlags"];
        frame.fields["OutcomeLowBits"] = std::to_string(outcome_low_bits);
        frame.fields["TerminalOutcomeObserved"] = terminal ? "true" : "false";
        frame.fields["MonsterHpChainContributionEligible"] = "false";
        frame.fields["MonsterHpCorrelationRequirement"] =
            "SourceBattlePositionPlusPrecedingCommandTargetMask";
        frame.fields["ObservedResultCandidate"] = std::to_string(delta);
        frame.fields["DeltaDirection"] = delta < 0 ? "Negative" : delta > 0 ? "Positive" : "Zero";
        frame.fields["DeltaDirectionCandidate"] = frame.fields["DeltaDirection"];
        frame.fields["CandidateConfidence"] = terminal ? "0.92" : "0.84";
        frame.fields["HitCandidate"] = "Unknown";
        frame.fields["CriticalCandidate"] = "Unknown";
        frame.fields["ElementCandidate"] = "Unknown";
        frame.fields["ProtocolSemanticStatus"] = terminal
            ? "SourcePositionAndTerminalOutcomeVerified_TargetRequiresCommandMaskCorrelation"
            : "SourcePositionAndDeltaVerified_TargetRequiresCommandMaskCorrelation";
    }
}

bool HasFormulaEvidence(const Frame& frame) {
    if (ContainsEvent(kFormulaEvents, frame.message_type)) return true;
    if (!GetString(frame.fields, "FormulaType").empty()) return true;
    return frame.message_type == "SkillDamage" || frame.message_type == "SkillHealing" || frame.message_type == "SkillHit" ||
           frame.message_type == "SkillMiss" || frame.message_type == "SkillCritical" ||
           frame.message_type.find("LevelChanged") != std::string::npos;
}

std::string DirectionFromRaw(std::string_view raw) {
    static const std::map<std::string, std::string> mapping = {
        {"0", "North"}, {"1", "NorthEast"}, {"2", "East"}, {"3", "SouthEast"},
        {"4", "South"}, {"5", "SouthWest"}, {"6", "West"}, {"7", "NorthWest"},
        {"8", "Idle"}
    };
    const auto it = mapping.find(std::string(raw));
    return it == mapping.end() ? "UnknownDirection" : it->second;
}

MovementDirection ParseDirection(std::string_view value) {
    static const std::map<std::string, MovementDirection> values = {
        {"North", MovementDirection::North}, {"NorthEast", MovementDirection::NorthEast},
        {"East", MovementDirection::East}, {"SouthEast", MovementDirection::SouthEast},
        {"South", MovementDirection::South}, {"SouthWest", MovementDirection::SouthWest},
        {"West", MovementDirection::West}, {"NorthWest", MovementDirection::NorthWest},
        {"Idle", MovementDirection::Idle}, {"UnknownDirection", MovementDirection::UnknownDirection}
    };
    const auto it = values.find(std::string(value));
    return it == values.end() ? MovementDirection::UnknownDirection : it->second;
}

std::string QuestCorrelationKey(const Frame& frame) {
    return "quest:" + GetString(frame.fields, "QuestId", "unknown");
}

std::string QuestActionFamily(std::string_view message_type) {
    if (message_type.find("QuestList") != std::string_view::npos) return "List";
    if (message_type.find("QuestDetails") != std::string_view::npos) return "Details";
    if (message_type.find("QuestAccept") != std::string_view::npos ||
        message_type == "QuestAcceptedNotification") return "Accept";
    if (message_type.find("Objective") != std::string_view::npos ||
        message_type.find("Progress") != std::string_view::npos) return "Progress";
    if (message_type.find("TurnIn") != std::string_view::npos ||
        message_type.find("Completed") != std::string_view::npos ||
        message_type.find("Reward") != std::string_view::npos) return "TurnInReward";
    if (message_type.find("Abandon") != std::string_view::npos) return "Abandon";
    if (message_type.find("Share") != std::string_view::npos) return "Share";
    if (message_type.find("Tracked") != std::string_view::npos ||
        message_type.find("Untracked") != std::string_view::npos) return "Tracking";
    if (message_type.find("Marker") != std::string_view::npos) return "Marker";
    if (message_type == "QuestFailed" || message_type == "QuestExpired" ||
        message_type == "QuestRestarted") return "Lifecycle";
    return std::string(message_type);
}

std::string QuestActionCorrelationKey(const Frame& frame) {
    const auto family = QuestActionFamily(frame.message_type);
    std::string key = "quest-action:" + GetString(frame.fields, "QuestId", "unknown") + ":" + family;
    if (family == "Progress") {
        key += ":" + GetString(frame.fields, "QuestStageId", "unknown") + ":" +
               GetString(frame.fields, "ObjectiveId", "unknown");
    }
    return key;
}

bool StartsQuestAction(std::string_view message_type) {
    return message_type.find("Request") != std::string_view::npos || message_type == "QuestRestarted";
}

bool EndsQuestAction(std::string_view message_type) {
    return message_type.find("Result") != std::string_view::npos ||
           message_type.find("Rejected") != std::string_view::npos ||
           message_type == "QuestAcceptedNotification" || message_type == "QuestCompletedNotification" ||
           message_type == "QuestRewardGranted" || message_type == "QuestFailed" ||
           message_type == "QuestExpired";
}

std::string RelatedEntityType(const Frame& frame) {
    const auto explicit_type = GetString(frame.fields, "RelatedEntityType");
    if (!explicit_type.empty()) return explicit_type;
    const auto& type = frame.message_type;
    if (type.find("Monster") != std::string::npos) return "Monster";
    if (type.find("Item") != std::string::npos || type.find("Inventory") != std::string::npos) return "Item";
    if (type.find("Npc") != std::string::npos || type.find("NPC") != std::string::npos) return "NPC";
    if (type.find("Map") != std::string::npos) return "Map";
    if (type.find("Portal") != std::string::npos) return "Portal";
    if (type.find("Party") != std::string::npos) return "Party";
    if (type.find("Battle") != std::string::npos) return "Battle";
    if (type.find("Skill") != std::string::npos) return "Skill";
    if (type.find("Pet") != std::string::npos) return "Pet";
    if (type.find("Immortal") != std::string::npos) return "Immortal";
    if (type.find("Experience") != std::string::npos) return "Experience";
    if (type.find("Currency") != std::string::npos) return "Currency";
    if (type.find("Reputation") != std::string::npos) return "Reputation";
    if (type.find("Reward") != std::string::npos) return "Reward";
    return "QuestRelatedEntity";
}

std::string RelatedEntityId(const Frame& frame, std::string_view type) {
    const auto explicit_id = GetString(frame.fields, "RelatedEntityId");
    if (!explicit_id.empty()) return explicit_id;
    std::vector<std::string_view> candidates;
    if (type == "Monster") candidates = {"MonsterEntityId", "MonsterId", "TargetMonsterId"};
    else if (type == "Item") candidates = {"DroppedItemId", "ItemId", "TargetItemId"};
    else if (type == "NPC") candidates = {"NpcId", "TargetNpcId"};
    else if (type == "Map") candidates = {"MapId", "TargetMapId"};
    else if (type == "Portal") candidates = {"PortalId"};
    else if (type == "Party") candidates = {"PartyId"};
    else if (type == "Battle") candidates = {"BattleId"};
    else if (type == "Skill") candidates = {"SkillId"};
    else if (type == "Pet") candidates = {"PetId"};
    else if (type == "Immortal") candidates = {"ImmortalId"};
    for (const auto candidate : candidates) {
        const auto value = GetString(frame.fields, candidate);
        if (!value.empty()) return value;
    }
    return GetString(frame.fields, "EntityId");
}

std::string SkillCorrelationKey(const Frame& frame) {
    const auto caster = GetString(frame.fields, "CasterEntityId", "unknown");
    const auto skill = GetString(frame.fields, "SkillId", "unknown");
    return "skill:" + caster + ":" + skill;
}

std::string ConsumableCorrelationKey(const Frame& frame) {
    return "item:" + GetString(frame.fields, "CharacterId", "unknown") + ":" +
           GetString(frame.fields, "ItemId", "unknown");
}

std::vector<std::string> ValidFacets(const Frame& frame, bool unknown_skill) {
    if (unknown_skill) return {};
    std::vector<std::string> requested = SplitList(GetString(frame.fields, "FacetIds"));
    const auto& catalog = SkillFacetCatalog();
    requested.erase(std::remove_if(requested.begin(), requested.end(), [&](const std::string& value) {
        return !catalog.contains(value);
    }), requested.end());
    std::sort(requested.begin(), requested.end());
    requested.erase(std::unique(requested.begin(), requested.end()), requested.end());
    return requested;
}

} // namespace

std::string ToString(MovementDirection value) {
    switch (value) {
    case MovementDirection::North: return "North";
    case MovementDirection::NorthEast: return "NorthEast";
    case MovementDirection::East: return "East";
    case MovementDirection::SouthEast: return "SouthEast";
    case MovementDirection::South: return "South";
    case MovementDirection::SouthWest: return "SouthWest";
    case MovementDirection::West: return "West";
    case MovementDirection::NorthWest: return "NorthWest";
    case MovementDirection::Idle: return "Idle";
    default: return "UnknownDirection";
    }
}

std::string DirectionChinese(MovementDirection value) {
    switch (value) {
    case MovementDirection::North: return "上";
    case MovementDirection::NorthEast: return "右上";
    case MovementDirection::East: return "右";
    case MovementDirection::SouthEast: return "右下";
    case MovementDirection::South: return "下";
    case MovementDirection::SouthWest: return "左下";
    case MovementDirection::West: return "左";
    case MovementDirection::NorthWest: return "左上";
    case MovementDirection::Idle: return "停止";
    default: return "未知";
    }
}

DirectionResult ResolveDirection(const Frame& frame) {
    const auto transmitted = GetString(frame.fields, "DirectionNormalized");
    if (!transmitted.empty()) {
        const auto parsed = ParseDirection(transmitted);
        if (parsed != MovementDirection::UnknownDirection || transmitted == "UnknownDirection") {
            return {parsed, FrameEvidence(frame, EvidenceLevel::Transmitted), "PacketField"};
        }
    }

    const auto raw = GetString(frame.fields, "DirectionRaw");
    const auto encoding = GetString(frame.fields, "DirectionEncoding");
    if (!raw.empty() && encoding == "EightWay0NorthClockwiseV1") {
        const auto normalized = DirectionFromRaw(raw);
        const auto verified = GetBool(frame.fields, "DirectionMappingVerified").value_or(false);
        return {ParseDirection(normalized), verified ? EvidenceLevel::Verified : EvidenceLevel::Candidate,
                verified ? "VerifiedDirectionCode" : "DeclaredDirectionCode"};
    }

    const auto start_x = GetDouble(frame.fields, "StartX");
    const auto start_y = GetDouble(frame.fields, "StartY");
    const auto end_x = GetDouble(frame.fields, "EndX");
    const auto end_y = GetDouble(frame.fields, "EndY");
    if (!start_x || !start_y || !end_x || !end_y) return {};
    const double dx = *end_x - *start_x;
    const double dy = *end_y - *start_y;
    constexpr double epsilon = 0.0001;
    if (std::abs(dx) <= epsilon && std::abs(dy) <= epsilon) {
        return {MovementDirection::Idle, EvidenceLevel::Derived, "CoordinateDelta"};
    }
    const double absolute_x = std::abs(dx);
    const double absolute_y = std::abs(dy);
    const bool north = dy < -epsilon;
    const bool south = dy > epsilon;
    const bool east = dx > epsilon;
    const bool west = dx < -epsilon;
    constexpr double cardinal_ratio = 2.41421356237; // tan(67.5 degrees)
    MovementDirection direction = MovementDirection::UnknownDirection;
    if (absolute_x > absolute_y * cardinal_ratio) direction = east ? MovementDirection::East : MovementDirection::West;
    else if (absolute_y > absolute_x * cardinal_ratio) direction = north ? MovementDirection::North : MovementDirection::South;
    else if (north && east) direction = MovementDirection::NorthEast;
    else if (south && east) direction = MovementDirection::SouthEast;
    else if (south && west) direction = MovementDirection::SouthWest;
    else if (north && west) direction = MovementDirection::NorthWest;
    return {direction, EvidenceLevel::Derived, "CoordinateDelta"};
}

ProtocolParserRegistry::ProtocolParserRegistry() {
    features_.push_back({"protocol.flat-json-v1", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown",
                         {"Movement", "Mount", "Quest", "Skill", "Consumable"}, {"*"}});
}

bool ProtocolParserRegistry::Parse(std::string_view json, Frame& frame, std::string* error) const {
    Fields fields;
    if (!ParseFlatJson(json, fields, error)) return false;
    frame.source_json = std::string(json);
    frame.frame_id = GetString(fields, "SourceFrameId", GetString(fields, "FrameId"));
    if (frame.frame_id.empty()) frame.frame_id = NewId();
    frame.source_frame_ids = GetString(fields, "SourceFrameIds", frame.frame_id);
    frame.timestamp_utc = GetString(fields, "ObservedAtUtc", GetString(fields, "TimestampUtc"));
    if (frame.timestamp_utc.empty()) frame.timestamp_utc = UtcNow();
    frame.direction = GetString(fields, "PacketDirection", "Unknown");
    frame.opcode = GetString(fields, "Opcode");
    frame.message_type = GetString(fields, "MessageType");
    frame.payload_hex = GetString(fields, "PayloadHex");
    fields["SourceFrameId"] = frame.frame_id;
    fields["ObservedAtUtc"] = frame.timestamp_utc;
    frame.fields = std::move(fields);
    NormalizeCapturedDecodedFrame(frame);
    fields = frame.fields;
    std::vector<std::pair<std::string, std::string>> canonical_fields(fields.begin(), fields.end());
    std::sort(canonical_fields.begin(), canonical_fields.end());
    frame.original_json = MakeJsonObject(canonical_fields);
    frame.fields = std::move(fields);
    return true;
}

void GameplayClassifierRegistry::Register(const FeatureMetadata& metadata,
                                          const std::vector<std::string>& message_types,
                                          Handler handler) {
    features_.push_back(metadata);
    for (const auto& type : message_types) handlers_[type] = handler;
}

bool GameplayClassifierRegistry::Dispatch(const Frame& frame, std::string* error) const {
    const auto it = handlers_.find(frame.message_type);
    return it != handlers_.end() && it->second(frame, error);
}

bool GameplayClassifierRegistry::Contains(std::string_view message_type) const {
    return handlers_.contains(std::string(message_type));
}

void EventCorrelatorRegistry::Remember(std::string key, std::string correlation_id, bool active) {
    if (!correlations_.contains(key)) {
        if (correlations_.size() >= kMaximumCorrelations) {
            correlations_.erase(insertion_order_.front());
            insertion_order_.pop_front();
        }
        insertion_order_.push_back(key);
    }
    correlations_[std::move(key)] = {std::move(correlation_id), active};
}

std::string EventCorrelatorRegistry::Begin(std::string_view key, std::string_view transmitted) {
    auto id = transmitted.empty() ? NewId() : std::string(transmitted);
    Remember(std::string(key), id, true);
    return id;
}

std::string EventCorrelatorRegistry::Resolve(std::string_view key, std::string_view transmitted) {
    if (!transmitted.empty()) {
        Remember(std::string(key), std::string(transmitted), true);
        return std::string(transmitted);
    }
    const auto it = correlations_.find(std::string(key));
    if (it != correlations_.end()) return it->second.correlation_id;
    auto id = NewId();
    Remember(std::string(key), id, true);
    return id;
}

void EventCorrelatorRegistry::End(std::string_view key) {
    const auto it = correlations_.find(std::string(key));
    if (it != correlations_.end()) it->second.active = false;
}

bool EventCorrelatorRegistry::IsActive(std::string_view key) const {
    const auto it = correlations_.find(std::string(key));
    return it != correlations_.end() && it->second.active;
}

FeatureMetadata EventCorrelatorRegistry::Metadata() const {
    return {"event-correlator", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown",
            {"Quest", "SkillCast", "ConsumableUse"}, {"*"}};
}

FeatureMetadata FormulaAnalyzerRegistry::Metadata() const {
    return {"movement-direction-formula", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown",
            {"Movement", "Mount"}, kMovementEvents};
}

FeatureMetadata ExportRegistry::Metadata() const {
    return {"sqlite-jsonl-csv-export", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown",
            {"Movement", "Mount", "Quest", "Skill", "Consumable"}, {"*"}};
}

GameplayAnalysisEngine::GameplayAnalysisEngine(SessionStore& store) : store_(store) {
    RegisterHandlers();
    for (const auto& [facet, dimension] : SkillFacetCatalog()) {
        store_.Db().Insert("skill_facets", {
            {"facet_id", facet}, {"facet_dimension", dimension}, {"display_name", facet}, {"schema_version", "6"}
        });
    }
    const std::pair<const char*, const std::vector<std::string>*> groups[] = {
        {"Movement", &kMovementEvents}, {"Mount", &kMountEvents}, {"Quest", &kQuestEvents},
        {"Skill", &kSkillEvents}, {"Consumable", &kConsumableEvents}, {"Entity", &kEntityEvents},
        {"CharacterLifecycle", &kCharacterLifecycleEvents}, {"Party", &kPartyEvents},
        {"Economy", &kEconomyEvents}, {"Progression", &kProgressionEvents}, {"Formula", &kFormulaEvents},
        {"DecodedProtocol", &kDecodedProtocolEvents}
    };
    if (!store_.AnalysisRunId().empty()) {
        for (const auto& [group, events] : groups) {
            for (const auto& event : *events) {
                store_.Db().Execute("INSERT OR IGNORE INTO gameplay_coverage(session_id,analysis_run_id,feature_group,feature_id,coverage_status,observed_count,last_source_frame_ids,evidence_level) VALUES(" +
                    SqlQuote(store_.SessionId()) + "," + SqlQuote(store_.AnalysisRunId()) + "," + SqlQuote(group) + "," +
                    SqlQuote(event) + ",'NotObserved',0,'','Unknown');");
            }
        }
        for (const auto& formula : kFormulaTypes) {
            store_.Db().Execute("INSERT OR IGNORE INTO formula_coverage(session_id,analysis_run_id,formula_type,coverage_status,observed_count,last_source_frame_ids,evidence_level) VALUES(" +
                SqlQuote(store_.SessionId()) + "," + SqlQuote(store_.AnalysisRunId()) + "," + SqlQuote(formula) +
                ",'NotObserved',0,'','Unknown');");
        }
    }
}

void GameplayAnalysisEngine::RegisterHandlers() {
    classifiers_.Register({"movement", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Character", "Entity"}, kMovementEvents},
                          kMovementEvents, [this](const Frame& f, std::string* e) { return HandleMovement(f, e); });
    classifiers_.Register({"mount", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Character", "Mount"}, kMountEvents},
                          kMountEvents, [this](const Frame& f, std::string* e) { return HandleMount(f, e); });
    classifiers_.Register({"quest-lifecycle", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Quest", "Objective"}, kQuestEvents},
                          kQuestEvents, [this](const Frame& f, std::string* e) { return HandleQuest(f, e); });
    classifiers_.Register({"skill-facets", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Skill", "SkillCast"}, kSkillEvents},
                          kSkillEvents, [this](const Frame& f, std::string* e) { return HandleSkill(f, e); });
    classifiers_.Register({"consumable", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Item", "Medicine"}, kConsumableEvents},
                          kConsumableEvents, [this](const Frame& f, std::string* e) { return HandleConsumable(f, e); });
    classifiers_.Register({"entity-observation", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown",
                           {"NPC", "Monster", "Character", "BattlePet", "Pet", "Immortal", "Mount"}, kEntityEvents},
                          kEntityEvents, [this](const Frame& f, std::string* e) { return HandleEntity(f, e); });
    classifiers_.Register({"character-lifecycle", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Character"}, kCharacterLifecycleEvents},
                          kCharacterLifecycleEvents, [this](const Frame& f, std::string* e) { return HandleCharacterLifecycle(f, e); });
    classifiers_.Register({"party-lifecycle", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Party", "Character"}, kPartyEvents},
                          kPartyEvents, [this](const Frame& f, std::string* e) { return HandleParty(f, e); });
    classifiers_.Register({"shop-economy", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Shop", "Item", "Character"}, kEconomyEvents},
                          kEconomyEvents, [this](const Frame& f, std::string* e) { return HandleEconomy(f, e); });
    classifiers_.Register({"entity-progression", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown",
                           {"Character", "BattlePet", "Pet", "Immortal"}, kProgressionEvents},
                          kProgressionEvents, [this](const Frame& f, std::string* e) { return HandleProgression(f, e); });
    classifiers_.Register({"formula-recovery", GOD2_TOOL_VERSION, "6", GOD2_TOOL_VERSION, "unknown", {"Battle", "Entity"}, kFormulaEvents},
                          kFormulaEvents, [this](const Frame& f, std::string* e) { return HandleFormula(f, e); });
    classifiers_.Register({"decoded-protocol", GOD2_TOOL_VERSION, "7", GOD2_TOOL_VERSION, "current-build",
                           {"Packet", "Battle"}, kDecodedProtocolEvents},
                          kDecodedProtocolEvents,
                          [this](const Frame& f, std::string* e) { return HandleDecodedProtocol(f, e); });
}

bool GameplayAnalysisEngine::ProcessJsonLine(std::string_view json, bool persist_raw, std::string* error) {
    Frame frame;
    if (!parsers_.Parse(json, frame, error)) return false;
    return ProcessFrame(frame, persist_raw, error);
}

bool GameplayAnalysisEngine::ProcessFrame(const Frame& frame, bool persist_raw, std::string* error) {
    if (persist_raw && !store_.PersistRawFrame(frame, error)) return false;
    // Transport packets are retained as source evidence.  Their decoded protocol
    // events are classified separately, so replaying frames.jsonl must not count
    // the packet envelope as an additional unknown gameplay frame.
    if (frame.message_type.empty() && frame.opcode.empty() &&
        !GetString(frame.fields, "Transport").empty()) {
        if (!persist_raw) return true;
        ++statistics_.frames;
        if (!HandleUnknown(frame, error)) return false;
        return store_.WriteJsonl(L"raw/unknown-frames.jsonl", frame.original_json, error);
    }
    ++statistics_.frames;
    bool processed = false;
    if (!frame.message_type.empty() && classifiers_.Contains(frame.message_type)) {
        processed = classifiers_.Dispatch(frame, error);
    } else {
        processed = HandleUnknown(frame, error);
        if (processed && persist_raw) processed = store_.WriteJsonl(L"raw/unknown-frames.jsonl", frame.original_json, error);
    }
    if (!processed) return false;
    // A handler-decoded record can be promoted to a semantic event (for
    // example 0x83 -> SkillDamage) while still needing its exact protocol
    // bytes/offset candidates retained. The classifier registry has one
    // primary handler per event, so explicitly persist the protocol layer for
    // promoted events instead of silently losing that half of the evidence.
    const auto capture_stage = GetString(frame.fields, "CaptureStage");
    const bool decoded_capture = capture_stage == "PostDecrypt" ||
        capture_stage == "PreEncrypt" || capture_stage == "HandlerDecoded";
    if (decoded_capture && !ContainsEvent(kDecodedProtocolEvents, frame.message_type) &&
        !HandleDecodedProtocol(frame, error)) return false;
    if (!ContainsEvent(kFormulaEvents, frame.message_type) && HasFormulaEvidence(frame) &&
        !HandleFormula(frame, error)) return false;
    const bool primary_quest_event = std::find(kQuestEvents.begin(), kQuestEvents.end(), frame.message_type) != kQuestEvents.end();
    if (!primary_quest_event && !GetString(frame.fields, "QuestId").empty()) {
        if (!HandleQuestRelationship(frame, error)) return false;
    }
    return UpdateGameplayCoverage(frame, error);
}

bool GameplayAnalysisEngine::PersistRawEvidence(const Frame& frame, std::string* error) {
    return store_.PersistRawFrame(frame, error);
}

void GameplayAnalysisEngine::RememberKnownSkill(const std::string& skill_id) {
    if (skill_id.empty() || known_skill_ids_.contains(skill_id)) return;
    if (known_skill_ids_.size() >= kMaximumKnownSkills) {
        known_skill_ids_.erase(known_skill_order_.front());
        known_skill_order_.pop_front();
    }
    known_skill_ids_.insert(skill_id);
    known_skill_order_.push_back(skill_id);
}

bool GameplayAnalysisEngine::AnalyzeJsonl(const fs::path& input, bool persist_raw, std::string* error) {
    std::ifstream stream(input, std::ios::binary);
    if (!stream) { if (error) *error = "cannot open input JSONL"; return false; }
    std::string line;
    while (std::getline(stream, line)) {
        if (line.empty()) continue;
        if (!ProcessJsonLine(line, persist_raw, error)) return false;
    }
    return true;
}

bool GameplayAnalysisEngine::HandleMovement(const Frame& frame, std::string* error) {
    ++statistics_.movement;
    const auto direction = formulas_.AnalyzeDirection(frame);
    Fields values = Common(frame, direction.evidence);
    values["event_type"] = frame.message_type;
    Copy(values, frame, "entity_id", "EntityId"); Copy(values, frame, "character_id", "CharacterId");
    Copy(values, frame, "map_id", "MapId"); Copy(values, frame, "start_x", "StartX");
    Copy(values, frame, "start_y", "StartY"); Copy(values, frame, "start_z", "StartZ");
    Copy(values, frame, "end_x", "EndX"); Copy(values, frame, "end_y", "EndY"); Copy(values, frame, "end_z", "EndZ");
    Copy(values, frame, "direction_raw", "DirectionRaw"); Copy(values, frame, "facing_raw", "FacingRaw");
    Copy(values, frame, "facing_normalized", "FacingNormalized"); Copy(values, frame, "movement_sequence", "MovementSequence");
    Copy(values, frame, "client_timestamp", "ClientTimestamp"); Copy(values, frame, "server_timestamp", "ServerTimestamp");
    Copy(values, frame, "path_point_count", "PathPointCount");
    values["direction_normalized"] = ToString(direction.direction);
    values["direction_source"] = direction.source;
    values["server_correction"] = frame.message_type == "ServerPositionCorrection" ? "true" :
        GetString(frame.fields, "ServerCorrection", "false");
    if (frame.message_type == "Teleport") values["movement_mode"] = "Teleport";
    else if (frame.message_type == "ServerPositionCorrection") values["movement_mode"] = "ServerCorrection";
    else if (frame.message_type == "ForcedMovement" || frame.message_type == "KnockbackMovement") values["movement_mode"] = "Forced";
    else if (frame.message_type == "BattlePositionChanged") values["movement_mode"] = "Battle";
    else values["movement_mode"] = GetString(frame.fields, "MovementMode", "Walking");
    if (!store_.WriteObservation("movement_observations", L"gameplay/movement.jsonl", values, error)) return false;

    Fields direction_values = Common(frame, direction.evidence);
    direction_values["movement_observation_id"] = "";
    direction_values["direction_raw"] = GetString(frame.fields, "DirectionRaw");
    direction_values["direction_normalized"] = ToString(direction.direction);
    direction_values["direction_source"] = direction.source;
    direction_values["movement_mode"] = values["movement_mode"];
    return store_.WriteObservation("movement_direction_observations", L"gameplay/movement-directions.jsonl",
                                   direction_values, error);
}

bool GameplayAnalysisEngine::HandleMount(const Frame& frame, std::string* error) {
    ++statistics_.mount;
    const auto evidence = FrameEvidence(frame);
    Fields values = Common(frame, evidence);
    values["event_type"] = frame.message_type;
    Copy(values, frame, "character_id", "CharacterId"); Copy(values, frame, "mount_runtime_id", "MountRuntimeId");
    Copy(values, frame, "mount_template_id", "MountTemplateId"); Copy(values, frame, "mount_variant_id", "MountVariantId");
    Copy(values, frame, "mount_name", "MountName"); Copy(values, frame, "mount_level", "MountLevel");
    Copy(values, frame, "mount_state", "MountState"); Copy(values, frame, "mount_start_utc", "MountStartUtc");
    Copy(values, frame, "dismount_utc", "DismountUtc"); Copy(values, frame, "map_id", "MapId");
    Copy(values, frame, "x", "X"); Copy(values, frame, "y", "Y"); Copy(values, frame, "z", "Z");
    Copy(values, frame, "direction", "DirectionNormalized"); Copy(values, frame, "movement_sequence", "MovementSequence");
    Copy(values, frame, "mount_movement_speed", "MountMovementSpeed");
    Copy(values, frame, "character_stat_speed", "CharacterStatSpeed");
    Copy(values, frame, "normal_movement_speed", "NormalMovementSpeed");
    Copy(values, frame, "mount_buff_ids", "MountBuffIds"); Copy(values, frame, "mount_skill_ids", "MountSkillIds");
    if (frame.message_type == "MountStarted" || frame.message_type.rfind("MountedMovement", 0) == 0) values["is_mounted"] = "true";
    else if (frame.message_type == "Dismounted") values["is_mounted"] = "false";
    else values["is_mounted"] = GetString(frame.fields, "IsMounted", "false");
    if (!store_.WriteObservation("mount_state_observations", L"gameplay/mount.jsonl", values, error)) return false;

    if (frame.message_type == "MountList" || frame.message_type == "MountAcquire" ||
        frame.message_type == "MountAppearanceChanged" || frame.message_type == "MountLevelChanged") {
        Fields profile = Common(frame, evidence);
        Copy(profile, frame, "mount_runtime_id", "MountRuntimeId"); Copy(profile, frame, "mount_template_id", "MountTemplateId");
        Copy(profile, frame, "mount_variant_id", "MountVariantId"); Copy(profile, frame, "mount_name", "MountName");
        Copy(profile, frame, "mount_level", "MountLevel"); Copy(profile, frame, "mount_skill_ids", "MountSkillIds");
        Copy(profile, frame, "mount_buff_ids", "MountBuffIds");
        if (!store_.WriteObservation("mount_profiles", L"gameplay/mount.jsonl", profile, error)) return false;
    }
    if (frame.message_type.rfind("MountedMovement", 0) == 0) {
        const auto direction = ResolveDirection(frame);
        values["direction"] = ToString(direction.direction);
        Fields movement = Common(frame, direction.evidence);
        Copy(movement, frame, "character_id", "CharacterId"); Copy(movement, frame, "mount_runtime_id", "MountRuntimeId");
        Copy(movement, frame, "map_id", "MapId"); Copy(movement, frame, "start_x", "StartX");
        Copy(movement, frame, "start_y", "StartY"); Copy(movement, frame, "start_z", "StartZ");
        Copy(movement, frame, "end_x", "EndX"); Copy(movement, frame, "end_y", "EndY"); Copy(movement, frame, "end_z", "EndZ");
        Copy(movement, frame, "direction_raw", "DirectionRaw"); Copy(movement, frame, "movement_sequence", "MovementSequence");
        Copy(movement, frame, "mount_movement_speed", "MountMovementSpeed");
        Copy(movement, frame, "character_stat_speed", "CharacterStatSpeed");
        Copy(movement, frame, "normal_movement_speed", "NormalMovementSpeed");
        movement["direction_normalized"] = ToString(direction.direction);
        movement["movement_mode"] = "Mounted";
        return store_.WriteObservation("mounted_movement_observations", L"gameplay/mounted-movement.jsonl", movement, error);
    }
    return true;
}

bool GameplayAnalysisEngine::HandleQuest(const Frame& frame, std::string* error) {
    ++statistics_.quest;
    const auto evidence = FrameEvidence(frame);
    const auto quest_id = GetString(frame.fields, "QuestId");
    const auto correlation = correlators_.Resolve(QuestCorrelationKey(frame), GetString(frame.fields, "QuestCorrelationId"));
    const auto action_key = QuestActionCorrelationKey(frame);
    const auto transmitted_action = GetString(frame.fields, "QuestActionCorrelationId");
    const auto action = StartsQuestAction(frame.message_type) ?
        correlators_.Begin(action_key, transmitted_action) : correlators_.Resolve(action_key, transmitted_action);
    Fields observation = Common(frame, evidence);
    observation["event_type"] = frame.message_type; observation["quest_id"] = quest_id;
    Copy(observation, frame, "quest_stage_id", "QuestStageId"); observation["quest_correlation_id"] = correlation;
    observation["quest_action_correlation_id"] = action; Copy(observation, frame, "state", "QuestState");
    Copy(observation, frame, "failure_reason_code", "FailureReasonCode");
    Copy(observation, frame, "related_entity_type", "RelatedEntityType"); Copy(observation, frame, "related_entity_id", "RelatedEntityId");
    if (!store_.WriteObservation("quest_observations", L"gameplay/quests.jsonl", observation, error)) return false;
    const auto stage_id = GetString(frame.fields, "QuestStageId");
    if (!quest_id.empty() && !stage_id.empty()) {
        Fields stage = Common(frame, evidence);
        stage["quest_id"] = quest_id; stage["quest_stage_id"] = stage_id;
        stage["stage_state"] = GetString(frame.fields, "QuestStageState", GetString(frame.fields, "QuestState"));
        if (!store_.WriteObservation("quest_stages", L"gameplay/quests.jsonl", stage, error)) return false;
    }

    if (frame.message_type == "QuestListSnapshot" || frame.message_type == "QuestAvailable" ||
        frame.message_type == "QuestDetailsResponse" || frame.message_type == "QuestStateRefresh") {
        Fields profile = Common(frame, evidence);
        profile["quest_id"] = quest_id;
        auto quest_type = GetString(frame.fields, "QuestType", "UnknownQuestType");
        if (!QuestTypes().contains(quest_type)) quest_type = "UnknownQuestType";
        profile["quest_type"] = quest_type; Copy(profile, frame, "quest_name", "QuestName");
        profile["definition_status"] = GetString(frame.fields, "DefinitionStatus", "Transmitted");
        Copy(profile, frame, "required_level", "RequiredLevel"); Copy(profile, frame, "maximum_level", "MaximumLevel");
        Copy(profile, frame, "required_class", "RequiredClass"); Copy(profile, frame, "required_quest_ids", "RequiredQuestIds");
        Copy(profile, frame, "required_item_ids", "RequiredItemIds"); Copy(profile, frame, "required_party_state", "RequiredPartyState");
        Copy(profile, frame, "required_reputation", "RequiredReputation"); Copy(profile, frame, "required_map", "RequiredMap");
        Copy(profile, frame, "repeat_interval", "RepeatInterval"); Copy(profile, frame, "daily_reset_rule", "DailyResetRule");
        if (!store_.WriteObservation("quest_profiles", L"gameplay/quests.jsonl", profile, error)) return false;
    }

    if (frame.message_type.find("Objective") != std::string::npos || frame.message_type.find("Progress") != std::string::npos) {
        Fields objective = Common(frame, evidence);
        objective["quest_id"] = quest_id; Copy(objective, frame, "quest_stage_id", "QuestStageId");
        Copy(objective, frame, "objective_id", "ObjectiveId");
        auto objective_types = SplitList(GetString(frame.fields, "ObjectiveType", "UnknownObjective"));
        if (objective_types.empty()) objective_types.push_back("UnknownObjective");
        const std::pair<const char*, const char*> copies[] = {
            {"target_template_id","TargetTemplateId"},{"target_entity_id","TargetEntityId"},{"target_npc_id","TargetNpcId"},
            {"target_monster_id","TargetMonsterId"},{"target_item_id","TargetItemId"},{"target_map_id","TargetMapId"},
            {"target_x","TargetX"},{"target_y","TargetY"},{"target_z","TargetZ"},{"required_count","RequiredCount"},
            {"current_count","CurrentCount"},{"previous_count","PreviousCount"},{"completed","Completed"},
            {"optional","Optional"},{"shared_with_party","SharedWithParty"},{"time_limit_seconds","TimeLimitSeconds"}
        };
        for (const auto& [out, in] : copies) Copy(objective, frame, out, in);
        for (auto type : objective_types) {
            if (!QuestObjectiveTypes().contains(type)) type = "UnknownObjective";
            objective["objective_type"] = type;
            if (!store_.WriteObservation("quest_objectives", L"gameplay/quest-progress.jsonl", objective, error)) return false;
        }

        Fields progress = Common(frame, evidence);
        progress["quest_id"] = quest_id; Copy(progress, frame, "quest_stage_id", "QuestStageId");
        Copy(progress, frame, "objective_id", "ObjectiveId"); Copy(progress, frame, "previous_count", "PreviousCount");
        Copy(progress, frame, "current_count", "CurrentCount"); Copy(progress, frame, "required_count", "RequiredCount");
        Copy(progress, frame, "completed", "Completed"); progress["quest_correlation_id"] = correlation;
        progress["quest_action_correlation_id"] = action;
        if (!store_.WriteObservation("quest_progress_observations", L"gameplay/quest-progress.jsonl", progress, error)) return false;
    }

    if (frame.message_type.find("Reward") != std::string::npos || frame.message_type == "QuestTurnInResult") {
        Fields reward = Common(frame, evidence);
        reward["quest_id"] = quest_id;
        const std::pair<const char*, const char*> copies[] = {
            {"experience_reward","ExperienceReward"},{"currency_rewards","CurrencyRewards"},{"item_rewards","ItemRewards"},
            {"selectable_rewards","SelectableRewards"},{"skill_rewards","SkillRewards"},{"pet_rewards","PetRewards"},
            {"immortal_rewards","ImmortalRewards"},{"reputation_rewards","ReputationRewards"},
            {"title_rewards","TitleRewards"},{"unlock_rewards","UnlockRewards"}
        };
        for (const auto& [out, in] : copies) Copy(reward, frame, out, in);
        reward["definition_status"] = GetString(frame.fields, "DefinitionStatus", "NotTransmitted");
        if (!store_.WriteObservation("quest_reward_observations", L"gameplay/quest-rewards.jsonl", reward, error)) return false;
    }

    const auto related_type = GetString(frame.fields, "RelatedEntityType");
    if (!related_type.empty()) {
        Fields relation = Common(frame, evidence);
        relation["quest_id"] = quest_id; relation["quest_correlation_id"] = correlation;
        relation["quest_action_correlation_id"] = action; relation["related_event_type"] = frame.message_type;
        relation["related_entity_type"] = related_type; relation["related_entity_id"] = GetString(frame.fields, "RelatedEntityId");
        relation["relationship"] = GetString(frame.fields, "Relationship", "ObservedWithQuestAction");
        if (!store_.WriteObservation("quest_correlations", L"gameplay/quests.jsonl", relation, error)) return false;
    }
    if (EndsQuestAction(frame.message_type)) correlators_.End(action_key);
    return true;
}

bool GameplayAnalysisEngine::HandleQuestRelationship(const Frame& frame, std::string* error) {
    ++statistics_.quest;
    const auto evidence = FrameEvidence(frame);
    const auto quest_id = GetString(frame.fields, "QuestId");
    if (quest_id.empty()) return true;
    const auto quest_correlation = correlators_.Resolve(QuestCorrelationKey(frame),
                                                        GetString(frame.fields, "QuestCorrelationId"));
    Frame progress_key_frame = frame;
    progress_key_frame.message_type = "QuestProgressUpdate";
    const auto action_correlation = correlators_.Resolve(
        QuestActionCorrelationKey(progress_key_frame), GetString(frame.fields, "QuestActionCorrelationId"));
    const auto entity_type = RelatedEntityType(frame);
    Fields relation = Common(frame, evidence);
    relation["quest_id"] = quest_id;
    relation["quest_correlation_id"] = quest_correlation;
    relation["quest_action_correlation_id"] = action_correlation;
    relation["related_event_type"] = frame.message_type;
    relation["related_entity_type"] = entity_type;
    relation["related_entity_id"] = RelatedEntityId(frame, entity_type);
    relation["relationship"] = GetString(frame.fields, "Relationship", "ObservedInQuestAction");
    return store_.WriteObservation("quest_correlations", L"gameplay/quests.jsonl", relation, error);
}

bool GameplayAnalysisEngine::HandleSkill(const Frame& frame, std::string* error) {
    ++statistics_.skill;
    const auto evidence = FrameEvidence(frame);
    const auto skill_id = GetString(frame.fields, "SkillId");
    const auto explicitly_unknown = GetBool(frame.fields, "UnknownSkill");
    const bool profile_snapshot = frame.message_type == "SkillAvailable" ||
        GetBool(frame.fields, "ProfileSnapshot").value_or(false);
    const auto definition_status = GetString(frame.fields, "DefinitionStatus", "Transmitted");
    const bool transmitted_definition = profile_snapshot && definition_status != "NotTransmitted" &&
        (!GetString(frame.fields, "SkillName").empty() || !GetString(frame.fields, "FacetIds").empty() ||
         !GetString(frame.fields, "EffectIds").empty());
    if (!skill_id.empty() && explicitly_unknown != true &&
        (explicitly_unknown == false || transmitted_definition)) RememberKnownSkill(skill_id);
    const bool previously_known = !skill_id.empty() &&
        store_.Db().ScalarInt64("SELECT COUNT(*) FROM skill_profiles WHERE skill_id=" + SqlQuote(skill_id) +
                                " AND unknown_skill='false';") != 0;
    const bool unknown_skill = explicitly_unknown.value_or(
        skill_id.empty() || (!known_skill_ids_.contains(skill_id) && !previously_known));
    const auto facets = ValidFacets(frame, unknown_skill);
    if (profile_snapshot) {
        Fields profile = Common(frame, unknown_skill ? EvidenceLevel::Unknown : evidence);
        profile["skill_id"] = skill_id; Copy(profile, frame, "skill_level", "SkillLevel");
        Copy(profile, frame, "skill_name", "SkillName");
        auto skill_source = GetString(frame.fields, "SkillSourceType", "UnknownSource");
        const auto source_facet = SkillFacetCatalog().find(skill_source);
        if (source_facet == SkillFacetCatalog().end() || source_facet->second != "Source") skill_source = "UnknownSource";
        profile["skill_source_type"] = skill_source;
        const std::pair<const char*, const char*> copies[] = {
            {"element","Element"},{"activation_type","ActivationType"},{"targeting_type","TargetingType"},
            {"effect_types","EffectTypes"},{"control_types","ControlTypes"},{"duration_type","DurationType"},
            {"combat_role","CombatRole"},{"mp_cost","MpCost"},{"hp_cost","HpCost"},
            {"other_resource_cost","OtherResourceCost"},{"cast_time","CastTime"},{"cooldown","Cooldown"},
            {"range_value","Range"},{"area_radius","AreaRadius"},{"maximum_targets","MaximumTargets"},
            {"hit_count","HitCount"},{"effect_ids","EffectIds"},{"animation_ids","AnimationIds"},
            {"status_effect_ids","StatusEffectIds"},{"candidate_facets","CandidateFacets"},
            {"candidate_confidence","CandidateConfidence"}
        };
        for (const auto& [out, in] : copies) Copy(profile, frame, out, in);
        profile["unknown_skill"] = unknown_skill ? "true" : "false";
        if (!store_.WriteObservation("skill_profiles", L"gameplay/skills.jsonl", profile, error)) return false;
        for (const auto& facet : facets) {
            Fields facet_row = Common(frame, evidence);
            facet_row["skill_id"] = skill_id; facet_row["facet_id"] = facet;
            facet_row["facet_dimension"] = SkillFacetCatalog().at(facet);
            if (!store_.WriteObservation("skill_profile_facets", L"gameplay/skills.jsonl", facet_row, error)) return false;
        }
    }

    const auto correlation_key = SkillCorrelationKey(frame);
    const auto transmitted_correlation = GetString(frame.fields, "SkillCastCorrelationId");
    std::string correlation;
    if (frame.message_type == "SkillCastRequest") {
        correlation = correlators_.Begin(correlation_key, transmitted_correlation);
    } else if (frame.message_type == "SkillCastStart" && !correlators_.IsActive(correlation_key)) {
        correlation = correlators_.Begin(correlation_key, transmitted_correlation);
    } else {
        correlation = correlators_.Resolve(correlation_key, transmitted_correlation);
    }
    Fields cast = Common(frame, unknown_skill ? EvidenceLevel::Unknown : evidence);
    cast["skill_cast_correlation_id"] = correlation; cast["event_type"] = frame.message_type;
    cast["skill_id"] = skill_id; Copy(cast, frame, "caster_entity_id", "CasterEntityId");
    Copy(cast, frame, "target_entity_ids", "TargetEntityIds"); Copy(cast, frame, "parent_correlation_id", "ParentCorrelationId");
    Copy(cast, frame, "failure_reason_code", "FailureReasonCode");
    if (!store_.WriteObservation("skill_cast_correlations", L"gameplay/skill-casts.jsonl", cast, error)) return false;

    const auto targets = SplitList(GetString(frame.fields, "TargetEntityIds", GetString(frame.fields, "TargetEntityId")));
    if (frame.message_type == "SkillTargetSelected" || frame.message_type == "SkillHit" ||
        frame.message_type == "SkillMiss" || frame.message_type == "SkillCritical") {
        for (std::size_t i = 0; i < targets.size(); ++i) {
            Fields target = Common(frame, evidence);
            target["skill_cast_correlation_id"] = correlation; target["skill_id"] = skill_id;
            target["target_entity_id"] = targets[i]; target["target_index"] = std::to_string(i);
            target["hit_result"] = frame.message_type;
            if (!store_.WriteObservation("skill_target_observations", L"gameplay/skill-casts.jsonl", target, error)) return false;
        }
    }
    const bool effect_event = frame.message_type.find("Damage") != std::string::npos ||
        frame.message_type.find("Healing") != std::string::npos || frame.message_type.find("Buff") != std::string::npos ||
        frame.message_type.find("Debuff") != std::string::npos || frame.message_type.find("Effect") != std::string::npos;
    if (effect_event) {
        Fields effect = Common(frame, evidence);
        effect["skill_cast_correlation_id"] = correlation; effect["skill_id"] = skill_id;
        Copy(effect, frame, "target_entity_id", "TargetEntityId"); effect["effect_type"] = GetString(frame.fields, "EffectType", frame.message_type);
        Copy(effect, frame, "effect_id", "EffectId"); Copy(effect, frame, "amount", "Amount");
        Copy(effect, frame, "tick_index", "TickIndex"); Copy(effect, frame, "duration_ms", "DurationMs");
        Copy(effect, frame, "status_effect_id", "StatusEffectId");
        if (!store_.WriteObservation("skill_effect_observations", L"gameplay/skill-effects.jsonl", effect, error)) return false;
    }
    if (frame.message_type == "SkillControlApplied") {
        Fields control = Common(frame, evidence);
        control["skill_cast_correlation_id"] = correlation; control["skill_id"] = skill_id;
        Copy(control, frame, "target_entity_id", "TargetEntityId"); Copy(control, frame, "control_type", "ControlType");
        Copy(control, frame, "duration_ms", "DurationMs"); Copy(control, frame, "resisted", "Resisted");
        if (!store_.WriteObservation("skill_control_observations", L"gameplay/skill-effects.jsonl", control, error)) return false;
    }
    if (frame.message_type == "SkillCastCompleted" || frame.message_type == "SkillCastRejected" ||
        frame.message_type == "SkillCastInterrupted") correlators_.End(correlation_key);
    return true;
}

bool GameplayAnalysisEngine::HandleConsumable(const Frame& frame, std::string* error) {
    ++statistics_.consumable;
    const auto evidence = FrameEvidence(frame);
    const auto correlation_key = ConsumableCorrelationKey(frame);
    const auto transmitted_correlation = GetString(frame.fields, "ItemUseCorrelationId");
    const auto correlation = frame.message_type == "ItemUseRequest" ?
        correlators_.Begin(correlation_key, transmitted_correlation) :
        correlators_.Resolve(correlation_key, transmitted_correlation);
    if (frame.message_type == "ItemUseRequest" || GetBool(frame.fields, "ProfileSnapshot").value_or(false)) {
        Fields profile = Common(frame, evidence);
        Copy(profile, frame, "item_id", "ItemId"); Copy(profile, frame, "item_name", "ItemName");
        auto type = GetString(frame.fields, "ConsumableType", "UnknownConsumable");
        if (!ConsumableTypes().contains(type)) type = "UnknownConsumable";
        profile["consumable_type"] = type; profile["is_item_skill"] = GetString(frame.fields, "IsItemSkill", "false");
        Copy(profile, frame, "shared_cooldown_group", "SharedCooldownGroup");
        if (!store_.WriteObservation("consumable_profiles", L"gameplay/consumables.jsonl", profile, error)) return false;
    }
    Fields use = Common(frame, evidence);
    use["event_type"] = frame.message_type; use["item_use_correlation_id"] = correlation;
    const std::pair<const char*, const char*> copies[] = {
        {"character_id","CharacterId"},{"item_id","ItemId"},{"quantity_before","QuantityBefore"},
        {"quantity_after","QuantityAfter"},{"hp_before","HpBefore"},{"hp_after","HpAfter"},
        {"mp_before","MpBefore"},{"mp_after","MpAfter"},{"buff_ids","BuffIds"},
        {"quest_id","QuestId"},{"failure_reason_code","FailureReasonCode"}
    };
    for (const auto& [out, in] : copies) Copy(use, frame, out, in);
    if (!store_.WriteObservation("consumable_use_observations", L"gameplay/consumables.jsonl", use, error)) return false;
    if (frame.message_type == "ItemUseResult") correlators_.End(correlation_key);
    return true;
}

bool GameplayAnalysisEngine::HandleEntity(const Frame& frame, std::string* error) {
    ++statistics_.entity;
    const auto evidence = FrameEvidence(frame);
    const auto entity_type = EntityTypeForFrame(frame);
    const auto entity_id = EntityIdForFrame(frame, entity_type);
    Fields observation = Common(frame, evidence);
    observation["event_type"] = frame.message_type;
    observation["entity_type"] = entity_type;
    observation["entity_id"] = entity_id;
    const std::pair<const char*, const char*> copies[] = {
        {"template_id","TemplateId"},{"map_id","MapId"},{"x","X"},{"y","Y"},{"z","Z"},
        {"level","Level"},{"hp","Hp"},{"max_hp","MaxHp"},{"mp","Mp"},{"max_mp","MaxMp"},
        {"strength","Strength"},{"stamina","Stamina"},{"intelligence","Intelligence"},
        {"character_stat_speed","CharacterStatSpeed"},{"element","Element"}
    };
    for (const auto& [out, in] : copies) Copy(observation, frame, out, in);
    if (!store_.WriteObservation("entity_observations", L"gameplay/entities.jsonl", observation, error)) return false;
    if (frame.message_type.find("Spawn") != std::string::npos || frame.message_type.find("Snapshot") != std::string::npos) {
        Fields profile = Common(frame, evidence);
        profile["entity_type"] = entity_type;
        profile["entity_id"] = entity_id;
        Copy(profile, frame, "template_id", "TemplateId");
        Copy(profile, frame, "entity_name", "EntityName");
        if (profile["entity_name"].empty()) profile["entity_name"] = GetString(frame.fields, entity_type + "Name");
        Copy(profile, frame, "level", "Level");
        Copy(profile, frame, "element", "Element");
        return store_.WriteObservation("entity_profiles", L"gameplay/entities.jsonl", profile, error);
    }
    return true;
}

bool GameplayAnalysisEngine::HandleCharacterLifecycle(const Frame& frame, std::string* error) {
    ++statistics_.character_lifecycle;
    Fields observation = Common(frame, FrameEvidence(frame));
    observation["event_type"] = frame.message_type;
    const std::pair<const char*, const char*> copies[] = {
        {"account_id","AccountId"},{"character_id","CharacterId"},{"character_name","CharacterName"},
        {"slot_id","SlotId"},{"class_id","ClassId"},{"level","Level"},{"result","Result"},
        {"failure_reason_code","FailureReasonCode"}
    };
    for (const auto& [out, in] : copies) Copy(observation, frame, out, in);
    return store_.WriteObservation("character_lifecycle_observations", L"gameplay/character-lifecycle.jsonl",
                                   observation, error);
}

bool GameplayAnalysisEngine::HandleParty(const Frame& frame, std::string* error) {
    ++statistics_.party;
    const auto party_id = GetString(frame.fields, "PartyId", "unknown");
    const auto character_id = GetString(frame.fields, "CharacterId", "unknown");
    const std::string key = "party:" + party_id + ":" + character_id;
    const auto transmitted = GetString(frame.fields, "PartyCorrelationId");
    const bool begins = frame.message_type == "PartyCreateRequest" || frame.message_type == "PartyInviteRequest" ||
                        frame.message_type == "PartyJoinRequest";
    const auto correlation = begins ? correlators_.Begin(key, transmitted) : correlators_.Resolve(key, transmitted);
    Fields observation = Common(frame, FrameEvidence(frame));
    observation["event_type"] = frame.message_type;
    observation["party_correlation_id"] = correlation;
    observation["party_id"] = GetString(frame.fields, "PartyId");
    Copy(observation, frame, "leader_entity_id", "LeaderEntityId");
    Copy(observation, frame, "member_count", "MemberCount");
    Copy(observation, frame, "state", "PartyState");
    Copy(observation, frame, "failure_reason_code", "FailureReasonCode");
    if (!store_.WriteObservation("party_observations", L"gameplay/party.jsonl", observation, error)) return false;
    const auto member_id = GetString(frame.fields, "MemberEntityId");
    if (!member_id.empty() || frame.message_type.find("Member") != std::string::npos) {
        Fields member = Common(frame, FrameEvidence(frame));
        member["party_correlation_id"] = correlation;
        member["party_id"] = GetString(frame.fields, "PartyId");
        member["member_entity_id"] = member_id;
        Copy(member, frame, "member_name", "MemberName");
        Copy(member, frame, "member_level", "MemberLevel");
        Copy(member, frame, "member_role", "MemberRole");
        Copy(member, frame, "online", "Online");
        if (!store_.WriteObservation("party_member_observations", L"gameplay/party.jsonl", member, error)) return false;
    }
    if (frame.message_type == "PartyLeft" || frame.message_type == "PartyDisbanded" ||
        frame.message_type == "PartyInviteRejected") correlators_.End(key);
    return true;
}

bool GameplayAnalysisEngine::HandleEconomy(const Frame& frame, std::string* error) {
    ++statistics_.economy;
    const std::string key = "economy:" + GetString(frame.fields, "CharacterId", "unknown") + ":" +
                            GetString(frame.fields, "ShopId", "unknown") + ":" +
                            GetString(frame.fields, "ItemId", "unknown");
    const auto transmitted = GetString(frame.fields, "EconomyCorrelationId");
    const bool begins = frame.message_type.find("Request") != std::string::npos;
    const auto correlation = begins ? correlators_.Begin(key, transmitted) : correlators_.Resolve(key, transmitted);
    Fields observation = Common(frame, FrameEvidence(frame));
    observation["event_type"] = frame.message_type;
    observation["economy_correlation_id"] = correlation;
    const std::pair<const char*, const char*> copies[] = {
        {"character_id","CharacterId"},{"shop_id","ShopId"},{"item_id","ItemId"},{"quantity","Quantity"},
        {"unit_price","UnitPrice"},{"total_price","TotalPrice"},{"currency_type","CurrencyType"},
        {"currency_before","CurrencyBefore"},{"currency_after","CurrencyAfter"},
        {"durability_before","DurabilityBefore"},{"durability_after","DurabilityAfter"},
        {"result","Result"},{"failure_reason_code","FailureReasonCode"}
    };
    for (const auto& [out, in] : copies) Copy(observation, frame, out, in);
    if (!store_.WriteObservation("economy_observations", L"gameplay/economy.jsonl", observation, error)) return false;
    if (frame.message_type.find("Result") != std::string::npos) correlators_.End(key);
    return true;
}

bool GameplayAnalysisEngine::HandleProgression(const Frame& frame, std::string* error) {
    ++statistics_.progression;
    const auto entity_type = EntityTypeForFrame(frame);
    Fields observation = Common(frame, FrameEvidence(frame));
    observation["event_type"] = frame.message_type;
    observation["entity_type"] = entity_type;
    observation["entity_id"] = EntityIdForFrame(frame, entity_type);
    const std::pair<const char*, const char*> copies[] = {
        {"previous_level","PreviousLevel"},{"current_level","CurrentLevel"},
        {"previous_experience","PreviousExperience"},{"current_experience","CurrentExperience"},
        {"required_experience","RequiredExperience"}
    };
    for (const auto& [out, in] : copies) Copy(observation, frame, out, in);
    return store_.WriteObservation("progression_observations", L"gameplay/progression.jsonl", observation, error);
}

bool GameplayAnalysisEngine::HandleFormula(const Frame& frame, std::string* error) {
    ++statistics_.formula;
    const auto evidence = FrameEvidence(frame, EvidenceLevel::Candidate);
    std::string formula_type = GetString(frame.fields, "FormulaType");
    if (formula_type.empty()) {
        if (frame.message_type.find("Healing") != std::string::npos) formula_type = "Healing";
        else if (frame.message_type.find("Damage") != std::string::npos) formula_type = "Damage";
        else if (frame.message_type.find("Critical") != std::string::npos) formula_type = "Critical";
        else if (frame.message_type.find("Dodge") != std::string::npos || frame.message_type == "SkillMiss") formula_type = "Dodge";
        else if (frame.message_type.find("Hit") != std::string::npos) formula_type = "Hit";
        else if (frame.message_type.find("Element") != std::string::npos) formula_type = "Element";
        else if (frame.message_type.find("Order") != std::string::npos) formula_type = "SpeedOrder";
        else if (frame.message_type.find("Level") != std::string::npos) formula_type = "LevelProgression";
        else formula_type = "Damage";
    }
    if (!ContainsEvent(kFormulaTypes, formula_type)) formula_type = "Damage";
    const std::string verification = evidence == EvidenceLevel::Verified ? "Verified" :
        evidence == EvidenceLevel::Derived ? "Derived" : evidence == EvidenceLevel::Unknown ? "EvidenceBlocked" : "Candidate";
    Fields candidate = Common(frame, evidence);
    candidate["formula_type"] = formula_type;
    candidate["candidate_expression"] = GetString(frame.fields, "CandidateExpression");
    candidate["input_values"] = GetString(frame.fields, "InputValues");
    candidate["observed_result"] = GetString(frame.fields, "ObservedResult",
                                               GetString(frame.fields, "Amount", GetString(frame.fields, "Result")));
    candidate["sample_count"] = GetString(frame.fields, "SampleCount", "1");
    candidate["confidence"] = GetString(frame.fields, "CandidateConfidence");
    candidate["verification_status"] = verification;
    if (!store_.WriteObservation("formula_candidates", L"gameplay/formulas.jsonl", candidate, error)) return false;
    return store_.Db().Execute(
        "INSERT INTO formula_coverage(session_id,analysis_run_id,formula_type,coverage_status,observed_count,last_source_frame_ids,evidence_level) VALUES(" +
        SqlQuote(store_.SessionId()) + "," + SqlQuote(store_.AnalysisRunId()) + "," + SqlQuote(formula_type) + "," +
        SqlQuote(verification) + ",1," + SqlQuote(frame.source_frame_ids.empty() ? frame.frame_id : frame.source_frame_ids) + "," +
        SqlQuote(ToString(evidence)) + ") ON CONFLICT(session_id,analysis_run_id,formula_type) DO UPDATE SET "
        "coverage_status=excluded.coverage_status,observed_count=observed_count+1,last_source_frame_ids=excluded.last_source_frame_ids,evidence_level=excluded.evidence_level;",
        error);
}

bool GameplayAnalysisEngine::HandleDecodedProtocol(const Frame& frame, std::string* error) {
    ++statistics_.protocol_decoded;
    Fields observation = Common(frame, FrameEvidence(frame, EvidenceLevel::Candidate));
    observation["event_type"] = frame.message_type;
    observation["direction"] = frame.direction;
    observation["opcode"] = frame.opcode;
    observation["capture_stage"] = GetString(frame.fields, "CaptureStage", "Unknown");
    observation["transport"] = GetString(frame.fields, "Transport");
    observation["declared_frame_length"] = GetString(frame.fields, "DeclaredFrameLength");
    observation["handler_record_length"] = GetString(frame.fields, "HandlerRecordLength");
    const std::pair<const char*, const char*> copies[] = {
        {"attacker_position_candidate","AttackerPositionCandidate"},
        {"target_position_candidate","TargetPositionCandidate"},
        {"effect_kind_candidate","EffectKindCandidate"},
        {"effect_flags_candidate","EffectFlagsCandidate"},
        {"observed_signed_delta_candidate","ObservedSignedDeltaCandidate"},
        {"outcome_flags_candidate","OutcomeFlagsCandidate"},
        {"battle_position_candidate","BattlePositionCandidate"},
        {"action_code_candidate","ActionCodeCandidate"},
        {"continuation_candidate","ContinuationCandidate"},
        {"side_candidate","SideCandidate"},
        {"target_mask_0_candidate","TargetMask0Candidate"},
        {"target_mask_1_candidate","TargetMask1Candidate"},
        {"target_mask_2_candidate","TargetMask2Candidate"},
        {"battle_context_candidate","BattleContextCandidate"},
        {"action_parameter_candidate","ActionParameterCandidate"},
        {"entity_or_item_id_candidate","EntityOrItemIdCandidate"},
        {"slot_candidate","SlotCandidate"},
        {"state_candidate","StateCandidate"},
        {"client_item_id","ClientItemId"},
        {"slot_index","SlotIndex"},
        {"raw_tail","RawTail"},
        {"quantity_or_mode_candidate","QuantityOrModeCandidate"},
        {"movement_argument_candidate","MovementArgumentCandidate"},
        {"reserved_byte_candidate","ReservedByteCandidate"},
        {"character_create_marker_offset","CharacterCreateMarkerOffset"},
        {"character_create_marker_value","CharacterCreateMarkerValue"},
        {"checksum_valid","ChecksumValid"},
        {"decode_validation","DecodeValidation"}
    };
    for (const auto& [out, in] : copies) Copy(observation, frame, out, in);
    observation["semantic_status"] = GetString(frame.fields, "ProtocolSemanticStatus", "EvidenceBlocked");
    return store_.WriteObservation(
        "protocol_handler_observations",
        L"gameplay/protocol-handler-observations.jsonl",
        observation,
        error);
}

bool GameplayAnalysisEngine::UpdateGameplayCoverage(const Frame& frame, std::string* error) {
    if (frame.message_type.empty()) return true;
    const auto evidence = FrameEvidence(frame, EvidenceLevel::Unknown);
    const auto group = GameplayGroupForEvent(frame.message_type);
    const std::string status = group == "Unknown" ? "EvidenceBlocked" :
        evidence == EvidenceLevel::Verified ? "Verified" : evidence == EvidenceLevel::Derived ? "Derived" :
        evidence == EvidenceLevel::Candidate ? "Candidate" : evidence == EvidenceLevel::Unknown ? "EvidenceBlocked" : "Observed";
    return store_.Db().Execute(
        "INSERT INTO gameplay_coverage(session_id,analysis_run_id,feature_group,feature_id,coverage_status,observed_count,last_source_frame_ids,evidence_level) VALUES(" +
        SqlQuote(store_.SessionId()) + "," + SqlQuote(store_.AnalysisRunId()) + "," + SqlQuote(group) + "," +
        SqlQuote(frame.message_type) + "," + SqlQuote(status) + ",1," +
        SqlQuote(frame.source_frame_ids.empty() ? frame.frame_id : frame.source_frame_ids) + "," + SqlQuote(ToString(evidence)) +
        ") ON CONFLICT(session_id,analysis_run_id,feature_id) DO UPDATE SET coverage_status=excluded.coverage_status,"
        "observed_count=observed_count+1,last_source_frame_ids=excluded.last_source_frame_ids,evidence_level=excluded.evidence_level;",
        error);
}

bool GameplayAnalysisEngine::HandleUnknown(const Frame& frame, std::string* error) {
    ++statistics_.unknown;
    const std::string prefix = frame.payload_hex.substr(0, std::min<std::size_t>(16, frame.payload_hex.size()));
    const std::string cluster_key = !frame.opcode.empty() ? "opcode:" + frame.opcode :
        "payload:" + prefix + ":" + std::to_string(frame.payload_hex.size() / 2);
    return store_.Db().Execute(
        "INSERT INTO unknown_opcode_clusters(session_id,analysis_run_id,cluster_key,opcode,payload_prefix,payload_length,observed_count,sample_source_frame_ids,evidence_level) VALUES(" +
        SqlQuote(store_.SessionId()) + "," + SqlQuote(store_.AnalysisRunId()) + "," + SqlQuote(cluster_key) + "," +
        SqlQuote(frame.opcode) + "," + SqlQuote(prefix) + "," + std::to_string(frame.payload_hex.size() / 2) +
        ",1," + SqlQuote(frame.source_frame_ids.empty() ? frame.frame_id : frame.source_frame_ids) + ",'Unknown') "
        "ON CONFLICT(session_id,analysis_run_id,cluster_key) DO UPDATE SET observed_count=observed_count+1,"
        "sample_source_frame_ids=excluded.sample_source_frame_ids;", error);
}

const std::set<std::string>& QuestTypes() {
    static const std::set<std::string> values = {
        "MainQuest", "SideQuest", "DailyQuest", "RepeatableQuest", "ChainQuest", "ClassQuest",
        "FactionQuest", "GuildQuest", "PartyQuest", "EventQuest", "TutorialQuest", "HiddenQuest",
        "EscortQuest", "TimedQuest", "InstanceQuest", "PetQuest", "ImmortalQuest", "UnknownQuestType"
    };
    return values;
}

const std::set<std::string>& QuestObjectiveTypes() {
    static const std::set<std::string> values = {
        "KillMonster", "CollectItem", "TalkToNpc", "VisitLocation", "UseItem", "DeliverItem", "ProtectNpc",
        "EscortNpc", "DefeatBoss", "LearnSkill", "EquipItem", "ReachLevel", "JoinParty", "CompleteBattle",
        "CapturePet", "UpgradePet", "UpgradeImmortal", "UsePortal", "TriggerEvent", "WaitDuration",
        "CurrencyRequirement", "ReputationRequirement", "UnknownObjective"
    };
    return values;
}

const std::map<std::string, std::string>& SkillFacetCatalog() {
    static const std::map<std::string, std::string> values = [] {
        std::map<std::string, std::string> result;
        auto add = [&](const char* dimension, std::initializer_list<const char*> facets) {
            for (const char* facet : facets) result.emplace(facet, dimension);
        };
        add("Source", {"CharacterSkill","MonsterSkill","BattlePetSkill","PetSkill","ImmortalSkill","MountSkill",
                       "NpcSkill","SummonSkill","ItemSkill","SystemSkill","UnknownSource"});
        add("Activation", {"Active","Passive","Innate","Automatic","Reactive","Triggered","Aura","Toggle",
                           "Channeled","Charged","Combo","Counter","OnSpawn","OnHit","OnCritical","OnDamaged",
                           "OnDeath","UnknownActivation"});
        add("Effect", {"PhysicalDamage","MagicalDamage","ElementalDamage","TrueDamageCandidate","Healing","HpDrain",
                       "MpDamage","MpDrain","Shield","Buff","Debuff","Dispel","Cleanse","Control","Summon",
                       "Resurrect","Teleport","ForcedMovement","StatChange","ResourceRecovery","ResourceCost",
                       "ThreatChange","UnknownEffect","Damage","Magical"});
        add("Targeting", {"SelfTarget","SingleTarget","MultiTarget","AreaOfEffect","PartyTarget","AlliesOnly",
                          "EnemiesOnly","RandomTarget","RowTarget","ColumnTarget","ConeTarget","LineTarget",
                          "GroundTarget","GlobalTarget","NoTarget","UnknownTargeting"});
        add("Element", {"MetalElement","WoodElement","WaterElement","FireElement","EarthElement","NonElemental",
                        "MixedElement","UnknownElement"});
        add("Control", {"Stun","Sleep","Silence","Confusion","Fear","Root","Slow","Blind","Seal","Taunt",
                        "Knockback","TurnSkip","SkillLock","ItemLock","UnknownControl"});
        add("Duration", {"Instant","DamageOverTime","HealingOverTime","TimedBuff","TimedDebuff",
                         "PermanentUntilRemoved","TurnLimited","StackingEffect","PeriodicEffect","UnknownDuration"});
        add("CombatRole", {"BasicAttack","Offensive","Defensive","Support","Recovery","ControlRole","Mobility",
                           "Summoning","Utility","Finisher","OpeningSkill","DeathSkill","UnknownRole"});
        return result;
    }();
    return values;
}

const std::set<std::string>& ConsumableTypes() {
    static const std::set<std::string> values = {
        "HpMedicine", "MpMedicine", "DualResourceMedicine", "ReviveMedicine", "BuffMedicine",
        "DebuffCleanseMedicine", "ElementMedicine", "ExperienceMedicine", "PetMedicine", "ImmortalMedicine",
        "QuestItem", "TeleportItem", "BattleConsumable", "OutOfBattleConsumable", "CooldownSharedItem",
        "UnknownConsumable"
    };
    return values;
}

const std::vector<std::string>& CoverageMarkers() {
    static const std::vector<std::string> values = {
        "MARKER_LOGIN_BEGIN","MARKER_LOGIN_COMPLETE","MARKER_IDLE_BEGIN","MARKER_IDLE_END",
        "MARKER_OPEN_INVENTORY","MARKER_CLOSE_INVENTORY","MARKER_NPC_CLICK","MARKER_SHOP_OPEN",
        "MARKER_BATTLE_BEGIN","MARKER_NORMAL_ATTACK","MARKER_SKILL_CAST","MARKER_BATTLE_END",
        "MARKER_LOGOUT","MARKER_RELOGIN_BEGIN","MARKER_RELOGIN_COMPLETE",
        "向上走路","向右上走路","向右走路","向右下走路","向下走路","向左下走路","向左走路","向左上走路",
        "停止走路","連續改變方向","伺服器座標校正","坐上坐騎","坐騎向上移動","坐騎向右上移動",
        "坐騎向右移動","坐騎向右下移動","坐騎向下移動","坐騎向左下移動","坐騎向左移動","坐騎向左上移動",
        "下坐騎","取得任務列表","查看任務內容","接受主線任務","接受支線任務","接受每日任務","殺怪任務進度",
        "收集物品任務進度","與 NPC 對話任務進度","到達地點任務進度","使用道具任務進度","組隊任務進度",
        "任務目標完成","選擇任務獎勵","提交任務","放棄任務","任務失敗","任務分享","普通攻擊技能",
        "物理技能","法術技能","金系技能","木系技能","水系技能","火系技能","土系技能","單體技能","群體技能",
        "治療技能","Buff 技能","Debuff 技能","控制技能","召喚技能","被動技能觸發","持續傷害技能",
        "持續治療技能","技能施放失敗","技能被打斷","使用 HP 藥品","使用 MP 藥品","使用 Buff 藥品","使用任務道具"
    };
    return values;
}

bool IsCoverageMarker(std::string_view marker) {
    const auto& values = CoverageMarkers();
    return std::find(values.begin(), values.end(), marker) != values.end();
}

} // namespace god2
