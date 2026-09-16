namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class LiveDialogSpawnEvidenceMigrationTests
{
    private static readonly string MigrationPath =
        Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "470_pin_server_v2_live_dialog_spawn_evidence.sql");

    [Fact]
    public void Migration_promotes_only_exact_handle_3793_spawn()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains(
            "`spawn_id` = 170153793",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`observed_client_entity_handle` = 3793",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`official_resource_ordinal` = 87",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`official_direction_code` = 5",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "`wire_evidence_status` = 'Verified'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "LiveRecovery/attempt-759-decode-2579",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "observed_client_entity_handle` = 1504",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "observed_client_entity_handle` = 5042",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "observed_client_entity_handle` = 5096",
            sql,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var current =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null &&
               !File.Exists(
                   Path.Combine(
                       current.FullName,
                       "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new DirectoryNotFoundException(
                "Repository root was not found.");
    }
}
