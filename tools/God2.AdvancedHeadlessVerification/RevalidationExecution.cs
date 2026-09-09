using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using God2.OfflineClientReverseEngineering;

namespace God2.AdvancedHeadlessVerification;

public sealed record RevalidationProcessResult(
    string Name, string Command, DateTimeOffset StartTimeUtc, DateTimeOffset EndTimeUtc,
    int ExitCode, string Status, string LogArtifact, string LogSha256);

public static class RevalidationExecution
{
    public static async Task<RevalidationBuildIdentity> PrepareAsync(string root, string runId, CancellationToken cancellationToken = default)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        Directory.CreateDirectory(Path.Combine(runRoot, "Build"));
        var debug = await RunAsync(root, runRoot, "build-debug", "dotnet",
            ["build", "God2ClassicServer.sln", "--no-restore", "-warnaserror"], "Build/build-debug.log", cancellationToken);
        VerificationGuard.Require(debug.ExitCode == 0, "Fresh Debug build failed.");
        var release = await RunAsync(root, runRoot, "build-release", "dotnet",
            ["build", "God2ClassicServer.sln", "-c", "Release", "--no-restore", "-warnaserror"], "Build/build-release.log", cancellationToken);
        VerificationGuard.Require(release.ExitCode == 0, "Fresh Release build failed.");
        var warningCount = CountBuildDiagnostics(runRoot, [debug, release], "warning");
        var errorCount = CountBuildDiagnostics(runRoot, [debug, release], "error");
        var buildPassed = debug.Status == "PASS" && release.Status == "PASS" && warningCount == 0 && errorCount == 0;
        VerificationGuard.Require(buildPassed, "Fresh build emitted a warning or error diagnostic.");
        var identity = RevalidationIdentityBuilder.Create(root, runId);
        RevalidationJson.Write(Path.Combine(runRoot, "build-summary.json"), new
        {
            SchemaVersion = "advanced-final-freeze-build-result-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            identity.BuildOutputManifestHash,
            Status = buildPassed ? "PASS" : "FAIL",
            WarningCount = warningCount,
            ErrorCount = errorCount,
            WarningsAsErrors = true,
            Debug = debug,
            Release = release
        });
        return identity;
    }

    public static async Task RunRegressionAsync(string root, string runId, CancellationToken cancellationToken = default)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var regressionRoot = Path.Combine(runRoot, "Regression");
        Directory.CreateDirectory(regressionRoot);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"god2-affr-trx-{runId}");
        if (Directory.Exists(temporaryRoot))
        {
            throw new InvalidOperationException("The isolated TRX staging directory already exists.");
        }
        Directory.CreateDirectory(temporaryRoot);
        var started = DateTimeOffset.UtcNow;
        try
        {
            var definitions = new[]
            {
                ("Full", "God2ClassicServer.sln"),
                ("Protocol", "tests/God2.ClassicServer.Protocol.Tests/God2.ClassicServer.Protocol.Tests.csproj"),
                ("Runtime", "tests/God2.ClassicServer.Runtime.Tests/God2.ClassicServer.Runtime.Tests.csproj"),
                ("Headless", "tests/God2.ClassicServer.HeadlessGameplay.Tests/God2.ClassicServer.HeadlessGameplay.Tests.csproj")
            };
            var suites = new List<RevalidationTestSuite>();
            foreach (var definition in definitions)
            {
                var stage = Path.Combine(temporaryRoot, definition.Item1);
                Directory.CreateDirectory(stage);
                var process = await RunAsync(root, runRoot, $"test-{definition.Item1.ToLowerInvariant()}", "dotnet",
                    ["test", definition.Item2, "--no-build", "--no-restore", "--logger", "trx", "--results-directory", stage],
                    $"Regression/{definition.Item1.ToLowerInvariant()}.log", cancellationToken);
                VerificationGuard.Require(process.ExitCode == 0, $"{definition.Item1} regression command failed.");
                var suite = ImportTrx(root, identity, definition.Item1, stage, Path.Combine(regressionRoot, definition.Item1));
                VerificationGuard.Require(suite.Status == "PASS", $"{definition.Item1} regression evidence failed validation.");
                suites.Add(suite);
            }

            var formatter = await RunAsync(root, runRoot, "formatter", "dotnet",
                ["format", "God2ClassicServer.sln", "--verify-no-changes", "--no-restore"],
                "Regression/formatter.log", cancellationToken);
            VerificationGuard.Require(formatter.ExitCode == 0, "Formatter verification failed.");
            var configFiles = Directory.EnumerateFiles(Path.Combine(root, "config"), "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".local.json", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (var config in configFiles)
            {
                using var _ = JsonDocument.Parse(File.ReadAllBytes(config));
            }
            var fastStarted = DateTimeOffset.UtcNow;
            var fast = new AdvancedVerificationRunner().RunFast();
            var fastEnded = DateTimeOffset.UtcNow;
            var fastPassed = fast.Status == "PASS" && fast.UnresolvedInvariantFailures == 0 &&
                fast.Differential.MismatchCount == 0 && fast.Concurrency.FailureCount == 0 &&
                fast.Replay.NonDeterministicFailureCount == 0 && fast.Replay.ReplayDivergenceCount == 0 &&
                fast.FakeNetworkBytes == 0;
            VerificationGuard.Require(fastPassed, "Advanced fast verification did not satisfy its pass invariants.");
            RevalidationJson.Write(Path.Combine(regressionRoot, "advanced-fast.json"), new
            {
                SchemaVersion = "advanced-final-freeze-fast-v1",
                identity.BuildIdentityId,
                StartTimeUtc = fastStarted,
                EndTimeUtc = fastEnded,
                Status = fast.Status,
                Result = fast
            });
            var identityVerification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
            var buildBindingPassed = BindingPassed(identity, identityVerification);
            var suitesPassed = suites.All(value => value.Status == "PASS" && value.Failed == 0 && value.Skipped == 0);
            var regressionPassed = suitesPassed && formatter.Status == "PASS" && fastPassed;
            var regressionSummary = new
            {
                SchemaVersion = "advanced-final-freeze-regression-v1",
                identity.RunId,
                identity.BuildIdentityId,
                identity.SourceManifestHash,
                StartTimeUtc = started,
                EndTimeUtc = DateTimeOffset.UtcNow,
                Status = regressionPassed ? "PASS" : "FAIL",
                Suites = suites,
                AdvancedFast = new
                {
                    Status = fast.Status,
                    fast.Exhaustive.CaseCount,
                    fast.Property.GeneratedCases,
                    fast.Property.AcceptedCases,
                    DifferentialEventCount = fast.Differential.EventCount,
                    fast.Differential.MismatchCount,
                    fast.Concurrency.ScheduleCount,
                    fast.Concurrency.FailureCount,
                    fast.FakeNetworkBytes
                },
                Formatter = formatter,
                ConfigurationJson = new { Status = configFiles.Length > 0 ? "PASS" : "FAIL", FileCount = configFiles.Length },
                BuildBinding = buildBindingPassed ? "PASS" : "FAIL"
            };
            RevalidationJson.Write(Path.Combine(runRoot, "regression-summary.json"), regressionSummary);
            RevalidationJson.Write(Path.Combine(runRoot, "code-review-remediation-summary.json"), new
            {
                SchemaVersion = "code-review-remediation-result-v1",
                identity.RunId,
                identity.BuildIdentityId,
                identity.SourceManifestHash,
                StartTimeUtc = started,
                EndTimeUtc = DateTimeOffset.UtcNow,
                Status = regressionPassed ? "PASS" : "FAIL",
                Checks = new
                {
                    Regression = regressionPassed,
                    Formatter = formatter.Status == "PASS",
                    ConfigurationJson = configFiles.Length > 0,
                    AdvancedFast = fastPassed,
                    FakeNetworkBytes = fast.FakeNetworkBytes
                }
            });
            VerificationGuard.Require(regressionPassed, "Code review remediation verification did not pass.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    public static async Task RunMariaDbAsync(string root, string runId, int cases, int workers, CancellationToken cancellationToken = default)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var started = DateTimeOffset.UtcNow;
        var result = await new MariaDbHighValueVerifier().RunAsync(root, cases, workers, cancellationToken);
        var ended = DateTimeOffset.UtcNow;
        var identityVerification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var buildBindingPassed = BindingPassed(identity, identityVerification);
        RevalidationJson.Write(Path.Combine(runRoot, "mariadb-summary.json"), new
        {
            SchemaVersion = "advanced-final-freeze-mariadb-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            StartTimeUtc = started,
            EndTimeUtc = ended,
            Result = result,
            ArtifactHash = RevalidationJson.HashText(JsonSerializer.Serialize(result, RevalidationJson.Options)),
            BuildBinding = buildBindingPassed ? "PASS" : "FAIL"
        });
    }

    public static void RunMap(string root, string runId)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var started = DateTimeOffset.UtcNow;
        var mapRoot = FindOfficialMapRoot(identity.ClientBuildSha256);
        var mapRootSafeHash = RevalidationJson.HashText(string.Join('\n', Directory.EnumerateFiles(mapRoot, "*.mbd", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(mapRoot, path), StringComparer.Ordinal)
            .Select(path => $"{Path.GetRelativePath(mapRoot, path).Replace('\\', '/')}|{RevalidationJson.Hash(path)}")));
        var result = MapContentRecovery.Recover(mapRoot);
        var mapPassed = result.CandidateFileCount > 0 && result.FailedMapCount == 0 &&
            result.ParsedMapCount == result.CandidateFileCount && result.AStarPassedCount == result.ParsedMapCount &&
            !result.MovementProtocolModified;
        VerificationGuard.Require(mapPassed, "Current official map corpus did not fully parse or pass A*.");
        var identityVerification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var buildBindingPassed = BindingPassed(identity, identityVerification);
        RevalidationJson.Write(Path.Combine(runRoot, "Map", "map-results.json"), result);
        RevalidationJson.Write(Path.Combine(runRoot, "map-summary.json"), new
        {
            SchemaVersion = "advanced-final-freeze-map-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            StartTimeUtc = started,
            EndTimeUtc = DateTimeOffset.UtcNow,
            Status = mapPassed ? "PASS" : "FAIL",
            MapCorpusHash = mapRootSafeHash,
            result.CandidateFileCount,
            result.ParsedMapCount,
            result.FailedMapCount,
            result.AStarPassedCount,
            result.MovementProtocolModified,
            BuildBinding = buildBindingPassed ? "PASS" : "FAIL"
        });
    }

    public static async Task RunSoakAsync(string root, string runId, TimeSpan duration, int workers, long seed, CancellationToken cancellationToken = default)
    {
        var runRoot = RevalidationPaths.RunRoot(root, runId);
        var identity = RevalidationJson.Read<RevalidationBuildIdentity>(Path.Combine(runRoot, "build-identity.json"));
        RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var result = await new LowConcurrencyResourceSoak().RunAsync(root, duration, workers, seed, cancellationToken);
        var identityVerification = RevalidationIdentityBuilder.VerifyCurrent(root, identity);
        var buildBindingPassed = BindingPassed(identity, identityVerification);
        var samplesPath = Path.Combine(runRoot, "Soak", "resource-samples.json");
        RevalidationJson.Write(samplesPath, new
        {
            SchemaVersion = "advanced-final-freeze-soak-samples-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            RuntimeAssemblyHashes = identity.RuntimeAssemblyHashes,
            result.Samples
        });
        RevalidationJson.Write(Path.Combine(runRoot, "soak-summary.json"), new
        {
            SchemaVersion = "advanced-final-freeze-soak-v1",
            identity.RunId,
            identity.BuildIdentityId,
            identity.SourceManifestHash,
            TestAssemblyHashes = identity.TestAssemblyHashes,
            RuntimeAssemblyHashes = identity.RuntimeAssemblyHashes,
            identity.DatabaseMigrationHead,
            identity.DatabaseMigrationHeadHash,
            DatabaseIdentitySafeReference = "MariaDB/configured-test-instance",
            Result = result,
            SamplesArtifact = "Soak/resource-samples.json",
            SamplesArtifactHash = RevalidationJson.Hash(samplesPath),
            BuildBinding = buildBindingPassed ? "PASS" : "FAIL"
        });
    }

    private static bool BindingPassed(
        RevalidationBuildIdentity identity,
        RevalidationIdentityVerification verification) =>
        !string.IsNullOrWhiteSpace(identity.BuildIdentityId) &&
        verification.SourceHashMatches && verification.AssemblyHashesUnchanged;

    private static RevalidationTestSuite ImportTrx(
        string root, RevalidationBuildIdentity identity, string name, string stagingRoot, string destinationRoot)
    {
        var candidates = Directory.EnumerateFiles(stagingRoot, "*.trx", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Document = XDocument.Load(path, LoadOptions.None) })
            .Select(item => new { item.Path, item.Document, Identity = TrxIdentity(item.Document) }).ToArray();
        VerificationGuard.Require(candidates.Length > 0, $"{name} did not produce TRX evidence.");
        var selected = candidates.GroupBy(item => item.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => File.GetLastWriteTimeUtc(item.Path)).First())
            .OrderBy(item => item.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicates = candidates.Length - selected.Length;
        Directory.CreateDirectory(destinationRoot);
        var total = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var starts = new List<DateTimeOffset>();
        var ends = new List<DateTimeOffset>();
        var artifacts = new List<string>();
        var assemblyHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in selected)
        {
            var counters = item.Document.Descendants().First(value => value.Name.LocalName == "Counters");
            total += Attribute(counters, "total");
            passed += Attribute(counters, "passed");
            failed += Math.Max(Attribute(counters, "failed"), Attribute(counters, "error") + Attribute(counters, "timeout") + Attribute(counters, "aborted"));
            skipped += Attribute(counters, "notExecuted") + Attribute(counters, "inconclusive") + Attribute(counters, "notRunnable");
            var times = item.Document.Descendants().FirstOrDefault(value => value.Name.LocalName == "Times");
            if (DateTimeOffset.TryParse(times?.Attribute("start")?.Value, out var start)) starts.Add(start.ToUniversalTime());
            if (DateTimeOffset.TryParse(times?.Attribute("finish")?.Value, out var finish)) ends.Add(finish.ToUniversalTime());
            var assemblyName = item.Identity.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
            var assembly = identity.TestAssemblyHashes.Where(pair => Path.GetFileName(pair.Key).Equals(assemblyName, StringComparison.OrdinalIgnoreCase) &&
                    pair.Key.Contains("/Debug/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault();
            VerificationGuard.Require(!string.IsNullOrEmpty(assembly.Key), $"TRX assembly '{assemblyName}' is not bound to the BuildIdentity.");
            assemblyHashes[assembly.Key] = assembly.Value;
            SanitizeTrx(item.Document);
            var destination = Path.Combine(destinationRoot, $"{Path.GetFileNameWithoutExtension(assemblyName)}.trx");
            item.Document.Save(destination, SaveOptions.DisableFormatting);
            artifacts.Add(Path.GetRelativePath(RevalidationPaths.RunRoot(root, identity.RunId), destination).Replace('\\', '/'));
        }
        var hashes = artifacts.Select(path => RevalidationJson.Describe(RevalidationPaths.RunRoot(root, identity.RunId),
            Path.Combine(RevalidationPaths.RunRoot(root, identity.RunId), path.Replace('/', Path.DirectorySeparatorChar))));
        var hash = RevalidationJson.CompositeHash(hashes);
        var status = total > 0 && total == passed && failed == 0 && skipped == 0 ? "PASS" : "FAIL";
        return new RevalidationTestSuite(name, status, total, passed, failed, skipped, selected.Length, duplicates,
            starts.Count == 0 ? DateTimeOffset.MinValue : starts.Min(), ends.Count == 0 ? DateTimeOffset.MinValue : ends.Max(),
            hash, artifacts, assemblyHashes);
    }

    private static string TrxIdentity(XDocument document)
    {
        var assemblies = document.Descendants().Where(value => value.Name.LocalName == "TestMethod")
            .Select(value => Path.GetFileName(value.Attribute("codeBase")?.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return assemblies.Length == 0 ? throw new InvalidDataException("TRX test assembly identity is missing.") : string.Join(';', assemblies);
    }

    private static void SanitizeTrx(XDocument document)
    {
        foreach (var attribute in document.Descendants().Attributes().ToArray())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }
            if (attribute.Name.LocalName == "codeBase")
            {
                attribute.Value = Path.GetFileName(attribute.Value);
            }
            else if (Regex.IsMatch(attribute.Value, @"(?<![A-Za-z0-9_])[A-Za-z]:[\\/]"))
            {
                attribute.Value = "<local-path-redacted>";
            }
            else if (attribute.Name.LocalName is "computerName" or "userName")
            {
                attribute.Value = "revalidation";
            }
            else if (attribute.Name.LocalName == "name" && attribute.Parent?.Name.LocalName == "TestRun")
            {
                attribute.Value = "revalidation";
            }
        }
        foreach (var output in document.Descendants().Where(value => value.Name.LocalName is "StdOut" or "StdErr"))
        {
            if (Regex.IsMatch(output.Value, @"(?<![A-Za-z0-9_])[A-Za-z]:[\\/]")) output.Value = "<local-output-redacted>";
        }
    }

    private static int CountBuildDiagnostics(
        string runRoot,
        IReadOnlyList<RevalidationProcessResult> results,
        string severity)
    {
        var pattern = new Regex(
            $@"(?im)^.*\b{Regex.Escape(severity)}\s+(?:[A-Z]{{2,}}\d{{3,}}|[A-Z]+\d+)\b.*$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
        return results
            .Select(value => Path.Combine(runRoot, value.LogArtifact.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .SelectMany(path => File.ReadLines(path, Encoding.UTF8))
            .Count(line => pattern.IsMatch(line));
    }

    private static async Task<RevalidationProcessResult> RunAsync(
        string root, string runRoot, string name, string executable, IReadOnlyList<string> arguments,
        string relativeLog, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Unable to start {name}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        var combined = SanitizeLog(root, $"COMMAND: {executable} {string.Join(' ', arguments)}\n{output}\n{error}");
        var logPath = Path.Combine(runRoot, relativeLog.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, combined, new UTF8Encoding(false));
        var result = new RevalidationProcessResult(name, $"{executable} {string.Join(' ', arguments)}", start,
            DateTimeOffset.UtcNow, process.ExitCode, process.ExitCode == 0 ? "PASS" : "FAIL",
            relativeLog.Replace('\\', '/'), RevalidationJson.Hash(logPath));
        Console.WriteLine($"REVALIDATION_PROCESS name={name} status={result.Status} exit={result.ExitCode}");
        return result;
    }

    private static string SanitizeLog(string root, string value)
    {
        var result = value.Replace(root, "<workspace>", StringComparison.OrdinalIgnoreCase);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) result = result.Replace(profile, "<profile>", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(result, @"[A-Za-z]:[\\/][^\r\n]*", "<local-path-redacted>");
    }

    private static int Attribute(XElement value, string name) =>
        int.TryParse(value.Attribute(name)?.Value, out var result) ? result : 0;

    private static string FindOfficialMapRoot(string expectedClientSha)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var executable in EnumerateFilesSafe(desktop, "God2_opt.exe"))
        {
            if (!string.Equals(RevalidationJson.Hash(executable), expectedClientSha, StringComparison.OrdinalIgnoreCase)) continue;
            var mapRoot = Path.Combine(Path.GetDirectoryName(executable)!, "Data2", "map");
            if (Directory.Exists(mapRoot)) return mapRoot;
        }
        throw new DirectoryNotFoundException("Build-locked official MBD map corpus was not found.");
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string searchPattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(current, searchPattern, SearchOption.TopDirectoryOnly);
                directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            foreach (var file in files) yield return file;
            foreach (var directory in directories) pending.Push(directory);
        }
    }
}
