[CmdletBinding()]
param(
    [string]$LauncherPath='',
    [ValidateRange(30,1800)][int]$LauncherTimeoutSeconds=900,
    [ValidateRange(30,600)][int]$ObserveSeconds=180,
    [switch]$PreflightOnly
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$packageRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$expectedClientSha='6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
$expectedClientVersion='1.0.0.1'

function Write-Json([string]$Path,[object]$Value){
    if(Test-Path -LiteralPath $Path){throw "Refusing to overwrite output: $Path"}
    [IO.File]::WriteAllText($Path,(($Value|ConvertTo-Json -Depth 16)+"`n"),$utf8)
}
function Get-PeMachine([string]$Path){
    $bytes=[IO.File]::ReadAllBytes($Path)
    if($bytes.Length -lt 0x40){return 0}
    $pe=[BitConverter]::ToInt32($bytes,0x3c)
    if($pe -lt 0 -or $pe+6 -gt $bytes.Length){return 0}
    return [BitConverter]::ToUInt16($bytes,$pe+4)
}

$preflightRoot=Join-Path $packageRoot 'Preflight'
if(-not(Test-Path -LiteralPath $preflightRoot)){[void](New-Item -ItemType Directory -Path $preflightRoot)}
$preflightReport=Join-Path $preflightRoot ('portable-preflight-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.json')
$preflight=& (Join-Path $packageRoot 'Test-DeepContractPortablePackage.ps1') `
    -PackageRoot $packageRoot -ReportPath $preflightReport
if([string]$preflight.Status -cne 'PASS'){throw 'Portable package preflight failed.'}
if($PreflightOnly -or [string]$env:GOD2_PORTABLE_PREFLIGHT_ONLY -ceq '1'){
    [pscustomobject][ordered]@{
        Status='PASS';PreflightOnly=$true;CleanRoomPortableTest='PASS'
        ManagedFileCount=[int]$preflight.ManagedFileCount
        ExternalWorkspaceDependencyCount=[int]$preflight.ExternalWorkspaceDependencyCount
        ExternalRuntimeScriptDependencyCount=[int]$preflight.ExternalRuntimeScriptDependencyCount
        DirectClientLaunchAllowed=$false;LauncherOnly=$true
        FunctionStartBreakpointContract=[string]$preflight.FunctionStartBreakpointContract
        ProducerMetricConsistency=[string]$preflight.ProducerMetricConsistency
        ReportPath=$preflightReport
    }
    exit 0
}

if([string]::IsNullOrWhiteSpace($LauncherPath)){$LauncherPath=[string]$env:GOD2_OFFICIAL_LAUNCHER}
if([string]::IsNullOrWhiteSpace($LauncherPath)){
    Add-Type -AssemblyName System.Windows.Forms
    $picker=[Windows.Forms.OpenFileDialog]::new()
    $picker.Title='Select the official Launcher.exe'
    $picker.Filter='Launcher.exe|Launcher.exe'
    $picker.CheckFileExists=$true
    if($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){throw 'Launcher selection was cancelled.'}
    $LauncherPath=$picker.FileName
    $picker.Dispose()
}
$launcherPathResolved=(Resolve-Path -LiteralPath $LauncherPath).Path
if([IO.Path]::GetFileName($launcherPathResolved) -ine 'Launcher.exe'){
    throw 'Only the user-selected official Launcher.exe is allowed.'
}
if(@(Get-Process God2_opt -ErrorAction SilentlyContinue).Count -ne 0){
    throw 'A God2_opt.exe process already exists; launcher parentage cannot be proven fail-closed.'
}

Add-Type -Path (Join-Path $packageRoot 'PortableLauncherChain.cs')
$sessionId='deep-contract-portable-'+(Get-Date -Format 'yyyyMMdd-HHmmss')
$stateRoot=Join-Path $packageRoot ('State\'+$sessionId)
$resultsRoot=Join-Path $packageRoot 'Results'
[void](New-Item -ItemType Directory -Path $stateRoot)
if(-not(Test-Path -LiteralPath $resultsRoot)){[void](New-Item -ItemType Directory -Path $resultsRoot)}
$launcher=Start-Process -FilePath $launcherPathResolved `
    -WorkingDirectory (Split-Path -Parent $launcherPathResolved) -PassThru
$launcherSha=(Get-FileHash -LiteralPath $launcherPathResolved -Algorithm SHA256).Hash
$client=$null
$clientParent=0
$deadline=(Get-Date).AddSeconds($LauncherTimeoutSeconds)
do{
    $children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($launcher.Id)" -ErrorAction SilentlyContinue|
        Where-Object Name -ieq 'God2_opt.exe')
    if($children.Count -gt 0){
        $clientParent=[uint32]$children[0].ParentProcessId
        $client=Get-Process -Id ([uint32]$children[0].ProcessId) -ErrorAction SilentlyContinue
        if($client){break}
    }
    Start-Sleep -Milliseconds 100
}while((Get-Date)-lt$deadline)
if(-not $client){throw 'Launcher did not create God2_opt.exe before the timeout.'}

$dialogObserved=$false
$dialogClickIssued=$false
$dialogDismissed=$false
$dialogDeadline=(Get-Date).AddSeconds(15)
do{
    if([God2PortableLauncherChain]::IsDirectSoundDialogPresent([uint32]$client.Id)){
        $dialogObserved=$true
        if([God2PortableLauncherChain]::ClickDirectSoundDialog([uint32]$client.Id)){
            $dialogClickIssued=$true
        }
    }elseif($dialogObserved -and $dialogClickIssued){
        $dialogDismissed=$true
        break
    }
    Start-Sleep -Milliseconds 25
}while((Get-Date)-lt$dialogDeadline -and -not $client.HasExited)
if(-not $dialogObserved -or -not $dialogClickIssued -or -not $dialogDismissed){
    throw 'Direct Sound Create failed dialog was not observed, clicked, and confirmed closed.'
}
$dialogDismissedAt=[DateTimeOffset]::UtcNow.ToString('o')

