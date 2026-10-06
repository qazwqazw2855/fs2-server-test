using God2.ServerV2.Application;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class MerchantPurchaseRequestIdentityTests
{
    [Fact]
    public void IdentityPreservesOriginalReceiptFormat()
    {
        var request = new MerchantPurchaseRequest(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "purchase-fixture", 7, 99, 253231541, 1,
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            3, 4, 5);

        Assert.Equal(
            "92948cd89acae395529c4f9e5994392fe700e7bd37aec1044a158815e68671fd",
            MerchantPurchaseRequestIdentity.Key(request));
        Assert.Equal(
            "7329dc0374a065e1f0a208cad2c1c82a5cfedcd01f0ea0bcda3774fb500f4db9",
            MerchantPurchaseRequestIdentity.Fingerprint(request));

        var changed = request with { ExpectedWalletVersion = 6 };
        Assert.Equal(
            MerchantPurchaseRequestIdentity.Key(request),
            MerchantPurchaseRequestIdentity.Key(changed));
        Assert.NotEqual(
            MerchantPurchaseRequestIdentity.Fingerprint(request),
            MerchantPurchaseRequestIdentity.Fingerprint(changed));
    }
}
