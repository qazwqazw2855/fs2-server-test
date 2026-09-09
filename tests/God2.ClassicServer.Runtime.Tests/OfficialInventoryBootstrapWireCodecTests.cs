using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialInventoryBootstrapWireCodecTests
{
    [Fact]
    public void Exact_stage_five_purchase_shape_restores_the_verified_authority_slot()
    {
        var snapshot = Snapshot([Slot(0, OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId, 1)]);

        var result = OfficialInventoryBootstrapWireCodec.Serialize(
            OfficialInventoryBootstrapWireCodec.ClientBuildId,
            snapshot,
            walletBalance: 960);

        Assert.True(result.Succeeded, result.FailureCode);
        var frame = Assert.Single(result.DecodedFrames).Span;
        Assert.Equal(OfficialMerchantTransactionWireCodec.PurchaseResultFrameLength, frame.Length);
        Assert.Equal(OfficialMerchantTransactionWireCodec.PurchaseResultOpcode, frame[2]);
        Assert.Equal((uint)OfficialMerchantTransactionWireCodec.LiveStageClientItemId, BitConverter.ToUInt32(frame[7..11]));
        Assert.Equal(960u, BitConverter.ToUInt32(frame[40..44]));
        Assert.Equal((uint)OfficialMerchantTransactionWireCodec.LiveStageMerchantHandle, BitConverter.ToUInt32(frame[45..49]));
        Assert.Equal(1, BitConverter.ToUInt16(frame[49..51]));
        Assert.Equal(OfficialInventoryBootstrapWireCodec.VerifiedMerchantCatalogIndex, BitConverter.ToUInt16(frame[54..56]));
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(frame), frame[^1]);
    }

    [Fact]
    public void Empty_inventory_emits_no_restore_frame()
    {
        var result = OfficialInventoryBootstrapWireCodec.Serialize(
            OfficialInventoryBootstrapWireCodec.ClientBuildId,
            Snapshot([]),
            walletBalance: 0);

        Assert.True(result.Succeeded);
        Assert.Empty(result.DecodedFrames);
    }

    [Theory]
    [InlineData(1, OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId, 1)]
    [InlineData(0, 123, 1)]
    [InlineData(0, OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId, 2)]
    public void Unverified_slot_item_and_quantity_shapes_fail_closed(int slot, int item, int quantity)
    {
        var result = OfficialInventoryBootstrapWireCodec.Serialize(
            OfficialInventoryBootstrapWireCodec.ClientBuildId,
            Snapshot([Slot(slot, item, quantity)]),
            walletBalance: 960);

        Assert.Equal(OfficialInventoryBootstrapWireResultCode.UnsupportedSnapshot, result.Code);
        Assert.Equal("wire.inventory_bootstrap.slot_evidence_blocked", result.FailureCode);
        Assert.Empty(result.DecodedFrames);
    }

    [Fact]
    public void Multiple_inventory_items_fail_closed_until_a_complete_layout_is_verified()
    {
        var result = OfficialInventoryBootstrapWireCodec.Serialize(
            OfficialInventoryBootstrapWireCodec.ClientBuildId,
            Snapshot([
                Slot(0, OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId, 1),
                Slot(1, OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId, 1)
            ]),
            walletBalance: 960);

        Assert.Equal(OfficialInventoryBootstrapWireResultCode.UnsupportedSnapshot, result.Code);
        Assert.Equal("wire.inventory_bootstrap.layout_evidence_blocked", result.FailureCode);
    }

    [Fact]
    public void Exact_stage_six_client_slot_maps_to_the_authoritative_purchase_slot()
    {
        Assert.True(OfficialInventoryBootstrapWireCodec.TryMapClientSaleSlotToAuthority(4, out var authoritySlot));
        Assert.Equal(0, authoritySlot);
        Assert.False(OfficialInventoryBootstrapWireCodec.TryMapClientSaleSlotToAuthority(0, out authoritySlot));
        Assert.Equal(-1, authoritySlot);
    }

    private static PlayerInventorySnapshot Snapshot(IReadOnlyList<InventorySlot> slots) =>
        new(Guid.Parse("bd5a433d-17ee-4cbb-b1f7-ff6da286cad4"), 9, 32, 1, 1, "Clean", slots);

    private static InventorySlot Slot(int slot, int item, int quantity) =>
        new(
            slot,
            100 + slot,
            200 + slot,
            item,
            item,
            quantity,
            "Unbound",
            "{}",
            DateTimeOffset.Parse("2026-08-13T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-13T00:00:00Z"),
            1);
}
