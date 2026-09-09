using System.Text.Json;
using God2.ClassicServer.Persistence;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class EvidencePackage20260807112752PersistenceTests
{
    [Fact]
    public void Migration_persists_structures_patterns_and_lifecycle_candidates_without_runtime_mutation()
    {
        var root = RepositoryRoot();
        var migration = Assert.Single(
            SqlMigrationFile.Discover(Path.Combine(root, "database", "schema")),
            value => value.Version == "059");
        var sql = File.ReadAllText(migration.Path);

        Assert.Equal("059_god2_evidence_package_20260807_112752.sql", migration.Name);
        Assert.Contains("`packet_capture_frame_variant_catalog_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_action_grouping_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_action_pattern_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`packet_capture_lifecycle_field_candidate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'god2-evidence-package-20260807-112752'", sql, StringComparison.Ordinal);
        Assert.Contains("'CharacterCreateRequestCandidate'", sql, StringComparison.Ordinal);
        Assert.Contains("'CharacterLifecycleSlotActionCandidate'", sql, StringComparison.Ordinal);
        Assert.Contains("'GameplayRuntimeMutation', 'EvidenceBlocked', 0", sql, StringComparison.Ordinal);
        Assert.Equal(64, CountOccurrences(sql, "INSERT INTO `packet_capture_frame_variant_catalog_evidence`"));
        Assert.Equal(116, CountOccurrences(sql, "INSERT INTO `packet_capture_action_pattern_evidence`"));
        Assert.Equal(3, CountOccurrences(sql, "INSERT INTO `packet_capture_lifecycle_field_candidate_evidence`"));
        Assert.DoesNotContain("INSERT INTO `characters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `characters`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `inventory", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `inventory", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `battle", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `battle", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_evidence_and_database_import_are_payload_free_and_reconciled()
    {
        var root = RepositoryRoot();
        var evidencePath = Path.Combine(root, "src", "God2.ClassicServer.Protocol", "Evidence",
            "OfficialProtocolCompletion", "God2Evidence.20260807.112752.json");
        var importPath = Path.Combine(root, "db", "imports", "evidence", "packet_capture",
            "god2_evidence_package_20260807_112752.database.json");

        using var evidenceDocument = JsonDocument.Parse(File.ReadAllText(evidencePath));
        var evidence = evidenceDocument.RootElement;
        Assert.Equal(1778, evidence.GetProperty("session").GetProperty("decodedMessageCount").GetInt32());
        Assert.Equal(64, evidence.GetProperty("newDecodedFrameEvidence").GetProperty("familyCount").GetInt32());
        Assert.Equal(88, evidence.GetProperty("newDecodedFrameEvidence").GetProperty("observationCount").GetInt32());
        Assert.Equal(116, evidence.GetProperty("actionGroupingEvidence").GetProperty("patternCount").GetInt32());
        Assert.Equal(655, evidence.GetProperty("actionGroupingEvidence").GetProperty("groupedTriggerCount").GetInt32());
        Assert.Equal(3, evidence.GetProperty("lifecycleFieldCandidates").GetArrayLength());
        Assert.Equal(0, evidence.GetProperty("serverApplication").GetProperty("verifiedGameplayPromotions").GetInt32());
        Assert.False(evidence.GetProperty("serverApplication").GetProperty("productionEnabled").GetBoolean());
        Assert.False(evidence.GetProperty("sensitiveDataPolicy").GetProperty("rawPayloadRetainedInRepository").GetBoolean());
        Assert.False(evidence.GetProperty("sensitiveDataPolicy").GetProperty("characterNamesRetainedInRepository").GetBoolean());

        using var importDocument = JsonDocument.Parse(File.ReadAllText(importPath));
        var import = importDocument.RootElement;
        Assert.Equal(64, import.GetProperty("newDecodedFrameFamilies").GetArrayLength());
        Assert.Equal(8, import.GetProperty("handlerFamilies").GetArrayLength());
        Assert.Equal(116, import.GetProperty("actionGrouping").GetProperty("patterns").GetArrayLength());
        Assert.Equal(3, import.GetProperty("lifecycleFieldCandidates").GetArrayLength());
        Assert.False(import.GetProperty("serverApplication").GetProperty("productionEnabled").GetBoolean());
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
