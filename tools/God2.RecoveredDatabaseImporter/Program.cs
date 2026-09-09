using System.Text;
using System.Text.Json;

namespace God2.RecoveredDatabaseImporter;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Any(value => value is "--help" or "-h"))
        {
            PrintUsage();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var invocation = ImporterInvocation.Parse(args);
            var result = await new God2RecoveryBundleImporter().ImportAsync(
                new RecoveryImportOptions(invocation.RepositoryRoot, invocation.BundlePath, invocation.OutputRoot),
                cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(result, RecoveryJson.Options));
            return 0;
        }
        catch (RecoveryImportException exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                status = "RECOVERY_BUNDLE_IMPORTER_BLOCKED",
                code = exception.Code,
                message = exception.Message,
                productionDatabaseConnectionAttempted = false,
                productionDatabaseMutationAttempted = false
            }, RecoveryJson.Options));
            return 4;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("RECOVERY_BUNDLE_IMPORTER_CANCELLED");
            return 5;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                status = "RECOVERY_BUNDLE_IMPORTER_FAILED",
                code = "importer.execution_failed",
                message = exception.Message,
                productionDatabaseConnectionAttempted = false,
                productionDatabaseMutationAttempted = false
            }, RecoveryJson.Options));
            return 6;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                status = "RECOVERY_BUNDLE_IMPORTER_FAILED",
                code = "importer.unexpected_failure",
                message = exception.Message,
                productionDatabaseConnectionAttempted = false,
                productionDatabaseMutationAttempted = false
            }, RecoveryJson.Options));
            return 7;
        }
    }

    private static void PrintUsage() => Console.WriteLine(
        "God2.RecoveredDatabaseImporter --bundle <God2UltimateRecovery.zip> [--repository-root <path>] [--output-root <path>]");
}

internal sealed record ImporterInvocation(string RepositoryRoot, string BundlePath, string OutputRoot)
{
    private static readonly IReadOnlySet<string> AllowedArguments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bundle",
        "repository-root",
        "output-root"
    };

    public static ImporterInvocation Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {args[index]}");
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for {args[index]}");
            }
            var key = args[index][2..];
            if (!AllowedArguments.Contains(key))
            {
                throw new ArgumentException($"Unknown argument: --{key}");
            }
            if (!values.TryAdd(key, args[++index]))
            {
                throw new ArgumentException($"Duplicate argument: --{key}");
            }
        }

        var repositoryRoot = Path.GetFullPath(values.GetValueOrDefault("repository-root") ?? Directory.GetCurrentDirectory());
        if (!values.TryGetValue("bundle", out var bundle) || string.IsNullOrWhiteSpace(bundle))
        {
            throw new ArgumentException("--bundle is required.");
        }
        var bundlePath = Path.GetFullPath(bundle);
        var outputRoot = Path.GetFullPath(values.GetValueOrDefault("output-root") ??
            Path.Combine(repositoryRoot, "Artifacts", "Ultimate", "ServerDbImporter"));
        return new ImporterInvocation(repositoryRoot, bundlePath, outputRoot);
    }
}
