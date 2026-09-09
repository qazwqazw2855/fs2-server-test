using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class XjzCSharpEvidenceIntakeMigrationTests
{
    [Fact]
    public void Migration420IntakesClientRecoveredCSharpServicePortsWithoutRawPayloads()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "420_intake_xjz_csharp_client_recovered_service_ports.sql"));

        Assert.Contains("xjz_csharp_evidence_pack_intake", sql, StringComparison.Ordinal);
        Assert.Contains("xjz_csharp_service_port_bindings", sql, StringComparison.Ordinal);
        Assert.Contains("xjz_csharp_apply_queue", sql, StringComparison.Ordinal);
        Assert.Contains("client-recovered C# server contract evidence", sql, StringComparison.Ordinal);
        Assert.Contains("道具使用效果", sql, StringComparison.Ordinal);
        Assert.Contains("怪物外觀與抓包目標", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_payload", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packet_hex", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\ufffd', sql);
        Assert.Equal(316, Regex.Matches(sql, @"\('([^']+)'\s*,\s*'[^']*'\s*,\s*'[^']*'\s*,\s*'Native(?:CppAndCSharp|ProjectEntry)'").Count);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
