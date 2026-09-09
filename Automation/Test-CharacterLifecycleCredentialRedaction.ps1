param(
    [string[]] $ScanRoots = @("Reports", "Artifacts"),
    [string] $OutputPath = "",
    [switch] $Redact
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "Artifacts\CharacterLifecycleClosure\credential-redaction-scan.json"
}

$secretValues = New-Object System.Collections.Generic.List[string]
$null = Initialize-God2DatabasePasswordEnvironment
$config = Get-God2DatabaseConfig
$dbSecret = [Environment]::GetEnvironmentVariable([string]$config.passwordEnvironmentVariable, "Process")
if (-not [string]::IsNullOrWhiteSpace($dbSecret) -and $dbSecret.Length -ge 4) {
    $secretValues.Add($dbSecret)
}

$loginSecretPath = Join-Path $repoRoot "Automation\State\login-secret.bin"
if (Test-Path -LiteralPath $loginSecretPath) {
    $protected = [IO.File]::ReadAllBytes($loginSecretPath)
    $plain = [Security.Cryptography.ProtectedData]::Unprotect(
        $protected,
        $null,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        $json = [Text.Encoding]::UTF8.GetString($plain)
        $loginSecret = $json | ConvertFrom-Json
        foreach ($value in @([string]$loginSecret.account, [string]$loginSecret.password)) {
            if (-not [string]::IsNullOrWhiteSpace($value) -and $value.Length -ge 4) {
                $secretValues.Add($value)
            }
        }
    }
    finally {
        [Array]::Clear($plain, 0, $plain.Length)
        $json = $null
        $loginSecret = $null
    }
}

$uniqueSecrets = @($secretValues | Select-Object -Unique)
$roots = @($ScanRoots) |
    ForEach-Object { Join-Path $repoRoot $_ } |
    Where-Object { Test-Path -LiteralPath $_ }

$binaryExtensions = @(
    ".png", ".jpg", ".jpeg", ".gif", ".bmp",
    ".raw", ".bin", ".exe", ".dll", ".pdb",
    ".zip", ".7z", ".db", ".sqlite", ".sqlite3"
)

$files = foreach ($root in $roots) {
    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $binaryExtensions -notcontains $_.Extension.ToLowerInvariant() }
}

$matchFiles = New-Object System.Collections.Generic.List[string]
$redactedFiles = New-Object System.Collections.Generic.List[string]
$scanErrors = New-Object System.Collections.Generic.List[object]
foreach ($file in @($files)) {
    try {
        $textValue = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
        $text = if ($null -eq $textValue) { "" } else { [string]$textValue }
        $redactedText = $text
        $matched = $false
        foreach ($secret in $uniqueSecrets) {
            if ($text.Contains($secret)) {
                $matched = $true
                if ($Redact) {
                    $redactedText = $redactedText.Replace($secret, "<REDACTED_CREDENTIAL>")
                }
            }
        }

        if ($matched) {
            $relativePath = Resolve-Path -LiteralPath $file.FullName -Relative
            $matchFiles.Add($relativePath)
            if ($Redact -and -not [string]::Equals($redactedText, $text, [StringComparison]::Ordinal)) {
                [IO.File]::WriteAllText($file.FullName, $redactedText, [Text.UTF8Encoding]::new($false))
                $redactedFiles.Add($relativePath)
            }
        }
    }
    catch {
        $relativePath = $file.FullName.Substring($repoRoot.Length).TrimStart([char[]]@('\', '/'))
        $scanErrors.Add([pscustomobject]@{
            path = $relativePath
            error = $_.Exception.Message
        })
    }
}

$result = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    scannedRoots = @($ScanRoots)
    scannedFileCount = @($files).Count
    secretValueCount = $uniqueSecrets.Count
    matchCount = $matchFiles.Count
    matchFiles = @($matchFiles)
    redactionMode = [bool]$Redact
    redactedFileCount = $redactedFiles.Count
    redactedFiles = @($redactedFiles)
    scanErrorCount = $scanErrors.Count
    scanErrors = @($scanErrors.ToArray())
}

Write-God2AtomicJson -Value $result -Path $OutputPath
$result | ConvertTo-Json -Depth 8
if ($scanErrors.Count -ne 0) {
    throw "Credential redaction scan could not read $($scanErrors.Count) files."
}
