using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Startup;
using God2.ClassicServer.ConsoleHost;
using God2.ClassicServer.Infrastructure;
using God2.ClassicServer.Persistence.OfficialImports;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.IntegrationTests;

public sealed class ArchitectureAndHostTests
{
    [Fact]
    public void Domain_project_has_no_project_references()
    {
        var references = ProjectReferences("src/God2.ClassicServer.Domain/God2.ClassicServer.Domain.csproj");

        Assert.Empty(references);
    }

    [Fact]
    public void Application_project_depends_only_on_domain()
    {
        var references = ProjectReferences("src/God2.ClassicServer.Application/God2.ClassicServer.Application.csproj");

        Assert.Equal(["God2.ClassicServer.Domain"], references);
    }

    [Fact]
    public void Projects_do_not_have_circular_references()
    {
        var graph = ProjectGraph();

        foreach (var project in graph.Keys)
        {
            Assert.False(HasCycle(project, project, graph, []), $"{project} has a circular reference.");
        }
    }

    [Fact]
    public void Console_host_build_removes_sensitive_runtime_evidence_from_packaged_output()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.ConsoleHost",
            "God2.ClassicServer.ConsoleHost.csproj"));
        var removedDirectories = project.Descendants("RemoveDir")
            .Select(element => element.Attribute("Directories")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(
            removedDirectories,
            value => value.Contains("runtime\\protocol-evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Source_configuration_bat_and_docs_do_not_contain_local_absolute_paths()
    {
        var root = RepositoryRoot();
        var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat",
            ".cs",
            ".cpp",
            ".csproj",
            ".json",
            ".md",
            ".props",
            ".ps1",
            ".sln",
            ".sql",
            ".targets",
            ".txt"
        };
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => sourceExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Validation{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Automation{Path.DirectorySeparatorChar}State{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}backups{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var forbiddenUserProfilePrefix = string.Join(Path.DirectorySeparatorChar, "C:", "Users", string.Empty);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(forbiddenUserProfilePrefix, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Forbidden_server_projects_do_not_exist()
    {
        var projectNames = Directory.EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.DoesNotContain("LoginServer", projectNames);
        Assert.DoesNotContain("WorldServer", projectNames);
        Assert.DoesNotContain(projectNames, name => name is not null && name.Contains("Gui", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_projects_target_dotnet_10_and_only_console_host_is_executable()
    {
        var root = RepositoryRoot();
        var projects = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var executableProjects = new List<string>();
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            Assert.All(document.Descendants("TargetFramework"), element => Assert.Equal("net10.0", element.Value));
            if (document.Descendants("OutputType").Any(element => string.Equals(element.Value, "Exe", StringComparison.OrdinalIgnoreCase)))
            {
                executableProjects.Add(Path.GetFileNameWithoutExtension(project));
            }
        }

        Assert.Equal(["God2.ClassicServer.ConsoleHost"], executableProjects.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Bat_invokes_only_one_server_executable()
    {
        var batFiles = Directory.EnumerateFiles(RepositoryRoot(), "*.bat", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Start_Server.bat"], batFiles);

        var bat = File.ReadAllText(Path.Combine(RepositoryRoot(), "Start_Server.bat"));

        Assert.Contains("scripts\\Start-God2ClassicServer.ps1", bat, StringComparison.OrdinalIgnoreCase);
        var wrapper = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Start-God2ClassicServer.ps1"));
        Assert.Contains("Get-God2ServerExecutablePath", wrapper, StringComparison.Ordinal);
        Assert.Contains("& $server @ServerArguments", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginServer.exe", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorldServer.exe", bat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unified", bat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Automation_host_scheduled_task_scripts_keep_highest_relative_contract()
    {
        var root = RepositoryRoot();
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));
        var register = File.ReadAllText(Path.Combine(root, "Automation", "Register-God2AutomationHost.ps1"));
        var start = File.ReadAllText(Path.Combine(root, "Automation", "Start-God2AutomationHost.ps1"));
        var test = File.ReadAllText(Path.Combine(root, "Automation", "Test-God2AutomationHost.ps1"));

        Assert.Contains("Get-God2AutomationTaskValidation", common, StringComparison.Ordinal);
        Assert.Contains("HighestAvailable", common, StringComparison.Ordinal);
        Assert.Contains("-File .\\Automation\\God2AutomationHost.ps1", common, StringComparison.Ordinal);
        Assert.Contains("-RunLevel Highest", register, StringComparison.Ordinal);
        Assert.Contains("-LogonType Interactive", register, StringComparison.Ordinal);
        Assert.Contains("-WorkingDirectory $repoRoot", register, StringComparison.Ordinal);
        Assert.Contains("AlreadyRegistered", register, StringComparison.Ordinal);
        Assert.Contains("Get-God2AutomationTaskValidation", start, StringComparison.Ordinal);
        Assert.Contains("Register-God2AutomationHost.ps1", start, StringComparison.Ordinal);
        Assert.Contains("schtasks /Run /TN $TaskName", start, StringComparison.Ordinal);
        Assert.Contains("scheduledTaskCorrect", test, StringComparison.Ordinal);
        Assert.DoesNotContain("-Verb RunAs", common + register + start + test, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\", common + register + start + test, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Automation_environment_preflight_declares_required_matrix_and_secret_gate()
    {
        var root = RepositoryRoot();
        var preflight = File.ReadAllText(Path.Combine(root, "Automation", "Test-God2AutomationEnvironment.ps1"));
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));

        foreach (var setting in new[]
        {
            "GOD2_DB_PASSWORD",
            "DB Host",
            "DB Port",
            "DB Schema/Database",
            "DB Username",
            "SSL Mode",
            "Server Config Path",
            "Runtime Import Path",
            "Launcher Path",
            "Client Path",
            "Automation Status Path",
            "Automation Command Path",
            "Log Path",
            "Artifact Path"
        })
        {
            Assert.Contains(setting, preflight, StringComparison.Ordinal);
        }

        Assert.Contains("Initialize-God2DatabasePasswordEnvironment", common + preflight, StringComparison.Ordinal);
        Assert.Contains("mariadb.password_missing", preflight, StringComparison.Ordinal);
        Assert.Contains("Secret Resolve", preflight, StringComparison.Ordinal);
        Assert.Contains("MariaDB Authentication", preflight, StringComparison.Ordinal);
        Assert.Contains("secretValueEmitted = $false", preflight, StringComparison.Ordinal);
        Assert.Contains("taskArgumentsContainSecret = $false", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_database_secret_supports_dpapi_and_config_value_without_plaintext_output()
    {
        var root = RepositoryRoot();
        var prepare = File.ReadAllText(Path.Combine(root, "Automation", "Prepare-God2DatabaseSecret.ps1"));
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("ProtectedData", prepare + common, StringComparison.Ordinal);
        Assert.Contains("DataProtectionScope]::CurrentUser", prepare + common, StringComparison.Ordinal);
        Assert.Contains("db-secret.bin", prepare + common, StringComparison.Ordinal);
        Assert.Contains("secretValue = \"MASKED\"", prepare, StringComparison.Ordinal);
        Assert.Contains("Initialize-God2DatabasePasswordEnvironment", common, StringComparison.Ordinal);
        Assert.Contains("[string]$storedConfig.password", common, StringComparison.Ordinal);
        Assert.Contains("New-God2DatabaseSecretStatus -HasSecret $true -Source \"ConfigValue\"", common, StringComparison.Ordinal);
        Assert.DoesNotContain("password = [string]$config.password", common, StringComparison.Ordinal);
        Assert.DoesNotContain("SetEnvironmentVariable($name, $config.password", common, StringComparison.Ordinal);
        Assert.DoesNotContain("SetEnvironmentVariable($EnvironmentVariableName, $password, \"User\")", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("SetEnvironmentVariable($EnvironmentVariableName, $password, \"Machine\")", prepare, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_host_status_exposes_database_secret_source_without_value()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("databaseSecretAvailable", host, StringComparison.Ordinal);
        Assert.Contains("databaseSecretSource", host, StringComparison.Ordinal);
        Assert.Contains("databaseSecretFailureCode", host, StringComparison.Ordinal);
        Assert.DoesNotContain("databaseSecretValue", host + common, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commandLine = $null", common, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_environment_preflight_uses_dotnet_probe_without_external_process_shortcuts()
    {
        var root = RepositoryRoot();
        var preflight = File.ReadAllText(Path.Combine(root, "Automation", "Test-God2AutomationEnvironment.ps1"));

        Assert.DoesNotContain("Start-Process", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start_Server.bat", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("God2 Classic Server.exe\"", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("God2_opt | Stop-Process", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("God2.AutomationEnvironmentProbe", preflight, StringComparison.Ordinal);
        Assert.Contains("mariadb-preflight", preflight, StringComparison.Ordinal);
        Assert.Contains("server-ready", preflight, StringComparison.Ordinal);
        Assert.Contains("mariadb-preflight-probe.json", preflight, StringComparison.Ordinal);
        Assert.Contains("server-ready-probe.json", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("Add-Type -Path", preflight, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Automation_environment_probe_captures_reflection_loader_diagnostics()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "tools", "God2.AutomationEnvironmentProbe", "God2.AutomationEnvironmentProbe.csproj"));
        var program = File.ReadAllText(Path.Combine(root, "tools", "God2.AutomationEnvironmentProbe", "Program.cs"));

        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("MySqlConnector", project, StringComparison.Ordinal);
        Assert.Contains("ReflectionTypeLoadException", program, StringComparison.Ordinal);
        Assert.Contains("LoaderExceptions", program, StringComparison.Ordinal);
        Assert.Contains("AssemblyDiagnostic", program, StringComparison.Ordinal);
        Assert.Contains("AssemblyName", program, StringComparison.Ordinal);
        Assert.Contains("TypeLoadDiagnostic", program, StringComparison.Ordinal);
        Assert.Contains("TypeName", program, StringComparison.Ordinal);
        Assert.Contains("DllPath", program, StringComparison.Ordinal);
        Assert.Contains("FusionLog", program, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_LocatesDistinctAccountAndPasswordControls()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var common = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("Get-LoginControlModel", host, StringComparison.Ordinal);
        Assert.Contains("DescribeWindowTree", common, StringComparison.Ordinal);
        Assert.Contains("Win32ChildWindow", host, StringComparison.Ordinal);
        Assert.Contains("ClientRelativeBoundsFallback", host, StringComparison.Ordinal);
        Assert.Contains("accountPasswordDistinct", host, StringComparison.Ordinal);
        Assert.Contains("Account", host, StringComparison.Ordinal);
        Assert.Contains("Password", host, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_VerifiesAccountInputCompleteness()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("Wait-WmCharCount", host, StringComparison.Ordinal);
        Assert.Contains("AccountInputVerified", host, StringComparison.Ordinal);
        Assert.Contains("expectedLength", host, StringComparison.Ordinal);
        Assert.Contains("receivedCharCount", host, StringComparison.Ordinal);
        Assert.Contains("AccountInputIncompleteNoSubmit", host, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_DoesNotSubmitWithIncompleteAccount()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("if (-not $accountResult.success)", host, StringComparison.Ordinal);
        Assert.Contains("Complete-LoginAutomationAttempt -Context $loginContext", host, StringComparison.Ordinal);
        Assert.Contains("SubmitExecuting", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("if (-not $accountResult.success)", StringComparison.Ordinal) <
            host.IndexOf("Add-LoginAutomationState -Context $loginContext -State \"SubmitExecuting\"", StringComparison.Ordinal));
    }

    [Fact]
    public void LoginAutomation_VerifiesPasswordFieldFocus()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var common = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("Focus-LoginControl", host, StringComparison.Ordinal);
        Assert.Contains("PasswordFieldFocusNotConfirmed", host, StringComparison.Ordinal);
        Assert.Contains("PasswordInputVerified", host, StringComparison.Ordinal);
        Assert.Contains("GetFocusedWindowFor", common, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_DoesNotSubmitWithoutPasswordFieldConfirmation()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("if (-not $passwordResult.success)", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("if (-not $passwordResult.success)", StringComparison.Ordinal) <
            host.IndexOf("Add-LoginAutomationState -Context $loginContext -State \"SubmitExecuting\"", StringComparison.Ordinal));
    }

    [Fact]
    public void LoginAutomation_RetriesAfterFocusLoss()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("for ($focusAttempt = 1; $focusAttempt -le 3; $focusAttempt++)", host, StringComparison.Ordinal);
        Assert.Contains("for ($fieldAttempt = 1; $fieldAttempt -le 3; $fieldAttempt++)", host, StringComparison.Ordinal);
        Assert.Contains("FocusNotConfirmed", host, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_SubmitRequiresObservedStateTransition()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("SubmitVerificationWaiting", host, StringComparison.Ordinal);
        Assert.Contains("observedTransition = $true", host, StringComparison.Ordinal);
        Assert.Contains("SubmitDeliveredNoTransition", host, StringComparison.Ordinal);
        Assert.Contains("Test-ServerSelectionScreenImage", host, StringComparison.Ordinal);
        Assert.Contains("Test-CharacterSelectScreenImage", host, StringComparison.Ordinal);
    }

    [Fact]
    public void World_screen_detector_uses_its_independent_toolbar_and_chat_contract()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var start = host.IndexOf("function Test-WorldScreenImage", StringComparison.Ordinal);
        var end = host.IndexOf("function Get-FileSha256", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var detector = host[start..end];
        Assert.Contains("$topToolbarBlue -gt 900 -and $bottomChatBlue -gt 900", detector, StringComparison.Ordinal);
        Assert.DoesNotContain("Test-LoginScreenImage", detector, StringComparison.Ordinal);
        Assert.DoesNotContain("Test-ServerSelectionScreenImage", detector, StringComparison.Ordinal);
        Assert.DoesNotContain("Test-CharacterSelectScreenImage", detector, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_RedactsCredentialsFromArtifacts()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("Save-RedactedWindowScreenshot", host, StringComparison.Ordinal);
        Assert.Contains("credentialRedaction = \"PASS\"", host, StringComparison.Ordinal);
        Assert.Contains("No plaintext account or password is stored", host, StringComparison.Ordinal);
        Assert.DoesNotContain("NotePropertyName password", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NotePropertyName account", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordText", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountText", host, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginAutomation_ReplayCleanupIsScopedToTheActiveLauncherProfile()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("CleanupReplayProcesses", host, StringComparison.Ordinal);
        Assert.Contains("Get-ActiveRuntimeExecutablePaths", host, StringComparison.Ordinal);
        Assert.Contains("Get-ActiveRuntimeProcesses", host, StringComparison.Ordinal);
        Assert.Contains("Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient", host, StringComparison.Ordinal);
        Assert.Contains("ActiveLauncherProfileExactExecutablePaths", host, StringComparison.Ordinal);
        Assert.Contains("ActiveLauncherProfileExactClientPath", host, StringComparison.Ordinal);
        Assert.Contains("StopClientAndExit target PID is not an active-profile client.", host, StringComparison.Ordinal);
        Assert.Contains("remainingLauncherCount", host, StringComparison.Ordinal);
        Assert.Contains("ReplayProcessesCleaned", host, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Get-Process -Name God2_opt, Launcher, God2ClassicLauncher -ErrorAction SilentlyContinue | ForEach-Object",
            host,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Get-Process -Name God2_opt, Launcher -ErrorAction SilentlyContinue | ForEach-Object",
            host,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Get-Process -Name God2_opt -ErrorAction SilentlyContinue | Stop-Process",
            host,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Map_identity_FileIO_trace_is_owned_scoped_and_never_captures_packets()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));
        var command = File.ReadAllText(Path.Combine(root, "Automation", "Invoke-God2MapFileIoTrace.ps1"));
        var analyzer = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.OfflineClientReverseEngineering",
            "Program.cs"));

        Assert.Contains("StartMapFileIoTrace", host + command, StringComparison.Ordinal);
        Assert.Contains("StopMapFileIoTrace", host + command, StringComparison.Ordinal);
        Assert.Contains("map-fileio-trace.json", host + command, StringComparison.Ordinal);
        Assert.Contains("Invoke-God2NativeCommand", host, StringComparison.Ordinal);
        Assert.Contains("Microsoft-Windows-Kernel-File", host, StringComparison.Ordinal);
        Assert.Contains("\"0x1F0\"", host, StringComparison.Ordinal);
        Assert.Contains("God2MapFileIo-", host, StringComparison.Ordinal);
        Assert.Contains("Invoke-God2NativeCommand -FilePath \"logman.exe\"", host, StringComparison.Ordinal);
        Assert.Contains("@(\"stop\", $traceSessionName, \"-ets\")", host, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError = $true", host, StringComparison.Ordinal);
        Assert.Contains("session identity mismatch", host, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outputs must remain under the repository root", host, StringComparison.Ordinal);
        Assert.Contains("packetCapture = $false", host, StringComparison.Ordinal);
        Assert.DoesNotContain("wpr.exe", host + command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Network", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FileShare.ReadWrite | FileShare.Delete", analyzer, StringComparison.Ordinal);
        Assert.Contains("TemporarilyUnavailable", analyzer, StringComparison.Ordinal);
        Assert.DoesNotContain("File.ReadAllBytes(physicalPath)", analyzer, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_identity_replay_requires_signed_local_launcher_and_client_pid_endpoint_attestation()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));
        var replay = File.ReadAllText(Path.Combine(root, "Automation", "Invoke-God2MapIdentityEvidence.ps1"));

        Assert.Contains("PostRemediationCompatibilityCheckpoint\\launcher-profile.json", replay, StringComparison.Ordinal);
        Assert.Contains("God2ClassicLauncher.exe", replay, StringComparison.Ordinal);
        Assert.Contains("expectedServerIp", replay, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", replay, StringComparison.Ordinal);
        Assert.Contains("expectedServerPort", replay, StringComparison.Ordinal);
        Assert.Contains("launcherProfilePath = $launcherProfilePath", replay, StringComparison.Ordinal);
        Assert.Contains("Get-God2LocalEndpointAttestation", host, StringComparison.Ordinal);
        Assert.Contains("Get-NetTCPConnection -OwningProcess $ClientPid", host, StringComparison.Ordinal);
        Assert.Contains("LocalEndpointNotAttested", host, StringComparison.Ordinal);
        Assert.Contains("localEndpointAttested", replay, StringComparison.Ordinal);
        Assert.Contains("packetCapture = $false", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Frozen_regression_recognizes_both_entry_buttons_and_keeps_the_client_pid_past_login()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("Get-EntryButtonEvidence -Rx1 0.24 -Rx2 0.48", host, StringComparison.Ordinal);
        Assert.Contains("Get-EntryButtonEvidence -Rx1 0.51 -Rx2 0.76", host, StringComparison.Ordinal);
        Assert.Contains("[int]$button.gold -lt 350", host, StringComparison.Ordinal);
        Assert.Contains("-not (Test-EnterGameScreenImage -Path $beforePath)", host, StringComparison.Ordinal);
        Assert.Contains("NativeApi]::PrintWindow", host, StringComparison.Ordinal);
        Assert.Contains("$rect = Get-ClientRectObject -Hwnd $Hwnd", host, StringComparison.Ordinal);
        Assert.Contains("0x00000003", host, StringComparison.Ordinal);
        Assert.Contains("$entryFallbackClickSent", host, StringComparison.Ordinal);
        Assert.Contains("NativeApi]::MoveTopLeft", host, StringComparison.Ordinal);
        Assert.Contains("Resolve-StableGod2ClientWindow -InitialSnapshot $refreshedSnapshot", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("return \"CharacterSelectReady\"", StringComparison.Ordinal) <
            host.IndexOf("return \"ServerSelectionScreen\"", StringComparison.Ordinal));
        Assert.Contains("$bottomCharacterActions -gt 1200", host, StringComparison.Ordinal);
        Assert.Contains("alreadyPastLogin = $true", host, StringComparison.Ordinal);
        Assert.Contains("trace = $trace", host, StringComparison.Ordinal);
        Assert.Contains("targetPid = [int]$clientSnapshot.pid", host, StringComparison.Ordinal);
        Assert.Contains("if ($worldCaptureCommand -or $frozenRegressionCommand)", host, StringComparison.Ordinal);
        Assert.Contains("Start-LoginAttemptTraceProbe -TargetPid ([int]$loginResult.trace.targetPid) -Attempt 100", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_identity_evidence_host_is_test_only_and_cannot_enter_production_composition()
    {
        var root = RepositoryRoot();
        var evidenceHost = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.AutomationEnvironmentProbe",
            "Program.cs"));
        var evidenceScript = File.ReadAllText(Path.Combine(
            root,
            "Automation",
            "Invoke-God2MapIdentityEvidence.ps1"));
        var production = File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.ConsoleHost",
            "ConsoleEntry.cs"));
        var evidenceHostCompositionStart = evidenceHost.IndexOf(
            "static async Task<int> RunMapIdentityEvidenceHostAsync",
            StringComparison.Ordinal);
        var evidenceHostCompositionEnd = evidenceHost.IndexOf(
            "static async Task WatchStopFileAsync",
            evidenceHostCompositionStart,
            StringComparison.Ordinal);
        Assert.True(evidenceHostCompositionStart >= 0);
        Assert.True(evidenceHostCompositionEnd > evidenceHostCompositionStart);
        var evidenceHostComposition = evidenceHost[evidenceHostCompositionStart..evidenceHostCompositionEnd];

        Assert.Contains("map-identity-evidence-host", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("WorldContentAuthorityKind.TestOnly", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("WorldContentMode.FrozenCompatibility", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("new InMemoryPortalTransitionStore()", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("19 => new MapIdentityEvidenceLocation(19, 4, 28, 34)", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("3 => new MapIdentityEvidenceLocation(3, 4, 196, 139)", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("Only evidence-pinned Client maps 19 and 3 are supported", evidenceHost, StringComparison.Ordinal);
        Assert.Contains(
            "clientMapId != 19 && (requestedX != baseline.X || requestedY != baseline.Y)",
            evidenceHost,
            StringComparison.Ordinal);
        Assert.Contains("evidence-client-map", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("evidence-position-x", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("evidence-position-y", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("SerializeTestOnlyMap19PositionEvidence", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("evidence-bootstrap-player-identity", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("GoldenCharacterId", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("GoldenCharacterName", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("GoldenIdentity", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("CurrentCreateProfile", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("CurrentCreateProfileMinimal", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("LegacyTailOmit5To10", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("OmitLegacyBootstrapFrames", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("BuildCurrentCreateProfilePlayerSpawnFrame128", evidenceHost, StringComparison.Ordinal);
        Assert.Contains("SkipFileIoTrace", evidenceScript, StringComparison.Ordinal);
        Assert.Contains("WorldEntryProfileIsolation", evidenceScript, StringComparison.Ordinal);
        Assert.Contains("StaticPositionConsumerAcceptance", evidenceScript, StringComparison.Ordinal);
        Assert.Contains("REUSED_PINNED_MAP19_RESOURCE_EVIDENCE", evidenceScript, StringComparison.Ordinal);
        Assert.Contains("NOT COLLECTED — FILEIO TRACE SKIPPED", evidenceScript, StringComparison.Ordinal);
        Assert.DoesNotContain("MariaDbPortalTransitionStore", evidenceHostComposition, StringComparison.Ordinal);
        Assert.DoesNotContain("MapIdentityEvidence", production, StringComparison.Ordinal);
        Assert.DoesNotContain("SerializeTestOnlyMap19PositionEvidence", production, StringComparison.Ordinal);
        Assert.Contains("new MariaDbWorldSessionCoordinator(", production, StringComparison.Ordinal);
        Assert.Contains("promotedGameplayContent,", production, StringComparison.Ordinal);
        Assert.Contains("productionMapRuntimes);", production, StringComparison.Ordinal);
        Assert.Contains("new MariaDbOfficialPortalMapIdentitySource(staticDataCache)", production, StringComparison.Ordinal);
        Assert.Contains("new LegacyCompatibilityWorldBootstrapProjector(", production, StringComparison.Ordinal);
        Assert.Contains("excludeLegacyWorldEntities: true", production, StringComparison.Ordinal);
        Assert.Contains("new MariaDbGameplayInventoryRuntime(configurationResult.Value.Database)", production, StringComparison.Ordinal);
        Assert.Contains("new MariaDbCanonicalGameplayContentRuntime(configurationResult.Value.Database)", production, StringComparison.Ordinal);
        Assert.DoesNotContain("new MariaDbPromotedGameplayContentRuntime(", production, StringComparison.Ordinal);
        Assert.Contains("inventoryTransactionCoordinator: gameplayInventory", production, StringComparison.Ordinal);
        Assert.Contains("petLifecycleCoordinator: gameplayInventory", production, StringComparison.Ordinal);
        Assert.Contains("new ServerOwnedMultiplayerRuntime(gameplayInventory,", production, StringComparison.Ordinal);
        Assert.Contains("new MariaDbPlayerSocialRepository(configurationResult.Value.Database)", production, StringComparison.Ordinal);
        Assert.Contains("serverOwnedMultiplayerRuntime: serverOwnedMultiplayer", production, StringComparison.Ordinal);
        Assert.Contains("new CompositeRuntimeCacheBuilder(", production, StringComparison.Ordinal);
        Assert.Contains("productionMapRuntimes)", production, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryInventoryPersistenceStore", production, StringComparison.Ordinal);
        Assert.Contains("SingleCharacterResponseModel()", File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.Runtime",
            "RuntimeFoundation.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationRunsServerFromRepositoryRootWithoutCopyingLocalConfigurationToBuildOutput()
    {
        var root = RepositoryRoot();
        var preflight = File.ReadAllText(Path.Combine(root, "Automation", "Test-God2AutomationEnvironment.ps1"));
        var frozenReplay = File.ReadAllText(Path.Combine(root, "Automation", "Invoke-FrozenProtocolRegression.ps1"));
        var lifecycleProbe = File.ReadAllText(Path.Combine(root, "Automation", "Invoke-CharacterLifecycleUiProbe.ps1"));
        var consoleProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "God2.ClassicServer.ConsoleHost",
            "God2.ClassicServer.ConsoleHost.csproj"));

        Assert.Contains("\"--base-directory\", $repoRoot", preflight, StringComparison.Ordinal);
        Assert.Contains("-WorkingDirectory $repoRoot", frozenReplay, StringComparison.Ordinal);
        Assert.Contains("-WorkingDirectory $repoRoot", lifecycleProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("_RuntimeConfig", consoleProject, StringComparison.Ordinal);
        Assert.Contains("<RemoveDir Directories=\"$(OutDir)config\"", consoleProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_server_readiness_is_listener_owned_and_localization_independent()
    {
        var root = RepositoryRoot();
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));
        var frozenReplay = File.ReadAllText(Path.Combine(root, "Automation", "Invoke-FrozenProtocolRegression.ps1"));
        var lifecycleProbe = File.ReadAllText(Path.Combine(root, "Automation", "Invoke-CharacterLifecycleUiProbe.ps1"));

        Assert.Contains("function Test-God2ProcessOwnsTcpListener", common, StringComparison.Ordinal);
        Assert.Contains("[int]$_.OwningProcess -eq $ProcessId", common, StringComparison.Ordinal);
        Assert.Contains("[string]$_.LocalAddress -eq $ExpectedLocalAddress", common, StringComparison.Ordinal);
        Assert.Contains(
            "Test-God2ProcessOwnsTcpListener -ProcessId $server.Id -Port 2592 -ExpectedLocalAddress \"127.0.0.1\"",
            frozenReplay,
            StringComparison.Ordinal);
        Assert.Contains(
            "Test-God2ProcessOwnsTcpListener -ProcessId $server.Id -Port 2592 -ExpectedLocalAddress \"127.0.0.1\"",
            lifecycleProbe,
            StringComparison.Ordinal);
        Assert.DoesNotContain("-match \"Server Ready\"", frozenReplay, StringComparison.Ordinal);
        Assert.DoesNotContain("-match \"Server Ready\"", lifecycleProbe, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_release_verifier_checks_file_lengths_and_retained_trx_evidence()
    {
        var common = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("Release length mismatch", common, StringComparison.Ordinal);
        Assert.Contains("Release test summary is missing or inconsistent", common, StringComparison.Ordinal);
        Assert.Contains("Artifacts\\FormalServerLogs\\TestResults\\", common, StringComparison.Ordinal);
        Assert.Contains("Release TRX length mismatch", common, StringComparison.Ordinal);
        Assert.Contains("Release TRX hash mismatch", common, StringComparison.Ordinal);
        Assert.Contains("Release TRX aggregate counters do not match", common, StringComparison.Ordinal);
        Assert.Contains("testSummary", common, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_migration_runner_pins_utf8_for_exact_localized_sql_literals()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Automation", "Invoke-God2PendingSchemaMigrationsAsAdmin.ps1"));

        Assert.Contains("[Text.UTF8Encoding]::new($false)", script, StringComparison.Ordinal);
        Assert.Contains("$sqlBytes = $utf8NoBom.GetBytes($Sql)", script, StringComparison.Ordinal);
        Assert.Contains("$process.StandardInput.BaseStream.Write($sqlBytes, 0, $sqlBytes.Length)", script, StringComparison.Ordinal);
        Assert.Contains("$process.StandardInput.BaseStream.Flush()", script, StringComparison.Ordinal);
        Assert.Contains("[Array]::Clear($sqlBytes, 0, $sqlBytes.Length)", script, StringComparison.Ordinal);
        Assert.Contains("Administrator migration input failed:", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardInput.Write($Sql)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StartInfo.StandardInputEncoding =", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_migration_runner_verifies_applied_migration_checksums()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Automation", "Invoke-God2PendingSchemaMigrationsAsAdmin.ps1"));

        Assert.Contains("SELECT ``Version``,COALESCE(``Checksum``,'')", script, StringComparison.Ordinal);
        Assert.Contains("$row -split \"`t\", 2", script, StringComparison.Ordinal);
        Assert.Contains("Migration checksum mismatch: $($migration.Name)", script, StringComparison.Ordinal);
        Assert.Contains("SET ``Checksum``='$checksum'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ``Version`` FROM ``__SchemaVersion``", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_account_provisioning_removes_legacy_direct_privileges_before_assigning_roles()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "scripts", "Provision-God2DatabaseRuntimeAccounts.ps1"));

        foreach (var account in new[] { "god2_server", "god2_catalog_builder" })
        {
            foreach (var host in new[] { "localhost", "127.0.0.1" })
            {
                Assert.Contains(
                    $"REVOKE ALL PRIVILEGES, GRANT OPTION FROM '{account}'@'{host}';",
                    script,
                    StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain("GRANT ALL PRIVILEGES ON god2.*", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT SELECT ON god2.* TO 'god2_catalog_builder'", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT SELECT,INSERT,UPDATE,DELETE,CREATE,ALTER,INDEX,TRIGGER ON god2_game.*", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GRANT god2_runtime_role TO 'god2_server'@'localhost';", script, StringComparison.Ordinal);
        Assert.Contains("GRANT god2_catalog_builder_role TO 'god2_catalog_builder'@'localhost';", script, StringComparison.Ordinal);
        Assert.Contains("GRANT INSERT ON god2_game_meta.admin_change_audit", script, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE ON god2_game_meta.admin_field_locks", script, StringComparison.Ordinal);
        Assert.Contains("FROM `information_schema`.`TRIGGERS`", script, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, TRIGGER ON `", script, StringComparison.Ordinal);
        Assert.Contains("`DEFINER` IN ('god2_catalog_builder@localhost','god2_catalog_builder@127.0.0.1')", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Credential_redaction_scan_fails_closed_when_a_file_cannot_be_read()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Automation", "Test-CharacterLifecycleCredentialRedaction.ps1"));

        Assert.Contains("$scanErrors.Add", script, StringComparison.Ordinal);
        Assert.Contains("scanErrorCount = $scanErrors.Count", script, StringComparison.Ordinal);
        Assert.Contains("Credential redaction scan could not read", script, StringComparison.Ordinal);
        Assert.Contains("$text = if ($null -eq $textValue) { \"\" }", script, StringComparison.Ordinal);
        Assert.DoesNotContain("catch {\n        continue\n    }", script.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_validation_accepts_runtime_eligible_derived_npc_wire_evidence()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "tools", "God2.GameCatalogBuilder", "Program.cs"));

        Assert.Contains("`wire_evidence_status` NOT IN ('Verified','Derived')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`wire_evidence_status`<>'Verified'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_trigger_coverage_excludes_tables_without_auditable_columns()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "tools", "God2.GameCatalogBuilder", "Program.cs"));

        Assert.Contains("AND EXISTS (", source, StringComparison.Ordinal);
        Assert.Contains("column_row.`COLUMN_KEY`<>'PRI'", source, StringComparison.Ordinal);
        Assert.Contains("'created_at_utc','updated_at_utc','password_hash'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_server_configuration_is_explicitly_production()
    {
        var root = RepositoryRoot();
        var serverConfig = File.ReadAllText(Path.Combine(root, "config", "server.json"));
        var schema = File.ReadAllText(Path.Combine(root, "config", "config.schema.md"));

        Assert.Contains("\"environment\": \"Production\"", serverConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("\"environment\": \"Development\"", serverConfig, StringComparison.Ordinal);
        Assert.Contains("| server.json | environment | string | Production |", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_waits_for_the_custom_launcher_start_control_to_be_uniquely_ready()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("$buttonDeadline = (Get-Date).AddMilliseconds($buttonTimeoutMs)", source, StringComparison.Ordinal);
        Assert.Contains("if ($startButton.Count -eq 1)", source, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Milliseconds 200", source, StringComparison.Ordinal);
        Assert.Contains("launcher-start-window-tree.json", source, StringComparison.Ordinal);
        Assert.Contains("God2ClassicLauncher start button identity was not uniquely verified.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_only_clicks_the_dui_launcher_start_control_after_build_skin_and_layout_identity_match()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("God2ClassicLauncherDuiWindow", source, StringComparison.Ordinal);
        Assert.Contains("launcherHashMatch", source, StringComparison.Ordinal);
        Assert.Contains("skinHashMatch", source, StringComparison.Ordinal);
        Assert.Contains("uniqueStartControl", source, StringComparison.Ordinal);
        Assert.Contains("clientSizeMatch", source, StringComparison.Ordinal);
        Assert.Contains("SendClientClick", source, StringComparison.Ordinal);

        var profilePath = Path.Combine(root, "Artifacts", "PostRemediationCompatibilityCheckpoint", "launcher-profile.json");
        if (!File.Exists(profilePath))
        {
            return;
        }
        var profile = File.ReadAllText(profilePath);
        Assert.Contains("FFC453C0A19EEFEABF73B392FBACACC8521941B68800643DD5EE72BA1BB36902", profile, StringComparison.Ordinal);
        Assert.Contains("293016882F38106AB5220CD04E725A2B72BD62212AB259D16E56B47C6FE9B9B5", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_automation_prefers_synchronous_character_messages_over_lossy_posted_characters()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var input = SourceSection(source, "function Invoke-ReliableFieldInput {", "function Get-NetworkTraceSummary {");

        Assert.Contains("if ($fieldAttempt -eq 2) { \"PostMessageWmChar\" } else { \"SendMessageWmChar\" }", input, StringComparison.Ordinal);
        Assert.Contains("SendUnicodeChars", input, StringComparison.Ordinal);
        Assert.Contains("PostCharsWithDelay", input, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_automation_clears_fields_synchronously_before_writing_credentials()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var clear = SourceSection(source, "function Invoke-ClearFocusedField {", "function Invoke-ReliableFieldInput {");

        Assert.Contains("param([long] $Hwnd)", clear, StringComparison.Ordinal);
        Assert.Contains("[string]::new([char]8, 32)", clear, StringComparison.Ordinal);
        Assert.Contains("SendUnicodeChars", clear, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeApi]::Key(0x08)", clear, StringComparison.Ordinal);
        Assert.Contains("Invoke-ClearFocusedField -Hwnd $Hwnd", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Frozen_replay_preserves_the_first_world_blocker_when_the_client_later_retries_login()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "Invoke-FrozenProtocolRegression.ps1"));

        Assert.Contains("[ValidateRange(240, 600)]", source, StringComparison.Ordinal);
        Assert.Contains("[int] $RegressionTimeoutSeconds = 270", source, StringComparison.Ordinal);
        Assert.Contains("if ($serverLoginRejected -and -not $serverLoginAccepted)", source, StringComparison.Ordinal);
        Assert.Contains("firstBlocker = if ($hostResult) { $hostResult.firstBlocker }", source, StringComparison.Ordinal);
        Assert.Contains("secondaryLoginRejection = ($serverLoginAccepted -and $serverLoginRejected)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Official_client_replay_selects_the_authoritative_left_character_slot()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var enterWorld = SourceSection(source, "function Invoke-EnterWorld {", "function Invoke-WorldPacketCaptureMatrix {");

        Assert.Contains("-Name \"AuthoritativeCharacterSlot0\" -RatioX 0.40 -RatioY 0.285", enterWorld, StringComparison.Ordinal);
        Assert.DoesNotContain("-Name \"CharacterSlot0\" -RatioX 0.50", enterWorld, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_WorldTraceSummaryAcceptsMissingOptionalMetadata()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var worldTraceSummary = SourceSection(
            host,
            "function Get-WorldTraceSummary {",
            "function Test-LoginScreenImage {");

        Assert.Contains(
            "if ([string]::IsNullOrWhiteSpace($MetadataPath) -or -not (Test-Path -LiteralPath $MetadataPath))",
            worldTraceSummary,
            StringComparison.Ordinal);
        Assert.Contains("return [pscustomobject]$summary", worldTraceSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialClientFixturePreparationUsesOnlyEvidenceVerifiedWorldValues()
    {
        var analyzer = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.ClientInstrumentation",
            "Analyzer",
            "Program.cs"));

        Assert.Contains("private const int OfficialWireStartMapId = 19;", analyzer, StringComparison.Ordinal);
        Assert.Contains("private const int OfficialWireStartPositionX = 28;", analyzer, StringComparison.Ordinal);
        Assert.Contains("private const int OfficialWireStartPositionY = 34;", analyzer, StringComparison.Ordinal);
        Assert.Contains("name.Length is >= 1 and <= 11", analyzer, StringComparison.Ordinal);
        Assert.Contains("RemediatedForOfficialWire", analyzer, StringComparison.Ordinal);

        var preparation = SourceSection(
            analyzer,
            "private static async Task<int> PrepareFourClassesAsync",
            "private static async Task<long> ReadAccountIdAsync");
        Assert.Contains("BeginTransactionAsync", preparation, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", preparation, StringComparison.Ordinal);
        Assert.Contains("Character preparation invariants were not satisfied", preparation, StringComparison.Ordinal);

        var runtimeVisibleCharacterQuery = SourceSection(
            analyzer,
            "private static async Task<PreparedCharacter?> ReadSingleRuntimeVisibleCharacterAsync",
            "private static async Task<PreparedCharacter?> ReadCharacterAsync");
        Assert.Contains("`Status` <> 'Deleted'", runtimeVisibleCharacterQuery, StringComparison.Ordinal);
        Assert.Contains("LIMIT 2", runtimeVisibleCharacterQuery, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", runtimeVisibleCharacterQuery, StringComparison.Ordinal);
        Assert.Contains("multiple non-deleted characters were found", runtimeVisibleCharacterQuery, StringComparison.Ordinal);

        var remediation = SourceSection(
            analyzer,
            "private static async Task PrepareOfficialWireCharacterAsync",
            "private static async Task<long> InsertCharacterAsync");
        Assert.Contains("`Class` = @class", remediation, StringComparison.Ordinal);
        Assert.Contains("`Status` = 'Active'", remediation, StringComparison.Ordinal);
        Assert.Contains("`DeletedAtUtc` = NULL", remediation, StringComparison.Ordinal);

        var accountUpsert = SourceSection(
            analyzer,
            "private static async Task UpsertAccountAsync",
            "private static async Task<IReadOnlyList<object>> ReadAccountsSchemaAsync");
        Assert.DoesNotContain("`CurrentSessionId` = NULL", accountUpsert, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentSourceCheckpointRecordsDependencyIdentity()
    {
        var generator = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "CurrentSourceCompatibilityCheckpoint",
            "Generate-Checkpoint.ps1"));

        Assert.Contains("dotnetSdkVersion", generator, StringComparison.Ordinal);
        Assert.Contains("projectAssetsCount", generator, StringComparison.Ordinal);
        Assert.Contains("aggregateSha256 = Get-AggregateHash $orderedProjectAssetEntries", generator, StringComparison.Ordinal);
        Assert.Contains("dependencyIdentity = $dependencyIdentity", generator, StringComparison.Ordinal);
        Assert.Contains("$solutionProjectMatches = [regex]::Matches", generator, StringComparison.Ordinal);
        Assert.Contains("outOfSolutionProjectCount", generator, StringComparison.Ordinal);
        Assert.Contains("restore out-of-solution project", generator, StringComparison.Ordinal);
        Assert.Contains("build solution $configuration", generator, StringComparison.Ordinal);
        Assert.Contains("Required current build output is missing", generator, StringComparison.Ordinal);
        Assert.Contains("buildExecution = @($executionEvidence)", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("missingProjectAssets", generator, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_CleansClientAfterFailure()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("LoginFailureCleanup", host, StringComparison.Ordinal);
        Assert.Contains("login-failure-cleanup-result.json", host, StringComparison.Ordinal);
        Assert.Contains("remainingClientCount", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Elevated_world_drag_is_bounded_to_the_selected_client_window()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("WorldDragAndSnapshotOnly", host, StringComparison.Ordinal);
        Assert.Contains("ratios must stay within the target client rectangle", host, StringComparison.Ordinal);
        Assert.Contains("source and destination must both resolve to the target client HWND", host, StringComparison.Ordinal);
        Assert.Contains("[God2Automation.NativeApi]::RealDrag", host, StringComparison.Ordinal);
        Assert.Contains("public static void RealDrag", common, StringComparison.Ordinal);
        Assert.Contains("const int steps = 12", common, StringComparison.Ordinal);
    }

    [Fact]
    public void Elevated_world_right_click_is_bounded_to_the_selected_client_window()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));
        var common = File.ReadAllText(Path.Combine(root, "Automation", "God2Automation.Common.ps1"));

        Assert.Contains("WorldRightClickAndSnapshotOnly", host, StringComparison.Ordinal);
        Assert.Contains("ratios must stay within the target client rectangle", host, StringComparison.Ordinal);
        Assert.Contains("target must resolve to the selected client HWND", host, StringComparison.Ordinal);
        Assert.Contains("[God2Automation.NativeApi]::RealRightClick", host, StringComparison.Ordinal);
        Assert.Contains("public static void RealRightClick", common, StringComparison.Ordinal);
        Assert.Contains("MOUSEEVENTF_RIGHTDOWN", common, StringComparison.Ordinal);
        Assert.Contains("MOUSEEVENTF_RIGHTUP", common, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_ReenumeratesControlsAfterClientRestart()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("Get-OrStartGod2Client", host, StringComparison.Ordinal);
        Assert.Contains("RestartingUnresponsiveClient", host, StringComparison.Ordinal);
        Assert.Contains("RestartingNonLauncherClient", host, StringComparison.Ordinal);
        Assert.Contains("$controls = Get-LoginControlModel", host, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAutomation_ProducesFailureArtifact()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("Artifacts\\ClientLoginAutomation", host, StringComparison.Ordinal);
        Assert.Contains("summary.json", host, StringComparison.Ordinal);
        Assert.Contains("state-machine.json", host, StringComparison.Ordinal);
        Assert.Contains("window-tree-before.json", host, StringComparison.Ordinal);
        Assert.Contains("window-tree-after.json", host, StringComparison.Ordinal);
        Assert.Contains("control-focus-timeline.json", host, StringComparison.Ordinal);
        Assert.Contains("input-attempts.json", host, StringComparison.Ordinal);
        Assert.Contains("submit-attempts.json", host, StringComparison.Ordinal);
        Assert.Contains("process-state.json", host, StringComparison.Ordinal);
        Assert.Contains("network-stage.json", host, StringComparison.Ordinal);
        Assert.Contains("summary.md", host, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomatedObservation_StartsOnlyAfterEnterWorld()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("if ($worldCaptureCommand -and $enterResult.worldReady)", host, StringComparison.Ordinal);
        Assert.Contains("Invoke-WorldPacketCaptureMatrix", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("if ($worldCaptureCommand -and $enterResult.worldReady)", StringComparison.Ordinal) <
            host.IndexOf("$captureResult = Invoke-WorldPacketCaptureMatrix", StringComparison.Ordinal));
    }

    [Fact]
    public void AutomatedObservation_StartsOnlyAfterHeartbeatStable()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("[int]$Trace.heartbeatCount -ge 13", host, StringComparison.Ordinal);
        Assert.Contains("[double]$Trace.heartbeatDurationSeconds -ge 60", host, StringComparison.Ordinal);
        Assert.Contains("if ($worldCaptureCommand -and $enterResult.worldReady)", host, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomatedObservation_FreshNpcModeRunsIdleOnly()
    {
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "Automation", "God2AutomationHost.ps1"));
        var evidence = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "God2.RuntimeNpcEvidence", "Program.cs"));

        Assert.Contains("RunFreshNpcObservation", host, StringComparison.Ordinal);
        Assert.Contains("FreshNpcObservation", host, StringComparison.Ordinal);
        Assert.Contains("fresh-npc-observation", host, StringComparison.Ordinal);
        Assert.Contains("Invoke-Scenario -Scenario \"Idle30s\"", host, StringComparison.Ordinal);
        Assert.Contains("if ($Mode -ne \"FreshNpcObservation\")", host, StringComparison.Ordinal);
        Assert.Contains("freshObservationAccepted", host, StringComparison.Ordinal);
        Assert.Contains("analyzerRawExitCode", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("Invoke-Scenario -Scenario \"Idle30s\"", StringComparison.Ordinal) <
            host.IndexOf("if ($Mode -ne \"FreshNpcObservation\")", StringComparison.Ordinal));
        Assert.Contains("fresh-npc-observation", evidence, StringComparison.Ordinal);
        Assert.Contains("FindObservationMatrixPath", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_world_capture_matrices_do_not_contain_npc_spawn_s2c()
    {
        var matrixRoot = Path.Combine(
            RepositoryRoot(),
            "Artifacts",
            "ClientInstrumentation",
            "ElevatedAutomationHost");
        var matrixFiles = Directory.EnumerateFiles(matrixRoot, "world-packet-matrix.json", SearchOption.AllDirectories)
            .ToArray();

        // Local capture artifacts are optional. When present, validate them; their absence must
        // not turn the headless/offline regression suite into a request for new manual captures.
        if (matrixFiles.Length == 0)
        {
            return;
        }

        var totalPackets = 0;
        var serverToClient = 0;
        var npcOrMerchantCandidates = new List<(string Direction, int Length, string Hex, string Opcode)>();

        foreach (var matrixFile in matrixFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(matrixFile));
            foreach (var scenario in document.RootElement.GetProperty("Scenarios").EnumerateArray())
            {
                foreach (var packet in scenario.GetProperty("Packets").EnumerateArray())
                {
                    totalPackets++;
                    var direction = packet.GetProperty("Direction").GetString() ?? string.Empty;
                    var length = packet.GetProperty("Length").GetInt32();
                    var hex = packet.GetProperty("EncodedHex").GetString() ?? string.Empty;
                    var opcode = packet.GetProperty("OpcodeCandidate").GetString() ?? string.Empty;
                    if (string.Equals(direction, "ServerToClient", StringComparison.Ordinal))
                    {
                        serverToClient++;
                    }

                    if (length == 8 ||
                        hex.Contains("7758", StringComparison.OrdinalIgnoreCase) ||
                        hex.Contains("77B5", StringComparison.OrdinalIgnoreCase) ||
                        opcode.Contains("7758", StringComparison.OrdinalIgnoreCase) ||
                        opcode.Contains("77B5", StringComparison.OrdinalIgnoreCase))
                    {
                        npcOrMerchantCandidates.Add((direction, length, hex, opcode));
                    }
                }
            }
        }

        Assert.True(totalPackets > 0);
        Assert.Equal(0, serverToClient);
        Assert.NotEmpty(npcOrMerchantCandidates);
        Assert.All(npcOrMerchantCandidates, candidate => Assert.Equal("ClientToServer", candidate.Direction));
        Assert.All(npcOrMerchantCandidates, candidate => Assert.Equal(8, candidate.Length));
        Assert.Contains(npcOrMerchantCandidates, candidate => candidate.Opcode == "77B5");
        Assert.DoesNotContain(npcOrMerchantCandidates, candidate => candidate.Opcode == "7758" && candidate.Direction == "ServerToClient");
    }

    [Fact]
    public void Runtime_npc_evidence_index_keeps_serializer_blocked_without_s2c_candidates()
    {
        var indexPath = Path.Combine(RepositoryRoot(), "Artifacts", "RuntimeNpcEvidence", "index.json");

        if (!File.Exists(indexPath))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Equal("SerializerBlockedByEvidence", summary.GetProperty("npcSerializerStatus").GetString());
        Assert.Equal("NOT_FOUND", summary.GetProperty("npcS2cPacket").GetString());
        Assert.Equal(0, summary.GetProperty("npcS2cCandidateCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("serverToClientPackets").GetInt32());
        Assert.Equal(0, summary.GetProperty("fakeNetworkBytes").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("latestObservation").GetString()));
    }

    [Fact]
    public void Runtime_npc_evidence_tool_does_not_overwrite_existing_outputs_without_matrix_or_observation_input()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "God2.RuntimeNpcEvidence", "Program.cs"));

        Assert.Contains("if (matrices.Count == 0 && observation is null)", source, StringComparison.Ordinal);
        Assert.Contains("existing NPC evidence outputs were not overwritten", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (matrices.Count == 0 && observation is null)", StringComparison.Ordinal) <
            source.IndexOf("await WriteIndexAsync(repoRoot, index)", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_npc_evidence_outputs_only_hash_and_required_metadata_for_unknown_packets()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "tools", "God2.RuntimeNpcEvidence", "Program.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("packet.PayloadSha256,\n                        string.Empty,\n                        string.Empty,", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("protocolCandidate.RawSha256,\n                string.Empty,\n                string.Empty,", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("encoded={packet.RawBytesHex}", source, StringComparison.Ordinal);

        var indexPath = Path.Combine(root, "Artifacts", "RuntimeNpcEvidence", "index.json");
        if (!File.Exists(indexPath))
        {
            return;
        }
        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        Assert.All(index.RootElement.GetProperty("candidates").EnumerateArray(), candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.GetProperty("rawSha256").GetString()));
            Assert.Equal(string.Empty, candidate.GetProperty("rawHex").GetString());
            Assert.Equal(string.Empty, candidate.GetProperty("decodedHex").GetString());
        });
    }

    [Fact]
    public void Runtime_npc_observation_records_automated_official_client_capture_without_s2c()
    {
        var root = RepositoryRoot();
        var indexPath = Path.Combine(root, "Artifacts", "RuntimeNpcEvidence", "index.json");
        if (!File.Exists(indexPath))
        {
            return;
        }
        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        var observationRel = index.RootElement.GetProperty("summary").GetProperty("latestObservation").GetString();

        Assert.False(string.IsNullOrWhiteSpace(observationRel));

        var observationRoot = Path.Combine(root, observationRel!.Replace('/', Path.DirectorySeparatorChar));
        var summaryPath = Path.Combine(observationRoot, "summary.md");
        var clientProcessPath = Path.Combine(observationRoot, "client-process.json");
        var packetsPath = Path.Combine(observationRoot, "packets.json");

        Assert.True(File.Exists(summaryPath), "Observation summary missing.");
        Assert.True(File.Exists(clientProcessPath), "Observation client-process snapshot missing.");
        Assert.True(File.Exists(packetsPath), "Observation packet list missing.");

        var summaryText = File.ReadAllText(summaryPath);
        Assert.Contains("Observation status | Captured", summaryText, StringComparison.Ordinal);
        Assert.Contains("Host stage | ", summaryText, StringComparison.Ordinal);
        Assert.Contains("ServerToClient | 0", summaryText, StringComparison.Ordinal);
        Assert.Contains("NPC S2C candidates | 0", summaryText, StringComparison.Ordinal);
        Assert.Contains("NPC serializer remains `SerializerBlockedByEvidence`", summaryText, StringComparison.Ordinal);

        using var clientProcess = JsonDocument.Parse(File.ReadAllText(clientProcessPath));
        var clientRoot = clientProcess.RootElement;
        Assert.StartsWith("Captured", clientRoot.GetProperty("observationStatus").GetString());
        Assert.EndsWith("Complete", clientRoot.GetProperty("hostStage").GetString());
        Assert.True(clientRoot.GetProperty("worldCaptureMatrixPresent").GetBoolean());
        Assert.True(clientRoot.GetProperty("enterWorldResultPresent").GetBoolean());
        Assert.Equal(1, clientRoot.GetProperty("loginAttemptCount").GetInt32());

        using var packets = JsonDocument.Parse(File.ReadAllText(packetsPath));
        Assert.Equal(JsonValueKind.Array, packets.RootElement.ValueKind);
        Assert.True(packets.RootElement.GetArrayLength() > 0);
        Assert.All(packets.RootElement.EnumerateArray(), packet =>
            Assert.Equal("ClientToServer", packet.GetProperty("direction").GetString()));
    }

    [Fact]
    public void Runtime_npc_evidence_tool_does_not_create_golden_bytes_or_success_payload()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "tools", "God2.RuntimeNpcEvidence", "Program.cs"));
        var goldenReport = File.ReadAllText(Path.Combine(root, "Reports", "RuntimeNpcSerializer.GoldenArtifact.md"));

        Assert.Contains("SerializerBlockedByEvidence", program, StringComparison.Ordinal);
        Assert.Contains("NPC S2C Packet: `NOT_FOUND`", program, StringComparison.Ordinal);
        Assert.Contains("BlockedBeforeWorldCapture", program, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllBytes", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Artifacts/Golden", program.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("No NPC golden serializer artifact was created", goldenReport, StringComparison.Ordinal);
        Assert.Contains("SHA-256 | N/A", goldenReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Golden artifact SHA-256: `", goldenReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Character_lifecycle_protocol_evidence_index_keeps_create_delete_blocked_without_raw_frames()
    {
        var root = RepositoryRoot();
        var indexPath = Path.Combine(root, "Artifacts", "CharacterLifecycleEvidence", "protocol-index.json");
        var statusPath = Path.Combine(root, "Artifacts", "CharacterLifecycleEvidence", "status.json");
        var staticAnalysisPath = Path.Combine(root, "Artifacts", "CharacterLifecycleEvidence", "static-analysis.json");

        if (!File.Exists(indexPath) || !File.Exists(statusPath) || !File.Exists(staticAnalysisPath))
        {
            return;
        }

        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        var records = index.RootElement.GetProperty("Records").EnumerateArray().ToArray();
        Assert.Contains(records, record =>
            record.GetProperty("Classification").GetString() == "CreateVisualOnly" &&
            record.GetProperty("OpcodeCandidate").ValueKind == JsonValueKind.Null &&
            record.GetProperty("FrameLength").ValueKind == JsonValueKind.Null);
        Assert.Contains(records, record =>
            record.GetProperty("Classification").GetString() == "VerifiedCatalogExclusion" &&
            record.GetProperty("ExclusionReason").GetString()!.Contains("No verified CharacterCreate or CharacterDelete", StringComparison.Ordinal));

        using var status = JsonDocument.Parse(File.ReadAllText(statusPath));
        Assert.Equal("BlockedByEvidence", status.RootElement.GetProperty("CreateC2S").GetString());
        Assert.Equal("BlockedByEvidence", status.RootElement.GetProperty("DeleteC2S").GetString());
        Assert.Equal("SerializerBlockedByEvidence", status.RootElement.GetProperty("CreateResultS2C").GetString());
        Assert.Equal("SerializerBlockedByEvidence", status.RootElement.GetProperty("DeleteResultS2C").GetString());
        Assert.Equal("BlockedByEvidence", status.RootElement.GetProperty("CharacterListRefresh").GetString());
        Assert.Equal(0, status.RootElement.GetProperty("FakeNetworkBytes").GetInt32());

        using var staticAnalysis = JsonDocument.Parse(File.ReadAllText(staticAnalysisPath));
        Assert.NotEqual("VerifiedProtocolLayout", staticAnalysis.RootElement.GetProperty("Status").GetString());
        Assert.All(staticAnalysis.RootElement.GetProperty("Findings").EnumerateArray(), finding =>
        {
            Assert.Equal("UNKNOWN", finding.GetProperty("ReferencedOpcode").GetString());
            Assert.Equal("UNKNOWN", finding.GetProperty("ExpectedLength").GetString());
        });
    }

    [Fact]
    public void Character_lifecycle_protocol_reports_do_not_create_golden_bytes_or_fake_results()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "tools", "God2.CharacterLifecycleEvidence", "Program.cs"));
        var finalReportWriter = File.ReadAllText(Path.Combine(root, "tools", "God2.CharacterLifecycleFinalReport", "Program.cs"));
        var finalFreeze = File.ReadAllText(Path.Combine(root, "Reports", "CharacterLifecycle.ProtocolFinalFreeze.md"));
        var protocolAutomation = File.ReadAllText(Path.Combine(root, "Reports", "CharacterLifecycle.ProtocolAutomation.md"));
        Assert.DoesNotContain("File.WriteAllBytes", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterLifecycle.TestResults.md", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterLifecycle.FinalFreeze.md", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterLifecycle.ProtocolTestResults.md", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterLifecycle.ProtocolFinalFreeze.md", program, StringComparison.Ordinal);
        Assert.Contains("CharacterLifecycle.ProtocolTestResults.md", finalReportWriter, StringComparison.Ordinal);

        var goldenCreatePath = Path.Combine(root, "Artifacts", "Golden", "CharacterCreate", "README.md");
        var goldenDeletePath = Path.Combine(root, "Artifacts", "Golden", "CharacterDelete", "README.md");
        var goldenListPath = Path.Combine(root, "Artifacts", "Golden", "CharacterList", "README.md");
        if (!File.Exists(goldenCreatePath) || !File.Exists(goldenDeletePath) || !File.Exists(goldenListPath))
        {
            return;
        }
        var goldenCreate = File.ReadAllText(goldenCreatePath);
        var goldenDelete = File.ReadAllText(goldenDeletePath);
        var goldenList = File.ReadAllText(goldenListPath);
        Assert.Contains("CharacterLifecycle.ProtocolFinalFreeze.md", finalReportWriter, StringComparison.Ordinal);
        Assert.Contains("closure-results.json", finalReportWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllBytes", finalReportWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending final verification run", finalReportWriter, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending final verification run", finalFreeze, StringComparison.Ordinal);
        Assert.DoesNotContain("Create Character C2S: VERIFIED", finalFreeze, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete Character C2S: VERIFIED", finalFreeze, StringComparison.Ordinal);
        Assert.Contains("Create Result S2C: SerializerBlockedByEvidence", finalFreeze, StringComparison.Ordinal);
        Assert.Contains("Delete Result S2C: SerializerBlockedByEvidence", finalFreeze, StringComparison.Ordinal);
        Assert.Contains("Fake Network Bytes: 0", finalFreeze, StringComparison.Ordinal);
        Assert.Contains("User Manual Operation:", protocolAutomation, StringComparison.Ordinal);
        Assert.Contains("Status: NOT CREATED", goldenCreate, StringComparison.Ordinal);
        Assert.Contains("Status: NOT CREATED", goldenDelete, StringComparison.Ordinal);
        Assert.Contains("Status: NOT CREATED", goldenList, StringComparison.Ordinal);
    }

    [Fact]
    public void Character_lifecycle_ui_probe_command_does_not_submit_without_verified_locator()
    {
        var root = RepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Automation", "God2AutomationHost.ps1"));

        Assert.Contains("CharacterLifecycleUiProbe", host, StringComparison.Ordinal);
        Assert.Contains("CreateLocatorStatus = \"NOT_FOUND\"", host, StringComparison.Ordinal);
        Assert.Contains("DeleteLocatorStatus = \"NOT_FOUND\"", host, StringComparison.Ordinal);
        Assert.Contains("SubmitAttempted = $false", host, StringComparison.Ordinal);
        Assert.Contains("PacketCaptureAttempted = $false", host, StringComparison.Ordinal);
        Assert.Contains("No create/delete click was sent", host, StringComparison.Ordinal);
        Assert.Contains("Invoke-EnterWorld -Hwnd", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_console_composition_uses_mariadb_repositories_and_real_static_cache()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.ConsoleHost",
            "ConsoleEntry.cs"));

        Assert.Contains("new MariaDbAccountRepository", source, StringComparison.Ordinal);
        Assert.Contains("new MariaDbCharacterRepository", source, StringComparison.Ordinal);
        Assert.Contains("new MariaDbCharacterCreationAuthority", source, StringComparison.Ordinal);
        Assert.Contains("UnifiedRuntimeComposition.CreateProduction", source, StringComparison.Ordinal);
        Assert.Contains("OfficialServerSelectionWireCodec.ClientBuildId", source, StringComparison.Ordinal);
        Assert.Contains("characterCreationAuthority", source, StringComparison.Ordinal);
        Assert.Contains("new MariaDbStaticDataLoader", source, StringComparison.Ordinal);
        Assert.Contains("new MariaDbWorldSessionCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("new LegacyCompatibilityWorldBootstrapProjector", source, StringComparison.Ordinal);
        Assert.Contains("excludeLegacyWorldEntities: true", source, StringComparison.Ordinal);
        Assert.Contains("worldSessionCoordinator: worldSessions", source, StringComparison.Ordinal);
        Assert.Contains("worldBootstrapProjector: worldProjector", source, StringComparison.Ordinal);
        Assert.Contains("npcInteractionClosedLoop: OfficialNpcInteractionClosedLoop.Create(worldSessions, promotedGameplayContent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OfficialNpcInteractionClosedLoop.CreateForTesting", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeReplicationEmitter", File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Runtime",
            "RuntimeFoundation.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("EmptyStaticData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryAccountRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryCharacterRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InMemoryWorldContentRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TestOnlyFixedCharacterCreationAuthority", source, StringComparison.Ordinal);
        var runtimeComposition = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Runtime",
            "RuntimeServices.cs"));
        Assert.DoesNotContain("public static UnifiedRuntimeComposition Create(int maximumSessions)", runtimeComposition, StringComparison.Ordinal);
        Assert.Contains("internal static UnifiedRuntimeComposition CreateForTesting", runtimeComposition, StringComparison.Ordinal);
        Assert.Contains("internal sealed class InMemoryAccountRepository", runtimeComposition, StringComparison.Ordinal);
        Assert.Contains("internal sealed class InMemoryCharacterRepository", runtimeComposition, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_m7_physical_fixture_uses_production_repositories_and_scoped_cleanup()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.AutomationEnvironmentProbe",
            "Program.cs"));

        Assert.Contains("m7-character-lifecycle-physical-fixture", source, StringComparison.Ordinal);
        Assert.Contains("new MariaDbCharacterCreationAuthority", source, StringComparison.Ordinal);
        Assert.Contains("new MariaDbCharacterRepository", source, StringComparison.Ordinal);
        Assert.Contains("concurrentCreateWinners == 1", source, StringComparison.Ordinal);
        Assert.Contains("duplicateNameRolledBack", source, StringComparison.Ordinal);
        Assert.Contains("WHERE `AccountId` = @accountId", source, StringComparison.Ordinal);
        Assert.Contains("existingPlayerRowsTouched = false", source, StringComparison.Ordinal);
        Assert.Contains("connectionStringRetained = false", source, StringComparison.Ordinal);
        Assert.Contains("networkBytesEmitted = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Roadmap_m9_backup_restore_is_isolated_exact_and_secret_safe()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.AutomationEnvironmentProbe",
            "Program.cs"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.AutomationEnvironmentProbe",
            "M9ProductionBackupRestoreProbe.cs"));

        Assert.Contains("m9-production-backup-restore", program, StringComparison.Ordinal);
        Assert.Contains("MariaDbPromotedGameplayContentRuntime", source, StringComparison.Ordinal);
        Assert.Contains("--single-transaction", source, StringComparison.Ordinal);
        Assert.Contains("--quick", source, StringComparison.Ordinal);
        Assert.Contains("MYSQL_PWD", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--password", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WaitForIoAndExitOrKillAsync", source, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAny(completionTask, timeoutTask)", source, StringComparison.Ordinal);
        Assert.Contains("operationCancellation.Cancel()", source, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.Contains("god2_m9_restore_", source, StringComparison.Ordinal);
        Assert.Contains("AcquireRunLock", source, StringComparison.Ordinal);
        Assert.Contains("OwnedRestoreSchema", source, StringComparison.Ordinal);
        Assert.Contains("OwnedTemporaryBackup", source, StringComparison.Ordinal);
        Assert.Contains(".g2enc", source, StringComparison.Ordinal);
        Assert.Contains("CryptoStream", source, StringComparison.Ordinal);
        Assert.Contains("AES-256-CBC-EPHEMERAL-KEY", source, StringComparison.Ordinal);
        Assert.Contains("FileShare.None", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileShare.Read", source, StringComparison.Ordinal);
        Assert.Contains("ReadDatabaseFingerprintAsync", source, StringComparison.Ordinal);
        Assert.Contains("CHECKSUM TABLE", source, StringComparison.Ordinal);
        Assert.Contains("SHOW CREATE TABLE", source, StringComparison.Ordinal);
        Assert.Contains("INFORMATION_SCHEMA.VIEWS", source, StringComparison.Ordinal);
        Assert.Contains("INFORMATION_SCHEMA.ROUTINES", source, StringComparison.Ordinal);
        Assert.Contains("INFORMATION_SCHEMA.EVENTS", source, StringComparison.Ordinal);
        Assert.Contains("INFORMATION_SCHEMA.TRIGGERS", source, StringComparison.Ordinal);
        Assert.Contains("EnsureTransactionalSnapshotEligible", source, StringComparison.Ordinal);
        Assert.Contains("ResolveAndVerifyBuildIdentity", source, StringComparison.Ordinal);
        Assert.Contains("SourceManifestHash", source, StringComparison.Ordinal);
        Assert.Contains("BuildIdentityId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadExactTableCountsAsync", source, StringComparison.Ordinal);
        Assert.Contains("DROP DATABASE IF EXISTS", source, StringComparison.Ordinal);
        Assert.Contains("backupPayloadRetained = false", source, StringComparison.Ordinal);
        Assert.Contains("backupPayloadRetained = File.Exists(backupPath)", source, StringComparison.Ordinal);
        Assert.Contains("restoreSchemaRetained = restoreSchemaCreated", source, StringComparison.Ordinal);
        Assert.Contains("connectionStringRetained = false", source, StringComparison.Ordinal);
        Assert.Contains("gameplayNetworkBytesEmitted = 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_source_mariadb_fixture_tracks_production_composition_and_character_lifecycle_contracts()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.CurrentSourceMariaDbFixture",
            "Program.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.CurrentSourceMariaDbFixture",
            "God2.CurrentSourceMariaDbFixture.csproj"));
        var solution = File.ReadAllText(Path.Combine(root, "God2ClassicServer.sln"));
        var invocation = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "God2.CurrentSourceMariaDbFixture",
            "Invoke-CurrentSourceMariaDbFixture.ps1"));

        Assert.Contains("UnifiedRuntimeComposition.CreateProduction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UnifiedRuntimeComposition.Create(", source, StringComparison.Ordinal);
        Assert.Contains("secondCharacter.CharacterId,", source, StringComparison.Ordinal);
        Assert.Contains("Guid.NewGuid().ToString(\"N\")", source, StringComparison.Ordinal);
        Assert.Contains("19, 28, 34, 10, CancellationToken.None", source, StringComparison.Ordinal);
        Assert.Contains("--build-identity", source, StringComparison.Ordinal);
        Assert.Contains("RevalidationIdentityBuilder.VerifyCurrent", source, StringComparison.Ordinal);
        Assert.Contains("BuildOutputManifestHash", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Artifacts\\CurrentSourceMariaDbFixture", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tools\\God2.CurrentSourceMariaDbFixture\\God2.CurrentSourceMariaDbFixture.csproj", solution, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bin\\$Configuration", invocation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("God2.CurrentSourceMariaDbFixture.dll", invocation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Artifacts\\CurrentSourceMariaDbFixture\\runner\\bin", invocation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roadmap_truth_baseline_records_authority_cutover_and_evidence_blockers()
    {
        var roadmap = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "RoadMap.md"));

        Assert.Contains("MariaDbWorldContentRepository", roadmap, StringComparison.Ordinal);
        Assert.Contains("client_map_identities", roadmap, StringComparison.Ordinal);
        Assert.Contains("LegacyCompatibilityBootstrap", roadmap, StringComparison.Ordinal);
        Assert.Contains("SerializerBlockedByEvidence", roadmap, StringComparison.Ordinal);
        Assert.Contains("M1 — 玩家與世界 Authority Cutover | `COMPLETE`", roadmap, StringComparison.Ordinal);
        Assert.Contains("M2 — Map 19↔3 真實內容切片 | `BLOCKED`", roadmap, StringComparison.Ordinal);
        Assert.Contains("M3 — 真實 NPC／怪物 Replication | `IN PROGRESS`", roadmap, StringComparison.Ordinal);
        Assert.Contains("M7 — 角色生命週期與 Gameplay Families | `IN PROGRESS`", roadmap, StringComparison.Ordinal);
        Assert.Contains("MariaDbCharacterCreationAuthority", roadmap, StringComparison.Ordinal);
        Assert.Contains("First broken node：`Official Character Create request/result semantic closure`", roadmap, StringComparison.Ordinal);
        Assert.Contains("runtime NPC=2、serializer Ready=2", roadmap, StringComparison.Ordinal);
        Assert.Contains("其餘 317 個 formal NPC", roadmap, StringComparison.Ordinal);
        Assert.Contains("durable transactional outbox", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("M3 — 真實 NPC／怪物 Replication | `COMPLETE`", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_missing_file_fails_startup_load()
    {
        using var temp = TempServerBase(copyConfig: false);
        var loader = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path));

        var result = loader.Load();

        Assert.False(result.Succeeded);
        Assert.Equal("configuration.missing", result.Error.Code);
    }

    [Fact]
    public void Configuration_invalid_json_fails_startup_load()
    {
        using var temp = TempServerBase(copyConfig: true);
        File.WriteAllText(Path.Combine(temp.Path, "config", "server.json"), "{ invalid");
        var loader = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path));

        var result = loader.Load();

        Assert.False(result.Succeeded);
        Assert.Equal("configuration.invalid_json", result.Error.Code);
    }

    [Fact]
    public void Environment_variables_do_not_override_json_configuration_values()
    {
        using var temp = TempServerBase(copyConfig: true);
        var baseline = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path)).Load();
        Environment.SetEnvironmentVariable("GOD2_DATABASE_HOST", "db.example.internal");
        Environment.SetEnvironmentVariable("GOD2_NETWORK_LOGIN_PORT", "6200");
        Environment.SetEnvironmentVariable("GOD2_NETWORK_ADVERTISED_IP", "203.0.113.10");

        try
        {
            var result = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path)).Load();

            Assert.True(result.Succeeded);
            Assert.True(baseline.Succeeded);
            Assert.Equal(baseline.Value!.Database.Host, result.Value!.Database.Host);
            Assert.Equal(baseline.Value.Network.LoginPort, result.Value.Network.LoginPort);
            Assert.Equal(baseline.Value.Network.AdvertisedIp, result.Value.Network.AdvertisedIp);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOD2_DATABASE_HOST", null);
            Environment.SetEnvironmentVariable("GOD2_NETWORK_LOGIN_PORT", null);
            Environment.SetEnvironmentVariable("GOD2_NETWORK_ADVERTISED_IP", null);
        }
    }

    [Fact]
    public void Database_password_environment_source_is_resolved_without_writing_secret_to_configuration()
    {
        using var temp = TempServerBase(copyConfig: true);
        var databasePath = Path.Combine(temp.Path, "config", "database.json");
        var variableName = $"GOD2_TEST_DATABASE_PASSWORD_{Guid.NewGuid():N}";
        const string expectedPassword = "resolved-test-password";
        var database = JsonNode.Parse(File.ReadAllText(databasePath))!.AsObject();
        database["password"] = string.Empty;
        database["passwordSource"] = "EnvironmentVariable";
        database["passwordEnvironmentVariable"] = variableName;
        File.WriteAllText(databasePath, database.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Environment.SetEnvironmentVariable(variableName, expectedPassword);

        try
        {
            var result = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path)).Load();

            Assert.True(result.Succeeded);
            Assert.True(
                string.Equals(result.Value!.Database.Password, expectedPassword, StringComparison.Ordinal),
                "Resolved database password did not match the isolated fixture value.");
            Assert.DoesNotContain(expectedPassword, File.ReadAllText(databasePath), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Missing_database_password_environment_value_fails_configuration_validation()
    {
        using var temp = TempServerBase(copyConfig: true);
        var databasePath = Path.Combine(temp.Path, "config", "database.json");
        var variableName = $"GOD2_TEST_MISSING_DATABASE_PASSWORD_{Guid.NewGuid():N}";
        var database = JsonNode.Parse(File.ReadAllText(databasePath))!.AsObject();
        database["password"] = string.Empty;
        database["passwordSource"] = "EnvironmentVariable";
        database["passwordEnvironmentVariable"] = variableName;
        File.WriteAllText(databasePath, database.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Environment.SetEnvironmentVariable(variableName, null);

        var result = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path)).Load();

        Assert.True(result.Succeeded);
        Assert.Contains(result.Value!.Validate(), error =>
            error.Code == "configuration.required" && error.Source == "database.json:password");
    }

    [Fact]
    public void ConfigValue_database_password_is_loaded_from_configuration()
    {
        using var temp = TempServerBase(copyConfig: true);
        var databasePath = Path.Combine(temp.Path, "config", "database.json");
        var database = JsonNode.Parse(File.ReadAllText(databasePath))!.AsObject();
        database["password"] = "fixture-password-value";
        database["passwordSource"] = "ConfigValue";
        File.WriteAllText(databasePath, database.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var result = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path)).Load();

        Assert.True(result.Succeeded);
        Assert.Equal("fixture-password-value", result.Value!.Database.Password);
        Assert.DoesNotContain(result.Value.Validate(), error =>
            error.Source == "database.json:password" || error.Source == "database.json:passwordSource");
    }

    [Fact]
    public void Legacy_network_config_without_world_port_still_loads()
    {
        using var temp = TempServerBase(copyConfig: true);
        File.WriteAllText(Path.Combine(temp.Path, "config", "network.json"), """
            {
                "_comment": "網路 Listener 設定",
                "_version": "1.1",
                "_example": {
                    "bindIp": "127.0.0.1",
                    "loginPort": 2592
                },
                "bindIp": "127.0.0.1",
                "_bindIp": "用途：Server 綁定 IP。預設值：127.0.0.1。可接受範圍：本機可綁定的 IPv4 位址。是否需要重新啟動：是。影響：決定 Client 可從哪個網路介面連入 Server。",
                "loginPort": 2592,
                "_loginPort": "用途：統一 Game Endpoint Port。預設值：2592。可接受範圍：1 到 65535。是否需要重新啟動：是。影響：決定唯一 TCP Listener 的連入 Port。"
            }
            """);

        var result = new JsonServerConfigurationLoader(new AppPathProvider(temp.Path)).Load();

        Assert.True(result.Succeeded);
        Assert.Equal(2592, result.Value!.Network.LoginPort);
        Assert.Equal(0, result.Value.Network.WorldPort);
    }

    [Fact]
    public async Task Ready_console_output_shows_single_unified_game_endpoint()
    {
        using var output = new StringWriter();
        var method = typeof(ConsoleEntry).GetMethod("WriteReady", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var runtime = UnifiedRuntimeComposition.CreateForTesting(
            maximumSessions: 10,
            new InMemoryAccountRepository(),
            new InMemoryCharacterRepository());
        var networkHost = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            publicBetaCompatibilityProfile: PublicBetaCompatibilityProfile.Production);
        var port = GetFreeTcpPort();
        var network = new God2.ClassicServer.Application.Configuration.NetworkOptions("127.0.0.1", port, 2596);
        Assert.True((await networkHost.StartAsync(network, CancellationToken.None)).Succeeded);

        method!.Invoke(
            null,
            [
                output,
                network,
                runtime,
                networkHost,
                DateTimeOffset.UtcNow
            ]);
        await networkHost.StopAsync(CancellationToken.None);

        var text = output.ToString();
        Assert.Contains($"遊戲連線位置：127.0.0.1:{port}", text);
        Assert.Contains("登入／世界模式：統一連線", text);
        Assert.Contains("網路監聽數量：1", text);
        var compatibility = PublicBetaCompatibilityProfile.Production.Summary;
        Assert.Contains($"{compatibility.CatalogAppliedCount}/{compatibility.CatalogTotalCount}", text);
        Assert.Contains($"{compatibility.CurrentWireVerifiedCount}/{compatibility.CatalogTotalCount}", text);
        Assert.Contains($"{compatibility.CurrentWireImplementedCount}/{compatibility.CatalogTotalCount}", text);
        Assert.Contains(compatibility.CurrentWireAdapterRequiredCount.ToString(), text);
        Assert.DoesNotContain("Login Endpoint:", text);
        Assert.DoesNotContain("World Endpoint:", text);
    }

    [Fact]
    public void Startup_console_summarizes_current_migrations_without_hiding_other_details()
    {
        using var output = new StringWriter();
        var method = typeof(ConsoleEntry).GetMethod(
            "WriteStage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var startupEvent = new StartupEvent(
            4,
            "Schema and Migration",
            OperationResult.Success,
            ["Migration 001 Current", "Migration 002 Current", "Database Ready"]);

        method!.Invoke(null, [output, startupEvent]);

        var text = output.ToString();
        Assert.Contains("資料庫遷移：2/2 版本一致", text, StringComparison.Ordinal);
        Assert.Contains("資料庫已就緒", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Migration 001 Current", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Migration 002 Current", text, StringComparison.Ordinal);
        Assert.Contains("[4/10] 資料庫結構與遷移：成功", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_configuration_field_has_traditional_chinese_explanation()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "config"), "*.json", SearchOption.TopDirectoryOnly)
                     .Where(file => !Path.GetFileName(file).EndsWith(".local.json", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{Path.GetFileName(file)} must not contain a UTF-8 BOM.");

            var text = File.ReadAllText(file);
            Assert.DoesNotContain("\t", text);
            foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.TrimStart().StartsWith('"')))
            {
                var leadingSpaces = line.Length - line.TrimStart(' ').Length;
                Assert.True(leadingSpaces % 4 == 0, $"{Path.GetFileName(file)} must use 4-space indentation: {line}");
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.True(root.TryGetProperty("_comment", out var comment), $"{Path.GetFileName(file)} is missing _comment.");
            Assert.False(string.IsNullOrWhiteSpace(comment.GetString()), $"{Path.GetFileName(file)} has an empty _comment.");
            Assert.True(root.TryGetProperty("_version", out var version), $"{Path.GetFileName(file)} is missing _version.");
            Assert.False(string.IsNullOrWhiteSpace(version.GetString()), $"{Path.GetFileName(file)} has an empty _version.");
            Assert.True(root.TryGetProperty("_example", out var example), $"{Path.GetFileName(file)} is missing _example.");
            Assert.Equal(JsonValueKind.Object, example.ValueKind);

            foreach (var property in root.EnumerateObject().Where(property => !property.Name.StartsWith('_')))
            {
                var explanationName = $"_{property.Name}";
                Assert.True(root.TryGetProperty(explanationName, out var explanation), $"{Path.GetFileName(file)} is missing {explanationName}.");
                var explanationText = explanation.GetString();
                Assert.False(string.IsNullOrWhiteSpace(explanationText), $"{Path.GetFileName(file)} has an empty {explanationName}.");
                Assert.Contains("\u7528\u9014", explanationText);
                Assert.Contains("\u9810\u8a2d\u503c", explanationText);
                Assert.Contains("\u53ef\u63a5\u53d7\u7bc4\u570d", explanationText);
                Assert.Contains("\u662f\u5426\u9700\u8981\u91cd\u65b0\u555f\u52d5", explanationText);
                Assert.Contains("\u5f71\u97ff", explanationText);
            }
        }
    }

    [Fact]
    public void Configuration_contains_only_core_policy_operational_files()
    {
        var configFiles = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "config"), "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).EndsWith(".local.json", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "database.json",
            "localization.json",
            "logging.json",
            "network.json",
            "persistence.json",
            "rates.json",
            "security.json",
            "server.json"
        ], configFiles);

        var forbidden = new[]
        {
            "moneyRate",
            "goldRate",
            "currencyRate",
            "goldDropRate",
            "moneyDropRate",
            "enableMoneyDrop",
            "enableGoldDrop",
            "enableCurrency",
            "enableQuest",
            "enableMonster",
            "enableNPC",
            "enableMerchant",
            "enableItem",
            "enableBattlePet",
            "enableImmortal",
            "questReward",
            "accountAutoCreate",
            "playerKillEnabled",
            "enabled"
        };

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "config"), "*.json", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var field in forbidden)
            {
                Assert.DoesNotContain($"\"{field}\"", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Permanent_directory_policy_allows_only_declared_data_folders()
    {
        var dbAllowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "imports",
            "official",
            "converted",
            "legacy",
            "exports",
            "backups",
            "snapshots"
        };
        var databaseAllowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema",
            "migrations",
            "seeds"
        };

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot(), "db"), "*", SearchOption.TopDirectoryOnly))
        {
            Assert.Contains(Path.GetFileName(directory), dbAllowed);
        }

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot(), "database"), "*", SearchOption.TopDirectoryOnly))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Assert.Contains(Path.GetFileName(directory), databaseAllowed);
            }
        }
    }

    [Fact]
    public void Source_does_not_depend_on_windows_code_page_encoding()
    {
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Encoding.Default", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GetEncoding(0", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CodePagesEncodingProvider", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Database_schema_migrations_are_contiguous_and_ordered()
    {
        var migrations = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "database", "schema"), "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(migrations);
        var versions = migrations.Select(file => int.Parse(file[..3], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(Enumerable.Range(1, migrations.Length), versions);
        Assert.All(migrations, file => Assert.Matches(@"^\d{3}_[a-z0-9_]+\.sql$", file));
    }

    [Theory]
    [InlineData("skill_executions")]
    [InlineData("skill_effect_executions")]
    [InlineData("skill_cost_reservations")]
    [InlineData("battle_skill_usage")]
    [InlineData("skill_idempotency")]
    [InlineData("skill_audit")]
    public void Automation_preflight_requires_skill_runtime_schema(string table)
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "tools",
            "God2.AutomationEnvironmentProbe",
            "Program.cs"));

        Assert.Contains($"\"{table}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_and_packet_handler_code_does_not_direct_sql_gameplay_mutations()
    {
        var root = Path.Combine(RepositoryRoot(), "src");
        var candidateFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}God2.ClassicServer.Protocol{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(path).Contains("Handler", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in candidateFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("MySqlConnection", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CommandText", text, StringComparison.Ordinal);
            Assert.DoesNotContain("INSERT INTO `inventory", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `inventory", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `inventory", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO `merchant", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `merchant", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `merchant", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `characters`", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO `world_interaction", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `world_interaction", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `world_interaction", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO `combat_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `combat_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `combat_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO `monster_combat", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `monster_combat", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `monster_combat", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO `battle_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `battle_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `battle_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO `skill_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE `skill_", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM `skill_", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Official_client_recovery_inventory_covers_permanent_scope_without_premature_missing()
    {
        var expectedCategories = new[]
        {
            "animations",
            "battle_pets",
            "containers",
            "dialogs",
            "drop_tables",
            "effects",
            "equipment",
            "icons",
            "immortals",
            "items",
            "localization",
            "lua",
            "maps",
            "merchants",
            "models",
            "monsters",
            "music",
            "npcs",
            "portals",
            "quests",
            "resource_tables",
            "rewards",
            "scripts",
            "skills",
            "sounds",
            "spawns",
            "string_tables",
            "textures"
        };

        var importRoot = Path.Combine(RepositoryRoot(), "db", "imports", "official");
        var importerCategories = OfficialImporters.All
            .Select(importer => importer.Category)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedCategories, importerCategories);

        var categoryFiles = Directory.EnumerateFiles(importRoot, "*.official.json", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedCategories, categoryFiles);

        foreach (var category in expectedCategories)
        {
            var file = Path.Combine(importRoot, category, $"{category}.official.json");
            var text = File.ReadAllText(file);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            Assert.True(root.TryGetProperty("source", out var source), $"{category} is missing source.");
            Assert.False(string.IsNullOrWhiteSpace(source.GetString()), $"{category} source is empty.");
            Assert.True(root.TryGetProperty("format", out var format), $"{category} is missing format.");
            Assert.False(string.IsNullOrWhiteSpace(format.GetString()), $"{category} format is empty.");
            Assert.True(root.TryGetProperty("recordCount", out var recordCount), $"{category} is missing recordCount.");
            Assert.True(recordCount.TryGetInt32(out _), $"{category} recordCount must be an integer.");
            Assert.True(root.TryGetProperty("recoveredFields", out var recoveredFields), $"{category} is missing recoveredFields.");
            Assert.Equal(JsonValueKind.Array, recoveredFields.ValueKind);
            Assert.True(root.TryGetProperty("missingFields", out var missingFields), $"{category} is missing missingFields.");
            Assert.Equal(JsonValueKind.Array, missingFields.ValueKind);
            Assert.True(root.TryGetProperty("verificationStatus", out var verificationStatus), $"{category} is missing verificationStatus.");
            var status = verificationStatus.GetString() ?? string.Empty;
            Assert.DoesNotContain("Missing;", status, StringComparison.OrdinalIgnoreCase);
            Assert.True(root.TryGetProperty("recoveryProgress", out var recoveryProgress), $"{category} is missing recoveryProgress.");
            Assert.False(string.IsNullOrWhiteSpace(recoveryProgress.GetString()), $"{category} recoveryProgress is empty.");
        }
    }

    [Fact]
    public void Exact_client_immortal_and_battle_pet_catalogs_import_as_non_runtime_verified_evidence()
    {
        var importRoot = Path.Combine(RepositoryRoot(), "db", "imports", "official");

        var immortalResult = new ImmortalImporter().Load(importRoot);
        Assert.True(immortalResult.Succeeded, immortalResult.Error.Message);
        var immortals = Assert.IsType<OfficialImportCategory>(immortalResult.Value);
        Assert.Equal(34, immortals.RecordCount);
        Assert.Equal("Verified", immortals.RecoveryStatus);
        Assert.False(immortals.CanDirectImportToGameplay);
        Assert.All(immortals.Records, record =>
        {
            Assert.Equal("c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", record.SourceHash);
            Assert.False(record.CanDirectImportToGameplay);
        });

        var petResult = new BattlePetImporter().Load(importRoot);
        Assert.True(petResult.Succeeded, petResult.Error.Message);
        var pets = Assert.IsType<OfficialImportCategory>(petResult.Value);
        Assert.Equal(178, pets.RecordCount);
        Assert.Equal("Verified", pets.RecoveryStatus);
        Assert.False(pets.CanDirectImportToGameplay);
        Assert.All(pets.Records, record =>
        {
            Assert.Equal("c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", record.SourceHash);
            Assert.False(record.CanDirectImportToGameplay);
        });
    }

    [Fact]
    public void Protocol_knowledge_migration_keeps_evidence_without_unified_runtime_dependencies()
    {
        var protocolRoot = Path.Combine(RepositoryRoot(), "src", "God2.ClassicServer.Protocol");
        var evidenceRoot = Path.Combine(protocolRoot, "Evidence", "OfficialProtocolCompletion");
        var knowledgeFile = Path.Combine(protocolRoot, "Knowledge", "protocol-knowledge-base.json");

        var requiredEvidence = new[]
        {
            "VerifiedPacketCatalog.json",
            "UnknownPacketCatalog.json",
            "PacketFieldMap.json",
            "PacketSequenceMap.json",
            "PacketCoverageMatrix.csv",
            "OfficialClientE2ESummary.json",
            "OfficialClientMilestones.jsonl",
            "CapturedPacketEvidence.20260806.json",
            "CapturedPacketEvidence.20260806.Batch2.json",
            "God2Evidence.20260806.102928.json",
            "ProtocolCompletionSummary.md"
        };

        foreach (var file in requiredEvidence)
        {
            Assert.True(File.Exists(Path.Combine(evidenceRoot, file)), $"{file} protocol evidence is missing.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(knowledgeFile));
        var root = document.RootElement;
        Assert.Contains("Knowledge and evidence only", root.GetProperty("sourcePolicy").GetString());
        Assert.NotEmpty(root.GetProperty("verifiedOpcodes").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("unknownOpcodes").EnumerateArray());

        var projectText = File.ReadAllText(Path.Combine(protocolRoot, "God2.ClassicServer.Protocol.csproj"));
        Assert.DoesNotContain("God2 Classic Unified Server", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LoginServer", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorldServer", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("net8.0", projectText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase33_mount_state_recovery_keeps_unverified_mount_semantics_gated()
    {
        var evidenceFile = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion",
            "WorldProtocolRecovery.Phase33.MountStateParserGuided.json");

        Assert.True(File.Exists(evidenceFile), "Phase 3.3 mount-state recovery evidence is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(evidenceFile));
        var root = document.RootElement;
        Assert.Equal("world-protocol-recovery-phase33-v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("UNCHANGED", root.GetProperty("goldenPathFreeze").GetString());

        var semanticCorrections = root.GetProperty("semanticCorrections");
        Assert.Equal("PASS", semanticCorrections.GetProperty("status").GetString());
        Assert.Contains("no independent MountedFlag semantic", semanticCorrections.GetProperty("correction").GetString());

        var fiveByte = root.GetProperty("worldActionStateC2S5Candidate");
        Assert.Equal("050011C79E", fiveByte.GetProperty("packet").GetString());
        Assert.Contains("shared world tick/action-state candidate", fiveByte.GetProperty("classification").GetString());
        Assert.Equal("Unmount", fiveByte.GetProperty("mustNotRenameTo").GetString());

        var walking = root.GetProperty("walkingRecovery");
        Assert.Equal("NOT_FIXED", walking.GetProperty("coordinateScale").GetString());
        Assert.Equal("BLOCKED_UNTIL_WALKING_EVIDENCE", walking.GetProperty("movementSpeedModel").GetString());
    }

    [Fact]
    public void Phase34_post_decode_dispatch_map_keeps_write_target_unverified_until_component_is_bounded()
    {
        var evidenceFile = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion",
            "WorldSemanticDispatchMap.json");

        Assert.True(File.Exists(evidenceFile), "Phase 3.4 world semantic dispatch map is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(evidenceFile));
        var root = document.RootElement;
        Assert.Equal("world-semantic-dispatch-map-v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(12, root.GetProperty("frameCount").GetInt32());

        var priority = root.GetProperty("priorityFrameHandlerRvaCandidates");
        foreach (var frame in new[] { "5", "7", "8", "9", "10" })
        {
            Assert.Equal("god2_opt.exe+0x0008E4F1", priority.GetProperty(frame).GetString());
        }

        Assert.Equal(
            "NOT_CAPTURED",
            root.GetProperty("writeTargetInstrumentation").GetProperty("status").GetString());
        Assert.Equal(
            "NOT_LOCALIZED",
            root.GetProperty("localPlayerObject").GetProperty("status").GetString());
        Assert.Equal(
            "NOT_FIXED",
            root.GetProperty("walking").GetProperty("coordinateScale").GetString());
    }

    [Fact]
    public void Phase35_local_player_entity_requires_non_stack_persistent_anchor()
    {
        var evidenceRoot = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion");
        var layoutFile = Path.Combine(evidenceRoot, "LocalPlayerEntityLayout.json");
        var componentFile = Path.Combine(evidenceRoot, "LocalPlayerComponentMap.json");
        var phaseFile = Path.Combine(evidenceRoot, "WorldProtocolRecovery.Phase35.LocalPlayerEntityLocalization.json");

        Assert.True(File.Exists(layoutFile), "Phase 3.5 local-player entity layout evidence is missing.");
        Assert.True(File.Exists(componentFile), "Phase 3.5 local-player component map evidence is missing.");
        Assert.True(File.Exists(phaseFile), "Phase 3.5 recovery summary evidence is missing.");

        using var layoutDocument = JsonDocument.Parse(File.ReadAllText(layoutFile));
        var layout = layoutDocument.RootElement;
        Assert.Equal("local-player-entity-layout-v1", layout.GetProperty("schemaVersion").GetString());
        Assert.Equal("NOT_VERIFIED", layout.GetProperty("status").GetString());
        Assert.Equal("NO_NON_STACK_HIT", layout.GetProperty("anchorLocalization").GetProperty("characterIdBased").GetProperty("status").GetString());

        var coordinateCorrelation = layout.GetProperty("anchorLocalization").GetProperty("coordinateCorrelation");
        Assert.Equal("NO_STABLE_RAW_XY_FIELD", coordinateCorrelation.GetProperty("status").GetString());
        Assert.True(coordinateCorrelation.GetProperty("firstAllCoordinateCandidatesEliminatedAtMovementIndex").GetInt32() > 0);
        Assert.Equal("NOT_CAPTURED", layout.GetProperty("movementWriter").GetProperty("status").GetString());
        Assert.Contains(
            "stack",
            layout.GetProperty("rejectedFalsePositive").GetProperty("reason").GetString(),
            StringComparison.OrdinalIgnoreCase);

        using var componentDocument = JsonDocument.Parse(File.ReadAllText(componentFile));
        var components = componentDocument.RootElement;
        Assert.Equal("local-player-component-map-v1", components.GetProperty("schemaVersion").GetString());
        Assert.Equal("BLOCKED_LOCAL_PLAYER_ENTITY_NOT_VERIFIED", components.GetProperty("status").GetString());
        Assert.Equal("NOT_FOUND", components.GetProperty("appearanceComponent").GetString());
        Assert.Equal("NOT_FOUND", components.GetProperty("equipmentComponent").GetString());
        Assert.Equal("NOT_FOUND", components.GetProperty("modelRenderComponent").GetString());

        using var phaseDocument = JsonDocument.Parse(File.ReadAllText(phaseFile));
        var phase = phaseDocument.RootElement;
        Assert.Equal("world-protocol-recovery-phase35-v1", phase.GetProperty("schemaVersion").GetString());
        Assert.Equal("PASS", phase.GetProperty("answers").GetProperty("goldenRegression").GetString());
        Assert.Contains(
            "non-stack persistent local-player anchor",
            phase.GetProperty("answers").GetProperty("nextFirstBlocker").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("NO", phase.GetProperty("scanSafety").GetProperty("credentialsPersisted").GetString());
    }

    [Fact]
    public void Phase36_world_protocol_priority_reset_defers_walking_and_catalogs_bootstrap()
    {
        var evidenceRoot = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion");
        var phaseFile = Path.Combine(evidenceRoot, "WorldProtocolRecovery.Phase36.PriorityReset.json");
        var bootstrapFile = Path.Combine(evidenceRoot, "WorldBootstrapSequence.json");

        Assert.True(File.Exists(phaseFile), "Phase 3.6 priority reset evidence is missing.");
        Assert.True(File.Exists(bootstrapFile), "World bootstrap sequence catalog is missing.");

        using var phaseDocument = JsonDocument.Parse(File.ReadAllText(phaseFile));
        var phase = phaseDocument.RootElement;
        Assert.Equal("world-protocol-recovery-phase36-priority-reset-v1", phase.GetProperty("schemaVersion").GetString());
        Assert.Contains(
            phase.GetProperty("deferredUntilOfficialWalkingEvidence").EnumerateArray(),
            item => item.GetString() == "Walking Recovery");
        Assert.Contains(
            phase.GetProperty("deferredUntilOfficialWalkingEvidence").EnumerateArray(),
            item => item.GetString() == "MovementSpeedModel");
        Assert.Equal(
            "COMPLETE_12_OF_12_FRAMES_CATALOGED",
            phase.GetProperty("bootstrap").GetProperty("status").GetString());
        Assert.Equal(
            "STRUCTURAL_BUILDER_COMPLETE_FIELD_SEMANTICS_PARTIAL",
            phase.GetProperty("playerSpawn").GetProperty("status").GetString());
        Assert.Equal(
            "COMPLETE_KEEPALIVE_CLASSIFICATION",
            phase.GetProperty("heartbeat").GetProperty("status").GetString());
        Assert.Equal(
            "FIRST_BATCH_CLASSIFIED_NO_LONGER_GENERIC_UNKNOWN",
            phase.GetProperty("worldPacketCatalog").GetProperty("status").GetString());
        Assert.Equal(
            "ENTRYPOINT_IDENTIFIED_NOT_STARTED",
            phase.GetProperty("npcSpawnNext").GetProperty("status").GetString());

        using var bootstrapDocument = JsonDocument.Parse(File.ReadAllText(bootstrapFile));
        var bootstrap = bootstrapDocument.RootElement;
        Assert.Equal("world-bootstrap-sequence-v2", bootstrap.GetProperty("schemaVersion").GetString());
        Assert.Equal("FORMAL_SEQUENCE_SPLIT_COMPLETE", bootstrap.GetProperty("status").GetString());
        var frames = bootstrap.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(12, frames.Length);
        Assert.Equal(1778, frames.Sum(frame => frame.GetProperty("length").GetInt32()));
        Assert.All(frames, frame =>
        {
            Assert.False(string.IsNullOrWhiteSpace(frame.GetProperty("frameName").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(frame.GetProperty("decodedOpcode").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(frame.GetProperty("builder").GetString()));
            Assert.DoesNotContain("Unknown", frame.GetProperty("frameName").GetString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Phase4_npc_world_recovery_keeps_spawn_blocked_until_s2c_raw_packet_exists()
    {
        var evidenceFile = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion",
            "WorldProtocolRecovery.Phase4.NpcWorldRecovery.json");

        Assert.True(File.Exists(evidenceFile), "Phase 4 NPC recovery evidence is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(evidenceFile));
        var root = document.RootElement;
        Assert.Equal("world-protocol-recovery-phase4-npc-v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("Deferred", root.GetProperty("frozenBaseline").GetProperty("walkingMountCoordinateScale").GetString());

        var spawnTimeline = root.GetProperty("spawnTimeline");
        Assert.Equal("NOT_FOUND", spawnTimeline.GetProperty("firstNpcSpawnS2C").GetString());
        Assert.Equal(0, spawnTimeline.GetProperty("packetSummary").GetProperty("serverToClient").GetInt32());

        var interaction = root.GetProperty("npcInteractionMatrix");
        Assert.Equal("SEED_ONLY_NO_DISTANCE_MATRIX_CAPTURED", interaction.GetProperty("status").GetString());
        Assert.Equal("0800775884CB3F09", interaction.GetProperty("knownCandidate").GetProperty("encodedHex").GetString());
        Assert.Equal("080077B5F3D73FB8", interaction.GetProperty("currentLocalMatrixFinding").GetProperty("single8BytePacketInCurrentWorldMatrix").GetString());

        Assert.Equal("BLOCKED_NO_S2C_RAW_PACKET", root.GetProperty("npcSpawn").GetProperty("status").GetString());
        Assert.Equal("BLOCKED_NO_S2C_RAW_PACKET", root.GetProperty("npcUpdate").GetProperty("status").GetString());
        Assert.Equal("BLOCKED_NO_S2C_RAW_PACKET", root.GetProperty("npcDespawn").GetProperty("status").GetString());

        var entityCatalog = root.GetProperty("entityCatalog");
        Assert.Equal("STARTED", entityCatalog.GetProperty("status").GetString());
        Assert.Contains(
            entityCatalog.GetProperty("entries").EnumerateArray(),
                entry => entry.GetProperty("entityType").GetString() == "NPC" &&
                entry.GetProperty("recoveryStatus").GetString() == "NeedsRecovery");
    }

    [Fact]
    public void Phase41_world_content_recovery_blocks_npc_packet_guessing_until_entities_exist()
    {
        var evidenceFile = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion",
            "WorldContentRecovery.Phase41.MapRuntime.json");

        Assert.True(File.Exists(evidenceFile), "Phase 4.1 world content recovery evidence is missing.");

        using var document = JsonDocument.Parse(File.ReadAllText(evidenceFile));
        var root = document.RootElement;
        Assert.Equal("world-content-recovery-phase41-v1", root.GetProperty("schemaVersion").GetString());

        var currentMap = root.GetProperty("currentRuntimeMap");
        Assert.Equal("FROZEN_BOOTSTRAP_ONLY", currentMap.GetProperty("status").GetString());
        Assert.Equal("UNRESOLVED_FROM_DB", currentMap.GetProperty("mapId").GetString());

        var entities = root.GetProperty("runtimeEntityManager");
        Assert.Equal("ABSENT_FOR_WORLD_CONTENT", entities.GetProperty("status").GetString());
        Assert.Equal(1, entities.GetProperty("players").GetInt32());
        Assert.Equal(0, entities.GetProperty("npcs").GetInt32());
        Assert.Equal(0, entities.GetProperty("monsters").GetInt32());
        Assert.Equal(0, entities.GetProperty("portals").GetInt32());
        Assert.Equal(0, entities.GetProperty("merchants").GetInt32());

        var pipeline = root.GetProperty("spawnPipeline");
        Assert.Equal("FROZEN_BOOTSTRAP_ONLY", pipeline.GetProperty("mapLoad").GetString());
        Assert.False(pipeline.GetProperty("spawnQueue").GetProperty("exists").GetBoolean());
        Assert.Equal(0, pipeline.GetProperty("spawnQueue").GetProperty("entityCount").GetInt32());
        Assert.False(pipeline.GetProperty("broadcast").GetProperty("sent").GetBoolean());
        Assert.Equal(0, pipeline.GetProperty("broadcast").GetProperty("serverToClientAfterWorldReady").GetInt32());

        var database = root.GetProperty("database");
        Assert.Equal(319, database.GetProperty("npcs").GetProperty("total").GetInt32());
        Assert.Equal(0, database.GetProperty("npcs").GetProperty("mappedToAnyMap").GetInt32());
        Assert.Equal(0, database.GetProperty("monsters").GetProperty("spawns").GetInt32());
        Assert.Equal(16, database.GetProperty("merchants").GetProperty("unmapped").GetInt32());

        Assert.Equal("STOPPED", root.GetProperty("decision").GetProperty("npcPacketRecovery").GetString());
    }

    [Fact]
    public void Phase4_db_backed_world_content_runtime_is_evidence_gated()
    {
        var evidenceFile = Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "OfficialProtocolCompletion",
            "WorldContentRecovery.Phase4.DbBackedRuntime.json");
        var snapshotFile = Path.Combine(
            RepositoryRoot(),
            "Artifacts",
            "WorldRuntimeInspector",
            "20260730-041612",
            "world-runtime-snapshot.json");

        Assert.True(File.Exists(evidenceFile), "DB-backed world content runtime evidence is missing.");
        if (!File.Exists(snapshotFile))
        {
            return;
        }

        using var evidence = JsonDocument.Parse(File.ReadAllText(evidenceFile));
        var root = evidence.RootElement;
        Assert.Equal("world-content-recovery-phase4-db-backed-runtime-v1", root.GetProperty("schemaVersion").GetString());

        var runtime = root.GetProperty("dbBackedExperimental");
        Assert.Equal(1785918668, runtime.GetProperty("mapId").GetInt32());
        Assert.Equal(1, runtime.GetProperty("sceneId").GetInt32());
        Assert.True(runtime.GetProperty("mapRuntimeCreated").GetBoolean());
        Assert.True(runtime.GetProperty("worldSessionBound").GetBoolean());
        Assert.Equal(1, runtime.GetProperty("validatedNpcPlacements").GetInt32());
        Assert.Equal(1, runtime.GetProperty("monsterSpawns").GetInt32());
        Assert.Equal(1, runtime.GetProperty("portalEntities").GetInt32());
        Assert.Equal(1, runtime.GetProperty("merchantMappings").GetInt32());
        Assert.Equal(3, runtime.GetProperty("spawnQueueCount").GetInt32());
        Assert.Equal(4, runtime.GetProperty("semanticBroadcastEventCount").GetInt32());
        Assert.Equal(3, runtime.GetProperty("serializerBlockedCount").GetInt32());

        var boundary = root.GetProperty("serializerBoundary");
        Assert.Equal("EVIDENCE_GATED", boundary.GetProperty("officialNpcMonsterPortalSerializer").GetString());
        Assert.False(boundary.GetProperty("fakePacketsGenerated").GetBoolean());

        using var snapshot = JsonDocument.Parse(File.ReadAllText(snapshotFile));
        var snap = snapshot.RootElement;
        Assert.Equal("world-runtime-snapshot-v1", snap.GetProperty("SchemaVersion").GetString());
        Assert.Equal(1, snap.GetProperty("PlayerCount").GetInt32());
        Assert.Equal(1, snap.GetProperty("NpcCount").GetInt32());
        Assert.Equal(1, snap.GetProperty("MonsterCount").GetInt32());
        Assert.Equal(1, snap.GetProperty("PortalCount").GetInt32());
        Assert.Equal(1, snap.GetProperty("MerchantCount").GetInt32());
    }

    [Fact]
    public async Task Missing_json_database_password_does_not_show_ready()
    {
        using var temp = TempServerBase(copyConfig: true);
        using var output = new StringWriter();
        var databasePath = Path.Combine(temp.Path, "config", "database.json");
        var database = JsonNode.Parse(File.ReadAllText(databasePath))!.AsObject();
        database["password"] = string.Empty;
        database["passwordSource"] = "ConfigValue";
        File.WriteAllText(databasePath, database.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exitCode = await ConsoleEntry.RunAsync(new ConsoleEntryContext(temp.Path, output), CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("作者：RayCat", output.ToString());
        Assert.DoesNotContain("服務端已就緒", output.ToString());
    }

    [Fact]
    public void Three_language_resources_load_and_missing_key_falls_back_to_english()
    {
        using var temp = TempServerBase(copyConfig: true);
        var paths = new AppPathProvider(temp.Path);

        var traditional = new JsonLocalizer(paths, "zh-TW", "en-US");
        var simplified = new JsonLocalizer(paths, "zh-CN", "en-US");
        var englishFallback = new JsonLocalizer(paths, "missing", "en-US");

        Assert.Equal("關機完成", traditional.Translate("shutdown.complete"));
        Assert.Equal("關機完成", simplified.Translate("shutdown.complete"));
        Assert.Equal("Shutdown Complete", englishFallback.Translate("shutdown.complete"));
    }

    private static IReadOnlyList<string> ProjectReferences(string relativeProject)
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), relativeProject));
        return document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")?.Value))
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .Order()
            .ToArray();
    }

    private static string SourceSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Source start marker was not found: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Source end marker was not found after its start marker: {endMarker}");
        return source[start..end];
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static Dictionary<string, IReadOnlyList<string>> ProjectGraph()
    {
        return Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => (IReadOnlyList<string>)ProjectReferences(Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/')));
    }

    private static bool HasCycle(string start, string current, IReadOnlyDictionary<string, IReadOnlyList<string>> graph, HashSet<string> visited)
    {
        if (!graph.TryGetValue(current, out var references))
        {
            return false;
        }

        foreach (var reference in references)
        {
            if (reference == start)
            {
                return true;
            }

            if (visited.Add(reference) && HasCycle(start, reference, graph, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static TemporaryDirectory TempServerBase(bool copyConfig)
    {
        var temp = new TemporaryDirectory();
        if (copyConfig)
        {
            CopyDirectory(Path.Combine(RepositoryRoot(), "config"), Path.Combine(temp.Path, "config"));
            CopyDirectory(Path.Combine(RepositoryRoot(), "localization"), Path.Combine(temp.Path, "localization"));
        }

        return temp;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"god2-foundation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
