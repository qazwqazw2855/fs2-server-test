namespace God2.ServerV2.Persistence.IntegrationTests;

internal static class QuestIsolatedDatabaseGuard
{
    internal static void ValidateEnvironment()
    {
        if (Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION") != "1" ||
            Environment.GetEnvironmentVariable("GOD2_DB_HOST") != "127.0.0.1" ||
            Environment.GetEnvironmentVariable("GOD2_DB_PORT") != "3310" ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                Environment.GetEnvironmentVariable("GOD2_QUEST_FIXTURE_USER") ?? "",
                @"\Aquesttest_[0-9a-f]{16}\z") ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                Environment.GetEnvironmentVariable("GOD2_QUEST_ACCEPT_FAULT_USER") ?? "",
                @"\Aquestfault_[0-9a-f]{16}\z"))
            throw new InvalidOperationException("Unapproved isolated quest target.");
    }
}
