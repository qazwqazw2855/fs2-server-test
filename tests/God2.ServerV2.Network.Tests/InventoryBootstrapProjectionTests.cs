using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class InventoryBootstrapProjectionTests
{
    private static CharacterInventorySnapshot Snapshot(
        IReadOnlyList<CharacterInventorySlot> slots) =>
        new(Guid.NewGuid(), 7, 8, 1, 1, "Clean", slots);

    [Fact]
    public void RestoreUsesCurrentWalletAndExistingRestrictedWireProfile()
    {
        var inventory = Snapshot(
            [new CharacterInventorySlot(0, 253231541, 1)]);
        Assert.True(InventoryBootstrapProjection.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            7, inventory, 60, out var frame, out var failure), failure);
        Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            new(3954, 6901, 1,
                OfficialMerchantTransactionOperation.Buy, 7),
            60, out var expected, out _));
        Assert.Equal(expected, frame);
        Assert.Equal(1, inventory.Version);
        Assert.Equal(1, inventory.MutationSequence);
        Assert.Single(inventory.Slots);
    }

    [Fact]
    public void EmptyInventoryProducesNoFrame()
    {
        Assert.True(InventoryBootstrapProjection.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            7, Snapshot([]), 100, out var frame, out var failure), failure);
        Assert.Null(frame);
    }

    [Theory]
    [InlineData(1, 253231541, 1)]
    [InlineData(0, 123, 1)]
    [InlineData(0, 253231541, 2)]
    public void UnprovenSlotItemOrQuantityIsBlocked(
        int slot, long item, int quantity)
    {
        Assert.False(InventoryBootstrapProjection.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            7, Snapshot([new(slot, item, quantity)]),
            60, out var frame, out var failure));
        Assert.Null(frame);
        Assert.Equal("InventorySlotEvidenceBlocked", failure);
    }

    [Fact]
    public void MultipleItemsAreBlocked()
    {
        Assert.False(InventoryBootstrapProjection.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            7, Snapshot([
                new(0, 253231541, 1),
                new(1, 253231541, 1)]),
            60, out var frame, out var failure));
        Assert.Null(frame);
        Assert.Equal("InventoryLayoutEvidenceBlocked", failure);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4294967296L)]
    public void UnrepresentableWalletIsBlocked(long balance)
    {
        Assert.False(InventoryBootstrapProjection.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            7, Snapshot([]), balance, out var frame, out var failure));
        Assert.Null(frame);
        Assert.Equal("WalletBalanceOutOfRange", failure);
    }

    [Fact]
    public void WrongCharacterIsBlocked()
    {
        Assert.False(InventoryBootstrapProjection.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            8, Snapshot([]), 60, out var frame, out var failure));
        Assert.Null(frame);
        Assert.Equal("InventorySnapshotMismatch", failure);
    }

    [Fact]
    public void WrongBuildIsBlocked()
    {
        Assert.False(InventoryBootstrapProjection.TryEncode(
            "other-build", 7, Snapshot([]), 60,
            out var frame, out var failure));
        Assert.Null(frame);
        Assert.Equal("BuildMismatch", failure);
    }
}
