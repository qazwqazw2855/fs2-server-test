param(
    [ValidateSet("Debug", "Release", "Both")]
    [string] $Configuration = "Both",
    [string] $OutRoot = "",
    [switch] $PinGenericProbeModule
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ($OutRoot.Length -eq 0) {
    $OutRoot = Join-Path $repoRoot "Build\ClientInstrumentation"
}

$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$vcvars = Join-Path $programFilesX86 "Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path -LiteralPath $vcvars)) {
    throw "vcvarsall.bat not found: $vcvars"
}

$configs = if ($Configuration -eq "Both") { @("Debug", "Release") } else { @($Configuration) }
$sourceDir = Join-Path $toolRoot "src"
$probeSource = Join-Path $sourceDir "God2ClientTraceProbe.cpp"
$genericProbeSource = Join-Path $sourceDir "GenericProbePlan.cpp"
$genericProbeSelfTestSource = Join-Path $sourceDir "GenericProbePlanSelfTest.cpp"
$launcherSource = Join-Path $sourceDir "God2ClientTraceLauncher.cpp"
$packetCaptureInjectorSource = Join-Path $sourceDir "God2PacketCaptureInjector.cpp"
$selfTestSource = Join-Path $sourceDir "God2TraceSelfTestClient.cpp"
$results = @()

foreach ($config in $configs) {
    $outDir = Join-Path $OutRoot $config
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $buildLog = Join-Path $outDir "build.log"
    Set-Content -LiteralPath $buildLog -Value "" -Encoding ASCII

    $common = "/nologo /EHsc /W4 /WX /utf-8 /DWIN32_LEAN_AND_MEAN /DNOMINMAX /D_CRT_SECURE_NO_WARNINGS"
    if ($config -eq "Debug") {
        $cflags = "$common /Zi /Od /DDEBUG"
    } else {
        $cflags = "$common /O2 /DNDEBUG"
    }

    $dllOut = Join-Path $outDir "God2ClientTraceProbe.dll"
    $testDllOut = Join-Path $outDir "God2ClientTraceProbeTest.dll"
    $launcherOut = Join-Path $outDir "God2ClientTraceLauncher.exe"
    $packetCaptureInjectorOut = Join-Path $outDir "God2PacketCaptureInjector.exe"
    $packetCaptureInjectorTestOut = Join-Path $outDir "God2PacketCaptureInjectorTest.exe"
    $selfTestOut = Join-Path $outDir "God2TraceSelfTestClient.exe"
    $genericProbeSelfTestOut = Join-Path $outDir "GenericProbePlanSelfTest.exe"
    $selfTestCmdPath = Join-Path $outDir "build-selftest.cmd"
    $selfTestCmd = @"
call "$vcvars" x86
cl $cflags /Fe"$selfTestOut" "$selfTestSource" ws2_32.lib >> "$buildLog" 2>&1
exit /b %errorlevel%
"@
    Set-Content -LiteralPath $selfTestCmdPath -Value $selfTestCmd -Encoding ASCII
    cmd.exe /c "`"$selfTestCmdPath`""
    if ($LASTEXITCODE -ne 0) {
        throw "$config self-test client build failed with exit code $LASTEXITCODE. See $buildLog"
    }
    $selfTestSha256 = (Get-FileHash -LiteralPath $selfTestOut -Algorithm SHA256).Hash
    $selfTestIdentityVersion = "God2TraceSelfTestClient/1"
    $testProbeDefines = "/DGOD2_PROBE_ALLOW_SELF_TEST_IDENTITY " +
        "/DGOD2_PROBE_EXPECTED_CLIENT_SHA256=\`"$selfTestSha256\`" " +
        "/DGOD2_PROBE_EXPECTED_CLIENT_VERSION=\`"$selfTestIdentityVersion\`""
    $productionProbeDefines = if ($PinGenericProbeModule) {
        "/DGOD2_PROBE_PIN_GENERIC_MODULE"
    }
    else {
        ""
    }
    $cmdPath = Join-Path $outDir "build.cmd"
    $cmd = @"
call "$vcvars" x86
cl $cflags $productionProbeDefines /LD /Fe"$dllOut" "$probeSource" "$genericProbeSource" ws2_32.lib user32.lib imm32.lib >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
cl $cflags $testProbeDefines /LD /Fe"$testDllOut" "$probeSource" "$genericProbeSource" ws2_32.lib user32.lib imm32.lib >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
cl $cflags /Fe"$launcherOut" "$launcherSource" >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
cl $cflags /Fe"$genericProbeSelfTestOut" "$genericProbeSelfTestSource" "$genericProbeSource" >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
"$genericProbeSelfTestOut" >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
cl $cflags /Fe"$packetCaptureInjectorOut" "$packetCaptureInjectorSource" /link /SUBSYSTEM:WINDOWS /ENTRY:wmainCRTStartup >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
cl $cflags /DGOD2_INJECTOR_ALLOW_SELF_TEST_TARGET /Fe"$packetCaptureInjectorTestOut" "$packetCaptureInjectorSource" /link /SUBSYSTEM:WINDOWS /ENTRY:wmainCRTStartup >> "$buildLog" 2>&1
if errorlevel 1 exit /b %errorlevel%
exit /b %errorlevel%
"@
    Set-Content -LiteralPath $cmdPath -Value $cmd -Encoding ASCII
    cmd.exe /c "`"$cmdPath`""
    if ($LASTEXITCODE -ne 0) {
        throw "$config build failed with exit code $LASTEXITCODE. See $buildLog"
    }

    $results += [pscustomobject]@{
        configuration = $config
        outDir = $outDir
        dll = $dllOut
        testDll = $testDllOut
        testIdentitySha256 = $selfTestSha256
        testIdentityVersion = $selfTestIdentityVersion
        launcher = $launcherOut
        packetCaptureInjector = $packetCaptureInjectorOut
        packetCaptureInjectorTest = $packetCaptureInjectorTestOut
        selfTestClient = $selfTestOut
        genericProbeSelfTest = $genericProbeSelfTestOut
        buildLog = $buildLog
    }
}

$analyzerProject = Join-Path $toolRoot "Analyzer\God2.ClientInstrumentation.Analyzer.csproj"
dotnet build $analyzerProject -c Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Analyzer build failed."
}

$launcherAutomationProject = Join-Path $toolRoot "LauncherAutomationRecorder\God2.LauncherAutomationRecorder.csproj"
dotnet build $launcherAutomationProject -c Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Launcher automation recorder build failed."
}

[pscustomobject]@{
    outRoot = $OutRoot
    configurations = $results
    analyzerProject = $analyzerProject
    launcherAutomationProject = $launcherAutomationProject
} | ConvertTo-Json -Depth 5
