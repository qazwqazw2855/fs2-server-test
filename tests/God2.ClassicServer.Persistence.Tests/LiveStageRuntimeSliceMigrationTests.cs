namespace God2.ClassicServer.Persistence.Tests;

public sealed class LiveStageRuntimeSliceMigrationTests
{
    private static readonly string MigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "103_publish_live_stage3_stage7_runtime_slice.sql");

    [Fact]
    public void Migration_publishes_verified_route_npc_and_merchant_prices()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("0C0061CF010F0FC000A20051", sql, StringComparison.Ordinal);
        Assert.Contains("180072720F00005260000081000000CF010000C0036C00A1", sql, StringComparison.Ordinal);
        Assert.Contains("'god2-opt-6b127086e0c0',3954,0,81,3,4,1,'Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("180072D10E000058600000A1000000CF01000004017A0075", sql, StringComparison.Ordinal);
        Assert.Contains("'god2-opt-6b127086e0c0',3793,0,87,3,5,1,'Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("'Verified','Verified','OpenOnly'", sql, StringComparison.Ordinal);
        Assert.Contains("253231541", sql, StringComparison.Ordinal);
        Assert.Contains("`selling_price`,`purchasing_price`", sql, StringComparison.Ordinal);
        Assert.Contains("1,40,4,1,NULL,'Verified',1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_is_idempotent_and_does_not_publish_unknown_names_as_official()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.True(Count(sql, "ON DUPLICATE KEY UPDATE") >= 6);
        Assert.Contains("official name and resource archive remain unknown", sql, StringComparison.Ordinal);
        Assert.Contains("official display name remains unknown", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
        {
            count++;
        }

        return count;
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
