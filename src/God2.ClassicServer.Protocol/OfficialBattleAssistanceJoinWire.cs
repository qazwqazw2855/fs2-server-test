using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleAssistanceJoinWireResultCode
{
    LayoutDecoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidChecksum,
    InvalidEntityId,
    SameEntity,
    SemanticEvidenceBlocked
}

public sealed record OfficialBattleAssistanceJoinWireResult<T>(
    OfficialBattleAssistanceJoinWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleAssistanceJoinWireResultCode.LayoutDecoded;

    public static OfficialBattleAssistanceJoinWireResult<T> Success(T value) =>
        new(OfficialBattleAssistanceJoinWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialBattleAssistanceJoinWireResult<T> Failure(
        OfficialBattleAssistanceJoinWireResultCode code,
        string failureCode) => new(code, default, failureCode);
}

public sealed record OfficialBattleAssistanceJoinRequest(
    uint JoiningEntityId,
    uint BattleAnchorEntityId,
    string DecodedFrameSha256,
    string EvidenceChain,
    bool RuntimeMutationAllowed);

/// <summary>
/// Exact-build codec for the current official client's C2S 0x6A request.
/// The controlled three-client trace binds the first entity to the joining
/// character and the second to an existing participant. The request is followed
/// by an S2C 0x82 bootstrap to the joiner and S2C 0x86 roster synchronization to
/// both existing participants. Runtime mutation remains disabled until the
/// authoritative battle lookup and current-build serializers are connected.
/// </summary>
public static class OfficialBattleAssistanceJoinWireCodec
{
    public const string ClientBuildId = OfficialBattleCommandWireCodec.ClientBuildId;
    public const byte CommandOpcode = 0x6A;
    public const int DecodedFrameLength = 12;
    public const string EvidenceFrameSha256 = "9CD2644836F8A09BFD273F8A3194EB84B5CF14F02DCCEF9334CD840D53281669";
    public const string EvidenceChain =
        "C2S-0x6A(joiner=254,anchor=242)->S2C-0x82(joiner)->S2C-0x86(existing-participants)";
    public const bool RuntimeMutationEnabled = false;

    public static OfficialBattleAssistanceJoinWireResult<OfficialBattleAssistanceJoinRequest> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.BuildMismatch, "wire.battle_join.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.InvalidState, "wire.battle_join.state_invalid");
        }

        if (decodedFrame.Length != DecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != DecodedFrameLength)
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.InvalidLength, "wire.battle_join.length_invalid");
        }

        if (decodedFrame[2] != CommandOpcode)
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.InvalidOpcode, "wire.battle_join.opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.InvalidChecksum, "wire.battle_join.checksum_invalid");
        }

        var joiningEntityId = BinaryPrimitives.ReadUInt32LittleEndian(decodedFrame[3..]);
        var battleAnchorEntityId = BinaryPrimitives.ReadUInt32LittleEndian(decodedFrame[7..]);
        if (joiningEntityId == 0 || battleAnchorEntityId == 0)
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.InvalidEntityId, "wire.battle_join.entity_id_invalid");
        }

        if (joiningEntityId == battleAnchorEntityId)
        {
            return Failure(OfficialBattleAssistanceJoinWireResultCode.SameEntity, "wire.battle_join.same_entity");
        }

        return OfficialBattleAssistanceJoinWireResult<OfficialBattleAssistanceJoinRequest>.Success(
            new OfficialBattleAssistanceJoinRequest(
                joiningEntityId,
                battleAnchorEntityId,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceChain,
                RuntimeMutationAllowed: false));
    }

    public static OfficialBattleAssistanceJoinWireResult<ReadOnlyMemory<byte>> EncodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        uint joiningEntityId,
        uint battleAnchorEntityId)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleAssistanceJoinWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleAssistanceJoinWireResultCode.BuildMismatch,
                "wire.battle_join.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return OfficialBattleAssistanceJoinWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleAssistanceJoinWireResultCode.InvalidState,
                "wire.battle_join.state_invalid");
        }

        if (joiningEntityId == 0 || battleAnchorEntityId == 0)
        {
            return OfficialBattleAssistanceJoinWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleAssistanceJoinWireResultCode.InvalidEntityId,
                "wire.battle_join.entity_id_invalid");
        }

        if (joiningEntityId == battleAnchorEntityId)
        {
            return OfficialBattleAssistanceJoinWireResult<ReadOnlyMemory<byte>>.Failure(
                OfficialBattleAssistanceJoinWireResultCode.SameEntity,
                "wire.battle_join.same_entity");
        }

        var frame = new byte[DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, DecodedFrameLength);
        frame[2] = CommandOpcode;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), joiningEntityId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), battleAnchorEntityId);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return OfficialBattleAssistanceJoinWireResult<ReadOnlyMemory<byte>>.Success(frame);
    }

    public static OfficialBattleAssistanceJoinWireResult<OfficialBattleAssistanceJoinRequest> DecodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        var decoded = DecodeLayout(clientBuildId, state, decodedFrame);
        return decoded.LayoutDecoded
            ? Failure(OfficialBattleAssistanceJoinWireResultCode.SemanticEvidenceBlocked, "wire.battle_join.runtime_authority_blocked")
            : decoded;
    }

    private static OfficialBattleAssistanceJoinWireResult<OfficialBattleAssistanceJoinRequest> Failure(
        OfficialBattleAssistanceJoinWireResultCode code,
        string failureCode) =>
        OfficialBattleAssistanceJoinWireResult<OfficialBattleAssistanceJoinRequest>.Failure(code, failureCode);
}
