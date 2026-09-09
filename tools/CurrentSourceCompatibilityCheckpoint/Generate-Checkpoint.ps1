param(
    [Parameter(Mandatory = $false)]
    [string] $Root = ".",

    [Parameter(Mandatory = $false)]
    [string] $ClientWorkingCopy = "",

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string] $OutputDirectoryName = "CurrentSourceCompatibilityCheckpoint"
)

$ErrorActionPreference = "Stop"
$rootPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Root).Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
$solutionPath = Join-Path $rootPath "God2ClassicServer.sln"
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "God2ClassicServer.sln was not found at the requested checkpoint root."
}

$outputRoot = [IO.Path]::GetFullPath((Join-Path $rootPath ("Artifacts\" + $OutputDirectoryName)))
if (-not $outputRoot.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Checkpoint output escaped the project root."
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function Get-FileEntry([string] $Path, [string] $Group) {
    $item = Get-Item -LiteralPath $Path
    $fullPath = [IO.Path]::GetFullPath($item.FullName)
    if (-not $fullPath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest input escaped the project root."
    }
    $relative = $fullPath.Substring($rootPath.Length + 1).Replace('\', '/')
    [pscustomobject]@{
        group = $Group
        relativePath = $relative
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.FullName).Hash
        length = $item.Length
    }
}

function Get-AggregateHash([object[]] $Entries) {
    $lines = $Entries |
        Sort-Object relativePath |
        ForEach-Object { "{0}`0{1}`0{2}`n" -f $_.relativePath, $_.sha256, $_.length }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join ""))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

$sourceRoots = [ordered]@{
    productionSource = "src"
    tests = "tests"
    tools = "tools"
    automation = "Automation"
    database = "database"
    protocolEvidence = "protocol"
    configuration = "config"
}

$sourceEntries = [Collections.Generic.List[object]]::new()
foreach ($pair in $sourceRoots.GetEnumerator()) {
    $directory = Join-Path $rootPath $pair.Value
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        continue
    }

    Get-ChildItem -LiteralPath $directory -Recurse -File -Force |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|TestResults|runtime)[\\/]' -and
            $_.FullName -notmatch '[\\/]Automation[\\/]State[\\/]' -and
            $_.Name -notmatch '\.local\.json$' -and
            $_.Name -notmatch '\.(user|suo)$'
        } |
        ForEach-Object { $sourceEntries.Add((Get-FileEntry $_.FullName $pair.Key)) }
}

foreach ($name in @("God2ClassicServer.sln", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", ".editorconfig")) {
    $path = Join-Path $rootPath $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $sourceEntries.Add((Get-FileEntry $path "buildDefinition"))
    }
}

foreach ($relativePath in @(
    "tools\God2.CurrentSourceMariaDbFixture\Program.cs",
    "tools\God2.CurrentSourceMariaDbFixture\God2.CurrentSourceMariaDbFixture.csproj",
    "tools\God2.CurrentSourceMariaDbFixture\Invoke-CurrentSourceMariaDbFixture.ps1")) {
    $path = Join-Path $rootPath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required CurrentSource MariaDB fixture source is missing: $relativePath"
    }
    $sourceEntries.Add((Get-FileEntry $path "fixtureSource"))
}

$orderedSourceEntries = @($sourceEntries | Sort-Object relativePath)
$sourceGroups = @($orderedSourceEntries | Group-Object group | ForEach-Object {
    [pscustomobject]@{
        group = $_.Name
        fileCount = $_.Count
        aggregateSha256 = Get-AggregateHash @($_.Group)
    }
})
$sourceAggregate = Get-AggregateHash $orderedSourceEntries
$generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")

