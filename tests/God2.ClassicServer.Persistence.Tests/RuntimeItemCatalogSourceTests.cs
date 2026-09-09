using System;
using System.IO;
using System.Linq;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class RuntimeItemCatalogSourceTests
{
    [Fact]
    public void StaticDataLoaderReadsFormalItemRootTableInsteadOfMergedDefinitionView()
    {
        var source = ReadSource("src", "God2.ClassicServer.Persistence", "MariaDbRuntimeData.cs");

        Assert.Contains("FROM `god2_game`.`items` WHERE `enabled`=1 ORDER BY `item_id`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2_game`.`vw_all_item_definitions` WHERE `enabled`=1 ORDER BY `item_id`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GameplayInventoryCatalogReadsFormalItemRootTableInsteadOfMergedDefinitionView()
    {
        var source = ReadSource("src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRepository.cs");

        Assert.Contains("FROM `god2_game`.`items` item_row", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2_game`.`vw_all_item_definitions` item_row", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] pathParts)
    {
        var baseDirectory = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(baseDirectory))
        {
            var candidate = Path.Combine(new[] { baseDirectory }.Concat(pathParts).ToArray());

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            baseDirectory = Directory.GetParent(baseDirectory)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathParts)}");
    }
}
