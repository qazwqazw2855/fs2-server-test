using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum PublicBetaGameplayRequestAdapterResultCode
{
    Adapted,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidAttributeCode,
    InvalidOpcode,
    InvalidChecksum
}

public sealed record PublicBetaCompatibilityInteraction24Command(
    byte RawValue0,
    byte RawValue1,
    string DecodedFrameSha256,
    string CompatibilityEvidence,
    bool ProductionMutationAllowed);

public sealed record PublicBetaCompatibilityInteraction27Command(
    uint RawValue,
    string DecodedFrameSha256,
    string CompatibilityEvidence,
    bool ProductionMutationAllowed);

public sealed record PublicBetaCompatibilityInteraction26Command(
    ushort RawValue0,
    ushort RawValue2,
    byte RawValue4,
    byte RawTail,
    string DecodedFrameSha256,
    string CompatibilityEvidence,
    bool ProductionMutationAllowed);

public sealed record PublicBetaCompatibilityCombinationStepCommand(
    byte RawSelector,
    sbyte Step,
    string DecodedFrameSha256,
    string CompatibilityEvidence,
    bool ProductionMutationAllowed);

public sealed record PublicBetaCompatibilityRawRequestCommand(
    byte Opcode,
    ReadOnlyMemory<byte> RawPayload,
    string DecodedFrameSha256,
    string CompatibilityEvidence,
    bool ProductionMutationAllowed);

public sealed record PublicBetaGameplayRequestAdapterResult(
    PublicBetaGameplayRequestAdapterResultCode Code,
    PublicBetaCompatibilityInteraction24Command? Command,
    string FailureCode)
{
    public bool Adapted =>
        Code == PublicBetaGameplayRequestAdapterResultCode.Adapted && Command is not null;
}

public sealed record PublicBetaGameplayInteraction27AdapterResult(
    PublicBetaGameplayRequestAdapterResultCode Code,
    PublicBetaCompatibilityInteraction27Command? Command,
    string FailureCode)
{
    public bool Adapted =>
        Code == PublicBetaGameplayRequestAdapterResultCode.Adapted && Command is not null;
}

public sealed record PublicBetaGameplayInteraction26AdapterResult(
    PublicBetaGameplayRequestAdapterResultCode Code,
    PublicBetaCompatibilityInteraction26Command? Command,
    string FailureCode)
{
    public bool Adapted =>
        Code == PublicBetaGameplayRequestAdapterResultCode.Adapted && Command is not null;
}

public sealed record PublicBetaCombinationStepAdapterResult(
    PublicBetaGameplayRequestAdapterResultCode Code,
    PublicBetaCompatibilityCombinationStepCommand? Command,
    string FailureCode)
{
    public bool Adapted =>
        Code == PublicBetaGameplayRequestAdapterResultCode.Adapted && Command is not null;
}

public sealed record PublicBetaRawRequestAdapterResult(
    PublicBetaGameplayRequestAdapterResultCode Code,
    PublicBetaCompatibilityRawRequestCommand? Command,
    string FailureCode)
{
    public bool Adapted =>
        Code == PublicBetaGameplayRequestAdapterResultCode.Adapted && Command is not null;
}

public sealed record PublicBetaAttributeIncrementAdapterResult(
    PublicBetaGameplayRequestAdapterResultCode Code,
    OfficialAttributeIncrementCandidate? Command,
    string FailureCode)
{
    public bool Adapted =>
        Code == PublicBetaGameplayRequestAdapterResultCode.Adapted && Command is not null;
}

