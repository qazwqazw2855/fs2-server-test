using System.Text.Json;

namespace God2.GameplayContentRecovery.Tests;

public sealed class OfficialItemUsageFlagCatalogTests
{
    [Fact]
    public void Exact_current_catalog_preserves_all_flags_and_isolates_the_eleven_new_identities()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialClientStaticCatalogs", "item-usage-flags.exact.json")));
        var root = document.RootElement;
        Assert.Equal(17418, root.GetProperty("recordCount").GetInt32());
        Assert.Equal(17407, root.GetProperty("formalBaselineCount").GetInt32());
        Assert.Equal(11, root.GetProperty("exactCurrentAdditionCount").GetInt32());
        Assert.Equal("c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", root.GetProperty("sourceSha256").GetString());
        Assert.Equal("52cf0d96a89e9451f625fd5e70beefdf9c84539e0c93bfe2f654c3206cc77b07", root.GetProperty("normalizedRuntimeGateSha256").GetString());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("normalizedFullStaticFlagSha256").GetString());
        Assert.False(root.GetProperty("runtimeEligible").GetBoolean());

        var additions = root.GetProperty("records").EnumerateArray()
            .Where(row => !row.GetProperty("presentInFormalBaseline").GetBoolean())
            .Select(row => (row.GetProperty("sourceSection").GetString(), row.GetProperty("clientItemId").GetInt32()))
            .ToArray();
        Assert.Equal(11, additions.Length);
        Assert.Contains(("CBK", 9530), additions);
        Assert.Contains(("COM", 26275), additions);
        Assert.Contains(("MIS03", 31408), additions);
        Assert.Contains(("MIS03", 31409), additions);
        Assert.Equal(7, additions.Count(row => row.Item1 == "SCD" && row.Item2 is >= 9701 and <= 9707));
        Assert.All(root.GetProperty("records").EnumerateArray().Where(row => !row.GetProperty("presentInFormalBaseline").GetBoolean()), row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("resourceKey").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("displayNameOriginal").GetString()));
        });
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
