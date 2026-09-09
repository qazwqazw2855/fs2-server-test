namespace God2.ClassicServer.Persistence.Tests;

public sealed class RuntimeVerificationFixtureContractTests
{
    [Fact]
    public void Migration_and_high_value_verifier_share_the_least_privilege_table_contract()
    {
        var root = RepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root,
            "database",
            "schema",
            "204_move_runtime_verification_fixture_to_research_schema.sql"));
        var verifier = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.AdvancedHeadlessVerification",
            "MariaDbVerification.cs"));

        Assert.Contains("`god2_research`.`runtime_verification_transactions`", migration, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`runtime_verification_transactions`", migration, StringComparison.Ordinal);
        Assert.Contains("ON `god2_research`.`runtime_verification_transactions`", migration, StringComparison.Ordinal);
        Assert.Contains("TO `god2_runtime_role`", migration, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`runtime_verification_transactions`", verifier, StringComparison.Ordinal);
        Assert.Contains("VerifyTableContractAsync", verifier, StringComparison.Ordinal);
        Assert.Contains("`verification_case_sha256`", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("`payload_sha256`", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS `advanced_headless", verifier, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM `god2_research`.`runtime_verification_transactions` WHERE `run_id`=@run", verifier, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
