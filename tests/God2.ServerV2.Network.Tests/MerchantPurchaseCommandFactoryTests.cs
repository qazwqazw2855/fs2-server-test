using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantPurchaseCommandFactoryTests
{
    private static readonly MerchantInteractionBinding Binding =
        new(100, 200, 300, 400, 3954, "test-build", null, "", false);
    private static readonly OfficialMerchantTransactionSelection Selection =
        new(3954, 6901, 1, OfficialMerchantTransactionOperation.Buy, 7);
    private static readonly MerchantCatalogIdentity Catalog =
        new(500, 100, 253231541, 7, 6901);
    private static readonly CharacterInventorySnapshot Inventory =
        new(Guid.NewGuid(), 10, 8, 12, 34, "Clean", []);
    private static readonly CharacterWalletSnapshot Wallet =
        new(10, "Gold", 100, 56);

    [Fact]
    public void ProjectsCanonicalIdentityAndAuthoritativeVersions()
    {
        var id = Guid.NewGuid();
        Assert.True(MerchantPurchaseCommandFactory.TryCreate(
            10, Binding, Selection, Catalog, Inventory, Wallet, id,
            out var command, out var failure));
        Assert.Equal("", failure);
        Assert.Equal(Catalog.ItemId, command!.ItemId);
        Assert.NotEqual((long)Selection.ClientItemId, command.ItemId);
        Assert.Equal(Inventory.InventoryId, command.InventoryId);
        Assert.Equal(12, command.ExpectedInventoryVersion);
        Assert.Equal(34, command.ExpectedMutationSequence);
        Assert.Equal(56, command.ExpectedWalletVersion);
        Assert.Equal(id, command.TransactionId);
    }

    [Theory]
    [InlineData("catalog-missing", "CatalogMissing")]
    [InlineData("merchant", "CatalogMismatch")]
    [InlineData("index", "CatalogMismatch")]
    [InlineData("item", "CatalogMismatch")]
    [InlineData("inventory-missing", "InventoryMissing")]
    [InlineData("inventory-owner", "InventoryMismatch")]
    [InlineData("inventory-version", "InventoryMismatch")]
    [InlineData("wallet-missing", "WalletMissing")]
    [InlineData("wallet-owner", "WalletMismatch")]
    [InlineData("currency", "WalletMismatch")]
    [InlineData("wallet-version", "WalletMismatch")]
    [InlineData("quantity", "UnsupportedQuantity")]
    [InlineData("sell", "UnsupportedOperation")]
    [InlineData("handle", "BindingMismatch")]
    [InlineData("transaction", "InvalidServerIdentity")]
    public void RejectsUnusableProjectionWithoutProducingCommand(
        string scenario, string expected)
    {
        var selection = Selection;
        MerchantCatalogIdentity? catalog = Catalog;
        CharacterInventorySnapshot? inventory = Inventory;
        CharacterWalletSnapshot? wallet = Wallet;
        var id = Guid.NewGuid();

        switch (scenario)
        {
            case "catalog-missing": catalog = null; break;
            case "merchant": catalog = Catalog with { MerchantId = 999 }; break;
            case "index": catalog = Catalog with { ClientCatalogIndex = 8 }; break;
            case "item": catalog = Catalog with { ClientItemId = 6906 }; break;
            case "inventory-missing": inventory = null; break;
            case "inventory-owner": inventory = Inventory with { CharacterId = 11 }; break;
            case "inventory-version": inventory = Inventory with { Version = -1 }; break;
            case "wallet-missing": wallet = null; break;
            case "wallet-owner": wallet = Wallet with { CharacterId = 11 }; break;
            case "currency": wallet = Wallet with { CurrencyType = "Other" }; break;
            case "wallet-version": wallet = Wallet with { Version = -1 }; break;
            case "quantity": selection = Selection with { Quantity = 2 }; break;
            case "sell": selection = Selection with
                { Operation = OfficialMerchantTransactionOperation.Sell }; break;
            case "handle": selection = Selection with { ClientEntityHandle = 1504 }; break;
            case "transaction": id = Guid.Empty; break;
        }

        Assert.False(MerchantPurchaseCommandFactory.TryCreate(
            10, Binding, selection, catalog, inventory, wallet, id,
            out var command, out var failure));
        Assert.Null(command);
        Assert.Equal(expected, failure);
    }

    [Fact]
    public void InternalRetryIsStableAndSeparatePurchasesRemainDistinct()
    {
        MerchantPurchaseCommand Create(Guid id)
        {
            Assert.True(MerchantPurchaseCommandFactory.TryCreate(
                10, Binding, Selection, Catalog, Inventory, Wallet, id,
                out var command, out _));
            return command!;
        }

        var id = Guid.NewGuid();
        Assert.Equal(Create(id), Create(id));
        Assert.NotEqual(Create(id).IdempotencyKey,
                        Create(Guid.NewGuid()).IdempotencyKey);
    }
}
