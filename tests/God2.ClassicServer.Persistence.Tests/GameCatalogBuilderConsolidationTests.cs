using System;
using System.IO;
using System.Linq;
using Xunit;

public sealed class GameCatalogBuilderConsolidationTests
{
    [Fact]
    public void GameCatalogBuilderReadsArchivedFieldEvidenceFromResearchSchema()
    {
        var source = ReadSource("tools", "God2.GameCatalogBuilder", "Program.cs");

        Assert.Contains("FROM `god2_research`.`content_field_evidence` evidence_row", source, StringComparison.Ordinal);
        Assert.Contains("mapping_row.`source_schema`='god2_research'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2`.`content_field_evidence` evidence_row", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
