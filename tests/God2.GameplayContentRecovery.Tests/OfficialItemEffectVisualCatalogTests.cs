using System.Text.Json;

namespace God2.GameplayContentRecovery.Tests;

public sealed class OfficialItemEffectVisualCatalogTests
{
    private const string ItemEft3Sha256 = "c83717a53d06d1736393212de975206110145553f80e71349ac11a4863f155da";
    private const string GodItemEftSha256 = "7a702d8c432d7afbbc4eea33e5e98869c2d51cd4489486ac2a2225365a84c010";

    [Fact]
    public void Exact_client_catalog_contains_both_complete_visual_identity_ranges_and_stays_runtime_blocked()
    {
        var path = Path.Combine(RepositoryRoot(), "Artifacts", "OfficialClientStaticCatalogs", "item-effect-visuals.exact.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.Equal(102, root.GetProperty("recordCount").GetInt32());
        Assert.Equal(72, root.GetProperty("itemEft3Count").GetInt32());
        Assert.Equal(30, root.GetProperty("godItemEftCount").GetInt32());
        Assert.False(root.GetProperty("canDirectImportToGameplay").GetBoolean());
        Assert.Contains("MechanicalSemanticsEvidenceBlocked", root.GetProperty("verificationStatus").GetString(), StringComparison.Ordinal);

        var records = root.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(8400, 72).Concat(Enumerable.Range(8500, 30)),
            records.Select(record => record.GetProperty("effectId").GetInt32()));
        Assert.All(records, record =>
        {
            Assert.False(record.GetProperty("runtimeEligible").GetBoolean());
            Assert.Equal("VerifiedExactClientStaticVisualCatalog", record.GetProperty("evidenceStatus").GetString());
            Assert.Equal("DisplayTextOnlyServerAuthorityBlocked", record.GetProperty("mechanicalSemanticsStatus").GetString());
            var expectedHash = record.GetProperty("sourceTable").GetString() == "ItemEft3"
                ? ItemEft3Sha256
                : GodItemEftSha256;
            Assert.Equal(expectedHash, record.GetProperty("sourceSha256").GetString());
            Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("visualDescriptionOriginal").GetString()));
        });

        var exactCurrentDelta = Assert.Single(records, record => record.GetProperty("effectId").GetInt32() == 8435);
        Assert.Equal("/Data2/Item/Itm8435.rom", exactCurrentDelta.GetProperty("romResourcePath").GetString());
        Assert.Equal("/Data2/Item/Itm8435.rtg", exactCurrentDelta.GetProperty("rtgResourcePath").GetString());
        var corroboration = root.GetProperty("crossVersionCorroboration");
        Assert.Equal(102, corroboration.GetProperty("matchingIdentityCount").GetInt32());
        Assert.Equal(30, corroboration.GetProperty("exactCurrentAdditionalRomBindings").GetInt32());
        Assert.Equal(30, corroboration.GetProperty("exactCurrentAdditionalRtgBindings").GetInt32());
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
