using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialMerchantTransactionCode
{
    ShopOpened,
    PurchaseCommitted,
    SaleCommitted,
    Rejected
}

public sealed record OfficialMerchantTransactionReceipt(
    OfficialMerchantTransactionCode Code,
    string FailureCode,
    long IngressOrdinal,
    ushort ClientEntityHandle,
    int? MerchantTemplateId,
    int? ItemTemplateId,
    int Quantity,
    InventoryTransactionResultCode? InventoryResult,
    long? CurrencyAfter,
    string RequestSha256,
    ReadOnlyMemory<byte> EncodedResponse,
    bool RuntimeValidated,
    bool NetworkBytesEmitted);

/// <summary>
/// Production bridge from the capture-backed Merchant wire family to the authoritative
/// inventory coordinator. It requires a matching active Merchant interaction and emits
/// bytes only for the two observed successful result shapes.
/// </summary>
public sealed class OfficialMerchantTransactionClosedLoop
{
    private readonly OfficialNpcInteractionClosedLoop _interactions;
    private readonly IInventoryTransactionCoordinator _inventory;

    public OfficialMerchantTransactionClosedLoop(
        OfficialNpcInteractionClosedLoop interactions,
        IInventoryTransactionCoordinator inventory)
    {
        _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public static bool RecognizesEncodedFrame(ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length is not OfficialMerchantTransactionWireCodec.SelectionFrameLength and
            not OfficialMerchantTransactionWireCodec.TransactionFrameLength ||
            encodedFrame.Length < 3 || encodedFrame[0] != encodedFrame.Length || encodedFrame[1] != 0)
        {
            return false;
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame);
        try
        {
            return decoded[2] is OfficialMerchantTransactionWireCodec.SelectionOpcode or
                OfficialMerchantTransactionWireCodec.TransactionOpcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public async Task<OfficialMerchantTransactionReceipt> ExecuteFrameAsync(
        string sessionId,
        long ingressOrdinal,
        ReadOnlyMemory<byte> encodedFrame,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (ingressOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ingressOrdinal));
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame.Span);
        try
        {
            var requestHash = Convert.ToHexString(SHA256.HashData(decoded));
            if (decoded.Length < 3)
            {
                return Failure(ingressOrdinal, requestHash, "wire.merchant.frame_too_short");
            }

            return decoded[2] == OfficialMerchantTransactionWireCodec.SelectionOpcode
                ? ExecuteSelection(sessionId, ingressOrdinal, requestHash, decoded)
                : await ExecuteTransactionAsync(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    decoded,
                    cancellationToken);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    private OfficialMerchantTransactionReceipt ExecuteSelection(
        string sessionId,
        long ingressOrdinal,
        string requestHash,
        ReadOnlySpan<byte> decodedFrame)
    {
        var decoded = OfficialMerchantTransactionWireCodec.DecodeSelection(
            OfficialMerchantTransactionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedFrame);
        if (!decoded.Succeeded || decoded.Value is null)
        {
            return Failure(ingressOrdinal, requestHash, decoded.FailureCode);
        }

        var wire = decoded.Value;
        var active = _interactions.ResolveActiveMerchant(sessionId, wire.ClientEntityHandle);
        if (!active.Succeeded || active.Value is null)
        {
            return Failure(ingressOrdinal, requestHash, active.Error.Code, wire.ClientEntityHandle);
        }

        var projection = OfficialMerchantTransactionWireCodec.SerializeShopOpen(
            OfficialMerchantTransactionWireCodec.ClientBuildId,
            wire.ClientEntityHandle);
        if (!projection.Succeeded || projection.Value is null)
        {
            return Failure(ingressOrdinal, requestHash, projection.FailureCode, wire.ClientEntityHandle);
        }

        return new OfficialMerchantTransactionReceipt(
            OfficialMerchantTransactionCode.ShopOpened,
            string.Empty,
            ingressOrdinal,
            wire.ClientEntityHandle,
            active.Value.MerchantTemplateId,
            null,
            0,
            null,
            null,
            requestHash,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(projection.Value.DecodedFrame.Span),
            RuntimeValidated: true,
            NetworkBytesEmitted: false);
    }

    private async Task<OfficialMerchantTransactionReceipt> ExecuteTransactionAsync(
        string sessionId,
        long ingressOrdinal,
        string requestHash,
        ReadOnlyMemory<byte> decodedFrame,
        CancellationToken cancellationToken)
    {
        var decoded = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            OfficialMerchantTransactionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedFrame.Span);
        if (!decoded.Succeeded || decoded.Value is null)
        {
            return Failure(ingressOrdinal, requestHash, decoded.FailureCode);
        }

        var wire = decoded.Value;
        if (!OfficialMerchantTransactionWireCodec.TryResolveCanonicalItemId(
                wire.ClientItemId,
                out var canonicalItemId))
        {
            return Failure(
                ingressOrdinal,
                requestHash,
                "wire.merchant.client_item_identity_missing",
                wire.ClientEntityHandle);
        }

        var active = _interactions.ResolveActiveMerchant(sessionId, wire.ClientEntityHandle);
        if (!active.Succeeded || active.Value is null)
        {
            return Failure(ingressOrdinal, requestHash, active.Error.Code, wire.ClientEntityHandle);
        }

        var context = active.Value;
        var inventory = await _inventory.LoadInventoryAsync(context.CharacterId, cancellationToken);
        if (!inventory.Succeeded || inventory.Value is null)
        {
            return Failure(
                ingressOrdinal,
                requestHash,
                inventory.Succeeded ? "inventory.snapshot_missing" : inventory.Error.Code,
                wire.ClientEntityHandle,
                context.MerchantTemplateId,
                canonicalItemId,
                wire.Quantity);
        }

        var snapshot = inventory.Value;
        if (snapshot.CharacterId != context.CharacterId || snapshot.Version < 0)
        {
            return Failure(
                ingressOrdinal,
                requestHash,
                "inventory.snapshot_identity_invalid",
                wire.ClientEntityHandle,
                context.MerchantTemplateId,
                canonicalItemId,
                wire.Quantity);
        }

        var inventoryStateSupported = OfficialInventoryBootstrapWireCodec.Validate(snapshot) is null &&
            (wire.Operation == OfficialMerchantOperation.Buy
                ? snapshot.Slots.Count == 0
                : snapshot.Slots.Count == 1);
        if (!inventoryStateSupported)
        {
            return Failure(
                ingressOrdinal,
                requestHash,
                wire.Operation == OfficialMerchantOperation.Buy
                    ? "wire.merchant.buy_inventory_state_evidence_blocked"
                    : "wire.merchant.sell_inventory_state_evidence_blocked",
                wire.ClientEntityHandle,
                context.MerchantTemplateId,
                canonicalItemId,
                wire.Quantity);
        }

        var transaction = OfficialMerchantTransactionWireCodec.ToInventoryTransactionRequest(
            wire,
            DeterministicGuid($"{sessionId}|{ingressOrdinal}|{requestHash}"),
            context.CharacterId,
            sessionId,
            context.AccountId,
            snapshot.Version,
            context.MerchantTemplateId,
            ingressOrdinal,
            DateTimeOffset.UtcNow);
        var result = await _inventory.ExecuteAsync(transaction, cancellationToken);
        if (!result.Succeeded)
        {
            return Failure(
                ingressOrdinal,
                requestHash,
                string.IsNullOrWhiteSpace(result.FailureCode)
                    ? "inventory.transaction_rejected"
                    : result.FailureCode,
                wire.ClientEntityHandle,
                context.MerchantTemplateId,
                canonicalItemId,
                wire.Quantity,
                result.Code,
                result.CurrencyAfter);
        }

        var projection = OfficialMerchantTransactionWireCodec.SerializeSuccess(
            OfficialMerchantTransactionWireCodec.ClientBuildId,
            wire,
            result);
        if (!projection.Succeeded || projection.Value is null)
        {
            return Failure(
                ingressOrdinal,
                requestHash,
                projection.FailureCode,
                wire.ClientEntityHandle,
                context.MerchantTemplateId,
                canonicalItemId,
                wire.Quantity,
                result.Code,
                result.CurrencyAfter);
        }

        return new OfficialMerchantTransactionReceipt(
            wire.Operation == OfficialMerchantOperation.Buy
                ? OfficialMerchantTransactionCode.PurchaseCommitted
                : OfficialMerchantTransactionCode.SaleCommitted,
            string.Empty,
            ingressOrdinal,
            wire.ClientEntityHandle,
            context.MerchantTemplateId,
            canonicalItemId,
            wire.Quantity,
            result.Code,
            result.CurrencyAfter,
            requestHash,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(projection.Value.DecodedFrame.Span),
            RuntimeValidated: true,
            NetworkBytesEmitted: false);
    }

    private static OfficialMerchantTransactionReceipt Failure(
        long ingressOrdinal,
        string requestHash,
        string failureCode,
        ushort clientEntityHandle = 0,
        int? merchantTemplateId = null,
        int? itemTemplateId = null,
        int quantity = 0,
        InventoryTransactionResultCode? inventoryResult = null,
        long? currencyAfter = null) =>
        new(
            OfficialMerchantTransactionCode.Rejected,
            failureCode,
            ingressOrdinal,
            clientEntityHandle,
            merchantTemplateId,
            itemTemplateId,
            quantity,
            inventoryResult,
            currencyAfter,
            requestHash,
            ReadOnlyMemory<byte>.Empty,
            RuntimeValidated: false,
            NetworkBytesEmitted: false);

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
