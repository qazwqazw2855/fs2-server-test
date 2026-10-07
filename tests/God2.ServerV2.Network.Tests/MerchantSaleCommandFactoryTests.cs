using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantSaleCommandFactoryTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void UsesServerIdentityAndBlocksUnsupportedState(int scenario)
    {
        var transaction = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var selection = new OfficialMerchantTransactionSelection(
            3954, 6901, 1, OfficialMerchantTransactionOperation.Sell, 4);
        var binding = new MerchantInteractionBinding(
            99, 8001, 9001, 100, 3954,
            OfficialMerchantSaleResultCodec.ClientBuildId,
            2, "synthetic-fixture", true);
        var inventory = new CharacterInventorySnapshot(
            inventoryId, 7, 8, 3, 4, "Clean",
            [new CharacterInventorySlot(0, 253231541, 1, "Unbound", "{}")]);
        var wallet = new CharacterWalletSnapshot(7, "Gold", 60, 5);
        var identity = new CharacterInventoryItemIdentity(
            7, 123456, 0, 253231541, 1, 6, "Unbound", false, "{}");

        switch (scenario)
        {
            case 0: identity = identity with { Bound = true }; break;
            case 1: identity = identity with { Bound = null }; break;
            case 2: identity = identity with { ItemInstanceId = 0 }; break;
            case 3: identity = identity with { SlotIndex = 1 }; break;
            case 4: identity = identity with { CharacterId = 8 }; break;
            case 5: identity = identity with { SlotVersion = long.MaxValue }; break;
            case 6: inventory = inventory with { Slots = [
                inventory.Slots[0],
                new CharacterInventorySlot(1, 253231541, 1)] }; break;
            case 7: wallet = wallet with { CharacterId = 8 }; break;
            case 8: inventory = inventory with { Version = long.MaxValue }; break;
            case 9: identity = identity with
                { ItemInstanceMetadata = "{\"extra\":1}" }; break;
            case 10: identity = identity with { ItemId = 6901 }; break;
        }

        var success = MerchantSaleCommandFactory.TryCreate(
            OfficialMerchantSaleResultCodec.ClientBuildId, 7,
            binding, selection, inventory, wallet, identity, transaction,
            out var command, out var failure);

        if (scenario != -1)
        {
            Assert.False(success);
            Assert.Null(command);
            Assert.NotEmpty(failure);
            return;
        }

        Assert.True(success);
        Assert.Empty(failure);
        Assert.NotNull(command);
        Assert.Equal(transaction, command!.TransactionId);
        Assert.Equal($"MerchantSell:{transaction:N}", command.IdempotencyKey);
        Assert.Equal(inventoryId, command.InventoryId);
        Assert.Equal(253231541L, command.ItemId);
        Assert.Equal(123456L, command.ItemInstanceId);
        Assert.Equal(0, command.SlotIndex);
        Assert.Equal(1, command.Quantity);
        Assert.Equal(3, command.ExpectedInventoryVersion);
        Assert.Equal(4, command.ExpectedMutationSequence);
        Assert.Equal(6, command.ExpectedSlotVersion);
        Assert.Equal(5, command.ExpectedWalletVersion);
    }
}
