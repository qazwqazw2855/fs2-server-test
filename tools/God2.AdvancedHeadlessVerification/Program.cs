using God2.AdvancedHeadlessVerification;
using God2.OfflineClientReverseEngineering;
using System.Text.Json;

string Option(string name, string fallback)
{
    for (var index = 0; index + 1 < args.Length; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }
    return fallback;
}

string FindRepositoryRoot()
{
    var seeds = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (var seed in seeds)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(seed));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "Unable to locate God2ClassicServer.sln. Run the tool inside the repository or pass --root <repository-path>.");
}

string WriteDiagnosticLog(Exception exception)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "AdvancedHeadlessVerification.diagnostic.log");
    try
    {
        var details = new System.Text.StringBuilder();
        details.AppendLine($"TimestampUtc: {DateTimeOffset.UtcNow:O}");
        details.AppendLine($"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        details.AppendLine($"ProcessArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        details.AppendLine($"OSArchitecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");

        Exception? current = exception;
        var depth = 0;
        while (current is not null)
        {
            details.AppendLine($"Exception[{depth}].Type: {current.GetType().FullName}");
            details.AppendLine($"Exception[{depth}].HResult: {current.HResult}");
            details.AppendLine($"Exception[{depth}].Message: {RevalidationSecurityScanner.SanitizeDiagnostic(current.Message)}");
            details.AppendLine($"Exception[{depth}].StackTrace:");
            details.AppendLine(RevalidationSecurityScanner.SanitizeDiagnostic(current.StackTrace ?? "<none>"));
            current = current.InnerException;
            depth++;
        }

        File.WriteAllText(logPath, details.ToString(), new System.Text.UTF8Encoding(false));
        return Path.GetFileName(logPath);
    }
    catch (Exception logException)
    {
        return $"<diagnostic log unavailable: {logException.GetType().FullName}>";
    }
}

