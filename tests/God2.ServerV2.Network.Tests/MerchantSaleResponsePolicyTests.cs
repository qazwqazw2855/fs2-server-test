using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantSaleResponsePolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EncodesOnlyMatchingFreshCommittedSale(int scenario)
    {
        var transaction = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var binding = new MerchantInteractionBinding(
            99, 8001, 9001, 100, 3954,
            OfficialMerchantSaleResultCodec.ClientBuildId,
            2, "synthetic-fixture", true);
        var selection = new OfficialMerchantTransactionSelection(
            3954, 6901, 1, OfficialMerchantTransactionOperation.Sell, 4);
        var inventory = new CharacterInventorySnapshot(
            inventoryId, 7, 8, 3, 4, "Clean",
            [new CharacterInventorySlot(0, 253231541, 1, "Unbound", "{}")]);
        var before = new CharacterEconomySnapshot(
            inventory, new CharacterWalletSnapshot(7, "Gold", 60, 5));
        var identity = new CharacterInventoryItemIdentity(
            7, 123456, 0, 253231541, 1, 6, "Unbound", false, "{}");
        var command = new MerchantSaleCommand(
            transaction, $"MerchantSell:{transaction:N}",
            253231541, 123456, 0, 1, inventoryId, 3, 4, 6, 5);
        var result = new MerchantSaleResult(
            MerchantSaleStatus.Sold, transaction, 60, 64, 6, 3, 4);
        var after = new CharacterEconomySnapshot(
            inventory with { Version = 4, MutationSequence = 5, Slots = [] },
            new CharacterWalletSnapshot(7, "Gold", 64, 6));

        switch (scenario)
        {
            case 1: result = result with { TransactionId = Guid.NewGuid() }; break;
            case 2: result = result with { BalanceAfter = 65 }; break;
            case 3: after = after with { Inventory = inventory with
                { Version = 4, MutationSequence = 5 } }; break;
            case 4: result = result with { InventoryVersionAfter = 5 }; break;
            case 5: result = result with { Status = MerchantSaleStatus.Replayed }; break;
            case 6: after = after with
                { Wallet = after.Wallet! with { Version = 7 } }; break;
            case 7: identity = identity with { ItemInstanceId = 999 }; break;
        }

        var success = MerchantSaleResponsePolicy.TryEncodeCommitted(
            binding, 7, selection, command, before, identity, after, result,
            out var response, out var failure);

        if (scenario == 0)
        {
            Assert.True(success);
            Assert.Empty(failure);
            Assert.True(OfficialMerchantSaleResultCodec.TryEncode(
                binding.ClientBuildId, selection, 64, out var expected, out _));
            Assert.Equal(expected, response);
        }
        else
        {
            Assert.False(success);
            Assert.Null(response);
            Assert.NotEmpty(failure);
        }
    }
}
