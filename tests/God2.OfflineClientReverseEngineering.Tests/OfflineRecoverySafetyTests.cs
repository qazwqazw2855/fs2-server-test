using System.Security.Cryptography;
using System.Text.Json;
using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class OfflineRecoverySafetyTests
{
    [Fact]
    public void Evidence_load_reports_a_clear_error_for_an_invalid_repository_root()
    {
        using var fixture = new TemporaryWorkspace();

        var exception = Assert.Throws<InvalidDataException>(() => OfflineVerificationEvidence.Load(fixture.Root));

        Assert.Contains("No verification source files", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_capture_requires_same_pid_and_verifies_capture_attestation()
    {
        using var fixture = new TemporaryWorkspace();
        var output = Path.Combine(fixture.Root, "Artifacts", "OfflineClientReverseEngineering");
        var codeRoot = Path.Combine(output, "runtime-memory-readonly");
        var tableRoot = Path.Combine(output, "runtime-table-readonly");
        Directory.CreateDirectory(codeRoot);
        Directory.CreateDirectory(tableRoot);
        var code = Path.Combine(codeRoot, "pid-42-rva-1100.bin");
        var table = Path.Combine(tableRoot, "pid-42-rva-493F90.bin");
        File.WriteAllBytes(code, [1, 2, 3]);
        File.WriteAllBytes(table, [4, 5, 6]);
        const string clientHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        WriteAttestation(codeRoot, 42, clientHash, Hash(code), "0x00001100", 3);
        WriteAttestation(tableRoot, 42, clientHash, Hash(table), "0x00493F90", 3);

        var result = RuntimeCaptureSelection.Resolve(
            fixture.Root, output, "build", clientHash, new JsonSerializerOptions { WriteIndented = true });

        Assert.NotNull(result);
        Assert.True(result.Provenance.SameProcess);
        Assert.True(result.Provenance.CaptureAttested);
        Assert.Equal(Hash(code), result.Provenance.CodeSha256);
        Assert.Equal(Hash(table), result.Provenance.LengthTableSha256);

        File.WriteAllBytes(Path.Combine(codeRoot, "pid-99-rva-1100.bin"), [9]);
        var replay = RuntimeCaptureSelection.Resolve(
            fixture.Root, output, "build", clientHash, new JsonSerializerOptions());
        Assert.Equal(result.CodePath, replay!.CodePath);

        File.WriteAllBytes(code, [7, 8, 9]);
        Assert.Throws<InvalidDataException>(() => RuntimeCaptureSelection.Resolve(
            fixture.Root, output, "build", clientHash, new JsonSerializerOptions()));
    }

    [Fact]
    public void Runtime_capture_rejects_ambiguous_files_instead_of_selecting_arbitrarily()
    {
        using var fixture = new TemporaryWorkspace();
        var output = Path.Combine(fixture.Root, "Artifacts", "OfflineClientReverseEngineering");
        var codeRoot = Path.Combine(output, "runtime-memory-readonly");
        var tableRoot = Path.Combine(output, "runtime-table-readonly");
        Directory.CreateDirectory(codeRoot);
        Directory.CreateDirectory(tableRoot);
        File.WriteAllBytes(Path.Combine(codeRoot, "pid-1-rva-1100.bin"), [1]);
        File.WriteAllBytes(Path.Combine(codeRoot, "pid-2-rva-1100.bin"), [2]);
        File.WriteAllBytes(Path.Combine(tableRoot, "pid-1-rva-493F90.bin"), [3]);
        File.WriteAllBytes(Path.Combine(tableRoot, "pid-2-rva-493F90.bin"), [4]);

        Assert.Throws<InvalidDataException>(() => RuntimeCaptureSelection.Resolve(
            fixture.Root, output, "build", new string('A', 64), new JsonSerializerOptions()));
    }

    [Fact]
    public void Runtime_analyzer_rejects_oversized_snapshot_before_loading_it()
    {
        using var fixture = new TemporaryWorkspace();
        var path = Path.Combine(fixture.Root, "oversized.bin");
        using (var stream = File.Create(path))
        {
            stream.SetLength((16L * 1024 * 1024) + 1);
        }

        Assert.Throws<InvalidDataException>(() => new RuntimeCodeAnalyzer(path));
    }

    [Fact]
    public void Runtime_capture_rejects_oversized_manifest_before_parsing_it()
    {
        using var fixture = new TemporaryWorkspace();
        var output = Path.Combine(fixture.Root, "Artifacts", "OfflineClientReverseEngineering");
        Directory.CreateDirectory(output);
        using (var stream = File.Create(Path.Combine(output, "runtime-capture-manifest.json")))
        {
            stream.SetLength((1024 * 1024) + 1);
        }

        Assert.Throws<InvalidDataException>(() => RuntimeCaptureSelection.Resolve(
            fixture.Root, output, "build", new string('A', 64), new JsonSerializerOptions()));
    }

    [Fact]
    public void Knowledge_domains_only_index_primary_entities_and_extract_array_references()
    {
        using var fixture = new TemporaryWorkspace();
        var official = Path.Combine(fixture.Root, "official");
        var knowledge = Path.Combine(fixture.Root, "Knowledge");
        Directory.CreateDirectory(official);
        File.WriteAllText(Path.Combine(official, "items.official.json"), """
            {
              "category": "items",
              "recordCount": 1,
              "verificationStatus": "Recovered",
              "canDirectImportToGameplay": true,
              "recoveredFields": ["itemId"],
              "missingFields": [],
              "records": [
                { "itemId": 1, "name": "Item", "relatedIds": [2, 3], "nested": [{ "questId": 9 }] }
              ]
            }
            """);

        var result = OfficialKnowledgeRecovery.Recover(official, knowledge, new JsonSerializerOptions());
        var item = Assert.Single(result.Domains, domain => domain.Domain == "Item");
        var mount = Assert.Single(result.Domains, domain => domain.Domain == "Mount");

        Assert.Equal(1, item.IndexedRecordCount);
        Assert.Equal(0, mount.IndexedRecordCount);
        Assert.Equal(1, mount.RelatedSourceRecordCount);
        Assert.DoesNotContain("itemId", item.Entities[0].References.Keys);
        Assert.Equal("2", item.Entities[0].References["relatedIds[0]"]);
        Assert.Equal("3", item.Entities[0].References["relatedIds[1]"]);
        Assert.Equal("9", item.Entities[0].References["nested[0].questId"]);
        Assert.Contains(result.Edges, edge =>
            edge.SourceDomain == "Item" && edge.SourcePath == "nested[0].questId" &&
            edge.TargetDomain == "Quest" && edge.ResolutionStatus == "Unresolved");
    }

    [Fact]
    public void Test_evidence_fails_closed_when_trx_is_stale()
    {
        using var fixture = new TemporaryWorkspace();
        var source = Path.Combine(fixture.Root, "src", "a.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "class A { }");
        var solution = Path.Combine(fixture.Root, "God2ClassicServer.sln");
        File.WriteAllText(solution, string.Empty);
        File.SetLastWriteTimeUtc(solution, DateTime.UtcNow.AddMinutes(-3));
        var verification = Path.Combine(fixture.Root, "Artifacts", "OfflineClientReverseEngineering", "verification");
        Directory.CreateDirectory(verification);
        foreach (var name in new[] { "full", "protocol", "runtime", "headless" })
        {
            WriteTrx(Path.Combine(verification, $"{name}.trx"));
        }

        var current = OfflineVerificationEvidence.Load(fixture.Root);
        Assert.True(current.AllCurrentTestsPass);

        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
        var stale = OfflineVerificationEvidence.Load(fixture.Root);
        Assert.False(stale.AllCurrentTestsPass);
        Assert.All(stale.Tests.Values, item => Assert.Equal("STALE", item.Status));
    }

    [Fact]
    public void Full_test_evidence_uses_latest_result_per_test_assembly()
    {
        using var fixture = new TemporaryWorkspace();
        var source = Path.Combine(fixture.Root, "src", "a.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "class A { }");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-3));
        var solution = Path.Combine(fixture.Root, "God2ClassicServer.sln");
        File.WriteAllText(solution, string.Empty);
        File.SetLastWriteTimeUtc(solution, DateTime.UtcNow.AddMinutes(-3));
        var verification = Path.Combine(fixture.Root, "Artifacts", "OfflineClientReverseEngineering", "verification");
        var full = Path.Combine(verification, "full");
        Directory.CreateDirectory(full);
        WriteTrx(Path.Combine(full, "old.trx"), "suite.dll", passed: false, DateTime.UtcNow.AddMinutes(-2));
        WriteTrx(Path.Combine(full, "new.trx"), "suite.dll", passed: true, DateTime.UtcNow.AddMinutes(-1));
        foreach (var name in new[] { "protocol", "runtime", "headless" })
        {
            WriteTrx(Path.Combine(verification, $"{name}.trx"));
        }

        var evidence = OfflineVerificationEvidence.Load(fixture.Root);

        Assert.Equal("PASS", evidence.Tests["Full"].Status);
        Assert.Equal(1, evidence.Tests["Full"].Total);
        Assert.Equal(1, evidence.Tests["Full"].Passed);
        Assert.Equal(0, evidence.Tests["Full"].Failed);
        Assert.EndsWith("new.trx", evidence.Tests["Full"].Artifact, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_evidence_marks_oversized_trx_invalid_without_loading_it()
    {
        using var fixture = new TemporaryWorkspace();
        var source = Path.Combine(fixture.Root, "src", "a.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "class A { }");
        var solution = Path.Combine(fixture.Root, "God2ClassicServer.sln");
        File.WriteAllText(solution, string.Empty);
        var old = DateTime.UtcNow.AddMinutes(-1);
        File.SetLastWriteTimeUtc(source, old);
        File.SetLastWriteTimeUtc(solution, old);
        var verification = Path.Combine(fixture.Root, "Artifacts", "OfflineClientReverseEngineering", "verification");
        Directory.CreateDirectory(verification);
        foreach (var name in new[] { "full", "runtime", "headless" })
        {
            WriteTrx(Path.Combine(verification, $"{name}.trx"));
        }
        using (var stream = File.Create(Path.Combine(verification, "protocol.trx")))
        {
            stream.SetLength((128L * 1024 * 1024) + 1);
        }

        var evidence = OfflineVerificationEvidence.Load(fixture.Root);

        Assert.Equal("INVALID", evidence.Tests["Protocol"].Status);
        Assert.False(evidence.AllCurrentTestsPass);
    }

    private static void WriteAttestation(string root, int pid, string clientHash, string artifactHash, string rva, int bytesRead) =>
        File.WriteAllText(Path.Combine(root, "memory-dump-result.json"), JsonSerializer.Serialize(new
        {
            targetPid = pid,
            clientExecutableSha256 = clientHash,
            results = new[] { new { binSha256 = artifactHash, rva, before = 256, bytesRead } }
        }));

    private static void WriteTrx(string path)
    {
        WriteTrx(path, null, passed: true, DateTime.UtcNow.AddSeconds(1));
    }

    private static void WriteTrx(string path, string? codeBase, bool passed, DateTime writeTimeUtc)
    {
        var testDefinitions = codeBase is null
            ? string.Empty
            : $"<TestDefinitions><UnitTest><TestMethod codeBase=\"{codeBase}\" /></UnitTest></TestDefinitions>";
        File.WriteAllText(path, $$"""
            <TestRun>
              <Times finish="2030-01-01T00:00:00Z" />
              {{testDefinitions}}
              <ResultSummary outcome="Completed">
                <Counters total="1" executed="1" passed="{{(passed ? 1 : 0)}}" failed="{{(passed ? 0 : 1)}}" error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="0" />
              </ResultSummary>
            </TestRun>
            """);
        File.SetLastWriteTimeUtc(path, writeTimeUtc);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"god2-offline-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
