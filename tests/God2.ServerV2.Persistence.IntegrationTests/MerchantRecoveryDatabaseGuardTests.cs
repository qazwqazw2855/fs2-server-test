namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class MerchantRecoveryDatabaseGuardTests
{
    [Fact]
    public void IsolatedMerchantTargetIsAccepted()
    {
        MerchantRecoveryDatabaseGuard.Validate(
            "1", "127.0.0.1", "3310", "shoptest_fixture");
    }

    [Theory]
    [InlineData("0", "127.0.0.1", "3310", "shoptest_fixture")]
    [InlineData("1", "localhost", "3310", "shoptest_fixture")]
    [InlineData("1", "0.0.0.0", "3310", "shoptest_fixture")]
    [InlineData("1", "127.0.0.1", "3310", "root")]
    [InlineData("1", "127.0.0.1", "3310", "shoptest_")]
    [InlineData("1", "127.0.0.1", "3310", "")]
    [InlineData("1", "127.0.0.1", "3310", "   ")]
    [InlineData("0", "127.0.0.1", "3308", "shoptest_fixture")]
    [InlineData("", "127.0.0.1", "3308", "shoptest_fixture")]
    [InlineData("1", "localhost", "3308", "shoptest_fixture")]
    [InlineData("1", "0.0.0.0", "3308", "shoptest_fixture")]
    [InlineData("1", "127.0.0.1", "3306", "shoptest_fixture")]
    [InlineData("1", "127.0.0.1", "3309", "shoptest_fixture")]
    [InlineData("1", "127.0.0.1", "3308", "shoptest_fixture")]
    [InlineData("1", "127.0.0.1", "3308", "root")]
    [InlineData("1", "127.0.0.1", "3308", "shoptest_")]
    [InlineData("1", "127.0.0.1", "3308", "")]
    public void UnapprovedTargetsAreRejected(
        string enabled,
        string host,
        string port,
        string user)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MerchantRecoveryDatabaseGuard.Validate(
                enabled, host, port, user));
    }
}
