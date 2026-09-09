using System;
using System.IO;
using System.Linq;
using Xunit;

public sealed class PromotedRuntimeResearchSchemaTests
{
    [Fact]
    public void PromotedRuntimeReadsArchivedCandidateTablesFromResearchSchema()
    {
        var source = ReadSource("src", "God2.ClassicServer.Persistence", "MariaDbPromotedGameplayContentRuntime.cs");

        Assert.Contains("`god2_research`.`merchant_inventory_candidates`", source, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`quest_objective_candidates`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `merchant_inventory_candidates`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `quest_objective_candidates`", source, StringComparison.Ordinal);
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
