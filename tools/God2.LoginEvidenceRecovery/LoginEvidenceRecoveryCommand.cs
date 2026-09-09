namespace God2.LoginEvidenceRecovery;

public static class LoginEvidenceRecoveryCommand
{
    public static LoginEvidenceAnalysisSummary Run(string startPath)
    {
        var projectRoot = FindProjectRoot(startPath);
        var workspaceRoot = Directory.GetParent(projectRoot)?.FullName
            ?? throw new InvalidOperationException("God2 workspace root was not found.");
        var unifiedRoot = Path.Combine(workspaceRoot, "God2 Classic Unified Server");
        if (!Directory.Exists(unifiedRoot))
        {
            throw new DirectoryNotFoundException($"Read-only evidence source was not found: {unifiedRoot}");
        }

        return new LoginEvidenceAnalyzer(projectRoot, unifiedRoot).Run();
    }

    private static string FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("God2ClassicServer.sln was not found.");
    }
}