try
{
    var phase = Option("--phase", "fast").ToLowerInvariant();
    var rootOption = Option("--root", string.Empty);
    var repositoryRoot = string.IsNullOrWhiteSpace(rootOption)
        ? FindRepositoryRoot()
        : Path.GetFullPath(rootOption);
    var runId = Option("--run-id", string.Empty);
    var writer = new VerificationArtifactWriter(repositoryRoot);
    if (!phase.StartsWith("revalidation-", StringComparison.Ordinal) &&
        !string.Equals(phase, "official-wire-phase1", StringComparison.Ordinal) &&
        !string.Equals(phase, "official-wire-phase2", StringComparison.Ordinal)) writer.BeginStage(phase);

    switch (phase)
    {
        case "fast":
            {
                var result = new AdvancedVerificationRunner().RunFast();
                writer.WriteFast(result);
                Console.WriteLine($"FAST_PASS exhaustive={result.Exhaustive.CaseCount} property={result.Property.GeneratedCases} differential={result.Differential.EventCount} schedules={result.Concurrency.ScheduleCount}");
                break;
            }
        case "mariadb":
            {
                var cases = int.Parse(Option("--cases", "10000"), System.Globalization.CultureInfo.InvariantCulture);
                var workers = int.Parse(Option("--workers", "8"), System.Globalization.CultureInfo.InvariantCulture);
                var result = await new MariaDbHighValueVerifier().RunAsync(repositoryRoot, cases, workers);
                writer.WriteMariaDb(result);
                Console.WriteLine($"MARIADB_PASS cases={result.IntegrationCaseCount} exactlyOnce={result.ExactlyOnceVerificationCount} rollback={result.TransactionRollbackCount} recovery={result.RecoveryCount}");
                break;
            }
        case "soak":
            {
                var duration = TimeSpan.Parse(Option("--duration", "02:00:00"), System.Globalization.CultureInfo.InvariantCulture);
                var workers = int.Parse(Option("--workers", "2"), System.Globalization.CultureInfo.InvariantCulture);
                var result = await new LowConcurrencyResourceSoak().RunAsync(repositoryRoot, duration, workers);
                writer.WriteResources(result);
                Console.WriteLine($"RESOURCE_SOAK_PASS duration={result.DurationSeconds:F3} loops={result.CompletedGameplayLoops} samples={result.SampleCount}");
                break;
            }
        case "build":
            {
                var sourceFingerprint = OfflineVerificationEvidence.Load(repositoryRoot).SourceFingerprintSha256;
                var result = await BuildVerificationRunner.RunAsync(repositoryRoot, sourceFingerprint);
                writer.WriteBuild(result);
                Console.WriteLine($"BUILD_PASS warnings={result.WarningCount} errors={result.ErrorCount}");
                break;
            }
        case "finalize":
            {
                try
                {
                    writer.FinalizeFreeze();
                    Console.WriteLine("FINAL_FREEZE_PASS");
                }
                catch (InvalidOperationException exception)
                {
                    writer.MarkFinalizeBlocked(exception.Message);
                    Console.Error.WriteLine($"FINAL_FREEZE_BLOCKED reason={exception.Message}");
                    Environment.ExitCode = 5;
                }
                break;
            }
        case "revalidation-prepare":
            {
                var identity = await RevalidationExecution.PrepareAsync(repositoryRoot, runId);
                Console.WriteLine($"REVALIDATION_PREPARE_PASS runId={identity.RunId} buildIdentityId={identity.BuildIdentityId}");
                break;
            }
        case "revalidation-regression":
            {
                await RevalidationExecution.RunRegressionAsync(repositoryRoot, runId);
                Console.WriteLine($"REVALIDATION_REGRESSION_PASS runId={runId}");
                break;
            }
        case "revalidation-mariadb":
            {
                var cases = int.Parse(Option("--cases", "10000"), System.Globalization.CultureInfo.InvariantCulture);
                var workers = int.Parse(Option("--workers", "8"), System.Globalization.CultureInfo.InvariantCulture);
                await RevalidationExecution.RunMariaDbAsync(repositoryRoot, runId, cases, workers);
                Console.WriteLine($"REVALIDATION_MARIADB_PASS runId={runId} cases={cases}");
                break;
            }
        case "revalidation-map":
            {
                RevalidationExecution.RunMap(repositoryRoot, runId);
                Console.WriteLine($"REVALIDATION_MAP_PASS runId={runId}");
                break;
            }
        case "revalidation-soak":
            {
                var duration = TimeSpan.Parse(Option("--duration", "02:00:00"), System.Globalization.CultureInfo.InvariantCulture);
                var workers = int.Parse(Option("--workers", "2"), System.Globalization.CultureInfo.InvariantCulture);
                var seed = long.Parse(Option("--seed", "5138394310215845210"), System.Globalization.CultureInfo.InvariantCulture);
                await RevalidationExecution.RunSoakAsync(repositoryRoot, runId, duration, workers, seed);
                Console.WriteLine($"REVALIDATION_SOAK_PASS runId={runId} duration={duration.TotalSeconds}");
                break;
            }
        case "revalidation-security":
            {
                var result = RevalidationSecurityScanner.Run(repositoryRoot, runId);
                Console.WriteLine($"REVALIDATION_SECURITY_PASS runId={runId} scanned={result.ScannedFileCount}");
                break;
            }
        case "revalidation-bind":
            {
                RevalidationFinalizer.BindEvidence(repositoryRoot, runId);
                Console.WriteLine($"REVALIDATION_BIND_PASS runId={runId}");
                break;
            }
        case "revalidation-finalize":
            {
                try
                {
                    var result = RevalidationFinalizer.Finalize(repositoryRoot, runId);
                    Console.WriteLine($"REVALIDATION_FINAL_PASS runId={runId} status={result.FinalStatus}");
                }
                catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or JsonException)
                {
                    Console.Error.WriteLine($"REVALIDATION_FINAL_BLOCKED type={exception.GetType().Name}");
                    Environment.ExitCode = 5;
                }
                break;
            }
        case "official-wire-phase1":
            {
                await OfficialWireClosedLoopPhase1.RunAsync(repositoryRoot);
                Console.WriteLine("OFFICIAL_WIRE_CLOSED_LOOP_PHASE1_COMPLETE");
                break;
            }
        case "official-wire-phase2":
            {
                await OfficialWireClosedLoopPhase2.RunAsync(repositoryRoot);
                Console.WriteLine("OFFICIAL_WIRE_CLOSED_LOOP_PHASE2_COMPLETE");
                break;
            }
        default:
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown verification phase.");
    }
}
catch (Exception exception)
{
    var diagnosticLog = WriteDiagnosticLog(exception);
    Console.Error.WriteLine("ADVANCED_HEADLESS_VERIFICATION_FAILED");
    Console.Error.WriteLine($"Exception Type: {exception.GetType().FullName}");
    Console.Error.WriteLine($"Message: {RevalidationSecurityScanner.SanitizeDiagnostic(exception.Message)}");
    Console.Error.WriteLine($"InnerException Type: {exception.InnerException?.GetType().FullName ?? "<none>"}");
    Console.Error.WriteLine($"Diagnostic Log: {diagnosticLog}");
    Environment.ExitCode = 10;
}
