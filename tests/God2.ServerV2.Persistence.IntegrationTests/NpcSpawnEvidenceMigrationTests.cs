namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class NpcSpawnEvidenceMigrationTests
{
    [Fact]
    public void Migration_pins_only_the_two_derived_map_three_profiles()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "469_pin_server_v2_npc_spawn_evidence.sql"));

        Assert.Contains(
            "wire_evidence_status = 'Derived'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "observed_client_entity_handle = 5042",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "observed_client_entity_handle = 5096",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "3F25673AE985BF8F4818F2EE1254AB019B0406B8BD1C38C44F701DD5BAE27834",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "78230A74DC17C388FE1A6FFDF6EBBD284A05C3E77E20719BE1921941903CC9B7",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "wire_evidence_status = 'Verified'",
            sql,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(
                   directory.FullName,
                   "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Repository root was not found.");
    }
}
