using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

public sealed record OfficialNpcDialogSelectionRequest(
    ushort ClientEntityHandle,
    byte Selector,
    ushort PreservedState,
    byte OpaqueClientValue,
    bool IsCompoundTransport);

public static class OfficialNpcDialogSelectionCodec
{
    public const byte DialogSelectionOpcode = 0x85;
    public const byte DialogOrQuestCompanionOpcode = 0x86;
    public const int StandaloneFrameLength = 10;
    public const int CompoundFrameLength = 15;
    public const ushort LiveDialogHandle = 3793;
    public const byte LiveDialogSelector = 7;

    public const string EvidenceId =
        "LiveRecovery/attempt-759-seq-28955+" +
        "host-run-20260813-072107-seq-280+" +
        "host-run-20260813-073550-seq-299-303";

    public static bool IsCandidate(
        ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length is not
                (StandaloneFrameLength or CompoundFrameLength) ||
            BinaryPrimitives.ReadUInt16LittleEndian(
                encodedFrame) != encodedFrame.Length)
        {
            return false;
        }

        var decoded =
            OfficialWorldBootstrapCodec.DecodeFrame(
                encodedFrame);

        try
        {
            return decoded.Length == StandaloneFrameLength
                ? decoded[2] == DialogSelectionOpcode
                : decoded[2] ==
                    DialogOrQuestCompanionOpcode &&
                  decoded[7] == DialogSelectionOpcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> encodedFrame,
        out OfficialNpcDialogSelectionRequest? request,
        out string failureCode)
    {
        request = null;
        failureCode = string.Empty;

        if (encodedFrame.Length is not
                (StandaloneFrameLength or CompoundFrameLength) ||
            BinaryPrimitives.ReadUInt16LittleEndian(
                encodedFrame) != encodedFrame.Length)
        {
            failureCode = "InvalidLength";
            return false;
        }

        var decoded =
            OfficialWorldBootstrapCodec.DecodeFrame(
                encodedFrame);

        try
        {
            var isCompound =
                decoded.Length == CompoundFrameLength;

            if ((!isCompound &&
                    decoded[2] != DialogSelectionOpcode) ||
                (isCompound &&
                    (decoded[2] !=
                        DialogOrQuestCompanionOpcode ||
                     decoded[7] !=
                        DialogSelectionOpcode)))
            {
                failureCode = "UnsupportedOpcode";
                return false;
            }

            if (decoded[^1] !=
                OfficialLoginWireTransform.ComputeChecksum(
                    decoded))
            {
                failureCode = "ChecksumMismatch";
                return false;
            }

            var handleOffset = isCompound ? 8 : 3;
            var selectorOffset = isCompound ? 10 : 5;
            var stateOffset = isCompound ? 11 : 6;
            var opaqueOffset = isCompound ? 13 : 8;

            var handle =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(
                        handleOffset,
                        sizeof(ushort)));
            var selector = decoded[selectorOffset];
            var preservedState =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(
                        stateOffset,
                        sizeof(ushort)));
            var opaqueClientValue =
                decoded[opaqueOffset];

            if (handle != LiveDialogHandle ||
                selector != LiveDialogSelector ||
                preservedState != 0 ||
                opaqueClientValue is not (0x11 or 0x12))
            {
                failureCode =
                    "DialogSelectionEvidenceBlocked";
                return false;
            }

            request =
                new OfficialNpcDialogSelectionRequest(
                    handle,
                    selector,
                    preservedState,
                    opaqueClientValue,
                    isCompound);

            return true;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static byte[] EncodeStandaloneForProbe(
        byte opaqueClientValue = 0x11)
    {
        if (opaqueClientValue is not (0x11 or 0x12))
        {
            throw new ArgumentOutOfRangeException(
                nameof(opaqueClientValue));
        }

        var decoded = new byte[StandaloneFrameLength];

        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                decoded,
                StandaloneFrameLength);
            decoded[2] = DialogSelectionOpcode;
            BinaryPrimitives.WriteUInt16LittleEndian(
                decoded.AsSpan(3, sizeof(ushort)),
                LiveDialogHandle);
            decoded[5] = LiveDialogSelector;
            decoded[8] = opaqueClientValue;
            decoded[^1] =
                OfficialLoginWireTransform.ComputeChecksum(
                    decoded);

            return OfficialWorldBootstrapCodec.EncodeFrame(
                decoded);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }
}
