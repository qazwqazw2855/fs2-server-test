param(
    [string] $RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    Get-God2RepoRoot
}
else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$config = Get-Content -LiteralPath (Join-Path $repoRoot "config\database.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$releaseStatus = Test-God2ServerReleaseManifest
if (-not $releaseStatus.succeeded) {
    throw "Published release verification failed: $($releaseStatus.errorCode): $($releaseStatus.diagnostic)"
}
$migrationRoot = Join-Path (Split-Path -Parent (Get-God2ServerExecutablePath)) "database\schema"
if (-not (Test-Path -LiteralPath $migrationRoot -PathType Container)) {
    throw "Published migration directory is missing: $migrationRoot"
}
$mariaDb = "C:\Program Files\MariaDB 12.3\bin\mariadb.exe"

if (-not (Test-Path -LiteralPath $mariaDb -PathType Leaf)) {
    throw "MariaDB client was not found: $mariaDb"
}

$adminStatus = Initialize-God2DatabaseAdminPasswordEnvironment
if (-not $adminStatus.hasSecret) {
    throw "Database administrator secret is unavailable: $($adminStatus.failureCode)"
}

function Invoke-AdminSql {
    param([Parameter(Mandatory = $true)] [string] $Sql)

    $env:MYSQL_PWD = [Environment]::GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD", "Process")
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $mariaDb
    $process.StartInfo.Arguments = @(
        "--protocol=tcp"
        "--host=`"$([string]$config.host)`""
        "--port=$([int]$config.port)"
        "--user=root"
        "--connect-timeout=$([int]$config.connectionTimeoutSeconds)"
        "--default-character-set=utf8mb4"
        "--database=`"$([string]$config.databaseName)`""
        "--batch"
        "--skip-column-names"
    ) -join " "
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    # Migrations contain exact zh-TW evidence literals. Hidden/elevated wrapper
    # processes do not inherit a stable console code page. Windows PowerShell
    # 5.1 also lacks ProcessStartInfo.StandardInputEncoding, so write explicit
    # UTF-8 bytes to the redirected base stream instead of using StreamWriter.
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $sqlBytes = $utf8NoBom.GetBytes($Sql)
    $writeException = $null
    $outputText = ""
    $errorText = ""
    $exitCode = -1
    try {
        if (-not $process.Start()) {
            throw "MariaDB client could not be started."
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        try {
            $process.StandardInput.BaseStream.Write($sqlBytes, 0, $sqlBytes.Length)
            $process.StandardInput.BaseStream.Flush()
        }
        catch {
            $writeException = $_.Exception
        }
        finally {
            try { $process.StandardInput.Close() } catch { }
        }
        $process.WaitForExit()
        $outputText = $standardOutput.GetAwaiter().GetResult()
        $errorText = $standardError.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    finally {
        [Array]::Clear($sqlBytes, 0, $sqlBytes.Length)
        $process.Dispose()
    }
    if ($null -ne $writeException) {
        throw "Administrator migration input failed: $($writeException.Message) MariaDB: $errorText"
    }
    if ($exitCode -ne 0) {
        throw "Administrator migration SQL failed: $errorText"
    }
    return @($outputText -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith("WARNING:", [StringComparison]::OrdinalIgnoreCase) })
}

function Get-SqlChecksum {
    param([Parameter(Mandatory = $true)] [string] $Sql)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Sql)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

try {
    $applied = @{}
    foreach ($row in @(Invoke-AdminSql -Sql "SELECT ``Version``,COALESCE(``Checksum``,'') FROM ``__SchemaVersion``;")) {
        $fields = @($row -split "`t", 2)
        if ($fields.Count -ne 2 -or [string]::IsNullOrWhiteSpace($fields[0])) {
            throw "Invalid schema version row returned by MariaDB."
        }
        $applied[[string]$fields[0]] = [string]$fields[1]
    }

    $appliedNow = [Collections.Generic.List[string]]::new()
    $migrations = @(Get-ChildItem -LiteralPath $migrationRoot -Filter "*.sql" -File | Sort-Object Name)
    foreach ($migration in $migrations) {
        if ($migration.BaseName -notmatch '^(?<version>[0-9]+)_') {
            throw "Invalid migration file name: $($migration.Name)"
        }
        $version = $Matches.version
        $sql = Get-Content -LiteralPath $migration.FullName -Raw -Encoding UTF8
        if ([string]::IsNullOrWhiteSpace($sql)) {
            throw "Migration is empty: $($migration.Name)"
        }
        $checksum = Get-SqlChecksum -Sql $sql
        if ($applied.ContainsKey($version)) {
            $storedChecksum = [string]$applied[$version]
            if ([string]::IsNullOrWhiteSpace($storedChecksum)) {
                Invoke-AdminSql -Sql "UPDATE ``__SchemaVersion`` SET ``Checksum``='$checksum' WHERE ``Version``='$version' AND (``Checksum`` IS NULL OR ``Checksum``='');" | Out-Null
            }
            elseif (-not [string]::Equals($storedChecksum, $checksum, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Migration checksum mismatch: $($migration.Name)"
            }
            continue
        }

        Invoke-AdminSql -Sql $sql | Out-Null

        $escapedName = $migration.Name.Replace("'", "''")
        Invoke-AdminSql -Sql @"
INSERT INTO ``__SchemaVersion`` (``Version``,``Name``,``Checksum``,``AppliedAtUtc``)
VALUES ('$version','$escapedName','$checksum',UTC_TIMESTAMP(6));
"@ | Out-Null
        $applied[$version] = $checksum
        $appliedNow.Add($migration.Name)
    }

    [pscustomobject]@{
        status = "SCHEMA_MIGRATIONS_CURRENT"
        buildId = $releaseStatus.buildId
        discovered = $migrations.Count
        appliedNow = @($appliedNow)
        adminSecretSource = $adminStatus.source
        secretEmitted = $false
    } | ConvertTo-Json -Depth 5
}
finally {
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD", $null, "Process")
}
