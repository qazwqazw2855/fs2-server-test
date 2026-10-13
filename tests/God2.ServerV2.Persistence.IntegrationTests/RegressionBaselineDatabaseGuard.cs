using God2.ServerV2.Persistence;

namespace God2.ServerV2.Persistence.IntegrationTests;

internal static class RegressionBaselineDatabaseGuard
{
    internal static MariaDbAuthenticationOptions CreateFaultOptions(
        string host, int port, string user, string password)
    {
        if (Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION") != "1" ||
            host != "127.0.0.1" || port != 3311 ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                user, @"\Agrantfault_[0-9a-f]{16}\z"))
            throw new InvalidOperationException("Unapproved baseline fault target.");
        return new MariaDbAuthenticationOptions(host, port, user, password);
    }

    internal static MariaDbAuthenticationOptions CreateOptions(
        string host, int port, string user, string password)
    {
        if (Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION") != "1" ||
            host != "127.0.0.1" || port != 3311 ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                user, @"\Aregtest_[0-9a-f]{16}\z"))
            throw new InvalidOperationException("Unapproved regression baseline target.");
        return new MariaDbAuthenticationOptions(host, port, user, password);
    }
}
