using System.Text.Json;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class ItemStaticPermissionEvidenceMigrationTests
{
    [Fact]
    public void Migration_requires_the_exact_full_flag_hash_and_never_enables_behavior()
    {
        var root = RepositoryRoot();
        var sql = File.ReadAllText(Path.Combine(root, "database", "schema", "141_publish_verified_item_static_permission_evidence.sql"));
        using var artifact = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "Artifacts", "OfficialClientStaticCatalogs", "item-usage-flags.exact.json")));
        var fullHash = artifact.RootElement.GetProperty("normalizedFullStaticFlagSha256").GetString();

        Assert.Equal("41ce1d3cc9914e1f31289374bd530bcdf843eb3c8a27996dbd4313b0d25b95fb", fullHash);
        Assert.Contains(fullHash!, sql, StringComparison.Ordinal);
        Assert.Contains("actual_count<>17407", sql, StringComparison.Ordinal);
        Assert.Contains("`item_static_permission_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("$.rawFields[23]", sql, StringComparison.Ordinal);
        Assert.Contains("$.rawFields[24]", sql, StringComparison.Ordinal);
        Assert.Contains("'VerifiedExactCurrentClientStaticFlags'", sql, StringComparison.Ordinal);
        Assert.Contains("'EvidenceBlockedMissingOfficialTransition',1,0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`item_registry`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`item_usage_rules`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
