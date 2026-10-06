using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

// Projection of the existing Classic capture-pinned BUY profile.
// This codec does not approve merchant interaction, pricing, inventory layout,
// evidence promotion or transaction execution. TCP execution remains unwired.
// The observed client inventory slot is 4; arbitrary inventory layouts and
// stacking must not be authorized merely because this frame can be encoded.
public static class OfficialMerchantPurchaseResultCodec
{
    public const string EvidenceReference =
        "LiveRecovery/Stages4-7-attempt-759-trace";
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const int FrameLength = 57;

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
        else if (selection.Operation != OfficialMerchantTransactionOperation.Buy)
            failureCode = "UnsupportedOperation";
        else if (selection.ClientEntityHandle != 3954 ||
                 selection.ClientItemId != 6901)
            failureCode = "ProfileEvidenceBlocked";
        else if (selection.Quantity != 1)
            failureCode = "QuantityEvidenceBlocked";
        else if (selection.CatalogIndexOrClientInventorySlot != 7)
            failureCode = "SelectionEvidenceBlocked";
        else if (walletBalanceAfter < 0 || walletBalanceAfter > uint.MaxValue)
            failureCode = "WalletBalanceOutOfRange";

        if (failureCode.Length != 0)
            return false;

        var decoded = new byte[FrameLength];
        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(decoded, FrameLength);
            decoded[2] = 0x3B;
            decoded[3] = 1;
            decoded[4] = 4;
            BinaryPrimitives.WriteUInt32LittleEndian(
                decoded.AsSpan(7), 6901);
            decoded[39] = 0x27;
            BinaryPrimitives.WriteUInt32LittleEndian(
                decoded.AsSpan(40), checked((uint)walletBalanceAfter));
            decoded[44] = 0x69;
            BinaryPrimitives.WriteUInt32LittleEndian(
                decoded.AsSpan(45), 3954);
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(49), 1);
            decoded[51] = 0x41;
            BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(54), 7);
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
