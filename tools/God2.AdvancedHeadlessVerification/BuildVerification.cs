using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace God2.AdvancedHeadlessVerification;

public sealed record BuildVerificationResult(
    string SchemaVersion,
    string Status,
    DateTimeOffset CompletedAtUtc,
    string SourceFingerprintSha256,
    int ExitCode,
    int WarningCount,
    int ErrorCount,
    string OutputSha256);

public static class BuildVerificationRunner
{
    public static async Task<BuildVerificationResult> RunAsync(string repositoryRoot, string sourceFingerprintSha256)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add("God2ClassicServer.sln");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--warnaserror");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start the verified solution build.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        var combinedHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{output}\n--STDERR--\n{error}"));
        if (process.ExitCode != 0)
        {
            var diagnostic = (output + Environment.NewLine + error).Trim();
            if (diagnostic.Length > 4000)
            {
                diagnostic = diagnostic[^4000..];
            }
            throw new InvalidOperationException($"Verified solution build failed with exit code {process.ExitCode}.{Environment.NewLine}{diagnostic}");
        }

        return new BuildVerificationResult(
            "advanced-headless-verification/build-v1",
            "PASS",
            DateTimeOffset.UtcNow,
            sourceFingerprintSha256,
            process.ExitCode,
            WarningCount: 0,
            ErrorCount: 0,
            Convert.ToHexString(combinedHash));
    }
}