/// <summary>
/// Build-pinned adapter for the exact-current C2S 0x24 interaction record.
/// Current producer RVA 0x000AEE90 copies its two byte arguments to payload
/// offsets 0 and 1 before enqueue call RVA 0x000AEF2B. The public-beta producer
/// independently has the same opcode and two-byte raw interaction contract.
/// No mission, item, NPC or reward meaning is assigned to either byte.
/// </summary>
public static class PublicBetaCompatibilityGameplayRequestWireAdapter
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte AccountLoginOpcode = 0x04;
    public const byte GameLoginOpcode = 0x00;
    public const byte GameplayDisconnectOpcode = 0x0A;
    public const byte RouteOpcode = 0xA9;
    public const byte SocialTextEnvelopeOpcode = 0x33;
    public const byte Interaction24Opcode = 0x24;
    public const byte Interaction28Opcode = 0x28;
    public const byte AttributeIncrementOpcode = OfficialPlayerSnapshotCandidateWireCodec.AttributeIncrementOpcode;
    public const byte Interaction26Opcode = 0x26;
    public const byte Interaction27Opcode = 0x27;
    public const byte CombinationStepOpcode = 0xA4;
    public const byte CombinationCommitOpcode = 0xA5;
    public const byte MailRecordActionOpcode = 0xAE;
    public const byte PartySelectionOpcode = 0x93;
    public const byte PartyRequest95Opcode = 0x95;
    public const byte PartyRequest8FOpcode = 0x8F;
    public const byte PartyRequest90Opcode = 0x90;
    public const byte PartyRequest91Opcode = 0x91;
    public const byte PartyRequest92Opcode = 0x92;
    public const byte PartyRequest94Opcode = 0x94;
    public const byte PkCursorTargetOpcode = 0x69;
    public const byte PetPkTargetOpcode = 0xB8;
    public const byte PetEggRequestOpcode = 0xB7;
    public const byte FPetRequestOpcode = 0xBB;
    public const byte VendorCartActionB2Opcode = 0xB2;
    public const byte VendorCartAddRecordOpcode = 0xBD;
    public const byte VendorCartPublishOpcode = 0xB0;
    public const byte TeamRequestOpcode = 0x5A;
    public const byte CharacterDeleteRequestOpcode = 0x19;
    public const byte CharacterSelectRequestOpcode = 0x1A;
    public const byte CharacterCreate1bRequestOpcode = 0x1B;
    public const int AccountLoginDecodedFrameLength = 205;
    public const int GameLoginDecodedFrameLength = 205;
    public const int ApplicationPayloadLength = 2;
    public const int GameplayDisconnectDecodedFrameLength = 21;
    public const int AttributeIncrementDecodedFrameLength = 5;
    public const int DecodedFrameLength = 6;
    public const int RouteDecodedFrameLength = 5;
    public const int CombinationCommitDecodedFrameLength = 11;
    public const int MailRecordActionDecodedFrameLength = 13;
    public const int Interaction27ApplicationPayloadLength = 4;
    public const int Interaction27DecodedFrameLength = 8;
    public const int Interaction28DecodedFrameLength = 8;
    public const int Interaction26ApplicationPayloadLength = 6;
    public const int Interaction26DecodedFrameLength = 10;
    public const int CombinationStepApplicationPayloadLength = 2;
    public const int CombinationStepDecodedFrameLength = 6;
    public const int PartySelectionDecodedFrameLength = 8;
    public const int PartyRequest95DecodedFrameLength = 6;
    public const int TeamRequestDecodedFrameLength = 22;
    public const int CharacterDeleteDecodedFrameLength = 57;
    public const int CharacterSelectDecodedFrameLength = 41;
    public const int CharacterCreate1bDecodedFrameLength = 48;
    public const int PartyRequest8FDecodedFrameLength = 25;
    public const int PartyRequest90DecodedFrameLength = 5;
    public const int PartyRequest91DecodedFrameLength = 5;
    public const int PartyRequest92DecodedFrameLength = 6;
    public const int PartyRequest94DecodedFrameLength = 9;
    public const int PkCursorTargetDecodedFrameLength = 45;
    public const int PetPkTargetDecodedFrameLength = 6;
    public const int PetEggRequestDecodedFrameLength = 5;
    public const int FPetRequestDecodedFrameLength = 85;
    public const int VendorCartActionB2DecodedFrameLength = 5;
    public const int VendorCartAddRecordDecodedFrameLength = 25;
    public const int VendorCartPublishDecodedFrameLength = 3;
    public const string EvidenceId =
        "God2_opt:rva-0x000AEE90..0x000AEF32->0x000AEF2B/current-payload-u8-u8;" +
        "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_mission_protocol/interaction_24-raw-u8-u8";
    public const string InventoryActivationEvidenceId =
        "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_mission_protocol/interaction28-u16-u8-u8-corroboration";

    public static PublicBetaGameplayRequestAdapterResult AdaptClientCommand(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(
                PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
                "wire.public_beta_compat.interaction_24_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidState,
                "wire.public_beta_compat.interaction_24_state_invalid");
        }

        if (decodedFrame.Length != DecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != DecodedFrameLength)
        {
            return Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
                "wire.public_beta_compat.interaction_24_length_invalid");
        }

        if (decodedFrame[2] != Interaction24Opcode)
        {
            return Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
                "wire.public_beta_compat.interaction_24_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
                "wire.public_beta_compat.interaction_24_checksum_invalid");
        }

        return new PublicBetaGameplayRequestAdapterResult(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityInteraction24Command(
                decodedFrame[3],
                decodedFrame[4],
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceId,
                ProductionMutationAllowed: false),
            string.Empty);
    }

    public static PublicBetaRawRequestAdapterResult AdaptInventoryActivation(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            Interaction28Opcode,
            Interaction28DecodedFrameLength,
            InventoryActivationEvidenceId);

    public static PublicBetaAttributeIncrementAdapterResult AdaptAttributeIncrement(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return AttributeIncrementFailure(
                PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
                "wire.public_beta_compat.attribute_increment_build_mismatch");
        }
        if (state != GameplayProtocolState.World)
        {
            return AttributeIncrementFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidState,
                "wire.public_beta_compat.attribute_increment_state_invalid");
        }
        if (decodedFrame.Length != AttributeIncrementDecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != AttributeIncrementDecodedFrameLength)
        {
            return AttributeIncrementFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
                "wire.public_beta_compat.attribute_increment_length_invalid");
        }
        if (decodedFrame[2] != AttributeIncrementOpcode)
        {
            return AttributeIncrementFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
                "wire.public_beta_compat.attribute_increment_opcode_invalid");
        }
        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return AttributeIncrementFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
                "wire.public_beta_compat.attribute_increment_checksum_invalid");
        }

        var decoded = OfficialPlayerSnapshotCandidateWireCodec.DecodeAttributeIncrementLayout(
            clientBuildId,
            state,
            decodedFrame[2..^1]);
        if (!decoded.LayoutDecoded || decoded.Value is null)
        {
            return AttributeIncrementFailure(
                decoded.Code == OfficialPlayerSnapshotWireResultCode.InvalidAttributeCode
                    ? PublicBetaGameplayRequestAdapterResultCode.InvalidAttributeCode
                    : PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
                decoded.FailureCode);
        }

        return new PublicBetaAttributeIncrementAdapterResult(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            decoded.Value,
            string.Empty);
    }

    public static PublicBetaGameplayInteraction27AdapterResult AdaptInteraction27(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Interaction27Failure(
                PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
                "wire.public_beta_compat.interaction_27_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Interaction27Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidState,
                "wire.public_beta_compat.interaction_27_state_invalid");
        }

        if (decodedFrame.Length != Interaction27DecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != Interaction27DecodedFrameLength)
        {
            return Interaction27Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
                "wire.public_beta_compat.interaction_27_length_invalid");
        }

        if (decodedFrame[2] != Interaction27Opcode)
        {
            return Interaction27Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
                "wire.public_beta_compat.interaction_27_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Interaction27Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
                "wire.public_beta_compat.interaction_27_checksum_invalid");
        }

        return new PublicBetaGameplayInteraction27AdapterResult(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityInteraction27Command(
                BinaryPrimitives.ReadUInt32LittleEndian(decodedFrame[3..]),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                "God2_opt:rva-0x000AEF50..0x000AEF6F->0x000AEF69/current-payload-u32;" +
                "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
                ":legacy_mission_protocol/interaction_27-raw-u32",
                ProductionMutationAllowed: false),
            string.Empty);
    }

    public static PublicBetaGameplayInteraction26AdapterResult AdaptInteraction26(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Interaction26Failure(
                PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
                "wire.public_beta_compat.interaction_26_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Interaction26Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidState,
                "wire.public_beta_compat.interaction_26_state_invalid");
        }

        if (decodedFrame.Length != Interaction26DecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != Interaction26DecodedFrameLength)
        {
            return Interaction26Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
                "wire.public_beta_compat.interaction_26_length_invalid");
        }

        if (decodedFrame[2] != Interaction26Opcode)
        {
            return Interaction26Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
                "wire.public_beta_compat.interaction_26_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Interaction26Failure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
                "wire.public_beta_compat.interaction_26_checksum_invalid");
        }

        return new PublicBetaGameplayInteraction26AdapterResult(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityInteraction26Command(
                BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]),
                BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[5..]),
                decodedFrame[7],
                decodedFrame[8],
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                "God2_opt:rva-0x000AEFF0..0x000AF062->0x000AF050+" +
                "rva-0x000AF6F0..0x000AF7A0->0x000AF795/current-payload-u16-u16-u8-raw;" +
                "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
                ":legacy_mission_protocol/world_interaction_26-raw-overlay",
                ProductionMutationAllowed: false),
            string.Empty);
    }

    public static PublicBetaCombinationStepAdapterResult AdaptCombinationStep(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return CombinationStepFailure(
                PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
                "wire.public_beta_compat.combination_step_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return CombinationStepFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidState,
                "wire.public_beta_compat.combination_step_state_invalid");
        }

        if (decodedFrame.Length != CombinationStepDecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != CombinationStepDecodedFrameLength)
        {
            return CombinationStepFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
                "wire.public_beta_compat.combination_step_length_invalid");
        }

        if (decodedFrame[2] != CombinationStepOpcode)
        {
            return CombinationStepFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
                "wire.public_beta_compat.combination_step_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return CombinationStepFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
                "wire.public_beta_compat.combination_step_checksum_invalid");
        }

        var step = unchecked((sbyte)decodedFrame[4]);
        if (step is not (-1 or 1))
        {
            return CombinationStepFailure(
                PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
                "wire.public_beta_compat.combination_step_value_invalid");
        }

        return new PublicBetaCombinationStepAdapterResult(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityCombinationStepCommand(
                decodedFrame[3],
                step,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                "God2_opt:rva-0x001144FB..0x001146A7->0x0011469C/current-selector-step-plus-minus-one;" +
                "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
                ":legacy_combination_protocol/a4-selector-step-plus-minus-one",
                ProductionMutationAllowed: false),
            string.Empty);
    }

    public static PublicBetaRawRequestAdapterResult AdaptPartySelection(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartySelectionOpcode,
            PartySelectionDecodedFrameLength,
            "God2_opt:rva-0x000AED40..0x000AED6A->0x000AED64/current-raw-u32;" +
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/93-raw-u32");

    public static PublicBetaRawRequestAdapterResult AdaptTeamRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            TeamRequestOpcode,
            TeamRequestDecodedFrameLength,
            "God2_opt:rva-0x000B0610..0x000B066B->0x000B0657+" +
            "rva-0x00118690..0x001186A6->0x001186A1/current-u16-plus-16-raw-bytes;" +
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_team_protocol/5a-u16-plus-16-raw-bytes");

    public static PublicBetaRawRequestAdapterResult AdaptGameplayDisconnect(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            GameplayDisconnectOpcode,
            GameplayDisconnectDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_gameplay_protocol/0a-disconnect-request");

    public static PublicBetaRawRequestAdapterResult AdaptPartyRequest95(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartyRequest95Opcode,
            PartyRequest95DecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/95-u16");

    public static PublicBetaRawRequestAdapterResult AdaptCharacterDeleteRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            CharacterDeleteRequestOpcode,
            CharacterDeleteDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_character_protocol/19-character-delete-request");

    public static PublicBetaRawRequestAdapterResult AdaptCharacterSelectRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            CharacterSelectRequestOpcode,
            CharacterSelectDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_character_protocol/1a-character-select-request");

    public static PublicBetaRawRequestAdapterResult AdaptCharacterCreate1bRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            CharacterCreate1bRequestOpcode,
            CharacterCreate1bDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_character_protocol/1b-character-create-request");

    public static PublicBetaRawRequestAdapterResult AdaptSocialTextEnvelope(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptVariableLengthRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            SocialTextEnvelopeOpcode,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_text_protocol/33-text-envelope-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptAccountLogin(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            AccountLoginOpcode,
            AccountLoginDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_account_login/04-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptGameLogin(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            GameLoginOpcode,
            GameLoginDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_game_server_login/00-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptRouteQuery(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            RouteOpcode,
            RouteDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_route_query/a9-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptCombinationCommit(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            CombinationCommitOpcode,
            CombinationCommitDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_combination_protocol/a5-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptMailRecordAction(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            MailRecordActionOpcode,
            MailRecordActionDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_mail_protocol/ae-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPartyRequest8F(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartyRequest8FOpcode,
            PartyRequest8FDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/8f-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPartyRequest90(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartyRequest90Opcode,
            PartyRequest90DecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/90-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPartyRequest91(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartyRequest91Opcode,
            PartyRequest91DecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/91-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPartyRequest92(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartyRequest92Opcode,
            PartyRequest92DecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/92-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPartyRequest94(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PartyRequest94Opcode,
            PartyRequest94DecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_party_protocol/94-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPetEggRequestB7(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PetEggRequestOpcode,
            PetEggRequestDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_pet_protocol/b7-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptFPetRequestBB(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            FPetRequestOpcode,
            FPetRequestDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_pet_protocol/bb-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPkCursorTarget(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PkCursorTargetOpcode,
            PkCursorTargetDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_pk_protocol/69-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptPetPkTarget(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            PetPkTargetOpcode,
            PetPkTargetDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_pk_protocol/b8-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptVendorCartActionB2(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            VendorCartActionB2Opcode,
            VendorCartActionB2DecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_vendor_cart_protocol/b2-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptVendorCartAddRecord(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequest(
            clientBuildId,
            state,
            decodedFrame,
            VendorCartAddRecordOpcode,
            VendorCartAddRecordDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_vendor_cart_protocol/bd-current-build-raw");

    public static PublicBetaRawRequestAdapterResult AdaptVendorCartPublish(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame) =>
        AdaptRawRequestNoChecksum(
            clientBuildId,
            state,
            decodedFrame,
            VendorCartPublishOpcode,
            VendorCartPublishDecodedFrameLength,
            "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
            ":legacy_vendor_cart_protocol/b0-current-build-raw");

    private static PublicBetaGameplayRequestAdapterResult Failure(
        PublicBetaGameplayRequestAdapterResultCode code,
        string failureCode) => new(code, null, failureCode);

    private static PublicBetaGameplayInteraction27AdapterResult Interaction27Failure(
        PublicBetaGameplayRequestAdapterResultCode code,
        string failureCode) => new(code, null, failureCode);

    private static PublicBetaGameplayInteraction26AdapterResult Interaction26Failure(
        PublicBetaGameplayRequestAdapterResultCode code,
        string failureCode) => new(code, null, failureCode);

    private static PublicBetaCombinationStepAdapterResult CombinationStepFailure(
        PublicBetaGameplayRequestAdapterResultCode code,
        string failureCode) => new(code, null, failureCode);

    private static PublicBetaRawRequestAdapterResult AdaptRawRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame,
        byte opcode,
        int frameLength,
        string evidence)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.BuildMismatch, null, "wire.public_beta_compat.raw_request_build_mismatch");
        }
        if (state != GameplayProtocolState.World)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidState, null, "wire.public_beta_compat.raw_request_state_invalid");
        }
        if (decodedFrame.Length != frameLength || BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != frameLength)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidLength, null, "wire.public_beta_compat.raw_request_length_invalid");
        }
        if (decodedFrame[2] != opcode)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode, null, "wire.public_beta_compat.raw_request_opcode_invalid");
        }
        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum, null, "wire.public_beta_compat.raw_request_checksum_invalid");
        }

        return new(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityRawRequestCommand(
                opcode,
                decodedFrame[3..(decodedFrame.Length - Math.Min(decodedFrame.Length - 3, 1))].ToArray(),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                evidence,
                ProductionMutationAllowed: false),
            string.Empty);
    }

    private static PublicBetaRawRequestAdapterResult AdaptVariableLengthRawRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame,
        byte opcode,
        string evidence)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.BuildMismatch, null, "wire.public_beta_compat.raw_request_build_mismatch");
        }
        if (state != GameplayProtocolState.World)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidState, null, "wire.public_beta_compat.raw_request_state_invalid");
        }
        if (decodedFrame.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != decodedFrame.Length)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidLength, null, "wire.public_beta_compat.raw_request_length_invalid");
        }
        if (decodedFrame[2] != opcode)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode, null, "wire.public_beta_compat.raw_request_opcode_invalid");
        }
        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum, null, "wire.public_beta_compat.raw_request_checksum_invalid");
        }

        return new(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityRawRequestCommand(
                opcode,
                decodedFrame.Slice(3, Math.Max(decodedFrame.Length - 4, 0)).ToArray(),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                evidence,
                ProductionMutationAllowed: false),
                string.Empty);
    }

    private static PublicBetaRawRequestAdapterResult AdaptRawRequestNoChecksum(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame,
        byte opcode,
        int frameLength,
        string evidence)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.BuildMismatch, null, "wire.public_beta_compat.raw_request_build_mismatch");
        }
        if (state != GameplayProtocolState.World)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidState, null, "wire.public_beta_compat.raw_request_state_invalid");
        }
        if (decodedFrame.Length != frameLength || BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != frameLength)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidLength, null, "wire.public_beta_compat.raw_request_length_invalid");
        }
        if (decodedFrame[2] != opcode)
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode, null, "wire.public_beta_compat.raw_request_opcode_invalid");
        }
        if (decodedFrame.Length > 3 && decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return new(PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum, null, "wire.public_beta_compat.raw_request_checksum_invalid");
        }

        return new(
            PublicBetaGameplayRequestAdapterResultCode.Adapted,
            new PublicBetaCompatibilityRawRequestCommand(
                opcode,
                decodedFrame.Slice(3, Math.Max(decodedFrame.Length - 4, 0)).ToArray(),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                evidence,
                ProductionMutationAllowed: false),
            string.Empty);
    }

    private static PublicBetaAttributeIncrementAdapterResult AttributeIncrementFailure(
        PublicBetaGameplayRequestAdapterResultCode code,
        string failureCode) => new(code, null, failureCode);
}
