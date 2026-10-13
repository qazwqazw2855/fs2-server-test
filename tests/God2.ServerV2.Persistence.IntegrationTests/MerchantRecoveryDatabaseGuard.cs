namespace God2.ServerV2.Persistence.IntegrationTests;

internal static class MerchantRecoveryDatabaseGuard
{
    internal static void Validate(
        string integrationEnabled,
        string host,
        string port,
        string fixtureUser)
    {
        if (integrationEnabled != "1")
            throw new InvalidOperationException(
                "Merchant DB integration must be explicitly enabled.");

        if (host != "127.0.0.1" || port != "3310")
            throw new InvalidOperationException(
                "Merchant DB target is not approved.");

        if (string.IsNullOrWhiteSpace(fixtureUser) ||
            !fixtureUser.StartsWith(
                "shoptest_", StringComparison.Ordinal) ||
            fixtureUser.Length <= "shoptest_".Length)
            throw new InvalidOperationException(
                "Merchant fixture user is not approved.");
    }
}
