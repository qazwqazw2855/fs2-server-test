[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageZip,
    [Parameter(Mandatory)][string]$OutputRoot
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$zipPath=(Resolve-Path -LiteralPath $PackageZip).Path
$outputPath=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $outputPath){throw "Refusing to overwrite security regression output: $outputPath"}
[void](New-Item -ItemType Directory -Path $outputPath)
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-TestPackage([string]$Name){
    $root=Join-Path $outputPath $Name
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath,$root)
    return $root
}

function Update-ManifestIdentity([string]$Root,[string]$RelativePath){
    $manifestPath=Join-Path $Root 'package-manifest.json'
    $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
    $row=@($manifest.Files|Where-Object Path -ceq $RelativePath)
    if($row.Count -ne 1){throw "Managed test file is missing from manifest: $RelativePath"}
    $path=Join-Path $Root $RelativePath.Replace('/','\')
    $row[0].Bytes=[uint64](Get-Item -LiteralPath $path).Length
    $row[0].SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($manifestPath,(($manifest|ConvertTo-Json -Depth 20)+"`n"),$utf8)
}

function Invoke-Validator([string]$Root,[string]$ReportName){
    $auditRoot=Join-Path $Root 'Preflight'
    if(-not(Test-Path -LiteralPath $auditRoot)){[void](New-Item -ItemType Directory -Path $auditRoot)}
    $report=Join-Path $auditRoot $ReportName
    $stdout=Join-Path $auditRoot ($ReportName+'.stdout.txt')
    $stderr=Join-Path $auditRoot ($ReportName+'.stderr.txt')
    $process=Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -Wait -PassThru `
        -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',
            ('"'+(Join-Path $Root 'Test-DeepContractPortablePackage.ps1')+'"'),
            '-PackageRoot',('"'+$Root+'"'),'-ReportPath',('"'+$report+'"')) `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $result=if(Test-Path -LiteralPath $report){
        Get-Content -LiteralPath $report -Raw -Encoding UTF8|ConvertFrom-Json
    }else{$null}
    return [pscustomobject][ordered]@{
        ExitCode=$process.ExitCode;Result=$result;Stdout=$stdout;Stderr=$stderr
    }
}

$rows=[Collections.Generic.List[object]]::new()

$positiveRoot=New-TestPackage 'positive'
$positive=Invoke-Validator $positiveRoot 'security-positive.json'
[void]$rows.Add([pscustomobject][ordered]@{
    Name='positive.package';ExpectedExitCode=0;ActualExitCode=$positive.ExitCode
    Passed=($positive.ExitCode -eq 0 -and [string]$positive.Result.Status -ceq 'PASS')
})

$unmanagedRoot=New-TestPackage 'negative-unmanaged-file'
[IO.File]::WriteAllText((Join-Path $unmanagedRoot 'UNMANAGED-RUNTIME-DEPENDENCY.ps1'),
    "Get-Date`n",$utf8)
$unmanaged=Invoke-Validator $unmanagedRoot 'security-unmanaged.json'
[void]$rows.Add([pscustomobject][ordered]@{
    Name='negative.unmanaged-file';ExpectedExitCode=4;ActualExitCode=$unmanaged.ExitCode
    Passed=($unmanaged.ExitCode -eq 4 -and
        @($unmanaged.Result.Checks|Where-Object Name -ceq 'manifest.static-files-complete'|
            Where-Object{-not $_.Passed}).Count -eq 1)
})

$externalRoot=New-TestPackage 'negative-external-path'
$runnerRelative='Invoke-Portable-Deep-Contract-Capture.ps1'
$runnerPath=Join-Path $externalRoot $runnerRelative
[IO.File]::AppendAllText($runnerPath,"`n`$null=Test-Path `$env:USERPROFILE`n",$utf8)
Update-ManifestIdentity $externalRoot $runnerRelative
$external=Invoke-Validator $externalRoot 'security-external.json'
[void]$rows.Add([pscustomobject][ordered]@{
    Name='negative.external-path';ExpectedExitCode=4;ActualExitCode=$external.ExitCode
    Passed=($external.ExitCode -eq 4 -and
        @($external.Result.Checks|Where-Object Name -ceq 'runtime.scripts.package-relative'|
            Where-Object{-not $_.Passed}).Count -eq 1)
})

$schemaRoot=New-TestPackage 'negative-schema'
$planPath=Join-Path $schemaRoot 'next-evidence-plan.json'
$plan=Get-Content -LiteralPath $planPath -Raw -Encoding UTF8|ConvertFrom-Json
$plan|Add-Member -NotePropertyName UnexpectedProperty -NotePropertyValue $true
[IO.File]::WriteAllText($planPath,(($plan|ConvertTo-Json -Depth 20)+"`n"),$utf8)
Update-ManifestIdentity $schemaRoot 'next-evidence-plan.json'
$schema=Invoke-Validator $schemaRoot 'security-schema.json'
[void]$rows.Add([pscustomobject][ordered]@{
    Name='negative.schema-additional-property';ExpectedExitCode=4;ActualExitCode=$schema.ExitCode
    Passed=($schema.ExitCode -eq 4 -and
        @($schema.Result.Checks|Where-Object Name -ceq 'evidence-plan.json-schema'|
            Where-Object{-not $_.Passed}).Count -eq 1)
})

$failed=@($rows|Where-Object{-not $_.Passed})
$result=[ordered]@{
    SchemaVersion='god2-portable-security-regression-v1'
    TestedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Status=if($failed.Count -eq 0){'PASS'}else{'FAIL'}
    TestCount=$rows.Count;PassedCount=$rows.Count-$failed.Count;FailedCount=$failed.Count
    Rows=@($rows)
}
$reportPath=Join-Path $outputPath 'portable-security-regression.json'
[IO.File]::WriteAllText($reportPath,(($result|ConvertTo-Json -Depth 10)+"`n"),$utf8)
[pscustomobject]$result
if($failed.Count){exit 6}