$sourceManifest = [ordered]@{
    schemaVersion = "current-source-compatibility-checkpoint/source-manifest-v1"
    generatedAtUtc = $generatedAtUtc
    algorithm = "SHA-256"
    manifestIdentity = "current-source-sha256-$($sourceAggregate.ToLowerInvariant())"
    aggregateSha256 = $sourceAggregate
    fileCount = $orderedSourceEntries.Count
    scope = [ordered]@{
        includedRoots = @($sourceRoots.Values) + @("God2ClassicServer.sln", "Directory.Build.*", "Directory.Packages.props", "global.json", ".editorconfig")
        excluded = @("Reports", "Artifacts", ".git", "bin", "obj", "TestResults", "runtime output", "*.user", "*.suo")
        note = "The manifest includes production, validation, analysis-tool, database, protocol-evidence, configuration, build-definition inputs, and the solution-linked CurrentSource MariaDB fixture source under tools; generated outputs and historical reports/artifacts are excluded."
    }
    groups = $sourceGroups
    entries = $orderedSourceEntries
}

function Get-ProjectRelativePath([IO.FileInfo] $Project) {
    return $Project.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
}

function Invoke-DotNetChecked([string] $Operation, [string[]] $Arguments) {
    $output = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "dotnet $Operation failed with exit code $exitCode.$([Environment]::NewLine)$details"
    }

    return [pscustomobject]@{
        operation = $Operation
        command = "dotnet " + ($Arguments -join " ")
        exitCode = $exitCode
    }
}

$solutionText = Get-Content -LiteralPath $solutionPath -Raw
$solutionProjectMatches = [regex]::Matches(
    $solutionText,
    '(?m)^Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"')
$solutionProjectRelativePaths = @($solutionProjectMatches |
    ForEach-Object { $_.Groups[1].Value.Replace('\', '/') } |
    Sort-Object -Unique)
if ($solutionProjectRelativePaths.Count -eq 0) {
    throw "God2ClassicServer.sln contains no C# projects."
}

$solutionProjectFiles = @($solutionProjectRelativePaths | ForEach-Object {
    $projectPath = [IO.Path]::GetFullPath((Join-Path $rootPath $_))
    if (-not $projectPath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Solution project escaped the project root: $_"
    }
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Solution project does not exist: $_"
    }
    Get-Item -LiteralPath $projectPath
})

$repositoryProjectFiles = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter "*.csproj" |
    Where-Object { $_.FullName -notmatch '[\\/](Artifacts|bin|obj)[\\/]' } |
    Sort-Object FullName)
$solutionProjectSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relativePath in $solutionProjectRelativePaths) {
    $null = $solutionProjectSet.Add($relativePath)
}
$outOfSolutionProjectFiles = @($repositoryProjectFiles |
    Where-Object { -not $solutionProjectSet.Contains((Get-ProjectRelativePath $_)) })

$executionEvidence = [Collections.Generic.List[object]]::new()
$executionEvidence.Add((Invoke-DotNetChecked "restore solution" @("restore", $solutionPath, "--verbosity", "minimal")))
foreach ($project in $outOfSolutionProjectFiles) {
    $relativePath = Get-ProjectRelativePath $project
    $executionEvidence.Add((Invoke-DotNetChecked "restore out-of-solution project $relativePath" @("restore", $project.FullName, "--verbosity", "minimal")))
}
foreach ($configuration in @("Debug", "Release")) {
    $executionEvidence.Add((Invoke-DotNetChecked "build solution $configuration" @(
        "build", $solutionPath, "--configuration", $configuration, "--no-restore", "--verbosity", "minimal")))
}

