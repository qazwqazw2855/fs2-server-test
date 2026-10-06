using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;

namespace God2.ServerV2.Persistence;

internal static class MerchantPurchaseRequestIdentity
{
    // Preserve the existing receipt namespace and serialization exactly.
    public static string Key(MerchantPurchaseRequest request) =>
        Hash("God2.ServerV2.MerchantPurchase/1:" +
             request.CharacterId + ":" + request.IdempotencyKey);

    public static string Serialize(MerchantPurchaseRequest request) =>
        JsonSerializer.Serialize(request);

    public static string Fingerprint(MerchantPurchaseRequest request) =>
        Hash(Serialize(request));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
