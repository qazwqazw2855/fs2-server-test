using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

public enum OfficialMerchantTransactionOperation : byte
{
    Buy = 1,
    Sell = 2
}

public sealed record OfficialMerchantTransactionSelection(
    ushort ClientEntityHandle,
    ushort ClientItemId,
    byte Quantity,
    OfficialMerchantTransactionOperation Operation,
    ushort CatalogIndexOrClientInventorySlot);

// Structural layout corroborated by the Classic implementation and
// CN official merchant capture of 2026-10-04.
// Input must already be decoded with the connection's verified transform.
// Success does not authorize content, item mapping or a transaction.
// Catalog indexes and client inventory slots remain distinct namespaces.
public static class OfficialMerchantTransactionCodec
{

    public static byte[] EncodeRequest(
        ushort clientEntityHandle,
        ushort clientItemId,
        byte quantity,
        OfficialMerchantTransactionOperation operation,
        ushort catalogIndexOrClientInventorySlot)
    {
        var decoded = new byte[12];
        try
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                decoded, 12);
            decoded[2] = 0x38;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                decoded.AsSpan(3), clientEntityHandle);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                decoded.AsSpan(5), clientItemId);
            decoded[7] = quantity;
            decoded[8] = (byte)operation;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                decoded.AsSpan(9), catalogIndexOrClientInventorySlot);
            decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);

            if (!TryDecodeDecoded(decoded, out _, out var failure))
                throw new ArgumentException(
                    $"Invalid merchant request: {failure}");

            return OfficialWorldBootstrapCodec.EncodeFrame(decoded);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public const byte Opcode = 0x38;
    public const int FrameLength = 12;

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
            return decoded[2] == Opcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> encodedFrame,
        out OfficialMerchantTransactionSelection? selection,
        out string failureCode)
    {
        selection = null;
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
            return TryDecodeDecoded(
                decoded,
                out selection,
                out failureCode);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecodeDecoded(
        ReadOnlySpan<byte> decodedFrame,
        out OfficialMerchantTransactionSelection? selection,
        out string failureCode)
    {
        selection = null;
        failureCode = string.Empty;

        if (decodedFrame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != FrameLength)
        {
            failureCode = "InvalidLength";
            return false;
        }

        if (decodedFrame[2] != Opcode)
        {
            failureCode = "UnsupportedOpcode";
            return false;
        }

        if (decodedFrame[^1] !=
            OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            failureCode = "ChecksumMismatch";
            return false;
        }

        var handle = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        var item = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[5..]);
        var quantity = decodedFrame[7];
        var operation = decodedFrame[8];

        if (handle == 0 || item == 0 || quantity == 0)
        {
            failureCode = "InvalidFields";
            return false;
        }

        if (operation is not (1 or 2))
        {
            failureCode = "UnsupportedOperation";
            return false;
        }

        selection = new(
            handle,
            item,
            quantity,
            (OfficialMerchantTransactionOperation)operation,
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[9..]));
        return true;
    }
}
