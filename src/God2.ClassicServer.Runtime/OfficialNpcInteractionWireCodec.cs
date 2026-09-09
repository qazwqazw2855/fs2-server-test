using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialNpcInteractionWireResultCode
{
    Success,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    Malformed,
    UnsupportedProfile,
    EvidenceMismatch
}

public sealed record OfficialNpcInteractionWireResult<T>(
    OfficialNpcInteractionWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == OfficialNpcInteractionWireResultCode.Success;

    public static OfficialNpcInteractionWireResult<T> Success(T value) =>
        new(OfficialNpcInteractionWireResultCode.Success, value, string.Empty);

    public static OfficialNpcInteractionWireResult<T> Failure(
        OfficialNpcInteractionWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialNpcInteractionOpenRequest(
    ushort ClientEntityHandle,
    ushort PreservedState,
    string DecodedFrameSha256,
    string EvidenceId);

public enum OfficialNpcInteractionCloseKind
{
    DialogOrQuest,
    Merchant
}

public sealed record OfficialNpcInteractionCloseRequest(
    ushort ClientEntityHandle,
    ushort PreservedState,
    OfficialNpcInteractionCloseKind Kind,
    string DecodedFrameSha256,
    string EvidenceId);

public sealed record OfficialNpcDialogSelectionRequest(
    ushort ClientEntityHandle,
    byte Selector,
    ushort PreservedState,
    byte OpaqueClientValue,
    string DecodedFrameSha256,
    string EvidenceId);

public sealed record OfficialNpcDialogRecord(
    byte MessageType,
    byte Subtype,
    ushort ClientEntityHandle,
    ushort ClientLookupIdentity,
    ushort Field8,
    byte InlineTextMarker,
    int OpaqueLength,
    string OpaqueSha256);

public sealed record OfficialNpcDialogProjection(
    ReadOnlyMemory<byte> DecodedFrame,
    ushort ClientEntityHandle,
    IReadOnlyList<OfficialNpcDialogRecord> Records,
    string ApplicationSha256,
    string EvidenceId);

/// <summary>
/// Exact-build semantic codec for the first evidence-complete NPC dialog-open slice.
/// It accepts the captured C2S 0x37 shape generically, but only serializes handles with a
/// complete, hash-pinned S2C 0x7A profile. The live handle-3793 dialog selection accepts only
/// the twice-observed C2S 0x85 identity/selector/state contract; its changing opaque client byte
/// is integrity-protected but has no inferred gameplay meaning. Merchant service close is accepted
/// only for the capture-pinned C2S 0x39 contract. Standalone 0x86 semantics remain blocked.
/// </summary>
public static class OfficialNpcInteractionWireCodec
{
    public const string ClientBuildId = OfficialNpcReplicationWireCodec.ClientBuildId;
    public const string ClientSha256 = OfficialNpcReplicationWireCodec.ClientSha256;
    public const string EvidenceId = "M4-existing-merchant-session-sequences-5-and-46";
    public const string LiveDialogEvidenceId = "LiveRecovery/attempt-759-decode-2594";
    public const string LiveDialogSelectionEvidenceId =
        "LiveRecovery/attempt-759-seq-28955+host-run-20260813-072107-seq-280+host-run-20260813-073550-seq-299-303";
    public const string ClientOpenSenderEvidence = "God2_opt+rva-0x000B0800->0x0007FC10";
    public const string ClientDialogConsumerEvidence = "God2_opt+rva-0x0009068E->0x000F60E0->0x000F5F80";
    public const byte OpenOpcode = 0x37;
    public const byte MerchantCloseOpcode = 0x39;
    public const byte DialogOrQuestCloseOpcode = 0x86;
    public const byte DialogResultOpcode = 0x7A;
    public const byte DialogSelectionOpcode = 0x85;
    public const int OpenFrameLength = 8;
    public const int CloseFrameLength = 8;
    public const int DialogFrameLength = 58;
    public const int DialogSelectionFrameLength = 10;
    public const int CompoundDialogSelectionFrameLength = 15;
    public const ushort LiveDialogHandle = 3793;
    public const byte LiveDialogSelector = 7;
    public const string DialogApplicationSha256 = "18BD3B82419B71C43BD7B84AC32FBE0B84320BC8AF473759C1D838EDE807560E";

    private static readonly IReadOnlyDictionary<ushort, OfficialNpcDialogRecord[]> DialogProfiles =
        new Dictionary<ushort, OfficialNpcDialogRecord[]>
        {
            [1504] =
            [
                new(0, 0, 1504, 1416, ushort.MaxValue, 0, 15, "5322FECFC92A5E3248A297A3DF3EDDFB9BD9049504272E4F572B87FA36D4B3BD"),
                new(4, 0, 1504, 144, 0, 0, 16, "374708FFF7719DD5979EC875D56CD2286F6D3CF7EC317A3B25632AAB28EC37BB")
            ]
        };

    private static readonly IReadOnlyDictionary<ushort, byte[]> ExactDialogProfiles =
        new Dictionary<ushort, byte[]>
        {
            [3793] = Convert.FromHexString(
                "20007A1B000100D10EF8280780000000000000000000000000000000005D16F3")
        };

    public static OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest> DecodeOpen(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Failure(
                OfficialNpcInteractionWireResultCode.BuildMismatch,
                "wire.npc_interaction.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidState,
                "wire.npc_interaction.state_invalid");
        }

        if (decodedFrame.Length != OpenFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != OpenFrameLength)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidLength,
                "wire.npc_interaction.open_length_invalid");
        }

        if (decodedFrame[2] != OpenOpcode)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidOpcode,
                "wire.npc_interaction.open_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame) ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[5..]) != 0)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Failure(
                OfficialNpcInteractionWireResultCode.Malformed,
                "wire.npc_interaction.open_integrity_invalid");
        }

        var handle = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        if (handle == 0)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Failure(
                OfficialNpcInteractionWireResultCode.Malformed,
                "wire.npc_interaction.target_invalid");
        }

        return OfficialNpcInteractionWireResult<OfficialNpcInteractionOpenRequest>.Success(
            new OfficialNpcInteractionOpenRequest(
                handle,
                0,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceId));
    }

    public static OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest> DecodeClose(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.BuildMismatch,
                "wire.npc_interaction.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidState,
                "wire.npc_interaction.state_invalid");
        }

        if (decodedFrame.Length != CloseFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != CloseFrameLength)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidLength,
                "wire.npc_interaction.close_length_invalid");
        }

        if (decodedFrame[2] == DialogOrQuestCloseOpcode)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.UnsupportedProfile,
                "wire.npc_interaction.dialog_close_evidence_blocked");
        }

        if (decodedFrame[2] != MerchantCloseOpcode)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidOpcode,
                "wire.npc_interaction.close_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.Malformed,
                "wire.npc_interaction.close_integrity_invalid");
        }

        var handle = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        var stateValue = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[5..]);
        if (handle == 0 || stateValue != 0)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Failure(
                OfficialNpcInteractionWireResultCode.Malformed,
                "wire.npc_interaction.close_fields_invalid");
        }

        return OfficialNpcInteractionWireResult<OfficialNpcInteractionCloseRequest>.Success(
            new OfficialNpcInteractionCloseRequest(
                handle,
                stateValue,
                OfficialNpcInteractionCloseKind.Merchant,
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                "M4-existing-merchant-session-sequences-45-and-70"));
    }

    public static OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest> DecodeDialogSelection(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Failure(
                OfficialNpcInteractionWireResultCode.BuildMismatch,
                "wire.npc_interaction.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidState,
                "wire.npc_interaction.state_invalid");
        }

        var isStandalone = decodedFrame.Length == DialogSelectionFrameLength &&
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) == DialogSelectionFrameLength;
        var isCompoundTransport = decodedFrame.Length == CompoundDialogSelectionFrameLength &&
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) == CompoundDialogSelectionFrameLength;
        if (!isStandalone && !isCompoundTransport)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidLength,
                "wire.npc_interaction.dialog_selection_length_invalid");
        }

        // The official client transport can coalesce the observed 0x86 UI companion and
        // the 0x85 selection. It removes both logical length/checksum pairs, appends their
        // bodies under one transport length/checksum, then encrypts the aggregate. Treat
        // 0x86 only as an opaque, capture-proven companion marker; no 0x86 semantic is inferred.
        var selectionOpcodeOffset = isCompoundTransport ? 7 : 2;
        if ((isCompoundTransport && decodedFrame[2] != DialogOrQuestCloseOpcode) ||
            decodedFrame[selectionOpcodeOffset] != DialogSelectionOpcode)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Failure(
                OfficialNpcInteractionWireResultCode.InvalidOpcode,
                "wire.npc_interaction.dialog_selection_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Failure(
                OfficialNpcInteractionWireResultCode.Malformed,
                "wire.npc_interaction.dialog_selection_integrity_invalid");
        }

        var handleOffset = isCompoundTransport ? 8 : 3;
        var selectorOffset = isCompoundTransport ? 10 : 5;
        var stateOffset = isCompoundTransport ? 11 : 6;
        var opaqueOffset = isCompoundTransport ? 13 : 8;
        var handle = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[handleOffset..]);
        var selector = decodedFrame[selectorOffset];
        var preservedState = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[stateOffset..]);
        if (handle != LiveDialogHandle || selector != LiveDialogSelector || preservedState != 0)
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Failure(
                OfficialNpcInteractionWireResultCode.UnsupportedProfile,
                "wire.npc_interaction.dialog_selection_evidence_blocked");
        }

        return OfficialNpcInteractionWireResult<OfficialNpcDialogSelectionRequest>.Success(
            new OfficialNpcDialogSelectionRequest(
                handle,
                selector,
                preservedState,
                decodedFrame[opaqueOffset],
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                LiveDialogSelectionEvidenceId));
    }

    public static OfficialNpcInteractionWireResult<OfficialNpcDialogProjection> SerializeDialogOpen(
        string clientBuildId,
        ushort clientEntityHandle)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Failure(
                OfficialNpcInteractionWireResultCode.BuildMismatch,
                "wire.npc_interaction.client_build_mismatch");
        }

        if (ExactDialogProfiles.TryGetValue(clientEntityHandle, out var exactProfile))
        {
            var exactDecoded = exactProfile.ToArray();
            if (BinaryPrimitives.ReadUInt16LittleEndian(exactDecoded.AsSpan(7)) != clientEntityHandle ||
                exactDecoded[^1] != OfficialLoginWireTransform.ComputeChecksum(exactDecoded))
            {
                Array.Clear(exactDecoded);
                return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Failure(
                    OfficialNpcInteractionWireResultCode.EvidenceMismatch,
                    "wire.npc_interaction.dialog_exact_profile_mismatch");
            }

            return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Success(
                new OfficialNpcDialogProjection(
                    exactDecoded,
                    clientEntityHandle,
                    Array.AsReadOnly(Array.Empty<OfficialNpcDialogRecord>()),
                    Convert.ToHexString(SHA256.HashData(exactDecoded.AsSpan(2, exactDecoded.Length - 3))),
                    LiveDialogEvidenceId));
        }

        if (!DialogProfiles.TryGetValue(clientEntityHandle, out var profile))
        {
            return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Failure(
                OfficialNpcInteractionWireResultCode.UnsupportedProfile,
                "wire.npc_interaction.dialog_profile_evidence_blocked");
        }

        var decoded = new byte[DialogFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, DialogFrameLength);
        var offset = 2;
        foreach (var record in profile)
        {
            var recordLength = checked((byte)(12 + record.OpaqueLength));
            decoded[offset] = DialogResultOpcode;
            decoded[offset + 1] = recordLength;
            decoded[offset + 2] = 0;
            decoded[offset + 3] = record.MessageType;
            decoded[offset + 4] = record.Subtype;
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(offset + 5), record.ClientEntityHandle);
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(offset + 7), record.ClientLookupIdentity);
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(offset + 9), record.Field8);
            decoded[offset + 11] = record.InlineTextMarker;
            var opaque = decoded.AsSpan(offset + 12, record.OpaqueLength);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(opaque)), record.OpaqueSha256, StringComparison.Ordinal))
            {
                Array.Clear(decoded);
                return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Failure(
                    OfficialNpcInteractionWireResultCode.EvidenceMismatch,
                    "wire.npc_interaction.dialog_opaque_mismatch");
            }

            offset += recordLength;
        }

        if (offset != decoded.Length - 1)
        {
            Array.Clear(decoded);
            return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Failure(
                OfficialNpcInteractionWireResultCode.EvidenceMismatch,
                "wire.npc_interaction.dialog_layout_mismatch");
        }

        var applicationHash = Convert.ToHexString(SHA256.HashData(decoded.AsSpan(2, decoded.Length - 3)));
        if (!string.Equals(applicationHash, DialogApplicationSha256, StringComparison.Ordinal))
        {
            Array.Clear(decoded);
            return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Failure(
                OfficialNpcInteractionWireResultCode.EvidenceMismatch,
                "wire.npc_interaction.dialog_application_mismatch");
        }

        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        return OfficialNpcInteractionWireResult<OfficialNpcDialogProjection>.Success(
            new OfficialNpcDialogProjection(
                decoded,
                clientEntityHandle,
                profile.ToArray(),
                applicationHash,
                EvidenceId));
    }

    public static bool HasVerifiedDialogProfile(ushort clientEntityHandle) =>
        DialogProfiles.ContainsKey(clientEntityHandle) || ExactDialogProfiles.ContainsKey(clientEntityHandle);

    public static IReadOnlyList<ushort> SnapshotVerifiedDialogHandles() =>
        Array.AsReadOnly(DialogProfiles.Keys.Concat(ExactDialogProfiles.Keys).Distinct().Order().ToArray());
}
