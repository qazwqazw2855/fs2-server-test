using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialMerchantTransactionWireTests
{
    private const string Build = OfficialMerchantTransactionWireCodec.ClientBuildId;

    [Fact]
    public void Stage_four_selection_decodes_the_live_frame()
    {
        var result = OfficialMerchantTransactionWireCodec.DecodeSelection(
            Build,
            GameplayProtocolState.World,
            Convert.FromHexString("0A0085720F000000EE1A"));

        Assert.True(result.Succeeded);
        Assert.Equal(3954, result.Value!.ClientEntityHandle);
        Assert.Equal(0, result.Value.Selector);
        Assert.Equal(0, result.Value.PreservedWord);
        Assert.Equal(0xEE, result.Value.PreservedByte);
    }

    [Fact]
    public void Stage_four_dialog_and_shop_projections_replay_the_live_frames()
    {
        var dialog = OfficialMerchantTransactionWireCodec.SerializeObservedDialogOpen(Build, 3954);
        var shop = OfficialMerchantTransactionWireCodec.SerializeShopOpen(Build, 3954);

        Assert.True(dialog.Succeeded);
        Assert.Equal(
            "1F007A1C000400720F900000000000000000000000000000000000000000D2",
            Convert.ToHexString(dialog.Value!.DecodedFrame.Span));
        Assert.True(shop.Succeeded);
        Assert.Equal(
            "100068720FA000000000006400000081",
            Convert.ToHexString(shop.Value!.DecodedFrame.Span));
    }

    [Fact]
    public void Merchant_profile_is_not_reused_for_unobserved_npc_handles()
    {
        var dialog = OfficialMerchantTransactionWireCodec.SerializeObservedDialogOpen(Build, 4000);
        var shop = OfficialMerchantTransactionWireCodec.SerializeShopOpen(Build, 4000);

        Assert.False(dialog.Succeeded);
        Assert.Equal("wire.merchant.dialog_profile_evidence_blocked", dialog.FailureCode);
        Assert.False(shop.Succeeded);
        Assert.Equal("wire.merchant.shop_profile_evidence_blocked", shop.FailureCode);
    }

    [Theory]
    [InlineData(
        "0C0038720FF51A0101070071",
        OfficialMerchantOperation.Buy,
        7)]
    [InlineData(
        "0C0038720FF51A010204006F",
        OfficialMerchantOperation.Sell,
        4)]
    public void Stage_five_and_six_transactions_decode_the_live_frames(
        string hex,
        OfficialMerchantOperation operation,
        ushort expectedIndex)
    {
        var result = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            Convert.FromHexString(hex));

        Assert.True(result.Succeeded);
        Assert.Equal(3954, result.Value!.ClientEntityHandle);
        Assert.Equal(6901, result.Value.ClientItemId);
        Assert.Equal(1, result.Value.Quantity);
        Assert.Equal(operation, result.Value.Operation);
        Assert.Equal(expectedIndex, result.Value.ItemIndexOrInventorySlot);
    }

    [Fact]
    public void Stage_five_purchase_success_replays_the_live_frame()
    {
        var request = Decode("0C0038720FF51A0101070071");
        var result = OfficialMerchantTransactionWireCodec.SerializeSuccess(
            Build,
            request,
            Success(currencyBefore: 50000, currencyAfter: 25000));

        Assert.True(result.Succeeded);
        Assert.Equal(
            "39003B01040000F51A00000000000000000000000000000000000000000000000000000000000027A861000069720F0000010041000007000B",
            Convert.ToHexString(result.Value!.DecodedFrame.Span));
    }

    [Fact]
    public void Stage_six_sale_success_replays_the_live_frame()
    {
        var request = Decode("0C0038720FF51A010204006F");
        var result = OfficialMerchantTransactionWireCodec.SerializeSuccess(
            Build,
            request,
            Success(currencyBefore: 25000, currencyAfter: 25004));

        Assert.True(result.Succeeded);
        Assert.Equal(
            "1900410000040027AC61000069720F00000100410000040062",
            Convert.ToHexString(result.Value!.DecodedFrame.Span));
    }

    [Fact]
    public void Transaction_maps_to_the_authoritative_inventory_backend_contract()
    {
        var decoded = Decode("0C0038720FF51A010204006F");
        var transactionId = Guid.Parse("6994990e-5440-42ec-9f44-27128898a309");
        var createdAt = DateTimeOffset.Parse("2026-08-12T00:00:00Z");

        var request = OfficialMerchantTransactionWireCodec.ToInventoryTransactionRequest(
            decoded,
            transactionId,
            characterId: 20,
            sessionId: "session",
            accountId: 10,
            expectedInventoryVersion: 4,
            merchantTemplateId: 4001,
            ingressOrdinal: 7,
            createdAt);

        Assert.Equal(transactionId, request.TransactionId);
        Assert.Equal(InventoryOperationType.MerchantSell, request.OperationType);
        Assert.Equal(4001, request.MerchantTemplateId);
        Assert.Equal(0, Assert.Single(request.RequestedMutations).SourceSlotIndex);
        Assert.Equal(253231541, request.RequestedMutations[0].ItemTemplateId);
        Assert.Equal(1, request.RequestedMutations[0].Quantity);
        Assert.StartsWith("merchant-wire:", request.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutated_or_unsupported_transaction_frames_fail_closed()
    {
        var checksumMutation = Convert.FromHexString("0C0038720FF51A0101070071");
        checksumMutation[^1] ^= 1;
        var operationMutation = Convert.FromHexString("0C0038720FF51A0101070071");
        operationMutation[8] = 3;
        operationMutation[^1] = OfficialLoginWireTransform.ComputeChecksum(operationMutation);

        var checksum = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            checksumMutation);
        var operation = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            operationMutation);

        Assert.Equal(OfficialMerchantWireResultCode.Malformed, checksum.Code);
        Assert.Equal(OfficialMerchantWireResultCode.UnsupportedOperation, operation.Code);
    }

    [Theory]
    [InlineData("0C0038720FF51A0101060072")]
    [InlineData("0C0038720FF51A0102030070")]
    public void Unverified_exact_item_catalog_or_sale_slots_fail_closed(string hex)
    {
        var frame = Convert.FromHexString(hex);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);

        var result = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            frame);

        Assert.Equal(OfficialMerchantWireResultCode.UnsupportedSelection, result.Code);
        Assert.Equal("wire.merchant.transaction_selection_evidence_blocked", result.FailureCode);
    }

    [Theory]
    [InlineData("0C0038720FF51A0201070070")]
    [InlineData("0C0038720FF51A020204006E")]
    public void Transaction_quantities_other_than_one_fail_closed(string hex)
    {
        var frame = Convert.FromHexString(hex);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);

        var result = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            frame);

        Assert.Equal(OfficialMerchantWireResultCode.UnsupportedSelection, result.Code);
        Assert.Equal("wire.merchant.transaction_quantity_evidence_blocked", result.FailureCode);
    }

    [Theory]
    [InlineData(3955, 6901)]
    [InlineData(3954, 6902)]
    public void Transaction_identity_outside_the_verified_3954_6901_profile_fails_closed(
        ushort handle,
        ushort clientItemId)
    {
        var frame = Convert.FromHexString("0C0038720FF51A0101070071");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), handle);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), clientItemId);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);

        var result = OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            frame);

        Assert.Equal(OfficialMerchantWireResultCode.UnsupportedSelection, result.Code);
        Assert.Equal("wire.merchant.transaction_profile_evidence_blocked", result.FailureCode);
    }

    [Fact]
    public void Failure_results_emit_no_unobserved_protocol_bytes()
    {
        var request = Decode("0C0038720FF51A0101070071");
        var failure = new InventoryTransactionResult(
            InventoryTransactionResultCode.InsufficientCurrency,
            Guid.NewGuid(),
            "failure",
            1,
            1,
            10,
            10,
            "currency.insufficient");

        var result = OfficialMerchantTransactionWireCodec.SerializeSuccess(Build, request, failure);

        Assert.Equal(OfficialMerchantWireResultCode.UnsupportedResult, result.Code);
        Assert.Null(result.Value);
    }

    private static OfficialMerchantTransactionRequest Decode(string hex) =>
        OfficialMerchantTransactionWireCodec.DecodeTransaction(
            Build,
            GameplayProtocolState.World,
            Convert.FromHexString(hex)).Value!;

    private static InventoryTransactionResult Success(long currencyBefore, long currencyAfter) =>
        new(
            InventoryTransactionResultCode.Success,
            Guid.Parse("de4dcb09-cff5-4277-9dc4-d1fb41a2726c"),
            "success",
            1,
            2,
            currencyBefore,
            currencyAfter);
}
