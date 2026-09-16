using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

public enum OfficialNpcInteractionKind
{
    Open,
    MerchantClose
}

public sealed record OfficialNpcInteractionRequest(
    OfficialNpcInteractionKind Kind,
    ushort ClientEntityHandle);

public static class OfficialNpcInteractionCodec
{
    public const byte OpenOpcode = 0x37;
    public const byte MerchantCloseOpcode = 0x39;
    public const byte DialogOrQuestCloseOpcode = 0x86;
    public const int FrameLength = 8;

    public static bool IsCandidate(
        ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(
                encodedFrame) != FrameLength)
        {
            return false;
        }

        var decoded =
            OfficialWorldBootstrapCodec.DecodeFrame(encodedFrame);

        try
        {
            return decoded[2] is
                OpenOpcode or
                MerchantCloseOpcode or
                DialogOrQuestCloseOpcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> encodedFrame,
        out OfficialNpcInteractionRequest? request,
        out string failureCode)
    {
        request = null;
        failureCode = string.Empty;

        if (encodedFrame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(
                encodedFrame) != FrameLength)
        {
            failureCode = "InvalidLength";
            return false;
        }

        var decoded =
            OfficialWorldBootstrapCodec.DecodeFrame(encodedFrame);

        try
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded) != FrameLength)
            {
                failureCode = "DecodedLengthMismatch";
                return false;
            }

            var kind = decoded[2] switch
            {
                OpenOpcode =>
                    OfficialNpcInteractionKind.Open,
                MerchantCloseOpcode =>
                    OfficialNpcInteractionKind.MerchantClose,
                _ => (OfficialNpcInteractionKind?)null
            };

            if (kind is null)
            {
                failureCode = "UnsupportedOpcode";
                return false;
            }

            if (decoded[^1] !=
                OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                failureCode = "ChecksumMismatch";
                return false;
            }

            var handle =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(3, sizeof(ushort)));

            var preservedState =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(5, sizeof(ushort)));

            if (handle == 0 || preservedState != 0)
            {
                failureCode = "InvalidFields";
                return false;
            }

            request = new OfficialNpcInteractionRequest(
                kind.Value,
                handle);

            return true;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }
}
