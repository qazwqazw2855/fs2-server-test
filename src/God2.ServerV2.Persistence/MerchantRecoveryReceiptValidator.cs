namespace God2.ServerV2.Persistence;

internal static class MerchantRecoveryReceiptValidator
{
    internal static void ValidatePurchase(
        long balanceBefore,
        long balanceAfter,
        long inventoryVersionBefore,
        long inventoryVersionAfter,
        long expectedInventoryVersion)
    {
        if (balanceBefore < 0 ||
            balanceAfter < 0 ||
            balanceAfter > balanceBefore ||
            inventoryVersionBefore != expectedInventoryVersion ||
            expectedInventoryVersion == long.MaxValue ||
            inventoryVersionAfter != expectedInventoryVersion + 1)
        {
            throw new InvalidDataException(
                "Purchase receipt integrity mismatch.");
        }
    }

    internal static void ValidateSale(
        long balanceBefore,
        long balanceAfter,
        long inventoryVersionBefore,
        long inventoryVersionAfter,
        long expectedInventoryVersion)
    {
        if (balanceBefore < 0 ||
            balanceAfter < 0 ||
            balanceAfter <= balanceBefore ||
            inventoryVersionBefore != expectedInventoryVersion ||
            expectedInventoryVersion == long.MaxValue ||
            inventoryVersionAfter != expectedInventoryVersion + 1)
        {
            throw new InvalidDataException(
                "Sale receipt integrity mismatch.");
        }
    }
}
