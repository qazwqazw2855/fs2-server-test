using God2.ServerV2.Persistence;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class MerchantRecoveryReceiptValidatorTests
{
    [Theory]
    [InlineData(100, 60, 0, 1, 0)]
    [InlineData(100, 100, 0, 1, 0)]
    public void PurchaseAcceptsValidReceipt(
        long before, long after,
        long versionBefore, long versionAfter,
        long expectedVersion)
    {
        MerchantRecoveryReceiptValidator.ValidatePurchase(
            before, after, versionBefore, versionAfter, expectedVersion);
    }

    [Theory]
    [InlineData(-1, 0, 0, 1, 0)]
    [InlineData(100, -1, 0, 1, 0)]
    [InlineData(100, 120, 0, 1, 0)]
    [InlineData(100, 60, 1, 2, 0)]
    [InlineData(100, 60, 0, 3, 0)]
    [InlineData(100, 60, 0, 0, 0)]
    [InlineData(100, 60, long.MaxValue, long.MaxValue, long.MaxValue)]
    public void PurchaseRejectsInvalidReceipt(
        long before, long after,
        long versionBefore, long versionAfter,
        long expectedVersion)
    {
        Assert.Throws<InvalidDataException>(() =>
            MerchantRecoveryReceiptValidator.ValidatePurchase(
                before, after, versionBefore, versionAfter, expectedVersion));
    }

    [Theory]
    [InlineData(60, 64, 1, 2, 1)]
    public void SaleAcceptsValidReceipt(
        long before, long after,
        long versionBefore, long versionAfter,
        long expectedVersion)
    {
        MerchantRecoveryReceiptValidator.ValidateSale(
            before, after, versionBefore, versionAfter, expectedVersion);
    }

    [Theory]
    [InlineData(-1, 64, 1, 2, 1)]
    [InlineData(60, -1, 1, 2, 1)]
    [InlineData(60, 60, 1, 2, 1)]
    [InlineData(60, 59, 1, 2, 1)]
    [InlineData(60, 64, 0, 2, 1)]
    [InlineData(60, 64, 1, 3, 1)]
    [InlineData(60, 64, 1, 1, 1)]
    [InlineData(60, 64, long.MaxValue, long.MaxValue, long.MaxValue)]
    public void SaleRejectsInvalidReceipt(
        long before, long after,
        long versionBefore, long versionAfter,
        long expectedVersion)
    {
        Assert.Throws<InvalidDataException>(() =>
            MerchantRecoveryReceiptValidator.ValidateSale(
                before, after, versionBefore, versionAfter, expectedVersion));
    }
}
