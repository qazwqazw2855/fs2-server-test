using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

// Projection of the existing Classic capture-pinned SELL profile.
// Encoding does not approve merchant authority, item ownership or execution.
// Only the observed quantity-one client slot 4 profile is supported.
public static class OfficialMerchantSaleResultCodec
{
    public const string EvidenceReference =
        "LiveRecovery/Stages4-7-attempt-759-trace";
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const int FrameLength = 25;

    public static bool TryEncode(
        string clientBuildId,
        OfficialMerchantTransactionSelection selection,
        long walletBalanceAfter,
        out byte[]? encoded,
        out string failureCode)
    {
        ArgumentNullException.ThrowIfNull(selection);
        encoded = null;
        failureCode = string.Empty;

        if (!string.Equals(clientBuildId, ClientBuildId,
                StringComparison.Ordinal))
            failureCode = "BuildMismatch";
        else if (selection.Operation != OfficialMerchantTransactionOperation.Sell)
            failureCode = "UnsupportedOperation";
        else if (selection.ClientEntityHandle != 3954 ||
                 selection.ClientItemId != 6901)
            failureCode = "ProfileEvidenceBlocked";
        else if (selection.Quantity != 1)
            failureCode = "QuantityEvidenceBlocked";
        else if (selection.CatalogIndexOrClientInventorySlot != 4)
            failureCode = "SelectionEvidenceBlocked";
        else if (walletBalanceAfter < 0 || walletBalanceAfter > uint.MaxValue)
            failureCode = "WalletBalanceOutOfRange";

        if (failureCode.Length != 0)
            return false;

        var decoded = new byte[FrameLength];
        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(decoded, FrameLength);
            decoded[2] = 0x41;
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(5), 4);
            decoded[7] = 0x27;
            BinaryPrimitives.WriteUInt32LittleEndian(
                decoded.AsSpan(8), checked((uint)walletBalanceAfter));
            decoded[12] = 0x69;
            BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(13), 3954);
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(17), 1);
            decoded[19] = 0x41;
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(22), 4);
            decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
            encoded = OfficialWorldBootstrapCodec.EncodeFrame(decoded);
            return true;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }
}