$productionProjectFiles = @($solutionProjectFiles |
    Where-Object { (Get-ProjectRelativePath $_).StartsWith("src/", [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object FullName)
$discoveredProductionProjectFiles = @(Get-ChildItem -LiteralPath (Join-Path $rootPath "src") -Recurse -File -Filter "*.csproj")
if ($productionProjectFiles.Count -ne $discoveredProductionProjectFiles.Count) {
    throw "Every production project under src must be included in God2ClassicServer.sln."
}

$projectOutputs = [Collections.Generic.List[object]]::new()
foreach ($project in $productionProjectFiles) {
    [xml] $xml = Get-Content -LiteralPath $project.FullName -Raw
    $assemblyName = [string](@($xml.Project.PropertyGroup.AssemblyName | Where-Object { $_ }) | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project.Name)
    }
    $targetFrameworksValue = [string](@($xml.Project.PropertyGroup.TargetFrameworks | Where-Object { $_ }) | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($targetFrameworksValue)) {
        $targetFrameworksValue = [string](@($xml.Project.PropertyGroup.TargetFramework | Where-Object { $_ }) | Select-Object -First 1)
    }
    if ([string]::IsNullOrWhiteSpace($targetFrameworksValue)) {
        throw "Production project has no TargetFramework or TargetFrameworks: $(Get-ProjectRelativePath $project)"
    }

    $outputType = [string](@($xml.Project.PropertyGroup.OutputType | Where-Object { $_ }) | Select-Object -First 1)
    $useAppHost = [string](@($xml.Project.PropertyGroup.UseAppHost | Where-Object { $_ }) | Select-Object -First 1)
    $extensions = @("dll")
    if ($outputType -in @("Exe", "WinExe") -and $useAppHost -ne "false") {
        $extensions += "exe"
    }

    foreach ($targetFramework in @($targetFrameworksValue.Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        foreach ($configuration in @("Debug", "Release")) {
            $directory = Join-Path $project.Directory.FullName ("bin\{0}\{1}" -f $configuration, $targetFramework.Trim())
            foreach ($extension in $extensions) {
                $candidate = Join-Path $directory ("{0}.{1}" -f $assemblyName, $extension)
                if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                    $relativeCandidate = $candidate.Substring($rootPath.Length + 1).Replace('\', '/')
                    throw "Required current build output is missing: $relativeCandidate"
                }

                $entry = Get-FileEntry $candidate "buildOutput"
                $projectOutputs.Add([pscustomobject]@{
                    configuration = $configuration
                    project = Get-ProjectRelativePath $project
                    targetFramework = $targetFramework.Trim()
                    relativePath = $entry.relativePath
                    sha256 = $entry.sha256
                    length = $entry.length
                })
             }
        }
    }
}

$orderedProjectOutputs = @($projectOutputs | Sort-Object configuration, relativePath)
$buildConfigurations = @($orderedProjectOutputs | Group-Object configuration | ForEach-Object {
    $entries = @($_.Group | ForEach-Object {
        [pscustomobject]@{
            relativePath = $_.relativePath
            sha256 = $_.sha256
            length = $_.length
        }
    })
    [pscustomobject]@{
        configuration = $_.Name
        outputCount = $_.Count
        aggregateSha256 = Get-AggregateHash $entries
    }
})

$projectAssetEntries = [Collections.Generic.List[object]]::new()
foreach ($project in $repositoryProjectFiles) {
    $relativeProjectPath = Get-ProjectRelativePath $project
    $assetsPath = Join-Path $project.Directory.FullName "obj\project.assets.json"
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "Required restored dependency identity is missing: $relativeProjectPath"
    }

    $entry = Get-FileEntry $assetsPath "dependencyIdentity"
    $projectAssetEntries.Add([pscustomobject]@{
        project = $relativeProjectPath
        membership = $(if ($solutionProjectSet.Contains($relativeProjectPath)) { "solution" } else { "out-of-solution" })
        relativePath = $entry.relativePath
        sha256 = $entry.sha256
        length = $entry.length
    })
}

$orderedProjectAssetEntries = @($projectAssetEntries | Sort-Object project)
$dependencyIdentity = [ordered]@{
    dotnetSdkVersion = (& dotnet --version).Trim()
    repositoryProjectCount = $repositoryProjectFiles.Count
    solutionProjectCount = $solutionProjectFiles.Count
    outOfSolutionProjectCount = $outOfSolutionProjectFiles.Count
    outOfSolutionProjects = @($outOfSolutionProjectFiles | ForEach-Object { Get-ProjectRelativePath $_ })
    projectAssetsCount = $orderedProjectAssetEntries.Count
    aggregateSha256 = Get-AggregateHash $orderedProjectAssetEntries
    projectAssets = $orderedProjectAssetEntries
}

$clientIdentity = [ordered]@{
    status = "NOT CHECKED"
    buildId = "god2-opt-6b127086e0c0"
    expectedExecutableSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B"
    executable = $null
    launcher = $null
    modules = @()
    resources = @()
}

if (-not [string]::IsNullOrWhiteSpace($ClientWorkingCopy)) {
    $clientRoot = [IO.Path]::GetFullPath($ClientWorkingCopy)
    $buildCatalogPath = Join-Path $rootPath "protocol\evidence\battle-v1\client-builds.json"
    $buildCatalog = Get-Content -LiteralPath $buildCatalogPath -Raw | ConvertFrom-Json
    $build = @($buildCatalog.builds | Where-Object clientBuildId -eq $clientIdentity.buildId) | Select-Object -First 1
    if ($null -eq $build) {
        throw "The expected client build is absent from client-builds.json."
    }

    function Get-ClientHashRow([string] $Kind, [string] $Name, [string] $Expected) {
        $path = Join-Path $clientRoot $Name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            return [pscustomobject]@{ kind = $Kind; file = $Name; length = $null; expectedSha256 = $Expected; currentSha256 = $null; state = "MISSING" }
        }
        $item = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        [pscustomobject]@{ kind = $Kind; file = $Name; length = $item.Length; expectedSha256 = $Expected; currentSha256 = $hash; state = $(if ($hash -eq $Expected) { "MATCH" } else { "MISMATCH" }) }
    }

    $executable = Get-ClientHashRow "Executable" "God2_opt.exe" $build.god2OptSha256
    $launcher = Get-ClientHashRow "Launcher" "Launcher.exe" $build.launcherSha256
    $modules = @($build.moduleHashes.psobject.Properties | ForEach-Object {
        Get-ClientHashRow "Module" ([IO.Path]::GetFileName($_.Name)) $_.Value
    })
    $resources = @($build.relevantConfigHashes.psobject.Properties | ForEach-Object {
        Get-ClientHashRow "Resource" ([IO.Path]::GetFileName($_.Name)) $_.Value
    })
    $clientIdentity = [ordered]@{
        status = $(if ($executable.state -eq "MATCH") { "EXECUTABLE MATCH" } else { "EXECUTABLE MISMATCH" })
        buildId = $build.clientBuildId
        expectedExecutableSha256 = $build.god2OptSha256
        executable = $executable
        launcher = $launcher
        modules = $modules
        resources = $resources
        exactExecutableRvaPolicy = $(if ($executable.state -eq "MATCH") { "Exact-build historical stable signatures may be evaluated; feature attribution still requires direct evidence." } else { "Historical RVAs forbidden." })
        sensitiveFilesExcluded = @("cookies.dat")
    }
}

$buildIdentity = [ordered]@{
    schemaVersion = "current-source-compatibility-checkpoint/build-identity-v1"
    generatedAtUtc = $generatedAtUtc
    sourceManifestIdentity = $sourceManifest.manifestIdentity
    sourceAggregateSha256 = $sourceAggregate
    buildExecution = @($executionEvidence)
    configurations = $buildConfigurations
    outputs = $orderedProjectOutputs
    dependencyIdentity = $dependencyIdentity
    clientIdentity = $clientIdentity
}

$jsonOptionsDepth = 12
$sourceJson = $sourceManifest | ConvertTo-Json -Depth $jsonOptionsDepth
$buildJson = $buildIdentity | ConvertTo-Json -Depth $jsonOptionsDepth
[IO.File]::WriteAllText((Join-Path $outputRoot "current-source-manifest.json"), $sourceJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $outputRoot "build-identity.json"), $buildJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    sourceManifestIdentity = $sourceManifest.manifestIdentity
    sourceFiles = $orderedSourceEntries.Count
    debugOutputs = @($orderedProjectOutputs | Where-Object configuration -eq "Debug").Count
    releaseOutputs = @($orderedProjectOutputs | Where-Object configuration -eq "Release").Count
    clientExecutableStatus = $clientIdentity.status
    outputRoot = "Artifacts/$OutputDirectoryName"
} | ConvertTo-Json -Depth 4
