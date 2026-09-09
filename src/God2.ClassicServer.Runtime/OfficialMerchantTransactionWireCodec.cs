using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialMerchantWireResultCode
{
    Success,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    Malformed,
    UnsupportedOperation,
    UnsupportedSelection,
    UnsupportedResult
}

public sealed record OfficialMerchantWireResult<T>(
    OfficialMerchantWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == OfficialMerchantWireResultCode.Success;

    public static OfficialMerchantWireResult<T> Success(T value) =>
        new(OfficialMerchantWireResultCode.Success, value, string.Empty);

    public static OfficialMerchantWireResult<T> Failure(
        OfficialMerchantWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public enum OfficialMerchantOperation : byte
{
    Buy = 1,
    Sell = 2
}

public sealed record OfficialMerchantSelectionRequest(
    ushort ClientEntityHandle,
    byte Selector,
    ushort PreservedWord,
    byte PreservedByte,
    string DecodedFrameSha256,
    string EvidenceId);

public sealed record OfficialMerchantTransactionRequest(
    ushort ClientEntityHandle,
    ushort ClientItemId,
    byte Quantity,
    OfficialMerchantOperation Operation,
    ushort ItemIndexOrInventorySlot,
    string DecodedFrameSha256,
    string EvidenceId);

public sealed record OfficialMerchantWireProjection(
    ReadOnlyMemory<byte> DecodedFrame,
    byte Opcode,
    ushort ClientEntityHandle,
    string DecodedFrameSha256,
    string EvidenceId);

/// <summary>
/// Exact-build merchant wire contract recovered from controlled live Stages 4-7.
/// Unknown fields remain capture-pinned; the correlated handle/item identity and
/// observed quantity-one slot/index shapes are fixed while wallet balance is authoritative.
/// </summary>
public static class OfficialMerchantTransactionWireCodec
{
    public const string ClientBuildId = OfficialNpcInteractionWireCodec.ClientBuildId;
    public const string EvidenceId = "LiveRecovery/Stages4-7-attempt-759-trace";
    public const byte SelectionOpcode = 0x85;
    public const byte ShopOpenOpcode = 0x68;
    public const byte TransactionOpcode = 0x38;
    public const byte PurchaseResultOpcode = 0x3B;
    public const byte SaleResultOpcode = 0x41;
    public const int SelectionFrameLength = 10;
    public const int ShopOpenFrameLength = 16;
    public const int TransactionFrameLength = 12;
    public const int PurchaseResultFrameLength = 57;
    public const int SaleResultFrameLength = 25;
    public const ushort LiveStageMerchantHandle = 3954;
    public const int LiveStageClientItemId = 6901;
    public const int LiveStageCanonicalItemId = 253231541;

    public static OfficialMerchantWireResult<OfficialMerchantSelectionRequest> DecodeSelection(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        var envelope = ValidateInbound(
            clientBuildId,
            state,
            decodedFrame,
            SelectionFrameLength,
            SelectionOpcode,
            "selection");
        if (envelope is not null)
        {
            return OfficialMerchantWireResult<OfficialMerchantSelectionRequest>.Failure(
                envelope.Value.Code,
                envelope.Value.FailureCode);
        }

        var handle = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        if (handle == 0)
        {
            return OfficialMerchantWireResult<OfficialMerchantSelectionRequest>.Failure(
                OfficialMerchantWireResultCode.Malformed,
                "wire.merchant.selection_target_invalid");
        }

        return OfficialMerchantWireResult<OfficialMerchantSelectionRequest>.Success(
            new OfficialMerchantSelectionRequest(
                handle,
                decodedFrame[5],
                BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[6..]),
                decodedFrame[8],
                Hash(decodedFrame),
                EvidenceId));
    }

    public static OfficialMerchantWireResult<OfficialMerchantTransactionRequest> DecodeTransaction(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        var envelope = ValidateInbound(
            clientBuildId,
            state,
            decodedFrame,
            TransactionFrameLength,
            TransactionOpcode,
            "transaction");
        if (envelope is not null)
        {
            return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Failure(
                envelope.Value.Code,
                envelope.Value.FailureCode);
        }

        var handle = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]);
        var clientItemId = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[5..]);
        var quantity = decodedFrame[7];
        var operationValue = decodedFrame[8];
        var index = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[9..]);
        if (handle == 0 || clientItemId == 0 || quantity == 0)
        {
            return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Failure(
                OfficialMerchantWireResultCode.Malformed,
                "wire.merchant.transaction_fields_invalid");
        }

        if (handle != LiveStageMerchantHandle || clientItemId != LiveStageClientItemId)
        {
            return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Failure(
                OfficialMerchantWireResultCode.UnsupportedSelection,
                "wire.merchant.transaction_profile_evidence_blocked");
        }

        if (quantity != 1)
        {
            return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Failure(
                OfficialMerchantWireResultCode.UnsupportedSelection,
                "wire.merchant.transaction_quantity_evidence_blocked");
        }

        if (operationValue is not (byte)OfficialMerchantOperation.Buy and
            not (byte)OfficialMerchantOperation.Sell)
        {
            return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Failure(
                OfficialMerchantWireResultCode.UnsupportedOperation,
                "wire.merchant.transaction_operation_unsupported");
        }

        if ((operationValue == (byte)OfficialMerchantOperation.Buy &&
             index != OfficialInventoryBootstrapWireCodec.VerifiedMerchantCatalogIndex) ||
            (operationValue == (byte)OfficialMerchantOperation.Sell &&
             index != OfficialInventoryBootstrapWireCodec.VerifiedClientSaleSlot))
        {
            return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Failure(
                OfficialMerchantWireResultCode.UnsupportedSelection,
                "wire.merchant.transaction_selection_evidence_blocked");
        }

        return OfficialMerchantWireResult<OfficialMerchantTransactionRequest>.Success(
            new OfficialMerchantTransactionRequest(
                handle,
                clientItemId,
                quantity,
                (OfficialMerchantOperation)operationValue,
                index,
                Hash(decodedFrame),
                EvidenceId));
    }

    public static InventoryTransactionRequest ToInventoryTransactionRequest(
        OfficialMerchantTransactionRequest request,
        Guid transactionId,
        long characterId,
        string sessionId,
        long accountId,
        long expectedInventoryVersion,
        int merchantTemplateId,
        long ingressOrdinal,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mutation = request.Operation == OfficialMerchantOperation.Sell
            ? new InventoryMutationRequest(
                SourceSlotIndex: ResolveAuthoritySaleSlot(request),
                ItemTemplateId: ResolveCanonicalItemId(request.ClientItemId),
                Quantity: request.Quantity)
            : new InventoryMutationRequest(
                ItemTemplateId: ResolveCanonicalItemId(request.ClientItemId),
                Quantity: request.Quantity);

        return new InventoryTransactionRequest(
            transactionId,
            $"merchant-wire:{characterId}:{sessionId}:{ingressOrdinal}:{request.DecodedFrameSha256}",
            characterId,
            sessionId,
            accountId,
            request.Operation == OfficialMerchantOperation.Buy
                ? InventoryOperationType.MerchantBuy
                : InventoryOperationType.MerchantSell,
            expectedInventoryVersion,
            EvidenceId,
            [mutation],
            0,
            merchantTemplateId,
            createdAtUtc);
    }

    private static int ResolveAuthoritySaleSlot(OfficialMerchantTransactionRequest request)
    {
        if (request.ClientItemId == LiveStageClientItemId &&
            OfficialInventoryBootstrapWireCodec.TryMapClientSaleSlotToAuthority(
                request.ItemIndexOrInventorySlot,
                out var authoritySlot))
        {
            return authoritySlot;
        }

        throw new InvalidOperationException(
            $"No capture-backed authority slot mapping exists for Client item {request.ClientItemId} slot {request.ItemIndexOrInventorySlot}.");
    }

    public static OfficialMerchantWireResult<OfficialMerchantWireProjection> SerializeObservedDialogOpen(
        string clientBuildId,
        ushort clientEntityHandle)
    {
        var buildFailure = ValidateBuild(clientBuildId, "dialog");
        if (buildFailure is not null)
        {
            return OfficialMerchantWireResult<OfficialMerchantWireProjection>.Failure(
                buildFailure.Value.Code,
                buildFailure.Value.FailureCode);
        }

        if (!HasVerifiedObservedDialogProfile(clientEntityHandle))
        {
            return InvalidProjection("wire.merchant.dialog_profile_evidence_blocked");
        }

        var frame = new byte[31];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = OfficialNpcInteractionWireCodec.DialogResultOpcode;
        frame[3] = 28;
        frame[5] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), clientEntityHandle);
        frame[9] = 0x90;
        FinalizeChecksum(frame);
        return Projection(frame, clientEntityHandle);
    }

    public static bool HasVerifiedObservedDialogProfile(ushort clientEntityHandle) =>
        clientEntityHandle == LiveStageMerchantHandle;

    public static OfficialMerchantWireResult<OfficialMerchantWireProjection> SerializeShopOpen(
        string clientBuildId,
        ushort clientEntityHandle)
    {
        var buildFailure = ValidateBuild(clientBuildId, "shop_open");
        if (buildFailure is not null)
        {
            return OfficialMerchantWireResult<OfficialMerchantWireProjection>.Failure(
                buildFailure.Value.Code,
                buildFailure.Value.FailureCode);
        }

        if (!HasVerifiedObservedDialogProfile(clientEntityHandle))
        {
            return InvalidProjection("wire.merchant.shop_profile_evidence_blocked");
        }

        var frame = new byte[ShopOpenFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = ShopOpenOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), clientEntityHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(5), 160);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(11), 100);
        FinalizeChecksum(frame);
        return Projection(frame, clientEntityHandle);
    }

    public static OfficialMerchantWireResult<OfficialMerchantWireProjection> SerializeSuccess(
        string clientBuildId,
        OfficialMerchantTransactionRequest request,
        InventoryTransactionResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        var buildFailure = ValidateBuild(clientBuildId, "transaction_result");
        if (buildFailure is not null)
        {
            return OfficialMerchantWireResult<OfficialMerchantWireProjection>.Failure(
                buildFailure.Value.Code,
                buildFailure.Value.FailureCode);
        }

        if (!result.Succeeded || result.CurrencyAfter is < 0 or > uint.MaxValue)
        {
            return OfficialMerchantWireResult<OfficialMerchantWireProjection>.Failure(
                OfficialMerchantWireResultCode.UnsupportedResult,
                "wire.merchant.result_not_capture_proven");
        }

        if (!HasVerifiedObservedDialogProfile(request.ClientEntityHandle))
        {
            return InvalidProjection("wire.merchant.transaction_profile_evidence_blocked");
        }

        return request.Operation == OfficialMerchantOperation.Buy
            ? Projection(BuildPurchaseResult(request, checked((uint)result.CurrencyAfter)), request.ClientEntityHandle)
            : Projection(BuildSaleResult(request, checked((uint)result.CurrencyAfter)), request.ClientEntityHandle);
    }

    private static byte[] BuildPurchaseResult(OfficialMerchantTransactionRequest request, uint walletBalance)
    {
        var frame = new byte[PurchaseResultFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, PurchaseResultFrameLength);
        frame[2] = PurchaseResultOpcode;
        frame[3] = 1;
        frame[4] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), request.ClientItemId);
        frame[39] = 0x27;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40), walletBalance);
        frame[44] = 0x69;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(45), request.ClientEntityHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(49), request.Quantity);
        frame[51] = 0x41;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(54), request.ItemIndexOrInventorySlot);
        FinalizeChecksum(frame);
        return frame;
    }

    private static byte[] BuildSaleResult(OfficialMerchantTransactionRequest request, uint walletBalance)
    {
        var frame = new byte[SaleResultFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, SaleResultFrameLength);
        frame[2] = SaleResultOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), 4);
        frame[7] = 0x27;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), walletBalance);
        frame[12] = 0x69;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(13), request.ClientEntityHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(17), request.Quantity);
        frame[19] = 0x41;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(22), request.ItemIndexOrInventorySlot);
        FinalizeChecksum(frame);
        return frame;
    }

    public static bool TryResolveCanonicalItemId(ushort clientItemId, out int canonicalItemId)
    {
        canonicalItemId = clientItemId == LiveStageClientItemId
            ? LiveStageCanonicalItemId
            : 0;
        return canonicalItemId > 0;
    }

    public static int ResolveCanonicalItemId(ushort clientItemId) =>
        TryResolveCanonicalItemId(clientItemId, out var canonicalItemId)
            ? canonicalItemId
            : throw new InvalidOperationException(
                $"No capture-backed canonical item identity exists for Client item {clientItemId}.");

    private static (OfficialMerchantWireResultCode Code, string FailureCode)? ValidateInbound(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame,
        int expectedLength,
        byte expectedOpcode,
        string family)
    {
        var buildFailure = ValidateBuild(clientBuildId, family);
        if (buildFailure is not null)
        {
            return buildFailure;
        }

        if (state != GameplayProtocolState.World)
        {
            return (OfficialMerchantWireResultCode.InvalidState, $"wire.merchant.{family}_state_invalid");
        }

        if (decodedFrame.Length != expectedLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != expectedLength)
        {
            return (OfficialMerchantWireResultCode.InvalidLength, $"wire.merchant.{family}_length_invalid");
        }

        if (decodedFrame[2] != expectedOpcode)
        {
            return (OfficialMerchantWireResultCode.InvalidOpcode, $"wire.merchant.{family}_opcode_invalid");
        }

        return decodedFrame[^1] == OfficialLoginWireTransform.ComputeChecksum(decodedFrame)
            ? null
            : (OfficialMerchantWireResultCode.Malformed, $"wire.merchant.{family}_integrity_invalid");
    }

    private static (OfficialMerchantWireResultCode Code, string FailureCode)? ValidateBuild(
        string clientBuildId,
        string family) =>
        string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal)
            ? null
            : (OfficialMerchantWireResultCode.BuildMismatch, $"wire.merchant.{family}_build_mismatch");

    private static OfficialMerchantWireResult<OfficialMerchantWireProjection> Projection(
        byte[] frame,
        ushort clientEntityHandle) =>
        OfficialMerchantWireResult<OfficialMerchantWireProjection>.Success(
            new OfficialMerchantWireProjection(
                frame,
                frame[2],
                clientEntityHandle,
                Hash(frame),
                EvidenceId));

    private static OfficialMerchantWireResult<OfficialMerchantWireProjection> InvalidProjection(string failureCode) =>
        OfficialMerchantWireResult<OfficialMerchantWireProjection>.Failure(
            OfficialMerchantWireResultCode.Malformed,
            failureCode);

    private static void FinalizeChecksum(Span<byte> frame) =>
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);

    private static string Hash(ReadOnlySpan<byte> frame) =>
        Convert.ToHexString(SHA256.HashData(frame));
}