$client.Refresh()
$clientPath=[IO.Path]::GetFullPath([string]$client.MainModule.FileName)
$clientItem=Get-Item -LiteralPath $clientPath
$clientSha=(Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
if([IO.Path]::GetFileName($clientPath) -ine 'God2_opt.exe' -or
    $clientSha -cne $expectedClientSha -or
    $clientItem.VersionInfo.FileVersion -cne $expectedClientVersion -or
    (Get-PeMachine $clientPath) -ne 0x14c -or $clientParent -ne [uint32]$launcher.Id){
    throw 'Launcher child failed the exact God2_opt.exe x86 identity or parent contract.'
}

$attestationPath=Join-Path $stateRoot 'official-launch-chain-attestation.json'
$attestation=[ordered]@{
    SchemaId='God2OfficialLauncherAutomationAttestation';SchemaVersion=1
    GeneratedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');Passed=$true
    LauncherPath=$launcherPathResolved;LauncherSHA256=$launcherSha
    LauncherProcessId=[uint32]$launcher.Id
    ClientPath=$clientPath;ClientSHA256=$clientSha;ClientVersion=$expectedClientVersion
    ClientProcessId=[uint32]$client.Id
    ClientProcessCreationTime=$client.StartTime.ToUniversalTime().ToFileTimeUtc().ToString()
    ClientParentProcessId=$clientParent;ParentIsVerifiedLauncher=$true
    AgreementAccepted=$true
    AgreementAcceptanceBasis='LauncherEnforcedPrerequisiteConfirmedByVerifiedChildCreation'
    AgreementDirectlyObserved=$false
    StartGameClicked=$true
    StartGameEvidenceBasis='VerifiedLauncherChildCreationCausalInference'
    StartGameDirectlyObserved=$false
    DirectSoundDialogObserved=$dialogObserved
    DirectSoundDialogHandled=$true;DirectSoundDialogHandledAtUtc=$dialogDismissedAt
    DirectSoundDialogMethod='BM_CLICK_THEN_WINDOW_ABSENCE_CONFIRMED'
    FormalAttachTimingAllowed=$dialogDismissed
    DirectClientLaunch=$false
}
Write-Json $attestationPath $attestation
$exactIdentityPath=Join-Path $stateRoot 'exact-target-identity.json'
Write-Json $exactIdentityPath ([ordered]@{
    SchemaVersion='god2-exact-target-identity-v1';SessionId=$sessionId
    AuthorityProfile='CurrentOfficialClientLive';TargetExecutable='God2_opt.exe'
    TargetArchitecture='x86';TargetVersion=$expectedClientVersion;TargetSHA256=$clientSha
    ClientProcessId=[uint32]$client.Id
    ClientProcessCreationTime=$client.StartTime.ToUniversalTime().ToFileTimeUtc().ToString()
    LauncherPath=$launcherPathResolved;LauncherSHA256=$launcherSha;LauncherProcessId=[uint32]$launcher.Id
    LauncherParentVerified=$true;DirectClientLaunch=$false;Passed=$true
})

$runtimeScript=Join-Path $packageRoot 'tools\God2.ClientInstrumentation\scripts\Invoke-OfficialClientRuntime.ps1'
$runtimeOutput=@(& $runtimeScript -ClientExe $clientPath -TargetProcessId ([uint32]$client.Id) `
    -LaunchChainAttestationPath $attestationPath -RunId $sessionId -Configuration Release `
    -ObserveSeconds $ObserveSeconds -EnablePreAttachDeepStaticDiscovery -EnableContractAcquisition `
    -ContractAcquisitionObserveSeconds $ObserveSeconds `
    -DeepEvidencePlanPath (Join-Path $packageRoot 'next-evidence-plan.json') `
    -RingSessionRoot (Join-Path $stateRoot 'production-ring'))
$runtimeRoot=Join-Path $packageRoot ('Artifacts\ClientInstrumentation\LoginTrial\'+$sessionId)
$summaryPath=Join-Path $runtimeRoot 'analysis\official-runtime-summary.json'
if(-not(Test-Path -LiteralPath $summaryPath -PathType Leaf)){throw 'Official runtime summary is missing.'}
$summary=Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8|ConvertFrom-Json
if(-not [bool]$summary.Passed -or [bool]$summary.DirectClientLaunch -or
    -not [bool]$summary.LauncherParentVerified){throw 'Official runtime failed the launcher-only contract.'}

$resultZip=Join-Path $resultsRoot ('God2DeepContractPromotionResult_'+$sessionId+'.zip')
$packager=Join-Path $packageRoot 'Invoke-PortableDeepContractResultPackager.ps1'
$result=& $packager -PackageRoot $packageRoot -RuntimeRoot $runtimeRoot -SessionId $sessionId `
    -ExactTargetIdentityPath $exactIdentityPath -OutputZip $resultZip
[pscustomobject][ordered]@{
    Status='PASS';SessionId=$sessionId;LauncherOnly=$true;DirectClientLaunchAllowed=$false
    LauncherSHA256=$launcherSha;LauncherProcessId=[uint32]$launcher.Id
    ClientSHA256=$clientSha;ClientProcessId=[uint32]$client.Id
    ClientProcessCreationTime=$client.StartTime.ToUniversalTime().ToFileTimeUtc().ToString()
    LauncherParentVerified=$true;ResultZip=[string]$result.ResultZip
    ResultZipBytes=[uint64]$result.ResultZipBytes;ResultZipSHA256=[string]$result.ResultZipSHA256
}
