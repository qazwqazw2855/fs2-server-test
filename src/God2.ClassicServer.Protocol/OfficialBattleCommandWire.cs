using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public enum OfficialBattleCommandWireResultCode
{
    LayoutDecoded,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidChecksum,
    InvalidPosition,
    InvalidTargetMask,
    SemanticEvidenceBlocked
}

public enum OfficialBattleCommandAction
{
    Unknown = 0,
    BasicAttack,
    Defend,
    Skill,
    Flee
}

public sealed record OfficialBattleCommandWireResult<T>(
    OfficialBattleCommandWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool LayoutDecoded => Code == OfficialBattleCommandWireResultCode.LayoutDecoded;

    public static OfficialBattleCommandWireResult<T> Success(T value) =>
        new(OfficialBattleCommandWireResultCode.LayoutDecoded, value, string.Empty);

    public static OfficialBattleCommandWireResult<T> Failure(
        OfficialBattleCommandWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialBattleCommandCandidate(
    byte PositionIndex,
    byte ActionCode,
    OfficialBattleCommandAction Action,
    bool Continuation,
    byte SideFlag,
    byte PreservedByte3,
    ushort TargetMaskGroup0,
    ushort TargetMaskGroup1,
    ushort TargetMaskGroup2,
    IReadOnlyList<int> TargetPositionIndices,
    ushort BattleContext,
    uint ActionParameter,
    string DecodedFrameSha256,
    string FieldLayoutEvidence,
    string SemanticEvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Exact-build, fail-closed decoder for the official Client's C2S 0x35 Battle command
/// layout. This boundary deliberately stops before gameplay mutation: the numeric action
/// and action-parameter meanings, and the corresponding S2C result layouts, have not yet
/// passed the M5 semantic promotion gate.
/// </summary>
public static class OfficialBattleCommandWireCodec
{
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string ClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string FieldLayoutEvidence =
        "God2_opt+rva-0x0014E7D0..0x0014EB3C;" +
        "FS2TW-public-beta-0x35-stable-opcode-actor-action-prefix-only-remainder-changed";
    public const byte CommandOpcode = 0x35;
    public const int DecodedFrameLength = 20;
    public const int ApplicationPayloadLength = 16;
    public const int TargetPositionsPerGroup = 14;
    public const int MaximumTargetCount = 14;
    public const bool RuntimeMutationEnabled = false;
    public const bool ServerResultSerializerEnabled = false;

    private const ushort UnsupportedTargetBits = 0xC000;

    public static OfficialBattleCommandWireResult<OfficialBattleCommandCandidate> DecodeLayout(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.BuildMismatch,
                "wire.battle.client_build_mismatch");
        }

        if (state != GameplayProtocolState.Battle)
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.InvalidState,
                "wire.battle.state_invalid");
        }

        if (decodedFrame.Length != DecodedFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != DecodedFrameLength)
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.InvalidLength,
                "wire.battle.command_length_invalid");
        }

        if (decodedFrame[2] != CommandOpcode)
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.InvalidOpcode,
                "wire.battle.command_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.InvalidChecksum,
                "wire.battle.command_checksum_invalid");
        }

        var payload = decodedFrame.Slice(3, ApplicationPayloadLength);
        if (payload[0] > 0x0E)
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.InvalidPosition,
                "wire.battle.position_invalid");
        }

        var group0 = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        var group1 = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
        var group2 = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]);
        var targetCount = BitOperations.PopCount(group0) +
                          BitOperations.PopCount(group1) +
                          BitOperations.PopCount(group2);
        if (((group0 | group1 | group2) & UnsupportedTargetBits) != 0 ||
            targetCount > MaximumTargetCount)
        {
            return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
                OfficialBattleCommandWireResultCode.InvalidTargetMask,
                "wire.battle.target_mask_invalid");
        }

        var targets = ExpandTargets(group0, group1, group2);
        var action = (payload[1] & 0x7F) switch
        {
            1 => OfficialBattleCommandAction.BasicAttack,
            2 => OfficialBattleCommandAction.Defend,
            3 => OfficialBattleCommandAction.Skill,
            11 => OfficialBattleCommandAction.Flee,
            _ => OfficialBattleCommandAction.Unknown
        };
        var candidate = new OfficialBattleCommandCandidate(
            payload[0],
            (byte)(payload[1] & 0x7F),
            action,
            (payload[1] & 0x80) != 0,
            payload[2],
            payload[3],
            group0,
            group1,
            group2,
            targets,
            BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]),
            Convert.ToHexString(SHA256.HashData(decodedFrame)),
            FieldLayoutEvidence,
            action == OfficialBattleCommandAction.Unknown
                ? "FieldLayoutVerified_ActionAndParameterSemanticsEvidenceBlocked"
                : "ActionCodeVerified_ActionParameterSemanticsEvidenceBlocked",
            RuntimeMutationAllowed: false);
        return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Success(candidate);
    }

    public static OfficialBattleCommandWireResult<OfficialBattleCommandCandidate> DecodeForRuntime(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        var layout = DecodeLayout(clientBuildId, state, decodedFrame);
        if (!layout.LayoutDecoded)
        {
            return layout;
        }

        return OfficialBattleCommandWireResult<OfficialBattleCommandCandidate>.Failure(
            OfficialBattleCommandWireResultCode.SemanticEvidenceBlocked,
            "wire.battle.command_semantics_evidence_blocked");
    }

    private static IReadOnlyList<int> ExpandTargets(ushort group0, ushort group1, ushort group2)
    {
        var targets = new List<int>(MaximumTargetCount);
        var groups = new[] { group0, group1, group2 };
        for (var group = 0; group < groups.Length; group++)
        {
            for (var bit = 0; bit < TargetPositionsPerGroup; bit++)
            {
                if ((groups[group] & (1 << bit)) != 0)
                {
                    targets.Add(group * TargetPositionsPerGroup + bit);
                }
            }
        }

        return new ReadOnlyCollection<int>(targets);
    }
}
